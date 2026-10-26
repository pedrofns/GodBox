using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace WorldfallAIChat
{
    /// <summary>
    /// v0.7.9: conditional god promises. "vá até X que eu retiro a sua maldição", "se você for até X, eu te abençoo",
    /// "quando chegar em X eu te curo" -> go_to runs now; the reward is stored and executed when the unit arrives.
    /// Mod-side only (kept in memory for the session; a promise lapses if the walk fails or is cancelled).
    /// </summary>
    public static class Promises
    {
        public class Pending { public Actor Npc; public string RewardId, RewardTarget, RewardPt, Dest, Said; public float Added; public bool Bound; }
        static readonly Dictionary<long, Pending> _p = new Dictionary<long, Pending>();
        static Pending _parsed;   // waiting for GoTo to bind it

        const string Move = @"(?:va|vai|ande|caminhe|siga|dirija-se|parta|viaje|corra|marche|chegue|leve-se)";
        const string Prep = @"(?:ate|para|pra|pro|ao|a|em|na|no)";
        static readonly Regex OrderThenRx = new Regex(@"^(?:agora\s+|primeiro\s+|apenas\s+|so\s+)?" + Move + @"\s+" + Prep + @"\s+(.+?)\s*,?\s+(?:que|e|entao|depois|ai|la)\s+(?:entao\s+|la\s+)?(eu\s+.+|te\s+.+|lhe\s+.+|sua\s+maldi.+|a\s+maldi.+)$", RegexOptions.CultureInvariant);
        static readonly Regex IfRx = new Regex(@"^se\s+(?:voce\s+|tu\s+)?(?:for|fores|ir|chegar|chegares|andar|caminhar|viajar)\s+" + Prep + @"\s+(.+?)\s*,?\s+(?:entao\s+)?(eu\s+.+|te\s+.+|lhe\s+.+)$", RegexOptions.CultureInvariant);
        static readonly Regex WhenRx = new Regex(@"^(?:quando|assim que|logo que)\s+(?:voce\s+|tu\s+)?(?:chegar|chegares|estiver|pisar)\s+" + Prep + @"\s+(.+?)\s*,?\s+(?:entao\s+)?(eu\s+.+|te\s+.+|lhe\s+.+)$", RegexOptions.CultureInvariant);

        static readonly (string id, Regex rx, string pt)[] Rewards =
        {
            ("lift_curse", new Regex(@"\b(retir\w*|tir\w*|quebr\w*|remov\w*|desfa\w*|levant\w*|livr\w*|libert\w*|acab\w*|anul\w*)\b.*\b(maldi|praga)", RegexOptions.CultureInvariant), "tirar a sua maldição"),
            ("lift_curse", new Regex(@"\b(maldi\w*|praga)\b.*\b(some|acaba|termina|sai)\b", RegexOptions.CultureInvariant), "tirar a sua maldição"),
            ("heal", new Regex(@"\b(curo|curarei|curar|saro|sararei|restauro|restaurarei)\b", RegexOptions.CultureInvariant), "curar você"),
            ("bless", new Regex(@"\b(abencoo|abencoarei|abencoar|bencao|bencaos)\b", RegexOptions.CultureInvariant), "abençoar você"),
            ("give_item", new Regex(@"\b(dou|darei|entrego|entregarei|presenteio|presentearei)\b\s+(?:a\s+voce\s+|te\s+|lhe\s+)?(?:uma?|o|a)\s+\w+", RegexOptions.CultureInvariant), "dar um presente"),
            ("add_trait", new Regex(@"\b(concedo|concederei|outorgo|outorgarei)\b", RegexOptions.CultureInvariant), "conceder um dom"),
        };

        /// <summary>God voice only. Returns a go_to request and stores the pending reward until GoTo binds it.</summary>
        public static WorldfallActions.Request Parse(string text, Actor npc, List<WorldfallActions.Available> avail)
        {
            _parsed = null;
            if (npc == null || avail == null || !avail.Any(a => a.Def.Id == "go_to")) return null;
            string f = NativeActions.Fold(text ?? "").Trim().TrimEnd('!', '.');
            if (f.EndsWith("?")) return null;
            Match m = OrderThenRx.Match(f); if (!m.Success) m = IfRx.Match(f); if (!m.Success) m = WhenRx.Match(f);
            if (!m.Success) return null;
            string dest = m.Groups[1].Value.Trim(), rest = m.Groups[2].Value;
            foreach (var (id, rx, pt) in Rewards)
            {
                if (!rx.IsMatch(rest)) continue;
                if (!avail.Any(a => a.Def.Id == id)) { Main.LogInfo("[GodBox] promise: reward " + id + " is not available (config); treated as a plain order."); return null; }
                string rt = id == "give_item" || id == "add_trait" ? rest : "";
                _parsed = new Pending { Npc = npc, RewardId = id, RewardTarget = rt, RewardPt = pt, Dest = dest, Said = text.Trim(), Added = Time.unscaledTime };
                Main.LogInfo("[GodBox] conditional promise: go_to \"" + dest + "\" now, then " + id + " on arrival.");
                return new WorldfallActions.Request { Id = "go_to", Target = dest, Confidence = 1f };
            }
            return null;
        }

        /// <summary>Called by GoTo once the walk really started; returns the note for the action result (or null).</summary>
        public static string Bind(Actor npc, string label)
        {
            var p = _parsed; _parsed = null;
            if (p == null || p.Npc != npc) return null;
            long id; try { id = npc.getID(); } catch { return null; }
            p.Bound = true; p.Dest = label;
            _p[id] = p;
            Main.LogInfo("[GodBox] promise stored: " + p.RewardId + " when " + SafeName(npc) + " reaches " + label + ".");
            return "Deus PROMETEU: quando você chegar a " + label + ", Ele vai " + p.RewardPt + " (AINDA NÃO aconteceu; só acontece na chegada)";
        }

        public static void ClearParsed() => _parsed = null;

        /// <summary>FACTS line for the unit: the promise it is walking towards.</summary>
        public static string Fact(Actor npc)
        {
            try { return _p.TryGetValue(npc.getID(), out var p) ? "Deus prometeu " + p.RewardPt + " quando você chegar a " + p.Dest + " (ainda não chegou; ainda não aconteceu)" : null; } catch { return null; }
        }

        public static void Drop(Actor npc, string why)
        {
            try { if (npc != null && _p.Remove(npc.getID())) Main.LogInfo("[GodBox] promise lapsed (" + why + ") [" + SafeName(npc) + "]."); } catch { }
        }

        /// <summary>The walk reached its destination: God keeps the promise.</summary>
        public static void OnArrived(Actor npc)
        {
            Pending p;
            try { if (npc == null || !_p.TryGetValue(npc.getID(), out p)) return; _p.Remove(npc.getID()); } catch { return; }
            try
            {
                if (!npc.isAlive()) return;
                var res = GodPowers.Execute(npc, null, new WorldfallActions.Request { Id = p.RewardId, Target = p.RewardTarget }, Main.Cfg);
                string nm = SafeName(npc);
                Main.LogInfo("[GodBox] promise kept on arrival: " + p.RewardId + " -> " + res.Outcome + (res.Changes.Count > 0 ? " (" + string.Join("; ", res.Changes) + ")" : "") + (string.IsNullOrEmpty(res.Reason) ? "" : " [" + res.Reason + "]") + " [" + nm + "]");
                bool ok = res.Outcome == "feito";
                ChatWindow.Toast(ok ? "Deus cumpriu a promessa: " + nm + " chegou a " + p.Dest + " e " + PastPt(p.RewardId) + "." : "A promessa a " + nm + " não pôde ser cumprida: " + (res.Reason ?? res.Outcome) + ".");
                Rumors.Deliver(npc, ok
                    ? "Você chegou a " + p.Dest + " como Deus mandou, e Deus cumpriu a promessa: " + string.Join("; ", res.Changes.Take(3))
                    : "Você chegou a " + p.Dest + " como Deus mandou, mas a promessa (" + p.RewardPt + ") não se cumpriu: " + (res.Reason ?? res.Outcome));
                City c = null; try { c = npc.city; } catch { }
                if (ok) try { Rumors.FromResult(res, nm, c, null, npc); } catch { }
            }
            catch (Exception e) { Main.LogWarning("[GodBox] promise failed: " + e.Message); }
        }

        static float _nextTick;
        /// <summary>Drops promises whose walk is gone (failed, cancelled, unit dead).</summary>
        public static void Tick()
        {
            if (_p.Count == 0) return;
            // v0.8.2-alpha: checked 4x a second, not every frame (and no list copy per frame)
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 0.25f;
            foreach (var kv in _p.ToList())
            {
                var a = kv.Value.Npc;
                bool alive = false; try { alive = a != null && a.isAlive(); } catch { }
                if (!alive) { _p.Remove(kv.Key); Main.LogInfo("[GodBox] promise lapsed (unit gone)."); continue; }
                if (Time.unscaledTime - kv.Value.Added > 3f && !NativeActions.HasTask(a, "goto")) Drop(a, "the walk ended without arriving");
            }
        }

        static string PastPt(string id)
        {
            switch (id)
            {
                case "lift_curse": return "a maldição foi retirada";
                case "heal": return "foi curado(a)";
                case "bless": return "foi abençoado(a)";
                case "give_item": return "recebeu o presente";
                default: return "recebeu o dom prometido";
            }
        }
        static string SafeName(Actor a) { try { return a.getName(); } catch { return "?"; } }
    }
}
