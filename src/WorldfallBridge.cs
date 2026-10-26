using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;

namespace WorldfallAIChat
{
    /// <summary>
    /// Optional, read-mostly integration with the Worldfall mod (s3cond2.worldfall).
    /// Uses reflection ONLY: no Worldfall code is copied or referenced at compile time, so this mod
    /// compiles and runs with or without Worldfall. Every call is guarded; if a Worldfall update renames
    /// something, that piece of context is just skipped.
    /// Names used (Worldfall 0.9.8.9): FirstPerson.Chronicle, FirstPerson.PeopleMet, FirstPerson.Mind, FirstPerson.Romance.
    /// </summary>
    public static class WorldfallBridge
    {
        const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags I = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static bool _searched;
        static Type _chronicle, _peopleMet, _mind, _romance, _service, _rep, _repHud, _regalia;

        public static bool Present
        {
            get { Find(); return _chronicle != null; }
        }

        static void Find()
        {
            if (_searched) return;
            _searched = true;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t;
                try { t = asm.GetType("FirstPerson.Chronicle", false); } catch { continue; }
                if (t == null) continue;
                _chronicle = t;
                _peopleMet = asm.GetType("FirstPerson.PeopleMet", false);
                _mind = asm.GetType("FirstPerson.Mind", false);
                _romance = asm.GetType("FirstPerson.Romance", false);
                // v0.8.0: ranks / positions (recognition)
                _service = asm.GetType("FirstPerson.Service", false);
                _regalia = asm.GetType("FirstPerson.Regalia", false);
                _rep = asm.GetType("FirstPerson.Towns.Rep", false);
                _repHud = asm.GetType("FirstPerson.Towns.RepHud", false);
                if (_rep == null || _repHud == null)
                    foreach (var a2 in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try
                        {
                            if (_rep == null) _rep = a2.GetType("FirstPerson.Towns.Rep", false);
                            if (_repHud == null) _repHud = a2.GetType("FirstPerson.Towns.RepHud", false);
                        }
                        catch { }
                    }
                Main.LogInfo("[GodBox] Worldfall ranks: Service " + (_service != null) + ", Regalia " + (_regalia != null) + ", Rep " + (_rep != null) + ", RepHud " + (_repHud != null));
                Main.LogAlways("[GodBox] Worldfall detected (" + asm.GetName().Name + " " + asm.GetName().Version + "): integration on.");
                break;
            }
            if (_chronicle == null) Main.LogAlways("[GodBox] Worldfall not detected: running standalone.");
        }

        static readonly Dictionary<string, MethodInfo> _calls = new Dictionary<string, MethodInfo>();
        static object Call(Type t, string name, params object[] args)
        {
            if (t == null) return null;
            string k = t.FullName + "|" + name + "|" + args.Length;
            if (!_calls.TryGetValue(k, out var m))
                _calls[k] = m = t.GetMethods(S).FirstOrDefault(x => x.Name == name && x.GetParameters().Length == args.Length);
            return m?.Invoke(null, args);
        }

        static object Get(object o, string member)
        {
            if (o == null) return null;
            var t = o.GetType();
            var f = t.GetField(member, I);
            if (f != null) return f.GetValue(o);
            var p = t.GetProperty(member, I);
            return p?.GetValue(o, null);
        }

        static void Try(Action a) { try { a(); } catch (Exception e) { if (Main.Cfg != null && Main.Cfg.DebugLogPrompts) Main.LogWarning("[GodBox] Worldfall read failed: " + (e.InnerException?.Message ?? e.Message)); } }

        /// <summary>Text block with what Worldfall knows about this NPC (and about the player's body, if possessing).</summary>
        public static string Describe(Actor npc, Actor you)
        {
            if (!Present || npc == null) return "";
            var sb = new StringBuilder();

            Try(() =>
            {
                var rec = Call(_chronicle, "Of", npc);
                if (rec == null) return;
                if (Get(rec, "Kin") is IEnumerable kin)
                {
                    var parts = (from object k in kin
                                 let role = Get(k, "Role") as string
                                 let name = Get(k, "Name") as string
                                 let dead = Get(k, "Dead") is bool d && d
                                 where !string.IsNullOrEmpty(name)
                                 select $"{role}: {name}{(dead ? " (dead)" : "")}").Take(16).ToList();
                    if (parts.Count > 0) sb.AppendLine("- Family (Worldfall chronicle): " + string.Join("; ", parts));
                }
                if (Get(rec, "Memories") is IEnumerable mems)
                {
                    var parts = (from object m in mems
                                 let kind = Get(m, "Kind") as string
                                 let other = Get(m, "Other") as string
                                 let year = Get(m, "Year")
                                 where !string.IsNullOrEmpty(kind)
                                 select $"year {year}: {kind}{(string.IsNullOrEmpty(other) ? "" : " (" + other + ")")}").ToList();
                    if (parts.Count > 0) sb.AppendLine("- Life memories (Worldfall chronicle, raw event codes): " + string.Join("; ", parts));
                }
            });

            if (you != null && you != npc)
            {
                Try(() =>
                {
                    var regard = Call(_chronicle, "RegardOf", npc, you, false);
                    if (regard == null) { sb.AppendLine("- Worldfall regard: no history with the player's body yet."); return; }
                    sb.AppendLine($"- Opinion of the person speaking to you (Worldfall, -100..100): {Get(regard, "Opinion")}; " +
                                  $"past talks {Get(regard, "Talks")}, gifts received {Get(regard, "Gifts")}, " +
                                  $"quests done for you {Get(regard, "Quests")}, crimes against you/your people {Get(regard, "Crimes")}.");
                });
                Try(() =>
                {
                    var knows = Call(_peopleMet, "Knows", you, npc);
                    if (knows is bool k) sb.AppendLine(k ? "- You have met the speaker before (Worldfall)." : "- You have never met the speaker before (Worldfall).");
                    var label = Call(_peopleMet, "Label", you, npc) as string;
                    if (!string.IsNullOrEmpty(label)) sb.AppendLine("- How the speaker knows you: " + label);
                });
            }

            Try(() =>
            {
                var rec = Call(_mind, "Of", npc);
                var last = Get(rec, "LastTopic") as string;
                var prev = Get(rec, "PrevTopic") as string;
                if (!string.IsNullOrEmpty(last)) sb.AppendLine($"- Recent topics on your mind (Worldfall): {last}{(string.IsNullOrEmpty(prev) ? "" : ", " + prev)}");
            });

            Try(() =>
            {
                string fav = Call(_romance, "Favourite", npc) as string;
                string place = Call(_romance, "FavouritePlace", npc) as string;
                string dis = Call(_romance, "Disliked", npc) as string;
                var bits = new[] { fav != null ? "favourite thing: " + fav : null, place != null ? "favourite place: " + place : null, dis != null ? "dislikes: " + dis : null }
                    .Where(x => x != null).ToArray();
                if (bits.Length > 0) sb.AppendLine("- Tastes (Worldfall): " + string.Join("; ", bits));
            });

            return sb.ToString();
        }

        /// <summary>v7: Worldfall opinion (-100..100) of npc about you, or null when unknown / Worldfall absent.</summary>
        public static int? Opinion(Actor npc, Actor you)
        {
            if (!Present || npc == null || you == null) return null;
            try
            {
                var regard = Call(_chronicle, "RegardOf", npc, you, false);
                var o = Get(regard, "Opinion");
                if (o == null) return null;
                return Convert.ToInt32(o);
            }
            catch { return null; }
        }

        /// <summary>v7: Worldfall's own "can I hire them as a guard?" check (Economy.CanHire). null = unknown.</summary>
        public static string CanHireWhyNot(Actor you, Actor npc)
        {
            try
            {
                var t = Refl.Type("FirstPerson.Economy");
                var m = t == null ? null : Refl.Method(t, "CanHire");
                if (m == null) return null;
                var args = new object[] { you, npc, null };
                bool ok = (bool)m.Invoke(null, args);
                return ok ? "" : (args[2] as string ?? "Not now.");
            }
            catch { return null; }
        }

        /// <summary>v7: Worldfall's Romance.WouldSayYes(npc, you). null = unknown.</summary>
        public static bool? WouldSayYes(Actor npc, Actor you)
        {
            try
            {
                Find();
                var m = _romance == null ? null : Refl.Method(_romance, "WouldSayYes", new[] { typeof(Actor), typeof(Actor) });
                if (m == null) return null;
                return (bool)m.Invoke(null, new object[] { npc, you });
            }
            catch { return null; }
        }

        /// <summary>v7: mark a unit with Worldfall's map ping (WorldBoxMod.PingFavorite toggles, so only call it when no
        /// ping exists yet: Pings.On(actor) == null). Returns true when the unit is pinged afterwards.</summary>
        public static bool Ping(Actor a)
        {
            try
            {
                var modT = Refl.Type("FirstPerson.WorldBoxMod");
                var inst = Refl.Property(modT, "Instance")?.GetValue(null, null);
                if (inst == null) return false;
                var pings = Refl.Field(modT, "Pings")?.GetValue(inst);
                var on = pings == null ? null : Refl.Method(pings.GetType(), "On", new[] { typeof(Actor) });
                if (on != null && on.Invoke(pings, new object[] { a }) != null) return true; // already pinged
                var pf = Refl.Method(modT, "PingFavorite", new[] { typeof(Actor) });
                if (pf == null) return false;
                var r = pf.Invoke(inst, new object[] { a });
                if (on != null) return on.Invoke(pings, new object[] { a }) != null;
                return r is bool b && b;
            }
            catch { return false; }
        }

        /// <summary>v0.7.4: is this unit currently pinged by Worldfall (Pings.On(actor) != null)?</summary>
        public static bool IsPinged(Actor a)
        {
            try
            {
                var modT = Refl.Type("FirstPerson.WorldBoxMod");
                var inst = Refl.Property(modT, "Instance")?.GetValue(null, null);
                if (inst == null) return false;
                var pings = Refl.Field(modT, "Pings")?.GetValue(inst);
                var on = pings == null ? null : Refl.Method(pings.GetType(), "On", new[] { typeof(Actor) });
                return on != null && on.Invoke(pings, new object[] { a }) != null;
            }
            catch { return false; }
        }

        /// <summary>v0.7.4: remove a ping (PingFavorite toggles it off when one exists). Only call for pings we added.</summary>
        public static void Unping(Actor a)
        {
            try
            {
                if (!IsPinged(a)) return;
                var modT = Refl.Type("FirstPerson.WorldBoxMod");
                var inst = Refl.Property(modT, "Instance")?.GetValue(null, null);
                var pf = inst == null ? null : Refl.Method(modT, "PingFavorite", new[] { typeof(Actor) });
                pf?.Invoke(inst, new object[] { a });
            }
            catch { }
        }

        /// <summary>Optionally record the AI conversation as a "talk" in Worldfall's regard data (off by default).</summary>
        public static void NoteTalk(Actor npc, Actor you)
        {
            if (!Present || npc == null || you == null || npc == you) return;
            Try(() => Call(_chronicle, "Note", npc, you, 1, 0, 0, 0));
        }

        // ---------------- v0.8.0: Worldfall ranks / positions of the possessed body (recognition) ----------------
        /// <summary>One Worldfall rank or position the player's body holds (English as Worldfall shows it, plus pt-BR).</summary>
        public class WfRank
        {
            public string Kind;    // king, heir, marshal, steward, chancellor, spymaster, mage, champion, captain, lord, leader, guard, soldier
            public string En;      // e.g. "Captain of Neen's army"
            public string Pt;      // e.g. "capitão do exército de Neen"
            public string Place;   // realm or town name
        }

        static readonly (string kind, Regex rx)[] RankRx =
        {
            ("king", new Regex(@"^(?:King|Queen) of (.+)$")),
            ("heir", new Regex(@"^Heir of (.+)$")),
            ("marshal", new Regex(@"^Marshal of (.+)$")),
            ("steward", new Regex(@"^Steward of (.+)$")),
            ("chancellor", new Regex(@"^Chancellor of (.+)$")),
            ("spymaster", new Regex(@"^Spymaster of (.+)$")),
            ("mage", new Regex(@"^Court mage of (.+)$")),
            ("champion", new Regex(@"^Champion of (.+)$")),
            ("captain", new Regex(@"^Captain of (.+)'s army$")),
            ("lord", new Regex(@"^Lord of (.+)$")),
            ("leader", new Regex(@"^Leader of (.+)$")),
            ("guard", new Regex(@"^Guard of (.+)$")),
            ("soldier", new Regex(@"^Soldier of (.+)$")),
        };

        static string RankPt(string kind, string place, bool fem)
        {
            switch (kind)
            {
                case "king": return (fem ? "rainha de " : "rei de ") + place;
                case "heir": return (fem ? "herdeira do trono de " : "herdeiro do trono de ") + place;
                case "marshal": return (fem ? "marechala de " : "marechal de ") + place;
                case "steward": return (fem ? "intendente (tesoureira) de " : "intendente (tesoureiro) de ") + place;
                case "chancellor": return "chanceler de " + place;
                case "spymaster": return (fem ? "mestra dos espiões de " : "mestre dos espiões de ") + place;
                case "mage": return (fem ? "maga da corte de " : "mago da corte de ") + place;
                case "champion": return (fem ? "campeã de " : "campeão de ") + place;
                case "captain": return (fem ? "capitã do exército de " : "capitão do exército de ") + place;
                case "lord": return (fem ? "senhora de " : "senhor de ") + place;
                case "leader": return (fem ? "líder de " : "líder de ") + place;
                case "guard": return "guarda de " + place;
                case "soldier": return (fem ? "soldada de " : "soldado de ") + place;
            }
            return null;
        }

        /// <summary>Worldfall's own titles for this body: RepHud.Titles (King/Queen, Leader, Champion, Lord of ...)
        /// plus Regalia.NameplateRole (Heir, council seats, Champion, Captain, Guard, Soldier). Empty when Worldfall is absent.</summary>
        public static List<WfRank> Ranks(Actor you)
        {
            var res = new List<WfRank>();
            if (!Present || you == null) return res;
            var raw = new List<string>();
            Try(() => { if (Call(_repHud, "Titles", you) is IEnumerable l) foreach (var o in l) if (o is string t && t.Length > 0) raw.Add(t); });
            Try(() =>
            {
                var np = Call(_regalia, "NameplateRole", you) as string;
                if (!string.IsNullOrEmpty(np)) raw.Add(np.Split(new[] { "  ·  " }, StringSplitOptions.None)[0].Trim());
            });
            bool fem = false;
            try { fem = you.isSexFemale(); } catch { }
            foreach (var t in raw.Distinct())
                foreach (var (kind, rx) in RankRx)
                {
                    var m = rx.Match(t);
                    if (!m.Success) continue;
                    string place = m.Groups[1].Value.Trim();
                    if (res.Any(r => r.Kind == kind && r.Place == place)) break;
                    res.Add(new WfRank { Kind = kind, En = t, Pt = RankPt(kind, place, fem), Place = place });
                    break;
                }
            return res;
        }

        /// <summary>Worldfall Service: soldier? captain? merit (years served + kills/2), kills while serving.</summary>
        public static bool ServiceInfo(Actor you, out bool soldier, out bool captain, out float merit, out int kills)
        {
            soldier = captain = false; merit = 0f; kills = 0;
            if (!Present || you == null || _service == null) return false;
            bool ok = false; bool s1 = false, c1 = false; float m1 = 0f; int k1 = 0;
            Try(() =>
            {
                if (Call(_service, "IsSoldier", you) is bool a) { s1 = a; ok = true; }
                if (Call(_service, "IsCaptain", you) is bool b) c1 = b;
                if (Call(_service, "Merit", you) is float f) m1 = f;
                if (Call(_service, "Kills", you) is int k) k1 = k;
            });
            soldier = s1; captain = c1; merit = m1; kills = k1;
            return ok;
        }

        /// <summary>Worldfall per-town reputation level name (Outlaw, Distrusted, Stranger, Trusted, Honored, Champion) or null.</summary>
        public static string TownStanding(Actor you, City c)
        {
            if (!Present || you == null || c == null || _rep == null) return null;
            string r = null;
            Try(() => { var v = Call(_rep, "Level", you, c); if (v != null) r = v.ToString(); });
            return r;
        }

        /// <summary>Worldfall PeopleMet: has this body met the npc before?</summary>
        public static bool? HasMet(Actor you, Actor npc)
        {
            if (!Present || you == null || npc == null) return null;
            bool? r = null;
            Try(() => { if (Call(_peopleMet, "Knows", you, npc) is bool k) r = k; });
            return r;
        }
    }
}
