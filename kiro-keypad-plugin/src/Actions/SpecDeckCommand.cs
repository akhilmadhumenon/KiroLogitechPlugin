namespace Loupedeck.KiroKeypadPlugin.Actions
{
    using System;
    using KiroKeypad;
    using Loupedeck.KiroKeypadPlugin;

    public class SpecDeckCommand : PluginDynamicCommand
    {
        private static readonly String[] Ids =
        [
            "next", "run", "pause", "phase", "count", "open", "reqs", "design", "tasks",
        ];

        public SpecDeckCommand()
        {
            this.DisplayName = "Spec";
            this.Description = "Spec phase, task count, and the files behind them.";
            this.GroupName = "Kiro · Spec";
            var labels = new[] { "Next task", "Run task", "Pause", "Phase", "3/11", "Open", "Reqs", "Design", "Tasks" };
            for (var i = 0; i < Ids.Length; i++)
            {
                this.AddParameter(Ids[i], labels[i], this.GroupName);
            }

            Cockpit.Spec.Changed += () => this.ActionImageChanged();
            Cockpit.Board.Changed += () => this.ActionImageChanged();
        }

        protected override void RunCommand(String actionParameter)
        {
            var spec = Cockpit.Spec.Current;
            switch (actionParameter)
            {
                case "next":
                    CompanionClient.Prompt(
                        spec.CurrentTask is String task && task.Length > 0
                            ? "Work on the next spec task: " + task
                            : "Work on the next incomplete task in .kiro/specs.",
                        submit: true);
                    break;
                case "run":
                    CompanionClient.Prompt("Execute the current spec task. Keep going until that task is done.", submit: true);
                    break;
                case "pause":
                    Cockpit.Board.StopPinned();
                    CompanionClient.Stop(Cockpit.Board.ControlTarget()?.SessionId);
                    break;
                case "reqs":
                    CompanionClient.Open("requirements");
                    break;
                case "design":
                    CompanionClient.Open("design");
                    break;
                default:
                    CompanionClient.Open("tasks");
                    break;
            }
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => String.Empty;

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var spec = Cockpit.Spec.Current;
            var count = spec.Total > 0 ? $"{spec.Done}/{spec.Total}" : "0/0";
            var (caption, glyph) = actionParameter switch
            {
                "next" => ("Next", Glyph.Play),
                "run" => ("Run", Glyph.Play),
                "pause" => ("Pause", Glyph.Pause),
                "phase" => (Captions.Fit(spec.Phase, "Spec"), Glyph.Book),
                "count" => (Captions.Fit(count, "0/0"), Glyph.List),
                "open" => ("Open", Glyph.Book),
                "reqs" => ("Reqs", Glyph.List),
                "design" => ("Design", Glyph.Layers),
                _ => ("Tasks", Glyph.Check),
            };
            return KiroTiles.Render(imageSize, TileFace.Action(caption, glyph));
        }
    }
}
