using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace WorldfallAIChat
{
    /// <summary>
    /// v0.7.8: trait lists and categories for the god voice ("retiro todos os seus traços positivos", "retiro gigante e forte"),
    /// the pt-name -> trait id lookup (WorldBox's own pt locale + synonyms) used BEFORE the classifier, taking the weapon /
    /// the equipment (take_item), and the player's god-power intent (for the "no fake effect" check).
    /// Good/bad comes from the game data (ActorTrait.type Positive/Negative), with a small curated list for "Other" traits.
    /// </summary>
    public static partial class GodPowers
    {
        static readonly HashSet<string> GoodExtra = new HashSet<string>
            { "blessed", "long_liver", "fire_proof", "freeze_proof", "poison_immune", "battle_reflexes", "dodge", "light_lamp", "miracle_born",
              "chosen_one", "kingslayer", "flower_prints", "voices_of_wisdom", "nightchild", "acid_proof", "bubble_defense", "shiny", "energized" };
        static readonly HashSet<string> BadExtra = new HashSet<string>
            { "evil", "madness", "plague", "infected", "tumor_infection", "mush_spores", "zombie", "voices_in_my_head", "clumsy", "desire_harp",
              "heliophobia", "scar_of_divinity", "cursed", "death_mark", "hunger", "infertile", "crippled", "eyepatch", "skin_burns", "short_sighted" };

        public static bool IsGood(ActorTrait t)
        {
            try { if (t.type == TraitType.Positive) return true; if (t.type == TraitType.Negative) return false; return GoodExtra.Contains(t.id); } catch { return false; }
        }
        public static bool IsBad(ActorTrait t)
        {
            try { if (t.type == TraitType.Negative) return true; if (t.type == TraitType.Positive) return false; return BadExtra.Contains(t.id); } catch { return false; }
        }

        public static string TraitPt(ActorTrait t)
        {
            string s = null; try { s = GameLocalePt.Trait(t); } catch { }
            try { return string.IsNullOrWhiteSpace(s) ? t.id : s; } catch { return "?"; }
        }
        static string TraitPt(string id) { try { var t = AssetManager.traits.get(id); return t != null ? TraitPt(t) : id; } catch { return id; } }

        // ---------- pt name -> id index ----------
        static Dictionary<string, string> _index;
        static void Key(Dictionary<string, string> d, string name, string id)
        {
            string k = NativeActions.Fold(name ?? "").Replace('_', ' ').Trim();
            k = Regex.Replace(k, @"\(.*?\)", "").Trim();
            if (k.Length < 3 || d.ContainsKey(k)) return;
            d[k] = id;
            if (Regex.IsMatch(k, @"o$")) { var f = k.Substring(0, k.Length - 1) + "a"; if (!d.ContainsKey(f)) d[f] = id; }   // sábio/sábia
            if (!k.EndsWith("s") && !d.ContainsKey(k + "s")) d[k + "s"] = id;
        }
        static Dictionary<string, string> Index()
        {
            if (_index != null) return _index;
            var d = new Dictionary<string, string>();
            try
            {
                foreach (var t in AssetManager.traits.list)
                {
                    if (t == null || string.IsNullOrEmpty(t.id)) continue;
                    Key(d, t.id, t.id);
                    try { Key(d, GameLocalePt.Trait(t), t.id); } catch { }
                    try { Key(d, t.getTranslatedName(), t.id); } catch { }
                }
                foreach (var (rx, id, pt) in TraitWords) foreach (var part in pt.Split('/')) Key(d, part, id);
                _index = d;
            }
            catch (Exception e) { Main.LogWarning("[GodBox] trait index failed: " + e.Message); return d; }
            return _index;
        }

        /// <summary>"positive" / "negative" / "all" / null from the player's words.</summary>
        public static string TraitCategory(string text)
        {
            string f = NativeActions.Fold(text ?? "");
            if (Regex.IsMatch(f, @"\b(positiv\w*|bons|boas|virtudes|qualidades|dons|good)\b")) return "positive";
            if (Regex.IsMatch(f, @"\b(negativ\w*|ruins|maus|defeitos|bad)\b")) return "negative";
            if (Regex.IsMatch(f, @"\b(todos|todas|tudo|all)\b") && Regex.IsMatch(f, @"\b(traco|tracos|caracteristicas?|traits?|todos|todas|tudo|all)\b") && !Regex.IsMatch(f, @"\b(arma|armas|equipamento|itens|objetos)\b")) return "all";
            return null;
        }

        static readonly Regex Stop = new Regex(@"\b(o|a|os|as|um|uma|seu|sua|seus|suas|teu|tua|teus|tuas|de|do|da|dos|das|voce|ti|te|lhe|agora|tambem|traco|tracos|caracteristica|caracteristicas|trait|eu|que|ser|mais|e|seja|sera|ficar|fique|o traco de|de voce)\b", RegexOptions.CultureInvariant);

        /// <summary>One trait from one phrase ("gigante", "o traço sábio"): exact pt/en/id name first, then the synonym table.</summary>
        public static string ResolveTrait(string phrase, out string pt)
        {
            pt = null;
            string f = NativeActions.Fold(phrase ?? "").Trim(' ', '.', '!', '?', '"', '\'');
            if (f.Length == 0) return null;
            var idx = Index();
            string bare = Regex.Replace(Stop.Replace(f, " "), @"\s+", " ").Trim();
            foreach (var k in new[] { f, bare })
                if (k.Length > 0 && idx.TryGetValue(k, out var id)) { pt = TraitPt(id); return id; }
            if (bare.Length == 0) return null;
            foreach (var (rx, id, p) in TraitWords)
                if (Regex.IsMatch(bare, rx)) { pt = TraitPt(id); return id; }
            foreach (var w in bare.Split(' '))
                if (w.Length >= 4 && idx.TryGetValue(w, out var id2)) { pt = TraitPt(id2); return id2; }
            return null;
        }

        static List<string> Parts(string target, List<string> extra)
        {
            var parts = new List<string>();
            foreach (var p in Regex.Split(NativeActions.Fold(target ?? ""), @",|;|/|\be\b|\band\b")) if (p.Trim().Length > 0) parts.Add(p.Trim());
            if (extra != null) parts.AddRange(extra.Where(x => !string.IsNullOrWhiteSpace(x)));
            return parts;
        }

        static string TraitList(Actor npc)
        {
            try { var l = npc.traits.Select(TraitPt).Take(12).ToList(); return l.Count == 0 ? "nenhum" : string.Join(", ", l); } catch { return "?"; }
        }

        static void RemoveTraits(Actor npc, WorldfallActions.Request req, DialogueConfig cfg, WorldfallActions.Result res)
        {
            string target = req.Target ?? "";
            string cat = TraitCategory(target);
            var todo = new List<ActorTrait>();
            var notes = new List<string>();
            if (cat != null)
            {
                foreach (var t in npc.traits.ToList())
                    if (cat == "all" || (cat == "positive" && IsGood(t)) || (cat == "negative" && IsBad(t))) todo.Add(t);
                string cw = cat == "positive" ? "positivo" : cat == "negative" ? "negativo" : "";
                if (todo.Count == 0)
                {
                    res.Outcome = "sem_efeito";
                    res.Reason = "não tem nenhum traço " + (cw.Length > 0 ? cw : "") + " (seus traços: " + TraitList(npc) + ")";
                    return;
                }
            }
            else
            {
                foreach (var p in Parts(target, req.Targets))
                {
                    string id = ResolveTrait(p, out string pt);
                    if (id == null) { notes.Add("traço desconhecido: \"" + p + "\""); continue; }
                    if (!Has(npc, id)) { notes.Add("não tem o traço " + pt); continue; }
                    var t = AssetManager.traits.get(id);
                    if (t != null && !todo.Contains(t)) todo.Add(t);
                }
                if (todo.Count == 0)
                {
                    res.Outcome = notes.Any(n => n.StartsWith("não tem")) ? "sem_efeito" : "falhou";
                    res.Reason = (notes.Count > 0 ? string.Join("; ", notes) : "não disse qual traço") + " (seus traços: " + TraitList(npc) + ")";
                    return;
                }
                if (todo.Count == 1 && MindTraits.Contains(todo[0].id) && Defies(npc, cfg, res, "a alma resistiu: o traço " + TraitPt(todo[0]) + " continua")) return;
            }
            var gone = new List<string>(); var stuck = new List<string>();
            foreach (var t in todo)
            {
                try { npc.removeTrait(t); } catch { }
                if (Has(npc, t.id)) stuck.Add(TraitPt(t)); else gone.Add(TraitPt(t) + " (" + t.id + ")");
            }
            try { npc.setStatsDirty(); } catch { }
            if (gone.Count == 0) { res.Outcome = "falhou"; res.Reason = "o jogo não deixou remover: " + string.Join(", ", stuck); return; }
            res.Outcome = "feito";
            res.Changes.Add("perdeu " + gone.Count + " traço(s)" + (cat == "positive" ? " positivo(s)" : cat == "negative" ? " negativo(s)" : "") + ": " + string.Join(", ", gone));
            if (stuck.Count > 0) res.Changes.Add("NÃO saíram: " + string.Join(", ", stuck));
            foreach (var n in notes) res.Changes.Add(n);
            res.Changes.Add("traços que restam: " + TraitList(npc));
        }

        static void AddTraits(Actor npc, WorldfallActions.Request req, DialogueConfig cfg, WorldfallActions.Result res)
        {
            var got = new List<string>(); var notes = new List<string>();
            foreach (var p in Parts(req.Target, req.Targets))
            {
                string id = ResolveTrait(p, out string pt);
                if (id == null) { notes.Add("traço desconhecido: \"" + p + "\""); continue; }
                if (Has(npc, id)) { notes.Add("já tem o traço " + pt); continue; }
                bool ok = false; try { ok = npc.addTrait(id, true) || Has(npc, id); } catch { }
                if (ok && Has(npc, id)) got.Add(pt + " (" + id + ")"); else notes.Add("o jogo não permite o traço " + pt + " para esta criatura");
            }
            try { npc.setStatsDirty(); } catch { }
            if (got.Count == 0)
            {
                res.Outcome = notes.Any(n => n.StartsWith("já tem")) ? "sem_efeito" : "falhou";
                res.Reason = notes.Count > 0 ? string.Join("; ", notes) : "não disse qual traço";
                return;
            }
            res.Outcome = "feito";
            res.Changes.Add("ganhou o(s) traço(s): " + string.Join(", ", got));
            foreach (var n in notes) res.Changes.Add(n);
        }

        // ---------- take the weapon / equipment ----------
        static readonly Dictionary<string, string> TypePt = new Dictionary<string, string>
        {
            { "sword", "espada" }, { "axe", "machado" }, { "hammer", "martelo" }, { "spear", "lança" }, { "bow", "arco" }, { "stick", "cajado" },
            { "staff", "cajado" }, { "armor", "armadura" }, { "helmet", "capacete" }, { "boots", "botas" }, { "ring", "anel" }, { "amulet", "amuleto" },
        };
        static readonly Dictionary<string, string> SpecialItemPt = new Dictionary<string, string>
        {
            { "flame_sword", "espada flamejante" }, { "flame_hammer", "martelo flamejante" }, { "ice_hammer", "martelo de gelo" },
            { "evil_staff", "cajado maligno" }, { "white_staff", "cajado branco" }, { "druid_staff", "cajado de druida" },
            { "necromancer_staff", "cajado de necromante" }, { "plague_doctor_staff", "cajado de médico da peste" },
            { "alien_blaster", "pistola alienígena" }, { "shotgun", "espingarda" }, { "rocks", "pedras" }, { "snowball", "bola de neve" },
            { "stick_wood", "cajado de madeira" },
        };
        /// <summary>v0.8.0: pt-BR name of an equipment asset id ("spear_iron" -> "lança de ferro"); never the raw id.</summary>
        public static string ItemNamePt(string id)
        {
            if (string.IsNullOrEmpty(id)) return "item";
            if (SpecialItemPt.TryGetValue(id, out var sp)) return sp;
            string type = id.Split('_')[0];
            string mat = id.Contains("_") ? id.Substring(id.IndexOf('_') + 1) : null;
            string tp = TypePt.TryGetValue(type, out var x) ? x : null;
            if (tp == null) foreach (var kv in TypePt) if (id.Contains(kv.Key)) { tp = kv.Value; break; }
            string mp = mat != null ? Materials.FirstOrDefault(m => m.mat == mat).pt : null;
            if (tp == null) return "item";
            return tp + (mp != null ? " de " + mp : "");
        }
        static string ItemPt(Item it)
        {
            string id = null; try { id = it.getAsset().id; } catch { }
            return ItemNamePt(id);
        }

        static void TakeItem(Actor npc, string target, WorldfallActions.Result res)
        {
            string f = NativeActions.Fold(target ?? "");
            bool all = Regex.IsMatch(f, @"\b(tudo|todos|todas|equipamento|equipamentos|itens|objetos|pertences|all|everything)\b");
            var slots = new List<ActorEquipmentSlot>();
            if (all) { foreach (var s in npc.equipment) slots.Add(s); }
            else
            {
                string type = ItemTypes.FirstOrDefault(t => Regex.IsMatch(f, t.rx)).type;
                EquipmentType et = EquipmentType.Weapon;
                if (type == "armor") et = EquipmentType.Armor; else if (type == "helmet") et = EquipmentType.Helmet; else if (type == "boots") et = EquipmentType.Boots;
                else if (type == "ring") et = EquipmentType.Ring; else if (type == "amulet") et = EquipmentType.Amulet;
                slots.Add(npc.equipment.getSlot(et));
            }
            var taken = new List<string>();
            foreach (var s in slots)
            {
                if (s == null || s.isEmpty()) continue;
                var it = s.getItem();
                string name = ItemPt(it);
                s.takeAwayItem();   // same call as the game's equipment editor
                try { World.world.items.removeObject(it); } catch { }   // destroyed: nothing is given back
                taken.Add(name);
            }
            try { npc.setStatsDirty(); } catch { }
            if (taken.Count == 0)
            {
                res.Outcome = "sem_efeito";
                res.Reason = all ? "não usa nenhum equipamento" : "não tem " + (Regex.IsMatch(f, @"armadur|capacete|elmo|bota|anel|amulet|colar") ? "esse item equipado" : "nenhuma arma equipada (luta de mãos vazias ou com magia própria)");
                return;
            }
            res.Outcome = "feito";
            res.Changes.Add("perdeu (destruído pelos deuses, nada foi devolvido): " + string.Join(", ", taken));
            Main.LogInfo("[GodBox] take_item: " + Name(npc) + " lost " + string.Join(", ", taken) + ".");
        }

        // ---------- deterministic god-voice shortcuts (before the classifier) ----------
        static readonly Regex TakeRx = new Regex(@"\b(retiro|tiro|tomo|confisco|removo|remova|retire|tire|tomarei|retirarei|tirarei|pego|arranco|desarmo|quebro|destruo)\b[^.?!]*?\b(arma|armas|espada|machado|lanca|arco|cajado|martelo|equipamento|equipamentos|itens|objetos|pertences|armadura|capacete|elmo|botas|anel|amuleto)\b", RegexOptions.CultureInvariant);
        static readonly Regex DisarmRx = new Regex(@"\b(te desarmo|desarmo voce|fique desarmad[oa]|sem arma)\b", RegexOptions.CultureInvariant);
        static readonly Regex RemoveRx = new Regex(@"\b(retiro|tiro|removo|remova|remove|retire|tire|arranco|apago|elimino|tirarei|retirarei|removerei|tomo)\b(?:\s+(?:de voce|de ti|agora|tambem|eu))*\s+(.+)$", RegexOptions.CultureInvariant);
        static readonly Regex AddRx = new Regex(@"\b(dou|concedo|outorgo|darei|concederei|te dou|lhe dou|lhe concedo|te concedo)\b(?:\s+(?:a voce|a ti|agora|tambem))*\s+(.+)$", RegexOptions.CultureInvariant);

        public static WorldfallActions.Request Shortcut(string text, List<WorldfallActions.Available> avail)
        {
            if (string.IsNullOrWhiteSpace(text) || avail == null) return null;
            bool Has_(string id) => avail.Any(a => a.Def.Id == id);
            string f = NativeActions.Fold(text);
            if (f.TrimEnd().EndsWith("?")) return null;
            WorldfallActions.Request R(string id, string t) => new WorldfallActions.Request { Id = id, Target = t, Message = text, Confidence = 1f };
            if (Has_("take_item"))
            {
                var m = TakeRx.Match(f);
                if (m.Success)
                {
                    bool all = Regex.IsMatch(f, @"\b(tudo|todos|todas|equipamento|equipamentos|itens|objetos|pertences)\b");
                    return R("take_item", all ? "tudo" : m.Groups[2].Value);
                }
                if (DisarmRx.IsMatch(f)) return R("take_item", "arma");
            }
            if (Has_("remove_trait"))
            {
                var m = RemoveRx.Match(f);
                if (m.Success)
                {
                    string rest = m.Groups[2].Value.Trim(' ', '.', '!');
                    if (Regex.IsMatch(rest, @"maldi|amaldi|praga")) return Has_("lift_curse") ? R("lift_curse", "") : null;
                    if (TraitCategory(rest) != null) return R("remove_trait", rest);
                    var ids = Parts(rest, null).Select(p => ResolveTrait(p, out _)).Where(x => x != null).ToList();
                    if (ids.Count > 0) return R("remove_trait", rest);
                }
            }
            if (Has_("add_trait"))
            {
                var m = AddRx.Match(f);
                if (m.Success)
                {
                    string rest = m.Groups[2].Value.Trim(' ', '.', '!');
                    bool item = ItemTypes.Any(t => Regex.IsMatch(rest, t.rx));
                    if (!item && !Regex.IsMatch(rest, @"\b(poder|forca divina|bencao|maldicao|vida|morte|missao|ordem)\b"))
                    {
                        var ids = Parts(rest, null).Select(p => ResolveTrait(p, out _)).Where(x => x != null).ToList();
                        if (ids.Count > 0 && Parts(rest, null).All(p => p.Split(' ').Length <= 4)) return R("add_trait", rest);
                    }
                }
            }
            return null;
        }

        // ---------- bless only on bless words ----------
        static readonly Regex BlessRx = new Regex(@"\b(abenco\w*|abencoad\w*|bencao|bencaos|bendit\w*|bendigo|bless\w*)\b", RegexOptions.CultureInvariant);
        public static bool TakeWords(string text) { string f = NativeActions.Fold(text ?? ""); return TakeRx.IsMatch(f) || DisarmRx.IsMatch(f); }
        public static bool BlessWords(string text) => BlessRx.IsMatch(NativeActions.Fold(text ?? ""));

        // ---------- the god power the player COMMANDED (for the no-fake-effect check) ----------
        public class Intent { public string Id, Pt, What; }

        static readonly (string id, Regex rx, string pt)[] IntentRx =
        {
            ("take_item", TakeRx, "tomar a arma/equipamento"),
            ("teleport", new Regex(@"\b(teleport\w*|apareca|aparecer|surja|materialize-se)\b", RegexOptions.CultureInvariant), "teletransportar"),
            ("go_to", new Regex(@"\b(va|vai|ande|caminhe|siga|dirija-se|parta|ir|corra|marche)\b\s+(ate|para|pra|pro|ao|a)\b", RegexOptions.CultureInvariant), "ir até um lugar"),
            ("lift_curse", new Regex(@"\b(retiro|tiro|quebro|removo|desfaco|levanto)\b[^.?!]*\bmaldi", RegexOptions.CultureInvariant), "tirar a maldição"),
            ("remove_trait", new Regex(@"\b(retiro|tiro|removo|remova|retire|tire|arranco|apago)\b", RegexOptions.CultureInvariant), "tirar traço(s)"),
            ("curse", new Regex(@"\b(amaldicoo|maldito seja|te amaldico)\b", RegexOptions.CultureInvariant), "amaldiçoar"),
            ("bless", BlessRx, "abençoar"),
            ("heal", new Regex(@"\b(eu te curo|fique curad[oa]|te curo|curo voce)\b", RegexOptions.CultureInvariant), "curar"),
            ("add_trait", new Regex(@"\b(concedo|outorgo|te dou o traco|te faco)\b", RegexOptions.CultureInvariant), "dar traço"),
        };

        /// <summary>God voice only: the power the player clearly commanded, or null.</summary>
        public static Intent CommandedIntent(string text)
        {
            string f = NativeActions.Fold(text ?? "");
            if (f.Length == 0 || f.TrimEnd().EndsWith("?")) return null;
            foreach (var (id, rx, pt) in IntentRx)
            {
                var m = rx.Match(f);
                if (m.Success) return new Intent { Id = id, Pt = pt, What = text.Trim() };
            }
            return null;
        }

        /// <summary>Does the executed action match what the player commanded?</summary>
        public static bool Matches(Intent i, string ranId)
        {
            if (i == null || ranId == null) return false;
            if (i.Id == ranId) return true;
            if (i.Id == "remove_trait" && (ranId == "take_item" || ranId == "lift_curse")) return ranId == "take_item";
            if (i.Id == "go_to" && (ranId == "teleport" || ranId == "attack_city" || ranId == "summon" || ranId == "guide_to" || ranId == "where_is")) return ranId == "teleport";
            if (i.Id == "teleport" && ranId == "go_to") return false;
            return false;
        }
    }
}
