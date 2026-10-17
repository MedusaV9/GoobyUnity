using System;
using System.Collections.Generic;
using System.Text;

namespace Gooby.Core.Minigames
{
    /// <summary>
    /// Schach — pure move logic (port of GOOBY-GODOT chess_logic.gd): 0x88 board with full legality (castling, en passant,
    /// promotion), mate/stalemate and the draw rules (50 moves, threefold repetition, dead material), FEN import/export,
    /// UCI moves ("e2e4", "a7a8q") and <see cref="Perft"/> for the standard test positions. No engine types — the server
    /// stays a pure turn relay (like <see cref="BattleshipLogic"/>), both clients evaluate the same rules.
    /// <para>
    /// Move encoding (int): from | to&lt;&lt;7 | promo&lt;&lt;14 | flags&lt;&lt;17. <see cref="MakeMove"/>/<see cref="UnmakeMove"/>
    /// are the fast search path (perft/AI, NO repetition tracking); <see cref="PlayMove"/>/<see cref="UndoPlay"/> are the
    /// game path (validates legality, counts positions for the repetition rule, keeps the UCI/SAN move list).
    /// Squares: rank*16 + file (a1 = 0, h8 = 0x77); files 0..7 = a..h, ranks 0..7 = 1..8.
    /// </para>
    /// </summary>
    public sealed class ChessLogic
    {
        public const int White = 1;
        public const int Black = -1;

        public const int Pawn = 1;
        public const int Knight = 2;
        public const int Bishop = 3;
        public const int Rook = 4;
        public const int Queen = 5;
        public const int King = 6;

        public const int CastleWK = 1;
        public const int CastleWQ = 2;
        public const int CastleBK = 4;
        public const int CastleBQ = 8;

        public const int FlagEp = 1;
        public const int FlagCastle = 2;
        public const int FlagDouble = 4;

        public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

        // result ids = the Godot reason strings (GAME_OVER {reason}) and the chess.reason.* string keys
        public const string ResultRunning = "";
        public const string ResultCheckmate = "checkmate";
        public const string ResultStalemate = "stalemate";
        public const string ResultDraw50 = "draw_50";
        public const string ResultDrawRepetition = "draw_repetition";
        public const string ResultDrawMaterial = "draw_material";

        private const string FenPieces = "PNBRQK";
        private const string PromoLetters = "nbrq";
        private static readonly int[] KnightOffsets = { 18, 33, 31, 14, -18, -33, -31, -14 };
        private static readonly int[] KingOffsets = { 1, -1, 16, -16, 15, 17, -15, -17 };
        private static readonly int[] BishopDirs = { 15, 17, -15, -17 };
        private static readonly int[] RookDirs = { 1, -1, 16, -16 };
        private static readonly int[] CastleMasks = BuildCastleMasks();

        private struct Undo
        {
            public int Move, Captured, CapturedSq, Castling, Ep, Halfmove;
        }

        private readonly int[] _board = new int[128];
        private readonly List<Undo> _undo = new List<Undo>();
        private readonly Dictionary<string, int> _keys = new Dictionary<string, int>();
        private readonly List<string> _played = new List<string>();
        private readonly List<string> _uci = new List<string>();
        private readonly List<string> _san = new List<string>();
        private int _kingWhite = -1, _kingBlack = -1;

        public int ToMove { get; private set; } = White;
        public int Castling { get; private set; }
        public int EpSquare { get; private set; } = -1;
        public int Halfmove { get; private set; }
        public int Fullmove { get; private set; } = 1;

        /// <summary>Moves played via <see cref="PlayMove"/> since the last FEN load, as UCI ("e2e4").</summary>
        public IReadOnlyList<string> Moves => _uci;
        /// <summary>The same moves in short algebraic notation with English letters ("e4", "Nf3", "O-O", "exd8=Q#").</summary>
        public IReadOnlyList<string> SanMoves => _san;
        /// <summary>Last played move (0 before the first).</summary>
        public int LastMove => _undo.Count > 0 && _played.Count > 0 ? _undo[_undo.Count - 1].Move : 0;

        public ChessLogic() => Reset();

        public ChessLogic(string fen)
        {
            if (!FromFen(fen)) throw new ArgumentException("bad FEN: " + fen, nameof(fen));
        }

        public void Reset() => FromFen(StartFen);

        // ------------------------------------------------------------------ FEN

        /// <summary>Loads a FEN. False = unusable FEN (the state is undefined then).</summary>
        public bool FromFen(string fen)
        {
            var parts = (fen ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) return false;
            Array.Clear(_board, 0, _board.Length);
            _kingWhite = -1;
            _kingBlack = -1;
            var ranks = parts[0].Split('/');
            if (ranks.Length != 8) return false;
            for (var rankIdx = 0; rankIdx < 8; rankIdx++)
            {
                var rank = 7 - rankIdx;
                var file = 0;
                foreach (var ch in ranks[rankIdx])
                {
                    if (ch >= '0' && ch <= '9')
                    {
                        file += ch - '0';
                        continue;
                    }
                    if (file > 7) return false;
                    var kind = FenPieces.IndexOf(char.ToUpperInvariant(ch)) + 1;
                    if (kind == 0) return false;
                    var color = char.IsUpper(ch) ? White : Black;
                    var sq = rank * 16 + file;
                    _board[sq] = kind * color;
                    if (kind == King)
                    {
                        if (color == White) _kingWhite = sq;
                        else _kingBlack = sq;
                    }
                    file++;
                }
            }
            ToMove = parts[1] == "w" ? White : Black;
            Castling = 0;
            if (parts[2].Contains("K")) Castling |= CastleWK;
            if (parts[2].Contains("Q")) Castling |= CastleWQ;
            if (parts[2].Contains("k")) Castling |= CastleBK;
            if (parts[2].Contains("q")) Castling |= CastleBQ;
            EpSquare = ParseSquare(parts[3]);
            Halfmove = parts.Length > 4 && int.TryParse(parts[4], out var h) ? h : 0;
            Fullmove = parts.Length > 5 && int.TryParse(parts[5], out var f) ? f : 1;
            _undo.Clear();
            _keys.Clear();
            _played.Clear();
            _uci.Clear();
            _san.Clear();
            _keys[PositionKey()] = 1;
            return _kingWhite >= 0 && _kingBlack >= 0;
        }

        public string ToFen()
        {
            var sb = new StringBuilder(90);
            for (var rankIdx = 0; rankIdx < 8; rankIdx++)
            {
                var rank = 7 - rankIdx;
                var empty = 0;
                for (var file = 0; file < 8; file++)
                {
                    var piece = _board[rank * 16 + file];
                    if (piece == 0)
                    {
                        empty++;
                        continue;
                    }
                    if (empty > 0)
                    {
                        sb.Append(empty);
                        empty = 0;
                    }
                    var letter = FenPieces[Math.Abs(piece) - 1];
                    sb.Append(piece > 0 ? letter : char.ToLowerInvariant(letter));
                }
                if (empty > 0) sb.Append(empty);
                if (rankIdx < 7) sb.Append('/');
            }
            var rights = "";
            if ((Castling & CastleWK) != 0) rights += "K";
            if ((Castling & CastleWQ) != 0) rights += "Q";
            if ((Castling & CastleBK) != 0) rights += "k";
            if ((Castling & CastleBQ) != 0) rights += "q";
            sb.Append(ToMove == White ? " w " : " b ");
            sb.Append(rights.Length > 0 ? rights : "-");
            sb.Append(' ').Append(EpSquare < 0 ? "-" : SquareName(EpSquare));
            sb.Append(' ').Append(Halfmove).Append(' ').Append(Fullmove);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ squares / moves

        /// <summary>Piece from the UI's point of view: file 0..7 (a..h), rank 0..7 (1..8). Positive = white.</summary>
        public int PieceAt(int file, int rank) => file < 0 || file > 7 || rank < 0 || rank > 7 ? 0 : _board[rank * 16 + file];

        public int PieceOn(int sq) => (sq & 0x88) != 0 ? 0 : _board[sq];

        public static int Square(int file, int rank) => rank * 16 + file;
        public static int FileOf(int sq) => sq & 7;
        public static int RankOf(int sq) => sq >> 4;

        public static int MoveFrom(int m) => m & 127;
        public static int MoveTo(int m) => (m >> 7) & 127;
        public static int MovePromo(int m) => (m >> 14) & 7;
        public static int MoveFlags(int m) => m >> 17;

        public static string MoveToUci(int m)
        {
            var uci = SquareName(m & 127) + SquareName((m >> 7) & 127);
            var promo = (m >> 14) & 7;
            if (promo != 0) uci += PromoLetters[promo - 2];
            return uci;
        }

        /// <summary>The legal move for a UCI string; 0 = no legal move with that notation (also for a missing promotion letter).</summary>
        public int UciToMove(string uci)
        {
            var text = (uci ?? "").Trim().ToLowerInvariant();
            if (text.Length < 4) return 0;
            var from = ParseSquare(text.Substring(0, 2));
            var to = ParseSquare(text.Substring(2, 2));
            if (from < 0 || to < 0) return 0;
            var promo = 0;
            if (text.Length >= 5)
            {
                promo = PromoLetters.IndexOf(text[4]) + 2;
                if (promo == 1) return 0;
            }
            foreach (var m in LegalMoves())
                if ((m & 127) == from && ((m >> 7) & 127) == to && ((m >> 14) & 7) == promo) return m;
            return 0;
        }

        /// <summary>All legal moves of the side to move.</summary>
        public List<int> LegalMoves()
        {
            var pseudo = PseudoMoves();
            var outList = new List<int>(pseudo.Count);
            foreach (var m in pseudo)
            {
                MakeMove(m);
                var kingSq = ToMove == Black ? _kingWhite : _kingBlack;
                if (!IsSquareAttacked(kingSq, ToMove)) outList.Add(m);
                UnmakeMove();
            }
            return outList;
        }

        public List<int> LegalMovesFrom(int sq)
        {
            var outList = new List<int>();
            foreach (var m in LegalMoves()) if ((m & 127) == sq) outList.Add(m);
            return outList;
        }

        /// <summary>Square of the king of <paramref name="color"/> (-1 when missing).</summary>
        public int KingSquare(int color) => color == White ? _kingWhite : _kingBlack;

        public bool InCheck() => IsSquareAttacked(ToMove == White ? _kingWhite : _kingBlack, -ToMove);

        /// <summary>Does <paramref name="by"/> (White/Black) attack the square? Standard 0x88 scan.</summary>
        public bool IsSquareAttacked(int sq, int by)
        {
            if (sq < 0) return false;
            var p = sq - 15 * by;
            if ((p & 0x88) == 0 && _board[p] == Pawn * by) return true;
            p = sq - 17 * by;
            if ((p & 0x88) == 0 && _board[p] == Pawn * by) return true;
            foreach (var d in KnightOffsets)
            {
                p = sq + d;
                if ((p & 0x88) == 0 && _board[p] == Knight * by) return true;
            }
            foreach (var d in KingOffsets)
            {
                p = sq + d;
                if ((p & 0x88) == 0 && _board[p] == King * by) return true;
            }
            foreach (var d in BishopDirs)
            {
                p = sq + d;
                while ((p & 0x88) == 0)
                {
                    var piece = _board[p];
                    if (piece != 0)
                    {
                        if (piece == Bishop * by || piece == Queen * by) return true;
                        break;
                    }
                    p += d;
                }
            }
            foreach (var d in RookDirs)
            {
                p = sq + d;
                while ((p & 0x88) == 0)
                {
                    var piece = _board[p];
                    if (piece != 0)
                    {
                        if (piece == Rook * by || piece == Queen * by) return true;
                        break;
                    }
                    p += d;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ make / unmake (search path)

        /// <summary>Fast search path (perft/AI): the move must be pseudo-legal, NO repetition tracking. Games use <see cref="PlayMove"/>.</summary>
        public void MakeMove(int m)
        {
            var from = m & 127;
            var to = (m >> 7) & 127;
            var promo = (m >> 14) & 7;
            var flags = m >> 17;
            var piece = _board[from];
            var capturedSq = to;
            if ((flags & FlagEp) != 0) capturedSq = to - 16 * ToMove;
            var captured = _board[capturedSq];
            _undo.Add(new Undo { Move = m, Captured = captured, CapturedSq = capturedSq, Castling = Castling, Ep = EpSquare, Halfmove = Halfmove });
            if (captured != 0) _board[capturedSq] = 0;
            _board[from] = 0;
            _board[to] = promo == 0 ? piece : promo * ToMove;
            if ((flags & FlagCastle) != 0)
            {
                var rankBase = from & 0x70;
                if (to > from)
                {
                    _board[rankBase + 5] = _board[rankBase + 7];
                    _board[rankBase + 7] = 0;
                }
                else
                {
                    _board[rankBase + 3] = _board[rankBase];
                    _board[rankBase] = 0;
                }
            }
            if (piece == King * ToMove)
            {
                if (ToMove == White) _kingWhite = to;
                else _kingBlack = to;
            }
            EpSquare = (flags & FlagDouble) != 0 ? (from + to) >> 1 : -1;
            Castling = Castling & CastleMasks[from] & CastleMasks[to];
            Halfmove = captured != 0 || Math.Abs(piece) == Pawn ? 0 : Halfmove + 1;
            if (ToMove == Black) Fullmove++;
            ToMove = -ToMove;
        }

        public void UnmakeMove()
        {
            var u = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            var m = u.Move;
            var from = m & 127;
            var to = (m >> 7) & 127;
            var promo = (m >> 14) & 7;
            var flags = m >> 17;
            ToMove = -ToMove;
            if (ToMove == Black) Fullmove--;
            var piece = _board[to];
            if (promo != 0) piece = Pawn * ToMove;
            _board[from] = piece;
            _board[to] = 0;
            if (u.Captured != 0) _board[u.CapturedSq] = u.Captured;
            if ((flags & FlagCastle) != 0)
            {
                var rankBase = from & 0x70;
                if (to > from)
                {
                    _board[rankBase + 7] = _board[rankBase + 5];
                    _board[rankBase + 5] = 0;
                }
                else
                {
                    _board[rankBase] = _board[rankBase + 3];
                    _board[rankBase + 3] = 0;
                }
            }
            if (piece == King * ToMove)
            {
                if (ToMove == White) _kingWhite = from;
                else _kingBlack = from;
            }
            Castling = u.Castling;
            EpSquare = u.Ep;
            Halfmove = u.Halfmove;
        }

        // ------------------------------------------------------------------ game path

        /// <summary>Game path: validates legality, counts the position for the repetition rule, records UCI + SAN. False = illegal (nothing happens).</summary>
        public bool PlayMove(int m)
        {
            if (m == 0 || !LegalMoves().Contains(m)) return false;
            var san = SanBody(m);
            MakeMove(m);
            var key = PositionKey();
            _keys.TryGetValue(key, out var c);
            _keys[key] = c + 1;
            _played.Add(key);
            _uci.Add(MoveToUci(m));
            if (InCheck()) san += LegalMoves().Count == 0 ? "#" : "+";
            _san.Add(san);
            return true;
        }

        public bool PlayUci(string uci)
        {
            var m = UciToMove(uci);
            return m != 0 && PlayMove(m);
        }

        /// <summary>Takes the last <see cref="PlayMove"/> back (the server rejected it, practice undo …).</summary>
        public bool UndoPlay()
        {
            if (_played.Count == 0 || _undo.Count == 0) return false;
            var key = _played[_played.Count - 1];
            _played.RemoveAt(_played.Count - 1);
            _uci.RemoveAt(_uci.Count - 1);
            _san.RemoveAt(_san.Count - 1);
            if (_keys.TryGetValue(key, out var count))
            {
                if (count <= 1) _keys.Remove(key);
                else _keys[key] = count - 1;
            }
            UnmakeMove();
            return true;
        }

        /// <summary>"" = running, else checkmate/stalemate/draw_*. On checkmate the side to move has lost.</summary>
        public string Result()
        {
            if (LegalMoves().Count == 0) return InCheck() ? ResultCheckmate : ResultStalemate;
            if (Halfmove >= 100) return ResultDraw50;
            if (_keys.TryGetValue(PositionKey(), out var n) && n >= 3) return ResultDrawRepetition;
            if (InsufficientMaterial()) return ResultDrawMaterial;
            return ResultRunning;
        }

        /// <summary>How often the current position occurred (repetition rule).</summary>
        public int RepetitionCount => _keys.TryGetValue(PositionKey(), out var n) ? n : 0;

        /// <summary>Node counter for the standard test positions (proof of legality).</summary>
        public long Perft(int depth)
        {
            if (depth <= 0) return 1;
            var moves = LegalMoves();
            if (depth == 1) return moves.Count;
            long count = 0;
            foreach (var m in moves)
            {
                MakeMove(m);
                count += Perft(depth - 1);
                UnmakeMove();
            }
            return count;
        }

        // ------------------------------------------------------------------ notation

        /// <summary>SAN of a legal move in the current position (with +/#), English letters; "" for an illegal move.</summary>
        public string San(int m)
        {
            if (m == 0 || !LegalMoves().Contains(m)) return "";
            var san = SanBody(m);
            MakeMove(m);
            if (InCheck()) san += LegalMoves().Count == 0 ? "#" : "+";
            UnmakeMove();
            return san;
        }

        private string SanBody(int m)
        {
            var from = m & 127;
            var to = (m >> 7) & 127;
            var promo = (m >> 14) & 7;
            var flags = m >> 17;
            if ((flags & FlagCastle) != 0) return to > from ? "O-O" : "O-O-O";
            var kind = Math.Abs(_board[from]);
            var capture = _board[to] != 0 || (flags & FlagEp) != 0;
            var sb = new StringBuilder(8);
            if (kind == Pawn)
            {
                if (capture) sb.Append((char)('a' + (from & 7))).Append('x');
                sb.Append(SquareName(to));
                if (promo != 0) sb.Append('=').Append(FenPieces[promo - 1]);
                return sb.ToString();
            }
            sb.Append(FenPieces[kind - 1]);
            // disambiguation: another piece of the same kind reaching the same square
            bool sameFile = false, sameRank = false, other = false;
            foreach (var o in LegalMoves())
            {
                var of = o & 127;
                if (of == from || ((o >> 7) & 127) != to || Math.Abs(_board[of]) != kind) continue;
                other = true;
                if ((of & 7) == (from & 7)) sameFile = true;
                if ((of >> 4) == (from >> 4)) sameRank = true;
            }
            if (other)
            {
                if (!sameFile) sb.Append((char)('a' + (from & 7)));
                else if (!sameRank) sb.Append((char)('1' + (from >> 4)));
                else sb.Append(SquareName(from));
            }
            if (capture) sb.Append('x');
            sb.Append(SquareName(to));
            return sb.ToString();
        }

        public static string SquareName(int sq) => ((char)('a' + (sq & 7))).ToString() + (char)('1' + (sq >> 4));

        public static int ParseSquare(string text)
        {
            if (text == null || text.Length != 2) return -1;
            var file = text[0] - 'a';
            var rank = text[1] - '1';
            if (file < 0 || file > 7 || rank < 0 || rank > 7) return -1;
            return rank * 16 + file;
        }

        private static int Encode(int from, int to, int promo, int flags) => from | (to << 7) | (promo << 14) | (flags << 17);

        // ------------------------------------------------------------------ generation

        private List<int> PseudoMoves()
        {
            var outList = new List<int>(48);
            for (var sq = 0; sq < 128; sq++)
            {
                if ((sq & 0x88) != 0) continue;
                var piece = _board[sq];
                if (piece == 0 || Math.Sign(piece) != ToMove) continue;
                switch (Math.Abs(piece))
                {
                    case Pawn: PawnMoves(sq, outList); break;
                    case Knight: LeaperMoves(sq, KnightOffsets, outList); break;
                    case Bishop: SliderMoves(sq, BishopDirs, outList); break;
                    case Rook: SliderMoves(sq, RookDirs, outList); break;
                    case Queen: SliderMoves(sq, KingOffsets, outList); break;
                    case King:
                        LeaperMoves(sq, KingOffsets, outList);
                        CastleMoves(outList);
                        break;
                }
            }
            return outList;
        }

        private void PawnMoves(int sq, List<int> outList)
        {
            var dir = 16 * ToMove;
            var startRank = ToMove == White ? 1 : 6;
            var promoRank = ToMove == White ? 7 : 0;
            var one = sq + dir;
            if ((one & 0x88) == 0 && _board[one] == 0)
            {
                PushPawn(sq, one, promoRank, outList);
                var two = one + dir;
                if ((sq >> 4) == startRank && _board[two] == 0) outList.Add(Encode(sq, two, 0, FlagDouble));
            }
            for (var i = 0; i < 2; i++)
            {
                var to = sq + (i == 0 ? dir - 1 : dir + 1);
                if ((to & 0x88) != 0) continue;
                if (_board[to] != 0 && Math.Sign(_board[to]) != ToMove) PushPawn(sq, to, promoRank, outList);
                else if (to == EpSquare) outList.Add(Encode(sq, to, 0, FlagEp));
            }
        }

        private static void PushPawn(int from, int to, int promoRank, List<int> outList)
        {
            if ((to >> 4) == promoRank)
            {
                outList.Add(Encode(from, to, Queen, 0));
                outList.Add(Encode(from, to, Rook, 0));
                outList.Add(Encode(from, to, Bishop, 0));
                outList.Add(Encode(from, to, Knight, 0));
            }
            else outList.Add(Encode(from, to, 0, 0));
        }

        private void LeaperMoves(int sq, int[] offsets, List<int> outList)
        {
            foreach (var d in offsets)
            {
                var to = sq + d;
                if ((to & 0x88) != 0) continue;
                if (_board[to] == 0 || Math.Sign(_board[to]) != ToMove) outList.Add(Encode(sq, to, 0, 0));
            }
        }

        private void SliderMoves(int sq, int[] dirs, List<int> outList)
        {
            foreach (var d in dirs)
            {
                var to = sq + d;
                while ((to & 0x88) == 0)
                {
                    var piece = _board[to];
                    if (piece == 0) outList.Add(Encode(sq, to, 0, 0));
                    else
                    {
                        if (Math.Sign(piece) != ToMove) outList.Add(Encode(sq, to, 0, 0));
                        break;
                    }
                    to += d;
                }
            }
        }

        /// <summary>Castling: rights + empty squares + the king does not move out of / through / into check.</summary>
        private void CastleMoves(List<int> outList)
        {
            var enemy = -ToMove;
            var rankBase = ToMove == White ? 0x00 : 0x70;
            var kingSide = ToMove == White ? CastleWK : CastleBK;
            var queenSide = ToMove == White ? CastleWQ : CastleBQ;
            if (_board[rankBase + 4] != King * ToMove) return;
            if ((Castling & kingSide) != 0
                && _board[rankBase + 5] == 0 && _board[rankBase + 6] == 0 && _board[rankBase + 7] == Rook * ToMove
                && !IsSquareAttacked(rankBase + 4, enemy) && !IsSquareAttacked(rankBase + 5, enemy) && !IsSquareAttacked(rankBase + 6, enemy))
                outList.Add(Encode(rankBase + 4, rankBase + 6, 0, FlagCastle));
            if ((Castling & queenSide) != 0
                && _board[rankBase + 3] == 0 && _board[rankBase + 2] == 0 && _board[rankBase + 1] == 0 && _board[rankBase] == Rook * ToMove
                && !IsSquareAttacked(rankBase + 4, enemy) && !IsSquareAttacked(rankBase + 3, enemy) && !IsSquareAttacked(rankBase + 2, enemy))
                outList.Add(Encode(rankBase + 4, rankBase + 2, 0, FlagCastle));
        }

        /// <summary>Position key for the repetition rule (board + side to move + castling + ep — the FIDE-relevant parts).</summary>
        private string PositionKey()
        {
            var sb = new StringBuilder(80);
            for (var rank = 0; rank < 8; rank++)
                for (var file = 0; file < 8; file++)
                {
                    var p = _board[rank * 16 + file];
                    sb.Append(p == 0 ? '.' : p > 0 ? FenPieces[p - 1] : char.ToLowerInvariant(FenPieces[-p - 1]));
                }
            sb.Append('|').Append(ToMove).Append('|').Append(Castling).Append('|').Append(EpSquare);
            return sb.ToString();
        }

        /// <summary>Dead material: K vs K, K+minor vs K, K+B vs K+B (bishops on the same colour).</summary>
        private bool InsufficientMaterial()
        {
            int a = -1, b = -1, minors = 0;
            for (var sq = 0; sq < 128; sq++)
            {
                if ((sq & 0x88) != 0) continue;
                var kind = Math.Abs(_board[sq]);
                if (kind == 0 || kind == King) continue;
                if (kind == Pawn || kind == Rook || kind == Queen) return false;
                minors++;
                if (minors > 2) return false;
                if (a < 0) a = sq;
                else b = sq;
            }
            if (minors <= 1) return true;
            if (Math.Abs(_board[a]) != Bishop || Math.Abs(_board[b]) != Bishop) return false;
            if (Math.Sign(_board[a]) == Math.Sign(_board[b])) return false;
            return ((a >> 4) + (a & 7)) % 2 == ((b >> 4) + (b & 7)) % 2;
        }

        private static int[] BuildCastleMasks()
        {
            var masks = new int[128];
            for (var i = 0; i < masks.Length; i++) masks[i] = 15;
            masks[0x04] = 15 - CastleWK - CastleWQ;
            masks[0x00] = 15 - CastleWQ;
            masks[0x07] = 15 - CastleWK;
            masks[0x74] = 15 - CastleBK - CastleBQ;
            masks[0x70] = 15 - CastleBQ;
            masks[0x77] = 15 - CastleBK;
            return masks;
        }
    }
}
