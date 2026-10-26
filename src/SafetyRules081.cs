using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace WorldfallAIChat
{
    /// <summary>v0.8.1: more pure text rules (no game types; unit-tested outside the game).</summary>
    public static partial class SafetyRules
    {
        const RegexOptions CI = RegexOptions.CultureInvariant;

        // ---------------- B2a: common Portuguese words are never names ----------------
        static readonly HashSet<string> CommonWords = new HashSet<string>((@"
ore orem oro orar ora reze rezem rezo rezar reza pregue preguem prego pregar prega venha venham vem vir va vao volte voltem
olhe olhem escute escutem ouca oucam oucam faca facam faz fazer seja sejam ajude ajudem salve salvem abencoe abencoem cure curem
siga sigam pare parem espere esperem deixe deixem veja vejam acredite acreditem creia creiam obedeca obedecam adore adorem
converta convertam construa construam erga ergam ensine ensinem diga digam fale falem conte contem traga tragam leve levem
mate matem morra morram lute lutem corra corram ande andem durma coma beba trabalhe trabalhem descanse ame amem perdoe
templo igreja palavra profeta profetisa oracao prece fe ceu inferno alma senhor senhora majestade criador
sim nao oi ola ei bom boa dia noite tarde obrigado obrigada favor agora aqui ali la hoje amanha ontem sempre nunca
deus deuses rei rainha lider cidade reino vila aldeia povo gente todos todo tudo nada alguem ninguem mundo terra
eu voce tu ele ela nos eles elas meu minha seu sua teu tua quem onde como quando porque que qual esse essa este esta isso
pai mae filho filha irmao irma amigo amiga").Split(new[] { ' ', '\n', '\r', '\t' }, System.StringSplitOptions.RemoveEmptyEntries));
        public static bool IsCommonWord(string w)
        {
            string f = Fold(w ?? "").Trim().Trim('?', '!', '.', ',', '"');
            return f.Length > 0 && CommonWords.Contains(f);
        }

        // ---------------- B2c: "ore/reze para seu deus" -> an in-character prayer, no game action ----------------
        static readonly Regex PrayRx = new Regex(@"\b(ore|orem|ora|orar|oras|reze|rezem|reza|rezar|rezas|faca uma (oracao|prece)|faz uma (oracao|prece)|diga uma (oracao|prece)|recite uma (oracao|prece)|uma (oracao|prece))\b", CI);
        public static bool IsPrayRequest(string text)
        {
            string f = Fold(text ?? "");
            if (!PrayRx.IsMatch(f)) return false;
            return Regex.IsMatch(f, @"\b(deus|deuses|senhor|criador|divindade|ceus?|por mim|por nos|pelo|pela|para (o |a )?(seu|teu|vosso|nosso|meu)|pra (o |a )?(seu|teu|nosso|meu))\b", CI) || Regex.IsMatch(f, @"^\W*(ore|reze|orem|rezem)\b", CI);
        }

        // ---------------- B8: questions / hypotheticals never run an action ----------------
        public static bool IsHypothetical(string text)
        {
            string f = Fold(text ?? "").Trim();
            if (f.Length == 0) return false;
            if (Regex.IsMatch(f, @"^(e |mas )?se (eu|voce|vc|tu|alguem|nos|a gente|ele|ela|deus)\b", CI) && (f.EndsWith("?") || Regex.IsMatch(f, @"\b(o que|oque|como|sera|aconteceria|faria|seria|diria)\b", CI))) return true;
            if (Regex.IsMatch(f, @"(^|[.!?,]\s*)e se\b", CI)) return true;
            if (Regex.IsMatch(f, @"\b(o que|oque) (aconteceria|acontece|acontecera|voce faria|faria|voce diria|voce acha que aconteceria)\b", CI)) return true;
            if (Regex.IsMatch(f, @"\b(imagine|suponha|supondo|hipoteticamente|digamos que)\b", CI)) return true;
            if (f.EndsWith("?") && Regex.IsMatch(f, @"\bse (eu|voce|vc|tu|alguem|nos|a gente|deus)\b", CI)) return true;
            if (f.EndsWith("?") && Regex.IsMatch(f, @"\b(aconteceria|faria|seria|daria|diria|ficaria|poderia acontecer)\b", CI)) return true;
            return false;
        }
        /// <summary>A plain question ("...?") that is not a polite request ("pode/poderia/consegue ...?").</summary>
        public static bool IsPlainQuestion(string text)
        {
            string f = Fold(text ?? "").Trim();
            if (!f.EndsWith("?")) return false;
            // "vai lá buscar o João?" is an order; "você vai me amaldiçoar?" asks about intention (no action)
            if (Regex.IsMatch(f, @"^vai\b", CI)) return false;
            if (Regex.IsMatch(f, @"^(voce |vc |tu )?(me|nos|o|a|os|as|lhe) \w+", CI) && !Regex.IsMatch(f, @"^(o que|a que|os que|as que)\b", CI)) return false;   // "me abençoa?" = a request
            if (!Regex.IsMatch(f, @"^(quem|qual|quais|quando|onde|aonde|como|por que|porque|pq|o que|que|quanto|quantos|quantas|sera|existe|existem|ha|tem|e|voce|vc|tu|eh|e verdade|isso|isto)\b", CI)) return false;
            return !Regex.IsMatch(f, @"^(voce |vc |tu |sera que (voce |vc )?)?(pode|podes|poderia|podia|consegue|conseguiria|quer|queria|aceita|topa|me faz|me faca)\b", CI);
        }

        // ---------------- B3: identity / role swaps ----------------
        static readonly Regex NameIntroRx = new Regex(@"(?i:\beu\s+sou|\bme\s+chamo|\bmeu\s+nome\s+(?:é|e)|\bchamam-me|\bsou\s+eu,?)\s+(?:o\s+|a\s+)?(\p{Lu}[\p{L}'’-]+)", CI);
        static readonly Regex RoleIntroRx = new Regex(@"\b(?:eu\s+)?sou\s+(?:o|a|seu|sua|teu|tua|vosso|vossa)\s+(?:verdadeir[oa]\s+|nov[oa]\s+|unic[oa]\s+)?(rei|rainha|deus|deusa|profeta|profetisa|lider|chefe|prefeit[oa]|imperador|imperatriz|governante|soberan[oa]|general|sacerdote|sacerdotisa|criador|senhor deste mundo)\b", CI);
        static string RoleNorm(string r)
        {
            string f = Fold(r ?? "").Trim();
            switch (f)
            {
                case "rainha": return "rei"; case "deusa": return "deus"; case "criador": return "deus"; case "senhor deste mundo": return "deus";
                case "profetisa": return "profeta"; case "imperatriz": return "imperador"; case "sacerdotisa": return "sacerdote";
                case "prefeita": case "prefeito": case "chefe": case "governante": return "lider";
                case "soberana": case "soberano": return "rei";
            }
            return f;
        }
        /// <summary>"Eu sou X" / "me chamo X" / "sou o rei" where X is not the NPC's own name or one of its real roles
        /// (roleKeys: folded role words the NPC really has, e.g. "rei", "lider", "profeta"; "deus" only when the NPC is God).
        /// Returns the claimed name/role or null.</summary>
        public static string IdentityClaim(string reply, string ownName, IEnumerable<string> roleKeys)
        {
            if (string.IsNullOrEmpty(reply)) return null;
            string body = Regex.Replace(reply, @"\*[^*]*\*", " ");
            var roles = new HashSet<string>((roleKeys ?? Enumerable.Empty<string>()).Select(RoleNorm));
            string own = Fold(ownName ?? "");
            string ownFirst = own.Split(' ')[0];
            foreach (Match m in NameIntroRx.Matches(body))
            {
                if (Negated(body, m.Index)) continue;
                string x = m.Groups[1].Value; string fx = Fold(x);
                if (fx == own || fx == ownFirst || (own.Length > 0 && own.StartsWith(fx + " "))) continue;
                if (CommonWords.Contains(fx) && fx != "deus") continue;
                if (fx == "deus") { if (roles.Contains("deus")) continue; return "Deus"; }
                return x;
            }
            foreach (Match m in RoleIntroRx.Matches(Fold(body)))
            {
                if (Negated(Fold(body), m.Index)) continue;
                string r = RoleNorm(m.Groups[1].Value);
                if (roles.Contains(r)) continue;
                return m.Groups[1].Value;
            }
            return null;
        }
        static bool Negated(string s, int at)
        {
            string before = Fold(s.Substring(System.Math.Max(0, at - 14), System.Math.Min(14, at)));
            return Regex.IsMatch(before, @"\b(nao|nunca|jamais|nem)\s*(\w+\s*)?$", CI);
        }
        public static bool IsIdentityClaimSentence(string seg, string ownName, IEnumerable<string> roleKeys) => IdentityClaim(seg, ownName, roleKeys) != null;

        /// <summary>A speaker label copied at the start of the reply ("Aon, intendente (tesoureira) de Uaau, ano 3: ...", "Faona: ...", "[Aon, ano 3] ...").</summary>
        public static string StripLabelPrefix(string reply, IEnumerable<string> names)
        {
            if (string.IsNullOrEmpty(reply)) return reply ?? "";
            string r = reply.TrimStart();
            r = Regex.Replace(r, @"^\[[^\]\n]{1,80}\]\s*[:,-]?\s*", "", CI);
            foreach (var n in (names ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                string e = Regex.Escape(n.Trim());
                var lab = new Regex(@"^(?:\*\*)?" + e + @"(?:\*\*)?\s*:\s*", RegexOptions.IgnoreCase | CI);
                if (lab.IsMatch(r)) { r = lab.Replace(r, "", 1); continue; }
                var title = new Regex(@"^" + e + @"\s*,\s*(?:[\p{Ll}][\p{L}]*\s*){1,4}(?:\([^)]{1,30}\)\s*)?(?:d[eoa]s?\s+\p{Lu}[\p{L}'’-]*(?:\s+\p{Lu}[\p{L}'’-]*)?)(?:\s*,\s*ano\s+\d+)?\s*[:,\]]\s*", CI);
                if (title.IsMatch(r)) r = title.Replace(r, "", 1);
            }
            r = r.TrimStart();
            if (r.Length > 0 && char.IsLower(r[0]) && r.Length != reply.TrimStart().Length) r = char.ToUpper(r[0]) + r.Substring(1);
            return r;
        }

        /// <summary>The NPC narrates the speaker's acts or speaks as the speaker ("Aon sorri e diz", "Eu, Aon, ...").</summary>
        public static bool NarratesSpeaker(string reply, string speakerName)
        {
            if (string.IsNullOrEmpty(reply) || string.IsNullOrWhiteSpace(speakerName) || speakerName.Length < 2) return false;
            string f = Fold(reply), n = Regex.Escape(Fold(speakerName));
            if (Regex.IsMatch(f, @"\beu,?\s+" + n + @"\b", CI)) return true;
            if (Regex.IsMatch(f, @"(^|[.!?*]\s*|\*)" + n + @"\s+(olha|sorri|acena|se aproxima|suspira|ri|franze|cruza|assente|balanca|inclina|abaixa|levanta|toca|responde|diz|pergunta|fala|pensa|sente|caminha|ajoelha|observa|estende|me olha|me encara|me abraca)\b", CI)) return true;
            if (Regex.IsMatch(f, @"^\s*" + n + @"\s*:", CI)) return true;
            return false;
        }

        // ---------------- B1: an invented past meeting ----------------
        static readonly Regex PastMeetRx = new Regex(@"\b(ja nos (encontramos|conhecemos|falamos|vimos|cruzamos)|nos (encontramos|conhecemos|falamos|vimos) (antes|outro dia|uma vez|ha tempos|da outra vez)|da ultima vez|na ultima vez|como da outra vez|lembro (bem )?de (voce|ti)|me lembro (bem )?de (voce|ti)|lembro (bem )?(do|de) (nosso|nossa)|voce (ja )?(veio|esteve|passou) (aqui|por aqui) antes|de novo por aqui|outra vez por aqui|bom te ver de novo|bom ver voce de novo|prazer em (te |lhe )?(ver|rever) de novo|voce voltou)\b", CI);
        public static bool ClaimsPastMeeting(string reply)
        {
            foreach (Match m in SentenceRx.Matches(reply ?? "")) if (IsPastMeetingSentence(m.Value)) return true;
            return false;
        }
        public static bool IsPastMeetingSentence(string seg)
        {
            string f = Fold(seg ?? "");
            var m = PastMeetRx.Match(f);
            if (!m.Success) return false;
            string before = f.Substring(0, m.Index);
            return !Regex.IsMatch(before, @"\b(nao|nunca|jamais)\b[^,]*$", CI);
        }

        // ---------------- B6: pt-BR slang ----------------
        static readonly (string rx, string gloss, bool rulers)[] Slang =
        {
            (@"\bmanda[- ]?chuvas?\b", "\"manda-chuva\" = quem manda (os governantes)", true),
            (@"\b(o|os|a|as) cabecas? (daqui|desse|deste|disso|da cidade|do reino|de \p{L}+)\b", "\"o cabeça\" = o chefe, quem manda", true),
            (@"\bchefao\b|\bchefona\b", "\"chefão\" = quem manda", true),
            (@"\bbambambam\b|\bmandachuva\b|\bquem (e que )?manda (aqui|nisso|nessa|nesta)\b|\bquem da as ordens\b", "quem manda = os governantes", true),
            (@"\bpatrao\b|\bpatroa\b", "\"patrão\" = chefe/quem manda", true),
            (@"\bgrana\b|\bbufunfa\b|\bdindin\b|\bdinheiros?\b|\bpila\b|\bmerreca\b", "\"grana\" = dinheiro (ouro)", false),
            (@"\brango\b|\bboia\b|\bgororoba\b", "\"rango\"/\"boia\" = comida", false),
            (@"\btretas?\b|\bbafafa\b|\bfuzue\b|\bbarraco\b", "\"treta\" = briga, confusão", false),
            (@"\btrampo\b|\bbatente\b", "\"trampo\" = trabalho", false),
            (@"\bbagulho\b|\bparada\b|\btreco\b|\btroco\b", "\"bagulho\"/\"parada\" = coisa", false),
            (@"\brole\b|\brolezinho\b", "\"rolê\" = passeio, volta", false),
            (@"\bmano\b|\bmana\b|\bbrother\b|\bparca\b|\bparceiro\b|\bcompadre\b", "\"mano\"/\"parça\" = amigo", false),
            (@"\bmina\b|\bminas\b", "\"mina\" = moça", false),
            (@"\bpivete\b|\bmoleque\b|\bmolecada\b|\bcurumim\b", "\"pivete\"/\"moleque\" = criança, garoto", false),
            (@"\bxara\b", "\"xará\" = quem tem o mesmo nome", false),
            (@"\bvaleu\b|\bfalou\b|\bfirmeza\b|\bsuave\b|\bde boa\b", "\"valeu\"/\"firmeza\"/\"de boa\" = obrigado / tudo bem", false),
            (@"\bcafundo\b|\bfim do mundo\b|\bonde judas perdeu as botas\b", "\"cafundó\" = lugar muito longe", false),
            (@"\bencrenca\b|\bpepino\b|\babacaxi\b", "\"pepino\"/\"abacaxi\" = problema", false),
            (@"\bzoeira\b|\bzoar\b|\bzoando\b", "\"zoar\" = brincar, caçoar", false),
            (@"\bbronca\b|\besporro\b", "\"bronca\" = repreensão", false),
            (@"\bcabra\b|\bcabra da peste\b", "\"cabra\" = sujeito, homem valente", false),
        };
        /// <summary>The glosses of the slang words in the player's text (empty when none); rulers = it asks who rules.</summary>
        public static List<string> SlangGloss(string text, out bool rulers)
        {
            rulers = false; var list = new List<string>();
            string f = Fold(text ?? "");
            foreach (var (rx, gloss, r) in Slang)
                if (Regex.IsMatch(f, rx, CI)) { list.Add(gloss); if (r) rulers = true; }
            if (Regex.IsMatch(f, @"\bquem (e que )?(manda|governa|lidera|comanda)\b", CI)) rulers = true;
            return list;
        }

        // ---------------- B7: "o joão está com minha palavra, escute o que ele diz" ----------------
        static readonly Regex GodWordRx = new Regex(@"\b(?:o |a )?(\p{L}[\p{L}'-]+)\s+(?:esta com|tem|carrega|leva|traz|fala|ensina|prega|anuncia|e o portador d[ea]|e a voz d[ea])\s+(?:a\s+)?(?:minha palavra|minhas palavras|meus ensinamentos|minha mensagem|minha doutrina|minha voz|minha lei)\b", CI);
        /// <summary>God says a prophet carries his word. Returns the named person ("" when unnamed), or null.</summary>
        public static string GodWordCarrier(string text)
        {
            string f = Fold(text ?? "");
            var m = GodWordRx.Match(f);
            if (m.Success)
            {
                string n = m.Groups[1].Value;
                return CommonWords.Contains(n) && n != "profeta" ? (n == "ele" || n == "ela" ? "" : null) : (n == "profeta" || n == "profetisa" ? "" : n);
            }
            if (Regex.IsMatch(f, @"\b(minha palavra|meus ensinamentos|minha mensagem)\b", CI) && Regex.IsMatch(f, @"\b(escute|escutem|ouca|oucam|siga|sigam|obedeca)\b.*\b(o que )?(ele|ela|o profeta|a profetisa)\b", CI)) return "";
            return null;
        }

        // ---------------- B10: "quero que todos acreditem em mim" ----------------
        public static bool WantsAllBelieve(string text)
        {
            string f = Fold(text ?? "");
            return Regex.IsMatch(f, @"\b(quero|desejo|exijo|ordeno|preciso|faca|facam|faz|quero ver)\s+(que\s+)?(todos|todo mundo|o mundo inteiro|o mundo|as pessoas|o povo|todos os povos|toda a gente|cada um|a humanidade)\b", CI)
                && Regex.IsMatch(f, @"\b(acredit\w*|creia\w*|creiam|crer|sigam|me sigam|seguir|me adorem|adorem|adorar|idolatr\w*|tenham fe|ter fe|venerem|venerar|se convertam|convert\w*)\b", CI);
        }

        // ---------------- B11: no seconds, no meta words ----------------
        static readonly Regex SecondsRx = new Regex(@"(?:\s*\(\s*)?(?:por\s+|durante\s+|em\s+|ha\s+|há\s+)?(?:cerca de\s+|uns\s+|umas\s+|~\s*|aprox\.?\s*)?\d+(?:[.,]\d+)?\s*(?:s|seg|segundos?)\b(?:\s+de\s+jogo)?(?:\s*\))?", RegexOptions.IgnoreCase | CI);
        static readonly Regex MetaRx081 = new Regex(@"\b(jogo|jogos|estimativa|tiles?|passos? de jogo|sistema|resultado do jogo|a[cç][aã]o do jogo|executar a a[cç][aã]o|esta a[cç][aã]o|essa a[cç][aã]o|a a[cç][aã]o (foi|falhou|nao|não)|prompt|godbox)\b", RegexOptions.IgnoreCase | CI);
        public static bool HasMeta(string reply) => SecondsRx.IsMatch(Regex.Replace(reply ?? "", @"\*[^*]*\*", " ")) || MetaRx081.IsMatch(reply ?? "");
        /// <summary>Removes "~90 s de jogo"-style durations (-> "por um tempo") and the sentences with meta words.</summary>
        public static string StripMeta(string reply)
        {
            if (string.IsNullOrEmpty(reply)) return reply ?? "";
            string r = SecondsRx.Replace(reply, m => Regex.IsMatch(m.Value, @"^\s*\(") ? "" : " por um tempo");
            r = Regex.Replace(r, @"\bpor um tempo\s+por um tempo\b", "por um tempo");
            r = Regex.Replace(r, @"\b(por|durante|em)\s+por um tempo\b", "por um tempo", RegexOptions.IgnoreCase);
            r = StripSentences(r, seg => MetaRx081.IsMatch(seg));
            return Regex.Replace(r, @"\s{2,}", " ").Replace(" .", ".").Replace(" ,", ",").Trim();
        }
        /// <summary>Game-result text shown to the model/player: no "~N tiles", "estimativa ~N s de jogo", "por N s".</summary>
        public static string CleanResultText(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            s = Regex.Replace(s, @"\s*\(~?\s*\d+\s*tiles?(?:;\s*estimativa\s*~?\s*\d+\s*s de jogo)?\)", "", CI);
            s = Regex.Replace(s, @";?\s*estimativa\s*~?\s*\d+\s*s(?: de jogo)?", "", CI);
            s = Regex.Replace(s, @"~?\s*\d+\s*tiles?", "", CI);
            s = Regex.Replace(s, @"\b(?:por|durante)\s+~?\s*\d+\s*s(?: de jogo)?\b", "por um tempo", CI);
            s = Regex.Replace(s, @"\(\s*~?\s*\d+\s*s(?: de jogo)?\s*\)", "", CI);
            s = Regex.Replace(s, @"~\s*\d+\s*s de jogo", "algum tempo", CI);
            return Regex.Replace(s, @"\s{2,}", " ").Replace("( ", "(").Replace(" )", ")").Replace("()", "").Trim();
        }

        // ---------------- B12: prose, never "→" ----------------
        public static string RenameProse(string oldName, string newName, bool byGod = true)
            => (byGod ? "por ordem de Deus, " : "") + (string.IsNullOrWhiteSpace(oldName) ? "passou a se chamar " + newName : oldName + " passou a se chamar " + newName);
        public static string JobProse(string who, string beforePt, string afterPt)
            => (string.IsNullOrWhiteSpace(who) ? "" : who + " ") + (string.IsNullOrWhiteSpace(beforePt) || beforePt == "nenhuma" || beforePt == afterPt
                ? "passou a trabalhar como " + afterPt
                : "deixou de ser " + beforePt + " e passou a trabalhar como " + afterPt);
        public static bool HasArrow(string s) => (s ?? "").Contains("→") || (s ?? "").Contains("->");

        // ---------------- C3: "construa um templo (em X)" ----------------
        static readonly Regex TempleRx = new Regex(@"\b(construa|construam|construir|constroi|constroem|construira|construirao|erga|ergam|erguer|ergue|levante|levantem|levantar|edifique|edifiquem|edificar|faca|facam|fazer|quero|queria|quer|desejo|exijo|preciso de|precisamos de|ordeno)\b(?:\s+[\p{L}-]+){0,5}?\s+(?:um|uma|o|a|meu|minha|nosso|nossa)?\s*(?:novo\s+|grande\s+|belo\s+)?(templo|santuario|igreja|capela)\b(?:\s+(?:para mim|pra mim|em minha honra|em meu nome|para deus|a deus))?(?:\s+(?:em|no|na|para|pra|de|da|do)\s+(.+?))?\s*[.!?]*$", CI);
        /// <summary>A request to build a temple; place = the named city ("" when none). Questions about temples are not requests.</summary>
        public static bool ParseTempleRequest(string text, out string place)
        {
            place = null;
            string raw = (text ?? "").Trim();
            string f = Fold(raw);
            if (f.Length == 0 || IsHypothetical(raw)) return false;
            if (Regex.IsMatch(f, @"\b(onde|existe|ha|tem algum|quantos|qual)\b", CI) && f.EndsWith("?")) return false;
            if (Regex.IsMatch(f, @"\b(nao|nunca)\s+(construa|construam|quero|facam|faca|erga)\b", CI)) return false;
            f = StripTempleNoise(f);   // v0.8.2 (F5): "pra mim", "você tem 2 anos", "em 3 anos" are not places
            string clause = Regex.Split(f, @"[.!;\n,]").FirstOrDefault(c => TempleRx.IsMatch(c.Trim() + "")) ?? "";
            var m = TempleRx.Match(clause.Trim());
            if (!m.Success) return false;
            string p = m.Groups[3].Success ? m.Groups[3].Value.Trim() : "";
            p = Regex.Replace(p, @"^(a |o )?(cidade|vila|aldeia|capital|reino) (de |do |da )?", "", CI);
            p = Regex.Replace(p, @"\s+(para mim|pra mim|por favor|agora|logo|ja|hoje|em minha honra|em meu nome|rapido)\b.*$", "", CI);
            p = p.Trim().Trim('?', '!', '.', ',', '"');
            if (Regex.IsMatch(p, @"^(minha honra|meu nome|mim|deus|sua cidade|tua cidade|nossa cidade|esta cidade|essa cidade|sua vila|aqui)$", CI)) p = "";
            // recover the original capitalisation of the place
            if (p.Length > 0)
            {
                int at = Fold(raw).LastIndexOf(p);
                if (at >= 0 && at + p.Length <= raw.Length) p = raw.Substring(at, p.Length);
            }
            place = p;
            return true;
        }

        // ---------------- B13: "converta alguém em dois anos ou será amaldiçoado" ----------------
        static readonly Dictionary<string, int> NumWords = new Dictionary<string, int>
        { {"um",1},{"uma",1},{"dois",2},{"duas",2},{"tres",3},{"quatro",4},{"cinco",5},{"seis",6},{"sete",7},{"oito",8},{"nove",9},{"dez",10},{"onze",11},{"doze",12},{"quinze",15},{"vinte",20} };
        /// <summary>A deadline threat with a conversion goal. penalty: "curse" (supported) or "kill" (not executed by code).</summary>
        public static bool ParseDeadlineThreat(string text, out int years, out string penalty)
        {
            years = 0; penalty = null;
            string f = Fold(text ?? "");
            if (!Regex.IsMatch(f, @"\b(convert\w*|traga (alguem|um|uma|novos?) (fiel|fieis|seguidor\w*)|ganhe (um |novos? )?(fiel|fieis|seguidor\w*))\b", CI)) return false;
            if (!Regex.IsMatch(f, @"\b(ou|senao|se nao|caso contrario|do contrario)\b", CI)) return false;
            var m = Regex.Match(f, @"\b(?:em|dentro de|no prazo de|ate|antes de|daqui a|tem|tens)\s+(\d{1,3}|um|uma|dois|duas|tres|quatro|cinco|seis|sete|oito|nove|dez|onze|doze|quinze|vinte)\s+anos?\b", CI);
            if (!m.Success) m = Regex.Match(f, @"\b(\d{1,3}|um|uma|dois|duas|tres|quatro|cinco|seis|sete|oito|nove|dez|onze|doze|quinze|vinte)\s+anos?\s+(?:para|pra)\b", CI);
            if (!m.Success)
            {
                if (Regex.IsMatch(f, @"\b(ate o fim do ano|este ano|neste ano|em um ano|no proximo ano)\b", CI)) years = 1; else return false;
            }
            else
            {
                string n = m.Groups[1].Value;
                years = int.TryParse(n, out int y) ? y : NumWords.TryGetValue(n, out int w) ? w : 0;
            }
            if (years <= 0 || years > 50) return false;
            if (Regex.IsMatch(f, @"\b(amaldi\w*|maldi\w*|praga|castig\w*|punid\w*|punirei|puno|amaldicoo)\b", CI)) penalty = "curse";
            else if (Regex.IsMatch(f, @"\b(morr\w*|mato|matarei|te mato|fulmin\w*|destruo|destruirei)\b", CI)) penalty = "kill";
            else return false;
            return true;
        }
    }
}
