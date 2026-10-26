using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WorldfallAIChat
{
    /// <summary>
    /// Small, defensive Harmony patches so typing in the chat does not trigger game hotkeys,
    /// move a possessed unit, click the map through the window, or (if present) drive Worldfall's controls.
    /// Each patch is applied separately; if one target is missing after a game update it is skipped.
    /// </summary>
    public static class Patches
    {
        public static void Apply(Harmony h)
        {
            TryPatch(h, AccessTools.Method(typeof(HotkeyLibrary), "isInputActive"), postfix: nameof(InputActivePostfix));
            TryPatch(h, AccessTools.Method(typeof(PlayerControl), "isOverUI"), postfix: nameof(OverUiPostfix));
            TryPatch(h, AccessTools.Method(typeof(ControllableUnit), "updateMovementVectorKeyboard"), prefix: nameof(SkipWhileTyping));
            TryPatch(h, AccessTools.Method(typeof(ControllableUnit), "checkActions"), prefix: nameof(SkipWhileTyping));

            // v0.8.0: the Avatar keeps EVERY SpellAsset after the game rebuilds its spell list
            TryPatch(h, AccessTools.Method(typeof(Actor), "updateStats"), postfix: nameof(AvatarSpellsPostfix));

            // v0.8.2 (E): the religion window's "deity" row (hardcoded "??" by the game) shows the god's real name
            try
            {
                MethodInfo row = null;
                foreach (var m in typeof(StatsWindow).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (m.Name != "showStatRow") continue;
                    var ps = m.GetParameters();
                    if (ps.Length >= 3 && ps[0].Name == "pId" && ps[1].Name == "pValue" && ps[2].Name == "pColor" && ps[2].ParameterType == typeof(string)) { row = m; break; }
                }
                TryPatch(h, row, prefix: nameof(DeityRowPrefix));
            }
            catch (Exception e) { Main.LogWarning("[GodBox] deity row patch skipped: " + e.Message); }

            // v0.8.2 (K): unit window shows "Indo pregar" / "Pregando" while God's preaching order is active
            TryPatch(h, AccessTools.Method(typeof(Actor), "getTaskText"), postfix: nameof(TaskTextPostfix));
            // v0.8.2 (G): base-game conversions (talk, reading a prophet's book) count for the prophet's deadline, anywhere.
            // Counting only: the game's own conversion odds are NOT changed.
            TryPatch(h, AccessTools.Method(typeof(BehFinishTalk), "tryToSpreadReligion"), prefix: nameof(TalkConvPrefix), postfix: nameof(TalkConvPostfix));
            TryPatch(h, AccessTools.Method(typeof(BehFinishReading), "tryToConvertActorToBookReligion"), prefix: nameof(ReadConvPrefix), postfix: nameof(ReadConvPostfix));

            // Worldfall reads keys/mouse through FirstPerson.PlayInput (internal). Make it see "nothing pressed" while the chat is open.
            var playInput = AccessTools.TypeByName("FirstPerson.PlayInput");
            if (playInput != null)
            {
                foreach (var name in new[] { "GetKey", "GetKeyDown", "GetKeyUp", "GetMouseButton", "GetMouseButtonDown", "GetMouseButtonUp" })
                {
                    var m = AccessTools.Method(playInput, name);
                    if (m != null && m.ReturnType == typeof(bool)) TryPatch(h, m, prefix: nameof(FalseWhileTyping));
                }
            }
        }

        static void TryPatch(Harmony h, MethodBase target, string prefix = null, string postfix = null)
        {
            if (target == null) { Main.LogWarning("[GodBox] patch target not found (skipped)."); return; }
            try
            {
                h.Patch(target,
                    prefix: prefix == null ? null : new HarmonyMethod(typeof(Patches), prefix),
                    postfix: postfix == null ? null : new HarmonyMethod(typeof(Patches), postfix));
            }
            catch (Exception e) { Main.LogWarning($"[GodBox] could not patch {target.DeclaringType?.Name}.{target.Name}: {e.Message}"); }
        }

        static void InputActivePostfix(ref bool __result)
        {
            if (ChatWindow.BlockGameInput) __result = true;
        }

        static void OverUiPostfix(ref bool __result)
        {
            Vector2 m = Input.mousePosition;
            m.y = Screen.height - m.y; // IMGUI coordinates
            if (GodFaithWindow.IsOpen && GodFaithWindow.ScreenRect.Contains(m)) { __result = true; return; }   // v0.7.9 religion window
            if (!ChatWindow.IsOpen || !ChatWindow.Visible) return;
            if (ChatWindow.ScreenRect.Contains(m)) __result = true;
        }

        static void AvatarSpellsPostfix(Actor __instance)
        {
            try { if (Avatar.IsAvatar(__instance)) Avatar.AddAllSpells(__instance); } catch { }
        }

        static bool _deityLogged;
        static void TaskTextPostfix(Actor __instance, ref string __result)
        {
            try { string l = GodPowers.TaskLabel(__instance); if (l != null) __result = l; } catch { }
        }

        static void TalkConvPrefix(Actor pTarget, out Religion __state) { __state = null; try { __state = pTarget?.religion; } catch { } }
        static void TalkConvPostfix(Actor pActor, Actor pTarget, Religion __state)
        {
            try
            {
                if (pTarget == null || pActor == null || pTarget.religion == __state) return;   // no change: skip the lookup
                var rel = GodPowers.CurrentReligion();
                if (rel == null || __state == rel || pTarget.religion != rel) return;
                CountProphetConversion(pActor.getID(), pTarget, "talk");
            }
            catch { }
        }
        static void ReadConvPrefix(Actor pActor, out Religion __state) { __state = null; try { __state = pActor?.religion; } catch { } }
        static void ReadConvPostfix(Actor pActor, Book pBook, Religion __state)
        {
            try
            {
                if (pActor == null || pBook == null || pActor.religion == __state) return;   // no change: skip the lookup
                var rel = GodPowers.CurrentReligion();
                if (rel == null || __state == rel || pActor.religion != rel) return;
                CountProphetConversion(pBook.data.author_id, pActor, "book");
            }
            catch { }
        }
        static void CountProphetConversion(long prophetId, Actor convert, string how)
        {
            var g = MemoryStore.God();
            if (g.Prophets == null || !g.Prophets.Contains(prophetId)) return;
            g.AddConversions(prophetId, 1); g.ConvertedTotal++;
            MemoryStore.SaveGod();
            string n = "?"; try { n = convert.getName(); } catch { }
            Main.LogInfo("[GodBox] CONVERSION (base game, " + how + "): " + n + " joined God's religion; counted for prophet id " + prophetId + ".");
        }

        static void DeityRowPrefix(object __instance, string pId, ref object pValue, ref string pColor)
        {
            try
            {
                if (pId != "deity" || !(__instance is ReligionWindow) || !(pValue is string v) || v != "??") return;
                Religion r = SelectedMetas.selected_religion;
                if (r == null) return;
                string n = r == GodPowers.CurrentReligion() ? Gods.GodName() : Gods.KnownTraditional(r);
                if (string.IsNullOrWhiteSpace(n)) return;   // unknown: leave the game's "??"
                pValue = n; pColor = null;   // normal text colour
                if (!_deityLogged) { _deityLogged = true; Main.LogInfo("[GodBox] religion window: deity row shows " + n + " for " + r.name + "."); }
            }
            catch { }
        }

        static bool SkipWhileTyping() => !ChatWindow.BlockGameInput;

        static bool FalseWhileTyping(ref bool __result)
        {
            if (!ChatWindow.BlockGameInput) return true;
            __result = false;
            return false;
        }
    }
}
