using System;
using System.Collections.Generic;

namespace WorldfallAIChat
{
    /// <summary>v0.8.2 (B/C/D): how a being faces God. Voice = an inner voice in the head (nothing seen); Avatar = God in
    /// person (a light-orb angel, seen). NPCs never ask God for news: they ask whether they serve him well, ask for blessings,
    /// plead, thank or pray according to their faith and real situation. Every creature understands God.</summary>
    public static class GodPresence
    {
        static bool Has(Func<bool> f) { try { return f(); } catch { return false; } }

        /// <summary>v0.8.2 (K): a devout adherent of God's religion: follows it AND (blessed, or a prophet, or lives in a city that follows it).</summary>
        public static bool Devout(Actor npc)
        {
            var rel = GodPowers.CurrentReligion();
            if (rel == null || npc == null || !Has(() => npc.religion == rel)) return false;
            if (GodFaith.IsProphet(npc)) return true;
            return Has(() => npc.hasTrait("blessed")) || Has(() => npc.city != null && npc.city.getReligion() == rel);
        }

        /// <summary>FACTOS line: how this NPC addresses God (null for malefic beings, who keep their defiance rules).</summary>
        public static string Attitude(Actor npc, bool pt)
        {
            if (npc == null || Has(() => Alignment.IsMalefic(npc))) return null;
            bool sapient = !Has(() => !npc.isSapient());
            bool knows = Gods.KnowsGod(npc);
            bool cursed = Has(() => npc.hasStatus("cursed"));
            bool blessed = Has(() => npc.hasTrait("blessed"));
            bool prophet = GodFaith.IsProphet(npc);
            bool king = Has(() => npc.isKing());
            bool leader = Has(() => npc.isCityLeader());
            bool war = Has(() => npc.kingdom != null && npc.kingdom.hasEnemies());
            bool hurt = Has(() => npc.getHealth() < npc.getMaxHealth() / 3);
            var l = new List<string>();
            if (!sapient)
            {
                l.Add(pt ? "você é um bicho: fala do SEU mundo de criatura (fome, bando/ninhada, medo, território, caça, frio, sono), com frases curtas e simples; pode pedir comida, proteção ou paz para o seu bando"
                         : "you are a creature: speak of your own world (hunger, pack, fear, territory) in short simple words");
                return string.Join("; ", l) + ".";
            }
            l.Add(pt ? "NUNCA peça notícias a quem fala (nada de \"como está a cidade?\", \"o que há nos reinos?\", \"quais as novidades?\"): quem fala com você é um poder divino, não um viajante"
                     : "NEVER ask the speaker for news of cities or kingdoms");
            if (!knows)
            {
                l.Add(pt ? "você não sabe quem é: fica assombrado e pergunta quem é, se é um deus, o que quer de você" : "you do not know who it is: ask who it is and what it wants");
                return string.Join("; ", l) + ".";
            }
            if (cursed) l.Add(pt ? "você está AMALDIÇOADO (estado real): implore perdão e peça que a maldição seja retirada" : "you are CURSED: beg forgiveness and ask for the curse to be lifted");
            if (prophet) l.Add(pt ? "você é profeta de Deus: pergunte se o está servindo bem e o que ele deseja de você" : "you are God's prophet: ask whether you serve him well");
            if (king && war) l.Add(pt ? "você é rei e seu reino está em GUERRA: peça a Deus a vitória e proteção para seu povo" : "you are a king at war: ask God for victory");
            else if ((king || leader) && !war) l.Add(pt ? "você governa: peça bênção e prosperidade para seu povo" : "you rule: ask blessing for your people");
            if (hurt) l.Add(pt ? "você está muito ferido: peça cura" : "you are badly hurt: ask for healing");
            if (blessed && !cursed) l.Add(pt ? "você foi abençoado: agradeça" : "you are blessed: give thanks");
            if (l.Count == 1) l.Add(pt ? "fale com reverência, conforme sua fé e seu jeito: pergunte se o serve bem, peça uma bênção, agradeça ou reze" : "speak reverently: ask whether you serve him well, ask a blessing, thank or pray");
            return string.Join("; ", l) + ".";
        }

        /// <summary>Prompt rule (replaces "animals answer only with sounds") when God talks: every being understands God.</summary>
        public static string CreatureRule(Actor npc, bool god, bool pt)
        {
            if (god)
                return pt ? "- DEUS FALA COM TODOS OS SERES: se você é um animal ou criatura sem mente, mesmo assim ENTENDE Deus e responde com palavras simples, do ponto de vista da sua criatura (instintos, fome, bando, medo, território); frases curtas, sem palavras difíceis. Com Deus você não fala só com sons."
                          : "GOD TALKS WITH EVERY BEING: if you are an animal or mindless creature, you still UNDERSTAND God and answer in simple words from your creature's point of view (instincts, hunger, pack, fear, territory).";
            return pt ? "- Se você não é inteligente (animal ou criatura sem mente), responda só com sons, gestos e pensamentos simples."
                      : "If you are not intelligent (an animal or mindless creature), answer only with sounds, body language and simple instinctive thoughts.";
        }

        /// <summary>Prompt rule for the god VOICE (not in person): an inner voice, nothing to see.</summary>
        public static string VoiceRule(bool pt) => pt
            ? "UMA VOZ DENTRO DA SUA CABEÇA: quem fala com você é uma voz que ecoa na sua mente. Você NÃO vê ninguém: nunca descreva aparência, luz, rosto, asas, roupas ou corpo de quem fala, nunca diga que ele está \"na sua frente\" ou que você o vê. Você pode olhar ao redor procurando a voz, mas não há ninguém."
            : "A VOICE INSIDE YOUR HEAD: the speaker is a voice echoing in your mind. You see NOBODY: never describe the speaker's appearance, light, face or body.";
    }
}
