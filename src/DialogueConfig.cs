using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace WorldfallAIChat
{
    /// <summary>
    /// User settings, stored in &lt;persistentDataPath&gt;/WorldfallAIChat/config.json (outside the mod folder).
    /// The file is created automatically (from these defaults) on first launch.
    /// The API key is NEVER shipped with the mod: the player pastes it into "api_key".
    /// </summary>
    public class DialogueConfig
    {
        // ---------- Backend (any OpenAI-compatible /chat/completions endpoint) ----------
        [JsonProperty("base_url")] public string BaseUrl = "http://localhost:11434/v1";   // Ollama (local, free); OpenAI: https://api.openai.com/v1
        [JsonProperty("api_key")] public string ApiKey = "";
        [JsonProperty("model")] public string Model = "qwen2.5:7b";   // recommended local model (ollama pull qwen2.5:7b)
        [JsonProperty("summary_model")] public string SummaryModel = ""; // empty = same as "model"
        [JsonProperty("reasoning_effort")] public string ReasoningEffort = ""; // "" = do not send (e.g. "none" for OpenAI reasoning models)
        [JsonProperty("max_tokens")] public int MaxTokens = 400;
        [JsonProperty("max_tokens_field")] public string MaxTokensField = "max_tokens"; // or "max_completion_tokens" (newer OpenAI models)
        [JsonProperty("temperature")] public float Temperature = -1f; // < 0 = do not send
        [JsonProperty("timeout_seconds")] public int TimeoutSeconds = 60;
        [JsonProperty("extra_headers")] public System.Collections.Generic.Dictionary<string, string> ExtraHeaders =
            new System.Collections.Generic.Dictionary<string, string>();

        // ---------- Language ----------
        // Default: Brazilian Portuguese. "auto" (or empty) = answer in the same language the player writes in.
        // Any other value forces that language, e.g. "English", "Español".
        [JsonProperty("reply_language")] public string ReplyLanguage = "português do Brasil";

        // ---------- Memory ----------
        [JsonProperty("history_turns_in_prompt")] public int HistoryTurnsInPrompt = 12; // messages (player+npc) sent verbatim
        [JsonProperty("summarize_after_messages")] public int SummarizeAfterMessages = 24; // when stored messages exceed this, older ones are summarized
        [JsonProperty("keep_recent_messages")] public int KeepRecentMessages = 10;
        [JsonProperty("max_summary_words")] public int MaxSummaryWords = 250;

        // ---------- Controls / UI ----------
        [JsonProperty("hotkey")] public string Hotkey = "F3";
        [JsonProperty("show_talk_button")] public bool ShowTalkButton = true;
        [JsonProperty("gui_depth")] public int GuiDepth = -5000; // IMGUI depth; must be below Worldfall's -1000 to draw on top of its 3D view
        [JsonProperty("ui_scale")] public float UiScale = 1f;
        [JsonProperty("chat_typing_fallback")] public bool ChatTypingFallback = true; // v0.7.1: type into the chat box even if IMGUI keys miss it
        [JsonProperty("block_game_input_while_typing")] public bool BlockGameInputWhileTyping = true;
        [JsonProperty("unlock_cursor_while_chatting")] public bool UnlockCursorWhileChatting = true;
        [JsonProperty("hold_units_while_chatting")] public bool HoldUnitsWhileChatting = true; // NPC (and your possessed unit) stand still, facing each other
        [JsonProperty("nearest_npc_radius")] public float NearestNpcRadius = 6f; // tiles, used when possessing a unit and nothing else is selected

        // ---------- Role-play ----------
        [JsonProperty("player_persona_when_not_possessing")] public string GodPersona =
            "a mysterious, powerful voice from the sky (the world's god). The character cannot see the speaker.";
        [JsonProperty("extra_instructions")] public string ExtraInstructions = "";
        [JsonProperty("max_reply_sentences")] public int MaxReplySentences = 4;
        // v5: live FACTOS block (real city/kingdom numbers, re-read every turn) + strict "never invent numbers" rule
        [JsonProperty("facts_block_enabled")] public bool FactsBlockEnabled = true;
        // writes the FACTOS block to the NML log each turn so you can compare it with the game
        [JsonProperty("log_facts_block")] public bool LogFactsBlock = true;

        // ---------- Worldfall ----------
        [JsonProperty("worldfall_integration")] public bool WorldfallIntegration = true;
        // If true, each AI conversation also counts as a "talk" in Worldfall's own regard data (writes Worldfall save data).
        [JsonProperty("worldfall_count_talks")] public bool WorldfallCountTalks = false;
        // Adds an option to Worldfall's E-key talk menu that opens this AI chat with the same unit.
        [JsonProperty("worldfall_menu_option")] public bool WorldfallMenuOption = true;
        [JsonProperty("worldfall_menu_label")] public string WorldfallMenuLabel = "Conversa livre (IA)";
        [JsonProperty("worldfall_menu_position")] public string WorldfallMenuPosition = "first"; // "first" or "before_goodbye"

        // ---------- v6: Worldfall actions from the AI chat ----------
        // The AI chat can trigger the same actions as Worldfall's E-menu (gift, trade, enlist, move to a city...).
        // Worldfall itself runs them (prices, refusals and rolls are Worldfall's). Needs a possessed unit.
        [JsonProperty("npc_actions_enabled")] public bool NpcActionsEnabled = true;
        // true = a first small call (JSON, temperature 0) decides the action, then the reply is written knowing the
        // real result. false = no actions are detected (chat only).
        [JsonProperty("actions_two_pass")] public bool ActionsTwoPass = true;
        [JsonProperty("classifier_model")] public string ClassifierModel = ""; // empty = same as "model"
        // v6: pt-BR for Worldfall's own talk texts at runtime. "auto" = on when reply_language is Portuguese; "true"/"false".

        // ---- v7 ----
        // Pending offers: actions with a cost or commitment (work/quest, enlist, court/propose, child, adopt, moving city,
        // buying/paying/bribing, founding a village...) are only proposed first; they run when your NEXT message confirms.
        [JsonProperty("confirm_commitments")] public bool ConfirmCommitments = true;
        // Destructive actions (end romance, leave city/army, give up the village charge, dismiss guard, sell everything)
        // always need explicit words in your message AND a confirmation.
        [JsonProperty("confirm_destructive")] public bool ConfirmDestructive = true;
        // Native (non-Worldfall) orders the NPC can carry out in the world. Each one can be switched off.
        [JsonProperty("native_actions_enabled")] public bool NativeActionsEnabled = true;
        [JsonProperty("action_rename")] public bool ActionRename = true;          // only the god voice or the NPC's own king
        [JsonProperty("action_follow")] public bool ActionFollow = true;          // follow me / stop following
        [JsonProperty("action_guide")] public bool ActionGuide = true;            // lead me to a person or city
        [JsonProperty("name_lookup")] public bool NameLookup = true;              // v0.7.5: look up names in the player's message (people/cities/kingdoms)
        [JsonProperty("mark_target_star")] public bool MarkTargetStar = true;     // v0.7.4: where_is / guide_to put the favorite star (+ Worldfall ping) on the person
        [JsonProperty("action_where_is")] public bool ActionWhereIs = true;       // where is X? (real direction + Worldfall ping / camera)
        [JsonProperty("action_attack")] public bool ActionAttack = true;          // attack X (about attack_seconds)
        [JsonProperty("action_assign_job")] public bool ActionAssignJob = true;   // change the NPC's city job / make warrior
        [JsonProperty("action_create_village")] public bool ActionCreateVillage = true; // a ruler (or god) orders a new village
        [JsonProperty("follow_seconds")] public float FollowSeconds = 600f;       // real seconds a "follow me" lasts
        [JsonProperty("guide_seconds")] public float GuideSeconds = 120f;
        [JsonProperty("attack_seconds")] public float AttackSeconds = 30f;
        [JsonProperty("job_hold_seconds")] public float JobHoldSeconds = 180f;
        [JsonProperty("village_walk_seconds")] public float VillageWalkSeconds = 420f;   // v0.7.6: game-time cap of the walk; then founds near the colonist (no failure)
        [JsonProperty("village_stuck_seconds")] public float VillageStuckSeconds = 45f;  // v0.7.6: no progress this long -> found at the best zone near the colonist
        [JsonProperty("action_war")] public bool ActionWar = true;                       // v0.7.6: declare_war / make_peace / attack_city (king or god only)
        // God voice: base chance an NPC defies a divine order (traits add/subtract, max 0.7). 0 = always obeys.
        [JsonProperty("god_defiance_base")] public float GodDefianceBase = 0.08f;
        // Refusal when possessing: Worldfall opinion at or below this = treated as an enemy.
        [JsonProperty("enemy_opinion_threshold")] public int EnemyOpinionThreshold = -40;
        // World knowledge in the FACTOS block.
        [JsonProperty("world_knowledge")] public bool WorldKnowledge = true;
        [JsonProperty("nearby_city_chunks")] public int NearbyCityChunks = 6;      // 1 chunk = 16 tiles
        [JsonProperty("world_max_exact_cities")] public int WorldMaxExactCities = 8;
        [JsonProperty("world_max_rumors")] public int WorldMaxRumors = 5;
        [JsonProperty("world_knowledge_max_chars")] public int WorldKnowledgeMaxChars = 1800;
        // v7: pause / god powers / summary
        [JsonProperty("pause_world_during_chat")] public bool PauseWorldDuringChat = true;
        [JsonProperty("action_god_powers")] public bool ActionGodPowers = true;
        [JsonProperty("action_kill")] public bool ActionKill = true;
        [JsonProperty("smite_lightning")] public bool SmiteLightning = true;
        [JsonProperty("action_religion")] public bool ActionReligion = true;
        [JsonProperty("preach_seconds")] public float PreachSeconds = 90f;          // game-running seconds (paused time does not count)
        [JsonProperty("preach_interval_seconds")] public float PreachIntervalSeconds = 6f;
        [JsonProperty("preach_radius")] public float PreachRadius = 12f;            // tiles
        [JsonProperty("preach_chance")] public float PreachChance = 0.35f;          // per nearby unit per sermon
        [JsonProperty("preach_city_share")] public float PreachCityShare = 0.5f;    // share of citizens converted at once for city/kingdom
        [JsonProperty("action_summon")] public bool ActionSummon = true;
        [JsonProperty("summon_seconds")] public float SummonSeconds = 240f;             // give up after this many running seconds
        [JsonProperty("summon_tiles_per_second")] public float SummonTilesPerSecond = 2.5f; // only for the ETA estimate shown
        [JsonProperty("action_messages")] public bool ActionMessages = true;
        [JsonProperty("message_max_seconds")] public float MessageMaxSeconds = 300f;    // after this, the message arrives "by travellers"
        [JsonProperty("notes_per_unit")] public int NotesPerUnit = 6;
        [JsonProperty("rumors_enabled")] public bool RumorsEnabled = true;
        [JsonProperty("malefic_evil_trait")] public bool MaleficEvilTrait = true;  // v0.7.7: units with the "evil" trait count as malefic (enemies of God)
        [JsonProperty("death_chronicle")] public bool DeathChronicle = true;     // v0.7.6: record deaths of notable units (_deaths.json)
        [JsonProperty("avatar_immortal")] public bool AvatarImmortal = true;    // v0.7.9: the Avatar of God cannot be hurt
        [JsonProperty("avatar_star")] public bool AvatarStar = true;            // v0.7.9: the Avatar of God is a favourite (star) while it exists
        [JsonProperty("rumors_per_city")] public int RumorsPerCity = 8;
        [JsonProperty("curse_seconds")] public float CurseSeconds = 600f;
        [JsonProperty("summary_max_chars")] public int SummaryMaxChars = 600;
        /// <summary>v0.8.2-alpha: verbose/diagnostic info lines (classifier, ACTION details, recognition, FACTOS, requests...).
        /// Off by default; warnings and errors are always logged.</summary>
        [JsonProperty("debug_log")] public bool DebugLog = false;
        [JsonProperty("debug_log_prompts")] public bool DebugLogPrompts = false;   // full prompts/requests (needs debug_log)

        // ---------- v0.8.0: local-model sampling (qwen drifted into Chinese mid-reply) ----------
        [JsonProperty("top_p")] public float TopP = -1f;                     // < 0 = auto (0.9 for local qwen/ollama, not sent otherwise)
        [JsonProperty("repeat_penalty")] public float RepeatPenalty = -1f;   // < 0 = auto (1.1 for local qwen/ollama; ignored by backends that do not know it)
        [JsonProperty("local_temperature")] public float LocalTemperature = 0.6f; // used for local qwen/ollama when "temperature" < 0
        [JsonProperty("max_reply_chars")] public int MaxReplyChars = 700;     // longer replies are cut at the last full sentence (0 = off)

        // ---------- v0.8.0: who recognises the possessed unit (renown + titles + Worldfall ranks) ----------
        [JsonProperty("recognition_enabled")] public bool RecognitionEnabled = true;
        // renown -> fame level: >= [0] own city knows the name; [1] city knows name+role, kingdom the name;
        // [2] kingdom knows name+role, other kingdoms the name; [3] world-famous (name, kingdom, title, real deeds)
        [JsonProperty("renown_tiers")] public int[] RenownTiers = { 10, 50, 150, 400 };
        [JsonProperty("king_world_famous_cities")] public int KingWorldFamousCities = 4;  // a king of a kingdom this big is world-famous
        [JsonProperty("notable_kills")] public int NotableKills = 10;                    // this many real kills add one fame level

        [JsonIgnore] public string FilePath;

        [JsonIgnore] KeyCode? _hotkeyParsed; [JsonIgnore] string _hotkeyFor;
        /// <summary>Read every frame: parsed once and cached (re-parsed only if "hotkey" changes).</summary>
        public KeyCode GetHotkey()
        {
            if (_hotkeyParsed.HasValue && ReferenceEquals(_hotkeyFor, Hotkey)) return _hotkeyParsed.Value;
            KeyCode k;
            try { k = (KeyCode)Enum.Parse(typeof(KeyCode), Hotkey, true); } catch { k = KeyCode.F3; }
            _hotkeyFor = Hotkey; _hotkeyParsed = k;
            return k;
        }

        public string GetClassifierModel() => string.IsNullOrWhiteSpace(ClassifierModel) ? Model : ClassifierModel;

        public string GetSummaryModel() => string.IsNullOrWhiteSpace(SummaryModel) ? Model : SummaryModel;

        public static DialogueConfig Load(string folder)
        {
            string path = Path.Combine(folder, "config.json");
            DialogueConfig cfg = null;
            try
            {
                if (File.Exists(path))
                    cfg = JsonConvert.DeserializeObject<DialogueConfig>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Main.LogError("[GodBox] config.json is invalid, using defaults: " + e.Message);
                try { File.Copy(path, path + ".broken", true); } catch { }
            }
            bool existed = cfg != null;
            cfg ??= new DialogueConfig();
            cfg.FilePath = path;
            cfg.Save(); // creates the file on first run / adds new fields after updates (keeps your values)
            return cfg;
        }

        public void Save()
        {
            try { File.WriteAllText(FilePath, JsonConvert.SerializeObject(this, Formatting.Indented)); }
            catch (Exception e) { Main.LogError("[GodBox] could not write config.json: " + e.Message); }
        }
    }
}
