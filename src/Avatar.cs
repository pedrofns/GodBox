using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using NeoModLoader.General;
using UnityEngine;

namespace WorldfallAIChat
{
    /// <summary>v0.7.9 "Avatar de Deus / Avatar of God": a special angel (asset "angle") with the energy skin
    /// (subspecies trait mutation_skin_light_orb "Orbe de Luz" since v0.8.1, on its OWN new subspecies so other angels keep their look).
    /// Button in the mod's OWN power-bar tab "GodBox" (NML TabManager.CreateTab; not in the vanilla creatures tab).
    /// Dropping it possesses it (Worldfall then enters first person). Leaving control (G, V, Esc, anything)
    /// removes it quietly (no death chronicle) and leaves a rumour in the nearest city.
    /// While it is controlled, the chat speaks as God in person (same powers as the god voice).</summary>
    /// <summary>v0.8.1: one Avatar only, the "Avatar de Deus" (angel with the Light Orb body). It gets ALL the game's spells
    /// (real SpellAssets added to the unit, castable through Worldfall's Tab &gt; Magic + X), the real "immortal" trait and
    /// the real "invincible" status kept on (0.51 has no "invulnerable" trait). God is always divine.</summary>
    public static class Avatar
    {
        public const string PowerId = "avatar_of_god";
        const string Flag = "wfaic_avatar";
        static float _nextBoost, _nextTraitCheck;
        static string _formWorld;

        public static Actor Current;
        public static long Id = -1;
        static bool _registered, _controlled, _fpSeen;
        static int _sweepsLeft;
        static float _nextFullSweep, _spawnAt, _fpOffSince = -1f, _lostSince = -1f, _nextImmortal, _nextAggro, _nextSweep, _nextRegTry;
        static int _regTries;

        // ---------------- registration (lazy, from Main.Update) ----------------
        public const string TabId = "WorldfallAIChat";
        /// <summary>v0.7.9: our own power-bar tab (NML TabManager.CreateTab), with groups so future mod buttons fit.</summary>
        public static PowersTab Tab;
        public static readonly List<string> TabGroups = new List<string> { "avatar", "religion", "more" };

        public static void TryRegister()
        {
            if (_registered || _regTries > 40 || Time.realtimeSinceStartup < _nextRegTry) return;
            _nextRegTry = Time.realtimeSinceStartup + 3f;
            try
            {
                if (AssetManager.powers == null || AssetManager.powers.get("angle") == null) return;
                if (CanvasMain.instance == null || PowerButtonCreator.GetTab(PowerTabNames.Creatures) == null) return;   // UI not built yet
                _regTries++;
                foreach (var (pid, act) in new (string, PowerActionWithID)[] { (PowerId, DropAngel) })
                {
                    if (AssetManager.powers.get(pid) != null) continue;
                    AssetManager.powers.add(new GodPower
                    {
                        id = pid,
                        name = pid,
                        type = PowerActionType.PowerSpecial,
                        rank = PowerRank.Rank0_free,
                        unselect_when_window = true,
                        set_used_camera_drag_on_long_move = true,
                        show_unit_stats_overview = false,
                        click_special_action = act,
                    });
                }
                SetTexts();
                foreach (var lang in new[] { "en", "pt" })
                {
                    LM.Add(lang, "tab_worldfall_ai_chat", "GodBox");
                    LM.Add(lang, "tab_worldfall_ai_chat_description", lang == "pt" ? "GodBox: seus poderes de Deus dentro do mundo (integrado ao Worldfall)" : "GodBox: your god powers inside the world (Worldfall integration)");
                }
                LM.ApplyLocale();

                // icons are the game's own sprites, loaded at runtime (nothing copied into the mod).
                // v0.8.1: tab = actor trait "scar_of_divinity" (Cicatriz da Divindade) icon; Avatar de Deus button = subspecies
                // trait "mutation_skin_light_orb" (Orbe de Luz) icon. Fallbacks: energy skin icon, then the mod's icon.png.
                Sprite orbIcon = GameIconAny("Avatar de Deus button",
                    ("mutation_skin_light_orb icon", () => AssetManager.subspecies_traits.get(AngelSkin)?.path_icon),
                    ("mutation_skin_energy icon", () => AssetManager.subspecies_traits.get("mutation_skin_energy")?.path_icon));
                string orbWhat = IconWhat;
                Sprite tabIcon = GameIconAny("GodBox tab",
                    ("scar_of_divinity icon", () => AssetManager.traits.get("scar_of_divinity")?.path_icon),
                    ("scar_of_divinity default path", () => "ui/Icons/actor_traits/iconDivineScar"),
                    ("mutation_skin_light_orb icon", () => AssetManager.subspecies_traits.get(AngelSkin)?.path_icon));
                if (Tab == null)
                {
                    Tab = NeoModLoader.General.UI.Tab.TabManager.CreateTab(TabId, "tab_worldfall_ai_chat", "tab_worldfall_ai_chat_description", tabIcon);
                    NeoModLoader.General.UI.Tab.PowersTabExtension.SetLayout(Tab, TabGroups);
                    Main.LogInfo("[GodBox] power-bar tab \"GodBox\" created (id " + TabId + ", icon " + IconWhat + ").");
                }
                var angelIcon = orbIcon;
                Main.LogInfo("[GodBox] Avatar de Deus icon: " + orbWhat + ".");
                var btn = PowerButtonCreator.CreateGodPowerButton(PowerId, angelIcon);
                NeoModLoader.General.UI.Tab.PowersTabExtension.AddPowerButton(Tab, "avatar", btn);
                NeoModLoader.General.UI.Tab.PowersTabExtension.UpdateLayout(Tab);
                try { GodFaith.AddButton(Tab); } catch (Exception e) { Main.LogWarning("[GodBox] Religião de Deus button not added: " + e.Message); }
                NeoModLoader.General.UI.Tab.PowersTabExtension.UpdateLayout(Tab);
                _registered = true;
                Main.LogInfo("[GodBox] Avatar de Deus button added to the GodBox tab (God is always divine).");
            }
            catch (Exception e) { Main.LogWarning("[GodBox] GodBox tab / Avatar button not added (try " + _regTries + "): " + e); }
        }

        /// <summary>Button name/tooltip.</summary>
        static void SetTexts()
        {
            LM.Add("en", PowerId, "Avatar of God");
            LM.Add("en", PowerId + "_description", "Walk among your creatures in person: an angel of light (Light Orb body) you control, with all spells.");
            LM.Add("pt", PowerId, "Avatar de Deus");
            LM.Add("pt", PowerId + "_description", "Caminhe entre suas criaturas em pessoa: um anjo de luz (corpo de Orbe de Luz) controlado por você, com todas as magias.");
        }

        static string IconWhat = "?";
        /// <summary>A game sprite by its asset path (runtime), else the mod's icon.png (logged).</summary>
        public const string AngelSkin = "mutation_skin_light_orb";

        /// <summary>v0.8.1: first game sprite found among the candidates (logged), else the mod's icon.png.</summary>
        public static Sprite GameIconAny(string forWhat, params (string what, Func<string> path)[] cands)
        {
            var tried = new List<string>();
            foreach (var c in cands)
            {
                string p = null;
                try { p = c.path(); var sp = string.IsNullOrEmpty(p) ? null : SpriteTextureLoader.getSprite(p); if (sp != null) { IconWhat = c.what + " (" + p + ")"; Main.LogInfo("[GodBox] icon for " + forWhat + ": " + IconWhat + (tried.Count > 0 ? " [not found before it: " + string.Join(", ", tried) + "]" : "") + "."); return sp; } } catch { }
                tried.Add(c.what + "=" + (p ?? "null"));
            }
            Main.LogWarning("[GodBox] icon for " + forWhat + ": none of the game icons found (" + string.Join(", ", tried) + ").");
            return GameIcon(() => null, forWhat);
        }

        public static Sprite GameIcon(Func<string> path, string what)
        {
            string p = null;
            try { p = path(); var sp = string.IsNullOrEmpty(p) ? null : SpriteTextureLoader.getSprite(p); if (sp != null) { IconWhat = what + " (" + p + ")"; return sp; } } catch { }
            Sprite own = null;
            try { var f = System.IO.Path.Combine(Main.ModFolder ?? "", "icon.png"); if (System.IO.File.Exists(f)) own = NeoModLoader.utils.SpriteLoadUtils.LoadSingleSprite(f); } catch { }
            IconWhat = "mod icon.png (fallback)";
            Main.LogWarning("[GodBox] game icon for " + what + " not found (path " + (p ?? "null") + "): using the mod's icon.png.");
            return own;
        }

        // ---------------- drop ----------------
        static bool DropAngel(WorldTile tile, string powerId) => Drop(tile);

        /// <summary>Subspecies traits of the game whose spells together cover every SpellAsset (logged if any is missing).</summary>
        static readonly string[] GiftTraits = { "gift_of_fire", "gift_of_thunder", "gift_of_void", "gift_of_air", "gift_of_blood", "gift_of_harmony", "gift_of_water", "gift_of_life", "gift_of_death" };

        static bool Drop(WorldTile tile)
        {
            const string label = "Avatar de Deus";
            try
            {
                if (tile == null) return false;
                if (!WorldfallBridge.Present)
                {   // v0.7.9: without Worldfall there is no first person, so no Avatar (God-voice mode only)
                    ChatWindow.Toast("O " + label + " precisa do Worldfall (primeira pessoa). Sem ele, o GodBox funciona só como voz de Deus.");
                    Main.LogInfo("[GodBox] Avatar not spawned: Worldfall is not installed.");
                    return true;
                }
                if (Alive(Current)) End("replaced by a new Avatar", false);   // only one Avatar at a time
                const string assetId = "angle";
                var asset = AssetManager.actor_library.get(assetId);
                if (asset == null) { ChatWindow.Toast("O jogo não tem a criatura \"" + assetId + "\"."); Main.LogWarning("[GodBox] actor asset " + assetId + " missing."); return false; }
                Subspecies sub = null;
                try
                {
                    sub = World.world.subspecies.newSpecies(asset, tile, false);   // its own subspecies: other angels keep their look and traits
                    {   // v0.8.1: the angel avatar's body is the game's "Orbe de Luz" skin (subspecies trait mutation_skin_light_orb),
                        // on its own subspecies; falls back to the energy skin if this game build lacks it
                        string skin = AssetManager.subspecies_traits.get(AngelSkin) != null ? AngelSkin : "mutation_skin_energy";
                        bool ok = false; try { ok = sub.addTrait(skin, true) || sub.hasTrait(skin); } catch { }
                        if (!ok && skin != "mutation_skin_energy") { skin = "mutation_skin_energy"; try { ok = sub.addTrait(skin, true); } catch { } }
                        Main.LogInfo("[GodBox] Avatar de Deus body skin: " + skin + " (added=" + ok + ").");
                    }
                    var added = new List<string>();
                    foreach (var g in GiftTraits) { try { if (AssetManager.subspecies_traits.get(g) != null && (sub.hasTrait(g) || sub.addTrait(g, true))) added.Add(g); } catch { } }
                    Main.LogInfo("[GodBox] " + label + " subspecies gift traits (real magic): " + string.Join(", ", added));
                }
                catch (Exception e) { Main.LogWarning("[GodBox] Avatar subspecies: " + e.Message); }
                var a = World.world.units.spawnNewUnit(assetId, tile, true, true, 3f, sub, false, true);
                if (a == null) { ChatWindow.Toast("O Avatar não pôde descer aqui."); return false; }
                try { a.clearSprites(); } catch { }
                try { a.data.addFlag(Flag); } catch { }
                try { a.setName(PromptBuilder.LanguagePortuguese(Main.Cfg) ? label : "Avatar of God"); } catch { }
                Current = a; Id = a.getID();
                _controlled = false; _fpSeen = false; _fpOffSince = -1f; _lostSince = -1f; _spawnAt = Time.unscaledTime;
                if (Main.Cfg.AvatarImmortal) { try { a.addTrait("immortal"); a.addStatusEffect("invincible", 5f); } catch { } }
                Powers(a, true);
                if (Main.Cfg.AvatarStar) { try { if (!a.isFavorite()) a.switchFavorite(); } catch { } }
                try { EffectsLibrary.spawn("fx_spawn", tile); } catch { }
                try { PowerButtonSelector.instance?.unselectAll(); } catch { }
                Main.RunOnMainThread(() =>
                {
                    try { if (Alive(a)) { ControllableUnit.setControllableCreature(a); Main.LogInfo("[GodBox] " + label + " descended and is now controlled."); } }
                    catch (Exception e) { Main.LogWarning("[GodBox] Avatar possession failed: " + e.Message); }
                });
                return true;
            }
            catch (Exception e) { Main.LogWarning("[GodBox] Avatar drop failed: " + e); return false; }
        }

        // ---------------- per frame ----------------
        public static void Tick()
        {
            TryRegister();
            if (World.world == null) return;
            var a = Current;
            if (a != null)
            {
                if (!Alive(a)) { Main.LogInfo("[GodBox] Avatar is gone (died or removed)."); Forget(); }
                else
                {
                    float now = Time.unscaledTime;
                    bool ctrl = Controlled(a);
                    if (ctrl) { _controlled = true; _lostSince = -1f; }
                    bool fp = FirstPerson(out bool known);
                    if (fp && ctrl) { _fpSeen = true; _fpOffSince = -1f; }
                    if (Main.Cfg.AvatarImmortal && now >= _nextImmortal) { _nextImmortal = now + 1f; try { a.addStatusEffect("invincible", 5f); } catch { } }
                    if (now >= _nextTraitCheck) { _nextTraitCheck = now + 2f; Powers(a, false); }
                    if (Main.Cfg.AvatarStar) { try { if (!a.isFavorite()) a.switchFavorite(); } catch { } }
                    string why = null;
                    if (_controlled && !ctrl)
                    {   // G / Esc / another unit: wait a moment so Worldfall leaves first person first
                        if (_lostSince < 0) _lostSince = now; else if (now - _lostSince > 0.4f) why = "control released";
                    }
                    else if (ctrl && known && _fpSeen && !fp)
                    {   // V in Worldfall: the view is off while still possessed -> counts as leaving
                        if (_fpOffSince < 0) _fpOffSince = now; else if (now - _fpOffSince > 0.5f) why = "first-person view turned off";
                    }
                    else if (!_controlled && now - _spawnAt > 3f) why = "possession never started";
                    if (why != null) End(why, true);
                    else if (now >= _nextAggro) { _nextAggro = now + 2f; Aggro(a); }
                }
            }
            if (Time.unscaledTime >= _nextSweep)
            {
                _nextSweep = Time.unscaledTime + 5f;
                try { string w = MemoryStore.CurrentWorldKey(); if (w != _formWorld) { _formWorld = w; _sweepsLeft = 3; GodForm.MigrateLegacy(w); } } catch { }   // v0.8.1: old demon form -> divine
                // v0.8.2-alpha: leftover Avatars only appear with a loaded world/save, so the full unit scan runs for
                // ~15 s after a world change and then once a minute (was: every 5 s, always)
                if (_sweepsLeft > 0 || Time.unscaledTime >= _nextFullSweep) { if (_sweepsLeft > 0) _sweepsLeft--; _nextFullSweep = Time.unscaledTime + 60f; Sweep(); }
            }
        }

        /// <summary>Quiet removal: no death chronicle, no favourite-death log.</summary>
        public static void End(string why, bool rumour)
        {
            var a = Current;
            Forget();
            if (a == null) return;
            WorldTile at = null; try { at = a.current_tile; } catch { }
            try { if (a.isFavorite()) a.switchFavorite(); } catch { }
            try { if (ControllableUnit.isControllingUnit(a)) ControllableUnit.clear(false); } catch { }
            try { if (a.isAlive()) a.dieAndDestroy(AttackType.None); } catch (Exception e) { Main.LogWarning("[GodBox] Avatar removal: " + e.Message); }
            Main.LogInfo("[GodBox] Avatar of God returned to the heavens (" + why + ").");
            if (!rumour) return;
            bool pt = PromptBuilder.LanguagePortuguese(Main.Cfg);
            try
            {
                var city = NearestCity(at);
                if (city != null) Rumors.Add(city, pt ? "Deus caminhou entre nós e voltou aos céus." : "God walked among us and went back to the heavens.");
            }
            catch { }
            try { ChatWindow.Toast(pt ? "O Avatar de Deus voltou aos céus." : "The Avatar of God went back to the heavens."); } catch { }
        }

        static void Forget() { Current = null; Id = -1; _controlled = false; _fpSeen = false; _fpOffSince = _lostSince = -1f; }

        /// <summary>Avatars left over from a save or a crash (flagged): removed quietly.</summary>
        static void Sweep()
        {
            try
            {
                List<Actor> stale = null;
                foreach (var u in World.world.units)
                {
                    if (u == null || u == Current) continue;
                    bool f = false; try { f = u.isAlive() && u.data != null && u.data.hasFlag(Flag); } catch { }
                    if (f) (stale ??= new List<Actor>()).Add(u);
                }
                if (stale == null) return;
                foreach (var u in stale)
                {
                    try { if (u.isFavorite()) u.switchFavorite(); if (ControllableUnit.isControllingUnit(u)) ControllableUnit.clear(false); u.dieAndDestroy(AttackType.None); } catch { }
                }
                Main.LogInfo("[GodBox] removed " + stale.Count + " leftover Avatar(s).");
            }
            catch { }
        }

        /// <summary>Nearby malefics (enemies of God) sometimes attack the Avatar.</summary>
        static void Aggro(Actor a)
        {
            try
            {
                var tile = a.current_tile; if (tile == null) return;
                // v0.8.2-alpha: only the map chunks around the Avatar (10 tiles), not every unit in the world
                foreach (var u in Finder.getUnitsFromChunk(tile, 1, 10f))
                {
                    if (u == null || u == a || !u.isAlive()) continue;
                    if ((u.current_position - a.current_position).sqrMagnitude > 100f) continue;
                    if (!Alignment.IsMalefic(u)) continue;
                    if (UnityEngine.Random.value > 0.15f) continue;
                    try { u.addAggro(a); u.startFightingWith(a); } catch { }
                }
            }
            catch { }
        }

        // ---------------- spells / traits (real game mechanics) ----------------
        static bool _loggedSpells;
        /// <summary>Keeps the real "immortal" trait, refreshes the real "spell_boost" status (mana) and makes sure every
        /// SpellAsset is on the unit (the updateStats postfix does the same after each recalc).</summary>
        static void Powers(Actor a, bool spawn)
        {
            if (!Alive(a)) return;
            if (Main.Cfg.AvatarImmortal)
            {
                bool imm = false; try { imm = a.hasTrait("immortal"); } catch { }
                if (!imm) { try { a.addTrait("immortal", true); } catch { } try { imm = a.hasTrait("immortal"); } catch { } Main.LogInfo("[GodBox] Avatar \"immortal\" trait " + (spawn ? "set" : "was missing, re-added") + ": " + imm + "."); }
                else if (spawn) Main.LogInfo("[GodBox] Avatar traits verified: immortal=" + imm + ", invincible status=" + Has(a, "invincible") + " (0.51 has no \"invulnerable\" trait; the \"invincible\" status is refreshed every second).");
            }
            float now = Time.unscaledTime;
            if (spawn || now >= _nextBoost) { _nextBoost = now + 60f; try { a.addStatusEffect("spell_boost", 180f); } catch { } }
            int n = AddAllSpells(a);
            if (spawn || n > 0 || !_loggedSpells)
            {
                _loggedSpells = true;
                Main.LogInfo("[GodBox] Avatar spells (" + SpellIds(a).Count + "/" + (AssetManager.spells?.list?.Count ?? 0) + "): " + string.Join(", ", SpellIds(a)) + "; mana " + Safe(() => a.getMaxMana()) + ".");
            }
        }
        static bool Has(Actor a, string st) { try { return a.hasStatus(st); } catch { return false; } }
        static string Safe(Func<int> f) { try { return f().ToString(); } catch { return "?"; } }

        static FieldInfo _spellsField;
        static SpellHolder Holder(Actor a)
        {
            try { _spellsField ??= AccessTools.Field(typeof(Actor), "_spells"); return _spellsField?.GetValue(a) as SpellHolder; } catch { return null; }
        }
        /// <summary>Adds every SpellAsset of the game missing from the unit's spell list. Returns how many were added.</summary>
        public static int AddAllSpells(Actor a)
        {
            var h = Holder(a); if (h == null) return 0;
            int n = 0;
            try { foreach (var sp in AssetManager.spells.list) { if (sp == null || h.hasSpell(sp)) continue; h.addSpell(sp); n++; } } catch { }
            return n;
        }
        public static List<string> SpellIds(Actor a)
        {
            var r = new List<string>();
            try { var h = Holder(a); if (h != null) foreach (var sp in h.getSpellsHashset()) r.Add(sp.id); } catch { }
            return r;
        }

        // ---------------- queries ----------------
        public static bool IsAvatar(Actor a) { try { return a != null && Current != null && a == Current; } catch { return false; } }
        public static bool IsAvatarId(long id) => id >= 0 && id == Id;
        /// <summary>God is speaking in person (the Avatar is alive and controlled).</summary>
        /// <summary>v0.8.2 (B): the chat was opened from the Avatar's body (control can drop while the chat pauses the world).</summary>
        public static bool ChatInPerson;
        public static bool InPerson { get { var a = Current; return Alive(a) && (Controlled(a) || ChatInPerson); } }

        public static string Suffix(bool pt) => InPerson
            ? (pt ? " (em pessoa)" : " (in person)")
            : " (" + (pt ? GodForm.VoicePt : GodForm.VoiceEn) + ")";

        /// <summary>How this listener reacts to God standing in front of it.</summary>
        public static string PromptRule(Actor npc, bool pt)
        {
            if (!InPerson || npc == null) return null;
            Nature n = Nature.Normal; try { n = Alignment.Of(npc); } catch { }
            string gn = Gods.NameFor(npc);
            string god = gn ?? (pt ? "Deus" : "God");
            bool knows = Gods.KnowsGod(npc);   // v0.8.2 (A/B): only followers of God's faith (or holy/malefic beings, or told by him) know who he is
            string body = pt ? "um anjo de pura luz cujo corpo é um ORBE DE LUZ brilhante" : "an angel of pure light whose body is a glowing ORB OF LIGHT";
            string head = pt
                ? (knows ? "DEUS EM PESSOA: quem fala com você NÃO é uma voz: é " + god + " em pessoa (o Avatar de Deus), " + body + ", parado bem na sua frente. Você o VÊ: reaja à presença dele (a luz, o brilho, o espanto de ver Deus) e NÃO o trate como um visitante qualquer. "
                         : "UM SER DE LUZ NA SUA FRENTE: quem fala com você é " + body + ", parado bem na sua frente. Você o VÊ, fica assombrado com a luz dele e NÃO sabe quem ele é: pergunte quem ele é e peça que se revele. NÃO o trate como um visitante qualquer. ")
                : (knows ? "GOD IN PERSON: the speaker is not a voice: it is " + god + " in person (the Avatar of God), " + body + ", right in front of you. You SEE him; react to his presence, never treat him as a random visitor. "
                         : "A BEING OF LIGHT IN FRONT OF YOU: " + body + ". You see him, are awed, and do not know who he is: ask him to reveal himself. ");
            if (n == Nature.Malefic)
                return head + (pt ? "Você é maléfico: ODEIA esse ser, sabe quem ele é e o desafia; pode ameaçá-lo ou atacá-lo." : "You are malefic: you HATE this being, know who he is and defy him; you may threaten or attack him.");
            bool same = false, hasRel = false;
            try { hasRel = npc.hasReligion(); } catch { }
            if (n == Nature.Holy) same = true;
            else if (hasRel && Gods.IsAdherent(npc)) same = true;   // v0.8.2: the Avatar is the god of God's religion only
            if (same && n == Nature.Holy)
                return head + (pt ? "Você é um ser sagrado: REVERENCIA-o, ajoelha-se e obedece de bom grado." : "You are a holy being: you REVERE him, kneel and obey gladly.");
            if (same && Devout(npc))
                return head + (pt ? "Você é devoto: está maravilhado, REVERENCIA-o e obedece com mais boa vontade do que obedeceria a uma voz." : "You are devout: awed, you REVERE him and obey more readily than a mere voice.");
            if (Sceptic(npc))
                return head + (pt ? "Você é desconfiado por natureza: DUVIDA que seja mesmo Deus; pode chamá-lo de falso deus ou impostor e pedir provas (sem atacá-lo)." : "You are sceptical by nature: you DOUBT it is really God; you may call him a false god or impostor and ask for proof (without attacking).");
            return head + (pt ? "Você fica assombrado e reverente, um pouco assustado, e tende a obedecer." : "You are awed and reverent, a little scared, and tend to obey.");
        }

        static bool Devout(Actor a)
        {
            try { return a.hasReligion() && !Sceptic(a); } catch { return false; }
        }
        static readonly string[] ScepticTraits = { "wise", "genius", "paranoid", "ambitious", "greedy", "deceitful", "strong_minded" };
        static readonly string[] AweTraits = { "content", "stupid", "peaceful" };
        static bool Sceptic(Actor a)
        {
            int s = 0;
            try { foreach (var t in ScepticTraits) if (a.hasTrait(t)) s++; foreach (var t in AweTraits) if (a.hasTrait(t)) s--; } catch { }
            bool rel = false; try { rel = a.hasReligion(); } catch { }
            return s > 0 || (!rel && s >= 0);
        }

        // ---------------- helpers ----------------
        static bool Alive(Actor a) { try { return a != null && a.isAlive(); } catch { return false; } }
        static bool Controlled(Actor a) { try { return ControllableUnit.isControllingUnit(a); } catch { return false; } }

        static PropertyInfo _inst, _fp; static bool _fpLooked;
        static bool FirstPerson(out bool known)
        {
            known = false;
            try
            {
                if (!_fpLooked)
                {
                    _fpLooked = true;
                    var t = AccessTools.TypeByName("FirstPerson.WorldBoxMod");
                    _inst = t == null ? null : AccessTools.Property(t, "Instance");
                    _fp = t == null ? null : AccessTools.Property(t, "IsFirstPerson");
                }
                if (_inst == null || _fp == null) return false;
                var m = _inst.GetValue(null);
                if (m == null) return false;
                known = true;
                return (bool)_fp.GetValue(m);
            }
            catch { known = false; return false; }
        }

        static City NearestCity(WorldTile at)
        {
            if (at == null) return null;
            City best = null; float bd = float.MaxValue;
            foreach (var c in World.world.cities)
            {
                try
                {
                    var t = c?.getTile(); if (t == null) continue;
                    float d = Vector2.SqrMagnitude(new Vector2(t.pos.x - at.pos.x, t.pos.y - at.pos.y));
                    if (d < bd) { bd = d; best = c; }
                }
                catch { }
            }
            return best;
        }
    }
}
