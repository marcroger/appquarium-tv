# -*- coding: utf-8 -*-
"""
¿Se DISTINGUEN las 7 luces en la tele? (2026-08-30)

Tercera pata del criterio del user ("fondos, suelos, luces y todo eso deben ser
diferenciales como en la app movil"). 5 de las 7 son de pago (0,49 EUR / 10 perlas):
light_blue, light_deep, light_purple, light_sunset, light_cycle.

⚠⚠ UNA LUZ NO SE MIDE COMO UN FONDO. Un preset de luz actua por DOS caminos distintos,
y hay que separarlos o no se sabe que se esta midiendo:

  1. ILUMINACION REAL  — 3 spots (color, spotIntensity), dirDimFactor, ambientBlend.
     Solo alcanza a lo que se ILUMINA: el suelo, las decos lit, los peces.
  2. POST               — colorFilter + postExposure en el ColorAdjustments del
     TankLightingController (Volume priority 11). Alcanza al FRAME ENTERO, incluidos
     los shaders unlit (TankBackground, decos GLB) que la iluminacion no toca.

De ahi las bandas: el telon de fondo es UNLIT, asi que la banda de agua alta ve
basicamente SOLO el camino 2, y el suelo cercano ve los dos sumados. La diferencia
entre las dos bandas ES la descomposicion.

Bandas (fraccion de alto). Las dos primeras se heredan de mide_bandas.py; la del suelo
NO es la de 0.80-0.93 sino el ultimo 10 %:
  agua alta      0.12-0.50   telon + agua  -> post casi puro
  agua honda     0.50-0.75   zona nieblada -> post + niebla
  suelo cercano  0.90-1.00   donde caen los spots -> iluminacion + post

⚠ El 0.90-1.00 sale de CAST_PARIDAD_VISUAL.md 0.6.2: la banda "suelo" del 25 % inferior
promedia el suelo cercano con el lejano, que va fuertemente niebleado por SubstrateFog,
y MIDE LA NIEBLA en vez del grado (-21 L* contra -9.7 L* reales).

CONVENIO: se promedia Lab pixel a pixel y C*/h se derivan del Lab MEDIO (igual que
mide_diferencial.py, para que los dE sean comparables con las tablas de fondos y
sustratos). NO es lo mismo que la media de C* por pixel de mide_bandas.py.

⚠ El tono (h) de una region con poco croma es RUIDO. Se imprime "--" por debajo de C* 3.

Uso:
    python Tools/mide_luces.py                  # lee _luces/luz_*.png
    python Tools/mide_luces.py --dir _luces
"""
import sys, glob, os, argparse, hashlib, io, time, re

# La consola de Windows es cp1252: un simbolo no-ASCII en un print mata la
# herramienta DESPUES de imprimir las tablas (visto el 2026-08-30). La salida va
# en ASCII a proposito, y esto es el cinturon por si alguien reintroduce uno.
try: sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception: pass
import numpy as np
from PIL import Image

# ── precio: un par fundido que cruza la linea de precio es el caso critico ────
GRATIS = {'light_white', 'light_warm'}
NOMINAL = {   # filterColor, exposureOffset — de TankLightingController.Presets
    'light_white':  ((1.00, 1.00, 1.00),  0.00),
    'light_warm':   ((1.00, 0.90, 0.76), -0.10),
    'light_blue':   ((0.72, 0.86, 1.00), -0.30),
    'light_deep':   ((0.55, 0.65, 1.00), -0.60),
    'light_purple': ((0.88, 0.72, 1.00), -0.25),
    'light_sunset': ((1.00, 0.82, 0.65), -0.10),
    'light_cycle':  (None,               -0.15),
}

# Suelo de ruido del estimador de B2, MEDIDO (no elegido): sobre sinteticas sin
# diferencia de iluminacion el residuo queda en dE 0.2-2.2. Por debajo de esto no se
# puede afirmar que haya componente de iluminacion; es el propio estimador.
RUIDO_ILUM = 2.5

BANDAS = [('agua alta', 0.12, 0.50), ('agua honda', 0.50, 0.75), ('suelo cercano', 0.90, 1.00)]
ASPECT_REF = None   # lo pone --aspect-ref; declarado aqui porque bandas_de() se define antes del parse
AVISOS = []


def lab_medio(a):
    rgb = np.asarray(a, dtype=np.float64) / 255.0
    m = rgb <= 0.04045
    lin = np.where(m, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)
    M = np.array([[0.4124564, 0.3575761, 0.1804375],
                  [0.2126729, 0.7151522, 0.0721750],
                  [0.0193339, 0.1191920, 0.9503041]])
    x = lin.reshape(-1, 3) @ M.T
    w = np.array([0.95047, 1.0, 1.08883]); t = x / w; d = 6.0 / 29.0
    f = np.where(t > d ** 3, np.cbrt(t), t / (3 * d * d) + 4.0 / 29.0)
    L = 116 * f[:, 1] - 16
    A = 500 * (f[:, 0] - f[:, 1]); B = 200 * (f[:, 1] - f[:, 2])
    return np.array([L.mean(), A.mean(), B.mean()])


def de(p, q): return float(np.sqrt(((p - q) ** 2).sum()))
def croma(v): return float(np.hypot(v[1], v[2]))


def tono(v):
    if croma(v) < 3.0: return None          # tono de una region acromatica = ruido
    return float(np.degrees(np.arctan2(v[2], v[1])) % 360.0)


def lin_medio(a):
    """RGB LINEAL medio. Hace falta para B2: el post es un producto por canal, y un
    producto solo es separable en un espacio lineal. En Lab no lo es."""
    rgb = np.asarray(a, dtype=np.float64) / 255.0
    m = rgb <= 0.04045
    lin = np.where(m, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)
    return lin.reshape(-1, 3).mean(axis=0)


def lab_de_lin(v):
    """Lab de un RGB lineal ya promediado (mismo camino que lab_medio, sin el promedio)."""
    M = np.array([[0.4124564, 0.3575761, 0.1804375],
                  [0.2126729, 0.7151522, 0.0721750],
                  [0.0193339, 0.1191920, 0.9503041]])
    x = np.clip(np.asarray(v, dtype=np.float64), 0, None) @ M.T
    w = np.array([0.95047, 1.0, 1.08883]); t = x / w; d = 6.0 / 29.0
    f = np.where(t > d ** 3, np.cbrt(t), t / (3 * d * d) + 4.0 / 29.0)
    return np.array([116 * f[1] - 16, 500 * (f[0] - f[1]), 200 * (f[1] - f[2])])


# ⚠⚠ COMPARAR TELE CONTRA MOVIL: EL ALTO CUADRA, EL ANCHO NO (2026-08-31)
#    `AquariumCameraController` hace `orthographicSize = worldHalfHeight` y saca el ancho del
#    aspect (`camHalfW = size * aspect`). ⇒ el ALTO de mundo visible es IDENTICO en las dos
#    pantallas, asi que las bandas —que son fracciones de alto— caen sobre el MISMO mundo.
#    Eso es lo que hace comparables las dos tablas, y es medida, no suposicion.
#    Pero el ANCHO no: tele 2560x1440 = 1.78 contra movil 2400x1080 = 2.22 ⇒ el movil ve un
#    25 % MAS de mundo a los lados, y una media sobre el ancho completo lo integra. En la
#    banda del suelo eso es suelo lejano nieblado de mas, o sea sesgo en la direccion que
#    justo se persiguio en §0.6.2.
# 🎯 `--aspect-ref R` recorta cada imagen, CENTRADA, al aspect R, para que las dos medias
#    integren el mismo trozo de mundo. Se deriva del aspect de CADA imagen, no de un factor
#    tecleado a mano: un movil distinto se corrige solo en vez de fallar en silencio.
def bandas_de(p):
    im = Image.open(p).convert('RGB'); w, h = im.size
    if ASPECT_REF:
        a = w / float(h)
        if a > ASPECT_REF + 1e-4:
            nw = int(round(h * ASPECT_REF)); x0 = (w - nw) // 2
            im = im.crop((x0, 0, x0 + nw, h)); w = nw
        elif a < ASPECT_REF - 1e-4:
            AVISOS.append("%s tiene aspect %.3f, MENOR que la referencia %.3f: no se puede "
                          "recortar para igualarlo (le falta mundo a los lados). Sin recorte."
                          % (os.path.basename(p), a, ASPECT_REF))
    lab, lin = {}, {}
    for n, y0, y1 in BANDAS:
        c = im.crop((0, int(h * y0), w, int(h * y1)))
        lab[n] = lab_medio(c); lin[n] = lin_medio(c)
    return lab, lin


def fmt(v):
    t = tono(v)
    return "%6.1f %6.1f %s" % (v[0], croma(v), ("%5.0f" % t) if t is not None else "   --")


def orden(x): return list(NOMINAL).index(x) if x in NOMINAL else 99


ap = argparse.ArgumentParser()
ap.add_argument('--dir', default='_luces')
ap.add_argument('--aspect-ref', type=float, default=None,
                help='recorta cada imagen CENTRADA a este aspect (ancho/alto) antes de medir, '
                     'para comparar dos pantallas de aspect distinto. Tele = 1.7778.')
args = ap.parse_args()
ASPECT_REF = args.aspect_ref
AVISOS = []

# ── carga: estaticas (luz_<id>.png) y ciclo (cycle_f<N>.png) ──────────────────
est, lin, ciclo, md5s = {}, {}, [], {}
shas = {}   # basename -> sha256[:16], para atar el acta a ESTOS pixeles
for f in sorted(glob.glob(os.path.join(args.dir, 'luz_*.png'))):
    if os.path.getsize(f) < 50000:
        print("!! descartada por tamano (%d B): %s" % (os.path.getsize(f), f)); continue
    raw = open(f, 'rb').read()
    md5s.setdefault(hashlib.md5(raw).hexdigest(), []).append(os.path.basename(f))
    shas[os.path.basename(f)[:-4]] = hashlib.sha256(raw).hexdigest()[:16]
    k = os.path.basename(f)[4:-4]
    est[k], lin[k] = bandas_de(f)
for f in sorted(glob.glob(os.path.join(args.dir, 'cycle_f*.png'))):
    if os.path.getsize(f) < 50000: continue
    raw = open(f, 'rb').read()
    md5s.setdefault(hashlib.md5(raw).hexdigest(), []).append(os.path.basename(f))
    shas[os.path.basename(f)[:-4]] = hashlib.sha256(raw).hexdigest()[:16]
    ciclo.append((os.path.basename(f), bandas_de(f)[0]))

if not est:
    sys.exit("sin capturas en %s (esperaba luz_<id>.png)" % args.dir)

# ⚠⚠ FOTOGRAMA CONGELADO: `adb exec-out screencap` devuelve capturas byte a byte
#    identicas sin dar error, con la app viva y el log sano (visto el 28-ago).
#    Dos capturas iguales de dos presets distintos = el cambio NO llego.
rep = [v for v in md5s.values() if len(v) > 1]
if rep:
    print("!!!! CAPTURAS IDENTICAS (fotograma congelado -- el cambio no llego):")
    for v in rep:
        print("     " + ", ".join(v))
    print()

falta = [k for k in NOMINAL if k not in est and k != 'light_cycle']
if falta:
    print("!! faltan presets: %s\n" % ", ".join(sorted(falta)))

# ── PROCEDENCIA ──────────────────────────────────────────────────────────────
# Avisada por la sesion del repo movil (2026-08-30): pegue una tabla de B2 con numeros
# de FIXTURE SINTETICA y nombres de preset reales, y leia como un hallazgo. Casi se lo
# cuenta al user como "light_blue es solo tinte". El texto de alrededor lo aclaraba; el
# BLOQUE no, y un pantallazo suelto viaja sin el texto de alrededor.
#
# 🧭 Mismo principio que la linea HORNEADO: y que el md5 de las capturas -- el artefacto
#    debe decir lo que es. Y NO se resuelve con un flag --device: un flag miente igual de
#    facil que un pie de foto. Se resuelve reportando QUE EVIDENCIA HAY:
#    el acta que escribe barre-luces.sh, con el sello del receptor dentro.
acta = sorted(glob.glob(os.path.join(args.dir, 'acta_*.txt')))
horneado = sello = ""
if acta:
    txt = io.open(acta[0], encoding='utf-8', errors='replace').read()
    for l in txt.splitlines():
        if 'HORNEADO:' in l: horneado = l.strip()
        if l.strip().startswith('RP: '): sello = l.strip()

sha_acta = {}
if acta:
    for l in txt.splitlines():
        if 'sha256=' in l:
            nom = l.split()[0]
            sha_acta[nom] = l.split('sha256=')[1].split()[0].strip()

# ¿cubre el acta TODAS las capturas que se van a analizar, y con el mismo contenido?
sin_acta, cambiadas = [], []
for nom, h in sorted(shas.items()):
    if nom not in sha_acta:
        sin_acta.append(nom)
    elif not h.startswith(sha_acta[nom]) and not sha_acta[nom].startswith(h):
        cambiadas.append(nom)

# ⚠⚠ POST MORTEM (2026-08-31): una captura tomada DESPUES de que muera la sesion no es el
#    acuario — es la pantalla negra del apagado o el lanzador de Android TV — y entra en las
#    tablas sin dar el menor error. Paso de verdad: `barre-luces.sh` dejaba a la rafaga del
#    ciclo menos tiempo del que tarda, y `cycle_f8`/`cycle_f9` (t=276 y 281 s, sesion muerta
#    a los 271,3) inflaron el RANGO de light_cycle: L* min 4,6 en la banda de agua.
#    🧭 El acreditador comprobaba QUE los pixeles son los del acta (sha256), pero no CUANDO
#    se tomaron. Atar el fichero al acta no basta si el acta no dice cuando acabo la fiesta.
#    Se detecto porque la PREDICCION impresa por esta misma herramienta no se cumplio.
tfin = None
for lg in sorted(glob.glob(os.path.join(args.dir, 'sender_*.log'))) + (acta or []):
    try: cont = io.open(lg, encoding='utf-8', errors='replace').read()
    except Exception: continue
    for l in cont.splitlines():
        if 'DURACION DE SESION' in l or 'DURACIÓN DE SESIÓN' in l:
            m = re.search(r'([0-9.]+)s', l.split(':', 1)[1] if ':' in l else l)
            if m: tfin = float(m.group(1))
    if tfin is not None: break

t_cap = {}
if acta:
    for l in txt.splitlines():
        m = re.match(r'\s*(\S+)\s+t=([0-9]+)s\s', l)
        if m: t_cap[m.group(1)] = int(m.group(2))

# ⚠⚠ ¿DONDE ESTA EL BORDE DEL SUELO? (2026-08-31, y la primera version ESTABA MAL)
#    Hizo falta para comparar tele contra movil, y el primer detector —"la fila con el mayor
#    salto de luminancia en la mitad baja"— dio TELE 0.9273 contra MOVIL 0.7792, o sea 160 px
#    de desajuste, y estuve a punto de reportarlo como que las dos pantallas encuadran el
#    tanque distinto. ERA FALSO: en el movil el mayor salto de luz ES el borde agua/grava,
#    pero en la tele lo es la BANDA OSCURA DEL FONDO (niebla + viñeta), que es mas fuerte.
#    El detector medía UN RASGO DISTINTO EN CADA PANTALLA y reportaba las dos cifras en las
#    mismas unidades. Con el criterio de color el borde sale 0.7921 contra 0.7792: 14 px.
#    🧭 Es [[el_instrumento_no_ve_la_magnitud]] en una forma nueva y peor: no es que el
#    instrumento no vea la magnitud, es que ve OTRA y no lo dice.
#    ⇒ De ahi que aqui NO se elija un criterio: se usan LOS DOS y, si no coinciden, la
#    herramienta se calla en vez de inventarse un borde. Un borde mal medido no es un dato
#    peor: es un dato que apunta a una conclusion sobre geometria que no existe.
def _borde_por_color(a):
    # agua = azul (B>R) · grava = calida (R>B). Una viñeta baja los tres canales por igual,
    # asi que no mueve el cruce. ⚠ Con una luz muy azul (light_deep) no hay cruce: devuelve None.
    h = a.shape[0]; d = (a[:, :, 2] - a[:, :, 0]).mean(axis=1); s = np.sign(d)
    for i in range(int(h * 0.30), h - 1):
        if s[i] > 0 and s[i + 1] <= 0: return (i + 0.5) / h
    return None

def _borde_por_luz(a):
    h = a.shape[0]
    lum = (0.2126 * a[:, :, 0] + 0.7152 * a[:, :, 1] + 0.0722 * a[:, :, 2]).mean(axis=1)
    dd = np.abs(np.diff(lum)); lo = int(h * 0.55)
    return (lo + int(np.argmax(dd[lo:int(h * 0.98)])) + 0.5) / h

def _borde_suelo(p):
    im = Image.open(p).convert('RGB')
    if ASPECT_REF:
        w, h = im.size; a_ = w / float(h)
        if a_ > ASPECT_REF + 1e-4:
            nw = int(round(h * ASPECT_REF)); im = im.crop(((w - nw) // 2, 0, (w - nw) // 2 + nw, h))
    a = np.asarray(im, dtype=float)
    bc, bl = _borde_por_color(a), _borde_por_luz(a)
    if bc is None or abs(bc - bl) > 0.02: return None      # los dos criterios no coinciden
    return (bc + bl) / 2.0

_refs = sorted(glob.glob(os.path.join(args.dir, 'luz_*.png')))
_ok = [b for b in (_borde_suelo(f) for f in _refs) if b is not None]
if len(_ok) >= 2:
    _b = sum(_ok) / len(_ok); _disp = max(_ok) - min(_ok)
    _y0 = [y0 for n_, y0, y1 in BANDAS if n_ == 'suelo cercano'][0]
    print("   borde del suelo: %.4f del alto  (%d/%d capturas con los DOS criterios de acuerdo"
          "; dispersion %.4f)" % (_b, len(_ok), len(_refs), _disp))
    if _y0 < _b - 0.005:
        print("!! la banda 'suelo cercano' empieza en %.2f, POR ENCIMA del borde (%.4f): "
              "un %.0f %% de esa banda es AGUA."
              % (_y0, _b, 100.0 * (_b - _y0) / (1.0 - _y0)))
    if _disp > 0.02:
        print("!! el borde se mueve %.4f entre capturas: la camara no estaba quieta." % _disp)
else:
    print("   borde del suelo: NO SE MIDE con fiabilidad en esta tanda (los dos criterios "
          "discrepan o la luz tapa el cruce de color). No se comprueba la banda.")

postmortem = []
if tfin is not None:
    for nom, tc in sorted(t_cap.items()):
        if tc >= tfin: postmortem.append((nom, tc))
if postmortem:
    print("!!!! %d CAPTURA(S) TOMADAS CON LA SESION YA MUERTA (fin a los %.1f s) -- EXCLUIDAS:"
          % (len(postmortem), tfin))
    for nom, tc in postmortem:
        print("       %-12s t=%ss   (no es el acuario: pantalla de apagado o lanzador)" % (nom, tc))
    print("     No dan ningun error por si solas y entran en las tablas como si fueran datos.")
    fuera = set(n for n, _ in postmortem)
    est = {k: v for k, v in est.items() if ('luz_' + k) not in fuera}
    lin = {k: v for k, v in lin.items() if ('luz_' + k) not in fuera}
    ciclo = [(nm, b) for nm, b in ciclo if nm[:-4] not in fuera]
    shas = {k: v for k, v in shas.items() if k not in fuera}

if acta and horneado and not sin_acta and not cambiadas:
    TAG = "DEVICE"
    print("PROCEDENCIA: DEVICE -- acta %s (%s)" %
          (os.path.basename(acta[0]),
           time.strftime('%Y-%m-%d %H:%M', time.localtime(os.path.getmtime(acta[0])))))
    print("  %s" % horneado)
    if sello: print("  %s" % sello)
else:
    TAG = "FIXTURE / SIN ACREDITAR"
    if not acta:      motivo = "no hay acta en '%s'" % args.dir
    elif not horneado: motivo = "el acta no lleva linea HORNEADO (prueba de que corrio el player)"
    elif sin_acta:    motivo = "capturas que el acta NO cubre: %s" % ", ".join(sin_acta)
    else:             motivo = "capturas cuyo sha256 NO cuadra con el acta: %s" % ", ".join(cambiadas)
    print("!!!! PROCEDENCIA NO ACREDITADA -- %s." % motivo)
    print("     Estos numeros NO se pueden presentar como medidos en la tele. Son una")
    print("     FIXTURE mientras no haya un acta que lo demuestre. Si de verdad salen del")
    print("     device, la tanda no dejo acta y hay que repetirla: sin sello no hay dato.")
print("     capturas: %d estaticas, %d del ciclo" % (len(est), len(ciclo)))
print()

# ── A. absolutos ─────────────────────────────────────────────────────────────
print("A. ABSOLUTOS  [%s]   (L* = claridad | C* = croma | h = tono en grados, '--' si C*<3)" % TAG)
print("%-14s %-4s %s" % ("", "", "  ".join("%-21s" % n for n, _, _ in BANDAS)))
print("%-14s %-4s %s" % ("preset", "eur", "  ".join("%6s %6s %5s" % ("L*", "C*", "h") for _ in BANDAS)))
print("-" * 88)
for k in sorted(est, key=orden):
    print("%-14s %-4s %s" % (k, "" if k in GRATIS else "0.49",
                             "  ".join(fmt(est[k][n]) for n, _, _ in BANDAS)))

# ── B. delta contra light_white ──────────────────────────────────────────────
# light_white es filterColor (1,1,1) y exposure 0.00: el unico preset NEUTRO en post.
# El delta contra el aisla lo que hace cada preset, sin la escena de por medio.
ref = est.get('light_white')
if ref is not None:
    print("\nB. DELTA CONTRA light_white  [%s]   (unico neutro en post: filter (1,1,1), exp 0.00)" % TAG)
    print("   agua alta = telon UNLIT -> post casi puro | suelo = post + los 3 spots")
    print("%-14s %s" % ("preset", "  ".join("%-21s" % n for n, _, _ in BANDAS)))
    print("%-14s %s" % ("", "  ".join("%6s %6s %5s" % ("dL*", "dC*", "dE") for _ in BANDAS)))
    print("-" * 88)
    for k in sorted(est, key=orden):
        if k == 'light_white':
            continue
        cols = []
        for n, _, _ in BANDAS:
            v, r = est[k][n], ref[n]
            cols.append("%+6.1f %+6.1f %5.1f" % (v[0] - r[0], croma(v) - croma(r), de(v, r)))
        print("%-14s %s" % (k, "  ".join(cols)))

    # prediccion falsable del postExposure, SOLO en la banda unlit
    print("\n   postExposure esperado contra medido (solo 'agua alta', que es unlit):")
    print("   %-14s %8s %6s %13s %12s" % ("preset", "EV", "lum", "dL* previsto", "dL* medido"))
    Lref = ref['agua alta'][0]
    Yref = ((Lref + 16) / 116.0) ** 3
    for k in sorted(est, key=orden):
        if k == 'light_white' or k not in NOMINAL:
            continue
        filt, ev = NOMINAL[k]
        # el filtro tambien quita luz: su luminancia Rec.709 multiplica igual que el EV
        lum = 0.2126 * filt[0] + 0.7152 * filt[1] + 0.0722 * filt[2]
        Lprev = 116 * ((Yref * lum * (2.0 ** ev)) ** (1 / 3.0)) - 16
        got = est[k]['agua alta'][0] - Lref
        marca = "" if abs((Lprev - Lref) - got) < 4.0 else "   <<< REVISAR"
        print("   %-14s %+8.2f %6.3f %+13.1f %+12.1f%s" % (k, ev, lum, Lprev - Lref, got, marca))
    print("   previsto = EV + luminancia del filterColor, sobre una banda que se SUPONE unlit.")
    print("   Un REVISAR no dice 'la luz esta rota': dice que esa banda no es tan unlit como")
    print("   se asume (niebla de agua, bloom, un pez cruzando). Lo que NO puede pasar es que")
    print("   light_deep, a -0.60 EV y filtro 0.65, no se oscurezca claramente.")

# ── B2. LA DESCOMPOSICION: post contra iluminacion ───────────────────────────
# Pedida por la sesion del repo movil (2026-08-30): "dame la DIFERENCIA entre bandas
# explicita, es lo que quiero pegar en la nota, mas que los absolutos".
#
# El telon de la banda de agua es UNLIT -> ahi el preset solo puede actuar por el post.
# El suelo recibe post + los 3 spots.
#
# ⚠⚠ NO SE RESTAN DELTAS DE Lab. El primer intento hacia ILUM = delta(suelo) - delta(agua)
#    y sobre sinteticas CON LA MISMA iluminacion en las dos bandas daba ILUM de 4.4 a 16.5
#    en vez de 0: Lab es no lineal, asi que el MISMO factor multiplicativo mueve mas L* en
#    una banda oscura que en una clara, y esa curvatura se colaba entera como "iluminacion".
#    Habria mandado a la nota del movil un artefacto del espacio de color.
#
# Lo correcto: el post es un PRODUCTO POR CANAL, y un producto solo es separable en un
# espacio LINEAL. Se mide la ganancia del post donde actua solo (agua), se aplica a la
# referencia del suelo, y lo que sobra es la iluminacion:
#     ganancia = lin_k(agua)  / lin_white(agua)          <- el post, medido
#     previsto = lin_white(suelo) * ganancia             <- el suelo si SOLO cambiara el post
#     ILUM     = medido(suelo) - previsto                <- lo que ponen los spots
# Calibrado: sobre las sinteticas sin diferencia de iluminacion, ILUM baja a ~0.
if ref is not None and 'light_white' in lin:
    print("\nB2. DESCOMPOSICION  [%s]  -- que parte es POST y que parte es LUZ DE VERDAD" % TAG)
    print("    post  = lo que haria el filtro solo, medido en el agua (unlit) y llevado al suelo")
    print("    total = lo que se ve de verdad en el suelo | ILUM = total - post (los 3 spots)")
    print("    Todo contra light_white, en Lab sobre medias LINEALES (ver el porque arriba).")
    print("%-14s %10s %9s   %10s %9s   %10s %9s   %s" %
          ("preset", "post dL*", "post dE", "total dL*", "total dE", "ILUM dL*", "ILUM dE",
           "lectura (ruido del estimador dE 2.2 ; umbral %.1f)" % RUIDO_ILUM))
    print("-" * 135)
    base_agua  = np.maximum(lin['light_white']['agua alta'], 1e-6)
    base_suelo = lin['light_white']['suelo cercano']
    lab_ref_suelo = lab_de_lin(base_suelo)
    for k in sorted(est, key=orden):
        if k == 'light_white':
            continue
        ganancia = lin[k]['agua alta'] / base_agua
        lab_prev = lab_de_lin(base_suelo * ganancia)
        lab_med  = lab_de_lin(lin[k]['suelo cercano'])
        dpost, dtot, dilu = lab_prev - lab_ref_suelo, lab_med - lab_ref_suelo, lab_med - lab_prev
        mod = lambda v: float(np.sqrt((v ** 2).sum()))
        m = mod(dilu)
        # el veredicto va EN LA FILA, no en el texto de debajo: una nota se lee por
        # columnas tres meses despues, y el umbral tiene que viajar CON el numero.
        lec = ("SOLO TINTE (ILUM <= ruido)" if m <= RUIDO_ILUM else
               "ilumina, poco" if m < 2 * RUIDO_ILUM else "ILUMINA de verdad")
        print("%-14s %10.1f %9.1f   %10.1f %9.1f   %10.1f %9.1f   %s" %
              (k, dpost[0], mod(dpost), dtot[0], mod(dtot), dilu[0], m, lec))
    print("\n    LECTURA: |ILUM| grande = el preset se nota porque ILUMINA distinto.")
    print("    |ILUM| ~ 0 = el preset es SOLO un filtro de color encima, y entonces da igual")
    print("    lo que valgan sus spots: al user le llega un tinte, no una luz.")
    print("    Es la cifra que decide si los 5 presets de pago valen lo que cuestan.")
    print("    El umbral %.1f es el SUELO DE RUIDO del estimador, MEDIDO: sobre sinteticas" % RUIDO_ILUM)
    print("    sin diferencia de iluminacion da ILUM dE 0.2-2.2, y sobre una con iluminacion")
    print("    propia inyectada en el suelo da 36.1 mientras las otras cuatro siguen en 0.2-1.3.")
    print("    !! Sigue siendo una ESTIMACION: el suelo lleva bloom y niebla, que no son un")
    print("      producto por canal. Vale para el reparto y el signo, no para la 3a cifra.")

# ── C. ¿son diferenciales? dE al vecino mas parecido, POR BANDA ───────────────
print("\nC. DIFERENCIALES?  [%s]  -- dE76 al vecino mas parecido, banda a banda" % TAG)
print("   Se juzga por la banda MAS FAVORABLE: si en alguna se distinguen, el user los distingue.")
print("%-14s %s   %s" % ("preset", "  ".join("%-16s" % n for n, _, _ in BANDAS), "lectura"))
print("-" * 88)
peor = {}
for k in sorted(est, key=orden):
    cols, mejor, quien = [], -1.0, '?'
    for n, _, _ in BANDAS:
        o = [(de(est[k][n], est[j][n]), j) for j in est if j != k]
        if not o:
            continue
        d, j = min(o)
        cols.append("%6.1f %-9s" % (d, j.replace('light_', '')))
        if d > mejor:
            mejor, quien = d, j
    if mejor < 2.0:
        lec = "FUNDIDO"
    elif mejor < 5.0:
        lec = "casi indistinguible"
    else:
        lec = "se distingue"
    if mejor < 5.0 and (k in GRATIS) != (quien in GRATIS):
        lec += "  <<< CRUZA LINEA DE PRECIO"
    peor[k] = (mejor, quien, lec)
    print("%-14s %s   %s" % (k, "  ".join(cols), lec))

fund = [k for k, v in peor.items() if v[0] < 2.0]
print("=" * 88)
print("fundidos en las TRES bandas: %s" % (", ".join(fund) if fund else "ninguno"))

# ── D. light_cycle: es una SERIE, no un punto ────────────────────────────────
# ⚠⚠ CAST_PARIDAD_VISUAL.md:452 — con light_cycle puesto DOS CAPTURAS NO SON
#    COMPARABLES: Update() reescribe el color de los spots y el colorFilter cada frame,
#    hue = Repeat(Time.time * 0.07, 1) -> periodo 1/0.07 = 14.3 s.
#    Por eso va FUERA de las tablas B y C y se reporta como RANGO sobre un periodo entero.
if ciclo:
    print("\nD. light_cycle  [%s]  -- RANGO sobre un periodo (0.07 Hz -> 14.3 s), %d fotogramas" % (TAG, len(ciclo)))
    print("   NO entra en B ni en C: no tiene un valor, tiene un recorrido.")
    print("%-14s %7s %7s %7s   %7s %7s   %s" %
          ("banda", "L* min", "L* med", "L* max", "C* med", "C* max", "recorrido dE"))
    print("-" * 88)
    for n, _, _ in BANDAS:
        vs = [v[n] for _, v in ciclo]
        Ls = [v[0] for v in vs]
        Cs = [croma(v) for v in vs]
        recorrido = max(de(a, b) for a in vs for b in vs)
        print("%-14s %7.1f %7.1f %7.1f   %7.1f %7.1f   %7.1f" %
              (n, min(Ls), sum(Ls) / len(Ls), max(Ls), sum(Cs) / len(Cs), max(Cs), recorrido))
    print("\n   PREDICCION: el ciclo mueve los SPOTS a saturacion HSV 0.90 pero el colorFilter")
    print("   solo entre 0.82 y 1.00 por canal. => recorrido GRANDE en 'suelo cercano' y")
    print("   PEQUENO en 'agua alta'. Si sale al reves, el que se mueve no es la luz.")
    print("\n   !! Un recorrido pequeno en las TRES bandas no significa 'el ciclo no se ve':")
    print("     significa que las capturas no cubren el periodo. Comprobar el acta.")
