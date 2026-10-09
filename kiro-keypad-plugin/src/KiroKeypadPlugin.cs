namespace Loupedeck.KiroKeypadPlugin
{
    using System;

    public class KiroKeypadPlugin : Plugin
    {
        private HookHttpServer? _hooks;

        public override Boolean UsesApplicationApiOnly => true;

        public override Boolean HasNoApplication => true;

        public KiroKeypadPlugin() => PluginLog.Init(this.Log);

        public override void Load()
        {
            HapticFeedback.Init(this);
            SettingsStore.Ensure();
            Cockpit.Start();
            try
            {
                this._hooks = new HookHttpServer();
                this._hooks.Start();
                PluginLog.Info("Kiro Keypad loaded");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Failed to start hook relay on 47831");
            }
        }

        public override void Unload()
        {
            Cockpit.Board.CancelAll();
            Cockpit.Stop();
            this._hooks?.Dispose();
            this._hooks = null;
        }
    }
}
