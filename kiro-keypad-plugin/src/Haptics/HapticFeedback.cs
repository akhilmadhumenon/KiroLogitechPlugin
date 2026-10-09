namespace Loupedeck.KiroKeypadPlugin
{
    using System;

    internal static class HapticFeedback
    {
        public const String NeedsYouEvent = "needsYou";
        public const String TaskDoneEvent = "taskDone";

        private static Plugin? _plugin;

        public static void Init(Plugin plugin)
        {
            _plugin = plugin;
            try
            {
                plugin.PluginEvents.AddEvent(NeedsYouEvent, "Session needs you", "Allow or Deny on the keypad");
                plugin.PluginEvents.AddEvent(TaskDoneEvent, "Spec task finished", "A spec task completed");
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"[haptics] AddEvent failed: {ex.Message}");
            }
        }

        public static void NeedsYou() => Raise(NeedsYouEvent);

        public static void TaskDone() => Raise(TaskDoneEvent);

        private static void Raise(String eventName)
        {
            try
            {
                _plugin?.PluginEvents.RaiseEvent(eventName);
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"[haptics] {eventName}: {ex.Message}");
            }
        }
    }
}
