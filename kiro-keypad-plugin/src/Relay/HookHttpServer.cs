namespace Loupedeck.KiroKeypadPlugin
{
    using System;
    using System.IO;
    using System.Net;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using KiroKeypad;

    public sealed class HookHttpServer : IDisposable
    {
        public const Int32 Port = 47831;

        private readonly HttpListener _listener = new();
        private CancellationTokenSource? _cts;

        public void Start()
        {
            this._listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            this._listener.Start();
            this._cts = new CancellationTokenSource();
            _ = Task.Run(() => this.RunAsync(this._cts.Token));
            PluginLog.Info($"[hook] listening on 127.0.0.1:{Port}");
        }

        public void Dispose()
        {
            try
            {
                this._cts?.Cancel();
                if (this._listener.IsListening)
                {
                    this._listener.Stop();
                }

                this._listener.Close();
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"[hook] dispose: {ex.Message}");
            }
        }

        private async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && this._listener.IsListening)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await this._listener.GetContextAsync().WaitAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    PluginLog.Warning($"[hook] accept: {ex.Message}");
                    continue;
                }

                _ = Task.Run(() => this.Handle(ctx), ct);
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            try
            {
                var path = ctx.Request.Url?.AbsolutePath ?? "/";
                if (ctx.Request.HttpMethod == "GET" && path == "/health")
                {
                    Write(ctx, 200, Cockpit.Health());
                    return;
                }

                if (ctx.Request.HttpMethod == "POST" && path == "/hook")
                {
                    using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
                    var raw = reader.ReadToEnd();
                    using var doc = JsonDocument.Parse(String.IsNullOrWhiteSpace(raw) ? "{}" : raw);
                    var ev = Parse(doc.RootElement);
                    var response = Cockpit.Board.Accept(ev);
                    if (ev.Name is "pretask" or "posttask")
                    {
                        Cockpit.Spec.ApplyHook(ev);
                        if (ev.Name == "posttask")
                        {
                            HapticFeedback.TaskDone();
                        }
                    }

                    Write(ctx, 200, new
                    {
                        ok = response.Ok,
                        permission = response.Permission,
                        reason = response.Reason,
                        slot = response.Slot,
                    });
                    return;
                }

                Write(ctx, 404, new { error = "not_found" });
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[hook] handle failed");
                try
                {
                    Write(ctx, 500, new { error = "internal_error" });
                }
                catch
                {
                    // The client has gone.
                }
            }
        }

        private static HookEvent Parse(JsonElement root)
        {
            String? Str(params String[] names)
            {
                foreach (var name in names)
                {
                    if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                    {
                        return value.GetString();
                    }
                }

                return null;
            }

            var toolInput = "";
            if (root.TryGetProperty("tool_input", out var input) || root.TryGetProperty("toolInput", out input))
            {
                toolInput = input.ValueKind == JsonValueKind.String ? input.GetString() ?? "" : input.ToString();
            }

            return new HookEvent
            {
                Name = HookEvent.Normalize(Str("hook_event_name", "trigger", "event", "name")),
                SessionId = Str("session_id", "sessionId"),
                Cwd = Str("cwd"),
                ToolName = Str("tool_name", "toolName"),
                UserPrompt = Str("user_prompt", "userPrompt", "prompt") ?? toolInput,
                TaskName = Str("task_name", "taskName", "task") ?? FirstLine(toolInput),
            };
        }

        private static String? FirstLine(String text)
        {
            if (String.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var line = text.Split('\n', 2)[0].Trim();
            return line.Length == 0 ? null : line;
        }

        private static void Write(HttpListenerContext ctx, Int32 status, Object body)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body));
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }
    }
}
