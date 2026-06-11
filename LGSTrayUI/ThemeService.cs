using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace LGSTrayUI;

public sealed class ThemeService
{
    private readonly UserSettingsWrapper _settings;
    private static string _language = "en-US";
    private static string _uiScaleMode = "standard";

    public ThemeService(UserSettingsWrapper settings)
    {
        _settings = settings;
        _language = _settings.Language;
        _uiScaleMode = _settings.UiScaleMode;
        CheckTheme.SetThemeMode(_settings.ThemeMode);
        ApplyCurrentResources();

        _settings.PropertyChanged += OnSettingsPropertyChanged;
        CheckTheme.StaticPropertyChanged += OnThemeChanged;
    }

    public static double CurrentScale => GetScale(_uiScaleMode);

    public static double GetScale(string? uiScaleMode)
    {
        return uiScaleMode?.ToLowerInvariant() switch
        {
            "small" => 0.94,
            "large" => 1.12,
            "maximum" => 1.25,
            _ => 1.00,
        };
    }

    public static void ApplyCurrentResources()
    {
        ApplyPalette(CheckTheme.LightTheme);
        ApplyTypography(_language, _uiScaleMode);
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UserSettingsWrapper.ThemeMode))
        {
            CheckTheme.SetThemeMode(_settings.ThemeMode);
            ApplyCurrentResources();
        }

        if (e.PropertyName == nameof(UserSettingsWrapper.Language))
        {
            _language = _settings.Language;
            ApplyCurrentResources();
        }

        if (e.PropertyName == nameof(UserSettingsWrapper.UiScaleMode))
        {
            _uiScaleMode = _settings.UiScaleMode;
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

    private static void ApplyTypography(string language, string uiScaleMode)
    {
        if (Application.Current == null)
        {
            return;
        }

        double scale = GetScale(uiScaleMode);
        ResourceDictionary resources = Application.Current.Resources;

        resources["UIFontFamily"] = GetFontFamily(language);
        resources["UIFontSize"] = 14.0 * scale;
        resources["UICaptionFontSize"] = 12.5 * scale;
        resources["UISectionTitleFontSize"] = 16.0 * scale;
        resources["UIPageTitleFontSize"] = 24.0 * scale;
        resources["UIDialogTitleFontSize"] = 18.0 * scale;
        resources["UIDialogDetailFontSize"] = 12.5 * scale;
        resources["UIMenuFontSize"] = 12.5;
        resources["UIMonospaceFontSize"] = 12.5 * scale;
        resources["UIButtonMinHeight"] = 32.0 * scale;
        resources["UIInputMinHeight"] = 34.0 * scale;
        resources["UISettingsComboWidth"] = 176.0 * scale;
        resources["UIComboItemMinHeight"] = 30.0 * scale;
        resources["UIPercentBadgeWidth"] = 56.0 * scale;
        resources["UIPercentBadgeHeight"] = 30.0 * scale;
        resources["UIPercentBadgeCornerRadius"] = new CornerRadius(15.0 * scale);
        resources["UIThresholdControlWidth"] = 330.0 * scale;
        resources["UIDeviceFieldWidth"] = 216.0 * scale;
        resources["UIDeviceThresholdControlWidth"] = 288.0 * scale;
        resources["UIDeviceOptionColumnWidth"] = 260.0 * scale;
        resources["UIDeviceOptionColumnGap"] = new GridLength(36.0 * scale);
        resources["UIDeviceThresholdColumnGap"] = new GridLength(80.0 * scale);
        resources["UIDeviceTitleMaxWidth"] = 460.0 * scale;
        resources["UIDeviceTitleLineHeight"] = 22.0 * scale;
        resources["UIDeviceAliasPadding"] = new Thickness(4.0 * scale, 3.0 * scale, 4.0 * scale, 1.0 * scale);
        resources["UIDiagnosticsTextPadding"] = new Thickness(7.0 * scale, 7.0 * scale, 0, 0);
        resources["UIScaleSmallLabelFontSize"] = 12.0;
        resources["UIScaleStandardLabelFontSize"] = 13.0;
        resources["UIScaleLargeLabelFontSize"] = 17.0;
        resources["UIScaleMaximumLabelFontSize"] = 21.0;
    }

    private static FontFamily GetFontFamily(string language)
    {
        return language switch
        {
            string value when value.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) =>
                new FontFamily("Microsoft YaHei UI, Segoe UI, Yu Gothic UI, Meiryo"),
            string value when value.Equals("ja-JP", StringComparison.OrdinalIgnoreCase) =>
                new FontFamily("Meiryo UI, Yu Gothic UI, Meiryo, Segoe UI, Microsoft YaHei UI"),
            _ => new FontFamily("Segoe UI, Microsoft YaHei UI, Yu Gothic UI, Meiryo"),
        };
    }

    private static void Set(ResourceDictionary resources, string key, string color)
    {
        SolidColorBrush brush = new((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        resources[key] = brush;
    }
}
