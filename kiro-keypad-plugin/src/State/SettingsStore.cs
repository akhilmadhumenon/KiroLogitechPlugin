namespace Loupedeck.KiroKeypadPlugin
{
    using System;
    using System.IO;
    using System.Text.Json;
    using KiroKeypad;

    internal static class SettingsStore
    {
        private static readonly String Path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".kiro-keypad",
            "settings.json");

        public static IReadOnlyList<FlowKey> Flow { get; private set; } = FlowKey.Defaults;

        public static void Ensure()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(Path)!;
                Directory.CreateDirectory(dir);
                if (!File.Exists(Path))
                {
                    var payload = new
                    {
                        flow = FlowKey.Defaults.Select(k => new
                        {
                            id = k.Id,
                            label = k.Label,
                            prompt = k.Prompt,
                            submit = k.Submit,
                        }),
                    };
                    File.WriteAllText(Path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
                }

                Flow = Read();
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"[settings] {ex.Message}");
                Flow = FlowKey.Defaults;
            }
        }

        private static IReadOnlyList<FlowKey> Read()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path));
            if (!doc.RootElement.TryGetProperty("flow", out var flow) || flow.ValueKind != JsonValueKind.Array)
            {
                return FlowKey.Defaults;
            }

            var keys = new List<FlowKey>();
            foreach (var item in flow.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                var label = item.TryGetProperty("label", out var labelEl) ? labelEl.GetString() : id;
                var prompt = item.TryGetProperty("prompt", out var promptEl) ? promptEl.GetString() : "";
                var submit = item.TryGetProperty("submit", out var submitEl) && submitEl.ValueKind == JsonValueKind.True;
                if (String.IsNullOrWhiteSpace(id) || String.IsNullOrWhiteSpace(label))
                {
                    continue;
                }

                keys.Add(new FlowKey(id, label, prompt ?? "", submit));
            }

            return keys.Count == 0 ? FlowKey.Defaults : keys;
        }
    }
}
