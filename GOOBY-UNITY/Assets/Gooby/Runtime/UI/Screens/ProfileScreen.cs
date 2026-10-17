using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.Meta;
using Gooby.Unity.App;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The profile (port of profil_screen.gd + passport_card.gd): on top the GOOBY-PASS as a flip card — front: the crest
    /// line, Gooby's 3D portrait stuck on a little crooked, the player's name, Gooby's nickname, the issue date and the
    /// day's "special features" gag, level and XP; a tap turns it over to the stamps page (one stamp per visited holiday
    /// destination with its postcard date, WELTENGOOBY in gold, 5.0 UMZUG in red) with the machine-readable nonsense at
    /// the bottom. Below: "your GOOBY adventure" in percent with its four parts and the extras, playtime, daily streak,
    /// the statistics, and the album, achievements and rename buttons.
    /// </summary>
    public sealed class ProfileScreen : UiScreen
    {
        private readonly GameHost _host;
        private VisualElement _card, _front, _back;
        private bool _showBack, _flipping;

        private static readonly Color Paper = new Color(1f, 0.965f, 0.918f);
        private static readonly Color Ink = new Color(0.23f, 0.21f, 0.31f);
        private static readonly Color Band = new Color(0.61f, 0.9f, 0.78f);
        private static readonly Color[] StampColours =
        {
            new Color(0.16f, 0.55f, 0.55f), new Color(0.85f, 0.35f, 0.55f), new Color(0.3f, 0.58f, 0.3f), new Color(0.8f, 0.58f, 0.12f),
        };

        public ProfileScreen(GameHost host)
        {
            _host = host;
            Id = "profile";
        }

        private JObject State => _host.Store.State;

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            var sheetStyle = Resources.Load<StyleSheet>("gooby-ui/Theme/gooby-album");
            if (sheetStyle != null) root.styleSheets.Add(sheetStyle);
            root.AddToClassList("gb-sheets");
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => _host.Ui.Pop());
            root.Add(scrim);
            var sheet = Ux.El("gb-sheet", "gb-win-sheet");
            sheet.name = "profile-sheet";
            sheet.style.left = 140; sheet.style.right = 140;
            sheet.style.maxHeight = Length.Percent(90);
            var head = CounterPanel.Head("user", new Color(0.8f, 0.9f, 1f), t.T("profil.titel"), t.T("profil.dabei_seit", ("date", DateText(J.Long(J.Path(State, "meta", "createdAt"))))), () => _host.Ui.Pop());
            sheet.Add(head.Row);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-win-scroll");
            var body = Ux.Col("gb-win-body");
            body.Add(BuildPassport());
            body.Add(BuildAdventure());
            body.Add(BuildFacts());
            body.Add(BuildButtons());
            scroll.Add(body);
            sheet.Add(scroll);
            root.Add(sheet);
        }

        // ── the passport flip card ──

        private VisualElement BuildPassport()
        {
            _card = new VisualElement { name = "passport" };
            _card.style.backgroundColor = Paper;
            Round(_card, 22f);
            _card.style.borderTopWidth = _card.style.borderBottomWidth = _card.style.borderLeftWidth = _card.style.borderRightWidth = 3;
            _card.style.borderTopColor = _card.style.borderBottomColor = _card.style.borderLeftColor = _card.style.borderRightColor = new Color(0.86f, 0.8f, 0.7f);
            _card.style.marginBottom = 14;
            _card.style.overflow = Overflow.Hidden;
            _card.style.minHeight = 250;
            _front = BuildFront();
            _back = BuildBack();
            _back.style.display = DisplayStyle.None;
            _card.Add(_front);
            _card.Add(_back);
            _card.RegisterCallback<ClickEvent>(_ => Flip());
            return _card;
        }

        private VisualElement BuildFront()
        {
            var t = _host.Strings;
            var face = Ux.Col();
            face.name = "passport-front";
            var band = Ux.Row();
            band.style.backgroundColor = Band;
            band.style.paddingLeft = 16; band.style.paddingRight = 16; band.style.paddingTop = 8; band.style.paddingBottom = 8;
            band.style.alignItems = Align.Center;
            var crest = Ux.IconTile("carrot", new Color(1f, 0.85f, 0.7f));
            band.Add(crest);
            var titles = Ux.Col();
            titles.style.marginLeft = 10;
            titles.Add(Label(t.T("reisepass.republik"), 20, true));
            titles.Add(Label(t.T("reisepass.dokument"), 13, false, 0.75f));
            band.Add(titles);
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            band.Add(spacer);
            band.Add(Label(t.T("profil.pass_wort"), 15, true));
            face.Add(band);

            var row = Ux.Row();
            row.style.paddingLeft = 18; row.style.paddingRight = 18; row.style.paddingTop = 14; row.style.paddingBottom = 6;
            row.Add(BuildPhotoSlot());
            var fields = Ux.Col();
            fields.style.marginLeft = 18;
            fields.style.flexGrow = 1; fields.style.flexShrink = 1;
            var player = J.Str(J.Path(State, "meta", "playerName"));
            var nick = J.Str(J.Path(State, "meta", "goobyNickname"), "Gooby");
            fields.Add(Label(t.T("reisepass.name"), 12, false, 0.6f));
            fields.Add(Label(player.Length > 0 ? player : t.T("reisepass.name_leer"), 18, true));
            var gooby = Label(nick, 28, true);
            gooby.name = "passport-nickname";
            gooby.style.color = new Color(0.86f, 0.36f, 0.52f);
            gooby.style.marginTop = 4;
            fields.Add(gooby);
            fields.Add(Label(t.T("reisepass.ausgestellt", ("date", DateText(J.Long(J.Path(State, "meta", "createdAt"))))), 13, false, 0.75f));
            var features = t.Items("reisepass.merkmale_liste");
            if (features != null && features.Count > 0)
            {
                var day = _host.Store.Clock.NowMs() / 86_400_000L;
                var gag = Label(t.T("reisepass.merkmale") + ": " + (string)features[Passport.FeatureIndex(nick, day, features.Count)], 13, false, 0.85f);
                gag.style.whiteSpace = WhiteSpace.Normal;
                gag.style.marginTop = 6;
                fields.Add(gag);
            }
            row.Add(fields);
            face.Add(row);

            var lvl = Ux.Row();
            lvl.style.paddingLeft = 18; lvl.style.paddingRight = 18; lvl.style.paddingBottom = 6;
            lvl.style.alignItems = Align.Center;
            var level = Math.Max(1, J.Int(J.Path(State, "progression", "level"), 1));
            var xp = J.NumOr(J.Path(State, "progression", "xp"), 0.0);
            lvl.Add(Label(level >= Leveling.MaxLevel ? t.T("profil.level_max") : t.T("profil.level_von", ("level", level), ("max", Leveling.MaxLevel)), 14, true));
            var bar = Ux.Bar(level >= Leveling.MaxLevel ? 1f : Mathf.Clamp01((float)(xp / Math.Max(1, Leveling.XpToNext(level)))), out _, "gb-bar-fill--sonne");
            bar.style.flexGrow = 1; bar.style.marginLeft = 12; bar.style.height = 12;
            lvl.Add(bar);
            face.Add(lvl);
            face.Add(Hint(t.T("reisepass.flip_hinweis")));
            return face;
        }

        private VisualElement BuildBack()
        {
            var t = _host.Strings;
            var face = Ux.Col();
            face.name = "passport-back";
            face.style.paddingLeft = 18; face.style.paddingRight = 18; face.style.paddingTop = 12;
            var title = Label(t.T("reisepass.stempel_titel"), 20, true);
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            face.Add(title);
            var stamps = Passport.Stamps(State);
            if (stamps.Count == 0)
            {
                var empty = Label(t.T("reisepass.stempel_leer"), 14, false, 0.75f);
                empty.style.whiteSpace = WhiteSpace.Normal;
                empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                empty.style.marginTop = 18; empty.style.marginBottom = 18;
                face.Add(empty);
            }
            else
            {
                var flow = Ux.Row();
                flow.name = "passport-stamps";
                flow.style.flexWrap = Wrap.Wrap;
                flow.style.justifyContent = Justify.Center;
                flow.style.marginTop = 8;
                foreach (var s in stamps) flow.Add(StampChip(s));
                face.Add(flow);
            }
            var mrz = Ux.Col();
            mrz.name = "passport-mrz";
            mrz.style.backgroundColor = new Color(0.94f, 0.9f, 0.84f);
            Round(mrz, 10f);
            mrz.style.paddingLeft = 10; mrz.style.paddingRight = 10; mrz.style.paddingTop = 6; mrz.style.paddingBottom = 6;
            mrz.style.marginTop = 12;
            foreach (var line in Passport.MrzLines(J.Str(J.Path(State, "meta", "playerName")), J.Str(J.Path(State, "meta", "goobyNickname"), "Gooby")))
            {
                var l = Label(line, 13, true, 0.9f);
                l.style.letterSpacing = 1.5f;
                mrz.Add(l);
            }
            face.Add(mrz);
            face.Add(Hint(t.T("reisepass.zurueck_hinweis")));
            return face;
        }

        private VisualElement StampChip(Passport.Stamp s)
        {
            var t = _host.Strings;
            var colour = s.Colour < 0 ? new Color(0.86f, 0.3f, 0.3f) : s.Colour >= StampColours.Length ? new Color(0.9f, 0.66f, 0.1f) : StampColours[s.Colour];
            var chip = Ux.Col();
            chip.name = "stamp-" + s.Id;
            chip.style.borderTopWidth = chip.style.borderBottomWidth = chip.style.borderLeftWidth = chip.style.borderRightWidth = 3;
            chip.style.borderTopColor = chip.style.borderBottomColor = chip.style.borderLeftColor = chip.style.borderRightColor = colour;
            Round(chip, 12f);
            chip.style.paddingLeft = 12; chip.style.paddingRight = 12; chip.style.paddingTop = 6; chip.style.paddingBottom = 6;
            chip.style.marginLeft = 8; chip.style.marginRight = 8; chip.style.marginTop = 6; chip.style.marginBottom = 6;
            chip.style.rotate = new Rotate(s.TiltDeg);
            chip.style.alignItems = Align.Center;
            var name = Label(t.T(s.NameKey).ToUpperInvariant(), 14, true);
            name.style.color = colour;
            chip.Add(name);
            if (s.AtMs > 0)
            {
                var when = Label(t.T("reisepass.stempel_datum", ("date", DateText(s.AtMs))), 12, false);
                when.style.color = colour;
                chip.Add(when);
            }
            return chip;
        }

        /// <summary>Turns the card over: squash to an edge, swap the face, open again (instant with reduced motion).</summary>
        private Texture2D _photoTex;
        private string _photoShown;

        /// <summary>
        /// The photo corner of the pass (passport_card.gd): the gallery photo the player picked, else Gooby's portrait —
        /// officially crooked by −2° — and "Foto ändern" under it, which opens the picker instead of flipping the card.
        /// </summary>
        private VisualElement BuildPhotoSlot()
        {
            var col = Ux.Col();
            col.style.alignItems = Align.Center;
            var name = Passport.PhotoOf(State);
            var path = name.Length > 0 ? System.IO.Path.Combine(PhotoAlbum.Folder(_host), name) : null;
            if (_photoShown != name)
            {
                if (_photoTex != null) UnityEngine.Object.Destroy(_photoTex);
                _photoTex = path != null ? PhotoAlbum.Load(path) : null;
                _photoShown = name;
            }
            VisualElement photo;
            if (_photoTex != null)
            {
                photo = new VisualElement { name = "passport-photo" };
                photo.AddToClassList("passport-photo--picked");
                photo.style.width = 118; photo.style.height = 118;
                photo.style.backgroundImage = new StyleBackground(_photoTex);
                photo.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop;
            }
            else
            {
                photo = Ux.Portrait(118);
                photo.name = "passport-photo";
            }
            photo.style.rotate = new Rotate(-2f);
            photo.style.backgroundColor = new Color(0.86f, 0.93f, 1f);
            Round(photo, 18f);
            photo.style.borderTopWidth = photo.style.borderBottomWidth = photo.style.borderLeftWidth = photo.style.borderRightWidth = 4;
            photo.style.borderTopColor = photo.style.borderBottomColor = photo.style.borderLeftColor = photo.style.borderRightColor = Color.white;
            col.Add(photo);
            var change = Ux.Btn(_host.Strings.T("reisepass.foto_aendern"), () => _host.Ui.Push(new PassPhotoPickerScreen(_host)), "gb-btn-secondary", "gb-btn-sm");
            change.name = "passport-photo-change";
            change.style.marginTop = 8;
            change.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            col.Add(change);
            return col;
        }

        /// <summary>Back from the picker: a changed photo rebuilds the front (the back side stays as it is).</summary>
        public override void OnShown()
        {
            if (_front == null || _photoShown == Passport.PhotoOf(State)) return;
            var fresh = BuildFront();
            fresh.style.display = _front.style.display;
            _card.Insert(_card.IndexOf(_front), fresh);
            _front.RemoveFromHierarchy();
            _front = fresh;
        }

        public override void OnHidden()
        {
            if (_host.Ui.IsMounted(this) || _photoTex == null) return;
            UnityEngine.Object.Destroy(_photoTex);
            _photoTex = null;
            _photoShown = null;
        }

        private void Flip()
        {
            if (_flipping) return;
            Sfx.PlayUi("soft_chip");
            if (World.Effects.Reduced)
            {
                SwapFace();
                return;
            }
            _flipping = true;
            var start = Time.realtimeSinceStartup;
            const float half = 0.16f;
            var swapped = false;
            _card.schedule.Execute(() =>
            {
                var k = (Time.realtimeSinceStartup - start) / half;
                if (k >= 1f && !swapped) { swapped = true; SwapFace(); }
                var sx = k < 1f ? 1f - k : Mathf.Min(1f, k - 1f);
                _card.style.scale = new Scale(new Vector3(Mathf.Max(0.02f, sx), 1f, 1f));
                if (k >= 2f) { _card.style.scale = new Scale(Vector3.one); _flipping = false; }
            }).Every(16).Until(() => !_flipping);
        }

        private void SwapFace()
        {
            _showBack = !_showBack;
            _front.style.display = _showBack ? DisplayStyle.None : DisplayStyle.Flex;
            _back.style.display = _showBack ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ── progress, facts, buttons ──

        private VisualElement BuildAdventure()
        {
            var t = _host.Strings;
            var card = Ux.El("gb-card");
            card.name = "profile-adventure";
            card.style.marginBottom = 14;
            var top = Ux.Row();
            top.style.alignItems = Align.Center;
            top.Add(Ux.Icon("check"));
            var title = Ux.Text(t.T("profil.abschluss"), "gb-lg", "gb-bold");
            title.style.marginLeft = 8; title.style.flexGrow = 1;
            top.Add(title);
            var parts = Completion.Parts(State);
            var percent = Completion.Percent(parts);
            top.Add(Ux.Text(t.T("profil.abschluss_prozent", ("p", percent)), "gb-lg", "gb-bold"));
            card.Add(top);
            foreach (var p in parts) card.Add(PartRow(p));
            var hint = Ux.Text(t.T(percent >= 100 ? "profil.abschluss_fertig" : "profil.abschluss_hinweis"), "gb-sm", "gb-mild");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginTop = 6;
            card.Add(hint);
            var extra = Ux.Text(t.T("profil.abschluss_zusatz"), "gb-sm", "gb-bold");
            extra.style.marginTop = 10;
            card.Add(extra);
            foreach (var p in Completion.Extras(State)) card.Add(PartRow(p));
            return card;
        }

        private VisualElement PartRow(Completion.Part p)
        {
            var t = _host.Strings;
            var row = Ux.Row();
            row.name = "part-" + p.Id;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 6;
            var label = Ux.Text(t.T("profil.abschluss_teil." + p.Id), "gb-sm", "gb-bold");
            label.style.width = 170;
            row.Add(label);
            var bar = Ux.Bar((float)p.N / p.Total, out _);
            bar.style.flexGrow = 1;
            row.Add(bar);
            var n = Ux.Text(t.T("profil.abschluss_stand", ("n", p.N), ("total", p.Total)), "gb-sm", "gb-mild");
            n.style.width = 76; n.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(n);
            return row;
        }

        private VisualElement BuildFacts()
        {
            var t = _host.Strings;
            var card = Ux.El("gb-card");
            card.name = "profile-facts";
            card.style.marginBottom = 14;
            var minutes = (long)J.NumOr(J.Path(State, "profile", "playtimeMin"), 0.0);
            var facts = new List<(string, string)>
            {
                (t.T("profil.spielzeit"), t.T("profil.spielzeit_wert", ("h", minutes / 60), ("mm", (minutes % 60).ToString("00")))),
                (t.T("profil.serie"), t.T("profil.serie_wert", ("n", J.Int(J.Path(State, "daily", "streak"))))),
            };
            var c = J.Dict(J.Path(State, "achievements", "counters"));
            foreach (var (key, counter) in new[] { ("fuetterungen", "feeds"), ("waeschen", "washes"), ("nickerchen", "sleeps"), ("kitzler", "tickles"), ("ballwuerfe", "balls"), ("einkaufsfahrten", "trips"), ("tierarzt", "vetTrips"), ("lieferungen", "deliveries"), ("ernten", "harvests"), ("quests", "questsDone") })
                facts.Add((t.T("profil.stat." + key), J.Int(c[counter]).ToString()));
            facts.Add((t.T("profil.stat.fotos"), J.Int(J.Path(State, "profile", "photos")).ToString()));
            card.Add(Ux.Text(t.T("profil.statistik"), "gb-lg", "gb-bold"));
            var grid = Ux.Row();
            grid.style.flexWrap = Wrap.Wrap;
            foreach (var (label, value) in facts)
            {
                var cell = Ux.Col();
                cell.style.width = Length.Percent(33.3f);
                cell.style.marginTop = 8;
                cell.Add(Ux.Text(value, "gb-lg", "gb-bold"));
                var l = Ux.Text(label, "gb-sm", "gb-mild");
                l.style.whiteSpace = WhiteSpace.Normal;
                cell.Add(l);
                grid.Add(cell);
            }
            card.Add(grid);
            return card;
        }

        private VisualElement BuildButtons()
        {
            var t = _host.Strings;
            var row = Ux.Row();
            row.style.justifyContent = Justify.Center;
            row.style.flexWrap = Wrap.Wrap;
            row.style.marginBottom = 8;
            foreach (var (icon, label, make) in new (string, string, Func<UiScreen>)[]
                     {
                         ("star", t.T("album.titel"), () => new AlbumScreen(_host)),
                         ("trophy", t.T("achievements.titel"), () => new AchievementsScreen(_host)),
                         ("pencil", t.T("nl.onboarding.rename_button"), () => new OnboardingScreen(_host, renameOnly: true)),
                     })
            {
                var b = Ux.IconTextBtn(icon, label, () => _host.Ui.Push(make()), "gb-btn-secondary");
                b.style.marginLeft = 6; b.style.marginRight = 6; b.style.marginTop = 6;
                row.Add(b);
            }
            return row;
        }

        // ── helpers ──

        private string DateText(long ms)
        {
            if (ms <= 0) ms = _host.Store.Clock.NowMs();
            var d = DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime();
            return _host.Strings.Locale == "en" ? d.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture) : d.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static Label Label(string text, int size, bool bold, float alpha = 1f)
        {
            var l = new Label(text);
            l.style.fontSize = size;
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.color = new Color(Ink.r, Ink.g, Ink.b, alpha);
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        private static Label Hint(string text)
        {
            var l = Label(text, 12, false, 0.55f);
            l.style.unityTextAlign = TextAnchor.MiddleRight;
            l.style.paddingRight = 16; l.style.paddingBottom = 8; l.style.paddingTop = 4;
            return l;
        }

        private static void Round(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r; e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r; e.style.borderBottomRightRadius = r;
        }
    }
}
