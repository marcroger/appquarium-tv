using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TV-only FoodManager — substituye el stub de TvStubs.cs con una implementación real.
///
/// Responsabilidades:
///   - Rastrear FoodItems activos en el tanque.
///   - Proporcionar GetNearestFood / ClaimFood para que FishBrain los detecte de forma natural.
///   - SpawnFood: instancia FoodItem procedural (sin prefab, visual autogenerado en FoodItem.Start).
///   - Auto-feed coroutine: cada 4 min spawna comida proporcional al nº de peces.
///
/// FishBrain.CheckForFood() guarda con if (FoodManager.Instance == null) return; de modo que
/// en cuanto Instance existe los peces detectan y comen la comida automáticamente.
/// </summary>
public class FoodManager : MonoBehaviour
{
    /// <summary>
    /// Escala global de la comida. Ver el comentario en SpawnFood(): los pellets que
    /// construye FoodItem son de tamaño móvil y en la TV se ven descomunales.
    /// </summary>
    public const float FoodScale = 0.33f;

    // ── Singleton ──────────────────────────────────────────────────────────────

    private static FoodManager _instance;
    public static FoodManager Instance
    {
        get
        {
            if (_instance != null) return _instance;
            var go = new GameObject("TvFoodManager");
            _instance = go.AddComponent<FoodManager>();
            return _instance;
        }
    }

    public static event System.Action OnFoodSpawned;

    // ── State ──────────────────────────────────────────────────────────────────

    private readonly List<FoodItem> _activeFood = new List<FoodItem>();
    private Bounds     _tankBounds;
    private Coroutine  _autoFeedCo;

    private const float AutoFeedInterval = 240f; // 4 minutes between auto-feeds

    /// <summary>
    /// ⚠⚠ 2026-08-27 — El autoalimentador del MOVIL tambien emite `feed` (lo cerraron el 26-ago
    /// en su `FoodManager`), y este de aqui no se enteraba: la tele comia lo suyo MAS lo del
    /// movil. No rompe nada, pero es comida doble y no lo pidio nadie. Lo encontro la sesion del
    /// repo movil revisando el contrato.
    ///
    /// 🧭 Manda el sender: en cuanto llega un `feed` de fuera, el automatico de aqui se calla.
    /// No se apaga para siempre —si el movil se desconecta, la tele se queda sola y tiene que
    /// seguir dando de comer—, sino que caduca: si pasa mas de un intervalo y medio sin recibir
    /// ninguno, se vuelve a encender solo.
    /// </summary>
    private float _ultimoFeedDelSender = -999f;
    private const float MargenSenderVivo = AutoFeedInterval * 1.5f;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
    }

    // ── Public API (used by FishBrain, AquariumManager, TvSceneBootstrap) ─────

    /// <summary>
    /// Spawna un FoodItem en la posición dada.
    /// El visual (3 pellets esféricos naranjas) se construye proceduralmente en FoodItem.Start().
    /// </summary>
    public void SpawnFood(Vector3 position)
    {
        var go = new GameObject("FoodItem");
        go.transform.position = position;

        // ⚠ 2026-08-11 — La comida se veía ENORME en la tele.
        // FoodItem construye 3 pellets de 0,45-0,52 / 0,30-0,36 / 0,28-0,34 unidades de
        // mundo. Para situarlo: el tanque mide 8,4 de alto y un Banggai ronda 1 ⇒ el pellet
        // central era MEDIO PEZ (~25 px en la Xiaomi con renderScale 0,7).
        // Se escala aquí y NO en FoodItem.cs porque ese fichero se sincroniza desde el móvil
        // (existe allí con los mismos valores, ver SYNC_NOTES.md) y el próximo sync
        // deshacería el cambio sin avisar. TvFoodManager es TV-only, así que esto sobrevive.
        // 0,33 deja el pellet central en ~0,16 de mundo ≈ 8 px en el device: se ve desde el
        // sofá sin parecer una pelota de playa.
        go.transform.localScale = Vector3.one * FoodScale;

        var fi = go.AddComponent<FoodItem>();
        fi.driftFreq  = Random.Range(1.5f, 2.5f);
        fi.driftAmp   = Random.Range(0.012f, 0.025f);
        fi.driftPhase = Random.Range(0f, Mathf.PI * 2f);
        fi.OnConsumed += () => _activeFood.Remove(fi);
        _activeFood.Add(fi);
        OnFoodSpawned?.Invoke();
    }

    /// <summary>
    /// Devuelve el FoodItem no reclamado más cercano a `position` dentro de `radius`.
    /// Llamado por FishBrain.CheckForFood() y ForceSeekFood().
    /// </summary>
    public FoodItem GetNearestFood(Vector3 position, float radius)
    {
        FoodItem best = null;
        float bestDist = radius;

        for (int i = _activeFood.Count - 1; i >= 0; i--)
        {
            var food = _activeFood[i];
            if (food == null) { _activeFood.RemoveAt(i); continue; } // destroyed (eaten/timeout)
            if (food.IsClaimed) continue;

            float d = Vector3.Distance(position, food.transform.position);
            if (d < bestDist) { bestDist = d; best = food; }
        }
        return best;
    }

    /// <summary>
    /// Marca la comida como reclamada para que otro pez busque otra diferente.
    /// Llamado por FishBrain antes de hacer TriggerFeed.
    /// </summary>
    public void ClaimFood(FoodItem food)
    {
        food?.Claim();
    }

    /// <summary>
    /// Inicia el auto-feed coroutine. Llamado desde AquariumManager tras inicializar el acuario.
    /// Spawna comida cada 4 min proporcional al número de peces activos (min 2, max 6 items).
    /// </summary>
    public void StartAutoFeed(Bounds tankBounds)
    {
        _tankBounds = tankBounds;
        if (_autoFeedCo != null) StopCoroutine(_autoFeedCo);
        _autoFeedCo = StartCoroutine(AutoFeedRoutine());
    }

    // ── Auto-feed coroutine ────────────────────────────────────────────────────

    private IEnumerator AutoFeedRoutine()
    {
        yield return new WaitForSeconds(AutoFeedInterval);
        while (true)
        {
            if (Time.time - _ultimoFeedDelSender < MargenSenderVivo)
            {
                // El movil esta alimentando: aqui no se toca. Se dice, o desde fuera parece
                // que el automatico se ha muerto.
                JsBridge.Log("auto-feed: lo salta el sender (el movil esta alimentando)");
            }
            else
            {
                var fishList = AquariumManager.Instance?.fishSpawner?.ActiveFish;
                if (fishList != null && fishList.Count > 0)
                    SpawnBatch(Mathf.Clamp(fishList.Count, 2, 6));
            }

            yield return new WaitForSeconds(AutoFeedInterval);
        }
    }

    /// <summary>
    /// Avisa de que ha llegado un `feed` del sender. Ver `_ultimoFeedDelSender`.
    /// </summary>
    public void FeedDelSender() => _ultimoFeedDelSender = Time.time;

    /// <summary>Spawna `count` items en posiciones X/Z aleatorias en la superficie del tanque.</summary>
    private void SpawnBatch(int count)
    {
        JsBridge.Log($"[AutoFeed] Spawning {count} food items");
        for (int i = 0; i < count; i++)
        {
            float x = Random.Range(_tankBounds.min.x + 0.5f, _tankBounds.max.x - 0.5f);
            float z = Random.Range(_tankBounds.min.z + 0.3f, _tankBounds.max.z - 0.3f);
            SpawnFood(new Vector3(x, _tankBounds.max.y - 0.2f, z));
        }
    }
}
