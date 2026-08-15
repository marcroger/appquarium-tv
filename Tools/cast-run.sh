#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════════════
# CICLO COMPLETO DE EXPERIMENTO CAST — sin intervención humana.
#
#   bash Tools/cast-run.sh <rung> [etiqueta]
#   bash Tools/cast-run.sh 2 baseline-produccion
#   bash Tools/cast-run.sh 23 sin-video
#
# Hace, en orden:
#   1. reinicia el device por adb
#   2. espera a que la memoria se asiente (>=40% y sin matanzas del lmkd)
#   3. arranca la captura forense (logcat + MemAvailable 2s + RSS 5s + dumpsys 10s)
#   4. castea el escalón con Tools/cast-headless.js (protocolo Cast v2, sin navegador)
#   5. al caer la sesión, para la captura y ANALIZA
#
# Salida: _cast_runs/<etiqueta>/ con los logs + resumen por stdout.
# ═══════════════════════════════════════════════════════════════════════════════
set -u
RUNG="${1:?uso: cast-run.sh <rung> [etiqueta]}"
LABEL="${2:-rung$RUNG}"
# ⚠ La IP de la caja la da el DHCP y CAMBIA (2026-08-10 estaba en .33, el 08-11 en .34).
# Peor aún: la .33 seguía respondiendo a ping —la había cogido otro cacharro—, así que el
# script se quedó 7 min esperando a una caja que no estaba ahí. No fiarse del ping: la señal
# buena es el puerto 8008 (Cast) abierto Y que el device diga que es el Xiaomi.
# CAST_IP=x.x.x.x salta el descubrimiento.
IP="${CAST_IP:-}"
esXiaomi() {  # $1 = ip → 0 si responde el receiver Cast del Xiaomi
  timeout 2 bash -c "echo > /dev/tcp/$1/8008" 2>/dev/null || return 1
  curl -s --max-time 3 "http://$1:8008/setup/eureka_info" 2>/dev/null | grep -qi "xiaomi"
}
if [ -z "$IP" ]; then
  for CAND in 192.168.1.34 192.168.1.33; do            # últimas conocidas primero
    esXiaomi "$CAND" && { IP="$CAND"; break; }
  done
fi
if [ -z "$IP" ]; then                                   # barrido de la subred
  echo "[$(date +%H:%M:%S)] buscando la caja en la red (puerto 8008)…"
  for i in $(seq 2 254); do
    ( esXiaomi "192.168.1.$i" && echo "192.168.1.$i" > /tmp/.castip.$$ ) &
  done
  wait
  [ -f /tmp/.castip.$$ ] && { IP=$(cat /tmp/.castip.$$); rm -f /tmp/.castip.$$; }
fi
[ -n "$IP" ] || { echo "FALLO: no encuentro la caja Xiaomi en la red (¿encendida? ¿otra subred?)"; exit 1; }
echo "[$(date +%H:%M:%S)] caja Xiaomi en $IP"
ADB="/c/Users/Behere/AppData/Local/Android/Sdk/platform-tools/adb.exe"
DEV="$IP:5555"
OUT="_cast_runs/$LABEL"
MEMTOTAL=0

# ⚠ Si una tanda anterior se cortó, su `node cast-headless.js` puede seguir vivo casteando.
# En Windows `rm -rf` NO borra un fichero abierto, así que los dos procesos escriben sobre el
# mismo sender.log y el resultado sale entrelazado (pasó el 2026-08-10: 2 bloques FIN, líneas
# partidas, medición inservible). Matar huérfanos ANTES de empezar.
powershell -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"Name='node.exe'\" | Where-Object { \$_.CommandLine -like '*cast-headless*' } | ForEach-Object { Stop-Process -Id \$_.ProcessId -Force }" >/dev/null 2>&1

rm -rf "$OUT"; mkdir -p "$OUT"
mkdir -p _cast_runs
# Comprobación dura: si el sender.log sobrevivió al rm, alguien lo tiene abierto → abortar.
[ -e "$OUT/sender.log" ] && { echo "FALLO: $OUT/sender.log sigue abierto por otro proceso"; exit 1; }
say() { echo "[$(date +%H:%M:%S)] $*"; echo "[$(date +%H:%M:%S)] [$LABEL] $*" >> _cast_runs/ESTADO.md; }

# ── 1. reinicio ───────────────────────────────────────────────────────────────
"$ADB" connect "$DEV" >/dev/null 2>&1
say "reiniciando $IP …"
"$ADB" -s "$DEV" reboot </dev/null >/dev/null 2>&1
"$ADB" disconnect >/dev/null 2>&1
for i in $(seq 1 90); do
  timeout 2 bash -c "echo > /dev/tcp/$IP/8008" 2>/dev/null && { say "device arriba"; break; }
  sleep 5
done
sleep 20
for i in 1 2 3 4 5 6; do
  OUT_C=$("$ADB" connect "$DEV" 2>&1)
  case "$OUT_C" in *"connected to"*) say "adb conectado"; break;; esac
  sleep 10
done
# `adb connect` responde "connected to" antes de que adbd acepte shells, y tras un reinicio
# el device puede quedar en `unauthorized` hasta que se acepte el diálogo con el mando.
# Reintentar hasta 2 min y distinguir ambos casos — si no, se aborta la tanda por una carrera.
SHELL_OK=0
for i in $(seq 1 24); do
  ERR=$("$ADB" -s "$DEV" shell true </dev/null 2>&1) && { SHELL_OK=1; break; }
  case "$ERR" in
    *unauthorized*) say "device UNAUTHORIZED — acepta el diálogo en la TV (marca «permitir siempre»)";;
  esac
  "$ADB" connect "$DEV" >/dev/null 2>&1
  sleep 5
done
[ "$SHELL_OK" = "1" ] || { say "FALLO: adb no da shell tras 2 min"; exit 1; }
say "shell adb operativo"

# Stay awake (AC+USB+wireless): evita que la caja entre en standby y cierre adbd a mitad de tanda.
"$ADB" -s "$DEV" shell "settings put global stay_on_while_plugged_in 7" </dev/null >/dev/null 2>&1

MEMTOTAL=$("$ADB" -s "$DEV" shell "grep MemTotal /proc/meminfo" </dev/null | tr -dc '0-9')
CRIT=$(( MEMTOTAL * 25 / 100 ))
say "MemTotal=${MEMTOTAL}kB · umbral crítico 25% = ${CRIT}kB"

# ── 2. asentamiento ───────────────────────────────────────────────────────────
say "esperando asentamiento (>=40% y 0 matanzas del lmkd)…"
for i in $(seq 1 40); do
  A=$("$ADB" -s "$DEV" shell "grep '^MemAvailable' /proc/meminfo" </dev/null 2>/dev/null | tr -dc '0-9')
  K=$("$ADB" -s "$DEV" shell "logcat -d -t 300 | grep -c 'lowmemorykiller.*Kill'" </dev/null 2>/dev/null | tr -dc '0-9')
  [ -z "$A" ] && { "$ADB" connect "$DEV" >/dev/null 2>&1; sleep 15; continue; }
  P=$(( A * 100 / MEMTOTAL )); K=${K:-0}
  say "  t+$((i*15))s mem=${P}% kills=${K}"
  if [ "$P" -ge 40 ] && [ "$K" -eq 0 ] && [ "$i" -ge 4 ]; then say "ASENTADO en ${P}%"; START_PCT=$P; break; fi
  sleep 15
done
START_PCT=${START_PCT:-$P}

# ── 3. captura ────────────────────────────────────────────────────────────────
"$ADB" -s "$DEV" logcat -c </dev/null >/dev/null 2>&1
( while true; do "$ADB" -s "$DEV" logcat -v time </dev/null >> "$OUT/logcat.log" 2>&1; sleep 3; "$ADB" connect "$DEV" >/dev/null 2>&1; done ) & LOGCAT_PID=$!
(
  while true; do
    TS=$(date +%H:%M:%S)
    A=$("$ADB" -s "$DEV" shell "grep '^MemAvailable' /proc/meminfo" </dev/null 2>/dev/null | tr -dc '0-9')
    [ -n "$A" ] && echo "$TS avail=${A}kB ($(( A * 100 / MEMTOTAL ))%)"
    sleep 2
  done
) > "$OUT/meminfo.log" 2>&1 & MEM_PID=$!
(
  while true; do
    TS=$(date +%H:%M:%S)
    echo "--- $TS"
    "$ADB" -s "$DEV" shell "ps -A -o PID,RSS,NAME | grep -E 'mediashell|webview'" </dev/null 2>/dev/null | tr -d '\r'
    sleep 5
  done
) > "$OUT/procs.log" 2>&1 & PROC_PID=$!
(
  while true; do
    TS=$(date +%H:%M:%S)
    P=$("$ADB" -s "$DEV" shell "ps -A -o PID,NAME | grep sandboxed_process0" </dev/null 2>/dev/null | tr -d '\r' | awk '{print $1}' | head -1)
    if [ -n "$P" ]; then
      echo "=== $TS pid=$P"
      "$ADB" -s "$DEV" shell "dumpsys meminfo $P" </dev/null 2>/dev/null | tr -d '\r' \
        | grep -iE "Native Heap|Graphics|GL mtrack|EGL mtrack|Unknown|TOTAL PSS" | sed 's/^/    /'
    fi
    sleep 10
  done
) > "$OUT/memdetail.log" 2>&1 & DETAIL_PID=$!

cleanup() { kill $LOGCAT_PID $MEM_PID $PROC_PID $DETAIL_PID 2>/dev/null; }
trap cleanup EXIT

# ── 3b. (opcional) liberar memoria del sistema ────────────────────────────────
# FREE_MEM=1 → force-stop de apps de fondo no esenciales justo antes de castear.
# Es REVERSIBLE (se relanzan solas o al reiniciar) y NO desinstala ni desactiva nada.
# Sirve para responder: ¿cuánta holgura de memoria hace falta para que NO corte?
if [ "${FREE_MEM:-0}" = "1" ]; then
  say "liberando memoria: force-stop de apps de fondo…"
  for P in com.netflix.ninja com.netflix.tokenmanager com.google.android.youtube.tv \
           com.android.vending com.google.android.videos com.mitv.tvhome.atv \
           com.mitv.tvhome.oemtab com.miui.tv.analytics com.xiaomi.mi_connect_service \
           com.google.android.apps.tv.dreamx com.android.providers.tv \
           com.google.android.katniss com.xiaomi.android.tvsetup.partnercustomizer; do
    "$ADB" -s "$DEV" shell "am force-stop $P" </dev/null >/dev/null 2>&1
  done
  sleep 5
  A=$("$ADB" -s "$DEV" shell "grep '^MemAvailable' /proc/meminfo" </dev/null | tr -dc '0-9')
  START_PCT=$(( A * 100 / MEMTOTAL ))
  say "tras liberar: ${A}kB (${START_PCT}%)"
fi

# ── 4. cast ───────────────────────────────────────────────────────────────────
say "CASTEANDO escalón $RUNG (headless)…"
# FISH=N → castea un acuario REAL con N peces (carga bundles remotos). Sin FISH la
# escena queda vacía y la medida NO representa el coste de memoria de producción.
node Tools/cast-headless.js --ip "$IP" --rung "$RUNG" --duration 660 --fish "${FISH:-0}" > "$OUT/sender.log" 2>&1
say "sesión terminada"
# red de seguridad: que NUNCA quede la app en pantalla en la TV
node Tools/cast-headless.js --ip "$IP" --stop >/dev/null 2>&1
sleep 8
cleanup

# ── 5. análisis ───────────────────────────────────────────────────────────────
exec > >(tee "$OUT/resumen.txt") 2>&1
echo
echo "══════════ RESULTADO · escalón $RUNG · $LABEL ══════════"
echo "memoria libre al empezar : ${START_PCT}%"
grep "DURACIÓN DE SESIÓN" "$OUT/sender.log" | tail -1
grep "FIN:" "$OUT/sender.log" | tail -1
echo
echo "-- crash del renderer --"
grep -cE "tombstoned" "$OUT/logcat.log" | sed 's/^/   firmas crashpad\/tombstoned: /'
echo "-- RSS del renderer --"
awk '/^--- /{ts=$2} /sandboxed_process0/{if($2+0>max){max=$2+0;mts=ts}; last=ts; lr=$2}
     END{printf "   pico  %.1f MB (%s)\n   muere %.1f MB (%s)\n", max/1024, mts, lr/1024, last}' "$OUT/procs.log"
echo "-- fuga (Native Heap del renderer, tras asentarse la carga) --"
awk '/^=== /{ts=$2; next} /^ *Native Heap +[0-9]/{nh=$3} /TOTAL PSS:/{print ts, nh/1024}' "$OUT/memdetail.log" \
  | sort -u | awk '{t[NR]=$1; v[NR]=$2} END{
      if (NR<6) { print "   (muestras insuficientes)"; exit }
      s=int(NR/2); printf "   %s → %s : %.1f → %.1f MB", t[s], t[NR], v[s], v[NR];
      printf "   (%.1f MB en %d muestras de 10s ≈ %.1f MB/min)\n", v[NR]-v[s], NR-s, (v[NR]-v[s])/((NR-s)*10/60) }'
echo "-- presión del sistema --"
grep -o "([0-9]*%)" "$OUT/meminfo.log" | tr -d '()%' | sort -n | head -1 | sed 's/^/   mínimo MemAvailable: /;s/$/%/'
grep -c "lowmemorykiller.*Kill" "$OUT/logcat.log" | sed 's/^/   procesos matados por el lmkd: /'
grep -c "ProduceSkia" "$OUT/logcat.log" | sed 's/^/   errores ProduceSkia (compositing): /'
echo "════════════════════════════════════════════════════════"
echo "logs en $OUT/"
