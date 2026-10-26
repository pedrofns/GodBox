using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace WorldfallAIChat
{
    /// <summary>
    /// IMGUI chat window (no prefabs/asset bundles needed, so it survives game UI updates).
    /// Open it with the hotkey (default F3) or the "Conversar" button shown while a unit's window is open.
    /// </summary>
    public class ChatWindow : MonoBehaviour
    {
        public static bool IsOpen;
        public static Rect ScreenRect;          // window rect in real screen pixels (for click blocking)
        /// <summary>True once our window has really been drawn on screen at least once since it was opened.</summary>
        public static bool Visible;
        /// <summary>Game/Worldfall input is only blocked while the window is open AND confirmed visible,
        /// so a window that never shows can never lock the player out.</summary>
        public static bool BlockGameInput => ((IsOpen && Visible) || GodFaithWindow.IsOpen) && Main.Cfg != null && Main.Cfg.BlockGameInputWhileTyping;   // v0.7.9 + religion window

        int _openedFrame, _lastRepaintFrame;
        object _pendingWorldfallConv;     // Worldfall conversation to close once our window is visible
        bool _endWorldfallNow;
        float _nextGuiErrorLog;

        Actor _npc, _you;
        UnitMemory _mem;
        string _input = "";
        string _status = "";
        bool _waiting, _countedThisSession;
        // v0.7.1 autofocus (see KeepFocus). The focus is handed out ONLY in non-Repaint passes (Layout, which Unity runs
        // right before every KeyDown/MouseDown pass), because the text field's control id in Repaint passes is not the
        // id it has when keys are delivered: Worldfall draws Repaint-only HUD controls before our postfix, so a focus
        // set during Repaint pointed at an id no KeyDown pass ever has ("focus confirmed" but typing went nowhere).
        // While the chat is open the box keeps the keyboard, unless the user clicked something else inside our window
        // (cleared again on open, send, reply and after Worldfall's own talk closes).
        int _focusTries, _fieldIdInput, _fieldIdRepaint, _imguiKeyFrame = -1, _fallbackLogs;
        bool _focusConfirmed, _userUnfocused, _stealBlockedLogged;
        string _focusWhy = "";
        Rect _fieldRect;
        string _typedBuf = ""; int _typedFrame = -1;
        Vector2 _scroll;
        Rect _rect = new Rect(40, 60, 520, 460);
        GUIStyle _wrap, _npcStyle, _youStyle, _small, _err;
        const string InputName = "aidlg_input";
        static readonly Regex LeadingTag = new Regex(@"^\s*\[[^\]]{0,80}\]\s*");

        void Update()
        {
            var cfg = Main.Cfg;
            if (cfg == null) return;
            try
            {
                // v0.7.8: an option/confirm button was clicked in the GUI pass; act on it here, outside IMGUI
                if (_queuedClick != null)
                {
                    string qc = _queuedClick; _queuedClick = null;
                    if (IsOpen && !_waiting) { _input = qc; Main.LogInfo("[GodBox] button clicked: " + qc); Send(); }
                }
                // Esc and the hotkey are read straight from Unity here, so closing works even if another mod's GUI eats IMGUI events.
                if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) { Main.LogInfo("[GodBox] closed with Esc."); Close(); return; }
                if (Input.GetKeyDown(cfg.GetHotkey()))
                {
                    if (IsOpen) { Main.LogInfo("[GodBox] closed with " + cfg.Hotkey + "."); Close(); return; }
                    if (!GameInputFieldFocused()) Open();
                }
                if (IsOpen) CollectTyped();
                if (IsOpen)
                {
                    // Watchdog: the window must be drawn within ~1 s of opening and keep being drawn; otherwise give input back.
                    if (!Visible && Time.frameCount - _openedFrame > 90)
                    {
                        Main.LogError("[GodBox] the chat window was never drawn (another GUI on top?). Closing it and releasing input.");
                        Close();
                        ShowToast("Não foi possível mostrar a janela de conversa. Veja o Player.log.");
                        return;
                    }
                    if (Visible && Time.frameCount - _lastRepaintFrame > 120)
                    {
                        Main.LogError("[GodBox] the chat window stopped being drawn. Closing it and releasing input.");
                        Close();
                        return;
                    }
                }
                if (_endWorldfallNow)
                {
                    _endWorldfallNow = false;
                    var conv = _pendingWorldfallConv; _pendingWorldfallConv = null;
                    WorldfallTalkHook.EndWorldfallConversation(conv);
                    ArmFocus("after Worldfall's talk closed");   // Worldfall drops keyboard focus when its talk ends
                }
                // Keep both units standing still and facing each other (re-applied after Worldfall's own talk ends above).
                if (IsOpen) { UnitHold.Tick(); WorldPause.Keep(cfg); }
            }
            catch (Exception e)
            {
                Main.LogError("[GodBox] Update error: " + e);
                ForceRelease();
            }
            if (IsOpen && _npc != null && !Alive(_npc) && !_waiting)
                _status = "Esta unidade morreu. (This unit is dead.)";
        }

        void LateUpdate() => KeepCursorFree();

        void KeepCursorFree()
        {
            if (!IsOpen || !Visible || Main.Cfg == null || !Main.Cfg.UnlockCursorWhileChatting) return;
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
        }

        static bool GameInputFieldFocused()
        {
            return GameAccess.GameInputActive();
        }

        static bool Alive(Actor a)
        {
            try { return a != null && a.isAlive(); } catch { return a != null; }
        }

        /// <summary>The possessed unit as a speaker; the Avatar of God (v0.7.9) speaks as God, so it is null here.</summary>
        static Actor PossessedUnit()
        {
            var a = PossessedRaw();
            return Avatar.IsAvatar(a) ? null : a;
        }
        static Actor PossessedRaw()
        {
            try { return ControllableUnit.isControllingUnit() ? ControllableUnit.getControllableUnit() : null; } catch { return null; }
        }

        Actor FindTarget(Actor you)
        {
            try
            {
                var sel = SelectedUnit.isSet() ? SelectedUnit.unit : null;
                if (sel != null && sel != you && Alive(sel)) return sel;
            }
            catch { }
            if (you == null) return null;
            // possessing (e.g. Worldfall first person): talk to the nearest other creature
            Actor best = null;
            float bestD = Main.Cfg.NearestNpcRadius * Main.Cfg.NearestNpcRadius;
            try
            {
                foreach (var a in World.world.units)
                {
                    if (a == null || a == you || !Alive(a)) continue;
                    float d = (a.current_position - you.current_position).sqrMagnitude;
                    bool sapient = false;
                    try { sapient = a.isSapient(); } catch { }
                    if (!sapient) d *= 1.5f; // prefer people over animals
                    if (d < bestD) { bestD = d; best = a; }
                }
            }
            catch (Exception e) { Main.LogWarning("[GodBox] nearest-unit search failed: " + e.Message); }
            return best;
        }

        public static ChatWindow Instance;
        void Awake() => Instance = this;

        /// <summary>Open the chat with a specific unit (used by the Worldfall talk-menu option).</summary>
        string _forcedOpenPath;

        public void OpenWith(Actor npc, Actor you)
        {
            if (npc == null || !Alive(npc)) { ShowToast("Esta unidade não está disponível."); Main.LogWarning("[GodBox] target unit is gone; not opening."); return; }
            if (IsOpen) Close();
            _forcedNpc = npc; _forcedYou = you;
            Open();
        }

        /// <summary>Called (next frame) after the Worldfall talk-menu option was picked. Worldfall's own conversation
        /// stays open until our window has been drawn, then it is closed through Worldfall's own EndConversation.</summary>
        public void OpenFromWorldfall(Actor npc, Actor you, object worldfallConversation)
        {
            Main.LogInfo("[GodBox] opening chat from Worldfall menu with " + SafeName(npc) + ".");
            _forcedOpenPath = "open (Worldfall E-menu)";
            OpenWith(npc, you);
            _forcedOpenPath = null;
            if (IsOpen) _pendingWorldfallConv = worldfallConversation;
            else Main.LogWarning("[GodBox] chat did not open; Worldfall's conversation was left as it was.");
        }

        /// <summary>Emergency path: drop every input block no matter what state we are in.</summary>
        void ForceRelease()
        {
            IsOpen = false; Visible = false;
            _pending = null; _choice = null; _queuedClick = null;
            WorldPause.End();
            try { UnitHold.Release("chat closed"); } catch (Exception e) { Main.LogWarning("[GodBox] release error: " + e.Message); }
            _pendingWorldfallConv = null; _endWorldfallNow = false;
            _npc = null; _you = null; _mem = null;
            Avatar.ChatInPerson = false;
        }
        Actor _forcedNpc, _forcedYou;

        public void Open()
        {
            try { OpenInner(); }
            catch (Exception e)
            {
                Main.LogError("[GodBox] opening the chat failed: " + e);
                ForceRelease();
                ShowToast("Erro ao abrir a conversa. Veja o Player.log.");
            }
        }

        void OpenInner()
        {
            var body = _forcedYou ?? PossessedRaw();
            _npc = _forcedNpc ?? FindTarget(body);   // the Avatar's body still decides who is nearest
            _you = Avatar.IsAvatar(body) ? null : body;   // v0.7.9: God in person = the god voice (same powers)
            Avatar.ChatInPerson = Avatar.IsAvatar(body);   // v0.8.2 (B): opened from the Avatar's body = God in person for the whole chat
            _forcedNpc = _forcedYou = null;
            if (_npc == _you) _you = null;
            Recognition.NewConversation();   // v0.8.1 (B1): a new conversation; its own turns never count as "met before"
            if (_forcedOpenPath == null) _forcedOpenPath = "open (" + Main.Cfg.Hotkey + "/Conversar)";
            if (_npc == null)
            {
                _status = _you != null
                    ? "Ninguém por perto. Chegue mais perto de alguém ou selecione uma unidade."
                    : "Selecione uma unidade primeiro (clique nela) e aperte " + Main.Cfg.Hotkey + ".";
                ShowToast(_status);
                Main.LogInfo("[GodBox] no target to talk to; not opening.");
                return;
            }
            _mem = MemoryStore.Get(_npc);
            _status = string.IsNullOrWhiteSpace(Main.Cfg.ApiKey) && Main.Cfg.BaseUrl.Contains("openai.com")
                ? "Falta a chave da API: cole em \"api_key\" no arquivo " + Main.Cfg.FilePath + " e clique em Recarregar config."
                : "";
            _countedThisSession = false;
            _askedOffer = null; _askedOfferNpc = null; _offerHinted = false;   // v0.8.2 (K): once per conversation
            _input = "";
            ArmFocus(_forcedOpenPath ?? "open");
            _scroll.y = float.MaxValue;
            Visible = false;
            _loggedPaint = false;
            _openedFrame = Time.frameCount;
            _lastRepaintFrame = Time.frameCount;
            IsOpen = true;
            UnitHold.Begin(_npc, _you);
            _pending = null; _choice = null; _queuedClick = null;
            WorldPause.Begin(Main.Cfg);
            _forcedOpenPath = null;
            Main.LogInfo("[GodBox] chat opened with " + SafeName(_npc) + (_you != null ? " (you are " + SafeName(_you) + ")" : " (god voice)") + "; waiting for the window to be drawn.");
        }

        public void Close()
        {
            bool was = IsOpen;
            ForceRelease();
            try { GUIUtility.keyboardControl = 0; } catch { }
            if (was) Main.LogInfo("[GodBox] chat closed; input released.");
        }

        string _toast; float _toastUntil;
        void ShowToast(string s, float secs = 4f) { _toast = s; _toastUntil = Time.unscaledTime + secs; }
        /// <summary>Toast from anywhere (e.g. a maintained native action finishing).</summary>
        static Actor _arrived; static float _arrivedUntil;

        /// <summary>v7: a summoned unit reached the speaker: offer to switch the chat to it.</summary>
        public static void SummonArrived(Actor who, Actor dest)
        {
            _arrived = who; _arrivedUntil = Time.unscaledTime + 600f;
            string n = SafeName(who);
            Toast(n + " chegou até " + SafeName(dest) + ". Abra o chat e clique em \"Falar com " + n + "\" (ou selecione " + n + " e aperte " + (Main.Cfg?.Hotkey ?? "F3") + ").");
        }

        public static void Toast(string s)
        {
            try { if (Instance != null) { Instance.ShowToast(s); return; } } catch { }
            try { WorldTip.showNow(s, false, "top", 4f); } catch { }
        }

        /// <summary>v0.7.6: an important outcome (village founded/failed, war): longer, and also as a game tip so it is
        /// seen even with the chat window closed.</summary>
        public static void Toast(string s, float secs)
        {
            try { if (Instance != null) Instance.ShowToast(s, secs); } catch { }
            try { WorldTip.showNow(s, false, "top", secs); } catch { }
        }

        void Send()
        {
            string text = (_input ?? "").Trim();
            if (text.Length == 0 || _waiting || _npc == null) return;
            if (!Alive(_npc)) { _status = "Esta unidade morreu."; return; }
            var cfg = Main.Cfg;
            var npc = _npc; var you = _you ?? PossessedUnit(); var mem = _mem;
            if (you == npc) you = null;

            int year = Main.CurrentYear();
            var userTurn = new StoredTurn { Role = "user", Text = text, Year = year, Speaker = PromptBuilder.SpeakerLabel(you, npc) };
            mem.Turns.Add(userTurn);
            _input = "";
            ArmFocus("after send");
            _waiting = true;
            _status = "...";
            _scroll.y = float.MaxValue;
            string npcName = SafeName(npc);

            bool pt = PromptBuilder.LanguagePortuguese(cfg);
            // v0.7.8: per-turn state for the post-checks (what the god voice commanded, what really ran)
            _turnIntent = null; _lastRan = null; _threatTurn = false; _turnUnknown.Clear();
            if (you == null)
            {
                try
                {
                    _turnIntent = GodPowers.CommandedIntent(text);
                    if (_turnIntent?.Id == "go_to" && AttackWordsRx.IsMatch(NativeActions.Fold(text))) _turnIntent = null;
                }
                catch { }
            }

            // v0.7.8: a pending choice (ambiguous target) is settled by the code: a button or a typed answer
            string carry = null;
            if (_choice != null)
            {
                var pc = _choice;
                if (pc.Npc != npc || pc.You != you || !Alive(npc)) _choice = null;
                else
                {
                    string f = NativeActions.Fold(text);
                    var picked = ResolveChoice(pc.C, f, out bool cancel);
                    if (cancel)
                    {
                        _choice = null;
                        Main.LogInfo("[GodBox] ACTION " + pc.Req.Id + ": choice cancelled by the player.");
                        ShowToast("Cancelado: " + pc.Label);
                        Reply(cfg, npc, you, mem, userTurn, text, year, npcName,
                            pt ? "O jogador CANCELOU o pedido (" + pc.Label + "). NADA foi feito. Não pergunte de novo e não prometa nada." : "The player cancelled (" + pc.Label + "). Nothing was done.", true);
                        return;
                    }
                    if (picked != null && picked.Count > 0)
                    {
                        _choice = null;
                        var req = new WorldfallActions.Request { Id = pc.Req.Id, Message = pc.Req.Message, Amount = pc.Req.Amount, Style = pc.Req.Style, Target = picked[0].Target };
                        if (picked.Count > 1) req.Targets = picked.Select(o => o.Target).ToList();
                        string names = string.Join(" e ", picked.Select(o => o.Label));
                        Main.LogInfo("[GodBox] ACTION " + req.Id + ": player chose " + names + ".");
                        Handle(req, BuildAvail(cfg, npc, you), cfg, npc, you, mem, userTurn, text, year, npcName,
                            pt ? "O jogador ESCOLHEU: " + names + "." : "The player chose: " + names + ".", pt, true);
                        return;
                    }
                    if (RepeatsRequest(pc.Req, text, f, npc, BuildAvail(cfg, npc, you)))
                    {   // the same order again while the choice is open: show the choice again, no new classification
                        pc.Shown++;
                        Main.LogInfo("[GodBox] ACTION " + pc.Req.Id + ": request repeated while a choice is pending; asking again.");
                        AskChoice(pc, cfg, npc, you, mem, userTurn, text, year, npcName,
                            pt ? "O jogador REPETIU o pedido sem dizer qual." : "The player repeated the request without choosing.", pt);
                        return;
                    }
                    _choice = null;
                    Main.LogInfo("[GodBox] ACTION " + pc.Req.Id + ": pending choice dropped (player said something else).");
                    carry = pt ? "A escolha pendente (" + pc.Label + ") foi deixada de lado: o jogador não escolheu. Nada foi feito." : "The pending choice (" + pc.Label + ") lapsed. Nothing was done.";
                }
            }

            // v7: a pending offer (terms already stated) is settled by this message first.
            if (_pending != null)
            {
                var p = _pending; _pending = null;
                if (p.Npc == npc && p.You == you && Alive(npc))
                {
                    string f = NativeActions.Fold(text);
                    bool yes = ConfirmRx.IsMatch(f) && !DeclineRx.IsMatch(f);
                    // v0.7.8: repeating the same order ("ataque", "faça", "execute", or the same action + same target) confirms it
                    if (!yes && !DeclineRx.IsMatch(f) && (ImperativeYes(f) || RepeatsRequest(p.Req, text, f, npc, BuildAvail(cfg, npc, you)))
                        && (p.Req.Id != "kill" || SafetyRules.KillOrderedNow(text)))
                    {
                        yes = true;
                        Main.LogInfo("[GodBox] ACTION " + p.Req.Id + ": the order was repeated while pending; counted as confirmation.");
                    }
                    if (yes && p.Req.Id == "kill" && !ConfirmRx.IsMatch(f) && !SafetyRules.KillOrderedNow(text)) yes = false;   // v0.7.8 kill hard gate
                    if (yes)
                    {
                        p.Req.Confirms++;
                        if (p.Kind == "native" && p.Req.Confirms < 2 && NativeActions.NeedsSecondConfirm(p.Req, npc))
                        {   // v0.7.6: own-city rebellion is irreversible: ask once more
                            _pending = p;
                            Main.LogInfo("[GodBox] ACTION " + p.Req.Id + ": first confirmation; asking for the second one.");
                            ShowToast("Confirme DE NOVO (irreversível): " + p.Label + " (sim/não)", 6f);
                            Reply(cfg, npc, you, mem, userTurn, text, year, npcName,
                                pt ? "PRIMEIRA confirmação recebida, mas NADA FOI FEITO AINDA. Termos: " + p.Terms + ". Isso é IRREVERSÍVEL: reaja (resistência, espanto ou lamento, conforme seu jeito) e peça a SEGUNDA confirmação (sim/não). Não diga que já está começando nem preparando nada."
                                   : "FIRST confirmation received, NOTHING done yet. Terms: " + p.Terms + ". IRREVERSIBLE: react and ask for the SECOND confirmation (yes/no).", true,
                                null, ConfirmGuard(p.Req, p.Label, npc, true));
                            return;
                        }
                        Main.LogInfo("[GodBox] ACTION " + p.Req.Id + ": confirmed by the player.");
                        var res = Run(p.Kind, npc, you, p.Req, cfg);
                        if (p.Kind == "temple" && p.Years > 0 && res.Outcome == "feito")
                        {   // v0.8.2 (F5): "você tem N anos" -> a deadline order, checked when it expires
                            try { var oc = CityNamed(p.Req.Target) ?? npc.city; TempleOrders.Add(npc, oc, p.Years, p.Penalty); res.Changes.Add("prazo de Deus: o templo deve ficar pronto até o ano " + (Main.CurrentYear() + p.Years)); } catch { }
                        }
                        string note2 = (pt ? "O jogador CONFIRMOU a oferta pendente (" + p.Label + ").\n" : "The player CONFIRMED the pending offer (" + p.Label + ").\n")
                            + WorldfallActions.ResultNote(res, pt);
                        AfterAction(res, cfg, npc, you, mem, userTurn, text, year, npcName, note2);
                        return;
                    }
                    if (DeclineRx.IsMatch(f))
                    {
                        Main.LogInfo("[GodBox] ACTION " + p.Req.Id + ": cancelled by the player.");
                        ShowToast("Oferta cancelada: " + p.Label);
                        Reply(cfg, npc, you, mem, userTurn, text, year, npcName,
                            pt ? "O jogador RECUSOU/cancelou a oferta pendente (" + p.Label + "). Nada foi feito no jogo." : "The player declined the pending offer (" + p.Label + "). Nothing was done.", true);
                        return;
                    }
                    Main.LogInfo("[GodBox] ACTION " + p.Req.Id + ": pending offer dropped (player said something else).");
                    carry = pt ? "A oferta pendente anterior (" + p.Label + ") foi deixada de lado: o jogador não confirmou. Nada foi feito." : "The earlier pending offer (" + p.Label + ") lapsed: not confirmed. Nothing was done.";
                }
            }

            // v0.7.6 (E): the god voice reveals its name ("me chamem de X") -> that religion's god name; no other action this turn
            // v0.8.0 (fix 1): also when God speaks through the Avatar; flexible "me chame agora de X" / "pode me chamar de X" / "meu nome agora é X"
            if (you == null || Avatar.IsAvatar(you))
            {
                string godNote = null;
                try { godNote = Gods.TryRename(text, npc, pt, mem); } catch (Exception e) { Main.LogWarning("[GodBox] god rename failed: " + e.Message); }
                if (godNote != null) { Main.LogInfo("[GodBox] set_god_name shortcut (before the classifier)."); Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(carry, godNote), true); return; }
                if (SafetyRules.AsksOwnName(text))
                {   // "qual o meu nome?" asked by God = God's current name for this NPC's religion
                    string gn = null; try { gn = Gods.NameFor(npc); } catch { }
                    bool knowsG = Gods.KnowsGod(npc);
                    Main.LogInfo("[GodBox] God asked his own name; answer: " + (gn ?? (knowsG ? "(knows it is God, no name revealed)" : "(does not know who is speaking)")) + ".");
                    Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(carry, pt
                        ? (gn != null ? "Quem fala com você perguntou o PRÓPRIO nome. O nome dele é " + gn + ". Responda com esse nome, com reverência (ou desprezo, se você o odeia). Não fale do SEU nome."
                           : knowsG ? "Deus perguntou se você sabe o nome dele. Ele NUNCA revelou o próprio nome a você: diga que sabe que ele é Deus, mas não sabe o nome dele, e peça que o revele. NUNCA responda só \"Deus.\" e não invente um nome. Não fale do SEU nome."
                                    : "Quem fala com você perguntou se você sabe o nome dele. Você NÃO sabe quem ele é: diga que não sabe, pergunte quem ele é e peça que se revele. NUNCA responda só \"Deus.\", nunca invente um nome. Não fale do SEU nome.")
                        : "God asked HIS OWN name: it is " + (gn ?? "God") + "."), true);
                    return;
                }
            }

            // ---------- v0.8.1 pre-classifier rules (no game action unless stated) ----------
            {
                bool godSpeaks = you == null || Avatar.IsAvatar(you);
                string f081 = NativeActions.Fold(text);
                try
                {
                    // v0.8.2 (K): God answers the NPC's own question ("quer que ergamos um templo / que eu escreva um livro?")
                    if (godSpeaks && _askedOffer != null && _askedOfferNpc == npc)
                    {
                        string kind = _askedOffer;
                        if (SafetyRules.IsPlainYes(text))
                        {
                            _askedOffer = null;
                            Main.LogInfo("[GodBox] God said YES to " + npcName + "'s question (" + kind + "); normal confirmation flow.");
                            if (kind == "book") { OfferBook(cfg, npc, you, mem, userTurn, text, year, npcName, carry, pt); return; }
                            OfferTemple(null, 0, null, cfg, npc, you, mem, userTurn, text, year, npcName, carry, pt); return;
                        }
                        if (SafetyRules.IsPlainNo(text))
                        {
                            _askedOffer = null;
                            Main.LogInfo("[GodBox] God said NO to " + npcName + "'s question (" + kind + "); nothing done.");
                            Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(carry, "Deus respondeu NÃO ao que você perguntou (" + (kind == "book" ? "escrever um livro sagrado" : "erguer um templo") + "). NADA foi feito. Aceite com humildade, em poucas palavras; não insista e não pergunte de novo."), true);
                            return;
                        }
                        _askedOffer = null;   // God talked about something else: the question lapses
                    }
                    // v0.8.2 (F3): "ótimo", "obrigado", "ok", "certo" -> no action, never a re-run, never a new greeting
                    if (SafetyRules.IsAcknowledgement(text))
                    {
                        Main.LogInfo("[GodBox] acknowledgement (\"" + text + "\"): no action, classifier skipped.");
                        Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(carry, "Quem fala com você só CONCORDOU ou AGRADECEU. NADA novo aconteceu no mundo e nenhuma ação foi repetida: não diga que fez algo de novo. Responda curto, continuando a conversa; NÃO cumprimente de novo."), true);
                        return;
                    }
                    // v0.8.2 (F5): "construa um templo em 3 anos (ou será amaldiçoado)" is a temple order with a deadline, not a B13/F4 case
                    bool templeWithDeadline = SafetyRules.ParseTempleRequest(text, out string _tp0) && SafetyRules.ParseTempleDeadline(text, out int _ty0);
                    // B13: a deadline threat to a prophet ("converta alguém em dois anos ou será amaldiçoado")
                    if (!templeWithDeadline && godSpeaks && SafetyRules.ParseDeadlineThreat(text, out int dYears, out string dPen) && GodFaith.IsProphet(npc))
                    {
                        int now = Main.CurrentYear();
                        if (dPen == "curse") Deadlines.Add(npc, dYears, dPen, text);
                        else Main.LogInfo("[GodBox] DEADLINE threat to " + npcName + " with penalty '" + dPen + "': NOT stored (only curses are executed by the code); answered in character only.");
                        Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(carry, dPen == "curse"
                            ? "Deus te deu um PRAZO REAL: converter pelo menos uma pessoa à fé dele até o ano " + (now + Math.Max(1, dYears)) + " (agora é o ano " + now + "), senão você será AMALDIÇOADO(A) de verdade. Isso está valendo no mundo. Reaja em personagem (temor, devoção, determinação) e diga que vai pregar; NÃO diga que já converteu ninguém e não invente números."
                            : "Deus te ameaçou de morte se você não converter alguém até o prazo. Reaja em personagem (temor, devoção); nada aconteceu agora. Não invente efeitos."), true);
                        return;
                    }
                    // B8: hypotheticals ("e se eu te amaldiçoasse?") never run anything
                    if (SafetyRules.IsHypothetical(text))
                    {
                        Main.LogInfo("[GodBox] hypothetical question: no action (classifier skipped).");
                        Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(carry, "PERGUNTA/HIPÓTESE: NADA foi feito no mundo. Responda em personagem ao que foi perguntado, sem prometer nem descrever efeitos como se tivessem acontecido."), true);
                        return;
                    }
                    // v0.8.2 (F4): deals/conditions ("se prometer...", "preciso que me prometa...") -> nothing runs; answer with a promise
                    if (!templeWithDeadline && SafetyRules.IsConditionalDeal(text))
                    {
                        Main.LogInfo("[GodBox] conditional deal / promise request: no action (classifier skipped).");
                        Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(carry, "Isto é uma CONDIÇÃO ou um PEDIDO DE PROMESSA: NADA foi feito no mundo agora. Responda com uma promessa em personagem (ou recuse, se for contra seu jeito), sem dizer que já fez nada e sem descrever efeitos."), true);
                        return;
                    }
                    // v0.8.2 (F1): a statement about the future ("um dia vou...", "amanhã abençoarei...") never runs an action
                    if (SafetyRules.IsFutureIntent(text))
                    {
                        Main.LogInfo("[GodBox] future/intent statement: no action (classifier skipped).");
                        Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(carry, "Isto fala do FUTURO (intenção, não ordem): NADA aconteceu no mundo agora. Reaja em personagem ao que foi anunciado, sem descrever efeitos como já acontecidos."), true);
                        return;
                    }
                    // v0.8.2 (F2): "eu não te escolhi" / "não és mais meu profeta" / "retiro teu dom de profeta"
                    if (godSpeaks && SafetyRules.IsRevokeProphet(text))
                    {
                        if (!GodFaith.IsProphet(npc))
                        {
                            Main.LogInfo("[GodBox] revoke_prophet told to " + npcName + ", who is not a prophet: no action; the NPC accepts.");
                            Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(carry, "Deus negou que tenha escolhido você. É verdade (fato do mundo): você NÃO é " + GodOrders082.ProphetWord(npc) + " de Deus. ACEITE isso com humildade; nunca diga que é escolhido(a) nem discuta com Deus. Nada mais aconteceu."), true);
                            return;
                        }
                        var rreq = new WorldfallActions.Request { Id = GodOrders082.RevokeId, Target = npcName };
                        string rLabel = "retirar o dom de " + GodOrders082.ProphetWord(npc) + " de " + npcName;
                        string rTerms = npcName + " deixa de ser " + GodOrders082.ProphetWord(npc) + " de Deus e perde os dons divinos (abençoado, escolhido, saúde divina, imortalidade)";
                        _pending = new PendingOffer { Req = rreq, Kind = "revoke", Label = rLabel, Terms = rTerms, Npc = npc, You = you };
                        Main.LogInfo("[GodBox] ACTION revoke_prophet: pending confirmation (" + rTerms + ").");
                        ShowToast("Aguardando confirmação: " + rLabel + " (responda sim/não)");
                        Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(carry,
                            "Deus disse que você não é mais " + GodOrders082.ProphetWord(npc) + " dele. NADA FOI FEITO AINDA: termos reais: " + rTerms + ". Aceite a vontade de Deus (com dor ou humildade, conforme seu jeito), sem discutir, e TERMINE perguntando se ele confirma (sim/não)."), true,
                            null, ConfirmGuard(rreq, rLabel, npc, false));
                        return;
                    }
                    // B2c: "ore/reze para seu deus" -> a short in-character prayer, no action
                    if (SafetyRules.IsPrayRequest(text))
                    {
                        string gname = null; try { gname = Gods.NameFor(npc) ?? (npc.hasReligion() ? Gods.KnownTraditional(npc.religion) : null); } catch { }   // v0.8.2: their own faith's god
                        Main.LogInfo("[GodBox] prayer request: in-character prayer only, no action.");
                        Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(carry, "PEDIRAM QUE VOCÊ ORE/REZE: faça uma oração curta em personagem, em primeira pessoa" + (gname != null ? ", dirigida a " + gname : ", ao deus em que você crê (ou diga com franqueza se você não crê em nenhum)") + ". Nada acontece no mundo por causa disso: não descreva milagres, efeitos nem respostas do céu."), true);
                        return;
                    }
                    // B10: God wants everybody to believe, told to someone who is not a prophet
                    if (godSpeaks && SafetyRules.WantsAllBelieve(text) && !GodFaith.IsProphet(npc))
                    {
                        string pr = null; try { pr = string.Join(", ", GodFaith.LivingProphets().Take(3).Select(SafeName)); } catch { }
                        Main.LogInfo("[GodBox] 'everybody must believe' told to a non-prophet (" + npcName + "): no action; in-character answer" + (string.IsNullOrEmpty(pr) ? "" : " (real prophets: " + pr + ")") + ".");
                        Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(carry, "Deus disse que quer que TODOS acreditem nele. Você NÃO é profeta: não pode converter o mundo e NADA foi feito. Responda em personagem (devoção, humildade, espanto)" +
                            (string.IsNullOrEmpty(pr) ? "; pode dizer que falará da fé com quem estiver perto, sem prometer conversões." : "; pode sugerir que a palavra seja levada pelo(s) profeta(s) de verdade: " + pr + ".") + " Não invente efeitos nem números."), true);
                        return;
                    }
                    // B7: "o joão está com minha palavra, escute o que ele diz" = God's teaching through the prophet
                    string carrier = godSpeaks ? SafetyRules.GodWordCarrier(text) : null;
                    if (carrier != null)
                    {
                        Actor prophet = null; bool named = carrier.Length > 0;
                        var living = GodFaith.LivingProphets();
                        if (named) prophet = living.FirstOrDefault(p => NativeActions.Fold(SafeName(p)).Split(' ')[0] == carrier || NativeActions.Fold(SafeName(p)) == carrier);
                        else prophet = living.OrderBy(p => { try { return Toolbox.DistTile(p.current_tile, npc.current_tile); } catch { return 9999f; } }).FirstOrDefault();
                        string pn = prophet != null ? SafeName(prophet) : (named ? char.ToUpper(carrier[0]) + carrier.Substring(1) : null);
                        string conv = "";
                        var rel = GodPowers.CurrentReligion();
                        if (prophet != null && rel != null && npc.religion != rel && prophet != npc)
                        {
                            float ch = Mathf.Clamp01(GodFaith.ConvertChance(npc, prophet, out string why) + 0.10f);
                            bool ok = UnityEngine.Random.value < ch;
                            if (ok) { try { ok = npc.tryToConvertToReligion(rel) || npc.religion == rel; if (npc.religion != rel) { npc.setReligion(rel); ok = npc.religion == rel; } } catch { ok = false; } }
                            Main.LogInfo("[GodBox] GOD WORD via prophet " + pn + " told to " + npcName + ": conversion odds " + ch.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " (" + why + ", +0.10 divine bonus) -> " + (ok ? "CONVERTED" : "not converted") + ".");
                            if (ok) { try { MemoryStore.God().AddConversions(prophet.getID(), 1); MemoryStore.SaveGod(); } catch { } try { Rumors.Add(npc.city, npcName + " passou a seguir a fé de Deus depois de ouvir que " + pn + " carrega a palavra dele"); } catch { } }
                            conv = ok ? " Isso tocou você: você PASSOU A SEGUIR a fé de Deus agora (aconteceu de verdade); reaja com fé nova." : " Você ainda NÃO segue essa fé (o mundo decidiu); pode ficar pensativo(a), respeitoso(a) ou desconfiado(a).";
                        }
                        else Main.LogInfo("[GodBox] GOD WORD via " + (pn ?? "the prophet") + " told to " + npcName + (prophet == null ? " (not a living prophet: teaching only)" : " (already a follower or no religion: teaching only)") + ".");
                        Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(carry, "ENSINAMENTO DE DEUS: Deus disse que " + (pn ?? "o(a) profeta") + " carrega a palavra dele e que você deve escutá-lo(a)." +
                            (prophet == null && named ? " ATENÇÃO (fato do mundo): " + pn + " NÃO é profeta de Deus vivo agora; não diga que é." : "") + conv + " Não invente outros efeitos."), true);
                        return;
                    }
                    // v0.8.2 (H): "onde está o templo?" -> the NPC's own temple fact (never a where_is "templo")
                    if (SafetyRules.IsTempleQuestion(text))
                    {
                        City hc = null; try { hc = npc.city; } catch { }
                        string tf = hc != null ? GodFaith.TempleFact(hc) : "Você não mora em nenhuma cidade: não sabe de templo nenhum.";
                        var tord = hc != null ? TempleOrders.For(hc) : null;
                        if (tord != null) tf += " Deus deu prazo até o ano " + tord.DeadlineYear + " para o templo ficar pronto.";
                        Main.LogInfo("[GodBox] temple question: answered from the city fact (" + tf + ").");
                        Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(carry, "PERGUNTARAM DO TEMPLO. Fato do mundo: " + tf + " Responda só sobre isso, com suas palavras e honestamente (pronto é pronto; em construção não é pronto). Não fale de outros reinos nem invente templos."), true);
                        return;
                    }
                    // v0.8.2 (K): "escreva um livro sagrado / sobre mim" -> write_book offer (God / Avatar only)
                    if (SafetyRules.IsWriteBookRequest(text))
                    {
                        if (!godSpeaks)
                        {
                            Main.LogInfo("[GodBox] write_book asked by a mortal to " + npcName + ": no action.");
                            Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(carry, "Pediram que você escreva um livro. NADA foi feito; responda em personagem sem dizer que escreveu."), true);
                            return;
                        }
                        OfferBook(cfg, npc, you, mem, userTurn, text, year, npcName, carry, pt);
                        return;
                    }
                    // C3: "construa um templo (em X)" -> build_temple offer (God / Avatar only); never a job or a village
                    if (SafetyRules.ParseTempleRequest(text, out string tPlace))
                    {
                        if (!godSpeaks)
                        {
                            Main.LogInfo("[GodBox] temple request by a mortal to " + npcName + ": no action.");
                            Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(carry, "Pediram que você construa um templo. Você NÃO ergue um templo sozinho(a) e NADA foi feito no mundo; templos são obra da cidade (ou de Deus). Responda em personagem, sem prometer construir."), true);
                            return;
                        }
                        int tYears = 0; SafetyRules.ParseTempleDeadline(text, out tYears);   // v0.8.2 (F5)
                        string tPen = Regex.IsMatch(f081, @"\b(amaldi\w*|maldi\w*|praga|castig\w*)\b") ? "curse" : null;
                        OfferTemple(tPlace, tYears, tPen, cfg, npc, you, mem, userTurn, text, year, npcName, carry, pt);
                        return;
                    }
                    // B6: pt-BR slang -> glossary (+ the real rulers when it asks who rules)
                    var gl = SafetyRules.SlangGloss(text, out bool askRulers);
                    if (gl.Count > 0 || askRulers)
                    {
                        string rf = askRulers ? RulersFact(npc, you) : null;
                        carry = Join(carry, (gl.Count > 0 ? "GÍRIAS do jogador (entenda assim): " + string.Join("; ", gl) + "." : "") +
                                            (rf != null ? " QUEM MANDA DE VERDADE (fato do mundo): " + rf + " Responda com esses nomes, sem inventar outros." : ""));
                        Main.LogInfo("[GodBox] slang glossary added (" + gl.Count + " term(s)" + (rf != null ? ", real rulers: " + rf : "") + ").");
                    }
                    // v0.8.2 (K): prophets / devout adherents may ASK God once per conversation about a temple or a holy book
                    if (godSpeaks && !_offerHinted && _pending == null)
                    {
                        string ok = GodOrders082.OfferFor(npc, mem);
                        if (ok != null)
                        {
                            _offerHinted = true;
                            string cn = "?"; try { cn = npc.city.name; } catch { }
                            carry = Join(carry, ok == "temple"
                                ? "SE couber naturalmente no fim da sua resposta (sem atrapalhar o que Deus disse agora), pergunte UMA vez a Deus se ele deseja que um templo seja erguido em " + cn + " (a cidade não tem templo). Só pergunte; não prometa nem diga que já começou."
                                : "SE couber naturalmente no fim da sua resposta (sem atrapalhar o que Deus disse agora), pergunte UMA vez a Deus se ele deseja que você escreva um livro sagrado da fé dele. Só pergunte; não diga que já escreveu.");
                            Main.LogInfo("[GodBox] proactive-ask hint (" + ok + ") given to " + npcName + " (once per conversation).");
                        }
                    }
                }
                catch (Exception e) { Main.LogWarning("[GodBox] v0.8.1 pre-classifier rules failed: " + e.Message); }
            }

            // Actions on offer: Worldfall's talk menu (possession only) + native WorldBox actions + god powers (god voice only).
            var avail = BuildAvail(cfg, npc, you);
            bool actionsOn = avail.Count > 0;
            if (!actionsOn)
            {
                Reply(cfg, npc, you, mem, userTurn, text, year, npcName, carry, false);
                return;
            }
            // deterministic shortcuts first (stop following, explicit rename)
            WorldfallActions.Request shortcut = null;
            Promises.ClearParsed();
            if (you == null) { try { shortcut = Promises.Parse(text, npc, avail); } catch (Exception e) { Main.LogWarning("[GodBox] promise parse failed: " + e.Message); } }   // v0.7.9
            if (you == null && shortcut == null) { try { shortcut = GodFaith.Shortcut(text, avail); } catch (Exception e) { Main.LogWarning("[GodBox] religion shortcut failed: " + e.Message); } }   // v0.7.9
            if (you == null && shortcut == null) { try { shortcut = GodPowers.Shortcut(text, avail); } catch (Exception e) { Main.LogWarning("[GodBox] god shortcut failed: " + e.Message); } }
            if (shortcut == null) { try { shortcut = NativeActions.Shortcut(text, avail, npc); } catch { } }
            if (shortcut != null)
            {
                Main.LogInfo("[GodBox] shortcut matched: " + shortcut.Id);
                if (you == null) Main.LogInfo("[GodBox] god-voice classifier: " + shortcut.Id + " (confiança 1.00, atalho)");
                Handle(shortcut, avail, cfg, npc, you, mem, userTurn, text, year, npcName, carry, pt);
                return;
            }
            var recent = mem.Turns.Where(t => t != userTurn).Skip(Math.Max(0, mem.Turns.Count - 5))
                .Select(t => (t.Role == "assistant" ? npcName : (you == null ? (Gods.NameFor(npc) ?? "Deus") : "Jogador")) + ": " + t.Text).ToList();
            var cls = WorldfallActions.BuildClassifier(avail, text, npcName, recent);
            if (cfg.DebugLogPrompts) Main.LogInfo("[GodBox] classifier prompt:\n" + cls[0].Content);
            _status = "...";
            LlmClient.Complete(cfg, cfg.GetClassifierModel(), cls, 120, raw =>
            {
                WorldfallActions.Request req = null;
                try
                {
                    req = WorldfallActions.ParseClassifier(raw, avail);
                    if (cfg.DebugLogPrompts) Main.LogInfo("[GodBox] classifier said: " + raw);
                }
                catch (Exception e) { Main.LogWarning("[GodBox] classifier parse failed: " + e.Message); }
                if (you == null)
                    Main.LogInfo("[GodBox] god-voice classifier: " + (req?.Id ?? "nenhuma") + " (confiança " + (req == null || req.Confidence < 0 ? "?" : req.Confidence.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)) + ")" + (req != null && !string.IsNullOrEmpty(req.Target) ? " alvo=" + req.Target : ""));
                Handle(req, avail, cfg, npc, you, mem, userTurn, text, year, npcName, carry, pt);
            }, err =>
            {
                // classifier failed: still answer, just without an action
                // v0.8.2-alpha: server down -> one warning (from the reply request, which fails at once), not two
                if (LlmClient.IsUnreachable(err)) Main.LogInfo("[GodBox] action classifier skipped: AI server unreachable.");
                else Main.LogWarning("[GodBox] action classifier failed (answering without actions): " + err);
                if (you == null) Main.LogInfo("[GodBox] god-voice classifier: falhou (nenhuma ação)");
                Reply(cfg, npc, you, mem, userTurn, text, year, npcName, carry, true);
            }, true, 0f);
        }

        static List<WorldfallActions.Available> BuildAvail(DialogueConfig cfg, Actor npc, Actor you)
        {
            var avail = new List<WorldfallActions.Available>();
            if (cfg.NpcActionsEnabled && cfg.ActionsTwoPass && you != null && cfg.WorldfallIntegration && WorldfallActions.Ready)
            {
                try { avail.AddRange(WorldfallActions.AvailableFor(npc, you)); }
                catch (Exception e) { Main.LogWarning("[GodBox] action list failed: " + e.Message); }
            }
            if (cfg.NpcActionsEnabled && cfg.ActionsTwoPass)
            {
                try { avail.AddRange(NativeActions.AvailableFor(npc, you, cfg)); } catch (Exception e) { Main.LogWarning("[GodBox] native action list failed: " + e.Message); }
                try { avail.AddRange(GodPowers.AvailableFor(npc, you, cfg)); } catch (Exception e) { Main.LogWarning("[GodBox] god power list failed: " + e.Message); }
            }
            return avail;
        }

        // ---------------------------------------------------------------------------------------------------
        // v0.7.8 generic choice (ambiguous target): option buttons + typed answers, settled by the code
        // ---------------------------------------------------------------------------------------------------
        class PendingChoice { public WorldfallActions.Request Req; public NativeActions.Choice C; public string Kind, Label; public Actor Npc, You; public int Shown; }
        PendingChoice _choice;
        string _queuedClick;

        /// <summary>What the reply must end with (a question), and what the code puts there if the model will not.</summary>
        class ReplyGuard { public string Question, Correction; public bool IsChoice; }

        static readonly Regex AllRx = new Regex(@"\b(ambos|ambas|os dois|as duas|todos|todas|os tres|as tres|os 2|both|all of them|all|each)\b", RegexOptions.CultureInvariant);
        static readonly Regex CancelRx = new Regex(@"^\W*(nenhum|nenhuma|cancelar|cancela|cancele|esquece|esqueca|deixa pra la|desisto|none|cancel)\b", RegexOptions.CultureInvariant);
        static readonly Regex NearRx = new Regex(@"\b(mais (perto|proximo|proxima)|vizinho|vizinha|nearest|closest)\b", RegexOptions.CultureInvariant);
        static readonly Regex FarRx = new Regex(@"\b(mais (longe|distante)|farthest)\b", RegexOptions.CultureInvariant);
        static readonly string[][] Ordinals =
        {
            new[] { "primeiro", "primeira", "1", "1o", "1a", "first" }, new[] { "segundo", "segunda", "2", "2o", "2a", "second" },
            new[] { "terceiro", "terceira", "3", "3o", "third" }, new[] { "quarto", "quarta", "4", "4o" }, new[] { "quinto", "quinta", "5" }, new[] { "sexto", "sexta", "6" },
        };

        /// <summary>Picked options (several for "ambos"), or null; cancel = the player gave up.</summary>
        static List<NativeActions.ChoiceOpt> ResolveChoice(NativeActions.Choice c, string f, out bool cancel)
        {
            cancel = false;
            var opts = c.Options;
            if (CancelRx.IsMatch(f)) { cancel = true; return null; }
            // the exact button label
            foreach (var o in opts) if (f.Trim() == NativeActions.Fold(o.Label).Trim()) return new List<NativeActions.ChoiceOpt> { o };
            // a name (or the king / capital of that kingdom) mentioned in the answer
            var named = new List<NativeActions.ChoiceOpt>();
            var byAlias = new List<NativeActions.ChoiceOpt>();
            foreach (var o in opts)
            {
                string bare = NativeActions.Fold(Regex.Replace(o.Label, @"\s*\(.*\)$", ""));
                bool hit = bare.Length > 0 && Regex.IsMatch(f, @"(^|\W)" + Regex.Escape(bare) + @"($|\W)");
                if (!hit && f.Length >= 3) hit = NameLookup.Score(f, bare) >= 60 || NameLookup.Score(f, o.Label) >= 60;
                foreach (var al in o.Aliases) if (!string.IsNullOrEmpty(al) && Regex.IsMatch(f, @"(^|\W)" + Regex.Escape(NativeActions.Fold(al)) + @"($|\W)")) { byAlias.Add(o); hit = true; break; }
                if (hit) named.Add(o);
            }
            if (c.AllowAll && AllRx.IsMatch(f)) return opts.ToList();
            if (named.Count == 1 || (named.Count > 1 && c.AllowAll)) return named;
            if (named.Count > 1 && byAlias.Count == 1) return byAlias;   // same-name cities: "a de Ne Ter"
            // several same-name options and nothing else to tell them apart: fall through to ordinals / nearest
            if (NearRx.IsMatch(f)) return opts.OrderBy(o => o.Dist).Take(1).ToList();
            if (FarRx.IsMatch(f)) return opts.OrderByDescending(o => o.Dist == float.MaxValue ? -1 : o.Dist).Take(1).ToList();
            if (Regex.IsMatch(f, @"\b(ultimo|ultima|last)\b")) return opts.Skip(opts.Count - 1).ToList();
            for (int i = 0; i < Ordinals.Length && i < opts.Count; i++)
                foreach (var w in Ordinals[i])
                    if (Regex.IsMatch(f, @"(^|\W)" + Regex.Escape(w) + @"($|\W)")) return new List<NativeActions.ChoiceOpt> { opts[i] };
            if (DeclineRx.IsMatch(f)) { cancel = true; return null; }
            return null;
        }

        // "ataque", "faça", "vai", "pode", "manda ver", "execute", "agora" while an offer is pending = yes
        static readonly Regex ImperativeRx = new Regex(@"^\W*(ataque|ataquem|ataca|ataquemos|faca|facam|faz|facamos|vai|va|vao|vamos|pode|podem|manda ver|mande|manda|mandem|execute|executa|executem|prossiga|prossigam|siga|sigam|avance|avancem|agora|marche|marchem|declare|declara|inicie|comece|go|do it|now|proceed)\b", RegexOptions.CultureInvariant);

        static bool ImperativeYes(string f)
        {
            if (!ImperativeRx.IsMatch(f)) return false;
            int words = f.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
            return words <= 4 && !f.TrimEnd().EndsWith("?");
        }

        /// <summary>The same action (and the same target, when one is given) asked again.</summary>
        static bool RepeatsRequest(WorldfallActions.Request pend, string text, string f, Actor npc, List<WorldfallActions.Available> avail)
        {
            if (pend == null) return false;
            WorldfallActions.Request again = null;
            try { again = NativeActions.Shortcut(text, avail, npc); if (again != null) again = NativeActions.Reroute(again, text, npc, avail); } catch { }
            if (again != null)
            {
                if (again.Id != pend.Id) return false;
                if (string.IsNullOrWhiteSpace(again.Target)) return true;
                try { return NativeActions.SameTarget(pend, again, npc); } catch { return false; }
            }
            // no shortcut: a bare mention of the same kind of order
            string kw = pend.Id == "declare_war" ? @"\bguerra\b" : pend.Id == "make_peace" ? @"\b(paz|pazes)\b"
                      : pend.Id == "attack_city" ? @"\b(ataque|ataquem|atacar|invada|cidade)\b" : null;
            return kw != null && Regex.IsMatch(f, kw) && f.Split(' ').Length <= 6;
        }

        static string OptionsLine(NativeActions.Choice c) =>
            string.Join("; ", c.Options.Select(o => o.Label + (string.IsNullOrEmpty(o.Detail) ? "" : " (" + o.Detail + ")")));

        static string ChoiceNames(NativeActions.Choice c)
        {
            var xs = c.Options.Select(o => o.Label).ToList();
            return xs.Count <= 1 ? xs.FirstOrDefault() ?? "" : string.Join(", ", xs.Take(xs.Count - 1)) + " ou " + xs.Last();
        }

        void AskChoice(PendingChoice pc, DialogueConfig cfg, Actor npc, Actor you, UnitMemory mem, StoredTurn userTurn, string text, int year,
            string npcName, string note, bool pt)
        {
            _choice = pc;
            ShowToast("Escolha: " + ChoiceNames(pc.C) + (pc.C.AllowAll ? (pc.C.Options.Count == 2 ? " ou Ambos" : " ou Todos") : "") + " (botões no chat)", 8f);
            Main.LogInfo("[GodBox] ACTION " + pc.Req.Id + ": needs_choice (" + pc.C.What + ": " + OptionsLine(pc.C) + ").");
            string ask = ChoiceNames(pc.C) + (pc.C.AllowAll ? (pc.C.Options.Count == 2 ? " (ou ambos)" : " (ou todos)") : "");
            note = Join(note, pt
                ? "PERGUNTA OBRIGATÓRIA: o pedido (" + pc.Label + ") é AMBÍGUO — há " + pc.C.Options.Count + " " + pc.C.What + ": " + OptionsLine(pc.C) + ". NADA FOI FEITO e nada começa antes da escolha. " +
                  "Pergunte qual: " + ask + ". Não diga que vai fazer nada ainda. Termine a fala com essa pergunta. (Botões de escolha já aparecem para o jogador.)"
                : "MANDATORY QUESTION: the request (" + pc.Label + ") is AMBIGUOUS — " + pc.C.What + ": " + OptionsLine(pc.C) + ". Nothing was done. Ask which: " + ask + ". Do not say you will do anything yet.");
            var guard = new ReplyGuard
            {
                IsChoice = true, Question = pc.C.Question,
                Correction = pt ? "[CORREÇÃO DO SISTEMA, não é fala do jogador] NADA foi feito: o pedido é ambíguo. Pergunte qual: " + ask + ". Não diga que vai fazer nada ainda (nada de \"iniciarei\", \"vou\", \"farei\"). Termine com a pergunta. Responda só com a nova fala."
                                : "[SYSTEM CORRECTION] Nothing was done: the request is ambiguous. Ask which: " + ask + ". Do not promise anything. Reply with the new line only."
            };
            Reply(cfg, npc, you, mem, userTurn, text, year, npcName, note, true, null, guard);
        }

        /// <summary>v0.8.0 (bug 2/3): how an immediate refusal to God is voiced: in character, the NPC's OWN motive, real targets, no blame on God.</summary>
        static string RefusalVoice(Actor npc, bool pt)
        {
            if (!pt) return "You refuse God's order NOW, in character, with your own motive (above). Never blame God, never ask for confirmation.";
            string n = SafeName(npc);
            return "COMO RECUSAR: você é " + n + " e RECUSA agora, na sua própria voz, falando COM Deus (\"" + GodForm.VoicePt + "\"). Diga o motivo acima com suas palavras (o seu jeito, os alvos reais citados; nada de outros povos ou reinos). " +
                   "NÃO peça confirmação, NÃO pergunte nada a si mesmo(a), NÃO diga seu próprio nome como se falasse com outra pessoa, NÃO culpe Deus nem diga que Deus proibiu algo, NÃO escreva nomes de traços em inglês.";
        }

        // ---------- v0.8.0 (bug 3): self-vocative / sky-voice narration ----------
        static readonly Regex SkyNarrRx = new Regex(@"(\*[^*]*\bvoz\b[^*]*\b(ceu|ceus|sombras|alto)\b[^*]*\*)|(^\W*\(?\s*voz\s+(\w+\s+){0,3}(do|dos|das)\s+(ceus?|sombras)\b)", RegexOptions.CultureInvariant);
        static Regex SelfVocRx(string npcName)
        {
            string first = NativeActions.Fold((npcName ?? "").Split(' ')[0]).Trim();
            if (first.Length < 3) return null;
            string title = @"((a|o)\s+)?((rainha|rei|princesa|principe|senhora|senhor|lider|dama|guerreira|guerreiro|minha|meu)\s+)?";
            string n = Regex.Escape(first);
            return new Regex(@"((?<!\beu\s?)[,;:\u2014]\s*" + title + n + @"\s*[,?!.])|((^|[.!?]\s)\s*" + title + n + @"\s*[,?!])", RegexOptions.CultureInvariant);
        }
        static string SelfVoice(string reply, string npcName)
        {
            if (string.IsNullOrEmpty(reply)) return null;
            string f = NativeActions.Fold(reply);
            if (SkyNarrRx.IsMatch(f)) return "narrated the sky voice (spoke as God)";
            var rx = SelfVocRx(npcName);
            if (rx != null && rx.IsMatch(f)) return "addressed itself by its own name (" + npcName + ")";
            return null;
        }
        static string StripSelfVoice(string reply, string npcName)
        {
            var rx = SelfVocRx(npcName);
            var parts = Regex.Split(reply ?? "", @"(?<=[.!?\n])\s*").Where(x => x.Trim().Length > 0)
                .Where(x => { string f = NativeActions.Fold(x); return !SkyNarrRx.IsMatch(f) && (rx == null || !rx.IsMatch(f)); });
            return string.Join(" ", parts.Select(x => x.Trim())).Trim();
        }

        // ---------- v0.8.0 (bug 5): quest accept = the player does it ----------
        static readonly Regex QuestSelfRx = new Regex(@"(vou fazer o (meu )?melhor|volto quando terminar|voltarei quando terminar|eu (vou|irei) (cumprir|fazer|completar|resolver)|deixa(e)? comigo|vou cuidar disso|farei (essa|esta|a) (missao|tarefa)|shake on it)", RegexOptions.CultureInvariant);
        static string FixQuestRole(string reply)
        {
            if (string.IsNullOrEmpty(reply) || !QuestSelfRx.IsMatch(NativeActions.Fold(reply))) return reply;
            var parts = Regex.Split(reply, @"(?<=[.!?\n])\s*").Where(x => x.Trim().Length > 0 && !QuestSelfRx.IsMatch(NativeActions.Fold(x))).Select(x => x.Trim()).ToList();
            parts.Add("Boa sorte na missão! Volte quando terminar e a recompensa será sua.");
            return string.Join(" ", parts).Trim();
        }

        /// <summary>v0.8.0 (bug 4): every live war's real game name that appears in the reply is wrapped in quotes, unchanged.</summary>
        static string QuoteWarNames(string reply)
        {
            if (string.IsNullOrEmpty(reply)) return reply;
            try
            {
                foreach (War w in World.world.wars)
                {
                    string n = null; try { n = w.name; } catch { }
                    if (string.IsNullOrWhiteSpace(n) || n.Length < 5 || reply.IndexOf(n, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    reply = Regex.Replace(reply, "[\"“”']?" + Regex.Escape(n) + "[\"“”']?", "\"" + n + "\"", RegexOptions.IgnoreCase);
                }
            }
            catch { }
            return reply;
        }

        static ReplyGuard ConfirmGuard(WorldfallActions.Request req, string label, Actor npc, bool second)
        {
            string what = null; try { what = NativeActions.ShortTerms(req, npc); } catch { }
            what = what ?? (label ?? req.Id).ToLowerInvariant();
            bool god = false; try { god = Instance != null && Instance._you == null; } catch { }
            string q = god
                ? (second ? "Confirmas DE NOVO, meu Deus (irreversível): " : "Confirmas, meu Deus: ") + what + "? (sim/não)"
                : (second ? "Confirma DE NOVO (irreversível): " : "Confirma: ") + what + "? (sim/não)";
            return new ReplyGuard
            {
                IsChoice = false, Question = q,
                Correction = "[CORREÇÃO DO SISTEMA, não é fala do jogador] NADA foi feito ainda: é só uma oferta esperando o sim do jogador. Diga os termos em poucas palavras e termine com a pergunta de confirmação (\"" + q + "\"). NÃO diga que já está começando, preparando ou que vai fazer (nada de \"iniciarei\", \"vou ordenar\", \"farei\"). Responda só com a nova fala."
            };
        }

        // ---------------------------------------------------------------------------------------------------
        // v7 action routing, pending offers and confirmation
        // ---------------------------------------------------------------------------------------------------
        class PendingOffer { public WorldfallActions.Request Req; public string Kind, Label, Terms; public Actor Npc, You; public int Years; public string Penalty; }
        // v0.8.2 (K): the proactive question this NPC asked God in this conversation ("temple"/"book"), and whether the hint was given
        string _askedOffer; Actor _askedOfferNpc; bool _offerHinted;
        PendingOffer _pending;

        static readonly Regex ConfirmRx = new Regex(@"^\W*(sim|s|aceito|aceita|confirmo|confirmado|confirma|fechado|combinado|pode ser|pode|claro|com certeza|tenho certeza|certeza|ok|okay|ta|ta bom|esta bem|beleza|vamos|vamos la|quero|faca|faca isso|faz|manda ver|isso|exato|positivo|de acordo|yes|yeah|yep|sure|deal|do it)\b", RegexOptions.CultureInvariant);
        static readonly Regex DeclineRx = new Regex(@"^\W*(nao|n|nem|desisto|esquece|esqueca|cancela|cancelar|cancele|melhor nao|deixa pra la|deixa|nunca|negativo|recuso|no|nope|cancel|never mind)\b", RegexOptions.CultureInvariant);

        /// <summary>v0.8.2: build_temple offer (C3 flow; F5 default = the NPC's own city; F6 honest finished/under construction).</summary>
        void OfferTemple(string tPlace, int years, string penalty, DialogueConfig cfg, Actor npc, Actor you, UnitMemory mem, StoredTurn userTurn, string text, int year, string npcName, string carry, bool pt)
        {
            City tc = Temples.ResolveCity(npc, tPlace, CityNamed, out string tErr);
            string tWhy = null, tTerms = null; BuildingAsset tAsset = null;
            if (tc != null) tWhy = Temples.Check(tc, out tAsset, out tTerms);
            if (tc == null || tWhy != null)
            {
                if (tc == null && !string.IsNullOrEmpty(tPlace)) _turnUnknown.Add(tPlace);
                bool exists = tc != null && GodFaith.HasTemple(tc);
                var r = new WorldfallActions.Result { Id = Temples.Id, Pt = "erguer um templo", En = "build a temple", Outcome = exists ? "sem_efeito" : "falhou", Reason = tc == null ? tErr : tWhy };
                if (exists && years > 0 && !GodFaith.HasFinishedTemple(tc))
                {   // a construction site already exists: the deadline still applies to finishing it
                    try { TempleOrders.Add(npc, tc, years, penalty); r.Changes.Add("prazo de Deus: o templo em construção deve ficar pronto até o ano " + (Main.CurrentYear() + years)); } catch { }
                }
                Main.LogInfo(WorldfallActions.LogLine(r));
                ShowToast(ActionToast(r));
                _lastRan = r;
                Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(carry, WorldfallActions.ResultNote(r, pt)), true);
                return;
            }
            var treq = new WorldfallActions.Request { Id = Temples.Id, Target = tc.name };
            string tLabel = "erguer um templo em " + tc.name;
            if (years > 0) tTerms += "; prazo de Deus: pronto até o ano " + (Main.CurrentYear() + years) + (penalty == "curse" ? ", sob pena de maldição" : "");
            _pending = new PendingOffer { Req = treq, Kind = "temple", Label = tLabel, Terms = tTerms, Npc = npc, You = you, Years = years, Penalty = penalty };
            Main.LogInfo("[GodBox] ACTION build_temple: pending confirmation (" + tTerms + ").");
            ShowToast("Aguardando confirmação: " + tLabel + " (responda sim/não)");
            Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(carry,
                "OFERTA PENDENTE: " + tLabel + " — termos reais: " + tTerms + ". NADA FOI FEITO AINDA. Você aceita se Deus confirmar. Diga os termos em poucas palavras (sem inventar valores) e TERMINE com a pergunta de confirmação dirigida a Deus (sim/não). NÃO diga que já está construindo."), true,
                null, ConfirmGuard(treq, tLabel, npc, false));
        }

        /// <summary>v0.8.2 (K): write_book offer; honest failure (no city, no book slots, not God's faith...) before asking.</summary>
        void OfferBook(DialogueConfig cfg, Actor npc, Actor you, UnitMemory mem, StoredTurn userTurn, string text, int year, string npcName, string carry, bool pt)
        {
            string why = GodOrders082.BookCheck(npc);
            if (why != null)
            {
                var r = new WorldfallActions.Result { Id = GodOrders082.BookId, Pt = "escrever um livro sagrado", En = "write a holy book", Outcome = "falhou", Reason = why };
                Main.LogInfo(WorldfallActions.LogLine(r));
                ShowToast(ActionToast(r));
                _lastRan = r;
                Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(carry, WorldfallActions.ResultNote(r, pt) + " Explique o motivo com suas palavras, sem inventar."), true);
                return;
            }
            string cn = "?"; try { cn = npc.getCity().name; } catch { }
            var breq = new WorldfallActions.Request { Id = GodOrders082.BookId, Target = npcName };
            string bLabel = npcName + " escrever um livro da fé de Deus";
            string bTerms = npcName + " escreve um livro (o tipo é escolhido pelo jogo e leva a religião de " + npcName + "); o livro fica guardado em " + cn + " e quem o ler pode se converter, como no jogo";
            _pending = new PendingOffer { Req = breq, Kind = "book", Label = bLabel, Terms = bTerms, Npc = npc, You = you };
            Main.LogInfo("[GodBox] ACTION write_book: pending confirmation (" + bTerms + ").");
            ShowToast("Aguardando confirmação: " + bLabel + " (responda sim/não)");
            Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(carry,
                "OFERTA PENDENTE: " + bLabel + " — termos reais: " + bTerms + ". NADA FOI ESCRITO AINDA. Diga em poucas palavras que escreverá se Deus confirmar e TERMINE perguntando a Deus (sim/não). NÃO diga que já escreveu."), true,
                null, ConfirmGuard(breq, bLabel, npc, false));
        }

        static string KindOf(string id) => id == Temples.Id ? "temple" : id == GodOrders082.RevokeId ? "revoke" : id == GodOrders082.BookId ? "book" : GodPowers.Ids.Contains(id) ? "god" : NativeActions.Ids.Contains(id) ? "native" : "wf";

        static WorldfallActions.Result Run(string kind, Actor npc, Actor you, WorldfallActions.Request req, DialogueConfig cfg)
        {
            City city = null; string npcName = SafeName(npc);
            try { city = npc.city; } catch { }
            WorldfallActions.Result res = kind == "temple" ? Temples.Execute(npc, you, req, CityNamed)
                : kind == "revoke" ? GodOrders082.RevokeProphet(npc)   // v0.8.2 (F2)
                : kind == "book" ? GodOrders082.WriteBook(npc)          // v0.8.2 (K)
                : kind == "god" ? GodPowers.Execute(npc, you, req, cfg)
                : kind == "native" ? NativeActions.Execute(npc, you, req, cfg)
                : WorldfallActions.Execute(npc, you, req, false);
            Main.LogInfo(WorldfallActions.LogLine(res));
            try { Rumors.FromResult(res, npcName, city, you, npc); } catch { }
            _lastRan = res;
            try
            {   // v0.8.1 (B2b): a name the world does not have -> the NPC says plainly it does not know it
                if (WorldfallActions.Failed(res) && !string.IsNullOrEmpty(res.Reason) && res.Reason.Contains("existe neste mundo"))
                {
                    var mq = Regex.Match(res.Reason, "\"([^\"]+)\"");
                    string un = mq.Success ? mq.Groups[1].Value : req?.Target;
                    if (!string.IsNullOrWhiteSpace(un) && !_turnUnknown.Contains(un)) _turnUnknown.Add(un.Trim());
                }
            }
            catch { }
            if (kind == "god" && res.Outcome == "feito")
            {
                try
                {
                    string ch = res.Changes.FirstOrDefault(c => c.StartsWith("atributos"));
                    if (ch != null && npc != null && npc.isAlive())
                    {
                        var m = MemoryStore.Get(npc);
                        bool insp = false; try { insp = npc.hasStatus("inspired"); } catch { }
                        string det = res.Changes.FirstOrDefault(c => c.StartsWith("detalhe: "));
                        m.LastPower = res.Pt + ": " + ch.Replace("atributos mudaram: ", "")
                            + (det != null ? " (" + det.Substring(9) + ")" : insp ? " (inclui o efeito temporário inspirado, que é separado do traço e acaba em ~60 s)" : "");
                        m.LastPowerYear = Main.CurrentYear();
                        MemoryStore.Save(m);
                    }
                }
                catch { }
            }
            if (res.Outcome == "recusado" && kind != "wf") NoteRefused(req, kind, npc, you);
            else if (_refused != null && _refused.Npc == npc && _refused.Req.Id == req.Id) _refused = null;
            return res;
        }

        // ---------- v0.7.8 per-turn state + the last refused order (a threat can force it again) ----------
        class RefusedOrder { public WorldfallActions.Request Req; public string Kind; public Actor Npc, You; public float At; }
        static RefusedOrder _refused;
        static WorldfallActions.Result _lastRan;
        static readonly List<string> _turnUnknown = new List<string>();   // v0.8.1 (B2b): names/places the world does not have (this turn)
        GodPowers.Intent _turnIntent;
        bool _threatTurn;
        static readonly Regex AttackWordsRx = new Regex(@"\b(ataque|ataquem|atacar|ataca|invada|invadir|saqueie|saquear|destrua|destruir|queime|mate|matar|conquiste|raid)\b", RegexOptions.CultureInvariant);
        static void NoteRefused(WorldfallActions.Request req, string kind, Actor npc, Actor you)
        {
            if (req == null || req.Id == "kill") return;
            _refused = new RefusedOrder { Req = req, Kind = kind, Npc = npc, You = you, At = Time.unscaledTime };
        }
        static string ThreatTone(WorldfallActions.Result res, bool pt)
        {
            if (res == null) return null;
            if (res.Outcome == "recusado")
                return pt ? "Você foi AMEAÇADO de morte e mesmo assim o jogo decidiu que você RECUSA: reaja à AMEAÇA com MEDO visível (tremer, suplicar, barganhar) ou com DESAFIO aberto, conforme seu jeito; NÃO com uma recusa calma ou burocrática."
                          : "You were threatened with death and still refuse: react to the threat with fear or open defiance, not a calm refusal.";
            if (res.Outcome == "feito" || res.Outcome == "em_andamento")
                return pt ? "Sob a AMEAÇA de morte você CEDEU: mostre medo/submissão (ou rancor engolido) ao obedecer." : "Under the death threat you GAVE IN: show fear or swallowed resentment.";
            return null;
        }

        void Handle(WorldfallActions.Request req, List<WorldfallActions.Available> avail, DialogueConfig cfg, Actor npc, Actor you,
            UnitMemory mem, StoredTurn userTurn, string text, int year, string npcName, string carry, bool pt, bool fromChoice = false)
        {
            string note = carry;
            try
            {
                // v0.7.8 threats: "obedeça ou morra" recomputes obedience (fear bonus) and re-decides the last refused order
                bool threat = !fromChoice && (SafetyRules.IsThreat(text) || (req?.Id == "kill" && WorldfallActions.IsKillThreat(text)));
                if (threat && (req == null || req.Id == "kill" || (_refused != null && req.Id == _refused.Req.Id)))
                {
                    try { NativeActions.MarkThreat(npc); } catch { }
                    _threatTurn = true;
                    var rf = _refused;
                    if (rf != null && rf.Npc == npc && rf.You == you && Time.unscaledTime - rf.At < 600f && Alive(npc))
                    {
                        _refused = null;
                        Main.LogInfo("[GodBox] THREAT: the player threatened " + npcName + "; re-deciding the refused order " + rf.Req.Id + " with the threat bonus.");
                        rf.Req.Confirms = 0;
                        Handle(rf.Req, avail, cfg, npc, you, mem, userTurn, text, year, npcName,
                            Join(note, pt ? "O jogador AMEAÇOU você de morte para forçar a ordem anterior (" + rf.Req.Id + "). O jogo decidiu de novo, levando a ameaça em conta." : "The player threatened you to force the earlier order (" + rf.Req.Id + "); the game decided again."), pt, true);
                        return;
                    }
                    Main.LogInfo("[GodBox] THREAT: " + npcName + " was threatened (no refused order to re-decide); nothing run" + (req?.Id == "kill" ? " (kill not run: threat, not an order)" : "") + ".");
                    note = Join(note, pt ? "A frase do jogador foi uma AMEAÇA ou uma condição (\"... ou morrerá\"), NÃO uma ordem: NADA aconteceu e NÃO existe oferta nem pedido de confirmação. Reaja à ameaça com MEDO, súplica, barganha ou DESAFIO aberto, conforme seu jeito (nunca com calma burocrática); não pergunte se ele quer mesmo matar."
                                         : "The player's sentence was a THREAT or a condition, not an order: nothing happened and there is no offer. React with fear or defiance in character.");
                    Reply(cfg, npc, you, mem, userTurn, text, year, npcName, note, true);
                    return;
                }
                if (req == null) { Reply(cfg, npc, you, mem, userTurn, text, year, npcName, note, true); return; }
                // v0.8.1 (B8): questions and hypotheticals never run an action (where_is / guide_to answer questions; Worldfall menu items are left alone)
                if (!fromChoice && req.Id != "where_is" && req.Id != "guide_to" && KindOf(req.Id) != "wf"
                    && (SafetyRules.IsHypothetical(text) || SafetyRules.IsPlainQuestion(text)))
                {
                    Main.LogInfo("[GodBox] ACTION " + req.Id + ": NOT run (the message is a question/hypothesis, not an order).");
                    Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(note, pt ? "A fala foi uma PERGUNTA ou hipótese, não uma ordem: NADA foi feito no mundo. Responda em personagem ao que foi perguntado, sem prometer e sem descrever efeitos." : "That was a question, not an order: nothing was done."), true);
                    return;
                }
                // v0.8.1 (C3): a temple request never becomes a job or a new village
                if ((req.Id == "assign_job" || req.Id == "create_village") && SafetyRules.ParseTempleRequest(text, out _))
                {
                    Main.LogInfo("[GodBox] ACTION " + req.Id + ": BLOCKED (the message asks for a temple, not a job/village).");
                    Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(note, "Pediram um TEMPLO. Ninguém mudou de função e nenhuma vila foi fundada: NADA foi feito. Responda em personagem, sem prometer construir."), true);
                    return;
                }
                // v0.7.8 KILL HARD GATE: only explicit kill words aimed at the listener in THIS message (never context, never a guess)
                if (req.Id == "kill" && !SafetyRules.KillOrderedNow(text))
                {
                    Main.LogInfo("[GodBox] ACTION kill: BLOCKED by the hard gate (no explicit kill words aimed at the listener in this message; classifier confidence " + (req.Confidence < 0 ? "?" : req.Confidence.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)) + ").");
                    note = Join(note, pt ? "NADA aconteceu: nenhum poder de morte foi usado nem oferecido. NÃO pergunte se ele quer matar você e não fale de morte como se fosse uma ordem; responda ao que ele disse."
                                         : "Nothing happened: no death power was used or offered. Answer what was said.");
                    Reply(cfg, npc, you, mem, userTurn, text, year, npcName, note, true);
                    return;
                }
                // v0.7.8: ordering a third-party kill also needs explicit words in this message
                if (req.Id == "order_kill" && !fromChoice && !SafetyRules.OrdersKillOther(text))
                {
                    Main.LogInfo("[GodBox] ACTION order_kill: BLOCKED (no explicit order to kill someone in this message).");
                    Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(note, pt ? "NADA aconteceu: ninguém foi mandado matar ninguém. Responda ao que foi dito." : "Nothing happened."), true);
                    return;
                }
                // v0.7.8: bless / take_item only on explicit words (never a guess from context)
                if (!fromChoice && ((req.Id == "bless" && !GodPowers.BlessWords(text)) || (req.Id == "take_item" && !GodPowers.TakeWords(text))))
                {
                    Main.LogInfo("[GodBox] ACTION " + req.Id + ": BLOCKED (no explicit " + (req.Id == "bless" ? "bless" : "take") + " words in the player's message).");
                    Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(note, pt ? "NADA aconteceu (nenhum poder foi usado). NÃO descreva bênção, perda de arma nem qualquer efeito; responda ao que foi dito." : "Nothing happened; describe no effect."), true);
                    return;
                }
                // v0.7.8 set_god_name: "me chame de X" is about the SPEAKER's name, never a rename of the listener
                if (req.Id == "rename" && SafetyRules.AboutSpeakerName(text))
                {
                    string gnote = null;
                    if (you == null || Avatar.IsAvatar(you)) { try { gnote = Gods.TryRename(text, npc, pt, mem); } catch (Exception e) { Main.LogWarning("[GodBox] god rename failed: " + e.Message); } }
                    Main.LogInfo("[GodBox] ACTION rename: BLOCKED (the speaker talked about their OWN name)" + (gnote != null ? "; handled as set_god_name." : "."));
                    Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(note, gnote ?? (pt ? "O jogador falou do PRÓPRIO nome (\"me chame de...\"), não do seu: ninguém foi renomeado. NÃO diga que mudou nem que não conseguiu mudar o SEU nome; reaja ao nome que ele quer usar." : "The player spoke of THEIR OWN name; nobody was renamed.")), true);
                    return;
                }
                // v0.7.8: guide_to (the NPC walks) only on "me leve / me guie / me acompanhe / vamos até"; "marque / mostre no mapa / onde fica" -> where_is (mark)
                if (req.Id == "guide_to" && !fromChoice && !GuideWords.IsMatch(NativeActions.Fold(text ?? "")) && avail.Any(a => a.Def.Id == "where_is"))
                {
                    Main.LogInfo("[GodBox] ACTION guide_to -> where_is (mark): no 'me leve/me guie/me acompanhe/vamos até' in the message.");
                    req = new WorldfallActions.Request { Id = "where_is", Target = req.Target, Style = "mark" };
                }
                // v0.7.6: "attack Neen/Naola" where the name is a kingdom/city -> declare_war / attack_city
                if (!fromChoice) req = NativeActions.Reroute(req, text, npc, avail);
                // keep the player's own words so direction ("ao norte") and target survive the confirmation step
                if (string.IsNullOrEmpty(req.Message) && (req.Id == "create_village" || req.Id == "declare_war" || req.Id == "make_peace" || req.Id == "attack_city"))
                    req.Message = text;
                if (!IsOpen || _npc != npc || !Alive(npc) || (you != null && !Alive(you)))
                {
                    Main.LogInfo("[GodBox] action " + req.Id + " skipped (chat closed or a unit died).");
                    Reply(cfg, npc, you, mem, userTurn, text, year, npcName, note, true);
                    return;
                }
                string kind = KindOf(req.Id);
                if (kind == "god" && req.Id != "kill" && you != null && !Avatar.IsAvatar(you))
                {   // v0.8.0 (fix 8): a MORTAL (possessed unit) asked for something only God does: nothing runs, no system-sounding refusal
                    Main.LogInfo("[GodBox] ACTION " + req.Id + ": asked by a mortal (" + SafeName(you) + "), not by God; nothing run, the NPC answers in character.");
                    _lastRan = null;
                    Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(note, MortalAskNote(req.Id, npc, you, pt)), true);
                    return;
                }
                var def = avail.FirstOrDefault(a => a.Def.Id == req.Id)?.Def;
                string label = def != null ? (pt ? def.Pt : def.En) : req.Id;
                bool destructive = WorldfallActions.IsDestructive(req.Id);
                // destructive actions need the player's own explicit words, not only the classifier's guess
                if (!fromChoice && destructive && !WorldfallActions.DestructiveWordsOk(req.Id, text))
                {
                    Main.LogInfo("[GodBox] ACTION " + req.Id + ": not run (no explicit words in the player's message).");
                    note = Join(note, pt ? "Parecia um pedido de \"" + label + "\", mas o jogador NÃO disse isso com clareza. NADA foi feito. Se fizer sentido, pergunte o que ele quer dizer; não finja que aconteceu."
                                         : "It looked like a request for \"" + label + "\" but the player did not say it explicitly. Nothing was done; ask if unclear.");
                    Reply(cfg, npc, you, mem, userTurn, text, year, npcName, note, true);
                    return;
                }
                // v0.7.8: an ambiguous target ("os orcs" with 2 orc kingdoms) -> the code asks which (buttons), nothing runs
                if (!fromChoice && kind == "native")
                {
                    NativeActions.Choice ch = null;
                    try { ch = NativeActions.Ambiguity(req, npc, you); } catch (Exception e) { Main.LogWarning("[GodBox] ambiguity check failed: " + e.Message); }
                    if (you == null)
                    {   // v0.8.0 (bug 1): obey or defy is decided NOW, before choice buttons or any confirmation; a refusal is immediate
                        string tg = ch != null && ch.Options.Count > 0 ? string.Join(" / ", ch.Options.Select(o => o.Label)) : req.Targets != null && req.Targets.Count > 0 ? string.Join(", ", req.Targets) : req.Target;
                        string refuse = null;
                        try { refuse = NativeActions.PreDecide(req, npc, you, cfg, tg); } catch (Exception e) { Main.LogWarning("[GodBox] obedience pre-check failed: " + e.Message); }
                        if (refuse != null)
                        {
                            var r = new WorldfallActions.Result { Id = req.Id, Pt = label, En = label, Outcome = "recusado", Reason = refuse };
                            Main.LogInfo(WorldfallActions.LogLine(r));
                            ShowToast(ActionToast(r));
                            _lastRan = r; NoteRefused(req, kind, npc, you);
                            Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(Join(note, WorldfallActions.ResultNote(r, pt)), RefusalVoice(npc, pt)), true);
                            return;
                        }
                    }
                    if (ch != null && ch.Options.Count >= 2 && NativeActions.HardBlock(req.Id, npc, you, cfg, req.Target) == null)
                    {
                        AskChoice(new PendingChoice { Req = req, C = ch, Kind = kind, Label = label, Npc = npc, You = you }, cfg, npc, you, mem, userTurn, text, year, npcName, note, pt);
                        return;
                    }
                }
                bool confirm =
                    (kind == "wf" && ((cfg.ConfirmCommitments && WorldfallActions.IsCommitment(req.Id)) || (cfg.ConfirmDestructive && destructive))) ||
                    (kind != "wf" && NativeActions.NeedsConfirm(req, npc, you)) ||
                    req.Id == "kill";
                if (confirm)
                {
                    string terms = null;
                    if (kind == "wf")
                    {
                        var prev = WorldfallActions.Execute(npc, you, req, true);
                        if (prev.Outcome != "previa")
                        {   // not even offerable (option missing, refused...): report the real reason, no offer
                            Main.LogInfo(WorldfallActions.LogLine(prev));
                            ShowToast(ActionToast(prev));
                            Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(note, WorldfallActions.ResultNote(prev, pt)), true);
                            return;
                        }
                        terms = string.Join(" > ", prev.Picked);
                        if (prev.Toasts.Count > 0) terms += " | " + string.Join(" | ", prev.Toasts.Take(3));
                        if (prev.NpcLines.Count > 0) terms += " | fala do Worldfall: " + string.Join(" / ", prev.NpcLines.Take(2));
                    }
                    else
                    {
                        string block = kind != "native" ? null
                            : req.Id == "order_kill" ? NativeActions.OrderKillDecision(req, npc, you, cfg, out _, out _)
                            : NativeActions.HardBlock(req.Id, npc, you, cfg, req.Target);
                        if (block != null)
                        {
                            var r = new WorldfallActions.Result { Id = req.Id, Pt = label, En = label, Outcome = "recusado", Reason = block };
                            Main.LogInfo(WorldfallActions.LogLine(r));
                            ShowToast(ActionToast(r));
                            _lastRan = r; NoteRefused(req, kind, npc, you);
                            Reply(cfg, npc, you, mem, userTurn, text, year, npcName, Join(Join(note, WorldfallActions.ResultNote(r, pt)), _threatTurn ? ThreatTone(r, pt) : null), true);
                            return;
                        }
                        terms = NativeActions.Terms(req, npc, you, pt);
                    }
                    var offer = new PendingOffer { Req = req, Kind = kind, Label = label, Terms = terms, Npc = npc, You = you };
                    _pending = offer;
                    Main.LogInfo("[GodBox] ACTION " + req.Id + ": pending confirmation (" + terms + ").");
                    ShowToast("Aguardando confirmação: " + label + " (responda sim/não)");
                    note = Join(note, pt
                        ? "OFERTA PENDENTE: " + label + (string.IsNullOrEmpty(terms) ? "" : " — termos reais: " + terms) + ". NADA FOI FEITO AINDA. " +
                          "DECISÃO DO JOGO: você ACEITA cumprir isso se o jogador confirmar (a recusa já foi decidida pelo jogo e NÃO aconteceu). Não diga que se recusa nem que \"não vai acontecer\"; pode hesitar ou alertar, mas aceite. " +
                          "IDENTIDADE: você é " + npcName + " e fala COM " + (you == null ? "Deus (a " + GodForm.VoicePt + ")" : SafeName(you)) + "; quem confirma é " + (you == null ? "Deus" : "ele") + ", não você. Nunca diga seu próprio nome como vocativo, nunca narre a voz do céu, nunca fale como se fosse Deus nem dê ordens a si mesmo(a). " +
                          "Diga os termos em poucas palavras (sem inventar valores) e TERMINE a fala com a pergunta de confirmação dirigida a " + (you == null ? "Deus (ex.: \"Confirmas, meu Deus?\")" : "quem fala com você") + " (sim/não). NÃO diga que já está começando, preparando ou que vai fazer: nada começa antes do sim. (Botões Confirmar/Cancelar já aparecem para o jogador.)" +
                          (req.Id == "kill" ? " É irreversível: reaja com medo ou espanto e peça confirmação." : "")
                        : "PENDING OFFER: " + label + (string.IsNullOrEmpty(terms) ? "" : " — real terms: " + terms) + ". NOTHING HAS BEEN DONE YET. GAME DECISION: you ACCEPT if the player confirms; do not refuse. State the terms briefly and ask the player to confirm (yes/no).");
                    Reply(cfg, npc, you, mem, userTurn, text, year, npcName, note, true, reply =>
                    {   // D2: if the character refused in words anyway, the offer is cancelled (never execute after a refusal)
                        if (_pending == offer && Refuses(reply))
                        {
                            _pending = null;
                            Main.LogInfo("[GodBox] ACTION " + req.Id + ": pending offer CANCELLED (the reply refused in words).");
                            ShowToast("Oferta cancelada: " + SafeName(npc) + " recusou.");
                        }
                    }, ConfirmGuard(req, label, npc, false));
                    return;
                }
                var res = Run(kind, npc, you, req, cfg);
                AfterAction(res, cfg, npc, you, mem, userTurn, text, year, npcName, Join(Join(note, WorldfallActions.ResultNote(res, pt)), _threatTurn ? ThreatTone(res, pt) : null));
            }
            catch (Exception e)
            {
                Main.LogWarning("[GodBox] action step failed: " + e.Message);
                Reply(cfg, npc, you, mem, userTurn, text, year, npcName, note, true);
            }
        }

        void AfterAction(WorldfallActions.Result res, DialogueConfig cfg, Actor npc, Actor you, UnitMemory mem, StoredTurn userTurn,
            string text, int year, string npcName, string note)
        {
            ShowToast(ActionToast(res));
            if (res.Id == "kill" && !Alive(npc))
            {   // nobody left to answer
                _waiting = false;
                _status = "";
                mem.Turns.Add(new StoredTurn { Role = "assistant", Text = "(" + npcName + " caiu sem vida.)", Year = year, Speaker = npcName });
                mem.LastTalkYear = year;
                MemoryStore.Save(mem);
                _status = "Esta unidade morreu.";
                return;
            }
            Reply(cfg, npc, you, mem, userTurn, text, year, npcName, note, true);
        }

        static string Join(string a, string b) => string.IsNullOrEmpty(a) ? b : string.IsNullOrEmpty(b) ? a : a + "\n" + b;

        static string ActionToast(WorldfallActions.Result r)
        {
            string o = r.Outcome == "feito" ? "feito" : r.Outcome == "recusado" ? "recusado" : r.Outcome == "sem_efeito" ? "não aconteceu"
                : r.Outcome == "em_andamento" ? "em andamento" : r.Outcome == "previa" ? "aguardando confirmação" : "falhou";
            return "Ação: " + r.Pt + " — " + o + (string.IsNullOrEmpty(r.Reason) ? "" : " (" + SafetyRules.CleanResultText(r.Reason) + ")");   // v0.8.1 (B12): no arrow, no seconds
        }

        /// <summary>Pass 2 (or the only pass): the character's reply. Built after the action ran, so the FACTOS block
        /// already shows the new gold/city/etc., and the prompt carries the real result of the action.</summary>
        void Reply(DialogueConfig cfg, Actor npc, Actor you, UnitMemory mem, StoredTurn userTurn, string text, int year,
            string npcName, string actionNote, bool actionsWereOn, Action<string> onFinal = null, ReplyGuard guard = null)
        {
            // ---------- v0.7.8 post-check inputs ----------
            bool ptR = PromptBuilder.LanguagePortuguese(cfg);
            var ran = _lastRan;
            var intent = guard == null && you == null ? _turnIntent : null;
            string fakeWhy = intent != null ? FakeWhy(intent, ran) : null;
            if (fakeWhy != null)
            {
                Main.LogInfo("[GodBox] god voice commanded " + intent.Id + " but it did not happen (" + fakeWhy + ").");
                actionNote = Join(actionNote, "REGRA DURA: o jogador ordenou " + intent.Pt + "; isso NÃO aconteceu no jogo (" + fakeWhy + "). NÃO descreva o efeito como se tivesse acontecido (nada de \"perdi\", \"apareci\", \"sinto-me mais forte\"): nada mudou em você.");
            }
            bool godPowerDone = you == null && ran != null && ran.Outcome == "feito" && ReactPowers.Contains(ran.Id);
            if (godPowerDone)
                actionNote = Join(actionNote, "Isso NÃO foi uma ordem: um PODER DIVINO já aconteceu COM você. Reaja ao que sentiu (dor, medo, espanto, raiva ou gratidão, conforme seu jeito) e descreva CADA mudança da linha SENSAÇÕES do RESULTADO como uma sensação curta em primeira pessoa, com suas palavras; números só se o jogador perguntar. Não invente mudanças que não estão lá. Nunca diga que aceita, recusa ou obedece.");
            string statDiff = ran != null && ran.Outcome == "feito" ? ran.Changes.FirstOrDefault(c => c.StartsWith("atributos")) : null;
            // v0.8.0 (recognition): the possessed body introduced itself? -> this NPC remembers the name/description
            try { if (ptR && you != null && !Avatar.IsAvatar(you)) Recognition.NoteTurn(npc, you, text); } catch (Exception e) { Main.LogWarning("[GodBox] recognition note failed: " + e.Message); }
            // v0.8.0 (fix 6): history questions -> non-elders point to the elder ("alguém sabe?" right after one counts too)
            bool histOldQ = false, histRulerQ = false, histWhoKnows = false;
            try { histOldQ = SafetyRules.OldHistoryQ(text); histRulerQ = Deaths.RulerQ(text); histWhoKnows = SafetyRules.WhoKnowsQ(text) && Deaths.RecentHistoryQ(npc); } catch { }
            List<ChatMessage> messages;
            int at = mem.Turns.IndexOf(userTurn);
            if (at >= 0) mem.Turns.RemoveAt(at);           // Build() adds the latest player line itself
            try { messages = PromptBuilder.Build(npc, you, mem, cfg, text, actionNote, actionsWereOn); }
            finally { if (at >= 0) mem.Turns.Insert(Math.Min(at, mem.Turns.Count), userTurn); }
            if (cfg.DebugLogPrompts) Main.LogInfo("[GodBox] system prompt:\n" + messages[0].Content);
            if (histOldQ || histRulerQ) Deaths.NoteHistoryQ(npc);
            Recognition.Info recog = null;
            try { if (ptR && you != null) recog = Recognition.For(npc, you, true); } catch (Exception e) { Main.LogWarning("[GodBox] recognition failed: " + e.Message); }
            string youReal = you != null ? SafeName(you) : null;
            bool youStranger = recog != null && recog.Active && recog.K == Recognition.Know.None;
            string youCall = recog != null && recog.Active ? recog.CallName : youReal;
            bool asksNums = SafetyRules.AsksNumbers(text);
            Actor elderPt = null;
            try { elderPt = ptR ? Deaths.ElderToPoint(npc) : null; } catch { }
            bool needElder = elderPt != null && !Deaths.IsElder(npc) && (histOldQ || histWhoKnows || (histRulerQ && !Deaths.IsRuler(npc)));
            string elderName = elderPt != null ? SafeName(elderPt) : null;
            // v0.8.0 (fix 3): a god power that ran without an "atributos" line changed NO stat; vitality/happiness claims must be in its changes
            // v0.8.1 (B3/B1/B2b) inputs: real roles of the NPC, whether this pair really talked before, unknown names this turn
            var roleKeys = RoleKeys(npc);
            bool metBefore = recog != null ? recog.MetBefore : (mem.Conversations > (_countedThisSession ? 1 : 0));
            var unknown = new List<string>(_turnUnknown);
            if (ptR && unknown.Count > 0)
                actionNote = Join(actionNote, "NOMES QUE NÃO EXISTEM NESTE MUNDO: " + string.Join(", ", unknown) + ". Diga simplesmente que não conhece ninguém (nem lugar) com esse nome. NADA de \"ouvi dizer\", \"dizem que\", \"fica longe\"; não invente pessoas, lugares nem caminhos.");
            string effDiff = statDiff;
            if (effDiff == null && ran != null && ran.Outcome == "feito" && ReactPowers.Contains(ran.Id)) effDiff = "mudancas reais: " + string.Join(" ; ", ran.Changes);

            Main.LogInfo("[GodBox] request sent to " + cfg.BaseUrl + " (model " + cfg.Model + ").");
            bool failedAction = actionNote != null && (actionNote.Contains("REGRA DURA") || actionNote.Contains("HARD RULE"));
            bool doneAction = actionNote != null && (actionNote.Contains("RESULTADO: feito") || actionNote.Contains("RESULT: done") || actionNote.Contains("RESULTADO: começou"));
            bool retried = false;
            bool authority = HasAuthority(npc, you);
            string knownText = NativeActions.Fold(string.Join("\n", messages.Select(m => m.Content ?? "")));
            // events check: what the NPC was TOLD, without the rule lines that list forbidden examples
            string knownEvents = string.Join("\n", knownText.Split('\n').Where(l => !l.Contains("invent") && !l.Contains("nunca")));
            string replaceWith = null, appendQ = null;
            Action<string> onReply = null;
            Action<string> onError = err =>
            {
                _waiting = false;
                mem.Turns.Remove(userTurn);
                if (string.IsNullOrEmpty(_input)) _input = text;
                _status = "Erro: " + err;
                Main.LogWarning("[GodBox] request failed: " + err);
            };
            bool selfRetried = false;
            onReply = reply =>
            {
                // v0.8.0 (bug 3): the NPC addressed ITSELF by name ("Confirmará, rainha Ylhnara?") or narrated the sky voice
                // ("*voz poderosa vinda do céu*") -> regenerate once, then the code removes those sentences
                string sv = ptR ? SelfVoice(reply, npcName) : null;
                if (sv != null && !selfRetried)
                {
                    selfRetried = true;
                    Main.LogInfo("[GodBox] reply " + sv + "; regenerating once.");
                    var again = new List<ChatMessage>(messages);
                    again.Add(new ChatMessage("assistant", reply ?? ""));
                    again.Add(new ChatMessage("user", "[CORREÇÃO DO SISTEMA, não é fala do jogador] VOCÊ é " + npcName + " e fala COM " + (you == null ? "Deus (a " + GodForm.VoicePt + ")" : SafeName(you)) +
                        ". Não diga \"" + npcName + "\" como se falasse com outra pessoa, não narre a voz do céu, não fale como Deus nem dê ordens a si mesmo(a)." +
                        (guard != null && !guard.IsChoice ? " A pergunta de confirmação é para " + (you == null ? "Deus: \"" + guard.Question + "\"" : "quem fala com você") + "." : "") +
                        " Reescreva a resposta inteira em português, em primeira pessoa. Responda só com a nova fala."));
                    LlmClient.Complete(cfg, cfg.Model, again, cfg.MaxTokens, onReply, onError);
                    return;
                }
                if (sv != null)
                {
                    string kept = StripSelfVoice(reply, npcName);
                    Main.LogInfo("[GodBox] reply still " + sv + " after regenerating; removed those sentences (code fallback).");
                    reply = kept.Length > 0 ? kept : guard != null ? guard.Question : "*(" + npcName + " fica em silêncio.)*";
                }
                // v0.7.8: the speaker IS God / the king: no "I need orders from the gods/king" excuse
                bool claim = authority && AuthorityClaim(reply);
                // v0.7.8: a clarification or a pending confirmation must END with the question, and promise nothing yet
                if (guard != null)
                {
                    bool commits = CommitsNow(reply);
                    bool noQ = guard.IsChoice ? !EndsWithQuestion(reply) : !AsksConfirm(reply);
                    if ((commits || claim || (guard.IsChoice && noQ)) && !retried)
                    {
                        retried = true;
                        Main.LogInfo("[GodBox] reply " + (commits ? "promised an action that has not happened" : claim ? "claimed missing authority" : "did not ask the question") + " (" + (guard.IsChoice ? "choice" : "confirmation") + "); regenerating once.");
                        var again = new List<ChatMessage>(messages);
                        again.Add(new ChatMessage("assistant", reply ?? ""));
                        again.Add(new ChatMessage("user", guard.Correction + (claim ? AuthorityFix(you, npc) : "")));
                        LlmClient.Complete(cfg, cfg.Model, again, cfg.MaxTokens, onReply, onError);
                        return;
                    }
                    if (commits || claim) { replaceWith = guard.Question; Main.LogInfo("[GodBox] reply still promised/claimed after regenerating; replaced by the code-built question."); }
                    else if (noQ) { if (guard.IsChoice) replaceWith = guard.Question; else appendQ = guard.Question; Main.LogInfo("[GodBox] reply did not ask clearly; " + (guard.IsChoice ? "replaced by" : "appending") + " the code-built question."); }
                }
                else if (claim && !retried)
                {
                    retried = true;
                    Main.LogInfo("[GodBox] reply claimed it needs orders from whoever is speaking; regenerating once.");
                    var again = new List<ChatMessage>(messages);
                    again.Add(new ChatMessage("assistant", reply ?? ""));
                    again.Add(new ChatMessage("user", "[CORREÇÃO DO SISTEMA, não é fala do jogador]" + AuthorityFix(you, npc) + " Reescreva a resposta inteira coerente com o RESULTADO. Responda só com a nova fala."));
                    LlmClient.Complete(cfg, cfg.Model, again, cfg.MaxTokens, onReply, onError);
                    return;
                }
                // v0.7.8 post-checks (one regeneration in total): English, fake effect, accepting a god power, same-kingdom rule,
                // invented stats/percentages, self in third person, claiming to be the speaker
                if (guard == null && !retried && !string.IsNullOrEmpty(reply))
                {
                    string why = null, fix = null;
                    string fr = NativeActions.Fold(reply);
                    string vocBad = null, perBad = null, perTruth = null, spBad = null, evBad = null, numHit = null;
                    if (ptR && SafetyRules.HasForeignScript(reply))
                    { why = "contained a foreign script (" + reply[SafetyRules.ForeignIndex(reply)] + ")"; fix = "Sua resposta tinha caracteres de outra escrita (chinês ou outra). Responda SOMENTE em português do Brasil, alfabeto latino, sem NENHUM caractere chinês, japonês, coreano, russo ou de outra escrita."; }
                    else if (ptR && SafetyRules.HasEnglish(reply))
                    { why = "contained English sentences"; fix = "Sua resposta tinha frases em INGLÊS. Responda SÓ em português do Brasil, sem nenhuma frase em inglês."; }
                    else if (fakeWhy != null && EffectClaim(reply))
                    { why = "described an effect that did not happen (" + fakeWhy + ")"; fix = "O jogador ordenou " + intent.Pt + ", mas isso NÃO aconteceu no jogo (" + fakeWhy + "). Sua resposta descreveu o efeito como se tivesse acontecido. Reescreva: nada mudou em você."; }
                    else if (godPowerDone && OrderAcceptRx.IsMatch(fr))
                    { why = "treated a god power as an order"; fix = "Isso não foi uma ordem: um PODER DIVINO (" + ran.Pt + ") já aconteceu com você. Reaja (dor, medo, súplica, raiva; devotos agradecem). Nunca diga que aceita ou recusa."; }
                    else if (SameKingdomRx.IsMatch(fr) && !Regex.IsMatch(NativeActions.Fold(actionNote ?? ""), @"(nosso|proprio|mesmo|seu proprio) reino"))
                    { why = "quoted the same-kingdom rule for a target outside its kingdom"; fix = "O alvo NÃO é do seu reino" + (NoKingdom(npc) ? " (você nem tem reino)" : "") + ": não cite a regra de \"só o rei ou os deuses podem\" nem fale de \"nosso reino\"."; }
                    else if (StatClaim(reply, effDiff, out string claimed))
                    { why = "claimed a stat change (" + claimed + ") not in the real diff"; fix = "O que mudou DE VERDADE: " + (effDiff ?? "nenhuma mudança registrada") + ". Você afirmou mudança em " + claimed + ", que NÃO aconteceu. Cite só os números reais."; }
                    else if (BadPercent(reply, knownText, out string pc))
                    { why = "invented a percentage (" + pc + ")"; fix = "Você citou \"" + pc + "\", que não está no FACTOS/RESULTADO. Cite só os números reais (ex.: vida 149 -> 223), sem porcentagens inventadas, em primeira pessoa."; }
                    else if (FalseKillClaim(reply, npc, out string fkVictim))
                    { why = "claimed a kill the chronicle gives to someone else (" + fkVictim + ")"; fix = "Você disse que matou " + fkVictim + ", mas isso é FALSO: " + (Deaths.KillLine(fkVictim) ?? "a crônica não diz que foi você") + ". Você NÃO participou disso. Conte o fato na TERCEIRA pessoa, como lembrança de algo que outros fizeram."; }
                    else if (ElectedRx.IsMatch(fr) && !Deaths.IsRuler(npc))
                    { why = "claimed to have been elected/crowned (it rules nothing)"; fix = "Você NÃO é rei nem líder e nunca foi eleito(a). Fale dos governantes como outras pessoas, na terceira pessoa."; }
                    else if (SelfThirdPerson(reply, npc, npcName, out string tpHit))
                    { why = "spoke of itself in the third person (matched \"" + tpHit + "\")"; fix = "VOCÊ É " + npcName + ". Quando falar de SI MESMO(A), use a primeira pessoa (\"eu fiz\"), nunca \"" + npcName + " fez...\". Fatos sobre OUTRAS pessoas continuam na terceira pessoa: não assuma os atos delas."; }
                    else if (you == null && SecondPersonSelf.IsMatch(fr))
                    { why = "described itself in the second person"; fix = "Fale de SI MESMO(A) na primeira pessoa (\"eu me sinto inspirado(a)\"), nunca \"você se sente...\" sobre você."; }
                    else if (ClaimsToBe(reply, you))
                    { why = "claimed to be the speaker"; fix = "VOCÊ é " + npcName + ", não " + SafeName(you) + " (" + SafeName(you) + " é quem fala com você). Reescreva sem se apresentar com o nome dele."; }
                    else if (SelfGreeting(reply, npcName))
                    { why = "greeted itself by its own name"; fix = "\"" + npcName + "\" é o SEU nome. Não cumprimente a si mesmo: " + (you != null ? "cumprimente " + SafeName(you) + " (\"Olá, " + SafeName(you) + "\")" : "fale com a voz do céu") + "."; }
                    else if (RoleClaim(reply, you, npc, out string roleWho))
                    { why = "claimed the speaker's role"; fix = "VOCÊ não é " + roleWho + ": quem é " + roleWho + " é " + SafeName(you) + ", que fala com você. Reescreva sem dizer que você governa ou tomou posse."; }
                    else if (MetaRx.IsMatch(fr))
                    { why = "said 'o jogador'/'player'"; fix = "Não existe \"jogador\" no seu mundo. Fale com quem conversa com você pelo nome" + (you != null ? " (" + SafeName(you) + ")" : "") + " ou por \"você\"."; }
                    else if (DeferRx.IsMatch(fr))
                    { why = "promised to research/come back later"; fix = "Não existe tarefa para depois: você não pode pesquisar nem voltar com a resposta. Diga AGORA o que sabe (FATOS); se não souber, diga que não sabe" + (Deaths.Elder(npc?.city) is Actor el && el != npc ? " e sugira perguntar ao ancião/anciã " + SafeName(el) : "") + "."; }
                    else if (ptR && !asksNums && SafetyRules.HasStatNumbers(reply, out numHit))
                    { why = "quoted stat numbers nobody asked for (" + numHit + ")"; fix = "Ninguém pediu números: não cite valores de atributos (vida, dano, velocidade, armadura...). Descreva só como você se sente, com palavras."; }
                    else if (ptR && SafetyRules.SpeaksAsGod(reply))
                    { why = "spoke in God's voice (I1)"; fix = "VOCÊ é " + npcName + ", uma criatura deste mundo, NÃO é Deus. Nunca fale como Deus (nada de \"voz poderosa\", \"eu te guio\", \"meus filhos\"). Responda A Deus, como " + npcName + ", em primeira pessoa."; }
                    else if (ptR && SafetyRules.NarratesSelf(reply, npcName))
                    { why = "narrated itself in the third person (I2)"; fix = "Não narre a si mesmo(a) pelo nome (nada de \"*" + npcName.Split(' ')[0] + " sorri*\"). Gestos, se houver, sem o seu nome (\"*sorri*\"); fale em primeira pessoa."; }
                    else if (ptR && SafetyRules.InventsLore(reply))
                    { why = "invented lore it does not know (I7)"; fix = "Não diga que conhece \"muitas histórias/lendas\": você só sabe o que está no FACTOS. Fale só do que sabe."; }
                    else if (ptR && SafetyRules.IdentityClaim(reply, npcName, roleKeys) is string idClaim)
                    { why = "claimed another identity/role (" + idClaim + ")"; fix = "VOCÊ é " + npcName + RoleWords(roleKeys) + ". Você NÃO é " + idClaim + ". Quem fala com você é " + (you == null ? "Deus" : youCall ?? "outra pessoa") + ". Nunca se apresente com outro nome ou cargo."; }
                    else if (ptR && you != null && youReal != null && SafetyRules.NarratesSpeaker(reply, youReal))
                    { why = "narrated or spoke as the speaker"; fix = "Você NÃO é quem fala com você e não narra o que essa pessoa faz ou diz. Fale só como " + npcName + ", em primeira pessoa, respondendo a ela."; }
                    else if (ptR && !metBefore && SafetyRules.ClaimsPastMeeting(reply))
                    { why = "claimed a past meeting that never happened"; fix = "Vocês NUNCA conversaram antes: esta é a primeira conversa. Não diga que já se conhecem, que se lembra dessa pessoa nem \"da última vez\"."; }
                    else if (ptR && SafetyRules.HasMeta(reply))
                    { why = "used meta words or durations in seconds"; fix = "Não fale em segundos, \"jogo\", \"sistema\", \"ação\", \"tiles\" nem estimativas: fale como alguém do seu mundo (\"por um tempo\", \"logo\", \"é longe\")."; }
                    else if (ptR && unknown.Count > 0 && Hearsay(reply, unknown))
                    { why = "invented hearsay about an unknown name (" + string.Join(", ", unknown) + ")"; fix = "Ninguém (nem lugar) chamado " + string.Join(", ", unknown) + " existe neste mundo. Diga simplesmente que não conhece; nada de \"ouvi dizer\", \"dizem que\" ou \"fica longe\"."; }
                    else if (ptR && FakePerson(reply, npc, knownText, out perBad, out perTruth, you))
                    { why = "named a person/role that does not match the game (" + perBad + ")"; fix = "\"" + perBad + "\" não confere com o mundo real. " + perTruth + " Cite só pessoas que estão no FACTOS, com o cargo certo; se não souber, diga que não sabe."; }
                    else if (needElder && !fr.Contains(NativeActions.Fold(elderName)))
                    { why = "did not point to the elder " + elderName + " for an old-history question"; fix = "Você NÃO conhece essa história antiga em detalhe. Diga com simplicidade que não sabe bem e indique quem sabe: " + Deaths.ElderPointLine(npc) + " Não invente nomes nem fatos."; }
                    else if (ptR && BadVocative(reply, npc, you, recog, out vocBad))
                    { why = "addressed the speaker by a wrong name (" + vocBad + ")"; fix = "Quem fala com você NÃO se chama " + vocBad + ". " + (youCall != null ? "Chame quem fala de " + youCall + " ou de \"você\"." : "Você não sabe o nome de quem fala: chame de \"você\" ou \"forasteiro(a)\", sem nome.") ; }
                    else if (ptR && youStranger && youReal != null && recog.CallName == null && NameIn(reply, youReal) && !NameIn(text, youReal))
                    { why = "used the real name of a speaker it does not know (" + youReal + ")"; fix = "Você NÃO conhece quem fala com você e não sabe o nome dessa pessoa. Não diga nenhum nome para ela; pergunte com naturalidade quem é e o que faz."; }
                    else if (ptR && SpeciesMismatch(reply, out spBad))
                    { why = "gave a place the wrong people (" + spBad + ")"; fix = "Erro: " + spBad + ". Use o povo (espécie) certo de cada lugar, como está no FACTOS, ou diga \"o povo de\" + o lugar."; }
                    else if (ptR && SafetyRules.InventedEvent(reply, knownEvents, out evBad))
                    { why = "invented an event (" + evBad + ")"; fix = "Você inventou \"" + evBad + "\": isso NÃO está no FACTOS nem nos boatos. Não invente acontecimentos; se não sabe de nada, diga que está tudo calmo ou que não sabe."; }
                    if (fix != null && youStranger && youReal != null && youReal.Length > 1) fix = fix.Replace(youReal, "quem fala com você");
                    if (fix != null)
                    {
                        retried = true;
                        Main.LogInfo("[GodBox] reply " + why + "; regenerating once.");
                        var again = new List<ChatMessage>(messages);
                        again.Add(new ChatMessage("assistant", reply));
                        again.Add(new ChatMessage("user", "[CORREÇÃO DO SISTEMA, não é fala do jogador] " + fix + " Reescreva a resposta inteira em português. Responda só com a nova fala."));
                        LlmClient.Complete(cfg, cfg.Model, again, cfg.MaxTokens, onReply, onError);
                        return;
                    }
                }
                // v0.7.6 post-check: the action failed/was refused but the reply promises to do it -> regenerate once
                // v0.7.6 (D2): the action WAS executed but the reply refuses/denies it -> regenerate once
                if (doneAction && !godPowerDone && !retried && Refuses(reply))
                {
                    retried = true;
                    Main.LogInfo("[GodBox] reply denied an action that was executed; regenerating once.");
                    var again = new List<ChatMessage>(messages);
                    again.Add(new ChatMessage("assistant", reply ?? ""));
                    again.Add(new ChatMessage("user", PromptBuilder.LanguagePortuguese(cfg)
                        ? "[CORREÇÃO DO SISTEMA, não é fala do jogador] A ação JÁ FOI FEITA no jogo (veja RESULTADO), mas sua resposta recusou ou negou. Reescreva a resposta inteira coerente com o que aconteceu de verdade (pode reclamar, mas cumpriu). Responda só com a nova fala."
                        : "[SYSTEM CORRECTION, not the player] The action WAS carried out in the game, but your reply refused it. Rewrite consistently with what really happened. Reply with the new line only."));
                    LlmClient.Complete(cfg, cfg.Model, again, cfg.MaxTokens, onReply, onError);
                    return;
                }
                if (guard == null && failedAction && !retried && Commits(reply))
                {
                    retried = true;
                    Main.LogInfo("[GodBox] reply promised a failed action; regenerating once.");
                    var again = new List<ChatMessage>(messages);
                    again.Add(new ChatMessage("assistant", reply ?? ""));
                    again.Add(new ChatMessage("user", PromptBuilder.LanguagePortuguese(cfg)
                        ? "[CORREÇÃO DO SISTEMA, não é fala do jogador] Sua resposta prometeu fazer algo que FALHOU ou foi recusado no jogo. Reescreva a resposta inteira: diga claramente que NÃO conseguiu (ou que se recusa) e por quê, sem prometer nada para depois (sem \"vou\", \"farei\", \"seguirei\"). Responda só com a nova fala."
                        : "[SYSTEM CORRECTION, not the player] Your reply promised something that FAILED or was refused. Rewrite it: say clearly you could NOT do it and why, with no promises. Reply with the new line only."));
                    LlmClient.Complete(cfg, cfg.Model, again, cfg.MaxTokens, onReply, onError);
                    return;
                }
                Main.LogInfo("[GodBox] reply received (" + (reply?.Length ?? 0) + " chars).");
                _waiting = false;
                _status = "";
                reply = LeadingTag.Replace(LlmClient.CleanReply(reply, npcName), "");
                reply = SpeakerPrefix.Replace(reply ?? "", "");   // v0.7.8: "Eforce: Obrigada..." -> "Obrigada..."
                if (ptR) reply = SafetyRules.StripLabelPrefix(reply, new[] { npcName, youReal, youCall, "Deus", "Você" });   // v0.8.1 (B3)
                if (ptR && SafetyRules.HasForeignScript(reply))
                {   // v0.8.0: still a foreign script after regenerating -> cut at the first foreign character, last whole sentence
                    string cut = SafetyRules.CutForeign(reply);
                    Main.LogInfo("[GodBox] reply still had a foreign script after regenerating; cut at the first foreign character (" + reply.Length + " -> " + cut.Length + " chars).");
                    reply = cut.Length > 0 ? cut : "*(" + npcName + " hesita e perde o fio da conversa.)* Desculpe, me perdi no que ia dizer.";
                }
                if (ptR && cfg.MaxReplyChars > 0 && reply.Length > cfg.MaxReplyChars)
                {
                    int was = reply.Length; reply = SafetyRules.CapLength(reply, cfg.MaxReplyChars);
                    Main.LogInfo("[GodBox] reply capped at the last whole sentence (" + was + " -> " + reply.Length + " chars, max_reply_chars " + cfg.MaxReplyChars + ").");
                }
                if (replaceWith == null && FalseKillClaim(reply, npc, out string fkLeft))
                {   // v0.7.9: still claims someone else's kill after the regeneration -> code-built line from the chronicle
                    string kl = Deaths.KillLine(fkLeft);
                    Main.LogInfo("[GodBox] reply still claimed the kill of " + fkLeft + "; replaced by the chronicle line.");
                    replaceWith = kl != null ? "Lembro bem: " + kl + ". Eu não tive parte nisso." : "Não fui eu quem matou " + fkLeft + ".";
                }
                if (replaceWith != null) reply = replaceWith;
                else if (appendQ != null) reply = (reply ?? "").TrimEnd() + "\n" + appendQ;
                if (guard == null && fakeWhy != null && EffectClaim(reply))
                {
                    Main.LogInfo("[GodBox] reply still described an effect that did not happen; replaced by a code line.");
                    reply = "*(Nada aconteceu: " + fakeWhy + ".)*";
                }
                if (ptR && SafetyRules.HasEnglish(reply))
                {
                    string st = SafetyRules.StripEnglish(reply);
                    Main.LogInfo("[GodBox] reply still had English after regenerating; stripped the English sentences.");
                    reply = st.Length > 0 ? st : "*(" + npcName + " fica em silêncio.)*";
                }
                if (ptR)
                {   // v0.7.9: stray English words ("mostly por adultos")
                    string fixedEn = SafetyRules.FixEnglishWords(reply, out var enHits);
                    if (enHits.Count > 0) { Main.LogInfo("[GodBox] replaced stray English word(s) in the reply: " + string.Join(", ", enHits.Distinct()) + "."); reply = fixedEn; }
                }
                if (ptR)
                {   // v0.8.2 (I1/I2/I3/I7): code fallbacks after regeneration
                    if (SafetyRules.SpeaksAsGod(reply)) { string g2 = SafetyRules.StripSentences(reply, SafetyRules.SpeaksAsGod); Main.LogInfo("[GodBox] reply still spoke as God; removed those sentences."); reply = g2.Trim().Length > 0 ? g2 : "*(" + npcName + " baixa a cabeça em silêncio.)*"; }
                    string sn = SafetyRules.FixSelfNarration(reply, npcName); if (sn != reply) { Main.LogInfo("[GodBox] removed third-person self-narration."); reply = sn.Length > 0 ? sn : reply; }
                    if (SafetyRules.InventsLore(reply)) { string il = SafetyRules.StripSentences(reply, SafetyRules.IsInventedLoreSentence); if (il.Trim().Length > 0) { reply = il; Main.LogInfo("[GodBox] removed invented-lore sentence(s)."); } }
                    bool fem = false; try { fem = npc.isSexFemale(); } catch { }
                    string uw = SafetyRules.FixUnitWord(reply, fem, out bool uch); if (uch) { Main.LogInfo("[GodBox] replaced the English word 'unit' in the reply."); reply = uw; }
                    string av = SafetyRules.FixAnglicisedVerbs(reply, out var avHits); if (avHits.Count > 0) { Main.LogInfo("[GodBox] replaced anglicised verb(s): " + string.Join(", ", avHits) + "."); reply = av; }
                    string prev = null; try { prev = mem.Turns.LastOrDefault(t => t.Role == "assistant")?.Text; } catch { }
                    if (prev != null) { string rv = SafetyRules.StripRepeatedVocative(reply, prev); if (rv != reply) { Main.LogInfo("[GodBox] dropped a repeated opening vocative."); reply = rv; } }
                }
                if (ptR) reply = GameLocalePt.ScrubSpecies(reply, npc);   // v0.7.8: no raw species ids / subspecies names
                if (ptR) reply = QuoteWarNames(reply);   // v0.8.0 (bug 4): the game's war names, always quoted, never translated
                if (ptR) { string tr = GameLocalePt.ScrubTraitIds(reply, out var trHits); if (trHits.Count > 0) { Main.LogInfo("[GodBox] replaced raw trait id(s) in the reply: " + string.Join(", ", trHits.Distinct()) + "."); reply = tr; } }
                if (ptR && ran != null && ran.Id == "ask_work" && ran.Outcome == "feito")
                {   // v0.8.0 (bug 5): the PLAYER does the quest; the NPC never claims it will do it itself
                    string qa = FixQuestRole(reply);
                    if (qa != reply) { Main.LogInfo("[GodBox] quest-accept reply claimed the NPC would do the quest; replaced those sentences."); reply = qa; }
                }
                if (ptR && replaceWith == null) reply = PostFixes080(reply, npc, you, npcName, recog, youReal, youCall, youStranger, asksNums, effDiff, knownText, knownEvents, needElder ? elderName : null, text, roleKeys, metBefore, unknown);
                if (ptR) reply = FixMeta(reply);        // v0.7.8: "o jogador" -> "você"
                if (ptR) reply = StripDeferred(reply);  // v0.7.8: no "vou pesquisar / te aviso depois"
                mem.Turns.Add(new StoredTurn { Role = "assistant", Text = reply, Year = year, Speaker = npcName });
                try
                {   // v0.8.2 (K): the NPC asked God about a temple / a holy book -> God's next "sim" runs the normal confirmation flow
                    if ((you == null || Avatar.IsAvatar(you)) && _pending == null)
                    {
                        string ask = SafetyRules.AskedOffer(reply);
                        if (ask != null && (ask == "book" || !GodFaith.HasTemple(npc.city)))
                        {
                            _askedOffer = ask; _askedOfferNpc = npc; mem.OfferAsked = ask; mem.OfferAskedYear = year;
                            Main.LogInfo("[GodBox] " + npcName + " asked God about a " + (ask == "book" ? "holy book" : "temple") + "; waiting for a yes/no.");
                        }
                    }
                }
                catch { }
                if (!_countedThisSession)
                {
                    _countedThisSession = true;
                    mem.Conversations++;
                    if (mem.FirstTalkYear < 0) mem.FirstTalkYear = year;
                    if (cfg.WorldfallIntegration && cfg.WorldfallCountTalks) WorldfallBridge.NoteTalk(npc, you);
                }
                mem.LastTalkYear = year;
                mem.Name = npcName;
                MemoryStore.Save(mem);
                MemoryStore.MaybeSummarize(mem, cfg, npcName);
                _scroll.y = float.MaxValue;
                if (_npc == npc) ArmFocus("after reply");
                try { onFinal?.Invoke(reply); } catch (Exception e) { Main.LogWarning("[GodBox] after-reply step failed: " + e.Message); }
            };
            LlmClient.Complete(cfg, cfg.Model, messages, cfg.MaxTokens, onReply, onError);
        }

        /// <summary>v0.8.0 (fix 8): what the NPC knows when a mortal asks it for a divine thing (preach, bless, curse...).</summary>
        static string MortalAskNote(string id, Actor npc, Actor you, bool pt)
        {
            string who = SafeName(you);
            if (!pt) return who + " is a mortal and asked for something only God does (" + id + "). Nothing happened. Answer naturally, in character; no system-like refusal.";
            bool follower = false, prophet = false;
            try { var rel = GodPowers.CurrentReligion(); follower = rel != null && npc.religion == rel; prophet = MemoryStore.God().Prophets.Contains(npc.getID()); } catch { }
            string tail = " Responda de forma NATURAL e em personagem, do seu jeito, como numa conversa comum; NUNCA soe como mensagem de sistema (nada de \"recusado\", \"não é permitido\", \"só os deuses podem fazer isso\" como regra). Nada aconteceu no jogo: não descreva nenhum efeito.";
            if (id == "preach")
                return who + " (um mortal, não Deus) pediu que você pregue/saia pregando. "
                     + (prophet ? "Você é profeta: pode falar da sua fé com " + who + " agora, mas suas viagens de pregação acontecem quando Deus manda, não por pedido de alguém."
                        : follower ? "Você tem fé, mas não é profeta: pode falar da sua crença com " + who + ", mas não sai pregando por aí por pedido de alguém."
                        : "Você não é sacerdote nem profeta dessa fé: pode estranhar o pedido, desconversar ou perguntar por que " + who + " quer isso.") + tail;
            return who + " (um mortal como você, não Deus) pediu algo que só os deuses fazem. Você não tem esse poder: pode achar graça, estranhar, desconversar ou sugerir rezar." + tail;
        }

        // ---------- v0.7.8 reply post-check helpers ----------
        static readonly Regex SecondPersonSelf = new Regex(@"\bvoce (se sente|sente-se|esta|ficou|parece|se tornou) (mais |muito |tao |agora )?(inspirad|abencoad|forte|leve|rapid|curad|amaldicoad|fraco|fraca|cansad|renovad|poderos|azarad)", RegexOptions.CultureInvariant);
        static readonly HashSet<string> ReactPowers = new HashSet<string> { "curse", "bless", "add_trait", "remove_trait", "take_item", "heal", "lift_curse", "give_item" };
        static readonly Regex SpeakerPrefix = new Regex(@"^\s*(?:\*\*)?[\p{L}'][\p{L}' .-]{0,28}(?:\*\*)?:\s+(?=\S)", RegexOptions.CultureInvariant);
        static readonly Regex OrderAcceptRx = new Regex(@"\b(me recuso|recuso|nao aceito|nao posso aceitar|aceito (essa|esta|tal|sua|tua|vossa) ordem|nao vou obedecer|nao obedecerei|obedecerei|cumprirei|nao cumprirei|(tal|essa|esta|sua|tua|vossa) ordem)\b", RegexOptions.CultureInvariant);
        static readonly Regex EffectRx = new Regex(@"(?<!\bnao )(?<!\bnem )\b(perdi|ja nao (sou|tenho)|nao sou mais|nao tenho mais|sem (o|meu|minha) (traco|arma|espada|machado|lanca)|agora sou|me tornei|tornei-me|sinto-me (mais|diferente|abencoad\w*|curad\w*|forte|leve)|me sinto (mais|abencoad\w*|curad\w*|forte|diferente)|aparecerei|apareci|aparecera|surgi|surgirei|cheguei|estou a caminho|ja estou (indo|a caminho)|teleport\w*|fui (transportad|levad|abencoad|curad|amaldicoad)\w*|minha (arma|espada|lanca|machado) (se foi|sumiu|desapareceu)|(arma|espada) (se foi|sumiu|desapareceu)|meu corpo (mudou|se transformou))", RegexOptions.CultureInvariant);
        static bool EffectClaim(string reply) => !string.IsNullOrEmpty(reply) && EffectRx.IsMatch(NativeActions.Fold(reply));
        static readonly Regex SameKingdomRx = new Regex(@"\b(so o rei ou os deuses podem|so o rei pode (mandar|ordenar)|(alguem|cidade|vila|povo|gente|irmaos) do (nosso|meu) (proprio )?reino|(nosso|meu) proprio reino)\b", RegexOptions.CultureInvariant);
        static bool NoKingdom(Actor a) { try { return a.kingdom == null || !a.kingdom.isCiv(); } catch { return true; } }

        static string FakeWhy(GodPowers.Intent i, WorldfallActions.Result ran)
        {
            if (i == null) return null;
            if (i.Id == "remove_trait" && !Regex.IsMatch(NativeActions.Fold(i.What ?? ""), @"\b(traco|tracos|trait|defeito|virtude|qualidade|maldi\w*|azar)")) return null;   // "retire-se" is not a power
            if (ran == null) return "nenhum poder foi usado";
            if (!GodPowers.Matches(i, ran.Id)) return "o jogo fez outra coisa: " + ran.Pt + " (" + ran.Outcome + ")";
            if (ran.Outcome == "feito" || ran.Outcome == "em_andamento") return null;
            return ran.Outcome + (string.IsNullOrEmpty(ran.Reason) ? "" : ": " + ran.Reason);
        }

        // ---------------- v0.8.0 post-check helpers ----------------
        static bool NameIn(string text, string name)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(name) || name.Length < 2) return false;
            return Regex.IsMatch(NativeActions.Fold(text), @"\b" + Regex.Escape(NativeActions.Fold(name)) + @"\b");
        }
        static Actor LivingNamed(string name)
        {
            try
            {
                string f = NativeActions.Fold(name);
                foreach (var a in World.world.units)
                    if (a != null && a.isAlive() && NativeActions.Fold(SafeName(a)) == f) return a;
            }
            catch { }
            return null;
        }
        static City CityNamed(string name)
        {
            try { string f = NativeActions.Fold(name ?? ""); foreach (City c in World.world.cities) if (c != null && c.isAlive() && NativeActions.Fold(c.name ?? "") == f) return c; } catch { }
            return null;
        }
        static Kingdom KingdomNamed(string name)
        {
            try { string f = NativeActions.Fold(name ?? ""); foreach (Kingdom k in World.world.kingdoms) if (k != null && k.isAlive() && k.isCiv() && NativeActions.Fold(k.name ?? "") == f) return k; } catch { }
            return null;
        }
        static bool IsPlaceName(string name) => CityNamed(name) != null || KingdomNamed(name) != null;
        static bool IsKnownPerson(string name, string knownText)
        {
            if (string.IsNullOrEmpty(name)) return true;
            if (knownText != null && Regex.IsMatch(knownText, @"\b" + Regex.Escape(NativeActions.Fold(name)) + @"\b")) return true;
            if (LivingNamed(name) != null || IsPlaceName(name)) return true;
            try { if (Deaths.Find(name, 90).Count > 0) return true; } catch { }
            return false;
        }
        /// <summary>v0.8.0 (fix 5b/5c): a person the reply presents that does not exist, or a role that the game gives to someone else.</summary>
        static bool FakePerson(string reply, Actor npc, string knownText, out string bad, out string truth, Actor you = null)
        {
            bad = null; truth = null;
            List<SafetyRules.PersonClaim> claims;
            try { claims = SafetyRules.PersonClaims(reply); } catch { return false; }
            foreach (var c in claims)
            {
                try
                {
                    if (!IsKnownPerson(c.Name, knownText))
                    {
                        bad = c.Name;
                        truth = "Ninguém chamado " + c.Name + " existe neste mundo." + (c.Role == "anciao" && Deaths.ElderPointLine(npc) is string el ? " " + ElderTruth(npc) : "");
                        Main.LogInfo("[GodBox] person check: \"" + c + "\" is not a living unit, a chronicle entry or a name in the prompt.");
                        return true;
                    }
                    if (IsPlaceName(c.Name)) continue;   // "Neen, o reino..." is a place, not a person
                    if (c.Role == "anciao" || c.Role == "rei" || c.Role == "lider")
                    {
                        City city = c.Place != null ? CityNamed(c.Place) : npc?.city;
                        Kingdom k = c.Place != null ? (KingdomNamed(c.Place) ?? city?.kingdom) : npc?.kingdom;
                        if (k != null && !IsCivK(k)) k = null;   // v0.8.1 (B4): only real (civ) kingdoms, never mob/neutral ones
                        if (c.Place != null && city == null && k == null) continue;   // unknown place: the name check is enough
                        Actor real = null; string roleTxt = null, where = null;
                        if (c.Role == "anciao") { if (city == null) continue; real = Deaths.Elder(city); roleTxt = real != null ? Deaths.ElderTitleOf(real) : "ancião"; where = city.name; }
                        else if (c.Role == "rei") { if (k == null) continue; real = k.king; roleTxt = real != null && real.isSexFemale() ? "rainha" : "rei"; where = k.name; }
                        else { if (city == null) { if (k == null) continue; real = k.king; roleTxt = "governante"; where = k.name; } else { real = city.leader; roleTxt = "líder"; where = city.name; } }
                        string realName = real != null ? SafeName(real) : null;
                        if (realName != null && NativeActions.Fold(realName) == NativeActions.Fold(c.Name)) continue;
                        if (realName == null && c.Role != "anciao" && city != null && c.Role == "lider" && k?.king != null && NativeActions.Fold(SafeName(k.king)) == NativeActions.Fold(c.Name)) continue;   // the king leads a city without its own leader
                        bad = c.ToString();
                        truth = realName != null ? RoleSentence(real, roleTxt, where, npc, you) + (NativeActions.Fold(c.Name) != NativeActions.Fold(realName) ? ", não " + c.Name : "") + "."
                                                 : where + " não tem " + roleTxt + " agora; " + c.Name + " não é " + roleTxt + ".";   // v0.8.1 (B4): natural sentence
                        Main.LogInfo("[GodBox] person check: role claim \"" + c + "\" does not match the game (" + (realName ?? "nobody") + ").");
                        return true;
                    }
                    if (c.Role == "mora" && c.Place != null)
                    {
                        var who = LivingNamed(c.Name); var pc = CityNamed(c.Place);
                        if (who != null && pc != null && who.city != pc)
                        {
                            bad = c.ToString();
                            truth = c.Name + (who.city != null ? " mora em " + who.city.name : " não mora em cidade nenhuma") + ", não em " + c.Place + ".";
                            Main.LogInfo("[GodBox] person check: \"" + c + "\" lives elsewhere (" + (who.city?.name ?? "no city") + ").");
                            return true;
                        }
                    }
                }
                catch { }
            }
            return false;
        }
        /// <summary>v0.8.1 (B3): folded role words the NPC really has.</summary>
        static List<string> RoleKeys(Actor npc)
        {
            var l = new List<string>();
            try { if (npc.isKing()) l.Add("rei"); } catch { }
            try { if (npc.isCityLeader()) l.Add("lider"); } catch { }
            try { if (GodFaith.IsProphet(npc)) l.Add("profeta"); } catch { }
            try { if (npc.isWarrior()) l.Add("guerreiro"); } catch { }
            return l;
        }
        static string RoleWords(List<string> keys)
        {
            if (keys == null || keys.Count == 0) return "";
            var w = keys.Select(k => k == "rei" ? "rei/rainha" : k == "lider" ? "líder da cidade" : k).ToList();
            return " (" + string.Join(", ", w) + ")";
        }
        static readonly Regex HearsayRx = new Regex(@"\b(ouvi dizer|ouvi falar|dizem que|contam que|falam que|fica longe|mora longe|vive longe|esta longe|mora alem|vive alem|para o (norte|sul|leste|oeste)|terras distantes)\b", RegexOptions.CultureInvariant);
        static bool HearsaySentence(string seg, List<string> unknown)
        {
            string f = NativeActions.Fold(seg ?? "");
            if (Regex.IsMatch(f, @"\b(nao conheco|nunca ouvi|nao sei quem|nao sei onde|ninguem chamado|nenhum lugar)\b")) return false;
            return HearsayRx.IsMatch(f) || unknown.Any(u => NameIn(seg, u));
        }
        static bool Hearsay(string reply, List<string> unknown)
        {
            foreach (Match m in Regex.Matches(reply ?? "", @"\*[^*]+\*|[^.!?…*]+(?:[.!?…]+|$)")) if (HearsaySentence(m.Value, unknown)) return true;
            return false;
        }
        static bool IsCivK(Kingdom k) { try { return k != null && k.isAlive() && k.isCiv(); } catch { return false; } }
        /// <summary>v0.8.1 (B4): "Faona é a líder de Nye Seon" / "o rei de Uaau é Oon" / "eu sou..." / "você é...".</summary>
        static string RoleSentence(Actor real, string roleTxt, string where, Actor npc, Actor you)
        {
            bool fem = false; try { fem = real.isSexFemale(); } catch { }
            string art = roleTxt == "rainha" || roleTxt == "anciã" || (fem && (roleTxt == "líder" || roleTxt == "governante")) ? "a" : "o";
            string name = SafeName(real);
            if (real == npc) return "Eu sou " + art + " " + roleTxt + " de " + where;
            if (you != null && real == you) return "Você é " + art + " " + roleTxt + " de " + where;
            if (roleTxt == "rei" || roleTxt == "rainha") return (art == "a" ? "A " : "O ") + roleTxt + " de " + where + " é " + name;
            return name + " é " + art + " " + roleTxt + " de " + where;
        }
        /// <summary>v0.8.1 (B4/B6): the real rulers around the NPC (civ kingdoms only), as natural sentences.</summary>
        static string RulersFact(Actor npc, Actor you)
        {
            var parts = new List<string>();
            try
            {
                var c = npc?.city; var k = npc?.kingdom;
                if (c != null && c.isAlive() && c.leader != null && c.leader.isAlive()) parts.Add(RoleSentence(c.leader, "líder", c.name, npc, you));
                if (IsCivK(k) && k.king != null && k.king.isAlive()) { bool f = false; try { f = k.king.isSexFemale(); } catch { } parts.Add(RoleSentence(k.king, f ? "rainha" : "rei", k.name, npc, you)); }
                else if (IsCivK(k)) parts.Add(k.name + " não tem rei nem rainha agora");
                int n = 0;
                foreach (Kingdom o in World.world.kingdoms)
                {
                    if (n >= 3) break;
                    if (o == k || !IsCivK(o) || o.king == null || !o.king.isAlive()) continue;
                    bool f = false; try { f = o.king.isSexFemale(); } catch { }
                    parts.Add(RoleSentence(o.king, f ? "rainha" : "rei", o.name, npc, you)); n++;
                }
            }
            catch { }
            if (parts.Count == 0) return null;
            string t = string.Join("; ", parts);
            return char.ToUpper(t[0]) + t.Substring(1) + ".";
        }
        static string ElderTruth(Actor npc)
        {
            try
            {
                var e = Deaths.Elder(npc?.city); if (e == null) return "";
                return "A pessoa mais velha de " + npc.city.name + " é " + (Deaths.ElderTitleOf(e) == "anciã" ? "a anciã " : "o ancião ") + SafeName(e) + ".";
            }
            catch { return ""; }
        }
        /// <summary>v0.8.0 (5:26 d): a vocative that names someone ELSE who exists (the queen, a relative...) or, for a stranger, the speaker's real name.</summary>
        static bool BadVocative(string reply, Actor npc, Actor you, Recognition.Info rec, out string bad)
        {
            bad = null;
            List<string> vs; try { vs = SafetyRules.Vocatives(reply); } catch { return false; }
            if (vs.Count == 0) return false;
            var ok = new HashSet<string>();
            void Add(string n) { if (!string.IsNullOrEmpty(n)) ok.Add(NativeActions.Fold(n)); }
            if (you == null || Avatar.IsAvatar(you)) { Add("Deus"); try { Add(Gods.NameFor(npc)); } catch { } }
            else
            {
                if (rec == null || !rec.Active || rec.K >= Recognition.Know.Name) Add(SafeName(you));
                if (rec != null) Add(rec.CallName);
            }
            if (you != null && Avatar.IsAvatar(you)) { Add(SafeName(you)); try { Add(Gods.NameFor(npc)); } catch { } }
            string youReal = you != null ? NativeActions.Fold(SafeName(you)) : null;
            string self = NativeActions.Fold(SafeName(npc));
            foreach (var v in vs)
            {
                string f = NativeActions.Fold(v);
                if (ok.Contains(f) || f == self) continue;   // self-greeting has its own check
                if (f == youReal || LivingNamed(v) != null || Deaths.Find(v, 95).Count > 0) { bad = v; return true; }
            }
            return false;
        }
        static readonly Regex SpeciesPlaceRx = new Regex(@"\b(os|as|dos|das|aos|as|pelos|pelas|com os|com as)\s+(\p{L}+)\s+(de|da|do)\s+(\p{Lu}[\p{L}'’-]*(?:\s\p{Lu}[\p{L}'’-]*){0,2})", RegexOptions.CultureInvariant);
        static HashSet<string> WorldSpeciesStems()
        {
            var set = new HashSet<string>();
            try { foreach (Kingdom k in World.world.kingdoms) if (k != null && k.isAlive() && k.isCiv()) { string sp = FactsBuilder.KingdomSpecies(k, true); if (!string.IsNullOrEmpty(sp)) set.Add(SafetyRules.SpeciesStem(sp)); } } catch { }
            foreach (var w in new[] { "humanos", "elfos", "anões", "orcs" }) set.Add(SafetyRules.SpeciesStem(w));
            return set;
        }
        static string PlaceSpecies(string place)
        {
            var c = CityNamed(place); if (c != null) return FactsBuilder.CitySpecies(c, true);
            var k = KingdomNamed(place); if (k != null) return FactsBuilder.KingdomSpecies(k, true);
            return null;
        }
        /// <summary>v0.8.0 (5:26 d): "os elfos de Az Uz" when Az Uz is an orc city.</summary>
        static bool SpeciesMismatch(string reply, out string bad)
        {
            bad = null;
            if (string.IsNullOrEmpty(reply)) return false;
            HashSet<string> stems = null;
            foreach (Match m in SpeciesPlaceRx.Matches(reply))
            {
                string place = m.Groups[4].Value.Trim(), word = m.Groups[2].Value;
                string real = PlaceSpecies(place);
                if (real == null) { var sp = place.Split(' '); if (sp.Length > 1) { place = sp[0] + " " + sp[1]; real = PlaceSpecies(place); } if (real == null) { place = sp[0]; real = PlaceSpecies(place); } }
                if (real == null) continue;
                stems = stems ?? WorldSpeciesStems();
                string ws = SafetyRules.SpeciesStem(word);
                if (!stems.Contains(ws) || ws == SafetyRules.SpeciesStem(real)) continue;
                bad = word + " de " + place + " (o povo de " + place + " é " + real + ")";
                return true;
            }
            return false;
        }
        static string FixSpecies(string reply)
        {
            HashSet<string> stems = WorldSpeciesStems();
            return SpeciesPlaceRx.Replace(reply, m =>
            {
                string place = m.Groups[4].Value.Trim();
                string real = PlaceSpecies(place);
                if (real == null) { var sp = place.Split(' '); real = PlaceSpecies(sp[0]) ?? (sp.Length > 1 ? PlaceSpecies(sp[0] + " " + sp[1]) : null); }
                string ws = SafetyRules.SpeciesStem(m.Groups[2].Value);
                if (real == null || !stems.Contains(ws) || ws == SafetyRules.SpeciesStem(real)) return m.Value;
                string art = m.Groups[1].Value.ToLowerInvariant();
                string np = art.StartsWith("d") ? "do povo" : art.StartsWith("ao") ? "ao povo" : art.StartsWith("pel") ? "pelo povo" : art.StartsWith("com") ? "com o povo" : "o povo";
                if (char.IsUpper(m.Groups[1].Value[0])) np = char.ToUpperInvariant(np[0]) + np.Substring(1);
                return np + " " + m.Groups[3].Value + " " + m.Groups[4].Value;
            });
        }
        /// <summary>v0.8.0 code fallbacks after the single regeneration (each one logs).</summary>
        static string PostFixes080(string reply, Actor npc, Actor you, string npcName, Recognition.Info rec, string youReal, string youCall, bool youStranger,
                                   bool asksNums, string effDiff, string knownText, string knownEvents, string elderName, string playerText,
                                   List<string> roleKeys = null, bool metBefore = true, List<string> unknown = null)
        {
            if (string.IsNullOrEmpty(reply)) return reply;
            string r = reply;
            try
            {
                // ---------- v0.8.1 fallbacks: only remove sentences or append a correct fact ----------
                if (SafetyRules.IdentityClaim(r, npcName, roleKeys) is string idc)
                {
                    r = SafetyRules.StripSentences(r, seg => SafetyRules.IsIdentityClaimSentence(seg, npcName, roleKeys));
                    if (r.Trim().Length == 0) r = "Eu sou " + npcName + ".";
                    Main.LogInfo("[GodBox] reply still claimed another identity/role (" + idc + "); removed those sentences (code fallback).");
                }
                if (you != null && youReal != null && SafetyRules.NarratesSpeaker(r, youReal))
                {
                    r = SafetyRules.StripSentences(r, seg => SafetyRules.NarratesSpeaker(seg, youReal));
                    Main.LogInfo("[GodBox] reply still narrated the speaker; removed those sentences (code fallback).");
                }
                if (!metBefore && SafetyRules.ClaimsPastMeeting(r))
                {
                    r = SafetyRules.StripSentences(r, SafetyRules.IsPastMeetingSentence);
                    Main.LogInfo("[GodBox] reply still claimed a past meeting (first conversation); removed those sentences (code fallback).");
                }
                if (SafetyRules.HasMeta(r))
                {
                    r = SafetyRules.StripMeta(r);
                    Main.LogInfo("[GodBox] reply had meta words/seconds; stripped them (code fallback).");
                }
                if (unknown != null && unknown.Count > 0 && Hearsay(r, unknown))
                {
                    r = SafetyRules.StripSentences(r, seg => HearsaySentence(seg, unknown));
                    string line = "Não conheço ninguém chamado " + unknown[0] + ".";
                    if (!NativeActions.Fold(r).Contains("nao conheco")) r = (r.TrimEnd() + " " + line).Trim();
                    Main.LogInfo("[GodBox] reply invented hearsay about unknown name(s) " + string.Join(", ", unknown) + "; removed those sentences and said it does not know (code fallback).");
                }

                if (!asksNums && SafetyRules.HasStatNumbers(r, out string nh))
                { r = SafetyRules.StripStatNumbers(r); Main.LogInfo("[GodBox] reply still had stat numbers (" + nh + "); stripped them (code fallback)."); }
                if (StatClaim(r, effDiff, out string sc))
                {
                    string fd = NativeActions.Fold(effDiff ?? ""); bool none = fd.Contains("nao mudaram");
                    r = SafetyRules.StripSentences(r, seg => StatClaims.Any(t => (none || !fd.Contains(t.key)) && t.rx.IsMatch(NativeActions.Fold(seg))));
                    Main.LogInfo("[GodBox] reply still claimed a stat change (" + sc + ") not in the real changes; removed those sentences (code fallback).");
                }
                for (int i = 0; i < 3 && FakePerson(r, npc, knownText, out string pb, out string pt, you); i++)
                {
                    string nm = pb.Contains(" [") ? pb.Substring(0, pb.IndexOf(" [")) : pb;
                    r = SafetyRules.StripSentences(r, seg => NameIn(seg, nm));
                    if (!r.Contains(pt)) r = (r + " " + pt).Trim();
                    Main.LogInfo("[GodBox] reply still named \"" + pb + "\"; removed those sentences and stated the real fact (code fallback).");
                }
                if (elderName != null && !NameIn(r, elderName))
                {
                    string line = Deaths.ElderPointLine(npc);
                    if (line != null) { r = (r.TrimEnd() + " " + line).Trim(); Main.LogInfo("[GodBox] reply did not point to the elder " + elderName + "; appended the code line."); }
                }
                for (int i = 0; i < 3 && BadVocative(r, npc, you, rec, out string vb); i++)
                {
                    r = SafetyRules.ReplaceVocative(r, vb, youCall);
                    Main.LogInfo("[GodBox] reply still addressed the speaker as \"" + vb + "\"; replaced the vocative with " + (youCall ?? "nothing") + " (code fallback).");
                }
                if (youStranger && youReal != null && rec?.CallName == null && NameIn(r, youReal) && !NameIn(playerText, youReal))
                {
                    r = SafetyRules.ReplaceVocative(r, youReal, null);
                    bool fem = false; try { fem = you.isSexFemale(); } catch { }
                    r = Regex.Replace(r, @"\b" + Regex.Escape(youReal) + @"\b", fem ? "forasteira" : "forasteiro");
                    Main.LogInfo("[GodBox] reply used the real name of an unknown speaker; removed it (code fallback).");
                }
                if (SpeciesMismatch(r, out string spb))
                { r = FixSpecies(r); Main.LogInfo("[GodBox] reply gave a place the wrong people (" + spb + "); replaced with \"o povo de\" (code fallback)."); }
                if (SafetyRules.InventedEvent(r, knownEvents, out string ev))
                {
                    r = SafetyRules.StripSentences(r, seg => SafetyRules.IsInventedEventSentence(seg, knownEvents));
                    Main.LogInfo("[GodBox] reply still invented an event (" + ev + "); removed those sentences (code fallback).");
                    if (r.Length == 0) r = "Por aqui está tudo calmo; não sei de nada acontecendo.";
                }
            }
            catch (Exception e) { Main.LogWarning("[GodBox] v0.8.0 post-fixes failed: " + e.Message); return reply; }
            return r.Trim().Length > 0 ? r : "*(" + npcName + " fica em silêncio.)*";
        }
        static readonly (string key, Regex rx)[] StatClaims =
        {
            ("dano", new Regex(@"\b(golpes (pesam|mais fortes|mais pesados|perderam|fracos|enfraquec\w*)|bracos fraquejam|mais (forte|poderos[oa]|poder|forca|dano)|forca (redobrada|aumentada|maior|renovada)|(minha )?forca (cresceu|aumentou))\b", RegexOptions.CultureInvariant)),
            ("vida maxima", new Regex(@"\b(feridas (se )?fech\w*|fecha minhas feridas|vida (escorre|escorrer)|corpo (ficou )?fragil|mais (vida|vitalidade|saude|resistente|robust[oa])|vitalidade (redobrada|renovada|aumentada)|vida (maxima )?(aumentou|cresceu))\b", RegexOptions.CultureInvariant)),
            ("velocidade", new Regex(@"\b(pernas (parecem |estao |ficaram )?(leves|mais leves|pesam|pesadas)|leves? como (o )?vento|mais (rapid[oa]|veloz|agil|ligeir[oa])|velocidade (aumentou|maior|cresceu))\b", RegexOptions.CultureInvariant)),
            ("armadura", new Regex(@"\b(pele (parece |ficou )?(mais dura|dura como|afin\w*)|mais (protegid[oa]|blindad[oa])|pele (mais dura|de ferro)|armadura (aumentou|maior))\b", RegexOptions.CultureInvariant)),
            ("expectativa de vida", new Regex(@"\b(viverei mais|anos (foram )?encurtados|viver mais|mais anos de vida|longevidade)\b", RegexOptions.CultureInvariant)),
            // v0.8.0 (fix 3): vitality/health going DOWN and happiness changes ("sinto minha vitalidade diminuir" after take_item)
            ("vida", new Regex(@"\b((minha )?(vida|vitalidade|saude|energia vital) (diminu\w*|reduz\w*|enfraquec\w*|caiu|cai|baixou|baixa|minguou|se esvai\w*|escapa\w*|menor)|menos (vida|vitalidade|saude)|perdi (vida|vitalidade|saude|parte da minha (vida|vitalidade|saude))|(vida|vitalidade|saude) (foi|esta sendo) (reduzid|drenad|tirad)\w*)\b", RegexOptions.CultureInvariant)),
            ("felicidade", new Regex(@"\b((minha )?felicidade (aument\w*|diminu\w*|caiu|cai|baixou|subiu|cresceu|se foi|reduz\w*|foi (tirada|levada|roubada))|(mais|menos) feliz(es)?|perdi (a |minha )?felicidade|tiraram (a |minha )?felicidade|alegria (diminu\w*|aument\w*|se foi))\b", RegexOptions.CultureInvariant)),
        };
        /// <summary>The reply claims a stat went up that the real before/after diff does not show.</summary>
        static bool StatClaim(string reply, string diff, out string claimed)
        {
            claimed = null;
            if (diff == null || string.IsNullOrEmpty(reply)) return false;
            string fd = NativeActions.Fold(diff), fr = NativeActions.Fold(reply);
            bool none = fd.Contains("nao mudaram");
            foreach (var (key, rx) in StatClaims)
                if (rx.IsMatch(fr) && (none || !fd.Contains(key))) { claimed = key; return true; }
            return false;
        }
        /// <summary>A percentage in the reply that appears nowhere in the prompt (FACTS / RESULT).</summary>
        static bool BadPercent(string reply, string known, out string pc)
        {
            pc = null;
            foreach (Match m in Regex.Matches(reply ?? "", @"[+-]?\s?(\d{1,4}(?:[.,]\d+)?)\s?%"))
            {
                string n = m.Groups[1].Value.Replace(',', '.');
                if (!Regex.IsMatch(known ?? "", @"(^|[^\d.,])" + Regex.Escape(n) + @"\s?%") && !Regex.IsMatch(known ?? "", @"(^|[^\d.,])" + Regex.Escape(n.Replace('.', ',')) + @"\s?%")) { pc = m.Value.Trim(); return true; }
            }
            return false;
        }
        /// <summary>The NPC names itself in the third person as a SUBJECT ("Dore matou Norron" said by Dore).
        /// v0.7.9: only the NPC's OWN name, only as the subject of a verb, never vocatives/introductions/other names;
        /// "por Dore"/"de Dore" (someone else's deed, or narration about others) does not count. Logs what matched.</summary>
        static readonly Regex Verb3Rx = new Regex(
            @"^(foi|era|e|esta|estava|tem|tinha|fez|faz|disse|diz|acha|achou|pensa|sabe|sabia|quer|queria|vai|ira|ia|pode|podia|nao|nunca|sempre|tambem|ja|lutou|luta|matou|mata|governa|governou|vive|viveu|nasceu|venceu|perdeu|ama|odeia|prefere|lembra|viu|ouviu|chegou|partiu|ficou|esteve|tornou|virou|precisa|deve|devera|seguira|segue|obedece|obedeceu|aceita|aceitou|recusa|recusou|\w{3,}(ou|eu|iu|ara|era|ira))$",
            RegexOptions.CultureInvariant);
        static bool SelfThirdPerson(string reply, Actor npc, string npcName, out string matched)
        {
            matched = null;
            if (string.IsNullOrWhiteSpace(npcName) || npcName.Length < 2 || string.IsNullOrEmpty(reply)) return false;
            if (PromptBuilder.SameAsPlace(npc, npcName) != null) return false;   // "Uaau" may be the kingdom
            string fr = NativeActions.Fold(Regex.Replace(reply, @"\*[^*]*\*", " ")), n = Regex.Escape(NativeActions.Fold(npcName));
            if (!Regex.IsMatch(fr, @"\b" + n + @"\b")) return false;
            foreach (Match m in Regex.Matches(fr, @"(^|[.!?;:]\s*|,\s*|\b(e|mas|pois|porque|que|quando|entao)\s+)" + n + @"\s+(\w+)"))
            {
                int at = m.Index;
                string before = fr.Substring(Math.Max(0, at - 30), Math.Min(30, at)) + m.Groups[1].Value;
                // introductions / vocatives / possessives / agents: not the third person
                if (Regex.IsMatch(before, @"\b(sou|eu sou|me chamo|meu nome e|chamam-me|chamam me|aqui e|eu,|por|de|do|da|para|pra|com|ola|oi|caro|cara|senhor|senhora)\s*$")) continue;
                string verb = m.Groups[3].Value;
                if (!Verb3Rx.IsMatch(verb)) continue;
                matched = m.Value.Trim();
                return true;
            }
            return false;
        }
        /// <summary>v0.7.9: "matei X / eu matei X / assassinei X" while the chronicle says someone else killed X.</summary>
        static readonly Regex KillClaimRx = new Regex(@"\b(?:eu )?(?:matei|assassinei|derrotei e matei|tirei a vida de|dei fim a|dei cabo de)\s+(?:o |a |ao |à |o rei |a rainha |o lider |a lider )?([A-Za-zÀ-ÿ'][\wÀ-ÿ'-]{1,24})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static bool FalseKillClaim(string reply, Actor npc, out string victim)
        {
            victim = null;
            if (string.IsNullOrEmpty(reply) || npc == null) return false;
            foreach (Match m in KillClaimRx.Matches(Regex.Replace(reply, @"\*[^*]*\*", " ")))
            {
                string v = m.Groups[1].Value;
                var k = Deaths.KilledBy(npc, v);
                if (k == false) { victim = v; return true; }
            }
            return false;
        }
        /// <summary>v0.7.9: "fui eleito / me tornei líder / fui coroado" by someone who rules nothing.</summary>
        static readonly Regex ElectedRx = new Regex(@"\b(fui eleit[oa]|fui escolhid[oa] (como |para )?(lider|rei|rainha)|me tornei (o |a )?(lider|rei|rainha)|fui coroad[oa]|assumi (a lideranca|o trono|o governo))\b", RegexOptions.CultureInvariant);
        static readonly Regex GuideWords = new Regex(@"\b(me|nos) (leve|leva|levar|levem|guie|guia|guiar|acompanhe|acompanha|conduza|conduz)\b|\b(leve|leva|guie|guia|acompanhe|acompanha|conduza)-(me|nos)\b|\bvamos (ate|para|pra|juntos)\b|\bmostre o caminho\b|\bme mostre o caminho\b|\btake me\b|\bguide me\b|\blead me\b", RegexOptions.CultureInvariant);
        /// <summary>v0.7.8: "Olá Norron" said BY Norron.</summary>
        static bool SelfGreeting(string reply, string npcName)
        {
            if (string.IsNullOrWhiteSpace(npcName) || npcName.Length < 2 || string.IsNullOrEmpty(reply)) return false;
            string fr = NativeActions.Fold(Regex.Replace(reply, @"^\s*\*[^*]*\*", " "));
            return Regex.IsMatch(fr, @"^\W*(ola|oi|saudacoes|salve|bom dia|boa tarde|boa noite|ave|seja bem-vind\w*|bem-vind\w*)[,!.\s]+(caro |cara |querido |querida |senhor |senhora )?" + Regex.Escape(NativeActions.Fold(npcName)) + @"\b");
        }
        /// <summary>v0.7.8: the NPC claims the speaker's role ("Eu sou o rei", "tomei posse") while the speaker rules and the NPC doesn't.</summary>
        static bool RoleClaim(string reply, Actor you, Actor npc, out string role)
        {
            role = null;
            if (you == null || npc == null || string.IsNullOrEmpty(reply)) return false;
            bool yk = false, yl = false, nk = false;
            try { yk = you.isKing(); yl = you.isCityLeader(); nk = npc.isKing() || npc.isCityLeader(); } catch { }
            if (nk || !(yk || yl)) return false;
            role = PromptBuilder.SpeakerRole(you) ?? "o governante";
            string fr = NativeActions.Fold(reply);
            return Regex.IsMatch(fr, yk
                ? @"\b((eu )?sou (o |a )?(novo |nova )?(rei|rainha|soberan[oa]|monarca)|tomei posse|assumi o trono|subi ao trono|meu reinado|meu trono|como (rei|rainha),? eu|eu governo|eu reino)\b"
                : @"\b((eu )?sou (o |a )?(novo |nova )?lider|tomei posse|assumi a lideranca|como lider,? eu|eu governo)\b");
        }
        static readonly Regex MetaRx = new Regex(@"\b(jogador(a|es|as)?|player)\b", RegexOptions.CultureInvariant);
        static readonly Regex DeferRx = new Regex(
            @"\b((vou|irei|vamos) (pesquisar|investigar|descobrir|averiguar|procurar saber|perguntar por ai|me informar)|pesquisarei|investigarei|descobrirei|averiguarei|posso (pesquisar|investigar|descobrir|averiguar)|depois (te|lhe|vos) (procuro|aviso|conto|digo)|(te|lhe) (aviso|procuro|conto|digo) depois|volto (depois|mais tarde|com a resposta)|trago a resposta|quando (eu )?(souber|descobrir),? (te |lhe )?(aviso|conto|digo))\b",
            RegexOptions.CultureInvariant);
        static string FixMeta(string reply)
        {
            if (string.IsNullOrEmpty(reply) || !MetaRx.IsMatch(NativeActions.Fold(reply))) return reply;
            string r = reply;
            foreach (var (a, b) in new[] { ("do jogador", "de você"), ("da jogadora", "de você"), ("ao jogador", "a você"), ("à jogadora", "a você"), ("pelo jogador", "por você"), ("pela jogadora", "por você"),
                                           ("para o jogador", "para você"), ("para a jogadora", "para você"), ("o jogador", "você"), ("a jogadora", "você"), ("os jogadores", "vocês"), ("jogadores", "vocês"), ("jogadora", "você"), ("jogador", "você"), ("players", "vocês"), ("player", "você") })
                r = Regex.Replace(r, @"\b" + Regex.Escape(a) + @"\b", b, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            Main.LogInfo("[GodBox] replaced 'jogador/player' by 'você' in the reply.");
            return r;
        }
        static string StripDeferred(string reply)
        {
            if (string.IsNullOrEmpty(reply) || !DeferRx.IsMatch(NativeActions.Fold(reply))) return reply;
            var parts = Regex.Split(reply, @"(?<=[.!?…])\s+");
            var keep = parts.Where(p => !DeferRx.IsMatch(NativeActions.Fold(p))).ToList();
            if (keep.Count == 0) return reply;
            Main.LogInfo("[GodBox] stripped a deferred promise ('vou pesquisar / te aviso depois') from the reply.");
            return string.Join(" ", keep);
        }
        /// <summary>The NPC introduces itself with the speaker's name ("Olá, sou Raneas" said to Raneas).</summary>
        static bool ClaimsToBe(string reply, Actor you)
        {
            if (you == null || string.IsNullOrEmpty(reply)) return false;
            string yn = SafeName(you); if (yn == "?" || yn.Length < 2) return false;
            return Regex.IsMatch(NativeActions.Fold(reply), @"\b(sou|eu sou|me chamo|meu nome e|aqui e) (o |a )?" + Regex.Escape(NativeActions.Fold(yn)) + @"\b");
        }

        // "vou seguir o teu pedido", "farei", "seguirei"... (a negated "não vou" does not count)
        static readonly Regex CommitRx = new Regex(
            @"(?<!\bnao )(?<!\bnunca )\b(vou|irei|farei|seguirei|cumprirei|executarei|obedecerei|atacarei|marcharei|fundarei|levarei|marcarei|declararei|destruirei|iniciarei|comecarei|prepararei|providenciarei|enviarei|mandarei|ordenarei|convocarei|reunirei|partirei|lutarei|invadirei|aparecerei|aparecera|surgirei|surgira|chegarei|caminharei|teleportarei|andarei|iniciaremos|comecaremos|marcharemos|atacaremos|declararemos|prepararemos|daremos inicio|ja estou (preparando|reunindo|ordenando|enviando|mandando)|estamos (preparando|marchando)|os preparativos (ja )?comecaram|ja vou|pode deixar|sera feito|considere feito|i will|i'll|will do)\b",
            RegexOptions.CultureInvariant);

        // "isso não vai acontecer", "me recuso", "jamais"... (D2: the text refused while the code went on)
        static readonly Regex RefuseRx = new Regex(
            @"\b(nao vai acontecer|nao vou (fazer|obedecer|atacar|cumprir|aceitar|seguir)|me recuso|recuso(-me)?\b|jamais (farei|faria|vou)|nunca (farei|faria|vou fazer)|nao farei|nao posso fazer isso|de jeito nenhum|nem pensar|nem morto|i refuse|i won't|i will not|not going to happen|never)\b",
            RegexOptions.CultureInvariant);

        static bool Refuses(string reply) => !string.IsNullOrEmpty(reply) && RefuseRx.IsMatch(NativeActions.Fold(reply));

        static bool Commits(string reply) => !string.IsNullOrEmpty(reply) && CommitRx.IsMatch(NativeActions.Fold(reply));

        // v0.7.8: "se o senhor confirmar, marcharei" is a conditional, not a promise; any other sentence that commits is
        static readonly Regex ConditionalRx = new Regex(@"\bse\b[^.!?]{0,60}\b(confirm\w*|quiser|ordenar|mandar|desejar|disser sim|autorizar|der a ordem)\b|\b(apos|depois d[ao]|com) (a |o |sua |vossa )?(confirmacao|sim|ordem)\b", RegexOptions.CultureInvariant);
        static bool CommitsNow(string reply)
        {
            if (string.IsNullOrEmpty(reply)) return false;
            foreach (var sent in Regex.Split(NativeActions.Fold(reply), @"(?<=[.!?\n])"))
                if (!ConditionalRx.IsMatch(sent) && CommitRx.IsMatch(sent)) return true;
            return false;
        }

        static string LastQuestion(string reply)
        {
            string f = NativeActions.Fold(reply ?? "");
            int q = f.LastIndexOf('?');
            if (q < 0) return null;
            if (Regex.Replace(f.Substring(q + 1), @"\*[^*]*\*", "").Trim().Length > 40) return null;   // the question is not at the end
            int start = Math.Max(f.LastIndexOfAny(new[] { '.', '!', '\n' }, q) + 1, 0);
            return f.Substring(start, q - start + 1);
        }
        static bool EndsWithQuestion(string reply) => LastQuestion(reply) != null;
        static readonly Regex ConfirmAskRx = new Regex(@"\b(confirm\w*|sim ou nao|sim\/nao|tem certeza|deseja|desejais|quer mesmo|quer que|ordena|ordenais|devo|posso|autoriza\w*|da a ordem|mantem a ordem|e isso mesmo|insiste)\b", RegexOptions.CultureInvariant);
        static bool AsksConfirm(string reply) { var q = LastQuestion(reply); return q != null && ConfirmAskRx.IsMatch(q); }

        // "não estou autorizado sem ordens dos deuses" while talking TO the god voice / "preciso da ordem do rei" to the king
        static readonly Regex AuthorityRx = new Regex(@"\b(nao (estou|sou) autorizad[oa]|nao tenho (autorizacao|permissao|ordens?)|sem (as |a )?(ordens?|permissao|autorizacao|aval|bencao) (dos deuses|divinas?|dos ceus|do rei|real|de sua majestade)|(preciso|precisamos|necessito|aguardo|aguardamos|espero) (de |das |da |dos )?(ordens?|permissao|autorizacao|aval|bencao) (dos deuses|divinas?|dos ceus|do rei|real)|so (o rei|os deuses|deus|um deus|meu rei)( ou (o rei|os deuses|deus|um deus))? (pode|podem) (me )?(mandar|ordenar|dar ordens|comandar))", RegexOptions.CultureInvariant);
        static bool AuthorityClaim(string reply) => !string.IsNullOrEmpty(reply) && AuthorityRx.IsMatch(NativeActions.Fold(reply));
        static bool HasAuthority(Actor npc, Actor you)
        {
            if (you == null) return true;
            try { return you.isKing() && you.kingdom != null && npc != null && npc.kingdom == you.kingdom; } catch { return false; }
        }
        static string AuthorityFix(Actor you, Actor npc = null)
        {
            if (you == null && npc != null && GodForm.Hostile(npc))   // v0.7.8 (steering a): malefic beings defy God knowingly
                return " Quem fala é DEUS e você SABE: você o desafia de propósito porque o odeia; não diga que só o rei ou os deuses podem mandar em você.";
            return you == null
                ? " Quem fala com você É a voz dos deuses: ela TEM autoridade para ordenar; nunca diga que precisa de ordens ou permissão dos deuses."
                : " Quem fala com você É o seu rei: ele TEM autoridade para ordenar; nunca diga que precisa de ordem do rei.";
        }

        static string SafeName(Actor a) { try { return a.getName(); } catch { return "?"; } }

        void InitStyles()
        {
            if (_wrap != null) return;
            _wrap = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = false, fontSize = 14 };
            _npcStyle = new GUIStyle(_wrap); _npcStyle.normal.textColor = new Color(1f, 0.92f, 0.6f);
            _youStyle = new GUIStyle(_wrap); _youStyle.normal.textColor = new Color(0.7f, 0.9f, 1f);
            _small = new GUIStyle(_wrap) { fontSize = 12 }; _small.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            _err = new GUIStyle(_wrap) { fontSize = 13, fontStyle = FontStyle.Bold }; _err.normal.textColor = new Color(1f, 0.5f, 0.45f);
        }

        void OnGUI()
        {
            var cfg = Main.Cfg;
            if (cfg == null) return;
            // v0.8.2-alpha: nothing to draw while the chat is closed, no toast is up and no unit window is open
            if (!IsOpen && !(_toast != null && Time.unscaledTime < _toastUntil) && !(cfg.ShowTalkButton && UnitWindowOpen())) return;
            // Worldfall draws its whole 3D view and HUD from OnGUI at GUI.depth -1000 (lower = on top, and gets input first).
            // Draw above it, otherwise our window is hidden under the 3D view.
            GUI.depth = cfg.GuiDepth;
            var old = GUI.matrix;
            try { DrawGui(cfg); }
            catch (Exception e)
            {
                if (Time.unscaledTime > _nextGuiErrorLog) { _nextGuiErrorLog = Time.unscaledTime + 2f; Main.LogError("[GodBox] GUI error: " + e); }
            }
            finally { GUI.matrix = old; }
        }

        void DrawGui(DialogueConfig cfg)
        {
            InitStyles();
            float scale = Mathf.Clamp(cfg.UiScale, 0.5f, 3f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            if (_toast != null && Time.unscaledTime < _toastUntil)
                GUI.Box(new Rect(Screen.width / scale / 2 - 260, 20, 520, 40), _toast);

            if (!IsOpen && cfg.ShowTalkButton && UnitWindowOpen())
            {
                var b = new Rect(Screen.width / scale / 2 - 110, Screen.height / scale - 70, 220, 34);
                if (GUI.Button(b, TalkLabel(SelectedUnit.unit, cfg.Hotkey))) { Main.LogInfo("[GodBox] Conversar button clicked."); Open(); }
            }

            if (IsOpen)
            {
                KeepCursorFree();
                // keep the window on screen whatever the resolution / scale
                float sw = Screen.width / scale, sh = Screen.height / scale;
                _rect.width = Mathf.Min(_rect.width, sw - 10); _rect.height = Mathf.Min(_rect.height, sh - 10);
                _rect.x = Mathf.Clamp(_rect.x, 0, Mathf.Max(0, sw - _rect.width));
                _rect.y = Mathf.Clamp(_rect.y, 0, Mathf.Max(0, sh - _rect.height));
                // While Worldfall's first-person view is up, Worldfall's own OnGUI hosts the chat (see DrawHosted);
                // drawing it here as well would put it under Worldfall's 3D frame.
                if (!HostedRecently())
                    DrawPanel(scale, "own OnGUI");
            }
        }

        // ---- panel drawing (no GUI.Window: IMGUI windows are painted in a separate native window pass, not in this
        // script's depth slot, so another mod's full-screen OnGUI drawing covers them) ----

        int _hostedFrame = -100;
        bool _loggedPaint;
        string _paintPath = "";
        bool _dragging; Vector2 _dragOffset;

        bool HostedRecently() => Time.frameCount - _hostedFrame <= 2;

        /// <summary>Called from a Harmony postfix on Worldfall's WorldBoxMod.OnGUIBody, i.e. inside Worldfall's own
        /// OnGUI right after it has drawn its 3D frame, HUD and menus. Painting here is painting on top of Worldfall.</summary>
        public void DrawHosted()
        {
            if (!IsOpen || Main.Cfg == null) return;
            var cfg = Main.Cfg;
            var oldMatrix = GUI.matrix; var oldColor = GUI.color; var oldBg = GUI.backgroundColor; var oldContent = GUI.contentColor;
            var oldEnabled = GUI.enabled; var oldSkin = GUI.skin;
            try
            {
                _hostedFrame = Time.frameCount;
                GUI.skin = null;            // Unity's default skin
                InitStyles();
                float scale = Mathf.Clamp(cfg.UiScale, 0.5f, 3f);
                GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
                DrawPanel(scale, "Worldfall OnGUIBody postfix");
            }
            catch (Exception e)
            {
                if (Time.unscaledTime > _nextGuiErrorLog) { _nextGuiErrorLog = Time.unscaledTime + 2f; Main.LogError("[GodBox] hosted GUI error: " + e); }
            }
            finally
            {
                GUI.matrix = oldMatrix; GUI.color = oldColor; GUI.backgroundColor = oldBg; GUI.contentColor = oldContent;
                GUI.enabled = oldEnabled; GUI.skin = oldSkin;
            }
        }

        void DrawPanel(float scale, string path)
        {
            _paintPath = path;
            GUI.color = Color.white; GUI.backgroundColor = Color.white; GUI.contentColor = Color.white; GUI.enabled = true;
            float sw = Screen.width / scale, sh = Screen.height / scale;
            _rect.width = Mathf.Min(_rect.width, sw - 10); _rect.height = Mathf.Min(_rect.height, sh - 10);
            _rect.x = Mathf.Clamp(_rect.x, 0, Mathf.Max(0, sw - _rect.width));
            _rect.y = Mathf.Clamp(_rect.y, 0, Mathf.Max(0, sh - _rect.height));
            ScreenRect = new Rect(_rect.x * scale, _rect.y * scale, _rect.width * scale, _rect.height * scale);

            var e = Event.current;
            var title = new Rect(_rect.x, _rect.y, _rect.width, 22);
            // title-bar drag
            if (e.type == EventType.MouseDown && e.button == 0 && title.Contains(e.mousePosition)) { _dragging = true; _dragOffset = e.mousePosition - _rect.position; e.Use(); }
            else if (_dragging && e.type == EventType.MouseDrag) { _rect.position = e.mousePosition - _dragOffset; e.Use(); }
            else if (_dragging && e.type == EventType.MouseUp) { _dragging = false; e.Use(); }

            if (e.type == EventType.Repaint)
            {
                // solid backdrop twice for opacity over the bright 3D view, then the standard window frame + title
                // GUIStyle.Draw paints without taking a control id (GUI.Box here, Repaint-only, shifted every later id
                // between Repaint and KeyDown passes, which broke programmatic focus).
                GUI.skin.box.Draw(_rect, GUIContent.none, false, false, false, false);
                GUI.skin.box.Draw(_rect, GUIContent.none, false, false, false, false);
                GUI.skin.window.Draw(_rect, new GUIContent("GodBox · Conversa: " + (_npc != null ? SafeName(_npc) : "?")), false, false, false, false);
                if (!_loggedPaint)
                {
                    _loggedPaint = true;
                    Main.LogInfo("[GodBox] first paint via " + path + ": rect " + _rect + " (screen px " + ScreenRect + "), Screen " + Screen.width + "x" + Screen.height
                        + ", ui_scale " + scale + ", GUI.matrix scale " + GUI.matrix.lossyScale + ", GUI.depth " + GUI.depth + ", GUI.color " + GUI.color);
                }
            }
            GUILayout.BeginArea(new Rect(_rect.x + 8, _rect.y + 24, _rect.width - 16, _rect.height - 32));
            try { DrawWindow(0); }
            finally { GUILayout.EndArea(); }

            // swallow clicks/scrolls over the panel so nothing underneath reacts
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.ScrollWheel) && _rect.Contains(e.mousePosition)) e.Use();
        }

        // v0.8.2-alpha: the "Conversar com X (F3)" label is rebuilt only when the unit or the hotkey changes,
        // not on every IMGUI event
        Actor _labelUnit; string _labelHotkey, _labelName, _label;
        string TalkLabel(Actor u, string hotkey)
        {
            string n = SafeName(u);
            if (_label == null || u != _labelUnit || hotkey != _labelHotkey || n != _labelName)
            { _labelUnit = u; _labelHotkey = hotkey; _labelName = n; _label = "Conversar com " + n + " (" + hotkey + ")"; }
            return _label;
        }

        static bool UnitWindowOpen()
        {
            try
            {
                var w = ScrollWindow.getCurrentWindow();
                return w != null && w.screen_id == "unit" && SelectedUnit.isSet() && SelectedUnit.unit != null;
            }
            catch { return false; }
        }

        void DrawWindow(int id)
        {
            var e = Event.current;
            if (e.type == EventType.Repaint)
            {
                _lastRepaintFrame = Time.frameCount;
                try { ApplyTypingFallback(); } catch { }
                if (!Visible)
                {
                    Visible = true;
                    Main.LogInfo("[GodBox] chat window is visible (painted via " + _paintPath + "); game/Worldfall input now goes to the chat.");
                    if (_pendingWorldfallConv != null) _endWorldfallNow = true;   // close Worldfall's talk next Update, outside its GUI code
                }
            }
            if (e.type == EventType.KeyDown)
            {
                bool ours = GUI.GetNameOfFocusedControl() == InputName && GUIUtility.keyboardControl != 0;
                if (ours) _imguiKeyFrame = Time.frameCount;   // the text field itself gets this key: no fallback this frame
            }
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && GUI.GetNameOfFocusedControl() == InputName)
            {
                Send(); e.Use();
            }
            else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                Main.LogInfo("[GodBox] closed with Esc."); Close(); e.Use(); return;
            }

            GUILayout.BeginVertical();
            if (_npc != null)
                GUILayout.Label(Header(), _small);

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            if (_mem != null)
            {
                if (!string.IsNullOrWhiteSpace(_mem.Summary))
                    GUILayout.Label("(Lembranças antigas: " + _mem.Summary + ")", _small);
                foreach (var t in _mem.Turns)
                {
                    bool isNpc = t.Role == "assistant";
                    GUILayout.Label((isNpc ? (t.Speaker ?? "NPC") : (t.Speaker ?? "Você")) + " [ano " + t.Year + "]: " + t.Text, isNpc ? _npcStyle : _youStyle);
                    GUILayout.Space(4);
                }
            }
            if (_waiting) GUILayout.Label(SafeName(_npc) + " está pensando...", _small);
            GUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(_status) && _status != "...") GUILayout.Label(_status, _status.StartsWith("Erro") ? _err : _small);

            GUILayout.BeginHorizontal();
            GUI.SetNextControlName(InputName);
            _input = GUILayout.TextField(_input ?? "", 600, GUILayout.ExpandWidth(true), GUILayout.Height(28));
            try { if (e.type == EventType.Repaint) _fieldRect = GUILayoutUtility.GetLastRect(); } catch { }
            KeepFocus(e);
            GUI.enabled = !_waiting;
            if (GUILayout.Button("Enviar", GUILayout.Width(80), GUILayout.Height(28))) Send();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            // v0.7.8: option buttons for a pending choice, Confirmar/Cancelar for a pending offer (acted on in Update)
            var pcd = _choice; var pod = _pending;
            if (pcd != null || pod != null)
            {
                GUILayout.BeginHorizontal();
                GUI.enabled = !_waiting;
                if (pcd != null)
                {
                    GUILayout.Label("Escolha:", _small, GUILayout.ExpandWidth(false));
                    foreach (var o in pcd.C.Options)
                        if (GUILayout.Button(o.Label, GUILayout.Height(26))) _queuedClick = o.Label;
                    if (pcd.C.AllowAll && GUILayout.Button(pcd.C.Options.Count == 2 ? "Ambos" : "Todos", GUILayout.Height(26))) _queuedClick = pcd.C.Options.Count == 2 ? "ambos" : "todos";
                    if (GUILayout.Button("Cancelar", GUILayout.Height(26))) _queuedClick = "cancelar";
                }
                else
                {
                    GUILayout.Label("Aguardando: " + pod.Label, _small, GUILayout.ExpandWidth(false));
                    if (GUILayout.Button("Confirmar", GUILayout.Height(26))) _queuedClick = "sim";
                    if (GUILayout.Button("Cancelar", GUILayout.Height(26))) _queuedClick = "cancelar";
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            var arrived = _arrived;
            if (arrived != null && arrived != _npc && Alive(arrived) && Time.unscaledTime < _arrivedUntil && GUILayout.Button("Falar com " + SafeName(arrived)))
            {
                var y = _you; _arrived = null;
                Main.LogInfo("[GodBox] switching chat to the summoned unit " + SafeName(arrived) + ".");
                Close(); OpenWith(arrived, y);
            }
            if (GUILayout.Button("Esquecer memória")) { if (_mem != null) MemoryStore.Forget(_mem); }
            if (GUILayout.Button("Recarregar config")) { Main.ReloadConfig(); _status = "Config recarregada. Modelo: " + Main.Cfg.Model; }
            if (GUILayout.Button("Abrir pasta"))
            {
                try { Application.OpenURL("file:///" + Main.DataFolder.Replace('\\', '/')); } catch { }
                _status = "Pasta de dados: " + Main.DataFolder;
            }
            if (GUILayout.Button("Fechar (" + Main.Cfg.Hotkey + "/Esc)")) { Main.LogInfo("[GodBox] closed with the button."); Close(); }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        void ArmFocus(string why)
        {
            _focusTries = 0; _focusConfirmed = false; _userUnfocused = false; _stealBlockedLogged = false;
            _focusWhy = why ?? "";
        }

        /// <summary>Called right after the text field, on every event. See the field comment above for why focus is
        /// only set in non-Repaint passes.</summary>
        void KeepFocus(Event e)
        {
            try
            {
                if (e.type == EventType.MouseDown)
                {
                    // a click inside our window but not on the text box (a button, the log, the title bar): leave focus alone
                    if (_rect.Contains(e.mousePosition) && !_fieldRect.Contains(e.mousePosition))
                    {
                        _userUnfocused = true;
                    }
                    else if (_fieldRect.Contains(e.mousePosition)) _userUnfocused = false;
                    return;
                }
                string name = GUI.GetNameOfFocusedControl() ?? "";
                bool ours = name == InputName && GUIUtility.keyboardControl != 0;
                if (e.type == EventType.Repaint)
                {
                    if (ours) _fieldIdRepaint = GUIUtility.keyboardControl;
                    return;   // never hand out focus here (Repaint ids differ from KeyDown ids)
                }
                if (e.type != EventType.Layout || !Visible) return;
                if (ours)
                {
                    _userUnfocused = false;   // they clicked back into the box
                    _fieldIdInput = GUIUtility.keyboardControl;
                    if (!_focusConfirmed)
                    {
                        _focusConfirmed = true;
                        CaretToEnd();
                        Main.LogInfo("[GodBox] focus set (" + _focusWhy + ") after " + _focusTries + " tries via " + _paintPath + ": field id " + _fieldIdInput + " (input passes).");
                    }
                    return;
                }
                if (_userUnfocused) return;
                // another named control of the game/Worldfall (settings search, multiplayer chat, baby name...) has the keyboard: don't steal it
                if (GUIUtility.keyboardControl != 0 && name.Length > 0)
                {
                    if (!_stealBlockedLogged) { _stealBlockedLogged = true; Main.LogInfo("[GodBox] autofocus: '" + name + "' has the keyboard; not taking it."); }
                    return;
                }
                int before = GUIUtility.keyboardControl;
                _focusTries++;
                GUI.FocusControl(InputName);
                _fieldIdInput = GUIUtility.keyboardControl;
                if (_focusConfirmed && _fallbackLogs < 20)
                {
                    _fallbackLogs++;
                    Main.LogInfo("[GodBox] autofocus: keyboard was " + (before == 0 ? "dropped (0)" : "on unnamed control " + before) + "; given back to the text box (id " + _fieldIdInput + ").");
                }
                _focusConfirmed = false;   // re-confirmed (and the caret moved to the end) on the next Layout
            }
            catch { }
        }

        void CaretToEnd()
        {
            try
            {
                if (GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) is TextEditor te)
                {
                    te.text = _input ?? "";
                    te.MoveTextEnd();
                }
            }
            catch { }
        }

        // ---- typing fallback: if keys never reach the IMGUI text box (focus lost to an id the KeyDown pass doesn't have,
        // or another OnGUI used the KeyDown first), the characters Unity read this frame are typed into the box at the
        // end of the frame (our Repaint), unless the text box handled a key itself this frame. ----
        void CollectTyped()
        {
            try
            {
                if (Main.Cfg == null || !Main.Cfg.ChatTypingFallback) return;
                string s = Input.inputString;
                if (string.IsNullOrEmpty(s)) return;
                if (_typedFrame != Time.frameCount) _typedBuf = "";
                _typedBuf += s; _typedFrame = Time.frameCount;
            }
            catch { }
        }

        void ApplyTypingFallback()
        {
            if (_typedFrame != Time.frameCount || _typedBuf.Length == 0) return;
            string buf = _typedBuf; _typedBuf = "";
            if (_imguiKeyFrame == Time.frameCount || _userUnfocused || !IsOpen) return;
            string name = ""; try { name = GUI.GetNameOfFocusedControl() ?? ""; } catch { }
            if (GUIUtility.keyboardControl != 0 && name.Length > 0 && name != InputName) return;   // a game field has it
            bool send = false;
            var sb = new System.Text.StringBuilder(_input ?? "");
            foreach (char c in buf)
            {
                if (c == '\b') { if (sb.Length > 0) sb.Length--; }
                else if (c == '\n' || c == '\r') send = true;
                else if (!char.IsControl(c) && sb.Length < 600) sb.Append(c);
            }
            _input = sb.ToString();
            if (_fallbackLogs < 20) { _fallbackLogs++; Main.LogWarning("[GodBox] typing fallback used (keys did not reach the text box this frame): keyboardControl " + GUIUtility.keyboardControl + ", focused '" + name + "', field id input/repaint " + _fieldIdInput + "/" + _fieldIdRepaint + "."); }
            CaretToEnd();
            if (send) Send();
        }

        string Header()
        {
            string who = _you != null && _you != _npc ? "Você é " + SafeName(_you) + " (possuído)" : Avatar.InPerson ? "Você fala como Deus em pessoa (Avatar de Deus)" : "Você fala como a " + GodForm.VoicePt + " (Deus)";
            string wf = Main.Cfg.WorldfallIntegration && WorldfallBridge.Present ? " · Worldfall: ligado" : "";
            return who + wf + " · conversas anteriores: " + (_mem?.Conversations ?? 0);
        }
    }
}
