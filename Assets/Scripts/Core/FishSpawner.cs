using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Instancia peces en el tanque en posiciones aleatorias válidas.
/// En producción, leerá la lista de peces del usuario desde el servidor.
/// </summary>
public class FishSpawner : MonoBehaviour
{
    [Header("Prefab de comportamiento")]
    [Tooltip("GameObject vacío con FishAgent, FishBrain, SteeringController, NeedsModule, FishProceduralAnimator. " +
             "El visual de cada especie (FishData.prefab) se instancia como hijo en runtime.")]
    public GameObject fishPrefab;

    [Header("Margen interior del tanque (para evitar spawn en paredes)")]
    public float spawnMargin = 0.8f;

    // Lista de peces activos (lectura pública)
    private readonly List<FishAgent> _activeFish = new();
    public IReadOnlyList<FishAgent> ActiveFish => _activeFish;

    // ── API pública ─────────────────────────────────────────────────────────

    /// <summary>
    /// Instancia un pez con los datos de especie dados dentro de los bounds del tanque.
    /// </summary>
    public FishAgent SpawnFish(FishData data, Bounds tankBounds, OwnedFishSave save = null)
    {
        if (fishPrefab == null)
        {
            Debug.LogError("[FishSpawner] fishPrefab no asignado. Asígnalo en el Inspector.");
            return null;
        }

        Vector3 spawnPos = GetRandomSpawnPosition(tankBounds);

        // Root: prefab de comportamiento (scripts de IA/FSM/steering)
        GameObject fishGO = Instantiate(fishPrefab, spawnPos, Quaternion.identity);
        fishGO.name = data.speciesName;

        // Visual: prefab de especie como hijo (mesh + Animator del Pack 24).
        // Orden de resolución: referencia directa → AssetBundle (PAD) → procedural placeholder.
        GameObject visualPrefab = data.prefab;
        if (visualPrefab == null
            && !string.IsNullOrEmpty(data.assetBundleName)
            && !string.IsNullOrEmpty(data.assetBundleAssetName))
        {
            visualPrefab = AssetBundleLoader.LoadPrefab(data.assetBundleName, data.assetBundleAssetName);
            if (visualPrefab != null)
                Debug.Log($"[FishSpawner] {data.itemId} — loaded from bundle '{data.assetBundleName}'");
        }

        if (visualPrefab != null)
        {
            GameObject visual = Instantiate(visualPrefab, fishGO.transform);
            visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            // Fix shader AssetBundle: el bytecode del bundle puede no coincidir con el runtime.
            // Re-encontrar cada shader por nombre garantiza que se usa la versión del proyecto.
            FixBundleShaders(visual);

            // Deshabilitar root motion: el clip NO debe mover la posición del pez.
            // Posición = SteeringController | Rotación = FishProceduralAnimator | Visual = Animator
            var visualAnimator = visual.GetComponentInChildren<Animator>();
            if (visualAnimator != null)
                visualAnimator.applyRootMotion = false;

            // Escalar usando sharedMesh.bounds (local space, siempre disponible desde el asset)
            // Evita depender de SkinnedMeshRenderer.bounds que puede ser cero antes del primer frame
            float meshHeight = 0f;
            var smr = visual.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr != null && smr.sharedMesh != null)
                meshHeight = smr.sharedMesh.bounds.size.y;

            if (meshHeight < 0.001f)
            {
                // Fallback: MeshFilter (para prefabs sin skinning)
                var mf = visual.GetComponentInChildren<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                    meshHeight = mf.sharedMesh.bounds.size.y;
            }

            if (meshHeight > 0.001f && data.baseSize > 0f)
            {
                // Normalizar el visual a 1 unidad de alto en espacio local del root.
                // FishAgent.Initialize luego escala el root a data.baseSize →
                // altura mundo final = root(baseSize) × visual_local(1/meshH) × meshH = baseSize ✓
                float scale = 1f / meshHeight;
                visual.transform.localScale = Vector3.one * scale;

                // Pasar al SteeringController la fracción real del mesh bajo el pivot
                // para que FloorMargin calcule el margen correcto sin asumir pivot centrado.
                if (smr != null && smr.sharedMesh != null)
                {
                    float ratio = Mathf.Abs(smr.sharedMesh.bounds.min.y) / meshHeight;
                    var steering = fishGO.GetComponent<SteeringController>();
                    if (steering != null) steering.SetPivotToBottomRatio(ratio);
                }

                Debug.Log($"[FishSpawner] ✓ {data.itemId} | meshH={meshHeight:F3} baseSize={data.baseSize:F2} normScale={scale:F3} | pos={spawnPos:F1}");
            }
            else
            {
                Debug.LogWarning($"[FishSpawner] ⚠ {data.itemId} meshHeight={meshHeight:F4} — sin escala aplicada");
            }
        }
        else
        {
            // Sin prefab 3D: visual procedural de placeholder hasta tener assets reales.
            // El pez sigue siendo funcional (IA, colisión, inspector) y visualmente reconocible.
            BuildProceduralFishVisual(data, fishGO.transform);
            Debug.LogWarning($"[FishSpawner] ⚠ {data.itemId} sin prefab — usando visual procedural.");
        }

        FishAgent agent = fishGO.GetComponent<FishAgent>();
        if (agent == null)
        {
            Debug.LogError("[FishSpawner] fishPrefab no tiene componente FishAgent.");
            Destroy(fishGO);
            return null;
        }

        agent.Initialize(data, tankBounds, save);
        _activeFish.Add(agent);

        return agent;
    }

    /// <summary>Elimina un pez específico del tanque (lo quita de la lista activa).</summary>
    public void DespawnFish(FishAgent agent)
    {
        _activeFish.Remove(agent);
        if (agent != null) Destroy(agent.gameObject);
    }

    /// <summary>
    /// Intenta sustituir el visual procedural de los peces que spawnearon sin prefab
    /// por el modelo real del pack ahora que el asset pack puede estar descargado.
    /// El pez sigue en su posición actual y mantiene toda su IA/estado.
    /// Llamar desde AquariumManager después de que CheckOnLaunch complete.
    /// </summary>
    public void RespawnPlaceholders()
    {
        int replaced = 0;
        foreach (var agent in _activeFish)
        {
            if (agent == null) continue;

            // Detectar visual procedural: hijo con nombre "[Visual_Procedural]"
            Transform oldVisual = agent.transform.Find("[Visual_Procedural]");
            if (oldVisual == null) continue;

            FishData data = agent.Data;

            // Intentar obtener el prefab real ahora (directo o vía bundle)
            GameObject realPrefab = data.prefab;
            if (realPrefab == null
                && !string.IsNullOrEmpty(data.assetBundleName)
                && !string.IsNullOrEmpty(data.assetBundleAssetName))
            {
                realPrefab = AssetBundleLoader.LoadPrefab(data.assetBundleName, data.assetBundleAssetName);
            }

            if (realPrefab == null) continue; // pack aún no disponible

            // Destruir el placeholder
            Destroy(oldVisual.gameObject);

            // Instanciar visual real como hijo (misma lógica que SpawnFish)
            GameObject visual = Instantiate(realPrefab, agent.transform);
            visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            FixBundleShaders(visual);

            var visualAnimator = visual.GetComponentInChildren<Animator>();
            if (visualAnimator != null) visualAnimator.applyRootMotion = false;

            float meshHeight = 0f;
            var smr = visual.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr != null && smr.sharedMesh != null)
                meshHeight = smr.sharedMesh.bounds.size.y;
            if (meshHeight < 0.001f)
            {
                var mf = visual.GetComponentInChildren<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                    meshHeight = mf.sharedMesh.bounds.size.y;
            }
            if (meshHeight > 0.001f && data.baseSize > 0f)
            {
                visual.transform.localScale = Vector3.one * (1f / meshHeight);
                if (smr != null && smr.sharedMesh != null)
                {
                    float ratio = Mathf.Abs(smr.sharedMesh.bounds.min.y) / meshHeight;
                    var steering = agent.GetComponent<SteeringController>();
                    if (steering != null) steering.SetPivotToBottomRatio(ratio);
                }
            }

            replaced++;
            Debug.Log($"[FishSpawner] ✅ {data.itemId} — placeholder reemplazado por modelo real.");
        }

        if (replaced > 0)
            Debug.Log($"[FishSpawner] ✅ {replaced} peces reemplazados de placeholder a modelo real.");
    }

    /// <summary>
    /// Quita UN pez de la especie indicada. Devuelve cuántos quitó (0 ó 1).
    ///
    /// ⚠ 2026-08-15 — el móvil saca peces DE UNO EN UNO (por uid), pero el protocolo Cast
    /// sólo transporta la especie: `SendUpdate("remove_fish", savedFish.speciesId)`.
    /// La TV llamaba a DespawnBySpecies, que borra TODOS los coincidentes: si tenías 3
    /// Banggai y quitabas uno en el móvil, en la tele desaparecían los tres. Sin ningún
    /// error: el log decía alegremente "removed=3".
    /// </summary>
    public int DespawnOneBySpecies(string speciesId)
    {
        foreach (var f in _activeFish)
        {
            if (f == null || f.Data?.itemId != speciesId) continue;
            _activeFish.Remove(f);
            Destroy(f.gameObject);
            return 1;
        }
        return 0;
    }

    /// <summary>Quita TODOS los peces de la especie. Devuelve cuántos quitó.</summary>
    public int DespawnBySpecies(string speciesId)
    {
        var toRemove = new List<FishAgent>();
        foreach (var f in _activeFish)
            if (f != null && f.Data?.itemId == speciesId) toRemove.Add(f);
        foreach (var f in toRemove)
        {
            _activeFish.Remove(f);
            if (f != null) Destroy(f.gameObject);
        }
        return toRemove.Count;
    }

    /// <summary>Elimina todos los peces activos.</summary>
    public void DespawnAll()
    {
        foreach (var fish in _activeFish)
            if (fish != null) Destroy(fish.gameObject);

        _activeFish.Clear();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private Vector3 GetRandomSpawnPosition(Bounds bounds)
    {
        float m = spawnMargin;
        return new Vector3(
            Random.Range(bounds.min.x + m, bounds.max.x - m),
            Random.Range(bounds.min.y + m, bounds.max.y - m),
            Random.Range(bounds.min.z + m, bounds.max.z - m)
        );
    }

    // ── Visual procedural (placeholder hasta tener assets 3D) ────────────────

    /// <summary>
    /// Construye un pez simplificado con primitivas Unity cuando FishData.prefab == null.
    /// Color según rareza; forma reconocible como pez. Orientado en +Z (dirección forward).
    /// La escala final la controla el root (FishAgent.Initialize → transform.localScale = baseSize).
    /// </summary>
    private static void BuildProceduralFishVisual(FishData data, Transform parent)
    {
        // Color base por rareza
        Color body = data.rarity switch
        {
            FishRarity.Common    => new Color(0.50f, 0.60f, 0.68f),  // gris azulado
            FishRarity.Uncommon  => new Color(0.22f, 0.72f, 0.38f),  // verde esmeralda
            FishRarity.Rare      => new Color(0.20f, 0.46f, 0.90f),  // azul marino
            FishRarity.Epic      => new Color(0.65f, 0.18f, 0.88f),  // púrpura
            FishRarity.Legendary => new Color(0.95f, 0.76f, 0.05f),  // dorado
            _                   => new Color(0.50f, 0.60f, 0.68f)
        };
        Color belly = Color.Lerp(body, Color.white, 0.45f);
        Color dark  = body * 0.65f;

        var root = new GameObject("[Visual_Procedural]");
        root.transform.SetParent(parent, false);

        // Cuerpo principal — elipsoide alargado en Z (forward)
        AddPart(root, PrimitiveType.Sphere,
            pos:   new Vector3(0f,    0f,     0f),
            scale: new Vector3(1.30f, 0.80f, 2.40f),
            color: body);

        // Vientre — más claro, ligeramente desplazado hacia abajo y adelante
        AddPart(root, PrimitiveType.Sphere,
            pos:   new Vector3(0f,   -0.12f,  0.15f),
            scale: new Vector3(1.00f, 0.60f, 1.70f),
            color: belly);

        // Aleta caudal — aplanada en X, grande en Y, poco en Z
        AddPart(root, PrimitiveType.Sphere,
            pos:   new Vector3(0f,    0f,    -1.35f),
            scale: new Vector3(0.10f, 0.55f,  0.42f),
            color: dark);

        // Aleta dorsal — cresta estrecha encima del cuerpo
        AddPart(root, PrimitiveType.Sphere,
            pos:   new Vector3(0f,    0.50f,  0.10f),
            scale: new Vector3(0.08f, 0.30f,  0.50f),
            color: dark);

        // Ojo — pequeña esfera oscura a cada lado
        AddPart(root, PrimitiveType.Sphere,
            pos:   new Vector3( 0.56f, 0.08f, 0.70f),
            scale: Vector3.one * 0.14f,
            color: new Color(0.08f, 0.08f, 0.08f));
        AddPart(root, PrimitiveType.Sphere,
            pos:   new Vector3(-0.56f, 0.08f, 0.70f),
            scale: Vector3.one * 0.14f,
            color: new Color(0.08f, 0.08f, 0.08f));
    }

    private static void AddPart(GameObject parent, PrimitiveType type,
                                 Vector3 pos, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale    = scale;

        var mr     = go.GetComponent<MeshRenderer>();
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat    = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     color);
        mr.material = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// <summary>
    /// Reasigna cada shader por nombre tras cargar un prefab de AssetBundle.
    /// El bytecode compilado en el bundle puede no coincidir con el shader del proyecto
    /// actual, produciendo el material morado/lila típico de shader no encontrado.
    /// </summary>
    private static void FixBundleShaders(GameObject visual)
    {
        // Sprites/Default está garantizado en el build (TankBackground lo usa)
        var fallback = Shader.Find("Sprites/Default");

        // ⚠ 2026-08-25 — LAS ALETAS NO SON `FishUnlit`, SON `Sprites/Default`.
        // Se descubrio con un control extremo sobre la tele: con `fishDesat=1.0` los CUERPOS
        // salian en escala de grises y las ALETAS seguian amarillas y azules fluorescentes,
        // porque `Sprites/Default` no conoce ninguno de los globales del pez.
        // `Appquarium/FishFin` es ese mismo shader clonado (mismo blend, mismo orden) mas los
        // globales de ciclo, tono y niebla, para que la aleta reciba el mismo trato que el
        // cuerpo al que esta pegada.
        //
        // El mapeo es exacto y no puede desbordarse: este metodo recibe el VISUAL DEL PEZ, asi
        // que un material que apunte a `Sprites/Default` aqui es por definicion parte del pez.
        // El suelo y el fondo tambien usan `Sprites/Default` pero no pasan por aqui.
        var finShader = Shader.Find("Appquarium/FishFin");   // null → se queda como estaba
        foreach (var r in visual.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.materials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null || mats[i].shader == null) continue;
                var found = Shader.Find(mats[i].shader.name);
                if (found == null) found = fallback; // shader stripeado → Sprites/Default
                // Las aletas: Sprites/Default → FishFin, para que sigan al cuerpo.
                if (finShader != null && found == fallback) found = finShader;
                if (found != null && found != mats[i].shader)
                {
                    mats[i].shader = found;
                    changed = true;
                }
            }
            if (changed) r.materials = mats;
        }
    }
}
