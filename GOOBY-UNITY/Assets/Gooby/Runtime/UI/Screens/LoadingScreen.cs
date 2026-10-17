using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Boot cover (concept board 04): the painted living room opening onto the garden as a full-bleed backdrop,
    /// Gooby sitting on the round rug, the logo wordmark + claim top-left, the tip card top-right, the centred
    /// progress card with the apricot-to-carrot bar and its carrot knob, the bunny hopping along the page dots
    /// and the brand slogan on the floorboards.
    /// </summary>
    public sealed class LoadingScreen : UiScreen
    {
        private const int DotCount = 7;
        /// <summary>Gooby and the card are anchored to the bottom edge, so on every aspect he sits right above the card on the rug.</summary>
        private const float CardBottom = 66f;
        private const float GoobySize = 400f;
        private const float GoobyBottom = 188f;

        private static readonly Color Cocoa = new Color(0.29f, 0.204f, 0.149f);      // #4A3426 titles
        private static readonly Color MildBrown = new Color(0.49f, 0.424f, 0.376f);  // #7D6C60 body copy
        private static readonly Color Paper = new Color(1f, 0.984f, 0.96f, 0.97f);   // #FFFBF5 card paper

        private readonly GameHost _host;

        public override string SoundScene => "boot";
        private Label _phase;
        private Label _percent;
        private VisualElement _fill;
        private VisualElement _knob;
        private VisualElement[] _dots;
        private VisualElement[] _bunnies;
        private VisualElement _gooby;
        private float _time;

        public LoadingScreen(GameHost host)
        {
            _host = host;
            Id = "loading";
            Opaque = true;
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.style.backgroundColor = new Color(0.98f, 0.90f, 0.80f);
            root.Add(Ux.Backdrop("bg_loading"));

            // Gooby sits on the rug in the middle of the room (the backdrop leaves the rug empty for him)
            _gooby = Ux.Image("gooby_sitting", GoobySize, GoobySize);
            Ux.Abs(_gooby, null, null, null, GoobyBottom);
            _gooby.style.left = Length.Percent(50);
            _gooby.style.translate = new Translate(Length.Percent(-50), 0);
            // board 04: two yellow sparkles over Gooby's ears (white icons wash out on the painting); they bob with him
            AddSparkle(_gooby, top: 14, left: GoobySize / 2f - 34f, size: 36, rot: 20);
            AddSparkle(_gooby, top: 41, left: GoobySize / 2f + 22f, size: 24, rot: -12);
            root.Add(_gooby);

            // logo block: wordmark image + claim in white over a soft brown shadow so it reads on the painting
            var logo = Ux.Col();
            Ux.Abs(logo, 40, 14);
            logo.Add(Ux.Image("logo_wordmark", 448, 196));
            var claim = Ux.Text(t.T("nl.marke.claim"), "gb-extrabold");
            claim.style.fontSize = 22;
            claim.style.color = new Color(0.353f, 0.239f, 0.173f); // #5A3D2C board 01 Schoko
            claim.style.marginTop = -8;
            claim.style.marginLeft = 22;
            claim.style.textShadow = new TextShadow { offset = new Vector2(0, 1), blurRadius = 5, color = new Color(1f, 0.97f, 0.92f, 0.92f) };
            logo.Add(claim);
            root.Add(logo);

            root.Add(BuildTip(t));
            root.Add(BuildProgressCard(t));

            // the bunny hops along the page dots below the card, the slogan sits on the floorboards
            var footer = Ux.Col();
            Ux.Abs(footer, null, null, null, 12);
            footer.style.left = Length.Percent(50);
            footer.style.translate = new Translate(Length.Percent(-50), 0);
            footer.style.alignItems = Align.Center;
            var dots = Ux.Row();
            dots.style.alignItems = Align.Center;
            _dots = new VisualElement[DotCount];
            _bunnies = new VisualElement[DotCount];
            for (var i = 0; i < DotCount; i++)
            {
                var slot = new VisualElement { pickingMode = PickingMode.Ignore };
                slot.style.width = 22; slot.style.height = 22;
                slot.style.marginLeft = 3; slot.style.marginRight = 3;
                slot.style.alignItems = Align.Center;
                slot.style.justifyContent = Justify.Center;
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.style.width = 7; dot.style.height = 7;
                dot.style.borderTopLeftRadius = 3.5f; dot.style.borderTopRightRadius = 3.5f;
                dot.style.borderBottomLeftRadius = 3.5f; dot.style.borderBottomRightRadius = 3.5f;
                dot.style.backgroundColor = new Color(0.93f, 0.89f, 0.85f, 0.9f);
                var bunny = Ux.Icon("bunny_dot");
                bunny.style.width = 22; bunny.style.height = 22;
                bunny.style.display = DisplayStyle.None;
                slot.Add(dot);
                slot.Add(bunny);
                _dots[i] = dot;
                _bunnies[i] = bunny;
                dots.Add(slot);
            }
            footer.Add(dots);
            var slogan = Ux.Text(t.T("nl.marke.slogan"), "gb-semibold");
            slogan.style.fontSize = 16;
            slogan.style.color = new Color(1f, 0.97f, 0.93f);
            slogan.style.marginTop = 6;
            slogan.style.textShadow = new TextShadow { offset = new Vector2(0, 1), blurRadius = 6, color = new Color(0.25f, 0.13f, 0.06f, 0.65f) };
            footer.Add(slogan);
            root.Add(footer);
        }

        /// <summary>Tip card hanging under the lantern: bulb with rays, bold title, three-line hint, chevron, leaf pair.</summary>
        private VisualElement BuildTip(Gooby.Core.I18n.I18n t)
        {
            var wrap = new VisualElement { pickingMode = PickingMode.Ignore };
            Ux.Abs(wrap, null, 92, 36);
            wrap.style.width = 262;
            wrap.Add(Ux.El("gb-shadow"));
            var tip = Ux.El("gb-card", "gb-row");
            tip.style.backgroundColor = Paper;
            SetRadius(tip, 26);
            tip.style.paddingTop = 16; tip.style.paddingBottom = 14;
            tip.style.paddingLeft = 14; tip.style.paddingRight = 16;
            tip.style.alignItems = Align.FlexStart;

            var bulbBox = new VisualElement { pickingMode = PickingMode.Ignore };
            bulbBox.style.width = 58; bulbBox.style.height = 58;
            bulbBox.style.flexShrink = 0;
            bulbBox.style.marginRight = 6;
            bulbBox.style.marginTop = -8;
            bulbBox.style.marginLeft = -4;
            var rays = Ux.Icon("bulb_rays");
            Ux.Abs(rays, 0, 0);
            rays.style.width = 58; rays.style.height = 58;
            bulbBox.Add(rays);
            var bulb = Ux.Icon("bulb");
            Ux.Abs(bulb, 8, 12);
            bulb.style.width = 42; bulb.style.height = 42;
            bulbBox.Add(bulb);
            tip.Add(bulbBox);

            var col = Ux.Col("gb-grow");
            col.style.flexShrink = 1;
            col.style.minWidth = 0;
            var title = Ux.Text(t.T("nl.loading.tipp_titel"), "gb-extrabold");
            title.style.fontSize = 22;
            title.style.color = Cocoa;
            title.style.marginBottom = -3;
            col.Add(title);
            var line = Ux.Row();
            line.style.alignItems = Align.FlexEnd;
            var text = Ux.Text(t.T("nl.loading.tipp_sticker"));
            text.style.fontSize = 15;
            text.style.color = MildBrown;
            text.style.whiteSpace = WhiteSpace.Normal;
            text.style.flexGrow = 1; text.style.flexShrink = 1;
            line.Add(text);
            var chevron = Ux.Icon("chevron");
            chevron.style.width = 14; chevron.style.height = 14;
            chevron.style.flexShrink = 0;
            chevron.style.marginLeft = 6;
            chevron.style.marginBottom = 3;
            line.Add(chevron);
            col.Add(line);
            tip.Add(col);

            var leaf = Ux.Icon("leaf_pair");
            Ux.Abs(leaf, null, 8, 10);
            leaf.style.width = 30; leaf.style.height = 30;
            tip.Add(leaf);
            wrap.Add(tip);
            return wrap;
        }

        /// <summary>Wide paper card over the front of the rug: bold phase line, thick bar with the carrot knob, percent, cozy subline.</summary>
        private VisualElement BuildProgressCard(Gooby.Core.I18n.I18n t)
        {
            var wrap = new VisualElement { pickingMode = PickingMode.Ignore };
            Ux.Abs(wrap, null, null, null, CardBottom);
            wrap.style.width = 780;
            wrap.style.left = Length.Percent(50);
            wrap.style.translate = new Translate(Length.Percent(-50), 0);
            wrap.Add(Ux.El("gb-shadow"));
            var card = Ux.El("gb-card");
            card.style.backgroundColor = Paper;
            SetRadius(card, 34);
            card.style.paddingTop = 22; card.style.paddingBottom = 18;
            card.style.paddingLeft = 44; card.style.paddingRight = 40;

            _phase = Ux.Text(t.T("nl.loading.kuscheldecke"), "gb-bold", "gb-center");
            _phase.style.fontSize = 26;
            _phase.style.color = Cocoa;
            card.Add(_phase);

            var barRow = Ux.Row();
            barRow.style.marginTop = 18;
            barRow.style.alignItems = Align.Center;
            var bar = Ux.Bar(0f, out _fill, "gb-bar-fill--pfirsich");
            bar.style.flexGrow = 1;
            bar.style.height = 22;
            SetRadius(bar, 11);
            bar.style.overflow = Overflow.Visible;
            bar.style.backgroundColor = new Color(0.945f, 0.906f, 0.855f); // #F1E7DA track
            SetRadius(_fill, 11);
            var gradient = Resources.Load<Texture2D>("gooby-ui/Icons/bar_carrot");
            if (gradient != null)
            {
                _fill.style.backgroundImage = new StyleBackground(gradient);
                _fill.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            }
            _knob = Ux.Icon("carrot_knob");
            _knob.style.position = Position.Absolute;
            // the ball sits at 59 % of the glyph height (leaves on top): centre it on the bar
            _knob.style.width = 60; _knob.style.height = 60; _knob.style.top = 11f - 0.594f * 60f;
            _knob.style.marginLeft = -30;
            bar.Add(_knob);
            barRow.Add(bar);
            _percent = Ux.Text("0%", "gb-semibold");
            _percent.style.fontSize = 20;
            _percent.style.color = new Color(0.478f, 0.416f, 0.369f); // #7A6A5E
            _percent.style.width = 70;
            _percent.style.marginLeft = 18;
            _percent.style.unityTextAlign = TextAnchor.MiddleRight;
            barRow.Add(_percent);
            card.Add(barRow);

            var sub = Ux.Text(t.T("nl.loading.moment"), "gb-center");
            sub.style.fontSize = 16;
            sub.style.color = new Color(0.545f, 0.482f, 0.44f); // #8B7B70
            sub.style.marginTop = 12;
            card.Add(sub);
            wrap.Add(card);
            return wrap;
        }

        private static void SetRadius(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r; e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r; e.style.borderBottomRightRadius = r;
        }

        /// <summary>Gold sparkle with a warm drop so it reads on the painted peach backdrop (board 04).</summary>
        private static void AddSparkle(VisualElement parent, float top, float left, float size, float rot)
        {
            var glow = Ux.Icon("sparkle");
            Ux.Abs(glow, left + 2, top + 2);
            glow.style.width = size; glow.style.height = size;
            glow.style.rotate = new Rotate(rot);
            glow.style.unityBackgroundImageTintColor = new Color(0.42f, 0.18f, 0.04f, 0.7f);
            parent.Add(glow);
            var spark = Ux.Icon("sparkle");
            Ux.Abs(spark, left, top);
            spark.style.width = size; spark.style.height = size;
            spark.style.rotate = new Rotate(rot);
            spark.style.unityBackgroundImageTintColor = new Color(1f, 0.9f, 0.22f);
            parent.Add(spark);
        }

        public override void Update()
        {
            // gentle idle bob so the cover feels alive while the world loads
            _time += Time.unscaledDeltaTime;
            if (_gooby != null) _gooby.style.translate = new Translate(Length.Percent(-50), Mathf.Sin(_time * 2.4f) * 4f);
        }

        public void SetProgress(double ratio, string phaseKey)
        {
            var r = Mathf.Clamp01((float)ratio);
            Ux.SetBar(_fill, r);
            _knob.style.left = Length.Percent(r * 100f);
            _percent.text = Mathf.RoundToInt(r * 100f) + "%";
            // board 04 keeps the cozy line on the card; boot phases only drive the bar and the bunny
            _phase.text = r < 0.92f ? _host.Strings.T("nl.loading.kuscheldecke") : _host.Strings.T(phaseKey);
            var active = Mathf.Clamp(Mathf.FloorToInt(r * DotCount), 0, DotCount - 1);
            for (var i = 0; i < DotCount; i++)
            {
                _dots[i].style.display = i == active ? DisplayStyle.None : DisplayStyle.Flex;
                _bunnies[i].style.display = i == active ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }
}
