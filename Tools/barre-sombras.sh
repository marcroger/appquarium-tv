#!/usr/bin/env bash
# ¿El BLOOM se come las sombras de los peces? (2026-08-31)
#
# El user reporto mirando la tele: «cuidado con las sombras de los peces que no se por que ahora
# se ven menos… no mucho, pero lo he notado». Hipotesis: el bloom paso de OFF a 0.30 con umbral
# 0.92 -> 0.60 el 28-ago, y un bloom de umbral bajo sobre un suelo claro DERRAMA luz sobre los
# rasgos oscuros pequenos — que es exactamente lo que es la sombra de un pez.
# Se comprueba EN CALIENTE con `GRADE`, sin gastar un build.
#
# ⚠⚠ TODO se deja FIJO menos el bloom: `light_white`, `bg_classic`, `sub_gravel`, `ambient=day`.
#    El primer intento de esta medida comparo bloom ON con luz MORADA contra bloom OFF con luz
#    BLANCA, porque el patron de espera caso con una linea vieja del log. Salio un numero
#    perfectamente formado (P50 49.4 contra 64.7) que no medía el bloom.
# ⚠⚠ Y se vuelve a encender al final: si el efecto es reversible, es el bloom; si no, es otra cosa.
set -u
IP="${1:-}"
[ -z "$IP" ] && { echo "uso: $0 <IP-de-la-tele>"; exit 1; }
ADB="/c/Users/Behere/AppData/Local/Android/Sdk/platform-tools/adb.exe"
cd "$(dirname "$0")/.."
OUT="$(pwd)/_sombras"; mkdir -p "$OUT"
LOG="$OUT/sender.log"; ACTA="$OUT/acta.txt"
rm -f "$LOG" "$ACTA" "$OUT"/*.png

RC=$(curl -s -o /dev/null -m 8 -w '%{http_code}' "https://appquarium-assets.appquarium.workers.dev/bundle/x" 2>/dev/null)
[ "${RC:-000}" = "000" ] && { echo "ABORTA: el Worker no tiene ruta. La tanda saldria negra."; exit 1; }
echo "preflight ruta: HTTP $RC" | tee -a "$ACTA"
echo "procesos node antes de arrancar: $(ps -W 2>/dev/null | grep -c node.exe)" | tee -a "$ACTA"

node Tools/cast-headless.js --ip "$IP" --fish 8 --decos deco_anchor --duration 210 \
     --update "change_bg=bg_classic@45" --update "change_sub=sub_gravel@55" \
     --update "ambient=day@65"  --update "change_light=light_white@75" \
     --raw "GRADE={\"bloom\":false}@110" --raw "GRADE={\"bloom\":true}@150" \
     --update "dump=@180" > "$LOG" 2>&1 &
S=$!

T0=$(date +%s); ahora(){ echo $(( $(date +%s) - T0 )); }
VISTOS=""
cap(){ local f="$OUT/$1.png"
  "$ADB" -s "$IP:5555" exec-out screencap -p > "$f" 2>/dev/null
  local b; b=$(stat -c %s "$f" 2>/dev/null); b=${b:-0}
  # ⚠⚠ adb puede devolver 0 BYTES sin dar error si se cae la conexion — paso el 31-ago y las
  #    dos capturas salieron vacias con el mismo sha256 del fichero vacio. `barre-luces.sh` ya
  #    tenia este reintento y al escribir este script lo olvide. Escribir la regla no la ejecuta.
  if [ "$b" -lt 50000 ]; then
    "$ADB" connect "$IP:5555" >/dev/null 2>&1; sleep 2
    "$ADB" -s "$IP:5555" exec-out screencap -p > "$f" 2>/dev/null
    b=$(stat -c %s "$f" 2>/dev/null); b=${b:-0}
  fi
  if [ "$b" -lt 50000 ]; then
    echo "$1  !! CAPTURA VACIA (${b}B) — adb no responde" | tee -a "$ACTA"; FALTAN=$((FALTAN+1)); return
  fi
  local m; m=$(sha256sum "$f" 2>/dev/null | cut -c1-16); local nota=""
  echo "$VISTOS" | grep -q "$m" && nota="  !! FOTOGRAMA REPETIDO — el cambio NO llego"
  VISTOS="$VISTOS $m"
  echo "$1  t=$(ahora)s  ${b}B  sha256=$m$nota" | tee -a "$ACTA"
}
# ⚠⚠ Mira SOLO desde la marca. Grepear el log entero da falsos positivos con lineas viejas —
#    es el bug que `test-updates.js` arreglo el 27-ago con `desde()` y que costo esta medida.
MARCA=0
desde(){ MARCA=$(wc -l < "$LOG" 2>/dev/null || echo 0); }
espera(){ local i=0
  while [ $i -lt "$2" ]; do
    tail -n +$((MARCA+1)) "$LOG" 2>/dev/null | grep -q "$1" && return 0
    sleep 1; i=$((i+1)); done; return 1; }
FALTAN=0
paso(){ desde
  if espera "$1" "$2"; then sleep 5; cap "$3"
  else echo "!! FALTA '$3': no llego «$1» en $2 s" | tee -a "$ACTA"; FALTAN=$((FALTAN+1)); fi
}

espera "AQUARIUM READY:" 120 || { echo "ABORTA: el acuario no monto." | tee -a "$ACTA"; kill $S 2>/dev/null; exit 1; }
grep -m1 "HORNEADO:" "$LOG" | tee -a "$ACTA"

# ⚠ La referencia NO puede esperar una linea `GRADE`: al arrancar no se manda ninguna,
#   el bloom viene HORNEADO. Se espera a que la luz se asiente y se captura.
paso "change_light:.*light_white" 60 "1_bloomON_a"   # el bloom desplegado (0.30)
paso "GRADE: bloom=OFF" 60 "2_bloomOFF"
paso "GRADE: bloom=0"   60 "3_bloomON_b"     # vuelta atras: ¿es reversible?
grep -E "GRADE: bloom" "$LOG" | tee -a "$ACTA"
espera "luz=" 60 && grep -m1 "luz=" "$LOG" | tee -a "$ACTA"
wait $S 2>/dev/null
grep -E "DURACION DE SESION|DURACIÓN DE SESIÓN" "$LOG" | tail -1 | tee -a "$ACTA"
echo "== fin ==" | tee -a "$ACTA"
[ "$FALTAN" -gt 0 ] && { echo "!! LA TANDA NO VALE: faltan $FALTAN capturas." | tee -a "$ACTA"; exit 1; }
echo "las 3 capturas estan." | tee -a "$ACTA"
