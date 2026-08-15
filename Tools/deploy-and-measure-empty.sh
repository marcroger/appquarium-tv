#!/usr/bin/env bash
# Despliega el build del rig VACÍO a R2 y lo mide de punta a punta.
#
#   bash Tools/deploy-and-measure-empty.sh <etiqueta>
#
# 1. compara tamaños contra la línea base del 2026-07-27
# 2. sube Build/webgl-output-empty.* + el receiver de test a R2 /index.html
# 3. lanza el ciclo completo (reinicio → asentamiento → cast → análisis)
#
# ⚠ NO toca el player de producción (Build/webgl-output.*): nombres distintos.
set -u
LABEL="${1:-slim}"
BASE_WASM=44249290
BASE_DATA=20814692
SRC="webgl-output-empty/Build"

[ -f "$SRC/webgl-output-empty.wasm" ] || { echo "FALTA $SRC/webgl-output-empty.wasm"; exit 1; }

W=$(stat -c%s "$SRC/webgl-output-empty.wasm")
D=$(stat -c%s "$SRC/webgl-output-empty.data")
echo "══════════ TAMAÑOS · $LABEL ══════════"
awk -v w="$W" -v d="$D" -v bw="$BASE_WASM" -v bd="$BASE_DATA" 'BEGIN{
  printf "  .wasm  %d → %d   (%+.1f MB · queda el %.0f%%)\n", bw, w, (w-bw)/1048576, w*100/bw;
  printf "  .data  %d → %d   (%+.1f MB · queda el %.0f%%)\n", bd, d, (d-bd)/1048576, d*100/bd;
  printf "  total  %.1f MB → %.1f MB\n", (bw+bd)/1048576, (w+d)/1048576;
}'
echo

export AWS_REQUEST_CHECKSUM_CALCULATION="when_required"
export AWS_RESPONSE_CHECKSUM_VALIDATION="when_supported"
python -c "
import boto3, configparser, os
c = configparser.ConfigParser(); c.read([os.path.expanduser('~/.aws/credentials')])
cl = boto3.client('s3', endpoint_url='https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com',
    aws_access_key_id=c.get('r2','aws_access_key_id'), aws_secret_access_key=c.get('r2','aws_secret_access_key'), region_name='auto')
B='$SRC/'
for f,ct in [('webgl-output-empty.loader.js','application/javascript'),
             ('webgl-output-empty.framework.js','application/javascript'),
             ('webgl-output-empty.data','application/octet-stream'),
             ('webgl-output-empty.wasm','application/wasm')]:
    body=open(B+f,'rb').read()
    cl.put_object(Bucket='appquarium-tv', Key='Build/'+f, Body=body, ContentType=ct, CacheControl='public, max-age=60')
    print('  subido', f, len(body))
# ⚠ 2026-08-15 — copia de seguridad ANTES de pisar el receiver de produccion.
# Este script sube el rig de diagnostico sobre /index.html, que es LA TELE DE VERDAD, y
# no hacia backup ni pedia confirmacion: si la tanda se abortaba a media, la produccion se
# quedaba con el rig vacio hasta que alguien lo notara. (restore-production-receiver.sh si
# respalda; este no.) El backup queda con marca de tiempo para poder volver siempre.
import time
try:
    prod = cl.get_object(Bucket='appquarium-tv', Key='index.html')['Body'].read()
    clave = 'backup/index.html.%s' % time.strftime('%Y%m%d-%H%M%S')
    cl.put_object(Bucket='appquarium-tv', Key=clave, Body=prod, ContentType='text/html')
    print('  backup del receiver de produccion ->', clave, len(prod), 'B')
except Exception as e:
    raise SystemExit('ABORTA: no he podido respaldar el index.html de produccion: %s' % e)

cl.put_object(Bucket='appquarium-tv', Key='index.html', Body=open('Tools/rcv-empty-test.html','rb').read(),
    ContentType='text/html', CacheControl='public, max-age=15')
print('  receiver: rcv-empty-test (EMPTY-mem)')
" || exit 1

echo; echo "verificando que producción sigue intacta…"
curl -sI https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev/Build/webgl-output.wasm \
  | tr -d '\r' | grep -i content-length | sed 's/^/  prod /'

echo; echo "══ lanzando ciclo de medición ══"
bash Tools/cast-run.sh 2 "$LABEL"
