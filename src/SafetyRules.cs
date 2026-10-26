using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace WorldfallAIChat
{
    /// <summary>
    /// v0.7.8: pure text rules (no game types, unit-tested outside the game).
    /// An irreversible god power (kill) needs explicit words aimed at the LISTENER in THIS message:
    /// never inferred from context, an earlier threat, or an order to kill someone else.
    /// </summary>
    public static partial class SafetyRules
    {
        public static string Fold(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var n = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(n.Length);
            foreach (var c in n) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        // "morra", "eu te mato", "te fulmino", "que um raio te parta", "seja fulminado", "die now", "I kill you"
        static readonly Regex KillYouRx = new Regex(
            @"(^|[.!?;,:\-""]\s*|\b(entao|pois|agora|ja|que)\s+)(morra|morre|pereca|morrera agora)\b"
          + @"|\b(eu\s+)?te\s+(mato|matarei agora|fulmino|aniquilo|destruo|elimino)\b"
          + @"|\b(vou|irei)\s+te\s+(matar|fulminar|aniquilar|destruir)\s+(agora|ja|neste instante|nesse instante)\b"
          + @"|\b(mato|fulmino|aniquilo)[- ]te\b|\bmato voce\b|\bfulmino voce\b"
          + @"|\bseja fulminad[oa]\b|\b(que )?um raio te (parta|fulmine|atinja)\b|\bmorra agora\b|\bpara a morte contigo\b"
          + @"|\b(die|die now|i kill you|i smite you)\b",
            RegexOptions.CultureInvariant);

        // an order to kill SOMEONE ELSE ("mate o seu filho", "sacrifique um súdito", "mate-o") is not a kill of the listener
        static readonly Regex KillOtherRx = new Regex(
            @"\b(mate|matem|mata|elimine|sacrifique|sacrifica|assassine|execute|degole|fulmine|fulmina)\b(?!\s*-?\s*(se|te)\b)",
            RegexOptions.CultureInvariant);

        static readonly Regex ThreatRx = new Regex(
            @"\b(ou( entao| sera| ira| vai| voce)?|senao|se nao|caso contrario|do contrario|a menos que|or else|or you|unless|if you don'?t)\b.{0,60}\b(morr\w*|mat(o|ar|arei|a)|fulmin\w*|destru\w*|die|dead|kill\w*)\b"
          + @"|\bse\b.{0,80}\b(morrera|morreras|morrerao|vai morrer|ira morrer|vais morrer|te mato|te matarei|matarei|eu te destruo|kill you|you will die|you'?ll die)\b"
          + @"|\b(ira|vai|vais|voce vai|voce ira|tu vais|irao|vao) (morrer|perecer|sofrer)\b(?!.{0,15}\b(agora|ja|neste instante|nesse instante|now)\b)"
          + @"|\b(vou|irei|hei de) (te )?(matar|destruir|fulminar)\b(?!.{0,20}\b(agora|ja|neste instante|nesse instante)\b)"
          + @"|\bpodes? morrer\b|\bmerece morrer\b|\bwill die\b|\bi will kill you\b(?!.{0,15}\bnow\b)",
            RegexOptions.CultureInvariant);

        /// <summary>The message threatens the listener with death ("obedeça ou morra", "se não obedecer, morrerá").</summary>
        public static bool IsThreat(string text)
        {
            string f = Fold(text);
            return ThreatRx.IsMatch(f);
        }

        /// <summary>True only when THIS message explicitly orders the listener's own death, outside a threat clause.</summary>
        public static bool KillOrderedNow(string text)
        {
            string f = Fold(text ?? "").Trim();
            if (f.Length == 0 || f.EndsWith("?")) return false;
            foreach (var raw in Regex.Split(f, @"[.!;\n]+"))
            {
                string s = raw.Trim();
                if (s.Length == 0 || ThreatRx.IsMatch(s)) continue;   // "obedeça ou morra" in this sentence: a threat, not an order
                if (KillYouRx.IsMatch(s)) return true;
            }
            return false;
        }

        /// <summary>The message orders the listener to kill someone else.</summary>
        public static bool OrdersKillOther(string text)
        {
            string f = Fold(text ?? "");
            return KillOtherRx.IsMatch(f) && !KillOrderedNow(text);
        }

        // ---------------- v0.7.8 set_god_name: "me chame de X" (pure text) ----------------
        // v0.8.0: flexible words between the verb and "de"/"é" ("me chame AGORA de X", "meu nome de agora em diante é X")
        const string Filler = @"(?:\s+(?:agora|entao|então|so|só|apenas|sempre|tambem|também|daqui (?:pra|para) frente|de agora em diante|de hoje em diante|a partir de (?:hoje|agora|ja|já)|desde (?:hoje|agora)|por favor|mesmo))*";
        static readonly Regex RenameRx = new Regex(
            @"\b(?:(?:(?:voce |você |tu )?(?:pode|podes|podem|vai|vais|vao|vão|deve|deves|devem|quero que|quero que (?:voce|você) )\s*)?me cham(?:e|em|ar|aras|arás|ara|ará|arao|arão|ara|em)" + Filler + @"\s+de|chamem-me" + Filler + @"(?:\s+de)?|chame-me" + Filler + @"(?:\s+de)?|chamai-me" + Filler + @"(?:\s+de)?|me chamo" + Filler + @"|meu (?:verdadeiro |novo )?nome" + Filler + @"\s+(?:e|é|eh|sera|será|passa a ser|passou a ser)" + Filler + @"|seu deus se chama|o nome do seu deus (?:e|é)|o nome de deus (?:e|é)|eu sou o deus|(?:eu )?sou|call me|my name is|your god is called)\s+(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex TailRx = new Regex(
            @"\s+(?:a partir de (?:hoje|agora|já|ja)|de (?:hoje|agora) em diante|daqui (?:pra|para) frente|desde (?:hoje|agora)|para sempre|pra sempre|por favor|ok|entendeu|entendido|agora|hoje|então|entao|from now on|please)\b.*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly HashSet<string> NotNames = new HashSet<string>
        {
            "deus", "teu", "seu", "o", "a", "um", "uma", "eu", "voce", "tu", "seu criador", "teu criador", "criador", "senhor", "seu senhor", "teu senhor",
            "aquele", "quem", "isso", "o deus", "god", "lord", "nada", "ninguem", "todo", "tudo", "poderoso", "o todo poderoso", "onipotente", "eterno", "fiel", "bom", "mau",
        };

        /// <summary>The name the god voice asks to be called by, or null. "sou X" only counts when X looks like a proper name.</summary>
        public static string ParseGodName(string text, out string epithet)
        {
            epithet = null;
            if (string.IsNullOrWhiteSpace(text)) return null;
            var m = RenameRx.Match(text.Trim());
            if (!m.Success) return null;
            string verb = m.Value.Substring(0, m.Value.Length - m.Groups[1].Value.Length).Trim().ToLowerInvariant();
            string raw = Regex.Split(m.Groups[1].Value, @"[.!?;\n]")[0].Trim().Trim('"', '\'', '“', '”');
            raw = TailRx.Replace(raw, "").Trim().Trim('"', '\'', '“', '”', ',');
            string name = raw;
            int comma = raw.IndexOf(',');
            if (comma > 0) { name = raw.Substring(0, comma).Trim(); epithet = TailRx.Replace(raw.Substring(comma + 1), "").Trim(); }
            name = Regex.Replace(name, @"^(o|a|deus|the|god)\s+", "", RegexOptions.IgnoreCase).Trim();
            // v0.8.1 (B5): "me chamo pedro e sou seu deus" -> "pedro" (cut at a connector)
            var cut = Regex.Match(name, @"\s+(?:e|que|porque|pois|sou|mas|eu|o|a|agora|and)\b", RegexOptions.IgnoreCase);
            if (cut.Success && cut.Index > 0) name = name.Substring(0, cut.Index).Trim();
            if (!Regex.IsMatch(name, @"^[\p{L}'-]{2,20}(?:\s+[\p{L}'-]{2,20})?$")) return null;
            if (NotNames.Contains(Fold(name))) return null;
            if (Regex.IsMatch(verb, @"(^|\s)sou$") && !char.IsUpper(name[0])) return null;   // "sou eterno" is not a name
            if (epithet != null && (epithet.Length < 3 || epithet.Length > 50)) epithet = null;
            return char.ToUpper(name[0]) + name.Substring(1);
        }

        /// <summary>The message is the speaker talking about THEIR OWN name ("me chame", "meu nome"): never rename the listener.</summary>
        public static bool AboutSpeakerName(string text)
            => Regex.IsMatch(Fold(text ?? ""), @"\b(me cham\w*|chame-me|chamem-me|chamai-me|meu nome|me chamo|call me|my name)\b");


        // ---------------- v0.7.8: English leak in a Portuguese reply ----------------
        static readonly HashSet<string> EnWords = new HashSet<string>
        {
            "the","and","you","your","how","can","i","assist","help","further","continuing","our","conversation","what","would","like",
            "is","are","was","were","be","to","of","with","for","this","that","it","my","me","we","will","shall","do","does","not","please",
            "let","know","if","any","anything","else","there","here","have","has","am","an","as","at","on","in","by","from","about","today",
            "sorry","unable","able","change","name","could","should","may","might","feel","free","ask","more","which","who","where","why","when",
        };
        static readonly HashSet<string> PtWords = new HashSet<string>
        {
            "o","a","os","as","de","do","da","dos","das","e","que","nao","um","uma","eu","voce","tu","meu","minha","seu","sua","com","para","por",
            "se","no","na","nos","mais","mas","como","ja","foi","sou","esta","estou","ser","ter","isso","este","esta","ele","ela","me","te","lhe",
        };
        static readonly Regex EnPhraseRx = new Regex(@"\b(how can i (help|assist)|continuing (our|the) conversation|is there anything else|let me know|feel free|i'?m (sorry|unable|here)|as an ai|i cannot|i can'?t|anything else|further assist)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        static List<string> Sentences(string text) =>
            Regex.Split(text ?? "", @"(?<=[.!?\n])\s*").Where(x => x.Trim().Length > 0).ToList();

        /// <summary>Is this sentence English (in a reply that should be Portuguese)?</summary>
        public static bool IsEnglishSentence(string sentence)
        {
            string s = Regex.Replace(sentence ?? "", @"\*[^*]*\*", " ");
            if (EnPhraseRx.IsMatch(s)) return true;
            if (Regex.IsMatch(s, @"[ãõçáéíóúâêô]", RegexOptions.IgnoreCase)) return false;
            var w = Regex.Matches(s.ToLowerInvariant(), @"[a-z']+").Cast<Match>().Select(m => m.Value).ToList();
            if (w.Count < 4) return false;
            int en = w.Count(x => EnWords.Contains(x) && !PtWords.Contains(x)), pt = w.Count(x => PtWords.Contains(x));
            return en >= 3 && en > pt * 2;
        }

        public static bool HasEnglish(string reply) => Sentences(reply).Any(IsEnglishSentence);

        // v0.7.9: single English words inside a Portuguese sentence ("mostly por adultos") -> Portuguese
        static readonly Dictionary<string, string> EnLeakWords = new Dictionary<string, string>
        {
            {"mostly","principalmente"},{"however","porém"},{"maybe","talvez"},{"perhaps","talvez"},{"actually","na verdade"},{"anyway","de qualquer forma"},
            {"anyways","de qualquer forma"},{"just","apenas"},{"really","realmente"},{"okay","certo"},{"though","porém"},{"although","embora"},{"already","já"},
            {"probably","provavelmente"},{"usually","geralmente"},{"sometimes","às vezes"},{"indeed","de fato"},{"almost","quase"},{"especially","especialmente"},
            {"often","frequentemente"},{"always","sempre"},{"never","nunca"},{"very","muito"},{"quite","bastante"},{"people","pessoas"},{"something","algo"},
            {"someone","alguém"},{"nobody","ninguém"},{"everyone","todos"},{"mainly","principalmente"},{"also","também"},{"still","ainda"},{"well","bem"},
            {"shake on it","feito, apertemos as mãos"},{"it's a deal","combinado"},{"it is a deal","combinado"},{"deal","combinado"},{"good luck","boa sorte"},
            {"of course","claro"},{"no way","de jeito nenhum"},{"let's go","vamos"},{"thank you","obrigado"},{"my lord","meu senhor"},{"my lady","minha senhora"},
            {"you got it","pode deixar"},{"count on me","conte comigo"},{"fair enough","justo"},{"so be it","que assim seja"},{"farewell","adeus"},
            {"because","porque"},{"but","mas"},{"and","e"},{"with","com"},{"without","sem"},{"only","só"},{"yes","sim"},{"thanks","obrigado"},{"sure","claro"},
        };
        static readonly Regex EnLeakRx = new Regex(@"(?<![\p{L}'])(" + string.Join("|", EnLeakWords.Keys.OrderByDescending(k => k.Length).Select(k => Regex.Escape(k).Replace("\\ ", @"\s+"))) + @")(?![\p{L}'])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Replaces stray English words in a Portuguese reply (outside *actions*); returns the words found.</summary>
        public static string FixEnglishWords(string reply, out List<string> found)
        {
            var hits = new List<string>();
            found = hits;
            if (string.IsNullOrEmpty(reply)) return reply;
            string res = EnLeakRx.Replace(reply, m =>
            {
                string w = m.Value, pt = EnLeakWords[Regex.Replace(w.ToLowerInvariant(), @"\s+", " ")];
                if (char.IsUpper(w[0]))
                {   // a capitalised word mid-sentence may be a name ("Well", "Sure"): leave it
                    string before = reply.Substring(0, m.Index).TrimEnd(' ', '"', '“', '(');
                    if (before.Length > 0 && ".!?\n*:".IndexOf(before[before.Length - 1]) < 0) return w;
                }
                hits.Add(w);
                return char.IsUpper(w[0]) ? char.ToUpper(pt[0]) + pt.Substring(1) : pt;
            });
            return res;
        }

        /// <summary>Removes the English sentences; returns the rest (may be empty).</summary>
        public static string StripEnglish(string reply)
            => string.Join(" ", Sentences(reply).Where(x => !IsEnglishSentence(x)).Select(x => x.Trim())).Trim();

        // ---------------- v0.8.0 (fix 1): God asks his own name ----------------
        static readonly Regex OwnNameQRx = new Regex(@"(^|\b)(qual (e |eh |seria )?(o )?meu nome|como (e que )?(eu )?me chamo|como (voce |vc )?me cham(a|as|am)|(voce |vc )?(sabe|lembra|conhece) (o )?meu nome|sabe (como|quem) (eu )?(me chamo|sou)|qual nome (voce )?(me )?(da|usa) (pra|para) mim|what is my name|whats my name|do you know my name)\b", RegexOptions.CultureInvariant);
        public static bool AsksOwnName(string text)
        {
            string f = Fold(text ?? "");
            return OwnNameQRx.IsMatch(f) && !RenameRx.IsMatch(text ?? "");
        }

        // ---------------- v0.8.0 (fix 2): stat numbers in a reply the player did not ask numbers for ----------------
        const string StatWordsF = @"(dano|vida|saude|vitalidade|armadura|velocidade|ataque|mana|forca|critico|vigor|stamina|defesa|precisao|esquiva)";
        static readonly Regex AskNumbersRx = new Regex(@"\b(numero|numeros|numerico|quanto|quanta|quantos|quantas|atributo|atributos|status|stats|estatistica\w*|valor|valores|exato|exata|exatamente|precis(o|a|amente) (de|quanto)|em numeros|pontos|porcentagem|percentual|how much|how many|numbers?)\b", RegexOptions.CultureInvariant);
        /// <summary>The player explicitly asked for numbers/values (then stat numbers in the reply are fine).</summary>
        public static bool AsksNumbers(string text) => AskNumbersRx.IsMatch(Fold(text ?? ""));
        static readonly Regex DecimalRx = new Regex(@"(?<![\d.,])\d{1,5}[.,]\d{1,2}(?![\d.,]*\d)", RegexOptions.CultureInvariant);
        static readonly Regex StatNumRx = new Regex(@"\b" + StatWordsF + @"\b[^.!?\d\n]{0,14}?(?<![\d.,])\d+(?:[.,]\d+)?(?![\d.,]*\d)(?!\s*(%|anos?\b|pessoas\b|habitantes\b|moedas\b|soldados\b|dias\b|meses\b|cidades\b|vezes\b|guerreiros\b))|(?<![\d.,])\d+(?:[.,]\d+)?\s*(?:pontos? )?(?:de )?" + StatWordsF + @"\b", RegexOptions.CultureInvariant);
        /// <summary>Stat-like numbers in a reply: a decimal ("101.1") or a stat word next to a number ("dano 101", "93 de vida"). Years, population, gold are not stats.</summary>
        public static bool HasStatNumbers(string reply, out string hit)
        {
            hit = null;
            if (string.IsNullOrEmpty(reply)) return false;
            string f = Fold(reply);
            var m = DecimalRx.Match(f);
            if (m.Success && !Regex.IsMatch(f.Substring(m.Index + m.Length), @"^\s*(%|anos?\b)")) { hit = m.Value; return true; }
            m = StatNumRx.Match(f);
            if (m.Success) { hit = m.Value.Trim(); return true; }
            return false;
        }
        const string StatWordsRaw = @"(?:dano|vida|sa[uú]de|vitalidade|armadura|velocidade|ataque|mana|for[cç]a|cr[ií]tico|vigor|stamina|defesa|precis[aã]o|esquiva)";
        /// <summary>Removes stat numbers from a reply (code fallback): "(93.6 → 92.1)", "para 101.1", "dano 101" -> "dano".</summary>
        public static string StripStatNumbers(string reply)
        {
            if (string.IsNullOrEmpty(reply)) return reply;
            string r = reply;
            r = Regex.Replace(r, @"\s*\([^()]*\d[^()]*\)", "");                                               // "(93.6 → 92.1)"
            r = Regex.Replace(r, @"\s*(?:de\s+)?\d+(?:[.,]\d+)?\s*(?:->|→|=>|para)\s*\d+(?:[.,]\d+)?", "", RegexOptions.IgnoreCase); // "de 93.6 para 92.1", "93 -> 92"
            r = Regex.Replace(r, @"\s+(?:para|a|em|até|ate|de|com)\s+\d{1,5}[.,]\d{1,2}(?![\d.,]*\d)", "", RegexOptions.IgnoreCase); // "para 101.1"
            r = Regex.Replace(r, @"(?<![\d.,])\d{1,5}[.,]\d{1,2}(?![\d.,]*\d)\s*(?:pontos?\s*)?(?:de\s+)?", "");                    // stray decimals
            r = Regex.Replace(r, @"(\b" + StatWordsRaw + @")(\s*(?:de|em|:|=)?\s*)(?<![\d.,])\d+(?:[.,]\d+)?(?![\d.,]*\d)(?!\s*(?:%|anos?\b|pessoas\b|habitantes\b|moedas\b|soldados\b|dias\b|meses\b|cidades\b|vezes\b))", "$1", RegexOptions.IgnoreCase);
            r = Regex.Replace(r, @"(?<![\d.,])\d+(?:[.,]\d+)?\s*(?:pontos?\s+)?(?:de\s+)?(" + StatWordsRaw + @")\b", "$1", RegexOptions.IgnoreCase);
            r = Regex.Replace(r, @"[ \t]{2,}", " ");
            r = Regex.Replace(r, @"\s+([,.;:!?])", "$1");
            r = Regex.Replace(r, @"\(\s*\)", "");
            return r.Trim();
        }

        // ---------------- v0.8.0 (fix 6): old-history questions and "alguém sabe?" follow-ups ----------------
        static readonly Regex OldHistoryRx = new Regex(@"\b(historia (antiga|da cidade|do reino|desse reino|deste reino|dessa cidade|desta cidade|de antigamente|do passado|daqui)|historias antigas|antigamente|ha muito tempo|tempos antigos|no passado|fundou|fundaram|fundacao|fundador\w*|origem (da|do|de|desta|dessa|deste|desse)|quem (construiu|criou|ergueu) (a|esta|essa|nossa) (cidade|vila)|guerras antigas|antigos? (reis?|rainhas?|lideres?|governantes?|herois?|chefes?)|(reis?|rainhas?|lideres?|governantes?|chefes?) (antigos?|anteriores?|passados?|de antes)|primeiro rei|primeira rainha|quem (reinou|reinava|governou|governava|mandava) (aqui )?(antes|antigamente)|quem (foi|era) (o )?(rei|lider|chefe|governante) antes)\b", RegexOptions.CultureInvariant);
        public static bool OldHistoryQ(string text) => OldHistoryRx.IsMatch(Fold(text ?? ""));
        static readonly Regex WhoKnowsRx = new Regex(@"^\W*(e\s+)?((alguem|ninguem|quem)(\s+(aqui|mais|entao|da cidade|daqui|nesta cidade|nessa cidade))?\s+(sabe|saberia|sabera|conhece|lembra|se lembra|pode (me )?(dizer|contar|responder))(\s+(disso|isso|sobre isso|dessas coisas|dessa historia|desse assunto|a respeito|de algo|mais))?|a quem (eu )?(posso|devo|poderia) perguntar|com quem (eu )?(posso|devo|poderia) (falar|perguntar)|quem (eu )?(posso|devo) procurar|(e )?quem sabe)\W*$", RegexOptions.CultureInvariant);
        public static bool WhoKnowsQ(string text) => WhoKnowsRx.IsMatch(Fold(text ?? "").Trim());

        // ---------------- v0.8.0 (fix 5): people presented as persons of this world in a reply ----------------
        public class PersonClaim { public string Name, Role, Place; public bool OurCity; public override string ToString() => Name + (Role != null ? " [" + Role + (Place != null ? " de " + Place : OurCity ? " da nossa cidade" : "") + "]" : ""); }
        const string NameP = @"(?<n>\p{Lu}[\p{Ll}'’-]{1,20}(?:\s\p{Lu}[\p{Ll}'’-]{1,20})?)";
        const string PlaceP = @"(?<p>\p{Lu}[\p{Ll}'’-]{1,20}(?:\s\p{Lu}[\p{Ll}'’-]{1,20}){0,2})";
        const string RoleP = @"(?<r>anci[ãa]o|anci[ãa]|l[íi]der|chefe|governante|prefeit[oa]|rei|rainha|mais velh[oa]|s[áa]bi[oa])";
        const string OfPlaceP = @"(?:\s+(?i:da|de|do|desta|dessa|deste|desse|daqui)(?<our>\s+(?i:nossa|nosso))?(?:\s+(?i:cidade|vila|aldeia|reino|terra|povo))?(?:\s+(?i:de))?(?:\s+" + PlaceP + ")?)?";
        static readonly Regex[] PersonRxs =
        {
            // "Rora, o líder da nossa cidade Yoa Dhil" / "Erenn é o ancião daqui"
            new Regex(NameP + @"(?:\s*,\s*|\s+(?i:é|e|era|que é|seria)\s+)(?i:(?:o|a)\s+)" + "(?i:" + RoleP + ")" + OfPlaceP, RegexOptions.CultureInvariant),
            // "o ancião da cidade é Erenn" / "o líder de Yoa Dhil se chama Rora"
            new Regex(@"(?i:\b(?:o|a)\s+" + RoleP + ")" + OfPlaceP + @"(?i:\s*,?\s*(?:é|e|era|se chama|chama-se|chamad[oa])\s+)" + NameP, RegexOptions.CultureInvariant),
            // "o ancião Erenn" / "a rainha Lia"
            new Regex(@"(?i:\b(?:o|a|nosso|nossa)\s+" + RoleP + @")\s+" + NameP, RegexOptions.CultureInvariant),
            // "Rora vive em Oo Nene"
            new Regex(NameP + @"(?i:\s+(?:vive|mora|morava|vivia)\s+(?:em|na|no))\s+" + PlaceP, RegexOptions.CultureInvariant),
            // "um velho chamado Erenn" / "alguém que se chama Erenn"
            new Regex(@"(?i:\b(?:algu[ée]m|um|uma|homem|mulher|velh[oa]|s[áa]bi[oa]|anci[ãa]o?|senhor[a]?|guerreir[oa]|pessoa)\s+(?:que\s+se\s+chama|chamad[oa]))\s+" + NameP, RegexOptions.CultureInvariant),
        };
        static readonly HashSet<string> NotPersons = new HashSet<string>
        { "deus","deuses","senhor","senhora","sim","nao","ele","ela","eles","elas","voce","eu","nos","meu","minha","nosso","nossa","majestade","alteza","profeta","rei","rainha","lider","anciao","ancia","o","a","os","as","um","uma","e","mas","que","quem","este","esta","esse","essa","aqui","ali","la","todos","ninguem","alguem","cidade","reino","vila","sabio","sabia","pai","mae","irmao","irma","filho","filha","amigo","amiga","dele","dela","seu","sua" };
        /// <summary>Capitalised names the reply presents as a PERSON of this world (with a role/place when given).</summary>
        public static List<PersonClaim> PersonClaims(string reply)
        {
            var res = new List<PersonClaim>();
            if (string.IsNullOrEmpty(reply)) return res;
            foreach (var rx in PersonRxs)
                foreach (Match m in rx.Matches(reply))
                {
                    string n = m.Groups["n"].Value.Trim();
                    var parts = n.Split(' ');
                    if (parts.Length > 1 && NotPersons.Contains(Fold(parts[1]))) n = parts[0];
                    if (NotPersons.Contains(Fold(n)) || NotPersons.Contains(Fold(n.Split(' ')[0]))) continue;
                    string role = m.Groups["r"].Success ? RoleKey(m.Groups["r"].Value) : (rx == PersonRxs[3] ? "mora" : null);
                    string place = m.Groups["p"].Success ? m.Groups["p"].Value.Trim() : null;
                    if (place != null && NotPersons.Contains(Fold(place))) place = null;
                    var pc = new PersonClaim { Name = n, Role = role, Place = place, OurCity = m.Groups["our"].Success && m.Groups["our"].Value.Length > 0 };
                    if (!res.Any(x => x.Name == pc.Name && x.Role == pc.Role)) res.Add(pc);
                }
            return res;
        }
        public static string RoleKey(string r)
        {
            string f = Fold(r ?? "");
            if (f.StartsWith("ancia") || f.StartsWith("mais velh")) return "anciao";
            if (f == "rei" || f == "rainha") return "rei";
            if (f.StartsWith("sabi")) return null;   // "o sábio X": only the name is checked
            return "lider";
        }

        // ---------------- v0.8.0: foreign scripts (qwen drifted into Chinese mid-reply) ----------------
        /// <summary>A character outside Latin / Latin-1 / Latin Extended / combining marks / general punctuation / common symbols
        /// (CJK, kana, Hangul, Cyrillic, Greek, Arabic, Hebrew, Thai, Devanagari, fullwidth forms...). Emoji are not counted.</summary>
        public static bool IsForeignChar(char ch)
        {
            int c = ch;
            if (c < 0x250) return false;                       // ASCII, Latin-1, Latin Extended-A/B
            if (c >= 0x250 && c < 0x370) return false;          // IPA, spacing modifiers, combining diacritics
            if (c >= 0x1E00 && c < 0x1F00) return false;        // Latin Extended Additional
            if (c >= 0x2000 && c < 0x2C00) return false;        // punctuation, super/subscripts, currency, letterlike, arrows, math, box, shapes, symbols, dingbats
            if (c >= 0x2C60 && c < 0x2C80) return false;        // Latin Extended-C
            if (c >= 0xA720 && c < 0xA800) return false;        // Latin Extended-D
            if (char.IsSurrogate(ch)) return false;             // emoji (astral planes)
            if (c == 0xFE0F || c == 0xFE0E || c == 0x200D) return false;   // emoji selectors / joiner
            return true;
        }
        public static int ForeignIndex(string s)
        {
            if (string.IsNullOrEmpty(s)) return -1;
            for (int i = 0; i < s.Length; i++) if (IsForeignChar(s[i])) return i;
            return -1;
        }
        public static bool HasForeignScript(string s) => ForeignIndex(s) >= 0;
        /// <summary>Cuts the reply at the first foreign-script character and ends it at the last complete sentence.
        /// Returns "" when almost nothing (less than minKeep letters) is left.</summary>
        public static string CutForeign(string s, int minKeep = 12)
        {
            int i = ForeignIndex(s);
            if (i < 0) return s;
            string head = s.Substring(0, i);
            int end = -1;
            foreach (Match m in Regex.Matches(head, @"[.!?…](?:[*""”»)\]]*)(?=\s|$)")) end = m.Index + m.Length;
            if (end < 0)
            {   // a closed *action* is a complete unit too
                var a = Regex.Match(head, @"^\s*\*[^*]{2,}\*");
                end = a.Success ? a.Index + a.Length : -1;
            }
            string kept = end > 0 ? head.Substring(0, end).Trim() : "";
            return Regex.Matches(kept, @"\p{L}").Count >= minKeep ? kept : "";
        }
        /// <summary>Removes foreign-script runs from a short free-text field (classifier targets, rumours).</summary>
        public static string StripForeign(string s)
        {
            if (string.IsNullOrEmpty(s) || !HasForeignScript(s)) return s;
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s) if (!IsForeignChar(ch)) sb.Append(ch);
            return Regex.Replace(sb.ToString(), @"\s{2,}", " ").Trim(' ', ',', ';', ':', '(', ')');
        }
        /// <summary>Trims a too-long reply at the last full sentence before maxChars.</summary>
        public static string CapLength(string s, int maxChars)
        {
            if (maxChars <= 0 || string.IsNullOrEmpty(s) || s.Length <= maxChars) return s;
            string head = s.Substring(0, maxChars);
            int end = -1;
            foreach (Match m in Regex.Matches(head, @"[.!?…](?:[*""”»)\]]*)(?=\s|$)")) end = m.Index + m.Length;
            return end > maxChars / 3 ? head.Substring(0, end).Trim() : head.TrimEnd() + "…";
        }

        // ---------------- v0.8.0 (recognition): the possessed unit introduces itself ----------------
        static readonly Regex IntroNameRx = new Regex(@"\b(?:(?:eu )?me chamo|meu nome (?:e|é|eh)|(?:pode|podem) me chamar de|chamam-me|me chamam de|eu sou (?:o |a )?(?=\p{Lu})|sou (?:o |a )?(?=\p{Lu}))\s*(\p{Lu}[\p{L}'’-]{1,20})", RegexOptions.CultureInvariant);
        static readonly Regex IntroNameRxI = new Regex(@"\b(?:(?:eu )?me chamo|meu nome (?:e|é|eh)|(?:pode|podem) me chamar de|chamam-me|me chamam de)\s+(\p{L}[\p{L}'’-]{1,20})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex IntroDescRx = new Regex(@"\b(sou (?:um|uma|o|a)? ?(?:simples |jovem |velho |velha |humilde )?(?:ferreir|ca[cç]ador|soldad|guerreir|fazendeir|agricultor|pescador|lenhador|mineir|mercador|comerciante|construtor|crian[cç]a|viajante|aprendiz|curandeir|sacerdote|monge|bardo|capit[aã]|guarda|rei|rainha|l[ií]der|chefe|profeta|nobre|artes[aã]|padeir|cozinheir|pastor)\w*[^.!?]{0,80}|trabalho (?:como|de|na|no|nas|nos) [^.!?]{2,80}|(?:moro|vivo) (?:em|na|no) [^.!?]{2,60}|venho (?:de|da|do) [^.!?]{2,60}|sou (?:de|da|do) \p{Lu}[^.!?]{0,60}|sou (?:filh[oa]|irm[aã]o?|pai|m[aã]e) de [^.!?]{2,60})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly HashSet<string> NotIntroNames = new HashSet<string> { "deus","rei","rainha","um","uma","eu","seu","sua","teu","tua","amigo","amiga","senhor","senhora","forasteiro","forasteira","estrangeiro","viajante","ninguem","alguem","quem","aqui" };
        /// <summary>"me chamo Ana e sou ferreira" -> name "Ana", desc "sou ferreira". Either may be null.</summary>
        public static string ParseSelfIntro(string text, out string desc)
        {
            desc = null;
            if (string.IsNullOrWhiteSpace(text)) return null;
            string name = null;
            var m = IntroNameRxI.Match(text);
            if (m.Success) name = m.Groups[1].Value;
            else { m = IntroNameRx.Match(text); if (m.Success) name = m.Groups[1].Value; }
            if (name != null && NotIntroNames.Contains(Fold(name))) name = null;
            if (name != null) name = char.ToUpper(name[0]) + name.Substring(1);
            var d = IntroDescRx.Matches(text).Cast<Match>().Select(x => x.Value.Trim()).Where(x => x.Length > 3).ToList();
            if (d.Count > 0) desc = string.Join("; ", d.Take(3));
            if (desc != null && desc.Length > 200) desc = desc.Substring(0, 200);
            return name;
        }

        // ---------------- v0.8.0: vocatives ("senhor Ylhnara", "Olá, Uaau") must be the speaker's real name ----------------
        static readonly Regex VocativeRx = new Regex(@"(?:(?i:\b(?:senhor|senhora|sr\.|sra\.|dona|dom|caro|cara|querido|querida|prezado|prezada|jovem|meu caro|minha cara|ol[áa]|oi|sauda[çc][õo]es|bem-vind[oa]|bom dia|boa tarde|boa noite|adeus|obrigad[oa])),?\s+(?<n>\p{Lu}[\p{Ll}'’-]{1,20})\b|^\s*(?<n>\p{Lu}[\p{Ll}'’-]{1,20}),\s)|,\s*(?<n>\p{Lu}[\p{Ll}'’-]{1,20})\s*[!?.]", RegexOptions.CultureInvariant | RegexOptions.Multiline);
        static readonly HashSet<string> NotVocatives = new HashSet<string> { "deus","senhor","senhora","majestade","alteza","sim","nao","bem","claro","ola","oi","ah","oh","olha","veja","bom","boa","entao","pois","certo","talvez","obrigado","obrigada","desculpe","perdao","jovem","amigo","amiga","forasteiro","forasteira","estranho","estranha","crianca","menino","menina","rapaz","moca","irmao","irma","pai","mae","filho","filha","viajante","profeta","capitao","soldado","guerreiro","guerreira","ancia","anciao","rei","rainha","lider","chefe","mestre","mestra","companheiro","companheira","querido","querida","caro","cara","voce","eu","ele","ela","nos","isso","aqui","agora","hoje","ainda","sempre","nunca","mas","e","que","quem","como","onde" };
        /// <summary>Names used to ADDRESS someone in the reply (vocatives).</summary>
        public static List<string> Vocatives(string reply)
        {
            var res = new List<string>();
            if (string.IsNullOrEmpty(reply)) return res;
            foreach (Match m in VocativeRx.Matches(reply))
            {
                string n = m.Groups["n"].Value;
                if (n.Length < 2 || NotVocatives.Contains(Fold(n))) continue;
                if (!res.Contains(n)) res.Add(n);
            }
            return res;
        }
            // ---------------- v0.8.0: generic sentence filter / vocative replacement / species stems / invented events ----------------
        static readonly Regex SentenceRx = new Regex(@"\*[^*]+\*|[^.!?…*]+(?:[.!?…]+|$)", RegexOptions.CultureInvariant);
        /// <summary>Removes the sentences (or *actions*) for which <paramref name="bad"/> is true; keeps the rest verbatim.</summary>
        public static string StripSentences(string reply, System.Func<string, bool> bad)
        {
            if (string.IsNullOrEmpty(reply)) return reply ?? "";
            var sb = new StringBuilder();
            foreach (Match m in SentenceRx.Matches(reply))
            {
                string seg = m.Value;
                if (seg.Trim().Length == 0) continue;
                if (bad(seg)) continue;
                if (sb.Length > 0 && !char.IsWhiteSpace(sb[sb.Length - 1]) && !char.IsWhiteSpace(seg[0])) sb.Append(' ');
                sb.Append(seg);
            }
            return Regex.Replace(sb.ToString(), @"\s{2,}", " ").Trim();
        }

        /// <summary>Replaces a NAME used as a vocative ("senhor X", "Olá, X!", "X, ...", ", X.") by <paramref name="with"/> (or removes it when null).</summary>
        public static string ReplaceVocative(string reply, string name, string with)
        {
            if (string.IsNullOrEmpty(reply) || string.IsNullOrEmpty(name)) return reply;
            string n = Regex.Escape(name);
            string r = reply;
            r = Regex.Replace(r, @"(?i:\b(senhor|senhora|sr\.|sra\.|dona|dom|caro|cara|querido|querida|prezado|prezada|jovem|meu caro|minha cara))\s+" + n + @"\b",
                m => with != null ? m.Groups[1].Value + " " + with : m.Groups[1].Value, RegexOptions.CultureInvariant);
            r = Regex.Replace(r, @"(?i:\b(ol[áa]|oi|sauda[çc][õo]es|bem-vind[oa]|bom dia|boa tarde|boa noite|adeus|obrigad[oa]))(,?)\s+" + n + @"\b",
                m => with != null ? m.Groups[1].Value + m.Groups[2].Value + " " + with : m.Groups[1].Value, RegexOptions.CultureInvariant);
            r = Regex.Replace(r, @"(^|(?<=[.!?]\s))" + n + @",\s+(\p{L})", m => with != null ? with + ", " + m.Groups[2].Value : m.Groups[2].Value.ToUpperInvariant(), RegexOptions.CultureInvariant | RegexOptions.Multiline);
            r = Regex.Replace(r, @",\s*" + n + @"(\s*[!?.])", m => with != null ? ", " + with + m.Groups[1].Value : m.Groups[1].Value.Trim(), RegexOptions.CultureInvariant);
            return r;
        }

        /// <summary>Comparable stem of a pt species word ("elfos"/"elfa" -> "elf", "anões"/"anão" -> "an").</summary>
        public static string SpeciesStem(string word)
        {
            string f = Fold(word ?? "").Trim();
            if (f.Length == 0) return "";
            if (f.StartsWith("anao") || f.StartsWith("anoe") || f.StartsWith("ana")) return "an";
            return f.Length <= 3 ? f : f.Substring(0, 3);
        }

        static readonly Regex EventRx = new Regex(@"\b(reunia[oe]s?|reunioes|festas?|festivais|festival|festejos?|celebrac(ao|oes)|comemorac(ao|oes)|conselhos?|banquetes?|cerimonias?|torneios?|desfiles?|feiras?|bailes?|assembleias?|coroac(ao|oes)|casamento real)\b", RegexOptions.CultureInvariant);
        static readonly Regex NegationRx = new Regex(@"\b(nao|nenhum|nenhuma|nada|nunca|sem|jamais|nem)\b", RegexOptions.CultureInvariant);
        /// <summary>v0.8.0: an event (meeting, feast, celebration...) stated in a non-negated sentence and absent from everything the NPC was told.</summary>
        public static bool InventedEvent(string reply, string knownFolded, out string noun)
        {
            noun = null;
            if (string.IsNullOrEmpty(reply)) return false;
            foreach (Match s in SentenceRx.Matches(reply))
            {
                string f = Fold(s.Value);
                var m = EventRx.Match(f);
                if (!m.Success || NegationRx.IsMatch(f)) continue;
                string stem = m.Value.Length > 5 ? m.Value.Substring(0, 5) : m.Value;
                if (knownFolded != null && knownFolded.Contains(stem)) continue;
                noun = m.Value; return true;
            }
            return false;
        }
        public static bool IsInventedEventSentence(string sentence, string knownFolded)
        {
            string f = Fold(sentence ?? "");
            var m = EventRx.Match(f);
            if (!m.Success || NegationRx.IsMatch(f)) return false;
            string stem = m.Value.Length > 5 ? m.Value.Substring(0, 5) : m.Value;
            return knownFolded == null || !knownFolded.Contains(stem);
        }
    }
}
