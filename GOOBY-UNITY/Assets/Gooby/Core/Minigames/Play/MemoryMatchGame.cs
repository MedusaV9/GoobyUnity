using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Memory round — port of memory_match.gd's _deal_board/_process/_unhandled_input/_resolve_pick/
    /// _board_cleared/_use_peek over the certified MemoryMatch rules, engine-free. A board is dealt face up for the
    /// reveal window, then two flips at a time: a pair stays, a miss turns back after the flip beat. Three clean
    /// matches in a row earn one peek (the whole board for PeekSec). Timed modes end with the board; endless deals
    /// the next board until the misses run out. Score = 20 − misses + time bonus + clear bonus (MemoryMatch.Score).
    /// </summary>
    public sealed class MemoryMatchGame
    {
        public const double IntroSec = 1.5;
        public const double ResolveExtraSec = 0.22;
        public const string Down = "down", Up = "up", Matched = "matched";

        public sealed class Card
        {
            public int Face;
            public string State = Down;
            public string Key => MemoryMatch.FaceKeys[Face % MemoryMatch.FaceKeys.Length];
        }

        public MemoryMatch.Tune Tune { get; }
        public MemoryMatch.Layout Layout { get; }
        public GoobyRng Rng { get; }
        public readonly List<Card> Cards = new List<Card>();
        public readonly List<int> Picked = new List<int>();
        public int Misses { get; private set; }
        public int MatchedPairs { get; private set; }
        public int MatchStreak { get; private set; }
        public int BoardsCleared { get; private set; }
        public double Elapsed { get; private set; }
        public double RevealLeft { get; private set; }
        public double PeekLeft { get; private set; }
        public double ResolveLeft { get; private set; }
        public double IntroLeft { get; private set; } = IntroSec;
        public int CleanMatches { get; private set; }
        public bool PeekReady { get; private set; }
        public bool PeekUsed { get; private set; }
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }

        public event Action<bool, int> Resolved;       // hit, index of the second card
        public event Action BoardCleared;
        public event Action PeekEarned;
        public event Action Finished_;

        public MemoryMatchGame(string mode, int seed, int level = 1)
        {
            Tune = MemoryMatch.ApplyDifficulty(MemoryMatch.Memory, mode);
            Layout = MemoryMatch.LayoutForLevel(level);
            Rng = new GoobyRng(seed);
            DealBoard();
        }

        /// <summary>All cards show their faces: the reveal window after a deal, or a peek.</summary>
        public bool Revealing => RevealLeft > 0.0 || PeekLeft > 0.0;
        /// <summary>Rest of the visible window (1 → 0) for the countdown bar; 0 when none is open.</summary>
        public double CountdownFrac => RevealLeft > 0.0 ? Math.Min(1.0, RevealLeft / Math.Max(0.001, Tune.RevealSec)) : PeekLeft > 0.0 ? Math.Min(1.0, PeekLeft / Math.Max(0.001, Tune.PeekSec)) : 0.0;
        public bool CanUsePeek => PeekReady && !PeekUsed;
        public int LiveScore => MemoryMatch.Score(Misses, Elapsed, Layout, Tune);

        private void DealBoard()
        {
            var deck = MemoryMatch.BuildDeck(Layout.Pairs, Rng);
            Cards.Clear();
            foreach (var face in deck) Cards.Add(new Card { Face = face });
            Picked.Clear();
            MatchedPairs = 0;
            RevealLeft = Tune.RevealSec;
        }

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            if (IntroLeft > 0.0)
            {
                IntroLeft = Math.Max(0.0, IntroLeft - dt);
                return;
            }
            Elapsed += dt;
            if (RevealLeft > 0.0) RevealLeft = Math.Max(0.0, RevealLeft - dt);
            if (PeekLeft > 0.0) PeekLeft = Math.Max(0.0, PeekLeft - dt);
            if (ResolveLeft > 0.0)
            {
                ResolveLeft = Math.Max(0.0, ResolveLeft - dt);
                if (ResolveLeft <= 0.0) ResolvePick();
            }
        }

        /// <summary>memory_match_logic.gd can_flip_card: playing, no window open, fewer than two picked, card face down.</summary>
        public bool CanFlip(int index) =>
            !Finished && IntroLeft <= 0.0 && !Revealing && ResolveLeft <= 0.0 && Picked.Count < 2
            && index >= 0 && index < Cards.Count && Cards[index].State == Down;

        /// <summary>Turns a card face up; the second one starts the resolve beat (FlipSec + 0.22 s).</summary>
        public bool Flip(int index)
        {
            if (!CanFlip(index)) return false;
            Cards[index].State = Up;
            Picked.Add(index);
            if (Picked.Count == 2) ResolveLeft = Tune.FlipSec + ResolveExtraSec;
            return true;
        }

        private void ResolvePick()
        {
            if (Picked.Count < 2) return;
            var a = Cards[Picked[0]];
            var b = Cards[Picked[1]];
            var second = Picked[1];
            var hit = MemoryMatch.IsMatch(a.Face, b.Face);
            if (hit)
            {
                a.State = Matched; b.State = Matched;
                MatchedPairs++;
                MatchStreak++;
            }
            else
            {
                a.State = Down; b.State = Down;
                Misses++;
                MatchStreak = 0;
            }
            Picked.Clear();
            // advance_peek_progress: three clean matches in a row earn the one peek of the round
            CleanMatches = hit ? CleanMatches + 1 : 0;
            var earned = !PeekUsed && !PeekReady && CleanMatches >= Tune.PeekEarnMatches;
            if (earned) PeekReady = true;
            Resolved?.Invoke(hit, second);
            if (earned) PeekEarned?.Invoke();
            if (MemoryMatch.EndlessShouldEnd(Misses, Tune)) { Finish(); return; }
            if (MatchedPairs >= Layout.Pairs) OnBoardCleared();
        }

        private void OnBoardCleared()
        {
            BoardsCleared++;
            BoardCleared?.Invoke();
            if (!Tune.Endless) { Finish(); return; }
            DealBoard();   // §G5.4 endless: boards chain on, only the misses carry over
        }

        /// <summary>The earned peek: every card face up for PeekSec, once per round.</summary>
        public bool UsePeek()
        {
            if (Finished || !CanUsePeek || IntroLeft > 0.0) return false;
            PeekUsed = true;
            PeekLeft = Tune.PeekSec;
            return true;
        }

        private void Finish()
        {
            if (Finished) return;
            Finished = true;
            FinalScore = LiveScore;
            Finished_?.Invoke();
        }
    }
}
