#!/usr/bin/env bash
# Barrido del BLOOM (2026-08-28, v2).
#
# ⚠⚠ LA v1 PRODUJO SEIS ETIQUETAS FALSAS. Dos causas, las dos de sincronizacion:
#   1. Esperaba la linea `BLOOM:`, que es IDENTICA con el bloom encendido y apagado.
#      La que sirve es `GRADE: bloom=OFF` / `GRADE: bloom=1.20`.
#   2. Encadenaba `sleep` ciegos (9+10+12=31 s) contra una agenda de 40 s: el desfase
#      se acumulaba y al tercer fondo capturaba el SIGUIENTE fondo, y la ultima captura
#      salio 7 s DESPUES de acabar la sesion.
# 🧭 Regla del proyecto que incumpli a medias: capturar POR EVENTO, NUNCA por reloj.
#    Aqui TODO espera a su linea concreta del log, y ademas se anota que se estaba
#    viendo de verdad para poder auditarlo despues.
#
# QUE MIDE: quemar altas luces es ABSOLUTO (% de pixeles al blanco + croma de la cola),
# asi que basta con el bloom ENCENDIDO —el estado de produccion— para detectarlo.
# El A/B contra bloom apagado se reserva para el fondo que salga peor.
set -u
IP="${1:-192.168.1.46}"
LISTA="${2:-}"
TIPO="${3:-bg}"          # bg | sub | light
ADB="/c/Users/Behere/AppData/Local/Android/Sdk/platform-tools/adb.exe"
cd "$(dirname "$0")/.."
OUT="$(pwd)/_bloom"; mkdir -p "$OUT"; LOG="$OUT/sender_$TIPO.log"; rm -f "$LOG"

IFS=',' read -ra IT <<< "$LISTA"
case "$TIPO" in
  bg)    MSG=change_bg;    PAT='change_bg: .*';;
  sub)   MSG=change_sub;   PAT='change_sub: .*';;
  light) MSG=change_light; PAT='change_light: .*';;
esac

# 40 s por elemento: de sobra para que el bucle vaya SIEMPRE por delante de la agenda.
ARGS=(); T=55
for x in "${IT[@]}"; do ARGS+=(--update "$MSG=$x@$T"); T=$((T+40)); done
DUR=$((T+10))
echo "== $TIPO: ${IT[*]}  ==  $DUR s =="

node Tools/cast-headless.js --stop --ip "$IP" >/dev/null 2>&1; sleep 6
node Tools/cast-headless.js --ip "$IP" --fish 8 --decos deco_anchor,deco_starfish_blue \
  --duration "$DUR" "${ARGS[@]}" > "$LOG" 2>&1 &
S=$!
T0=$(date +%s)

# Espera a que el patron aparezca por vez N. Devuelve 1 si la sesion muere antes.
esperaN(){ local p="$1" n="$2" lim=$(( $(date +%s)+$3 )) c
  while [ "$(date +%s)" -lt "$lim" ]; do
    c=$(grep -c -- "$p" "$LOG" 2>/dev/null | head -1); c=${c:-0}
    [ "$c" -ge "$n" ] && return 0
    kill -0 $S 2>/dev/null || return 1
    sleep 1
  done; return 1; }

cap(){ "$ADB" -s "$IP:5555" exec-out screencap -p > "$OUT/$1.png" 2>/dev/null
  local b; b=$(stat -c %s "$OUT/$1.png" 2>/dev/null); b=${b:-0}
  if [ "$b" -lt 50000 ]; then
    "$ADB" connect "$IP:5555" >/dev/null 2>&1; sleep 1
    "$ADB" -s "$IP:5555" exec-out screencap -p > "$OUT/$1.png" 2>/dev/null
    b=$(stat -c %s "$OUT/$1.png" 2>/dev/null); b=${b:-0}
  fi
  # ⚠ Se anota el segundo de sesion de CADA captura: si algo vuelve a desfasarse,
  #    se ve en el acta en vez de salir como un dato mas.
  # ⚠⚠ `adb exec-out screencap` puede devolver un FOTOGRAMA CONGELADO sin dar error,
  #    con la app viva y el log corriendo (visto el 28-ago: 4 capturas byte a byte iguales,
  #    y una de ellas iba a colarse como "dos suelos fundidos a dE 0.1"). El md5 lo caza.
  local m; m=$(md5sum "$OUT/$1.png" 2>/dev/null | cut -c1-32)
  if [ -n "${VISTOS:-}" ] && echo "$VISTOS" | grep -q "$m"; then
    echo "   !! FOTOGRAMA REPETIDO ($1) — la pantalla no cambio, capturа invalida"
    mv "$OUT/$1.png" "$OUT/REPETIDA_$1.png" 2>/dev/null
  fi
  VISTOS="${VISTOS:-} $m"
  echo "   $1  t=$(( $(date +%s)-T0 ))s  ${b}B" | tee -a "$OUT/acta_$TIPO.txt"
  [ "$b" -lt 50000 ] && echo "   !! CAPTURA MALA"; }

rm -f "$OUT/acta_$TIPO.txt"
esperaN "AQUARIUM READY" 1 170 || { echo "NO MONTO"; kill $S 2>/dev/null; exit 1; }
echo "acuario montado"
k=0
for x in "${IT[@]}"; do
  k=$((k+1))
  esperaN "$PAT" "$k" 70 || { echo "  ! no llego $x"; continue; }
  sleep 6                       # que asiente el cambio; el bucle va sobrado de margen
  cap "${TIPO}_${x}"
done
wait $S 2>/dev/null
echo "== fin =="
