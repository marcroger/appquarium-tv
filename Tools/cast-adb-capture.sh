#!/usr/bin/env bash
# Captura forense del corte Cast ~3min en el Xiaomi TV Box S.
#
# Prueba la hipótesis principal (research 2026-07-21): el watchdog de Cast
# (chromecast/browser/cast_memory_pressure_monitor.cc) mide MemAvailable del SISTEMA
# cada 5s y a partir del 25% dispara presión crítica. Nuestros indicadores de
# "receiver sano" (heap WASM/JS, lag del hilo) son CIEGOS a eso.
#
# Uso:  bash Tools/cast-adb-capture.sh [IP]     (por defecto 192.168.1.47)
# Luego: castear desde localhost:3003 y esperar al corte. Ctrl+C para parar.
# Salida: _cast_adb_<fecha>/  con meminfo.log, logcat.log, procs.log

set -u
IP="${1:-192.168.1.47}"
ADB="/c/Users/Behere/AppData/Local/Android/Sdk/platform-tools/adb"
OUT="_cast_adb_capture"
mkdir -p "$OUT"

echo "== conectando a $IP =="
$ADB connect "$IP:5555" || $ADB connect "$IP:4321" || { echo "FALLO: adb no conecta. Activa depuración por red en la TV."; exit 1; }
$ADB devices -l

DEV="$IP:5555"
$ADB -s "$DEV" shell true 2>/dev/null || DEV="$IP:4321"

echo "== RAM total del device =="
$ADB -s "$DEV" shell cat /proc/meminfo | head -3 | tee "$OUT/meminfo-baseline.txt"

# 1) logcat: quién mata a quién. Limpiamos el buffer para tener solo la sesión.
$ADB -s "$DEV" logcat -c 2>/dev/null
$ADB -s "$DEV" logcat -v time > "$OUT/logcat.log" 2>&1 &
LOGCAT_PID=$!

# 2) meminfo del sistema cada 2s (el watchdog de Cast muestrea a 5s)
(
  while true; do
    TS=$(date +%H:%M:%S)
    LINE=$($ADB -s "$DEV" shell "grep -E '^(MemTotal|MemAvailable|MemFree)' /proc/meminfo | tr '\n' ' '" 2>/dev/null)
    echo "$TS $LINE"
    sleep 2
  done
) > "$OUT/meminfo.log" 2>&1 &
MEM_PID=$!

# 3) estado del proceso del receiver cada 5s (¿lo matan y reaparece con otro PID?)
(
  while true; do
    TS=$(date +%H:%M:%S)
    P=$($ADB -s "$DEV" shell "ps -A 2>/dev/null | grep -iE 'mediashell|cast_shell|chrome' | head -5" 2>/dev/null)
    echo "--- $TS"; echo "$P"
    sleep 5
  done
) > "$OUT/procs.log" 2>&1 &
PROC_PID=$!

echo
echo "== CAPTURANDO =="
echo "AHORA: castea desde http://localhost:3003 (RUNG 2) y NO toques nada."
echo "Cuando corte, espera ~10s más y pulsa Ctrl+C."
echo "Salida en $OUT/"

trap 'kill $LOGCAT_PID $MEM_PID $PROC_PID 2>/dev/null; echo; echo "== PARADO. Analizando =="; \
  echo "-- eventos de muerte en logcat --"; \
  grep -iE "lowmemorykiller|lmkd|kill|died|ANR|OutOfMemory|Renderer|mediashell|MemoryPressure|SIGKILL" "$OUT/logcat.log" | tail -40; \
  echo; echo "-- MemAvailable (primera y ultimas lineas) --"; \
  head -2 "$OUT/meminfo.log"; tail -10 "$OUT/meminfo.log"; \
  exit 0' INT

wait $LOGCAT_PID
