# -*- coding: utf-8 -*-
"""
¿Se DISTINGUEN los fondos/suelos en la tele? (2026-08-28)

Criterio del user: "los fondos, suelos, luces y todo eso deben ser diferenciales como en
la app movil". Varios son de pago: si en la tele se ven iguales, el usuario paga por algo
que no ve.

Se mide la distancia de color (dE76 en Lab) de cada elemento a su VECINO MAS PARECIDO,
y se compara el dE EN PANTALLA contra el dE DEL ARTE. Dos numeros distintos:
  · arte      lo que el diseñador separo
  · pantalla  lo que el usuario distingue de verdad, tras agua, niebla, grado y bloom

⚠⚠ SE MIDE POR BANDA, NO EL ENCUADRE ENTERO. Con el suelo al 53 % de blanco, cualquier
medida global devuelve "todos identicos" y no es por el fondo: es el suelo dominando.
Esa trampa ya costo una tabla entera hoy.
"""
import sys, glob, os
import numpy as np
from PIL import Image

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

def banda(p, y0, y1):
    a = Image.open(p).convert('RGB'); h = a.size[1]
    return lab_medio(a.crop((0, int(h * y0), a.size[0], int(h * y1))))

# ── que se mide: fondos (banda de arriba) o sustratos (banda del suelo) ───────
QUE = sys.argv[1] if len(sys.argv) > 1 else 'bg'
if QUE == 'bg':
    ARTE, CAPT, PRE, Y0, Y1 = 'Assets/Resources/Backgrounds/bg_*.png', '_bloom/bg_bg_*.png', 'bg_bg_', 0.12, 0.50
else:
    ARTE, CAPT, PRE, Y0, Y1 = 'Assets/Resources/Substrates/sub_*.png', '_bloom/subcand_sub_*.png', 'subcand_', 0.80, 0.93

arte = {}
for f in sorted(glob.glob(ARTE)):
    arte[os.path.basename(f)[:-4]] = lab_medio(Image.open(f).convert('RGB'))

pant = {}
for f in sorted(glob.glob(CAPT)):
    if os.path.getsize(f) < 50000: continue
    n = os.path.basename(f)[:-4]
    n = n.replace('bg_bg_', 'bg_') if QUE == 'bg' else n.replace('subcand_', '')
    pant[n] = banda(f, Y0, Y1)
if not pant: sys.exit("sin capturas para '%s'" % QUE)

def vecino(d, k):
    o = [(de(d[k], d[j]), j) for j in d if j != k]
    return min(o)

print("%-18s %18s %20s   %s" % (QUE, "dE arte (vecino)", "dE PANTALLA (vecino)", "lectura"))
print("-" * 84)
fund = []
for k in sorted(pant, key=lambda x: vecino(pant, x)[0]):
    da, na = vecino(arte, k) if k in arte else (float('nan'), '?')
    dp, np_ = vecino(pant, k)
    if dp < 2.0:   lec = "FUNDIDO en pantalla"; fund.append(k)
    elif dp < 5.0: lec = "casi indistinguible"
    else:          lec = "se distingue"
    corta = lambda z: z.replace('bg_', '').replace('sub_', '')
    print("%-18s %8.1f  %-10s %8.1f  %-10s   %s" % (k, da, corta(na), dp, corta(np_), lec))
print("=" * 84)
print("fundidos (dE<2 en pantalla): %s" % (", ".join(fund) if fund else "ninguno"))
