using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azuria.Shell.Native;

namespace Azuria.Shell;

/// <summary>
/// Alimente le tableau de bord : lecteur multimédia en cours, ressources de la machine, météo.
/// Les mesures ne tournent que pendant que le tableau de bord est ouvert (une par seconde) : au
/// repos, il ne coûte rien.
/// </summary>
internal sealed class DashboardFeed : IDisposable
{
    private readonly GlassWindow _window;
    private readonly MediaSession _media = new();
    private readonly Weather _weather = new();
    private readonly SystemStats _stats = new();
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };

    public DashboardFeed(GlassWindow window)
    {
        _window = window;
        _media.Changed += _ => SendMedia();
        _weather.Changed += _ => SendWeather();
        _tick.Tick += (_, _) => SendStats();

        window.PresenceChanged += (_, present) =>
        {
            if (present)
            {
                SendStats();
                _tick.Start();
                _weather.Refresh();
            }
            else _tick.Stop();
        };
        window.MessageReceived += OnMessage;
        _media.Start();
    }

    private void OnMessage(string msg)
    {
        switch (msg)
        {
            case "ready": SendMedia(); SendWeather(); SendStats(); return;
            case "media:toggle": _media.TogglePlay(); return;
            case "media:next": _media.Next(); return;
            case "media:prev": _media.Previous(); return;
            case "weather:location":
                // Réglage de Windows où l'on autorise la localisation.
                try { Process.Start(new ProcessStartInfo("ms-settings:privacy-location") { UseShellExecute = true }); } catch { }
                _window.Conceal();
                return;
        }
        if (msg.StartsWith("media:seek:") &&
            double.TryParse(msg[11..], NumberStyles.Float, CultureInfo.InvariantCulture, out double s))
            _media.Seek(s);
    }

    private void SendMedia()
    {
        var m = _media.State;
        Post(new
        {
            type = "media", present = m.Present, title = m.Title, artist = m.Artist, album = m.Album,
            source = m.Source, cover = m.Cover, playing = m.Playing, canPrev = m.CanPrev, canNext = m.CanNext,
            position = m.Position, duration = m.Duration, at = m.At,
        });
    }

    private void SendWeather()
    {
        var w = (JsonObject)_weather.Current.DeepClone();
        w["type"] = "weather";
        _window.PostJson(w.ToJsonString());
    }

    private void SendStats()
    {
        var s = _stats.Sample();
        Post(new
        {
            type = "stats", cpu = s.Cpu, gpu = s.Gpu, memUsed = s.MemUsed, memTotal = s.MemTotal,
            disk = s.Disk, diskUsed = s.DiskUsed, diskTotal = s.DiskTotal,
            down = s.NetDown, up = s.NetUp, battery = s.Battery, charging = s.Charging, uptime = s.Uptime,
        });
    }

    private void Post(object o) => _window.PostJson(JsonSerializer.Serialize(o));

    public void Dispose()
    {
        _tick.Dispose();
        _stats.Dispose();
    }
}
