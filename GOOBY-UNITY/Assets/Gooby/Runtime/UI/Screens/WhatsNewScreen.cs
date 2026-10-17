using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// "Neu in GOOBY" (port of news_50_panel.gd): the highlights of this version as cards — an icon tile in its colour, a
    /// title and one sentence — under the version's date, closed with "Los geht's!". Shown once after an update to players
    /// who already live with Gooby (newcomers get the guided first quarter hour instead) and any time from the settings.
    /// </summary>
    public sealed class WhatsNewScreen : UiScreen
    {
        /// <summary>Bump with every release that brings new highlights (and the nl.neu.* strings with it).</summary>
        public const string Version = "2026-10-12";

        /// <summary>The highlights: string key under nl.neu, icon and tile colour.</summary>
        public static readonly (string key, string icon, string tint)[] Items =
        {
            ("minispiele", "gamepad", "#CFF2EA"),
            ("garage", "car", "#FFE7C2"),
            ("flughafen", "plane", "#D6E8FF"),
            ("postkarten", "mail", "#FFE2EA"),
            ("fuettern", "carrot", "#FFE3CC"),
            ("auge", "sparkle", "#FFF2B8"),
            ("farbsehen", "monitor", "#E6E0FF"),
            ("momente", "heart", "#FFD9E4"),
        };

        private readonly GameHost _host;

        public WhatsNewScreen(GameHost host)
        {
            _host = host;
            Id = "whats-new";
        }

        /// <summary>After an update: an onboarded player who has not seen this version's highlights yet.</summary>
        public static bool Due(GameHost host) => host?.Settings != null && host.Settings.WhatsNewSeen != Version;

        /// <summary>Like the daily parcel (BootFlow.MaybeOfferParcel): never by itself in batch mode or QA runs.</summary>
        public static bool MayOfferByItself()
        {
            if (Application.isBatchMode) return false;
            foreach (var a in System.Environment.GetCommandLineArgs())
                if (a == "-gooby-qa-cmds" || a == "-gooby-open" || a == "-gooby-at" || a == "-gooby-server") return false;
            return true;
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            var ui = CounterPanel.Build(root, _host, "sparkle", new Color(1f, 0.95f, 0.74f), t.T("nl.neu.titel"), t.T("nl.neu.untertitel"), Close);
            var col = Ux.Col("gb-neu-col");
            var grid = Ux.El("gb-neu-grid");
            foreach (var (key, icon, tint) in Items)
            {
                var card = Ux.Row("gb-neu-card");
                card.name = "neu-" + key;
                card.Add(Ux.IconTile(icon, Gooby.Unity.World.ArtLibrary.Hex(tint)));
                var text = Ux.Col("gb-neu-text");
                text.Add(Ux.Text(t.T("nl.neu." + key + "_titel"), "gb-neu-title"));
                var line = Ux.Text(t.T("nl.neu." + key + "_text"), "gb-neu-line");
                line.style.whiteSpace = WhiteSpace.Normal;
                text.Add(line);
                card.Add(text);
                grid.Add(card);
            }
            col.Add(grid);
            var foot = Ux.Row("gb-quest-foot");
            var go = Ux.IconTextBtn("star", t.T("nl.neu.los"), Close, "gb-btn-primary");
            go.name = "neu-los";
            foot.Add(go);
            col.Add(foot);
            ui.Body.Add(col);
            Sfx.PlayUi("soft_open");
        }

        private void Close()
        {
            _host.Settings.SetWhatsNewSeen(Version);
            _host.Ui.Pop();
        }
    }
}
