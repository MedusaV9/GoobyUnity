using Gooby.Core.Minigames;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The record ghost in the frame (port of minigame_host.gd's W19/GEIST hooks + geist_chip.gd + results.gd's ghost
    /// line): the round records its score curve over <see cref="GeistRekord"/>; with a saved best run of this game a calm
    /// chip beside the score pill shows how far ahead (+12) or behind (−5) the player is at this moment of the round. The
    /// ghost's clock is the round's game time — it stands in the countdown and in pause. A regularly booked round with a
    /// live score replaces the curve when it beats it, and the results card then says "Geist geschlagen!" once.
    /// </summary>
    public abstract partial class MinigameHostScreen
    {
        public const string GeistChipName = "mg-geist", GeistRowName = "results-geist";

        private GeistRekord _geist;
        private VisualElement _geistChip;
        private Label _geistLabel;
        private int _geistShown = int.MinValue;
        /// <summary>The booked round beat the saved best run (the results card's ghost line).</summary>
        private bool _geistBeaten;

        /// <summary>The running round's ghost recorder — QA and tests.</summary>
        public GeistRekord Geist => _geist;

        private void BuildGeistChip(VisualElement hud)
        {
            _geistChip = Ux.Pill("ghost", "", out _geistLabel);
            _geistChip.name = GeistChipName;
            _geistChip.AddToClassList("gb-geist-chip");
            _geistChip.pickingMode = PickingMode.Ignore;
            _geistChip.style.marginLeft = 10;
            _geistChip.style.display = DisplayStyle.None;
            hud.Add(_geistChip);
        }

        private void GeistStart()
        {
            _geist = new GeistRekord();
            _geist.Starte(GeistRekord.RekordVon(Host.Store.State, Meta.Id));
            _geistBeaten = false;
            _geistShown = int.MinValue;
            if (_geistChip != null) _geistChip.style.display = DisplayStyle.None;
        }

        /// <summary>The frame advanced the running game by <paramref name="gameSec"/> after the GO (not in a countdown or pause).</summary>
        private void GeistTick(double gameSec)
        {
            if (_geist == null || gameSec <= 0.0) return;
            _geist.Tick(gameSec, _scoreKnown ? _lastScore : 0);
            UpdateGeistChip();
        }

        /// <summary>A live score change of the round opens the ghost's gate (games without one get neither chip nor curve).</summary>
        private void GeistScoreFlowed()
        {
            if (_geist == null || _phase != Phase.Playing) return;
            _geist.MeldeLiveScore();
        }

        private void UpdateGeistChip()
        {
            if (_geistChip == null) return;
            var show = _geist != null && _phase == Phase.Playing && _geist.HatReferenz && _geist.HatLiveScore;
            _geistChip.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            var delta = _geist.DeltaAktuell(_scoreKnown ? _lastScore : 0);
            if (delta == _geistShown) return;
            _geistShown = delta;
            _geistLabel.text = GeistRekord.DeltaText(delta);
            _geistChip.EnableInClassList("gb-geist-chip--vorn", delta > 0);
            _geistChip.EnableInClassList("gb-geist-chip--hinten", delta < 0);
        }

        private void HideGeistChip()
        {
            if (_geistChip != null) _geistChip.style.display = DisplayStyle.None;
        }

        /// <summary>The round's best-run candidate before the booking (null without a live score) and whether it beats the ghost.</summary>
        private JObject GeistBeforeBooking(int finalScore)
        {
            _geistBeaten = _geist != null && _geist.Geschlagen(finalScore);
            var snap = _geist?.Snapshot(finalScore);
            if (_geist != null) Debug.Log("[mg] " + Meta.Id + " geist: t=" + _geist.Zeit.ToString("0.0") + " s, live=" + _geist.HatLiveScore + ", ref=" + _geist.HatReferenz + ", beaten=" + _geistBeaten);
            _geist = null;
            HideGeistChip();
            return snap;
        }

        /// <summary>A dropped round (pause → Neustart / Aufgeben) records nothing.</summary>
        private void GeistDrop()
        {
            _geist = null;
            _geistBeaten = false;
            HideGeistChip();
        }

        /// <summary>The results card's "Geist geschlagen!" row (null when the round did not beat a saved best run).</summary>
        private VisualElement GeistResultRow()
        {
            if (!_geistBeaten) return null;
            var row = ResultRow("ghost", Host.Strings.T("mg.geist.geschlagen"), out _);
            row.name = GeistRowName;
            row.AddToClassList("gb-results-row--hot");
            return row;
        }
    }
}
