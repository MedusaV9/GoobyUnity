using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Games;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Tanzparty round — port of dance_party.gd's _process/_append_segment/_tick_endless/_expire_notes/
    /// _tap_lane/_judge over the certified DanceParty rules, engine-free. The seeded 75-s chart (100 BPM, three
    /// lanes) scrolls down to the hit line after a 2.4 s lead-in; a tap on a lane judges the nearest note within
    /// 140 ms (perfect ≤ 70 ms +4, good +2), a note that passes the line by more than 140 ms is a miss (−2, combo
    /// broken). Five perfects in a row at combo tier 3 start a 5-s encore that doubles every hit. Endless appends
    /// chart segments and ends after three 12-s sections with a miss in them.
    /// </summary>
    public sealed class DancePartyGame
    {
        public sealed class ChartNote
        {
            public double Time;
            public int Lane;
            public bool Hit, Missed;
        }

        public DanceParty.Tune Tune { get; }
        public DanceParty.Tally Tally { get; } = new DanceParty.Tally();
        public DanceParty.FeverChain Fever { get; } = new DanceParty.FeverChain();
        public double SongTime { get; private set; }
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }
        public int Breaks { get; private set; }
        public readonly List<ChartNote> Notes = new List<ChartNote>();
        /// <summary>Index of the first note that is neither hit nor missed (everything before is history).</summary>
        public int Head { get; private set; }
        private int _segment, _section;
        private bool _sectionMissed;

        public event Action<string, int, double> Judged;     // kind (perfect/good/miss), lane, note time
        public event Action EncoreStarted;
        public event Action Finished_;

        public DancePartyGame(string mode)
        {
            Tune = DanceParty.ApplyDifficulty(DanceParty.Tuning, mode);
            SongTime = -Tune.LeadInSec;
            AppendSegment();
        }

        public int Score => DanceParty.Score(Tally);
        public int Combo => Tally.Combo;
        public int Tier => DanceParty.ComboTier(Tally.Combo);
        public bool Encore => DanceParty.EncoreActive(Fever, SongTime);
        public double TimeLeft => Tune.Endless ? double.PositiveInfinity : Math.Max(0.0, Tune.DurationSec - SongTime);
        /// <summary>0 during the lead-in rising to 1 at the first beat.</summary>
        public double IntroProgress => Math.Max(0.0, Math.Min(1.0, 1.0 + SongTime / Tune.LeadInSec));
        public bool InLeadIn => SongTime < 0.0;

        private void AppendSegment()
        {
            var offset = _segment * Tune.DurationSec;
            foreach (var n in DanceParty.GeneratePattern((int)DanceParty.SegmentSeed(_segment), Tune))
                Notes.Add(new ChartNote { Time = n.Time + offset, Lane = n.Lane });
            _segment++;
        }

        /// <summary>
        /// Shifts the round's time line by the latency offset (DanceTiming.PlayTime): positive seconds make the notes arrive
        /// and the hit window open that much later against the song. Called once before the first tick; the judging rules stay.
        /// </summary>
        public void ShiftTimeline(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return;
            SongTime -= seconds;
        }

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            SongTime += dt;
            if (Tune.Endless)
            {
                TickEndless();
                if (Finished) return;
            }
            ExpireNotes();
            if (!Tune.Endless && SongTime >= Tune.DurationSec) Finish();
        }

        private void TickEndless()
        {
            if (SongTime >= 0.0)
            {
                var section = DanceParty.SectionIndex(SongTime);
                if (section > _section)
                {
                    if (_sectionMissed) Breaks++;
                    _section = section;
                    _sectionMissed = false;
                    if (Breaks >= Tune.EndlessBreakLimit) { Finish(); return; }
                }
            }
            while (SongTime + Tune.NoteTravelSec >= _segment * Tune.DurationSec) AppendSegment();
        }

        private void ExpireNotes()
        {
            while (Head < Notes.Count)
            {
                var n = Notes[Head];
                if (n.Hit || n.Missed) { Head++; continue; }
                if (DanceParty.NoteLifecycle(n.Time, SongTime, Tune) != "expired") break;
                n.Missed = true;
                Head++;
                Judge("miss", n.Lane, n.Time);
            }
        }

        /// <summary>A tap on a lane: the nearest open note within the good window is judged; nothing in reach is no tap at all.</summary>
        public string Tap(int lane)
        {
            if (Finished) return null;
            lane = Math.Max(0, Math.Min(DanceParty.Lanes - 1, lane));
            var end = Math.Min(Notes.Count, Head + 48);
            var window = Tune.GoodMs / 1000.0;
            var best = -1; var bestAbs = double.PositiveInfinity;
            for (var i = Head; i < end; i++)
            {
                var n = Notes[i];
                if (n.Lane != lane || n.Hit || n.Missed) continue;
                if (n.Time - SongTime > window) break;
                var d = Math.Abs(n.Time - SongTime);
                if (d <= window && d < bestAbs) { best = i; bestAbs = d; }
            }
            if (best < 0) return null;
            var note = Notes[best];
            note.Hit = true;
            var kind = DanceParty.ClassifyHit(note.Time - SongTime, Tune);
            if (kind == "") kind = "miss";
            Judge(kind, lane, note.Time);
            return kind;
        }

        private void Judge(string kind, int lane, double noteTime)
        {
            DanceParty.ApplyJudgment(Tally, kind);
            var (active, started) = DanceParty.AdvanceFeverChain(Fever, kind, Tally.Combo, SongTime);
            Tally.Bonus += DanceParty.EncoreBonus(kind, active);
            if (kind == "miss") _sectionMissed = true;
            Judged?.Invoke(kind, lane, noteTime);
            if (started) EncoreStarted?.Invoke();
        }

        /// <summary>The notes on their way down: (note, 0 far away … 1 on the line … beyond 1 past it).</summary>
        public IEnumerable<(ChartNote note, double progress)> Visible()
        {
            var travel = Tune.NoteTravelSec;
            for (var i = Head; i < Notes.Count; i++)
            {
                var n = Notes[i];
                if (n.Hit || n.Missed) continue;
                var ahead = n.Time - SongTime;
                if (ahead > travel) break;
                yield return (n, 1.0 - ahead / travel);
            }
        }

        private void Finish()
        {
            if (Finished) return;
            Finished = true;
            FinalScore = Score;
            Finished_?.Invoke();
        }
    }
}
