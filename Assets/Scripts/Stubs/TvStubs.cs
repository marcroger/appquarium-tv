// TvStubs.cs — Minimal stubs for mobile-only classes not included in the TV receiver build.
// All stubs are no-ops; they exist only to make the codebase compile.
// Do NOT copy this file back to the mobile project.

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ── Save data types ───────────────────────────────────────────────────────────

[Serializable]
public class DecoPlacement
{
    public string instanceId;
    public string itemId;
    public Vector3 position;
    public bool    flipped;
    public float   rotationY;
    public float   tiltX;
    public float   scaleFactor   = 1f;
    public string  mountedOnInstanceId;
    public bool    hasUserRot;
    public float   quatX, quatY, quatZ, quatW;
    // Legacy field — kept for JSON compatibility
    public float   floorYOffset;
    public float   pivotBaseHeight;
}

[Serializable]
public class BreedingPair
{
    public string maleUid;
    public string femaleUid;
}

[Serializable]
public class OwnedFishSave
{
    public string uid;
    public string speciesId;
    public string nickname;
    public float  hunger;
    public float  stress;
    public float  energy   = 1f;
    public float  ageInDays;
    public bool   isBred;
    public string sex;

    public FishAgeGroup GetAgeGroup()
    {
        // In TV mode we always treat fish as adults
        return FishAgeGroup.Adult;
    }
}

[Serializable]
public class ProfileData
{
    public float totalDaysPlayed;
}

[Serializable]
public class SaveData
{
    public string selectedTankId     = "";
    public string selectedBgId       = "";
    public string selectedSubId      = "";
    public string lightPresetId      = "light_white";
    public float  fishSpeedMultiplier = 1f;
    public bool   isPremium;
    public bool   gdprConsentShown;
    public bool   gdprConsentAccepted;
    public bool   onboardingDone;
    public string lastSaveDate;

    public List<OwnedFishSave>  ownedFish       = new List<OwnedFishSave>();
    public List<string>         activeFishUids   = new List<string>();
    public List<DecoPlacement>  decoPositions    = new List<DecoPlacement>();
    public List<string>         activeDecoIds    = new List<string>();
    public List<string>         ownedDecoIds     = new List<string>();
    public List<string>         ownedBgIds       = new List<string>();
    public List<string>         ownedSubIds      = new List<string>();
    public List<string>         ownedLightIds    = new List<string>();
    public List<string>         ownedSpeciesIds  = new List<string>();
    public List<string>         ownedPackIds     = new List<string>();
    public List<BreedingPair>   activePairs      = new List<BreedingPair>();
    public int                  pearls;

    public ProfileData profile = new ProfileData();
}

// ── SaveSystem stub ───────────────────────────────────────────────────────────

public static class SaveSystem
{
    public static SaveData Load()                                   => new SaveData();
    public static void     Save(SaveData data)                      {}
    public static void     DeleteSave()                             {}
    public static void     AddStartingFish(SaveData data, List<FishData> catalog) {}
    public static void     AddFish(SaveData data, string speciesId) {}

    public static float AgeScaleFactor(FishAgeGroup group)
    {
        return group switch
        {
            FishAgeGroup.Cria     => 0.40f,
            FishAgeGroup.Juvenile => 0.65f,
            FishAgeGroup.Senior   => 1.18f,
            _                     => 1.00f,  // Adult
        };
    }
}

// ── UIManager stub ────────────────────────────────────────────────────────────

public class UIManager : MonoBehaviour
{
    public static UIManager Instance => null;   // Not present in TV scene
    public bool IsReady    => true;
    public bool IsAmbientMode => true;

    public void SetAmbientMode(bool on)                               {}
    public void ShowAmbientHintIfNeeded()                             {}
    public void UpdateCastPill(object state)                          {}
    public void RefreshPearlCounter()                                 {}
    public void RefreshShopBadge(SaveData data)                       {}
    public void RefreshOvularioBadge(SaveData data)                   {}
    public void ShowDailyRewardPopup(int pearls, int streakDay)       {}
    public void ShowSeniorGoodbyeModal(OwnedFishSave fish, FishData fd,
                                       Action onLiberate, Action onLater) {}
    public void RunOnboarding(OwnedFishSave firstFish, Action<string> onRename,
                              Action onComplete)                       { onComplete?.Invoke(); }
    public IEnumerator ShowGDPRConsentCoroutine(Action<bool> callback)
    { callback?.Invoke(true); yield break; }

    // Download overlay / banner stubs
    public class DownloadOverlayController
    {
        public void SetProgress(float t, string text) {}
        public IEnumerator CloseAfterDelay(float d) { yield break; }
    }
    public class DownloadBannerController
    {
        public void SetProgress(float t) {}
        public IEnumerator CloseAfterDelay() { yield break; }
    }
    public DownloadOverlayController ShowDownloadOverlay()  => null;
    public DownloadBannerController  ShowDownloadBanner()   => null;
}

// FoodManager — real implementation in TvFoodManager.cs (not a stub)

// ── AdService stub ────────────────────────────────────────────────────────────

public static class AdService
{
    public static void Initialize() {}
}

// ── BreedingManager stub ──────────────────────────────────────────────────────

public class BreedingManager : MonoBehaviour
{
    public static BreedingManager Instance => null;

    public void TickLifecycles(SaveData data)                              {}
    public void CheckBreedingPairs(SaveData data)                          {}
    public void LiberateFish(string uid)                                   {}
    public static bool IsReadyForGoodbye(OwnedFishSave fish, FishData fd)  => false;
}

// ── DailyRewardManager stub ───────────────────────────────────────────────────

public static class DailyRewardManager
{
    public struct RewardResult
    {
        public int pearls;
        public int streakDay;
    }

    public static RewardResult? CheckAndGrant(SaveData data) => null;
}

// ── NotificationService stub ──────────────────────────────────────────────────

public static class NotificationService
{
    public static void Initialize(MonoBehaviour host) {}
    public static void CancelAll()                    {}
    public static void ScheduleAll(SaveData data)     {}
}

// ── AnalyticsService stub ─────────────────────────────────────────────────────

public static class AnalyticsService
{
    public static void AppOpen(bool isPremium, int daysSinceInstall) {}
    public static void OnboardingComplete(string nickname)           {}
}

// ── ReviewPromptManager stub ──────────────────────────────────────────────────

public static class ReviewPromptManager
{
    public static void CheckAndShow(SaveData data)          {}
    public static int  DaysSinceInstall(SaveData data) => 0;
}

// ── AssetBundleLoader stub ────────────────────────────────────────────────────

public static class AssetBundleLoader
{
    public static GameObject LoadPrefab(string bundleName, string assetName) => null;
    public static AudioClip  LoadAudioClip(string bundleName, string assetName) => null;
}

// ── AssetPackManager stub ─────────────────────────────────────────────────────

public static class AssetPackManager
{
    public static bool         IsPackReady(string packName)                     => true;
    public static List<string> BuildRequiredPackListPublic(SaveData data)        => new List<string>();
    public static List<string> BuildBackgroundPackList(SaveData data)            => new List<string>();

    public static IEnumerator CheckOnLaunch(SaveData data,
        Action<string, float> onProgress, Action<string> onStatusText, Action onComplete)
    { onComplete?.Invoke(); yield break; }

    public static IEnumerator DownloadBackgroundPacks(List<string> packs) { yield break; }

    public static IEnumerator DownloadPackOnPurchase(string packName,
        Action<float> onProgress, Action onComplete)
    { onComplete?.Invoke(); yield break; }
}
