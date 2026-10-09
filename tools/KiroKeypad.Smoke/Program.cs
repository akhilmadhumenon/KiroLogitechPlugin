using Loupedeck.KiroKeypadPlugin;

using var server = new HookHttpServer();
server.Start();
Console.WriteLine("ready");
await Task.Delay(Timeout.Infinite);
