namespace KiroKeypad;

public sealed class HookEvent
{
    public String Name { get; init; } = "other";
    public String? SessionId { get; init; }
    public String? Cwd { get; init; }
    public String? ToolName { get; init; }
    public String? UserPrompt { get; init; }
    public String? TaskName { get; init; }

    public static String Normalize(String? raw)
    {
        if (String.IsNullOrWhiteSpace(raw))
        {
            return "other";
        }

        var compact = raw.Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal);
        if (compact.Contains("pretask", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("PreTask", StringComparison.OrdinalIgnoreCase))
        {
            return "pretask";
        }

        if (compact.Contains("posttask", StringComparison.OrdinalIgnoreCase))
        {
            return "posttask";
        }

        if (compact.Contains("pretool", StringComparison.OrdinalIgnoreCase))
        {
            return "pretool";
        }

        if (compact.Contains("posttool", StringComparison.OrdinalIgnoreCase))
        {
            return "posttool";
        }

        if (compact.Contains("prompt", StringComparison.OrdinalIgnoreCase))
        {
            return "prompt";
        }

        if (compact.Contains("sessionstart", StringComparison.OrdinalIgnoreCase)
            || compact.Equals("session", StringComparison.OrdinalIgnoreCase))
        {
            return "session";
        }

        if (compact.Contains("stop", StringComparison.OrdinalIgnoreCase))
        {
            return "stop";
        }

        return "other";
    }
}
