using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Rellena en los <see cref="DecorationData"/> cargados de bundles los campos que sólo existen
/// en el catálogo JSON y que en TV nadie estaba aplicando.
///
/// ⚠ POR QUÉ EXISTE (medido en la tele el 2026-08-17)
/// La bioluminiscencia de los corales llevaba meses sin hacer nada, y una de las tres causas
/// era ésta: `decoration_catalog.json` marca 6 corales con `hasBioLuminescence: true`, pero
/// **`CatalogLoader` no lo llama nadie en el proyecto TV** (grep sobre todo `Assets/Scripts/`).
/// La fuente de verdad en runtime son los SOs de los bundles, y de los 54 **ninguno** tiene el
/// flag a 1 — es más, a 53 de ellos les falta el campo siquiera serializado, así que Unity los
/// deja en el default de C# (`false`) sin avisar de nada.
///
/// Medida que lo demostró: al pasar a noche el agua cayó un 42 % y el coral se movió un −0,2 %.
///
/// Es TV-only a propósito (`SyncFromMobile.ps1` no lo toca): en el móvil este trasvase ya lo
/// hace AquariumManager, que en TV es la versión slim y no lo incluye.
///
/// Sólo se trasvasan los campos que el JSON realmente trae y que el SO no tiene. `embedDepth` y
/// `supportPointLocal` NO están en el JSON (son perillas locales de ajuste, nunca rellenadas),
/// así que aquí no se tocan.
/// </summary>
public static class TvDecoCatalogPatch
{
    private const string CatalogPath = "Data/decoration_catalog";

    private static Dictionary<string, Entrada> _catalogo;

    private struct Entrada
    {
        public bool  hasBioLuminescence;
        public float bioGlowIntensity;
    }

    /// <summary>Aplica el catálogo a una lista de SOs recién cargados. Idempotente.</summary>
    public static void Aplicar(IEnumerable<DecorationData> decos)
    {
        if (decos == null) return;
        int tocados = 0, biolum = 0;
        foreach (var d in decos)
            if (AplicarA(d, out bool conBiolum)) { tocados++; if (conBiolum) biolum++; }

        if (tocados > 0)
            JsBridge.Log($"DecoCatalog: {tocados} decos parcheadas, {biolum} con bioluminiscencia");
    }

    /// <summary>Aplica el catálogo a un solo SO. Devuelve true si se cambió algo.</summary>
    public static bool AplicarA(DecorationData d) => AplicarA(d, out _);

    private static bool AplicarA(DecorationData d, out bool conBiolum)
    {
        conBiolum = false;
        if (d == null) return false;
        if (!Cargar().TryGetValue(d.itemId, out var e)) return false;

        bool cambio = false;
        if (d.hasBioLuminescence != e.hasBioLuminescence)
        {
            d.hasBioLuminescence = e.hasBioLuminescence;
            cambio = true;
        }
        // El JSON trae 0 en las decos que no fluorescentan; sólo pisar con un valor útil,
        // que si no nos quedamos sin el default de 1.5f del propio DecorationData.
        if (e.bioGlowIntensity > 0f && !Mathf.Approximately(d.bioGlowIntensity, e.bioGlowIntensity))
        {
            d.bioGlowIntensity = e.bioGlowIntensity;
            cambio = true;
        }

        conBiolum = d.hasBioLuminescence;
        return cambio;
    }

    private static Dictionary<string, Entrada> Cargar()
    {
        if (_catalogo != null) return _catalogo;
        _catalogo = new Dictionary<string, Entrada>();

        var asset = Resources.Load<TextAsset>(CatalogPath);
        if (asset == null)
        {
            // Un fallo que sólo se ve por Debug.Log es invisible: el canal Cast NO lo transporta.
            JsBridge.Log($"⚠ DecoCatalog: no encuentro Resources/{CatalogPath}.json — biolum OFF");
            return _catalogo;
        }

        try
        {
            // ⚠ El fichero está en UTF-8 CON BOM y `JsonUtility.FromJson` casca con el BOM
            // delante. `CatalogLoader` tiene el mismo agujero, pero como nadie lo llama nunca
            // salió a la luz.
            var texto = asset.text.TrimStart('\uFEFF', '\u200B').Trim();
            var wrapper = JsonUtility.FromJson<Wrapper>(texto);
            if (wrapper?.decorations == null)
            {
                JsBridge.Log("⚠ DecoCatalog: JSON sin 'decorations' — biolum OFF");
                return _catalogo;
            }

            foreach (var e in wrapper.decorations)
            {
                if (string.IsNullOrEmpty(e.itemId)) continue;
                _catalogo[e.itemId] = new Entrada
                {
                    hasBioLuminescence = e.hasBioLuminescence,
                    bioGlowIntensity   = e.bioGlowIntensity,
                };
            }
        }
        catch (Exception ex)
        {
            JsBridge.Log($"⚠ DecoCatalog: error parseando el JSON ({ex.Message}) — biolum OFF");
        }
        return _catalogo;
    }

    // Clases propias en vez de reutilizar las de `CatalogLoader`: ese fichero se sincroniza
    // desde el móvil y no quiero que un sync se lleve por delante este trasvase.
    [Serializable] private class Wrapper { public Entry[] decorations; }

    [Serializable]
    private class Entry
    {
        public string itemId;
        public bool   hasBioLuminescence;
        public float  bioGlowIntensity;
    }
}
