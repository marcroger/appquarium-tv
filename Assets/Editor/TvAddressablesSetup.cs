using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

/// <summary>
/// One-click Addressables setup for the Cast TV receiver.
/// Run "Appquarium TV → ★ Setup Addressables" once after installing the package.
/// </summary>
public static class TvAddressablesSetup
{
    private const string R2_LOAD_URL =
        "https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev/bundles";

    // ── Main MenuItem ─────────────────────────────────────────────────────────

    [MenuItem("Appquarium TV/★ Setup Addressables")]
    public static void SetupAddressables()
    {
        var settings = GetOrCreateSettings();
        // Always ensure the Remote Load URL has no trailing slash (prevents bundles// double-slash in settings.json).
        var profileId = settings.activeProfileId;
        settings.profileSettings.SetValue(profileId, AddressableAssetSettings.kRemoteLoadPath, R2_LOAD_URL);
        EditorUtility.SetDirty(settings);

        // ── Groups ────────────────────────────────────────────────────────────
        var fishGroup  = GetOrCreateRemoteGroup(settings, "Fish_Remote");
        var decoGroup  = GetOrCreateRemoteGroup(settings, "Decos_Remote");
        // El grupo se sigue creando aunque ya no reciba entradas: existe en disco, tiene sus
        // schemas y un grupo vacio no produce bundle (igual que `Default Local Group`).
        GetOrCreateRemoteGroup(settings, "Environments_Remote");
        var audioGroup = GetOrCreateRemoteGroup(settings, "Audio_Remote");

        // ── Fish SOs ──────────────────────────────────────────────────────────
        int fishCount = 0;
        var fishSOs = AssetDatabase.FindAssets("t:FishData",
            new[] { "Assets/ScriptableObjects/Fish" });
        foreach (var guid in fishSOs)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var so   = AssetDatabase.LoadAssetAtPath<FishData>(path);
            if (so == null || string.IsNullOrEmpty(so.itemId)) continue;
            SetAddress(settings, guid, so.itemId, fishGroup);
            fishCount++;
        }

        // ── Decoration SOs ────────────────────────────────────────────────────
        int decoCount = 0;
        var decoSOs = AssetDatabase.FindAssets("t:DecorationData",
            new[] { "Assets/ScriptableObjects/Decorations" });
        foreach (var guid in decoSOs)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var so   = AssetDatabase.LoadAssetAtPath<DecorationData>(path);
            if (so == null || string.IsNullOrEmpty(so.itemId)) continue;
            SetAddress(settings, guid, so.itemId, decoGroup);
            decoCount++;
        }

        // ── Background textures ───────────────────────────────────────────────
        // NOTE (2026-08-18): los 11 fondos YA NO se hacen addressables. Viajaban DOS veces
        // --horneados en el .data via Assets/Resources/Backgrounds/ y ademas como 11 bundles
        // remotos-- y esos bundles no los pedia NADIE: la unica ruta de carga en runtime es
        // Resources.Load<Texture2D>("Backgrounds/" + bgId), tanto en el arranque
        // (TankBackground.cs:296) como en el UPDATE change_bg (TankBackground.cs:207).
        // Verificado: TvSceneBootstrap solo hace LoadAssetAsync de FishData y DecorationData.
        //
        // Medido antes de quitarlos: los 11 bundles pesaban 0,52 MB en R2 (peso muerto puro,
        // nunca descargado). La copia del .data son ~0,7 MB y SI se usa, asi que se queda.
        //
        // ⚠ Este bucle es el motivo por el que no bastaba con borrar las entradas del grupo:
        // las volvia a crear en el siguiente `★ Setup Addressables`. Para deshacer el cambio
        // hay que restaurar este bucle, no solo re-anadir las entradas a mano.
        // Para limpiar las que ya existan: `★ Prune Environments_Remote (fondos muertos)`.
        int bgCount = 0;

        // ── Substrate textures ────────────────────────────────────────────────
        // NOTE: Substrate textures are intentionally NOT added as addressables.
        // The 12 substrate DecorationData SOs (sub_*) are already addressable in
        // Decos_Remote, and adding the textures here would create duplicate
        // addresses across groups (sub_sand SO + sub_sand PNG) which triggers
        // Addressables' dedup assertion `pred(*previous, *i)` during Build Player
        // Content. In Phase A the textures continue loading via Resources.Load
        // at runtime; in Phase B they'll move out of Resources/ and become
        // proper sub-deps of the substrate SO.
        int subCount = 0;

        // ── Audio ─────────────────────────────────────────────────────────────
        int audioCount = 0;
        var audioGuids = AssetDatabase.FindAssets("t:AudioClip",
            new[] { "Assets/Resources/Audio" });
        foreach (var guid in audioGuids)
        {
            var path    = AssetDatabase.GUIDToAssetPath(guid);
            var address = Path.GetFileNameWithoutExtension(path);
            SetAddress(settings, guid, address, audioGroup);
            audioCount++;
        }

        AssetDatabase.SaveAssets();

        Debug.Log($"[TvAddressables] ✅ Setup complete:\n" +
                  $"  Fish:        {fishCount} SOs → Fish_Remote\n" +
                  $"  Decos:       {decoCount} SOs → Decos_Remote\n" +
                  $"  Backgrounds: {bgCount} → Environments_Remote (0 a proposito: van por Resources)\n" +
                  $"  Substrates:  {subCount} → Environments_Remote\n" +
                  $"  Audio:       {audioCount} → Audio_Remote\n\n" +
                  $"Next: Window → Asset Management → Addressables → Groups → Build → New Build → Default Build Script");
    }

    [MenuItem("Appquarium TV/★ Clean Substrate Duplicates")]
    public static void CleanSubstrateDuplicates()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogWarning("No Addressables settings found."); return; }

        var envGroup = settings.FindGroup("Environments_Remote");
        if (envGroup == null) { Debug.LogWarning("Environments_Remote group not found."); return; }

        // Substrate texture GUIDs (Assets/Resources/Substrates/sub_*.png).
        // These collide by address with DecorationData SOs of the same itemId
        // already addressable in Decos_Remote → triggers the dedup assertion.
        var subTexGuids = AssetDatabase.FindAssets("t:Texture2D",
            new[] { "Assets/Resources/Substrates" });

        var subTexGuidSet = new HashSet<string>(subTexGuids);
        var toRemove = new List<AddressableAssetEntry>();
        foreach (var entry in envGroup.entries)
            if (subTexGuidSet.Contains(entry.guid)) toRemove.Add(entry);

        foreach (var entry in toRemove)
            settings.RemoveAssetEntry(entry.guid, false);

        EditorUtility.SetDirty(envGroup);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();

        Debug.Log($"[TvAddressables] ✅ Removed {toRemove.Count} substrate texture duplicates from Environments_Remote. " +
                  $"Remaining entries: {envGroup.entries.Count} (desde el 2026-08-18 deberian ser 0: los fondos van por Resources).");
    }

    [MenuItem("Appquarium TV/★ Fix Bundle Mode (PackTogether)")]
    public static void FixBundleMode()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogWarning("No Addressables settings found. Run ★ Setup Addressables first."); return; }

        var remoteGroups = new[] { "Fish_Remote", "Decos_Remote", "Environments_Remote", "Audio_Remote" };
        foreach (var groupName in remoteGroups)
        {
            var group = settings.FindGroup(groupName);
            if (group == null) { Debug.LogWarning($"Group not found: {groupName}"); continue; }
            var schema = group.GetSchema<BundledAssetGroupSchema>();
            if (schema == null) continue;
            schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
            EditorUtility.SetDirty(schema);
            Debug.Log($"[TvAddressables] {groupName} → PackTogether ✅");
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[TvAddressables] ✅ All remote groups set to PackTogether.\nNext: Build → New Build → Default Build Script");
    }

    [MenuItem("Appquarium TV/★ Set Bundle Mode (PackSeparately)")]
    public static void SetBundleModePackSeparately()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogWarning("No Addressables settings found."); return; }

        var remoteGroups = new[] { "Fish_Remote", "Decos_Remote", "Environments_Remote", "Audio_Remote" };
        foreach (var groupName in remoteGroups)
        {
            var group = settings.FindGroup(groupName);
            if (group == null) { Debug.LogWarning($"Group not found: {groupName}"); continue; }
            var schema = group.GetSchema<BundledAssetGroupSchema>();
            if (schema == null) continue;
            schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
            EditorUtility.SetDirty(schema);
            Debug.Log($"[TvAddressables] {groupName} → PackSeparately ✅");
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[TvAddressables] ✅ All remote groups set to PackSeparately (1 bundle per asset).");
    }

    /// <summary>
    /// Assigns FishData.prefab for all 25 fish SOs by matching assetBundleAssetName
    /// to the prefab filename in Assets/ThirdParty/Mikhail Nesterov/.
    /// Run this once, then rebuild Fish_Remote bundles (Build → Update a Previous Build).
    /// NOTE: must re-run after each mobile sync that overwrites FishData SOs.
    /// </summary>
    [MenuItem("Appquarium TV/★ Assign Fish Prefabs")]
    public static void AssignFishPrefabs()
    {
        var searchPaths = new[] { "Assets/ThirdParty/Mikhail Nesterov" };

        var fishSOs = AssetDatabase.FindAssets("t:FishData",
            new[] { "Assets/ScriptableObjects/Fish" });

        int assigned = 0, alreadySet = 0, notFound = 0;
        foreach (var guid in fishSOs)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var so   = AssetDatabase.LoadAssetAtPath<FishData>(path);
            if (so == null || string.IsNullOrEmpty(so.assetBundleAssetName)) continue;

            if (so.prefab != null) { alreadySet++; continue; }

            var prefabGuids = AssetDatabase.FindAssets(
                $"t:Prefab {so.assetBundleAssetName}", searchPaths);

            if (prefabGuids.Length == 0)
            {
                Debug.LogWarning($"[FishPrefabs] ✗ No prefab for {so.itemId} ({so.assetBundleAssetName})");
                notFound++;
                continue;
            }

            var prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[0]);
            var prefab     = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) { notFound++; continue; }

            so.prefab = prefab;
            EditorUtility.SetDirty(so);
            Debug.Log($"[FishPrefabs] ✅ {so.itemId} → {System.IO.Path.GetFileName(prefabPath)}");
            assigned++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[FishPrefabs] Done: {assigned} assigned, {alreadySet} already set, {notFound} not found.\n" +
                  "Next: Window → Asset Management → Addressables → Groups → Build → New Build → Default Build Script");
    }

    /// <summary>
    /// Removes all fish from Fish_Remote except banggai_cardinalfish.
    /// Use to do a fast 1-fish test build (~10-15 min). Restore with ★ Setup Addressables.
    /// </summary>
    [MenuItem("Appquarium TV/★ Test: Isolate Banggai (1 pez)")]
    public static void IsolateBanggai()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogWarning("No Addressables settings found."); return; }

        var fishGroup = settings.FindGroup("Fish_Remote");
        if (fishGroup == null) { Debug.LogWarning("Fish_Remote group not found."); return; }

        var toRemove = new List<AddressableAssetEntry>();
        foreach (var entry in fishGroup.entries)
            if (entry.address != "fish_banggai_cardinalfish")
                toRemove.Add(entry);

        foreach (var entry in toRemove)
            settings.RemoveAssetEntry(entry.guid, false);

        EditorUtility.SetDirty(fishGroup);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();

        Debug.Log($"[TvAddressables] ✅ Fish_Remote aislado: {toRemove.Count} peces eliminados, solo fish_banggai_cardinalfish queda.\n" +
                  "Next: Build → New Build → Default Build Script\n" +
                  "Para restaurar: Appquarium TV → ★ Setup Addressables");
    }

    /// <summary>
    /// Adds a second fish (moorish_idol) to Fish_Remote alongside banggai.
    /// Use to verify SBP incremental cache: second New Build should take ~same time as first,
    /// not double, because banggai's assets are already cached.
    /// </summary>
    [MenuItem("Appquarium TV/★ Test: Add Moorish Idol (2 peces)")]
    public static void AddMoorishIdol()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogWarning("No Addressables settings found."); return; }

        var fishGroup = settings.FindGroup("Fish_Remote");
        if (fishGroup == null) { Debug.LogWarning("Fish_Remote group not found."); return; }

        var fishSOs = AssetDatabase.FindAssets("t:FishData fish_moorish_idol",
            new[] { "Assets/ScriptableObjects/Fish" });

        if (fishSOs.Length == 0) { Debug.LogWarning("[TvAddressables] fish_moorish_idol SO not found."); return; }

        var path = AssetDatabase.GUIDToAssetPath(fishSOs[0]);
        var so   = AssetDatabase.LoadAssetAtPath<FishData>(path);
        if (so == null) { Debug.LogWarning("[TvAddressables] Could not load fish_moorish_idol FishData."); return; }

        SetAddress(settings, fishSOs[0], so.itemId, fishGroup);
        EditorUtility.SetDirty(fishGroup);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();

        Debug.Log($"[TvAddressables] ✅ Fish_Remote ahora tiene 2 peces: fish_banggai_cardinalfish + {so.itemId}\n" +
                  "Next: Appquarium TV → ★ New Build (Default Build Script)\n" +
                  "Si el build tarda ~2h (no ~4h) → SBP cache incremental funciona ✅");
    }

    /// <summary>
    /// Configures remote catalog so New Build generates catalog.json in ServerData/WebGL/.
    /// Run once, then do one WebGL player rebuild to embed the remote catalog URL in the player.
    /// After that: bundle changes only need New Build + deploy bundles+catalog — no player rebuild ever again.
    /// </summary>
    [MenuItem("Appquarium TV/★ Fix Remote Catalog")]
    public static void FixRemoteCatalog()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogWarning("No Addressables settings found."); return; }

        settings.BuildRemoteCatalog = true;
        settings.RemoteCatalogBuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
        settings.RemoteCatalogLoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();

        Debug.Log("[TvAddressables] ✅ Remote catalog configurado:\n" +
                  $"  BuildPath → ServerData/[BuildTarget] (= ServerData/WebGL)\n" +
                  $"  LoadPath  → {R2_LOAD_URL}\n\n" +
                  "Próximos pasos:\n" +
                  "  1. ★ New Build  → genera catalog.json + catalog.hash en ServerData/WebGL/\n" +
                  "  2. Build WebGL Player (una sola vez) → embebe URL del catálogo remoto\n" +
                  "  3. Deploy todo a R2\n" +
                  "  Tras esto: futuros builds solo necesitan New Build + deploy bundles+catalog");
    }

    /// <summary>
    /// Triggers New Build → Default Build Script programmatically.
    /// Equivalent to Window → Asset Management → Addressables → Groups → Build → New Build → Default Build Script.
    /// MCP will timeout but the build continues in Unity.
    /// </summary>
    [MenuItem("Appquarium TV/★ New Build (Default Build Script)")]
    public static void TriggerNewBuild()
    {
        // Ensure remote catalog is always configured before building.
        // This generates catalog.json + catalog.hash in ServerData/WebGL/ alongside bundles.
        // After one WebGL player rebuild, future bundle updates never need a player rebuild.
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings != null)
        {
            settings.BuildRemoteCatalog = true;
            settings.RemoteCatalogBuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
            settings.RemoteCatalogLoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log("[TvAddressables] Remote catalog configurado → generará catalog.json en ServerData/WebGL/");
        }

        // Force reimport of any externally-modified assets (e.g. .mat files edited outside Unity)
        // before SBP computes its cache keys. Without this, material changes may be missed.
        AssetDatabase.Refresh();

        Debug.Log("[TvAddressables] Lanzando New Build → Default Build Script...");
        AddressableAssetSettings.BuildPlayerContent();
        Debug.Log("[TvAddressables] ✅ Build completado. Verificar ServerData/WebGL/ para los bundles.");
    }

    [MenuItem("Appquarium TV/★ Print Addressables Summary")]
    public static void PrintSummary()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogWarning("No Addressables settings found."); return; }

        int total = 0;
        foreach (var group in settings.groups)
        {
            if (group == null) continue;
            Debug.Log($"[Group] {group.Name}: {group.entries.Count} entries");
            total += group.entries.Count;
        }
        Debug.Log($"[TvAddressables] Total addressable entries: {total}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AddressableAssetSettings GetOrCreateSettings()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings != null) return settings;

        settings = AddressableAssetSettings.Create(
            AddressableAssetSettingsDefaultObject.kDefaultConfigFolder,
            AddressableAssetSettingsDefaultObject.kDefaultConfigAssetName,
            true, true);

        // Remote catalog so devices always get the latest bundle locations
        settings.BuildRemoteCatalog = true;

        // Configure the Remote profile variable with the R2 URL
        var profileId = settings.activeProfileId;
        settings.profileSettings.SetValue(profileId,
            AddressableAssetSettings.kRemoteLoadPath, R2_LOAD_URL);

        AddressableAssetSettingsDefaultObject.Settings = settings;
        Debug.Log("[TvAddressables] Created Addressables settings.");
        return settings;
    }

    private static AddressableAssetGroup GetOrCreateRemoteGroup(
        AddressableAssetSettings settings, string groupName)
    {
        var group = settings.FindGroup(groupName);
        if (group == null)
        {
            group = settings.CreateGroup(groupName, false, false, false, null,
                typeof(BundledAssetGroupSchema),
                typeof(ContentUpdateGroupSchema));
        }

        var schema = group.GetSchema<BundledAssetGroupSchema>();
        if (schema != null)
        {
            schema.BuildPath.SetVariableByName(
                settings, AddressableAssetSettings.kRemoteBuildPath);
            schema.LoadPath.SetVariableByName(
                settings, AddressableAssetSettings.kRemoteLoadPath);
            schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
            // Always ensure remote groups are included in builds.
            // m_IncludeInBuild can silently become 0 (e.g. after NonRecursiveBuilding experiments),
            // which causes the group to produce no bundles and no catalog entries.
            schema.IncludeInBuild = true;
            EditorUtility.SetDirty(schema);
        }

        return group;
    }

    // ── Fondos: quitar las entradas muertas ───────────────────────────────────

    [MenuItem("Appquarium TV/★ Prune Environments_Remote (fondos muertos)")]
    public static void PruneEnvironments() => PruneEnvironments(preguntar: true);

    /// Entrada para batchmode (`-executeMethod TvAddressablesSetup.PruneEnvironmentsBatch`).
    public static void PruneEnvironmentsBatch() => PruneEnvironments(preguntar: false);

    /// <summary>
    /// Borra del grupo `Environments_Remote` las entradas de Assets/Resources/Backgrounds/.
    /// Esos 11 bundles (0,52 MB en R2, medido el 2026-08-18) no los descargaba nadie: los fondos
    /// se cargan SIEMPRE por `Resources.Load` (ver la nota en `SetupAddressables`). Reversible:
    /// restaurar el bucle de fondos en `SetupAddressables` y volver a ejecutarlo.
    ///
    /// ⚠ Deja el grupo VACIO a proposito en vez de borrarlo: un grupo sin entradas no produce
    /// bundle, y asi el .asset y sus schemas siguen versionados por si hay que volver atras.
    /// </summary>
    private static void PruneEnvironments(bool preguntar)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogError("[TvAddressables] No hay settings de Addressables."); return; }

        var envGroup = settings.FindGroup("Environments_Remote");
        if (envGroup == null) { Debug.LogWarning("[TvAddressables] No existe Environments_Remote."); return; }

        // Filtrar por RUTA, no por el grupo entero: si algun dia entra ahi otra cosa que si se
        // pida por Addressables, este menu no debe llevarsela por delante.
        var aBorrar = new List<AddressableAssetEntry>();
        foreach (var entry in envGroup.entries)
        {
            var ruta = AssetDatabase.GUIDToAssetPath(entry.guid);
            if (!string.IsNullOrEmpty(ruta) && ruta.StartsWith("Assets/Resources/Backgrounds/"))
                aBorrar.Add(entry);
        }

        if (aBorrar.Count == 0)
        { Debug.Log("[TvAddressables] Environments_Remote ya no tiene fondos. Nada que hacer."); return; }

        if (preguntar && !EditorUtility.DisplayDialog("Quitar fondos de Addressables",
                $"Se van a quitar {aBorrar.Count} fondos de Environments_Remote.\n\n" +
                "Se cargan por Resources.Load, asi que sus bundles son peso muerto en R2.\n" +
                "Requiere ★ New Build + deploy del catalogo para que R2 lo refleje.\n\n¿Seguir?",
                "Quitar", "Cancelar")) return;

        foreach (var entry in aBorrar)
            settings.RemoveAssetEntry(entry.guid, false);

        EditorUtility.SetDirty(envGroup);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();

        Debug.Log($"[TvAddressables] ✅ Quitados {aBorrar.Count} fondos de Environments_Remote. " +
                  $"Quedan {envGroup.entries.Count} entradas (deberian ser 0).\n" +
                  "Siguiente: ★ New Build y subir bundles + catalog. Los .bundle viejos de los " +
                  "fondos quedan huerfanos en R2: limpiarlos con `python Tools/r2_huerfanos.py --borrar`.");
    }

    private static void SetAddress(
        AddressableAssetSettings settings,
        string guid,
        string address,
        AddressableAssetGroup group)
    {
        var entry = settings.FindAssetEntry(guid)
                    ?? settings.CreateOrMoveEntry(guid, group, false, false);
        settings.MoveEntry(entry, group, false, false);
        entry.address = address;
    }
}
