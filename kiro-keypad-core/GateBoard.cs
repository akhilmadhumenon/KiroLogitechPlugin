namespace KiroKeypad;

/// <summary>
/// Six session slots and the PreToolUse gate. Accept blocks until Allow, Deny, or Always.
/// </summary>
public sealed class GateBoard
{
    public const Int32 SlotCount = 6;
    public static readonly TimeSpan GateTimeout = TimeSpan.FromMinutes(15);

    private readonly Object _gate = new();
    private readonly Slot[] _slots = new Slot[SlotCount];
    private Int32 _pinned = -1;

    public GateBoard()
    {
        for (var i = 0; i < SlotCount; i++)
        {
            this._slots[i] = new Slot(i);
        }
    }

    public event Action? Changed;

    public void Touch() => this.Changed?.Invoke();

    public void CancelAll()
    {
        List<TaskCompletionSource<String>> waiters = [];
        lock (this._gate)
        {
            foreach (var slot in this._slots)
            {
                if (slot.Waiter != null)
                {
                    waiters.Add(slot.Waiter);
                    slot.Waiter = null;
                    slot.PendingTool = null;
                    if (slot.Phase == SlotPhase.NeedsYou)
                    {
                        slot.Phase = SlotPhase.Running;
                    }
                }
            }
        }

        foreach (var waiter in waiters)
        {
            waiter.TrySetResult("deny");
        }
    }

    public Int32 PinnedIndex
    {
        get
        {
            lock (this._gate)
            {
                return this._pinned;
            }
        }
    }

    public HookResponse Accept(HookEvent ev) => this.Accept(ev, GateTimeout);

    public HookResponse Accept(HookEvent ev, TimeSpan gateTimeout)
    {
        TaskCompletionSource<String>? waiter = null;
        Int32 slotIndex;
        lock (this._gate)
        {
            slotIndex = this.Bind(ev);
            var slot = this._slots[slotIndex];
            slot.LastEventAt = DateTimeOffset.UtcNow;
            switch (ev.Name)
            {
                case "prompt":
                    slot.Phase = SlotPhase.Running;
                    slot.Started = true;
                    slot.Title = FirstLine(ev.UserPrompt) ?? slot.Title;
                    if (String.IsNullOrWhiteSpace(slot.Title))
                    {
                        slot.Title = "Kiro";
                    }

                    slot.Reserved = false;
                    break;
                case "session":
                    if (slot.Phase == SlotPhase.Empty)
                    {
                        slot.Phase = SlotPhase.Resting;
                        slot.Title = "New";
                    }

                    break;
                case "pretool":
                    slot.Started = true;
                    if (this.IsAlwaysAllowed(slot, ev.ToolName))
                    {
                        slot.Phase = SlotPhase.Tool;
                        break;
                    }

                    waiter = new TaskCompletionSource<String>(TaskCreationOptions.RunContinuationsAsynchronously);
                    slot.Waiter = waiter;
                    slot.PendingTool = ev.ToolName ?? "tool";
                    slot.Phase = SlotPhase.NeedsYou;
                    break;
                case "posttool":
                    slot.Started = true;
                    slot.ToolCount++;
                    if (slot.Phase != SlotPhase.NeedsYou)
                    {
                        slot.Phase = SlotPhase.Running;
                    }

                    break;
                case "stop":
                    slot.Waiter?.TrySetResult("deny");
                    slot.Waiter = null;
                    slot.PendingTool = null;
                    slot.Phase = SlotPhase.Done;
                    break;
                case "pretask":
                case "posttask":
                    if (slot.Phase is SlotPhase.Empty or SlotPhase.Resting)
                    {
                        slot.Phase = SlotPhase.Running;
                    }

                    break;
            }
        }

        this.Changed?.Invoke();
        if (waiter == null)
        {
            return new HookResponse { Slot = slotIndex };
        }

        if (!waiter.Task.Wait(gateTimeout))
        {
            lock (this._gate)
            {
                var slot = this._slots[slotIndex];
                if (ReferenceEquals(slot.Waiter, waiter))
                {
                    slot.Waiter = null;
                    slot.PendingTool = null;
                    slot.Phase = SlotPhase.Running;
                }
            }

            this.Changed?.Invoke();
            return new HookResponse
            {
                Slot = slotIndex,
                Permission = "deny",
                Reason = "Keypad gate timed out",
            };
        }

        var permission = waiter.Task.Result;
        return new HookResponse
        {
            Slot = slotIndex,
            Permission = permission,
            Reason = permission == "deny" ? "Denied from the MX Keypad" : null,
        };
    }

    public Boolean ResolvePinned(String permission, Boolean always)
    {
        TaskCompletionSource<String>? waiter;
        lock (this._gate)
        {
            var slot = this.ControlSlot();
            if (slot?.Waiter == null)
            {
                return false;
            }

            if (always && !String.IsNullOrWhiteSpace(slot.PendingTool))
            {
                slot.AlwaysTools.Add(slot.PendingTool);
            }

            waiter = slot.Waiter;
            slot.Waiter = null;
            slot.PendingTool = null;
            slot.Phase = permission == "allow" ? SlotPhase.Tool : SlotPhase.Running;
            slot.LastEventAt = DateTimeOffset.UtcNow;
        }

        waiter.TrySetResult(permission == "allow" ? "allow" : "deny");
        this.Changed?.Invoke();
        return true;
    }

    public Boolean HasPendingDecision()
    {
        lock (this._gate)
        {
            return this._slots.Any(s => s.Waiter != null || s.Phase == SlotPhase.NeedsYou);
        }
    }

    /// <summary>Answer the open question. Leaves the chat alive so Kill can happen on a later press.</summary>
    public Boolean DenyPending()
    {
        TaskCompletionSource<String>? waiter;
        lock (this._gate)
        {
            var slot = this.ControlSlot();
            if (slot?.Waiter == null)
            {
                slot = this._slots.FirstOrDefault(s => s.Waiter != null || s.Phase == SlotPhase.NeedsYou);
            }

            if (slot == null)
            {
                return false;
            }

            waiter = slot.Waiter;
            slot.Waiter = null;
            slot.PendingTool = null;
            if (slot.Phase == SlotPhase.NeedsYou)
            {
                slot.Phase = SlotPhase.Running;
            }

            slot.LastEventAt = DateTimeOffset.UtcNow;
        }

        waiter?.TrySetResult("deny");
        this.Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// End the chat that is open in Kiro. Refuses while a decision is open.
    /// Returns the session id when a chat should be closed, or null when nothing was killed.
    /// </summary>
    public String? KillActive(String? focusedSessionId = null)
    {
        String? sessionId = null;
        var killed = false;
        lock (this._gate)
        {
            if (this._slots.Any(s => s.Waiter != null || s.Phase == SlotPhase.NeedsYou))
            {
                return null;
            }

            var slot = this.SlotFor(focusedSessionId) ?? this.ControlSlot();
            if (slot != null && slot.Phase != SlotPhase.Empty)
            {
                sessionId = slot.SessionId;
                if (this._pinned == slot.Index)
                {
                    this._pinned = -1;
                }

                this.Clear(slot);
                killed = true;
            }
            else if (!String.IsNullOrWhiteSpace(focusedSessionId))
            {
                sessionId = focusedSessionId;
                killed = true;
            }
        }

        if (killed)
        {
            this.Changed?.Invoke();
        }

        return killed ? sessionId ?? "" : null;
    }

    public void StopPinned()
    {
        TaskCompletionSource<String>? waiter = null;
        lock (this._gate)
        {
            var slot = this.ControlSlot();
            if (slot == null || slot.Phase == SlotPhase.Empty)
            {
                return;
            }

            // A chat that never started, or one already stuck on Stop, goes back to an empty S-key.
            if (slot.Waiter == null && slot.Phase is SlotPhase.Resting or SlotPhase.Stopping or SlotPhase.Done)
            {
                if (this._pinned == slot.Index)
                {
                    this._pinned = -1;
                }

                this.Clear(slot);
            }
            else
            {
                if (slot.Waiter != null)
                {
                    waiter = slot.Waiter;
                    slot.Waiter = null;
                    slot.PendingTool = null;
                }

                slot.Phase = SlotPhase.Stopping;
                slot.LastEventAt = DateTimeOffset.UtcNow;
            }
        }

        waiter?.TrySetResult("deny");
        this.Changed?.Invoke();
    }

    /// <summary>A stop that never gets a hook confirmation must not leave the key labeled Stop.</summary>
    public void ExpireStopping(TimeSpan age)
    {
        var changed = false;
        var now = DateTimeOffset.UtcNow;
        lock (this._gate)
        {
            foreach (var slot in this._slots)
            {
                if (slot.Phase != SlotPhase.Stopping || now - slot.LastEventAt < age)
                {
                    continue;
                }

                if (this._pinned == slot.Index)
                {
                    this._pinned = -1;
                }

                this.Clear(slot);
                changed = true;
            }
        }

        if (changed)
        {
            this.Changed?.Invoke();
        }
    }

    public void MarkStopped(Int32 index)
    {
        lock (this._gate)
        {
            if (index < 0 || index >= SlotCount)
            {
                return;
            }

            var slot = this._slots[index];
            if (slot.Phase == SlotPhase.Stopping)
            {
                slot.Phase = SlotPhase.Done;
            }
        }

        this.Changed?.Invoke();
    }

    public void Pin(Int32 index)
    {
        lock (this._gate)
        {
            if (index < 0 || index >= SlotCount || this._slots[index].Phase == SlotPhase.Empty)
            {
                return;
            }

            this._pinned = index;
        }

        this.Changed?.Invoke();
    }

    public void Unpin(Int32 index)
    {
        lock (this._gate)
        {
            if (this._pinned == index)
            {
                this._pinned = -1;
            }
        }

        this.Changed?.Invoke();
    }

    public Int32? TryReserve(Int32? preferred = null)
    {
        Int32? reserved = null;
        lock (this._gate)
        {
            if (this.UnstartedSlotLocked() != null)
            {
                return null;
            }

            Slot? slot = null;
            if (preferred is Int32 index && index >= 0 && index < SlotCount && this._slots[index].Phase == SlotPhase.Empty)
            {
                slot = this._slots[index];
            }

            slot ??= this._slots.FirstOrDefault(s => s.Phase == SlotPhase.Empty)
                ?? this.EvictUnpinned();
            if (slot == null)
            {
                return null;
            }

            this.Clear(slot);
            slot.Reserved = true;
            slot.Started = false;
            slot.Phase = SlotPhase.Resting;
            slot.Title = "New";
            slot.SessionId = "pending:" + Guid.NewGuid().ToString("N");
            slot.LastEventAt = DateTimeOffset.UtcNow;
            this._pinned = slot.Index;
            reserved = slot.Index;
        }

        this.Changed?.Invoke();
        return reserved;
    }

    /// <summary>A New chat that has not received a prompt yet. Slot order does not matter.</summary>
    public Int32? UnstartedSlot()
    {
        lock (this._gate)
        {
            return this.UnstartedSlotLocked();
        }
    }

    private Int32? UnstartedSlotLocked() =>
        this._slots.FirstOrDefault(s => !s.Started && s.Phase == SlotPhase.Resting)?.Index;

    public SlotView[] Snapshot()
    {
        lock (this._gate)
        {
            return this._slots.Select(s => s.View(s.Index == this._pinned)).ToArray();
        }
    }

    public SlotView? ControlTarget()
    {
        lock (this._gate)
        {
            return this.ControlSlot()?.View(pinned: true);
        }
    }

    private Slot? SlotFor(String? sessionId)
    {
        if (String.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        return this._slots.FirstOrDefault(s => s.Phase != SlotPhase.Empty && SameSession(s.SessionId, sessionId));
    }

    private Slot? ControlSlot()
    {
        if (this._pinned >= 0)
        {
            var pinned = this._slots[this._pinned];
            if (pinned.Phase != SlotPhase.Empty)
            {
                return pinned;
            }
        }

        return this._slots.FirstOrDefault(s => s.Phase == SlotPhase.NeedsYou)
            ?? this._slots
                .Where(s => s.Phase != SlotPhase.Empty)
                .OrderByDescending(s => s.LastEventAt)
                .FirstOrDefault();
    }

    private Int32 Bind(HookEvent ev)
    {
        var id = ev.SessionId;
        if (!String.IsNullOrWhiteSpace(id))
        {
            var existing = this._slots.FirstOrDefault(s => SameSession(s.SessionId, id));
            if (existing != null)
            {
                return existing.Index;
            }
        }

        var reserved = this._slots.FirstOrDefault(s => s.Reserved);
        if (reserved != null)
        {
            reserved.Reserved = false;
            if (!String.IsNullOrWhiteSpace(id))
            {
                reserved.SessionId = id;
            }

            return reserved.Index;
        }

        var empty = this._slots.FirstOrDefault(s => s.Phase == SlotPhase.Empty);
        var slot = empty ?? this.EvictUnpinned() ?? this._slots[0];
        if (empty == null)
        {
            this.Clear(slot);
        }

        slot.SessionId = String.IsNullOrWhiteSpace(id) ? "local:" + Guid.NewGuid().ToString("N") : id;
        if (slot.Phase == SlotPhase.Empty)
        {
            slot.Phase = SlotPhase.Resting;
            slot.Title = "Kiro";
        }

        return slot.Index;
    }

    /// <summary>
    /// Mirror Kiro's open chats onto the six keys in tab order.
    /// The first chat tab is key 1. Tabs do not move, so the keys stay in that same order.
    /// </summary>
    public void SyncOpenSessions(IReadOnlyList<OpenSession> open)
    {
        var keep = new List<OpenSession>(SlotCount);
        var seen = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in open)
        {
            if (String.IsNullOrWhiteSpace(session.Id) || !seen.Add(NormalizeSession(session.Id)))
            {
                continue;
            }

            keep.Add(session);
            if (keep.Count == SlotCount)
            {
                break;
            }
        }

        var changed = false;
        lock (this._gate)
        {
            var pinnedId = this._pinned >= 0 ? this._slots[this._pinned].SessionId : null;
            for (var i = 0; i < keep.Count; i++)
            {
                var session = keep[i];
                var dest = this._slots[i];
                if (SameSession(dest.SessionId, session.Id))
                {
                    changed |= this.TouchOpen(dest, session);
                    continue;
                }

                var src = this._slots.FirstOrDefault(s => SameSession(s.SessionId, session.Id));
                if (src != null)
                {
                    if (IsSticky(dest) && !keep.Any(s => SameSession(s.Id, dest.SessionId)))
                    {
                        continue;
                    }

                    Swap(dest, src);
                    changed |= this.TouchOpen(dest, session);
                    changed = true;
                    continue;
                }

                if (IsSticky(dest))
                {
                    continue;
                }

                if (dest.Phase != SlotPhase.Empty)
                {
                    this.Clear(dest);
                }

                this.AdoptOpen(dest, session);
                changed = true;
            }

            foreach (var slot in this._slots)
            {
                if (slot.Phase == SlotPhase.Empty || IsSticky(slot))
                {
                    continue;
                }

                if (keep.Any(s => SameSession(s.Id, slot.SessionId)))
                {
                    continue;
                }

                if (this._pinned == slot.Index)
                {
                    this._pinned = -1;
                }

                this.Clear(slot);
                changed = true;
            }

            if (!String.IsNullOrWhiteSpace(pinnedId))
            {
                var followed = this._slots.FirstOrDefault(s => SameSession(s.SessionId, pinnedId));
                var followedIndex = followed?.Index ?? -1;
                if (this._pinned != followedIndex)
                {
                    this._pinned = followedIndex;
                    changed = true;
                }
            }

            if (this._pinned < 0 || this._slots[this._pinned].Phase == SlotPhase.Empty)
            {
                var focused = keep.FirstOrDefault(s => s.Focused);
                var slot = focused == null
                    ? null
                    : this._slots.FirstOrDefault(s => SameSession(s.SessionId, focused.Id));
                if (slot != null && this._pinned != slot.Index)
                {
                    this._pinned = slot.Index;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            this.Changed?.Invoke();
        }
    }

    private Boolean TouchOpen(Slot slot, OpenSession session)
    {
        var changed = false;
        if (!IsBlankTitle(session.Title)
            && slot.Title != session.Title
            && slot.Phase is not (SlotPhase.NeedsYou or SlotPhase.Stopping))
        {
            slot.Title = session.Title;
            changed = true;
        }

        var stamp = Stamp(session);
        if (stamp > slot.LastEventAt)
        {
            slot.LastEventAt = stamp;
            changed = true;
        }

        return changed;
    }

    private void AdoptOpen(Slot slot, OpenSession session)
    {
        var blank = IsBlankTitle(session.Title);
        slot.SessionId = session.Id;
        slot.Title = blank ? "New" : session.Title;
        slot.Phase = blank ? SlotPhase.Resting : SlotPhase.Running;
        slot.Reserved = false;
        slot.Started = true;
        slot.ToolCount = 0;
        slot.PendingTool = null;
        slot.AlwaysTools.Clear();
        slot.LastEventAt = Stamp(session);
    }

    private static Boolean IsSticky(Slot slot) => slot.Waiter != null || slot.Phase == SlotPhase.NeedsYou;

    private static void Swap(Slot left, Slot right)
    {
        (left.SessionId, right.SessionId) = (right.SessionId, left.SessionId);
        (left.Title, right.Title) = (right.Title, left.Title);
        (left.Phase, right.Phase) = (right.Phase, left.Phase);
        (left.ToolCount, right.ToolCount) = (right.ToolCount, left.ToolCount);
        (left.PendingTool, right.PendingTool) = (right.PendingTool, left.PendingTool);
        (left.Reserved, right.Reserved) = (right.Reserved, left.Reserved);
        (left.Started, right.Started) = (right.Started, left.Started);
        (left.LastEventAt, right.LastEventAt) = (right.LastEventAt, left.LastEventAt);
        (left.Waiter, right.Waiter) = (right.Waiter, left.Waiter);
        var tools = left.AlwaysTools.ToArray();
        left.AlwaysTools.Clear();
        foreach (var tool in right.AlwaysTools)
        {
            left.AlwaysTools.Add(tool);
        }

        right.AlwaysTools.Clear();
        foreach (var tool in tools)
        {
            right.AlwaysTools.Add(tool);
        }
    }

    private static DateTimeOffset Stamp(OpenSession session) =>
        session.Focused
            ? DateTimeOffset.UtcNow
            : DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(0, session.LastUsed));

    private static Boolean IsBlankTitle(String? title) =>
        String.IsNullOrWhiteSpace(title)
        || title.Equals("New", StringComparison.OrdinalIgnoreCase)
        || title.Equals("New Session", StringComparison.OrdinalIgnoreCase)
        || title.Equals("New chat", StringComparison.OrdinalIgnoreCase);

    private static Boolean SameSession(String? left, String? right)
    {
        if (String.IsNullOrWhiteSpace(left) || String.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        if (String.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }

        return String.Equals(NormalizeSession(left), NormalizeSession(right), StringComparison.OrdinalIgnoreCase);
    }

    private static String NormalizeSession(String id)
    {
        var value = id.Trim();
        return value.StartsWith("sess_", StringComparison.OrdinalIgnoreCase) ? value[5..] : value;
    }

    private Slot? EvictUnpinned()
    {
        return this._slots
            .Where(s => s.Index != this._pinned && s.Phase != SlotPhase.NeedsYou && s.Phase != SlotPhase.Empty)
            .OrderBy(s => s.LastEventAt)
            .FirstOrDefault();
    }

    private void Clear(Slot slot)
    {
        slot.Waiter?.TrySetResult("deny");
        slot.Waiter = null;
        slot.SessionId = null;
        slot.Title = "";
        slot.Phase = SlotPhase.Empty;
        slot.ToolCount = 0;
        slot.PendingTool = null;
        slot.Reserved = false;
        slot.Started = false;
        slot.AlwaysTools.Clear();
    }

    private Boolean IsAlwaysAllowed(Slot slot, String? toolName)
    {
        if (String.IsNullOrWhiteSpace(toolName))
        {
            return false;
        }

        return slot.AlwaysTools.Any(saved =>
            toolName.Contains(saved, StringComparison.OrdinalIgnoreCase)
            || saved.Contains(toolName, StringComparison.OrdinalIgnoreCase));
    }

    private static String? FirstLine(String? text)
    {
        if (String.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var line = text.Split('\n', 2)[0].Trim();
        return line.Length == 0 ? null : line;
    }

    private sealed class Slot(Int32 index)
    {
        public Int32 Index { get; } = index;
        public String? SessionId { get; set; }
        public String Title { get; set; } = "";
        public SlotPhase Phase { get; set; } = SlotPhase.Empty;
        public Int32 ToolCount { get; set; }
        public String? PendingTool { get; set; }
        public Boolean Reserved { get; set; }
        public Boolean Started { get; set; }
        public DateTimeOffset LastEventAt { get; set; }
        public TaskCompletionSource<String>? Waiter { get; set; }
        public HashSet<String> AlwaysTools { get; } = new(StringComparer.OrdinalIgnoreCase);

        public SlotView View(Boolean pinned) => new()
        {
            Index = this.Index,
            SessionId = this.SessionId,
            Title = this.Title,
            Phase = this.Phase,
            ToolCount = this.ToolCount,
            Pinned = pinned,
            PendingTool = this.PendingTool,
            Reserved = this.Reserved,
            LastEventAt = this.LastEventAt,
        };
    }
}

public sealed class OpenSession
{
    public required String Id { get; init; }
    public String Title { get; init; } = "";
    public Int64 LastUsed { get; init; }
    public Boolean Focused { get; init; }
}
