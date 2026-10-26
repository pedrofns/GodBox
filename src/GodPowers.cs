using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace WorldfallAIChat
{
    /// <summary>
    /// v7: god-voice-only powers (talking with no possessed unit): add / remove a trait, bless, give a weapon or armour,
    /// heal, kill (smite). Done with WorldBox's public API (addTrait/removeTrait, ItemManager.generateItem +
    /// ActorEquipment.setItem like the game's own equipment editor, restoreHealth, die). Defiance (trait-based chance):
    /// only removing a personality trait ("the soul resists") and wielding a gift can be defied; blessings, curses, healing
    /// and death cannot. Killing always asks for confirmation first.
    /// </summary>
    public static partial class GodPowers
    {
        public static readonly HashSet<string> Ids = new HashSet<string> { "add_trait", "remove_trait", "bless", "give_item", "heal", "kill", "make_prophet", "preach", "appoint_prophet", "spread_religion", "curse", "lift_curse", "take_item" };

        static WorldfallActions.ActionDef D(string id, string pt, string en, string param) => new WorldfallActions.ActionDef(id, pt, en, param);

        public static readonly Dictionary<string, WorldfallActions.ActionDef> Defs = new Dictionary<string, WorldfallActions.ActionDef>
        {
            { "add_trait", D("add_trait", "(poder divino) DAR um traço/característica ao personagem, ou AMALDIÇOÁ-LO", "give a trait", "alvo = traço (ex.: sábio, forte, sortudo)") },
            { "curse", D("curse", "(poder divino) AMALDIÇOAR o personagem (\"eu te amaldiçoo\", \"maldito seja\")", "curse", "") },
            { "lift_curse", D("lift_curse", "(poder divino) TIRAR/QUEBRAR a maldição do personagem", "lift the curse", "") },
            { "remove_trait", D("remove_trait", "(poder divino) TIRAR/REMOVER um traço do personagem", "remove a trait", "alvo = traço(s) como dito (ex.: traiçoeiro; \"gigante, forte\"; ou \"positivos\", \"negativos\", \"todos\")") },
            { "bless", D("bless", "(poder divino) ABENÇOAR o personagem (só com palavras de bênção: \"eu te abençoo\", \"receba minha bênção\")", "bless", "") },
            { "take_item", D("take_item", "(poder divino) TOMAR/RETIRAR a arma do personagem (ou todo o equipamento); nada é devolvido", "take the weapon / equipment", "alvo = \"arma\", o item dito, ou \"tudo\" para todo o equipamento") },
            { "give_item", D("give_item", "(poder divino) DAR/CRIAR uma arma ou armadura para o personagem", "give a weapon/armour", "alvo = item e material como dito (ex.: espada de ferro, arco, lança de madeira)") },
            { "heal", D("heal", "(poder divino) CURAR o personagem", "heal", "") },
            { "make_prophet", D("make_prophet", "(poder divino) tornar o personagem PROFETA/sacerdote do deus (\"você será meu profeta\")", "make prophet", "alvo = nome da religião se o jogador disser, senão \"\"") },
            { "preach", D("preach", "(poder divino) mandar o personagem PREGAR/ESPALHAR a religião (converter gente perto, a cidade ou o reino)", "preach / spread the religion", "alvo = \"perto\", \"cidade\" ou \"reino\" conforme o pedido") },
            { "appoint_prophet", D("appoint_prophet", "(poder divino) fazer de OUTRA pessoa (dita pelo nome) o profeta de Deus (\"faça de X meu profeta\")", "appoint someone else as prophet", "alvo = nome da pessoa") },
            { "spread_religion", D("spread_religion", "(poder divino) mandar este profeta/fiel ANDAR até uma cidade e pregar a religião de Deus lá (\"espalhe minha religião em X\")", "send to spread the religion in a city", "alvo = nome da cidade, ou \"\" para as cidades próximas") },
            { "kill", D("kill", "(poder divino) MATAR / fulminar o PRÓPRIO ouvinte (morra!); para mandar matar OUTRA pessoa use order_kill", "kill / smite", "alvo = \"raio\" se pedir com raio/relâmpago") },
        };

        static bool Has(Actor a, string t) { try { return a.hasTrait(t); } catch { return false; } }

        public static List<WorldfallActions.Available> AvailableFor(Actor npc, Actor you, DialogueConfig cfg)
        {
            var list = new List<WorldfallActions.Available>();
            if (you != null || cfg == null || !cfg.NativeActionsEnabled || !cfg.ActionGodPowers) return list;
            foreach (var id in new[] { "add_trait", "remove_trait", "bless", "curse", "lift_curse", "give_item", "take_item", "heal" })
                list.Add(new WorldfallActions.Available { Def = Defs[id] });
            if (cfg.ActionReligion)
            {
                bool smart = false; try { smart = npc.subspecies != null && npc.subspecies.has_advanced_memory; } catch { }
                list.Add(new WorldfallActions.Available { Def = Defs["make_prophet"], Enabled = smart, Note = smart ? null : "esta espécie não tem memória avançada: não pode ter religião" });
                bool can = smart && npc.hasReligion();
                list.Add(new WorldfallActions.Available { Def = Defs["preach"], Enabled = can,
                    Note = can ? null : !smart ? "esta espécie não pode ter religião" : "ainda não tem religião: primeiro torne-o profeta" });
                list.Add(new WorldfallActions.Available { Def = Defs["appoint_prophet"] });
                bool follower = false; try { var gr = GodReligion(); follower = gr != null && npc.religion == gr; } catch { }
                list.Add(new WorldfallActions.Available { Def = Defs["spread_religion"], Enabled = follower,
                    Note = follower ? null : "não segue a religião de Deus: primeiro torne-o profeta" });
            }
            if (cfg.ActionKill) list.Add(new WorldfallActions.Available { Def = Defs["kill"] });
            return list;
        }

        // ------------------------------------------------------------------------------------------------
        // Traits (pt -> id). Order matters: longer / more specific first.
        // ------------------------------------------------------------------------------------------------
        static readonly (string rx, string id, string pt)[] TraitWords =
        {
            (@"mente forte|determinad|teimos|obstinad|strong.?minded", "strong_minded", "mente forte"),
            (@"traid|traic|mentiros|enganad|desonest|falso|falsidade|deceit", "deceitful", "traiçoeiro"),
            (@"desonest", "deceitful", "traiçoeiro"),
            (@"abencoad|bencao|blessed", "blessed", "abençoado"),
            (@"honest|sincer", "honest", "honesto"),
            (@"ganan|avar|cobic|greedy", "greedy", "ganancioso"),
            (@"psicopat|psycho", "psychopath", "psicopata"),
            (@"malvad|maligno|perverso|cruel|crueldade|\bmau\b|\bma\b|maldade|evil", "evil", "cruel/malvado"),
            (@"pacifist", "pacifist", "pacifista"),
            (@"pacific|paz\b|peaceful", "peaceful", "pacífico"),
            (@"ambic|ambit", "ambitious", "ambicioso"),
            (@"paranoi", "paranoid", "paranoico"),
            (@"esquentad|cabeca quente|irritad|raivos|explosiv|hothead", "hotheaded", "esquentado"),
            (@"estupid|burr|\btolo|idiota|stupid", "stupid", "estúpido"),
            (@"genio|genial|inteligen|genius", "genius", "gênio"),
            (@"sabi|sabedoria|wise", "wise", "sábio"),
            (@"content|satisfeit", "content", "contente"),
            (@"ladra|ladrao|thief", "thief", "ladrão"),
            (@"azar|unlucky", "unlucky", "azarado"),
            (@"sort|lucky", "lucky", "sortudo"),
            (@"imortal|immortal", "immortal", "imortal"),
            (@"regenera", "regeneration", "regeneração"),
            (@"fraco|fraqueza|weak", "weak", "fraco"),
            (@"forte|forca|strong", "strong", "forte"),
            (@"rapid|veloz|fast", "fast", "rápido"),
            (@"lent|slow", "slow", "lento"),
            (@"feio|feiura|ugly", "ugly", "feio"),
            (@"bonit|atraente|lind|belo|beleza|attractive", "attractive", "atraente"),
            (@"gord|fat\b", "fat", "gordo"),
            (@"gigant|giant", "giant", "gigante"),
            (@"pequen|minuscul|tiny", "tiny", "minúsculo"),
            (@"louc|insan|madness", "madness", "louco"),
            (@"veteran", "veteran", "veterano"),
            (@"infertil|esteril", "infertile", "infértil"),
            (@"fertil", "fertile", "fértil"),
            (@"imun", "immune", "imune"),
            (@"resistent|duro|tough", "tough", "resistente"),
            (@"praga|peste|plague", "plague", "peste"),
            (@"infect", "infected", "infectado"),
            (@"zumbi|zombie", "zombie", "zumbi"),
            (@"escolhid|chosen", "chosen_one", "escolhido"),
            (@"sanguinar|sede de sangue|bloodlust", "bloodlust", "sanguinário"),
            (@"agil|agilidade", "agile", "ágil"),
            (@"olho de aguia|visao agucada|eagle", "eagle_eyed", "olhos de águia"),
            (@"longev|vida longa", "long_liver", "longevo"),
            (@"guloso|gula", "gluttonous", "guloso"),
            (@"selvag|savage", "savage", "selvagem"),
        };

        static readonly HashSet<string> MindTraits = new HashSet<string>
            { "deceitful", "greedy", "evil", "psychopath", "ambitious", "paranoid", "hotheaded", "strong_minded", "honest", "content", "peaceful", "pacifist", "thief", "bloodlust", "madness" };


        // ------------------------------------------------------------------------------------------------
        // Items (pt -> equipment asset id)
        // ------------------------------------------------------------------------------------------------
        static readonly (string rx, string type)[] ItemTypes =
        {
            (@"espad|sword|lamina", "sword"), (@"machad|axe", "axe"), (@"martel|marreta|hammer", "hammer"), (@"lanc|spear|pique", "spear"),
            (@"\barco\b|\bbow\b", "bow"), (@"cajad|bastao|vara|graveto|stick", "stick"), (@"armadur|couraca|armor", "armor"),
            (@"capacete|elmo|helmet", "helmet"), (@"bota|boots", "boots"), (@"\banel\b|ring", "ring"), (@"amulet|colar|pingente|amulet", "amulet"),
        };
        static readonly (string rx, string mat, string pt)[] Materials =
        {
            (@"madeira|wood", "wood", "madeira"), (@"pedra|stone", "stone", "pedra"), (@"cobre|copper", "copper", "cobre"), (@"bronze", "bronze", "bronze"),
            (@"prata|silver", "silver", "prata"), (@"\baco\b|steel", "steel", "aço"), (@"ferro|iron", "iron", "ferro"), (@"mitril|mithril|mythril", "mythril", "mitril"),
            (@"adamant", "adamantine", "adamantina"), (@"couro|leather", "leather", "couro"), (@"osso|bone", "bone", "osso"),
            (@"\bouro\b|dourad|gold", "gold", "ouro"),
        };

        static EquipmentAsset Item(string id) { try { return AssetManager.items.dict.TryGetValue(id, out var a) ? a : null; } catch { return null; } }

        /// <summary>Resolve "espada de ouro" etc. Returns null with a reason when it does not exist in this world.</summary>
        public static EquipmentAsset ItemFor(string text, out string why)
        {
            why = null;
            string f = NativeActions.Fold(text);
            string type = ItemTypes.FirstOrDefault(t => Regex.IsMatch(f, t.rx)).type;
            var mt = Materials.FirstOrDefault(m => Regex.IsMatch(f, m.rx));
            if (type == null) { why = "não entendi qual item (há: espada, machado, martelo, lança, arco, cajado, armadura, capacete, botas, anel, amuleto)"; return null; }
            var order = new List<string>();
            if (mt.mat != null) order.Add(type + "_" + mt.mat);
            else order.AddRange(new[] { type + "_iron", type + "_wood", type + "_leather", type + "_copper", type + "_bone", type });
            foreach (var id in order) { var a = Item(id); if (a != null) return a; }
            var mats = Materials.Where(m => Item(type + "_" + m.mat) != null).Select(m => m.pt).ToList();
            why = mt.mat != null
                ? "não existe " + (TypePt.TryGetValue(type, out var tpt) ? tpt : "esse item") + " de " + mt.pt + " neste mundo" + (mats.Count > 0 ? " (materiais que existem: " + string.Join(", ", mats) + ")" : "")
                : "esse item não existe neste mundo";
            return null;
        }

        // ------------------------------------------------------------------------------------------------
        // Execute
        // ------------------------------------------------------------------------------------------------
        static readonly System.Random Rng = new System.Random();
        static string Name(Actor a) { try { return a.getName(); } catch { return "?"; } }

        /// <summary>v0.7.7: why the game would refuse a status (mirrors BaseSimObject.addStatusEffect / canAddStatus). null = allowed.</summary>
        public static string StatusBlock(Actor a, string statusId)
        {
            try
            {
                var st = AssetManager.status.get(statusId);
                if (st == null) return "o estado '" + statusId + "' não existe nesta versão";
                if (!a.isAlive()) return "já morreu";
                if (a.asset.allowed_status_tiers < st.tier) return "a espécie " + (FactsBuilder.Species(a) ?? a.asset.id) + " é imune a estados desse nível no jogo";
                if (st.opposite_traits != null)
                    foreach (var t in st.opposite_traits)
                        if (a.hasTrait(t))
                        {
                            string pt = null; try { pt = GameLocalePt.Trait(AssetManager.traits.get(t)); } catch { }
                            return "no jogo, quem tem o traço " + (pt ?? t) + " (" + t + ") é imune ao estado amaldiçoado" + (Alignment.Why(a) is string w ? " (" + w + ")" : "");
                        }
                if (st.opposite_tags != null && a.stats.hasTags() && a.stats.hasTags(st.opposite_tags)) return "um efeito atual (tag) impede esse estado";
                if (st.opposite_status != null)
                    foreach (var o in st.opposite_status) if (a.hasStatus(o)) return "o estado '" + o + "' impede esse";
            }
            catch { }
            return null;
        }

        static bool Defies(Actor npc, DialogueConfig cfg, WorldfallActions.Result res, string what)
        {
            var nat = Alignment.Of(npc);   // v0.7.7
            if (nat == Nature.Holy) return false;
            if (nat == Nature.Malefic && (what.Contains("profeta") || what.Contains("pregar")))
            {
                res.Outcome = "recusado";
                res.Reason = what + " (é um ser maléfico: jamais serve a Deus)";
                try { npc.addStatusEffect("angry", 0f, true); } catch { }
                return true;
            }
            float p = NativeActions.DefianceChance(npc, cfg, out string because);
            double roll = Rng.NextDouble();
            Main.LogInfo("[GodBox] OBEDIENCE roll [" + npc.getName() + "] " + what + ": p(defy)=" + p.ToString("0.00") + " roll=" + roll.ToString("0.00") + " traits=" + (because ?? "nenhum rebelde") + " -> " + (roll < p ? "REFUSES" : "obeys"));
            if (roll >= p) return false;
            res.Outcome = "recusado";
            res.Reason = what + (because != null ? " (por ser " + because.Replace(", ", " e ") + ", não quer; decisão própria)" : "");
            try { npc.addStatusEffect("angry", 0f, true); } catch { }
            return true;
        }

        // ------------------------------------------------------------------------------------------------
        // Religion (WorldBox 0.51: Actor.religion / setReligion / tryToConvertToReligion, ReligionManager.newReligion,
        // City.setReligion, Kingdom.setReligion). There is no "prophet" trait or role in the game: a prophet here is the
        // god-chosen founder/carrier of the religion (blessed + inspired) and is remembered in our memory files.
        // ------------------------------------------------------------------------------------------------
        public static Religion CurrentReligion() => GodReligion();   // v0.7.9
        static Religion GodReligion()
        {
            var g = MemoryStore.God();
            if (g.ReligionId < 0) return null;
            try { var r = World.world.religions.get(g.ReligionId); if (r != null && r.isAlive()) return r; } catch { }
            return null;
        }

        static string RelName(Religion r) { try { return r.name; } catch { return "?"; } }

        static string CleanReligionName(string t)
        {
            t = (t ?? "").Trim().Trim('"', '\'', '.', '!');
            t = Regex.Replace(t, @"^(a |o |da |do |de )?(religi[aã]o|f[eé]|igreja|culto|cren[cç]a)( d[aoe]s?)?\s*", "", RegexOptions.IgnoreCase).Trim();
            if (t.Length < 2 || t.Length > 40 || Regex.IsMatch(NativeActions.Fold(t), @"^(minha|nova|sua|tua|deus|profeta|nenhum|nada)$")) return null;
            return char.ToUpper(t[0]) + t.Substring(1);
        }

        /// <summary>v0.8.0: every prophet gets the game's real traits blessed, chosen_one, super_health and immortal
        /// (addTrait with opposites removed, as the game's own trait editor). Returns the pt names of the traits newly gained; logged.</summary>
        public static readonly string[] ProphetTraitIds = { "blessed", "chosen_one", "super_health", "immortal" };
        public static List<string> ProphetGifts(Actor npc, string why)
        {
            var got = new List<string>();
            if (npc == null) return got;
            var failed = new List<string>();
            foreach (var id in ProphetTraitIds)
            {
                try
                {
                    if (AssetManager.traits.get(id) == null) { failed.Add(id + " (não existe)"); continue; }
                    if (npc.hasTrait(id)) continue;
                    npc.addTrait(id, true);
                    if (npc.hasTrait(id)) got.Add(NativeActions.TraitPt(id)); else failed.Add(id);
                }
                catch (Exception e) { failed.Add(id + " (" + e.Message + ")"); }
            }
            if (got.Count > 0 || failed.Count > 0)
                Main.LogInfo("[GodBox] PROPHET gifts [" + Name(npc) + "] (" + why + "): added " + (got.Count == 0 ? "nothing new" : string.Join(", ", got)) + (failed.Count > 0 ? "; NOT added: " + string.Join(", ", failed) : "") + ".");
            return got;
        }

        static string GiftFeelings(List<string> gifts)
        {
            var f = new List<string>();
            string all = string.Join(" ", gifts).ToLowerInvariant();
            if (gifts.Count > 0) f.Add("uma luz/graça divina pousando sobre você");
            if (all.Contains("escolhid") || gifts.Any(g => g.ToLowerInvariant().Contains("chosen"))) f.Add("a certeza de ter sido escolhido(a) por Deus para um destino");
            f.Add("um vigor que inunda o corpo");
            f.Add("a sensação de que o tempo não toca mais você");
            return string.Join("; ", f.Distinct()) + ". Não cite números nem nomes de traços em inglês.";
        }

        static void MakeProphet(Actor npc, string target, DialogueConfig cfg, WorldfallActions.Result res)
        {
            var g = MemoryStore.God();
            long id = -1; try { id = npc.getID(); } catch { }
            if (g.Prophets.Contains(id) && npc.hasReligion() && GodReligion() == npc.religion)
            {
                res.Outcome = "sem_efeito"; res.Reason = "já é profeta de " + RelName(npc.religion);
                var g0 = ProphetGifts(npc, "already a prophet, touched again");   // v0.8.0: old prophets get the gifts when touched
                if (g0.Count > 0) { res.Outcome = "feito"; res.Changes.Add("SENSAÇÕES (dons de profeta, traços reais ganhos agora: " + string.Join(", ", g0) + "): narre em primeira pessoa, SEM números: " + GiftFeelings(g0)); }
                return;
            }
            if (npc.subspecies == null || !npc.subspecies.has_advanced_memory) { res.Reason = "esta espécie não pode ter religião"; return; }
            if (Defies(npc, cfg, res, "recusou o chamado para ser profeta")) return;

            // v0.8.2 (L): God's religion is formed ONLY by the window or by make_prophet, and it is always a NEW game
            // religion. It is never an existing religion adopted from the prophet or the city (the old "adopt" path made
            // Belfon's Wunu, with its own god, God's religion). Once it exists, the prophet joins it; no second religion.
            var rel = GodReligion();
            bool created = false;
            int around = 0;
            string wanted = CleanReligionName(target);
            if (rel != null && wanted != null && !string.Equals(RelName(rel), wanted, StringComparison.OrdinalIgnoreCase))
                Main.LogInfo("[GodBox] make_prophet named \"" + wanted + "\", but God's religion already exists (" + RelName(rel) + "): the prophet joins it; no second religion is founded.");
            if (rel == null)
            {
                if (npc.kingdom == null) { res.Reason = "sem reino, o jogo não deixa fundar religião"; return; }
                if (wanted == null) { var tpl = GodFaith.Current(); if (tpl != null && !string.IsNullOrWhiteSpace(tpl.Name)) wanted = tpl.Name.Trim(); }   // template name
                string oldRel = npc.hasReligion() ? RelName(npc.religion) : null;
                rel = World.world.religions.newReligion(npc, true);  // the game's own founding (as in the new_religion plot); name generated by the game
                created = true;
                if (wanted != null) try { rel.setName(wanted); } catch { }
                g.ReligionId = rel.getID(); g.ReligionName = RelName(rel);
                npc.setReligion(rel); try { rel.countConversion(); } catch { }
                // the game's plot then converts same-species units around the founder (overriding their faith): do the same and COUNT them
                var before = new List<Actor>();
                try { foreach (var u in rel.getUnitFromChunkForConversion(npc)) if (u != null && u != npc && u.religion != rel) before.Add(u); } catch { }
                try { rel.forceConvertSameSpeciesAroundUnit(npc); } catch (Exception e) { Main.LogWarning("[GodBox] forceConvertSameSpeciesAroundUnit failed: " + e.Message); }
                foreach (var u in before) try { if (u.isAlive() && u.religion == rel) around++; } catch { }
                if (around > 0) g.AddConversions(id, around);
                Main.LogInfo("[GodBox] God's religion FOUNDED by make_prophet: " + RelName(rel) + " (id " + rel.getID() + ", new game religion), prophet " + Name(npc) + (oldRel != null ? " left " + oldRel : "") + "; " + around + " same-species unit(s) around converted by the game's founding.");
            }
            else if (npc.religion != rel) { npc.setReligion(rel); try { rel.countConversion(); } catch { } }
            var gifts = ProphetGifts(npc, "made prophet");   // v0.8.0: blessed + chosen_one + super_health + immortal (real addTrait)
            try { npc.setHappiness(npc.data.happiness + 40, true); npc.addStatusEffect("inspired", 0f, true); } catch { }
            if (!g.Prophets.Contains(id)) g.Prophets.Add(id);
            g.ConvertedTotal += around;
            MemoryStore.SaveGod();
            try { var mem = MemoryStore.Get(npc); mem.ProphetOf = RelName(rel); MemoryStore.Save(mem); } catch { }
            res.Outcome = "feito";
            // v0.8.0 fix: apply the God's-religion template (name / god name) BEFORE any text is built, so the
            // change line and the rumour use the CURRENT god name, never the one the game rolled at founding
            try { GodFaith.OnReligionReady(rel); } catch (Exception e) { Main.LogWarning("[GodBox] template not applied: " + e.Message); }
            g.ReligionName = RelName(rel); try { mem2(npc, RelName(rel)); } catch { }
            string godName = Gods.GodName();   // v0.8.2 (A)
            // v0.8.0 wording: God founded the faith (via the template); the unit only BECOMES its prophet
            res.Changes.Add("tornou-se " + GodOrders082.ProphetWord(npc) + " da religião " + RelName(rel) + (godName != null ? " (profeta de " + godName + ", o deus dessa fé)" : "") + (created ? "; a fé acaba de nascer pela vontade de Deus" : ""));
            if (gifts.Count > 0)
                res.Changes.Add("SENSAÇÕES (dons de profeta, traços reais ganhos: " + string.Join(", ", gifts) + "): narre em primeira pessoa, SEM números: " + GiftFeelings(gifts));
            if (around > 0) res.Changes.Add(around + " pessoa(s) ao redor se converteram na hora");
            try   // v0.7.9 Religião de Deus: template name/god name, then the prophet sets out to preach in nearby cities
            {
                if (GodFaith.Current() != null && GodFaith.StartMission(npc, null, false) == null)
                    res.Changes.Add("vai sair andando para pregar a doutrina nas cidades próximas (com o mundo rodando)");
            }
            catch (Exception e) { Main.LogWarning("[GodBox] prophet mission not started: " + e.Message); }
        }

        static void mem2(Actor npc, string relName) { var mem = MemoryStore.Get(npc); if (mem.ProphetOf != relName) { mem.ProphetOf = relName; MemoryStore.Save(mem); } }

        /// <summary>v0.8.1 (B14): each preach converts a few eligible people nearby; per-person odds from
        /// GodFaith.ConvertChance (stance, traits, temple, kingdom/city religion). cfg chance scales it (0.35 = x1).</summary>
        static int ConvertAround(Actor npc, Religion rel, float radius, float chance, int max)
        {
            int n = 0, seen = 0, eligible = 0; double sumP = 0; string sample = null;
            float scale = Mathf.Clamp(chance / 0.35f, 0.3f, 2f);
            foreach (var u in Finder.getUnitsFromChunk(npc.current_tile, Math.Max(1, (int)Math.Ceiling(radius / 16f)), radius, true))
            {
                if (n >= max) break;
                if (u == null || u == npc || !u.isAlive() || u.religion == rel) continue;
                seen++;
                try { if (u.kingdom != null && npc.kingdom != null && u.kingdom != npc.kingdom && u.kingdom.isEnemy(npc.kingdom)) continue; } catch { }
                bool mind = false; try { mind = u.hasSubspecies() && u.subspecies.has_advanced_memory; } catch { }
                if (!mind) continue;
                float p = GodFaith.ConvertChance(u, npc, out string why) * scale;
                if (p <= 0f) continue;
                eligible++; sumP += p; if (sample == null) sample = Name(u) + ": " + why + (scale != 1f ? " x" + scale.ToString("0.00") : "");
                if (Rng.NextDouble() >= p) continue;
                bool roots = false; try { roots = u.hasCulture() && !u.culture.isPossibleToConvertToOtherMeta(); } catch { }
                if (roots) continue;
                bool ok = false; try { ok = u.tryToConvertToReligion(rel); } catch { }
                if (ok && u.religion != rel) { try { u.setReligion(rel); } catch { } }
                if (u.religion == rel) n++;
            }
            if (seen > 0) Main.LogInfo("[GodBox] PREACH odds near " + Name(npc) + ": " + seen + " nearby, " + eligible + " eligible, avg " + (eligible > 0 ? (sumP / eligible).ToString("0.00") : "-") + (sample != null ? " (ex. " + sample + ")" : "") + " -> " + n + " converted.");
            if (n > 0) { try { var g = MemoryStore.God(); if (g.Prophets.Contains(npc.getID())) g.AddConversions(npc.getID(), n); } catch { } }
            return n;
        }

        [ThreadStatic] static bool _hereOnly;
        /// <summary>v0.8.2 (F7): "teleporte-se para X e pregue lá": preach where the prophet now stands (no walk home to the temple, no redirect).</summary>
        public static void PreachHere(Actor npc, DialogueConfig cfg, WorldfallActions.Result res)
        {
            _hereOnly = true;
            try { Preach(npc, "perto", cfg, res); } finally { _hereOnly = false; }
        }

        static void Preach(Actor npc, string target, DialogueConfig cfg, WorldfallActions.Result res)
        {
            if (!npc.hasReligion()) { res.Reason = "não tem religião para pregar (torne-o profeta primeiro)"; return; }
            var rel = npc.religion;
            if (Defies(npc, cfg, res, "recusou-se a pregar")) return;
            string f = NativeActions.Fold(target);
            string scope = Regex.IsMatch(f, "reino|imperio|nacao|kingdom") ? "reino" : Regex.IsMatch(f, "cidade|vila|aldeia|city|town") ? "cidade" : "perto";
            // v0.8.2 (G): preaching targets NON-followers. If everyone at home who can convert already follows the faith,
            // the prophet walks to the nearest city that still has someone to convert (temple cities preferred).
            try
            {
                if (!_hereOnly && scope != "reino" && npc.city != null && rel == CurrentReligion() && GodFaith.FullyFollows(npc.city, rel, npc))
                {
                    string home = npc.city.name;
                    string err = GodFaith.StartMission(npc, null, true, new[] { npc.city.getID() });
                    if (err != null)
                    {
                        res.Outcome = "sem_efeito";
                        res.Reason = "todos em " + home + " já seguem a fé, e " + err;
                        Main.LogInfo("[GodBox] ACTION preach: " + Name(npc) + " - nobody left to convert in " + home + " or nearby (" + err + ").");
                        return;
                    }
                    string dest = GodFaith.MissionFact(npc);
                    res.Outcome = "em_andamento";
                    res.Changes.Add("todos em " + home + " que podem se converter já seguem a fé; " + (dest ?? "o profeta partiu para pregar em outra cidade"));
                    res.Reason = "prega quando chegar, com o mundo rodando";
                    Main.LogInfo("[GodBox] ACTION preach: " + Name(npc) + " - " + home + " already follows; walking to the nearest city with people to convert.");
                    return;
                }
            }
            catch (Exception e) { Main.LogWarning("[GodBox] preach redirect skipped: " + e.Message); }
            // v0.8.1 (C2): at home the prophet walks to the city's temple first and preaches there
            WorldTile templeTile = null;
            try { if (!_hereOnly && npc.city != null && GodFaith.HasTemple(npc.city)) templeTile = GodFaith.PreachTile(npc.city); } catch { }
            if (templeTile != null)
            {
                NativeActions.WalkTo(npc, templeTile);
                bool god = false; try { god = MemoryStore.God().GodTempleCities.Contains(npc.city.getID()); } catch { }
                res.Changes.Add("foi pregar no templo de " + npc.city.name + (god ? " (o templo que Deus ergueu: a fé ali é mais forte)" : ""));
                Main.LogInfo("[GodBox] ACTION preach: " + Name(npc) + " walks to the temple of " + npc.city.name + (god ? " (God's temple)" : "") + ".");
            }
            int now = ConvertAround(npc, rel, cfg.PreachRadius, cfg.PreachChance, 6);
            if (scope == "cidade" || scope == "reino")
            {
                if (npc.city == null) { res.Reason = "não mora em nenhuma cidade"; return; }
                var cities = new List<City>();
                if (scope == "reino" && npc.kingdom != null) cities.AddRange(npc.kingdom.getCities()); else cities.Add(npc.city);
                int citizens = 0, adopted = 0;
                var lines = new List<string>();
                foreach (var c in cities)
                {
                    int conv = 0, total = 0;
                    foreach (var u in c.getUnits().ToList())
                    {
                        if (u == null || !u.isAlive() || u == npc) continue;
                        total++;
                        if (u.religion == rel) continue;
                        bool mind = false; try { mind = u.hasSubspecies() && u.subspecies.has_advanced_memory; } catch { }
                        if (!mind) continue;
                        float p = GodFaith.ConvertChance(u, npc) * Mathf.Clamp(cfg.PreachCityShare / 0.5f, 0.3f, 2f) * 0.6f;
                        if (Rng.NextDouble() >= p) continue;
                        bool roots = false; try { roots = u.hasCulture() && !u.culture.isPossibleToConvertToOtherMeta(); } catch { }
                        if (roots) continue;
                        try { if (u.tryToConvertToReligion(rel) && u.religion != rel) u.setReligion(rel); } catch { }
                        if (u.religion == rel) conv++;
                    }
                    citizens += conv;
                    int fol = 0; try { fol = c.getUnits().Count(x => x != null && x != npc && x.isAlive() && x.religion == rel); } catch { }   // v0.8.2 (G): same base as total
                    bool was = false; try { was = c.getReligion() == rel; } catch { }
                    bool adopts = !was && conv > 0 && total > 0 && fol * 2 >= total;
                    if (adopts) { try { c.setReligion(rel); adopts = c.getReligion() == rel; } catch { adopts = false; } }
                    if (adopts || was) adopted++;
                    Main.LogInfo("[GodBox] PREACH city " + c.name + ": " + conv + " converted, " + fol + "/" + total + " followers" + (was ? " (already the city's faith)" : adopts ? " (new majority: city adopted)" : " (no majority: city did NOT adopt)") + ".");
                    lines.Add(c.name + ": " + fol + " de " + total + " moradores seguem " + RelName(rel) + (adopts ? " e a cidade adotou a fé" : was ? " (a cidade já seguia a fé)" : ", a cidade ainda não adotou a fé"));
                }
                bool kingdomAdopts = false;
                if (scope == "reino" && npc.kingdom != null && cities.Count > 0 && adopted * 2 > cities.Count && npc.kingdom.getReligion() != rel)
                { try { npc.kingdom.setReligion(rel); kingdomAdopts = npc.kingdom.getReligion() == rel; } catch { } }
                if (citizens > 0) { try { var g0 = MemoryStore.God(); if (g0.Prophets.Contains(npc.getID())) g0.AddConversions(npc.getID(), citizens); } catch { } }
                now += citizens;
                foreach (var l in lines.Take(4)) res.Changes.Add(l);
                if (scope == "reino") res.Changes.Add(kingdomAdopts ? "o reino " + npc.kingdom.name + " adotou " + RelName(rel) : "o reino " + (npc.kingdom?.name ?? "?") + " ainda NÃO adotou " + RelName(rel) + " (" + adopted + " de " + cities.Count + " cidades seguem a fé)");
            }
            res.Changes.Add(now + " pessoa(s) convertida(s) agora");
            _preach.RemoveAll(p => p.Npc == npc);
            float t = Time.unscaledTime;
            _preach.Add(new PreachTask { Npc = npc, Rel = rel, Until = t + Math.Max(10f, cfg.PreachSeconds), Next = t + Math.Max(2f, cfg.PreachIntervalSeconds), Converted = now });
            var g = MemoryStore.God(); g.ConvertedTotal += now; MemoryStore.SaveGod();
            res.Outcome = "em_andamento";
            res.Reason = "continua pregando por um tempo enquanto o mundo estiver andando";
        }

        class PreachTask { public Actor Npc; public Religion Rel; public float Until, Next; public int Converted; }
        static readonly List<PreachTask> _preach = new List<PreachTask>();

        /// <summary>v0.8.2 (K): unit-window task label while God's preaching is active (null = the game's own text).</summary>
        public static void StopPreach(Actor a) { _preach.RemoveAll(p => p.Npc == a); }
        public static string TaskLabel(Actor a)
        {
            if (a == null) return null;
            try { if (GodFaith.OnMission(a)) return "Indo pregar"; } catch { }
            try { for (int i = 0; i < _preach.Count; i++) if (_preach[i].Npc == a) return "Pregando"; } catch { }
            return null;
        }

        /// <summary>Pause-aware maintenance of preaching (called from Main.Update).</summary>
        public static void Tick()
        {
            if (_preach.Count == 0) return;
            var cfg = Main.Cfg; if (cfg == null) return;
            bool paused = false; try { paused = World.world != null && World.world.isPaused(); } catch { }
            if (paused) { float dt = Time.unscaledDeltaTime; foreach (var p in _preach) { p.Until += dt; p.Next += dt; } return; }
            float now = Time.unscaledTime;
            for (int i = _preach.Count - 1; i >= 0; i--)
            {
                var p = _preach[i];
                try
                {
                    bool alive = p.Npc != null && p.Npc.isAlive() && p.Npc.religion == p.Rel;
                    if (!alive || now > p.Until)
                    {
                        _preach.RemoveAt(i);
                        string gnm = null; try { gnm = Gods.GodName(); } catch { }
                        string msg = Name(p.Npc) + (gnm != null ? ", profeta de " + gnm + "," : "") + " terminou de pregar: " + p.Converted + " convertido(s) a " + RelName(p.Rel) + (alive ? "" : " (interrompido)");
                        Main.LogInfo("[GodBox] ACTION preach: ended | " + msg);
                        ChatWindow.Toast(msg);
                        continue;
                    }
                    if (now < p.Next) continue;
                    p.Next = now + Math.Max(2f, cfg.PreachIntervalSeconds);
                    int n = ConvertAround(p.Npc, p.Rel, cfg.PreachRadius, cfg.PreachChance, 3);
                    if (n > 0) { p.Converted += n; var g = MemoryStore.God(); g.ConvertedTotal += n; MemoryStore.SaveGod(); Main.LogInfo("[GodBox] ACTION preach: +" + n + " [" + Name(p.Npc) + "]"); }
                }
                catch (Exception e) { _preach.RemoveAt(i); Main.LogWarning("[GodBox] preach tick failed: " + e.Message); }
            }
        }

        /// <summary>Status line for the prompt (religion / prophet / ongoing preaching).</summary>
        public static string Status(Actor npc)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                var g = MemoryStore.God();
                long id = npc.getID();
                bool fem = GodOrders082.Female(npc);
                if (g.Prophets.Contains(id)) sb.Append("Você é " + (fem ? "PROFETISA escolhida" : "PROFETA escolhido") + " por Deus" + (npc.hasReligion() ? " da religião " + RelName(npc.religion) : "") + ". ");
                else if (g.RevokedProphets != null && g.RevokedProphets.Contains(id)) sb.Append("Você NÃO é mais " + (fem ? "profetisa" : "profeta") + ": Deus retirou o seu dom. ");
                else if (npc.hasReligion()) sb.Append("Sua religião: " + RelName(npc.religion) + ". ");
                // v0.8.2 (I5): the REAL cursed state (the game's status), never guessed
                bool cursed = false; try { cursed = npc.hasStatus("cursed"); } catch { }
                sb.Append(cursed ? "Você está AMALDIÇOADO(A) agora (estado real). " : "Você NÃO está amaldiçoado(a). ");
                // v0.8.2 (H): what this NPC knows about the temple of its own city
                try { if (npc.city != null) sb.Append(GodFaith.TempleFact(npc.city) + " "); } catch { }
                var p = _preach.FirstOrDefault(x => x.Npc == npc);
                if (p != null) sb.Append("Está pregando agora (" + p.Converted + " convertido(s) até agora; continua quando o mundo voltar a andar). ");
                return sb.Length > 0 ? sb.ToString().Trim() : null;
            }
            catch { return null; }
        }

        public static WorldfallActions.Result Execute(Actor npc, Actor you, WorldfallActions.Request req, DialogueConfig cfg)
        {
            var d = Defs.TryGetValue(req?.Id ?? "", out var dd) ? dd : null;
            var res = new WorldfallActions.Result { Id = req?.Id, Pt = d?.Pt ?? req?.Id, En = d?.En ?? req?.Id };
            try
            {
                if (d == null) { res.Reason = "ação desconhecida"; return res; }
                if (you != null) { res.Outcome = "recusado"; res.Reason = "só os deuses podem fazer isso"; return res; }
                if (npc == null || !npc.isAlive()) { res.Reason = "o personagem já morreu"; return res; }
                string target = req.Target ?? "";
                string rid = req.Id;
                if ((rid == "add_trait" || rid == "remove_trait") && Regex.IsMatch(NativeActions.Fold(target), @"maldi|amaldi|praguej|curse") && TraitCategory(target) == null)
                    rid = rid == "add_trait" ? "curse" : "lift_curse";
                if (rid != req.Id) { res.Id = rid; res.Pt = Defs[rid].Pt; res.En = Defs[rid].En; }
                var beforeP = StatsFacts.ProbeOf(npc);   // v0.7.8: stats + statuses + traits, for feelings
                var before = beforeP.S;                  // v0.7.3: concrete stat change in RESULTADO
                StatsFacts.Snap mid = default; bool hasMid = false;   // v0.7.8: bless = permanent trait part vs temporary inspired part
                switch (rid)
                {
                    case "curse":
                    {   // never defied: a divine curse is not a request. 0.51.2 has no "cursed" TRAIT, only the "cursed" STATUS.
                        // v0.7.7: report exactly what applied. The game's own curse (drop "curse", ActionLibrary.giveCursed) is
                        // just addStatusEffect("cursed"); the "cursed" StatusAsset has opposite_traits = { "evil" }, so units with the
                        // evil trait (evil mages, necromancers, smores, cruel souls) are immune in vanilla too. We do not bypass it.
                        float secs = Math.Max(30f, cfg.CurseSeconds);
                        bool already = false; try { already = npc.hasStatus("cursed"); } catch { }
                        bool hadUnlucky = Has(npc, "unlucky");
                        string why = already ? null : StatusBlock(npc, "cursed");
                        bool st = false;
                        if (already) { try { st = npc.addStatusEffect("cursed", secs, true); } catch { } }   // vanilla: timer refresh only
                        else if (why == null) { try { st = npc.addStatusEffect("cursed", secs, true); } catch { } }
                        bool hasNow = false; try { hasNow = npc.hasStatus("cursed"); } catch { }
                        if (!hadUnlucky) { try { npc.addTrait("unlucky", true); } catch { } }
                        bool gotUnlucky = !hadUnlucky && Has(npc, "unlucky");
                        bool sad = false;
                        if (hasNow && !already || gotUnlucky) { try { npc.changeHappiness("just_cursed", -10); sad = true; } catch { } }
                        if (already && hasNow) res.Changes.Add("já estava amaldiçoado(a): só a duração foi renovada (estado 'cursed' por ~" + Mathf.RoundToInt(secs) + " s de jogo)");
                        else if (hasNow) res.Changes.Add("amaldiçoado(a) (estado 'cursed' por ~" + Mathf.RoundToInt(secs) + " s de jogo: menos vida, dano, velocidade e diplomacia)");
                        else res.Changes.Add("a maldição (estado 'cursed') NÃO pegou: " + (why ?? StatusBlock(npc, "cursed") ?? "o jogo recusou o estado"));
                        if (gotUnlucky) res.Changes.Add("ganhou o traço azarado (unlucky)");
                        else if (hadUnlucky) res.Changes.Add("já era azarado(a) (unlucky): nada mudou nisso");
                        else res.Changes.Add("o traço azarado (unlucky) não pegou");
                        if (sad) res.Changes.Add("ficou muito infeliz");
                        bool any = (hasNow && !already) || gotUnlucky || (already && hasNow);
                        if (!any)
                        {
                            res.Outcome = "sem_efeito";
                            res.Reason = hadUnlucky && !hasNow ? "nada mudou: a maldição não pega nesta criatura e ela já era azarada"
                                       : "a maldição não pegou";
                            break;
                        }
                        res.Outcome = "feito";
                        if (!hasNow) res.Reason = "só em parte: o estado amaldiçoado não pegou";
                        break;
                    }
                    case "lift_curse":
                    {   // never defied
                        bool had = false; try { had = npc.hasStatus("cursed"); } catch { }
                        bool unl = Has(npc, "unlucky");
                        if (!had && !unl) { res.Outcome = "sem_efeito"; res.Reason = "não está amaldiçoado(a)"; break; }
                        if (had)
                        {
                            npc.finishStatusEffect("cursed");
                            // v0.7.9: finish() only marks the status; vanilla removes it from the dict on the next status tick, so the
                            // after-snapshot still saw the curse's HP/damage/speed penalties. Remove it now (same call StatusManager makes)
                            // and recalc the stats before the after-snapshot.
                            try { if (npc._active_status_dict.TryGetValue("cursed", out var st)) npc.removeFinishedStatusEffect(st); } catch { }
                            try { npc.setStatsDirty(); npc.updateStats(); } catch { }
                            res.Changes.Add("a maldição (estado 'cursed') acabou");
                        }
                        if (unl) { npc.removeTrait("unlucky"); if (!Has(npc, "unlucky")) res.Changes.Add("perdeu o traço azarado"); }
                        try { npc.changeHappiness("just_received_gift"); } catch { }
                        res.Outcome = res.Changes.Count > 0 ? "feito" : "falhou";
                        if (res.Changes.Count == 0) res.Reason = "a maldição não pôde ser removida";
                        break;
                    }
                    case "add_trait": AddTraits(npc, req, cfg, res); break;
                    case "remove_trait": RemoveTraits(npc, req, cfg, res); break;
                    case "take_item": TakeItem(npc, target, res); break;
                    case "bless":
                    {   // v0.7.7: like the game's blessing drop, addTrait("blessed") WITHOUT removing opposites: the "evil" trait blocks it.
                        bool had = Has(npc, "blessed");
                        bool evilSoul = Has(npc, "evil");
                        if (!had) { try { npc.addTrait("blessed", false); } catch { } }
                        bool got = !had && Has(npc, "blessed");
                        bool malefic = GodForm.Hostile(npc);
                        if (malefic)
                        {   // the light still touches it, and it hates it
                            try { npc.addStatusEffect("angry", 0f, true); } catch { }
                        }
                        else
                        {
                            mid = StatsFacts.Take(npc); hasMid = true;
                            try { npc.setHappiness(npc.data.happiness + 50, true); } catch { }
                            try { npc.addStatusEffect("inspired", 0f, true); } catch { }
                        }
                        if (had) res.Changes.Add("já era abençoado(a)");
                        else if (got) res.Changes.Add("ganhou o traço abençoado (blessed)");
                        else res.Changes.Add("o traço abençoado NÃO pegou" + (evilSoul ? ": no jogo, o traço cruel (evil) é oposto ao abençoado" : ""));
                        res.Changes.Add(malefic ? "é um ser maléfico: sentiu a luz divina e ODIOU (ficou furioso)" : "sentiu alegria e inspiração");
                        res.Outcome = "feito";
                        if (!had && !got) res.Reason = "só em parte: o traço abençoado não pegou";
                        break;
                    }
                    case "give_item":
                    {
                        var asset = ItemFor(target, out string why);
                        if (asset == null) { res.Reason = why; break; }
                        var slot = npc.equipment.getSlot(asset.equipment_type);
                        if (!slot.canChangeSlot()) { res.Reason = "o item que já usa nesse lugar é amaldiçoado e não sai"; break; }
                        if (Defies(npc, cfg, res, "recusou o presente divino")) break;
                        string old = null; try { old = slot.isEmpty() ? null : slot.getItem().getAsset().id; } catch { }
                        var item = World.world.items.generateItem(asset, npc.kingdom, null, 1, npc, 0, true);
                        if (item == null) { res.Reason = "o jogo não criou o item"; break; }
                        npc.equipment.setItem(item, npc);
                        try { npc.setStatsDirty(); } catch { }
                        res.Outcome = "feito";
                        res.Changes.Add("equipou " + ItemNamePt(asset.id) + (old != null ? " (no lugar de " + ItemNamePt(old) + ")" : ""));   // v0.8.0: pt name, never the raw id
                        Main.LogInfo("[GodBox] give_item: " + Name(npc) + " equipped " + ItemNamePt(asset.id) + " [" + asset.id + "]" + (old != null ? " replacing " + ItemNamePt(old) + " [" + old + "]" : "") + ".");
                        break;
                    }
                    case "heal":
                    {
                        bool full = false; try { full = npc.hasMaxHealth(); } catch { }
                        try { npc.restoreHealth(npc.getMaxHealth()); } catch { }
                        int cured = 0;
                        foreach (var st in new[] { "burning", "poisoned", "cursed", "frozen", "stunned" })
                            try { if (npc.hasStatus(st)) { npc.finishStatusEffect(st); cured++; } } catch { }
                        foreach (var tr in new[] { "plague", "infected", "tumor_infection", "mush_spores" })
                            try { if (Has(npc, tr)) { npc.removeTrait(tr); cured++; } } catch { }
                        if (full && cured == 0) { res.Outcome = "sem_efeito"; res.Reason = "já estava com a saúde cheia"; break; }
                        res.Outcome = "feito"; res.Changes.Add("saúde restaurada" + (cured > 0 ? ", " + cured + " mal(es) curado(s)" : ""));
                        break;
                    }
                    case "make_prophet": MakeProphet(npc, target, cfg, res); break;
                    case "preach": Preach(npc, target, cfg, res); break;
                    case "appoint_prophet": GodFaith.AppointProphet(npc, target, cfg, res); break;
                    case "spread_religion": GodFaith.Spread(npc, target, res); break;
                    case "kill":
                    {
                        bool bolt = Regex.IsMatch(NativeActions.Fold(target), @"raio|relamp|trova|fulmin|lightning");
                        var tile = npc.current_tile;
                        string nm = Name(npc);
                        if (bolt && cfg.SmiteLightning) try { MapBox.spawnLightningSmall(tile, 0.15f); } catch { }
                        Deaths.MarkModCause(npc, "fulminado(a) pela voz dos deuses no chat");
                        if (npc.isAlive()) npc.die(false, AttackType.Divine, true, true);
                        res.Outcome = npc.isAlive() ? "falhou" : "feito";
                        if (res.Outcome == "feito") res.Changes.Add(nm + " morreu pela vontade divina" + (bolt && cfg.SmiteLightning ? " (raio)" : ""));
                        else res.Reason = "não morreu (imortal ou protegido)";
                        break;
                    }
                }
                if (res.Outcome == "feito" && rid != "kill" && npc.isAlive())
                {
                    try { npc.setStatsDirty(); npc.updateStats(); } catch { }   // v0.7.9: measure after the recalc
                    var afterP = StatsFacts.ProbeOf(npc);
                    var diff = StatsFacts.Diff(before, afterP.S);
                    if (diff != null)
                    {
                        res.Changes.Add("atributos mudaram: " + diff);
                        if (hasMid)
                        {
                            string tPart = StatsFacts.Diff(before, mid), sPart = StatsFacts.Diff(mid, afterP.S);
                            if (sPart != null) res.Changes.Add("detalhe: permanente pelo traço: " + (tPart ?? "nada") + "; temporário pelo estado inspirado (acaba em ~60 s): " + sPart);
                        }
                    }
                    else if (rid == "bless" || rid == "curse" || rid == "lift_curse" || rid == "add_trait" || rid == "remove_trait" || rid == "give_item" || rid == "take_item")
                        res.Changes.Add("atributos (vida máxima, dano, velocidade, armadura) não mudaram");
                    if (rid == "bless" || rid == "curse" || rid == "lift_curse" || rid == "add_trait" || rid == "remove_trait")
                    {
                        string feel = StatsFacts.Feelings(beforeP, afterP);
                        if (feel != null) res.Changes.Add(feel);
                    }
                }
            }
            catch (Exception e)
            {
                res.Outcome = "falhou"; res.Reason = "erro: " + e.Message;
                Main.LogWarning("[GodBox] god power " + req?.Id + " failed: " + e);
            }
            return res;
        }
    }
}
