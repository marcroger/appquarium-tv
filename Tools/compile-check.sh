#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════════════
# COMPROBAR QUE EL C# COMPILA — sin Unity, sin build, en ~10 segundos.
#
#   bash Tools/compile-check.sh            # Assembly-CSharp (el runtime)
#   bash Tools/compile-check.sh Editor     # Assembly-CSharp-Editor
#
# POR QUE EXISTE (2026-08-27): hasta hoy la unica forma de saber si un cambio de C#
# compilaba era (a) mirar la Console de Unity, o (b) gastarse un build de WebGL. Y ese
# dia el Editor estaba abierto pero **atascado**: `recompile_scripts` por MCP se quedo
# 7 minutos «working» y `Library/ScriptAssemblies/Assembly-CSharp.dll` seguia siendo
# del dia anterior. O sea: la Console no decia nada porque no habia compilado nada,
# que es peor que un error — parece que va bien.
#
# COMO: Unity trae su propio Roslyn y su propio host de .NET, y el `.csproj` que genera
# para el IDE ya lleva las 308 referencias y los ~2.500 caracteres de `define`. Se
# construye un fichero de respuesta con eso y se compila a un DLL de usar y tirar.
#
# ⚠ Lo que NO comprueba: nada de runtime, ni el stripping, ni que el shader exista, ni
# que el bundle cargue. Compilar es el escalon MAS BAJO. Por encima siguen estando
# `static-server.js` + `test-updates.js`, y por encima de todo, la tele.
#
# ⚠ Depende de que el `.csproj` este al dia. Lo regenera Unity al reimportar; si acabas
# de CREAR un fichero .cs y no aparece en el csproj, esto no lo compilara y dara verde.
# Comprueba la cuenta de fuentes que imprime.
# ═══════════════════════════════════════════════════════════════════════════════
set -u
CUAL="${1:-}"
PROJ="Assembly-CSharp.csproj"
[ "$CUAL" = "Editor" ] && PROJ="Assembly-CSharp-Editor.csproj"
[ -f "$PROJ" ] || { echo "FALLO: no encuentro $PROJ (lo genera Unity al reimportar)"; exit 1; }

UNITY_DATA="/c/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor/Data"
DOTNET="$UNITY_DATA/NetCoreRuntime/dotnet.exe"
CSC_WIN='C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Data\DotNetSdkRoslyn\csc.dll'
[ -x "$DOTNET" ] || { echo "FALLO: no encuentro el dotnet de Unity en $DOTNET"; exit 1; }

# ⚠ Ruta RELATIVA a proposito. Con `/tmp/...` (ruta de MSYS) bash y el csc de Windows
# no entienden lo mismo: python la resolvia contra `D:	mp` y el csc buscaba en
# `C:/Users/…/Temp`, con un CS2011 «no puedo abrir el fichero de respuesta» que no
# tenia nada que ver con el codigo. `Temp/` ya esta en .gitignore y lo limpia Unity.
OUT="Temp/compile-check"
mkdir -p "$OUT"

python Tools/compile-check.py "$PROJ" "$OUT" || exit 1

# ⚠ Guarda: si el generador falla, `orden.txt` no existe y el bucle de abajo no itera —
# y sin esto el script terminaba con CODE=0, o sea **verde con el chequeo sin ejecutar**.
# Es el fallo que este proyecto lleva un mes persiguiendo, y se colo aqui mismo.
[ -s "$OUT/orden.txt" ] || { echo "FALLO: no se genero ningun fichero de respuesta"; exit 1; }

# ⚠ Nada de `while read … done < fichero`: el csc hereda ese stdin y se come el resto de
# la lista, asi que la segunda pasada no llegaba a ejecutarse y salia un CS2011 enganoso.
CODE=0
for NOMBRE in $(cat "$OUT/orden.txt"); do
  "$DOTNET" "$CSC_WIN" "@$OUT/$NOMBRE.rsp" < /dev/null || CODE=1
done
[ $CODE -eq 0 ] && echo "OK — $PROJ compila (0 errores)" || echo "FALLO — $PROJ NO compila"
exit $CODE
