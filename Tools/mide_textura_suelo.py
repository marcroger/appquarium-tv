# -*- coding: utf-8 -*-
"""
¿Ha vuelto la TEXTURA de la arena, o solo ha dejado de clipar? (2026-08-28)

⚠⚠ POR QUE HACE FALTA (lo aporto la sesion del repo movil): `clip = 0` dice cuantos
pixeles estan clavados al blanco. NO dice si la arena tiene grano. Un suelo a 88 L* con
clip 0 puede seguir siendo UNA PLANCHA LISA: sin pixeles saturados y sin textura, porque
el rango que le queda es tan estrecho que no se distingue.
🧭 El criterio de aceptacion no es "clip 0", es "clip 0 Y textura recuperada".

COMO: energia de alta frecuencia de L* en la banda del suelo. Se resta a L* su version
suavizada (paso alto) y se mide la desviacion tipica, en unidades L* (perceptuales).
La referencia es el 27-ago, cuando el suelo se veia bien.

⚠ Se mide sobre L*, no sobre RGB: una plancha mas brillante tiene gradientes mayores en
RGB sin tener mas detalle. En L* la comparacion es honesta.
"""
import sys, glob, os
import numpy as np
from PIL import Image, ImageFilter

def L_estrella(a):
    rgb = np.asarray(a, dtype=np.float64) / 255.0
    m = rgb <= 0.04045
    lin = np.where(m, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)
    Y = lin @ np.array([0.2126729, 0.7151522, 0.0721750])
    d = 6.0 / 29.0
    f = np.where(Y > d ** 3, np.cbrt(Y), Y / (3 * d * d) + 4.0 / 29.0)
    return 116 * f - 16

def hf(p, y0=0.80, y1=0.93):
    """
    ⚠⚠ v2 — LA v1 DIO UN FALSO "TEXTURA OK 86 %" A UNA IMAGEN CON EL 53 % CLAVADO.
    Causa: la desviacion se calculaba sobre TODA la banda, y los bordes entre la zona
    clavada y la que no lo esta son gradientes enormes que sostienen la cifra solos.
    O sea: el instrumento no distinguia "detalle" de "frontera del defecto".
    🧭 Forma general, cuatro veces hoy: una metrica agregada sobre una region que
       CONTIENE el defecto mide el defecto, no lo que buscas.

    Cura (aportada por la sesion del repo movil): medir solo donde TODAVIA HAY RANGO,
    y erosionar la mascara para que la frontera no entre.
    """
    im = Image.open(p).convert('RGB')
    h = im.size[1]
    im = im.crop((0, int(h * y0), im.size[0], int(h * y1)))
    L = L_estrella(im)
    Ls = L_estrella(im.filter(ImageFilter.GaussianBlur(radius=2.0)))
    alto = L - Ls                      # paso alto en unidades L*

    # Mascara: pixeles con rango disponible, erosionada 4 px para excluir la frontera.
    ok = L < 85.0
    m = Image.fromarray((ok * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(9))
    ok = np.asarray(m) > 127

    frac = 100.0 * ok.mean()
    # ⚠ Si queda muy poca superficie util, la cifra no es comparable: se dice, no se calla.
    val = alto[ok].std() if ok.sum() > 500 else float('nan')
    return val, L.mean(), 100.0 * (L > 95).mean(), frac

print("%-30s %7s %7s %7s %7s   %s" %
      ("captura", "HF(L*)", "L*", "clip%", "util%", "lectura"))
print("-" * 84)
ref = None
for pat in sys.argv[1:]:
    for p in sorted(glob.glob(pat)):
        if os.path.getsize(p) < 50000: continue
        e, l, c, fr = hf(p)
        n = os.path.basename(p)[:-4]
        if ref is None:
            ref = e; nota = "<- REFERENCIA (el suelo se veia bien)"
        else:
            r = e / ref
            if r >= 0.85:   nota = "textura OK (%.0f%% de la referencia)" % (100 * r)
            elif r >= 0.55: nota = "textura MERMADA (%.0f%%)" % (100 * r)
            else:           nota = "PLANCHA LISA (%.0f%%)" % (100 * r)
        print("%-30s %7.3f %7.1f %7.2f %7.1f   %s" % (n[:30], e, l, c, fr, nota))
