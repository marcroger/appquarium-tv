#!/usr/bin/env bash
# ¿Cuanto cambia una luz si le subes el `spotIntensity`? (2026-08-31)
#
# POR QUE EXISTE: hasta hoy la LUZ era lo unico que no se podia barrer en caliente — el grado
# tiene `GRADE` y la niebla tiene `FOG`, pero elegir un `spotIntensity` costaba UN BUILD DE
# 55 MIN POR VARIANTE, y por eso esos numeros nunca se habian medido en cinco meses. El
# mensaje `LUZ` (build del 31-ago) lo arregla, y este script lo usa.
#
# El hallazgo que lo motiva: la luz que un spot ENTREGA es `luminancia(color) x spotIntensity`,
# y `light_purple` lleva el 3.5 mas alto de la tabla entregando 2,6 VECES MENOS luz que
# `light_warm`, que pone 3.2. Aqui se ve en pantalla lo que cuesta igualarlos.
#
# ⚠ Se deja `light_deep` FUERA a proposito: es el preset donde el modelo peor casa (entrega la
#   menor luz de todas y mide el tercer ILUM mas alto, porque sus palancas GLOBALES son las
#   extremas) y donde la tabla pediria el factor mayor. Ver CAST_PARIDAD_VISUAL.md 0.7.4.
set -u
IP="${1:-}"
[ -z "$IP" ] && { echo "uso: $0 <IP-de-la-tele>"; exit 1; }
ADB="/c/Users/Behere/AppData/Local/Android/Sdk/platform-tools/adb.exe"
cd "$(dirname "$0")/.."
OUT="$(pwd)/_spot"; mkdir -p "$OUT"
LOG="$OUT/sender.log"; ACTA="$OUT/acta.txt"
rm -f "$LOG" "$ACTA" "$OUT"/*.png

# ⚠⚠ PREFLIGHT DE RUTA — 8 s contra 5 min tirados. Si el Worker no responde, la tele se queda
#    en `BDL 1/7` SIN UN SOLO ERROR y sale negra.
RC=$(curl -s -o /dev/null -m 8 -w '%{http_code}' "https://appquarium-assets.appquarium.workers.dev/bundle/x" 2>/dev/null)
if [ "${RC:-000}" = "000" ]; then
  echo "ABORTA: el Worker de los bundles no tiene ruta (HTTP 000). La tanda saldria negra."; exit 1
fi
echo "preflight ruta: HTTP $RC (401 = el Worker vivo y rechazando; correcto)" | tee -a "$ACTA"

# ── el guion ────────────────────────────────────────────────────────────────
# ⚠⚠ La ventana de capturas TIENE que caber dentro de la sesion. El 31-ago `barre-luces.sh`
#    daba 45 s a una rafaga de ~50 y las dos ultimas capturas fueron la pantalla de apagado y
#    el lanzador de Android TV — y entraron en la tabla sin dar ningun error.
# ⚠ La segunda mitad mide LAS SOMBRAS DE LOS PECES con bloom ON y OFF. El user reporto el
#   31-ago que «se ven menos». El bloom paso de OFF a 0.30 con umbral 0.92->0.60 el 28-ago, y
#   un bloom con umbral bajo sobre un suelo claro DERRAMA luz sobre los rasgos oscuros
#   pequenos, que es justo lo que es la sombra de un pez. Se comprueba en caliente con GRADE.
T_BLUE=75; T_BLUE_UP=105; T_PURP=135; T_PURP_UP=165
T_LUZBASE=195; T_BLOOM_OFF=215; DUR=300
ARGS=(--update "change_light=light_blue@$T_BLUE"
      --raw    "LUZ={\"spotIntensity\":6.4}@$T_BLUE_UP"
      --update "change_light=light_purple@$T_PURP"
      --raw    "LUZ={\"spotIntensity\":9.0}@$T_PURP_UP"
      --raw    "LUZ={\"reset\":true}@$T_LUZBASE"
      --update "change_light=light_white@$T_LUZBASE"
      --raw    "GRADE={\"bloom\":false}@$T_BLOOM_OFF"
      --raw    "GRADE={\"bloom\":true}@$((T_BLOOM_OFF+35))"
      --update "dump=@$((T_BLOOM_OFF+55))")

echo "procesos node antes de arrancar: $(ps -W 2>/dev/null | grep -c node.exe)" | tee -a "$ACTA"
node Tools/cast-headless.js --ip "$IP" --fish 6 --decos deco_anchor --duration "$DUR" \
     --update "change_bg=bg_classic@45" --update "change_sub=sub_gravel@55" \
     --update "ambient=day@65" "${ARGS[@]}" > "$LOG" 2>&1 &
S=$!

T0=$(date +%s)
ahora(){ echo $(( $(date +%s) - T0 )); }
VISTOS=""
cap(){ local f="$OUT/$1.png"
  "$ADB" -s "$IP:5555" exec-out screencap -p > "$f" 2>/dev/null
  local b; b=$(stat -c %s "$f" 2>/dev/null); b=${b:-0}
  local m; m=$(sha256sum "$f" 2>/dev/null | cut -c1-16)
  local nota=""
  # ⚠⚠ screencap devuelve FOTOGRAMAS CONGELADOS sin dar error, con la app viva y el log sano.
  echo "$VISTOS" | grep -q "$m" && nota="  !! FOTOGRAMA REPETIDO — el cambio NO llego"
  VISTOS="$VISTOS $m"
  echo "$1  t=$(ahora)s  ${b}B  sha256=$m$nota" | tee -a "$ACTA"
}
# ⚠⚠ `espera` MIRA SOLO DE LA MARCA EN ADELANTE. La primera version grepeaba TODO el log
#    acumulado, y eso da falsos positivos con lineas VIEJAS: el patron
#    `change_light:.*light_white` caso con `change_light: light_white -> light_blue` del minuto
#    anterior, la captura salio disparada al instante y la comparacion de sombras se hizo entre
#    DOS LUCES DISTINTAS. La medida parecia perfecta (P50 49.4 vs 64.7) y no medía el bloom.
# 🧭 Es EXACTAMENTE el bug que `Tools/test-updates.js` ya arreglo el 27-ago con `desde()` —
#    escrito en el CLAUDE.md— y que no porte aqui. Escribir la regla no la ejecuta.
MARCA=0
desde(){ MARCA=$(wc -l < "$LOG" 2>/dev/null || echo 0); }
espera(){ # espera a que $1 aparezca DESPUES de la marca, hasta $2 s
  local i=0
  while [ $i -lt "$2" ]; do
    tail -n +$((MARCA+1)) "$LOG" 2>/dev/null | grep -q "$1" && return 0
    sleep 1; i=$((i+1))
  done; return 1; }

# ⚠ La guarda pide `AQUARIUM READY:` CON DOS PUNTOS. Sin ellos casa tambien con la linea de
#   FRACASO de la splash («AQUARIUM READY no llego en 90s»), que da VERDE diciendo lo contrario.
if ! espera "AQUARIUM READY:" 120; then
  echo "ABORTA: el acuario no monto en 120 s." | tee -a "$ACTA"; kill $S 2>/dev/null; exit 1
fi
grep -m1 "HORNEADO:" "$LOG" | tee -a "$ACTA"

# ⚠⚠ La prueba de que corre el BUILD NUEVO: la linea `LUZ:` no existe en el player anterior.
#    Si no aparece, el device esta sirviendo el .wasm cacheado (Build/* va con max-age=3600).
# ⚠⚠ EL PATRON. El receiver escribe `change_light: light_white -> light_blue`, NO
#    `change_light: light_blue`. La primera version buscaba lo segundo, no casaba, y el `&&`
#    SE SALTABA LA CAPTURA SIN DAR NINGUN ERROR: la tanda salio con las dos variantes y sin
#    ninguna de las dos referencias, que es justo lo que habia que comparar.
#    ⚠⚠ Y el segundo intento fallo IGUAL: puse "> light_blue" y el receiver escribe una
#    FLECHA UNICODE (→), no ">". Dos intentos, dos patrones que no casaban, cero errores.
#    🧭 Casar contra `change_light:.*<id>`, que no depende de como se pinte la flecha.
paso(){ # $1 = patron  $2 = segundos  $3 = nombre de la captura
  # ⚠ La marca se pone AL EMPEZAR a esperar, y AQUI DENTRO: si hubiera que acordarse
  #   de ponerla en cada llamada, el dia que se olvide ese paso volveria a mirar el
  #   log entero y el falso positivo seria silencioso otra vez.
  desde
  if espera "$1" "$2"; then sleep 4; cap "$3"
  else echo "!! FALTA LA CAPTURA '$3': no llego «$1» en $2 s" | tee -a "$ACTA"; FALTAN=$((FALTAN+1)); fi
}
FALTAN=0
paso "change_light:.*light_blue"   60 "1_blue_3.5"
# ⚠⚠ La prueba de que corre el BUILD NUEVO: la linea `LUZ:` no existe en el player anterior.
#    Si no aparece, el device sirve el .wasm cacheado (Build/* va con max-age=3600).
if ! espera "LUZ: " 60; then
  echo "ABORTA: no llego la linea LUZ: el device corre el BUILD VIEJO (cache)." | tee -a "$ACTA"
  kill $S 2>/dev/null; exit 1
fi
grep -m1 "LUZ: " "$LOG" | tee -a "$ACTA"
sleep 4; cap "2_blue_6.4"
paso "change_light:.*light_purple"  60 "3_purple_3.5"
paso "LUZ: light_purple" 60 "4_purple_9.0"
paso "change_light:.*light_white" 60 "5_sombras_bloomON"
paso "GRADE: bloom=OFF"           60 "6_sombras_bloomOFF"
grep -E "LUZ: |GRADE: bloom" "$LOG" | tee -a "$ACTA"

espera "luz=" 60 && grep -m1 "luz=" "$LOG" | tee -a "$ACTA"
wait $S 2>/dev/null
# ⚠⚠ El FIN DE SESION va AL ACTA: sin el, el analizador no puede saber que una captura se tomo
#    con la app ya muerta, y una pantalla negra entra en la tabla como si fuera el acuario.
grep -E "DURACION DE SESION|DURACIÓN DE SESIÓN" "$LOG" | tail -1 | tee -a "$ACTA"
echo "== fin ==" | tee -a "$ACTA"
# ⚠ Que la tanda FALLE si falta alguna captura, en vez de dejar un directorio a medias que
#   parece bueno. Una comparacion sin su referencia no es media comparacion: es ninguna.
[ "${FALTAN:-0}" -gt 0 ] && { echo "!! LA TANDA NO VALE: faltan $FALTAN capturas." | tee -a "$ACTA"; exit 1; }
echo "las 4 capturas estan." | tee -a "$ACTA"
