#!/usr/bin/env bash
# ¿Se DISTINGUEN las 7 luces en la tele? (2026-08-30)
#
# Tercera pata del criterio del user (fondos OK, sustratos casi, luces SIN MEDIR).
# 5 de las 7 son de pago: light_blue, light_deep, light_purple, light_sunset, light_cycle.
#
# ⚠ NO manda GRADE. El player desplegado (rcv 2026-08-28 tmA) ya lleva el grado bueno
#   HORNEADO (bloom 0.30 + tonemapping), y mandar un GRADE mediria otra cosa distinta de
#   produccion. Lo que se hace es LEER la linea `HORNEADO:` y guardarla en el acta: esa
#   linea demuestra a la vez que corre el binario nuevo y con que valores.
#
# ⚠ Se fija TODO lo que no es la luz, o el barrido mide la escena en vez del preset:
#   · bg_classic + sub_gravel   -> los mismos de CAST_PARIDAD_VISUAL.md 0.6, para que
#                                  la tabla salga comparable con la medida a dos pantallas.
#   · ambient=day               -> el ciclo dia/noche toca la direccional Y el ambiente,
#                                  que caen dentro de las mismas bandas. Ademas el reloj
#                                  LOCAL puede cambiar de fase al pasar la hora en mitad
#                                  del barrido: un UPDATE explicito pone _modoManual=true
#                                  y lo calla (AmbientModeController.Update).
#   · deco_anchor               -> objeto FIJO y ACROMATICO como referencia.
#
# ⚠⚠ light_cycle va APARTE y AL FINAL (ver la funcion de abajo): reescribe colorFilter y
#    el color de los spots CADA FRAME a 0.07 Hz, asi que no tiene un valor sino un
#    recorrido, y dos capturas suyas no son comparables entre si.
set -u
IP="${1:-}"
[ -z "$IP" ] && { echo "uso: $0 <IP-de-la-tele> [etiqueta]"; echo
  echo "  ⚠ ni el ping ni el 8008 identifican la caja (hay otro Cast en la casa)."
  echo "    curl http://IP:8008/setup/eureka_info | grep -i xiaomi"; exit 1; }
ETIQ="${2:-prod}"
ADB="/c/Users/Behere/AppData/Local/Android/Sdk/platform-tools/adb.exe"
cd "$(dirname "$0")/.."
OUT="$(pwd)/_luces"; mkdir -p "$OUT"
LOG="$OUT/sender_$ETIQ.log"; ACTA="$OUT/acta_$ETIQ.txt"
rm -f "$LOG" "$ACTA" "$OUT"/luz_*.png "$OUT"/cycle_f*.png "$OUT"/REPETIDA_*.png

# ⚠ light_white NO va la primera: es el preset por defecto, y el handler responde
#   "ya estaba puesta, sin cambio" — el conteo funcionaria igual, pero se pierde la
#   prueba de que la transicion ocurre de verdad. Va segunda, tras un cambio real.
LUCES=(light_warm light_white light_blue light_deep light_purple light_sunset)

T0S=75; PASO=25
ARGS=(); T=$T0S
for l in "${LUCES[@]}"; do ARGS+=(--update "change_light=$l@$T"); T=$((T+PASO)); done
TCYCLE=$T                       # el ciclo, el ultimo
ARGS+=(--update "change_light=light_cycle@$TCYCLE")
ARGS+=(--update "dump=@$((TCYCLE+30))")
# ⚠⚠ 2026-08-31: ERA `TCYCLE+45` Y SE QUEDABA CORTO. La rafaga del ciclo tarda ~50 s
#    (9 capturas x ~5,5 s: `sleep 2` MAS los ~3,5 s que cuesta el propio screencap por red),
#    y con +45 la sesion moria a mitad => las DOS ultimas capturas no eran el acuario:
#    `cycle_f8` (692 colores) era la pantalla negra tras morir la app y `cycle_f9` el
#    lanzador de Android TV. Se colaron en el RANGO de light_cycle y lo inflaron.
# 🧭 El comentario de abajo decia «9 capturas a ~2 s = ~18 s» y esa cuenta OLVIDABA LO QUE
#    CUESTA LA PROPIA CAPTURA. Un calculo de tiempos que solo suma los `sleep` miente.
DUR=$((TCYCLE+90))

# ⚠⚠ PREFLIGHT DE RUTA — antes de gastar 5 minutos. La primera tanda real (2026-08-30) se
#    perdio entera porque `*.workers.dev` no era alcanzable desde esta red: el acuario se
#    quedo en `BDL 1/7` sin un solo error, y la pantalla salio negra. No era el token, ni el
#    anti-bot, ni el Worker — era la RUTA (traceroute muriendo dentro del ISP, TCP 443
#    rechazado en 188.114.96.0/24 mientras cloudflare.com iba fino).
#    🧭 Es la regla que ya estaba en memoria: comprobar la ruta ANTES que el codigo.
if ! curl -s -o /dev/null -m 8 -w '' "https://appquarium-assets.appquarium.workers.dev/" 2>/dev/null; then
  RC=$(curl -s -o /dev/null -m 8 -w '%{http_code}' "https://appquarium-assets.appquarium.workers.dev/" 2>/dev/null)
  if [ "${RC:-000}" = "000" ]; then
    echo "ABORTA ANTES DE EMPEZAR: el Worker de los bundles no responde (HTTP 000)."
    echo "  La tele descargaria 1 de 7 bundles y saldria negra, sin un solo error en el log."
    echo "  Comprobar la RUTA antes que el codigo:"
    echo "    curl -sv -m 8 https://appquarium-assets.appquarium.workers.dev/  2>&1 | tail -5"
    echo "    tracert -h 8 188.114.96.5"
    echo "  Si cloudflare.com SI responde y esas IP no, no es nuestro: es el ISP."
    exit 2
  fi
fi

# ⚠ el receiver SOBREVIVE al sender: sin este --stop, la tanda hereda su cuenta atras.
node Tools/cast-headless.js --stop --ip "$IP" >/dev/null 2>&1; sleep 6
# ⚠⚠ y parar el proceso no para lo que lanzo: contar huerfanos ANTES, no suponer.
H=$(ps -W 2>/dev/null | grep -ci "node" || true)
echo "procesos node antes de arrancar: $H" | tee -a "$ACTA"

node Tools/cast-headless.js --ip "$IP" --fish 6 --decos deco_anchor --duration "$DUR" \
  --update "change_bg=bg_classic@50" \
  --update "change_sub=sub_gravel@60" \
  --update "ambient=day@68" \
  "${ARGS[@]}" > "$LOG" 2>&1 &
S=$!; T0=$(date +%s)

# ── sincronizacion POR EVENTO, nunca por reloj ───────────────────────────────
# (el 28-ago un barrido por `sleep` encadenados dio 6 etiquetas falsas, una captura
#  llego 7 s DESPUES de acabar la sesion)
esperaN(){ local p="$1" n="$2" lim=$(( $(date +%s)+$3 )) c
  while [ "$(date +%s)" -lt "$lim" ]; do
    c=$(grep -c -- "$p" "$LOG" 2>/dev/null | head -1); c=${c:-0}
    [ "$c" -ge "$n" ] && return 0; kill -0 $S 2>/dev/null || return 1; sleep 1
  done; return 1; }

VISTOS=""
cap(){ local f="$OUT/$1.png"
  "$ADB" -s "$IP:5555" exec-out screencap -p > "$f" 2>/dev/null
  local b; b=$(stat -c %s "$f" 2>/dev/null); b=${b:-0}
  if [ "$b" -lt 50000 ]; then
    "$ADB" connect "$IP:5555" >/dev/null 2>&1; sleep 1
    "$ADB" -s "$IP:5555" exec-out screencap -p > "$f" 2>/dev/null
    b=$(stat -c %s "$f" 2>/dev/null); b=${b:-0}
  fi
  # ⚠⚠ screencap puede devolver un FOTOGRAMA CONGELADO sin dar error, con la app viva
  #    y el log sano. Dos capturas iguales a los lados de un cambio = el cambio NO llego.
  # sha256 y no md5: aquí el hash hace DOS trabajos. Uno es cazar el fotograma congelado
  # (para eso valdría md5), y el otro es ATAR EL ACTA A ESTOS PIXELES — `mide_luces.py`
  # sólo acredita [DEVICE] si cada PNG que analiza figura en el acta con su hash. Sin eso,
  # un acta vieja junto a capturas nuevas acreditaría lo que no midió.
  local m; m=$(sha256sum "$f" 2>/dev/null | cut -c1-16)
  local nota=""
  if [ -n "$VISTOS" ] && echo "$VISTOS" | grep -q "$m"; then
    nota="  !! FOTOGRAMA REPETIDO — la pantalla no cambio, captura invalida"
    mv "$f" "$OUT/REPETIDA_$1.png" 2>/dev/null
  fi
  VISTOS="$VISTOS $m"
  # el acta lleva el SEGUNDO de cada captura: si algun dia hay desfase, que salga como
  # desfase y no como dato.
  echo "$1  t=$(( $(date +%s)-T0 ))s  ${b}B  sha256=$m$nota" | tee -a "$ACTA"; }

# ⚠⚠ 2026-08-30, primera tanda real: esperar "AQUARIUM READY" A SECAS ES UN FALSO POSITIVO.
#    Cuando los bundles no llegan, la splash emite a los 90 s su red de seguridad:
#        "⚠ splash: AQUARIUM READY no llegó en 90s — se descubre la escena igual"
#    ...que CONTIENE la cadena, así que la guarda daba verde con la línea que dice justo lo
#    contrario, y el barrido siguió 4 minutos capturando una pantalla negra.
#    La línea buena es `AQUARIUM READY: <n> fish active | shaders reapuntados...` (con DOS
#    PUNTOS). 🧭 Un patrón que casa con el éxito Y con su fracaso no es una guarda.
esperaN "AQUARIUM READY:" 1 170 || { echo "NO MONTO EL ACUARIO (sin 'AQUARIUM READY:' en 170s)" | tee -a "$ACTA"
  grep -c "BDL" "$LOG" | xargs -I{} echo "  bundles anunciados: {} (de 7 esperados)" | tee -a "$ACTA"
  grep -m1 "splash: AQUARIUM READY no llegó" "$LOG" | tee -a "$ACTA"
  kill $S 2>/dev/null; exit 1; }
# prueba de artefacto: que binario corre y con que grado horneado
grep -m1 "HORNEADO:" "$LOG" | tee -a "$ACTA" || echo "!! sin linea HORNEADO — binario viejo?" | tee -a "$ACTA"
grep -m1 "RP: "       "$LOG" | tee -a "$ACTA" || true
# ⚠⚠ POR DONDE VINIERON LOS BUNDLES — pedido por la sesion del repo movil (2026-08-30).
#   Si algun dia se sirven por un camino distinto del de produccion (bucket publico,
#   dominio alternativo), la tanda sigue siendo un dato pero es UN DATO DE OTRA COSA, y
#   dentro de un mes nadie recordara que ese dia fuimos por otro sitio.
#   El discriminador es gratis: `TvBundleAuth` solo emite `AUTH:` cuando la URL contiene
#   "/bundle/", o sea cuando paso por el Worker. Si esa linea NO esta, no fue por el Worker.
if grep -m1 "AUTH: " "$LOG" | tee -a "$ACTA" | grep -q .; then :; else
  echo "!! SIN linea AUTH: los bundles NO pasaron por el Worker — ESTA TANDA NO ES DE PRODUCCION" | tee -a "$ACTA"
fi
esperaN "change_sub: " 1 90 || echo "! no llego el sustrato" | tee -a "$ACTA"

# ── 1. las 6 estaticas ───────────────────────────────────────────────────────
# ⚠ 2026-08-30: la primera tanda real gasto 4 minutos capturando una pantalla NEGRA
#   (10.754 B, 19 colores) porque los bundles no llegaron. Ahora la PRIMERA captura
#   decide: si no hay imagen, se aborta en vez de rellenar el acta de basura.
k=0
for l in "${LUCES[@]}"; do
  k=$((k+1))
  esperaN "change_light: " "$k" 60 || { echo "! no llego $l" | tee -a "$ACTA"; continue; }
  sleep 5                     # transicion = 0.7 s (TransitionTo) + margen
  cap "luz_$l"
  if [ "$k" = 1 ]; then
    B1=$(stat -c %s "$OUT/luz_$l.png" 2>/dev/null); B1=${B1:-0}
    if [ "$B1" -lt 50000 ]; then
      echo "ABORTA: la primera captura son ${B1} B — la pantalla no esta pintando el acuario." | tee -a "$ACTA"
      echo "  (mirar 'BDL n/7' en el log: si se quedo en 1, no llegaron los bundles)" | tee -a "$ACTA"
      kill $S 2>/dev/null; exit 1
    fi
  fi
done

# ── 2. light_cycle: una SERIE que cubra un periodo entero ────────────────────
# hue = Repeat(Time.time * 0.07, 1) -> periodo 1/0.07 = 14.3 s.
# 9 capturas a ~5,5 s reales (sleep 2 + ~3,5 s de screencap por red) = ~50 s, o sea ~3,5
# periodos: el recorrido queda cubierto de sobra. ⚠ Lo que hay que vigilar NO es que
# lleguen a un periodo, es que la SESION dure mas que la rafaga (ver DUR arriba).
if esperaN "change_light: " $((k+1)) 60; then
  sleep 3
  for i in 1 2 3 4 5 6 7 8 9; do cap "cycle_f$i"; sleep 2; done
else
  echo "! no llego light_cycle" | tee -a "$ACTA"
fi

esperaN "luz=" 1 40 && grep -m1 "luz=" "$LOG" | tee -a "$ACTA" || true
wait $S 2>/dev/null
# ⚠⚠ El FINAL DE SESION va AL ACTA, no solo al log: sin el, `mide_luces.py` no puede saber
#    que una captura se tomo con la app ya muerta, y una pantalla negra o el lanzador de
#    Android TV entran en la tabla como si fueran el acuario. Paso el 31-ago.
grep -E "DURACION DE SESION|DURACIÓN DE SESIÓN" "$LOG" | tail -1 | tee -a "$ACTA" || true
echo "== fin ==" | tee -a "$ACTA"
echo
echo "ahora:  python Tools/mide_luces.py --dir _luces"
