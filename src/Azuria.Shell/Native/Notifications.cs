using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Azuria.Shell.Native;

/// <summary>Une notification, telle que le panneau droit l'affiche.</summary>
internal sealed record Notice(uint Id, string App, string Icon, string Title, string Body, long Time);

/// <summary>
/// Les notifications de Windows (celles du centre de notifications), lues par l'écouteur officiel
/// (<c>UserNotificationListener</c>), avec l'accord de l'utilisateur que Windows demande une fois.
/// On peut en retirer une ou toutes : elles disparaissent aussi du centre de Windows.
/// </summary>
internal sealed class Notifications
{
    private readonly SynchronizationContext _ui;
    private UserNotificationListener? _listener;
    private readonly Dictionary<string, string> _icons = new(); // appli → logo en data URL

    /// <summary>« ok », « denied » (refusé dans les paramètres) ou « unavailable ».</summary>
    public string Access { get; private set; } = "unavailable";

    public List<Notice> Items { get; private set; } = new();

    public event Action? Changed;

    public Notifications() => _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

    public async void Start()
    {
        try
        {
            _listener = UserNotificationListener.Current;
            var status = await _listener.RequestAccessAsync();
            Access = status == UserNotificationListenerAccessStatus.Allowed ? "ok" : "denied";
            if (Access == "ok")
            {
                // L'évènement n'est pas toujours disponible hors appli empaquetée : on relit alors à
                // chaque ouverture du panneau (Refresh).
                try { _listener.NotificationChanged += (_, _) => _ui.Post(_ => Refresh(), null); } catch { }
            }
        }
        catch { Access = "unavailable"; }
        Refresh();
    }

    public async void Refresh()
    {
        if (_listener is null || Access != "ok") { Changed?.Invoke(); return; }
        try
        {
            var list = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            var items = new List<Notice>();
            foreach (var n in list)
            {
                string app = "", icon = "";
                try
                {
                    app = n.AppInfo.DisplayInfo.DisplayName;
                    icon = await Logo(n.AppInfo.DisplayInfo, app);
                }
                catch { }
                var texts = n.Notification.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements()
                    .Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? new();
                items.Add(new Notice(n.Id, app, icon, texts.FirstOrDefault() ?? app,
                    string.Join("\n", texts.Skip(1)), n.CreationTime.ToUnixTimeMilliseconds()));
            }
            Items = items.OrderByDescending(i => i.Time).ToList();
        }
        catch { }
        Changed?.Invoke();
    }

    private async Task<string> Logo(Windows.ApplicationModel.AppDisplayInfo info, string app)
    {
        if (_icons.TryGetValue(app, out var cached)) return cached;
        string url = "";
        try
        {
            var stream = await info.GetLogo(new Windows.Foundation.Size(32, 32)).OpenReadAsync();
            using var input = stream.AsStreamForRead();
            using var ms = new MemoryStream();
            await input.CopyToAsync(ms);
            if (ms.Length > 0) url = $"data:{(string.IsNullOrEmpty(stream.ContentType) ? "image/png" : stream.ContentType)};base64,{Convert.ToBase64String(ms.ToArray())}";
        }
        catch { }
        _icons[app] = url;
        return url;
    }

    public void Remove(uint id)
    {
        try { _listener?.RemoveNotification(id); } catch { }
        Items = Items.Where(i => i.Id != id).ToList();
        Changed?.Invoke();
    }

    public void Clear()
    {
        try { _listener?.ClearNotifications(); } catch { }
        Items = new();
        Changed?.Invoke();
    }
}
