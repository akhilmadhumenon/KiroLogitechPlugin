namespace Loupedeck.KiroKeypadPlugin.Actions
{
    using System;
    using System.Collections.Concurrent;
    using System.Globalization;
    using KiroKeypad;
    using Loupedeck.KiroKeypadPlugin;

    public class SessionSlotCommand : PluginDynamicCommand
    {
        private static readonly TimeSpan LongPress = TimeSpan.FromMilliseconds(550);
        private readonly ConcurrentDictionary<String, DateTimeOffset> _down = new();
        private readonly ConcurrentDictionary<String, Byte> _handled = new();

        public SessionSlotCommand()
        {
            this.DisplayName = "Sessions";
            this.Description = "Six Kiro sessions. Short-press pins and focuses. Long-press unpins.";
            this.GroupName = "Kiro · Sessions";
            for (var i = 1; i <= GateBoard.SlotCount; i++)
            {
                this.AddParameter(i.ToString(CultureInfo.InvariantCulture), "S" + i, this.GroupName);
            }

            Cockpit.Board.Changed += () => this.ActionImageChanged();
        }

        protected override Boolean ProcessButtonEvent2(String actionParameter, DeviceButtonEvent2 buttonEvent)
        {
            if (buttonEvent.IsPress())
            {
                this._down[actionParameter] = DateTimeOffset.UtcNow;
                this._handled.TryRemove(actionParameter, out _);
                return true;
            }

            if (buttonEvent.IsLongPress())
            {
                this.Handle(actionParameter, longPress: true);
                this._handled[actionParameter] = 1;
                this._down.TryRemove(actionParameter, out _);
                return true;
            }

            if (buttonEvent.IsRelease())
            {
                if (this._handled.TryRemove(actionParameter, out _))
                {
                    return true;
                }

                var longPress = this._down.TryRemove(actionParameter, out var started)
                    && DateTimeOffset.UtcNow - started >= LongPress;
                this.Handle(actionParameter, longPress);
                return true;
            }

            return false;
        }

        protected override void RunCommand(String actionParameter) => this.Handle(actionParameter, longPress: false);

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => String.Empty;

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var index = Index(actionParameter);
            var slots = Cockpit.Board.Snapshot();
            var slot = slots[Math.Clamp(index, 0, slots.Length - 1)];
            var title = slot.Phase == SlotPhase.Empty ? "S" + (slot.Index + 1) : slot.Title;
            var face = TileFace.Slot(slot.Phase, title, slot.Pinned, Cockpit.Pulse == 1);
            return KiroTiles.Render(imageSize, face);
        }

        private void Handle(String actionParameter, Boolean longPress)
        {
            var index = Index(actionParameter);
            if (index < 0)
            {
                return;
            }

            var slot = Cockpit.Board.Snapshot()[index];
            if (longPress)
            {
                Cockpit.Board.Unpin(index);
                return;
            }

            if (slot.Phase is SlotPhase.Empty or SlotPhase.Stopping)
            {
                if (slot.Phase == SlotPhase.Stopping)
                {
                    Cockpit.Board.StopPinned();
                }

                if (Cockpit.Board.TryReserve(index) != null)
                {
                    CompanionClient.NewChat();
                }
                else if (Cockpit.Board.UnstartedSlot() is Int32 pending)
                {
                    var existing = Cockpit.Board.Snapshot()[pending];
                    Cockpit.Board.Pin(pending);
                    CompanionClient.Focus(existing.SessionId, existing.Title);
                }

                return;
            }

            Cockpit.Board.Pin(index);
            CompanionClient.Focus(slot.SessionId, slot.Title);
        }

        private static Int32 Index(String? actionParameter) =>
            Int32.TryParse(actionParameter, out var oneBased) && oneBased is >= 1 and <= GateBoard.SlotCount
                ? oneBased - 1
                : -1;
    }
}
