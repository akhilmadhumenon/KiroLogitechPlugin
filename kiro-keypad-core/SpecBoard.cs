namespace KiroKeypad;

public sealed class SpecSnapshot
{
    public String Phase { get; init; } = "Spec";
    public Int32 Done { get; init; }
    public Int32 Total { get; init; }
    public String? CurrentTask { get; init; }
    public String? SpecDir { get; init; }
    public String? RequirementsPath { get; init; }
    public String? DesignPath { get; init; }
    public String? TasksPath { get; init; }
}

public sealed class SpecBoard
{
    private readonly Object _gate = new();
    private SpecSnapshot _current = new();

    public event Action? Changed;

    public SpecSnapshot Current
    {
        get
        {
            lock (this._gate)
            {
                return this._current;
            }
        }
    }

    public void ApplyHook(HookEvent ev)
    {
        if (ev.Name is not ("pretask" or "posttask"))
        {
            return;
        }

        lock (this._gate)
        {
            var done = this._current.Done;
            var total = Math.Max(this._current.Total, 1);
            if (ev.Name == "posttask")
            {
                done = Math.Min(total, done + 1);
            }

            this._current = new SpecSnapshot
            {
                Phase = "Tasks",
                Done = done,
                Total = total,
                CurrentTask = ev.TaskName ?? this._current.CurrentTask,
                SpecDir = this._current.SpecDir,
                RequirementsPath = this._current.RequirementsPath,
                DesignPath = this._current.DesignPath,
                TasksPath = this._current.TasksPath,
            };
        }

        this.Changed?.Invoke();
    }

    public void ApplySnapshot(SpecSnapshot snapshot)
    {
        lock (this._gate)
        {
            var same = this._current.Phase == snapshot.Phase
                && this._current.Done == snapshot.Done
                && this._current.Total == snapshot.Total
                && this._current.CurrentTask == snapshot.CurrentTask
                && this._current.SpecDir == snapshot.SpecDir;
            if (same)
            {
                return;
            }

            this._current = snapshot;
        }

        this.Changed?.Invoke();
    }
}
