using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace LGSTrayUI;

public sealed class ThemeService
{
    private readonly UserSettingsWrapper _settings;

    public ThemeService(UserSettingsWrapper settings)
    {
        _settings = settings;
        CheckTheme.SetThemeMode(_settings.ThemeMode);
        ApplyCurrentResources();

        _settings.PropertyChanged += OnSettingsPropertyChanged;
        CheckTheme.StaticPropertyChanged += OnThemeChanged;
    }

    public static void ApplyCurrentResources()
    {
        ApplyPalette(CheckTheme.LightTheme);
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UserSettingsWrapper.ThemeMode))
        {
            CheckTheme.SetThemeMode(_settings.ThemeMode);
            ApplyCurrentResources();
        }
    }

    private static void OnThemeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CheckTheme.LightTheme) or nameof(CheckTheme.ThemeMode))
        {
            ApplyCurrentResources();
        }
    }

    private static void ApplyPalette(bool light)
    {
        if (Application.Current == null)
        {
            return;
        }

        ResourceDictionary resources = Application.Current.Resources;

        Set(resources, "AccentBrush", light ? "#2563EB" : "#4F8CF7");
        Set(resources, "AccentHoverBrush", light ? "#1D4ED8" : "#6EA2FF");
        Set(resources, "AccentPressedBrush", light ? "#1E40AF" : "#8BB6FF");
        Set(resources, "WindowBackgroundBrush", light ? "#F3F5F8" : "#121417");
        Set(resources, "SurfaceBrush", light ? "#FFFFFF" : "#1B1F26");
        Set(resources, "SurfaceElevatedBrush", light ? "#FFFFFF" : "#20252D");
        Set(resources, "MutedSurfaceBrush", light ? "#EEF2F7" : "#252B33");
        Set(resources, "BorderBrushSoft", light ? "#D7DEE8" : "#343B46");
        Set(resources, "TextBrush", light ? "#111827" : "#F3F4F6");
        Set(resources, "MutedTextBrush", light ? "#5B6472" : "#A7AFBC");
        Set(resources, "DisabledTextBrush", light ? "#9CA3AF" : "#6F7785");
        Set(resources, "ButtonBackgroundBrush", light ? "#EEF2FF" : "#1E2C44");
        Set(resources, "ButtonHoverBrush", light ? "#DBEAFE" : "#263A5C");
        Set(resources, "ButtonPressedBrush", light ? "#BFDBFE" : "#314E7D");
        Set(resources, "ButtonBorderBrush", light ? "#C7D2FE" : "#365887");
        Set(resources, "ButtonTextBrush", light ? "#1E3A8A" : "#D7E8FF");
        Set(resources, "InputBackgroundBrush", light ? "#FFFFFF" : "#151922");
        Set(resources, "InputBorderBrush", light ? "#CBD5E1" : "#384150");
        Set(resources, "NavigationHoverBrush", light ? "#E8EEF7" : "#232B36");
        Set(resources, "NavigationSelectedTextBrush", "#FFFFFF");
        Set(resources, "SliderTrackBrush", light ? "#DCE4EF" : "#353C47");
        Set(resources, "BadgeBackgroundBrush", light ? "#F8FAFC" : "#202631");
        Set(resources, "BadgeBorderBrush", light ? "#CBD5E1" : "#3A4658");
        Set(resources, "MetaPillBackgroundBrush", light ? "#F8FAFC" : "#202631");
        Set(resources, "MenuBackgroundBrush", light ? "#FFFFFF" : "#181B21");
        Set(resources, "MenuHoverBrush", light ? "#E8EEF7" : "#252B34");
        Set(resources, "MenuSeparatorBrush", light ? "#E2E8F0" : "#343B46");
        Set(resources, "TooltipBackgroundBrush", light ? "#FFFFFF" : "#1B1F26");

        resources["ShadowColor"] = light ? Color.FromRgb(0x64, 0x74, 0x8B) : Color.FromRgb(0x00, 0x00, 0x00);
        resources["IsDarkTheme"] = !light;

        resources[SystemColors.WindowBrushKey] = resources["SurfaceBrush"];
        resources[SystemColors.ControlBrushKey] = resources["SurfaceBrush"];
        resources[SystemColors.ControlTextBrushKey] = resources["TextBrush"];
        resources[SystemColors.MenuBrushKey] = resources["MenuBackgroundBrush"];
        resources[SystemColors.MenuTextBrushKey] = resources["TextBrush"];
        resources[SystemColors.HighlightBrushKey] = resources["AccentBrush"];
        resources[SystemColors.HighlightTextBrushKey] = resources["NavigationSelectedTextBrush"];
        resources[SystemColors.GrayTextBrushKey] = resources["DisabledTextBrush"];
    }

    private static void Set(ResourceDictionary resources, string key, string color)
    {
        SolidColorBrush brush = new((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        resources[key] = brush;
    }
}
