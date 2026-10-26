using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace WorldfallAIChat
{
    /// <summary>
    /// v0.7.2: WorldBox's own Portuguese texts (Resources "locales/pt", the same files WorldBox loads when its
    /// language is pt), read once on demand, so FACTOS can show trait/species names in Portuguese even when the
    /// game itself runs in English. Fallback: a small mapping of common trait ids, then the id.
    /// </summary>
    public static class GameLocalePt
    {
        static Dictionary<string, string> _pt;
        static bool _tried;

        static readonly Dictionary<string, string> TraitPt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "soldier", "Soldado" }, { "veteran", "Veterano" }, { "strong", "Forte" }, { "weak", "Fraco" },
            { "fast", "Rápido" }, { "slow", "Lento" }, { "smart", "Inteligente" }, { "stupid", "Burro" },
            { "genius", "Gênio" }, { "lucky", "Sortudo" }, { "unlucky", "Azarado" }, { "tough", "Resistente" },
            { "giant", "Gigante" }, { "tiny", "Pequeno" }, { "fat", "Gordo" }, { "skinny", "Magro" },
            { "eagle_eyed", "Olhos de águia" }, { "short_sighted", "Míope" }, { "bloodlust", "Sede de sangue" },
            { "peaceful", "Pacífico" }, { "pyromaniac", "Piromaníaco" }, { "greedy", "Ganancioso" },
            { "honest", "Honesto" }, { "deceitful", "Mentiroso" }, { "content", "Contente" }, { "paranoid", "Paranoico" },
            { "ambitious", "Ambicioso" }, { "kingslayer", "Regicida" }, { "attractive", "Atraente" }, { "ugly", "Feio" },
            { "immortal", "Imortal" }, { "blessed", "Abençoado" }, { "madness", "Loucura" }, { "plague", "Praga" },
            { "infected", "Infectado" }, { "tumor_infection", "Tumor" }, { "mush_spores", "Esporos" },
            { "regeneration", "Regeneração" }, { "poisonous", "Venenoso" }, { "fire_proof", "À prova de fogo" },
            { "cold_aura", "Aura fria" }, { "hotheaded", "Cabeça quente" }, { "wise", "Sábio" }, { "nightchild", "Filho da noite" },
            { "light_lamp", "Lamparina" }, { "agile", "Ágil" }, { "clumsy", "Desajeitado" }, { "battle_reflexes", "Reflexos de batalha" },
            { "dodge", "Esquiva" }, { "evil", "Malvado" }, { "flower_prints", "Pegadas de flores" }, { "heliophobia", "Heliofobia" },
            { "miracle_born", "Nascido de milagre" }, { "fertile", "Fértil" }, { "infertile", "Infértil" }, { "voices_in_my_head", "Vozes na cabeça" },
        };

        // v0.7.3: traits whose game text is English only (other mods / no pt text): map by the English display name
        static readonly Dictionary<string, string> EnNamePt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "King", "Rei" }, { "Queen", "Rainha" }, { "Commoner", "Plebeu" }, { "Soldier", "Soldado" }, { "Courageous", "Corajoso" },
            { "Leader", "Líder" }, { "Captain", "Capitão" }, { "Warrior", "Guerreiro" }, { "Noble", "Nobre" }, { "Brave", "Corajoso" },
            { "Coward", "Covarde" }, { "Cowardly", "Covarde" }, { "Blessed", "Abençoado" }, { "Cursed", "Amaldiçoado" }, { "Prophet", "Profeta" },
            { "Veteran", "Veterano" }, { "Strong", "Forte" }, { "Weak", "Fraco" }, { "Wise", "Sábio" }, { "Lucky", "Sortudo" }, { "Unlucky", "Azarado" },
            { "Peasant", "Camponês" }, { "Knight", "Cavaleiro" }, { "Guard", "Guarda" }, { "Merchant", "Mercador" }, { "Priest", "Sacerdote" },
            { "Loyal", "Leal" }, { "Traitor", "Traidor" }, { "Kind", "Bondoso" }, { "Cruel", "Cruel" }, { "Proud", "Orgulhoso" }, { "Humble", "Humilde" },
        };

        static void Load()
        {
            if (_tried) return;
            _tried = true;
            try
            {
                var assets = Resources.LoadAll<TextAsset>("locales/pt");
                if (assets == null || assets.Length == 0) return;
                var d = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var a in assets)
                {
                    try
                    {
                        var part = JsonConvert.DeserializeObject<Dictionary<string, string>>(a.text);
                        if (part == null) continue;
                        foreach (var kv in part) if (!d.ContainsKey(kv.Key)) d[kv.Key] = kv.Value;
                    }
                    catch { }
                }
                _pt = d;
                Main.LogInfo("[GodBox] WorldBox pt locale read for FACTOS: " + d.Count + " texts.");
            }
            catch (Exception e) { Main.LogWarning("[GodBox] could not read WorldBox's pt locale: " + e.Message); }
        }

        static bool GameIsPt()
        {
            try { var l = LocalizedTextManager.instance?.language; return l == "pt"; } catch { return false; }
        }

        /// <summary>Portuguese text for a WorldBox locale id, or null.</summary>
        public static string Text(string localeId)
        {
            if (string.IsNullOrEmpty(localeId)) return null;
            try
            {
                if (GameIsPt())
                {
                    var t = LocalizedTextManager.getText(localeId);
                    if (!string.IsNullOrWhiteSpace(t) && t != localeId) return t;
                }
            }
            catch { }
            Load();
            if (_pt == null) return null;
            if (_pt.TryGetValue(localeId, out var v) && !string.IsNullOrWhiteSpace(v)) return v;
            string u = null; try { u = localeId.Underscore(); } catch { }
            if (u != null && _pt.TryGetValue(u, out v) && !string.IsNullOrWhiteSpace(v)) return v;
            return null;
        }

        public static string Trait(ActorTrait t)
        {
            if (t == null) return null;
            string id = null; try { id = t.id; } catch { }
            string s = null;
            try { s = Text(t.getLocaleID()); } catch { }
            if (!string.IsNullOrWhiteSpace(s)) return s;
            if (id != null && TraitPt.TryGetValue(id, out var m)) return m;
            string en = null; try { en = t.getTranslatedName(); } catch { }
            if (!string.IsNullOrWhiteSpace(en) && EnNamePt.TryGetValue(en.Trim(), out var m2)) return m2;
            if (id != null && EnNamePt.TryGetValue(id.Replace('_', ' ').Trim(), out var m3)) return m3;
            return !string.IsNullOrWhiteSpace(en) ? en : id;
        }

        // v0.7.8: never show a raw species id ("evil_mage") or an English name to the model
        public static readonly Dictionary<string, string> SpeciesPt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "human", "Humano" }, { "orc", "Orc" }, { "elf", "Elfo" }, { "dwarf", "Anão" }, { "evil_mage", "Mago Maligno" }, { "white_mage", "Mago Branco" },
            { "necromancer", "Necromante" }, { "druid", "Druida" }, { "plague_doctor", "Médico da Peste" }, { "bandit", "Bandido" }, { "cold_one", "Gélido" },
            { "fire_skull", "Caveira de Fogo" }, { "jumpy_skull", "Caveira Saltitante" }, { "angle", "Anjo" }, { "demon", "Demônio" }, { "skeleton", "Esqueleto" },
            { "zombie", "Zumbi" }, { "ghost", "Fantasma" }, { "snowman", "Boneco de Neve" }, { "alien", "Alienígena" }, { "mush", "Fungo" }, { "fairy", "Fada" },
            { "god_finger", "Dedo de Deus" }, { "dragon", "Dragão" }, { "ufo", "Óvni" }, { "greg", "Greg" }, { "crabzilla", "Caranguejo Gigante" },
            { "wolf", "Lobo" }, { "bear", "Urso" }, { "sheep", "Ovelha" }, { "cow", "Vaca" }, { "chicken", "Galinha" }, { "cat", "Gato" }, { "dog", "Cão" },
            { "rabbit", "Coelho" }, { "fox", "Raposa" }, { "rat", "Rato" }, { "monkey", "Macaco" }, { "crocodile", "Crocodilo" }, { "snake", "Cobra" },
            { "penguin", "Pinguim" }, { "turtle", "Tartaruga" }, { "buffalo", "Búfalo" }, { "hyena", "Hiena" }, { "piranha", "Piranha" }, { "crab", "Caranguejo" },
        };
        static readonly Dictionary<string, string> SpeciesEnPt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Evil Mage", "Mago Maligno" }, { "White Mage", "Mago Branco" }, { "Plague Doctor", "Médico da Peste" }, { "Cold One", "Gélido" },
            { "Fire Skull", "Caveira de Fogo" }, { "Angel", "Anjo" }, { "Necromancer", "Necromante" }, { "Druid", "Druida" }, { "Demon", "Demônio" },
        };

        public static string Species(ActorAsset a)
        {
            if (a == null) return null;
            string s = null;
            try { s = Text(a.getLocaleID()); } catch { }
            if (!string.IsNullOrWhiteSpace(s)) return s;
            string id = null; try { id = a.id; } catch { }
            if (id != null && SpeciesPt.TryGetValue(id, out var m)) return m;
            string en = null; try { en = a.getTranslatedName(); } catch { }
            if (!string.IsNullOrWhiteSpace(en) && SpeciesEnPt.TryGetValue(en.Trim(), out var m2)) return m2;
            if (!string.IsNullOrWhiteSpace(en) && en.IndexOf('_') < 0) return en;
            return id != null ? char.ToUpper(id[0]) + id.Substring(1).Replace('_', ' ') : null;
        }

        /// <summary>v0.8.0 (bug 2): replaces raw trait ids ("deceitful", "strong_minded", "deceitful (enganoso)") with the game's
        /// Portuguese trait name, in lower case. Ids shorter than 4 letters are skipped. found = the ids replaced.</summary>
        public static string ScrubTraitIds(string reply, out List<string> found)
        {
            var hits = new List<string>(); found = hits;
            if (string.IsNullOrEmpty(reply)) return reply;
            string r = reply;
            try
            {
                var ids = new List<string>();
                try { foreach (var t in AssetManager.traits.list) if (t?.id != null && t.id.Length >= 4 && t.id[0] != '$') ids.Add(t.id); } catch { }
                foreach (var k in TraitPt.Keys) if (k.Length >= 4 && !ids.Contains(k)) ids.Add(k);
                ids.Sort((a, b) => b.Length.CompareTo(a.Length));
                foreach (var id in ids)
                {
                    string spaced = id.Replace('_', ' ');
                    if (r.IndexOf(id, StringComparison.OrdinalIgnoreCase) < 0 && (spaced == id || r.IndexOf(spaced, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                    string pt = null;
                    try { var t = AssetManager.traits.get(id); if (t != null) pt = Trait(t); } catch { }
                    if (pt == null && TraitPt.TryGetValue(id, out var m)) pt = m;
                    if (string.IsNullOrWhiteSpace(pt) || string.Equals(pt.Replace(' ', '_'), id, StringComparison.OrdinalIgnoreCase)) continue;
                    string ptl = pt.ToLowerInvariant();
                    string pat = @"(?<![\p{L}_])(" + System.Text.RegularExpressions.Regex.Escape(id) + (spaced != id ? "|" + System.Text.RegularExpressions.Regex.Escape(spaced) : "") + @")(?![\p{L}_])(\s*\([^)]{1,30}\))?";
                    string before = r;
                    r = System.Text.RegularExpressions.Regex.Replace(r, pat, ptl, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (r != before) hits.Add(id);
                }
            }
            catch { }
            return r;
        }

        /// <summary>v0.7.8: replaces raw species ids, English species names and the unit's subspecies (Latin-like) name in a reply.</summary>
        public static string ScrubSpecies(string reply, Actor npc)
        {
            if (string.IsNullOrEmpty(reply)) return reply;
            string r = reply;
            try
            {
                string sub = null; try { sub = npc?.subspecies?.name; } catch { }
                string sp = null; try { sp = Species(npc?.asset); } catch { }
                if (!string.IsNullOrWhiteSpace(sub) && sp != null && sub.Length >= 4)
                    r = System.Text.RegularExpressions.Regex.Replace(r, System.Text.RegularExpressions.Regex.Escape(sub), "linhagem de " + sp, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                foreach (var kv in SpeciesPt)
                    if (kv.Key.IndexOf('_') > 0 && r.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                        r = System.Text.RegularExpressions.Regex.Replace(r, @"\b" + kv.Key + @"\b", kv.Value, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                foreach (var kv in SpeciesEnPt)
                    if (r.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                        r = System.Text.RegularExpressions.Regex.Replace(r, @"\b" + kv.Key + @"\b", kv.Value, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            catch { }
            return r;
        }
    }
}
