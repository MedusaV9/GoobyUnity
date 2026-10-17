using Gooby.Core.Meta;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// "Deine Knöpfe" (port of hud_coachmark.gd): once per install, after the guided start, a calm card beside the nav
    /// column says where things live — the tabs on the left, the actions at the bottom, profile and phone up top and the
    /// eye that shows everything tappable. Like Godot's overlay queue it never crowds another card: it waits for a quiet
    /// home (no sheet, blend, confetti, guide, stats sheet or eye) and stays until "Alles klar!"; a sheet hides it meanwhile.
    /// </summary>
    public sealed partial class HomeHudScreen
    {
        private const float ButtonsCalmS = 4f, ButtonsGapPx = 14f;
        private VisualElement _buttonsCard, _tabCol;
        private float _buttonsCalm;

        private bool ButtonsCardDue => !_host.Settings.HudButtonsSeen && Onboarded && _guide == null && !OnboardingGuide.Active(_host.Store.State);

        private bool HomeQuiet => _host.Ui.Current == this && !_host.Ui.Fading && !_host.Ui.Celebrating && !SpotlightOn && _whatsNewIn < 0f
            && (_statsSheet == null || _statsSheet.style.display == DisplayStyle.None);

        private void TickButtonsCard(float dt)
        {
            var quiet = HomeQuiet;
            // a language switch rebuilds the HUD and drops the card with it: build it anew on the next quiet moment
            if (_buttonsCard != null && _buttonsCard.parent == null) _buttonsCard = null;
            if (_buttonsCard != null)
            {
                _buttonsCard.style.display = quiet ? DisplayStyle.Flex : DisplayStyle.None;
                return;
            }
            if (!quiet || !ButtonsCardDue) { _buttonsCalm = 0f; return; }
            _buttonsCalm += dt;
            if (_buttonsCalm >= ButtonsCalmS) ShowButtonsCard();
        }

        private void ShowButtonsCard()
        {
            if (_buttonsCard != null || Root == null) return;
            var t = _host.Strings;
            _buttonsCard = Ux.Col("gb-knoepfe", "gb-knoepfe--in");
            _buttonsCard.name = "hud-buttons-card";
            if (_compact) _buttonsCard.AddToClassList("gb-knoepfe--compact");
            _buttonsCard.Add(Ux.Text(t.T("hud.coachmark_titel"), "gb-knoepfe-title"));
            _buttonsCard.Add(ButtonsRow(Ux.IconTile("inventory"), t.T("nl.hud.knoepfe_links")));
            _buttonsCard.Add(ButtonsRow(Ux.IconTile("toolbox"), t.T("nl.hud.knoepfe_unten")));
            _buttonsCard.Add(ButtonsRow(Ux.IconTile("user"), t.T("nl.hud.knoepfe_oben")));
            var eye = Ux.El("gb-knoepfe-eye");
            eye.Add(Ux.Icon("eye_white"));
            _buttonsCard.Add(ButtonsRow(eye, t.T("nl.hud.knoepfe_auge")));
            var ok = Ux.Btn(t.T("hud.coachmark_ok"), DismissButtonsCard, "gb-btn-primary", "gb-knoepfe-ok");
            ok.name = "hud-buttons-ok";
            _buttonsCard.Add(ok);
            // the tab labels decide the column's width (wider than its 168-px floor in German): sit right beside it
            var edge = _tabCol != null ? _tabCol.layout.xMax : float.NaN;
            if (!float.IsNaN(edge) && edge > 0f) _buttonsCard.style.left = edge + ButtonsGapPx;
            Root.Add(_buttonsCard);
            _buttonsCard.schedule.Execute(() => _buttonsCard?.RemoveFromClassList("gb-knoepfe--in")).StartingIn(30);
            Sfx.PlayUi("soft_open");
            Debug.Log("[hud] buttons card");
        }

        private static VisualElement ButtonsRow(VisualElement glyph, string text)
        {
            var row = Ux.Row("gb-knoepfe-row");
            row.Add(glyph);
            row.Add(Ux.Text(text, "gb-knoepfe-text"));
            return row;
        }

        private void DismissButtonsCard()
        {
            _host.Settings.SetHudButtonsSeen(true);
            Sfx.PlayUi("soft_confirm");
            _buttonsCard?.RemoveFromHierarchy();
            _buttonsCard = null;
            Debug.Log("[hud] buttons card ok");
        }

        /// <summary>QA "knoepfe": forget the confirmation so the card comes again on a quiet home; "knoepfe ok" confirms it.</summary>
        public string QaButtonsCard(string arg)
        {
            if (arg == "ok")
            {
                if (_buttonsCard == null) return "no card";
                DismissButtonsCard();
                return "ok";
            }
            _host.Settings.SetHudButtonsSeen(false);
            _buttonsCalm = ButtonsCalmS;
            return ButtonsCardDue ? "due" : "waits (onboarding or guide)";
        }
    }
}
