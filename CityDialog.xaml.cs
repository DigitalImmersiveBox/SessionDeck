using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace ClaudeSessions;

/// <summary>Search for a city (Open-Meteo geocoding) and pick one for the weather.</summary>
public partial class CityDialog : Window
{
    public CityMatch? Chosen { get; private set; }

    public CityDialog(string? current)
    {
        InitializeComponent();
        Query.Text = current ?? "";
        Loaded += (_, _) =>
        {
            Query.Focus();
            Query.SelectAll();
        };
        SourceInitialized += (_, _) =>
        {
            int on = 1; // dark title bar, like the dashboard
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref on, sizeof(int));
        };
    }

    async void Search_Click(object sender, RoutedEventArgs e)
    {
        var name = Query.Text.Trim();
        if (name.Length < 2)
        {
            Message.Text = "Type at least two letters.";
            return;
        }
        SearchButton.IsEnabled = false;
        Message.Text = "Searching…";
        try
        {
            var matches = await WeatherService.SearchCities(name);
            Results.ItemsSource = matches;
            Results.Visibility = matches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            Message.Text = matches.Count switch
            {
                0 => $"No place called \"{name}\" found. Check the spelling, or try a nearby bigger town.",
                1 => "Found it:",
                _ => "Pick the right one:",
            };
            if (matches.Count > 0) Results.SelectedIndex = 0;
        }
        catch
        {
            Message.Text = "Couldn't search right now. Check the internet connection and try again.";
        }
        finally
        {
            SearchButton.IsEnabled = true;
        }
    }

    void Results_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        UseButton.IsEnabled = Results.SelectedItem is CityMatch;

    void Results_DoubleClick(object sender, MouseButtonEventArgs e) => Use_Click(sender, e);

    void Use_Click(object sender, RoutedEventArgs e)
    {
        if (Results.SelectedItem is not CityMatch city) return;
        Chosen = city;
        DialogResult = true;
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
