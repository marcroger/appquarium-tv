#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Extrae las texturas embebidas de un .glb a ficheros sueltos y escribe el `mapeo.txt`
que consume `Assets/Editor/TvDecoOptimize.cs`.

POR QUE EXISTE
--------------
GLTFast decodifica las texturas embebidas del GLB a Texture2D **RGBA32 sin comprimir** y
su importador NO expone compresion. Tampoco declara `SupportsRemappedAssetType`, asi que
el remapeo estandar de Unity tampoco vale (comprobado el 2026-08-16, no supuesto). La
unica salida es sacar las texturas a assets sueltos, importarlas comprimidas (DXT1) y
montar un prefab con materiales nuevos que las referencien.

Este script hace la mitad "de fuera de Unity": parsear el glTF y volcar las imagenes.
La otra mitad (import comprimido, materiales, prefab, reapuntar el SO) la hace
`TvDecoOptimize.cs` desde el Editor.

MEDIDO: `greek_underwater_broken_statue_2` paso de 9,89 MB a 2,05 MB de bundle (-79 %).

SALIDA (en Assets/Content/Decos/<nombre_glb>/)
    tex_0.jpg, tex_1.jpg, ...   una por imagen usada como baseColor
    mapeo.txt                   lineas `<nombre_material>=<indice_de_textura>`

USO
    python Tools/extract_glb_textures.py Assets/ThirdParty/Corals/acropora_valenciennesi.glb
    python Tools/extract_glb_textures.py --todas          # las 21 GLB de ThirdParty
    python Tools/extract_glb_textures.py --todas --dry-run

NOTA: no sobreescribe una carpeta que ya tenga `mapeo.txt` salvo que se pase `--forzar`.
Asi una re-ejecucion no pisa un lote ya optimizado y validado.
"""

import argparse
import json
import os
import struct
import sys

EXT_POR_MIME = {
    'image/jpeg': '.jpg',
    'image/png': '.png',
}

DESTINO_BASE = 'Assets/Content/Decos'


def leer_glb(ruta):
    """Devuelve (json_dict, bin_bytes) de un glTF binario 2.0."""
    with open(ruta, 'rb') as f:
        datos = f.read()

    magic, version, _total = struct.unpack_from('<III', datos, 0)
    if magic != 0x46546C67:  # 'glTF'
        raise ValueError('%s no es un GLB (magic 0x%08X)' % (ruta, magic))
    if version != 2:
        raise ValueError('%s es glTF v%d; este script solo entiende v2' % (ruta, version))

    gltf = None
    binario = b''
    offset = 12
    while offset < len(datos):
        largo, tipo = struct.unpack_from('<II', datos, offset)
        cuerpo = datos[offset + 8: offset + 8 + largo]
        if tipo == 0x4E4F534A:      # 'JSON'
            gltf = json.loads(cuerpo.decode('utf-8'))
        elif tipo == 0x004E4942:    # 'BIN\0'
            binario = cuerpo
        offset += 8 + largo + ((4 - largo % 4) % 4)

    if gltf is None:
        raise ValueError('%s no tiene chunk JSON' % ruta)
    return gltf, binario


def bytes_de_imagen(gltf, binario, idx_img):
    """Bytes crudos + extension de la imagen `idx_img`, este embebida o en bufferView."""
    img = gltf['images'][idx_img]

    if 'bufferView' in img:
        bv = gltf['bufferViews'][img['bufferView']]
        ini = bv.get('byteOffset', 0)
        fin = ini + bv['byteLength']
        crudo = binario[ini:fin]
        ext = EXT_POR_MIME.get(img.get('mimeType', ''))
        if ext is None:
            # sin mimeType fiable: deducir por la cabecera
            ext = '.png' if crudo[:8] == b'\x89PNG\r\n\x1a\n' else '.jpg'
        return crudo, ext

    if 'uri' in img and not img['uri'].startswith('data:'):
        # textura externa junto al .glb
        return None, os.path.splitext(img['uri'])[1]

    raise ValueError('imagen %d sin bufferView ni uri utilizable' % idx_img)


def imagen_de_material(gltf, mat):
    """Indice de imagen del baseColorTexture de un material, o None."""
    pbr = mat.get('pbrMetallicRoughness', {})
    tex = pbr.get('baseColorTexture')
    if tex is None:
        return None
    fuente = gltf['textures'][tex['index']].get('source')
    return fuente


def procesar(ruta_glb, forzar=False, dry_run=False):
    nombre = os.path.splitext(os.path.basename(ruta_glb))[0].rstrip('.')
    destino = os.path.join(DESTINO_BASE, nombre).replace(os.sep, '/')

    gltf, binario = leer_glb(ruta_glb)
    materiales = gltf.get('materials') or []
    if not materiales:
        print('  ! %s no declara materiales -- nada que mapear' % nombre)
        return False

    # Cada imagen usada se vuelca UNA vez; varios materiales pueden compartirla.
    orden_imgs = []      # indice glTF de imagen, en orden de aparicion
    mapeo = []           # (nombre_material, indice_en_orden_imgs)
    sin_textura = []

    for i, mat in enumerate(materiales):
        nombre_mat = mat.get('name', 'material_%d' % i)
        idx_img = imagen_de_material(gltf, mat)
        if idx_img is None:
            sin_textura.append(nombre_mat)
            continue
        if idx_img not in orden_imgs:
            orden_imgs.append(idx_img)
        mapeo.append((nombre_mat, orden_imgs.index(idx_img)))

    if not mapeo:
        print('  ! %s: ningun material con baseColorTexture' % nombre)
        return False

    ya_estaba = os.path.exists(os.path.join(destino, 'mapeo.txt'))
    if ya_estaba and not forzar and not dry_run:
        print('  = %s ya extraido (mapeo.txt existe) -- usa --forzar para rehacerlo' % nombre)
        return False

    print('  > %s: %d materiales, %d imagenes -> %s'
          % (nombre, len(mapeo), len(orden_imgs), destino))
    for nm, idx in mapeo:
        print('      %s = tex_%d' % (nm, idx))
    if sin_textura:
        # TvDecoOptimize los deja intactos, y un material intacto arrastra la textura
        # RGBA32 del GLB al bundle: es justo lo que estamos intentando evitar.
        print('      AVISO: sin baseColorTexture (quedaran sin optimizar): %s'
              % ', '.join(sin_textura))

    if dry_run:
        if ya_estaba and not forzar:
            print('      (ya extraido: en real se saltaria salvo --forzar)')
        return True

    os.makedirs(destino, exist_ok=True)
    for pos, idx_img in enumerate(orden_imgs):
        crudo, ext = bytes_de_imagen(gltf, binario, idx_img)
        if crudo is None:
            print('      ! imagen %d es externa (uri), copiala a mano' % idx_img)
            continue
        salida = os.path.join(destino, 'tex_%d%s' % (pos, ext))
        with open(salida, 'wb') as f:
            f.write(crudo)
        print('      escrito %s (%.2f MB)' % (salida, len(crudo) / 1e6))

    with open(os.path.join(destino, 'mapeo.txt'), 'w', encoding='utf-8') as f:
        for nm, idx in mapeo:
            f.write('%s=%d\n' % (nm, idx))
    print('      escrito %s/mapeo.txt' % destino)
    return True


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('glb', nargs='*', help='rutas .glb a procesar')
    ap.add_argument('--todas', action='store_true',
                    help='procesar todas las .glb de Assets/ThirdParty')
    ap.add_argument('--forzar', action='store_true',
                    help='rehacer aunque la carpeta destino ya tenga mapeo.txt')
    ap.add_argument('--dry-run', action='store_true',
                    help='solo listar lo que haria, sin escribir nada')
    args = ap.parse_args()

    rutas = list(args.glb)
    if args.todas:
        for raiz, _, ficheros in os.walk('Assets/ThirdParty'):
            for f in ficheros:
                if f.lower().endswith('.glb'):
                    rutas.append(os.path.join(raiz, f).replace(os.sep, '/'))
    if not rutas:
        ap.error('nada que hacer: pasa rutas .glb o --todas')

    hechas = 0
    for r in sorted(set(rutas)):
        try:
            if procesar(r, forzar=args.forzar, dry_run=args.dry_run):
                hechas += 1
        except Exception as e:
            print('  ! %s: %s' % (r, e))
    print('\n%d/%d %s' % (hechas, len(set(rutas)),
                          'listas (dry-run)' if args.dry_run else 'extraidas'))


if __name__ == '__main__':
    sys.exit(main())
