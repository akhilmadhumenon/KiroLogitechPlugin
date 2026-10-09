namespace KiroKeypad;

public sealed class SlotView
{
    public Int32 Index { get; init; }
    public String? SessionId { get; init; }
    public String Title { get; init; } = "";
    public SlotPhase Phase { get; init; }
    public Int32 ToolCount { get; init; }
    public Boolean Pinned { get; init; }
    public String? PendingTool { get; init; }
    public Boolean Reserved { get; init; }
    public DateTimeOffset LastEventAt { get; init; }
}
