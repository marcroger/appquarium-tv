#!/usr/bin/env bash
# ¿Hay una INTENSIDAD de bloom que de brillo sin lavar la arena? (2026-08-28)
#
# MEDIDO ANTES, todo sobre la misma escena y la misma sesion:
#   bloom 1.20 + tm OFF  -> 53.68 % de la banda del suelo clavada al blanco
#   bloom 1.20 + tm ON   -> clip 0.00 pero la textura del suelo cae al 66 %
#   bloom OFF  + tm ON   -> clip 0.00 y textura al 91 %  (mas claro que el 27-ago)
# ⚠ El user pidio "mas brillo y vida", asi que quitar el bloom entero le devuelve una
#   decision que ya tomo. Se busca si a intensidad baja aporta glow SIN comerse el grano.
set -u
IP="${1:-192.168.1.46}"
ADB="/c/Users/Behere/AppData/Local/Android/Sdk/platform-tools/adb.exe"
cd "$(dirname "$0")/.."
OUT="$(pwd)/_bloom"; LOG="$OUT/sender_int.log"; rm -f "$LOG" "$OUT/acta_int.txt" "$OUT"/int_*.png

node Tools/cast-headless.js --stop --ip "$IP" >/dev/null 2>&1; sleep 6
node Tools/cast-headless.js --ip "$IP" --fish 8 --decos deco_anchor,deco_starfish_blue --duration 250 \
  --update "change_bg=bg_tropical@50" \
  --raw 'GRADE={"bloom":true,"tonemapping":true,"bloomIntensity":0.30}@70' \
  --raw 'GRADE={"bloom":true,"tonemapping":true,"bloomIntensity":0.60}@110' \
  --raw 'GRADE={"bloom":true,"tonemapping":true,"bloomIntensity":0.90}@150' \
  --raw 'GRADE={"bloom":true,"tonemapping":false,"bloomIntensity":0.30}@190' > "$LOG" 2>&1 &
S=$!; T0=$(date +%s)
esperaN(){ local p="$1" n="$2" lim=$(( $(date +%s)+$3 )) c
  while [ "$(date +%s)" -lt "$lim" ]; do
    c=$(grep -c -- "$p" "$LOG" 2>/dev/null | head -1); c=${c:-0}
    [ "$c" -ge "$n" ] && return 0; kill -0 $S 2>/dev/null || return 1; sleep 1
  done; return 1; }
cap(){ "$ADB" -s "$IP:5555" exec-out screencap -p > "$OUT/int_$1.png" 2>/dev/null
  local b; b=$(stat -c %s "$OUT/int_$1.png" 2>/dev/null); b=${b:-0}
  [ "$b" -lt 50000 ] && { "$ADB" connect "$IP:5555" >/dev/null 2>&1; sleep 1
    "$ADB" -s "$IP:5555" exec-out screencap -p > "$OUT/int_$1.png" 2>/dev/null
    b=$(stat -c %s "$OUT/int_$1.png" 2>/dev/null); b=${b:-0}; }
  echo "$1 t=$(( $(date +%s)-T0 ))s ${b}B <- $(grep -o 'GRADE: bloom=[^|]*' "$LOG" | tail -1)" \
    | tee -a "$OUT/acta_int.txt"; }
esperaN "AQUARIUM READY" 1 170 || { echo NO_MONTO; kill $S 2>/dev/null; exit 1; }
esperaN "GRADE: bloom=0.30 tm=Neutral" 1 60 && { sleep 8; cap "i030_tmON"; }
esperaN "GRADE: bloom=0.60 tm=Neutral" 1 60 && { sleep 8; cap "i060_tmON"; }
esperaN "GRADE: bloom=0.90 tm=Neutral" 1 60 && { sleep 8; cap "i090_tmON"; }
esperaN "GRADE: bloom=0.30 tm=OFF"     1 60 && { sleep 8; cap "i030_tmOFF"; }
wait $S 2>/dev/null; echo "== fin =="
