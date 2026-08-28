#!/usr/bin/env bash
# ¿Se recupera la claridad aprobada SIN reventar el suelo? (2026-08-28)
#
# MEDIDO: el tonemapping es la compuerta del clip. Con tm ON el suelo va a 0.00 % aunque
# el bloom este a tope; sin el, el bloom lo lleva al 53.7 %. Pero recuperar el tonemapping
# cuesta 5.4 L* de agua, y la claridad es justo lo que el user aprobo mirando la tele.
#
# ⚠ El HDR (que seria la otra salida, la del movil) NO es ajustable en caliente: no hay
#    campo en GradePayload, asi que costaria un build. La exposicion SI, y con el
#    tonemapping puesto sube la luz RODANDO las altas en vez de clavarlas.
#
# Se barre la exposicion con tm ON + bloom ON. Cada estado se espera por su `exp=` en el log.
set -u
IP="${1:-192.168.1.46}"
ADB="/c/Users/Behere/AppData/Local/Android/Sdk/platform-tools/adb.exe"
cd "$(dirname "$0")/.."
OUT="$(pwd)/_bloom"; LOG="$OUT/sender_dulce.log"; rm -f "$LOG" "$OUT/acta_dulce.txt"

node Tools/cast-headless.js --stop --ip "$IP" >/dev/null 2>&1; sleep 6
node Tools/cast-headless.js --ip "$IP" --fish 8 --decos deco_anchor,deco_starfish_blue --duration 250 \
  --update "change_bg=bg_tropical@50" \
  --raw 'GRADE={"bloom":true,"tonemapping":true,"exposure":0.05}@70' \
  --raw 'GRADE={"bloom":true,"tonemapping":true,"exposure":0.20}@110' \
  --raw 'GRADE={"bloom":true,"tonemapping":true,"exposure":0.35}@150' \
  --raw 'GRADE={"bloom":true,"tonemapping":true,"exposure":0.50}@190' > "$LOG" 2>&1 &
S=$!; T0=$(date +%s)
esperaN(){ local p="$1" n="$2" lim=$(( $(date +%s)+$3 )) c
  while [ "$(date +%s)" -lt "$lim" ]; do
    c=$(grep -c -- "$p" "$LOG" 2>/dev/null | head -1); c=${c:-0}
    [ "$c" -ge "$n" ] && return 0; kill -0 $S 2>/dev/null || return 1; sleep 1
  done; return 1; }
cap(){ "$ADB" -s "$IP:5555" exec-out screencap -p > "$OUT/dulce_$1.png" 2>/dev/null
  local b; b=$(stat -c %s "$OUT/dulce_$1.png" 2>/dev/null); b=${b:-0}
  [ "$b" -lt 50000 ] && { "$ADB" connect "$IP:5555" >/dev/null 2>&1; sleep 1
    "$ADB" -s "$IP:5555" exec-out screencap -p > "$OUT/dulce_$1.png" 2>/dev/null
    b=$(stat -c %s "$OUT/dulce_$1.png" 2>/dev/null); b=${b:-0}; }
  echo "$1 t=$(( $(date +%s)-T0 ))s ${b}B <- $(grep -o 'GRADE: bloom=[^|]*' "$LOG" | tail -1)" \
    | tee -a "$OUT/acta_dulce.txt"; }
esperaN "AQUARIUM READY" 1 170 || { echo NO_MONTO; kill $S 2>/dev/null; exit 1; }
for e in 0.05 0.20 0.35 0.50; do
  esperaN "tm=Neutral sat=18 con=10 exp=$e" 1 60 && { sleep 8; cap "exp$e"; }
done
wait $S 2>/dev/null; echo "== fin =="
