using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace WorldfallAIChat
{
    /// <summary>
    /// v7: "native" actions, done with WorldBox's own public game API (no Worldfall menu): rename, follow / stop
    /// following, guide, where-is (+ Worldfall ping or camera), attack, assign a city job, and a ruler's order to found a
    /// new village. They are offered to the same pass-1 classifier as Worldfall's actions (and, unlike those, also to the
    /// god voice). Every action: config switch, try/catch, one "ACTION ..." log line.
    ///
    /// Refusal rules when you possess a unit: the NPC refuses everything if it is your enemy (kingdoms at war, it is
    /// attacking you, or Worldfall opinion &lt;= enemy_opinion_threshold), and refuses ORDERS (follow, guide, attack, job,
    /// village, rename) from someone of lower rank: king 5 &gt; city leader 4 &gt; army captain / clan chief 3 &gt; warrior 2 &gt; adult 1.
    /// Rename: only the god voice or the NPC's own king (deterministic block for anyone else).
    /// God voice: the NPC tends to obey; a trait-based chance makes it defy (strong_minded, ambitious, evil...).
    /// Maintained actions (follow, guide, attack, job, colonist walk) are ticked from Main.Update.
    /// </summary>
    public static partial class NativeActions
    {
        public static readonly HashSet<string> Ids = new HashSet<string>
            { "rename", "follow_me", "stop_follow", "guide_to", "where_is", "attack", "assign_job", "create_village", "summon", "relay_message",
              "declare_war", "make_peace", "attack_city", "go_to", "teleport", "order_kill" };   // v0.7.6: real diplomacy; v0.7.8: movement

        static readonly HashSet<string> Orders = new HashSet<string>
            { "rename", "follow_me", "guide_to", "attack", "assign_job", "create_village", "summon", "declare_war", "make_peace", "attack_city", "go_to", "teleport" };

        static WorldfallActions.ActionDef D(string id, string pt, string en, string param) => new WorldfallActions.ActionDef(id, pt, en, param);

        static readonly Dictionary<string, WorldfallActions.ActionDef> Defs = new Dictionary<string, WorldfallActions.ActionDef>
        {
            { "rename", D("rename", "ordenar que o personagem passe a usar um NOVO NOME", "order a new name", "alvo = o novo nome exato") },
            { "follow_me", D("follow_me", "pedir/mandar que o personagem SIGA o jogador (venha comigo)", "follow me", "") },
            { "stop_follow", D("stop_follow", "pedir que o personagem PARE DE SEGUIR o jogador", "stop following", "") },
            { "guide_to", D("guide_to", "pedir que o personagem LEVE/GUIE o jogador até uma pessoa ou cidade", "guide me to", "alvo = nome da pessoa/cidade (ou rei, líder)") },
            { "where_is", D("where_is", "perguntar ONDE ESTÁ uma pessoa/cidade (ou pedir para marcar no mapa)", "where is", "alvo = nome da pessoa/cidade (ou rei, líder)") },
            { "attack", D("attack", "mandar o personagem ATACAR UMA PESSOA específica (para cidade use attack_city; para reino/guerra use declare_war)", "attack one person", "alvo = nome da pessoa") },
            { "order_kill", D("order_kill", "mandar o personagem MATAR OUTRA pessoa (\"sacrifique seu filho\", \"mate um súdito\", \"mate o Borin\"); NÃO é para matar o próprio ouvinte (isso é kill)", "order to kill someone else", "alvo = nome da pessoa, OU parente (filho, filha, esposa, marido, pai, mãe, irmão), OU \"súdito\"") },
            { "declare_war", D("declare_war", "DECLARAR GUERRA a outro reino (\"inicie uma guerra com os orcs\", \"declare guerra a X\", \"ataque/invada o reino X\")", "declare war on a kingdom", "alvo = nome do reino, OU espécie (orcs, elfos, humanos, anões), OU nome do rei/cidade desse reino") },
            { "make_peace", D("make_peace", "FAZER AS PAZES / acabar a guerra com outro reino", "make peace with a kingdom", "alvo = nome do reino (ou espécie, rei, cidade)") },
            { "attack_city", D("attack_city", "mandar ATACAR/DESTRUIR/INVADIR uma CIDADE (\"destrua a cidade X\", \"invada Naola\"); NÃO é para \"vá até a cidade X\" (isso é go_to)", "attack a city", "alvo = nome da cidade (pode ser só parte do nome)") },
            { "go_to", D("go_to", "mandar o personagem IR/ANDAR até uma cidade, pessoa ou direção (\"vá até a cidade de Vento\", \"ande até o Raar\", \"vá para o norte\"); só andar, sem atacar", "walk somewhere", "alvo = cidade, pessoa ou direção") },
            { "teleport", D("teleport", "(poder divino) TELETRANSPORTAR o personagem (\"teleporte-se para Vento\", \"apareça em Naola\")", "teleport", "alvo = cidade, pessoa ou direção") },
            { "assign_job", D("assign_job", "mandar o personagem TRABALHAR em outra função (fazendeiro, lenhador, minerador, construtor, caçador, coletor, guerreiro...)", "assign a job", "alvo = função") },
            { "summon", D("summon", "pedir/mandar que o personagem CHAME/TRAGA outra pessoa até aqui (\"traga o Raar\", \"chame o líder\")", "summon someone here", "alvo = nome da pessoa (ou rei, líder); se o jogador disser \"ele/ela\", use o nome citado antes na conversa") },
            { "relay_message", D("relay_message", "pedir que o personagem LEVE UM RECADO a outra pessoa (\"diga ao Raar que...\", \"avise o rei que...\")", "deliver a message", "alvo = destinatário; recado = conteúdo do recado") },
            { "create_village", D("create_village", "ORDENAR (como governante/deus) que o personagem vá fundar uma NOVA VILA do reino", "order a new village", "alvo = direção pedida, se houver (norte, sul, leste, oeste, nordeste...)") },
        };

        public static WorldfallActions.ActionDef Def(string id) => id != null && Defs.TryGetValue(id, out var d) ? d : null;

        static bool Alive(Actor a) { try { return a != null && a.isAlive(); } catch { return false; } }
        static string Name(Actor a) { try { return a.getName(); } catch { return "?"; } }

        // ------------------------------------------------------------------------------------------------
        // Availability
        // ------------------------------------------------------------------------------------------------
        public static List<WorldfallActions.Available> AvailableFor(Actor npc, Actor you, DialogueConfig cfg)
        {
            var list = new List<WorldfallActions.Available>();
            if (cfg == null || !cfg.NativeActionsEnabled || !Alive(npc)) return list;
            bool god = you == null;
            void Add(string id, bool on, bool enabled = true, string note = null)
            {
                if (on) list.Add(new WorldfallActions.Available { Def = Defs[id], Enabled = enabled, Note = note });
            }
            try
            {
                Add("rename", cfg.ActionRename);
                Add("where_is", cfg.ActionWhereIs);
                if (!god)
                {
                    Add("follow_me", cfg.ActionFollow);
                    Add("stop_follow", cfg.ActionFollow);
                    Add("guide_to", cfg.ActionGuide);
                }
                Add("attack", cfg.ActionAttack);
                Add("order_kill", cfg.ActionAttack);   // v0.7.8
                Add("go_to", cfg.ActionSummon);   // v0.7.8: walk somewhere (same switch as summon: maintained walking)
                if (god) Add("teleport", cfg.ActionSummon);
                Add("summon", cfg.ActionSummon);
                Add("relay_message", cfg.ActionMessages);
                bool hasCity = false; try { hasCity = npc.city != null && npc.city.isAlive(); } catch { }
                Add("assign_job", cfg.ActionAssignJob, hasCity, hasCity ? null : "não mora em nenhuma cidade");
                if (cfg.ActionWar)   // v0.7.6
                {
                    string why = WarBlock(npc);
                    Add("declare_war", true, why == null, why);
                    Add("make_peace", true, why == null, why);
                    if (why != null && god) Add("attack_city", true);   // v0.7.8: no kingdom -> the unit itself raids the city
                    else Add("attack_city", true, why == null, why);
                }
                if (cfg.ActionCreateVillage)
                {
                    bool leader = god;
                    try { leader = god || ((you.isKing() || you.isCityLeader()) && you.kingdom != null && you.kingdom == npc.kingdom); } catch { }
                    if (leader)
                    {
                        string why = VillageBlock(npc);
                        Add("create_village", true, why == null, why);
                    }
                }
            }
            catch (Exception e) { Main.LogWarning("[GodBox] native action list failed: " + e.Message); }
            return list;
        }

        static string VillageBlock(Actor npc)
        {
            try
            {
                var k = npc.kingdom;
                if (k == null || !k.isAlive() || !k.isCiv()) return "não pertence a um reino";
                if (!k.hasCapital()) return "o reino não tem capital";
                if (!WorldLawLibrary.world_law_kingdom_expansion.isEnabled()) return "a lei do mundo de expansão de reinos está desligada";
            }
            catch { return "não foi possível checar"; }
            return null;
        }

        /// <summary>Native actions that need a confirmation first (shown as a pending offer).</summary>
        public static bool NeedsConfirm(WorldfallActions.Request req, Actor npc, Actor you)
        {
            if (req == null) return false;
            if (req.Id == "create_village" || req.Id == "kill") return true;
            if (req.Id == "order_kill") return OrderKillNeedsConfirm(req, npc, you);   // v0.7.8: a relative or a subject of the own kingdom
            if (req.Id == "preach" && Regex.IsMatch(Fold(req.Target ?? ""), "reino|imperio|nacao|kingdom")) return true;
            if (req.Id == "attack")
            {
                var t = FindUnit(req.Target, npc, you, out _);
                try { return t != null && npc.kingdom != null && t.kingdom == npc.kingdom; } catch { return true; }
            }
            if (req.Id == "declare_war" || req.Id == "make_peace" || req.Id == "attack_city")
                return WarPrecheck(req, npc, you) == null;   // v0.7.6: only offer terms when it can really happen
            return false;
        }

        /// <summary>Terms text for a pending native offer.</summary>
        public static string Terms(WorldfallActions.Request req, Actor npc, Actor you, bool pt)
        {
            if (req.Id == "create_village")
            {
                string col = null; try { var c = PickColonist(npc); col = c == npc ? (pt ? "você mesmo" : "yourself") : Name(c); } catch { }
                string dir = ParseDir((req.Message ?? "") + " " + (req.Target ?? ""), out string dn, out _, out _) ? " ao " + dn + " (ou a terra livre mais próxima, se não houver)" : "";
                return pt ? "fundar uma nova vila do reino" + dir + " em terra livre válida; colono: " + (col ?? "um adulto da cidade") + "; o colono deixa a cidade atual e caminha até lá; se empacar, a vila é fundada na melhor terra perto dele"
                          : "found a new village of the kingdom on free land the game picks; colonist: " + (col ?? "an adult of the city");
            }
            if (req.Id == "preach")
            {
                string k = null, r = null; try { k = npc.kingdom?.name; r = npc.religion?.name; } catch { }
                return pt ? "impor a religião " + (r ?? "do profeta") + " ao reino inteiro" + (k != null ? " (" + k + ")" : "") + ": todas as cidades e o reino mudam de religião, e parte do povo se converte na hora"
                          : "impose the religion on the whole kingdom";
            }
            if (req.Id == "kill")
                return pt ? "MATAR " + Name(npc) + " para sempre (irreversível)" + (Regex.IsMatch(Fold(req.Target ?? ""), "raio|relamp|trova|fulmin") ? ", com um raio" : "")
                          : "KILL " + Name(npc) + " forever (irreversible)";
            if (req.Id == "order_kill")
            {
                var kt = KillTarget(req, npc, you, out string rel, out _);
                return pt ? "MATAR " + Name(kt) + (rel != null ? " (seu/sua " + rel + ")" : SameKingdom(npc, kt) ? " (súdito do seu próprio reino)" : "") + ": " + Name(npc) + " vai até ele(a) e ataca até a morte (irreversível)"
                          : "KILL " + Name(kt) + " (irreversible)";
            }
            if (req.Id == "declare_war" || req.Id == "make_peace" || req.Id == "attack_city")
                return WarTerms(req, npc, you, pt);
            if (req.Id == "attack")
            {
                var t = FindUnit(req.Target, npc, you, out _);
                return pt ? "atacar " + Name(t) + ", que é do SEU PRÓPRIO reino (crime contra o próprio povo)" : "attack " + Name(t) + ", from your own kingdom";
            }
            return "";
        }

        // ------------------------------------------------------------------------------------------------
        // Ranks, enemies, god defiance
        // ------------------------------------------------------------------------------------------------
        public static int Rank(Actor a)
        {
            try
            {
                if (a.isKing()) return 5;
                if (a.isCityLeader()) return 4;
                if (a.is_army_captain || (a.clan != null && a.clan.getChief() == a)) return 3;
                if (a.isWarrior()) return 2;
                if (a.isAdult()) return 1;
            }
            catch { }
            return 0;
        }

        public static string RankName(int r) =>
            r == 5 ? "rei/rainha" : r == 4 ? "líder da cidade" : r == 3 ? "capitão/chefe de clã" : r == 2 ? "guerreiro(a)" : r == 1 ? "adulto(a)" : "criança";

        /// <summary>Why npc treats you as an enemy, or null.</summary>
        public static string EnemyReason(Actor npc, Actor you, DialogueConfig cfg)
        {
            if (npc == null || you == null) return null;
            try
            {
                var a = npc.kingdom; var b = you.kingdom;
                if (a != null && b != null && a != b && a.isCiv() && b.isCiv() && a.isEnemy(b)) return "seus reinos estão em guerra";
            }
            catch { }
            try { if (npc.attack_target == you) return "está lutando contra você"; } catch { }
            var op = WorldfallBridge.Opinion(npc, you);
            if (op.HasValue && op.Value <= (cfg?.EnemyOpinionThreshold ?? -40)) return "detesta você (opinião " + op.Value + " no Worldfall)";
            return null;
        }

        // v0.8.0: in the divine form only truly rebellious natures can defy God. "Minor" traits (deceitful, greedy, stupid)
        // add a little ONLY on top of a rebellious one; alone they never make a ruler defy (bug: "deceitful" alone refused a war).
        static readonly (string id, float w)[] RebelTraits =
        {
            ("strong_minded", 0.25f), ("ambitious", 0.12f), ("evil", 0.15f), ("psychopath", 0.15f),
            ("hotheaded", 0.1f), ("paranoid", 0.1f), ("madness", 0.1f),
        };
        static readonly (string id, float w)[] MinorTraits = { ("greedy", 0.03f), ("deceitful", 0.03f), ("stupid", 0.03f) };
        static readonly (string id, float w)[] CalmTraits =
        {
            ("honest", -0.05f), ("content", -0.05f), ("peaceful", -0.05f), ("wise", -0.05f), ("blessed", -0.05f), ("pacifist", -0.05f),
        };

        static bool HasTrait(Actor a, string id) { try { return a.hasTrait(id); } catch { return false; } }

        /// <summary>Portuguese trait name from the game's own pt locale (never a raw id).</summary>
        public static string TraitPt(string id)
        {
            try { var t = AssetManager.traits.get(id); var n = t == null ? null : GameLocalePt.Trait(t); if (!string.IsNullOrWhiteSpace(n)) return n.ToLowerInvariant(); } catch { }
            return id.Replace('_', ' ');
        }

        /// <summary>Chance that a NON-malefic, non-holy being defies the divine sky voice. because = pt trait names (not ids).
        /// Ambitious alone (+ base) is the minimum "rebellious" case; "deceitful"/"greedy"/"stupid" only count next to a rebel trait.</summary>
        public static float DefianceChance(Actor npc, DialogueConfig cfg, out string because)
        {
            var why = new List<string>();
            float rebel = 0f;
            foreach (var (id, w) in RebelTraits) if (HasTrait(npc, id)) { rebel += w; why.Add(TraitPt(id)); }
            float p = 0f;
            if (rebel > 0f)
            {
                p = (cfg?.GodDefianceBase ?? 0.08f) + rebel;
                foreach (var (id, w) in MinorTraits) if (HasTrait(npc, id)) { p += w; why.Add(TraitPt(id)); }
                foreach (var (id, w) in CalmTraits) if (HasTrait(npc, id)) p += w;
            }
            if (p > 0f && Threatened(npc))
            {   // v0.7.8: a death threat from God makes most beings give in (fearful ones almost always)
                p *= Fearful(npc) ? 0.15f : 0.5f;
                why.Add(Fearful(npc) ? "ameaçado(a) e medroso(a)" : "ameaçado(a)");
            }
            because = why.Count == 0 ? null : string.Join(", ", why);
            return Mathf.Clamp(p, 0f, 0.7f);
        }

        /// <summary>v0.8.0: the god voice's order is accepted or refused HERE, once, before any choice buttons or confirmation.
        /// null = obeys (req.GodDecided set so nothing re-rolls later); otherwise the refusal reason as a Portuguese narrative motive.
        /// Divine form: malefic defy; holy obey; others roll DefianceChance (logged).</summary>
        public static string PreDecide(WorldfallActions.Request req, Actor npc, Actor you, DialogueConfig cfg, string targets)
        {
            if (req == null || you != null || npc == null || req.GodDecided) return null;
            if (!Orders.Contains(req.Id) || req.Id == "order_kill") return null;   // order_kill has its own decision (OrderKillDecision)
            string tw = string.IsNullOrWhiteSpace(targets) ? "" : " (alvo: " + targets + ")";
            string who = Name(npc);
            var nat = Nature.Normal; try { nat = Alignment.Of(npc); } catch { }
            if (nat == Nature.Malefic)
            {
                Main.LogInfo("[GodBox] OBEDIENCE [" + who + "] " + req.Id + tw + ": REFUSES before any confirmation (malefic, defies God).");
                Flavor(npc, false);
                return "é um ser maléfico (" + (Alignment.Why(npc) ?? "servo das trevas") + "): sabe que quem fala é Deus, odeia-o e o desafia de propósito (não é falta de autoridade: Deus tem autoridade, ele simplesmente não obedece)" + tw;
            }
            if (nat == Nature.Holy) { Main.LogInfo("[GodBox] OBEDIENCE [" + who + "] " + req.Id + tw + ": obeys (holy being)."); Flavor(npc, true); req.GodDecided = true; return null; }
            float p = DefianceChance(npc, cfg, out string because);
            double roll = Rng.NextDouble();
            bool defy = roll < p;
            Main.LogInfo("[GodBox] OBEDIENCE roll [" + who + "] " + req.Id + tw + ": p(defy)=" + p.ToString("0.00") + " roll=" + roll.ToString("0.00") + " traits=" + (because ?? "nenhum rebelde") + " -> " + (defy ? "REFUSES (decided before any confirmation)" : "obeys"));
            if (!defy) { Flavor(npc, true); req.GodDecided = true; return null; }
            Flavor(npc, false);
            return Motive(npc, req.Id, because) + tw;
        }

        /// <summary>Narrative motive in Portuguese ("por ser ambiciosa e desonesta, não quer arriscar o reino nisso"). Never raw ids.</summary>
        static string Motive(Actor npc, string id, string because)
        {
            string traits = because == null ? "pelo seu jeito" : "por ser " + because.Replace(", ", " e ");
            bool ruler = false; try { ruler = npc.isKing(); } catch { }
            string what = id == "declare_war" || id == "attack_city" ? (ruler ? "não quer arriscar o reino nessa guerra" : "não quer arriscar a vida nessa luta")
                : id == "make_peace" ? "não quer largar essa guerra agora"
                : "não quer cumprir essa ordem";
            return traits + ", " + what + " (decisão própria; Deus não proibiu nada)";
        }

        static void Flavor(Actor a, bool obeyed)
        {
            try
            {
                if (obeyed) { if (!a.addStatusEffect("inspired", 0f, true)) a.addStatusEffect("surprised", 0f, true); }
                else { a.addStatusEffect("angry", 0f, true); a.addStatusEffect("swearing", 2f, false); }
            }
            catch { }
        }

        static readonly System.Random Rng = new System.Random();

        /// <summary>null = allowed; otherwise the refusal reason (pt). Also adds god-voice flavour statuses.</summary>
        static string Gate(WorldfallActions.Request req, Actor npc, Actor you, DialogueConfig cfg, out bool defied)
        {
            defied = false;
            if (you == null)
            {
                if (req == null || !Orders.Contains(req.Id) || req.GodDecided) return null;   // v0.8.0: already decided before the confirmation
                string r = PreDecide(req, npc, you, cfg, req.Targets != null && req.Targets.Count > 0 ? string.Join(", ", req.Targets) : req.Target);
                if (r != null) defied = true;
                return r;
            }
            return HardBlock(req.Id, npc, you, cfg, req.Target);
        }

        /// <summary>v0.7.4: attacking someone of the NPC's OWN kingdom is only accepted from the god voice
        /// or from the king of that kingdom (player possessing him). null = allowed.</summary>
        public static string OwnKingdomAttackBlock(Actor npc, Actor you, Actor t)
        {
            if (you == null || t == null || npc == null) return null;   // god voice: trait-based defiance only
            try
            {
                var k = npc.kingdom;
                if (k == null || t.kingdom != k) return null;
                bool king = you.isKing() && you.kingdom == k;
                if (king) return null;
                return "só o rei ou os deuses podem mandar atacar alguém do nosso reino (" + Name(t) + " é do reino " + k.name + ")";
            }
            catch { return null; }
        }

        /// <summary>Deterministic refusals for a possessed speaker (no dice): enemy, rename rule, rank. null = allowed.
        /// Used before offering terms, so an offer the NPC would refuse anyway is not made.</summary>
        public static string HardBlock(string id, Actor npc, Actor you, DialogueConfig cfg, string target = null)
        {
            if (you == null || id == null) return null;
            bool order = Orders.Contains(id);
            string enemy = EnemyReason(npc, you, cfg);
            if (enemy != null) return "recusa: " + enemy;
            if (id == "attack" && target != null)
            {
                string own = OwnKingdomAttackBlock(npc, you, FindUnit(target, npc, you, out _));
                if (own != null) return own;
            }
            if (id == "declare_war" || id == "make_peace" || id == "attack_city")
            {
                string kb = KingOrGodBlock(id, npc, you, target);   // v0.7.6
                if (kb != null) return kb;
                return null;   // the king outranks everyone: no rank rule here
            }
            if (id == "rename")
            {
                bool king = false;
                try { king = you.isKing() && you.kingdom != null && npc.kingdom == you.kingdom; } catch { }
                if (!king) return "só o rei (do próprio reino) ou os deuses podem mudar o nome de alguém";
                return null;
            }
            if (order)
            {
                int rn = Rank(npc), ry = Rank(you);
                if (rn > ry) return "tem posição mais alta (" + RankName(rn) + ") do que quem dá a ordem (" + RankName(ry) + ")";
            }
            return null;
        }

        // ------------------------------------------------------------------------------------------------
        // Targets
        // ------------------------------------------------------------------------------------------------
        public static string Fold(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var n = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(n.Length);
            foreach (char c in n)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString().Trim();
        }

        static string CleanTarget(string q)
        {
            q = Fold(q);
            q = Regex.Replace(q, @"^(o|a|os|as|ao|à|até|ate|para|pra|pro|um|uma|the)\s+", "");
            q = Regex.Replace(q, @"[^\p{L}\p{N}' -]", " ").Trim();
            return Regex.Replace(q, @"\s+", " ");
        }

        static float Dist(Actor a, WorldTile t)
        {
            try { var p = a.current_tile; return Mathf.Sqrt((p.x - t.x) * (p.x - t.x) + (p.y - t.y) * (p.y - t.y)); } catch { return 99999f; }
        }

        /// <summary>Fuzzy, accent-folded unit search (aliases: rei/rainha, líder/prefeito, eu/mim). ambiguous = how many equal matches.</summary>
        public static Actor FindUnit(string query, Actor npc, Actor you, out int ambiguous)
        {
            ambiguous = 0;
            string q = CleanTarget(query);
            if (q.Length == 0) return null;
            if (Regex.IsMatch(q, @"^(cidade|vila|aldeia|capital|reino|imperio)\b")) return null;   // v0.7.6: a place, not a person
            if (SafetyRules.IsCommonWord(q) && !Regex.IsMatch(q, @"^(rei|rainha|lider|eu|mim|voce|deus|pai|mae|filho|filha|irmao|irma|amigo|amiga)$")) return null;   // v0.8.1 (B2a): "ore", "venha"... are not names
            try
            {
                if (Regex.IsMatch(q, @"^(meu |seu |nosso |teu )?(rei|rainha|soberano|monarca|king|queen)\b")) return npc.kingdom?.king;
                if (Regex.IsMatch(q, @"^(meu |seu |nosso |teu )?(lider|prefeito|chefe da cidade|governante|leader)\b")) return npc.city?.leader;
                if (Regex.IsMatch(q, @"^(eu|mim|me|comigo|me)$") && you != null) return you;
            }
            catch { }
            Actor best = null; int bestScore = 0; float bestD = float.MaxValue;
            WorldTile from = null; try { from = npc.current_tile; } catch { }
            foreach (Actor a in World.world.units)
            {
                try
                {
                    if (a == null || !a.isAlive() || a == npc) continue;
                    string n = Fold(a.getName());
                    if (n.Length == 0) continue;
                    int sc = NameLookup.Score(q, n);   // v0.7.6: shared fuzzy matcher (prefix/word/typo)
                    if (sc == 0) continue;
                    float d = from != null ? Dist(a, from) : 0f;
                    if (sc > bestScore || (sc == bestScore && d < bestD))
                    {
                        if (sc > bestScore) ambiguous = 0;
                        best = a; bestScore = sc; bestD = d;
                    }
                    if (sc == bestScore) ambiguous++;
                }
                catch { }
            }
            return best;
        }

        public static City FindCity(string query, Actor npc)
        {
            if (TryToken(query, "#c:", out long cid))
            {   // v0.7.8: a city picked from the choice buttons
                try { foreach (City c in World.world.cities) if (c != null && c.isAlive() && c.getID() == cid) return c; } catch { }
                return null;
            }
            string q = CleanTarget(query);
            q = Regex.Replace(q, @"^(cidade|vila|aldeia|capital|city|town)( de| do| da)?\s+", "");
            if (q.Length == 0) return null;
            try
            {
                if (Regex.IsMatch(q, @"^(nossa |sua |minha )?(capital)$")) return npc.kingdom?.capital;
                if (Regex.IsMatch(q, @"^(nossa |sua |minha |esta |essa )?(cidade|vila)$")) return npc.city;
            }
            catch { }
            City best = null; int bestScore = 0;
            foreach (City c in World.world.cities)
            {
                try
                {
                    if (c == null || !c.isAlive()) continue;
                    int sc = NameLookup.Score(q, c.name);   // v0.7.6: "Naola" -> "Naola Raadi"
                    if (sc > bestScore) { best = c; bestScore = sc; }
                }
                catch { }
            }
            return best;
        }

        // ------------------------------------------------------------------------------------------------
        // Execute
        // ------------------------------------------------------------------------------------------------
        static WorldfallActions.Result NewResult(string id)
        {
            var d = Def(id);
            return new WorldfallActions.Result { Id = id, Pt = d?.Pt ?? id, En = d?.En ?? id };
        }

        public static WorldfallActions.Result Execute(Actor npc, Actor you, WorldfallActions.Request req, DialogueConfig cfg)
        {
            var res = NewResult(req?.Id);
            try
            {
                if (req == null || !Ids.Contains(req.Id)) { res.Reason = "ação desconhecida"; return res; }
                if (!Alive(npc)) { res.Reason = "o personagem morreu"; return res; }
                string block = Gate(req, npc, you, cfg, out bool defied);
                if (block != null) { res.Outcome = "recusado"; res.Reason = block; return res; }
                if (defied) { res.Outcome = "recusado"; return res; }
                switch (req.Id)
                {
                    case "rename": Rename(npc, req.Target, res, you == null || Avatar.IsAvatar(you)); break;
                    case "follow_me": Follow(npc, you, cfg, res); break;
                    case "stop_follow": StopFollow(npc, res); break;
                    case "guide_to": Guide(npc, you, req.Target, cfg, res); break;
                    case "where_is":
                        WhereIs(npc, you, ResolveRef(req.Target, npc) ?? req.Target, res, req.Style);
                        if (you == null && req.Style == "guide" && res.Outcome == "feito")
                            res.Changes.Insert(0, "pediram para LEVAR até lá; como quem pede é Deus (sem corpo), a visão divina (câmera) foi levada até o alvo, que ficou marcado com estrela");
                        break;
                    case "attack": Attack(npc, you, req.Target, cfg, res); break;
                    case "assign_job": AssignJob(npc, req.Target, cfg, res); break;
                    case "create_village": CreateVillage(npc, you, req, cfg, res); break;
                    case "declare_war":
                        if (req.Targets != null && req.Targets.Count > 1) ForEachTarget(npc, you, req, res); else DeclareWar(npc, you, req.Target, res); break;
                    case "make_peace":
                        if (req.Targets != null && req.Targets.Count > 1) ForEachTarget(npc, you, req, res); else MakePeace(npc, you, req.Target, res); break;
                    case "attack_city": if (Kingdomless(npc)) SoloRaid(npc, you, req, cfg, res); else AttackCity(npc, you, req, res); break;
                    case "go_to": GoTo(npc, you, req.Target, cfg, res); break;
                    case "order_kill": OrderKill(npc, you, req, cfg, res); break;
                    case "teleport": Teleport(npc, you, req.Target, res, cfg); break;
                    case "summon": Summon(npc, you, req.Target, cfg, res); break;
                    case "relay_message": RelayMessage(npc, you, req.Target, req.Message, cfg, res); break;
                }
            }
            catch (Exception e)
            {
                res.Outcome = "falhou"; res.Reason = "erro: " + e.Message;
                Main.LogWarning("[GodBox] native action " + req?.Id + " failed: " + e);
            }
            return res;
        }

        // ---------- summon (v7) ----------
        static void Summon(Actor npc, Actor you, string target, DialogueConfig cfg, WorldfallActions.Result res)
        {
            var t = FindUnit(target, npc, you, out int amb);
            if (t == null) { res.Reason = string.IsNullOrWhiteSpace(target) ? "não disse quem chamar" : "ninguém chamado \"" + target + "\" existe neste mundo"; return; }
            Actor dest = Alive(you) ? you : npc;
            if (t == you) { res.Outcome = "sem_efeito"; res.Reason = "quem pediu já está aqui"; return; }
            try
            {
                var a = t.kingdom; var b = dest.kingdom;
                if (a != null && b != null && a != b && a.isEnemy(b)) { res.Outcome = "recusado"; res.Reason = Name(t) + " é de um reino inimigo e não atende ao chamado"; return; }
            }
            catch { }
            if (you != null && Rank(t) > Rank(you)) { res.Outcome = "recusado"; res.Reason = Name(t) + " (" + RankName(Rank(t)) + ") não atende ao chamado de alguém de posição mais baixa"; return; }
            float d = Dist(t, dest.current_tile);
            string where = FactsBuilder.Where(dest.current_tile, t.current_tile, true);
            if (d <= 4f)
            {
                res.Outcome = "feito"; res.Changes.Add(Name(t) + " já está bem aqui ao lado");
                ChatWindow.SummonArrived(t, dest);
                return;
            }
            Drop(t, "follow", "guide", "attack", "summon", "relay");
            try { t.finishStatusEffect("possessed_follower"); } catch { }
            float now = Time.unscaledTime;
            float eta = d / Math.Max(0.5f, cfg.SummonTilesPerSecond);
            _tasks.Add(new Task { Kind = "summon", Npc = t, Target = dest, Until = now + Math.Max(30f, cfg.SummonSeconds), Next = now, NextStatus = now });
            res.Outcome = "em_andamento";
            res.Changes.Add(Name(t) + " foi chamado(a) e vem vindo até " + (dest == npc ? Name(npc) : "o jogador") + ": está " + where + (eta > 60f ? " (é longe: vai demorar um bom tempo)" : eta > 20f ? " (vai levar um tempinho)" : " (está perto)"));
            res.Changes.Add(Name(t) + " AINDA NÃO CHEGOU");
            res.Reason = "só anda com o mundo rodando (feche o chat); você será avisado quando chegar";
            if (amb > 1) res.Changes.Add("havia " + amb + " com esse nome; chamou o mais perto");
        }

        // ---------- relay a message (v7) ----------
        static void RelayMessage(Actor npc, Actor you, string target, string message, DialogueConfig cfg, WorldfallActions.Result res)
        {
            var t = FindUnit(target, npc, you, out _);
            if (t == null) { res.Reason = string.IsNullOrWhiteSpace(target) ? "não disse para quem é o recado" : "ninguém chamado \"" + target + "\" existe neste mundo"; return; }
            if (t == npc) { res.Outcome = "sem_efeito"; res.Reason = "o recado é para o próprio personagem"; return; }
            message = (message ?? "").Trim().Trim('"');
            if (message.Length < 2) { res.Reason = "não ficou claro qual é o recado"; return; }
            if (message.Length > 220) message = message.Substring(0, 220) + "…";
            string from = you == null ? "Deus" : Name(you);
            string text = Name(npc) + " te trouxe um recado de " + from + ": \"" + message + "\"";
            bool sameCity = false; try { sameCity = npc.city != null && npc.city == t.city; } catch { }
            if (sameCity || Dist(npc, t.current_tile) <= 6f)
            {
                Rumors.Deliver(t, text);
                res.Outcome = "feito";
                res.Changes.Add("recado entregue a " + Name(t) + (sameCity ? " (moram na mesma cidade)" : " (está perto)"));
                return;
            }
            Drop(npc, "follow", "guide", "relay");
            float now = Time.unscaledTime;
            _tasks.Add(new Task { Kind = "relay", Npc = npc, Target = t, Job = text, Until = now + Math.Max(30f, cfg.MessageMaxSeconds), Next = now });
            res.Outcome = "em_andamento";
            res.Changes.Add("vai levar o recado a " + Name(t) + ", que está " + FactsBuilder.Where(npc.current_tile, t.current_tile, true) + "; entrega quando chegar perto (ou, se demorar demais, por viajantes)");
            res.Changes.Add("o recado AINDA NÃO FOI ENTREGUE");
        }

        // ---------- rename ----------
        static void Rename(Actor npc, string newName, WorldfallActions.Result res, bool byGod = false)
        {
            string name = (newName ?? "").Trim().Trim('"', '\'', '“', '”', '.', '!', '?', ',');
            name = Regex.Replace(name, @"\s+", " ");
            if (name.Length < 2 || name.Length > 40) { res.Reason = "nome inválido ou não dito"; return; }
            string old = Name(npc);
            if (Fold(old) == Fold(name)) { res.Outcome = "sem_efeito"; res.Reason = "já se chama assim"; return; }
            npc.data.custom_name = true;
            npc.setName(name);
            long id = npc.getID();
            // keep the names other objects store in sync (same as the game's own rename box)
            try { foreach (City c in World.world.cities) { if (!c.isAlive()) continue; c.updateRulers(); if (c.data.founder_id == id) c.data.founder_name = name; } } catch { }
            try { foreach (Army a in World.world.armies) if (a.isAlive()) a.updateCaptains(); } catch { }
            try { foreach (Kingdom k in World.world.kingdoms) if (k.isAlive()) k.updateRulers(); } catch { }
            try { foreach (War w in World.world.wars) if (w.isAlive() && w.data.started_by_actor_id == id) w.data.started_by_actor_name = name; } catch { }
            try { foreach (Alliance a in World.world.alliances) if (a.isAlive() && a.data.founder_actor_id == id) a.data.founder_actor_name = name; } catch { }
            try { foreach (Religion r in World.world.religions) if (r.isAlive() && r.data.creator_id == id) r.data.creator_name = name; } catch { }
            try { foreach (Culture c in World.world.cultures) if (c.isAlive() && c.data.creator_id == id) c.data.creator_name = name; } catch { }
            try { foreach (Clan c in World.world.clans) { if (!c.isAlive()) continue; if (c.data.founder_actor_id == id) c.data.founder_actor_name = name; c.updateChiefs(); } } catch { }
            res.Outcome = "feito";
            res.Changes.Add(SafetyRules.RenameProse(old, name, byGod));   // v0.8.1 (B12): prose, no arrow
        }

        // ---------- maintained tasks ----------
        class Task
        {
            public string Kind; public Actor Npc, Target; public WorldTile Tile; public City City; public TileZone Zone; public Kingdom Kingdom;
            public string Job; public float Until, Next, NextStatus; public int PathFails;
            public bool StarAdded, PingAdded, Arrived;
            public VState V;   // v0.7.6: village walk state   // v0.7.4: star/ping put on the guide target by us (removed when the guide ends)
        }

        // ---------- v0.7.4: favorite star on a person ----------
        /// <summary>Puts WorldBox's favorite star on the unit (and the Worldfall map ping when the player has a body).
        /// Reports what WE added, so we never unstar someone Pedro starred himself.</summary>
        static bool MarkStar(Actor unit, bool withPing, out bool starAdded, out bool pingAdded)
        {
            starAdded = false; pingAdded = false;
            if (unit == null || !Alive(unit)) return false;
            bool marked = false;
            try
            {
                if (unit.isFavorite()) marked = true;
                else { unit.switchFavorite(); starAdded = unit.isFavorite(); marked = starAdded; }
            }
            catch { }
            if (withPing && WorldfallBridge.Present)
            {
                bool had = WorldfallBridge.IsPinged(unit);
                if (WorldfallBridge.Ping(unit)) { marked = true; pingAdded = !had; }
            }
            return marked;
        }

        static void Unmark(Task t)
        {
            if (t == null || t.Kind != "guide" || t.Target == null) return;
            try { if (t.StarAdded && t.Target.isFavorite()) t.Target.switchFavorite(); } catch { }
            try { if (t.PingAdded && WorldfallBridge.Present) WorldfallBridge.Unping(t.Target); } catch { }
            t.StarAdded = t.PingAdded = false;
        }
        static readonly List<Task> _tasks = new List<Task>();

        static void Drop(Actor npc, params string[] kinds)
        {
            if (kinds.Length > 0 && !kinds.Contains("goto")) kinds = kinds.Concat(new[] { "goto", "raid", "order_kill" }).ToArray();   // v0.7.8: a new order replaces a walk/raid
            foreach (var t in _tasks) if (t.Npc == npc && kinds.Contains(t.Kind)) Unmark(t);
            _tasks.RemoveAll(t => t.Npc == npc && kinds.Contains(t.Kind));
        }

        static void ReleaseHoldFor(Actor npc, string why)
        {
            try { if (UnitHold.Active) UnitHold.Release(why); } catch { }
            try { npc.makeWait(0f); } catch { }
        }

        /// <summary>v0.7.9: public walk for prophets' missions.</summary>
        public static bool WalkTo(Actor a, WorldTile t) => a != null && t != null && Go(a, t);

        static bool Go(Actor a, WorldTile t)
        {
            try { var r = a.goTo(t); return r.ToString() != "False"; } catch { return false; }
        }

        // ---------- follow ----------
        static void Follow(Actor npc, Actor you, DialogueConfig cfg, WorldfallActions.Result res)
        {
            if (!Alive(you)) { res.Reason = "sem unidade possuída para seguir"; return; }
            Drop(npc, "follow", "guide", "attack");
            ReleaseHoldFor(npc, "follow");
            try { npc.addStatusEffect("possessed_follower", 0f, true); } catch { }
            float now = Time.unscaledTime;
            _tasks.Add(new Task { Kind = "follow", Npc = npc, Target = you, Until = now + Math.Max(10f, cfg.FollowSeconds), Next = now, NextStatus = now + 60f });
            res.Outcome = "feito";
            res.Changes.Add("passa a seguir " + Name(you) + " por até " + Mathf.RoundToInt(cfg.FollowSeconds) + " s");
        }

        static void StopFollow(Actor npc, WorldfallActions.Result res)
        {
            bool was = _tasks.Any(t => t.Npc == npc && (t.Kind == "follow" || t.Kind == "guide"));
            try { was |= npc.hasStatus("possessed_follower"); } catch { }
            Drop(npc, "follow", "guide");
            try { npc.finishStatusEffect("possessed_follower"); } catch { }
            res.Outcome = was ? "feito" : "sem_efeito";
            if (!was) res.Reason = "não estava seguindo ninguém";
            else res.Changes.Add("parou de seguir");
        }

        // ---------- guide ----------
        static void Guide(Actor npc, Actor you, string target, DialogueConfig cfg, WorldfallActions.Result res)
        {
            var unit = FindUnit(target, npc, you, out int amb);
            City city = unit == null ? FindCity(target, npc) : null;
            WorldTile tile = unit != null ? unit.current_tile : city?.getTile();
            if (tile == null) { res.Reason = string.IsNullOrWhiteSpace(target) ? "não disse para onde" : "não conhece nenhuma pessoa ou cidade chamada \"" + target + "\""; return; }
            Drop(npc, "follow", "guide", "attack");
            try { npc.finishStatusEffect("possessed_follower"); } catch { }
            ReleaseHoldFor(npc, "guide");
            float now = Time.unscaledTime;
            var task = new Task { Kind = "guide", Npc = npc, Target = unit, City = city, Tile = tile, Until = now + Math.Max(10f, cfg.GuideSeconds), Next = now };
            _tasks.Add(task);
            string dest = unit != null ? Name(unit) : city.name;
            res.Outcome = "feito";
            res.Changes.Add("guiando até " + dest + " (" + FactsBuilder.Where(npc.current_tile, tile, true) + ")");
            if (unit != null && (cfg == null || cfg.MarkTargetStar) && MarkStar(unit, you != null, out task.StarAdded, out task.PingAdded))
            {
                res.Changes.Add(dest + " foi marcado(a) com uma ESTRELA no mapa (dá para ver onde está)");
                res.Toasts.Add(dest + " marcado(a) com estrela.");
            }
            res.Toasts.Add("Siga " + Name(npc) + " até " + dest + ".");
            if (amb > 1) res.Changes.Add("havia " + amb + " com esse nome; escolheu o mais perto");
        }

        // ---------- where is ----------
        static void WhereIs(Actor npc, Actor you, string target, WorldfallActions.Result res, string style = null)
        {
            var unit = FindUnit(target, npc, you, out int amb);
            City city = unit == null ? FindCity(target, npc) : null;
            if (unit == null && city == null)
            {
                res.Outcome = "falhou";
                res.Reason = string.IsNullOrWhiteSpace(target) ? "não disse quem" : "ninguém e nenhuma cidade com o nome \"" + target + "\" existe neste mundo";
                return;
            }
            WorldTile tile = unit != null ? unit.current_tile : city.getTile();
            string who = unit != null ? Name(unit) : city.name;
            res.Outcome = "feito";
            res.Changes.Add(who + ": " + FactsBuilder.Where(npc.current_tile, tile, true) + " de você (NPC)");
            if (you != null) res.Changes.Add("de " + Name(you) + " (quem fala): " + FactsBuilder.Where(you.current_tile, tile, true));
            if (unit != null)
            {
                string c = null; try { c = unit.city?.name; } catch { }
                res.Changes.Add(who + (c != null ? " mora em " + c : " não mora em cidade"));
            }
            if (amb > 1) res.Changes.Add("há " + amb + " pessoas com esse nome; falou da mais próxima");
            bool mark = style == "mark";   // v0.7.8: "marque / mostre no mapa onde fica X" -> camera + highlight, nobody walks
            if (you == null || mark)
            {
                try { MoveCamera.instance.focusOn(new Vector3(tile.x, tile.y, 0f)); res.Toasts.Add("Câmera em " + who + "."); } catch { }
            }
            if (city != null && (mark || you == null))
            {
                bool fx = false;
                try { fx = EffectsLibrary.spawnAtTile("fx_cast_ground_blue", tile, 1.5f) != null; } catch { }
                res.Changes.Add("a cidade " + who + " foi MARCADA no mapa (a câmera foi até lá" + (fx ? " e o chão brilhou" : "") + "); ninguém precisa andar até lá");
                res.Toasts.Add(who + " marcada no mapa.");
            }
            else if (mark && unit != null) res.Changes.Add(who + " foi mostrado(a) no mapa (a câmera foi até lá); ninguém precisa andar até lá");
            if (unit != null && (Main.Cfg == null || Main.Cfg.MarkTargetStar))
            {   // v0.7.4: same star as guide_to (kept: the player asked where this person is)
                if (MarkStar(unit, you != null, out _, out _))
                {
                    res.Changes.Add(who + " foi marcado(a) com uma ESTRELA no mapa");
                    res.Toasts.Add(who + " marcado(a) com estrela.");
                }
            }
            else if (unit != null && you != null && WorldfallBridge.Present)
            {
                if (WorldfallBridge.Ping(unit)) res.Toasts.Add(who + " marcado no mapa (ping do Worldfall).");
            }
        }

        // ---------- attack ----------
        static void Attack(Actor npc, Actor you, string target, DialogueConfig cfg, WorldfallActions.Result res)
        {
            var t = FindUnit(target, npc, you, out _);
            if (t == null) { res.Reason = string.IsNullOrWhiteSpace(target) ? "não disse quem atacar" : "não encontrou \"" + target + "\""; return; }
            if (t == you) { res.Outcome = "recusado"; res.Reason = "não vai atacar quem deu a ordem"; return; }
            string own = OwnKingdomAttackBlock(npc, you, t);   // v0.7.4: re-checked right before acting
            if (own != null) { res.Outcome = "recusado"; res.Reason = own; Main.LogInfo("[GodBox] ACTION attack: refused (own kingdom) [" + Name(npc) + " -> " + Name(t) + "]"); return; }
            bool can = true; try { can = npc.canAttackTarget(t, false, false); } catch { }
            if (!can) { res.Reason = "não consegue atacar " + Name(t); return; }
            Drop(npc, "follow", "guide", "attack");
            try { npc.finishStatusEffect("possessed_follower"); } catch { }
            ReleaseHoldFor(npc, "attack");
            Deaths.MarkModCause(t, "atacado(a) por " + Name(npc) + " por ordem de " + (you == null ? "Deus" : Name(you)));
            npc.addAggro(t);
            npc.startFightingWith(t);
            float now = Time.unscaledTime;
            _tasks.Add(new Task { Kind = "attack", Npc = npc, Target = t, Until = now + Math.Max(5f, cfg.AttackSeconds), Next = now + 1f });
            res.Outcome = "feito";
            res.Changes.Add("atacando " + Name(t) + " (" + FactsBuilder.Where(npc.current_tile, t.current_tile, true) + ")");
        }

        // ---------- assign job ----------
        static readonly (string rx, string id, string pt)[] JobWords =
        {
            (@"guerr|soldad|exercit|milic|lutar|combat|warrior|soldier", "warrior", "guerreiro(a)"),
            (@"fazend|agricult|lavour|lavrad|plant|colheit|farm", "farmer", "fazendeiro(a)"),
            (@"constru|pedreir|obra|builder", "builder", "construtor(a)"),
            (@"estrad|caminho|road", "road_builder", "construtor(a) de estradas"),
            (@"lenhad|madeir|cortar arvor|woodcut|lumber", "woodcutter", "lenhador(a)"),
            (@"minera|miner|mina\b|minas\b|cavar|pedreira", "miner", "minerador(a)"),
            (@"cacad|cacar|caca\b|hunt", "hunter", "caçador(a)"),
            (@"ervas|herb", "gatherer_herbs", "coletor(a) de ervas"),
            (@"\bmel\b|abelh|honey", "gatherer_honey", "coletor(a) de mel"),
            (@"colet|frut|baga|arbust|gather|berr", "gatherer_bushes", "coletor(a) de frutas"),
            (@"esterco|estrume|manure", "manure_cleaner", "limpador(a) de esterco"),
            (@"limp|lixo|clean", "cleaner", "limpador(a)"),
        };

        public static string JobFor(string text, out string pt)
        {
            string f = Fold(text);
            foreach (var (rx, id, p) in JobWords)
                if (Regex.IsMatch(f, rx)) { pt = p; return id; }
            pt = null; return null;
        }

        static void AssignJob(Actor npc, string target, DialogueConfig cfg, WorldfallActions.Result res)
        {
            string id = JobFor(target, out string pt);
            if (id == null) { res.Reason = "função desconhecida: \"" + target + "\" (há: fazendeiro, construtor, lenhador, minerador, caçador, coletor, guerreiro...)"; return; }
            City city = npc.city;
            if (city == null || !city.isAlive()) { res.Reason = "não mora em nenhuma cidade"; return; }
            if (!npc.isAdult()) { res.Outcome = "recusado"; res.Reason = "é criança"; return; }
            if (id == "warrior")
            {
                if (npc.isWarrior()) { res.Outcome = "sem_efeito"; res.Reason = "já é guerreiro(a)"; return; }
                if (!city.checkCanMakeWarrior(npc)) { res.Reason = "a cidade não pode ter mais guerreiros agora (exército cheio, falta comida ou regra da cultura)"; return; }
                city.makeWarrior(npc);
                res.Outcome = "feito"; res.Changes.Add("agora é guerreiro(a)");
                return;
            }
            var asset = AssetManager.citizen_job_library.get(id);
            if (asset == null) { res.Reason = "essa função não existe nesta versão do jogo"; return; }
            string before = npc.citizen_job?.id;
            if (before == id) { res.Outcome = "sem_efeito"; res.Reason = "já trabalha como " + pt; return; }
            if (npc.isWarrior()) { try { npc.stopBeingWarrior(); } catch { } }
            bool slot = false;
            try { slot = city.checkCitizenJob(asset, city, npc); } catch { }
            if (!slot) npc.setCitizenJob(asset);
            Drop(npc, "job");
            float now = Time.unscaledTime;
            _tasks.Add(new Task { Kind = "job", Npc = npc, Job = id, Until = now + Math.Max(10f, cfg.JobHoldSeconds), Next = now + 5f });
            res.Outcome = "feito";
            res.Changes.Add(SafetyRules.JobProse(Name(npc), before != null ? FactsBuilder.JobNamePt(before) : null, FactsBuilder.JobNamePt(id) ?? pt) + (slot ? "" : " (fora das vagas da cidade, por um tempo)"));   // v0.8.1 (B12)
        }

        // ---------- create village ----------
        static Actor PickColonist(Actor npc)
        {
            bool Ok(Actor a)
            {
                try { return a != null && a.isAlive() && a.isAdult() && !a.isKing() && !a.isCityLeader() && a.kingdom == npc.kingdom; } catch { return false; }
            }
            if (Ok(npc)) return npc;
            City src = null; try { src = npc.city ?? npc.kingdom?.capital; } catch { }
            if (src == null) return null;
            Actor best = null; float bd = float.MaxValue;
            foreach (var a in src.units)
            {
                if (!Ok(a)) continue;
                bool warrior = false; try { warrior = a.isWarrior(); } catch { }
                float d = Dist(a, npc.current_tile) + (warrior ? 1000f : 0f);
                if (d < bd) { bd = d; best = a; }
            }
            return best;
        }

        static void Done(Task t, string id, string outcome, string reason)
        {
            Main.LogInfo("[GodBox] ACTION " + id + ": " + outcome + " (" + reason + ") [" + Name(t.Npc) + "]");
            if (outcome != "feito") try { ChatWindow.Toast(Name(t.Npc) + ": " + reason + "."); } catch { }
        }

        /// <summary>Called every frame from Main.Update. Cheap when there is nothing to do.</summary>
        public static void Tick()
        {
            if (_tasks.Count == 0) return;
            float now = Time.unscaledTime;
            // v7: the world is paused while the chat is open; deadlines must not run out while nothing can move.
            bool paused = false; try { paused = World.world != null && World.world.isPaused(); } catch { }
            if (paused)
            {
                float dt = Time.unscaledDeltaTime;
                foreach (var pt in _tasks) { pt.Until += dt; pt.Next += dt; pt.NextStatus += dt; if (pt.V != null) pt.V.LastStep += dt; }
                return;
            }
            for (int i = _tasks.Count - 1; i >= 0; i--)
            {
                var t = _tasks[i];
                bool remove = false;
                try
                {
                    if (!Alive(t.Npc)) { remove = true; if (t.Kind == "village") VillageFail(t, "o colono " + Name(t.Npc) + " morreu no caminho"); }
                    else if (now > t.Until)
                    {
                        remove = true;
                        if (t.Kind == "follow") try { t.Npc.finishStatusEffect("possessed_follower"); } catch { }
                        if (t.Kind == "village") VillageFallback(t, "tempo esgotado");
                        else if (t.Kind == "relay" && Alive(t.Target))
                        {   // "after some game time" the word gets there anyway
                            Rumors.Deliver(t.Target, t.Job + " (chegou por viajantes)");
                            Main.LogInfo("[GodBox] ACTION relay_message: delivered by travellers (time) [" + Name(t.Target) + "]");
                            ChatWindow.Toast("O recado chegou a " + Name(t.Target) + " por viajantes.");
                        }
                        else if (t.Kind == "goto") { Main.LogInfo("[GodBox] ACTION go_to: gave up (time) [" + Name(t.Npc) + "]"); ChatWindow.Toast(Name(t.Npc) + " não chegou a " + t.Job + " a tempo."); }
                        else if (t.Kind == "summon") { Main.LogInfo("[GodBox] ACTION summon: gave up (time) [" + Name(t.Npc) + "]"); ChatWindow.Toast(Name(t.Npc) + " não chegou a tempo e desistiu do chamado."); }
                        else Main.LogInfo("[GodBox] ACTION " + t.Kind + ": ended (time) [" + Name(t.Npc) + "]");
                    }
                    else if (now >= t.Next)
                    {
                        switch (t.Kind)
                        {
                            case "follow":
                                t.Next = now + 2f;
                                if (!Alive(t.Target)) { remove = true; break; }
                                if (now >= t.NextStatus) { t.NextStatus = now + 60f; try { t.Npc.addStatusEffect("possessed_follower", 0f, true); } catch { } }
                                if (Dist(t.Npc, t.Target.current_tile) > 8f && !UnitHold.Active) Go(t.Npc, t.Target.current_tile);
                                break;
                            case "guide":
                                t.Next = now + 2f;
                                if (t.Target != null) { if (!Alive(t.Target)) { remove = true; break; } t.Tile = t.Target.current_tile; }
                                if (Dist(t.Npc, t.Tile) <= 3f) { remove = true; Main.LogInfo("[GodBox] ACTION guide_to: arrived [" + Name(t.Npc) + "]"); break; }
                                if (!UnitHold.Active) Go(t.Npc, t.Tile);
                                break;
                            case "attack":
                                t.Next = now + 1f;
                                if (!Alive(t.Target)) { remove = true; Main.LogInfo("[GodBox] ACTION attack: target down [" + Name(t.Npc) + "]"); break; }
                                if (t.Npc.attack_target != t.Target) { t.Npc.addAggro(t.Target); t.Npc.startFightingWith(t.Target); }
                                break;
                            case "job":
                                t.Next = now + 5f;
                                if (t.Npc.citizen_job?.id != t.Job && !t.Npc.isWarrior())
                                {
                                    var asset = AssetManager.citizen_job_library.get(t.Job);
                                    if (asset != null) t.Npc.setCitizenJob(asset);
                                }
                                break;
                            case "summon":
                                t.Next = now + 2f;
                                if (!Alive(t.Target)) { remove = true; Main.LogInfo("[GodBox] ACTION summon: destination gone [" + Name(t.Npc) + "]"); break; }
                                if (Dist(t.Npc, t.Target.current_tile) <= 3.5f)
                                {
                                    remove = true;
                                    int secs = Mathf.RoundToInt(now - t.NextStatus);
                                    Main.LogInfo("[GodBox] ACTION summon: arrived after ~" + secs + " s [" + Name(t.Npc) + " -> " + Name(t.Target) + "]");
                                    ChatWindow.SummonArrived(t.Npc, t.Target);
                                    break;
                                }
                                if (!UnitHold.Active && !Go(t.Npc, t.Target.current_tile) && ++t.PathFails >= 5)
                                {
                                    remove = true;
                                    Main.LogInfo("[GodBox] ACTION summon: failed (no path) [" + Name(t.Npc) + "]");
                                    ChatWindow.Toast(Name(t.Npc) + " não encontrou caminho até " + Name(t.Target) + ".");
                                }
                                break;
                            case "relay":
                                t.Next = now + 2f;
                                if (!Alive(t.Target)) { remove = true; Main.LogInfo("[GodBox] ACTION relay_message: recipient died [" + Name(t.Npc) + "]"); break; }
                                bool near = Dist(t.Npc, t.Target.current_tile) <= 5f;
                                try { near |= t.Npc.city != null && t.Npc.city == t.Target.city && Dist(t.Npc, t.Target.current_tile) <= 30f; } catch { }
                                if (near)
                                {
                                    remove = true;
                                    Rumors.Deliver(t.Target, t.Job);
                                    Main.LogInfo("[GodBox] ACTION relay_message: delivered [" + Name(t.Npc) + " -> " + Name(t.Target) + "]");
                                    ChatWindow.Toast(Name(t.Npc) + " entregou o recado a " + Name(t.Target) + ".");
                                    break;
                                }
                                if (!UnitHold.Active) Go(t.Npc, t.Target.current_tile);
                                break;
                            case "goto":
                            case "raid":
                                remove = MoveStep(t, now);
                                break;
                            case "order_kill":
                                remove = OrderKillStep(t, now);
                                break;
                            case "village":
                                if (t.V == null) { remove = true; break; }
                                remove = VillageStep(t, now, Main.Cfg);
                                break;
                        }
                    }
                }
                catch (Exception e)
                {
                    remove = true;
                    Main.LogWarning("[GodBox] maintained action " + t.Kind + " stopped: " + e.Message);
                }
                if (remove && i < _tasks.Count && _tasks[i] == t) { Unmark(t); _tasks.RemoveAt(i); }
            }
        }

        /// <summary>Short status for the prompt: what this NPC is doing because of an earlier order.</summary>
        public static string Ongoing(Actor npc)
        {
            var coming = _tasks.FirstOrDefault(x => x.Kind == "summon" && x.Target == npc);
            var t = _tasks.FirstOrDefault(x => x.Npc == npc);
            var ordered = _tasks.FirstOrDefault(x => x.Kind == "village" && x.V?.Orderer == npc && x.Npc != npc);
            if (t == null && ordered != null) return VillageOngoing(ordered, npc);
            if (t == null)
                return coming != null ? Name(coming.Npc) + " foi chamado(a) e está a caminho, mas AINDA NÃO CHEGOU (" + FactsBuilder.Where(npc.current_tile, coming.Npc.current_tile, true) + ")" : null;
            switch (t.Kind)
            {
                case "follow": return "você está seguindo " + Name(t.Target) + " (ordem anterior)";
                case "guide": return "você está guiando o jogador até " + (t.Target != null ? Name(t.Target) : t.City?.name);
                case "attack": return "você está atacando " + Name(t.Target);
                case "job": return "você está trabalhando como " + t.Job + " por ordem recebida";
                case "village": return VillageOngoing(t, npc);
                case "summon": return "você foi chamado(a) e está indo até " + Name(t.Target);
                case "relay": return "você está levando um recado para " + Name(t.Target) + " (ainda não entregou)";
                case "order_kill": return "você recebeu a ordem de MATAR " + Name(t.Target) + (t.Job != null ? " (seu/sua " + t.Job + ")" : "") + " e está " + (Dist(npc, t.Target.current_tile) > 4f ? "indo até ele(a) (AINDA NÃO CHEGOU)" : "atacando-o(a)");
                case "goto": return "você está andando até " + t.Job + " por ordem recebida (AINDA NÃO CHEGOU)";
                case "raid": return t.Arrived ? "você está atacando a cidade " + t.Job + " sozinho(a), por ordem de Deus" : "você está indo sozinho(a) atacar a cidade " + t.Job + " por ordem de Deus (AINDA NÃO CHEGOU)";
            }
            return null;
        }

        // ------------------------------------------------------------------------------------------------
        // Deterministic shortcuts (before the classifier)
        // ------------------------------------------------------------------------------------------------
        static readonly Regex GuideRx = new Regex(@"\b(?:me |nos )?(?:leve|leva|levar|levem|guie|guia|guiar|conduza|mostre|mostra|leve-me|leva-me)(?: me| nos)? (?:ate|para|pra|pro|ao|a|onde (?:esta|fica|mora))\s+(?:a cidade de |a vila de |o |a |os |as )?([\p{L}'-]{2,}(?:\s+(?:d[aeo]s?\s+)?\p{L}[\p{L}'-]+)?)\s*[?!.]*$", RegexOptions.CultureInvariant);
        static readonly Regex WhereRx = new Regex(@"^\W*(?:e |mas )?(?:onde (?:esta|fica|mora|anda|vive)|cade|where is)\s+(?:a cidade de |a vila de |o |a |os |as )?([\p{L}'-]{2,}(?:\s+(?:d[aeo]s?\s+)?\p{L}[\p{L}'-]+)?)\s*[?!.]*$", RegexOptions.CultureInvariant);
        // v0.7.8: "marque pra mim onde fica X", "mostre no mapa X", "aponte onde fica X" -> where_is (mark), never guide_to
        static readonly Regex MarkRx = new Regex(@"\b(marque|marca|marcar|marcai|mostre|mostra|mostrar|aponte|aponta|apontar|indique|indica|indicar)(?:-me|-nos)?(?:\s+(?:pra mim|para mim|pra nos|para nos|me|nos|ai|aqui))?(?:\s+(?:no|num|em um) mapa)?(?:\s+(?:onde|aonde)\s+(?:fica|esta|mora|e)|\s+a localizacao d[aeo]|\s+o lugar d[aeo])?\s+(?:a cidade de |a vila de |o reino de |a |o |os |as )?([\p{L}'-]{2,}(?:\s+(?:d[aeo]s?\s+)?\p{L}[\p{L}'-]+){0,2}?)(?:\s+(?:no|num|em um) mapa)?(?:\s+(?:pra mim|para mim|por favor))?\s*[?!.]*$", RegexOptions.CultureInvariant);
        static readonly Regex StopFollowRx = new Regex(@"\b(pare|para|parar|deixe|deixa|chega) de me seguir\b|\bnao (me )?siga mais\b|\bpode parar de me seguir\b|\bstop following\b", RegexOptions.CultureInvariant);
        static readonly Regex[] RenameRx =
        {
            new Regex(@"(?:seu|teu)\s+(?:novo\s+)?nome\s+(?:agora\s+|de\s+agora\s+em\s+diante\s+)?(?:e|é|sera|será|vai\s+ser|passa\s+a\s+ser)\s+(.+)$", RegexOptions.IgnoreCase),
            new Regex(@"(?:mude|troque|mudar|trocar|altere|alterar)\s+(?:o\s+)?(?:seu|teu)\s+nome\s+(?:para|pra)\s+(.+)$", RegexOptions.IgnoreCase),
            new Regex(@"(?:a\s+partir\s+de\s+(?:hoje|agora)\s*,?\s*)?(?:voce|você|tu)\s+(?:se\s+chamara|se\s+chamará|vai\s+se\s+chamar|passa\s+a\s+se\s+chamar)\s+(.+)$", RegexOptions.IgnoreCase),
            new Regex(@"(?:eu\s+)?(?:te\s+)?(?:renomeio|batizo|nomeio)\s+(?:voce\s+|você\s+)?(?:como\s+|de\s+)?(.+)$", RegexOptions.IgnoreCase),
        };

        // v0.7.9: pronouns / question words in a target ("onde ele esta", "la", "essa cidade")
        static readonly Regex RefPronounRx = new Regex(@"^(ele|ela|eles|elas|dele|dela|deles|delas|la|ali|lá|esse|essa|este|esta|aquele|aquela|esse cara|essa pessoa|essa mulher|esse homem|esse lugar|essa cidade|essa vila|esse reino|o mesmo|a mesma|ele mesmo|ela mesma)$", RegexOptions.CultureInvariant);
        /// <summary>Strips question words ("onde", "está", "fica"...) from a target and resolves a pronoun to the last person or
        /// city named in this NPC's recent turns (player or NPC). Returns null for an unresolved pronoun, else the cleaned target.</summary>
        public static string ResolveRef(string target, Actor npc)
        {
            if (string.IsNullOrWhiteSpace(target)) return target;
            string t = Fold(target).Trim().Trim('?', '!', '.', ',');
            for (int i = 0; i < 3; i++)
            {
                t = Regex.Replace(t, @"^(onde|aonde|cade|em que lugar|que lugar)\s+(?:e\s+que\s+)?", "");
                t = Regex.Replace(t, @"^(esta|fica|mora|anda|vive|e)\s+", "");
                t = Regex.Replace(t, @"\s+(esta|fica|mora|anda|vive|se encontra|e|agora|neste momento)$", "");
                t = Regex.Replace(t, @"^(o|a)\s+(?=(mesmo|mesma)$)", "");
            }
            t = t.Trim();
            if (!RefPronounRx.IsMatch(t))
            {
                if (t != Fold(target).Trim().Trim('?', '!', '.', ',')) Main.LogInfo("[GodBox] target \"" + target + "\" cleaned to \"" + t + "\".");
                return t.Length > 0 ? t : null;
            }
            string found = LastMentioned(npc, t.StartsWith("essa cidade") || t.StartsWith("essa vila") || t == "la" || t == "lá" || t == "ali" || t == "esse lugar");
            Main.LogInfo("[GodBox] pronoun \"" + target + "\" -> " + (found ?? "(nothing recent to refer to)") + ".");
            return found;
        }

        /// <summary>The last unit or city name mentioned in the recent turns (newest first).</summary>
        static string LastMentioned(Actor npc, bool placeOnly)
        {
            try
            {
                var mem = npc != null ? MemoryStore.Get(npc) : null;
                if (mem?.Turns == null || mem.Turns.Count == 0) return null;
                string self = null; try { self = Fold(npc.getName()); } catch { }
                var cities = new List<string>();
                try { foreach (var c in World.world.cities) if (c != null && !string.IsNullOrEmpty(c.name)) cities.Add(c.name); } catch { }
                var units = new List<string>();
                if (!placeOnly) try { foreach (var u in World.world.units) { if (u == null || !u.isAlive()) continue; bool sap = false; try { sap = u.isSapient(); } catch { } if (!sap) continue; var n = u.getName(); if (!string.IsNullOrEmpty(n) && n.Length >= 3) units.Add(n); } } catch { }
                foreach (var turn in mem.Turns.AsEnumerable().Reverse().Take(8))
                {
                    string f = " " + Fold(Regex.Replace(turn.Text ?? "", @"\*[^*]*\*", " ")) + " ";
                    string best = null; int bestAt = -1;
                    void Try(string name)
                    {
                        string fn = Fold(name);
                        if (fn == self) return;
                        var m = Regex.Match(f, @"(?<![\p{L}])" + Regex.Escape(fn) + @"(?![\p{L}])", RegexOptions.RightToLeft);
                        if (m.Success && m.Index > bestAt) { bestAt = m.Index; best = name; }
                    }
                    foreach (var n in units) Try(n);
                    foreach (var n in cities) Try(n);
                    if (best != null) return best;
                }
            }
            catch { }
            return null;
        }

        /// <summary>Requests recognised without the LLM (rename with a capitalised name, stop following).</summary>
        public static WorldfallActions.Request Shortcut(string text, List<WorldfallActions.Available> avail, Actor npc = null)
        {
            if (string.IsNullOrWhiteSpace(text) || avail == null) return null;
            bool Has(string id) => avail.Any(a => a.Def.Id == id);
            if (Has("where_is"))
            {
                string fm = Fold(text.Replace("[", " ").Replace("]", " ")).Trim();
                var mk = MarkRx.Match(fm);
                if (mk.Success && !Regex.IsMatch(fm, @"\bcaminho\b|\bme (leve|leva|guie|guia|acompanhe|conduza)\b|\bvamos ate\b")
                    && (mk.Groups[1].Value.StartsWith("marc") || Regex.IsMatch(fm, @"\bmapa\b|\bonde\b|\blocalizacao\b")))
                {
                    string tg = ResolveRef(mk.Groups[2].Value.Trim(), npc);   // v0.7.9: "onde ele esta" -> the last person/city mentioned
                    if (tg != null && !Regex.IsMatch(tg, @"^(voce|vc|ele|ela|isso|aquilo|tudo|algo|me|nos)$"))
                    {
                        Main.LogInfo("[GodBox] mark shortcut: where_is (mark) -> " + tg);
                        return new WorldfallActions.Request { Id = "where_is", Target = tg, Style = "mark" };
                    }
                }
            }
            try { var mv = MoveShortcut(Fold(text), avail); if (mv != null) return mv; }   // v0.7.8: "vá até a cidade X" is walking, not attack_city
            catch (Exception e) { Main.LogWarning("[GodBox] move shortcut failed: " + e.Message); }
            try { var war = WarShortcut(Fold(text), text, npc, avail); if (war != null) return war; }   // v0.7.6
            catch (Exception e) { Main.LogWarning("[GodBox] war shortcut failed: " + e.Message); }
            if (Has("stop_follow") && StopFollowRx.IsMatch(Fold(text))) return new WorldfallActions.Request { Id = "stop_follow" };
            // v0.7.5: god voice "me leve até X" (no body to guide) and "onde está X" -> where_is, deterministic
            if (Has("where_is"))
            {
                string f = Fold(text);
                var g = Has("guide_to") ? Match.Empty : GuideRx.Match(f);
                if (g.Success && ResolveRef(g.Groups[1].Value.Trim(), npc) is string gt) return new WorldfallActions.Request { Id = "where_is", Target = gt, Style = "guide" };
                var w = WhereRx.Match(f);
                if (w.Success && ResolveRef(w.Groups[1].Value.Trim(), npc) is string wt) return new WorldfallActions.Request { Id = "where_is", Target = wt };
            }
            if (Has("rename"))
            {
                foreach (var rx in RenameRx)
                {
                    var m = rx.Match(text.Trim());
                    if (!m.Success) continue;
                    var nm = Regex.Match(m.Groups[1].Value, @"[""“']?\s*(\p{Lu}[\p{L}'-]*(?:\s+(?:d[aeo]s?\s+)?\p{Lu}[\p{L}'-]*){0,2})");
                    if (nm.Success) return new WorldfallActions.Request { Id = "rename", Target = nm.Groups[1].Value.Trim() };
                }
            }
            return null;
        }
    }
}
