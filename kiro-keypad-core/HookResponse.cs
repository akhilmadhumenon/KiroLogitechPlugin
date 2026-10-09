namespace KiroKeypad;

public sealed class HookResponse
{
    public Boolean Ok { get; init; } = true;
    public String Permission { get; init; } = "allow";
    public String? Reason { get; init; }
    public Int32 Slot { get; init; } = -1;
}
