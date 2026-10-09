namespace KiroKeypad;

public sealed class FlowKey
{
    public FlowKey(String id, String label, String prompt, Boolean submit)
    {
        this.Id = id;
        this.Label = label;
        this.Prompt = prompt;
        this.Submit = submit;
    }

    public String Id { get; }
    public String Label { get; }
    public String Prompt { get; }
    public Boolean Submit { get; }

    public static IReadOnlyList<FlowKey> Defaults { get; } =
    [
        new("spec", "Spec", "Start a Spec workflow. Ask what is unclear, then write requirements, design, and tasks under .kiro/specs.", true),
        new("plan", "Plan", "Plan only. Break this into an implementation plan and do not edit files yet.", true),
        new("bugfix", "Bugfix", "Bug fix workflow: investigate, diagnose, and then fix the failure.", true),
        new("review", "Review", "Review the current changes for correctness, regressions, and missing tests.", true),
        new("tests", "Tests", "Add or update tests for the behavior that just changed.", true),
        new("explain", "Explain", "Explain the selected code and how it fits the surrounding system.", true),
        new("steer", "Steer", "", false),
        new("new", "New chat", "", false),
        new("focus", "Focus", "", false),
    ];
}
