#!/usr/bin/env bash
# VUELTA A PRODUCCIÓN — restaura el acuario funcionando en la TV.
#
# Sube webgl-output/index.html (receiver procesado, sello 'rcv 2026-07-17', apunta a
# Build/webgl-output.*) sobre R2 /index.html.
#
# El PLAYER de producción (/Build/webgl-output.{loader.js,data,framework.js,wasm},
# build 06-23b con peces + sombras + ageScale) NUNCA se ha tocado durante la
# investigación del disconnect — sigue en R2 intacto y verificado por ETag.
# Por eso restaurar solo el index.html basta para volver al acuario.
#
# ⚠ NO ejecutar mientras se estén haciendo tests de la bisección: pisa el receiver
#   de diagnóstico. Ver CAST_NEXT_SESSION_2026-07-22.md.
#
# Uso:  bash Tools/restore-production-receiver.sh

set -euo pipefail
cd "$(dirname "$0")/.."

export AWS_REQUEST_CHECKSUM_CALCULATION="when_required"
export AWS_RESPONSE_CHECKSUM_VALIDATION="when_supported"

SRC="webgl-output/index.html"
[ -f "$SRC" ] || { echo "FALLO: no existe $SRC"; exit 1; }

# Guardas: que sea el receiver bueno y no un template ni un diagnóstico.
grep -q '{{{' "$SRC" && { echo "FALLO: $SRC tiene placeholders sin procesar ({{{ ... }}}). Es el TEMPLATE, no el build. Abortando."; exit 1; }
grep -q "Build/webgl-output.loader.js" "$SRC" || { echo "FALLO: $SRC no apunta al player de producción. Abortando."; exit 1; }
grep -q "RUNG_CONFIG" "$SRC" && { echo "AVISO: $SRC contiene el harness de rungs (receiver de diagnóstico)."; }

python - <<'PY'
import boto3, configparser, os, hashlib
c = configparser.ConfigParser(); c.read([os.path.expanduser('~/.aws/credentials')])
cl = boto3.client('s3', endpoint_url='https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com',
    aws_access_key_id=c.get('r2','aws_access_key_id'),
    aws_secret_access_key=c.get('r2','aws_secret_access_key'), region_name='auto')

# 0) backup de lo que haya vivo ahora, por si acaso
try:
    cur = cl.get_object(Bucket='appquarium-tv', Key='index.html')['Body'].read()
    os.makedirs('scratchpad', exist_ok=True)
    open('scratchpad/r2-index-antes-de-restaurar.html','wb').write(cur)
    print('backup del index.html vivo ->', len(cur), 'B en scratchpad/r2-index-antes-de-restaurar.html')
except Exception as e:
    print('aviso: no se pudo respaldar el index.html vivo:', e)

# 1) subir el receiver de producción
b = open('webgl-output/index.html','rb').read()
cl.put_object(Bucket='appquarium-tv', Key='index.html', Body=b,
              ContentType='text/html', CacheControl='public, max-age=60')
v = cl.get_object(Bucket='appquarium-tv', Key='index.html')['Body'].read()
ok = hashlib.md5(v).hexdigest() == hashlib.md5(b).hexdigest()
print('index.html restaurado:', len(b), 'B  ->', 'VERIFICADO IDENTICO' if ok else 'DIFIERE!!')

# 2) comprobar que el player de produccion sigue ahi
for k in ['Build/webgl-output.loader.js','Build/webgl-output.data',
          'Build/webgl-output.framework.js','Build/webgl-output.wasm']:
    h = cl.head_object(Bucket='appquarium-tv', Key=k)
    print('  OK', k, h['ContentLength'], 'B', h['LastModified'])
PY

echo
echo "LISTO. Reinicia el Xiaomi para bustear cache y castea: debe salir el ACUARIO con peces."
echo "Si sale el cubo azul o un panel de debug -> cache vieja, reinicia otra vez."
