namespace Loupedeck.KiroKeypadPlugin
{
    using System;
    using System.Diagnostics;
    using System.Text;

    /// <summary>
    /// Talks to Kiro directly when the companion extension is not running.
    /// Cmd+L focuses chat. Cmd+T starts a new session once chat is focused.
    /// </summary>
    internal static class KiroKeys
    {
        public static void NewChat() => Run("""
            tell application "Kiro" to activate
            delay 0.2
            tell application "System Events"
              keystroke "l" using command down
              delay 0.25
              keystroke "t" using command down
            end tell
            """);

        public static void FocusChat() => Run("""
            tell application "Kiro" to activate
            delay 0.15
            tell application "System Events"
              keystroke "l" using command down
            end tell
            """);

        public static void FocusSession(String? sessionId)
        {
            var shift = OpenSessionFeed.Toward(sessionId);
            PluginLog.Info($"[keys] focus {sessionId ?? "(none)"} -> {shift?.ToString() ?? "chat only"}");
            if (shift == null || shift.Value.Steps <= 0)
            {
                FocusChat();
                return;
            }

            // Cmd+Option+Shift+Left/Right is bound to the chat-tab commands with no when-clause,
            // so the palette never opens and the shortcut does not depend on chat focus.
            var keyCode = shift.Value.Forward ? 124 : 123;
            var steps = Math.Min(shift.Value.Steps, 6);
            var script = new StringBuilder();
            script.AppendLine("tell application \"Kiro\" to activate");
            script.AppendLine("delay 0.15");
            script.AppendLine("tell application \"System Events\"");
            for (var i = 0; i < steps; i++)
            {
                script.AppendLine($"key code {keyCode} using {{command down, option down, shift down}}");
                script.AppendLine("delay 0.12");
            }

            script.AppendLine("end tell");
            Run(script.ToString());
        }

        public static void Stop() => Run("""
            tell application "Kiro" to activate
            delay 0.15
            tell application "System Events"
              key code 53
            end tell
            """);

        public static void Kill() => Run("""
            tell application "Kiro" to activate
            delay 0.12
            tell application "System Events"
              keystroke "c" using {command down, option down, shift down}
              delay 0.12
              keystroke "w" using {command down, option down, shift down}
            end tell
            """);

        public static void Prompt(String text, Boolean submit)
        {
            if (String.IsNullOrEmpty(text))
            {
                FocusChat();
                return;
            }

            var escaped = text.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var submitLine = submit ? "key code 36" : "";
            Run($"""
                tell application "Kiro" to activate
                delay 0.2
                set oldClip to the clipboard
                set the clipboard to "{escaped}"
                tell application "System Events"
                  keystroke "l" using command down
                  delay 0.2
                  keystroke "v" using command down
                  delay 0.12
                  {submitLine}
                end tell
                delay 0.05
                set the clipboard to oldClip
                """);
        }

        private static void Run(String script)
        {
            if (!OperatingSystem.IsMacOS())
            {
                PluginLog.Warning("[keys] Kiro keystrokes are macOS only");
                return;
            }

            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = "osascript",
                    Arguments = "-",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    CreateNoWindow = true,
                };
                var process = Process.Start(start);
                if (process == null)
                {
                    return;
                }

                process.StandardInput.Write(script);
                process.StandardInput.Close();
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"[keys] {ex.Message}");
            }
        }
    }
}
