namespace KiroKeypad;

public static class Captions
{
    public const Int32 MaxLength = 10;

    public static String Fit(String? text, String fallback)
    {
        var source = String.IsNullOrWhiteSpace(text) ? fallback : text;
        var oneLine = source.Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (oneLine.Contains("  ", StringComparison.Ordinal))
        {
            oneLine = oneLine.Replace("  ", " ", StringComparison.Ordinal);
        }

        if (oneLine.Length == 0)
        {
            oneLine = fallback;
        }

        if (oneLine.Length <= MaxLength)
        {
            return oneLine;
        }

        return oneLine[..(MaxLength - 1)] + "…";
    }
}
