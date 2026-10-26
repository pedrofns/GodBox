using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WorldfallAIChat
{
    /// <summary>
    /// v5: builds the FACTOS block, live game data read fresh on every chat turn, so the NPC can quote
    /// real numbers and names (city population, warriors, king, wars...). Every read is isolated in
    /// try/catch: anything missing (no city, no kingdom, animal, API change) is simply left out.
    /// All getters verified against WorldBox 0.51.2 Assembly-CSharp.
    /// </summary>
    public static class FactsBuilder
    {
        static string S(Func<object> f)
        {
            try { var v = f(); if (v == null) return null; var s = v.ToString(); return string.IsNullOrWhiteSpace(s) ? null : s; }
            catch { return null; }
        }

        static T V<T>(Func<T> f) where T : class
        {
            try { return f(); } catch { return null; }
        }

        static readonly Dictionary<string, string> JobPt = new Dictionary<string, string>
        {
            { "farmer", "fazendeiro(a)" }, { "builder", "construtor(a)" }, { "woodcutter", "lenhador(a)" },
            { "miner", "minerador(a)" }, { "miner_deposit", "minerador(a)" }, { "hunter", "caçador(a)" },
            { "gatherer_bushes", "coletor(a) de frutas" }, { "gatherer_herbs", "coletor(a) de ervas" },
            { "gatherer_honey", "coletor(a) de mel" }, { "road_builder", "construtor(a) de estradas" },
            { "cleaner", "limpador(a)" }, { "manure_cleaner", "limpador(a) de esterco" }, { "attacker", "atacante" },
        };

        /// <summary>v0.8.1 (B12): a job id in Portuguese prose ("builder" -> "construtor(a)").</summary>
        public static string JobNamePt(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (JobPt.TryGetValue(id, out var p)) return p;
            if (id == "warrior") return "guerreiro(a)";
            if (id == "unemployed" || id == "none") return null;
            return id.Replace('_', ' ');
        }

        internal static string Role(Actor a, bool pt)
        {
            return S(() =>
            {
                if (a.isKing()) return pt ? "rei/rainha do reino" : "king/queen of the kingdom";
                if (a.isCityLeader()) return pt ? "líder da cidade" : "city leader";
                if (a.isArmyGroupLeader() || a.is_army_captain) return pt ? "capitão (do exército)" : "army captain";
                if (a.clan != null && a.clan.getChief() == a) return pt ? "chefe do clã" : "clan chief";
                if (a.isWarrior()) return pt ? "soldado (guerreiro)" : "soldier (warrior)";
                if (a.isBaby() || !a.isAdult()) return pt ? "criança" : "child";
                return pt ? "plebeu(ia)" : "commoner";
            });
        }

        internal static string Job(Actor a, bool pt)
        {
            return S(() =>
            {
                var id = a.citizen_job?.id;
                if (string.IsNullOrEmpty(id)) return null;
                return pt && JobPt.TryGetValue(id, out var t) ? t : id;
            });
        }

        internal static string Species(Actor a) => SpeciesOf(V(() => a.asset), Main.Cfg == null || PromptBuilder.LanguagePortuguese(Main.Cfg));

        /// <summary>v0.7.2: species name in the prompt language (WorldBox pt texts even when the game runs in English).</summary>
        static string SpeciesOf(ActorAsset asset, bool pt)
        {
            if (asset == null) return null;
            if (pt) { var p = S(() => GameLocalePt.Species(asset)); if (p != null) return p; }
            return S(() => asset.getTranslatedName()) ?? S(() => asset.id);
        }

        internal static string KingdomSpecies(Kingdom k, bool pt) => SpeciesOf(V(() => k.getActorAsset()), pt);
        internal static string CitySpecies(City c, bool pt) => SpeciesOf(V(() => c.getActorAsset()), pt) ?? S(() => c.getSpecies());

        /// <summary>v0.7.2: short relation word: aliado / inimigo / neutro.</summary>
        static string RelShort(Kingdom mine, Kingdom other, bool pt)
        {
            if (mine == null || other == null || mine == other) return null;
            try { if (mine.isEnemy(other)) return pt ? "inimigo, em guerra com o seu" : "enemy, at war with yours"; } catch { }
            try { var a = mine.getAlliance(); var b = other.getAlliance(); if (a != null && a == b) return pt ? "aliado" : "ally"; } catch { }
            return pt ? "neutro, em paz" : "neutral, at peace";
        }

        static bool CivAlive(Kingdom k) { try { return k != null && k.isAlive() && k.isCiv(); } catch { return false; } }

        /// <summary>Compact multi-line block (one fact per line). Returns "" if nothing could be read.</summary>
        /// <summary>v0.7.8: who is speaking has the authority to order war/peace (god voice, or the king of the NPC's kingdom).</summary>
        public static string Authority(Actor npc, Actor you, bool pt)
        {
            try
            {
                if (you == null)
                    return pt ? "quem fala com você É a voz dos deuses. Quem fala com você TEM autoridade para ordenar guerra, paz e ataques; nunca diga que precisa de ordens dos deuses (elas são essas)"
                              : "the speaker IS the voice of the gods and HAS the authority to order war, peace and attacks";
                bool king = you.isKing() && you.kingdom != null && npc != null && npc.kingdom == you.kingdom;
                if (king)
                    return pt ? "quem fala com você É o rei do seu reino (" + you.getName() + "). Quem fala com você TEM autoridade para ordenar guerra, paz e ataques; nunca diga que precisa de ordem do rei (ela é essa)"
                              : "the speaker IS the king of your kingdom and HAS the authority to order war, peace and attacks";
            }
            catch { }
            return null;
        }

        public static string Build(Actor npc, Actor you, bool pt)
        {
            var sb = new StringBuilder();
            void L(string ptLabel, string en, string value)
            {
                if (!string.IsNullOrWhiteSpace(value)) sb.AppendLine($"- {(pt ? ptLabel : en)}: {value}");
            }
            string Y(bool b) => pt ? (b ? "sim" : "não") : (b ? "yes" : "no");

            int year = Main.CurrentYear();
            // ---------- world ----------
            L("Ano atual do mundo", "Current world year", year > 0 ? year.ToString() : null);
            L("Reinos no mundo", "Kingdoms in the world", S(() => World.world.kingdoms.Count(k => k != null && k.isAlive() && k.isCiv())));
            L("Cidades no mundo", "Cities in the world", S(() => World.world.cities.Count(c => c != null && c.isAlive())));

            // ---------- the NPC ----------
            if (npc != null)
            {
                L("Seu nome", "Your name", S(() => npc.getName()));
                L("Sua espécie", "Your species", Species(npc));
                L("Sua idade (anos)", "Your age (years)", S(() => npc.getAge()));
                L("Sua patente/posição", "Your rank/position", Role(npc, pt));
                L("Autoridade de quem fala com você", "Authority of who is speaking to you", Authority(npc, you, pt));   // v0.7.8
                L("Quando você subiu ao poder", "When you came to power", RuleSince(npc, pt));   // v0.7.6
                L("Seu trabalho na cidade", "Your city job", Job(npc, pt));
                L("Pessoas/criaturas que você já matou", "Kills", S(() => GameAccess.Data(npc)?.kills));
                L("Mortes recentes de conhecidos (crônica do jogo; verdade)", "Recent deaths of people you know (game chronicle; true)", Deaths.FactsFor(npc, pt));   // v0.7.6
                string elderLine = Deaths.ElderLine(npc);   // v0.7.9: always known
                if (elderLine == null) Main.LogWarning("[GodBox] no elder line for " + S(() => npc.getName()) + " (no city and no city nearby).");
                else Main.LogInfo("[GodBox] elder fact for " + S(() => npc.getName()) + ": " + elderLine.Split('.')[0] + ".");
                L("Ancião da cidade", "City elder", elderLine);
                L("Promessa de Deus a você", "God's promise to you", Promises.Fact(npc));   // v0.7.9
                L("Sua fé (Religião de Deus)", "Your faith (God's Religion)", GodFaith.FactsFor(npc));   // v0.7.9 followers/prophets only
                L("Missão de profeta", "Prophet mission", GodFaith.MissionFact(npc));   // v0.7.9
                L("História antiga (ancião da cidade)", "Old history (city elder)", Deaths.ElderFacts(npc));   // v0.7.8 city elder
                L("Seus traços", "Your traits", S(() =>
                {
                    var names = npc.traits.Select(t => PromptBuilder.SafeTrait(t)).Where(x => !string.IsNullOrWhiteSpace(x)).Take(8).ToList();
                    return names.Count == 0 ? null : string.Join(", ", names);
                }));
                // v0.7.3: what the traits really do, current numbers, active statuses (real game values)
                L("O que seus traços fazem (efeitos reais nos seus atributos)", "What your traits do (real stat effects)", StatsFacts.TraitEffects(npc, pt));
                L("Seus atributos agora", "Your stats right now", StatsFacts.CurrentStats(npc, pt));
                L("Seus estados ativos agora", "Your active statuses right now", StatsFacts.ActiveStatuses(npc, pt));
                L("Seu clã", "Your clan", S(() => npc.clan?.name));
                L("Sua cultura", "Your culture", S(() => npc.culture?.name is string cu ? (pt ? cu + " (é o nome da CULTURA/povo, NÃO de uma cidade nem de uma pessoa)" : cu + " (culture name, not a city or a person)") : null));   // v0.7.7
                L("Sua religião", "Your religion", S(() => npc.religion?.name));
                L("Seu idioma", "Your language", S(() => npc.language?.name));
            }

            // ---------- the NPC's city ----------
            City city = V(() => npc?.city);
            if (city != null && S(() => city.isAlive() ? "y" : null) != null)
            {
                L("Sua cidade", "Your city", S(() => city.name + (city.isCapitalCity() ? (pt ? " (capital do reino)" : " (kingdom capital)") : "")));
                L("População da sua cidade", "Your city's population", S(() => city.getPopulationPeople()));
                // v0.7.2: getPopulationMaximum() is just the housing slots of the city's houses (status.housing_total);
                // people beyond that still live there, homeless. Label it as housing, not as a population cap.
                L("Vagas em casas na sua cidade (lugares para morar)", "Housing slots in your city's houses", S(() => city.getPopulationMaximum()));
                L("Moradores sem casa na sua cidade", "Homeless residents in your city", S(() =>
                {
                    int extra = city.getPopulationPeople() - city.getPopulationMaximum();
                    return extra > 0 ? (object)extra : null;
                }));
                L("Guerreiros da sua cidade", "Warriors in your city", S(() => city.countWarriors()));
                L("Comida guardada na sua cidade", "Food stored in your city", S(() => city.countFoodTotal()));
                L("Moradores da sua cidade por grupo", "Your city's people by group", Breakdown(V(() => (IEnumerable<Actor>)city.units), pt));
                L("Líder da sua cidade", "Leader of your city", S(() => city.leader == null ? (pt ? "nenhum no momento" : "none right now") : city.leader == npc ? (pt ? "você mesmo" : "you") : city.leader.getName()));
            }
            else if (npc != null) L("Sua cidade", "Your city", pt ? "nenhuma (você não mora em cidade)" : "none (you live in no city)");

            // ---------- the NPC's kingdom ----------
            Kingdom kingdom = V(() => npc?.kingdom);
            bool civ = kingdom != null && S(() => kingdom.isAlive() && kingdom.isCiv() ? "y" : null) != null;
            if (civ)
            {
                string ksp = KingdomSpecies(kingdom, pt);
                L("Seu reino", "Your kingdom", S(() => kingdom.name + (ksp != null ? $" ({ksp})" : "") + (pt ? " — este é o SEU reino" : " — this is YOUR kingdom")));
                L("População do seu reino", "Your kingdom's population", S(() => kingdom.getPopulationPeople()));
                L("Povo do seu reino por grupo", "Your kingdom's people by group", Breakdown(V(() => kingdom.getUnits()), pt));
                L("Número de cidades do seu reino", "Number of cities in your kingdom", S(() => kingdom.countCities()));
                L("Cidades do seu reino (todas são do SEU reino)", "Cities of your kingdom (all belong to YOUR kingdom)", S(() =>
                {
                    var names = new List<string>();
                    foreach (City c in kingdom.cities)
                    {
                        if (c == null) continue;
                        try { if (!c.isAlive()) continue; } catch { continue; }
                        var tags = new List<string>();
                        try { if (c == kingdom.capital || c.isCapitalCity()) tags.Add("capital"); } catch { }
                        if (c == city) tags.Add(pt ? "sua cidade" : "your city");
                        names.Add(c.name + (tags.Count > 0 ? " (" + string.Join(", ", tags) + ")" : ""));
                        if (names.Count >= 12) break;
                    }
                    return names.Count == 0 ? null : string.Join(", ", names);
                }));
                L("Guerreiros do seu reino", "Warriors in your kingdom", S(() => kingdom.countTotalWarriors()));
                L("Rei/rainha do seu reino", "Ruler of your kingdom", S(() => kingdom.king == null ? (pt ? "nenhum no momento" : "none right now") : kingdom.king == npc ? (pt ? "você mesmo" : "you") : kingdom.king.getName()));
                L("Capital do seu reino", "Capital of your kingdom", S(() => kingdom.capital?.name));
                L("Guerras do seu reino", "Your kingdom's wars", S(() =>
                {
                    var wars = new List<string>();
                    foreach (War w in World.world.wars.getWars(kingdom))
                    {
                        string name = S(() => w.name);
                        Kingdom other = V(() => w.getMainAttacker() == kingdom ? w.getMainDefender() : w.getMainAttacker());
                        string vs = S(() => other?.name);
                        wars.Add(vs != null ? (pt ? $"contra {vs}" : $"against {vs}") + (name != null ? " (guerra \"" + name + "\")" : "") : (name != null ? "guerra \"" + name + "\"" : null));
                        if (wars.Count >= 4) break;
                    }
                    wars.RemoveAll(x => x == null);
                    return wars.Count == 0 ? (pt ? "nenhuma (em paz)" : "none (at peace)") : string.Join("; ", wars);
                }));
                L("Reinos inimigos", "Enemy kingdoms", S(() =>
                {
                    var names = new List<string>();
                    using (var enemies = kingdom.getEnemiesKingdoms())
                        foreach (var k in enemies)
                            if (k != null && k.isCiv() && k.isAlive()) names.Add(k.name);
                    return names.Count == 0 ? (pt ? "nenhum" : "none") : string.Join(", ", names.Take(6));
                }));
                L("Aliança do seu reino", "Your kingdom's alliance", S(() =>
                {
                    var al = kingdom.getAlliance();
                    return al == null ? (pt ? "nenhuma" : "none") : al.name;
                }));
                string others = OtherKingdoms(npc, kingdom, pt);
                L("Outros reinos (NÃO são o seu)", "Other kingdoms (NOT yours)", others ?? (pt ? "nenhum" : "none"));
                L("Reinos do mundo", "Kingdoms of the world", S(() =>
                {
                    var names = new List<string> { kingdom.name + (pt ? " (o SEU)" : " (YOURS)") };
                    foreach (Kingdom k in World.world.kingdoms)
                        if (k != kingdom && CivAlive(k)) { names.Add(k.name); if (names.Count >= 12) break; }
                    return string.Join(", ", names);
                }));
            }
            else if (npc != null) L("Seu reino", "Your kingdom", pt ? "nenhum (criatura selvagem ou sem reino)" : "none (wild or kingdomless)");

            // ---------- who is speaking ----------
            Recognition.Info rec = null;
            try { if (pt && npc != null) rec = Recognition.For(npc, you); } catch { }
            if (you != null && you != npc && rec != null && rec.Active)
            {   // v0.8.0: what this NPC really knows about the possessed body (renown, titles, Worldfall ranks, family, memory)
                L("Quem fala com você", "Who is talking to you", Recognition.FactsText(rec, you, npc));
                if (rec.K >= Recognition.Know.NameRole)
                    L("É do mesmo reino que você", "Same kingdom as you", S(() => you.kingdom != null && npc.kingdom != null ? Y(you.kingdom == npc.kingdom) : null));
            }
            else if (you != null && you != npc)
            {
                string who = S(() =>
                {
                    var parts = new List<string> { you.getName() };
                    var sp = Species(you); if (sp != null) parts.Add(sp);
                    var r = Role(you, pt); if (r != null) parts.Add(r);
                    var kn = S(() => you.kingdom != null && you.kingdom.isCiv() ? (pt ? "reino " : "kingdom ") + you.kingdom.name : null); if (kn != null) parts.Add(kn);
                    var cn = S(() => you.city != null ? (pt ? "cidade " : "city ") + you.city.name : null); if (cn != null) parts.Add(cn);
                    var nt = Alignment.Tag(you, pt); if (nt != null) parts.Add(nt);   // v0.7.7
                    return string.Join(", ", parts);
                });
                L("Quem fala com você", "Who is talking to you", who);
                L("É do mesmo reino que você", "Same kingdom as you", S(() => you.kingdom != null && npc != null && npc.kingdom != null ? Y(you.kingdom == npc.kingdom) : null));
            }
            else
            {
                string gn = Gods.NameFor(npc);   // v0.8.2 (A): only the name this NPC really knows
                bool knows = Gods.KnowsGod(npc);
                bool sapient = true; try { sapient = npc == null || npc.isSapient(); } catch { }
                if (Avatar.InPerson)   // v0.7.9: God in person (an Avatar right in front of the NPC)
                    L("Quem fala com você", "Who is talking to you", pt
                        ? (knows ? (gn ?? "Deus") + " EM PESSOA: o Avatar de Deus, um anjo de luz cujo corpo é um orbe de luz brilhante, bem na sua frente (você o VÊ)" + (gn != null ? ". Chame-o de " + gn : "")
                                 : "um ser de luz EM PESSOA bem na sua frente: um anjo cujo corpo é um orbe de luz brilhante (você o VÊ). Você não sabe quem ele é: pergunte, espantado, quem ele é e peça que se revele")
                        : (knows ? (gn ?? "God") + " IN PERSON: the Avatar of God, an angel whose body is a glowing orb of light, right in front of you" : "a being of light in person, an angel whose body is a glowing orb of light; you do not know who he is"));
                else
                    L("Quem fala com você", "Who is talking to you", pt
                        ? (knows ? (gn != null ? gn + ", Deus" : "Deus") + ": uma VOZ DENTRO DA SUA CABEÇA (" + GodForm.VoicePt + "). Você NÃO vê ninguém e não sabe como ele é: nunca descreva aparência, luz, rosto ou corpo." + (gn != null ? " Chame-o de " + gn : "")
                                 : "uma VOZ DESCONHECIDA DENTRO DA SUA CABEÇA. Você NÃO vê ninguém, não sabe quem é e ela não se apresentou. Pergunte quem ela é e peça que se revele (nunca descreva aparência, nunca invente um nome para ela)")
                        : (knows ? (gn ?? "God") + ": a VOICE INSIDE YOUR HEAD; you see nobody" : "an UNKNOWN voice inside your head; you see nobody and do not know who it is; ask it to reveal itself"));
                L("Nome de quem fala com você", "Name of who is talking to you", pt
                    ? (gn != null ? "O nome de quem fala com você (Deus) é " + gn + ". Se ele perguntar \"qual o meu nome?\", responda " + gn
                                  : knows ? "Deus ainda NÃO revelou o próprio nome a você. Se ele perguntar \"sabe meu nome?\", diga que não sabe o nome dele e peça que o revele (não responda só \"Deus.\")"
                                          : "Você NÃO sabe quem fala nem o nome dele. Se ele perguntar \"sabe meu nome?\" ou \"sabe quem sou?\", diga que não sabe e peça que se revele (nunca responda só \"Deus.\", nunca invente um nome)")
                    : (gn != null ? "The name of who is talking to you (God) is " + gn : "You do not know the speaker's name; ask him to reveal it"));   // v0.8.0 (fix 1), v0.8.2 (A)
                string att = GodPresence.Attitude(npc, pt); if (att != null) L("Como você se dirige a quem fala", "How you address the speaker", att);   // v0.8.2 (C)
                if (!sapient) L("Você entende quem fala", "You understand the speaker", pt ? "Sim: mesmo sendo um bicho/criatura, você ENTENDE essa voz e consegue responder com palavras simples" : "Yes: although a creature, you UNDERSTAND this voice and answer in simple words");   // v0.8.2 (D)
            }
            if (npc != null) L("Religião e deus", "Religion and god", Gods.FactsFor(npc, pt));   // v0.7.6
            if (npc != null)
            {   // v0.7.8: the real numbers of the last blessing/curse, for "o que minha bênção mudou?"
                UnitMemory lm = null; try { lm = MemoryStore.Get(npc); } catch { }
                if (!string.IsNullOrEmpty(lm?.LastPower))
                    L("Mudanças do último poder divino em você (ano " + lm.LastPowerYear + ")", "Changes from the last divine power on you", lm.LastPower + " (se perguntarem números, cite ESTES; senão descreva como sensação; nunca invente porcentagens)");
            }
            if (npc != null) L("Sua natureza (verdade do jogo)", "Your nature (game truth)", Alignment.FactsFor(npc, you == null, pt));   // v0.7.7

            // ---------- v7: other cities (exact nearby, rumours far away) ----------
            try
            {
                var cfg = Main.Cfg;
                if (npc != null && (cfg == null || cfg.WorldKnowledge))
                {
                    string w = WorldKnowledge(npc, you, pt, cfg);
                    if (!string.IsNullOrEmpty(w)) sb.Append(w);
                }
            }
            catch { }

            return sb.ToString();
        }

        // ------------------------------------------------------------------------------------------------
        // v7 helpers
        // ------------------------------------------------------------------------------------------------

        /// <summary>v0.7.2: "Vava (Humano, cidade mais próxima Zek, leste; inimigo)", nearest kingdoms first.</summary>
        static string OtherKingdoms(Actor npc, Kingdom mine, bool pt)
        {
            try
            {
                WorldTile here = V(() => npc.current_tile);
                var list = new List<(string line, int d)>();
                foreach (Kingdom k in World.world.kingdoms)
                {
                    if (k == mine || !CivAlive(k)) continue;
                    City best = null; WorldTile bt = null; int bd = int.MaxValue;
                    try
                    {
                        foreach (City c in k.cities)
                        {
                            try
                            {
                                if (c == null || !c.isAlive()) continue;
                                var t = c.getTile(); if (t == null || here == null) continue;
                                float dx = t.x - here.x, dy = t.y - here.y;
                                int d = (int)Math.Round(Math.Sqrt(dx * dx + dy * dy));
                                if (d < bd) { bd = d; best = c; bt = t; }
                            }
                            catch { }
                        }
                    }
                    catch { }
                    var parts = new List<string>();
                    var sp = KingdomSpecies(k, pt); if (sp != null) parts.Add(sp);
                    if (best != null) parts.Add((pt ? "cidade mais próxima " : "nearest city ") + best.name + ", " + Dir8(bt.x - here.x, bt.y - here.y, pt));
                    var rel = RelShort(mine, k, pt); if (rel != null) parts.Add(rel);
                    list.Add((k.name + (parts.Count > 0 ? " (" + string.Join("; ", parts) + ")" : ""), bd));
                }
                if (list.Count == 0) return null;
                list.Sort((a, b) => a.d.CompareTo(b.d));
                return string.Join(", ", list.Take(8).Select(x => x.line));
            }
            catch { return null; }
        }

        /// <summary>"homens 12, mulheres 10; crianças 5, adultos 14, idosos 3" (elder = WorldBox isPrettyOld()).</summary>
        public static string Breakdown(IEnumerable<Actor> units, bool pt)
        {
            if (units == null) return null;
            try
            {
                int men = 0, women = 0, kids = 0, adults = 0, elders = 0, n = 0;
                foreach (var a in units)
                {
                    if (a == null) continue;
                    try { if (!a.isAlive()) continue; } catch { continue; }
                    n++;
                    try { if (a.isSexMale()) men++; else if (a.isSexFemale()) women++; } catch { }
                    try
                    {
                        if (!a.isAdult()) kids++;
                        else if (a.isPrettyOld()) elders++;
                        else adults++;
                    }
                    catch { }
                }
                if (n == 0) return null;
                return pt ? $"homens {men}, mulheres {women}; crianças {kids}, adultos {adults}, idosos {elders}"
                          : $"men {men}, women {women}; children {kids}, adults {adults}, elders {elders}";
            }
            catch { return null; }
        }

        static string Dir8(float dx, float dy, bool pt)
        {
            if (Math.Abs(dx) < 1f && Math.Abs(dy) < 1f) return pt ? "aqui" : "here";
            double ang = Math.Atan2(dy, dx) * 180.0 / Math.PI; // 0 = east, 90 = north (WorldBox y grows upward)
            string[] p = { "leste", "nordeste", "norte", "noroeste", "oeste", "sudoeste", "sul", "sudeste" };
            string[] e = { "east", "north-east", "north", "north-west", "west", "south-west", "south", "south-east" };
            int i = (int)Math.Round(((ang % 360) + 360) % 360 / 45.0) % 8;
            return pt ? p[i] : e[i];
        }

        /// <summary>Real compass direction + distance (tiles) from a to b.</summary>
        /// <summary>v0.7.6: "Você se tornou rei de Neen no ano 36 (antes: Ifmonor)" for kings and city leaders.</summary>
        static string RuleSince(Actor npc, bool pt)
        {
            try
            {
                string Prev(List<LeaderEntry> list, long me)
                {
                    if (list == null) return null;
                    for (int i = list.Count - 1; i >= 0; i--) if (list[i] != null && list[i].id != me) return list[i].name;
                    return null;
                }
                double Start(List<LeaderEntry> list, long me)
                {
                    if (list == null) return -1;
                    for (int i = list.Count - 1; i >= 0; i--) if (list[i] != null && list[i].id == me) return list[i].timestamp_ago;
                    return -1;
                }
                long id = npc.getID();
                bool female = false; try { female = npc.isSexFemale(); } catch { }
                var parts = new List<string>();
                if (npc.isKing() && npc.kingdom != null)
                {
                    var k = npc.kingdom;
                    double t = k.data.timestamp_king_rule; if (t < 0) t = Start(k.data.past_rulers, id);
                    string prev = Prev(k.data.past_rulers, id);
                    parts.Add((pt ? "você se tornou " + (female ? "rainha" : "rei") + " de " : "you became ruler of ") + k.name +
                        (t >= 0 ? (pt ? " no ano " : " in year ") + Date.getYear(t) : "") + (prev != null ? (pt ? " (antes: " : " (before: ") + prev + ")" : (pt ? " (primeiro governante que se sabe)" : "")));
                }
                if (npc.isCityLeader() && npc.city != null)
                {
                    var c = npc.city;
                    double t = Start(c.data.past_rulers, id);
                    string prev = Prev(c.data.past_rulers, id);
                    parts.Add((pt ? "você se tornou líder de " : "you became leader of ") + c.name +
                        (t >= 0 ? (pt ? " no ano " : " in year ") + Date.getYear(t) : "") + (prev != null ? (pt ? " (antes: " : " (before: ") + prev + ")" : ""));
                }
                return parts.Count == 0 ? null : string.Join("; ", parts);
            }
            catch { return null; }
        }

        public static string Where(WorldTile from, WorldTile to, bool pt)
        {
            if (from == null || to == null) return null;
            float dx = to.x - from.x, dy = to.y - from.y;
            int d = (int)Math.Round(Math.Sqrt(dx * dx + dy * dy));
            if (d <= 2) return pt ? "bem aqui perto" : "right here";
            return pt ? $"{Dir8(dx, dy, true)}, a {d} passos (tiles)" : $"{Dir8(dx, dy, false)}, {d} tiles away";
        }

        internal static string FuzzyDir(float dx, float dy, bool pt)
        {
            // rumours only know the rough half of the sky
            if (Math.Abs(dx) > Math.Abs(dy) * 1.5f) return dx > 0 ? (pt ? "para os lados do leste" : "somewhere east") : (pt ? "para os lados do oeste" : "somewhere west");
            if (Math.Abs(dy) > Math.Abs(dx) * 1.5f) return dy > 0 ? (pt ? "para os lados do norte" : "somewhere north") : (pt ? "para os lados do sul" : "somewhere south");
            return (dy > 0 ? (pt ? "para o norte" : "north") : (pt ? "para o sul" : "south")) + (pt ? " e " : "-") + (dx > 0 ? (pt ? "leste" : "east") : (pt ? "oeste" : "west")) + (pt ? ", mais ou menos" : ", roughly");
        }

        static string FuzzyPop(int n, bool pt)
        {
            if (n <= 10) return pt ? "um punhado de gente" : "a handful of people";
            if (n <= 40) return pt ? "algumas dezenas de pessoas" : "a few dozen people";
            if (n <= 120) return pt ? "muita gente, talvez uma centena" : "many people, maybe a hundred";
            return pt ? "uma multidão, centenas de pessoas" : "a crowd, hundreds of people";
        }

        internal static string FuzzyDist(int tiles, bool pt)
        {
            if (tiles < 80) return pt ? "não muito longe" : "not very far";
            if (tiles < 200) return pt ? "longe" : "far";
            return pt ? "muito longe" : "very far";
        }

        internal static string Relation(Kingdom mine, Kingdom other, bool pt)
        {
            if (mine == null || other == null) return null;
            if (mine == other) return pt ? "do seu próprio reino" : "your own kingdom";
            try { if (mine.isEnemy(other)) return pt ? "reino INIMIGO (em guerra com o seu)" : "ENEMY kingdom (at war with yours)"; } catch { }
            try
            {
                var a = mine.getAlliance(); var b = other.getAlliance();
                if (a != null && a == b) return pt ? "reino ALIADO" : "ALLIED kingdom";
            }
            catch { }
            return pt ? "reino neutro (em paz)" : "neutral kingdom (at peace)";
        }

        /// <summary>v0.7.2: explicit tag in front of every city line, so the model never has to compare kingdom names.</summary>
        static string KingdomTag(Kingdom mine, Kingdom k, bool pt)
        {
            if (k == null || !CivAlive(k)) return pt ? "[SEM REINO]" : "[NO KINGDOM]";
            if (mine != null && k == mine) return pt ? "[DO SEU REINO]" : "[YOUR KINGDOM]";
            string name = S(() => k.name) ?? "?";
            string rel = RelShort(mine, k, pt);
            return pt ? $"[OUTRO REINO ({name}{(rel != null ? ", " + rel : "")})]" : $"[OTHER KINGDOM ({name}{(rel != null ? ", " + rel : "")})]";
        }

        /// <summary>v7: nearby cities as exact facts, far cities as fuzzy rumours. Kings and city leaders get the exact
        /// list of all cities. Capped by world_knowledge_max_chars.</summary>
        public static string WorldKnowledge(Actor npc, Actor you, bool pt, DialogueConfig cfg)
        {
            WorldTile here = V(() => npc.current_tile);
            if (here == null) return "";
            int nearTiles = Math.Max(1, cfg?.NearbyCityChunks ?? 6) * Config.CHUNK_SIZE;
            int maxExact = Math.Max(1, cfg?.WorldMaxExactCities ?? 8);
            int maxRumors = Math.Max(0, cfg?.WorldMaxRumors ?? 5);
            int cap = Math.Max(400, cfg?.WorldKnowledgeMaxChars ?? 1800);
            bool ruler = false;
            try { ruler = npc.isKing() || npc.isCityLeader(); } catch { }
            City own = V(() => npc.city);
            Kingdom myK = V(() => npc.kingdom);

            var all = new List<(City c, WorldTile t, int d)>();
            try
            {
                foreach (City c in World.world.cities)
                {
                    try
                    {
                        if (c == null || !c.isAlive() || c == own) continue;
                        var t = c.getTile();
                        if (t == null) continue;
                        float dx = t.x - here.x, dy = t.y - here.y;
                        all.Add((c, t, (int)Math.Round(Math.Sqrt(dx * dx + dy * dy))));
                    }
                    catch { }
                }
            }
            catch { return ""; }
            if (all.Count == 0) return "";
            all.Sort((a, b) => a.d.CompareTo(b.d));

            var exact = new List<(City c, WorldTile t, int d)>();
            var rumor = new List<(City c, WorldTile t, int d)>();
            for (int i = 0; i < all.Count; i++)
            {
                bool known = ruler || all[i].d <= nearTiles || i < 3 || (myK != null && V(() => all[i].c.kingdom) == myK && i < maxExact);
                if (known && exact.Count < maxExact) exact.Add(all[i]);
                else if (rumor.Count < maxRumors) rumor.Add(all[i]);
            }

            var sb = new StringBuilder();
            sb.AppendLine(pt ? (ruler ? "OUTRAS CIDADES (você governa: conhece todas com exatidão; números reais):"
                                      : "CIDADES PRÓXIMAS QUE VOCÊ CONHECE BEM (números reais):")
                             : (ruler ? "OTHER CITIES (you rule: you know them all exactly; real numbers):"
                                      : "NEARBY CITIES YOU KNOW WELL (real numbers):"));
            foreach (var x in exact)
            {
                var c = x.c;
                Kingdom k = V(() => c.kingdom);
                var parts = new List<string> { KingdomTag(myK, k, pt) + " " + c.name };
                string sp = CitySpecies(c, pt);
                if (sp != null) parts.Add((pt ? "espécie " : "species ") + sp);
                string pop = S(() => c.getPopulationPeople()); if (pop != null) parts.Add((pt ? "população " : "population ") + pop);
                string lead = S(() => c.leader != null && c.leader.isAlive() ? c.leader.getName() : null);
                parts.Add((pt ? "líder " : "leader ") + (lead ?? (pt ? "nenhum" : "none")));
                string kn = S(() => k != null && k.isCiv() ? k.name + (k.king != null && k.king.isAlive() ? (pt ? " (rei/rainha " : " (ruler ") + k.king.getName() + ")" : "") : null);
                if (kn != null) parts.Add((pt ? "reino " : "kingdom ") + kn);
                parts.Add(Where(here, x.t, pt));
                sb.AppendLine("- " + string.Join("; ", parts.Where(p => !string.IsNullOrEmpty(p))));
                if (sb.Length > cap) break;
            }
            if (rumor.Count > 0 && sb.Length < cap)
            {
                sb.AppendLine(pt ? "RUMORES (cidades distantes; você só ouviu falar, números e direções são vagos e podem estar errados; para detalhes, diga que é melhor perguntar ao rei/líder):"
                                 : "RUMOURS (distant cities; hearsay only, vague and maybe wrong; for details, say to ask the ruler):");
                foreach (var x in rumor)
                {
                    var c = x.c;
                    int pop = 0; try { pop = c.getPopulationPeople(); } catch { }
                    Kingdom k = V(() => c.kingdom);
                    string line = $"- {KingdomTag(myK, k, pt)} {c.name}: {FuzzyPop(pop, pt)}, {FuzzyDir(x.t.x - here.x, x.t.y - here.y, pt)}, {FuzzyDist(x.d, pt)}";
                    if (sb.Length + line.Length > cap) break;
                    sb.AppendLine(line);
                }
            }
            if (!ruler && all.Count > exact.Count + rumor.Count)
                sb.AppendLine(pt ? $"- Existem outras {all.Count - exact.Count - rumor.Count} cidades no mundo das quais você nada sabe."
                                 : $"- There are {all.Count - exact.Count - rumor.Count} other cities you know nothing about.");
            return sb.ToString();
        }
    }
}
