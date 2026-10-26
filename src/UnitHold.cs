using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WorldfallAIChat
{
    /// <summary>
    /// Keeps the two people in a chat standing still and facing each other while our chat is open, the way Worldfall
    /// holds them during its own conversations (names checked against the decompiled Worldfall 0.9.8.9, no code copied):
    ///  * the NPC: WorldBox's public Actor.makeWait(seconds) (stops movement + sets the action timer) and
    ///    Actor.lookTowardsPosition(you), refreshed before the wait runs out;
    ///  * the possessed unit: Worldfall's static flag FirstPerson.PossessionHooks.Frozen = true (Worldfall's own
    ///    "frozen for talk" switch), restored on release only if we were the ones who set it;
    ///  * Worldfall's head-turn: SceneCollector.LookAt[npc] = your position (WorldBoxMod._scene), removed on release.
    /// Everything except makeWait/lookTowardsPosition is reflection-guarded and optional.
    /// </summary>
    public static class UnitHold
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static Actor _npc, _you;
        static bool _active, _weFroze, _weLook, _resolved;
        static float _nextWait;
        static FieldInfo _frozen;          // FirstPerson.PossessionHooks.Frozen (static bool)
        static Type _modType;

        public static bool Active => _active;

        public static void Begin(Actor npc, Actor you)
        {
            if (_active) Release("re-hold");
            if (npc == null || Main.Cfg == null || !Main.Cfg.HoldUnitsWhileChatting) return;
            _npc = npc; _you = you; _active = true; _weFroze = false; _weLook = false; _nextWait = 0f;
            Main.LogInfo("[GodBox] hold: keeping " + Name(npc) + (you != null ? " and " + Name(you) : "") + " in place while chatting.");
            Tick();
        }

        /// <summary>Call every frame while the chat is open.</summary>
        public static void Tick()
        {
            if (!_active) return;
            try
            {
                if (_npc == null || !Alive(_npc)) { Release("target gone"); return; }
                if (Time.unscaledTime >= _nextWait)
                {
                    _nextWait = Time.unscaledTime + 0.25f; // short, so fast game speeds cannot run the 3 s wait out
                    _npc.makeWait(3f);
                    if (_you != null && Alive(_you)) _npc.lookTowardsPosition(_you.current_position);
                    if (_you != null && Alive(_you) && !IsPossessed(_you))
                    {
                        // F3 outside possession with two units: hold the speaker too
                        _you.makeWait(3f);
                        _you.lookTowardsPosition(_npc.current_position);
                    }
                }
                if (_you != null && IsPossessed(_you))
                {
                    HoldPossessed();
                    SetLookAt(true);
                }
            }
            catch (Exception e)
            {
                Main.LogWarning("[GodBox] hold failed, releasing: " + e.Message);
                Release("error");
            }
        }

        public static void Release(string why)
        {
            if (!_active) return;
            _active = false;
            try
            {
                if (_npc != null && Alive(_npc)) _npc.makeWait(0f);      // let its AI resume right away
                if (_you != null && Alive(_you) && !IsPossessed(_you)) _you.makeWait(0f);
            }
            catch { }
            try { SetLookAt(false); } catch { }
            try
            {
                if (_weFroze && _frozen != null && !WorldfallInside())
                    _frozen.SetValue(null, false);
            }
            catch (Exception e) { Main.LogWarning("[GodBox] could not unfreeze the possessed unit: " + e.Message); }
            Main.LogInfo("[GodBox] hold released (" + why + ")" + (_weFroze ? "; possessed unit unfrozen." : "."));
            _weFroze = false; _npc = null; _you = null;
        }

        // ---------- Worldfall bits (all optional) ----------

        static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                _modType = Refl.Type("FirstPerson.WorldBoxMod");
                var hooks = Refl.Type("FirstPerson.PossessionHooks");
                _frozen = hooks == null ? null : Refl.Field(hooks, "Frozen");
                if (_frozen != null && (!_frozen.IsStatic || _frozen.FieldType != typeof(bool))) _frozen = null;
            }
            catch { _frozen = null; }
        }

        static object ModInstance()
        {
            Resolve();
            if (_modType == null) return null;
            try { return Refl.Property(_modType, "Instance")?.GetValue(null, null); } catch { return null; }
        }

        static bool IsPossessed(Actor a)
        {
            try { return a != null && ControllableUnit.isControllingUnit() && ControllableUnit.getControllableUnit() == a; } catch { return false; }
        }

        static void HoldPossessed()
        {
            Resolve();
            if (_frozen == null) return;
            if (!(bool)_frozen.GetValue(null))
            {
                _frozen.SetValue(null, true);
                if (!_weFroze) Main.LogInfo("[GodBox] hold: possessed unit frozen (Worldfall PossessionHooks.Frozen).");
                _weFroze = true;
            }
            // Worldfall lets go of a body that is "held still with nothing holding it" after ~1.5 s; tell its
            // stray-freeze timer that we are the ones holding it (only while our chat is open).
            if (_weFroze)
            {
                try
                {
                    var inst = ModInstance();
                    var f = inst == null ? null : Refl.Field(_modType, "_strayFrozenSince");
                    if (f != null && f.FieldType == typeof(float)) f.SetValue(inst, -1f);
                }
                catch { }
            }
        }

        static bool WorldfallInside()
        {
            try
            {
                var inst = ModInstance();
                if (inst == null) return false;
                var interior = Refl.Field(_modType, "Interior")?.GetValue(inst) ?? Refl.Property(_modType, "Interior")?.GetValue(inst, null);
                if (interior == null) return false;
                var p = Refl.Property(interior.GetType(), "Inside");
                return p != null && p.GetValue(interior, null) is bool b && b;
            }
            catch { return false; }
        }

        static void SetLookAt(bool on)
        {
            if (!on && !_weLook) return;
            var inst = ModInstance();
            if (inst == null) return;
            var scene = Refl.Field(_modType, "_scene")?.GetValue(inst);
            if (scene == null) return;
            if (!(Refl.Field(scene.GetType(), "LookAt")?.GetValue(scene) is IDictionary look)) return;
            if (on)
            {
                if (_npc == null || _you == null) return;
                look[_npc] = (Vector2)_you.current_position;
                _weLook = true;
            }
            else
            {
                if (_npc != null && look.Contains(_npc)) look.Remove(_npc);
                _weLook = false;
            }
        }

        static bool Alive(Actor a) { try { return a != null && a.isAlive(); } catch { return false; } }
        static string Name(Actor a) { try { return a.getName(); } catch { return "?"; } }
    }
}
