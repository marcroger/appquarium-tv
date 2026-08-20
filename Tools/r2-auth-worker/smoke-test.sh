#!/usr/bin/env bash
# Matriz de comprobacion del Worker. Ejecutar ANTES de tocar nada de Unity.
#   ./smoke-test.sh https://appquarium-assets.<sub>.workers.dev <TOKEN> <fichero.bundle>
set -u
WORKER="${1:?falta la URL del worker}"; TOKEN="${2:?falta el token}"; BUNDLE="${3:?falta el nombre de bundle}"
ORIGIN="https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev"
LOCAL="../../ServerData/WebGL/$BUNDLE"
pass=0; fail=0
chk() { # chk <descripcion> <esperado> <obtenido>
  if [ "$2" = "$3" ]; then echo "  OK   $1 ($3)"; pass=$((pass+1));
  else echo "  FALLO $1 -> esperado $2, obtenido $3"; fail=$((fail+1)); fi
}
code() { curl -s -o /dev/null -w '%{http_code}' "$@"; }

echo "== Worker: $WORKER"
chk "/health responde"                200 "$(code "$WORKER/health")"
chk "sin token -> 401"                401 "$(code "$WORKER/bundle/$BUNDLE")"
chk "token invalido -> 403"           403 "$(code -H 'Authorization: Bearer no-soy-el-token' "$WORKER/bundle/$BUNDLE")"
chk "token valido -> 200"             200 "$(code -H "Authorization: Bearer $TOKEN" "$WORKER/bundle/$BUNDLE")"
chk "bundle inexistente -> 404"       404 "$(code -H "Authorization: Bearer $TOKEN" "$WORKER/bundle/no_existe_1234.bundle")"
chk "path traversal -> 404"           404 "$(code -H "Authorization: Bearer $TOKEN" "$WORKER/bundle/..%2findex.html")"
chk "POST -> 405"                     405 "$(code -X POST -H "Authorization: Bearer $TOKEN" "$WORKER/bundle/$BUNDLE")"
chk "preflight OPTIONS -> 204"        204 "$(code -X OPTIONS -H "Origin: $ORIGIN" -H 'Access-Control-Request-Method: GET' -H 'Access-Control-Request-Headers: authorization' "$WORKER/bundle/$BUNDLE")"

hdrs=$(curl -s -D - -o /dev/null -X OPTIONS -H "Origin: $ORIGIN" "$WORKER/bundle/$BUNDLE")
echo "$hdrs" | grep -qi 'access-control-allow-headers:.*[Aa]uthorization' \
  && { echo "  OK   preflight permite el header Authorization"; pass=$((pass+1)); } \
  || { echo "  FALLO preflight NO permite Authorization -> el receiver no podra descargar"; fail=$((fail+1)); }
echo "$hdrs" | grep -qi "access-control-allow-origin: $ORIGIN" \
  && { echo "  OK   preflight devuelve el origen del receiver"; pass=$((pass+1)); } \
  || { echo "  FALLO preflight sin Access-Control-Allow-Origin"; fail=$((fail+1)); }

ghdrs=$(curl -s -D - -o /tmp/aq-bundle.bin -H "Authorization: Bearer $TOKEN" -H "Origin: $ORIGIN" "$WORKER/bundle/$BUNDLE")
echo "$ghdrs" | grep -qi 'cache-control:.*max-age=604800' \
  && { echo "  OK   Cache-Control conservado (604800)"; pass=$((pass+1)); } \
  || { echo "  FALLO Cache-Control perdido -> el device se re-baja todo cada sesion"; fail=$((fail+1)); }

if [ -f "$LOCAL" ]; then
  a=$(md5sum "$LOCAL" | cut -d' ' -f1); b=$(md5sum /tmp/aq-bundle.bin | cut -d' ' -f1)
  chk "los bytes son identicos al bundle local" "$a" "$b"
else
  echo "  AVISO no encuentro $LOCAL -> no se comparan los bytes"
fi
rm -f /tmp/aq-bundle.bin
echo; echo "== $pass OK, $fail fallos"; [ "$fail" -eq 0 ]
