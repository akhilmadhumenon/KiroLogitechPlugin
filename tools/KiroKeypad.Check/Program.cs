using KiroKeypad;

var failed = false;

void Fail(String message)
{
    failed = true;
    Console.Error.WriteLine(message);
}

foreach (var face in TileFace.Samples())
{
    foreach (var problem in face.Problems())
    {
        Fail($"{face.Caption}: {problem}");
    }
}

var (done, total, current) = TasksMarkdown.Count("""
    - [x] Write requirements
    - [x] Write design
    - [ ] Implement the gate
    - [ ] Paint the keys
    """);
if (done != 2 || total != 4 || current != "Implement the gate")
{
    Fail($"tasks parser got {done}/{total} current='{current}'");
}

var board = new GateBoard();
var prompt = board.Accept(new HookEvent
{
    Name = "prompt",
    SessionId = "s1",
    UserPrompt = "Fix the login bug",
});
var slots = board.Snapshot();
if (prompt.Permission != "allow" || slots[0].Phase != SlotPhase.Running || slots[0].Title != "Fix the login bug")
{
    Fail("prompt did not occupy slot 0");
}

var gate = Task.Run(() => board.Accept(new HookEvent
{
    Name = "pretool",
    SessionId = "s1",
    ToolName = "shell",
}, TimeSpan.FromSeconds(5)));

var sawNeeds = SpinWait.SpinUntil(
    () => board.Snapshot()[0].Phase == SlotPhase.NeedsYou,
    TimeSpan.FromSeconds(2));
if (!sawNeeds)
{
    Fail("pretool did not raise NeedsYou");
}
else if (!board.ResolvePinned("allow", always: true))
{
    Fail("Allow did not resolve the gate");
}

var allowed = gate.Result;
if (allowed.Permission != "allow")
{
    Fail($"gate returned {allowed.Permission}");
}

var skipped = board.Accept(new HookEvent
{
    Name = "pretool",
    SessionId = "s1",
    ToolName = "shell",
}, TimeSpan.FromSeconds(1));
if (skipped.Permission != "allow" || board.Snapshot()[0].Phase == SlotPhase.NeedsYou)
{
    Fail("Always did not skip the next matching tool");
}

for (var n = 2; n <= 6; n++)
{
    board.Accept(new HookEvent
    {
        Name = "prompt",
        SessionId = "s" + n,
        UserPrompt = "Task " + n,
    });
}

board.Pin(0);
board.Accept(new HookEvent
{
    Name = "prompt",
    SessionId = "s7",
    UserPrompt = "Task 7",
});
var after = board.Snapshot();
if (after[0].SessionId != "s1")
{
    Fail("pinned slot was evicted");
}

if (after.Count(s => s.Phase != SlotPhase.Empty) > GateBoard.SlotCount)
{
    Fail("more than six occupied slots");
}

if (!after.Any(s => s.SessionId == "s7"))
{
    Fail("seventh session was not assigned");
}

var synced = new GateBoard();
List<OpenSession> Open(Int64 latest, String focusedId) =>
    Enumerable.Range(1, 8).Select(i => new OpenSession
    {
        Id = "sess_" + i,
        Title = "Chat " + i,
        LastUsed = i == 1 ? latest : i,
        Focused = "sess_" + i == focusedId,
    }).ToList();

synced.SyncOpenSessions(Open(1, "sess_8"));
var adopted = synced.Snapshot();
for (var i = 0; i < 6; i++)
{
    if (adopted[i].SessionId != "sess_" + (i + 1))
    {
        Fail("sync did not follow tab order: " + String.Join(",", adopted.Select(s => s.SessionId)));
    }
}

synced.SyncOpenSessions(Open(100, "sess_1"));
var next = synced.Snapshot();
for (var i = 0; i < 6; i++)
{
    if (next[i].SessionId != "sess_" + (i + 1))
    {
        Fail("last use reshuffled the tab order: " + String.Join(",", next.Select(s => s.SessionId)));
    }
}

var mirrored = new GateBoard();
mirrored.TryReserve(0);
mirrored.SyncOpenSessions(
[
    new OpenSession { Id = "sess_a", Title = "New Session", LastUsed = 3, Focused = true },
    new OpenSession { Id = "sess_b", Title = "New Coding Session", LastUsed = 2 },
    new OpenSession { Id = "sess_c", Title = "New Session", LastUsed = 1 },
]);
var mirroredSlots = mirrored.Snapshot();
var mirroredIds = mirroredSlots.Where(s => s.Phase != SlotPhase.Empty).Select(s => s.SessionId).ToArray();
if (mirroredIds.Length != 3 || mirroredIds.Any(id => id != null && id.StartsWith("pending:")))
{
    Fail("open chats picked up an extra slot: " + String.Join(",", mirroredIds));
}

if (mirroredSlots[0].SessionId != "sess_a" || mirroredSlots[1].SessionId != "sess_b" || mirroredSlots[2].SessionId != "sess_c")
{
    Fail("open chats were not placed in tab order: " + String.Join(",", mirroredSlots.Select(s => s.SessionId)));
}

mirrored.SyncOpenSessions(
[
    new OpenSession { Id = "sess_b", Title = "New Coding Session", LastUsed = 2 },
    new OpenSession { Id = "sess_c", Title = "New Session", LastUsed = 1 },
]);
var shifted = mirrored.Snapshot();
if (shifted[0].SessionId != "sess_b" || shifted[1].SessionId != "sess_c" || shifted[2].Phase != SlotPhase.Empty)
{
    Fail("closing the first chat did not keep the remaining order: " + String.Join(",", shifted.Select(s => s.SessionId)));
}

var sheet = WriteSheet(TileFace.Samples());
Console.WriteLine(sheet);

if (failed)
{
    Environment.Exit(1);
}

Console.WriteLine("tile and gate checks passed");

static String WriteSheet(IReadOnlyList<TileFace> faces)
{
    var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "contact-sheet.svg");
    var width = faces.Count * 140 + 20;
    var body = new System.Text.StringBuilder();
    body.Append($$"""
        <svg xmlns="http://www.w3.org/2000/svg" width="{{width}}" height="180" viewBox="0 0 {{width}} 180">
          <rect width="100%" height="100%" fill="#000000"/>
        """);
    for (var i = 0; i < faces.Count; i++)
    {
        var face = faces[i];
        var x = 20 + i * 140;
        body.Append($"""
            <g transform="translate({x},30)">
              <rect width="100" height="100" rx="8" fill="{face.Field.Hex}"/>
        """);
        if (face.TopBar)
        {
            body.Append("""<rect width="100" height="4" fill="#C6A0FF"/>""");
        }

        if (face.BottomBand)
        {
            body.Append("""<rect y="94" width="100" height="6" fill="#C6A0FF"/>""");
        }

        if (face.Pip is Rgb pip)
        {
            body.Append($"""<rect x="88" y="8" width="6" height="6" fill="{pip.Hex}"/>""");
        }

        body.Append($"""
              <text x="50" y="78" text-anchor="middle" fill="{face.Ink.Hex}" font-family="sans-serif" font-size="14">{System.Net.WebUtility.HtmlEncode(face.Caption)}</text>
            </g>
        """);
    }

    body.Append("</svg>\n");
    File.WriteAllText(path, body.ToString());
    return path;
}
