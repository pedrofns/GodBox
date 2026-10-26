using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WorldfallAIChat
{
    /// <summary>v0.8.1 (C3): build_temple. Native construction (ai.behaviours.CityBehBuild.tryToBuild: builders raise it) is preferred;
    /// if the city cannot place it natively, God raises it by miracle (BuildingManager.addBuilding + fx_spawn_big).
    /// Never uses create_village / assign_job. Always asked for confirmation first (ChatWindow PendingOffer kind "temple").</summary>
    public static class Temples
    {
        public const string Id = "build_temple";
        static string Nm(Actor a) { try { return a.getName(); } catch { return "?"; } }

        public static City ResolveCity(Actor npc, string place, Func<string, City> named, out string err)
        {
            err = null;
            City c = null;
            if (!string.IsNullOrWhiteSpace(place))
            {
                c = named(place);
                if (c == null) { err = "não existe nenhuma cidade chamada \"" + place + "\" neste mundo"; return null; }
            }
            else
            {
                try { c = npc?.city; } catch { }
                if (c == null) { err = Nm(npc) + " não mora em nenhuma cidade (diga em qual cidade o templo deve ser erguido)"; return null; }
            }
            bool alive = false; try { alive = c.isAlive(); } catch { }
            if (!alive) { err = "a cidade " + c.name + " não existe mais"; return null; }
            return c;
        }

        public static BuildingAsset AssetFor(City c)
        {
            try { var a = c.getActorAsset()?.architecture_asset?.getBuilding("order_temple"); if (a != null) return a; } catch { }
            try { var a = c.getFounderSpecies()?.architecture_asset?.getBuilding("order_temple"); if (a != null) return a; } catch { }
            try { return AssetManager.buildings.get("temple_human"); } catch { return null; }
        }

        static string CostPt(ConstructionCost k)
        {
            if (k == null) return "sem custo";
            var p = new List<string>();
            if (k.wood > 0) p.Add(k.wood + " de madeira");
            if (k.stone > 0) p.Add(k.stone + " de pedra");
            if (k.common_metals > 0) p.Add(k.common_metals + " de metal");
            if (k.gold > 0) p.Add(k.gold + " de ouro");
            return p.Count == 0 ? "sem custo" : string.Join(", ", p);
        }
        static int Have(City c, string id) { try { return c.getResourcesAmount(id); } catch { return 0; } }
        static IEnumerable<(string id, string pt, int need)> Needs(City c, ConstructionCost k)
        {
            if (k == null) yield break;
            if (k.wood > Have(c, "wood")) yield return ("wood", "madeira", k.wood - Have(c, "wood"));
            if (k.stone > Have(c, "stone")) yield return ("stone", "pedra", k.stone - Have(c, "stone"));
            if (k.common_metals > Have(c, "common_metals")) yield return ("common_metals", "metal", k.common_metals - Have(c, "common_metals"));
            if (k.gold > Have(c, "gold")) yield return ("gold", "ouro", k.gold - Have(c, "gold"));
        }

        /// <summary>Validation before the offer: null = OK (terms filled), else an honest reason.</summary>
        public static string Check(City c, out BuildingAsset asset, out string terms)
        {
            asset = null; terms = null;
            // v0.8.2 (F6): say honestly whether the temple is finished or still a construction site
            if (GodFaith.HasFinishedTemple(c)) return "a cidade " + c.name + " já tem um templo pronto";
            if (GodFaith.HasTemple(c)) return "a cidade " + c.name + " já tem um templo em construção (a obra ainda não terminou)";
            asset = AssetFor(c);
            if (asset == null) return "o jogo não tem um templo para o povo de " + c.name;
            var miss = Needs(c, asset.cost).ToList();
            terms = "erguer um templo em " + c.name + " (custo: " + CostPt(asset.cost) + "; "
                + (miss.Count == 0 ? "a cidade tem os recursos" : "falta " + string.Join(", ", miss.Select(m => m.need + " de " + m.pt)) + ", que Deus providencia")
                + "; os construtores da cidade erguem a obra, ou Deus a ergue por milagre se não houver lugar para a obra)";
            return null;
        }

        public static WorldfallActions.Result Execute(Actor npc, Actor you, WorldfallActions.Request req, Func<string, City> named)
        {
            var res = new WorldfallActions.Result { Id = Id, Pt = "construir um templo", En = "build a temple" };
            try
            {
                bool god = you == null || Avatar.IsAvatar(you);
                if (!god) { res.Outcome = "recusado"; res.Reason = "só Deus pode mandar erguer um templo assim"; return res; }
                var c = ResolveCity(npc, req?.Target, named, out string err);
                if (c == null) { res.Reason = err; return res; }
                string why = Check(c, out var asset, out _);
                if (why != null) { res.Outcome = c != null && GodFaith.HasTemple(c) ? "sem_efeito" : "falhou"; res.Reason = why; return res; }
                // God tops up what is missing (real stockpile resources)
                var miss = Needs(c, asset.cost).ToList();
                var given = new List<string>();
                foreach (var m in miss) { int got = 0; try { got = c.addResourcesToRandomStockpile(m.id, m.need); } catch { } if (got > 0) given.Add(got + " de " + m.pt); }
                Building b = null; string path = null, note = null;
                bool busy = false; try { var u = c.under_construction_building; busy = u != null && u.isAlive() && u.isUnderConstruction(); } catch { }
                bool enough = false; try { enough = c.hasEnoughResourcesFor(asset.cost); } catch { }
                if (busy) note = "a cidade já tinha outra obra em andamento";
                else if (!enough) note = "a cidade não tem depósito para guardar os recursos";
                else
                {
                    try { b = ai.behaviours.CityBehBuild.tryToBuild(c, asset); } catch (Exception e) { Main.LogWarning("[GodBox] TEMPLE native build failed: " + e.Message); }
                    if (b != null) path = "nativo"; else note = "não houve lugar livre para a obra pelos construtores";
                }
                if (b == null)
                {   // miracle fallback: a finished temple on a good tile of the city's land
                    WorldTile tile = null;
                    try
                    {
                        foreach (var z in c.zones.ToList().OrderBy(_ => UnityEngine.Random.value))
                        {
                            if (z?.centerTile != null && ai.behaviours.CityBehBuild.isGoodTileForBuilding(z.centerTile, asset, c)) { tile = z.centerTile; break; }
                            foreach (var t in z.tiles) if (t != null && ai.behaviours.CityBehBuild.isGoodTileForBuilding(t, asset, c)) { tile = t; break; }
                            if (tile != null) break;
                        }
                    }
                    catch { }
                    if (tile != null)
                    {
                        try { b = World.world.buildings.addBuilding(asset, tile, false, true); } catch (Exception e) { Main.LogWarning("[GodBox] TEMPLE miracle failed: " + e.Message); }
                        if (b != null) { path = "milagre"; try { EffectsLibrary.spawn("fx_spawn_big", tile); } catch { } }
                    }
                }
                if (b == null)
                {
                    res.Reason = "não há nenhum lugar livre nas terras de " + c.name + " para um templo" + (note != null ? " (" + note + ")" : "");
                    Main.LogInfo("[GodBox] TEMPLE " + c.name + ": failed (" + res.Reason + ").");
                    return res;
                }
                var g = MemoryStore.God(); long cid = c.getID(); if (!g.GodTempleCities.Contains(cid)) g.GodTempleCities.Add(cid); MemoryStore.SaveGod();
                res.Outcome = "feito";
                if (path == "nativo") res.Changes.Add("os construtores de " + c.name + " começaram a erguer um templo por ordem de Deus (a obra leva um tempo)");
                else res.Changes.Add("Deus ergueu um templo em " + c.name + " por milagre" + (note != null ? " (" + note + ")" : ""));
                if (given.Count > 0) res.Changes.Add("Deus deu à cidade " + string.Join(", ", given) + " para a obra");
                res.Toasts.Add(path == "nativo" ? "Templo em obra em " + c.name : "Templo erguido por milagre em " + c.name);
                Main.LogInfo("[GodBox] TEMPLE " + c.name + ": " + asset.id + " via " + (path == "nativo" ? "native construction (ai.behaviours.CityBehBuild.tryToBuild)" : "miracle (addBuilding)") + (given.Count > 0 ? "; God gave " + string.Join(", ", given) : "") + (note != null ? "; " + note : "") + ".");
                try { Rumors.Add(c, path == "nativo" ? "por ordem de Deus, começaram a erguer um templo em " + c.name : "um templo surgiu do nada em " + c.name + ", erguido por Deus"); } catch { }
            }
            catch (Exception e) { res.Outcome = "falhou"; res.Reason = "erro: " + e.Message; Main.LogWarning("[GodBox] TEMPLE failed: " + e); }
            return res;
        }
    }

    /// <summary>v0.8.2 (F5): temple orders with a deadline ("erga um templo em 3 anos"). Checked at expiry:
    /// finished temple = fulfilled; a construction site or nothing = not fulfilled (curse only if God threatened one).</summary>
    public static class TempleOrders
    {
        static string Nm(Actor a) { try { return a.getName(); } catch { return "?"; } }
        public static void Add(Actor npc, City c, int years, string penalty)
        {
            if (npc == null || c == null || years <= 0) return;
            var g = MemoryStore.God(); long cid = c.getID();
            g.TempleOrders.RemoveAll(o => o.CityId == cid);
            int y = Main.CurrentYear();
            g.TempleOrders.Add(new MemoryStore.TempleOrder { NpcId = npc.getID(), NpcName = Nm(npc), CityId = cid, CityName = c.name, MadeYear = y, DeadlineYear = y + years, Penalty = penalty });
            MemoryStore.SaveGod();
            Main.LogInfo("[GodBox] TEMPLE deadline stored: " + c.name + " must have a finished temple by year " + (y + years) + " (now " + y + ", order to " + Nm(npc) + ")" + (penalty != null ? "; penalty " + penalty : "; no penalty stated") + ".");
        }
        public static MemoryStore.TempleOrder For(City c) { try { long id = c.getID(); return MemoryStore.God().TempleOrders.FirstOrDefault(o => o.CityId == id); } catch { return null; } }

        public static void Tick()
        {
            try
            {
                var g = MemoryStore.God(); if (g.TempleOrders == null || g.TempleOrders.Count == 0) return;
                int year = Main.CurrentYear();
                bool dirty = false;
                foreach (var o in g.TempleOrders.ToList())
                {
                    if (year < o.DeadlineYear) continue;
                    g.TempleOrders.Remove(o); dirty = true;
                    City c = null; foreach (var x in World.world.cities) { try { if (x != null && x.getID() == o.CityId) { c = x; break; } } catch { } }
                    Actor npc = null; foreach (Actor a in World.world.units) { try { if (a != null && a.isAlive() && a.getID() == o.NpcId) { npc = a; break; } } catch { } }
                    string st = c == null ? "gone" : GodFaith.TempleState(c);
                    if (st == "pronto")
                    {
                        Main.LogInfo("[GodBox] TEMPLE deadline for " + o.CityName + ": FULFILLED (finished temple by year " + o.DeadlineYear + ").");
                        try { Rumors.Add(c, "o templo que Deus pediu em " + o.CityName + " ficou pronto dentro do prazo"); } catch { }
                        if (npc != null) try { Rumors.Deliver(npc, "O templo que Deus pediu em " + o.CityName + " ficou pronto dentro do prazo."); } catch { }
                        continue;
                    }
                    string why = st == null ? "não há templo" : st == "em obra" ? "o templo ainda está em construção" : "a cidade não existe mais";
                    Main.LogInfo("[GodBox] TEMPLE deadline for " + o.CityName + ": EXPIRED in year " + year + " (" + why + ").");
                    try { ChatWindow.Toast("Prazo do templo vencido em " + o.CityName + ": " + why); } catch { }
                    if (npc != null) try { Rumors.Deliver(npc, "O prazo de Deus para o templo em " + o.CityName + " acabou: " + why + "."); } catch { }
                    if (o.Penalty == "curse" && npc != null)
                    {
                        var res = GodPowers.Execute(npc, null, new WorldfallActions.Request { Id = "curse", Target = "" }, Main.Cfg);
                        Main.LogInfo("[GodBox] TEMPLE deadline penalty: curse on " + o.NpcName + " -> " + res.Outcome + ".");
                        try { Rumors.Add(npc.city, "Deus amaldiçoou " + o.NpcName + " porque o templo não ficou pronto no prazo"); } catch { }
                    }
                }
                if (dirty) MemoryStore.SaveGod();
            }
            catch (Exception e) { Main.LogWarning("[GodBox] temple deadline tick failed: " + e.Message); }
        }
    }

    /// <summary>v0.8.1 (B13): God's deadline threats to a prophet ("converta alguém em dois anos ou será amaldiçoado").
    /// Condition: the prophet's conversions grow by >= 1 before the deadline (game years). Otherwise a REAL curse at the
    /// deadline (GodPowers curse), logged + rumour. "kill" penalties are not executed (logged as unsupported).</summary>
    public static class Deadlines
    {
        static float _next;
        public static string Add(Actor prophet, int years, string penalty, string text)
        {
            var g = MemoryStore.God();
            long id = prophet.getID();
            g.Threats.RemoveAll(t => t.ProphetId == id);
            int y = Main.CurrentYear();
            g.Threats.Add(new MemoryStore.DeadlineThreat { ProphetId = id, ProphetName = SafeName(prophet), StartConversions = g.ConversionsOf(id), MadeYear = y, DeadlineYear = y + Math.Max(1, years), Penalty = penalty, Text = text });
            MemoryStore.SaveGod();
            Main.LogInfo("[GodBox] DEADLINE threat stored: " + SafeName(prophet) + " must convert >=1 person by year " + (y + Math.Max(1, years)) + " (now " + y + ", conversions so far " + g.ConversionsOf(id) + "); penalty " + penalty + ".");
            return null;
        }
        public static MemoryStore.DeadlineThreat For(Actor a) { try { long id = a.getID(); return MemoryStore.God().Threats.FirstOrDefault(t => t.ProphetId == id); } catch { return null; } }
        static string SafeName(Actor a) { try { return a.getName(); } catch { return "?"; } }

        public static void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 5f;
            TempleOrders.Tick();   // v0.8.2 (F5)
            try
            {
                var g = MemoryStore.God(); if (g.Threats == null || g.Threats.Count == 0) return;
                int year = Main.CurrentYear();
                Dictionary<long, Actor> alive = null;
                foreach (var t in g.Threats.ToList())
                {
                    if (alive == null) { alive = new Dictionary<long, Actor>(); var ids = new HashSet<long>(g.Threats.Select(x => x.ProphetId)); foreach (Actor a in World.world.units) { try { if (a != null && a.isAlive() && ids.Contains(a.getID())) alive[a.getID()] = a; } catch { } } }
                    if (!alive.TryGetValue(t.ProphetId, out var p)) { g.Threats.Remove(t); Main.LogInfo("[GodBox] DEADLINE for " + t.ProphetName + ": dropped (the prophet is dead)."); continue; }
                    int done = g.ConversionsOf(t.ProphetId) - t.StartConversions;
                    if (done >= 1)
                    {
                        g.Threats.Remove(t);
                        Main.LogInfo("[GodBox] DEADLINE for " + t.ProphetName + ": FULFILLED (" + done + " conversion(s) since year " + t.MadeYear + "); no punishment.");
                        try { Rumors.Deliver(p, "Você cumpriu a exigência de Deus: converteu " + done + " pessoa(s) antes do prazo e escapou da maldição."); } catch { }
                        try { Rumors.Add(p.city, "o profeta " + t.ProphetName + " cumpriu a exigência de Deus e escapou da maldição"); } catch { }
                        continue;
                    }
                    if (year < t.DeadlineYear) continue;
                    g.Threats.Remove(t);
                    if (t.Penalty != "curse")
                    {
                        Main.LogInfo("[GodBox] DEADLINE for " + t.ProphetName + ": expired without conversions; penalty '" + t.Penalty + "' is not supported (not executed).");
                        continue;
                    }
                    var res = GodPowers.Execute(p, null, new WorldfallActions.Request { Id = "curse", Target = "" }, Main.Cfg);
                    Main.LogInfo("[GodBox] DEADLINE for " + t.ProphetName + ": EXPIRED in year " + year + " with no conversion since year " + t.MadeYear + "; curse " + res.Outcome + " (" + string.Join("; ", res.Changes) + ").");
                    try { Rumors.Add(p.city, "Deus amaldiçoou o profeta " + t.ProphetName + " por não converter ninguém no prazo"); } catch { }
                    try { Rumors.Deliver(p, "O prazo de Deus acabou e você não converteu ninguém: Deus cumpriu a ameaça e " + (res.Outcome == "feito" ? "amaldiçoou você." : "tentou amaldiçoar você, mas a maldição não pegou.")); } catch { }
                    try { ChatWindow.Toast("Prazo vencido: Deus amaldiçoou o profeta " + t.ProphetName + (res.Outcome == "feito" ? "" : " (não pegou)")); } catch { }
                }
                MemoryStore.SaveGod();
            }
            catch (Exception e) { Main.LogWarning("[GodBox] deadline tick failed: " + e.Message); }
        }
    }
}
