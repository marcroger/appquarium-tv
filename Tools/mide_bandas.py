# -*- coding: utf-8 -*-
"""
Mide el quemado POR BANDAS (2026-08-28).

⚠⚠ POR QUE HIZO FALTA: midiendo la imagen entera, los 11 fondos dieron cifras IDENTICAS
(clip ~10 %, P99 99.7, croma 0.0) incluido `bg_abyss`, que es negro. No era el fondo:
era EL SUELO, que es constante en todos y domina la cola alta. Una metrica global no
puede responder "que fondo quema" cuando hay un objeto fijo mas brillante que todos.

Bandas (fraccion de alto), elegidas mirando la captura:
  agua    0.12-0.50   fondo + agua, sin HUD
  suelo   0.80-0.93   la arena
"""
import sys, glob, os
import numpy as np
from PIL import Image

def lab(a):
    rgb = np.asarray(a, dtype=np.float64) / 255.0
    m = rgb <= 0.04045
    lin = np.where(m, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)
    M = np.array([[0.4124564, 0.3575761, 0.1804375],
                  [0.2126729, 0.7151522, 0.0721750],
                  [0.0193339, 0.1191920, 0.9503041]])
    x = lin @ M.T
    w = np.array([0.95047, 1.0, 1.08883]); t = x / w; d = 6.0 / 29.0
    f = np.where(t > d ** 3, np.cbrt(t), t / (3 * d * d) + 4.0 / 29.0)
    L = 116 * f[..., 1] - 16
    aa = 500 * (f[..., 0] - f[..., 1]); bb = 200 * (f[..., 1] - f[..., 2])
    return L, np.sqrt(aa * aa + bb * bb)

BANDAS = {'agua': (0.12, 0.50), 'suelo': (0.80, 0.93)}

def banda(p, cual):
    a = np.asarray(Image.open(p).convert('RGB')); h = a.shape[0]
    y0, y1 = BANDAS[cual]
    L, C = lab(a[int(h * y0):int(h * y1)])
    return dict(L=L.mean(), C=C.mean(), clip=100.0 * (L > 95).mean(),
                p99=np.percentile(L, 99))

fich = []
for pat in sys.argv[1:]:
    fich += sorted(glob.glob(pat))
if not fich: sys.exit("sin ficheros")

print("%-34s %s" % ("", "        AGUA                    SUELO"))
print("%-34s %6s %6s %6s   %6s %6s %6s %6s" %
      ("captura", "L*", "C*", "clip%", "L*", "C*", "clip%", "P99"))
print("-" * 86)
for p in fich:
    if os.path.getsize(p) < 50000: continue
    a = banda(p, 'agua'); s = banda(p, 'suelo')
    marca = "  <<< SUELO CLAVADO AL BLANCO" if s['clip'] > 25 else ""
    print("%-34s %6.1f %6.1f %6.2f   %6.1f %6.1f %6.2f %6.1f%s" %
          (os.path.basename(p)[:-4][:34], a['L'], a['C'], a['clip'],
           s['L'], s['C'], s['clip'], s['p99'], marca))
