using System.Collections.Generic;
using System.Globalization;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Gvz
{
    /// <summary>
    /// A Godot Dictionary's iteration contract: insertion order, an erased key re-inserted goes to the end. The GvZ
    /// rules and the bot walk the towers / mowers in that order (and the boss tosses a bin by key index), so a plain
    /// C# Dictionary would drift from the reference after the first removal.
    /// </summary>
    public sealed class OrderedMap<TKey, TValue>
    {
        private readonly List<TKey> _order = new List<TKey>();
        private readonly Dictionary<TKey, TValue> _map = new Dictionary<TKey, TValue>();

        public int Count => _order.Count;
        public IReadOnlyList<TKey> Keys => _order;
        public bool ContainsKey(TKey key) => _map.ContainsKey(key);
        public bool TryGetValue(TKey key, out TValue value) => _map.TryGetValue(key, out value);
        public TValue Get(TKey key) => _map.TryGetValue(key, out var v) ? v : default;

        public TValue this[TKey key]
        {
            get => _map[key];
            set
            {
                if (!_map.ContainsKey(key)) _order.Add(key);
                _map[key] = value;
            }
        }

        public bool Remove(TKey key)
        {
            if (!_map.Remove(key)) return false;
            _order.Remove(key);
            return true;
        }

        /// <summary>A copy of the keys (Godot's dict.keys()) for loops that may erase.</summary>
        public TKey[] KeysSnapshot() => _order.ToArray();

        public IEnumerable<TValue> Values
        {
            get { foreach (var k in _order) yield return _map[k]; }
        }
    }

    public sealed class GvzTower
    {
        public int Id, Lane, Col, Hp, MaxHp, NextAct;
        public string Type;
        public int? Charges, ArmedAt, FuseAt;
    }

    public sealed class GvzZombie
    {
        public int Id, Lane, X, Hp, MaxHp, ArmorHp, SpeedPct, Dir = -1, Jumps, SlowUntil;
        public string Type, Armor = "", State = "walk";
        public bool Flying, Crusher, Raged, ThrewMini, Dead;
    }

    public sealed class GvzProjectile
    {
        public int Id, Lane, X, Dmg, Splash, SlowTicks;
        public string Kind;
    }

    public sealed class GvzDrop
    {
        public int Id, Lane, Col, Amount, Expires;
    }

    public sealed class GvzMower
    {
        public bool Used, Active;
        public int X;
    }

    public sealed class GvzConveyor
    {
        public readonly List<string> Queue = new List<string>();
        public List<string> Pool = new List<string>();
        public int Interval, MaxQueue, NextTick;
    }

    public sealed class GvzBoss
    {
        public string Type;
        public int Hp, MaxHp, Phase, Lane, X, NextMove, NextToss, NextSummon;
    }

    public sealed class GvzSpawn
    {
        public int Tick, Lane;
        public string Type;
    }

    public sealed class GvzWave
    {
        public int Tick;
        public bool Huge;
    }

    /// <summary>A tick event (kind: spawn/die/hit/blast/boom/wave/mower/collect/drop/place/tower_gone/…).</summary>
    public sealed class GvzEvent
    {
        public string Kind, Type = "", Cause = "", Source = "", HitKind = "", Proj = "", Which = "", By = "";
        public int Id = -1, Lane = -1, Col = -1, Amount, N, Radius, Phase, Hp, MaxHp, Count, X;
        public bool Huge, Armor, Boss;

        public override string ToString() => string.Join("|", Kind, Type, Cause, Source, HitKind, Proj, Which, By,
            Id.ToString(CultureInfo.InvariantCulture), Lane.ToString(CultureInfo.InvariantCulture), Col.ToString(CultureInfo.InvariantCulture),
            Amount.ToString(CultureInfo.InvariantCulture), N.ToString(CultureInfo.InvariantCulture), Radius.ToString(CultureInfo.InvariantCulture),
            Phase.ToString(CultureInfo.InvariantCulture), Hp.ToString(CultureInfo.InvariantCulture), MaxHp.ToString(CultureInfo.InvariantCulture),
            Count.ToString(CultureInfo.InvariantCulture), X.ToString(CultureInfo.InvariantCulture), Huge ? "1" : "0", Armor ? "1" : "0", Boss ? "1" : "0");
    }

    public sealed class GvzRun
    {
        public int Tick;
        public GoobyRng Rng;
        public long Seed;
        public JObject Level;
        public JObject Balance;
        public string Diff = "normal";
        public List<int> Lanes = new List<int>();
        public JObject Mods = new JObject();
        public int Nutella;
        public readonly OrderedMap<int, GvzTower> Towers = new OrderedMap<int, GvzTower>();
        public readonly List<GvzZombie> Zombies = new List<GvzZombie>();
        public readonly List<GvzProjectile> Projectiles = new List<GvzProjectile>();
        public readonly List<GvzDrop> Drops = new List<GvzDrop>();
        public readonly OrderedMap<int, GvzMower> Mowers = new OrderedMap<int, GvzMower>();
        public readonly Dictionary<string, int> Cooldowns = new Dictionary<string, int>();
        public GvzConveyor Conveyor;
        public GvzBoss Boss;
        public List<GvzSpawn> SpawnPlan = new List<GvzSpawn>();
        public List<GvzWave> WavePlan = new List<GvzWave>();
        public int SpawnIdx, WaveIdx, NextDropTick, Kills, Score, NextIdValue = 1;
        public string Outcome = "";
        public bool Goldi;
        public List<GvzEvent> Events = new List<GvzEvent>();
        /// <summary>Sticker counters of the run (drops_collected, eis_placed, bert_placed, moehren_shots) — not hashed.</summary>
        public readonly Dictionary<string, int> Stats = new Dictionary<string, int>
        {
            ["drops_collected"] = 0, ["eis_placed"] = 0, ["bert_placed"] = 0, ["moehren_shots"] = 0,
        };

        public JObject TowerRow(string type) => Balance?["towers"]?[type] as JObject ?? new JObject();
        public JObject ZombieRow(string type) => Balance?["zombies"]?[type] as JObject ?? new JObject();
        public JObject DiffRow => Balance?["difficulty"]?[Diff] as JObject ?? new JObject();
        public JObject Economy => Balance?["economy"] as JObject ?? new JObject();
        public JObject Combat => Balance?["combat"] as JObject ?? new JObject();
        public bool Mod(string key) => GvzJson.B(Mods, key, false);
    }

    /// <summary>GDScript-style reads off the JSON rows: int(row.get(key, default)) and friends.</summary>
    public static class GvzJson
    {
        public static bool Has(JObject o, string key) => o?[key] != null && o[key].Type != JTokenType.Null;

        public static int I(JObject o, string key, int fallback)
        {
            var t = o?[key];
            if (t == null || t.Type == JTokenType.Null) return fallback;
            switch (t.Type)
            {
                case JTokenType.Integer: return (int)t.Value<long>();
                case JTokenType.Float: return (int)t.Value<double>();
                case JTokenType.Boolean: return t.Value<bool>() ? 1 : 0;
                case JTokenType.String: return int.TryParse(t.Value<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
                default: return 0;
            }
        }

        public static double D(JObject o, string key, double fallback)
        {
            var t = o?[key];
            if (t == null || t.Type == JTokenType.Null) return fallback;
            return t.Type == JTokenType.Integer || t.Type == JTokenType.Float ? t.Value<double>() : t.Type == JTokenType.Boolean ? (t.Value<bool>() ? 1.0 : 0.0) : 0.0;
        }

        public static string S(JObject o, string key, string fallback)
        {
            var t = o?[key];
            if (t == null || t.Type == JTokenType.Null) return fallback;
            return t.Type == JTokenType.String ? t.Value<string>() : t.ToString();
        }

        public static bool B(JObject o, string key, bool fallback)
        {
            var t = o?[key];
            if (t == null || t.Type == JTokenType.Null) return fallback;
            if (t.Type == JTokenType.Boolean) return t.Value<bool>();
            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) return t.Value<double>() != 0.0;
            return t.Type == JTokenType.String && t.Value<string>().Length > 0;
        }

        public static List<string> Strings(JToken t)
        {
            var list = new List<string>();
            if (t is JArray arr) foreach (var x in arr) list.Add(x.Type == JTokenType.String ? x.Value<string>() : x.ToString());
            return list;
        }

        public static List<int> Ints(JToken t)
        {
            var list = new List<int>();
            if (t is JArray arr) foreach (var x in arr) list.Add(x.Type == JTokenType.Integer ? (int)x.Value<long>() : (int)x.Value<double>());
            return list;
        }
    }
}
