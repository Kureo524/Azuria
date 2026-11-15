using Windows.Devices.Radios;

namespace Azuria.Shell.Native;

/// <summary>
/// Wi-Fi et Bluetooth allumés ou éteints, par l'API des radios de Windows (celle des réglages
/// rapides). <see cref="Changed"/> se déclenche quand l'état change, d'ici ou d'ailleurs.
/// </summary>
internal sealed class Radios
{
    private readonly SynchronizationContext _ui;
    private readonly Dictionary<RadioKind, Radio> _radios = new();

    public event Action? Changed;

    public Radios() => _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

    public async void Start()
    {
        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed) return;
            foreach (var r in await Radio.GetRadiosAsync())
            {
                if (r.Kind is not (RadioKind.WiFi or RadioKind.Bluetooth) || _radios.ContainsKey(r.Kind)) continue;
                _radios[r.Kind] = r;
                r.StateChanged += (_, _) => _ui.Post(_ => Changed?.Invoke(), null);
            }
            _ui.Post(_ => Changed?.Invoke(), null);
        }
        catch { }
    }

    /// <summary>true / false, ou null si la machine n'a pas cette radio.</summary>
    public bool? State(RadioKind kind) => _radios.TryGetValue(kind, out var r) ? r.State == RadioState.On : null;

    public async void Toggle(RadioKind kind)
    {
        if (!_radios.TryGetValue(kind, out var r)) return;
        try { await r.SetStateAsync(r.State == RadioState.On ? RadioState.Off : RadioState.On); }
        catch { }
    }
}
