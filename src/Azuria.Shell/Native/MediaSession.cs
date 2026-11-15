using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Azuria.Shell.Native;

/// <summary>Ce qui est en lecture, tel que le tableau de bord l'affiche.</summary>
internal sealed record MediaState(
    bool Present, string Title, string Artist, string Album, string Source, string Cover,
    bool Playing, bool CanPrev, bool CanNext, double Position, double Duration, long At);

/// <summary>
/// Le lecteur multimédia en cours (Spotify, navigateur, lecteur de Windows…), lu par l'API publique
/// de Windows qui alimente aussi les contrôles de volume (<c>GlobalSystemMediaTransportControls</c>).
/// Lecture et commandes (lecture/pause, précédent, suivant, position) passent par cette API : rien
/// n'est injecté dans le lecteur.
/// </summary>
internal sealed class MediaSession
{
    private readonly SynchronizationContext _ui;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string _cover = "";
    private string _coverKey = "";

    /// <summary>L'état a changé (sur le fil d'interface).</summary>
    public event Action<MediaState>? Changed;

    public MediaState State { get; private set; } = Empty;

    private static readonly MediaState Empty = new(false, "", "", "", "", "", false, false, false, 0, 0, 0);

    public MediaSession()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
    }

    public async void Start()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => _ui.Post(_ => Attach(), null);
            Attach();
        }
        catch { /* API indisponible : le tableau de bord affiche « Rien en lecture » */ }
    }

    private void Attach()
    {
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnChanged;
            _session.PlaybackInfoChanged -= OnChanged;
            _session.TimelinePropertiesChanged -= OnChanged;
        }
        _session = _manager?.GetCurrentSession();
        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnChanged;
            _session.PlaybackInfoChanged += OnChanged;
            _session.TimelinePropertiesChanged += OnChanged;
        }
        Refresh();
    }

    private void OnChanged<T>(GlobalSystemMediaTransportControlsSession s, T _) => _ui.Post(_ => Refresh(), null);

    private async void Refresh()
    {
        var s = _session;
        if (s is null) { Publish(Empty); return; }
        try
        {
            var props = await s.TryGetMediaPropertiesAsync();
            var info = s.GetPlaybackInfo();
            var time = s.GetTimelineProperties();
            if (s != _session) return; // une autre session a pris la main entre-temps

            string key = $"{props.Title}|{props.Artist}|{props.AlbumTitle}";
            if (key != _coverKey)
            {
                _coverKey = key;
                _cover = await Cover(props.Thumbnail);
            }

            bool playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            // Position au moment de la dernière mise à jour du lecteur, avancée jusqu'à maintenant.
            double pos = time.Position.TotalSeconds;
            if (playing) pos += (DateTimeOffset.Now - time.LastUpdatedTime).TotalSeconds;
            double dur = (time.EndTime - time.StartTime).TotalSeconds;

            Publish(new MediaState(true, props.Title ?? "", props.Artist ?? "", props.AlbumTitle ?? "",
                SourceName(s.SourceAppUserModelId), _cover, playing,
                info.Controls.IsPreviousEnabled, info.Controls.IsNextEnabled,
                Math.Clamp(pos, 0, Math.Max(dur, 0)), Math.Max(dur, 0), DateTimeOffset.Now.ToUnixTimeMilliseconds()));
        }
        catch { Publish(Empty); }
    }

    private void Publish(MediaState state)
    {
        State = state;
        Changed?.Invoke(state);
    }

    // Pochette en image intégrée à la page (data URL).
    private static async Task<string> Cover(IRandomAccessStreamReference? thumb)
    {
        if (thumb is null) return "";
        try
        {
            using var stream = await thumb.OpenReadAsync();
            using var input = stream.AsStreamForRead();
            using var ms = new MemoryStream();
            await input.CopyToAsync(ms);
            if (ms.Length == 0 || ms.Length > 4_000_000) return "";
            string type = string.IsNullOrEmpty(stream.ContentType) ? "image/png" : stream.ContentType;
            return $"data:{type};base64,{Convert.ToBase64String(ms.ToArray())}";
        }
        catch { return ""; }
    }

    // « Spotify.exe » → « Spotify » ; « MSEdge » → « Edge » ; identifiant d'appli du Store → son nom court.
    private static string SourceName(string aumid)
    {
        if (string.IsNullOrEmpty(aumid)) return "";
        string s = aumid;
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        int bang = s.IndexOf('!');
        if (bang > 0) s = s[..bang];
        int under = s.IndexOf('_');
        if (under > 0) s = s[..under];
        int dot = s.LastIndexOf('.');
        if (dot >= 0 && dot < s.Length - 1) s = s[(dot + 1)..];
        return s switch
        {
            "MSEdge" => "Edge",
            "308046B0AF4A39CB" => "Firefox",
            "ZuneMusic" => "Lecteur multimédia",
            "ZuneVideo" => "Films et TV",
            _ => s,
        };
    }

    // ----- commandes -----

    public async void TogglePlay() { try { if (_session is { } s) await s.TryTogglePlayPauseAsync(); } catch { } }
    public async void Next() { try { if (_session is { } s) await s.TrySkipNextAsync(); } catch { } }
    public async void Previous() { try { if (_session is { } s) await s.TrySkipPreviousAsync(); } catch { } }

    public async void Seek(double seconds)
    {
        try
        {
            if (_session is not { } s) return;
            var t = s.GetTimelineProperties();
            long ticks = (t.StartTime + TimeSpan.FromSeconds(Math.Max(0, seconds))).Ticks;
            await s.TryChangePlaybackPositionAsync(ticks);
        }
        catch { }
    }
}
