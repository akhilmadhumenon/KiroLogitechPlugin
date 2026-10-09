namespace Loupedeck.KiroKeypadPlugin
{
    using System;
    using System.IO;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using KiroKeypad;

    internal static class CompanionClient
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };

        private static readonly String DiscoveryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".kiro-keypad",
            "companion.json");

        public static void Focus(String? sessionId, String? title = null) =>
            PostFireAndForget("/v1/focus", new { sessionId, title }, () => KiroKeys.FocusSession(sessionId));

        public static void Stop(String? sessionId) =>
            PostFireAndForget("/v1/stop", new { sessionId }, KiroKeys.Stop);

        public static void Kill(String? sessionId) =>
            PostFireAndForget("/v1/kill", new { sessionId }, KiroKeys.Kill);

        public static void NewChat() =>
            PostFireAndForget("/v1/new-chat", new { }, KiroKeys.NewChat);

        public static void Prompt(String text, Boolean submit) =>
            PostFireAndForget("/v1/prompt", new { text, submit }, () => KiroKeys.Prompt(text, submit));

        public static void Command(String command) =>
            PostFireAndForget("/v1/command", new { command });

        public static void Open(String which) =>
            PostFireAndForget("/v1/open", new { which });

        public static void Steer() => PostFireAndForget("/v1/steer", new { });

        public static async Task<SpecSnapshot?> SpecAsync()
        {
            try
            {
                if (!TryRead(out var port, out var token))
                {
                    return null;
                }

                using var req = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/v1/spec");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var res = await Http.SendAsync(req).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                {
                    return null;
                }

                var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                return new SpecSnapshot
                {
                    Phase = ReadString(root, "phase") ?? "Spec",
                    Done = ReadInt(root, "done"),
                    Total = ReadInt(root, "total"),
                    CurrentTask = ReadString(root, "currentTask"),
                    SpecDir = ReadString(root, "specDir"),
                    RequirementsPath = ReadString(root, "requirementsPath"),
                    DesignPath = ReadString(root, "designPath"),
                    TasksPath = ReadString(root, "tasksPath"),
                };
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"[companion] spec: {ex.Message}");
                return null;
            }
        }

        public static async Task<List<ListedSession>?> SessionsAsync()
        {
            try
            {
                if (!TryRead(out var port, out var token))
                {
                    return null;
                }

                using var req = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/v1/sessions");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var res = await Http.SendAsync(req).ConfigureAwait(false);
                var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!res.IsSuccessStatusCode || json.Contains("\"ok\":false", StringComparison.Ordinal))
                {
                    return null;
                }

                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("sessions", out var sessions) || sessions.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                var list = new List<ListedSession>();
                foreach (var item in sessions.EnumerateArray())
                {
                    var id = ReadString(item, "id");
                    if (String.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    list.Add(new ListedSession
                    {
                        Id = id,
                        Title = ReadString(item, "title") ?? "New",
                        Focused = item.TryGetProperty("focused", out var focused) && focused.ValueKind == JsonValueKind.True,
                    });
                }

                return list;
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"[companion] sessions: {ex.Message}");
                return null;
            }
        }

        public static async Task<Int32?> ContextAsync()
        {
            try
            {
                if (!TryRead(out var port, out var token))
                {
                    return null;
                }

                using var req = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/v1/context");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var res = await Http.SendAsync(req).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                {
                    return null;
                }

                var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("contextTokens", out var value) && value.ValueKind == JsonValueKind.Number)
                {
                    return value.GetInt32();
                }

                return null;
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"[companion] context: {ex.Message}");
                return null;
            }
        }

        private static void PostFireAndForget(String path, Object body, Action? fallback = null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    if (!TryRead(out var port, out var token))
                    {
                        PluginLog.Warning("[companion] not running, using keystrokes");
                        fallback?.Invoke();
                        return;
                    }

                    using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}{path}");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                    using var res = await Http.SendAsync(req).ConfigureAwait(false);
                    var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var ok = res.IsSuccessStatusCode && !json.Contains("\"ok\":false", StringComparison.Ordinal);
                    if (!ok)
                    {
                        PluginLog.Warning($"[companion] {path} failed, using keystrokes");
                        fallback?.Invoke();
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.Warning($"[companion] {path}: {ex.Message}");
                    fallback?.Invoke();
                }
            });
        }

        private static Boolean TryRead(out Int32 port, out String token)
        {
            port = 0;
            token = "";
            if (!File.Exists(DiscoveryPath))
            {
                return false;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(DiscoveryPath));
            var root = doc.RootElement;
            if (!root.TryGetProperty("port", out var portEl) || !root.TryGetProperty("token", out var tokenEl))
            {
                return false;
            }

            port = portEl.GetInt32();
            token = tokenEl.GetString() ?? "";
            return port > 0 && token.Length > 0;
        }

        private static String? ReadString(JsonElement root, String name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static Int32 ReadInt(JsonElement root, String name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetInt32()
                : 0;
    }
}
