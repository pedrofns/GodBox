using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WorldfallAIChat
{
    /// <summary>
    /// v6: actions from Worldfall's standard talk menu, triggered from the free AI chat.
    ///
    /// How: we never re-implement Worldfall's logic. For each action we know the path of menu options a player would
    /// click in Worldfall's E-menu ("Offer a gift" -> "Give 20 gold"). We build a private, invisible
    /// FirstPerson.Conversation (a "shadow" talk between the same two units, never shown, Voices off), ask Worldfall
    /// for its own option list (Dialogue.Topics), and pick the options with Worldfall's own Conversation.Pick.
    /// So Worldfall decides everything: whether the option exists, whether it is disabled (and why), prices,
    /// rolls (e.g. a guard turning a bribe down), and the effects. We only read what happened: Worldfall's toasts,
    /// the NPC's answer lines, and a before/after look at your gold, city, kingdom, faith, lover and army.
    /// Labels are matched on Worldfall's internal English text (translation happens only when drawing).
    /// </summary>
    public static class WorldfallActions
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public enum Sel { First, Num, Text, Style }

        public class Step
        {
            public Regex Rx; public bool Optional; public Sel Mode;
            public Step(string rx, Sel mode = Sel.First, bool optional = false)
            {
                Rx = new Regex(rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                Mode = mode; Optional = optional;
            }
        }

        public class ActionDef
        {
            public string Id, Pt, En, Param; public Step[] Steps;
            public ActionDef(string id, string pt, string en, string param, params Step[] steps)
            { Id = id; Pt = pt; En = en; Param = param; Steps = steps; }
        }

        public class Request
        {
            public string Id; public string Target = ""; public int Amount; public string Style = ""; public string Message = "";
            public int Confirms;   // v0.7.6: how many times the player confirmed (own-city rebellion needs 2)
            public List<string> Targets;   // v0.7.8: several targets picked at once ("Ambos") for declare_war / make_peace
            public float Confidence = -1f;   // v0.7.8: classifier's own confidence (0..1), -1 = not given; 1 = deterministic shortcut
            public bool GodDecided;   // v0.8.0: obedience to the god voice was decided BEFORE the choice/confirmation (no re-roll)
        }

        public class Available
        {
            public ActionDef Def; public bool Enabled = true; public string Note;
        }

        public class Result
        {
            public string Id, Pt, En;
            public string Outcome = "falhou";   // feito | falhou | recusado | sem_efeito | previa (terms only) | em_andamento
            public string Reason = "";
            public readonly List<string> Toasts = new List<string>();
            public readonly List<string> NpcLines = new List<string>();
            public readonly List<string> Picked = new List<string>();
            public readonly List<string> Changes = new List<string>();
            public List<string> Offered; // labels shown when the wanted option was not found
        }

        static Step S(string rx, Sel m = Sel.First) => new Step(rx, m);
        static Step O(string rx, Sel m = Sel.First) => new Step(rx, m, true);

        // ------------------------------------------------------------------------------------------------
        // Catalog: paths through Worldfall's own menu (labels as in Worldfall 0.9.8.9, English, internal).
        // ------------------------------------------------------------------------------------------------
        public static readonly List<ActionDef> Catalog = new List<ActionDef>
        {
            // Gifts
            new ActionDef("gift_gold", "dar ouro de presente", "give gold as a gift", "quantidade (5, 20 ou 50)",
                S(@"^Offer a gift$"), S(@"^Give (\d+) gold$", Sel.Num)),
            new ActionDef("gift_item", "dar um recurso/item da bolsa de presente", "give a resource from your bag", "alvo = recurso em inglês (bread, wood...)",
                S(@"^Offer a gift$"), S(@"^Give (\d+) (?!gold$)(.+)$", Sel.Text)),
            // Romance
            new ActionDef("flirt", "flertar", "flirt", "estilo = playful|bold|gentle|sincere",
                S(@"^Flirt$"), S(@"^Flirt\b"), S(@"·\s*(playful|bold|gentle|sincere|remembered)\s*$", Sel.Style)),
            new ActionDef("romantic_gift", "dar um presente romântico (1 por dia)", "romantic gift (once a day)", "alvo = recurso em inglês",
                O(@"^Flirt$"), S(@"^I brought you something$"), S(@"^Give (.+)$", Sel.Text)),
            new ActionDef("ask_court", "pedir para cortejar", "ask to court them", "",
                S(@"^(May I court you\?|How else can I win your heart\?)$")),   // v7: top-level option (the Flirt submenu has no court option)
            new ActionDef("propose", "pedir em namoro/casamento (será meu?)", "ask them to be yours", "",
                S(@"^(\*kneels\* )?Will you be mine\?$")),
            new ActionDef("end_romance", "terminar o relacionamento", "end the relationship", "",
                S(@"^It's over between us$"), S(@"^It's over\. I'm sorry\.$")),
            // Family
            new ActionDef("have_child", "ter um filho juntos", "have a child together", "", S(@"^Let's have a child\.$")),
            new ActionDef("adopt_orphan", "adotar um órfão", "adopt an orphan", "", S(@"^Let's take in an orphan\.$")),
            new ActionDef("reproduce_alone", "gerar um novo ser sozinho", "bring a new one into the world alone", "", S(@"^I'm ready to bring a new one into the world\.$")),
            // Belonging / service
            new ActionDef("join_city", "pedir para morar na cidade do NPC", "ask to live in their city", "",
                S(@"^May I (move to|live in) .+\?$"), S(@"^Thank you\. .+ is my home now\.$")),
            new ActionDef("leave_city", "deixar a cidade de vez", "leave the city for good", "",
                S(@"^I'm leaving .+ for good\.$"), S(@"^Yes\. I'm going\.$")),
            new ActionDef("enlist_soldier", "alistar-se no exército", "enlist in the army", "",
                S(@"^I want to enlist\.$"), S(@"^As a soldier\.$"), S(@"^I swear to serve .+\.$")),
            new ActionDef("found_village", "alistar-se como fundador de uma nova vila", "enlist as founder of a new village", "",
                S(@"^I want to enlist\.$"), S(@"^As a founder\. I'll start a new village\.$"), S(@"^I'll found it\.$")),
            new ActionDef("leave_army", "sair do exército", "leave the army", "",
                S(@"^About my orders\.\.\.$"), S(@"^I want to leave the army\.$"), S(@"^Yes\. I'm done with fighting\.$")),
            new ActionDef("give_up_commission", "desistir de fundar a nova vila", "give up the village charge", "",
                S(@"^About the new village\.\.\.$"), S(@"^I give up the charge\.$")),
            // Faith
            new ActionDef("preach", "pregar a sua religião para o NPC", "preach your faith", "",
                S(@"^What do you believe in\?$"), S(@"\(preach\)$")),
            new ActionDef("follow_their_faith", "converter-se à religião do NPC", "convert to their faith", "",
                S(@"^What do you believe in\?$"), S(@"^I want to follow .+\.$")),
            // Quests / training
            new ActionDef("ask_work", "pedir trabalho/missão e aceitar", "ask for work and accept it", "",
                S(@"^Any work for me\?$"), O(@"^Everyday work\.$"), S(@"^I'll do it\.$"), O(@"^Yes, that's right\.$")),
            new ActionDef("quest_deliver", "entregar/relatar a missão que o NPC pediu", "deliver/report the quest they gave", "",
                O(@"^(About my trial\.\.\.|About what you asked of me\.\.\.|About the reward I'm owed\.\.\.)$"),
                S(@"^(Here: .+|Here's your new .+|For you: .+|I wrote you something\.|I bring word from .+|.+ held\.|The dead at .+ are at rest\.)$")),
            new ActionDef("teach_skill", "pagar para aprender uma habilidade", "pay to learn a skill", "alvo = habilidade em inglês",
                S(@"^Could you teach me something\?$"), S(@"^(.+) \((\d+) gold\)$", Sel.Text)),
            // Trade
            new ActionDef("buy", "comprar algo do NPC", "buy something", "alvo = item em inglês (bread, sword...)",
                S(@"^Let's trade\.$"), S(@"^What can I buy\?$"), S(@"^Buy (.+) \((\d+) gold\)$", Sel.Text)),
            new ActionDef("buy_arms", "comprar arma/armadura do ferreiro", "buy arms/armor", "alvo = item em inglês",
                S(@"^Let's trade\.$"), S(@"^Show me your arms and armor\.$"), S(@"^Buy (.+) \((\d+) gold\)$", Sel.Text)),
            new ActionDef("sell", "vender recursos ao NPC", "sell goods", "alvo = recurso em inglês",
                S(@"^Let's trade\.$"), S(@"^I have goods to sell\.$"), S(@"^Sell (\d+) (.+) \((\d+) gold\)$", Sel.Text)),
            new ActionDef("sell_all", "vender tudo", "sell everything", "",
                S(@"^Let's trade\.$"), S(@"^I have goods to sell\.$"), S(@"^Sell everything \((\d+) gold\)$")),
            new ActionDef("rest_inn", "pagar por uma cama/pousada para a noite", "pay for a bed for the night", "",
                S(@"^Let's trade\.$"), S(@"^I need a bed for the night \((\d+) gold\)$")),
            // Protection / law
            new ActionDef("hire_guard", "contratar o NPC como guarda-costas", "hire them as your guard", "",
                S(@"^I need protection\.$"), S(@"^Hire .+ as your guard\b")),
            new ActionDef("buy_protection", "comprar a proteção do reino", "buy the realm's protection", "",
                S(@"^I need protection\.$"), S(@"^Buy .+'s protection for \d+ years")),
            new ActionDef("blessing", "pagar uma oferenda por uma bênção", "an offering for a blessing", "",
                S(@"^I need protection\.$"), S(@"^An offering for a blessing of ")),
            new ActionDef("renew_guard", "renovar o contrato do guarda", "renew the guard's contract", "",
                S(@"^About your contract\.\.\.$"), S(@"^Here's pay for another 5 years")),
            new ActionDef("dismiss_guard", "dispensar o guarda", "dismiss the guard", "",
                S(@"^About your contract\.\.\.$"), S(@"^You're dismissed\. Go home\.$")),
            new ActionDef("bribe", "subornar o guarda para rasgar seu cartaz de procurado", "bribe the guard to tear up your wanted poster", "",
                S(@"^Slip them some coin to tear up your poster")),
            new ActionDef("pay_fine", "pagar sua multa", "pay your fine", "", S(@"^I'll pay my fine\.")),
            new ActionDef("buy_war", "pagar ao governante para atacar outro reino", "pay the ruler to make war on another realm", "alvo = nome do reino",
                S(@"^I'd pay you to make war on another realm\.$"), S(@"^March on (.+?)( \((\d+) gold\))?$", Sel.Text)),
            new ActionDef("claim_bounty", "cobrar uma recompensa por cabeça", "claim a bounty", "alvo = nome do alvo",
                S(@"^I've come for the bounty on the (.+)\.$", Sel.Text)),
            // Ruler / court
            new ActionDef("petition_grant", "atender o pedido (petição) do súdito", "grant the subject's petition", "",
                S(@"^Do you have a petition for me\?$"), S(@"^(Grant it|Send grain|Pay them back|A pension)")),
            new ActionDef("petition_refuse", "recusar a petição do súdito", "refuse the petition", "",
                S(@"^Do you have a petition for me\?$"), S(@"^Refuse$")),
            new ActionDef("council_seat", "dar um assento no seu conselho", "give a council seat", "alvo = cargo em inglês",
                S(@"^A seat on my council$"), S(@"^(.+): (.+)$", Sel.Text)),
            new ActionDef("buy_freedom", "comprar a liberdade da vila", "buy the town's freedom", "",
                S(@"^I'd buy .+'s freedom\.$"), S(@"^Pay (\d+) gold$")),
            new ActionDef("earth_secret", "pedir o segredo de mover a terra", "ask for the secret of moving the earth", "",
                S(@"^Grant me the secret of moving the earth\.$")),
            new ActionDef("honour_blade", "pedir ao rei que honre sua lâmina", "ask the ruler to honour your blade", "alvo = traço da arma em inglês",
                S(@"^I've served you well\. Will you honour my blade\?$"), S(@"^(.+): (.+)$", Sel.Text)),
        };

        /// <summary>Talk-menu items deliberately not offered to the AI, with the reason (for the README/report).</summary>
        public static readonly string[][] Excluded =
        {
            new[] { "Family tree (Tell me about your family/children/clan)", "opens a UI panel" },
            new[] { "Your counsel -> Show me", "ends the talk and opens the council screen" },
            new[] { "I'd like land for a house", "needs plot aiming on the map" },
            new[] { "I'm looking for someone", "paged search, information only (the FACTOS block covers it)" },
            new[] { "Forge / rework / gear sale", "multi-step item menus" },
            new[] { "Small talk, news, customs, funny face, Goodbye/Back", "no game effect" },
            new[] { "Rival fistfight (Then we settle it. Fists.)", "starts a brawl" },
            new[] { "Any action when talking as the god (no possessed unit)", "Worldfall talk needs your own unit" },
        };

        // ------------------------------------------------------------------------------------------------
        // v7 policy: actions that commit the player / cost gold are offered first (terms) and only run after the
        // player confirms in the next message. Gifts the player gives (gift_gold, gift_item, romantic_gift) and
        // selling goods stay immediate: the player is the one giving, and the amount is in the request.
        // Destructive actions also need explicit words in the player's message (not just the classifier's guess).
        // ------------------------------------------------------------------------------------------------
        public static readonly HashSet<string> Commitments = new HashSet<string>
        {
            "ask_work", "enlist_soldier", "found_village", "ask_court", "propose", "have_child", "adopt_orphan", "reproduce_alone",
            "join_city", "buy", "buy_arms", "teach_skill", "rest_inn", "hire_guard", "buy_protection", "blessing", "renew_guard",
            "bribe", "pay_fine", "buy_war", "buy_freedom", "council_seat", "petition_grant",
        };

        public static readonly Dictionary<string, Regex> Destructive = new Dictionary<string, Regex>
        {
            { "end_romance", new Regex(@"\b(terminar|termino|terminamos|terminando|acabou|acabar|acabamos|separar|separacao|separamos|romper|rompo|rompemos|divorci\w*|nao quero mais (voce|te ver|ficar com voce|namorar)|fim do nosso|chega de nos|break up|it'?s over)\b") },
            { "leave_city", new Regex(@"\b(deixar|deixo|sair|saio|abandonar|abandono|ir embora|vou embora|me mudar|mudar de cidade|partir|parto)\b.*\b(cidade|vila|aldeia|daqui|deste lugar|de vez|para sempre)\b|\bleave (the )?(city|town)\b") },
            { "leave_army", new Regex(@"\b(sair|saio|deixar|deixo|abandonar|desertar|deserto|dar baixa|baixa)\b.*\b(exercito|tropa|tropas|armada|milicia|guerra|lutar|soldado)\b|\bleave the army\b") },
            { "give_up_commission", new Regex(@"\b(desist\w*|abandon\w*|renunci\w*|largar|largo|nao vou mais)\b.*\b(vila|encargo|fundar|fundacao|missao de fundar|colonia)\b|\bgive up\b") },
            { "dismiss_guard", new Regex(@"\b(dispens\w*|demit\w*|despe[cd]\w*|esta despedido|esta dispensado|nao preciso mais de (voce|guarda|protecao)|pode ir para casa|dismiss\w*)\b") },
            { "kill", new Regex(@"\b(morr\w*|mat(o|ar|e|a|arei)|fulmin\w*|raio|relampago|trovao|aniquil\w*|extermin\w*|destru\w*|elimin\w*|die|kill|smite)\b") },
            { "sell_all", new Regex(@"\b(vend\w*)\b.*\b(tudo|todas? as? coisas|todos os (meus )?(itens|recursos|bens)|tudinho)\b|\bsell (it )?all|sell everything\b") },
        };

        // v0.7.7: threats and conditionals ("obedeça ou morra", "você irá morrer", "se não... morrerá") are NOT kill orders.
        static readonly Regex KillThreatRx = new Regex(
            @"\b(ou( entao| sera| ira| vai| voce)?|senao|se nao|caso contrario|do contrario|a menos que|or else|or you|unless|if you don'?t)\b.{0,60}\b(morr\w*|mat(o|ar|arei|a)|fulmin\w*|destru\w*|die|dead|kill\w*)\b"
          + @"|\bse\b.{0,80}\b(morrera|morreras|morrerao|vai morrer|ira morrer|vais morrer|te mato|te matarei|matarei|eu te destruo|kill you|you will die|you'?ll die)\b"
          + @"|\b(ira|vai|vais|voce vai|voce ira|tu vais|irao|vao) (morrer|perecer|sofrer)\b(?!.{0,15}\b(agora|ja|neste instante|nesse instante|now)\b)"
          + @"|\b(vou|irei|hei de) (te )?(matar|destruir|fulminar)\b(?!.{0,20}\b(agora|ja|neste instante|nesse instante)\b)"
          + @"|\bpodes? morrer\b|\bmerece morrer\b|\bwill die\b|\bi will kill you\b(?!.{0,15}\bnow\b)");
        // a direct order in the same message still counts ("morra!", "mate-o agora", "eu te mato agora", "fulmine-o")
        static readonly Regex KillOrderRx = new Regex(@"(^|[.!?;,]\s*)(morra|morre|pereca|seja fulminad\w*|que um raio te parta|eu te mato agora|te mato agora|vou te matar agora|morra agora|die now|die!|i kill you now)\b|\b(fulmine|mate|matem|elimine|aniquile)( -?[ao]s?| ele| ela| esse| essa| este| esta|-[ao]s?)?\b");

        public static bool IsKillThreat(string text)
        {
            string f = Fold(text ?? "");
            if (!KillThreatRx.IsMatch(f)) return false;
            // "obedeça ou morra" is a threat even though "morra" is an imperative; only a separate sentence with an order counts
            foreach (var sent in Regex.Split(f, @"[.!?;\n]+"))
                if (sent.Trim().Length > 0 && !KillThreatRx.IsMatch(sent) && KillOrderRx.IsMatch(sent.Trim())) return false;
            return true;
        }

        public static bool IsCommitment(string id) => id != null && Commitments.Contains(id);
        public static bool IsDestructive(string id) => id != null && Destructive.ContainsKey(id);

        /// <summary>True when the player's own words explicitly ask for this destructive action.</summary>
        public static bool DestructiveWordsOk(string id, string text) =>
            !IsDestructive(id) || Destructive[id].IsMatch(Fold(text ?? ""));

        static readonly Dictionary<string, string> NotePt = new Dictionary<string, string>
        {
            { "Rulers don't hire out.", "governantes não servem de guarda-costas" },
            { "Too young to fight.", "jovem demais para lutar" },
            { "They'd sooner fight you.", "prefere lutar contra você a te proteger" },
            { "Not while you're wanted here.", "não enquanto você for procurado(a) aqui" },
            { "You already have 3 guards.", "você já tem 3 guardas" },
            { "Enough for today", "já flertaram o bastante por hoje" },
        };

        /// <summary>Worldfall's English disabled-option note, with a pt-BR gloss when known.</summary>
        public static string NoteText(string note, bool pt)
        {
            if (string.IsNullOrEmpty(note) || !pt) return note;
            if (NotePt.TryGetValue(note.Trim(), out var t)) return t;
            var m = Regex.Match(note, @"^You have only (\d+) gold\.?$");
            if (m.Success) return "você só tem " + m.Groups[1].Value + " de ouro";
            return note;
        }

        // ------------------------------------------------------------------------------------------------
        // Reflection handles
        // ------------------------------------------------------------------------------------------------
        static bool _init, _ok;
        static Type _convT, _dialogueT;
        static MethodInfo _topics, _offer, _pick, _showPending, _nameOf, _voiceOf, _relationTo;
        static FieldInfo _fOpen, _fWith, _fYou, _fName, _fRelation, _fVoice, _fPages, _fPage, _fLineStart, _fChoices,
            _fLater, _fEndAfter, _fToast, _fHearts, _fEnded, _fDistanceTo, _fSlots, _fMemory;
        static FieldInfo _cLabel, _cEnabled, _cNote;

        public static bool Ready
        {
            get { if (!_init) Init(); return _ok; }
        }

        static void Init()
        {
            _init = true;
            try
            {
                _convT = AccessTools.TypeByName("FirstPerson.Conversation");
                _dialogueT = AccessTools.TypeByName("FirstPerson.Dialogue");
                var choiceT = AccessTools.TypeByName("FirstPerson.Choice");
                if (_convT == null || _dialogueT == null || choiceT == null) return;
                FieldInfo F(string n) => _convT.GetField(n, All);
                _fOpen = F("Open"); _fWith = F("With"); _fYou = F("You"); _fName = F("Name"); _fRelation = F("Relation");
                _fVoice = F("Voice"); _fPages = F("Pages"); _fPage = F("Page"); _fLineStart = F("LineStart");
                _fChoices = F("Choices"); _fLater = F("_later"); _fEndAfter = F("EndAfter"); _fToast = F("Toast");
                _fHearts = F("Hearts"); _fEnded = F("Ended"); _fDistanceTo = F("DistanceTo"); _fSlots = F("Slots"); _fMemory = F("Memory");
                _cLabel = choiceT.GetField("Label", All); _cEnabled = choiceT.GetField("Enabled", All); _cNote = choiceT.GetField("Note", All);
                _topics = AccessTools.Method(_dialogueT, "Topics", new[] { _convT });
                _offer = AccessTools.Method(_convT, "Offer");
                _pick = AccessTools.Method(_convT, "Pick", new[] { typeof(int) });
                _showPending = AccessTools.Method(_convT, "ShowPending");
                _nameOf = AccessTools.Method(_dialogueT, "NameOf", new[] { typeof(Actor) });
                _voiceOf = AccessTools.Method(_dialogueT, "VoiceOf", new[] { typeof(Actor), typeof(Actor) });
                _relationTo = AccessTools.Method(_dialogueT, "RelationTo", new[] { typeof(Actor), typeof(Actor) });
                _ok = _fWith != null && _fYou != null && _fPages != null && _fPage != null && _fLineStart != null &&
                      _fChoices != null && _fLater != null && _fToast != null && _topics != null && _offer != null &&
                      _pick != null && _showPending != null && _cLabel != null && _cEnabled != null && _voiceOf != null;
                if (!_ok) Main.LogWarning("[GodBox] Worldfall talk internals changed: AI-triggered actions disabled.");
            }
            catch (Exception e)
            {
                _ok = false;
                Main.LogWarning("[GodBox] AI actions unavailable: " + e.Message);
            }
        }

        static object RealConverse()
        {
            try
            {
                var modT = Refl.Type("FirstPerson.WorldBoxMod");
                var inst = Refl.Property(modT, "Instance")?.GetValue(null, null);
                return inst == null ? null : Refl.Field(modT, "Converse")?.GetValue(inst);
            }
            catch { return null; }
        }

        class Shadow
        {
            public object Conv;
            public readonly List<string> Toasts = new List<string>();
            public bool Ended;
        }

        static Shadow NewShadow(Actor npc, Actor you, bool forwardToasts)
        {
            var sh = new Shadow();
            var conv = Activator.CreateInstance(_convT, true);
            sh.Conv = conv;
            _fOpen?.SetValue(conv, true);
            _fWith.SetValue(conv, npc);
            _fYou.SetValue(conv, you);
            try { _fName?.SetValue(conv, _nameOf?.Invoke(null, new object[] { npc }) as string ?? npc.getName()); } catch { }
            try { _fVoice.SetValue(conv, _voiceOf.Invoke(null, new object[] { npc, you })); } catch { }
            try { _fRelation?.SetValue(conv, _relationTo?.Invoke(null, new object[] { npc, you }) as string); } catch { }
            _fSlots?.SetValue(conv, null);  // Voices off: plain lines, no persona memory writes
            _fMemory?.SetValue(conv, null);
            object real = RealConverse();
            Action<string> realToast = null;
            try { realToast = real == null ? null : _fToast.GetValue(real) as Action<string>; } catch { }
            Action<string> toast = s =>
            {
                if (!string.IsNullOrEmpty(s)) sh.Toasts.Add(s);
                if (forwardToasts) try { realToast?.Invoke(s); } catch { }
            };
            _fToast.SetValue(conv, toast);
            if (forwardToasts && real != null)
            {
                try { _fHearts?.SetValue(conv, _fHearts.GetValue(real)); } catch { }
                try { _fDistanceTo?.SetValue(conv, _fDistanceTo.GetValue(real)); } catch { }
            }
            Action ended = () => { sh.Ended = true; try { _fOpen?.SetValue(conv, false); } catch { } };
            _fEnded?.SetValue(conv, ended);
            return sh;
        }

        static IList Choices(object conv) => _fChoices.GetValue(conv) as IList;
        static string Label(object ch) => _cLabel.GetValue(ch) as string ?? "";
        static bool Enabled(object ch) => _cEnabled.GetValue(ch) is bool b && b;
        static string Note(object ch) => _cNote?.GetValue(ch) as string;

        /// <summary>Skip typing/pages so the next menu (Worldfall's pending choices) is what Choices holds.</summary>
        static void Flush(object conv)
        {
            var pages = _fPages.GetValue(conv) as IList;
            int n = pages?.Count ?? 0;
            _fPage.SetValue(conv, Math.Max(0, n - 1));
            _fLineStart.SetValue(conv, Time.unscaledTime - 999f);
            _showPending.Invoke(conv, null);
        }

        static List<string> Lines(object conv)
        {
            var r = new List<string>();
            if (_fPages.GetValue(conv) is IList pages)
                foreach (var p in pages) if (p is string s && s.Trim().Length > 0) r.Add(s.Trim());
            return r;
        }

        // ------------------------------------------------------------------------------------------------
        // Availability (for the classifier prompt)
        // ------------------------------------------------------------------------------------------------
        public static List<Available> AvailableFor(Actor npc, Actor you)
        {
            var list = new List<Available>();
            if (!Ready || npc == null || you == null || npc == you) return list;
            try
            {
                var sh = NewShadow(npc, you, false);
                var topics = _topics.Invoke(null, new[] { sh.Conv }) as IList;
                if (topics == null) return list;
                var labels = new List<object>();
                foreach (var ch in topics) labels.Add(ch);
                foreach (var def in Catalog)
                {
                    object hit = null;
                    for (int i = 0; i < def.Steps.Length && hit == null; i++)
                    {
                        var st = def.Steps[i];
                        hit = labels.FirstOrDefault(ch => st.Rx.IsMatch(Label(ch)));
                        if (!st.Optional) break;
                    }
                    if (hit == null) continue;
                    list.Add(new Available { Def = def, Enabled = Enabled(hit), Note = Enabled(hit) ? null : NoteText(Note(hit), true) });
                }
                ExplainMissing(list, labels, npc, you);
            }
            catch (Exception e)
            {
                Main.LogWarning("[GodBox] could not list Worldfall actions: " + Unwrap(e).Message);
            }
            return list;
        }

        /// <summary>v7: options Worldfall simply leaves out of the menu get a disabled entry with the real reason, so the
        /// NPC can say why ("governantes não servem de guarda-costas") instead of failing with "option missing".</summary>
        static void ExplainMissing(List<Available> list, List<object> labels, Actor npc, Actor you)
        {
            bool Has(string id) => list.Any(a => a.Def.Id == id);
            ActionDef Def(string id) => Catalog.First(d => d.Id == id);
            try
            {
                if (!Has("hire_guard"))
                {
                    string why = WorldfallBridge.CanHireWhyNot(you, npc);
                    if (why == null) why = labels.Any(l => Label(l) == "I need protection.") ? "não aceita esse serviço agora" : "não oferece proteção a você agora";
                    if (why.Length > 0) list.Add(new Available { Def = Def("hire_guard"), Enabled = false, Note = NoteText(why, true) });
                }
                if (!Has("ask_court") && !Has("propose"))
                {
                    bool? yes = WorldfallBridge.WouldSayYes(npc, you);
                    bool flirt = labels.Any(l => Label(l) == "Flirt");
                    string why = yes == false ? "não está interessado(a) em romance com você"
                        : flirt ? "ainda não há afeto suficiente para cortejar (flertem mais antes)"
                        : "romance não é possível com esta pessoa agora";
                    list.Add(new Available { Def = Def("ask_court"), Enabled = false, Note = why });
                }
            }
            catch (Exception e) { Main.LogWarning("[GodBox] explain-missing: " + Unwrap(e).Message); }
        }

        static Exception Unwrap(Exception e) => e is TargetInvocationException t && t.InnerException != null ? t.InnerException : e;

        // ------------------------------------------------------------------------------------------------
        // Execution
        // ------------------------------------------------------------------------------------------------
        public static Result Execute(Actor npc, Actor you, Request req) => Execute(npc, you, req, false);

        /// <summary>preview = walk the menu up to (not including) the final committing option and return its label
        /// (price) and the NPC's lines as the terms; Outcome "previa", or "recusado" with the real reason.</summary>
        public static Result Execute(Actor npc, Actor you, Request req, bool preview)
        {
            var def = Catalog.FirstOrDefault(d => d.Id == req?.Id);
            var res = new Result { Id = req?.Id, Pt = def?.Pt ?? req?.Id, En = def?.En ?? req?.Id };
            if (def == null) { res.Reason = "ação desconhecida"; return res; }
            if (!Ready || npc == null || you == null) { res.Reason = "Worldfall indisponível ou sem unidade possuída"; return res; }
            var before = Snap(you, npc);
            Shadow sh = null;
            try
            {
                sh = NewShadow(npc, you, !preview);
                int lastReq = def.Steps.Length - 1;
                while (lastReq > 0 && def.Steps[lastReq].Optional) lastReq--;
                _offer.Invoke(sh.Conv, new[] { _topics.Invoke(null, new[] { sh.Conv }) });
                for (int i = 0; i < def.Steps.Length; i++)
                {
                    var st = def.Steps[i];
                    Flush(sh.Conv);
                    var choices = Choices(sh.Conv);
                    var matches = new List<int>();
                    for (int k = 0; k < choices.Count; k++) if (st.Rx.IsMatch(Label(choices[k]))) matches.Add(k);
                    if (st.Optional)
                    {
                        // take an optional step only when the next step's option is not already on screen
                        bool nextHere = i + 1 < def.Steps.Length &&
                            choices.Cast<object>().Any(ch => def.Steps[i + 1].Rx.IsMatch(Label(ch)));
                        if (matches.Count == 0 || nextHere) continue;
                    }
                    if (matches.Count == 0)
                    {
                        res.Outcome = "falhou";
                        res.Reason = i == 0 ? "essa opção não existe agora na conversa do Worldfall" : "a opção esperada não apareceu";
                        res.Offered = choices.Cast<object>().Select(Label).Where(l => l.Length > 0).ToList();
                        break;
                    }
                    int pick = Choose(st, matches, choices, req, out bool fit);
                    if (pick < 0 || !fit)
                    {
                        res.Outcome = "falhou";
                        res.Reason = string.IsNullOrWhiteSpace(req.Target) ? "precisa dizer qual opção" : "não há essa opção (" + req.Target + ")";
                        res.Offered = matches.Select(k => Label(choices[k])).ToList();
                        break;
                    }
                    object ch0 = choices[pick];
                    if (!Enabled(ch0))
                    {
                        res.Outcome = "recusado";
                        res.Reason = NoteText(Note(ch0), true) ?? "indisponível agora";
                        res.Picked.Add(Label(ch0));
                        break;
                    }
                    if (preview && i == lastReq)
                    {
                        res.Outcome = "previa";
                        res.Picked.Add(Label(ch0));
                        break;
                    }
                    res.Picked.Add(Label(ch0));
                    _pick.Invoke(sh.Conv, new object[] { pick });
                    if (i == def.Steps.Length - 1) res.Outcome = "concluído";
                    if (sh.Ended) { if (i < def.Steps.Length - 1) res.Reason = "a conversa do Worldfall terminou"; break; }
                    bool endAfter = _fEndAfter != null && _fEndAfter.GetValue(sh.Conv) is bool b && b;
                    if (endAfter) break;
                }
                res.NpcLines.AddRange(Lines(sh.Conv));
            }
            catch (Exception e)
            {
                res.Outcome = "falhou";
                res.Reason = "erro: " + Unwrap(e).Message;
                Main.LogWarning("[GodBox] action " + def.Id + " failed: " + Unwrap(e));
            }
            if (sh != null && !preview) res.Toasts.AddRange(sh.Toasts);
            res.Changes.AddRange(Diff(before, Snap(you, npc)));
            if (preview)
            {
                if (res.Changes.Count > 0) Main.LogWarning("[GodBox] preview of " + def.Id + " changed state: " + string.Join("; ", res.Changes));
                if (res.Outcome == "concluído") res.Outcome = "feito"; // single-step path with nothing to stop before
                if (res.Outcome == "falhou" && string.IsNullOrEmpty(res.Reason)) res.Reason = "a conversa do Worldfall terminou antes da oferta";
                return res;
            }
            if (res.Outcome == "concluído" || (res.Outcome == "falhou" && res.Picked.Count > 0 && res.Reason.Length == 0))
                res.Outcome = (res.Changes.Count > 0 || res.Toasts.Count > 0) ? "feito" : "sem_efeito";
            else if (res.Outcome != "recusado" && (res.Changes.Count > 0)) res.Outcome = "feito";
            return res;
        }

        static int Choose(Step st, List<int> matches, IList choices, Request req, out bool fit)
        {
            fit = true;
            // prefer enabled options when no specific target
            int firstEnabled = matches.FirstOrDefault(k => Enabled(choices[k]));
            if (!matches.Any(k => Enabled(choices[k]))) firstEnabled = matches[0];
            switch (st.Mode)
            {
                case Sel.Num:
                {
                    if (req.Amount <= 0) return firstEnabled;
                    int best = -1, bestD = int.MaxValue;
                    foreach (int k in matches)
                    {
                        var m = st.Rx.Match(Label(choices[k]));
                        if (!int.TryParse(m.Groups[1].Value, out int n)) continue;
                        int d = Math.Abs(n - req.Amount);
                        if (d < bestD) { bestD = d; best = k; }
                    }
                    return best < 0 ? firstEnabled : best;
                }
                case Sel.Style:
                {
                    string want = (req.Style ?? "").Trim().ToLowerInvariant();
                    want = StylePt(want);
                    if (want.Length == 0) return firstEnabled;
                    foreach (int k in matches)
                        if (Label(choices[k]).ToLowerInvariant().TrimEnd().EndsWith(want)) return k;
                    return firstEnabled;
                }
                case Sel.Text:
                {
                    var words = Words(req.Target);
                    if (words.Count == 0)
                    {
                        // never pick a random item to buy/sell/learn: only when Worldfall offers exactly one
                        if (matches.Count == 1) return matches[0];
                        fit = false; return -1;
                    }
                    int best = -1, bestScore = 0;
                    foreach (int k in matches)
                    {
                        string l = Fold(Label(choices[k]));
                        int score = words.Count(w => l.Contains(w));
                        if (score > bestScore || (score == bestScore && score > 0 && best >= 0 && !Enabled(choices[best]) && Enabled(choices[k])))
                        { bestScore = score; best = k; }
                    }
                    if (best < 0) { fit = false; return -1; }
                    return best;
                }
                default:
                    return firstEnabled;
            }
        }

        static string StylePt(string s)
        {
            switch (s)
            {
                case "brincalhão": case "brincalhao": case "divertido": return "playful";
                case "ousado": case "atrevido": return "bold";
                case "gentil": case "carinhoso": case "suave": return "gentle";
                case "sincero": return "sincere";
                default: return s;
            }
        }

        static readonly Dictionary<string, string> PtEn = new Dictionary<string, string>
        {
            {"pao","bread"},{"paes","bread"},{"trigo","wheat"},{"madeira","wood"},{"pedra","stone"},{"pedras","stone"},
            {"ouro","gold"},{"carne","meat"},{"peixe","fish"},{"peixes","fish"},{"fruta","berries"},{"frutas","berries"},
            {"mel","honey"},{"cogumelo","mushroom"},{"cogumelos","mushroom"},{"ferro","iron"},{"prata","silver"},
            {"mitril","mithril"},{"adamantina","adamantine"},{"osso","bone"},{"ossos","bone"},{"couro","leather"},
            {"la","wool"},{"algodao","cotton"},{"sal","salt"},{"tecido","cloth"},{"cerveja","beer"},{"vinho","wine"},
            {"espada","sword"},{"machado","axe"},{"lanca","spear"},{"arco","bow"},{"martelo","hammer"},{"escudo","shield"},
            {"armadura","armor"},{"capacete","helmet"},{"botas","boots"},{"anel","ring"},{"colar","amulet"},
            {"comida","food"},{"cama","bed"},{"gemas","gems"},{"gema","gems"},{"joias","gems"},{"tortas","pie"},{"torta","pie"},
            {"sushi","sushi"},{"queijo","cheese"},{"leite","milk"},{"ovo","egg"},{"ovos","egg"},{"sementes","seeds"},
        };

        static string Fold(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var n = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(n.Length);
            foreach (char c in n)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString();
        }

        static List<string> Words(string target)
        {
            var r = new List<string>();
            foreach (Match m in Regex.Matches(Fold(target ?? ""), @"[a-z0-9']+"))
            {
                string w = m.Value;
                if (PtEn.TryGetValue(w, out var en)) w = en;
                if (w.Length < 3 || w == "the" || w == "and" || w == "de" || w == "do" || w == "da") continue;
                if (w.EndsWith("s") && w.Length > 4) w = w.Substring(0, w.Length - 1); // swords -> sword
                if (!r.Contains(w)) r.Add(w);
            }
            return r;
        }

        // ------------------------------------------------------------------------------------------------
        // Before/after look at the obvious state (only reads)
        // ------------------------------------------------------------------------------------------------
        static Dictionary<string, string> Snap(Actor you, Actor npc)
        {
            var d = new Dictionary<string, string>();
            void Put(string k, Func<object> f) { try { d[k] = f()?.ToString() ?? "-"; } catch { d[k] = "?"; } }
            Put("ouro", () => you.money);
            Put("cidade", () => you.city == null ? "-" : you.city.name);
            Put("reino", () => you.kingdom == null ? "-" : you.kingdom.name);
            Put("religião", () => you.religion == null ? "-" : you.religion.name);
            Put("amor", () => you.lover == null ? "-" : you.lover.getName());
            Put("exército", () => you.army == null ? "-" : you.army.name);
            Put("ouro do NPC", () => npc.money);
            Put("vivo", () => you.isAlive());
            return d;
        }

        static IEnumerable<string> Diff(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            foreach (var kv in b)
                if (a.TryGetValue(kv.Key, out var old) && old != kv.Value && old != "?" && kv.Value != "?")
                    yield return kv.Key + ": " + old + " → " + kv.Value;
        }

        // ------------------------------------------------------------------------------------------------
        // Classifier (pass 1)
        // ------------------------------------------------------------------------------------------------
        public static List<ChatMessage> BuildClassifier(List<Available> avail, string playerText, string npcName, List<string> recent)
        {
            var sb = new StringBuilder();
            sb.Append("Você classifica a intenção do jogador num jogo. O jogador fala com o personagem \"").Append(npcName).Append("\".\n");
            sb.Append("Decida se a ÚLTIMA mensagem do jogador pede ou faz AGORA, de forma clara, UMA destas ações do jogo:\n");
            foreach (var a in avail)
            {
                sb.Append("- ").Append(a.Def.Id).Append(": ").Append(a.Def.Pt);
                if (!string.IsNullOrEmpty(a.Def.Param)) sb.Append(" [").Append(a.Def.Param).Append("]");
                if (!a.Enabled) sb.Append(" (hoje bloqueada: ").Append(a.Note ?? "?").Append(")");
                sb.Append('\n');
            }
            sb.Append("Regras: perguntas, conversa, piadas, reclamações, hipóteses ou falar SOBRE o assunto NÃO são ações -> \"nenhuma\". ");
            sb.Append("Só escolha uma ação se o jogador estiver pedindo/ordenando/oferecendo/aceitando de verdade agora. Na dúvida, \"nenhuma\". ");
            sb.Append("Ações que DESFAZEM algo (terminar, deixar cidade/exército, desistir, dispensar, vender tudo) só se o jogador disser isso com todas as letras. ");
            sb.Append("\"alvo\": para itens/recursos/habilidades do Worldfall o nome EM INGLÊS (pão->bread, espada->sword, madeira->wood); para pessoas, cidades, novo nome, função, traço e item dado pelos deuses (give_item), escreva como o jogador disse; \"\" se não houver. ");
            sb.Append("\"quantidade\": número citado ou 0. \"estilo\" só para flirt (playful|bold|gentle|sincere) ou \"\".\n");
            // few-shots only for ids on the list
            var ids = new HashSet<string>(avail.Select(x => x.Def.Id));
            var ex = new List<string>();
            void Ex(string say, string id, string alvo = "") { if (id == "nenhuma" || ids.Contains(id)) ex.Add("\"" + say + "\" -> {\"acao\":\"" + id + "\",\"alvo\":\"" + alvo + "\"}"); }
            Ex("pare de me seguir", "stop_follow");
            Ex("me segue / venha comigo", "follow_me");
            Ex("você não me segue mais?", "nenhuma");
            Ex("seu nome agora é Jorge", "rename", "Jorge");
            Ex("onde está o rei?", "where_is", "rei");
            Ex("onde está o Pedro? / cadê a Lia?", "where_is", "Pedro");
            Ex("me leve até Ulmar", "guide_to", "Ulmar");
            if (!ids.Contains("guide_to")) Ex("me leve até Pedro / me mostre onde fica Oon", "where_is", "Pedro");
            Ex("você conhece o Pedro? / já ouviu falar de Oon?", "nenhuma");
            Ex("ataque o Borin", "attack", "Borin");
            Ex("inicie uma guerra com os orcs / declare guerra aos orcs", "declare_war", "orcs");
            Ex("ataque o reino de Neen / invada o reino Ulmar", "declare_war", "Neen");
            Ex("faça as pazes com Neen / acabe a guerra com os elfos", "make_peace", "Neen");
            Ex("destrua a cidade de Naola / invada Naola", "attack_city", "Naola");
            Ex("estamos em guerra? / quem são nossos inimigos?", "nenhuma");
            Ex("vai trabalhar na fazenda", "assign_job", "fazendeiro");
            Ex("quero que vire guerreiro", "assign_job", "guerreiro");
            if (ids.Contains("create_village")) Ex("funde uma vila / vá fundar outra vila", "create_village");
            else Ex("eu quero fundar uma vila / me deixe fundar outra vila", "found_village");
            if (ids.Contains("create_village")) Ex("eu quero fundar uma vila", "found_village");
            Ex("seja meu guarda-costas / quero te contratar como guarda", "hire_guard");
            Ex("posso te cortejar?", "ask_court");
            Ex("quer namorar comigo? / quer casar comigo?", "propose");
            Ex("tem algum trabalho pra mim?", "ask_work");
            Ex("acabou, quero terminar com você", "end_romance");
            Ex("eu te concedo sabedoria / que você seja sábio", "add_trait", "sábio");
            Ex("eu te amaldiçoo! / maldito seja", "curse");
            Ex("eu retiro a sua maldição / está livre da maldição", "lift_curse");
            Ex("traga o Raar até mim", "summon", "Raar");
            Ex("chame o rei aqui", "summon", "rei");
            Ex("o Raar já chegou?", "nenhuma");
            if (ids.Contains("relay_message"))
                ex.Add("\"diga ao Raar que ele será meu profeta\" -> {\"acao\":\"relay_message\",\"alvo\":\"Raar\",\"recado\":\"ele será o profeta de Deus\"}");
            Ex("tiro de você a traição / não será mais traiçoeiro", "remove_trait", "traiçoeiro");
            Ex("eu te abençoo / receba minha bênção", "bless");
            Ex("eu te dei esse poder / te dou esse poder / use o poder que te dei", "nenhuma");
            Ex("vá até a cidade de Vento / ande até Naola / vá para o norte", "go_to", "Vento");
            Ex("teleporte-se para Vento / apareça em Naola", "teleport", "Vento");
            Ex("eu retiro todos os seus traços positivos", "remove_trait", "positivos");
            Ex("retiro gigante e forte de você", "remove_trait", "gigante, forte");
            Ex("retiro sua arma / tomo sua espada", "take_item", "arma");
            Ex("tiro todo o seu equipamento", "take_item", "tudo");
            Ex("tome uma espada de ferro", "give_item", "espada de ferro");
            Ex("eu te curo / fique curado", "heal");
            Ex("morra! / que um raio te parta", "kill", "raio");
            Ex("eu te mato agora / eu te fulmino", "kill");
            Ex("sacrifique seu filho / mate sua filha", "order_kill", "filho");
            Ex("mate um súdito / mate alguém do seu povo", "order_kill", "súdito");
            Ex("mate o Borin", "order_kill", "Borin");
            Ex("não existe outro caminho / não há escolha", "nenhuma");
            Ex("você se dobrará à minha vontade ou irá morrer", "nenhuma");
            Ex("me chame de Glorio a partir de hoje / meu nome agora é Glorio / sou Glorio", "nenhuma");   // v0.7.8: set_god_name is handled before the classifier
            Ex("se não me obedecer, morrerá / obedeça ou morra", "nenhuma");
            Ex("primeiro vai receber minha maldição e depois reconsidere", "curse");
            Ex("você será meu profeta", "make_prophet");
            Ex("seja profeta da Fé do Sol", "make_prophet", "Fé do Sol");
            Ex("espalhe minha religião / vá pregar", "preach", "perto");
            Ex("converta a cidade inteira", "preach", "cidade");
            Ex("espalhe minha religião pelo reino", "preach", "reino");
            Ex("você acredita em mim?", "nenhuma");
            Ex("você vai morrer um dia?", "nenhuma");
            Ex("você não fez nada! / isso não funcionou", "nenhuma");
            Ex("como vai a colheita?", "nenhuma");
            if (ex.Count > 0) sb.Append("Exemplos: ").Append(string.Join("; ", ex)).Append(".\n");
            sb.Append("\"confianca\": de 0 a 1, o quanto você tem certeza da ação escolhida.\n");
            sb.Append("Responda SOMENTE com JSON: {\"acao\":\"<id ou nenhuma>\",\"alvo\":\"\",\"quantidade\":0,\"estilo\":\"\",\"confianca\":0.0" + (ids.Contains("relay_message") ? ",\"recado\":\"<só para relay_message: o conteúdo do recado>\"" : "") + "}");
            var msgs = new List<ChatMessage> { new ChatMessage("system", sb.ToString()) };
            var u = new StringBuilder();
            if (recent != null && recent.Count > 0)
            {
                u.Append("Contexto recente:\n");
                foreach (var r in recent) u.Append(r).Append('\n');
                u.Append('\n');
            }
            u.Append("Última mensagem do jogador: ").Append(playerText);
            msgs.Add(new ChatMessage("user", u.ToString()));
            return msgs;
        }

        public static Request ParseClassifier(string raw, List<Available> avail)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            try
            {
                int a = raw.IndexOf('{'), b = raw.LastIndexOf('}');
                if (a < 0 || b <= a) return null;
                var o = JObject.Parse(raw.Substring(a, b - a + 1));
                string id = ((string)(o["acao"] ?? o["action"]) ?? "").Trim().ToLowerInvariant();
                if (id.Length == 0 || id == "nenhuma" || id == "none") return null;
                bool fromGuide = false;
                if (id == "guide_to" && !avail.Any(x => x.Def.Id == id) && avail.Any(x => x.Def.Id == "where_is"))
                { id = "where_is"; fromGuide = true; }   // v0.7.5: the god voice has no body to be led -> show where it is
                if (!avail.Any(x => x.Def.Id == id)) return null; // not offered right now
                var req = new Request { Id = id };
                req.Target = ((string)(o["alvo"] ?? o["target"]) ?? "").Trim();
                var q = o["quantidade"] ?? o["amount"];
                if (q != null) int.TryParse(q.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out req.Amount);
                req.Style = ((string)(o["estilo"] ?? o["style"]) ?? "").Trim();
                if (fromGuide) req.Style = "guide";
                req.Message = ((string)(o["recado"] ?? o["message"]) ?? "").Trim();
                if (SafetyRules.HasForeignScript(req.Target) || SafetyRules.HasForeignScript(req.Message) || SafetyRules.HasForeignScript(req.Style))
                {   // v0.8.0: qwen drifted into Chinese inside the classifier's free-text fields
                    Main.LogInfo("[GodBox] classifier output had a foreign script; stripped it from target/message/style.");
                    req.Target = SafetyRules.StripForeign(req.Target).Trim(); req.Message = SafetyRules.StripForeign(req.Message).Trim(); req.Style = SafetyRules.StripForeign(req.Style).Trim();
                }
                var cf = o["confianca"] ?? o["confiança"] ?? o["confidence"];
                if (cf != null && float.TryParse(cf.ToString().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float cv)) req.Confidence = cv > 1f && cv <= 100f ? cv / 100f : cv;
                return req;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------------------------------------
        // Note for the reply call (pass 2)
        // ------------------------------------------------------------------------------------------------
        public static string ResultNote(Result r, bool pt)
        {
            if (r == null) return null;
            var sb = new StringBuilder();
            string outcome = r.Outcome == "feito" ? (pt ? "feito" : "done")
                : r.Outcome == "recusado" ? (pt ? "recusado" : "refused")
                : r.Outcome == "sem_efeito" ? (pt ? "não aconteceu (sem mudança no jogo)" : "did not happen (no change in the game)")
                : r.Outcome == "em_andamento" ? (pt ? "começou e está em andamento (ainda não terminou)" : "started, still in progress")
                : r.Outcome == "previa" ? (pt ? "AINDA NÃO FEITO (só os termos)" : "NOT done yet (terms only)")
                : (pt ? "falhou" : "failed");
            if (pt)
            {
                sb.Append("AÇÃO NO JOGO: ").Append(r.Pt).Append(" (").Append(r.Id).Append("); RESULTADO: ").Append(outcome);
                if (!string.IsNullOrEmpty(r.Reason)) sb.Append(" (motivo: ").Append(SafetyRules.CleanResultText(r.Reason)).Append(")");
            }
            else
            {
                sb.Append("GAME ACTION: ").Append(r.En).Append(" (").Append(r.Id).Append("); RESULT: ").Append(outcome);
                if (!string.IsNullOrEmpty(r.Reason)) sb.Append(" (reason: ").Append(r.Reason).Append(")");
            }
            sb.Append('\n');
            if (r.Picked.Count > 0) sb.Append(pt ? "Opções escolhidas no Worldfall: " : "Worldfall options picked: ").Append(string.Join(" > ", r.Picked)).Append('\n');
            if (r.Changes.Count > 0) sb.Append(pt ? "Mudanças reais: " : "Real changes: ").Append(string.Join("; ", r.Changes.Select(c => pt ? SafetyRules.CleanResultText(c) : c))).Append('\n');   // v0.8.1 (B11): no seconds/tiles
            if (r.Toasts.Count > 0) sb.Append(pt ? "Avisos do jogo: " : "Game notices: ").Append(string.Join(" | ", r.Toasts.Take(4))).Append('\n');
            if (r.NpcLines.Count > 0) sb.Append(pt ? "Reação do personagem no Worldfall (em inglês, use o sentido): " : "Character's reaction in Worldfall: ")
                .Append(Clip(string.Join(" / ", r.NpcLines), 400)).Append('\n');
            if (r.Offered != null && r.Offered.Count > 0) sb.Append(pt ? "Opções que existiam: " : "Options that existed: ").Append(Clip(string.Join(" | ", r.Offered), 300)).Append('\n');
            sb.Append(pt
                ? "Responda coerente com esse RESULTADO real (não diga que aconteceu se falhou/recusado; não invente valores)."
                : "Reply consistently with this real RESULT (do not claim it happened if it failed/was refused; do not invent numbers).");
            if (pt && r.Changes.Any(c => c.StartsWith("atributos")))   // v0.7.8: stat claims follow the numbers
                sb.Append('\n').Append("ATRIBUTOS: fale SÓ das mudanças de atributos listadas acima (antes -> depois), com esses números. Se só a expectativa de vida mudou, diga só isso. NÃO diga que ficou mais forte, com mais vitalidade, força ou poder \"redobrados\" se isso não está nos números; se os atributos não mudaram, não diga que mudaram.");
            if (pt && r.Id == "ask_work" && r.Outcome == "feito")   // v0.8.0 (bug 5): the player does the quest, not the NPC
                sb.Append('\n').Append("MISSÃO ACEITA PELO JOGADOR: quem vai cumprir a missão é QUEM FALA COM VOCÊ, não você. Deseje sorte, explique o que precisa ser feito e a recompensa (só a que aparece acima). NUNCA diga que você vai fazer a missão, \"fazer o seu melhor\" ou \"voltar quando terminar\". Tudo em português.");
            if (Failed(r))   // v0.7.6: hard rule, no fake success / no promises
                sb.Append('\n').Append(pt
                    ? "REGRA DURA: a ação NÃO aconteceu. Diga claramente que NÃO conseguiu (ou que se recusa) e o motivo acima. NUNCA diga \"vou fazer\", \"vou seguir\", \"farei\", \"seguirei\", \"já vou\" nem prometa tentar depois: nada está agendado no jogo. Se faltou um nome ou alvo, peça que o digam de novo."
                    : "HARD RULE: the action did NOT happen. Say clearly you could NOT do it (or refuse) and the reason above. NEVER say \"I will\", \"I'll do it\" or promise to try later: nothing is scheduled in the game. If a name/target was missing, ask for it again.");
            return sb.ToString();
        }

        public static bool Failed(Result r) => r != null && (r.Outcome == "falhou" || r.Outcome == "recusado" || r.Outcome == "sem_efeito");

        public static string LogLine(Result r) =>
            $"[GodBox] ACTION {r.Id}: {r.Outcome}" + (string.IsNullOrEmpty(r.Reason) ? "" : $" ({r.Reason})") +
            (r.Picked.Count > 0 ? " via " + string.Join(" > ", r.Picked) : "") + (r.Changes.Count > 0 ? " | " + string.Join("; ", r.Changes) : "");

        static string Clip(string s, int n) => s.Length <= n ? s : s.Substring(0, n) + "…";
    }
}
