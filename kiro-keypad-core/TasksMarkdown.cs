namespace KiroKeypad;

public static class TasksMarkdown
{
    public static (Int32 Done, Int32 Total, String? Current) Count(String? markdown)
    {
        if (String.IsNullOrWhiteSpace(markdown))
        {
            return (0, 0, null);
        }

        var done = 0;
        var total = 0;
        String? current = null;
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.Trim();
            var open = line.IndexOf("- [ ]", StringComparison.Ordinal);
            var closed = line.IndexOf("- [x]", StringComparison.OrdinalIgnoreCase);
            var mark = open >= 0 ? open : closed;
            if (mark < 0)
            {
                continue;
            }

            total++;
            if (closed >= 0 && (open < 0 || closed < open))
            {
                done++;
                continue;
            }

            current ??= line[(mark + 5)..].Trim().TrimStart('.', ' ', '-', '*');
        }

        return (done, total, current);
    }
}
