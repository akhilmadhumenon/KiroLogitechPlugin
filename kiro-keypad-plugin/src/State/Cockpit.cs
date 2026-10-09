namespace Loupedeck.KiroKeypadPlugin
{
    using System;
    using System.Linq;
    using System.Threading;
    using KiroKeypad;

    internal static class Cockpit
    {
        public static GateBoard Board { get; } = new();
        public static SpecBoard Spec { get; } = new();
        public static Int32 Pulse { get; private set; }
        public static Int32? ContextTokens { get; private set; }
        public static Boolean ShowContext { get; set; }

        private static Timer? _pulse;
        private static Timer? _spec;
        private static SlotPhase[] _last = new SlotPhase[GateBoard.SlotCount];

        public static void Start()
        {
            Board.Changed += OnChanged;
            _pulse = new Timer(_ =>
            {
                Board.ExpireStopping(TimeSpan.FromSeconds(3));
                if (!Board.Snapshot().Any(s => s.Phase == SlotPhase.Tool))
                {
                    return;
                }

                Pulse ^= 1;
                Board.Touch();
            }, null, 400, 400);
            _spec = new Timer(async _ =>
            {
                var snap = await CompanionClient.SpecAsync().ConfigureAwait(false);
                if (snap != null)
                {
                    Spec.ApplySnapshot(snap);
                }

                ContextTokens = await CompanionClient.ContextAsync().ConfigureAwait(false);
                var sessions = await OpenSessionFeed.ReadAsync().ConfigureAwait(false);
                if (sessions != null)
                {
                    Board.SyncOpenSessions(sessions);
                }
            }, null, 1000, 2500);
        }

        public static void Stop()
        {
            _pulse?.Dispose();
            _spec?.Dispose();
            Board.Changed -= OnChanged;
        }

        public static Object Health()
        {
            var slots = Board.Snapshot();
            return new
            {
                ok = true,
                port = HookHttpServer.Port,
                pinned = Board.PinnedIndex,
                slots = slots.Select(s => new
                {
                    index = s.Index,
                    sessionId = s.SessionId,
                    title = s.Title,
                    phase = s.Phase.ToString(),
                    toolCount = s.ToolCount,
                    pinned = s.Pinned,
                }),
            };
        }

        private static void OnChanged()
        {
            var now = Board.Snapshot();
            for (var i = 0; i < now.Length; i++)
            {
                if (now[i].Phase == SlotPhase.NeedsYou && _last[i] != SlotPhase.NeedsYou)
                {
                    HapticFeedback.NeedsYou();
                }

                _last[i] = now[i].Phase;
            }
        }
    }
}
