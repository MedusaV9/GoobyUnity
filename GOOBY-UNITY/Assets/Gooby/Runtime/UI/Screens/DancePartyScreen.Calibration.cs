using Gooby.Core.Minigames.Play;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// "Timing anpassen" on the Tanzparty's pregame card (port of dance_calibration.gd): eight metronome beats at the
    /// game's tempo, the player taps along on a big button, the median of the deviations (minus the device's own output
    /// latency) is saved as the fine offset (±150 ms) in <c>minigames.danceOffsetMs</c>; "Zurücksetzen" puts it back to 0.
    /// A round starts with its time line shifted by base latency + fine offset (<see cref="DanceTiming"/>).
    /// </summary>
    public sealed partial class DancePartyScreen
    {
        private VisualElement _calib;
        private Label _calibStatus, _calibOffset;
        private Button _calibTap, _calibStart, _pregameTiming;
        private DanceCalibrationRun _calibRun;
        private float _calibFlash;

        /// <summary>The device's output latency (ms) from the audio buffer (Unity's stand-in for Godot's AudioServer latency).</summary>
        private static double BaseLatencyMs()
        {
            AudioSettings.GetDSPBufferSize(out var length, out var count);
            var rate = Mathf.Max(1, AudioSettings.outputSampleRate);
            return DanceTiming.BaseLatencyFrom((double)length * Mathf.Max(1, count) / rate, 0.0);
        }

        private double ManualOffsetMs => DanceTiming.ManualOffsetFromState(Host.Store.State);

        protected override VisualElement PregameExtra()
        {
            var t = Host.Strings;
            var row = Ux.Row("gb-mgw-card");
            row.style.alignItems = Align.Center;
            row.Add(Ux.Icon("speaker"));
            _pregameTiming = PregamePill(null, "", OpenCalibration, "gb-mgw-sand");
            _pregameTiming.name = "dance-timing";
            _pregameTiming.style.marginLeft = 10;
            row.Add(_pregameTiming);
            RefreshTimingLabels();
            return row;
        }

        private void RefreshTimingLabels()
        {
            var t = Host.Strings;
            var ms = (int)System.Math.Round(ManualOffsetMs);
            var label = _pregameTiming?.Q<Label>(className: "gb-btn-label");
            if (label != null) label.text = t.T("mg.danceParty.kalib_knopf", ("ms", ms));
            if (_calibOffset != null) _calibOffset.text = t.T("mg.danceParty.kalib_offset", ("ms", ms));
        }

        /// <summary>Opens the calibration card over the pregame window.</summary>
        public void OpenCalibration()
        {
            if (_calib != null || PregameRoot == null) return;
            var t = Host.Strings;
            Sfx.PlayUi("soft_open");
            _calib = new VisualElement { name = "dance-calibration" };
            Ux.Abs(_calib, 0, 0, 0, 0);
            _calib.Add(Ux.El("gb-scrim"));
            var card = Ux.El("gb-mgw", "gb-dance-calib");
            card.style.position = Position.Absolute;
            card.style.left = Length.Percent(50); card.style.top = Length.Percent(50);
            card.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
            card.style.width = 520;
            card.style.paddingLeft = 24; card.style.paddingRight = 24; card.style.paddingTop = 20; card.style.paddingBottom = 20;
            card.style.alignItems = Align.Center;
            card.Add(Ux.Text(t.T("mg.danceParty.kalib_titel"), "gb-h2"));
            var text = Ux.Text(t.T("mg.danceParty.kalib_text"), "gb-md", "gb-mild");
            text.style.whiteSpace = WhiteSpace.Normal;
            text.style.unityTextAlign = TextAnchor.MiddleCenter;
            text.style.marginTop = 6;
            card.Add(text);
            _calibStatus = Ux.Text(t.T("mg.danceParty.kalib_beat", ("n", 0), ("max", DanceTiming.CalibrationBeats)), "gb-lg", "gb-bold");
            _calibStatus.name = "dance-calib-status";
            _calibStatus.style.marginTop = 12;
            card.Add(_calibStatus);
            _calibTap = new Button(TapCalibration) { text = t.T("mg.danceParty.kalib_tap"), name = "dance-calib-tap" };
            _calibTap.ClearClassList();
            _calibTap.AddToClassList("gb-dance-calib-tap");
            _calibTap.SetEnabled(false);
            card.Add(_calibTap);
            _calibOffset = Ux.Text("", "gb-sm", "gb-mild");
            card.Add(_calibOffset);
            var actions = Ux.Row();
            actions.style.marginTop = 14;
            var reset = PregamePill(null, t.T("mg.danceParty.kalib_reset"), ResetCalibration, "gb-mgw-sand");
            reset.name = "dance-calib-reset";
            reset.style.marginRight = 8;
            actions.Add(reset);
            _calibStart = PregamePill("play", t.T("mg.danceParty.kalib_start"), StartCalibration, "gb-btn-primary");
            _calibStart.name = "dance-calib-start";
            _calibStart.style.marginRight = 8;
            actions.Add(_calibStart);
            var done = PregamePill(null, t.T("mg.danceParty.kalib_zu"), CloseCalibration, "gb-mgw-sand");
            done.name = "dance-calib-done";
            actions.Add(done);
            card.Add(actions);
            _calib.Add(card);
            PregameRoot.Add(_calib);
            RefreshTimingLabels();
        }

        public void StartCalibration()
        {
            if (_calib == null) return;
            _calibRun = new DanceCalibrationRun(BaseLatencyMs());
            _calibRun.Start();
            _calibTap.SetEnabled(true);
            _calibStart.SetEnabled(false);
            _calibStatus.text = Host.Strings.T("mg.danceParty.kalib_beat", ("n", 0), ("max", DanceTiming.CalibrationBeats));
            Sfx.PlayUi("soft_tap");
        }

        public void TapCalibration()
        {
            if (_calibRun == null || !_calibRun.RegisterTap()) return;
            _calibStatus.text = Host.Strings.T("mg.danceParty.kalib_beat", ("n", _calibRun.TapDeltas.Count), ("max", DanceTiming.CalibrationBeats));
        }

        private void ResetCalibration()
        {
            Sfx.PlayUi("soft_chip");
            Host.Store.Update(s => DanceTiming.StoreManualOffset(s, 0.0), GameHost.TicksMs);
            _calibRun = null;
            if (_calibStatus != null) _calibStatus.text = Host.Strings.T("mg.danceParty.kalib_text");
            if (_calibTap != null) _calibTap.SetEnabled(false);
            if (_calibStart != null) _calibStart.SetEnabled(true);
            RefreshTimingLabels();
        }

        public void CloseCalibration()
        {
            if (_calib == null) return;
            Sfx.PlayUi("soft_close");
            _calib.RemoveFromHierarchy();
            _calib = null;
            _calibRun = null;
            RefreshTimingLabels();
        }

        /// <summary>The metronome and the result (from the screen's Update while the card is open).</summary>
        private void TickCalibration(float dt)
        {
            if (_calibFlash > 0f && _calibTap != null)
            {
                _calibFlash = Mathf.Max(0f, _calibFlash - dt);
                _calibTap.style.scale = new Scale(Vector3.one * (1f + _calibFlash * 0.4f));
            }
            if (_calibRun == null || !_calibRun.Running) return;
            foreach (var beat in _calibRun.Advance(dt))
            {
                // the last beat sounds higher: "nearly done"
                Sfx.Play("game_countdown", beat == DanceTiming.CalibrationBeats - 1 ? 3f : -1f, 0.9f);
                _calibFlash = 0.25f;
            }
            if (!_calibRun.Done) return;
            var t = Host.Strings;
            var result = _calibRun.ResultMs;
            if (_calibRun.TapDeltas.Count > 0) Host.Store.Update(s => DanceTiming.StoreManualOffset(s, result), GameHost.TicksMs);
            _calibStatus.text = t.T("mg.danceParty.kalib_ergebnis", ("ms", result));
            _calibTap.SetEnabled(false);
            _calibStart.SetEnabled(true);
            Sfx.PlayUi("soft_confirm");
            Debug.Log("[dance] calibration taps=" + _calibRun.TapDeltas.Count + " offset=" + result + " ms (base " + _calibRun.BaseLatencyMs.ToString("F0") + " ms)");
            RefreshTimingLabels();
        }

        public override void Update()
        {
            base.Update();
            if (_calib != null) TickCalibration(Time.unscaledDeltaTime);
        }
    }
}
