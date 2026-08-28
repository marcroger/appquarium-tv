using System;
using System.Collections.Generic;
using UnityEngine;

// Wire format for Cast Custom Channel messages.
// These types are SHARED between mobile (sender, encodes) and TV (receiver, decodes).
// The TV project only DECODES — it never encodes. Mobile keeps its own copy in CastManager.cs.

[Serializable]
public class TvFishEntry
{
    public string speciesId;
    public string nickname;
    // uid del pez EN EL MOVIL. Vacio = cliente viejo -> la TV genera uno propio (y entonces el
    // emparejamiento no puede funcionar, porque activePairs referencia los uid del movil).
    public string uid = "";
    // Multiplicador de tamaño por EDAD del pez (0.40 cría / 0.65 juvenil / 1.00 adulto / 1.18 senior).
    // El móvil lo manda = SaveSystem.AgeScaleFactor(GetAgeGroup()). El baseSize de especie lo aplica
    // el receiver aparte. Clientes viejos: campo ausente → ver fallback en OwnedFishSave.GetAgeGroup().
    public float ageScale = 1f;
}

[Serializable]
public class TvAquariumState
{
    public List<TvFishEntry> activeFish     = new List<TvFishEntry>();
    public string            decoJson       = "{}";
    public string            bgId           = "";
    public string            subId          = "";
    public string            lightId        = "light_white";
    public string            ambientMode    = "day";
    public float             fishSpeed      = 1f;
    public string            selectedTankId = "";
    public float             tankHalfWidth  = 0f;  // mobile camera half-width in world units; 0 = old client (no remap)
    public string            castJwt        = "";  // Fase 2: JWT por usuario que emite el Worker. Vacío = el receiver usa su token constante.

    // Parejas activas, por uid del MOVIL. Lista vacia o ausente = sin parejas.
    // ⚠ JsonUtility casa por nombre de CAMPO, no de clase: en el movil la clase se llama
    // TvPairEntry y aqui BreedingPair, y da igual mientras los campos sean maleUid/femaleUid.
    // El movil filtra en origen y solo manda parejas con los DOS miembros en el tanque.
    public List<BreedingPair> activePairs   = new List<BreedingPair>();
}

/// <summary>
/// Payload del UPDATE `pairs`: la lista COMPLETA de parejas activas, no un delta.
/// El wrapper `items` existe porque JsonUtility no deserializa una lista suelta en la raiz.
/// </summary>
[Serializable]
public class TvPairList
{
    public List<BreedingPair> items = new List<BreedingPair>();
}

[Serializable]
public class TvUpdateMessage
{
    public string type;
    public string value;
}

[Serializable]
public class CastMessage
{
    public string type;
    public string payload;
}

[Serializable]
public class DecoPlacementList
{
    public List<DecoPlacement> items = new List<DecoPlacement>();
}

// ── Payloads for real-time Cast UPDATE messages ──────────────────────────────
// Mobile serializes these as JSON into TvUpdateMessage.value.
// TV deserializes them in TvSceneBootstrap to act on the change.

[Serializable]
public class TvAddFishPayload
{
    public string speciesId;
    public string nickname;
    // ⚠ NO es opcional para el emparejamiento: un pez que entra por add_fish a mitad de sesion
    // recibia un uid GENERADO POR LA TV, distinto del del movil, con lo que activePairs no
    // podia referenciarlo jamas. Vacio = cliente viejo -> uid propio, sin emparejar.
    public string uid = "";
    public float  ageScale = 1f;  // ver TvFishEntry.ageScale
}

[Serializable]
public class TvRemoveFishPayload
{
    // ⚠ 2026-08-27 — El camino nuevo. `remove_fish` aceptaba SOLO una cadena suelta con la
    // especie, y por eso quitaba «la primera de esa especie», que casi nunca es la que el
    // usuario quitó. Es ADITIVO: si el valor no es un JSON se sigue tratando como especie.
    public string uid = "";
    // Informativo: sirve para el log y para soltar el bundle sin recorrer el tanque. Si viene
    // vacio se saca del propio pez antes de destruirlo.
    public string speciesId = "";
}

[Serializable]
public class TvAddDecoPayload
{
    public string  instanceId;  // mobile instance key, e.g. "deco_coral_brain_0"
    public string  itemId;      // addressable key, e.g. "deco_coral_brain"
    public Vector3 position;    // world position in tank space
    public float   scaleFactor;
    public bool    flipped;
    public float   rotationY;

    // ⚠⚠ 2026-08-27 — Los seis campos de arriba PIERDEN datos en cuanto el usuario gira una
    // deco. Tras la primera rotacion la verdad ya no esta en `rotationY`: esta en el cuaternion
    // acumulado `DecoPlacement.userRot`, y `rotationY` queda como legacy. Lo encontro la sesion
    // del repo movil (DecorationPlacer.cs:590, :608, :2386).
    //
    // Consecuencia: reemitir `add_deco` con los seis de arriba sincroniza mover, escalar y
    // voltear, pero NO girar, inclinar ni montar — justo los que se ven mal en la tele. Una
    // version parcial seria PEOR que nada: arregla lo que no se nota y deja lo que si.
    //
    // 🧭 Los nombres NO son inventados: son los de `DecoPlacement`, la clase del save que los
    // dos repos comparten, asi que el movil solo tiene que serializar lo que ya tiene en `pd`.
    // Y el camino del INIT (`DecorationPlacer.LoadFromSaveAsync`, :1167-1175) ya los aplica
    // todos desde `decoJson`: esto solo pone al dia el camino del UPDATE, que iba por detras.
    public float   tiltX;
    public bool    hasUserRot;
    public float   quatX, quatY, quatZ, quatW;
    public string  mountedOnInstanceId;
}
