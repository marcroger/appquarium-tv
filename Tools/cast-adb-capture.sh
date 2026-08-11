#!/usr/bin/env bash
# Captura forense del corte Cast ~3min en el Xiaomi TV Box S.
#
# Prueba la hipótesis principal (research 2026-07-21): el watchdog de Cast
# (chromecast/browser/cast_memory_pressure_monitor.cc) mide MemAvailable del SISTEMA
# cada 5s y a partir del 25% dispara presión crítica. Nuestros indicadores de
# "receiver sano" (heap WASM/JS, lag del hilo) son CIEGOS a eso.
#
# Uso:  bash Tools/cast-adb-capture.sh [IP]     (por defecto 192.168.1.33)
# Luego: castear desde localhost:3003 (RUNG 2) y esperar al corte. Ctrl+C para parar.
# Salida: _cast_adb_capture/ con meminfo.log, logcat.log, procs.log
#
# ⚠ IP 2026-07-27: la caja está en 192.168.1.33 (antes .47 — DHCP la cambió).
#   Verificar con: curl -s http://<IP>:8008/setup/eureka_info | grep name

set -u
IP="${1:-192.168.1.33}"
ADB="/c/Users/Behere/AppData/Local/Android/Sdk/platform-tools/adb.exe"
OUT="_cast_adb_capture"
mkdir -p "$OUT"

echo "== conectando a $IP =="
"$ADB" connect "$IP:5555" >/dev/null 2>&1
DEV="$IP:5555"
if ! "$ADB" -s "$DEV" shell true >/dev/null 2>&1; then
  "$ADB" connect "$IP:4321" >/dev/null 2>&1
  DEV="$IP:4321"
  if ! "$ADB" -s "$DEV" shell true >/dev/null 2>&1; then
    echo "FALLO: adb no conecta a $IP. Activa depuración por red en la TV y acepta el diálogo RSA."
    exit 1
  fi
fi
"$ADB" devices -l

# --- Umbrales del watchdog, calculados sobre el MemTotal REAL del device ---
MEMTOTAL=$("$ADB" -s "$DEV" shell "grep MemTotal /proc/meminfo" | tr -dc '0-9')
CRIT=$(( MEMTOTAL * 25 / 100 ))
MOD=$(( MEMTOTAL * 40 / 100 ))
echo "== umbrales del watchdog de Cast =="
echo "MemTotal   = $MEMTOTAL kB"
echo "moderado   = $MOD kB (40%)"
echo "CRITICO    = $CRIT kB (25%)  <-- si MemAvailable baja de aqui, H2 CONFIRMADA"
{ echo "MemTotal=$MEMTOTAL"; echo "moderado40=$MOD"; echo "critico25=$CRIT"; } > "$OUT/thresholds.txt"
"$ADB" -s "$DEV" shell "cat /proc/meminfo" | head -5 > "$OUT/meminfo-baseline.txt"

# 1) logcat: quién mata a quién. Limpiamos el buffer para tener solo la sesión.
#    ⚠ Con reintentos: un hipo del WiFi de la caja mata el logcat y antes se llevaba
#      por delante el run entero (pasó el 2026-07-27 tras un reinicio).
"$ADB" -s "$DEV" logcat -c >/dev/null 2>&1
(
  while true; do
    "$ADB" -s "$DEV" logcat -v time >> "$OUT/logcat.log" 2>&1
    echo "--- [logcat cayó, reconectando] ---" >> "$OUT/logcat.log"
    sleep 3
    "$ADB" connect "$DEV" >/dev/null 2>&1
  done
) &
LOGCAT_PID=$!

# 2) memoria del SISTEMA + RSS de mediashell cada 2s (el watchdog muestrea a 5s).
#    Una sola llamada adb por tick para no meter ruido de round-trips.
(
  while true; do
    TS=$(date +%H:%M:%S)
    RAW=$("$ADB" -s "$DEV" shell "grep -E '^(MemAvailable|MemFree)' /proc/meminfo | tr -d ' kB' | tr '\n' ' '; echo -n 'MSHELL:'; ps -A -o RSS,NAME 2>/dev/null | grep mediashell | head -1 | tr -s ' '" 2>/dev/null | tr -d '\r')
    AVAIL=$(echo "$RAW" | sed -n 's/.*MemAvailable:\([0-9]*\).*/\1/p')
    if [ -n "$AVAIL" ]; then
      PCT=$(( AVAIL * 100 / MEMTOTAL ))
      FLAG=""
      [ "$AVAIL" -lt "$MOD" ] && FLAG=" [MODERADO]"
      [ "$AVAIL" -lt "$CRIT" ] && FLAG=" [!!! CRITICO !!!]"
      echo "$TS avail=${AVAIL}kB (${PCT}%)$FLAG | $RAW"
    else
      echo "$TS (sin lectura) $RAW"
    fi
    sleep 2
  done
) > "$OUT/meminfo.log" 2>&1 &
MEM_PID=$!

# 3) estado del proceso del receiver cada 5s (¿lo matan y reaparece con otro PID?)
(
  while true; do
    TS=$(date +%H:%M:%S)
    P=$("$ADB" -s "$DEV" shell "ps -A -o PID,RSS,NAME 2>/dev/null | grep -iE 'mediashell|cast_shell|chrome|webview'" 2>/dev/null | tr -d '\r')
    echo "--- $TS"; echo "$P"
    sleep 5
  done
) > "$OUT/procs.log" 2>&1 &
PROC_PID=$!

# 4) DESGLOSE de la memoria del renderer y del proceso GPU cada 10s.
#    Responde QUE crece durante la fuga (~+50MB/min medidos el 2026-07-27):
#    Native Heap / Graphics / GL mtrack / EGL mtrack / Unknown.
(
  while true; do
    TS=$(date +%H:%M:%S)
    PIDS=$("$ADB" -s "$DEV" shell "ps -A -o PID,NAME 2>/dev/null | grep -E 'sandboxed_process0|privileged_process0'" 2>/dev/null | tr -d '\r')
    if [ -n "$PIDS" ]; then
      echo "$PIDS" | while read -r P NAME _; do
        [ -z "$P" ] && continue
        SHORT=$(echo "$NAME" | sed 's/.*://; s/:.*//')
        echo "=== $TS pid=$P $SHORT"
        # ⚠ el < /dev/null es OBLIGATORIO: adb shell se come el stdin del bucle
        #   y sin él solo se dumpea el PRIMER proceso de la lista (bug del run 2026-07-27).
        "$ADB" -s "$DEV" shell "dumpsys meminfo $P" < /dev/null 2>/dev/null | tr -d '\r' \
          | grep -iE "Native Heap|Graphics|GL mtrack|EGL mtrack|Gfx dev|Unknown|TOTAL PSS|TOTAL RSS" \
          | sed 's/^/    /'
      done
    else
      echo "=== $TS (sin procesos del receiver)"
    fi
    sleep 10
  done
) > "$OUT/memdetail.log" 2>&1 &
DETAIL_PID=$!

echo
echo "== CAPTURANDO =="
echo "AHORA: castea desde http://localhost:3003 en RUNG 2 (Unity ON) al 'Decodificador multimedia Xiaomi'."
echo "NO toques nada. Cuando corte, espera ~10s mas y pulsa Ctrl+C."
echo "Salida en $OUT/"

trap 'kill $LOGCAT_PID $MEM_PID $PROC_PID $DETAIL_PID 2>/dev/null; echo; echo "== PARADO. Analizando =="; \
  echo "-- DESGLOSE renderer: primer bloque vs ultimo (que crece) --"; \
  grep -A7 "sandboxed" "$OUT/memdetail.log" | head -9; echo "   ..."; \
  grep -A7 "sandboxed" "$OUT/memdetail.log" | tail -9; echo; \
  echo "-- eventos de muerte en logcat --"; \
  grep -iE "lowmemorykiller|lmkd|kill|died|ANR|OutOfMemory|Renderer|mediashell|MemoryPressure|SIGKILL|onTrimMemory" "$OUT/logcat.log" | tail -40; \
  echo; echo "-- H2: minimo de MemAvailable observado --"; \
  grep -o "avail=[0-9]*kB ([0-9]*%)" "$OUT/meminfo.log" | sort -t= -k2 -n | head -3; \
  echo "   (critico = $CRIT kB / 25%)"; \
  grep -c "CRITICO" "$OUT/meminfo.log" | sed "s/^/   ticks en CRITICO: /"; \
  grep -c "MODERADO" "$OUT/meminfo.log" | sed "s/^/   ticks en MODERADO: /"; \
  echo; echo "-- ultimos 12 ticks de memoria (el corte) --"; \
  tail -12 "$OUT/meminfo.log"; \
  exit 0' INT

# No hacemos `wait` del logcat: si cae, el bucle de arriba lo relanza. Dormimos
# hasta el Ctrl+C (o el SIGINT del `timeout -s INT`) para que salte el trap.
while true; do sleep 5; done
