using UnityEngine;

/// <summary>
/// WebGL-side bridge: the Cast Receiver SDK (JS) calls window.unityInstance.SendMessage
/// targeting this component when a message arrives on the Cast Custom Channel.
///
/// Attach to a GameObject named exactly "CastReceiver" in the TvScene.
///
/// JS side (cast-receiver.js):
///   castSession.addMessageListener(CHANNEL_NAMESPACE, (ns, msg) => {
///       window.unityInstance.SendMessage('CastReceiver', 'OnMessageReceived', msg);
///   });
/// </summary>
public class CastReceiver : MonoBehaviour
{
    public static CastReceiver Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        gameObject.name = "CastReceiver";  // must match JS SendMessage target
        Debug.Log("[CastReceiver] ✅ Ready — waiting for Cast sender messages.");
    }

    /// <summary>
    /// Entry point called from JavaScript by the Cast Receiver SDK.
    /// Parses the CastMessage and routes INIT / UPDATE to TvSceneBootstrap.
    /// </summary>
    public void OnMessageReceived(string json)
    {
        Debug.Log($"[CastReceiver] ← {json.Substring(0, Mathf.Min(100, json.Length))}…");

        CastMessage msg;
        try   { msg = JsonUtility.FromJson<CastMessage>(json); }
        catch { Debug.LogWarning($"[CastReceiver] Failed to parse message: {json}"); return; }

        if (msg == null || string.IsNullOrEmpty(msg.type)) return;

        switch (msg.type)
        {
            case "INIT":
                var state = JsonUtility.FromJson<TvAquariumState>(msg.payload);
                if (TvSceneBootstrap.Instance == null)
                    JsBridge.Log("ERR: TvSceneBootstrap.Instance is NULL at INIT time!");
                TvSceneBootstrap.Instance?.InitializeFromState(state);
                break;

            case "UPDATE":
                var upd = JsonUtility.FromJson<TvUpdateMessage>(msg.payload);
                TvSceneBootstrap.Instance?.ApplyUpdate(upd);
                break;

            case "PING":
            case "KEEPALIVE":
                break;

            default:
                Debug.LogWarning($"[CastReceiver] Unknown message type: {msg.type}");
                break;
        }
    }

    // ── JS interop helper (called from cast-receiver.js on sender connect) ───

    /// <summary>Called from JS when the Cast sender connects.</summary>
    public void OnSenderConnected(string senderId)
        => Debug.Log($"[CastReceiver] Sender connected: {senderId}");

    /// <summary>Called from JS when the Cast sender disconnects.</summary>
    public void OnSenderDisconnected(string senderId)
        => Debug.Log($"[CastReceiver] Sender disconnected: {senderId}");

    // ── Device input (Android TV remote / keyboard devtest) ──────────────────

    /// <summary>
    /// Called from JavaScript when the user presses a button on the Android TV remote
    /// or keyboard (devtest mode).
    ///   "startle" → fish scatter from tank center (analogous to tapping the screen)
    ///   "feed"    → spawn food visual + fish swim to eat
    /// </summary>
    public void OnDeviceInput(string action)
    {
        JsBridge.Log($"[Input] {action}");
        var mgr = AquariumManager.Instance;
        if (mgr == null) return;

        switch (action)
        {
            case "startle":
                mgr.StartleAll(mgr.tankController.GetTankBounds().center);
                break;
            case "feed":
                mgr.FeedAll();
                break;
        }
    }
}
