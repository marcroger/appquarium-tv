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



# ── bundles LOCALES (StreamingAssets/aa/WebGL/) ──────────────────────────────────
# Punto ciego historico de este script, y costo 6 decos rotas en produccion el
# 2026-08-18: los bundles del grupo `Shared_Local` viven FUERA de `bundles/`, se
# sirven por HTTP desde ahi, y cambian de hash en casi cada build. Como no estaban
# en el prefijo que miraba el script, ni se detectaban cuando faltaban ni se
# limpiaban cuando sobraban.
PREFIJO_LOCAL = 'StreamingAssets/aa/WebGL/'


def revisar_locales(cl, cat_bytes):
    """Compara los bundles locales de R2 contra los que referencia el catalogo servido.

    Devuelve la lista de objetos huerfanos. Aqui NO se filtra por hash de 32 chars como
    en `bundles/`: el nombre lleva el hash pero tambien un prefijo de build, asi que se
    busca la aparicion literal del nombre dentro del catalogo.
    """
    txt = cat_bytes.decode('utf-8', 'ignore')
    objetos, token = [], None
    while True:
        kw = {'Bucket': BUCKET, 'Prefix': PREFIJO_LOCAL}
        if token:
            kw['ContinuationToken'] = token
        r = cl.list_objects_v2(**kw)
        objetos.extend(r.get('Contents', []))
        if not r.get('IsTruncated'):
            break
        token = r.get('NextContinuationToken')

    bundles = [o for o in objetos if o['Key'].endswith('.bundle')]
    vivos, huerfanos = [], []
    for o in bundles:
        n = os.path.basename(o['Key'])
        # el catalogo puede citarlo sin el prefijo de build
        cita = n in txt or any(n.endswith(x) for x in re.findall(
            r'[0-9a-z_]*(?:shared_local|unitybuiltinassets|monoscripts)[0-9a-z_]*_[0-9a-f]{32}\.bundle', txt))
        (vivos if cita else huerfanos).append(o)

    print('\nen R2 %s: %d bundles locales' % (PREFIJO_LOCAL, len(bundles)))
    print('  vivos      %3d  %8.1f MB' % (len(vivos), sum(o['Size'] for o in vivos) / 1e6))
    print('  HUERFANOS  %3d  %8.1f MB' % (len(huerfanos), sum(o['Size'] for o in huerfanos) / 1e6))
    for o in sorted(huerfanos, key=lambda x: -x['Size']):
        print('     %6.2f MB  %s' % (o['Size'] / 1e6, os.path.basename(o['Key'])))

    # ⚠ Si el catalogo referencia un local que NO esta en R2, el device falla al cargar
    # las decos que dependan de el con "Dependency Exception". Avisar es lo importante.
    faltan = [n for n in set(re.findall(
        r'[0-9a-z_]*(?:shared_local|unitybuiltinassets|monoscripts)[0-9a-z_]*_[0-9a-f]{32}\.bundle', txt))
        if not any(os.path.basename(o['Key']).endswith(n) for o in bundles)]
    if faltan:
        print('\n  ❌ EL CATALOGO PIDE LOCALES QUE NO ESTAN EN R2 -> las decos que dependan')
        print('     de ellos fallaran con Dependency Exception. Subelos desde')
        print('     Library/com.unity.addressables/aa/WebGL/WebGL/ a ' + PREFIJO_LOCAL)
        for n in faltan:
            print('       ' + n)
    return huerfanos


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

    # Los bundles LOCALES van por su cuenta: otro prefijo y otro criterio.
    cuerpo_cat = cl.get_object(Bucket=BUCKET, Key=cats_r2[0])['Body'].read()
    huerfanos_locales = revisar_locales(cl, cuerpo_cat)

    if not huerfanos and not huerfanos_locales:
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

    todos = huerfanos + huerfanos_locales
    print()
    resp = input('Borrar %d objetos (%.1f MB) de R2? escribe SI: '
                 % (len(todos), mb(todos)))
    if resp.strip() != 'SI':
        print('cancelado.')
        return

    for i in range(0, len(todos), 1000):
        lote = todos[i:i + 1000]
        cl.delete_objects(Bucket=BUCKET,
                          Delete={'Objects': [{'Key': o['Key']} for o in lote]})
        print('borrados %d/%d' % (min(i + 1000, len(todos)), len(todos)))
    print('listo: %.1f MB liberados (%d de bundles/, %d locales).'
          % (mb(todos), len(huerfanos), len(huerfanos_locales)))


if __name__ == '__main__':
    main()
