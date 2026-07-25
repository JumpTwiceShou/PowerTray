namespace LGSTrayUI;

public enum TrayToolTipMode
{
    Disabled = 0,
    WindowsNative = 1,
    PowerTrayCustom = 2,
}

internal static class TrayToolTipModePolicy
{
    public const TrayToolTipMode DefaultMode = TrayToolTipMode.PowerTrayCustom;

    public static TrayToolTipMode Parse(string? value)
    {
        return value?.Trim() switch
        {
            string text when text.Equals(nameof(TrayToolTipMode.Disabled), System.StringComparison.OrdinalIgnoreCase)
                => TrayToolTipMode.Disabled,
            string text when text.Equals(nameof(TrayToolTipMode.WindowsNative), System.StringComparison.OrdinalIgnoreCase)
                => TrayToolTipMode.WindowsNative,
            string text when text.Equals(nameof(TrayToolTipMode.PowerTrayCustom), System.StringComparison.OrdinalIgnoreCase)
                => TrayToolTipMode.PowerTrayCustom,
            _ => DefaultMode,
        };
    }

    public static string Serialize(TrayToolTipMode mode)
    {
        return mode switch
        {
            TrayToolTipMode.Disabled => nameof(TrayToolTipMode.Disabled),
            TrayToolTipMode.WindowsNative => nameof(TrayToolTipMode.WindowsNative),
            TrayToolTipMode.PowerTrayCustom => nameof(TrayToolTipMode.PowerTrayCustom),
            _ => nameof(TrayToolTipMode.PowerTrayCustom),
        };
    }
}

internal readonly record struct TrayToolTipModeChangeDecision(
    bool ShouldSave,
    bool ShouldPromptForRestart
);

internal static class TrayToolTipModeChangePolicy
{
    public static TrayToolTipModeChangeDecision Evaluate(
        TrayToolTipMode effectiveMode,
        TrayToolTipMode savedMode,
        TrayToolTipMode requestedMode
    )
    {
        TrayToolTipMode normalizedRequested = TrayToolTipModePolicy.Parse(
            TrayToolTipModePolicy.Serialize(requestedMode)
        );
        if (normalizedRequested == savedMode)
        {
            return new(false, false);
        }

        return new(
            ShouldSave: true,
            ShouldPromptForRestart: normalizedRequested != effectiveMode
        );
    }
}

internal readonly record struct TrayToolTipRegistration(
    bool UsesNativeText,
    bool UsesCustomContent,
    bool SubscribesCustomOpenEvent
)
{
    public static TrayToolTipRegistration For(TrayToolTipMode mode)
    {
        return mode switch
        {
            TrayToolTipMode.Disabled => new(false, false, false),
            TrayToolTipMode.WindowsNative => new(true, false, false),
            TrayToolTipMode.PowerTrayCustom => new(false, true, true),
            _ => new(false, true, true),
        };
    }
}
