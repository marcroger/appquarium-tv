using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Diagnóstico de las sombras planares de las decos — 2026-08-11.
///
/// Por qué existe: las sombras (Appquarium/PlanarShadow) no se ven NI en el Cast
/// device NI en Chrome de escritorio, pero el shader se encuentra en runtime y se
/// crean los 6 objetos de sombra. Antes de tocar el shader hacía falta saber POR QUÉ
/// no pintan — y un build de WebGL cuesta horas, así que se mira aquí.
///
/// Uso:
///   1. Abrir Assets/Scenes/TvScene.unity
///   2. Play
///   3. Appquarium TV → 🔎 Sombras: inyectar acuario de prueba
///   4. esperar a que carguen los bundles (mirar la Console)
///   5. Appquarium TV → 🔎 Sombras: volcar diagnóstico
///
/// Es una herramienta de diagnóstico: no toca nada de la escena.
/// </summary>
public static class TvShadowDiag
{
    // Mismo par tanque/Y que usa el ?devtest=1 del receiver y Tools/cast-headless.js.
    // ⚠ tank_l y y:-2.8 van juntos: la Y del suelo se deriva de _tankBounds.
    private const string TestState = @"{
        ""activeFish"": [{""speciesId"": ""fish_banggai_cardinalfish"", ""nickname"": ""diag""}],
        ""bgId"": ""bg_tropical"", ""subId"": ""sub_sand"", ""lightId"": ""light_white"",
        ""ambientMode"": ""day"", ""fishSpeed"": 1.0, ""selectedTankId"": ""tank_l"",
        ""decoJson"": ""{\""items\"":[
            {\""itemId\"":\""deco_anchor\"",\""instanceId\"":\""deco_anchor_0\"",\""position\"":{\""x\"":-3.0,\""y\"":-2.8,\""z\"":2.0},\""scaleFactor\"":1},
            {\""itemId\"":\""deco_rock_hq_1\"",\""instanceId\"":\""deco_rock_hq_1_0\"",\""position\"":{\""x\"":0.0,\""y\"":-2.8,\""z\"":2.0},\""scaleFactor\"":1},
            {\""itemId\"":\""deco_coral_acropora\"",\""instanceId\"":\""deco_coral_acropora_0\"",\""position\"":{\""x\"":3.0,\""y\"":-2.8,\""z\"":2.0},\""scaleFactor\"":1}
        ]}""
    }";

    // El menú "Edit/Play" no es ejecutable por MenuItem desde fuera; esto sí.
    [MenuItem("Appquarium TV/ShadowDiag 0 Play", priority = 199)]
    public static void Play()
    {
        if (Application.isPlaying) { Debug.Log("[SHDIAG] ya estaba en play mode"); return; }
        EditorApplication.EnterPlaymode();
        Debug.Log("[SHDIAG] entrando en play mode…");
    }

    [MenuItem("Appquarium TV/ShadowDiag 0b Stop", priority = 199)]
    public static void Stop()
    {
        if (!Application.isPlaying) { Debug.Log("[SHDIAG] no estaba en play mode"); return; }
        EditorApplication.ExitPlaymode();
        Debug.Log("[SHDIAG] saliendo de play mode…");
    }

    [MenuItem("Appquarium TV/ShadowDiag 1 Inject", priority = 200)]
    public static void Inject()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("[SHDIAG] Hay que estar en PLAY MODE. Dale a Play y repite.");
            return;
        }

        var receiver = Object.FindFirstObjectByType<CastReceiver>();
        if (receiver == null)
        {
            Debug.LogError("[SHDIAG] No encuentro CastReceiver en la escena.");
            return;
        }

        // Compactar el JSON a una línea (el literal va indentado para poder leerlo).
        string state = TestState.Replace("\r", "").Replace("\n", "").Replace("            ", "").Replace("        ", "");
        string msg   = JsonUtility.ToJson(new Wrapper { type = "INIT", payload = state });
        receiver.OnMessageReceived(msg);
        Debug.Log("[SHDIAG] INIT inyectado (3 decos + 1 pez). Espera a que carguen los bundles y ejecuta el paso 2.");
    }

    [System.Serializable]
    private class Wrapper { public string type; public string payload; }

    /// <summary>
    /// Captura el Game view a PNG. Es la única forma que tengo de VER el resultado
    /// sin gastar un build de WebGL: itero shader → play → captura en ~1 min.
    /// </summary>
    [MenuItem("Appquarium TV/ShadowDiag 3 Shot", priority = 202)]
    public static void Shot()
    {
        if (!Application.isPlaying) { Debug.LogError("[SHDIAG] Shot necesita play mode."); return; }
        string dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "_shadowdiag");
        System.IO.Directory.CreateDirectory(dir);
        string path = System.IO.Path.Combine(dir, "shot.png");
        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        ScreenCapture.CaptureScreenshot(path);
        Debug.Log("[SHDIAG] captura solicitada → " + path);
    }

    /// <summary>
    /// Registra Appquarium/FishShadow en Graphics > Always Included Shaders.
    /// Se hace por API y no editando ProjectSettings/GraphicsSettings.asset a mano porque
    /// con el Editor abierto Unity puede reescribir el fichero y perder el cambio.
    /// Sin esto el shader se strippea del build de WebGL (no lo referencia ningún material,
    /// solo un Shader.Find en runtime) y las sombras de pez fallarían SOLO en la tele.
    /// </summary>
    [MenuItem("Appquarium TV/ShadowDiag 4 RegistrarShader", priority = 203)]
    public static void RegistrarShader()
    {
        var sh = Shader.Find("Appquarium/FishShadow");
        if (sh == null) { Debug.LogError("[SHDIAG] no encuentro Appquarium/FishShadow"); return; }

        var gs  = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0];
        var so  = new SerializedObject(gs);
        var arr = so.FindProperty("m_AlwaysIncludedShaders");

        for (int i = 0; i < arr.arraySize; i++)
            if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh)
            { Debug.Log("[SHDIAG] Appquarium/FishShadow ya estaba registrado"); return; }

        arr.InsertArrayElementAtIndex(arr.arraySize);
        arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
        so.ApplyModifiedProperties();

        // ⚠ ApplyModifiedProperties + SaveAssets NO basta para ProjectSettings: el cambio
        // se queda en memoria y el .asset del disco sigue igual (comprobado con grep del
        // GUID tras un File → Save Project). Hay que marcarlo sucio explícitamente y forzar
        // el guardado del asset concreto. Si esto falla, el shader se strippea del build
        // y las sombras de pez funcionan en el Editor pero NO en la tele.
        EditorUtility.SetDirty(gs);
        AssetDatabase.SaveAssetIfDirty(gs);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[SHDIAG] Appquarium/FishShadow AÑADIDO a Always Included ({arr.arraySize} shaders)");
    }

    /// <summary>
    /// Suelta comida a mano. El auto-feed va cada 4 minutos, que no sirve para revisar
    /// el tamaño de los pellets en pantalla.
    /// </summary>
    [MenuItem("Appquarium TV/ShadowDiag 5 Feed", priority = 204)]
    public static void Feed()
    {
        if (!Application.isPlaying) { Debug.LogError("[SHDIAG] Feed necesita play mode."); return; }
        var fm = Object.FindFirstObjectByType<FoodManager>();
        if (fm == null) { Debug.LogError("[SHDIAG] no hay FoodManager en la escena"); return; }

        for (int i = 0; i < 5; i++)
            fm.SpawnFood(new Vector3(-2.5f + i * 1.25f, 1.5f, 0f));
        Debug.Log("[SHDIAG] 5 raciones soltadas a y=1,5 (caerán hacia el suelo)");
    }

    [MenuItem("Appquarium TV/ShadowDiag 2 Dump", priority = 201)]
    public static void Dump()
    {
        var sb = new StringBuilder();
        sb.AppendLine("════════ SHDIAG ════════");

        // ── el suelo y sus capas ────────────────────────────────────────────────
        foreach (var name in new[] { "TankFloor", "TankFloorOccluder", "TankFloorFadeOverlay" })
        {
            var go = GameObject.Find(name);
            if (go == null) { sb.AppendLine($"[suelo] {name}: NO EXISTE"); continue; }
            var r = go.GetComponent<Renderer>();
            if (r == null) { sb.AppendLine($"[suelo] {name}: sin Renderer"); continue; }
            var m = r.sharedMaterial;
            sb.AppendLine($"[suelo] {name}: enabled={r.enabled} shader='{(m ? m.shader.name : "null")}' " +
                          $"queue={(m ? m.renderQueue : -1)} sortLayer='{r.sortingLayerName}' order={r.sortingOrder} " +
                          $"y={go.transform.position.y:F3}");
        }

        // ── las sombras ─────────────────────────────────────────────────────────
        var shadows = new List<GameObject>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t.name.StartsWith("Shadow_")) shadows.Add(t.gameObject);

        sb.AppendLine($"[sombra] contenedores encontrados: {shadows.Count}");
        foreach (var s in shadows)
        {
            sb.AppendLine($"[sombra] {s.name}: activo={s.activeInHierarchy} hijos={s.transform.childCount}");
            int i = 0;
            foreach (Transform child in s.transform)
            {
                if (i++ >= 2) { sb.AppendLine($"[sombra]   … y {s.transform.childCount - 2} hijos más"); break; }
                var mr = child.GetComponent<MeshRenderer>();
                var mf = child.GetComponent<MeshFilter>();
                if (mr == null) { sb.AppendLine($"[sombra]   {child.name}: SIN MeshRenderer"); continue; }
                var m = mr.sharedMaterial;
                sb.AppendLine($"[sombra]   {child.name}: enabled={mr.enabled} visible={mr.isVisible} " +
                              $"shader='{(m ? m.shader.name : "NULL")}' queue={(m ? m.renderQueue : -1)} " +
                              $"floorY={(m && m.HasProperty("_FloorY") ? m.GetFloat("_FloorY").ToString("F3") : "n/a")} " +
                              $"alpha={(m && m.HasProperty("_ShadowAlpha") ? m.GetFloat("_ShadowAlpha").ToString("F2") : "n/a")} " +
                              $"order={mr.sortingOrder} layer='{mr.sortingLayerName}' " +
                              $"mesh={(mf && mf.sharedMesh ? mf.sharedMesh.name : "NULL")} " +
                              $"boundsY=[{mr.bounds.min.y:F2}..{mr.bounds.max.y:F2}] centro={mr.bounds.center}");
            }
        }

        // ── las decos, para comparar ────────────────────────────────────────────
        var placer = Object.FindFirstObjectByType<DecorationPlacer>();
        sb.AppendLine($"[deco] DecorationPlacer: {(placer ? "OK" : "NO ENCONTRADO")}");
        if (placer != null)
        {
            foreach (Transform t in placer.transform)
            {
                if (t.name.StartsWith("Shadow_")) continue;
                var mr = t.GetComponentInChildren<MeshRenderer>();
                if (mr == null) continue;
                var m = mr.sharedMaterial;
                sb.AppendLine($"[deco] {t.name}: y={t.position.y:F3} boundsY=[{mr.bounds.min.y:F2}..{mr.bounds.max.y:F2}] " +
                              $"shader='{(m ? m.shader.name : "null")}' queue={(m ? m.renderQueue : -1)} order={mr.sortingOrder}");
            }
        }

        // ── sombras de peces (TvFishShadows) ────────────────────────────────────
        var raiz = GameObject.Find("FishShadows");
        sb.AppendLine($"[pez] raíz FishShadows: {(raiz ? "existe, hijos=" + raiz.transform.childCount : "NO EXISTE")}");
        if (raiz != null)
        {
            var camara = Camera.main;
            foreach (Transform c in raiz.transform)
            {
                var mr = c.GetComponent<MeshRenderer>();
                var mf = c.GetComponent<MeshFilter>();
                var m  = mr != null ? mr.sharedMaterial : null;
                string enPantalla = "n/a";
                if (camara != null)
                {
                    var vp = camara.WorldToViewportPoint(c.position);
                    enPantalla = $"viewport=({vp.x:F2},{vp.y:F2},z={vp.z:F2}) {(vp.x>0&&vp.x<1&&vp.y>0&&vp.y<1&&vp.z>0 ? "DENTRO" : "FUERA")}";
                }
                sb.AppendLine($"[pez]   {c.name}: pos={c.position} escala={c.localScale}");
                sb.AppendLine($"[pez]     renderer enabled={(mr!=null&&mr.enabled)} visible={(mr!=null&&mr.isVisible)} " +
                              $"order={(mr!=null?mr.sortingOrder:-999)} mesh={(mf&&mf.sharedMesh?mf.sharedMesh.name+" verts="+mf.sharedMesh.vertexCount:"NULL")}");
                sb.AppendLine($"[pez]     shader='{(m ? m.shader.name : "NULL")}' queue={(m ? m.renderQueue : -1)} " +
                              $"color={(m ? m.color.ToString("F2") : "n/a")} tex={(m && m.mainTexture ? m.mainTexture.width + "px" : "SIN TEXTURA")}");
                sb.AppendLine($"[pez]     {enPantalla}");
            }
        }

        // ── la cámara ───────────────────────────────────────────────────────────
        var cam = Camera.main;
        if (cam != null)
            sb.AppendLine($"[cam] ortho={cam.orthographic} size={cam.orthographicSize:F2} pos={cam.transform.position} " +
                          $"rot={cam.transform.eulerAngles} near={cam.nearClipPlane} far={cam.farClipPlane}");

        sb.AppendLine("════════ /SHDIAG ════════");
        Debug.Log(sb.ToString());
    }
}
