using System;
using System.Collections.Concurrent;
using HarmonyLib;
using NeoModLoader.api;
using UnityEngine;

namespace WorldfallAIChat
{
    /// <summary>NML entry point. NML compiles every .cs in this folder and instantiates this class on its own GameObject.</summary>
    public class Main : BasicMod<Main>
    {
        public static DialogueConfig Cfg;
        public static string ModFolder;
        /// <summary>Per-player data (config with the API key + conversation memory). Lives OUTSIDE the mod folder,
        /// so nothing private is ever uploaded with the mod to the Steam Workshop.
        /// Windows: %USERPROFILE%\AppData\LocalLow\mkarpenko\WorldBox\WorldfallAIChat</summary>
        public static string DataFolder;
        public const string HarmonyId = "pedro.worldfall_ai_chat";
        static readonly ConcurrentQueue<Action> MainThreadQueue = new ConcurrentQueue<Action>();
        ChatWindow _chat;

        /// <summary>v0.8.2-alpha: info lines are diagnostics, written only with "debug_log": true in config.json.
        /// Warnings and errors (LogWarning/LogError) are always written.</summary>
        public new static void LogInfo(string message)
        {
            var c = Cfg;
            if (c == null || c.DebugLog) BasicMod<Main>.LogInfo(message);
        }
        /// <summary>The few load-time lines everyone needs in a bug report (version, backend, Worldfall detection).</summary>
        public static void LogAlways(string message) => BasicMod<Main>.LogInfo(message);

        protected override void OnModLoad()
        {
            ModFolder = GetDeclaration().FolderPath;
            DataFolder = System.IO.Path.Combine(Application.persistentDataPath, "WorldfallAIChat");
            try { System.IO.Directory.CreateDirectory(DataFolder); }
            catch (Exception e) { LogError("[GodBox] cannot create data folder " + DataFolder + ": " + e.Message); }
            MemoryStore.Root = DataFolder;
            Cfg = DialogueConfig.Load(DataFolder);
            _chat = gameObject.AddComponent<ChatWindow>();
            gameObject.AddComponent<GodFaithWindow>();   // v0.7.9 Religião de Deus form

            try
            {
                Patches.Apply(new Harmony(HarmonyId));
            }
            catch (Exception e) { LogWarning("[GodBox] some input patches failed (chat still works): " + e.Message); }
            // NML loads Worldfall first (optional dependency), so its types are already in the AppDomain here.
            WorldfallTalkHook.Apply(new Harmony(HarmonyId + ".worldfall"));
            Deaths.Apply(new Harmony(HarmonyId + ".deaths"));   // v0.7.6 death chronicle

#if S3COND2_WORLDFALL
            LogAlways("[GodBox] compiled with Worldfall present (NML optional dependency symbol S3COND2_WORLDFALL).");
#endif
            if (string.IsNullOrWhiteSpace(Cfg.ApiKey) && !IsLocalUrl(Cfg.BaseUrl))
                LogWarning("[GodBox] api_key is empty but base_url is not local. Paste your API key into: " + Cfg.FilePath);
            LogAlways($"[GodBox] loaded {GetDeclaration().Version}. Model={Cfg.Model} BaseUrl={Cfg.BaseUrl} Hotkey={Cfg.Hotkey} debug_log={Cfg.DebugLog}. Config: {Cfg.FilePath}");
        }

        internal static bool IsLocalUrl(string u)
        {
            u = (u ?? "").ToLowerInvariant();
            return u.Contains("localhost") || u.Contains("127.0.0.1") || u.Contains("0.0.0.0") || u.Contains("[::1]");
        }

        public static void ReloadConfig()
        {
            Cfg = DialogueConfig.Load(DataFolder);
            LlmClient.ResetDown();
            LogAlways("[GodBox] config reloaded. Model=" + Cfg.Model + " BaseUrl=" + Cfg.BaseUrl + " debug_log=" + Cfg.DebugLog + ".");
        }

        /// <summary>Thread-safe: schedule work for Unity's main thread (used by the HTTP worker).</summary>
        public static void RunOnMainThread(Action a) => MainThreadQueue.Enqueue(a);

        int _hookRetries;
        float _nextHookTry = 5f;

        void Update()
        {
            // If Worldfall's assembly was not loaded yet when we loaded, try the menu hook again a few times.
            if (!WorldfallTalkHook.Applied && _hookRetries < 3 && Time.realtimeSinceStartup > _nextHookTry)
            {
                _hookRetries++;
                _nextHookTry = Time.realtimeSinceStartup + 20f;
                if (AccessTools.TypeByName("FirstPerson.Dialogue") != null)
                {
                    WorldfallTalkHook.Apply(new Harmony(HarmonyId + ".worldfall"));
                }
            }

            try { Avatar.Tick();
                try { Promises.Tick(); } catch { } try { GodFaith.Tick(); } catch { } } catch (Exception e) { LogWarning("[GodBox] avatar tick: " + e.Message); }   // v0.7.9 Avatar of God
            try { NativeActions.Tick(); GodPowers.Tick(); Rumors.TickKings(); } catch (Exception e) { LogWarning("[GodBox] native tick: " + e.Message); }

            while (MainThreadQueue.TryDequeue(out var a))
            {
                try { a(); } catch (Exception e) { LogError("[GodBox] " + e); }
            }
        }

        public static int CurrentYear()
        {
            try { return Date.getCurrentYear(); } catch { return 0; }
        }
    }
}
