using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Windows.Devices.Geolocation;

namespace Azuria.Shell.Native;

/// <summary>
/// La météo du lieu où se trouve la machine. Position : le service de localisation de Windows (qui
/// peut être coupé dans Paramètres › Confidentialité › Localisation), arrondie à 0,1° (≈ 10 km)
/// avant d'être envoyée. Prévisions : Open-Meteo (gratuit, sans compte ni clé). La dernière réponse
/// est gardée sur disque pour s'afficher tout de suite au démarrage ; mise à jour toutes les 30 min
/// au plus.
/// </summary>
internal sealed class Weather
{
    private static readonly string CacheFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Azuria", "meteo.json");
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly SynchronizationContext _ui;
    private DateTime _fetched = DateTime.MinValue;
    private bool _busy;

    /// <summary>
    /// Ce que la page reçoit : <c>{ state: "ok" | "location-off" | "offline" | "loading", … }</c>.
    /// </summary>
    public JsonObject Current { get; private set; } = new() { ["state"] = "loading" };

    public event Action<JsonObject>? Changed;

    static Weather() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("Azuria");

    public Weather()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        try
        {
            if (File.Exists(CacheFile) && JsonNode.Parse(File.ReadAllText(CacheFile)) is JsonObject cached)
            {
                Current = cached;
                _fetched = File.GetLastWriteTimeUtc(CacheFile);
            }
        }
        catch { }
    }

    /// <summary>Met à jour si la dernière réponse date de plus de 30 minutes.</summary>
    public async void Refresh()
    {
        if (_busy || DateTime.UtcNow - _fetched < MaxAge) return;
        _busy = true;
        try
        {
            var data = await Task.Run(Fetch);
            if (data["state"]?.GetValue<string>() == "ok")
            {
                _fetched = DateTime.UtcNow;
                try { File.WriteAllText(CacheFile, data.ToJsonString()); } catch { }
                Publish(data);
            }
            // Échec : on garde la dernière météo connue, sinon on dit pourquoi.
            else if (Current["state"]?.GetValue<string>() != "ok") Publish(data);
        }
        finally { _busy = false; }
    }

    private void Publish(JsonObject data) => _ui.Post(_ => { Current = data; Changed?.Invoke(data); }, null);

    private static async Task<JsonObject> Fetch()
    {
        // 1. Position (service de localisation de Windows).
        double lat, lon;
        try
        {
            if (await Geolocator.RequestAccessAsync() != GeolocationAccessStatus.Allowed)
                return new JsonObject { ["state"] = "location-off" };
            var pos = await new Geolocator { DesiredAccuracy = PositionAccuracy.Default }
                .GetGeopositionAsync(TimeSpan.FromHours(1), TimeSpan.FromSeconds(10));
            lat = Math.Round(pos.Coordinate.Point.Position.Latitude, 1);
            lon = Math.Round(pos.Coordinate.Point.Position.Longitude, 1);
        }
        catch { return new JsonObject { ["state"] = "location-off" }; }

        // 2. Prévisions.
        try
        {
            var inv = CultureInfo.InvariantCulture;
            string url = (
                $"https://api.open-meteo.com/v1/forecast?latitude={lat.ToString(inv)}&longitude={lon.ToString(inv)}" +
                "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,is_day" +
                "&hourly=temperature_2m,weather_code,is_day&daily=weather_code,temperature_2m_max,temperature_2m_min,sunrise,sunset" +
                $"&timezone={Uri.EscapeDataString(Zone())}&forecast_days=6");
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            return Shape(doc.RootElement);
        }
        catch { return new JsonObject { ["state"] = "offline" }; }
    }

    // Fuseau de l'horloge du PC (et pas celui qu'Open-Meteo déduirait de la position) : les heures
    // des prévisions s'alignent sur l'heure affichée par Azuria.
    private static string Zone() =>
        TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana) ? iana : "auto";

    // Réponse d'Open-Meteo → ce dont la page a besoin (maintenant, 8 prochaines heures, 6 jours).
    private static JsonObject Shape(JsonElement r)
    {
        var cur = r.GetProperty("current");
        var now = new JsonObject
        {
            ["temp"] = Math.Round(cur.GetProperty("temperature_2m").GetDouble()),
            ["feels"] = Math.Round(cur.GetProperty("apparent_temperature").GetDouble()),
            ["humidity"] = cur.GetProperty("relative_humidity_2m").GetDouble(),
            ["wind"] = Math.Round(cur.GetProperty("wind_speed_10m").GetDouble()),
            ["code"] = cur.GetProperty("weather_code").GetInt32(),
            ["day"] = cur.GetProperty("is_day").GetInt32() == 1,
        };

        var hourly = r.GetProperty("hourly");
        var times = hourly.GetProperty("time").EnumerateArray().Select(t => t.GetString()!).ToList();
        // 48 heures à partir de maintenant (heure du PC) : la page garde les 8 suivantes au moment où
        // elle s'affiche, même si la réponse date de 25 minutes.
        string nowKey = DateTime.Now.ToString("yyyy-MM-ddTHH", CultureInfo.InvariantCulture);
        int start = Math.Max(0, times.FindIndex(t => string.CompareOrdinal(t, nowKey) >= 0));
        var hours = new JsonArray();
        for (int i = start; i < Math.Min(times.Count, start + 48); i++)
            hours.Add(new JsonObject
            {
                ["time"] = times[i],
                ["h"] = int.Parse(times[i].Substring(11, 2), CultureInfo.InvariantCulture),
                ["temp"] = Math.Round(hourly.GetProperty("temperature_2m")[i].GetDouble()),
                ["code"] = hourly.GetProperty("weather_code")[i].GetInt32(),
                ["day"] = hourly.GetProperty("is_day")[i].GetInt32() == 1,
            });

        var daily = r.GetProperty("daily");
        var days = new JsonArray();
        int n = daily.GetProperty("time").GetArrayLength();
        for (int i = 0; i < n; i++)
            days.Add(new JsonObject
            {
                ["date"] = daily.GetProperty("time")[i].GetString(),
                ["min"] = Math.Round(daily.GetProperty("temperature_2m_min")[i].GetDouble()),
                ["max"] = Math.Round(daily.GetProperty("temperature_2m_max")[i].GetDouble()),
                ["code"] = daily.GetProperty("weather_code")[i].GetInt32(),
            });

        return new JsonObject
        {
            ["state"] = "ok",
            ["now"] = now,
            ["hours"] = hours,
            ["days"] = days,
            ["sunrise"] = daily.GetProperty("sunrise")[0].GetString()?[11..],
            ["sunset"] = daily.GetProperty("sunset")[0].GetString()?[11..],
        };
    }
}
