using UnityEngine;

/// <summary>
/// Gestiona el audio ambiental de Appquarium.
/// Tres canales: agua de fondo, burbujas, música.
///
/// Si los ficheros de audio no existen en Resources/Audio/
/// el manager arranca en silencio sin lanzar ningún error.
///
/// Rutas esperadas:
///   Assets/Resources/Audio/ambient_water.(ogg|mp3|wav)
///   Assets/Resources/Audio/ambient_bubbles.(ogg|mp3|wav)
///   Assets/Resources/Audio/ambient_music.(ogg|mp3|wav)
///
/// Setup en escena: crear un GameObject "AudioManager" y adjuntar este script.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    // ── Auto-creación ────────────────────────────────────────────────────────

    /// <summary>
    /// Se ejecuta antes de que cargue cualquier escena.
    /// Crea el GO "AudioManager" automáticamente — no hace falta ponerlo en la escena.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoCreate()
    {
        var go = new GameObject("AudioManager");
        go.AddComponent<AudioManager>();
        // DontDestroyOnLoad se aplica en Awake()
    }

    // ── Configuración de volumen por canal ───────────────────────────────────

    private const float VOL_WATER   = 0.15f;
    private const float VOL_BUBBLES = 0.08f;
    private const float VOL_MUSIC   = 0.22f;

    private const string MUTE_KEY           = "appquarium_muted";
    private const string VOLUME_KEY         = "appquarium_volume";
    private const string AMBIENT_VOLUME_KEY = "appquarium_ambient_volume";
    private const string MUSIC_VOLUME_KEY   = "appquarium_music_volume";

    // ── Estado ───────────────────────────────────────────────────────────────

    public bool  IsMuted       { get; private set; }
    public float Volume        { get; private set; } = 1f;
    public float AmbientVolume { get; private set; } = 1f;
    public float MusicVolume   { get; private set; } = 1f;

    private AudioSource _water;
    private AudioSource _bubbles;
    private AudioSource _music;

    // ── Ciclo de vida ────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        IsMuted       = PlayerPrefs.GetInt(MUTE_KEY, 0) == 1;
        Volume        = PlayerPrefs.GetFloat(VOLUME_KEY, 1f);
        AmbientVolume = PlayerPrefs.GetFloat(AMBIENT_VOLUME_KEY, 1f);
        MusicVolume   = PlayerPrefs.GetFloat(MUSIC_VOLUME_KEY, 1f);

        _water   = CreateChannel("Water");
        _bubbles = CreateChannel("Bubbles");
        _music   = CreateChannel("Music");

        LoadAndPlay(_water,   "Audio/ambient_water",   VOL_WATER);
        LoadAndPlay(_bubbles, "Audio/ambient_bubbles", VOL_BUBBLES);
        LoadAndPlay(_music,   "Audio/ambient_music",   VOL_MUSIC);

        ApplyMute();

        Debug.Log($"[Audio] Iniciado. Muted: {IsMuted}. " +
                  $"Canales activos: {ActiveChannelCount()}/3");
    }

    // ── API pública ──────────────────────────────────────────────────────────

    /// <summary>Silencia o reactiva todos los canales.</summary>
    public void SetMuted(bool muted)
    {
        IsMuted = muted;
        PlayerPrefs.SetInt(MUTE_KEY, muted ? 1 : 0);
        PlayerPrefs.Save();
        ApplyMute();
    }

    /// <summary>Alterna entre silenciado y activo.</summary>
    public void ToggleMute() => SetMuted(!IsMuted);

    /// <summary>
    /// Ajusta el volumen global (0–1).
    /// Volumen 0 = silenciado automático; >0 reactiva el audio.
    /// </summary>
    public void SetVolume(float volume)
    {
        Volume  = Mathf.Clamp01(volume);
        IsMuted = (Volume == 0f);
        PlayerPrefs.SetFloat(VOLUME_KEY, Volume);
        PlayerPrefs.SetInt(MUTE_KEY, IsMuted ? 1 : 0);
        PlayerPrefs.Save();
        ApplyMute();
    }

    /// <summary>Ajusta el volumen ambiental (agua + burbujas) (0-1).</summary>
    public void SetAmbientVolume(float volume)
    {
        AmbientVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(AMBIENT_VOLUME_KEY, AmbientVolume);
        PlayerPrefs.Save();
        ApplyVolumes();
    }

    /// <summary>Ajusta el volumen de la música (0-1).</summary>
    public void SetMusicVolume(float volume)
    {
        MusicVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(MUSIC_VOLUME_KEY, MusicVolume);
        PlayerPrefs.Save();
        ApplyVolumes();
    }

    // ── Helpers internos ─────────────────────────────────────────────────────

    private AudioSource CreateChannel(string channelName)
    {
        var go = new GameObject($"AudioChannel_{channelName}");
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.loop        = true;
        src.playOnAwake = false;
        src.spatialBlend = 0f; // 2D
        return src;
    }

    private static void LoadAndPlay(AudioSource source, string resourcePath, float volume)
    {
        var clip = Resources.Load<AudioClip>(resourcePath);

        if (clip == null)
        {
            // En Android, el audio está en el bundle pack_content_free (fast-follow).
            // El nombre del asset es solo el nombre de fichero sin ruta ni extensión.
            string assetName = System.IO.Path.GetFileNameWithoutExtension(resourcePath);
#if UNITY_EDITOR
            // En Editor: carga directa desde Assets/Audio/ sin necesitar el bundle construido.
            clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/Audio/{assetName}.wav")
                ?? UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/Audio/{assetName}.mp3")
                ?? UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/Audio/{assetName}.ogg");
#else
            clip = AssetBundleLoader.LoadAudioClip("pack_content_free", assetName);
#endif
        }

        if (clip == null)
        {
            Debug.Log($"[Audio] Clip no encontrado: {resourcePath} — canal silencioso hasta que se descargue el pack.");
            return;
        }
        source.clip   = clip;
        source.volume = volume;
        source.Play();
    }

    /// <summary>
    /// Recarga los canales que estén vacíos (sin clip). Llamar tras completar la descarga
    /// del pack fast-follow en AquariumManager para activar el audio en el primer arranque.
    /// </summary>
    public void ReloadAudio()
    {
        if (_water   != null && _water.clip   == null) LoadAndPlay(_water,   "Audio/ambient_water",   VOL_WATER);
        if (_bubbles != null && _bubbles.clip == null) LoadAndPlay(_bubbles, "Audio/ambient_bubbles", VOL_BUBBLES);
        if (_music   != null && _music.clip   == null) LoadAndPlay(_music,   "Audio/ambient_music",   VOL_MUSIC);
        ApplyMute();
    }

    private void ApplyMute() => ApplyVolumes();

    private void ApplyVolumes()
    {
        if (IsMuted)
        {
            SetChannelVolume(_water,   0f);
            SetChannelVolume(_bubbles, 0f);
            SetChannelVolume(_music,   0f);
        }
        else
        {
            SetChannelVolume(_water,   VOL_WATER   * AmbientVolume);
            SetChannelVolume(_bubbles, VOL_BUBBLES * AmbientVolume);
            SetChannelVolume(_music,   VOL_MUSIC   * MusicVolume);
        }
    }

    private static void SetChannelVolume(AudioSource source, float volume)
    {
        if (source == null || source.clip == null) return;
        source.volume = volume;
    }

    private int ActiveChannelCount()
    {
        int n = 0;
        if (_water   != null && _water.clip   != null) n++;
        if (_bubbles != null && _bubbles.clip != null) n++;
        if (_music   != null && _music.clip   != null) n++;
        return n;
    }
}
