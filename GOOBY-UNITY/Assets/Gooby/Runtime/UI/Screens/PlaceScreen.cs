using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.City;
using Gooby.Core.Life;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Inside a place along Sonnental's main street (port of ort_scene.gd + orte/*.gd): the world stages the
    /// place's interior with its shopkeeper Gooby, this screen lays the chrome over it — "Raus" button, place pill,
    /// coin pill — and runs the shopkeeper's dialog tree from city/dialoge in the concept bubble look, one line at
    /// a time with answer chips. Effects are applied when a node is entered: persistent city flags, items into the
    /// bag, the vet's markers, and "laden", which opens the place's counter (shop, offers, market, dealer, post,
    /// park rides, travel desk). Places without a tree (Funkelpark, airport) greet with a fixed line and open their
    /// counter from a chip.
    /// </summary>
    public sealed partial class PlaceScreen : UiScreen
    {
        private readonly GameHost _host;

        public override string SoundScene => _place.Id == "raumstation" ? "space" : "city/ort/" + _place.Id;
        private readonly DriveModel.Place _place;
        private DialogRunner _runner;
        private Label _line, _hint, _coins;
        private VisualElement _chips, _card;
        private List<string> _lines = new List<string>();
        private int _lineIndex;
        private string _appliedNode;
        private IVisualElementScheduledItem _sequence;
        private bool _leaving;

        public DialogRunner Runner => _runner;
        public DriveModel.Place Place => _place;

        /// <summary>The screen of a place: the walkable Funkelpark has its own, every other place the board-10 interior.</summary>
        public static UiScreen For(GameHost host, DriveModel.Place place) =>
            place.Id == "funkelpark" ? new FunkelparkScreen(host, place) : (UiScreen)new PlaceScreen(host, place);

        public PlaceScreen(GameHost host, DriveModel.Place place)
        {
            _host = host;
            _place = place;
            Id = "place-" + place.Id;
            Opaque = true;
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.pickingMode = PickingMode.Ignore;
            if (_host.World != null && _host.World.Interior?.Place != _place) _host.World.EnterInterior(_place);
            root.schedule.Execute(StartLeben).StartingIn(400);
            if (_place.Counter == DriveModel.CounterPark) ParkLogic.BesuchBuchen(_host.Store, ParkLogic.IstNacht(_host.World?.LocalHour ?? 12.0));
            _accent = Accent();
            root.AddToClassList("gb-place-chrome"); // board 10: back disc, tabs, coins and gear are the HUD's dark glass

            // top-left (board 10): the back disc, then the place card — coloured icon tile, name and slogan
            var back = Ux.IconBtn("back_white", Leave, "gb-place-back");
            back.name = "leave-place";
            Ux.Abs(back, 16, 16);
            root.Add(back);
            var head = Ux.El("gb-card", "gb-row");
            Ux.Abs(head, 84, 12);
            head.style.paddingLeft = 8; head.style.paddingTop = 6; head.style.paddingBottom = 6; head.style.paddingRight = 18;
            var tile = Ux.IconTile(_place.Icon, _accent);
            tile.style.borderTopLeftRadius = 22; tile.style.borderTopRightRadius = 22; tile.style.borderBottomLeftRadius = 22; tile.style.borderBottomRightRadius = 22;
            head.Add(tile);
            var hcol = Ux.Col();
            hcol.style.marginLeft = 10;
            hcol.Add(Ux.Text(Title(), "gb-place-title"));
            _hint = Ux.Text(Slogan(), "gb-sm", "gb-mild");
            hcol.Add(_hint);
            head.Add(hcol);
            root.Add(head);

            // top-right: coins + settings
            var tr = Ux.Row("gb-place-top");
            Ux.Abs(tr, null, 12, 16);
            var coinPill = Ux.Pill("coin", Ux.FormatNumber(ShopLogic.Coins(_host.Store)), out _coins);
            coinPill.style.marginRight = 10;
            tr.Add(coinPill);
            tr.Add(Ux.IconBtn("settings_white", () => _host.Ui.Push(new SettingsScreen(_host)), "gb-btn-icon-sm"));
            root.Add(tr);
            _host.Store.CoinsChanged += OnCoins;

            // left: the tab column of the place (first tab = the shopkeeper's talk, the counter tab opens the sheet)
            var tabs = Ux.El("gb-place-tabs");
            var accentClass = AccentClass();
            foreach (var (id, icon, action) in Tabs())
            {
                var tabId = id;
                var tabAction = action;
                var b = new Button(() => TabTapped(tabId, tabAction)) { text = "" };
                b.ClearClassList();
                b.AddToClassList("gb-tab");
                b.AddToClassList(accentClass);
                b.name = "tab-" + id;
                b.Add(Ux.IconTile(icon));
                b.Add(Ux.Text(t.T("nl.ort.tab." + id)));
                _tabs[id] = b;
                tabs.Add(b);
            }
            root.Add(tabs);
            SetTab(Tabs()[0].id);

            // bottom: the dialog card — avatar, coloured name, the line, action buttons, "more" at the right
            var card = _card = Ux.El("gb-dialog-card");
            card.name = "dialog-card";
            var top = Ux.Row();
            top.style.alignItems = Align.FlexStart;
            var avatar = Ux.El("gb-dialog-avatar");
            avatar.style.backgroundColor = Color.Lerp(_accent, Color.white, 0.55f);
            var face = Ux.ImageTexture("gooby_portrait");
            if (face != null) { avatar.style.backgroundImage = new StyleBackground(face); avatar.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover); }
            top.Add(avatar);
            var textCol = Ux.Col();
            textCol.style.flexGrow = 1; textCol.style.flexShrink = 1;
            _name = Ux.Text(SpeakerName().ToUpperInvariant(), "gb-dialog-name");
            _name.style.color = Color.Lerp(_accent, new Color(0.23f, 0.21f, 0.31f), 0.35f);
            textCol.Add(_name);
            _line = Ux.Text("", "gb-dialog-text");
            _line.style.flexShrink = 1;
            _line.style.minHeight = 44;
            textCol.Add(_line);
            top.Add(textCol);
            card.Add(top);
            _chips = Ux.El("gb-dialog-actions");
            _chips.style.flexWrap = Wrap.Wrap;
            _chips.style.marginRight = 56;
            card.Add(_chips);
            // board 10: "…" sits at the bottom right, in the row of the answers
            var more = Ux.IconBtn("dots", ToggleMore, "gb-btn-icon-sm", "gb-btn-secondary", "gb-dialog-more");
            more.name = "more-place";
            card.Add(more);
            root.Add(card);

            // the market square outside Saturday 8–14: no Greta talk, just the closed sign and the own stall
            var marketClosed = _place.Id == "wochenmarkt" && !MarktOffen();
            _runner = new DialogRunner(marketClosed ? null : DialogRunner.Laden(_place.Dialog, _host.Settings.Locale), StartFlags());
            if (_runner.IstGeladen) ShowNode();
            else
            {
                // no tree: a fixed welcome line, then the counter / leave chips
                _lines = new List<string> { t.T(marketClosed ? "nl.markt.zu_begruessung" : _place.Counter == DriveModel.CounterPark ? "nl.ort.willkommen_park" : _place.Counter == DriveModel.CounterTravel ? "nl.ort.willkommen_flughafen" : "nl.stadt.willkommen") };
                _lineIndex = 0;
                ShowLine();
            }
        }

        private Color _accent;
        private Label _name;
        private readonly Dictionary<string, Button> _tabs = new Dictionary<string, Button>();
        private string _activeTab;
        private bool _moreOpen;

        /// <summary>Accent colour of the kind of place (board 10: shops rose, practices and the market green, the dealer blue, post yellow).</summary>
        private Color Accent()
        {
            switch (_place.Id)
            {
                case "tierarzt": case "gouhbus": case "wochenmarkt": case "goobytheke": return ArtColor("#7FCF9B");
                case "autohaus": case "flughafen": case "raumstation": return ArtColor("#7FB8FF");
                case "post": return ArtColor("#FFD166");
                default: return ArtColor("#FF8B8B");
            }
        }

        private string AccentClass()
        {
            switch (_place.Id)
            {
                case "tierarzt": case "gouhbus": case "wochenmarkt": case "goobytheke": return "gb-tab--gruen";
                case "autohaus": case "flughafen": case "raumstation": return "gb-tab--blau";
                case "post": return "gb-tab--gelb";
                default: return "gb-tab--rosa";
            }
        }

        private static Color ArtColor(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        /// <summary>Tabs of the left column per kind of place (board 10); "counter" opens the place's sheet, "talk" the shopkeeper.</summary>
        private (string id, string icon, string action)[] Tabs()
        {
            switch (_place.Id)
            {
                case "tierarzt": case "gouhbus":
                    return new[] { ("wartezimmer", "heart", "talk"), ("behandlung", "plus", "talk"), ("pflegeprodukte", "sparkle", "info"), ("termin", "sun", "info"), ("haustier", "carrot", "pflege") };
                case "wochenmarkt":
                    return new[] { ("staende", "leaf", "counter"), ("meinstand", "shop", "stand"), ("saison", "sun", "saison"), ("rezepte", "book", "info") };
                case "autohaus":
                    return new[] { ("fahrzeuge", "car", "counter"), ("beratung", "chat", "info"), ("probefahrt", "compass", "counter"), ("service", "settings", "info"), ("finanzierung", "coin", "info") };
                case "post":
                    return new[] { ("schalter", "mail", "counter"), ("pakete", "gift", "paket"), ("postkarten", "camera", "postkarten"), ("briefkasten", "home", "briefkasten") };
                case "funkelpark":
                    return new[] { ("kasse", "star", "counter"), ("fahrten", "sparkle", "counter"), ("naschgasse", "heart", "counter"), ("parkplan", "map", "counter") };
                case "flughafen":
                    var air = new System.Collections.Generic.List<(string id, string icon, string action)>
                    {
                        ("checkin", "compass", "counter"), ("gfree", "gift", "gfree"), ("abfluege", "globe", "counter"), ("reiseziele", "map", "counter"), ("gepaeck", "shop", "info"),
                    };
                    if (Raumstation.Freigeschaltet(_host.Store.State))
                        air.Insert(0, ("shuttle", "sparkle", "shuttle"));
                    return air.ToArray();
                case "raumstation":
                    return new[] { ("talk", "chat", "talk"), ("terminals", "gamepad", "terminals"), ("automat", "shop", "counter"), ("foto", "camera", "foto") };
                case "baumarkt":
                    return new[] { ("shop", "hammer", "counter"), ("material", "shop", "counter"), ("bauplaene", "book", "counter"), ("kasse", "coin", "treue") };
                default:
                    return new[] { ("shop", "shop", "counter"), ("angebote", "star", "counter"), ("sammeln", "gift", "treue"), ("kasse", "coin", "treue") };
            }
        }

        private void TabTapped(string id, string action)
        {
            SetTab(id);
            var t = _host.Strings;
            switch (action)
            {
                case "counter":
                    if (_place.HasCounter) OpenCounter();
                    else if (_runner.IstGeladen) ShowNode();
                    break;
                case "shuttle":
                    OpenStation();
                    break;
                case "gfree":
                    // GOOBY FREE opens only before a departure (or in the souvenir window) — otherwise the clerk says so
                    if (GoobyFree.Offen(_host.Store.State, _host.Store.Clock.NowMs())) _host.Ui.Push(new GoobyFreeScreen(_host));
                    else SayAside(t.T("gfree.zu"));
                    break;
                case "stand":
                    OpenStand();
                    break;
                case "treue":
                    // the loyalty card and the till's receipt (ShopTreue)
                    if (_host.Ui.Current == this) _host.Ui.Push(new TreueScreen(_host, _place));
                    break;
                case "paket":
                    if (_host.Ui.Current == this) _host.Ui.Push(new PackageScreen(_host, SayAside));
                    break;
                case "postkarten":
                    if (_host.Ui.Current == this) _host.Ui.Push(new PostcardsScreen(_host));
                    break;
                case "briefkasten":
                    if (_host.Ui.Current == this) _host.Ui.Push(new MailboxScreen(_host));
                    break;
                case "pflege":
                    if (_host.Ui.Current == this) _host.Ui.Push(new CareSheetScreen(_host, SayAside));
                    break;
                case "saison":
                    SayAside(t.T("nl.ort.saison_heute", ("liste", MarktGefragt())));
                    break;
                case "terminals":
                    // GOOB-1's terminals play the two space games
                    _line.text = t.T("nl.ort.tabinfo.terminals");
                    _chips.Clear();
                    foreach (var spiel in new[] { "rocketRescue", "starHopper" })
                    {
                        var spielId = spiel;
                        AddChip(t.T("mg." + spielId + ".title"), () => { var game = MinigameHostScreen.For(_host, spielId); if (game != null && _host.Ui.Current == this) _host.Ui.Push(game); }, "gb-btn-primary", "gamepad")
                            .name = "terminal-" + spielId;
                    }
                    AddChip(t.T("nl.ort.weiter"), ShowLine, "gb-btn-confirm");
                    break;
                case "foto":
                    Sternenfoto();
                    break;
                case "info":
                    var key = "nl.ort.tabinfo." + id;
                    if (t.HasKey(key)) SayAside(t.T(key));
                    break;
                default:
                    if (_runner.IstGeladen) ShowNode(); else ShowLine();
                    break;
            }
        }

        /// <summary>What Greta pays most for today (the next unit's price after today's sales), best three.</summary>
        private string MarktGefragt()
        {
            var t = _host.Strings;
            var heute = _host.Store.Clock.LocalDay();
            return string.Join(", ", MarktLogic.Sorten()
                .Select(s => (s, preis: MarktLogic.Stueckpreis(s.Id, MarktLogic.HeuteVerkauft(_host.Store, heute, s.Id))))
                .OrderByDescending(x => x.preis).Take(3)
                .Select(x => (t.HasKey("rewards.food." + x.s.Id) ? t.T("rewards.food." + x.s.Id) : x.s.NameDe) + " " + x.preis));
        }

        /// <summary>A one-off line from the shopkeeper (tab hints) that returns to the tree with "Weiter".</summary>
        private void SayAside(string text)
        {
            _line.text = text;
            _chips.Clear();
            AddChip(_host.Strings.T("nl.ort.weiter"), () => { if (_runner.IstGeladen) ShowLine(); else ShowLine(); }, "gb-btn-confirm");
        }

        private void SetTab(string id)
        {
            _activeTab = id;
            foreach (var kv in _tabs) kv.Value.EnableInClassList("gb-tab--active", kv.Key == id);
        }

        public string ActiveTab => _activeTab;

        /// <summary>"…": the extra actions — leave the place (the tree's own options stay in the action row).</summary>
        private void ToggleMore()
        {
            _moreOpen = !_moreOpen;
            if (!_moreOpen) { ShowLine(); return; }
            _chips.Clear();
            if (_place.HasCounter) AddChip(_host.Strings.T("nl.ort.zum_tresen"), OpenCounter, "gb-btn-secondary");
            if (_place.Id == "flughafen" && Raumstation.Freigeschaltet(_host.Store.State))
                AddChip(_host.Strings.T("gfree.shuttle_knopf"), OpenStation, "gb-btn-secondary");
            AddChip(_host.Strings.T("nl.ort.raus"), Leave, "gb-btn-secondary");
            AddChip(_host.Strings.T("nl.ort.weiter"), () => { _moreOpen = false; ShowLine(); }, "gb-btn-confirm");
        }

        private string Slogan()
        {
            var key = "nl.ort.slogan." + _place.Id;
            return _host.Strings.HasKey(key) ? _host.Strings.T(key) : SpeakerName();
        }

        private string Title()
        {
            var t = _host.Strings;
            if (_place.Id == "tierarzt") return t.T("vet.ort_name");
            if (_place.Id == "mart") return t.T("nl.stadt.ort_mart");
            var key = _place.Id == "funkelpark" ? "park.ort.funkelpark" : "city.ort." + _place.MapId;
            return t.HasKey(key) ? t.T(key) : t.T(_place.NameKey);
        }

        /// <summary>Persistent city flags plus the live health flags the vet's tree branches on.</summary>
        private Dictionary<string, bool> StartFlags()
        {
            var flags = CityState.Flags(_host.Store);
            var grade = Health.Grade(Gooby.Core.Json.J.Path(_host.Store.State, "gooby", "health"));
            flags["gesund"] = grade <= 0;
            flags["krank"] = grade == 1;
            flags["schwer_krank"] = grade >= 2;
            return flags;
        }

        private string SpeakerName()
        {
            var key = "nl.ort.npc." + (_runner != null && _runner.IstGeladen && _runner.Sprecher.Length > 0 ? _runner.Sprecher : SpeakerOf(_place));
            return _host.Strings.HasKey(key) ? _host.Strings.T(key) : _host.Strings.T(_place.NameKey);
        }

        private static string SpeakerOf(DriveModel.Place place)
        {
            switch (place.Id)
            {
                case "tierarzt": return "moehrchen";
                case "goobytheke": return "hilde";
                case "mart": return "rehwald";
                case "goobyman": return "goobyman";
                case "gouhbus": return "goouhbus";
                case "post": return "pia";
                case "pow": return "baem";
                case "baumarkt": return "bodo";
                case "autohaus": return "benzino";
                case "wochenmarkt": return "greta";
                case "funkelpark": return "kasse";
                case "raumstation": return "kosmo";
                default: return "schalter";
            }
        }

        /// <summary>Enter the current node: apply its effects once, show its first line.</summary>
        private void ShowNode()
        {
            if (_appliedNode != _runner.Aktuell)
            {
                _appliedNode = _runner.Aktuell;
                foreach (var e in _runner.Effekte()) Apply(e);
            }
            _lines = _runner.Text();
            _lineIndex = 0;
            ShowLine();
        }

        private void ShowLine()
        {
            var t = _host.Strings;
            _moreOpen = false;
            _line.text = _lineIndex < _lines.Count ? _lines[_lineIndex] : "";
            if (_name != null) _name.text = SpeakerName().ToUpperInvariant();
            _chips.Clear();
            var moreLines = _lineIndex < _lines.Count - 1;
            var options = _runner.IstGeladen ? _runner.Optionen() : new List<DialogRunner.Option>();
            if (moreLines || (options.Count == 0 && _runner.IstGeladen && _runner.AutoNext.Length > 0))
            {
                AddChip(t.T("nl.ort.weiter"), Next, "gb-btn-confirm");
                return;
            }
            if (options.Count > 0)
            {
                // board 10: the first answer is the rose primary action (with the cart when it leads to the counter),
                // the others are cream secondaries
                for (var i = 0; i < options.Count; i++)
                {
                    var idx = i;
                    var leadsToCounter = _runner.IstGeladen && options[i].Next == "laden";
                    if (i == 0) AddChip(options[i].Text, () => Choose(idx), "gb-btn-primary", leadsToCounter ? "shop" : null);
                    else AddChip(options[i].Text, () => Choose(idx), "gb-btn-secondary");
                }
                return;
            }
            if (_place.Id == "wochenmarkt") AddChip(t.T("nl.ort.tab.meinstand"), OpenStand, "gb-btn-primary", "shop");
            else if (_place.HasCounter) AddChip(t.T("nl.ort.zum_tresen"), OpenCounter, "gb-btn-primary", "shop");
            if (_place.Id == "flughafen" && Raumstation.Freigeschaltet(_host.Store.State))
                AddChip(t.T("gfree.shuttle_knopf"), OpenStation, "gb-btn-primary");
            if (_place.Id == "raumstation")
            {
                AddChip(t.T("mg.rocketRescue.title"), () => OpenGame(Raumstation.SpielRocket), "gb-btn-primary");
                AddChip(t.T("mg.starHopper.title"), () => OpenGame(Raumstation.SpielStar), "gb-btn-primary");
            }
            AddChip(t.T("nl.ort.weiter"), Leave, "gb-btn-confirm");
        }

        /// <summary>"Weiter": the next line of the node, or the node's own follow-up once all lines were read.</summary>
        public void Next()
        {
            if (_lineIndex < _lines.Count - 1) { _lineIndex++; ShowLine(); return; }
            if (_runner.IstGeladen && _runner.Weiter()) ShowNode();
        }

        /// <summary>Picks one of the currently visible options (index into DialogRunner.Optionen()).</summary>
        public bool Choose(int optionIndex)
        {
            if (!_runner.Waehlen(optionIndex)) return false;
            ShowNode();
            return true;
        }

        /// <summary>The shopkeeper's status line under the title (findings, receipts, "into the bag").</summary>
        public string Hint => _hint?.text;

        private Button AddChip(string text, Action onClick, string style, string icon = null)
        {
            var b = icon != null ? Ux.IconTextBtn(icon, text, onClick, style) : Ux.Btn(text, onClick, style);
            b.AddToClassList("gb-chip-action");
            b.style.marginBottom = 6;
            _chips.Add(b);
            return b;
        }

        /// <summary>Dialog effects → game state; vet markers stay local (ort_scene.gd / tierarzt.gd).</summary>
        private void Apply(DialogRunner.Effekt e)
        {
            var t = _host.Strings;
            switch (e.Typ)
            {
                case "flag":
                    if (!CityState.IsMarker(e.Name)) { CityState.SetFlag(_host.Store, e.Name, e.Wert); break; }
                    switch (e.Name)
                    {
                        case "vet_untersuchung": Examination(); break;
                        case "vet_cure": PayVet("cure"); break;
                        case "vet_checkup": PayVet("checkup"); break;
                        case "vet_rezept": _hint.text = t.T("vet.rezept_ok"); break;
                    }
                    break;
                case "item":
                    CityState.GiveItem(_host.Store, e.Name);
                    _hint.text = t.T("nl.ort.item_erhalten");
                    break;
                case "laden":
                    // a beat to read the line, then the counter opens over the dialog (the chip re-opens it)
                    Root.schedule.Execute(OpenCounter).StartingIn(900);
                    break;
            }
        }

        private static readonly string[] ExamSteps = { "vet.untersuchung.horchen", "vet.untersuchung.temperatur", "vet.untersuchung.ohren" };

        /// <summary>
        /// Three findings in rhythm while the doctor's lines run (tierarzt.gd _untersuchung_sequenz) — each one also
        /// played on the stage: stethoscope and heartbeat, the thermometer with the reading floating up, the ear test.
        /// </summary>
        private void Examination()
        {
            var sick = Health.Grade(Gooby.Core.Json.J.Path(_host.Store.State, "gooby", "health")) >= 1;
            var i = 0;
            ExamBeat(i++, sick);
            _sequence?.Pause();
            _sequence = Root.schedule.Execute(() =>
            {
                if (i < ExamSteps.Length) ExamBeat(i++, sick);
                if (i >= ExamSteps.Length) _sequence?.Pause();
            }).Every(1400).StartingIn(1400);
        }

        private void ExamBeat(int step, bool sick)
        {
            _hint.text = _host.Strings.T(ExamSteps[step]);
            var w = _host.World;
            if (w?.Interior == null || w.Avatar == null) return;
            var anchor = w.Interior.VetStep(step, w.Avatar, sick);
            if (step != 1 || w.Camera == null || Root.panel == null) return;
            var at = RuntimePanelUtils.CameraTransformWorldToPanel(Root.panel, anchor, w.Camera);
            var reading = (sick ? 38.9 : 37.2).ToString("0.0", _host.Settings?.Locale == "en" ? System.Globalization.CultureInfo.InvariantCulture : new System.Globalization.CultureInfo("de-DE")) + " °C";
            Root.schedule.Execute(() => _host.Ui.FloatText(at, reading, sick ? new Color(1f, 0.42f, 0.4f) : new Color(0.36f, 0.72f, 0.5f), 34f)).StartingIn(1000);
        }

        private void PayVet(string kind)
        {
            var t = _host.Strings;
            var result = new Health.CareResult();
            _host.Store.Update(s => result = Health.PayVet(s, kind, _host.Store.Clock.NowMs()), GameHost.TicksMs);
            if (result.Ok)
            {
                _host.Store.NotifySliceChanged("economy");
                _host.Store.NotifySliceChanged("achievements");
                _host.Store.NotifySliceChanged("gooby");
                _hint.text = t.T(kind == "cure" ? "vet.behandlung_ok" : "vet.checkup_ok");
                Sfx.PlayUi(kind == "cure" ? "soft_win" : "soft_confirm");
                if (_host.World?.Interior != null) _host.World.Interior.VetFinish(_host.World.Avatar, kind == "cure");
            }
            else if (result.Reason == "coins") _hint.text = t.T("vet.zu_wenig");
            else if (result.Reason == "healthy") _hint.text = t.T("vet.behandlung_gesund");
        }

        /// <summary>The place's counter sheet over the interior (what the tree's "laden" and the chip open).</summary>
        public UiScreen CounterScreen()
        {
            switch (_place.Counter)
            {
                case DriveModel.CounterShop: case DriveModel.CounterPow: case DriveModel.CounterBaumarkt: return new ShopScreen(_host, _place);
                case DriveModel.CounterMarkt: return new MarktScreen(_host, _place);
                case DriveModel.CounterAutohaus: return new AutohausScreen(_host, _place);
                case DriveModel.CounterPost: return new PostScreen(_host, _place);
                case DriveModel.CounterPark: return new ParkScreen(_host, _place);
                case DriveModel.CounterTravel: return new TravelScreen(_host, _place);
                case DriveModel.CounterStation: return new StationSnackScreen(_host);
            }
            return null;
        }

        /// <summary>Greta's opening hours on the game clock (Saturday 8–14 in the town plan).</summary>
        private bool MarktOffen()
        {
            var now = _host.Store.Clock.NowMs();
            var (wochentag, stunde) = CityMap.LokaleZeit(now, _host.Store.Clock.UtcOffsetMinutes(now));
            return CityMap.IstOffen(CityMap.Get(_place.MapId), wochentag, stunde);
        }

        /// <summary>The own market stall: the panel on the right, the camera on the stall.</summary>
        public void OpenStand()
        {
            if (_place.Id != "wochenmarkt" || _host.Ui.Current != this) return;
            Sfx.PlayUi("soft_open");
            _host.Ui.Push(new MarktStandScreen(_host));
        }

        private void OpenCounter()
        {
            if (!_place.HasCounter || _host.Ui.Current != this) return;
            var sheet = CounterScreen();
            if (sheet != null) _host.Ui.Push(sheet);
        }

        private void OnCoins(int coins)
        {
            if (_coins != null) _coins.text = Ux.FormatNumber(coins);
        }

        public override void OnShown()
        {
            _leaving = false;
            Root.style.display = DisplayStyle.Flex;
            _card?.RemoveFromClassList("gb-dialog-card--covered");
            if (_coins != null) _coins.text = Ux.FormatNumber(ShopLogic.Coins(_host.Store));
            if (_host.World != null && _host.World.Interior?.Place != _place) _host.World.EnterInterior(_place);
            StartLeben();
        }

        /// <summary>Airport shuttle: GOOB-1 after space is visited. Stacks on the airport so popping returns to the hall.</summary>
        private void OpenStation()
        {
            if (!Raumstation.Freigeschaltet(_host.Store.State)) return;
            var station = DriveModel.PlaceById(Raumstation.Id);
            if (station == null) return;
            _host.Ui.FadeThrough(() => _host.Ui.Push(new PlaceScreen(_host, station)));
        }

        private void OpenGame(string gameId)
        {
            var game = MinigameHostScreen.For(_host, gameId);
            if (game != null) _host.Ui.Push(game);
            else _host.Ui.Toast(_host.Strings.T("mg.arcade.coming_soon"));
        }

        private void Sternenfoto()
        {
            var t = _host.Strings;
            if (!Raumstation.KameraFrei(_host.Store.State))
            {
                SayAside(t.T("raumstation.foto.keine_kamera"));
                return;
            }
            SayAside(t.T("raumstation.foto.knopf") + "!");
            Debug.Log("[interior] sternenfoto raumstation");
        }

        /// <summary>A line of the clerk's beside the dialog (the dealer's question after a test drive).</summary>
        public void Aside(string text) => SayAside(text);

        /// <summary>The dealer's test drive: out through the door like <see cref="Leave"/>, the street car in the chosen model and paint.</summary>
        public void LeaveForTestDrive(string carId, string farbe)
        {
            if (_leaving || _host.Ui.Current != this) return;
            _leaving = true;
            _sequence?.Pause();
            _host.Ui.FadeThrough(() =>
            {
                _host.World?.LeaveInterior();
                if (_host.Ui.Current == this) _host.Ui.Pop();
                if (_host.Ui.Current is CityDriveScreen city) city.BeginTestDrive(carId, farbe, _place);
            });
        }

        /// <summary>Out through the door: fade, the world puts the street back, this screen pops. GOOB-1 returns to the airport hall.</summary>
        public void Leave()
        {
            if (_leaving || _host.Ui.Current != this) return;
            _leaving = true;
            _sequence?.Pause();
            _host.Ui.FadeThrough(() =>
            {
                _host.World?.LeaveInterior();
                if (_host.Ui.Current == this) _host.Ui.Pop();
                if (_place.Id == "raumstation" && !(_host.Ui.Current is PlaceScreen ps && ps.Place?.Id == "flughafen"))
                {
                    var airport = DriveModel.PlaceById("flughafen");
                    if (airport != null) _host.Ui.Push(new PlaceScreen(_host, airport));
                }
            });
        }

        public override void OnHidden()
        {
            _sequence?.Pause();
            if (_host.Ui.IsOpen(this))
            {
                // a counter sheet covers us — the interior stays, the dialog card steps back so it does not peek out beside
                // the sheet; the stall screens want the square without the chrome (checked once the new screen is current —
                // Push hides us first)
                _card?.AddToClassList("gb-dialog-card--covered");
                Root.schedule.Execute(() =>
                {
                    if (_host.Ui.Current is MarktStandScreen || _host.Ui.Current is MarktTagScreen) Root.style.display = DisplayStyle.None;
                });
                return;
            }
            _host.Store.CoinsChanged -= OnCoins;
            if (_host.World?.Interior?.Place == _place) _host.World.LeaveInterior(); // popped without the fade (tests, PopToRoot)
        }
    }
}
