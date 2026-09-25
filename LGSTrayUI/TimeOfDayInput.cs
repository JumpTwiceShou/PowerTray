using System;
using System.Globalization;

namespace LGSTrayUI;

internal static class TimeOfDayInput
{
    internal static bool TryNormalize(string? text, out string normalized)
    {
        bool valid = TimeOnly.TryParseExact(text?.Trim(), ["H:mm", "HH:mm"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time);
        normalized = valid ? time.ToString("HH:mm", CultureInfo.InvariantCulture) : string.Empty;
        return valid;
    }
}
