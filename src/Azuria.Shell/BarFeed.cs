using System.Text.Json;
using Azuria.Shell.Native;
using Windows.Devices.Radios;

namespace Azuria.Shell;

/// <summary>
/// Alimente la barre gauche : bureaux virtuels (liste, bureau courant, changement, création) et
/// icônes d'état (réseau, son, batterie). Relu chaque seconde, seulement barre ouverte. Les réglages
/// se font sur place, dépliés sous leur icône : volume et son coupé, Wi-Fi et Bluetooth.
/// </summary>
internal sealed class BarFeed : IDisposable
{
    private readonly GlassWindow _bar;
    private readonly Radios _radios = new();
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };
    private string _last = "";

    public BarFeed(GlassWindow bar)
    {
        _bar = bar;
        _radios.Changed += () => { if (_bar.Visible) Send(); };
        _radios.Start();
        _tick.Tick += (_, _) => Send();
        bar.PresenceChanged += (_, present) =>
        {
            if (present) { Send(force: true); _tick.Start(); }
            else _tick.Stop();
        };
        bar.MessageReceived += msg =>
        {
            if (msg == "status") Send(force: true);                      // la page est prête
            else if (msg == "desk:new") { VirtualDesktops.Create(); Later(); }
            else if (msg.StartsWith("desk:") && int.TryParse(msg[5..], out int i)) { VirtualDesktops.SwitchTo(i); Later(); }
            else if (msg.StartsWith("volume:") && int.TryParse(msg[7..], out int v)) Audio.SetOutputLevel(v);
            else if (msg == "toggle:mute") { if (Audio.Output is { } o) Audio.SetOutputMuted(!o.Muted); Send(); }
            else if (msg == "toggle:wifi") _radios.Toggle(RadioKind.WiFi);
            else if (msg == "toggle:bluetooth") _radios.Toggle(RadioKind.Bluetooth);
        };
    }

    // Après un changement de bureau, l'Explorateur met un instant à noter le nouveau bureau.
    private async void Later()
    {
        await Task.Delay(250);
        Send();
    }

    private void Send(bool force = false)
    {
        var (count, current) = VirtualDesktops.State();
        var net = Network.Current();
        var output = Audio.Output;
        var (battery, charging) = SystemStats.Battery();
        string json = JsonSerializer.Serialize(new
        {
            type = "status",
            desks = new { count, current },
            net = new { kind = net.Kind, name = net.Name, signal = net.Signal },
            volume = output?.Level, muted = output?.Muted ?? false,
            wifi = _radios.State(RadioKind.WiFi), bluetooth = _radios.State(RadioKind.Bluetooth),
            battery, charging,
        });
        if (!force && json == _last) return; // rien n'a changé
        _last = json;
        _bar.PostJson(json);
    }

    public void Dispose() => _tick.Dispose();
}
