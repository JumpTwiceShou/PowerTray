using System;

namespace LGSTrayUI;

internal static class NativeTrayToolTipText
{
    // NOTIFYICONDATAW.szTip has room for 128 UTF-16 code units including
    // the terminating null. Leave one code unit for that terminator.
    public const int MaximumUtf16CodeUnits = 127;

    public static string Limit(string? value)
    {
        string text = value ?? string.Empty;
        if (text.Length <= MaximumUtf16CodeUnits)
        {
            return text;
        }

        int length = MaximumUtf16CodeUnits;
        if (char.IsHighSurrogate(text[length - 1]) &&
            length < text.Length &&
            char.IsLowSurrogate(text[length]))
        {
            length--;
        }

        return text[..length];
    }
}
