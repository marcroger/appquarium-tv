# -*- coding: utf-8 -*-
"""
Mide si el BLOOM quema las altas luces (2026-08-28, v2).

⚠⚠ POR QUE NO VALE LA MEDIA DE L*: un bloom que quema afecta al 1-5 % de pixeles mas
claros. La media de la escena apenas se mueve -> MIRAR LA MEDIA DIRIA QUE NO PASA NADA.
Lo que delata el quemado es la COLA ALTA, y en concreto DOS cosas a la vez:

  · %clip    pixeles con L* > 95 (practicamente blancos)
  · C*@P99   croma del percentil 99 -> QUEMAR ES PERDER COLOR, no solo subir luz.
             Un bloom sano sube la luz MANTENIENDO el color (croma alto en la cola).
             Un bloom que quema empuja al blanco: croma -> 0.

🧭 El criterio es la CONJUNCION. Ninguna de las dos por separado vale:
   · %clip alto con croma alto  = brillo de color intenso, no quemado (agua turquesa)
   · croma bajo con %clip bajo  = escena gris, no quemado (bg_cave, bg_abyss)
"""
import sys, glob, os
import numpy as np
from PIL import Image

UMBRAL_CLIP = 3.0     # % de imagen practicamente blanca
UMBRAL_CROMA = 12.0   # croma de la cola alta por debajo del cual es blanco de verdad

# ⚠⚠ LINEA BASE DEL ARTE (aportada por la sesion del repo movil, 2026-08-28).
# Un fondo puede traer BLANCO DE FABRICA: hielo, una luna, rayos de luz. Eso mide
# "clip + croma bajo" sin que el bloom haya tocado nada -> FALSO POSITIVO.
# Se calcula aqui desde los PNG de origen (`Resources.Load<Texture2D>("Backgrounds/<id>")`)
# en vez de copiar sus cifras, para que sea reproducible y no otro dato escrito a mano.
#
# ⚠ NO es restable del %clip de la captura: en pantalla el fondo ocupa una fraccion y va
#    teñido por agua, niebla y grado. Sirve para saber DONDE mirar con lupa, no para restar.
def base_arte(bgid):
    import os
    f = 'Assets/Resources/Backgrounds/%s.png' % bgid
    if not os.path.exists(f): return None
    a = np.asarray(Image.open(f).convert('RGB'))
    L, C = lab(a)
    return 100.0 * ((L > 90) & (C < 40)).mean()

def lab(im):
    rgb = np.asarray(im, dtype=np.float64) / 255.0
    m = rgb <= 0.04045
    lin = np.where(m, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)
    M = np.array([[0.4124564, 0.3575761, 0.1804375],
                  [0.2126729, 0.7151522, 0.0721750],
                  [0.0193339, 0.1191920, 0.9503041]])
    x = lin @ M.T
    w = np.array([0.95047, 1.0, 1.08883]); t = x / w; d = 6.0 / 29.0
    f = np.where(t > d ** 3, np.cbrt(t), t / (3 * d * d) + 4.0 / 29.0)
    L = 116 * f[..., 1] - 16
    a = 500 * (f[..., 0] - f[..., 1]); b = 200 * (f[..., 1] - f[..., 2])
    return L, np.sqrt(a * a + b * b)

def stats(p):
    a = np.asarray(Image.open(p).convert('RGB'))
    h = a.shape[0]
    a = a[int(h * .08):int(h * .94)]        # fuera HUD arriba y borde abajo
    L, C = lab(a)
    alta = L >= np.percentile(L, 99)
    return dict(L=L.mean(), C=C.mean(), clip=100.0 * (L > 95).mean(),
                p99=np.percentile(L, 99), c99=C[alta].mean())

pref = sys.argv[1] if len(sys.argv) > 1 else 'bg'
fich = sorted(glob.glob('_bloom/%s_*.png' % pref))
if not fich:
    sys.exit("sin capturas de '%s' en _bloom/" % pref)

print("%-14s %6s %6s %7s %7s %7s %8s   %s" %
      ("elemento", "L*", "C*", "%clip", "P99", "C*@P99", "arte", "veredicto"))
print("-" * 82)
malos = []
for p in fich:
    if os.path.getsize(p) < 50000:
        print("%-16s   (captura mala, %d B)" % (os.path.basename(p)[:-4], os.path.getsize(p)))
        continue
    n = os.path.basename(p)[:-4]
    if n.startswith(pref + '_'): n = n[len(pref) + 1:]      # bg_bg_kelp -> bg_kelp
    s = stats(p)
    b = base_arte(n) if pref == 'bg' else None
    if s['clip'] > UMBRAL_CLIP and s['c99'] < UMBRAL_CROMA:
        if b is not None and b > 0.05:
            v = "revisar: el arte YA trae blanco"; malos.append(n)
        else:
            v = "<<< QUEMA"; malos.append(n)
    elif s['clip'] > UMBRAL_CLIP:
        v = "brillo con color, OK"
    else:
        v = "ok"
    print("%-14s %6.1f %6.1f %7.2f %7.1f %7.1f %8s   %s"
          % (n, s['L'], s['C'], s['clip'], s['p99'], s['c99'],
             ("%.2f%%" % b) if b is not None else "-", v))

print("=" * 82)
if malos:
    print("!! QUEMAN: %s" % ", ".join(malos))
    sys.exit(1)
print("OK  ninguno quema con el bloom actual (umbral: clip>%.0f%% Y croma<%.0f)"
      % (UMBRAL_CLIP, UMBRAL_CROMA))
