#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Lista (y opcionalmente borra) los bundles HUERFANOS de R2: los que estan en el bucket pero
NO aparecen en el catalogo que se acaba de desplegar. Son restos de builds viejos que nadie
pide nunca.

⚠ POR QUE EXISTE ESTE SCRIPT Y NO UN `aws s3 rm --recursive`
El deploy usa `sync` SIN `--delete` a proposito (con `--delete` se borran bundles vivos, y en la
raiz del bucket ademas se lleva `keepalive_black.mp4`, que es lo que mantiene viva la sesion
Cast). El precio de esa prudencia es que los bundles viejos se acumulan. La limpieza correcta es
selectiva: borrar SOLO lo que no esta en el catalogo vivo.

⚠ SEGURIDAD — el catalogo de referencia se baja DE R2, no del disco
La referencia es el catalogo que esta REALMENTE SIRVIENDO (`bundles/catalog_*.bin` en R2), no el
de `ServerData/WebGL/`. Aprendido a base de casi meterla el 2026-08-17: se leyo el catalogo local
mientras un build lo estaba reescribiendo, y el informe dio 96 huerfanos / 415 MB cuando en
realidad eran bundles vivos con hash nuevo todavia sin desplegar. Con el catalogo de R2 eso no
puede pasar: lo vivo es, por definicion, lo que el catalogo servido referencia.

Aun asi, el orden correcto sigue siendo:
  1. desplegar los bundles + catalogo nuevos,
  2. comprobar en la tele que el acuario carga,
  3. y solo entonces pasar `--borrar`.

Por defecto NO borra nada: imprime el informe y sale.

USO
    python Tools/r2_huerfanos.py                 # informe
    python Tools/r2_huerfanos.py --borrar        # borra, pidiendo confirmacion
"""

import argparse
import configparser
import glob
import os
import re
import sys

BUCKET = 'appquarium-tv'
ENDPOINT = 'https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com'
PREFIJO = 'bundles/'


def cliente():
    import boto3
    c = configparser.ConfigParser()
    c.read([os.path.expanduser('~/.aws/credentials')])
    return boto3.client(
        's3', endpoint_url=ENDPOINT,
        aws_access_key_id=c.get('r2', 'aws_access_key_id'),
        aws_secret_access_key=c.get('r2', 'aws_secret_access_key'),
        region_name='auto')


def hashes_vivos(cl, claves_catalogo):
    """
    Hashes de 32 hex referenciados por el/los catalogo(s) que R2 esta sirviendo AHORA.
    Se baja de R2 a proposito: es la unica referencia que no puede estar desfasada.
    """
    if not claves_catalogo:
        sys.exit('ERROR: no hay ningun catalog_*.bin en R2 — nada con lo que comparar.')
    vivos = set()
    for k in claves_catalogo:
        cuerpo = cl.get_object(Bucket=BUCKET, Key=k)['Body'].read()
        vivos |= set(re.findall(r'[a-f0-9]{32}', cuerpo.decode('latin-1')))
    return vivos


def hashes_locales():
    """Idem pero del disco — solo para avisar si local y R2 divergen."""
    vivos = set()
    for c in sorted(glob.glob('ServerData/WebGL/catalog_*.bin')):
        vivos |= set(re.findall(r'[a-f0-9]{32}', open(c, 'rb').read().decode('latin-1')))
    return vivos


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--borrar', action='store_true', help='borrar de verdad (pide confirmacion)')
    args = ap.parse_args()

    cl = cliente()
    objetos = []
    token = None
    while True:
        kw = {'Bucket': BUCKET, 'Prefix': PREFIJO}
        if token:
            kw['ContinuationToken'] = token
        r = cl.list_objects_v2(**kw)
        objetos.extend(r.get('Contents', []))
        if not r.get('IsTruncated'):
            break
        token = r.get('NextContinuationToken')

    cats_r2 = sorted(o['Key'] for o in objetos if re.search(r'catalog_.*\.bin$', o['Key']))
    vivos = hashes_vivos(cl, cats_r2)
    print('catalogo(s) EN R2: %s -> %d hashes vivos'
          % (', '.join(os.path.basename(k) for k in cats_r2), len(vivos)))

    locales = hashes_locales()
    if locales and locales != vivos:
        solo_local = len(locales - vivos)
        print('⚠ el catalogo LOCAL difiere del de R2 (%d hashes que R2 no sirve): hay un build sin'
              % solo_local)
        print('  desplegar. Se usa el de R2, que es lo correcto — pero no borres hasta desplegar')
        print('  y comprobar la tele, o borrarias bundles que el catalogo nuevo si va a pedir.')

    bundles = [o for o in objetos if o['Key'].endswith('.bundle')]
    huerfanos, vivas = [], []
    for o in bundles:
        m = re.search(r'([a-f0-9]{32})', os.path.basename(o['Key']))
        (vivas if (m and m.group(1) in vivos) else huerfanos).append(o)

    def mb(lista):
        return sum(o['Size'] for o in lista) / 1e6

    print('\nen R2 %s: %d bundles, %.1f MB' % (PREFIJO, len(bundles), mb(bundles)))
    print('  vivos      %3d  %8.1f MB' % (len(vivas), mb(vivas)))
    print('  HUERFANOS  %3d  %8.1f MB' % (len(huerfanos), mb(huerfanos)))

    if not huerfanos:
        print('\nnada que limpiar.')
        return

    print('\n--- 15 huerfanos mas grandes ---')
    for o in sorted(huerfanos, key=lambda x: -x['Size'])[:15]:
        print('  %8.2f MB  %s' % (o['Size'] / 1e6, o['Key']))

    # Red de seguridad: si "huerfano" sale mas de la mitad del bucket, algo huele mal
    # (catalogo local desfasado respecto a R2). Mejor parar que borrar lo vivo.
    if len(huerfanos) > len(bundles) * 0.6:
        print('\n⚠ MAS DEL 60 %% DEL BUCKET SALE HUERFANO. Eso apunta a que el catalogo local')
        print('  no es el que esta desplegado. NO se borra nada. Revisa antes de insistir.')
        return

    if not args.borrar:
        print('\n(informe solo — pasa --borrar para eliminarlos)')
        return

    resp = input('\nBorrar %d objetos (%.1f MB) de R2? escribe SI: ' % (len(huerfanos), mb(huerfanos)))
    if resp.strip() != 'SI':
        print('cancelado.')
        return

    for i in range(0, len(huerfanos), 1000):
        lote = huerfanos[i:i + 1000]
        cl.delete_objects(Bucket=BUCKET,
                          Delete={'Objects': [{'Key': o['Key']} for o in lote]})
        print('borrados %d/%d' % (min(i + 1000, len(huerfanos)), len(huerfanos)))
    print('listo: %.1f MB liberados.' % mb(huerfanos))


if __name__ == '__main__':
    main()
