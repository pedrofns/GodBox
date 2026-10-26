using UnityEngine;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WorldfallAIChat
{
    /// <summary>
    /// v0.7.3: what traits and statuses really DO (from their base_stats), the NPC's current stats, and active
    /// statuses with remaining time, so "how strong are you / what did the blessing do?" is answered from real
    /// numbers. Every read is isolated; anything missing is skipped.
    /// </summary>
    public static class StatsFacts
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static readonly Dictionary<string, string> StatPt = new Dictionary<string, string>
        {
            { "health", "vida" }, { "damage", "dano" }, { "speed", "velocidade" }, { "armor", "armadura" },
            { "attack_speed", "velocidade de ataque" }, { "lifespan", "expectativa de vida (anos)" }, { "diplomacy", "diplomacia" },
            { "stewardship", "administração" }, { "intelligence", "inteligência" }, { "warfare", "arte da guerra" },
            { "critical_chance", "chance de crítico" }, { "crit", "crítico" }, { "critical_damage_multiplier", "dano crítico" },
            { "accuracy", "precisão" }, { "range", "alcance" }, { "stamina", "vigor" }, { "mana", "mana" },
            { "happiness", "felicidade" }, { "loyalty_traits", "lealdade" }, { "loyalty_mood", "lealdade (humor)" },
            { "cities", "limite de cidades" }, { "army", "tamanho do exército" }, { "knockback", "empurrão" },
            { "size", "tamanho" }, { "scale", "tamanho" }, { "offspring", "filhos" }, { "birth_rate", "natalidade" },
            { "mutation", "mutação" }, { "targets", "alvos" }, { "projectiles", "projéteis" }, { "experience", "experiência" },
            { "area_of_effect", "área de efeito" }, { "throwing_range", "alcance de arremesso" }, { "construction_speed", "velocidade de construção" },
            { "maturation", "amadurecimento" }, { "max_nutrition", "nutrição máxima" }, { "metabolic_rate", "metabolismo" },
            { "opinion", "opinião" }, { "skill_combat", "habilidade de combate" }, { "skill_spell", "habilidade mágica" },
            { "mass", "massa" }, { "recoil", "recuo" }, { "status_chance", "chance de efeito" }, { "damage_range", "variação de dano" },
            { "personality_aggression", "agressividade" }, { "personality_administration", "administração (personalidade)" },
            { "personality_diplomatic", "diplomacia (personalidade)" }, { "personality_rationality", "racionalidade" },
            { "limit_population", "limite de população" }, { "limit_clan_members", "limite do clã" }, { "bonus_towers", "torres extras" },
            { "supply_timer", "tempo de suprimento" }, { "age_adult", "idade adulta" }, { "age_breeding", "idade fértil" },
        };

        static readonly Dictionary<string, string> StatusPt = new Dictionary<string, string>
        {
            { "cursed", "amaldiçoado(a)" }, { "inspired", "inspirado(a)" }, { "burning", "pegando fogo" }, { "poisoned", "envenenado(a)" },
            { "frozen", "congelado(a)" }, { "stunned", "atordoado(a)" }, { "possessed", "possuído(a)" },
            { "possessed_follower", "seguidor(a) de um possuído" }, { "enchanted", "encantado(a)" }, { "shield", "escudo mágico" },
            { "slowness", "lentidão" }, { "drowning", "afogando" }, { "angry", "com raiva" }, { "rage", "fúria" }, { "sleeping", "dormindo" },
            { "pregnant", "grávida" }, { "pregnant_parthenogenesis", "grávida" }, { "egg", "ovo" }, { "budding", "brotando" },
            { "just_ate", "acabou de comer" }, { "afterglow", "satisfeito(a) após o amor" }, { "fell_in_love", "apaixonado(a)" },
            { "festive_spirit", "espírito festivo" }, { "crying", "chorando" }, { "laughing", "rindo" }, { "singing", "cantando" },
            { "swearing", "xingando" }, { "surprised", "surpreso(a)" }, { "confused", "confuso(a)" }, { "tantrum", "birra" },
            { "magnetized", "magnetizado(a)" }, { "voices_in_my_head", "vozes na cabeça" }, { "invincible", "invencível" },
            { "motivated", "motivado(a)" }, { "powerup", "fortalecido(a)" }, { "caffeinated", "cafeinado(a)" },
            { "spell_boost", "magia fortalecida" }, { "spell_silence", "magia silenciada" }, { "soul_harvested", "alma colhida" },
            { "being_suspicious", "desconfiado(a)" }, { "on_guard", "em guarda" }, { "starving", "faminto(a)" },
            { "had_good_dream", "teve um sonho bom" }, { "had_bad_dream", "teve um sonho ruim" }, { "had_nightmare", "teve um pesadelo" },
            { "cough", "tossindo" }, { "ash_fever", "febre das cinzas" }, { "strange_urge", "impulso estranho" },
            { "handsome_migrant", "migrante charmoso" }, { "dodge", "esquivando" }, { "dash", "investida" },
            { "taking_roots", "criando raízes" }, { "uprooting", "arrancando raízes" }, { "flicked", "peteleco divino" },
            { "recovery_plot", "recuperando-se de uma trama" }, { "recovery_spell", "recuperando-se de magia" },
            { "recovery_social", "recuperando-se socialmente" }, { "recovery_combat_action", "recuperando-se de combate" },
        };

        static string F(float v) => Math.Abs(v - (float)Math.Round(v)) < 0.01f ? ((int)Math.Round(v)).ToString(Inv) : v.ToString("0.##", Inv);

        public static string StatName(string id, bool pt)
        {
            BaseStatAsset a = null;
            try { a = AssetManager.base_stats_library.get(id); } catch { }
            string key = id;
            try { if (a != null && a.multiplier && !string.IsNullOrEmpty(a.main_stat_to_multiply)) key = a.main_stat_to_multiply; } catch { }
            if (key.StartsWith("multiplier_")) key = key.Substring("multiplier_".Length);
            if (pt)
            {
                string loc = null;
                try { if (a != null) loc = GameLocalePt.Text(a.getLocaleID()); } catch { }
                if (!string.IsNullOrWhiteSpace(loc)) return loc.ToLowerInvariant();
                if (StatPt.TryGetValue(key, out var m)) return m;
                return key.Replace('_', ' ');
            }
            try { if (a != null) { var t = LocalizedTextManager.getText(a.getLocaleID()); if (!string.IsNullOrWhiteSpace(t) && t != a.getLocaleID()) return t.ToLowerInvariant(); } } catch { }
            return key.Replace('_', ' ');
        }

        /// <summary>"+50% vida, +5 expectativa de vida" from a BaseStats (non-zero, non-hidden entries).</summary>
        public static string Effects(BaseStats bs, bool pt, int max = 6)
        {
            if (bs == null) return null;
            var parts = new List<string>();
            try
            {
                foreach (var c in bs.getList())
                {
                    if (c == null || Math.Abs(c.value) < 0.0001f) continue;
                    BaseStatAsset a = null; try { a = c.asset; } catch { }
                    try { if (a != null && (a.hidden || a.ignore)) continue; } catch { }
                    string val;
                    bool mult = false, perc = false; float vis = 1f;
                    try { if (a != null) { mult = a.multiplier; perc = a.show_as_percents; vis = a.tooltip_multiply_for_visual_number; } } catch { }
                    if (mult || c.id.StartsWith("multiplier_")) { float p = c.value * 100f; val = (p > 0 ? "+" : "") + F(p) + "%"; }
                    else if (perc) { float p = c.value * (vis == 0 ? 1f : vis); val = (p > 0 ? "+" : "") + F(p) + "%"; }
                    else val = (c.value > 0 ? "+" : "") + F(c.value);
                    parts.Add(val + " " + StatName(c.id, pt));
                    if (parts.Count >= max) break;
                }
            }
            catch { }
            return parts.Count == 0 ? null : string.Join(", ", parts);
        }

        /// <summary>"Abençoado: +50% dano, +50% vida; Forte: +..." only traits that change stats, max 10.</summary>
        public static string TraitEffects(Actor a, bool pt)
        {
            try
            {
                var lines = new List<string>();
                foreach (var t in a.traits)
                {
                    if (t == null) continue;
                    string eff = null; try { eff = Effects(t.base_stats, pt); } catch { }
                    if (eff == null) continue;
                    lines.Add(PromptBuilder.SafeTrait(t) + ": " + eff);
                    if (lines.Count >= 10) break;
                }
                return lines.Count == 0 ? null : string.Join("; ", lines);
            }
            catch { return null; }
        }

        static float St(Actor a, string id) { try { return a.stats[id]; } catch { return 0f; } }

        public static string CurrentStats(Actor a, bool pt)
        {
            try
            {
                var p = new List<string>();
                try { p.Add((pt ? "vida " : "health ") + a.getHealth() + "/" + a.getMaxHealth()); } catch { }
                void Add(string id)
                {
                    float v = St(a, id); if (Math.Abs(v) < 0.0001f) return;
                    bool perc = false; float vis = 1f;
                    try { var asset = AssetManager.base_stats_library.get(id); perc = asset.show_as_percents; vis = asset.tooltip_multiply_for_visual_number; } catch { }
                    p.Add(StatName(id, pt) + " " + (perc ? F(v * (vis == 0 ? 1f : vis)) + "%" : F(v)));
                }
                foreach (var id in new[] { "damage", "armor", "speed", "attack_speed", "critical_chance", "lifespan", "diplomacy", "stewardship", "intelligence", "warfare" }) Add(id);
                try { p.Add((pt ? "felicidade " : "happiness ") + a.getHappinessPercent() + "%"); } catch { }
                return p.Count == 0 ? null : string.Join(", ", p);
            }
            catch { return null; }
        }

        public static string StatusName(StatusAsset s, bool pt)
        {
            if (s == null) return null;
            string id = null; try { id = s.id; } catch { }
            if (pt)
            {
                if (id != null && StatusPt.TryGetValue(id, out var m)) return m;
                string loc = null; try { loc = GameLocalePt.Text(s.getLocaleID()); } catch { }
                if (!string.IsNullOrWhiteSpace(loc)) return loc;
                return id;
            }
            try { var t = LocalizedTextManager.getText(s.getLocaleID()); if (!string.IsNullOrWhiteSpace(t) && t != s.getLocaleID()) return t; } catch { }
            return id;
        }

        public static string ActiveStatuses(Actor a, bool pt)
        {
            try
            {
                var p = new List<string>();
                foreach (Status st in a.getStatuses())
                {
                    if (st == null) continue;
                    try { if (st.is_finished) continue; } catch { }
                    StatusAsset asset = null; try { asset = st.asset; } catch { }
                    string name = StatusName(asset, pt); if (name == null) continue;
                    var extra = new List<string>();
                    try { double r = st.getRemainingTime(); if (r > 0 && r < 1e6) extra.Add((pt ? "faltam ~" : "~") + (int)Math.Ceiling(r) + (pt ? " s de jogo" : " game s left")); } catch { }
                    string eff = null; try { eff = Effects(asset.base_stats, pt, 4); } catch { }
                    if (eff != null) extra.Add(eff);
                    p.Add(name + (extra.Count > 0 ? " (" + string.Join("; ", extra) + ")" : ""));
                    if (p.Count >= 8) break;
                }
                return p.Count == 0 ? (pt ? "nenhum" : "none") : string.Join(", ", p);
            }
            catch { return null; }
        }

        // ---------- before/after snapshot for god actions ----------
        public struct Snap { public int MaxHp, Hp; public float Dmg, Spd, Arm, AtkSpd, Life; public bool Ok; }

        public static Snap Take(Actor a)
        {
            var s = new Snap();
            try
            {
                try { if (a.isStatsDirty()) a.updateStats(); } catch { }
                s.MaxHp = a.getMaxHealth(); s.Hp = a.getHealth();
                s.Dmg = St(a, "damage"); s.Spd = St(a, "speed"); s.Arm = St(a, "armor"); s.AtkSpd = St(a, "attack_speed"); s.Life = St(a, "lifespan");
                s.Ok = true;
            }
            catch { }
            return s;
        }

        // ---------- v0.7.8: first-person feeling hints for god-power changes ----------
        public class Probe { public Snap S; public HashSet<string> Statuses = new HashSet<string>(); public Dictionary<string, ActorTrait> Traits = new Dictionary<string, ActorTrait>(); }

        public static Probe ProbeOf(Actor a)
        {
            var p = new Probe { S = Take(a) };
            try { foreach (Status st in a.getStatuses()) { try { if (st != null && !st.is_finished && st.asset != null) p.Statuses.Add(st.asset.id); } catch { } } } catch { }
            try { foreach (var t in a.traits) if (t != null) p.Traits[t.id] = t; } catch { }
            return p;
        }

        static readonly string[,] StatFeel =
        {   // name, up, down
            { "vida máxima", "sinto um calor que fecha minhas feridas e enche meu corpo de vida", "sinto a vida escorrer de mim; meu corpo ficou frágil" },
            { "dano", "meus golpes pesam mais", "meus braços fraquejam; meus golpes perderam o peso" },
            { "velocidade", "minhas pernas parecem leves como o vento", "minhas pernas pesam como chumbo" },
            { "armadura", "minha pele parece mais dura, como couro curtido", "sinto-me exposto(a), como se minha pele afinasse" },
            { "velocidade de ataque", "meus braços se movem mais depressa", "meus movimentos ficaram lentos e pesados" },
            { "expectativa de vida", "sinto que viverei mais anos", "sinto que meus anos foram encurtados" },
        };
        static readonly Dictionary<string, string[]> StatusFeel = new Dictionary<string, string[]>
        {   // id -> began, ended
            { "inspired", new[] { "uma inspiração me invade, mas sinto que vai passar", "a inspiração que me tomava se foi" } },
            { "cursed", new[] { "uma sombra fria se agarra a mim e tudo pesa", "a sombra fria que me prendia se dissipou" } },
            { "angry", new[] { "uma fúria queima dentro de mim", "a fúria se apagou" } },
            { "powerup", new[] { "uma força estranha corre nas minhas veias", "a força estranha me deixou" } },
            { "slowness", new[] { "meu corpo ficou lento, como se andasse na lama", "a lentidão me largou" } },
            { "shield", new[] { "uma proteção invisível me envolve", "a proteção invisível sumiu" } },
        };
        static readonly Dictionary<string, string[]> TraitFeel = new Dictionary<string, string[]>
        {   // id -> gained, lost
            { "blessed", new[] { "uma luz pousa sobre mim e fica", "a luz que me abençoava se apagou" } },
            { "unlucky", new[] { "sinto que a sorte me abandonou", "sinto o azar soltar de mim" } },
        };

        /// <summary>One feeling hint per real change (stats with direction, statuses begun/ended, traits gained/lost).</summary>
        public static string Feelings(Probe b, Probe a)
        {
            if (b == null || a == null) return null;
            var p = new List<string>();
            if (b.S.Ok && a.S.Ok)
            {
                float[] x = { b.S.MaxHp, b.S.Dmg, b.S.Spd, b.S.Arm, b.S.AtkSpd, b.S.Life };
                float[] y = { a.S.MaxHp, a.S.Dmg, a.S.Spd, a.S.Arm, a.S.AtkSpd, a.S.Life };
                for (int i = 0; i < x.Length; i++)
                {
                    if (Math.Abs(x[i] - y[i]) < 0.01f) continue;
                    bool up = y[i] > x[i];
                    p.Add(StatFeel[i, 0] + (up ? " SUBIU" : " CAIU") + " ~ \"" + StatFeel[i, up ? 1 : 2] + "\"");
                }
            }
            foreach (var id in a.Statuses) if (!b.Statuses.Contains(id))
            {
                string nm = StatusPt.TryGetValue(id, out var n) ? n : id;
                float dur = 0f; try { dur = AssetManager.status.get(id).duration; } catch { }
                string tmp = dur > 0f ? " (TEMPORÁRIO, ~" + Mathf.RoundToInt(dur) + " s)" : "";
                p.Add("estado " + nm + tmp + " COMEÇOU ~ \"" + (StatusFeel.TryGetValue(id, out var f) ? f[0] : "sinto-me " + nm + (tmp != "" ? ", mas sinto que vai passar" : "")) + "\"");
            }
            foreach (var id in b.Statuses) if (!a.Statuses.Contains(id))
            {
                string nm = StatusPt.TryGetValue(id, out var n) ? n : id;
                p.Add("estado " + nm + " ACABOU ~ \"" + (StatusFeel.TryGetValue(id, out var f) ? f[1] : "já não estou " + nm) + "\"");
            }
            foreach (var kv in a.Traits) if (!b.Traits.ContainsKey(kv.Key))
            {
                string nm = PromptBuilder.SafeTrait(kv.Value); bool bad = false; try { bad = kv.Value.type == TraitType.Negative; } catch { }
                p.Add("traço " + nm + " GANHO ~ \"" + (TraitFeel.TryGetValue(kv.Key, out var f) ? f[0] : bad ? "algo sombrio se enraizou em mim (" + nm + ")" : "algo novo e bom nasceu em mim (" + nm + ")") + "\"");
            }
            foreach (var kv in b.Traits) if (!a.Traits.ContainsKey(kv.Key))
            {
                string nm = PromptBuilder.SafeTrait(kv.Value); bool bad = false; try { bad = kv.Value.type == TraitType.Negative; } catch { }
                p.Add("traço " + nm + " PERDIDO ~ \"" + (TraitFeel.TryGetValue(kv.Key, out var f) ? f[1] : bad ? "algo pesado saiu de mim (" + nm + ")" : "algo meu foi arrancado (" + nm + ")") + "\"");
            }
            if (p.Count == 0) return null;
            return "SENSAÇÕES (fale de CADA uma em 1ª pessoa, frase curta, com suas palavras, SEM números; os exemplos são só ideias): " + string.Join("; ", p);
        }

        /// <summary>"vida máxima 100 -> 150, dano 20 -> 30" (Portuguese, for RESULTADO).</summary>
        public static string Diff(Snap b, Snap a)
        {
            if (!b.Ok || !a.Ok) return null;
            var p = new List<string>();
            void D(string n, float x, float y) { if (Math.Abs(x - y) >= 0.01f) p.Add(n + " " + F(x) + " -> " + F(y)); }
            D("vida máxima", b.MaxHp, a.MaxHp); D("dano", b.Dmg, a.Dmg); D("velocidade", b.Spd, a.Spd);
            D("armadura", b.Arm, a.Arm); D("velocidade de ataque", b.AtkSpd, a.AtkSpd); D("expectativa de vida", b.Life, a.Life);
            return p.Count == 0 ? null : string.Join(", ", p);
        }
    }
}
