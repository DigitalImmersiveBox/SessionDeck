using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace ClaudeSessions;

/// <summary>
/// The gear in the dashboard header: weather city and units, and which mascot to show.
/// Every change applies straight away; Done just closes.
/// </summary>
public partial class SettingsDialog : Window
{
    readonly Settings settings;
    readonly WeatherService weather;

    public event Action<MascotKind>? MascotChanged;
    public event Action? UnitChanged;

    internal SettingsDialog(Settings settings, WeatherService weather)
    {
        InitializeComponent();
        this.settings = settings;
        this.weather = weather;

        (settings.Fahrenheit ? FahrenheitButton : CelsiusButton).IsChecked = true;
        foreach (var (kind, label) in Mascot.Choices)
        {
            var picture = new Image
            {
                Source = MascotIcon.Source(Mascot.For(kind), 64, 0, false),
                Width = 64,
                Height = 64,
                Margin = new Thickness(0, -10, 0, -8),
            };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.NearestNeighbor);
            var tile = new RadioButton
            {
                Style = (Style)FindResource("MascotTile"),
                IsChecked = kind == settings.Mascot,
                Content = new StackPanel { Children = { picture, new TextBlock { Text = label, HorizontalAlignment = HorizontalAlignment.Center } } },
            };
            tile.Checked += (_, _) => MascotChanged?.Invoke(kind);
            MascotTiles.Children.Add(tile);
        }

        ShowCity();
        weather.Changed += ShowCity;
        Closed += (_, _) => weather.Changed -= ShowCity;
        SourceInitialized += (_, _) =>
        {
            int on = 1; // dark title bar, like the dashboard
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref on, sizeof(int));
        };
    }

    void ShowCity()
    {
        CityName.Text = weather.Current?.Place ?? settings.WeatherPlace?.City ?? "Finding your location…";
        CityNote.Text = weather.HasManualCity ? "Chosen by you" : "Approximate, from your internet address";
        UseDetectedLink.Visibility = weather.HasManualCity ? Visibility.Visible : Visibility.Collapsed;
    }

    void ChangeCity_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CityDialog(weather.HasManualCity ? settings.WeatherPlace?.City : null) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Chosen is { } city)
            weather.SetCity(city);
    }

    void UseDetected_Click(object sender, RoutedEventArgs e) => weather.UseDetectedPlace();

    void Website_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://digitalimmersivebox.com") { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show("Could not open the website:\n" + ex.Message, "Session Deck"); }
    }

    void Unit_Checked(object sender, RoutedEventArgs e)
    {
        if (settings == null) return; // XAML checks one before the constructor has run
        bool fahrenheit = FahrenheitButton.IsChecked == true;
        if (fahrenheit == settings.Fahrenheit) return;
        settings.Fahrenheit = fahrenheit;
        settings.Save();
        UnitChanged?.Invoke();
    }

    /// <summary>For --snapshot: the panel rendered off-screen, no network.</summary>
    internal static void SaveSnapshot(Settings settings, string path)
    {
        var dialog = new SettingsDialog(settings, new WeatherService(settings)) { Left = -10000, Top = -10000, ShowActivated = false };
        dialog.Show();
        dialog.UpdateLayout();
        UsageWidget.SavePng((FrameworkElement)VisualTreeHelper.GetChild(dialog, 0), path); // the window's own border, with its background
        dialog.Close();
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
