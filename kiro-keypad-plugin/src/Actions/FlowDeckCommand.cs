namespace Loupedeck.KiroKeypadPlugin.Actions
{
    using System;
    using System.Globalization;
    using KiroKeypad;
    using Loupedeck.KiroKeypadPlugin;

    public class FlowDeckCommand : PluginDynamicCommand
    {
        public FlowDeckCommand()
        {
            this.DisplayName = "Flow";
            this.Description = "Spec, plan, review, and the other prompts from settings.";
            this.GroupName = "Kiro · Flow";
            for (var i = 0; i < FlowKey.Defaults.Count; i++)
            {
                var key = FlowKey.Defaults[i];
                this.AddParameter((i + 1).ToString(CultureInfo.InvariantCulture), key.Label, this.GroupName);
            }
        }

        protected override void RunCommand(String actionParameter)
        {
            var keys = SettingsStore.Flow;
            if (!Int32.TryParse(actionParameter, out var oneBased) || oneBased < 1 || oneBased > keys.Count)
            {
                return;
            }

            var key = keys[oneBased - 1];
            switch (key.Id)
            {
                case "steer":
                    CompanionClient.Steer();
                    break;
                case "new":
                    if (Cockpit.Board.TryReserve() != null)
                    {
                        CompanionClient.NewChat();
                    }

                    break;
                case "focus":
                    CompanionClient.Focus(null);
                    break;
                default:
                    CompanionClient.Prompt(key.Prompt, key.Submit);
                    break;
            }
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => String.Empty;

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var keys = SettingsStore.Flow;
            if (!Int32.TryParse(actionParameter, out var oneBased) || oneBased < 1 || oneBased > keys.Count)
            {
                return KiroTiles.Render(imageSize, TileFace.Action("Flow", Glyph.Spark, dim: true));
            }

            var key = keys[oneBased - 1];
            var glyph = key.Id switch
            {
                "spec" => Glyph.Book,
                "plan" => Glyph.List,
                "bugfix" => Glyph.Alert,
                "review" => Glyph.Check,
                "tests" => Glyph.List,
                "explain" => Glyph.Chat,
                "steer" => Glyph.Layers,
                "new" => Glyph.Chat,
                _ => Glyph.Spark,
            };
            return KiroTiles.Render(imageSize, TileFace.Action(key.Label, glyph));
        }
    }
}
