using System;
using System.Collections.Generic;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames
{
    /// <summary>
    /// Practice opponent for Schach (port of GOOBY-GODOT chess_ai.gd): negamax with alpha-beta over the PURE
    /// <see cref="ChessLogic"/> — material + small centre bonuses. Three strengths:
    /// 1 = "Gemütlich" (1 ply + strong evaluation noise, blunders), 2 = "Munter" (2 plies, finds mate in 1 and hanging
    /// pieces), 3 = "Knifflig" (3 plies + capture quiescence, finds mate in 2).
    /// Deterministic per injected <see cref="GoobyRng"/> — same position + same seed ⇒ same move (testable). Runs entirely
    /// on make/unmake, the logic passed in is unchanged afterwards. Answers well below 0.5 s on strength 3.
    /// </summary>
    public sealed class ChessAi
    {
        public const int MateScore = 100000;
        public const int MinStrength = 1;
        public const int MaxStrength = 3;

        private static readonly int[] DepthByStrength = { 1, 1, 2, 3 };
        private static readonly int[] NoiseByStrength = { 60, 60, 0, 0 };
        private static readonly int[] QuiescenceByStrength = { 0, 0, 0, 4 };
        private static readonly int[] PieceValues = { 0, 100, 320, 330, 500, 900, 0 };
        private static readonly int[] CenterBonus = { 0, 1, 2, 3, 3, 2, 1, 0 };

        private readonly GoobyRng _rng;
        private int _noise;
        private int _quiescence;

        public ChessAi(GoobyRng rng) => _rng = rng ?? new GoobyRng(1);

        public ChessAi(long seed = 1) : this(new GoobyRng(seed)) { }

        /// <summary>Nodes visited by the last <see cref="PickMove"/> (diagnostics / tests).</summary>
        public int LastNodes { get; private set; }

        /// <summary>Best move for the side to move; 0 = no legal move (mate/stalemate).</summary>
        public int PickMove(ChessLogic logic, int strength)
        {
            if (logic == null) return 0;
            var level = Math.Max(MinStrength, Math.Min(MaxStrength, strength));
            var depth = DepthByStrength[level];
            _noise = NoiseByStrength[level];
            _quiescence = QuiescenceByStrength[level];
            LastNodes = 0;
            var moves = OrderedMoves(logic);
            if (moves.Count == 0) return 0;
            var bestMove = 0;
            var bestScore = -MateScore * 2;
            foreach (var m in moves)
            {
                logic.MakeMove(m);
                var score = -Negamax(logic, depth - 1, -MateScore * 2, -bestScore);
                logic.UnmakeMove();
                if (_noise > 0) score += (int)(_rng.Next() * (_noise * 2.0)) - _noise;
                if (score > bestScore || (score == bestScore && _rng.Next() < 0.5))
                {
                    bestScore = score;
                    bestMove = m;
                }
            }
            return bestMove;
        }

        /// <summary>Same as <see cref="PickMove"/> as UCI ("" when there is no move).</summary>
        public string PickUci(ChessLogic logic, int strength)
        {
            var m = PickMove(logic, strength);
            return m == 0 ? "" : ChessLogic.MoveToUci(m);
        }

        /// <summary>Material balance in centipawns from the side to move's point of view (HUD).</summary>
        public static int Evaluate(ChessLogic logic)
        {
            var score = 0;
            for (var sq = 0; sq < 128; sq++)
            {
                if ((sq & 0x88) != 0) continue;
                var piece = logic.PieceOn(sq);
                if (piece == 0) continue;
                var kind = Math.Abs(piece);
                var value = PieceValues[kind];
                if (kind == ChessLogic.Pawn || kind == ChessLogic.Knight) value += CenterBonus[sq & 7] + CenterBonus[sq >> 4];
                score += piece > 0 ? value : -value;
            }
            return score * logic.ToMove;
        }

        private int Negamax(ChessLogic logic, int depth, int alpha, int beta)
        {
            LastNodes++;
            var moves = OrderedMoves(logic);
            if (moves.Count == 0) return logic.InCheck() ? -MateScore - depth : 0;
            if (depth <= 0) return _quiescence > 0 ? Quiesce(logic, _quiescence, alpha, beta) : Evaluate(logic);
            var best = -MateScore * 2;
            foreach (var m in moves)
            {
                logic.MakeMove(m);
                var score = -Negamax(logic, depth - 1, -beta, -Math.Max(alpha, best));
                logic.UnmakeMove();
                if (score > best)
                {
                    best = score;
                    if (best >= beta) break;
                }
            }
            return best;
        }

        /// <summary>Quiescence over captures only — stops the horizon blunders of strength 3.</summary>
        private int Quiesce(ChessLogic logic, int depth, int alpha, int beta)
        {
            LastNodes++;
            var stand = Evaluate(logic);
            if (depth <= 0 || stand >= beta) return stand;
            var best = Math.Max(stand, alpha);
            foreach (var m in logic.LegalMoves())
            {
                if (!IsCapture(logic, m)) continue;
                logic.MakeMove(m);
                var score = -Quiesce(logic, depth - 1, -beta, -best);
                logic.UnmakeMove();
                if (score > best)
                {
                    best = score;
                    if (best >= beta) break;
                }
            }
            return best;
        }

        /// <summary>Captures first (valuable victim before cheap attacker), promotions boosted — better cut-offs.</summary>
        private static List<int> OrderedMoves(ChessLogic logic)
        {
            var moves = logic.LegalMoves();
            var keys = new long[moves.Count];
            for (var i = 0; i < moves.Count; i++)
            {
                var m = moves[i];
                var target = logic.PieceOn(ChessLogic.MoveTo(m));
                var order = 0;
                if (target != 0 || (ChessLogic.MoveFlags(m) & ChessLogic.FlagEp) != 0)
                {
                    var victim = target != 0 ? PieceValues[Math.Abs(target)] : 100;
                    var attacker = PieceValues[Math.Abs(logic.PieceOn(ChessLogic.MoveFrom(m)))];
                    order = 10000 + victim * 10 - attacker;
                }
                if (ChessLogic.MovePromo(m) != 0) order += 9000;
                // Godot sorts Vector2i(-order, index): by order descending, ties in generation order
                keys[i] = ((long)(-order) << 32) | (uint)i;
            }
            var sorted = new List<int>(moves.Count);
            Array.Sort(keys);
            foreach (var k in keys) sorted.Add(moves[(int)(k & 0xFFFFFFFF)]);
            return sorted;
        }

        private static bool IsCapture(ChessLogic logic, int m) =>
            logic.PieceOn(ChessLogic.MoveTo(m)) != 0 || (ChessLogic.MoveFlags(m) & ChessLogic.FlagEp) != 0;
    }
}
