#!/usr/bin/env bash
# ¿Se DISTINGUEN los 12 sustratos en la tele? (2026-08-28)
#
# Criterio del user: fondos, suelos y luces deben ser diferenciales como en el movil.
# Caso critico: sub_sand y sub_white estan a dE 2.0 YA EN EL ARTE. Si algo comprime el
# rango, se funden. Con el suelo de produccion al 53 % de blanco no tiene sentido medirlo,
# asi que se barre CON LA VARIANTE CANDIDATA puesta desde el principio.
set -u
IP="${1:-192.168.1.46}"
GRADE="${2:-{\"bloom\":true,\"tonemapping\":true,\"bloomIntensity\":0.30\}}"
ETIQ="${3:-cand}"
ADB="/c/Users/Behere/AppData/Local/Android/Sdk/platform-tools/adb.exe"
cd "$(dirname "$0")/.."
OUT="$(pwd)/_bloom"; LOG="$OUT/sender_sub_$ETIQ.log"
rm -f "$LOG" "$OUT/acta_sub_$ETIQ.txt" "$OUT"/sub${ETIQ}_*.png

SUBS=(sub_lava sub_moss sub_mud sub_volcanic
      )
ARGS=(); T=75
for s in "${SUBS[@]}"; do ARGS+=(--update "change_sub=$s@$T"); T=$((T+35)); done

node Tools/cast-headless.js --stop --ip "$IP" >/dev/null 2>&1; sleep 6
node Tools/cast-headless.js --ip "$IP" --fish 6 --decos deco_anchor --duration $((T+10)) \
  --update "change_bg=bg_tropical@50" \
  --raw "GRADE=$GRADE@60" "${ARGS[@]}" > "$LOG" 2>&1 &
S=$!; T0=$(date +%s)
esperaN(){ local p="$1" n="$2" lim=$(( $(date +%s)+$3 )) c
  while [ "$(date +%s)" -lt "$lim" ]; do
    c=$(grep -c -- "$p" "$LOG" 2>/dev/null | head -1); c=${c:-0}
    [ "$c" -ge "$n" ] && return 0; kill -0 $S 2>/dev/null || return 1; sleep 1
  done; return 1; }
cap(){ "$ADB" -s "$IP:5555" exec-out screencap -p > "$OUT/sub${ETIQ}_$1.png" 2>/dev/null
  local b; b=$(stat -c %s "$OUT/sub${ETIQ}_$1.png" 2>/dev/null); b=${b:-0}
  [ "$b" -lt 50000 ] && { "$ADB" connect "$IP:5555" >/dev/null 2>&1; sleep 1
    "$ADB" -s "$IP:5555" exec-out screencap -p > "$OUT/sub${ETIQ}_$1.png" 2>/dev/null
    b=$(stat -c %s "$OUT/sub${ETIQ}_$1.png" 2>/dev/null); b=${b:-0}; }
  # ⚠⚠ `adb exec-out screencap` puede devolver un FOTOGRAMA CONGELADO sin dar error,
  #    con la app viva y el log corriendo (visto el 28-ago: 4 capturas byte a byte iguales,
  #    y una de ellas iba a colarse como "dos suelos fundidos a dE 0.1"). El md5 lo caza.
  local m; m=$(md5sum "$OUT/sub${ETIQ}_$1.png" 2>/dev/null | cut -c1-32)
  if [ -n "${VISTOS:-}" ] && echo "$VISTOS" | grep -q "$m"; then
    echo "   !! FOTOGRAMA REPETIDO ($1) — la pantalla no cambio, capturа invalida"
    mv "$OUT/sub${ETIQ}_$1.png" "$OUT/REPETIDA_sub${ETIQ}_$1.png" 2>/dev/null
  fi
  VISTOS="${VISTOS:-} $m"
  echo "$1 t=$(( $(date +%s)-T0 ))s ${b}B" | tee -a "$OUT/acta_sub_$ETIQ.txt"; }
esperaN "AQUARIUM READY" 1 170 || { echo NO_MONTO; kill $S 2>/dev/null; exit 1; }
esperaN "GRADE: bloom=" 1 60 || true
k=0
for s in "${SUBS[@]}"; do
  k=$((k+1))
  esperaN "change_sub: " "$k" 60 || { echo "! no llego $s"; continue; }
  sleep 6; cap "$s"
done
wait $S 2>/dev/null; echo "== fin =="
