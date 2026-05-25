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
        "https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev/bundles/";

    // ── Main MenuItem ─────────────────────────────────────────────────────────

    [MenuItem("Appquarium TV/★ Setup Addressables")]
    public static void SetupAddressables()
    {
        var settings = GetOrCreateSettings();

        // ── Groups ────────────────────────────────────────────────────────────
        var fishGroup  = GetOrCreateRemoteGroup(settings, "Fish_Remote");
        var decoGroup  = GetOrCreateRemoteGroup(settings, "Decos_Remote");
        var envGroup   = GetOrCreateRemoteGroup(settings, "Environments_Remote");
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
        int bgCount = 0;
        var bgGuids = AssetDatabase.FindAssets("t:Texture2D",
            new[] { "Assets/Resources/Backgrounds" });
        foreach (var guid in bgGuids)
        {
            var path    = AssetDatabase.GUIDToAssetPath(guid);
            var address = Path.GetFileNameWithoutExtension(path);
            SetAddress(settings, guid, address, envGroup);
            bgCount++;
        }

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
                  $"  Backgrounds: {bgCount} → Environments_Remote\n" +
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
                  $"Remaining entries: {envGroup.entries.Count} (should be 11 backgrounds).");
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
        if (group != null) return group;

        group = settings.CreateGroup(groupName, false, false, false, null,
            typeof(BundledAssetGroupSchema),
            typeof(ContentUpdateGroupSchema));

        var schema = group.GetSchema<BundledAssetGroupSchema>();
        if (schema != null)
        {
            schema.BuildPath.SetVariableByName(
                settings, AddressableAssetSettings.kRemoteBuildPath);
            schema.LoadPath.SetVariableByName(
                settings, AddressableAssetSettings.kRemoteLoadPath);
            schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
        }

        return group;
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
