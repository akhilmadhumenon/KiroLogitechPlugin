namespace Loupedeck.KiroKeypadPlugin.Actions
{
    using System;
    using KiroKeypad;
    using Loupedeck.KiroKeypadPlugin;

    public class SessionControlCommand : PluginDynamicCommand
    {
        public SessionControlCommand()
        {
            this.DisplayName = "Session controls";
            this.Description = "Allow, Always, and Deny or Kill for the pinned session.";
            this.GroupName = "Kiro · Sessions";
            this.AddParameter("allow", "Allow", this.GroupName);
            this.AddParameter("always", "Always", this.GroupName);
            this.AddParameter("deny", "Deny / Kill", this.GroupName);
            Cockpit.Board.Changed += () => this.ActionImageChanged();
        }

        protected override void RunCommand(String actionParameter)
        {
            switch (actionParameter)
            {
                case "allow":
                    Cockpit.Board.ResolvePinned("allow", always: false);
                    break;
                case "always":
                    Cockpit.Board.ResolvePinned("allow", always: true);
                    break;
                default:
                    if (Cockpit.Board.HasPendingDecision())
                    {
                        Cockpit.Board.DenyPending();
                    }
                    else if (Cockpit.Board.KillActive(OpenSessionFeed.FocusedId()) is String sessionId)
                    {
                        CompanionClient.Kill(sessionId);
                    }

                    this.ActionImageChanged();
                    break;
            }
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => String.Empty;

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var needs = Cockpit.Board.HasPendingDecision();
            var armed = !needs && Cockpit.Board.ControlTarget() is { Phase: not SlotPhase.Empty };
            var face = actionParameter switch
            {
                "allow" => TileFace.Action("Allow", Glyph.Check, needsYou: needs),
                "always" => TileFace.Action("Always", Glyph.Spark, needsYou: needs),
                _ when needs => TileFace.Action("Deny", Glyph.Cross, needsYou: true),
                _ => TileFace.Action("Kill", Glyph.Square, dim: !armed),
            };
            return KiroTiles.Render(imageSize, face);
        }
    }
}
