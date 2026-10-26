using System;

namespace WorldfallAIChat
{
    /// <summary>
    /// v7: pause the world while the AI chat is open, with the game's own pause flag (Config.paused, the same one the
    /// pause button uses). If the world was already paused when the chat opened, it is left alone and stays paused after
    /// closing. Worldfall itself respects this flag (its clock, buffs and abilities wait while paused), and our chat window
    /// is plain IMGUI driven by unscaled time, so typing keeps working.
    /// </summary>
    public static class WorldPause
    {
        static bool _pausedByUs;

        public static void Begin(DialogueConfig cfg)
        {
            try
            {
                if (cfg == null || !cfg.PauseWorldDuringChat || _pausedByUs) return;
                if (Config.paused) return;              // already paused by the player: not ours to undo
                Config.paused = true;
                _pausedByUs = true;
                Main.LogInfo("[GodBox] world paused while chatting.");
            }
            catch (Exception e) { Main.LogWarning("[GodBox] could not pause the world: " + e.Message); }
        }

        /// <summary>Re-assert our pause (e.g. Worldfall possession sets Config.paused = false when it starts).</summary>
        public static void Keep(DialogueConfig cfg)
        {
            try
            {
                if (cfg == null || !cfg.PauseWorldDuringChat) return;
                if (_pausedByUs && !Config.paused) Config.paused = true;
            }
            catch { }
        }

        public static void End()
        {
            try
            {
                if (!_pausedByUs) return;
                _pausedByUs = false;
                if (Config.paused) Config.paused = false;
                Main.LogInfo("[GodBox] world resumed (chat closed).");
            }
            catch (Exception e) { Main.LogWarning("[GodBox] could not resume the world: " + e.Message); }
        }
    }
}
