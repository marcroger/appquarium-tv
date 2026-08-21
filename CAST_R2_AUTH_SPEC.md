# Cast R2 Auth — Spec ejecutable

> ## ⚠⚠ LEER ESTO ANTES QUE NADA (2026-08-20)
>
> **La Fase 1 YA ESTÁ IMPLEMENTADA Y DESPLEGADA**, pero **NO como dice este documento**: dos de
> sus decisiones de diseño son erróneas y se descubrió al construirlo.
>
> 1. **El `302` a R2 (§3, §5) NO PUEDE FUNCIONAR.** El receiver pide los bundles cross-origin y
>    con header `Authorization` → request **con preflight**, y el spec de Fetch **prohíbe seguir
>    un redirect cross-origin** en ese caso. El Worker **sirve los bytes** (binding R2). Efecto
>    secundario bueno: así el bucket puede ser **privado**, en vez de «público pero oculto».
> 2. **«R2 sigue público» (§3) era innecesario y peor.** Los bundles están hoy en un bucket
>    **privado sin dominio público**.
>
> Lo que SÍ sigue siendo válido de este doc: el threat model (§2), la estructura del JWT (§4),
> el trabajo del móvil (§6), el coste (§9) y los riesgos (§10).
>
> 📄 **Lo realmente construido:** `Tools/r2-auth-worker/README.md` ·
> **contrato de la Fase 2:** `CAST_R2_AUTH_MOVIL.md` · **cierre:** `CAST_NEXT_SESSION_2026-08-21.md`

**Estado:** 2026-05-26 — diseño aprobado. **Fase 1 ejecutada el 2026-08-20 con las correcciones de arriba.**
**Pre-requisito:** Cast Fase A.1 funcional (bundles cargando desde R2 vía TvSceneBootstrap).
**Doc relacionada:** [`CAST_NETFLIX_SPEC.md`](CAST_NETFLIX_SPEC.md) — la arquitectura base.

---

## 1. Por qué

Los bundles de Addressables viven en `https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev/bundles/*.bundle` — **URL pública sin auth**. Esto es problema doble:

1. **Revenue leak.** Premium €25 desbloquea fish + decos. Sin auth, cualquiera con la URL puede `curl` los 92 bundles y montar el contenido completo gratis. Premium se vuelve opcional.

2. **License leak (más grave).** Pack 24 de Mikhail Nesterov (Unity Asset Store), Sketchfab assets no-CC0, etc. tienen licencias que prohíben redistribuir assets crudos. Bundles públicos extraibles con AssetStudio = redistribución sin auth = violación de TOS. Riesgo: takedowns, baneo Asset Store, en extremo demanda.

**Objetivo:** seguridad MÍNIMA viable que pare scrapers casuales + cumpla con licencias. No over-engineering.

---

## 2. Threat model

| Ataque | Impacto | Mitigación de esta spec |
|---|---|---|
| Curl ciego a URL bundle | Alto (descarga gratis todo) | ✅ Worker rechaza sin JWT válido |
| Compartir JWT entre 2 users | Medio | ✅ JWT contiene `ownedItems[]` del user emisor (no transferible útilmente) |
| Decompile APK + reverse-engineer JWT signing | Medio | ✅ Firma SOLO en server (Cloudflare Worker), no en cliente |
| Atacante dedicado robando cookie de sesión activa | Bajo | ⚠ No cubierto (fuera de scope MVP) |
| MITM en red insegura | Bajo | ✅ R2 ya es HTTPS, Worker también |

---

## 3. Arquitectura

```
┌─────────────────────────────────────────────────────────────────┐
│  Mobile (Android)                                                │
│  ───────────────                                                 │
│  IAP Premium / pack / fish → success                             │
│       │                                                          │
│       ▼                                                          │
│  Llama a Worker /mint-token con purchaseToken Google Play        │
│       │                                                          │
│       ▼                                                          │
│  Recibe JWT firmado (24h expiry) con:                            │
│    {userId, isPremium, ownedSpecies[], ownedDecoIds[],           │
│     ownedPackIds[], exp}                                         │
│       │                                                          │
│       ▼                                                          │
│  Guarda JWT en SaveData (string)                                 │
│       │                                                          │
│       ▼                                                          │
│  Al iniciar Cast: envía JWT a TV vía Cast INIT message           │
│  (campo nuevo en TvAquariumState)                                │
└────────────────────────────────┬────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│  TV WebGL Receiver                                               │
│  ─────────────────                                               │
│  Cast INIT → guarda JWT en variable estática                     │
│       │                                                          │
│       ▼                                                          │
│  Addressables.WebRequestOverride += AddAuthHeader                │
│  → cada bundle request lleva Authorization: Bearer <JWT>         │
│       │                                                          │
│       ▼                                                          │
│  Request va a https://auth.appquarium-tv.workers.dev/bundle/<X>  │
│  (no a R2 directo)                                               │
└────────────────────────────────┬────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│  Cloudflare Worker (auth.appquarium-tv.workers.dev)              │
│  ──────────────────────────                                      │
│  1. Lee Authorization header → extrae JWT                        │
│  2. Verifica firma con clave pública (HS256 simétrica)           │
│  3. Verifica exp no caducado                                     │
│  4. Extrae itemId de la URL: /bundle/fish_mandarinfish           │
│  5. Chequea: isPremium == true OR itemId in (ownedSpecies        │
│              ∪ ownedDecoIds ∪ pack_content_free)                 │
│  6. Si OK → 302 redirect a R2 URL real con cache 30s             │
│  7. Si NO → 403 Forbidden                                        │
└────────────────────────────────┬────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│  Cloudflare R2 (pub-2b11cc17....r2.dev/bundles/)                 │
│  ─────────────                                                   │
│  Sirve el bundle. Sigue siendo pública porque el redirect 302   │
│  es de corta duración (30s). Atacantes que pillen la URL viva    │
│  tienen 30s para descargar antes de que expire.                  │
└─────────────────────────────────────────────────────────────────┘
```

**Nota sobre R2 público:** mantenerlo público simplifica enormemente (no signed URLs, no rotating keys). El Worker actúa como portero: nada llega al cliente sin pasar primero por él. La URL R2 real queda "oculta" detrás del redirect y solo expone 30s.

---

## 4. JWT structure

### Algoritmo
**HS256 (HMAC-SHA256)** — clave simétrica, simple, suficiente para nuestro caso.
- Una sola clave secreta, vive en Cloudflare Worker (variable de entorno cifrada)
- NO embedded en el APK ni en WebGL build
- Worker es el único que puede firmar y validar

### Claims
```json
{
  "userId": "uuid-v4-anonimo",
  "isPremium": false,
  "ownedSpecies": ["fish_mandarinfish", "fish_boxfish_yellow"],
  "ownedDecoIds": ["deco_anchor_0", "deco_coral_pocillopora_1"],
  "ownedPackIds": ["pack_decos_marine"],
  "iat": 1716728400,
  "exp": 1716814800
}
```

### TTL
- **Validez: 24h** (`exp = iat + 86400`)
- Refresh:
  - Auto-mint al abrir la app si caducó (transparente al user)
  - Re-mint inmediato tras IAP exitosa (para incluir el nuevo ítem en claims)

### Anonimato
- `userId` es UUID generado en el dispositivo en primer arranque, persistido en SaveData
- NO se asocia a email, Google ID, ni info personal
- GDPR friendly — anónimo per definición

---

## 5. Cloudflare Worker — implementación

### Setup
- Cuenta Cloudflare (ya tienes — R2 está ahí)
- Workers free tier: 100.000 requests/día (sobra para early stage, ~3K users de caudal)
- Custom domain opcional: `auth.appquarium-tv.workers.dev` (free) o `auth.appquarium-tv.com` (requiere dominio propio)

### Variables de entorno (Workers secrets)
- `JWT_SECRET` — clave HMAC compartida con backend de mint
- `R2_PUBLIC_BASE` — `https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev/bundles/`
- `FREE_TIER_IDS` — comma-separated lista de itemIds en `pack_content_free` (no requieren auth)

### Código (TypeScript, ~80 líneas)

```typescript
// worker.ts
import { verify } from '@tsndr/cloudflare-worker-jwt'; // npm: lightweight JWT

interface JwtPayload {
  userId: string;
  isPremium: boolean;
  ownedSpecies: string[];
  ownedDecoIds: string[];
  ownedPackIds: string[];
  exp: number;
}

export default {
  async fetch(req: Request, env: Env): Promise<Response> {
    const url = new URL(req.url);

    // Solo procesar /bundle/<itemId>
    const match = url.pathname.match(/^\/bundle\/(.+)$/);
    if (!match) return new Response('Not found', { status: 404 });

    const itemId = match[1].replace(/\.bundle$/, ''); // normalize

    // Free tier: no auth needed
    const freeIds = (env.FREE_TIER_IDS || '').split(',');
    if (freeIds.includes(itemId)) {
      return Response.redirect(`${env.R2_PUBLIC_BASE}${match[1]}`, 302);
    }

    // JWT required
    const auth = req.headers.get('Authorization');
    if (!auth || !auth.startsWith('Bearer ')) {
      return new Response('Missing auth', { status: 401 });
    }
    const token = auth.substring(7);

    const valid = await verify(token, env.JWT_SECRET, { algorithm: 'HS256' });
    if (!valid) return new Response('Invalid token', { status: 403 });

    const payload = JSON.parse(atob(token.split('.')[1])) as JwtPayload;
    if (payload.exp < Date.now() / 1000) {
      return new Response('Expired', { status: 401 });
    }

    // Ownership check
    const owns =
      payload.isPremium ||
      payload.ownedSpecies.includes(itemId) ||
      payload.ownedDecoIds.some(d => d.startsWith(itemId)) || // instance suffix
      payload.ownedPackIds.length > 0; // simplificado: si tiene cualquier pack, deja pasar todo

    if (!owns) return new Response('Not owned', { status: 403 });

    // Redirect to R2 with short cache
    return Response.redirect(`${env.R2_PUBLIC_BASE}${match[1]}`, 302);
  }
};
```

### Endpoint /mint-token (mismo Worker, otra ruta)

```typescript
// Añadir antes del /bundle/ match
if (url.pathname === '/mint-token' && req.method === 'POST') {
  const body = await req.json() as MintRequest;

  // TODO opcional: verificar purchaseToken con Google Play API
  // Para MVP: confiamos en que el APK no esté tampered (poco realista pero acelera)

  const now = Math.floor(Date.now() / 1000);
  const payload: JwtPayload = {
    userId: body.userId,
    isPremium: body.isPremium,
    ownedSpecies: body.ownedSpecies || [],
    ownedDecoIds: body.ownedDecoIds || [],
    ownedPackIds: body.ownedPackIds || [],
    iat: now,
    exp: now + 86400
  };

  const token = await sign(payload, env.JWT_SECRET, { algorithm: 'HS256' });
  return new Response(JSON.stringify({ token }), {
    headers: { 'Content-Type': 'application/json' }
  });
}
```

**Mejora futura (no MVP):** validar `purchaseToken` con Google Play Developer API para confirmar que la compra es legítima. Requiere setup de Service Account JSON. Por ahora confiamos en el APK no-tampered.

---

## 6. Integración mobile (sender)

### Cambios en `D:\dev\appquarium-unity\`

**1. Generar userId si no existe** (`SaveSystem.cs`)
```csharp
if (string.IsNullOrEmpty(data.userId)) {
    data.userId = System.Guid.NewGuid().ToString();
    SaveSystem.Save(data);
}
```

**2. Llamar /mint-token tras IAP exitoso** (`IAPService.cs`)
```csharp
public static async Task RefreshJwtAfterPurchase(SaveData data) {
    var req = new {
        userId      = data.userId,
        isPremium   = data.isPremium,
        ownedSpecies= data.ownedSpeciesIds.ToList(),
        ownedDecoIds= data.ownedDecoIds.ToList(),
        ownedPackIds= data.ownedPackIds.ToList()
    };

    var response = await UnityWebRequest.PostJson(
        "https://auth.appquarium-tv.workers.dev/mint-token",
        JsonUtility.ToJson(req));

    var result = JsonUtility.FromJson<MintResponse>(response.downloadHandler.text);
    data.castJwt = result.token;
    SaveSystem.Save(data);
}
```

**3. Auto-refresh al abrir app si JWT caducó** (`AquariumManager.Start`)
```csharp
if (!string.IsNullOrEmpty(data.castJwt) && IsJwtExpired(data.castJwt)) {
    await IAPService.RefreshJwtAfterPurchase(data);
}
```

**4. Enviar JWT al TV vía Cast** (`CastManager.cs`)
Modificar el `TvAquariumState` para incluir nuevo campo:
```csharp
[Serializable]
public class TvAquariumState {
    // ... campos existentes
    public string castJwt; // NUEVO
}
```

Y al construir el state:
```csharp
state.castJwt = data.castJwt;
```

### Archivos a tocar en mobile:
- `Assets/Scripts/Utils/SaveSystem.cs` — añadir `userId` + `castJwt` fields
- `Assets/Scripts/Utils/IAPService.cs` — añadir `RefreshJwtAfterPurchase`
- `Assets/Scripts/Core/AquariumManager.cs` — auto-refresh en Start
- `Assets/Scripts/Core/CastManager.cs` — añadir `castJwt` al state JSON

---

## 7. Integración TV (receiver)

### Cambios en `D:\dev\appquarium-tv-unity\`

**1. Añadir campo `castJwt` a `CastDataTypes.TvAquariumState`** (debe coincidir con mobile)
```csharp
[Serializable]
public class TvAquariumState {
    // ... campos existentes
    public string castJwt;
}
```

**2. Guardar JWT al recibir INIT** (`TvSceneBootstrap.cs`)
```csharp
private static string _jwt;

public void InitializeFromState(TvAquariumState state) {
    if (state == null) return;
    _jwt = state.castJwt; // guardar para uso posterior
    StartCoroutine(LoadAndInitializeCoroutine(state));
}
```

**3. Subscribir al WebRequestOverride de Addressables** (Awake o Start)
```csharp
void Awake() {
    // ... existing code

    Addressables.WebRequestOverride = OverrideWebRequest;
}

private void OverrideWebRequest(UnityWebRequest request) {
    if (string.IsNullOrEmpty(_jwt)) return;
    if (!request.url.Contains("/bundle/")) return;
    request.SetRequestHeader("Authorization", "Bearer " + _jwt);
}
```

**4. Cambiar la BaseURL de Addressables** (en Addressables Profile o ProfileSettings)

De:
```
https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev/bundles/
```

A:
```
https://auth.appquarium-tv.workers.dev/bundle/
```

(Sí, sin `s` final en "bundle". Worker hace match `/bundle/<filename>`.)

Esto se hace en `AddressableAssetSettings.asset` → profile activo → `Remote.LoadPath`. NO requiere rebuild de bundles, solo del catalog.

### Archivos a tocar en TV:
- `Assets/Scripts/Core/CastDataTypes.cs` — añadir `castJwt` a `TvAquariumState`
- `Assets/Scripts/Core/TvSceneBootstrap.cs` — guardar JWT + suscribir override
- `Assets/AddressableAssetsData/AddressableAssetSettings.asset` — cambiar profile RemoteLoadPath
- `Assets/AddressableAssetsData/AssetGroups/*.asset` — verificar que herredan del profile

---

## 8. Implementación — orden recomendado

| Paso | Quién | Coste | Cuándo |
|---|---|---|---|
| 1. Crear Worker en Cloudflare (template) | TV agent | 2-3h | Post Fase A.1 |
| 2. Deploy Worker con FREE_TIER_IDS de pack_content_free | TV agent | 30 min | Mismo día |
| 3. Test manual: curl con JWT válido vs sin JWT | TV agent | 30 min | Mismo día |
| 4. Cambiar Addressables RemoteLoadPath en TV | TV agent | 15 min + rebuild WebGL | Mismo día |
| 5. Test Cast: TV debe seguir funcionando con JWT dummy | TV agent | 30 min | Mismo día |
| 6. Mobile: añadir userId + castJwt fields a SaveData | Mobile agent | 1h | Coordinar con TV |
| 7. Mobile: implementar /mint-token call + auto-refresh | Mobile agent | 2-3h | — |
| 8. Mobile: añadir castJwt al state JSON enviado a Cast | Mobile agent | 30 min | — |
| 9. Test end-to-end: phone → mint → cast → TV → R2 con JWT | Both | 1h | — |
| 10. Apply same setup a iOS cuando llegue ese momento | Mobile agent | 30 min (mismo código) | iOS launch |

**Total estimado: 1 día TV + 1 día mobile + 1 día testing = 3 días con un developer.**

---

## 9. Coste mensual

| Servicio | Tier | Coste |
|---|---|---|
| Cloudflare Workers | Free (100k req/día) | $0 |
| Cloudflare R2 storage | 10 GB free | $0 (hasta superar) |
| Cloudflare R2 egress (Class B) | 1M ops/mes free | $0 (hasta superar) |
| Custom domain (opcional) | n/a | $0-12/año |

**Para arrancar: $0/mes.** Worker free tier cubre ~3.000 daily active users (cada uno hace ~30 bundle requests/sesión × 1 sesión/día = 90k req/día margen).

Cuando superes free tier:
- Workers paid: $5/mes por 10M requests
- R2 egress: $0.36/GB tras el free tier (10 GB/mes)
- A 10K DAU: estimado <$20/mes total

---

## 10. Riesgos & mitigaciones

| Riesgo | Probabilidad | Mitigación |
|---|---|---|
| Worker free tier saturado | Baja (en early stage) | Monitor en Cloudflare dashboard. Si supera 80% durante 3 días → upgrade a paid ($5/mes). |
| JWT_SECRET filtrado en Worker logs | Baja | Cloudflare encripta secrets en transit + at rest. NUNCA loggear el secret. |
| User pierde JWT (clear data) | Media | App al detectar JWT vacío → llama /mint-token con ownedItems[] de SaveData. JWT nuevo. Transparente. |
| User comparte JWT con amigo | Baja | JWT contiene userId + ownedItems del owner. El amigo recibe los assets del owner — útil cero si quiere los suyos. |
| Atacante intercepta JWT en red insegura | Baja | TLS en todos los hops (R2, Worker, Cast). Atacante necesitaría MITM root cert instalado. |
| Worker cae (Cloudflare outage) | Baja | Worker tiene 99.9% SLA. Caída raras. TV mostraría error de carga, user reintentaría. |
| Atacante extrae JWT_SECRET de codigo public | n/a | Secret SOLO vive en Worker (servidor). Cliente nunca lo conoce. |

---

## 11. Mejoras futuras (NO MVP)

- **Validar purchaseToken con Google Play API** en /mint-token (requiere Service Account)
- **Rate limit per IP en Worker** (Cloudflare Rules, sin código)
- **Signed URLs short-lived en vez de 302 redirect** (más complejo, no necesario MVP)
- **Per-device JWT** (deviceFingerprint en claims) — pero romperia el Cast cross-device share
- **Token revocation list** en Cloudflare KV (para banear JWTs robados puntuales)

---

## 12. Definición de "done"

Spec ejecutada cuando:
- [ ] Worker deployed a `https://auth.appquarium-tv.workers.dev/`
- [ ] FREE_TIER_IDS configurado con los 7 items de `pack_content_free`
- [ ] TV Addressables RemoteLoadPath apunta al Worker
- [ ] Test manual: `curl -I https://auth.appquarium-tv.workers.dev/bundle/fish_black_durgon.bundle` devuelve 401 sin auth
- [ ] Test con JWT dummy válido: mismo URL devuelve 302
- [ ] Test con JWT premium=true: cualquier bundle devuelve 302
- [ ] Test con JWT sin item: bundle no-owned devuelve 403
- [ ] Mobile genera y guarda userId en SaveData
- [ ] Mobile llama /mint-token tras IAP success, recibe y guarda JWT
- [ ] Mobile envía castJwt en TvAquariumState Cast INIT
- [ ] TV WebGL recibe JWT, lo añade en Authorization header
- [ ] Cast en Xiaomi funciona end-to-end con auth
- [ ] Documentado el final state en `BUILD_REPORT_2026-XX-XX.md`
- [ ] Memoria `project_r2_security.md` actualizada a "done"

---

## 13. Notas para Sonnet implementador

- **NO empezar hasta que Cast Fase A.1 funcione** sin auth. Primero validar el caso happy-path, luego añadir capa de auth.
- **NO requiere rebuild de bundles** — solo del catalog (cambio en RemoteLoadPath del profile). Esto es 1 minuto vs 1-3h.
- **Coordinar con Marc para crear Worker** — necesita login Cloudflare. Marc hace setup, Sonnet escribe el código TypeScript.
- **Si validación Google Play falla** en /mint-token (MVP no la incluye), la app sigue funcionando — el Worker emite token igual. Es trade-off conocido para MVP.
- **El JWT en Cast INIT message es plain text** dentro del JSON. No es ideal (un MITM en LAN podría leerlo) pero Cast SDK usa TLS interno → riesgo bajo.
- **Cuando se mergee a main** del TV project: actualizar `MEMORY.md` con el deploy URL y status.
