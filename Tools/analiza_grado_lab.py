#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Mide un barrido de grado en L*a*b*, por bandas de la imagen.

POR QUE (2026-08-27): `grade_contact_sheet.py` informa media de canales RGB y saturacion HSV.
Las dos enganan justo en lo que se esta midiendo aqui:

  · La media RGB SUBE al desaturar un verde saturado (0,150,0 -> gris 107): la variante de
    control `Z`, que desatura del todo, salio como la MAS clara de las ocho sin serlo.
  · La saturacion HSV de un verde OSCURO es alta, asi que el fondo en penumbra puntua mas
    "saturado" que el suelo vivo, que es al reves de lo que se ve.

L* (claridad perceptual) y C* (croma perceptual) no tienen esos dos sesgos, y son las mismas
unidades con las que se midio la niebla de agua el 25-ago, asi que las cifras se pueden comparar
con las de aquel dia.

Uso:  python Tools/analiza_grado_lab.py --dir _gradetune
"""
import argparse, glob, os
import numpy as np
from PIL import Image

def srgb_a_lab(rgb):
    """rgb en [0,1] -> L*, a*, b*. D65."""
    m = rgb <= 0.04045
    lin = np.where(m, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)
    M = np.array([[0.4124564, 0.3575761, 0.1804375],
                  [0.2126729, 0.7151522, 0.0721750],
                  [0.0193339, 0.1191920, 0.9503041]])
    xyz = lin @ M.T
    blanco = np.array([0.95047, 1.0, 1.08883])
    t = xyz / blanco
    d = 6.0 / 29.0
    f = np.where(t > d ** 3, np.cbrt(t), t / (3 * d * d) + 4.0 / 29.0)
    L = 116 * f[..., 1] - 16
    a = 500 * (f[..., 0] - f[..., 1])
    b = 200 * (f[..., 1] - f[..., 2])
    return L, a, b

def bandas(h):
    """La escena es 2.5D: telon de fondo arriba, suelo abajo. Se parte por filas."""
    return [("fondo alto",  0,            int(h * 0.30)),
            ("fondo medio", int(h * 0.30), int(h * 0.60)),
            ("suelo",       int(h * 0.75), h)]

ap = argparse.ArgumentParser()
ap.add_argument('--dir', default='_gradetune')
args = ap.parse_args()

ficheros = sorted(f for f in glob.glob(os.path.join(args.dir, '*.png'))
                  if 'contact_sheet' not in os.path.basename(f))
if not ficheros:
    raise SystemExit("no hay capturas en " + args.dir)

print("L* = claridad perceptual (0 negro, 100 blanco) | C* = croma perceptual (0 = gris)\n")
filas = []
for f in ficheros:
    im = np.asarray(Image.open(f).convert('RGB'), dtype=np.float64) / 255.0
    L, a, b = srgb_a_lab(im)
    C = np.sqrt(a * a + b * b)
    fila = {'nombre': os.path.splitext(os.path.basename(f))[0]}
    for etiqueta, y0, y1 in bandas(im.shape[0]):
        fila[etiqueta] = (L[y0:y1].mean(), C[y0:y1].mean())
    filas.append(fila)

cab = f"{'variante':<24}" + "".join(f"{e:>22}" for e, _, _ in bandas(100))
print(cab); print("-" * len(cab))
for fila in filas:
    linea = f"{fila['nombre']:<24}"
    for etiqueta, _, _ in bandas(100):
        Lm, Cm = fila[etiqueta]
        linea += f"{'L*%5.1f C*%5.1f' % (Lm, Cm):>22}"
    print(linea)

base = next((f for f in filas if 'A_build' in f['nombre']), filas[0])
print(f"\nDiferencias contra {base['nombre']} (lo que sale del build hoy):\n")
cab2 = f"{'variante':<24}" + "".join(f"{e:>22}" for e, _, _ in bandas(100))
print(cab2); print("-" * len(cab2))
for fila in filas:
    if fila is base: continue
    linea = f"{fila['nombre']:<24}"
    for etiqueta, _, _ in bandas(100):
        dL = fila[etiqueta][0] - base[etiqueta][0]
        dC = fila[etiqueta][1] - base[etiqueta][1]
        linea += f"{'%+5.1f L*  %+5.1f C*' % (dL, dC):>22}"
    print(linea)

# ⚠ Guarda: si el barrido no separa las variantes, la tabla no dice nada y hay que decirlo.
rango = max(f['fondo medio'][1] for f in filas) - min(f['fondo medio'][1] for f in filas)
print(f"\nRango de croma en el fondo medio entre variantes: {rango:.1f}")
if rango < 1.0:
    print("[!] Las variantes NO se separan: el grado no se esta aplicando, o el barrido no mide.")
