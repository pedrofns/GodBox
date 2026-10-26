using System;
using System.Linq;

namespace WorldfallAIChat
{
    /// <summary>v0.8.2: revoke_prophet (F2) and write_book (K). Both are commitment actions: ChatWindow asks God
    /// for confirmation first (PendingOffer kinds "revoke" / "book"); these run only after a "sim".</summary>
    public static class GodOrders082
    {
        public const string RevokeId = "revoke_prophet", BookId = "write_book";
        static string Nm(Actor a) { try { return a.getName(); } catch { return "?"; } }
        public static bool Female(Actor a) { try { return a != null && a.isSexFemale(); } catch { return false; } }
        static string TraitName(string id) { try { var t = AssetManager.traits.get(id); var n = t != null ? GameLocalePt.Trait(t) : null; return string.IsNullOrWhiteSpace(n) ? id : n; } catch { return id; } }
        public static string ProphetWord(Actor a) => Female(a) ? "profetisa" : "profeta";

        // ------------------------------------------------------------------ F2: revoke_prophet ----
        public static WorldfallActions.Result RevokeProphet(Actor npc)
        {
            var res = new WorldfallActions.Result { Id = RevokeId, Pt = "retirar o dom de profeta", En = "revoke prophet" };
            try
            {
                if (npc == null || !npc.isAlive()) { res.Outcome = "falhou"; res.Reason = "a pessoa não está viva"; return res; }
                var g = MemoryStore.God(); long id = npc.getID();
                if (!g.Prophets.Contains(id)) { res.Outcome = "sem_efeito"; res.Reason = Nm(npc) + " não é " + ProphetWord(npc) + " de Deus"; return res; }
                g.Prophets.RemoveAll(x => x == id);
                if (!g.RevokedProphets.Contains(id)) g.RevokedProphets.Add(id);
                g.Threats.RemoveAll(t => t.ProphetId == id);
                MemoryStore.SaveGod();
                var removed = new System.Collections.Generic.List<string>();
                foreach (var t in GodPowers.ProphetTraitIds)
                {
                    bool had = false; try { had = npc.hasTrait(t); } catch { }
                    if (!had) continue;
                    try { npc.removeTrait(t); } catch { }
                    bool still = true; try { still = npc.hasTrait(t); } catch { }
                    if (!still) removed.Add(t);
                }
                try { var mem = MemoryStore.Get(npc); if (mem.ProphetOf != null) { mem.ProphetOf = null; MemoryStore.Save(mem); } } catch { }
                try { GodFaith.StopMission(npc); GodPowers.StopPreach(npc); } catch { }
                res.Outcome = "feito";
                res.Changes.Add(Nm(npc) + " deixou de ser " + ProphetWord(npc) + " de Deus");
                if (removed.Count > 0) res.Changes.Add("os dons de Deus foram retirados (" + string.Join(", ", removed.Select(TraitName)) + ")");
                res.Toasts.Add("Deus retirou o dom de " + ProphetWord(npc) + " de " + Nm(npc));
                Main.LogInfo("[GodBox] ACTION revoke_prophet: " + Nm(npc) + " is no longer God's prophet; traits removed: " + (removed.Count > 0 ? string.Join(", ", removed) : "none") + "; _god.json updated.");
                try { Rumors.Add(npc.city, "Deus retirou de " + Nm(npc) + " o dom de " + ProphetWord(npc)); } catch { }
            }
            catch (Exception e) { res.Outcome = "falhou"; res.Reason = "erro: " + e.Message; Main.LogWarning("[GodBox] revoke_prophet failed: " + e); }
            return res;
        }

        // ------------------------------------------------------------------ K: write_book ----
        /// <summary>null = the NPC can write now; else the honest reason it cannot.</summary>
        public static string BookCheck(Actor npc)
        {
            if (npc == null) return "não há ninguém para escrever";
            bool alive = false; try { alive = npc.isAlive(); } catch { }
            if (!alive) return "a pessoa não está viva";
            var rel = GodPowers.CurrentReligion();
            if (rel == null) return "Deus ainda não tem religião no mundo";
            if (npc.religion != rel) return Nm(npc) + " não segue a religião de Deus, então o livro não seria da fé de Deus";
            bool mind = false; try { mind = npc.hasSubspecies() && npc.subspecies.has_advanced_memory; } catch { }
            if (!mind) return "a espécie de " + Nm(npc) + " não sabe escrever livros";
            bool lang = false, cult = false; try { lang = npc.language != null; cult = npc.culture != null; } catch { }
            if (!lang) return Nm(npc) + " não tem uma língua escrita";
            if (!cult) return Nm(npc) + " não tem cultura (o jogo exige uma para escrever)";
            City c = null; try { c = npc.getCity(); } catch { }
            if (c == null) return Nm(npc) + " não mora em nenhuma cidade (o livro precisa ficar numa biblioteca ou estante da cidade)";
            bool slots = false; try { slots = c.hasBookSlots(); } catch { }
            if (!slots) return "a cidade " + c.name + " não tem lugar para livros (não há biblioteca ou estante, ou estão cheias)";
            return null;
        }

        public static WorldfallActions.Result WriteBook(Actor npc)
        {
            var res = new WorldfallActions.Result { Id = BookId, Pt = "escrever um livro sagrado", En = "write a holy book" };
            try
            {
                string why = BookCheck(npc);
                if (why != null) { res.Outcome = "falhou"; res.Reason = why; Main.LogInfo("[GodBox] ACTION write_book: " + Nm(npc) + " cannot write (" + why + ")."); return res; }
                var c = npc.getCity();
                Book b = null;
                try { b = World.world.books.generateNewBook(npc); } catch (Exception e) { Main.LogWarning("[GodBox] write_book: generateNewBook failed: " + e.Message); }
                if (b == null)
                {
                    res.Outcome = "falhou";
                    res.Reason = "o jogo não deixou " + Nm(npc) + " escrever agora (nenhum tipo de livro é possível para essa pessoa neste momento)";
                    Main.LogInfo("[GodBox] ACTION write_book: generateNewBook returned null for " + Nm(npc) + " in " + c.name + ".");
                    return res;
                }
                var rel = GodPowers.CurrentReligion();
                bool tied = false; try { tied = rel != null && b.data.religion_id == rel.id; } catch { }
                string title = null; try { title = b.name; } catch { }
                var g = MemoryStore.God(); g.HolyBooks++; MemoryStore.SaveGod();
                res.Outcome = "feito";
                res.Changes.Add(Nm(npc) + " escreveu o livro " + (string.IsNullOrEmpty(title) ? "" : "\"" + title + "\" ") + "e ele foi guardado em " + c.name);
                res.Changes.Add(tied ? "o livro leva a fé de " + (rel?.name ?? "Deus") + ": quem o ler pode se converter (como no jogo)"
                                     : "o jogo escolheu um tipo de livro que não carrega a religião; ler este livro não converte ninguém");
                res.Toasts.Add("Livro escrito por " + Nm(npc) + (string.IsNullOrEmpty(title) ? "" : ": " + title));
                string type = null; try { type = b.data.book_type; } catch { }
                Main.LogInfo("[GodBox] ACTION write_book: " + Nm(npc) + " wrote \"" + title + "\" (type " + type + ", religion " + (tied ? rel.name : "none") + ") via BookManager.generateNewBook; stored in " + c.name + ".");
                try { Rumors.Add(c, (Female(npc) ? "a profetisa " : "o profeta ") + Nm(npc) + " escreveu um livro" + (tied ? " da fé de " + rel.name : "") + " por ordem de Deus"); } catch { }
            }
            catch (Exception e) { res.Outcome = "falhou"; res.Reason = "erro: " + e.Message; Main.LogWarning("[GodBox] write_book failed: " + e); }
            return res;
        }

        // ------------------------------------------------------------------ K: proactive ask ----
        /// <summary>What a prophet / devout adherent may ask God about now: "temple", "book" or null.
        /// Not if asked in the last 5 game years.</summary>
        public static string OfferFor(Actor npc, UnitMemory mem)
        {
            try
            {
                var rel = GodPowers.CurrentReligion();
                if (rel == null || npc == null || npc.religion != rel) return null;
                bool devout = GodFaith.IsProphet(npc);
                if (!devout) { devout = GodPresence.Devout(npc); }
                if (!devout) return null;
                if (mem != null && mem.OfferAsked != null && Main.CurrentYear() - mem.OfferAskedYear < 5) return null;
                City c = null; try { c = npc.city; } catch { }
                if (c != null && !GodFaith.HasTemple(c)) return "temple";
                if (BookCheck(npc) == null) return "book";
                return null;
            }
            catch { return null; }
        }
    }
}
