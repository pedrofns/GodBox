using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldfallAIChat
{
    /// <summary>
    /// v0.7.7: malefic beings (enemies of God) and holy beings (devoted servants of God), derived from game data:
    ///  - malefic: the unit's species asset or kingdom asset carries the game's "evil" / "undead" / "demon" kingdom tags
    ///    (KingdomLibrary: evil_mage, necromancer, fire_skull, jumpy_skull), or its wild kingdom is one of the game's dark
    ///    mob kingdoms (undead = skeleton/ghost/zombies, demon, cold_one, necromancer, evil_mage, fire_skull, jumpy_skull),
    ///    or ActorAsset.unit_zombie, or the unit has the "evil" trait (shown in pt as Cruel/Malvado).
    ///  - holy: species asset white_mage or angle (angel) (KingdomLibrary: white_mage clones $TEMPLATE_MOB_VERY_GOOD$,
    ///    angle has the "good" tag). Other "good"-tagged mobs (druid, plague_doctor, fairy) are kind but not heavenly.
    /// Species-based malefic beats holy; a holy species with the evil trait counts as malefic (fallen).
    /// </summary>
    public enum Nature { Normal, Malefic, Holy }

    public static class Alignment
    {
        static readonly HashSet<string> DarkTags = new HashSet<string> { "evil", "undead", "demon" };
        static readonly HashSet<string> DarkKingdoms = new HashSet<string>
            { "evil_mage", "miniciv_evil_mage", "necromancer", "undead", "demon", "cold_one", "fire_skull", "jumpy_skull" };
        static readonly HashSet<string> HolySpecies = new HashSet<string> { "white_mage", "angle" };

        public static Nature Of(Actor a)
        {
            if (a == null) return Nature.Normal;
            try
            {
                var asset = a.asset;
                string sid = asset?.id ?? "";
                bool dark = false;
                try { if (asset != null && asset.unit_zombie) dark = true; } catch { }
                try { if (asset != null && DarkKingdoms.Contains(asset.kingdom_id_wild ?? "")) dark = true; } catch { }
                try { if (DarkKingdoms.Contains(sid)) dark = true; } catch { }
                try
                {
                    var ka = a.kingdom?.asset;
                    if (ka != null && (DarkKingdoms.Contains(ka.id ?? "") || (ka.list_tags != null && ka.list_tags.Overlaps(DarkTags)))) dark = true;
                }
                catch { }
                try
                {
                    var wild = asset != null && !string.IsNullOrEmpty(asset.kingdom_id_wild) ? AssetManager.kingdoms.get(asset.kingdom_id_wild) : null;
                    if (wild != null && wild.list_tags != null && wild.list_tags.Overlaps(DarkTags)) dark = true;
                }
                catch { }
                if (dark) return Nature.Malefic;
                bool evilTrait = false; try { evilTrait = a.hasTrait("evil"); } catch { }
                if (evilTrait && (Main.Cfg?.MaleficEvilTrait ?? true)) return Nature.Malefic;
                if (HolySpecies.Contains(sid)) return Nature.Holy;
            }
            catch { }
            return Nature.Normal;
        }

        public static bool IsMalefic(Actor a) => Of(a) == Nature.Malefic;
        public static bool IsHoly(Actor a) => Of(a) == Nature.Holy;

        public static string Why(Actor a)
        {
            try
            {
                var asset = a.asset;
                if (asset != null && asset.unit_zombie) return "morto-vivo (zumbi)";
                string k = asset?.kingdom_id_wild ?? "";
                if (k == "undead") return "morto-vivo";
                if (k == "demon" || asset?.id == "demon") return "demônio";
                if (k.Contains("evil_mage")) return "mago maligno";
                if (k == "necromancer") return "necromante";
                if (k == "cold_one") return "Gélido (cold one)";
                if (k == "fire_skull" || k == "jumpy_skull") return "caveira amaldiçoada";
                if (asset?.id == "angle") return "anjo";
                if (asset?.id == "white_mage") return "mago branco";
                if (a.hasTrait("evil")) return "alma cruel (traço evil)";
            }
            catch { }
            return null;
        }

        /// <summary>God as a malefic being calls him (never its own dark master).</summary>
        public static string GodFor(Actor npc, bool pt)
        {
            var n = Of(npc);
            string gn = Gods.NameFor(npc);
            if (n == Nature.Malefic) return gn != null ? gn : (pt ? "Deus" : "God");   // v0.7.8: he KNOWS it is God (his religion's god name)
            if (gn != null) return gn;
            return n == Nature.Holy ? (pt ? "o Senhor dos Céus" : "the Lord of the Heavens") : null;
        }

        /// <summary>FACTOS line for the NPC's own nature (null for normal units).</summary>
        public static string FactsFor(Actor npc, bool god, bool pt)
        {
            var n = Of(npc);
            string why = Why(npc);
            if (n == Nature.Normal) return null;
            if (n == Nature.Malefic)
            {
                string gn = null;
                try { gn = Gods.NameFor(npc); } catch { }
                bool noKingdom = true; try { noKingdom = npc.kingdom == null || !npc.kingdom.isCiv(); } catch { }
                string kq = noKingdom ? (pt ? " Você NÃO tem reino nem rei (diga isso se perguntarem; nunca invente um reino)." : " You have NO kingdom and no king.") : "";
                // v0.7.8 (fix 6 / steering a): he KNOWS the voice is God, hates him and defies him on purpose
                return pt
                    ? "Você é um ser maléfico" + (why != null ? " (" + why + ")" : "") + ". A voz do céu é DEUS" + (gn != null ? " (" + gn + ", o deus da sua religião)" : "") + " e você SABE disso: você o ODEIA e o desafia de propósito, sabendo quem ele é."
                      + " Nunca o chame de \"anjo caído\" nem de \"deus da voz do céu\", e nunca diga que \"só o rei ou os deuses podem\" mandar em você: quem fala É Deus, e você recusa porque quer." + kq + " "
                      + (god ? "NUNCA obedeça às ordens dele (o jogo recusa todas); zombe ou amaldiçoe-o do seu jeito; se for ameaçado, pode tremer, implorar ou barganhar, mas continua desafiando. Os poderes de Deus ainda funcionam em você (você os odeia e reage a eles com dor ou raiva)."
                             : "Os vivos te veem como servo das trevas.")
                    : "You are a malefic being" + (why != null ? " (" + why + ")" : "") + ". The sky voice is GOD" + (gn != null ? " (" + gn + ")" : "") + " and you KNOW it: you hate and defy him on purpose." + kq + " "
                      + (god ? "Never obey him (the game refuses every order); mock or curse him; God's powers still work on you." : "The living see you as a servant of darkness.");
            }
            string gname = GodFor(npc, pt);
            return pt
                ? "Você é um ser sagrado" + (why != null ? " (" + why + ")" : "") + ": um servo devoto de Deus. "
                  + (god ? "Fale com a voz do céu com reverência e chame-a de " + gname + "; obedeça quase sempre (só não faz o impossível), ofereça-se para servir, alegre-se com bênçãos e aceite maldições como provas de fé. Seres maléficos são seus inimigos: você aceita atacá-los por Deus."
                         : "Seres maléficos são seus inimigos. Os outros te veem como servo dos céus.")
                : "You are a holy being" + (why != null ? " (" + why + ")" : "") + ": a devoted servant of God. "
                  + (god ? "Address the sky voice reverently as " + gname + "; obey almost always, offer to serve, rejoice at blessings, accept curses as tests of faith. Malefic beings are your enemies." : "Malefic beings are your enemies.");
        }

        /// <summary>How others see a unit (for "Quem fala com você" and name lookups).</summary>
        public static string Tag(Actor a, bool pt)
        {
            var n = Of(a);
            if (n == Nature.Malefic) return pt ? "ser maléfico, servo das trevas" : "malefic being, servant of darkness";
            if (n == Nature.Holy) return pt ? "ser sagrado, servo dos céus" : "holy being, servant of the heavens";
            return null;
        }
    }
}
