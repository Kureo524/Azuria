using System.Text.Json;
using Azuria.Shell.Native;
using Windows.Devices.Radios;

namespace Azuria.Shell;

/// <summary>
/// Alimente le panneau droit : réglages rapides (Wi-Fi, Bluetooth, micro, mode jeu), volume,
/// luminosité, notifications de Windows et actions de session. Les mesures ne tournent que pendant
/// que le panneau est ouvert.
/// </summary>
internal sealed class RightPanelFeed : IDisposable
{
    private readonly GlassWindow _window;
    private readonly Action _quit;
    private readonly Radios _radios = new();
    private readonly Notifications _notices = new();
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 700 };
    private int _ticks;
    private int? _brightness;
    private int _brightnessWanted = -1;
    private bool _brightnessBusy;

    public RightPanelFeed(GlassWindow window, Action quit)
    {
        _window = window;
        _quit = quit;
        _radios.Changed += SendQuick;
        _notices.Changed += SendNotices;
        _tick.Tick += (_, _) =>
        {
            SendQuick();
            if (++_ticks % 4 == 0) ReadBrightness(); // WMI, plus lent : toutes les ~3 s
        };
        window.PresenceChanged += (_, present) =>
        {
            if (present)
            {
                ReadBrightness();
                SendQuick();
                _notices.Refresh();
                _tick.Start();
            }
            else _tick.Stop();
        };
        window.MessageReceived += OnMessage;
        _radios.Start();
        _notices.Start();
    }

    private void OnMessage(string msg)
    {
        string[] p = msg.Split(':');
        switch (p[0])
        {
            case "ready": ReadBrightness(); SendQuick(); SendNotices(); return;
            case "toggle":
                switch (p.ElementAtOrDefault(1))
                {
                    case "wifi": _radios.Toggle(RadioKind.WiFi); break;
                    case "bluetooth": _radios.Toggle(RadioKind.Bluetooth); break;
                    case "mic": if (Audio.Input is { } mic) Audio.SetInputMuted(!mic.Muted); break;
                    case "game": Session.GameMode = !Session.GameMode; break;
                    case "mute": if (Audio.Output is { } o) Audio.SetOutputMuted(!o.Muted); break;
                }
                SendQuick();
                return;
            case "volume":
                if (int.TryParse(p.ElementAtOrDefault(1), out int v)) Audio.SetOutputLevel(v);
                return;
            case "brightness":
                if (int.TryParse(p.ElementAtOrDefault(1), out int b)) SetBrightness(b);
                return;
            case "notif":
                if (p.ElementAtOrDefault(1) == "clear") _notices.Clear();
                else if (p.ElementAtOrDefault(1) == "remove" && uint.TryParse(p.ElementAtOrDefault(2), out uint id)) _notices.Remove(id);
                return;
            case "session":
                RunSession(p.ElementAtOrDefault(1));
                return;
        }
    }

    private void RunSession(string? action)
    {
        _window.Conceal();
        switch (action)
        {
            case "lock": Session.Lock(); break;
            case "sleep": Session.Sleep(); break;
            case "signout": _quit(); Session.SignOut(); break;   // la barre des tâches est rendue avant
            case "restart": _quit(); Session.Restart(); break;
            case "shutdown": _quit(); Session.ShutDown(); break;
            case "quit": _quit(); break;
        }
    }

    // ----- luminosité (WMI, hors du fil d'interface) -----

    private async void ReadBrightness()
    {
        if (_brightnessBusy) return;
        _brightness = await Task.Run(Brightness.Get);
        SendQuick();
    }

    // Le curseur envoie beaucoup de valeurs : on n'applique que la dernière, une à la fois.
    private async void SetBrightness(int level)
    {
        _brightnessWanted = level;
        _brightness = level;
        if (_brightnessBusy) return;
        _brightnessBusy = true;
        try
        {
            while (_brightnessWanted >= 0)
            {
                int l = _brightnessWanted;
                _brightnessWanted = -1;
                await Task.Run(() => Brightness.Set(l));
            }
        }
        finally { _brightnessBusy = false; }
    }

    // ----- vers la page -----

    private void SendQuick()
    {
        var output = Audio.Output;
        var input = Audio.Input;
        var net = Network.Current();
        Post(new
        {
            type = "quick",
            wifi = _radios.State(RadioKind.WiFi),
            wifiName = net.Kind == "wifi" ? net.Name : net.Kind == "ethernet" ? "Ethernet" : "",
            bluetooth = _radios.State(RadioKind.Bluetooth),
            mic = input is { } i ? !i.Muted : (bool?)null,
            game = Session.GameMode,
            volume = output?.Level, muted = output?.Muted ?? false,
            brightness = _brightness,
        });
    }

    private void SendNotices()
    {
        Post(new
        {
            type = "notices",
            access = _notices.Access,
            items = _notices.Items.Select(n => new { id = n.Id, app = n.App, icon = n.Icon, title = n.Title, body = n.Body, time = n.Time }),
        });
    }

    private void Post(object o) => _window.PostJson(JsonSerializer.Serialize(o));

    public void Dispose() => _tick.Dispose();
}
