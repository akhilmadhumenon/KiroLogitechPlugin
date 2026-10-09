namespace Loupedeck.KiroKeypadPlugin.Actions
{
    using System;
    using KiroKeypad;
    using Loupedeck.KiroKeypadPlugin;

    public class StudioDeckCommand : PluginDynamicCommand
    {
        private static readonly String[] Ids =
        [
            "context", "model", "autonomy", "hooks", "diff", "problems", "accept", "undo", "terminal",
        ];

        public StudioDeckCommand()
        {
            this.DisplayName = "Studio";
            this.Description = "Context, model, autonomy, and editor commands.";
            this.GroupName = "Kiro · Studio";
            var labels = new[] { "Context", "Model", "Autonomy", "Hooks", "Diff", "Problems", "Accept", "Undo", "Terminal" };
            for (var i = 0; i < Ids.Length; i++)
            {
                this.AddParameter(Ids[i], labels[i], this.GroupName);
            }

            Cockpit.Board.Changed += () => this.ActionImageChanged();
            Cockpit.Spec.Changed += () => this.ActionImageChanged();
        }

        protected override void RunCommand(String actionParameter)
        {
            switch (actionParameter)
            {
                case "context":
                    Cockpit.ShowContext = !Cockpit.ShowContext;
                    this.ActionImageChanged();
                    break;
                case "model":
                    CompanionClient.Command("kiro.chat.selectModel");
                    break;
                case "autonomy":
                    CompanionClient.Command("kiro.agent.cycleAutonomy");
                    break;
                case "hooks":
                    CompanionClient.Open("hooks");
                    break;
                case "diff":
                    CompanionClient.Command("workbench.action.compareEditor.nextChange");
                    break;
                case "problems":
                    CompanionClient.Command("workbench.actions.view.problems");
                    break;
                case "accept":
                    CompanionClient.Command("kiro.chat.accept");
                    break;
                case "undo":
                    CompanionClient.Command("undo");
                    break;
                default:
                    CompanionClient.Command("workbench.action.terminal.toggleTerminal");
                    break;
            }
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => String.Empty;

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var pinned = Cockpit.Board.ControlTarget();
            var activity = pinned?.ToolCount ?? 0;
            var context = Cockpit.ShowContext
                ? Cockpit.ContextTokens is Int32 tokens ? "C " + Compact(tokens) : "Ctx --"
                : "A " + activity;
            var (caption, glyph) = actionParameter switch
            {
                "context" => (context, Glyph.Gauge),
                "model" => ("Model", Glyph.Spark),
                "autonomy" => ("Auto", Glyph.Layers),
                "hooks" => ("Hooks", Glyph.List),
                "diff" => ("Diff", Glyph.Diff),
                "problems" => ("Problems", Glyph.Alert),
                "accept" => ("Accept", Glyph.Check),
                "undo" => ("Undo", Glyph.Undo),
                _ => ("Term", Glyph.Terminal),
            };
            return KiroTiles.Render(imageSize, TileFace.Action(caption, glyph));
        }

        private static String Compact(Int32 value) =>
            value >= 1_000_000 ? (value / 1_000_000) + "M"
            : value >= 1_000 ? (value / 1_000) + "k"
            : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
