namespace Loupedeck.KiroKeypadPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using KiroKeypad;

    internal sealed class ListedSession
    {
        public required String Id { get; init; }
        public String Title { get; init; } = "";
        public Boolean Focused { get; init; }
    }

    /// <summary>Reads the chats already open in Kiro and ranks them by last use.</summary>
    internal readonly record struct TabShift(Int32 Steps, Boolean Forward);

    internal static class OpenSessionFeed
    {
        private static String? _localFocus;
        private static DateTime _localFocusAt = DateTime.MinValue;

        public static String? FocusedId()
        {
            var listed = ReadLocal();
            if (listed == null || listed.Count == 0)
            {
                return null;
            }

            if (_localFocusAt >= LastListWrite && !String.IsNullOrWhiteSpace(_localFocus))
            {
                return _localFocus;
            }

            return listed.FirstOrDefault(s => s.Focused)?.Id;
        }

        public static TabShift? Toward(String? sessionId)
        {
            if (String.IsNullOrWhiteSpace(sessionId) || sessionId.StartsWith("pending:", StringComparison.Ordinal))
            {
                return null;
            }

            var listed = ReadLocal();
            if (listed == null || listed.Count == 0)
            {
                return null;
            }

            var target = listed.FindIndex(s => SameSession(s.Id, sessionId));
            if (target < 0)
            {
                return null;
            }

            var focusedId = _localFocusAt >= LastListWrite && !String.IsNullOrWhiteSpace(_localFocus)
                ? _localFocus
                : listed.FirstOrDefault(s => s.Focused)?.Id;
            var focused = focusedId == null ? -1 : listed.FindIndex(s => SameSession(s.Id, focusedId));
            if (focused < 0)
            {
                focused = 0;
            }

            var count = listed.Count;
            var forward = (target - focused + count) % count;
            var backward = (focused - target + count) % count;
            _localFocus = listed[target].Id;
            _localFocusAt = DateTime.UtcNow;
            if (forward == 0)
            {
                return new TabShift(0, true);
            }

            return forward <= backward
                ? new TabShift(forward, true)
                : new TabShift(backward, false);
        }

        private static DateTime LastListWrite { get; set; } = DateTime.MinValue;

        public static async Task<IReadOnlyList<OpenSession>?> ReadAsync()
        {
            var listed = await CompanionClient.SessionsAsync().ConfigureAwait(false);
            listed ??= ReadLocal();
            if (listed == null)
            {
                return null;
            }

            return SessionUsage.Rank(listed);
        }

        private static List<ListedSession>? ReadLocal()
        {
            var storage = WorkspaceStorage();
            if (storage == null || !Directory.Exists(storage))
            {
                return null;
            }

            List<ListedSession>? best = null;
            var bestTime = DateTime.MinValue;
            foreach (var dir in Directory.EnumerateDirectories(storage))
            {
                var db = Path.Combine(dir, "state.vscdb");
                if (!File.Exists(db))
                {
                    continue;
                }

                var listed = ReadDb(db);
                if (listed == null || listed.Count == 0)
                {
                    continue;
                }

                var mtime = File.GetLastWriteTimeUtc(db);
                if (best == null || mtime > bestTime)
                {
                    best = listed;
                    bestTime = mtime;
                    LastListWrite = mtime;
                }
            }

            return best ?? [];
        }

        private static Boolean SameSession(String? left, String? right)
        {
            if (String.IsNullOrWhiteSpace(left) || String.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            if (String.Equals(left, right, StringComparison.Ordinal))
            {
                return true;
            }

            static String Norm(String id)
            {
                var value = id.Trim();
                return value.StartsWith("sess_", StringComparison.OrdinalIgnoreCase) ? value[5..] : value;
            }

            return String.Equals(Norm(left), Norm(right), StringComparison.OrdinalIgnoreCase);
        }

        private static List<ListedSession>? ReadDb(String db)
        {
            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = OperatingSystem.IsMacOS() ? "/usr/bin/sqlite3" : "sqlite3",
                    ArgumentList = { db, "SELECT value FROM ItemTable WHERE key='kiro.kiroAgent';" },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var process = Process.Start(start);
                if (process == null)
                {
                    return null;
                }

                var json = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(2000);
                if (process.ExitCode != 0 || json.Length == 0)
                {
                    return [];
                }

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var focused = root.TryGetProperty("sessionPanels.focused", out var focusEl) && focusEl.ValueKind == JsonValueKind.String
                    ? focusEl.GetString()
                    : null;
                if (!root.TryGetProperty("sessionPanels.entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
                {
                    return [];
                }

                var list = new List<ListedSession>();
                foreach (var item in entries.EnumerateArray())
                {
                    if (!item.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var id = idEl.GetString();
                    if (String.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    var title = item.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String
                        ? titleEl.GetString()
                        : null;
                    list.Add(new ListedSession
                    {
                        Id = id,
                        Title = String.IsNullOrWhiteSpace(title) ? "New" : title,
                        Focused = id == focused,
                    });
                }

                return list;
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"[sessions] {ex.Message}");
                return null;
            }
        }

        private static String? WorkspaceStorage()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (OperatingSystem.IsMacOS())
            {
                return Path.Combine(home, "Library", "Application Support", "Kiro", "User", "workspaceStorage");
            }

            if (OperatingSystem.IsWindows())
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "Kiro", "User", "workspaceStorage");
            }

            return Path.Combine(home, ".config", "Kiro", "User", "workspaceStorage");
        }
    }

    internal static class SessionUsage
    {
        private static readonly String PathName = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".kiro-keypad",
            "session-usage.json");

        private static Dictionary<String, Int64>? _usage;

        public static IReadOnlyList<OpenSession> Rank(IReadOnlyList<ListedSession> sessions)
        {
            var usage = Load();
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var live = new HashSet<String>(sessions.Select(s => s.Id), StringComparer.Ordinal);
            for (var i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session.Focused)
                {
                    usage[session.Id] = now;
                }
                else if (!usage.ContainsKey(session.Id))
                {
                    usage[session.Id] = i + 1;
                }
            }

            foreach (var stale in usage.Keys.Where(id => !live.Contains(id)).ToArray())
            {
                usage.Remove(stale);
            }

            Save(usage);
            return sessions
                .Select(s => new OpenSession
                {
                    Id = s.Id,
                    Title = s.Title,
                    Focused = s.Focused,
                    LastUsed = usage.TryGetValue(s.Id, out var used) ? used : 0,
                })
                .ToList();
        }

        private static Dictionary<String, Int64> Load()
        {
            if (_usage != null)
            {
                return _usage;
            }

            _usage = new Dictionary<String, Int64>(StringComparer.Ordinal);
            try
            {
                if (!File.Exists(PathName))
                {
                    return _usage;
                }

                using var doc = JsonDocument.Parse(File.ReadAllText(PathName));
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt64(out var used))
                    {
                        _usage[prop.Name] = used;
                    }
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"[sessions] usage: {ex.Message}");
            }

            return _usage;
        }

        private static void Save(Dictionary<String, Int64> usage)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
                var json = JsonSerializer.Serialize(usage);
                File.WriteAllText(PathName, json);
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"[sessions] save: {ex.Message}");
            }
        }
    }
}
