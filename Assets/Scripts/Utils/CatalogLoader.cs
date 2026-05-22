using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Carga los catálogos de peces y decoraciones desde JSON en Resources/Data/.
/// Crea instancias de ScriptableObject en runtime — no hace falta asignarlas en el Inspector.
///
/// Uso:
///   CatalogLoader.LoadAll(out List<FishData> fish, out List<DecorationData> decos);
/// </summary>
public static class CatalogLoader
{
    private const string FishCatalogPath = "Data/fish_catalog";
    private const string DecoCatalogPath = "Data/decoration_catalog";

    // ── API pública ──────────────────────────────────────────────────────────

    /// <summary>Carga ambos catálogos y devuelve las listas listas para usar.</summary>
    public static void LoadAll(out List<FishData> fishList, out List<DecorationData> decoList)
    {
        fishList = LoadFishCatalog();
        decoList = LoadDecoCatalog();
    }

    // ── Fish ─────────────────────────────────────────────────────────────────

    public static List<FishData> LoadFishCatalog()
    {
        var list = new List<FishData>();
        var asset = Resources.Load<TextAsset>(FishCatalogPath);
        if (asset == null)
        {
            Debug.LogError("[CatalogLoader] No se encuentra fish_catalog.json en Resources/Data/");
            return list;
        }

        try
        {
            var wrapper = JsonUtility.FromJson<FishCatalogWrapper>(asset.text);
            foreach (var entry in wrapper.fish)
            {
                var so = ScriptableObject.CreateInstance<FishData>();
                so.name            = entry.itemId;
                so.itemId          = entry.itemId;
                so.speciesName     = entry.speciesName;
                so.speciesDescription = entry.description;
                so.isStarterGift   = entry.isStarterGift;
                so.price           = entry.price;
                so.rarity          = ParseEnum<FishRarity>(entry.rarity, FishRarity.Common);
                so.shyness         = entry.shyness;
                so.aggression      = entry.aggression;
                so.curiosity       = entry.curiosity;
                so.sociability     = entry.sociability;
                so.maxSpeed        = entry.maxSpeed;
                so.turnSpeed       = entry.turnSpeed;
                so.perceptionRadius = entry.perceptionRadius;
                so.baseSize        = entry.baseSize;
                so.wanderJitter    = entry.wanderJitter;
                so.wanderRadius    = entry.wanderRadius;
                so.wanderDistance  = entry.wanderDistance;
                so.preferredZone   = ParseEnum<TankZone>(entry.preferredZone, TankZone.MidWater);
                so.zoneAttraction  = entry.zoneAttraction;
                so.hungerRate      = entry.hungerRate;
                so.stressBaseRate  = entry.stressBaseRate;
                so.isNocturnal        = entry.isNocturnal;
                so.pearlPrice         = entry.pearlPrice;
                so.schoolingStrength  = entry.schoolingStrength;
                // prefab: null hasta tener assets 3D reales
                list.Add(so);
            }
            Debug.Log($"[CatalogLoader] {list.Count} peces cargados desde JSON.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[CatalogLoader] Error parseando fish_catalog.json: {e.Message}");
        }
        return list;
    }

    // ── Decorations ──────────────────────────────────────────────────────────

    public static List<DecorationData> LoadDecoCatalog()
    {
        var list = new List<DecorationData>();
        var asset = Resources.Load<TextAsset>(DecoCatalogPath);
        if (asset == null)
        {
            Debug.LogError("[CatalogLoader] No se encuentra decoration_catalog.json en Resources/Data/");
            return list;
        }

        try
        {
            var wrapper = JsonUtility.FromJson<DecoCatalogWrapper>(asset.text);
            foreach (var entry in wrapper.decorations)
            {
                var so = ScriptableObject.CreateInstance<DecorationData>();
                so.name             = entry.itemId;
                so.itemId           = entry.itemId;
                so.itemName         = entry.itemName;
                so.description      = entry.description;
                so.category         = ParseEnum<DecorationCategory>(entry.category, DecorationCategory.Rock);
                so.placement        = ParseEnum<PlacementType>(entry.placement, PlacementType.Floor);
                so.isStarterGift    = entry.isStarterGift;
                so.price            = entry.price;
                so.rarity           = ParseEnum<FishRarity>(entry.rarity, FishRarity.Common);
                so.stressReduction  = entry.stressReduction;
                so.hungerRateBonus  = entry.hungerRateBonus;
                so.generatesBubbles = entry.generatesBubbles;
                so.preventsAlgae    = entry.preventsAlgae;
                so.isHideout        = entry.isHideout;
                so.hasGlow          = entry.hasGlow;
                so.glowColor        = entry.glowColor;
                so.glowIntensity    = entry.glowIntensity;
                so.pearlPrice       = entry.pearlPrice;
                list.Add(so);
            }
            Debug.Log($"[CatalogLoader] {list.Count} decoraciones cargadas desde JSON.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[CatalogLoader] Error parseando decoration_catalog.json: {e.Message}");
        }
        return list;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static T ParseEnum<T>(string value, T fallback) where T : struct, Enum
    {
        if (Enum.TryParse<T>(value, true, out var result))
            return result;
        Debug.LogWarning($"[CatalogLoader] Enum '{typeof(T).Name}' valor desconocido: '{value}' — usando {fallback}");
        return fallback;
    }

    // ── Modelos de deserialización ────────────────────────────────────────────

    [Serializable]
    private class FishCatalogWrapper
    {
        public List<FishEntry> fish = new List<FishEntry>();
    }

    [Serializable]
    private class FishEntry
    {
        public string itemId;
        public string speciesName;
        public string description;
        public bool   isStarterGift;
        public float  price;
        public string rarity;
        public float  shyness;
        public float  aggression;
        public float  curiosity;
        public float  sociability;
        public float  maxSpeed;
        public float  turnSpeed;
        public float  perceptionRadius;
        public float  baseSize;
        public float  wanderJitter;
        public float  wanderRadius;
        public float  wanderDistance;
        public string preferredZone;
        public float  zoneAttraction;
        public float  hungerRate;
        public float  stressBaseRate;
        public bool   isNocturnal;
        public int    pearlPrice;
        public float  schoolingStrength;
    }

    [Serializable]
    private class DecoCatalogWrapper
    {
        public List<DecoEntry> decorations = new List<DecoEntry>();
    }

    [Serializable]
    private class DecoEntry
    {
        public string itemId;
        public string itemName;
        public string description;
        public string category;
        public string placement;
        public bool   isStarterGift;
        public float  price;
        public string rarity;
        public float  stressReduction;
        public float  hungerRateBonus;
        public bool   generatesBubbles;
        public bool   preventsAlgae;
        public bool   isHideout;
        public bool   hasGlow;
        public Color  glowColor = Color.white;
        public float  glowIntensity = 0.8f;
        public int    pearlPrice;
    }
}
