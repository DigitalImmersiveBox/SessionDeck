using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace ClaudeSessions;

public sealed record WeatherNow(string Place, double Temperature, int Code, bool IsDay,
                                double High, double Low, int RainChance, DateTime FetchedAt);

/// <summary>
/// Local weather for the dashboard strip. The place is detected once a day from your internet
/// address (approximate, city level) and kept in settings; the forecast comes from Open-Meteo
/// (free, no account) every 15 minutes.
/// </summary>
public sealed class WeatherService
{
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    static readonly TimeSpan Every = TimeSpan.FromMinutes(15);
    static readonly TimeSpan PlaceEvery = TimeSpan.FromDays(1);

    readonly Settings settings;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMinutes(1) };
    DateTime next;
    bool fetching;

    public WeatherNow? Current { get; private set; }
    public string? Error { get; private set; }
    public event Action? Changed;

    static WeatherService() => http.DefaultRequestHeaders.UserAgent.ParseAdd("claude-sessions/1.0");

    internal WeatherService(Settings settings)
    {
        this.settings = settings;
        timer.Tick += (_, _) => Tick();
    }

    public void Start()
    {
        timer.Start();
        Tick();
    }

    void Tick()
    {
        if (DateTime.UtcNow >= next && !fetching) _ = Fetch();
    }

    /// <summary>Use this city from now on (Change city), and show its weather straight away.</summary>
    public void SetCity(CityMatch city)
    {
        settings.WeatherPlace = new WeatherPlace(city.Name, city.Latitude, city.Longitude, DateTime.UtcNow, Manual: true);
        settings.Save();
        RefreshNow();
    }

    /// <summary>Go back to the approximate place from your internet address.</summary>
    public void UseDetectedPlace()
    {
        settings.WeatherPlace = null;
        settings.Save();
        RefreshNow();
    }

    public bool HasManualCity => settings.WeatherPlace?.Manual == true;

    void RefreshNow()
    {
        Current = null;
        Error = null;
        next = DateTime.MinValue;
        Changed?.Invoke();
        Tick();
    }

    /// <summary>Open-Meteo's free geocoding: up to 8 places matching the name, most populous first.</summary>
    public static async Task<List<CityMatch>> SearchCities(string name)
    {
        var url = "https://geocoding-api.open-meteo.com/v1/search?count=8&language=en&format=json&name=" + Uri.EscapeDataString(name.Trim());
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
        var matches = new List<CityMatch>();
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return matches;
        foreach (var r in results.EnumerateArray())
        {
            if (Json.Str(r, "name") is not { } city || Number(r, "latitude") is not double lat || Number(r, "longitude") is not double lon) continue;
            var parts = new[] { city, Json.Str(r, "admin1"), Json.Str(r, "country") }.Where(p => !string.IsNullOrEmpty(p)).Distinct();
            matches.Add(new CityMatch(city, string.Join(", ", parts), lat, lon));
        }
        return matches;
    }

    async Task Fetch()
    {
        fetching = true;
        try
        {
            var place = settings.WeatherPlace;
            if (place == null || !place.Manual && DateTime.UtcNow - place.DetectedAt > PlaceEvery)
            {
                place = await DetectPlace() ?? place;
                if (place == null) throw new InvalidOperationException("Couldn't work out your location");
                settings.WeatherPlace = place;
                settings.Save();
            }
            Current = await Forecast(place);
            Error = null;
            next = DateTime.UtcNow + Every;
        }
        catch (Exception ex)
        {
            Error = ex is InvalidOperationException ? ex.Message : "Weather unavailable";
            next = DateTime.UtcNow + TimeSpan.FromMinutes(5);
        }
        finally
        {
            fetching = false;
            Changed?.Invoke();
        }
    }

    /// <summary>City-level location from the public IP: ipapi.co, then ipwho.is as a fallback.</summary>
    static async Task<WeatherPlace?> DetectPlace()
    {
        try
        {
            using var doc = JsonDocument.Parse(await http.GetStringAsync("https://ipapi.co/json/"));
            var r = doc.RootElement;
            if (Json.Str(r, "city") is { Length: > 0 } city && Number(r, "latitude") is double lat && Number(r, "longitude") is double lon)
                return new WeatherPlace(city, lat, lon, DateTime.UtcNow);
        }
        catch { }
        try
        {
            using var doc = JsonDocument.Parse(await http.GetStringAsync("https://ipwho.is/"));
            var r = doc.RootElement;
            if (Json.Str(r, "city") is { Length: > 0 } city && Number(r, "latitude") is double lat && Number(r, "longitude") is double lon)
                return new WeatherPlace(city, lat, lon, DateTime.UtcNow);
        }
        catch { }
        return null;
    }

    static async Task<WeatherNow> Forecast(WeatherPlace place)
    {
        var url = FormattableString.Invariant(
            $"https://api.open-meteo.com/v1/forecast?latitude={place.Latitude:0.####}&longitude={place.Longitude:0.####}")
            + "&current=temperature_2m,weather_code,is_day"
            + "&daily=temperature_2m_max,temperature_2m_min,precipitation_probability_max"
            + "&timezone=auto&forecast_days=1";
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
        var current = doc.RootElement.GetProperty("current");
        var daily = doc.RootElement.GetProperty("daily");
        double First(string name) => daily.GetProperty(name)[0].ValueKind == JsonValueKind.Number ? daily.GetProperty(name)[0].GetDouble() : double.NaN;
        return new WeatherNow(
            place.City,
            current.GetProperty("temperature_2m").GetDouble(),
            current.GetProperty("weather_code").GetInt32(),
            current.TryGetProperty("is_day", out var day) && day.ValueKind == JsonValueKind.Number && day.GetInt32() == 1,
            First("temperature_2m_max"),
            First("temperature_2m_min"),
            double.IsNaN(First("precipitation_probability_max")) ? 0 : (int)First("precipitation_probability_max"),
            DateTime.UtcNow);
    }

    static double? Number(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    /// <summary>WMO weather code → words and a symbol (Segoe UI Symbol).</summary>
    public static (string Text, string Symbol) Describe(int code, bool isDay) => code switch
    {
        0 => (isDay ? "Clear" : "Clear night", isDay ? "☀" : "☾"),
        1 => ("Mostly clear", isDay ? "☀" : "☾"),
        2 => ("Partly cloudy", "⛅"),
        3 => ("Overcast", "☁"),
        45 or 48 => ("Fog", "☁"),
        51 or 53 or 55 => ("Drizzle", "☂"),
        56 or 57 => ("Freezing drizzle", "☂"),
        61 => ("Light rain", "☂"),
        63 => ("Rain", "☂"),
        65 => ("Heavy rain", "☂"),
        66 or 67 => ("Freezing rain", "☂"),
        71 => ("Light snow", "❄"),
        73 => ("Snow", "❄"),
        75 => ("Heavy snow", "❄"),
        77 => ("Snow grains", "❄"),
        80 or 81 or 82 => ("Rain showers", "☂"),
        85 or 86 => ("Snow showers", "❄"),
        95 => ("Thunderstorm", "⚡"),
        96 or 99 => ("Thunderstorm, hail", "⚡"),
        _ => ("—", "☁"),
    };
}

/// <param name="Manual">Chosen with Change city; never replaced by detection.</param>
public sealed record WeatherPlace(string City, double Latitude, double Longitude, DateTime DetectedAt, bool Manual = false);

/// <param name="Label">"Name, region, country" as shown in the search results.</param>
public sealed record CityMatch(string Name, string Label, double Latitude, double Longitude);
