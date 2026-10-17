using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gooby.Core.I18n;
using Gooby.Core.Life;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// Gooby's soul, second half (ports of test_soul_memories.gd, test_seele2_stimmung.gd, test_seele2_absicht.gd,
    /// test_seele2_gedaechtnis.gd, test_feelac_ausloeser.gd and the W14 line/dialogue gates): memories only from real
    /// data, wishes that come true, the inert mood, intents with a visible target, the feelings brake, chatter without
    /// repeats, the dialogue tree, the over-petting gag, idle acts, surprises and the birthday question.
    /// </summary>
    public class SoulLifeTests
    {
        private const long MsD = Soul.MsPerDay;
        private const long T0 = 1_700_000_000_000L;

        [SetUp]
        public void Setup() => Fixtures.InstallContentSource();

        private static Soul.Ctx Ctx(long nowMs, int hour = 12)
        {
            var d = DateTimeOffset.FromUnixTimeMilliseconds(nowMs).UtcDateTime;
            return new Soul.Ctx { NowMs = nowMs, Year = d.Year, Month = d.Month, Day = d.Day, Hour = hour, WetterTyp = "sonne", PlayerName = "Mira", Nickname = "Gooby" };
        }

        private static I18n Strings()
        {
            var dir = new DirectoryInfo(Fixtures.Root);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Assets"))) dir = dir.Parent;
            var root = Path.Combine(dir.FullName, "Assets", "Gooby", "Content", "Resources", "gooby", "strings");
            return new I18n(new IndexedStringSource(File.ReadAllText(Path.Combine(root, "index.json")),
                rel => File.Exists(Path.Combine(root, rel)) ? File.ReadAllText(Path.Combine(root, rel)) : null));
        }

        // ── memories ──

        [Test]
        public void NoDataNoMemories()
        {
            Assert.That(SoulMemories.Candidates(new JObject()), Is.Empty);
            var kaputt = JObject.Parse(@"{""minigames"":""quatsch"",""vacation"":{""visited"":""auch quatsch""},""achievements"":{""counters"":{""tickles"":""NaN""}},""daily"":{""streak"":null}}");
            Assert.That(SoulMemories.Candidates(kaputt), Is.Empty, "junk types stay quiet");
        }

        [Test]
        public void MemoriesComeFromRealData()
        {
            var rekord = SoulMemories.Candidates(JObject.Parse(@"{""minigames"":{""legacy"":{""best"":{""minigolf"":42,""sprint"":99}}}}"));
            Assert.That(rekord.Select(m => m.Id), Is.EqualTo(new[] { "rekord_sprint", "vorliebe_spiel" }));
            Assert.That(rekord[0].Args["punkte"], Is.EqualTo(99));
            Assert.That(SoulMemories.Candidates(JObject.Parse(@"{""minigames"":{""legacy"":{""best"":{""golf"":0}}}}")), Is.Empty, "a zero score is no record");

            Assert.That(SoulMemories.Candidates(JObject.Parse(@"{""vacation"":{""visited"":{""beach"":1}}}"))[0].Id, Is.EqualTo("urlaub_beach"));
            Assert.That(SoulMemories.Candidates(JObject.Parse(@"{""vacation"":{""visited"":{}}}")), Is.Empty);

            Assert.That(SoulMemories.Candidates(JObject.Parse(@"{""achievements"":{""counters"":{""tickles"":19}}}")), Is.Empty, "below the threshold");
            Assert.That(SoulMemories.Candidates(JObject.Parse(@"{""achievements"":{""counters"":{""tickles"":20}}}"))[0].Id, Is.EqualTo("kitzeln"));

            var mix = SoulMemories.Candidates(JObject.Parse(@"{""daily"":{""streak"":5},""profile"":{""playtimeMin"":600},""park"":{""visits"":2}}"));
            Assert.That(mix.Select(m => m.Id), Is.EqualTo(new[] { "streak", "funkelpark", "spielzeit" }), "stable order");
            Assert.That(mix[2].Args["stunden"], Is.EqualTo(10));

            var spaeter = SoulMemories.Candidates(JObject.Parse(@"{
                ""soul"":{""foodGiven"":{""apple"":2,""carrot"":4}},
                ""mcgooby"":{""schichten"":{""gespielt"":3,""bestwert"":80,""perfekt"":1}},
                ""dlc"":{""goobye"":{""umsatz"":{""tage"":2,""gesamt"":150},""transport"":{""lieferwagen"":true}}},
                ""ranch"":{""welt"":{""funde"":[""quelle"",""hoehle"",""quelle"",7,""turm""],""karteGesehen"":[""a""]}},
                ""minigames"":{""geist"":{""a"":{""score"":5},""b"":{""score"":9},""c"":{""score"":0}}},
                ""vacation"":{""heimkehrZiel"":""beach"",""heimkehrGefeiert"":true},
                ""inventory"":{""items"":{""mitbringsel_beach_muschel"":1,""mitbringsel_space_stein"":2,""seed"":4}}
            }")).Select(m => m.Id).ToList();
            Assert.That(spaeter, Is.EqualTo(new[]
            {
                "vorliebe_essen", "goobye_markttag", "goobye_umsatz", "goobye_lieferwagen",
                "mcgooby_schichten", "mcgooby_bestwert", "mcgooby_perfekt", "ranch_fund_hoehle", "ranch_funde", "ranch_karte",
                "geist_b", "geist_sammlung", "heimkehr_beach", "mitbringsel",
            }));
        }

        [Test]
        public void TitlesResolveThroughTheStrings()
        {
            var t = Strings();
            var m = SoulMemories.Candidates(JObject.Parse(@"{""vacation"":{""visited"":{""beach"":1}},""soul"":{""foodGiven"":{""carrot"":3}}}"), t);
            Assert.That(m[0].Args["ziel"], Is.EqualTo(t.T("travel.ziel.beach")));
            Assert.That(m[1].Args["essen"], Is.Not.EqualTo("carrot"), "the food's name, not its id");
        }

        [Test]
        public void PickHonoursTheCooldown()
        {
            var cands = new List<SoulMemories.Memory> { new SoulMemories.Memory { Id = "a" }, new SoulMemories.Memory { Id = "b" } };
            var now = 100 * MsD;
            Assert.That(SoulMemories.Pick(cands, new JObject(), now, 0.0).Id, Is.EqualTo("a"));
            Assert.That(SoulMemories.Pick(cands, new JObject(), now, 0.99).Id, Is.EqualTo("b"));
            Assert.That(SoulMemories.Pick(cands, new JObject { ["a"] = now - MsD }, now, 0.0).Id, Is.EqualTo("b"), "a cools down");
            Assert.That(SoulMemories.Pick(cands, new JObject { ["a"] = now - SoulMemories.MemoryCooldownMs }, now, 0.0).Id, Is.EqualTo("a"));
            Assert.That(SoulMemories.Pick(cands, new JObject { ["a"] = now - MsD, ["b"] = now - MsD }, now, 0.5), Is.Null, "silence beats repeating");
        }

        [Test]
        public void AMemoryComesOnEnteringAndCoolsDown()
        {
            var state = JObject.Parse(@"{""park"":{""visits"":3}}");
            var slice = Soul.Slice(state);
            slice["firstMetAt"] = T0 - 10 * MsD;
            slice["lastGreetDay"] = Ctx(T0).Today;
            slice["wunsch"] = new JObject { ["id"] = "urlaub", ["seitMs"] = T0 - MsD };
            var ctx = Ctx(T0);
            // roll 0.2 → pick roll 0.57 (past the wish share): the memory slot
            var m = Soul.DecideEnter(state, slice, Soul.Defs(), ctx, 0.2);
            Assert.That(m.Kind, Is.EqualTo("erinnerung"));
            Assert.That(m.TextKey, Is.EqualTo("soul.erinnerung.funkelpark"));
            Assert.That(m.Args["anzahl"], Is.EqualTo(3));
            Soul.BookEnter(slice, m, ctx);
            Assert.That(J(slice, "memoryShownAt", "funkelpark"), Is.EqualTo(T0));
            var later = Ctx(T0 + 120_000);
            Assert.That(Soul.DecideEnter(state, slice, Soul.Defs(), later, 0.2), Is.Null, "the only memory is cooling down");
            Assert.That(Soul.DecideEnter(state, slice, Soul.Defs(), Ctx(T0 + 240_000), 0.5), Is.Null, "above the 35 % gate: silence");
        }

        [Test]
        public void AWishIsFormedAndCelebratedWhenItComesTrue()
        {
            var state = new JObject();
            var slice = Soul.Slice(state);
            slice["firstMetAt"] = T0 - 5 * MsD;
            slice["lastGreetDay"] = Ctx(T0).Today;
            Assert.That(SoulMemories.OffeneWuensche(state, slice), Is.EqualTo(new[] { "funkelpark", "urlaub", "streak7", "kissenturm" }));
            var ctx = Ctx(T0);
            var wish = Soul.DecideEnter(state, slice, Soul.Defs(), ctx, 0.01);
            Assert.That(wish.Kind, Is.EqualTo("wunsch"));
            Assert.That(wish.TextKey, Is.EqualTo("soul.wunsch." + wish.WunschId));
            Soul.BookEnter(slice, wish, ctx);
            Assert.That(JStr(slice["wunsch"]["id"]), Is.EqualTo(wish.WunschId));

            // still open: no second wish while one is active, no celebration yet
            Assert.That(Soul.DecideEnter(state, slice, Soul.Defs(), Ctx(T0 + 200_000), 0.01)?.Kind, Is.Not.EqualTo("wunsch"));

            state["park"] = new JObject { ["visits"] = 1 };
            state["vacation"] = new JObject { ["visited"] = new JObject { ["beach"] = 1 } };
            state["daily"] = new JObject { ["streak"] = 7 };
            slice["surpriseAt"] = new JObject { ["sup_turm"] = T0 };
            var ctx2 = Ctx(T0 + 400_000);
            var party = Soul.DecideEnter(state, slice, Soul.Defs(), ctx2, 0.9);
            Assert.That(party.Kind, Is.EqualTo("wunsch_erfuellt"), "a fulfilled wish is celebrated for sure (no roll gate)");
            Assert.That(party.TextKey, Is.EqualTo("soul.wunsch." + wish.WunschId + "_erfuellt"));
            Assert.That(party.Aktion, Is.EqualTo("konfetti"));
            Soul.BookEnter(slice, party, ctx2);
            Assert.That(((JObject)slice["wunsch"]).HasValues, Is.False);
            Assert.That(SoulMemories.OffeneWuensche(state, slice), Is.Empty);
        }

        [Test]
        public void NoWishesInTheFirstDays()
        {
            var state = new JObject();
            var slice = Soul.Slice(state);
            slice["firstMetAt"] = T0 - MsD;
            slice["lastGreetDay"] = Ctx(T0).Today;
            Assert.That(Soul.DecideEnter(state, slice, Soul.Defs(), Ctx(T0), 0.01), Is.Null);
        }

        // ── mood ──

        [Test]
        public void MoodIsInertAndCapped()
        {
            Assert.That(SoulMood.Wert(JObject.Parse(@"{""wert"":""x"",""aktualisiertMs"":-4}")), Is.EqualTo(SoulMood.DefaultWert));
            Assert.That(SoulMood.Wert(JObject.Parse(@"{""wert"":400}")), Is.EqualTo(100.0));
            var start = new JObject { ["wert"] = 80.0, ["aktualisiertMs"] = T0 };
            Assert.That(SoulMood.Wert(SoulMood.Advance(start, 20.0, T0 + 90 * 60_000)), Is.EqualTo(50.0).Within(0.01), "half-life 90 min");
            Assert.That(SoulMood.Wert(SoulMood.Advance(start, 20.0, T0)), Is.EqualTo(80.0), "dt 0 → unchanged");
            var lang = SoulMood.Wert(SoulMood.Advance(start, 20.0, T0 + 3 * MsD));
            Assert.That(lang, Is.GreaterThanOrEqualTo(20.0).And.LessThan(21.0), "a long gap converges, never overshoots");
            Assert.That(SoulMood.Wert(SoulMood.Advance(SoulMood.Default(), 10.0, T0)), Is.EqualTo(SoulMood.DefaultWert), "the first tick does not jump");
            Assert.That(SoulMood.Wert(SoulMood.Bump(start, 30.0, T0)), Is.EqualTo(88.0), "bumps are capped at 8");
            Assert.That(SoulMood.Wert(SoulMood.Bump(new JObject { ["wert"] = 2.0 }, -8.0, T0)), Is.EqualTo(0.0));
        }

        [Test]
        public void TheRestingFaceFollowsTheMood()
        {
            var fit = JObject.Parse(@"{""hunger"":80,""energy"":80,""fun"":80,""hygiene"":80}");
            Assert.That(SoulMood.RuheEmotion(85, fit), Is.EqualTo("ecstatic"));
            Assert.That(SoulMood.RuheEmotion(70, fit), Is.EqualTo("happy"));
            Assert.That(SoulMood.RuheEmotion(50, fit), Is.EqualTo("neutral"));
            Assert.That(SoulMood.RuheEmotion(30, fit), Is.EqualTo("angry"));
            Assert.That(SoulMood.RuheEmotion(10, fit), Is.EqualTo("sad"));
            Assert.That(SoulMood.RuheEmotion(70, JObject.Parse(@"{""energy"":10}")), Is.EqualTo("sleepy"));
            Assert.That(SoulMood.RuheEmotion(90, fit, 1), Is.EqualTo("sad"), "sick → sad");
            Assert.That(SoulMood.IdleTaktFaktor(10), Is.GreaterThan(SoulMood.IdleTaktFaktor(90)), "a low mood idles slower");
            var tanzen = JObject.Parse(@"{""mindest_laune"":""happy""}");
            Assert.That(SoulMood.DefErlaubt(tanzen, "ecstatic") && SoulMood.DefErlaubt(tanzen, "happy"), Is.True);
            Assert.That(SoulMood.DefErlaubt(tanzen, "neutral"), Is.False);
            Assert.That(SoulMood.DefErlaubt(JObject.Parse(@"{""hoechst_laune"":""grumpy""}"), "happy"), Is.False);
            Assert.That(SoulMood.DefErlaubt(new JObject(), "miserable"), Is.True);
        }

        // ── intents ──

        [Test]
        public void IntentsNeedAVisibleTarget()
        {
            var hungry = JObject.Parse(@"{""hunger"":20,""energy"":90,""fun"":90,""hygiene"":90}");
            var fridge = new HashSet<string> { "item:kitchenFridge", "tuer:kitchen" };
            var a = SoulIntent.Waehle(hungry, false, false, fridge, null, T0);
            Assert.That((a.Id, a.ZielArt, a.Ziel), Is.EqualTo(("absicht_hunger", "item", "kitchenFridge")));
            a = SoulIntent.Waehle(hungry, false, false, new HashSet<string> { "tuer:kitchen" }, null, T0);
            Assert.That((a.ZielArt, a.Ziel), Is.EqualTo(("tuer", "kitchen")), "no fridge here: towards the kitchen door");
            Assert.That(SoulIntent.Waehle(hungry, false, false, new HashSet<string>(), null, T0), Is.Null, "wanting without a where looks broken");
            Assert.That(SoulIntent.Waehle(hungry, false, true, fridge, null, T0), Is.Null, "asleep: no intents");

            var both = JObject.Parse(@"{""hunger"":30,""energy"":5,""fun"":90,""hygiene"":90}");
            var ziele = new HashSet<string> { "item:kitchenFridge", "item:bed" };
            Assert.That(SoulIntent.Kandidaten(both, true, false, ziele).Select(k => k.Id), Is.EqualTo(new[] { "absicht_muede", "absicht_hunger", "absicht_regen" }), "most urgent first, rain last");
            var cd = new Dictionary<string, long> { ["absicht_muede"] = T0 + SoulIntent.CooldownMs };
            Assert.That(SoulIntent.Waehle(both, false, false, ziele, cd, T0).Id, Is.EqualTo("absicht_hunger"), "the tired one cools down");
            Assert.That(SoulIntent.Waehle(JObject.Parse(@"{""hygiene"":10}"), false, false, new HashSet<string> { "item:shower" }, null, T0).Ziel, Is.EqualTo("shower"));
        }

        // ── feelings ──

        [Test]
        public void FeelingsHaveTheirOwnBrake()
        {
            var today = "2026-10-11";
            var f = SoulFeelings.Default();
            Assert.That(SoulFeelings.Erlaubt(f, "fund", T0, today), Is.True);
            f = SoulFeelings.Buche(f, "fund", T0, today);
            Assert.That(SoulFeelings.Erlaubt(f, "ertappt", T0 + 5_000, today), Is.False, "20 s between two");
            Assert.That(SoulFeelings.Erlaubt(f, "donner", T0 + 5_000, today), Is.True, "prio 3 overrides the gap");
            Assert.That(SoulFeelings.Erlaubt(f, "geschenk", T0 + 60_000, today), Is.False, "same emotion (surprise) waits 4 min");
            Assert.That(SoulFeelings.Erlaubt(f, "geschenk", T0 + SoulFeelings.JeEmotionGapMs, today), Is.True);
            f = SoulFeelings.Buche(f, "dunkelheit", T0 + 300_000, today);
            Assert.That(SoulFeelings.Erlaubt(f, "dunkelheit", T0 + 3 * 3_600_000, today), Is.False, "the dark once a day");
            Assert.That(SoulFeelings.Erlaubt(f, "dunkelheit", T0 + 3 * 3_600_000, "2026-10-12"), Is.True);
            for (var i = 0; i < SoulFeelings.MaxProTag; i++) f = SoulFeelings.Buche(f, "essen", T0 + i, today);
            Assert.That(SoulFeelings.Erlaubt(f, "donner", T0 + MsD / 2, today), Is.False, "25 a day");
            Assert.That(SoulFeelings.Erlaubt(f, "donner", T0 + MsD / 2, "2026-10-12"), Is.True, "a new day resets the count");
            Assert.That(SoulFeelings.Erlaubt(f, "quatsch", T0, today), Is.False);
            Assert.That(SoulFeelings.IstDunkel(23) && SoulFeelings.IstDunkel(3) && !SoulFeelings.IstDunkel(12), Is.True);
            Assert.That(SoulFeelings.RekordMax(JObject.Parse(@"{""minigames"":{""legacy"":{""best"":{""a"":5},""bestByDiff"":{""b"":{""hard"":12}}}}}")), Is.EqualTo(12));
            Assert.That(SoulFeelings.IstLieblingsessen(JObject.Parse(@"{""a"":3,""b"":2}"), "a", 3), Is.True);
            Assert.That(SoulFeelings.IstLieblingsessen(JObject.Parse(@"{""a"":3,""b"":4}"), "a", 3), Is.False);
        }

        // ── lines and dialogues ──

        [Test]
        public void ChatterNeverRepeatsTheLastFive()
        {
            Assert.That(SoulLinien.GesamtAnzahl(), Is.EqualTo(124));
            var mem = new JObject();
            var seen = new List<string>();
            for (var i = 0; i < 6; i++)
            {
                var key = SoulLinien.Waehle("selbstgespraech", mem, 0.0);
                Assert.That(seen.Skip(Math.Max(0, seen.Count - 5)), Does.Not.Contain(key));
                seen.Add(key);
                mem = SoulLinien.Merke(mem, "selbstgespraech", key);
            }
            Assert.That(((JArray)mem["selbstgespraech"]).Count, Is.EqualTo(SoulLinien.GedaechtnisMax));
            var klein = SoulLinien.Merke(new JObject(), "wetter.wolken", "soul.linie.wetter.wolken.1");
            Assert.That(SoulLinien.Waehle("wetter.wolken", klein, 0.3), Is.EqualTo("soul.linie.wetter.wolken.1"), "a one-line pool still speaks");
            Assert.That(SoulLinien.Waehle("gibtsnicht", mem, 0.3), Is.EqualTo(""));
            Assert.That(SoulLinien.WiedersehenKey(5 * 3_600_000L), Is.EqualTo(""));
            Assert.That(SoulLinien.WiedersehenKey(30 * 3_600_000L), Is.EqualTo("soul.linie.wiedersehen.2"));
            Assert.That(SoulLinien.WiedersehenKey(40 * MsD), Is.EqualTo("soul.linie.wiedersehen.5"));
            Assert.That(SoulLinien.PlauderKategorie("regen", 0.1), Is.EqualTo("wetter.regen"));
            Assert.That(SoulLinien.PlauderKategorie("wolken", 0.1), Is.EqualTo("selbstgespraech"));
            Assert.That(SoulLinien.PlauderKategorie("regen", 0.9), Is.EqualTo("selbstgespraech"));
        }

        [Test]
        public void EverySoulKeyExistsInBothLanguages()
        {
            var t = Strings();
            var keys = SoulLinien.Kategorien.Keys.SelectMany(SoulLinien.KeysOf).ToList();
            keys.AddRange(SoulMemories.WunschIds.SelectMany(w => new[] { "soul.wunsch." + w, "soul.wunsch." + w + "_erfuellt" }));
            foreach (var g in SoulGespraech.Lade().OfType<JObject>())
                foreach (var a in Answers(g))
                {
                    keys.Add(JStr(a["label_key"]));
                    keys.AddRange(((JArray)a["folge_keys"]).Select(k => (string)k));
                }
            foreach (var def in Soul.Defs().OfType<JObject>())
                keys.AddRange((def["text_keys"] as JArray ?? new JArray()).Select(k => (string)k));
            foreach (var e in SoulFeelings.Ereignisse.Values.Where(e => e.prio >= 2).Select(e => e.emotion).Distinct())
                keys.AddRange(new[] { "soul.feel." + e + ".a", "soul.feel." + e + ".b" });
            foreach (var loc in new[] { "de", "en" })
            {
                t.SetLocale(loc);
                var missing = keys.Where(k => !t.HasKey(k)).ToList();
                Assert.That(missing, Is.Empty, loc + ": " + string.Join(", ", missing));
            }
        }

        private static IEnumerable<JObject> Answers(JObject ebene)
        {
            foreach (var a in SoulGespraech.Antworten(ebene))
            {
                yield return a;
                var next = SoulGespraech.NaechsteEbene(a);
                if (next != null) foreach (var b in Answers(next)) yield return b;
            }
        }

        [Test]
        public void DialoguesDockToMomentsAndAreRemembered()
        {
            var all = SoulGespraech.Lade();
            Assert.That(all.Count, Is.EqualTo(12));
            var gewitter = SoulGespraech.FuerAnlass(all, "wetter_gewitter");
            Assert.That(JStr(gewitter["id"]), Is.EqualTo("gewitter"));
            Assert.That(SoulGespraech.FuerAnlass(all, "ritual_ostern"), Is.Null);
            var troesten = SoulGespraech.Antworten(gewitter)[0];
            Assert.That(SoulGespraech.FolgeKey(troesten, 0.99), Is.EqualTo("gespraech.gewitter.troesten.f2"));
            Assert.That(SoulGespraech.Antworten(SoulGespraech.NaechsteEbene(troesten)).Count, Is.EqualTo(2));
            Assert.That(SoulGespraech.NaechsteEbene(SoulGespraech.Antworten(gewitter)[1]), Is.Null);
            Assert.That(SoulGespraech.GespraechIdOf(troesten), Is.EqualTo("gewitter"));
            var mem = SoulGespraech.MerkeAntwort(new JObject(), "gewitter", troesten, T0);
            mem = SoulGespraech.MerkeAntwort(mem, "gewitter", troesten, T0 + 5);
            Assert.That(J(mem, "gewitter", "anzahl"), Is.EqualTo(2L));
            Assert.That(JStr(mem["gewitter"]["erinnerung"]), Is.EqualTo("getroestet"));
        }

        // ── taps, idle acts, surprises, birthday ──

        [Test]
        public void TappingGigglesThenTicklesThenMakesDizzy()
        {
            Assert.That(Enumerable.Range(1, 7).Select(Soul.TapStage), Is.EqualTo(new[] { "tipp_kicher", "", "tipp_kitzlig", "", "", "tipp_schwindelig", "tipp_schwindelig" }));
            Assert.That(Soul.PetPitch(1), Is.EqualTo(1.0));
            Assert.That(Soul.PetPitch(10), Is.EqualTo(1.36).Within(1e-9));
            Assert.That(Soul.PetPitch(11), Is.EqualTo(1.0), "the ladder starts again after the bonus");
            Assert.That(Soul.PetBonusDue(10) && Soul.PetBonusDue(20) && !Soul.PetBonusDue(9), Is.True);
            var state = new JObject();
            Assert.That(Soul.CountPet(state, "2026-10-11"), Is.EqualTo(1));
            Assert.That(Soul.CountPet(state, "2026-10-11"), Is.EqualTo(2));
            Assert.That(Soul.CountPet(state, "2026-10-12"), Is.EqualTo(1), "a new day");

            var gag = new StreichelUebermut();
            for (var i = 0; i < 10; i++) Assert.That(gag.Registriere(T0 + i * 1000), Is.False);
            Assert.That(gag.Registriere(T0 + 10_500), Is.True, "the eleventh pet within 30 s");
            for (var i = 0; i < 12; i++) Assert.That(gag.Registriere(T0 + 11_000 + i * 100), Is.False, "two quiet minutes");
            Assert.That(gag.Registriere(T0 + 10_500 + StreichelUebermut.CooldownMs + 1), Is.False, "the window starts afresh");
        }

        [Test]
        public void IdleActsFollowTheRoomAndTheMood()
        {
            var defs = Soul.Defs();
            var ctx = new Soul.IdleCtx { NowMs = T0, LauneBand = "miserable" };
            var seen = new HashSet<string>();
            for (var r = 0.0; r < 1.0; r += 0.01) seen.Add(JStr(Soul.PickIdle(defs, ctx, null, r)?["id"]));
            Assert.That(seen, Is.EquivalentTo(new[] { "idle_doesen", "idle_fenster" }), "miserable: no tidying, TV or dancing; no TV/radio/favourite here");
            ctx = new Soul.IdleCtx { NowMs = T0, LauneBand = "happy", HatTv = true, RadioAn = true, HatFav = true };
            seen.Clear();
            for (var r = 0.0; r < 1.0; r += 0.01) seen.Add(JStr(Soul.PickIdle(defs, ctx, null, r)?["id"]));
            Assert.That(seen.Count, Is.EqualTo(6));
            var cd = defs.OfType<JObject>().Where(d => JStr(d["kind"]) == "idle").ToDictionary(d => JStr(d["id"]), _ => T0 + 1);
            Assert.That(Soul.PickIdle(defs, ctx, cd, 0.5), Is.Null, "everything cooling down");
        }

        [Test]
        public void SurprisesAreRareAndCoolDown()
        {
            var slice = Soul.Slice(new JObject());
            var ctx = Ctx(T0);
            Assert.That(Soul.PickSurprise(slice, Soul.Defs(), ctx, 0.0, 0.99), Is.Null, "the chance gate");
            var s = Soul.PickSurprise(slice, Soul.Defs(), ctx, 0.0, 0.0);
            Assert.That(s.Kind, Is.EqualTo("ueberraschung"));
            Soul.BookSurprise(slice, s, T0);
            var again = Soul.PickSurprise(slice, Soul.Defs(), Ctx(T0 + 60_000), 0.0, 0.0);
            Assert.That(again?.Id, Is.Not.EqualTo(s.Id), "the same surprise waits its cooldown");
            var ids = Soul.Defs().OfType<JObject>().Where(d => JStr(d["kind"]) == "ueberraschung").Select(d => JStr(d["id"])).ToList();
            foreach (var id in ids) J2(slice, "surpriseAt")[id] = T0;
            Assert.That(Soul.PickSurprise(slice, Soul.Defs(), Ctx(T0 + 3_600_000), 0.5, 0.0), Is.Null);
        }

        [Test]
        public void TheBirthdayQuestionWaitsAndIsRemembered()
        {
            var slice = Soul.Slice(new JObject());
            slice["firstMetAt"] = T0 - MsD;
            Assert.That(Soul.ShouldAskBirthday(slice, Ctx(T0)), Is.False, "not on the first days");
            slice["firstMetAt"] = T0 - 4 * MsD;
            Assert.That(Soul.ShouldAskBirthday(slice, Ctx(T0)), Is.True);
            slice["askedBirthdayAt"] = T0 - MsD;
            Assert.That(Soul.ShouldAskBirthday(slice, Ctx(T0)), Is.False, "at most every 7 days");
            slice["askedBirthdayAt"] = T0 - 8 * MsD;
            Assert.That(Soul.SetPlayerBirthday(slice, 2, 30), Is.False);
            Assert.That(Soul.SetPlayerBirthday(slice, 2, 29), Is.True);
            Assert.That(Soul.ShouldAskBirthday(slice, Ctx(T0)), Is.False, "known now");
            var heal = Soul.Slice(JObject.Parse(@"{""soul"":{""playerBirthday"":{""month"":13,""day"":40},""stimmung"":""x"",""feelings"":3}}"));
            Assert.That(J(heal, "playerBirthday", "month"), Is.EqualTo(0L));
            Assert.That(SoulMood.Wert(heal["stimmung"]), Is.EqualTo(SoulMood.DefaultWert));
        }

        [Test]
        public void FavouritesAndNewFurniture()
        {
            var state = new JObject();
            Assert.That(Soul.NoteFoodGiven(state, "carrot"), Is.EqualTo(1));
            Assert.That(Soul.NoteFoodGiven(state, "carrot"), Is.EqualTo(2));
            var slice = Soul.Slice(state);
            slice["firstMetAt"] = 7L;
            Assert.That(Soul.PickFavFurniture(slice, new[] { "sofa", "bear", "lamp" }), Is.EqualTo("lamp"), "sorted bear, lamp, sofa: 7 % 3 = 1");
            Assert.That(Soul.PickFavFurniture(slice, new[] { "sofa", "zebra" }), Is.EqualTo("lamp"), "picked once, kept");
            Assert.That(Soul.FreshItems(slice, new[] { "sofa", "bear" }), Is.Empty, "the first look only remembers");
            Assert.That(Soul.FreshItems(slice, new[] { "sofa", "bear", "radio", "radio" }), Is.EqualTo(new[] { "radio" }));
            Assert.That(Soul.FreshItems(slice, new[] { "sofa" }), Is.Empty);
        }

        private static string JStr(JToken t) => t == null ? "" : (string)t;

        private static long J(JObject o, params string[] path)
        {
            JToken cur = o;
            foreach (var p in path) cur = cur?[p];
            return cur == null ? -1 : (long)cur;
        }

        private static JObject J2(JObject o, string key) => (JObject)o[key];
    }
}
