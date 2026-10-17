using System;
using System.Collections.Generic;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames
{
    /// <summary>A cell of the 10×10 board: x = column A..J (0..9), y = row 1..10 (0..9). (-1,-1) = invalid.</summary>
    public readonly struct BattleshipCell : IEquatable<BattleshipCell>
    {
        public readonly int X;
        public readonly int Y;

        public BattleshipCell(int x, int y)
        {
            X = x;
            Y = y;
        }

        public static readonly BattleshipCell Invalid = new BattleshipCell(-1, -1);

        public bool InBounds => X >= 0 && Y >= 0 && X < BattleshipLogic.Grid && Y < BattleshipLogic.Grid;

        /// <summary>Protocol notation ("B4" = column B, row 4); "" when out of bounds.</summary>
        public string Ref => BattleshipLogic.CellToRef(this);

        public bool Equals(BattleshipCell other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is BattleshipCell c && Equals(c);
        public override int GetHashCode() => X * 31 + Y;
        public static bool operator ==(BattleshipCell a, BattleshipCell b) => a.Equals(b);
        public static bool operator !=(BattleshipCell a, BattleshipCell b) => !a.Equals(b);
        public override string ToString() => InBounds ? Ref : "(" + X + "," + Y + ")";
    }

    /// <summary>
    /// One ship: bow at (<see cref="X"/>, <see cref="Y"/>), <see cref="Len"/> cells to the right (horizontal) or down.
    /// JSON shape as in battleship_logic.gd: <c>{"at":[x,y],"len":n,"horizontal":bool}</c>.
    /// </summary>
    public sealed class BattleshipShip
    {
        public int X { get; }
        public int Y { get; }
        public int Len { get; }
        public bool Horizontal { get; }

        public BattleshipShip(int x, int y, int len, bool horizontal)
        {
            X = x;
            Y = y;
            Len = len;
            Horizontal = horizontal;
        }

        public BattleshipCell Bow => new BattleshipCell(X, Y);

        public List<BattleshipCell> Cells()
        {
            var cells = new List<BattleshipCell>(Math.Max(0, Len));
            for (var i = 0; i < Len; i++) cells.Add(Horizontal ? new BattleshipCell(X + i, Y) : new BattleshipCell(X, Y + i));
            return cells;
        }

        public bool Contains(BattleshipCell c) =>
            Horizontal ? c.Y == Y && c.X >= X && c.X < X + Len : c.X == X && c.Y >= Y && c.Y < Y + Len;

        /// <summary>Index of <paramref name="c"/> along the ship (0 = bow), -1 when not part of it.</summary>
        public int SegmentOf(BattleshipCell c) => Contains(c) ? (Horizontal ? c.X - X : c.Y - Y) : -1;

        public BattleshipShip Rotated() => new BattleshipShip(X, Y, Len, !Horizontal);

        public JObject ToJson() => new JObject { ["at"] = new JArray(X, Y), ["len"] = Len, ["horizontal"] = Horizontal };

        /// <summary>Parses {"at":[x,y],"len":n,"horizontal":b}; null for anything else.</summary>
        public static BattleshipShip FromJson(JToken token)
        {
            if (!(token is JObject o) || !(o["at"] is JArray at) || at.Count < 2) return null;
            if (!IsInt(at[0]) || !IsInt(at[1]) || !IsInt(o["len"])) return null;
            var horizontal = o["horizontal"] == null || o["horizontal"].Type != JTokenType.Boolean || (bool)o["horizontal"];
            return new BattleshipShip((int)(long)at[0], (int)(long)at[1], (int)(long)o["len"], horizontal);
        }

        private static bool IsInt(JToken t) => t != null && t.Type == JTokenType.Integer;

        public override string ToString() => BattleshipLogic.CellToRef(Bow) + (Horizontal ? "→" : "↓") + Len;
    }

    /// <summary>Answer of <see cref="BattleshipBoard.ReceiveShot"/> (battleship_logic.gd receive_shot).</summary>
    public sealed class ShotOutcome
    {
        public bool Hit { get; internal set; }
        public bool Sunk { get; internal set; }
        public bool AllSunk { get; internal set; }
        /// <summary>The cell was shot before: nothing is counted twice (the hit flag still tells what is there).</summary>
        public bool Repeat { get; internal set; }
        /// <summary>All cells of the ship when <see cref="Sunk"/>, else empty.</summary>
        public List<BattleshipCell> SunkCells { get; } = new List<BattleshipCell>();
    }

    /// <summary>The OWN board of a player: the fleet and every shot received (the defender answers SHOT_RESULT).</summary>
    public sealed class BattleshipBoard
    {
        private readonly List<BattleshipShip> _ships = new List<BattleshipShip>();
        private readonly HashSet<BattleshipCell> _shots = new HashSet<BattleshipCell>();
        private readonly Dictionary<BattleshipCell, int> _cellToShip = new Dictionary<BattleshipCell, int>();

        public IReadOnlyList<BattleshipShip> Ships => _ships;
        public IReadOnlyCollection<BattleshipCell> ShotsReceived => _shots;
        public bool IsReady => _ships.Count == BattleshipLogic.Fleet.Length;

        /// <summary>Sets the fleet (validated); false = invalid fleet, the board stays unchanged.</summary>
        public bool Setup(IList<BattleshipShip> fleet)
        {
            if (BattleshipLogic.ValidateFleet(fleet).Length > 0) return false;
            _ships.Clear();
            _shots.Clear();
            _cellToShip.Clear();
            foreach (var ship in fleet)
            {
                _ships.Add(ship);
                foreach (var c in ship.Cells()) _cellToShip[c] = _ships.Count - 1;
            }
            return true;
        }

        /// <summary>Ship index at <paramref name="c"/>, -1 = water.</summary>
        public int ShipAt(BattleshipCell c) => _cellToShip.TryGetValue(c, out var i) ? i : -1;

        public bool WasShot(BattleshipCell c) => _shots.Contains(c);

        public bool IsSunk(int shipIndex)
        {
            if (shipIndex < 0 || shipIndex >= _ships.Count) return false;
            foreach (var c in _ships[shipIndex].Cells()) if (!_shots.Contains(c)) return false;
            return true;
        }

        public ShotOutcome ReceiveShot(BattleshipCell c)
        {
            var o = new ShotOutcome();
            if (!c.InBounds) return o;
            var index = ShipAt(c);
            if (_shots.Contains(c))
            {
                o.Repeat = true;
                o.Hit = index >= 0;
                return o;
            }
            _shots.Add(c);
            if (index < 0) return o;
            o.Hit = true;
            if (IsSunk(index))
            {
                o.Sunk = true;
                o.SunkCells.AddRange(_ships[index].Cells());
                o.AllSunk = RemainingShips == 0;
            }
            return o;
        }

        /// <summary>Ships with at least one cell not hit yet.</summary>
        public int RemainingShips
        {
            get
            {
                var n = 0;
                for (var i = 0; i < _ships.Count; i++) if (!IsSunk(i)) n++;
                return n;
            }
        }
    }

    /// <summary>The shooter's view of the enemy board: own shots (hit or not) and the sunk ships.</summary>
    public sealed class BattleshipTracker
    {
        private readonly Dictionary<BattleshipCell, bool> _shots = new Dictionary<BattleshipCell, bool>();
        private readonly HashSet<BattleshipCell> _sunkCells = new HashSet<BattleshipCell>();

        public IReadOnlyDictionary<BattleshipCell, bool> Shots => _shots;
        public IReadOnlyCollection<BattleshipCell> SunkCells => _sunkCells;
        public int SunkCount { get; private set; }
        public int ShotCount => _shots.Count;

        public int HitCount
        {
            get
            {
                var n = 0;
                foreach (var kv in _shots) if (kv.Value) n++;
                return n;
            }
        }

        public bool IsNewTarget(BattleshipCell c) => c.InBounds && !_shots.ContainsKey(c);

        public bool IsSunkCell(BattleshipCell c) => _sunkCells.Contains(c);

        /// <summary>
        /// Records a SHOT_RESULT. <paramref name="sunkCells"/> (sent by Unity clients) marks the whole ship; a Godot
        /// peer only says "sunk", then the straight run of unsunk hits through the cell is inferred.
        /// Returns the cells marked as sunk by this result (empty unless <paramref name="sunk"/>).
        /// </summary>
        public List<BattleshipCell> Record(BattleshipCell c, bool hit, bool sunk, IList<BattleshipCell> sunkCells = null)
        {
            var marked = new List<BattleshipCell>();
            if (!c.InBounds) return marked;
            _shots[c] = hit;
            if (!sunk) return marked;
            SunkCount++;
            var cells = sunkCells != null && sunkCells.Count > 0 ? (IList<BattleshipCell>)sunkCells : InferSunkRun(c);
            foreach (var s in cells)
            {
                if (!s.InBounds) continue;
                _sunkCells.Add(s);
                if (!marked.Contains(s)) marked.Add(s);
            }
            if (!marked.Contains(c)) marked.Add(c);
            _sunkCells.Add(c);
            return marked;
        }

        /// <summary>Was this cell already shot (hit or miss)? <paramref name="hit"/> tells which.</summary>
        public bool TryGetShot(BattleshipCell c, out bool hit) => _shots.TryGetValue(c, out hit);

        /// <summary>Hits in one straight line through <paramref name="c"/> that are not part of an already sunk ship.</summary>
        public List<BattleshipCell> InferSunkRun(BattleshipCell c)
        {
            var horizontal = Run(c, 1, 0);
            var vertical = Run(c, 0, 1);
            return horizontal.Count >= vertical.Count ? horizontal : vertical;
        }

        private List<BattleshipCell> Run(BattleshipCell c, int dx, int dy)
        {
            var run = new List<BattleshipCell> { c };
            for (var dir = -1; dir <= 1; dir += 2)
            {
                var p = new BattleshipCell(c.X + dx * dir, c.Y + dy * dir);
                while (p.InBounds && _shots.TryGetValue(p, out var h) && h && !_sunkCells.Contains(p) && run.Count < 5)
                {
                    run.Add(p);
                    p = new BattleshipCell(p.X + dx * dir, p.Y + dy * dir);
                }
            }
            return run;
        }

        public bool HasWon => SunkCount >= BattleshipLogic.Fleet.Length;
    }

    /// <summary>
    /// Client mirror of the server's turn ownership (boardgames.js): SHOT only by the player whose turn it is, the
    /// SHOT_RESULT only by the one shot at, n exact; after the pair the turn switches. Round = completed pairs / 2.
    /// </summary>
    public sealed class BattleshipTurn
    {
        public const string PhaseShot = "shot";
        public const string PhaseResult = "result";

        private readonly List<string> _players = new List<string>();

        public int N { get; set; } = 1;
        public string Phase { get; set; } = PhaseShot;
        public string TurnCode { get; set; }
        public int Exchanges { get; set; }
        public IReadOnlyList<string> Players => _players;

        public BattleshipTurn(string first, IEnumerable<string> players)
        {
            TurnCode = first ?? "";
            if (players != null) foreach (var p in players) _players.Add(p ?? "");
        }

        public bool CanShoot(string code) => Phase == PhaseShot && TurnCode == code;

        public bool CanAnswer(string code) => Phase == PhaseResult && TurnCode != code;

        public void OnShot() => Phase = PhaseResult;

        public void OnResult()
        {
            Phase = PhaseShot;
            N++;
            Exchanges++;
            foreach (var p in _players)
            {
                if (p == TurnCode) continue;
                TurnCode = p;
                break;
            }
        }

        /// <summary>Round counting exactly like the server (tomato limit).</summary>
        public int RoundIndex => Exchanges / 2;
    }

    /// <summary>Client mirror of the server rule "one tomato per player per round" (the server stays the authority).</summary>
    public sealed class TomatoTracker
    {
        private int _lastRound = -1;

        public bool CanThrow(int round) => round != _lastRound;

        public void MarkThrown(int round) => _lastRound = round;

        public void Reset() => _lastRound = -1;
    }

    /// <summary>Emote wheel of the board game (emotes.gd): the four wheel emotes plus the relay-only selfie.</summary>
    public static class BoardEmotes
    {
        public static readonly string[] Ids = { "dance", "angry", "laugh", "sleep" };
        public static readonly string[] Extra = { "selfie" };

        public static bool IsValid(string id) => Array.IndexOf(Ids, id) >= 0 || Array.IndexOf(Extra, id) >= 0;

        /// <summary>String key of the emote label (social.json board.emote.*).</summary>
        public static string LabelKey(string id) => "board.emote." + id;
    }

    /// <summary>
    /// Schiffe versenken — pure rules (battleship_logic.gd): 10×10 board, fleet 5/4/3/3/2, deterministic random fleets
    /// from the mulberry32 <see cref="GoobyRng"/> (same seed ⇒ same fleet on Godot and Unity), cell notation "A1".."J10".
    /// The server never checks hits — both clients evaluate their own board.
    /// </summary>
    public static class BattleshipLogic
    {
        public const int Grid = 10;
        public static readonly int[] Fleet = { 5, 4, 3, 3, 2 };
        public const string Cols = "ABCDEFGHIJ";
        /// <summary>Step between reroll seeds (battleship_scene.gd _on_reroll_pressed).</summary>
        public const int RerollStep = 7919;

        public const string ReasonOk = "";
        public const string ReasonWrongCount = "wrong_count";
        public const string ReasonWrongLengths = "wrong_lengths";
        public const string ReasonOutOfBounds = "out_of_bounds";
        public const string ReasonOverlap = "overlap";

        public static bool InBounds(BattleshipCell c) => c.InBounds;

        /// <summary>"" when valid, else one of the Reason* codes (exactly 5/4/3/3/2, inside, no overlap).</summary>
        public static string ValidateFleet(IList<BattleshipShip> fleet)
        {
            if (fleet == null || fleet.Count != Fleet.Length) return ReasonWrongCount;
            var lengths = new List<int>();
            foreach (var s in fleet)
            {
                if (s == null) return ReasonWrongLengths;
                lengths.Add(s.Len);
            }
            lengths.Sort();
            var wanted = new List<int>(Fleet);
            wanted.Sort();
            for (var i = 0; i < wanted.Count; i++) if (lengths[i] != wanted[i]) return ReasonWrongLengths;
            var taken = new HashSet<BattleshipCell>();
            foreach (var s in fleet)
            {
                foreach (var c in s.Cells())
                {
                    if (!c.InBounds) return ReasonOutOfBounds;
                    if (!taken.Add(c)) return ReasonOverlap;
                }
            }
            return ReasonOk;
        }

        /// <summary>Deterministic random fleet (battleship_logic.gd auto_fleet — identical draw order).</summary>
        public static List<BattleshipShip> AutoFleet(long seed)
        {
            var rng = new GoobyRng(seed);
            var fleet = new List<BattleshipShip>();
            var taken = new HashSet<BattleshipCell>();
            foreach (var len in Fleet)
            {
                while (true)
                {
                    var horizontal = rng.Next() < 0.5;
                    var maxX = Grid - (horizontal ? len : 1);
                    var maxY = Grid - (horizontal ? 1 : len);
                    var x = (int)(rng.Next() * (maxX + 1));
                    var y = (int)(rng.Next() * (maxY + 1));
                    var ship = new BattleshipShip(x, y, len, horizontal);
                    var free = true;
                    foreach (var c in ship.Cells())
                    {
                        if (!taken.Contains(c)) continue;
                        free = false;
                        break;
                    }
                    if (!free) continue;
                    foreach (var c in ship.Cells()) taken.Add(c);
                    fleet.Add(ship);
                    break;
                }
            }
            return fleet;
        }

        /// <summary>Godot's String.hash() (djb2 over the code points, uint32) — needed for the shared fleet seed.</summary>
        public static uint GodotStringHash(string text)
        {
            unchecked
            {
                uint h = 5381;
                if (string.IsNullOrEmpty(text)) return h;
                for (var i = 0; i < text.Length; i++)
                {
                    int cp;
                    if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    {
                        cp = char.ConvertToUtf32(text[i], text[i + 1]);
                        i++;
                    }
                    else cp = text[i];
                    h = (h << 5) + h + (uint)cp;
                }
                return h;
            }
        }

        /// <summary>
        /// Per-player fleet seed (board_session.gd fleet_seed): (server seed + hash(friend code)) &amp; 0x7FFFFFFF —
        /// both devices can rebuild either fleet (tests, resume after a reconnect).
        /// </summary>
        public static long FleetSeed(long serverSeed, string friendCode) => (serverSeed + GodotStringHash(friendCode ?? "")) & 0x7FFFFFFFL;

        /// <summary>Seed of the n-th "Neu würfeln" (0 = the default fleet).</summary>
        public static long RerollSeed(long serverSeed, string friendCode, int rerolls) =>
            FleetSeed(serverSeed + (long)rerolls * RerollStep, friendCode);

        public static List<BattleshipShip> DefaultFleet(long serverSeed, string friendCode) => AutoFleet(FleetSeed(serverSeed, friendCode));

        public static string CellToRef(BattleshipCell c) => c.InBounds ? Cols[c.X] + (c.Y + 1).ToString() : "";

        /// <summary>"B4" → (1,3); <see cref="BattleshipCell.Invalid"/> for anything else ("Z9", "A0", "A11", "").</summary>
        public static BattleshipCell RefToCell(string text)
        {
            var t = (text ?? "").Trim().ToUpperInvariant();
            if (t.Length < 2 || t.Length > 3) return BattleshipCell.Invalid;
            var x = Cols.IndexOf(t[0]);
            if (x < 0) return BattleshipCell.Invalid;
            var digits = t.Substring(1);
            foreach (var ch in digits) if (ch < '0' || ch > '9') return BattleshipCell.Invalid;
            var c = new BattleshipCell(x, int.Parse(digits) - 1);
            return c.InBounds ? c : BattleshipCell.Invalid;
        }

        public static JArray FleetToJson(IEnumerable<BattleshipShip> fleet)
        {
            var arr = new JArray();
            if (fleet != null) foreach (var s in fleet) arr.Add(s.ToJson());
            return arr;
        }

        /// <summary>Parses a fleet array; null when any entry is malformed.</summary>
        public static List<BattleshipShip> FleetFromJson(JToken token)
        {
            if (!(token is JArray arr)) return null;
            var list = new List<BattleshipShip>();
            foreach (var t in arr)
            {
                var s = BattleshipShip.FromJson(t);
                if (s == null) return null;
                list.Add(s);
            }
            return list;
        }

        /// <summary>[[x,y],…] as sent in SHOT_RESULT.sunkCells (Unity addition, ignored by Godot).</summary>
        public static JArray CellsToJson(IEnumerable<BattleshipCell> cells)
        {
            var arr = new JArray();
            if (cells != null) foreach (var c in cells) arr.Add(new JArray(c.X, c.Y));
            return arr;
        }

        public static List<BattleshipCell> CellsFromJson(JToken token)
        {
            var list = new List<BattleshipCell>();
            if (!(token is JArray arr)) return list;
            foreach (var t in arr)
            {
                if (t is JArray p && p.Count >= 2 && p[0].Type == JTokenType.Integer && p[1].Type == JTokenType.Integer)
                {
                    var c = new BattleshipCell((int)(long)p[0], (int)(long)p[1]);
                    if (c.InBounds) list.Add(c);
                }
            }
            return list;
        }

        /// <summary>
        /// The fleet with the ship at <paramref name="c"/> turned by 90° (bow stays), or null when there is no ship
        /// there or the turned ship would leave the board / overlap (setup: tap a ship to rotate it).
        /// </summary>
        public static List<BattleshipShip> RotateAt(IList<BattleshipShip> fleet, BattleshipCell c)
        {
            if (fleet == null) return null;
            for (var i = 0; i < fleet.Count; i++)
            {
                if (!fleet[i].Contains(c)) continue;
                var candidate = new List<BattleshipShip>(fleet) { [i] = fleet[i].Rotated() };
                return ValidateFleet(candidate).Length == 0 ? candidate : null;
            }
            return null;
        }
    }
}
