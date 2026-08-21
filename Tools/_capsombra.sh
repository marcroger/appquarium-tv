#!/bin/bash
LOG="$1"; DIR="$2"; IP="192.168.1.42"; mkdir -p "$DIR"
cap() { adb connect $IP:5555 >/dev/null 2>&1; adb -s $IP:5555 exec-out screencap -p > "$DIR/$1.png" 2>/dev/null
        local n=$(stat -c%s "$DIR/$1.png" 2>/dev/null || echo 0); [ "$n" -lt 100000 ] && echo "FALLO $1" || echo "OK $1"; }
esperar() { local fin=$((SECONDS+$2)); while [ $SECONDS -lt $fin ]; do grep -aq "$1" "$LOG" && return 0; sleep 2; done; echo "TIMEOUT: $1"; return 1; }
esperar "AQUARIUM READY" 220 && { sleep 10; cap "s0_actual"; }
esperar "SOMBRA: fade=0[.,]80" 120 && { sleep 6; cap "s1_fade080"; }
esperar "SOMBRA: fade=0[.,]35" 120 && { sleep 6; cap "s2_fade035"; }
esperar "SOMBRA: fade=0[.,]00" 120 && { sleep 6; cap "s3_vuelta"; }
echo "--- fin ---"
