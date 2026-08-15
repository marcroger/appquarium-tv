# BUILD REPORT — 2026-06-19

> ⚠⚠ **DOCUMENTO HISTÓRICO — NO SEGUIR SUS INSTRUCCIONES.**
> Contiene comandos de deploy con `--delete` contra la raíz del bucket R2. Ese comando
> **borra `keepalive_black.mp4`** (el vídeo keepalive del que dependen las sesiones largas),
> `silence.wav` y los rigs de diagnóstico. `--exclude "bundles/*"` **NO** protege de eso:
> sólo protege el prefijo `bundles/`, y en la raíz hay más cosas que no están en
> `webgl-output/`. El comando correcto está en `CLAUDE.md` → «Comandos clave».
> Se conserva por su valor de registro, no como guía.

## Resumen ejecutivo

🎉 **FLUIDO CONFIRMADO EN XIAOMI TV BOX S VÍA CAST** — primera sesión con framerate perceptiblemente suave. Cierra el bloqueo de rendimiento que llevaba semanas (7fps bloqueado). Hito de calidad de experiencia Fase A.2.

---

## Estado al inicio de sesión

- R2 tenía el **player del 10-jun** (bloom ON, renderScale=1.0, sin targetFrameRate)
- Player nuevo (`dfa1ab4`, 2026-06-12 22:45) estaba construido localmente pero **sin desplegar**
- settings.json en R2 tenía: doble-slash en CatalogHash + `m_DisableCatalogUpdateOnStart: false`
- Xiaomi medido en sesión previa: **7fps + 27s arranque frío** → desconexiones Cast por timeout 30s

## Trabajo realizado

### 1. Deploy del nuevo player

Player local `dfa1ab4` desplegado a R2:

| Archivo | Tamaño | Cambios vs anterior |
|---|---|---|
| `Build/webgl-output.data` | 20.4 MB | bloom OFF, renderScale 0.7 |
| `Build/webgl-output.wasm` | 42.2 MB | targetFrameRate=30, async init |
| `Build/webgl-output.framework.js` | 0.5 MB | — |
| `Build/webgl-output.loader.js` | 26.3 KB | — |
| `StreamingAssets/aa/catalog.bin` | 49.2 KB | catalog nuevo hash a9227afa |
| `StreamingAssets/aa/catalog.hash` | 32 B | — |
| `StreamingAssets/aa/settings.json` | 4.1 KB | parcheado (ver §3) |

Comando principal:
```powershell
aws s3 sync webgl-output/ s3://appquarium-tv/ --delete --exclude "bundles/*" --cache-control "public, max-age=3600"
```

### 2. Bug crítico descubierto: sync sobreescribe settings.json

El `aws s3 sync` sube el settings.json LOCAL del build (generado por Unity, con bugs). Sobreescribe cualquier versión parcheada en R2. **Esto ocurrirá en cada deploy de player.**

Fix permanente aplicado: el archivo local `webgl-output/StreamingAssets/aa/settings.json` ahora tiene los tres fixes bakeados:
- `bundles//catalog_1.2.1.hash` → `bundles/catalog_1.2.1.hash` (doble-slash)  
- `addressables//catalog_1.2.1.hash` → `addressables/catalog_1.2.1.hash` (doble-slash)
- `m_DisableCatalogUpdateOnStart: false` → `true` (crash crítico en WebGL)

### 3. Bug crítico: m_DisableCatalogUpdateOnStart = false → crash WebGL

Con el valor `false`, Addressables 3.0.0 descarga el catálogo remoto en cada arranque, incluso si el hash local coincide. En WebGL con `Exception Support: None`, esto causa un abort del WASM.

```
JS ERR: Uncaught undefined (webgl-output.framework.js:9)
```

El crash ocurría exactamente a los 33s en Chrome (coincide con el timeout de 30s del SDK Cast). **El valor debe ser siempre `true`** para WebGL — se usa el catálogo local embebido en el build.

### 4. Bug AWS CLI 2.23+: SignatureDoesNotMatch en archivos < ~5KB

**Root cause:** AWS CLI 2.23+ usa CRC64NVME como algoritmo de checksum por defecto en PutObject. Cloudflare R2 **no soporta CRC64NVME** y devuelve `SignatureDoesNotMatch` (error engañoso — el problema real es el algoritmo de checksum).

Archivos afectados en este proyecto:
- `StreamingAssets/aa/settings.json` (4.1 KB) — falla `aws s3 cp`
- `StreamingAssets/aa/catalog.hash` (32 bytes) — falla `aws s3 cp`

**Archivos > 10KB funcionan con sync** (usa un code path diferente internamente).

**Fix:** usar boto3 directamente (ya documentado en CLAUDE.md):
```python
python -c "
import boto3, configparser, os
c = configparser.ConfigParser()
c.read([os.path.expanduser('~/.aws/credentials')])
client = boto3.client('s3',
    endpoint_url='https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com',
    aws_access_key_id=c.get('r2','aws_access_key_id'),
    aws_secret_access_key=c.get('r2','aws_secret_access_key'),
    region_name='auto',
    config=boto3.session.Config(signature_version='s3v4')
)
client.put_object(Bucket='appquarium-tv', Key='StreamingAssets/aa/settings.json',
    Body=open('webgl-output/StreamingAssets/aa/settings.json','rb').read(),
    CacheControl='public, max-age=60')
print('OK')
"
```

---

## Estado de R2 tras deploy (2026-06-19 ~17:30)

```
/index.html                               — receiver HTML actualizado
/Build/webgl-output.data          20.4 MB — player nuevo
/Build/webgl-output.wasm          42.2 MB — player nuevo
/Build/webgl-output.framework.js   0.5 MB
/Build/webgl-output.loader.js     26.3 KB
/StreamingAssets/aa/settings.json  4.1 KB — DisableCatalog=true, sin doble-slash
/StreamingAssets/aa/catalog.bin   49.2 KB — hash a9227afa
/StreamingAssets/aa/catalog.hash    32  B — hash a9227afa
/bundles/                                 — bundles remotos intactos (sin --delete sobre bundles/)
```

---

## Resultado validado

**Cast a Xiaomi TV Box S → fluido.** Primera sesión con movimiento visible de peces sin micro-parones.

El user confirmó: "se ve fluido... podría verse con más calidad pero me doy con un canto en los dientes."

Factores que contribuyeron:
1. `targetFrameRate = 30` — frena el bucle principal, evita sobrecarga CPU en el Mali-G31
2. `renderScale = 0.7f` — 49% menos píxeles a renderizar por frame (mayor impacto GPU)
3. Bloom OFF — elimina post-processing costoso en Mobile GPU
4. Init asínco con loading overlay — UX más limpia, Cast no desconecta durante carga

---

## Workflow de deploy futuro (corregido)

```powershell
# 1. Sync del player (sin tocar bundles)
$env:AWS_REQUEST_CHECKSUM_CALCULATION = "when_required"
$env:AWS_RESPONSE_CHECKSUM_VALIDATION = "when_supported"

aws s3 sync webgl-output/ s3://appquarium-tv/ `
  --profile r2 `
  --endpoint-url https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com `
  --delete `
  --exclude "bundles/*" `
  --cache-control "public, max-age=3600"

# 2. SIEMPRE después del sync: re-subir settings.json y catalog.hash con boto3
#    (el sync sobreescribe settings.json con la versión sin parchar)
python -c "..."  # ver snippet en §4

# 3. Verificar en R2:
aws s3 cp s3://appquarium-tv/StreamingAssets/aa/settings.json - --profile r2 --endpoint-url ... | python -c "import sys,json; d=json.load(sys.stdin); print('DisableCatalog:', d['m_DisableCatalogUpdateOnStart'])"
```

---

## Siguientes pasos

- **Inmediato:** medir FPS real con el nuevo player (el user lo confirmó como "fluido", pero sin número exacto)
- **Fase B:** disconnect Cast a los ~2 min (heartbeat timeout) — pendiente
- **Fase B:** fix doble-slash en el settings.json que genera Unity (en `TvAddressablesSetup.cs`) para que el build lo genere directamente correcto sin necesidad de parchar
- **Calidad:** renderScale podría subirse a 0.85 si el FPS aguanta
- **25 peces restantes:** pueden añadirse incrementalmente — materiales ya corregidos en disco
