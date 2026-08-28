#!/usr/bin/env bash
# ¿QUIEN revienta el suelo: el bloom o el tonemapping? (2026-08-28)
#
# HALLAZGO QUE LO MOTIVA: en los 11 fondos el suelo salia CLAVADO al blanco (L* 99.7,
# croma 0.0) con cifras identicas -> no era el fondo, era el SUELO, y es constante.
#
# ⚠ El 2026-08-28 se apago el TONEMAPPING para ganar luz (aprobado por el user mirando
# la tele). El tonemapping es precisamente lo que hace rodar las altas luces en vez de
# clavarlas. Asi que hay DOS sospechosos y hay que separarlos, no elegir uno.
#
# 4 estados sobre LA MISMA escena, cada uno esperado por SU linea del log:
#   1 prod    bloom ON  · tm OFF   <- lo desplegado
#   2         bloom OFF · tm OFF
#   3         bloom ON  · tm ON
#   4         bloom OFF · tm ON
set -u
IP="${1:-192.168.1.46}"
BG="${2:-bg_tropical}"
ADB="/c/Users/Behere/AppData/Local/Android/Sdk/platform-tools/adb.exe"
cd "$(dirname "$0")/.."
OUT="$(pwd)/_bloom"; mkdir -p "$OUT"; LOG="$OUT/sender_ab.log"; rm -f "$LOG" "$OUT/acta_ab.txt"

node Tools/cast-headless.js --stop --ip "$IP" >/dev/null 2>&1; sleep 6
node Tools/cast-headless.js --ip "$IP" --fish 8 --decos deco_anchor,deco_starfish_blue --duration 240 \
  --update "change_bg=$BG@50" \
  --raw 'GRADE={"bloom":true,"tonemapping":false}@70' \
  --raw 'GRADE={"bloom":false,"tonemapping":false}@110' \
  --raw 'GRADE={"bloom":true,"tonemapping":true}@150' \
  --raw 'GRADE={"bloom":false,"tonemapping":true}@190' > "$LOG" 2>&1 &
S=$!; T0=$(date +%s)

esperaN(){ local p="$1" n="$2" lim=$(( $(date +%s)+$3 )) c
  while [ "$(date +%s)" -lt "$lim" ]; do
    c=$(grep -c -- "$p" "$LOG" 2>/dev/null | head -1); c=${c:-0}
    [ "$c" -ge "$n" ] && return 0
    kill -0 $S 2>/dev/null || return 1; sleep 1
  done; return 1; }

cap(){ "$ADB" -s "$IP:5555" exec-out screencap -p > "$OUT/ab_$1.png" 2>/dev/null
  local b; b=$(stat -c %s "$OUT/ab_$1.png" 2>/dev/null); b=${b:-0}
  [ "$b" -lt 50000 ] && { "$ADB" connect "$IP:5555" >/dev/null 2>&1; sleep 1
    "$ADB" -s "$IP:5555" exec-out screencap -p > "$OUT/ab_$1.png" 2>/dev/null
    b=$(stat -c %s "$OUT/ab_$1.png" 2>/dev/null); b=${b:-0}; }
  # el acta guarda la linea EXACTA del receptor que habia cuando se capturo
  echo "$1  t=$(( $(date +%s)-T0 ))s  ${b}B  <- $(grep -o 'GRADE: bloom=[^|]*' "$LOG" | tail -1)" \
    | tee -a "$OUT/acta_ab.txt"; }

esperaN "AQUARIUM READY" 1 170 || { echo NO_MONTO; kill $S 2>/dev/null; exit 1; }
esperaN "change_bg: .*$BG" 1 60 || true
# Cada estado se espera por su linea GRADE concreta, no por reloj.
esperaN "GRADE: bloom=1.20 tm=OFF"      1 60 && { sleep 8; cap "1_prod_bloomON_tmOFF"; }
esperaN "GRADE: bloom=OFF tm=OFF"       1 60 && { sleep 8; cap "2_bloomOFF_tmOFF"; }
esperaN "GRADE: bloom=1.20 tm=Neutral"  1 60 && { sleep 8; cap "3_bloomON_tmON"; }
esperaN "GRADE: bloom=OFF tm=Neutral"   1 60 && { sleep 8; cap "4_bloomOFF_tmON"; }
wait $S 2>/dev/null; echo "== fin =="
