using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ── Data models ───────────────────────────────────────────────────────────────

/// <summary>Fish entry in the Cast state — only what the TV receiver needs to display.</summary>
[Serializable]
public class TvFishEntry
{
    public string speciesId;
    public string nickname;
}

/// <summary>Full aquarium state sent to the TV receiver on connect (INIT message).</summary>
[Serializable]
public class TvAquariumState
{
    public List<TvFishEntry> activeFish   = new List<TvFishEntry>();
    /// <summary>JSON-serialized List&lt;DecoPlacement&gt; via DecoPlacementList wrapper.</summary>
    public string            decoJson     = "{}";
    public string            bgId         = "";
    public string            subId        = "";
    public string            lightId      = "light_white";
    /// <summary>"day" | "sunset" | "night"</summary>
    public string            ambientMode  = "day";
    public float             fishSpeed    = 1f;
    public string            selectedTankId = "";
}

/// <summary>Delta update — sent when something changes during a Cast session.</summary>
[Serializable]
public class TvUpdateMessage
{
    /// <summary>"ambient" | "speed" | "refresh" | "feed"</summary>
    public string type;
    public string value;
}

/// <summary>Wire format for all Cast channel messages.</summary>
[Serializable]
public class CastMessage
{
    /// <summary>"INIT" (full state) | "UPDATE" (delta)</summary>
    public string type;
    /// <summary>JSON-encoded TvAquariumState (INIT) or TvUpdateMessage (UPDATE).</summary>
    public string payload;
}

/// <summary>Wrapper to serialize a List&lt;DecoPlacement&gt; via JsonUtility.</summary>
[Serializable]
public class DecoPlacementList
{
    public List<DecoPlacement> items = new List<DecoPlacement>();
}

// ── CastManager ───────────────────────────────────────────────────────────────

public enum CastConnectionState { Disconnected, Scanning, Connected }

/// <summary>
/// Manages Chromecast sessions for Appquarium.
/// Spotify-model: mobile app stays fully functional; TV receiver mirrors the aquarium visually.
///
/// Architecture:
///   Android → Java CastPlugin (Google Cast SDK) → UnitySendMessage → this class
///   This class → Java CastPlugin → Cast Custom Channel → WebGL receiver
///
/// Cast Custom Channel namespace: urn:x-cast:dev.unknownaerials.appquarium
/// </summary>
public class CastManager : MonoBehaviour
{
    public static CastManager Instance { get; private set; }

    // ── Cast App ID (register at cast.google.com/publish — use default for dev) ─
    public const string CAST_APP_ID         = "8F6C873F";
    public const string CHANNEL_NAMESPACE   = "urn:x-cast:dev.unknownaerials.appquarium";
    private const string UNITY_OBJ_NAME     = "CastManager"; // must match GameObject name in scene

    // ── State ────────────────────────────────────────────────────────────────
    public  CastConnectionState State   { get; private set; } = CastConnectionState.Disconnected;
    public  string              DeviceName { get; private set; } = "";

    /// <summary>Fired on the main thread whenever State changes.</summary>
    public event Action<CastConnectionState> OnStateChanged;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (!AppFlags.EnableCast) return;
        var go = new GameObject(UNITY_OBJ_NAME);
        go.AddComponent<CastManager>();
        DontDestroyOnLoad(go);
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Ensure the GameObject has the expected name so UnitySendMessage finds it
        gameObject.name = UNITY_OBJ_NAME;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Toggle: if connected → disconnect; if not → start discovery.</summary>
    public void ToggleCast()
    {
        if (State == CastConnectionState.Connected || State == CastConnectionState.Scanning)
            Disconnect();
        else
            StartDiscovery();
    }

    /// <summary>Show Cast device picker and begin scanning.</summary>
    public void StartDiscovery()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        CallJava("startDiscovery");
#else
        Debug.Log("[Cast] Mock: StartDiscovery");
        StartCoroutine(MockConnectRoutine());
#endif
        SetState(CastConnectionState.Scanning, "");
    }

    /// <summary>Disconnect from current Cast session.</summary>
    public void Disconnect()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        CallJava("disconnect");
#else
        Debug.Log("[Cast] Mock: Disconnect");
#endif
        SetState(CastConnectionState.Disconnected, "");
    }

    /// <summary>
    /// Send the full aquarium state to the receiver (called automatically on connect).
    /// Can also be called manually to force a re-sync.
    /// </summary>
    public void SendFullState()
    {
        if (State != CastConnectionState.Connected) return;
        var state = BuildCurrentState();
        var msg   = new CastMessage { type = "INIT", payload = JsonUtility.ToJson(state) };
        SendRaw(JsonUtility.ToJson(msg));
    }

    /// <summary>Send a delta update to the receiver (e.g. ambient mode changed).</summary>
    public void SendUpdate(string updateType, string value = "")
    {
        if (State != CastConnectionState.Connected) return;
        var upd = new TvUpdateMessage { type = updateType, value = value };
        var msg = new CastMessage     { type = "UPDATE",   payload = JsonUtility.ToJson(upd) };
        SendRaw(JsonUtility.ToJson(msg));
    }

    // ── Callbacks from Java (via UnitySendMessage) ────────────────────────────

    /// <summary>Called by Java when Cast session is established.</summary>
    public void OnCastConnected(string deviceName)
    {
        Debug.Log($"[Cast] ✅ Connected to: {deviceName}");
        SetState(CastConnectionState.Connected, deviceName);
        SendFullState();
    }

    /// <summary>Called by Java when Cast session ends.</summary>
    public void OnCastDisconnected(string reason)
    {
        Debug.Log($"[Cast] ❌ Disconnected: {reason}");
        SetState(CastConnectionState.Disconnected, "");
    }

    /// <summary>Called by Java when a message arrives on the custom channel.</summary>
    public void OnCastMessageReceived(string json)
    {
        Debug.Log($"[Cast] ← {json}");
        // Handle any receiver-to-sender messages here if needed
    }

    // ── State building ────────────────────────────────────────────────────────

    private TvAquariumState BuildCurrentState()
    {
        var mgr = AquariumManager.Instance;
        if (mgr == null) return new TvAquariumState();

        var save  = mgr.SaveData;
        var state = new TvAquariumState
        {
            bgId           = save.selectedBgId    ?? "",
            subId          = save.selectedSubId   ?? "",
            lightId        = !string.IsNullOrEmpty(save.lightPresetId) ? save.lightPresetId : "light_white",
            ambientMode    = GetCurrentAmbientModeString(),
            fishSpeed      = save.fishSpeedMultiplier,
            selectedTankId = save.selectedTankId  ?? ""
        };

        // Active fish (species + nickname only — TV spawns at random positions)
        foreach (string uid in save.activeFishUids)
        {
            var owned = save.ownedFish?.Find(f => f.uid == uid);
            if (owned == null) continue;
            state.activeFish.Add(new TvFishEntry
            {
                speciesId = owned.speciesId,
                nickname  = owned.nickname ?? ""
            });
        }

        // Deco placements (full positional data so TV mirrors layout exactly)
        if (save.decoPositions != null && save.decoPositions.Count > 0)
        {
            var wrapper = new DecoPlacementList { items = save.decoPositions };
            state.decoJson = JsonUtility.ToJson(wrapper);
        }

        return state;
    }

    private string GetCurrentAmbientModeString()
    {
        var amb = FindFirstObjectByType<AmbientModeController>();
        if (amb == null) return "day";
        return amb.CurrentMode switch
        {
            AmbientModeController.AmbientMode.Day    => "day",
            AmbientModeController.AmbientMode.Sunset => "sunset",
            AmbientModeController.AmbientMode.Night  => "night",
            _                                         => "day"
        };
    }

    // ── Low-level send ────────────────────────────────────────────────────────

    private void SendRaw(string json)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        CallJava("sendMessage", json);
#else
        Debug.Log($"[Cast] Mock send: {json.Substring(0, Mathf.Min(120, json.Length))}…");
#endif
    }

    private void CallJava(string method, string arg = null)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var jc = new AndroidJavaClass("com.appquarium.app.CastPlugin");
            if (arg == null) jc.CallStatic(method);
            else             jc.CallStatic(method, arg);
        }
        catch (Exception e) { Debug.LogWarning($"[Cast] Java call '{method}' failed: {e.Message}"); }
#endif
    }

    private void SetState(CastConnectionState newState, string deviceName)
    {
        State      = newState;
        DeviceName = deviceName;
        OnStateChanged?.Invoke(newState);
        UIManager.Instance?.UpdateCastPill(newState);
        Debug.Log($"[Cast] State → {newState}{(deviceName.Length > 0 ? $" ({deviceName})" : "")}");
    }

    // ── Editor / mock ─────────────────────────────────────────────────────────

    private IEnumerator MockConnectRoutine()
    {
        yield return new WaitForSeconds(0.8f);
        OnCastConnected("Mock TV");
    }
}
