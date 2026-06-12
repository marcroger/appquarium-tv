using UnityEngine;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

/// <summary>
/// Overlay de debug que muestra el nombre y estado de cada capa de renderizado.
/// Añadir al mismo GameObject que TvSceneBootstrap.
/// Desactivar el componente para ocultar el overlay sin rebuildar.
/// </summary>
public class TvLayerDebug : MonoBehaviour
{
    public static TvLayerDebug Instance { get; private set; }

    private readonly List<string> _lines = new List<string>();
    private GUIStyle _style;
    private GUIStyle _bgStyle;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    public static void Set(string key, string value)
    {
        if (Instance == null) return;
        string line = $"{key}: {value}";
        for (int i = 0; i < Instance._lines.Count; i++)
        {
            if (Instance._lines[i].StartsWith(key + ":"))
            {
                Instance._lines[i] = line;
                return;
            }
        }
        Instance._lines.Add(line);
    }

    void OnGUI()
    {
        if (_lines.Count == 0) return;

        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 22,
                fontStyle = FontStyle.Bold,
            };
            _style.normal.textColor = Color.yellow;

            _bgStyle = new GUIStyle(GUI.skin.box);
        }

        float lineH = 28f;
        float w     = 700f;
        float h     = _lines.Count * lineH + 12f;
        float x     = 16f;
        float y     = 16f;

        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.Box(new Rect(x - 6, y - 6, w + 12, h), GUIContent.none);
        GUI.color = Color.white;

        for (int i = 0; i < _lines.Count; i++)
            GUI.Label(new Rect(x, y + i * lineH, w, lineH), _lines[i], _style);
    }
}
