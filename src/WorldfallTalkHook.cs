using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;

namespace WorldfallAIChat
{
    /// <summary>
    /// Adds a "Conversa livre (IA)" option to Worldfall's E-key talk menu.
    ///
    /// Hook (checked against the decompiled Worldfall 0.9.8.9 assembly, names only):
    ///   internal static List&lt;FirstPerson.Choice&gt; FirstPerson.Dialogue.Topics(FirstPerson.Conversation c)
    /// builds the main list of talk options ("Tell me about your family.", ..., "Goodbye.").
    /// A Harmony postfix inserts one more FirstPerson.Choice(label, Action, ChoiceTone.Plain) into the returned list.
    /// Picking it opens our chat with Conversation.With (Conversation.You speaks); once our window is visible,
    /// Worldfall's conversation is ended through its own WorldBoxMod.EndConversation().
    /// Everything is reflection-based and guarded: if Worldfall is absent or renamed something, nothing is patched
    /// and the F3 hotkey / "Conversar" button still work.
    /// </summary>
    public static class WorldfallTalkHook
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static Type _choice, _tone;
        public static bool Applied;

        public static void Apply(Harmony h)
        {
            try
            {
                var dialogue = AccessTools.TypeByName("FirstPerson.Dialogue");
                var conversation = AccessTools.TypeByName("FirstPerson.Conversation");
                _choice = AccessTools.TypeByName("FirstPerson.Choice");
                _tone = AccessTools.TypeByName("FirstPerson.ChoiceTone");
                if (dialogue == null || conversation == null || _choice == null || _tone == null)
                {
                    Main.LogAlways("[GodBox] Worldfall talk menu not found: menu option disabled (F3 / Conversar button still work).");
                    return;
                }
                var topics = AccessTools.Method(dialogue, "Topics", new[] { conversation });
                if (topics == null || !typeof(IList).IsAssignableFrom(topics.ReturnType))
                {
                    Main.LogWarning("[GodBox] Worldfall Dialogue.Topics(Conversation) changed: menu option disabled.");
                    return;
                }
                h.Patch(topics, postfix: new HarmonyMethod(typeof(WorldfallTalkHook), nameof(TopicsPostfix)));
                ApplyMenuStatePatches(h);
                ApplyHostedDrawPatch(h);
                Applied = true;
                Main.LogAlways("[GodBox] added the AI option to Worldfall's talk menu (FirstPerson.Dialogue.Topics).");
            }
            catch (Exception e)
            {
                Main.LogWarning("[GodBox] could not hook Worldfall's talk menu: " + e.Message);
            }
        }

        static object F(object o, string name)
        {
            if (o == null) return null;
            var t = o.GetType();
            var f = t.GetField(name, All);
            if (f != null) return f.GetValue(o);
            return t.GetProperty(name, All)?.GetValue(o, null);
        }

        // __0 = the Conversation argument, __result = the List<Choice> being returned.
        static void TopicsPostfix(object __0, object __result)
        {
            try
            {
                var cfg = Main.Cfg;
                if (cfg == null || !cfg.WorldfallMenuOption || !(__result is IList list) || __0 == null) return;
                var conv = __0;
                Action act = () => OnOptionPicked(conv);
                string label = string.IsNullOrWhiteSpace(cfg.WorldfallMenuLabel) ? "Conversa livre (IA)" : cfg.WorldfallMenuLabel;
                object choice = Activator.CreateInstance(_choice, All, null, new object[] { label, act, Enum.ToObject(_tone, 0) }, null);
                int index = 0;
                if (string.Equals(cfg.WorldfallMenuPosition, "before_goodbye", StringComparison.OrdinalIgnoreCase))
                    index = Math.Max(0, list.Count - 1);
                list.Insert(index, choice);
            }
            catch (Exception e)
            {
                Main.LogWarning("[GodBox] Worldfall menu option failed: " + e.Message);
            }
        }

        /// <summary>
        /// Runs inside Worldfall's Conversation.Pick (called from Worldfall's OnGUI). Do as little as possible here:
        /// read the two actors and hand off to our window on the next Update. Worldfall's conversation is NOT closed
        /// here; ChatWindow closes it through Worldfall's own path only after our window has actually been drawn.
        /// </summary>
        static void OnOptionPicked(object conv)
        {
            Main.LogInfo("[GodBox] option clicked in Worldfall's talk menu.");
            try
            {
                var npc = F(conv, "With") as Actor;
                var you = F(conv, "You") as Actor;
                Main.LogInfo("[GodBox] target: " + SafeName(npc) + ", speaker: " + SafeName(you));
                if (ChatWindow.Instance == null)
                {
                    Main.LogError("[GodBox] chat window component is missing; cannot open.");
                    return;
                }
                Main.RunOnMainThread(() =>
                {
                    try { ChatWindow.Instance.OpenFromWorldfall(npc, you, conv); }
                    catch (Exception e) { Main.LogError("[GodBox] opening the chat failed: " + e); ChatWindow.Instance?.Close(); }
                });
            }
            catch (Exception e)
            {
                Main.LogError("[GodBox] option handler failed: " + e);
            }
        }

        static string SafeName(Actor a) { try { return a == null ? "null" : a.getName(); } catch { return "?"; } }

        /// <summary>
        /// Ends Worldfall's conversation the way Worldfall itself does when you leave a talk:
        /// FirstPerson.WorldBoxMod.Instance.EndConversation() (closes, unfreezes the possessed unit, clears look-at).
        /// Falls back to Conversation.End(), which calls the same method through its Ended callback.
        /// </summary>
        public static void EndWorldfallConversation(object conv)
        {
            try
            {
                if (conv != null && !(F(conv, "Open") is bool open && open))
                {
                    Main.LogInfo("[GodBox] Worldfall conversation already closed.");
                    return;
                }
                var modType = AccessTools.TypeByName("FirstPerson.WorldBoxMod");
                var inst = modType == null ? null : (AccessTools.Property(modType, "Instance")?.GetValue(null, null) ?? AccessTools.Field(modType, "Instance")?.GetValue(null));
                var end = modType == null ? null : AccessTools.Method(modType, "EndConversation", Type.EmptyTypes);
                if (inst != null && end != null)
                {
                    end.Invoke(inst, null);
                    Main.LogInfo("[GodBox] Worldfall conversation closed (WorldBoxMod.EndConversation).");
                    return;
                }
                var endConv = conv?.GetType().GetMethod("End", All, null, Type.EmptyTypes, null);
                if (endConv != null)
                {
                    endConv.Invoke(conv, null);
                    Main.LogInfo("[GodBox] Worldfall conversation closed (Conversation.End).");
                    return;
                }
                Main.LogWarning("[GodBox] could not find a way to close Worldfall's conversation.");
            }
            catch (Exception e)
            {
                Main.LogWarning("[GodBox] closing Worldfall's conversation failed: " + (e.InnerException ?? e).Message);
            }
        }

        /// <summary>
        /// While our window is visible, tell Worldfall a menu is open and the keyboard is taken. Worldfall then frees
        /// the cursor, stops mouse-look/attacks and mutes its keys by itself (the same state as its own menus).
        /// Getters: FirstPerson.WorldBoxMod.AnyMenu (private) and KeyboardTaken (internal).
        /// </summary>
        public static void ApplyMenuStatePatches(Harmony h)
        {
            var modType = AccessTools.TypeByName("FirstPerson.WorldBoxMod");
            if (modType == null) return;
            foreach (var prop in new[] { "AnyMenu", "KeyboardTaken" })
            {
                try
                {
                    var getter = AccessTools.PropertyGetter(modType, prop);
                    if (getter == null || getter.ReturnType != typeof(bool)) { Main.LogWarning("[GodBox] Worldfall " + prop + " not found (skipped)."); continue; }
                    h.Patch(getter, postfix: new HarmonyMethod(typeof(WorldfallTalkHook), nameof(MenuOpenPostfix)));
                    Main.LogInfo("[GodBox] Worldfall " + prop + " patched (frees cursor/keys while chat is open).");
                }
                catch (Exception e) { Main.LogWarning("[GodBox] could not patch Worldfall " + prop + ": " + e.Message); }
            }
        }

        /// <summary>
        /// Paint our chat from inside Worldfall's own GUI pass: postfix on the private FirstPerson.WorldBoxMod.OnGUIBody(),
        /// which draws Worldfall's 3D frame (GUI.DrawTexture of its rendered view), HUD and menus. Running after it puts
        /// our panel on top of Worldfall's first-person view, the same way Worldfall's own talk menu is visible.
        /// </summary>
        public static void ApplyHostedDrawPatch(Harmony h)
        {
            try
            {
                var modType = AccessTools.TypeByName("FirstPerson.WorldBoxMod");
                var body = modType == null ? null : AccessTools.Method(modType, "OnGUIBody", Type.EmptyTypes);
                if (body == null) { Main.LogWarning("[GodBox] Worldfall OnGUIBody not found: chat may be hidden under the first-person view."); return; }
                h.Patch(body, postfix: new HarmonyMethod(typeof(WorldfallTalkHook), nameof(OnGuiBodyPostfix)));
                Main.LogInfo("[GodBox] chat panel will be drawn inside Worldfall's GUI pass (WorldBoxMod.OnGUIBody postfix).");
            }
            catch (Exception e) { Main.LogWarning("[GodBox] could not patch Worldfall OnGUIBody: " + e.Message); }
        }

        static void OnGuiBodyPostfix()
        {
            if (!ChatWindow.IsOpen) return;
            try { ChatWindow.Instance?.DrawHosted(); }
            catch (Exception e) { Main.LogError("[GodBox] hosted draw failed: " + e.Message); }
        }

        static void MenuOpenPostfix(ref bool __result)
        {
            if (ChatWindow.BlockGameInput) __result = true;
        }
    }
}
