#!/usr/bin/env python3
"""
Hoja de contactos + numeros del barrido de grado (`Appquarium TV -> Barrido de grado`).

Para que: elegir el grado de color de la TV comparando variantes, y contra la captura del
movil si esta disponible. Ver CAST_PARIDAD_VISUAL.md.

Uso:
    python Tools/grade_contact_sheet.py                 # lee _gradesweep/
    python Tools/grade_contact_sheet.py --dir otra/ruta

Si existe `<dir>/ref_movil.png` (captura del telefono) se pone la PRIMERA y se marca como
referencia: el objetivo es acercarse a sus numeros, no a un ideal abstracto.

Regla que este script aplica a proposito (y que ya costo un diagnostico falso con la
bioluminiscencia el 16-ago): se miden valores ABSOLUTOS por region, nunca el contraste de una
region contra otra que tambien cambia entre variantes.

Las capturas del movil y de la TV tienen resoluciones distintas. Por eso las metricas se
calculan sobre bandas RELATIVAS (porcentajes de alto), no sobre pixeles absolutos.

Sin tildes ni flechas en los `print`: la consola de Windows es cp1252 y revienta con ellas.
"""

import argparse
import os
import sys

try:
    import numpy as np
    from PIL import Image, ImageDraw
except ImportError:
    sys.exit("Faltan dependencias: pip install pillow numpy")


# Bandas relativas de la imagen. El fondo ocupa todo el frame, pero la parte de ARRIBA es
# donde se ve casi limpio (el suelo ocupa el 20 % inferior del tanque, ver DecorationPlacer).
BANDAS = {
    "arriba (fondo)": (0.00, 0.35),
    "centro (agua)":  (0.35, 0.75),
    "abajo (suelo)":  (0.75, 1.00),
}

REF = "ref_movil.png"


def metricas(img):
    """Luminancia y saturacion medias (0-255 y 0-1) por banda y del frame entero."""
    rgb = np.asarray(img.convert("RGB"), dtype=np.float32)
    alto = rgb.shape[0]

    def de(region):
        r, g, b = region[..., 0], region[..., 1], region[..., 2]
        # Rec. 709: es la que usa el resto de mediciones del proyecto.
        lum = 0.2126 * r + 0.7152 * g + 0.0722 * b
        mx, mn = region.max(axis=-1), region.min(axis=-1)
        sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0.0)
        return float(lum.mean()), float(sat.mean())

    out = {"frame": de(rgb)}
    for nombre, (y0, y1) in BANDAS.items():
        out[nombre] = de(rgb[int(alto * y0):int(alto * y1)])
    return out


def etiqueta(img, texto, es_ref=False):
    """Franja con el nombre encima de la miniatura."""
    ancho, alto = img.size
    barra = 34
    lienzo = Image.new("RGB", (ancho, alto + barra), (70, 40, 10) if es_ref else (24, 24, 28))
    lienzo.paste(img, (0, barra))
    ImageDraw.Draw(lienzo).text((8, 9), texto, fill=(255, 255, 255))
    return lienzo


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir", default="_gradesweep", help="carpeta con las PNG del barrido")
    ap.add_argument("--ancho", type=int, default=520, help="ancho de cada miniatura")
    ap.add_argument("--cols", type=int, default=3)
    args = ap.parse_args()

    if not os.path.isdir(args.dir):
        sys.exit(f"No existe {args.dir}. Has ejecutado el barrido en el Editor?")

    pngs = sorted(f for f in os.listdir(args.dir)
                  if f.lower().endswith(".png") and f != "contact_sheet.png")
    if REF in pngs:
        pngs.remove(REF)
        pngs.insert(0, REF)

    if not pngs:
        sys.exit(f"{args.dir} no tiene PNG. El barrido llego a capturar algo?")

    print(f"{len(pngs)} imagenes en {args.dir}\n")
    cab = (f"{'variante':<24} {'lum frame':>10} {'sat frame':>10} "
           f"{'lum arriba':>11} {'sat arriba':>11} {'lum abajo':>10}")
    print(cab)
    print("-" * len(cab))

    minis, base, medidas = [], None, []
    for nombre in pngs:
        img = Image.open(os.path.join(args.dir, nombre))
        m = metricas(img)
        etq = os.path.splitext(nombre)[0]
        es_ref = nombre == REF

        print(f"{etq:<24} {m['frame'][0]:>10.1f} {m['frame'][1]:>10.3f} "
              f"{m['arriba (fondo)'][0]:>11.1f} {m['arriba (fondo)'][1]:>11.3f} "
              f"{m['abajo (suelo)'][0]:>10.1f}"
              f"{'  <- REFERENCIA (movil)' if es_ref else ''}")

        medidas.append((nombre, m))
        if es_ref:
            base = m
        elif base is not None:
            d_lum = m["arriba (fondo)"][0] - base["arriba (fondo)"][0]
            d_sat = m["arriba (fondo)"][1] - base["arriba (fondo)"][1]
            print(f"{'':<24} {'delta vs movil (fondo):':>32} lum {d_lum:+.1f} / sat {d_sat:+.3f}")

        alto = int(img.height * args.ancho / img.width)
        minis.append(etiqueta(img.resize((args.ancho, alto), Image.LANCZOS), etq, es_ref))

    cols = min(args.cols, len(minis))
    filas = (len(minis) + cols - 1) // cols
    cw, ch = minis[0].size
    hoja = Image.new("RGB", (cols * cw, filas * ch), (16, 16, 18))
    for i, mini in enumerate(minis):
        hoja.paste(mini, ((i % cols) * cw, (i // cols) * ch))

    salida = os.path.join(args.dir, "contact_sheet.png")
    hoja.save(salida)
    print(f"\nHoja de contactos -> {salida}  ({hoja.width}x{hoja.height})")

    # Guarda de "esto no mide nada". Paso el 21-ago: cam.Render() no ejecuta el pipeline
    # completo en URP, no aplica post-proceso, y el barrido devolvio 8 PNG con la MISMA
    # luminancia y saturacion. Todo parecia correcto: 8 ficheros y exit 0.
    variantes = [m for n, m in medidas if n != REF]
    if len(variantes) >= 2:
        r_lum = max(v["frame"][0] for v in variantes) - min(v["frame"][0] for v in variantes)
        r_sat = max(v["frame"][1] for v in variantes) - min(v["frame"][1] for v in variantes)
        if r_lum < 1.0 and r_sat < 0.01:
            print(f"\n*** AVISO: las variantes son IDENTICAS (rango lum {r_lum:.3f}, "
                  f"sat {r_sat:.4f}).")
            print("*** El post-proceso NO se aplico a las capturas: estas PNG no valen. ***")
        else:
            print(f"\nEl barrido si mide: lum varia {r_lum:.1f} y sat {r_sat:.3f} entre variantes.")

    if base is None:
        print(f"\n[!] No hay {REF}: se comparan las variantes entre si, pero NO contra la app.")
        print("    Deja la captura del telefono como", os.path.join(args.dir, REF))


if __name__ == "__main__":
    main()
