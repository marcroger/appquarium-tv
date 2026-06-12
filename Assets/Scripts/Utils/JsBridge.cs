using System.Runtime.InteropServices;

/// <summary>
/// Permite que C# escriba mensajes en el debug panel de JavaScript (panel cyan del receiver).
/// Solo activo en builds WebGL — en editor usa Debug.Log.
/// </summary>
public static class JsBridge
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void JsBridgeLog(string msg);

    public static void Log(string msg) => JsBridgeLog(msg);
#else
    public static void Log(string msg) => UnityEngine.Debug.Log($"[JsBridge] {msg}");
#endif
}
