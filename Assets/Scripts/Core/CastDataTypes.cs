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
}
