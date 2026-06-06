using System.Windows;

namespace LGSTrayUI;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeWindowInterop.Apply(this);
        CheckTheme.StaticPropertyChanged += OnThemeChanged;
        Closed += (_, _) => CheckTheme.StaticPropertyChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CheckTheme.LightTheme) or nameof(CheckTheme.ThemeMode))
        {
            Dispatcher.BeginInvoke(() => ThemeWindowInterop.Apply(this));
        }
    }
}
