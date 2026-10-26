using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using UnityEngine;

namespace WorldfallAIChat
{
    /// <summary>
    /// v0.7.8: order_kill — the NPC is ordered to kill SOMEONE ELSE ("sacrifique seu filho", "mate um súdito", "mate o Borin").
    /// The target is resolved by the code (named person, relative via family links, or a random commoner), and whether
    /// the NPC obeys is decided by the code (speaker, devotion, traits, family love, threats), never by the LLM.
    /// </summary>
    public static partial class NativeActions
    {
        // ---------- threats ("obedeça ou morra") ----------
        static readonly Dictionary<Actor, float> _threatened = new Dictionary<Actor, float>();
        public static void MarkThreat(Actor npc) { if (npc != null) _threatened[npc] = Time.unscaledTime; }
        public static bool Threatened(Actor npc) => npc != null && _threatened.TryGetValue(npc, out var at) && Time.unscaledTime - at < 600f;
        static readonly string[] FearTraits = { "paranoid", "weak", "tiny", "fragile_health", "unlucky" };
        public static bool Fearful(Actor npc) => FearTraits.Any(id => HasTrait(npc, id));

        // ---------- target ----------
        static readonly ConditionalWeakTable<WorldfallActions.Request, Actor> _killPick = new ConditionalWeakTable<WorldfallActions.Request, Actor>();

        static readonly Regex ChildRx = new Regex(@"\b(filh[oa]s?|crianca|bebe|herdeir[oa]|primogenit[oa]|prole)\b");
        static readonly Regex LoverRx = new Regex(@"\b(esposa|esposo|marido|mulher|amante|conjuge|companheir[oa]|amad[oa]|noiv[oa])\b");
        static readonly Regex ParentRx = new Regex(@"\b(pai|mae|pais|genitor\w*)\b");
        static readonly Regex SiblingRx = new Regex(@"\b(irmao|irma|irmaos|irmas)\b");
        static readonly Regex SubjectRx = new Regex(@"\b(sudit[oa]s?|plebeu|plebeia|campones\w*|aldea[oa]s?|cidada[oa]s?|morador\w*|habitante\w*|alguem( do| da| de)? (seu |sua |teu |tua )?(povo|reino|cidade|vila)|uma pessoa|um homem|uma mulher|um inocente|qualquer um)\b");

        static IEnumerable<Actor> SafeList(Func<IEnumerable<Actor>> f) { try { return (f() ?? Enumerable.Empty<Actor>()).Where(Alive).ToList(); } catch { return Enumerable.Empty<Actor>(); } }

        static List<Actor> Family(Actor npc)
        {
            var fam = new List<Actor>();
            fam.AddRange(SafeList(() => npc.getChildren(false)));
            fam.AddRange(SafeList(() => npc.getParents()));
            try { if (Alive(npc.lover)) fam.Add(npc.lover); } catch { }
            foreach (var p in SafeList(() => npc.getParents())) fam.AddRange(SafeList(() => p.getChildren(false)));
            return fam.Where(a => a != npc).Distinct().ToList();
        }

        static Actor PickBySex(List<Actor> list, string f, string male, string female)
        {
            if (list.Count == 0) return null;
            bool wantF = Regex.IsMatch(f, female), wantM = Regex.IsMatch(f, male);
            IEnumerable<Actor> l = list;
            if (wantF != wantM) l = list.Where(a => { try { return a.isSexFemale() == wantF; } catch { return true; } });
            // a name in the phrase picks that relative ("seu filho Borin")
            var named = l.FirstOrDefault(a => { string n = Fold(Name(a)); return n.Length > 2 && f.Contains(n); });
            return named ?? l.FirstOrDefault();
        }

        /// <summary>Resolves who the NPC is ordered to kill. rel = "filho(a)", "cônjuge", "pai/mãe", "irmão(ã)", "súdito", or null (named).</summary>
        public static Actor KillTarget(WorldfallActions.Request req, Actor npc, Actor you, out string rel, out string why)
        {
            rel = null; why = null;
            if (req != null && _killPick.TryGetValue(req, out var cached) && Alive(cached))
            { rel = RelationOf(npc, cached); return cached; }
            string f = Fold(((req?.Target) ?? "") + " " + ((req?.Message) ?? ""));
            Actor t = null;
            if (ChildRx.IsMatch(f))
            {
                rel = "filho(a)";
                t = PickBySex(SafeList(() => npc.getChildren(false)).ToList(), f, @"\bfilhos?\b", @"\bfilhas?\b");
                if (t == null) why = "não tem filhos vivos";
            }
            else if (LoverRx.IsMatch(f))
            {
                rel = "cônjuge";
                try { if (Alive(npc.lover)) t = npc.lover; } catch { }
                if (t == null) why = "não tem cônjuge/amante vivo";
            }
            else if (ParentRx.IsMatch(f))
            {
                rel = "pai/mãe";
                t = PickBySex(SafeList(() => npc.getParents()).ToList(), f, @"\bpai\b", @"\bmae\b");
                if (t == null) why = "não tem pai nem mãe vivos";
            }
            else if (SiblingRx.IsMatch(f))
            {
                rel = "irmão(ã)";
                var sib = SafeList(() => npc.getParents()).SelectMany(p => SafeList(() => p.getChildren(false))).Where(a => a != npc).Distinct().ToList();
                t = PickBySex(sib, f, @"\birmaos?\b", @"\birmas?\b");
                if (t == null) why = "não tem irmãos vivos";
            }
            else if (SubjectRx.IsMatch(f))
            {
                rel = "súdito";
                var fam = Family(npc);
                IEnumerable<Actor> pool = Enumerable.Empty<Actor>();
                try { if (npc.city != null) pool = SafeList(() => npc.city.units); } catch { }
                if (!pool.Any()) try { if (npc.kingdom != null) pool = SafeList(() => npc.kingdom.getUnits()); } catch { }
                var cand = pool.Where(a => a != npc && a != you && !fam.Contains(a) && !SafeIsKing(a) && !SafeIsLeader(a)).ToList();
                if (cand.Count > 0) t = cand[Rng.Next(cand.Count)];
                else why = npc.city == null && npc.kingdom == null ? "não tem cidade nem reino: não há súditos" : "não há súditos por perto (além da família)";
            }
            else
            {
                t = FindUnit(req?.Target, npc, you, out _);
                if (t == null) why = string.IsNullOrWhiteSpace(req?.Target) ? "não disse quem matar" : "não encontrou \"" + req.Target + "\"";
                else rel = RelationOf(npc, t);
            }
            if (t != null && req != null) { _killPick.Remove(req); _killPick.Add(req, t); }
            return t;
        }

        static bool SafeIsKing(Actor a) { try { return a.isKing(); } catch { return false; } }
        static bool SafeIsLeader(Actor a) { try { return a.isCityLeader(); } catch { return false; } }

        static string RelationOf(Actor npc, Actor t)
        {
            try
            {
                if (SafeList(() => npc.getChildren(false)).Contains(t)) return "filho(a)";
                if (npc.lover == t) return "cônjuge";
                if (SafeList(() => npc.getParents()).Contains(t)) return "pai/mãe";
                if (Family(npc).Contains(t)) return "irmão(ã)";
            }
            catch { }
            return null;
        }

        static bool SameKingdom(Actor npc, Actor t) { try { return npc.kingdom != null && t.kingdom == npc.kingdom; } catch { return false; } }

        /// <summary>Needs the player's confirmation: a relative, or a subject of the NPC's own kingdom.</summary>
        public static bool OrderKillNeedsConfirm(WorldfallActions.Request req, Actor npc, Actor you)
        {
            var t = KillTarget(req, npc, you, out string rel, out _);
            return t != null && (rel != null || SameKingdom(npc, t));
        }

        static readonly (string id, float w, string pt)[] KillTraits =
        {
            ("pacifist", -0.6f, "pacifista"), ("peaceful", -0.35f, "pacífico(a)"), ("kind", -0.3f, "bondoso(a)"), ("honest", -0.15f, "honesto(a)"),
            ("content", -0.1f, "contente"), ("wise", -0.1f, "sábio(a)"), ("blessed", -0.1f, "abençoado(a)"),
            ("evil", 0.35f, "mau/má"), ("bloodlust", 0.35f, "sedento(a) de sangue"), ("psychopath", 0.4f, "psicopata"),
            ("hotheaded", 0.1f, "esquentado(a)"), ("deceitful", 0.1f, "traiçoeiro(a)"), ("greedy", 0.05f, "ganancioso(a)"), ("madness", 0.15f, "louco(a)"),
        };

        /// <summary>Deterministic obedience for order_kill. null = obeys; otherwise the refusal reason (pt). Always logs the decision.</summary>
        public static string OrderKillDecision(WorldfallActions.Request req, Actor npc, Actor you, DialogueConfig cfg, out Actor target, out string rel)
        {
            target = KillTarget(req, npc, you, out rel, out string why);
            if (target == null) return null;   // "not found" is reported by Execute as sem_efeito
            if (target == npc) return "não vai se matar por essa ordem";
            if (you != null && target == you) return "não vai atacar quem deu a ordem";
            if (you != null)
            {
                string enemy = EnemyReason(npc, you, cfg);
                if (enemy != null) return "recusa: " + enemy;
                string own = OwnKingdomAttackBlock(npc, you, target);   // own-kingdom victims only on the god's or that king's word
                if (own != null) return own.Replace("mandar atacar", "mandar matar");
            }
            var nat = Nature.Normal; try { nat = Alignment.Of(npc); } catch { }
            var log = new List<string>();
            float s;
            if (you == null)
            {
                if (nat == Nature.Malefic)
                {
                    Main.LogInfo("[GodBox] ORDER_KILL decision [" + Name(npc) + " -> " + Name(target) + "]: refuses (malefic, defies God)" + (Threatened(npc) ? " even under threat" : ""));
                    return "é um ser maléfico: SABE que quem manda é Deus, odeia-o e não lhe dá esse prazer (desafio consciente, não falta de autoridade)";
                }
                s = 0.55f; log.Add("voz de Deus +0.55");
                if (nat == Nature.Holy) { s += 0.35f; log.Add("devoto(a) +0.35"); }
            }
            else
            {
                bool king = false; try { king = you.isKing() && you.kingdom != null && you.kingdom == npc.kingdom; } catch { }
                s = king ? 0.5f : 0.2f; log.Add(king ? "ordem do próprio rei +0.5" : "ordem de um estranho +0.2");
            }
            var pos = new List<string>(); var neg = new List<string>();
            foreach (var (id, w, pt) in KillTraits)
                if (HasTrait(npc, id)) { s += w; log.Add(id + (w >= 0 ? " +" : " ") + w.ToString("0.00")); (w < 0 ? neg : pos).Add(pt); }
            if (rel != null && rel != "súdito")
            {
                float fam = rel == "filho(a)" ? -0.55f : rel == "cônjuge" ? -0.5f : -0.4f;
                if (HasTrait(npc, "psychopath")) fam /= 2f;
                s += fam; log.Add("amor de família (" + rel + ") " + fam.ToString("0.00"));
            }
            try { var bf = npc.getBestFriend(); if (bf != null && bf == target) { s -= 0.25f; log.Add("melhor amigo -0.25"); } } catch { }
            bool threat = Threatened(npc);
            if (threat)
            {
                float b = Fearful(npc) ? 0.45f : 0.25f;
                if (HasTrait(npc, "strong_minded")) b -= 0.15f;
                s += b; log.Add("ameaça de morte +" + b.ToString("0.00") + (Fearful(npc) ? " (medroso)" : ""));
            }
            bool obeys = s >= 0.5f;
            Main.LogInfo("[GodBox] ORDER_KILL decision [" + Name(npc) + " -> " + Name(target) + (rel != null ? ", " + rel : "") + "]: score "
                + s.ToString("0.00") + " => " + (obeys ? "OBEYS" : "REFUSES") + " (" + string.Join(", ", log) + ")");
            if (obeys) return null;
            var why2 = new List<string>();
            if (rel != null && rel != "súdito") why2.Add("ama " + (rel == "filho(a)" ? "o(a) próprio(a) filho(a)" : rel == "cônjuge" ? "o(a) cônjuge" : "o(a) " + rel) + " " + Name(target));
            if (neg.Count > 0) why2.Add("é " + string.Join(", ", neg));
            if (you != null && s < 0.5f && why2.Count == 0) why2.Add("não mata a mando de quem não é seu rei nem um deus");
            if (why2.Count == 0) why2.Add("não tem coragem de matar " + Name(target));
            return "recusa matar " + Name(target) + ": " + string.Join("; ", why2) + (threat ? " (mesmo sob ameaça de morte)" : "");
        }

        static void OrderKill(Actor npc, Actor you, WorldfallActions.Request req, DialogueConfig cfg, WorldfallActions.Result res)
        {
            string block = OrderKillDecision(req, npc, you, cfg, out var t, out string rel);
            if (t == null)
            {
                KillTarget(req, npc, you, out _, out string why);
                res.Outcome = "sem_efeito"; res.Reason = why ?? "não encontrou o alvo"; return;
            }
            if (block != null) { res.Outcome = "recusado"; res.Reason = block; return; }
            bool can = true; try { can = npc.canAttackTarget(t, false, false); } catch { }
            if (!can) { res.Outcome = "sem_efeito"; res.Reason = "não consegue atacar " + Name(t); return; }
            Drop(npc, "follow", "guide", "attack", "order_kill");
            try { npc.finishStatusEffect("possessed_follower"); } catch { }
            ReleaseHoldFor(npc, "order_kill");
            Deaths.MarkModCause(t, "morto(a) por " + Name(npc) + (rel != null ? " (" + rel + ")" : "") + " por ordem de " + (you == null ? "Deus" : Name(you)));
            float now = Time.unscaledTime;
            _tasks.Add(new Task { Kind = "order_kill", Npc = npc, Target = t, Job = rel, Until = now + Math.Max(60f, cfg.AttackSeconds * 4f), Next = now + 0.5f });
            float d = Dist(npc, t.current_tile);
            string who = Name(t) + (rel != null ? " (" + rel + ")" : "");
            if (d <= 4f)
            {
                try { npc.addAggro(t); npc.startFightingWith(t); } catch { }
                res.Outcome = "feito";
                res.Changes.Add("atacando " + who + " para matá-lo(a), por ordem — o golpe começou, a morte AINDA não aconteceu");
            }
            else
            {
                res.Outcome = "em_andamento";
                res.Changes.Add("indo até " + who + " (" + FactsBuilder.Where(npc.current_tile, t.current_tile, true) + ") para matá-lo(a); AINDA NÃO CHEGOU, ninguém morreu ainda");
            }
            Main.LogInfo("[GodBox] ACTION order_kill: " + Name(npc) + " -> " + who + " (" + res.Outcome + ")");
        }

        static bool OrderKillStep(Task t, float now)
        {
            t.Next = now + 1f;
            if (!Alive(t.Target))
            {
                Main.LogInfo("[GodBox] ACTION order_kill: target dead [" + Name(t.Npc) + "]");
                ChatWindow.Toast(Name(t.Target) + " morreu (ordem de matar cumprida por " + Name(t.Npc) + ").");
                return true;
            }
            if (Dist(t.Npc, t.Target.current_tile) > 4f)
            {
                if (!UnitHold.Active && !Go(t.Npc, t.Target.current_tile))
                {
                    if (++t.PathFails > 8) { ChatWindow.Toast(Name(t.Npc) + " não encontrou caminho até " + Name(t.Target) + "."); return true; }
                }
                else t.PathFails = 0;
            }
            else if (t.Npc.attack_target != t.Target) { t.Npc.addAggro(t.Target); t.Npc.startFightingWith(t.Target); }
            return false;
        }
    }
}
