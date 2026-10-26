using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace WorldfallAIChat
{
    /// <summary>
    /// v0.7.6: create_village rewritten.
    /// Root causes of the 0.7.0-0.7.5 failures: (1) on arrival the zone was re-validated strictly and the order failed
    /// instead of re-picking ("o lugar escolhido deixou de servir"); the colonist's own city neighbour zones and a dirty
    /// CityPlaceFinder made isGoodForNewCity(actor) false; (2) a fixed 240 s wall-clock timeout, not progress-based
    /// ("não chegou a tempo"); (3) the requested direction was ignored; (4) failures were only a 4 s toast + log line.
    /// Now: the game's own path walk (goTo) as in vanilla migration, progress tracking, a fallback that founds at the best
    /// valid zone near the colonist (or, like vanilla expansion, remotely at the chosen zone so the colonist walks to its new
    /// city), and the result (success or failure) goes to the log, a long toast, rumors and the memory of who ordered it.
    /// </summary>
    public static partial class NativeActions
    {
        class VState
        {
            public Actor Orderer; public string By, Dir; public float Dx, Dy; public WorldTile Origin;
            public float Run, LastStep, BestDist = float.MaxValue, LastProgress, NextLog = 10f; public int Repicks;
        }

        static readonly (string rx, string pt, float dx, float dy)[] Dirs =
        {
            (@"\b(nordeste|northeast)\b", "nordeste", 1, 1), (@"\b(noroeste|northwest)\b", "noroeste", -1, 1),
            (@"\b(sudeste|southeast)\b", "sudeste", 1, -1), (@"\b(sudoeste|southwest)\b", "sudoeste", -1, -1),
            (@"\b(norte|north)\b", "norte", 0, 1), (@"\b(sul|south)\b", "sul", 0, -1),
            (@"\b(leste|oriente|east)\b", "leste", 1, 0), (@"\b(oeste|ocidente|west)\b", "oeste", -1, 0),
        };

        static bool ParseDir(string text, out string name, out float dx, out float dy)
        {
            string f = Fold(text);
            foreach (var d in Dirs)
                if (Regex.IsMatch(f, d.rx)) { name = d.pt; float l = Mathf.Sqrt(d.dx * d.dx + d.dy * d.dy); dx = d.dx / l; dy = d.dy / l; return true; }
            name = null; dx = dy = 0; return false;
        }

        static CityPlaceFinder Finder => World.world.city_zone_helper.city_place_finder;

        /// <summary>null = a city can be founded here now; otherwise why not (pt).</summary>
        static string ZoneWhyNot(TileZone z, Actor a)
        {
            try
            {
                if (z == null || z.centerTile == null) return "lugar inválido";
                if (z.city != null) return "a terra já pertence à cidade " + z.city.name;
                foreach (var n in z.neighbours_all) if (n != null && n.city != null) return "fica colado na cidade " + n.city.name;
                if (a != null && a.subspecies != null && !z.checkCanSettleInThisBiomes(a.subspecies)) return "bioma onde a espécie não se assenta";
                if (!z.isGoodForNewCity()) return "terreno que o jogo não aceita para cidade";
                return null;
            }
            catch (Exception e) { return "não deu para checar (" + e.Message + ")"; }
        }

        static float TDist(WorldTile a, WorldTile b) => a == null || b == null ? 99999f : Mathf.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));

        /// <summary>Nearest valid zone to 'from' (same island), optionally inside a cone around (dx,dy); maxDist 0 = any.</summary>
        static TileZone NearestValid(WorldTile from, Actor a, float dx = 0, float dy = 0, float minCos = -2f, float maxDist = 0f)
        {
            TileZone best = null; float bd = float.MaxValue;
            try
            {
                var finder = Finder; finder.recalc();
                foreach (var z in finder.zones.ToList())
                {
                    var c = z?.centerTile; if (c == null) continue;
                    float d = TDist(from, c);
                    if (maxDist > 0 && d > maxDist) continue;
                    if (minCos > -2f && d > 0.5f && ((c.x - from.x) * dx + (c.y - from.y) * dy) / d < minCos) continue;
                    if (d >= bd) continue;
                    try { if (!c.isSameIsland(from)) continue; } catch { }
                    if (ZoneWhyNot(z, a) != null) continue;
                    best = z; bd = d;
                }
            }
            catch (Exception e) { Main.LogWarning("[GodBox] village zone search failed: " + e.Message); }
            return best;
        }

        static void CreateVillage(Actor npc, Actor you, WorldfallActions.Request req, DialogueConfig cfg, WorldfallActions.Result res)
        {
            string block = VillageBlock(npc);
            if (block != null) { res.Reason = block; return; }
            var k = npc.kingdom;
            var colonist = PickColonist(npc);
            if (colonist == null) { res.Reason = "não há adulto disponível para ser colono"; return; }
            var finder = Finder;
            finder.recalc();
            if (!finder.hasPossibleZones()) { res.Reason = "não há terra livre boa para uma nova vila em todo o mundo"; return; }
            City from = null; try { from = colonist.city ?? npc.city ?? k.capital; } catch { }
            WorldTile origin = null; try { origin = from?.getTile() ?? colonist.current_tile; } catch { origin = colonist.current_tile; }
            bool hasDir = ParseDir((req?.Message ?? "") + " " + (req?.Target ?? ""), out string dir, out float dx, out float dy);
            TileZone zone = null; string pickNote = null;
            if (hasDir)
            {
                zone = NearestValid(origin, colonist, dx, dy, 0.5f);                      // within ~60°
                if (zone == null) { zone = NearestValid(origin, colonist, dx, dy, 0f); }  // within 90°
                if (zone == null) { zone = NearestValid(origin, colonist); if (zone != null) pickNote = "não há terra livre ao " + dir + "; escolhida a mais próxima"; }
            }
            else
            {
                try
                {
                    var beh = new BehKingCheckNewCityFoundation();
                    using (var list = beh.getCityListForExpansion(k))
                    {
                        if (list.Count == 0)
                            foreach (City c in k.getCities())
                                try { if (c.getTile() != null && c.status.population_adults >= 8) list.Add(c); } catch { }
                        if (list.Count > 0) { var z = beh.findZoneForExpansion(colonist, list, out City f2); if (z != null && ZoneWhyNot(z, colonist) == null) { zone = z; if (f2 != null) from = f2; } }
                    }
                }
                catch { }
                if (zone == null) zone = NearestValid(origin, colonist);
            }
            if (zone == null) { res.Reason = "nenhuma terra livre válida alcançável por terra a partir de " + (from?.name ?? "aqui"); return; }
            Drop(colonist, "follow", "guide", "attack", "job", "village");
            try { colonist.finishStatusEffect("possessed_follower"); } catch { }
            ReleaseHoldFor(colonist, "colonist");
            try { colonist.stopBeingWarrior(); } catch { }
            try { colonist.exitBuilding(); } catch { }
            try { colonist.cancelAllBeh(); } catch { }
            Go(colonist, zone.centerTile);
            float now = Time.unscaledTime;
            var v = new VState { Orderer = npc, By = you == null ? "Deus" : Name(you), Dir = hasDir ? dir : null, Dx = dx, Dy = dy, Origin = origin, LastStep = now };
            float cap = Math.Max(120f, cfg.VillageWalkSeconds);
            _tasks.Add(new Task { Kind = "village", Npc = colonist, Zone = zone, Tile = zone.centerTile, City = from, Kingdom = k, Until = now + cap * 4f + 600f, Next = now, V = v });
            res.Outcome = "em_andamento";
            res.Changes.Add("colono " + (colonist == npc ? "(o próprio personagem)" : Name(colonist)) + " partiu para fundar uma vila " + (hasDir ? "ao " + dir + " " : "") + "(" + FactsBuilder.Where(colonist.current_tile, zone.centerTile, true) + ")" + (pickNote != null ? "; " + pickNote : ""));
            res.Toasts.Add(Name(colonist) + " partiu para fundar uma nova vila" + (hasDir ? " ao " + dir : "") + ".");
            Main.LogInfo("[GodBox] VILLAGE " + Name(colonist) + ": started" + (hasDir ? " (direção " + dir + ")" : "") + ", zone " + zone.centerTile.x + "," + zone.centerTile.y + ", " + Mathf.RoundToInt(TDist(colonist.current_tile, zone.centerTile)) + " tiles" + (pickNote != null ? "; " + pickNote : ""));
        }

        /// <summary>One village step (about every second of running game). true = task finished.</summary>
        static bool VillageStep(Task t, float now, DialogueConfig cfg)
        {
            var v = t.V; var a = t.Npc;
            v.Run += Math.Max(0f, now - v.LastStep); v.LastStep = now;
            t.Next = now + 1f;
            float d = Dist(a, t.Tile);
            bool sameZone = false; try { sameZone = a.current_tile?.zone == t.Zone; } catch { }
            if (d <= 6f || sameZone) { Main.LogInfo("[GodBox] VILLAGE " + Name(a) + ": arrived after " + Mathf.RoundToInt(v.Run) + " s"); VillageArrive(t); return true; }
            if (d < v.BestDist - 1.5f) { v.BestDist = d; v.LastProgress = v.Run; }
            bool moving = false; try { moving = a.is_moving && a.tile_target == t.Tile; } catch { }
            if (v.Run >= v.NextLog)
            {
                v.NextLog = v.Run + 10f;
                Main.LogInfo("[GodBox] VILLAGE " + Name(a) + ": " + Mathf.RoundToInt(d) + " tiles to go, " + Mathf.RoundToInt(v.Run) + " s, " + (moving ? "walking" : "not walking") + ", last progress " + Mathf.RoundToInt(v.Run - v.LastProgress) + " s ago");
            }
            if (!moving && !UnitHold.Active)
            {
                try { a.cancelAllBeh(); } catch { }
                if (!Go(a, t.Tile)) t.PathFails++;
            }
            float stuck = Math.Max(15f, cfg?.VillageStuckSeconds ?? 45f), cap = Math.Max(120f, cfg?.VillageWalkSeconds ?? 420f);
            if (t.PathFails >= 8) { VillageFallback(t, "sem caminho por terra"); return true; }
            if (v.Run - v.LastProgress >= stuck) { VillageFallback(t, "o colono empacou (" + Mathf.RoundToInt(stuck) + " s sem avançar)"); return true; }
            if (v.Run >= cap) { VillageFallback(t, "a caminhada passou de " + Mathf.RoundToInt(cap) + " s"); return true; }
            return false;
        }

        static void VillageArrive(Task t)
        {
            var a = t.Npc;
            try { Finder.recalc(); } catch { }
            string why = ZoneWhyNot(t.Zone, a);
            Main.LogInfo("[GodBox] VILLAGE " + Name(a) + ": validation " + (why == null ? "ok" : "failed: " + why));
            if (why == null) { FoundVillage(t, t.Zone, "no lugar escolhido"); return; }
            var z = NearestValid(a.current_tile, a);
            if (z != null) { t.V.Repicks++; FoundVillage(t, z, "o lugar escolhido deixou de servir (" + why + "); fundada na terra livre mais próxima"); return; }
            VillageFail(t, "o lugar deixou de servir (" + why + ") e não há outra terra livre por perto");
        }

        static void VillageFallback(Task t, string why)
        {
            var a = t.Npc;
            Main.LogInfo("[GodBox] VILLAGE " + Name(a) + ": fallback (" + why + ")");
            var z = NearestValid(a.current_tile, a, 0, 0, -2f, 30f);
            if (z != null) { FoundVillage(t, z, why + "; fundada na melhor terra livre perto do colono"); return; }
            if (ZoneWhyNot(t.Zone, a) == null) { FoundVillage(t, t.Zone, why + "; fundada à distância no lugar escolhido, como o jogo faz na expansão (o colono segue para lá)"); return; }
            z = NearestValid(a.current_tile, a);
            if (z != null) { FoundVillage(t, z, why + "; fundada na terra livre mais próxima do colono"); return; }
            VillageFail(t, why + ", e não sobrou terra livre válida");
        }

        static void FoundVillage(Task t, TileZone zone, string how)
        {
            var a = t.Npc;
            City city = null;
            try { city = World.world.cities.buildNewCity(a, zone); } catch (Exception e) { Main.LogWarning("[GodBox] buildNewCity failed: " + e.Message); }
            if (city == null) { VillageFail(t, "o jogo não criou a cidade"); return; }
            try { if (a.city != city) a.joinCity(city); } catch { }
            try { a.cancelAllBeh(); } catch { }
            int moved = 0;
            try
            {
                if (t.City != null && t.City.isAlive() && t.City != city)
                { int before = city.units.Count; new BehKingCheckNewCityFoundation().moveSomeUnitsToNewCity(city, t.City); moved = Math.Max(0, city.units.Count - before); }
            }
            catch { }
            string where = FactsBuilder.Where(t.V?.Origin ?? t.City?.getTile(), city.getTile(), true);
            Main.LogInfo("[GodBox] ACTION create_village: feito | vila " + city.name + " fundada por " + Name(a) + " (" + how + "; " + where + (moved > 0 ? "; +" + moved + " colonos" : "") + ")");
            try { ChatWindow.Toast("Nova vila: " + city.name + ", fundada por " + Name(a) + (t.V?.Dir != null ? " (" + t.V.Dir + ")" : "") + ".", 8f); } catch { }
            string rumor = Name(a) + " fundou a vila " + city.name;
            try { Rumors.Add(t.City, rumor); Rumors.Add(city, rumor); } catch { }
            VillageNotify(t, "NOTÍCIA: " + rumor + " (" + where + (t.City != null ? " de " + t.City.name : "") + "), cumprindo a ordem de " + (t.V?.By ?? "?") + (how.StartsWith("no lugar") ? "" : "; " + how) + ".");
        }

        static void VillageFail(Task t, string reason)
        {
            var a = t.Npc;
            Main.LogInfo("[GodBox] ACTION create_village: falhou (" + reason + ") [" + Name(a) + "]");
            try { ChatWindow.Toast("A nova vila NÃO foi fundada: " + reason + " (colono " + Name(a) + ").", 8f); } catch { }
            try { Rumors.Add(t.City, "a tentativa de fundar uma nova vila com " + Name(a) + " fracassou: " + reason); } catch { }
            VillageNotify(t, "NOTÍCIA: a nova vila que " + (t.V?.By ?? "alguém") + " mandou fundar NÃO foi fundada (colono " + Name(a) + "): " + reason + ".");
        }

        /// <summary>Who ordered it, the colonist and the king learn the outcome (shown next time they talk).</summary>
        static void VillageNotify(Task t, string text)
        {
            var to = new HashSet<Actor>();
            if (t.V?.Orderer != null) to.Add(t.V.Orderer);
            to.Add(t.Npc);
            try { if (t.Kingdom?.king != null) to.Add(t.Kingdom.king); } catch { }
            foreach (var x in to) if (Alive(x)) try { Rumors.Deliver(x, text); } catch { }
        }

        static string VillageOngoing(Task t, Actor npc)
        {
            string dir = t.V?.Dir != null ? " ao " + t.V.Dir : "";
            string left = Mathf.RoundToInt(Dist(t.Npc, t.Tile)) + " tiles";
            return t.Npc == npc ? "você está a caminho de fundar uma nova vila" + dir + " (faltam ~" + left + ")"
                                : "o colono " + Name(t.Npc) + " está a caminho de fundar a nova vila" + dir + " que foi ordenada (faltam ~" + left + "; AINDA NÃO FOI FUNDADA)";
        }
    }
}
