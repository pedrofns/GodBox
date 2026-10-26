using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace WorldfallAIChat
{
    /// <summary>
    /// v0.7.8: go_to (walk to a city / person / direction with the game's own movement), teleport (god voice only, the
    /// game's teleport effect + spawnOn, like BehTeleportHome), and the kingdomless city raid (a lone holy/monster unit
    /// ordered by God to attack a city walks there and fights its units and buildings itself; no war declaration).
    /// </summary>
    public static partial class NativeActions
    {
        static WorldTile GroundNear(WorldTile c, int radius)
        {
            if (c == null) return null;
            try { if (c.Type.ground && !c.Type.block) return c; } catch { }
            for (int r = 1; r <= radius; r++)
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                        WorldTile t = null; try { t = World.world.GetTile(c.x + dx, c.y + dy); } catch { }
                        try { if (t != null && t.Type.ground && !t.Type.block) return t; } catch { }
                    }
            return null;
        }

        /// <summary>Where "target" is: a city, a person, or a direction from the NPC. Text label in label.</summary>
        static WorldTile Destination(string target, Actor npc, Actor you, out string label, out Actor who, out City city)
        {
            label = null; who = null; city = null;
            string f = Fold(target ?? "");
            bool dirOnly = Regex.IsMatch(f, @"^(?:para |pra |ao |a |o )?(?:o |a )?(norte|sul|leste|oeste|nordeste|noroeste|sudeste|sudoeste|oriente|ocidente|north|south|east|west)\b") && f.Split(' ').Length <= 4;
            if (!dirOnly)
            {
                var sm = SpeciesCityRx.Match(f.Trim());
                if (sm.Success || NearestCityRx.IsMatch(f.Trim()))
                {
                    string w = sm.Success ? sm.Groups[1].Value : null;
                    string sp = w == null ? null : w.StartsWith("hum") ? "human" : w.StartsWith("elf") ? "elf" : w.StartsWith("an") ? "dwarf" : "orc";
                    city = NearestCityOf(npc, sp);
                    Main.LogInfo("[GodBox] \"" + target + "\" -> nearest " + (sp ?? "any") + " city: " + (city?.name ?? "(none)"));
                    if (city != null) { label = "a cidade " + city.name; return city.getTile(); }
                    return null;
                }
                city = FindCity(target, npc);
                if (city != null) { label = "a cidade " + city.name; return city.getTile(); }
                int amb;
                who = FindUnit(target, npc, you, out amb);
                if (who != null && who != npc) { label = Name(who); return who.current_tile; }
                who = null;
            }
            if (ParseDir(target, out string dn, out float dx, out float dy))
            {
                var here = npc.current_tile;
                int x = Mathf.Clamp(Mathf.RoundToInt(here.x + dx * 40f), 2, MapBox.width - 3);
                int y = Mathf.Clamp(Mathf.RoundToInt(here.y + dy * 40f), 2, MapBox.height - 3);
                var t = GroundNear(World.world.GetTile(x, y), 10);
                label = "o " + dn + " (~40 tiles)";
                return t;
            }
            return null;
        }

        public static bool HasTask(Actor a, string kind) { try { return _tasks.Any(t => t.Npc == a && t.Kind == kind); } catch { return false; } }

        // v0.7.9: "uma cidade humana/élfica/anã/orc" = the nearest city of that species; "a cidade mais próxima" = nearest city
        static readonly Regex SpeciesCityRx = new Regex(@"^(?:uma|a|alguma|qualquer)?\s*(?:cidade|vila|aldeia)\s+(?:mais\s+proxima\s+)?(?:d[oa]s?\s+)?(humana|humanos?|elfica|elfos?|elfa|ana|anoes|anao|anas|orc|orcs|orquica)(?:\s+mais\s+proxima)?$", RegexOptions.CultureInvariant);
        static readonly Regex NearestCityRx = new Regex(@"^(?:a|uma|alguma|qualquer)\s+(?:cidade|vila|aldeia)(?:\s+(?:mais\s+proxima|perto|proxima|qualquer))?$", RegexOptions.CultureInvariant);
        static City NearestCityOf(Actor npc, string species)
        {
            City best = null; float bd = float.MaxValue;
            try
            {
                foreach (var c in World.world.cities)
                {
                    if (c == null || c == SafeCity(npc) && species == null) continue;
                    if (species != null) { string sp = null; try { sp = c.getSpecies(); } catch { } if (sp != species) continue; }
                    var t = c.getTile(); if (t == null) continue;
                    float d = Dist(npc, t);
                    if (d < bd) { bd = d; best = c; }
                }
            }
            catch { }
            return best;
        }

        static void GoTo(Actor npc, Actor you, string target, DialogueConfig cfg, WorldfallActions.Result res)
        {
            var dest = Destination(target, npc, you, out string label, out Actor who, out City city);
            if (dest == null) { res.Reason = string.IsNullOrWhiteSpace(target) ? "não disse para onde ir" : "não existe cidade, pessoa ou direção \"" + target + "\" neste mundo"; return; }
            float d = Dist(npc, dest);
            if (d <= 4f) { res.Outcome = "sem_efeito"; res.Reason = "já está em " + label; return; }
            Drop(npc, "follow", "guide", "attack", "summon", "goto", "raid");
            try { npc.finishStatusEffect("possessed_follower"); } catch { }
            ReleaseHoldFor(npc, "goto");
            float now = Time.unscaledTime;
            Go(npc, dest);
            _tasks.Add(new Task { Kind = "goto", Npc = npc, Target = who, City = city, Tile = dest, Job = label, Until = now + Math.Max(30f, cfg.SummonSeconds), Next = now + 2f, NextStatus = now });
            res.Outcome = "em_andamento";
            res.Changes.Add(Name(npc) + " começou a andar até " + label + " (" + FactsBuilder.Where(npc.current_tile, dest, true) + ", ~" + Mathf.RoundToInt(d) + " tiles)");
            res.Changes.Add(Name(npc) + " AINDA NÃO CHEGOU");
            res.Reason = "só anda com o mundo rodando (feche o chat); você será avisado quando chegar";
            Main.LogInfo("[GodBox] ACTION go_to: walking [" + Name(npc) + " -> " + label + "]");
            var promise = Promises.Bind(npc, label);   // v0.7.9
            if (promise != null) res.Changes.Add(promise);
        }

        /// <summary>v0.8.2 (F7): "reino élfico / dos elfos / dos orcs / anão / humano" -> the nearest city of that species,
        /// preferring one in another kingdom (a species "kingdom" is somewhere else).</summary>
        static City SpeciesKingdomCity(Actor npc, string species)
        {
            City best = null, any = null; float bd = float.MaxValue, ad = float.MaxValue;
            Kingdom own = null; try { own = npc.kingdom; } catch { }
            try
            {
                foreach (var c in World.world.cities)
                {
                    if (c == null) continue;
                    string sp = null; try { sp = c.getSpecies(); } catch { }
                    if (sp != species) continue;
                    var t = c.getTile(); if (t == null) continue;
                    float d = Dist(npc, t);
                    if (d < ad) { ad = d; any = c; }
                    bool other = true; try { other = c.kingdom != own; } catch { }
                    if (other && d < bd) { bd = d; best = c; }
                }
            }
            catch { }
            return best ?? any;
        }

        static void Teleport(Actor npc, Actor you, string target, WorldfallActions.Result res, DialogueConfig cfg = null)
        {
            if (you != null) { res.Outcome = "recusado"; res.Reason = "só os deuses podem teletransportar alguém"; return; }
            target = SafetyRules.SplitChainedPreach(target, out bool thenPreach);   // v0.8.2 (F7): "... e pregue lá"
            WorldTile dest; string label; Actor who; City city;
            string sk = SafetyRules.SpeciesKingdom(target);
            if (sk != null)
            {
                city = SpeciesKingdomCity(npc, sk); who = null;
                label = city != null ? "a cidade " + city.name + (city.kingdom != null ? " (reino " + city.kingdom.name + ")" : "") : null;
                dest = city?.getTile();
                Main.LogInfo("[GodBox] teleport \"" + target + "\" -> species kingdom " + sk + ": " + (city?.name ?? "(none)"));
                if (dest == null) { res.Reason = "não existe nenhuma cidade " + (sk == "elf" ? "élfica" : sk == "orc" ? "orc" : sk == "dwarf" ? "anã" : "humana") + " neste mundo"; return; }
            }
            else dest = Destination(target, npc, you, out label, out who, out city);
            if (dest == null) { res.Reason = string.IsNullOrWhiteSpace(target) ? "não disse para onde" : "não existe cidade, pessoa ou direção \"" + target + "\" neste mundo"; return; }
            var tile = GroundNear(dest, 8);
            if (tile == null) { res.Outcome = "sem_efeito"; res.Reason = "não há chão firme perto de " + label; return; }
            Drop(npc, "follow", "guide", "attack", "summon", "goto", "raid");
            ReleaseHoldFor(npc, "teleport");
            try { ActionLibrary.teleportEffect(npc, tile); } catch { }
            npc.cancelAllBeh();
            npc.spawnOn(tile);
            try { ActionLibrary.teleportEffect(npc, tile); } catch { }
            res.Outcome = "feito";
            res.Changes.Add(Name(npc) + " foi teletransportado(a) e agora está em " + label);
            Main.LogInfo("[GodBox] ACTION teleport: done [" + Name(npc) + " -> " + label + "]");
            if (thenPreach)
            {   // v0.8.2 (F7): chained "e pregue lá" -> the normal preach, where the prophet now stands
                var pr = new WorldfallActions.Result { Id = "preach", Pt = "pregar", En = "preach" };
                try { GodPowers.PreachHere(npc, cfg ?? Main.Cfg, pr); } catch (Exception e) { pr.Outcome = "falhou"; pr.Reason = "erro: " + e.Message; }
                Main.LogInfo(WorldfallActions.LogLine(pr));
                if (pr.Outcome == "feito" || pr.Outcome == "em_andamento") { res.Changes.Add("e começou a pregar lá"); foreach (var c in pr.Changes) res.Changes.Add(c); }
                else res.Changes.Add("mas NÃO pregou lá: " + pr.Reason);
            }
        }

        /// <summary>No civ kingdom: God's "attack the city X" makes the unit itself walk there and fight.</summary>
        public static bool Kingdomless(Actor npc) { try { return !LiveCiv(npc.kingdom); } catch { return true; } }

        static void SoloRaid(Actor npc, Actor you, WorldfallActions.Request req, DialogueConfig cfg, WorldfallActions.Result res)
        {
            if (you != null) { res.Outcome = "sem_efeito"; res.Reason = "não pertence a um reino (não tem exército para mandar)"; return; }
            var c = FindCity(req.Target, npc);
            if (c == null) { res.Reason = string.IsNullOrWhiteSpace(req.Target) ? "não disse qual cidade" : "não existe cidade chamada \"" + req.Target + "\" neste mundo"; return; }
            string owner = null; try { owner = c.kingdom?.name; } catch { }
            Drop(npc, "follow", "guide", "attack", "summon", "goto", "raid");
            try { npc.finishStatusEffect("possessed_follower"); } catch { }
            ReleaseHoldFor(npc, "raid");
            var tile = c.getTile();
            float now = Time.unscaledTime;
            float d = Dist(npc, tile);
            if (d > 6f) Go(npc, tile);
            _tasks.Add(new Task { Kind = "raid", Npc = npc, City = c, Tile = tile, Job = c.name, Until = now + Math.Max(60f, cfg.SummonSeconds), Next = now + 1f, NextStatus = now });
            res.Outcome = "em_andamento";
            res.Changes.Add(Name(npc) + " NÃO tem reino nem exército: vai SOZINHO(A) até a cidade " + c.name + (owner != null ? " (do reino " + owner + ", que não é o seu)" : "") + " e ataca as pessoas e construções dela (sem declaração de guerra)");
            if (d > 6f) { res.Changes.Add("está " + FactsBuilder.Where(npc.current_tile, tile, true) + " (~" + Mathf.RoundToInt(d) + " tiles) e AINDA NÃO CHEGOU"); res.Reason = "só anda com o mundo rodando (feche o chat)"; }
            Main.LogInfo("[GodBox] ACTION attack_city: solo raid (no kingdom) [" + Name(npc) + " -> " + c.name + "]");
        }

        static BaseSimObject RaidTarget(Actor npc, City c)
        {
            BaseSimObject best = null; float bd = 1e9f;
            try
            {
                foreach (var u in c.units)
                {
                    if (u == null || u == npc || !u.isAlive()) continue;
                    bool can = true; try { can = npc.canAttackTarget(u, false, true); } catch { }
                    if (!can) continue;
                    float d = Dist(npc, u.current_tile);
                    if (d < bd) { bd = d; best = u; }
                }
            }
            catch { }
            if (best != null) return best;
            try
            {
                foreach (var b in c.buildings)
                {
                    if (b == null || !b.isAlive() || b.isRuin()) continue;
                    bool can = true; try { can = npc.canAttackTarget(b, false, true); } catch { }
                    if (!can) continue;
                    float d = Dist(npc, b.current_tile);
                    if (d < bd) { bd = d; best = b; }
                }
            }
            catch { }
            return best;
        }

        /// <summary>Tick for goto/raid. true = finished.</summary>
        static bool MoveStep(Task t, float now)
        {
            if (t.Kind == "goto")
            {
                t.Next = now + 2f;
                var dest = Alive(t.Target) ? t.Target.current_tile : t.Tile;
                if (dest == null) return true;
                if (Dist(t.Npc, dest) <= 3.5f || (t.City != null && SafeCity(t.Npc) == t.City && Dist(t.Npc, dest) <= 12f))
                {
                    Main.LogInfo("[GodBox] ACTION go_to: arrived after ~" + Mathf.RoundToInt(now - t.NextStatus) + " s [" + Name(t.Npc) + " -> " + t.Job + "]");
                    ChatWindow.Toast(Name(t.Npc) + " chegou a " + t.Job + ".");
                    Promises.OnArrived(t.Npc);   // v0.7.9: conditional god promise
                    return true;
                }
                if (!UnitHold.Active && !Go(t.Npc, dest) && ++t.PathFails >= 5)
                {
                    Main.LogInfo("[GodBox] ACTION go_to: failed (no path) [" + Name(t.Npc) + "]");
                    ChatWindow.Toast(Name(t.Npc) + " não encontrou caminho até " + t.Job + ".");
                    return true;
                }
                return false;
            }
            if (t.Kind == "raid")
            {
                t.Next = now + 1f;
                bool alive = false; try { alive = t.City != null && t.City.isAlive(); } catch { }
                if (!alive) { Main.LogInfo("[GodBox] ACTION attack_city: city gone, raid over [" + Name(t.Npc) + "]"); ChatWindow.Toast("A cidade " + t.Job + " caiu."); return true; }
                if (Dist(t.Npc, t.Tile) > 14f && !t.Arrived)
                {
                    if (!UnitHold.Active && !Go(t.Npc, t.Tile) && ++t.PathFails >= 5)
                    { ChatWindow.Toast(Name(t.Npc) + " não encontrou caminho até " + t.Job + "."); return true; }
                    t.Next = now + 2f;
                    return false;
                }
                if (!t.Arrived) { t.Arrived = true; Main.LogInfo("[GodBox] ACTION attack_city: solo raid reached " + t.Job + " [" + Name(t.Npc) + "]"); ChatWindow.Toast(Name(t.Npc) + " chegou a " + t.Job + " e começou o ataque."); }
                var cur = t.Npc.attack_target;
                bool busy = false; try { busy = cur != null && cur.isAlive(); } catch { }
                if (!busy)
                {
                    var tg = RaidTarget(t.Npc, t.City);
                    if (tg == null) { Main.LogInfo("[GodBox] ACTION attack_city: nothing left to attack in " + t.Job); return true; }
                    if (tg is Actor ua) { Deaths.MarkModCause(ua, "morto(a) por " + Name(t.Npc) + " no ataque a " + t.Job + " por ordem de Deus"); t.Npc.addAggro(ua); }
                    t.Npc.startFightingWith(tg);
                    if (Dist(t.Npc, tg.current_tile) > 2f) Go(t.Npc, tg.current_tile);
                }
                return false;
            }
            return true;
        }

        static readonly Regex TeleRx = new Regex(@"\b(?:teleporte-se|teleporta-te|teleporte|teleportar|teletransporte-se|teletransporte|apareca|surja|materialize-se)\s+(?:se\s+)?(?:agora\s+)?(?:em|para|pra|ate|no|na|ao|junto (?:a|ao|de))\s+(.+?)\s*[!.]*$", RegexOptions.CultureInvariant);
        static readonly Regex GoRx = new Regex(@"(?:^|\b(?:quero que|ordeno que|exijo que|agora)\s+)(?:voce\s+)?(?:va|vai|ande|caminhe|siga|dirija-se|parta|marche|corra|viaje|ir)\s+(?:agora\s+|imediatamente\s+)?(?:ate|para|pra|pro|ao)\s+(.+?)\s*[!.]*$", RegexOptions.CultureInvariant);

        /// <summary>Deterministic walk/teleport orders (before the war shortcut). f = folded text.</summary>
        static WorldfallActions.Request MoveShortcut(string f, List<WorldfallActions.Available> avail)
        {
            if (f.TrimEnd().EndsWith("?")) return null;
            bool Has(string id) => avail.Any(a => a.Def.Id == id);
            if (Regex.IsMatch(f, @"\b(atac\w*|destru\w*|invad\w*|conquist\w*|saque\w*|queim\w*|guerra)\b")) return null;   // "vá até X e ataque" -> war shortcut / classifier
            if (Has("teleport"))
            {
                var m = TeleRx.Match(f);
                if (m.Success) return new WorldfallActions.Request { Id = "teleport", Target = Clean(m.Groups[1].Value), Confidence = 1f };
            }
            if (Has("go_to"))
            {
                var m = GoRx.Match(f);
                if (m.Success && !Regex.IsMatch(m.Groups[1].Value, @"^(?:mim|me|comigo|embora|longe|daqui|casa|dormir|trabalhar|pregar|fundar)\b")) return new WorldfallActions.Request { Id = "go_to", Target = Clean(m.Groups[1].Value), Confidence = 1f };
            }
            return null;
        }

        static string Clean(string q) => Regex.Replace(q ?? "", @"^(?:a |o |as |os )?(?:cidade|vila|aldeia) (?:de |do |da )?", "").Trim();

        static City SafeCity(Actor a) { try { return a.city; } catch { return null; } }
    }
}
