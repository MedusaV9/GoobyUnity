using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Net
{
    /// <summary>One line of an arcade friends leaderboard (arcade.js: {code, name, gooby, score, stars, at}).</summary>
    public sealed class ArcadeEntry
    {
        public string Code { get; internal set; } = "";
        public string Name { get; internal set; } = "";
        public string Gooby { get; internal set; } = "";
        public int Score { get; internal set; }
        /// <summary>0..3 — the stars of that best round.</summary>
        public int Stars { get; internal set; }
        /// <summary>Unix ms (server clock) the best was set.</summary>
        public long At { get; internal set; }

        public static ArcadeEntry FromJson(JObject o)
        {
            if (o == null) return null;
            var code = NetJson.Str(o["code"]);
            if (code.Length == 0) return null;
            return new ArcadeEntry
            {
                Code = code,
                Name = NetJson.Str(o["name"], "?"),
                Gooby = NetJson.Str(o["gooby"], "Gooby"),
                Score = Math.Max(0, NetJson.Int(o["score"])),
                Stars = Math.Max(0, Math.Min(ArcadeScores.StarsMax, NetJson.Int(o["stars"]))),
                At = NetJson.Long(o["at"]),
            };
        }

        public override string ToString() => Code + " " + Name + " " + Score + "★" + Stars;
    }

    /// <summary>GET /api/arcade/leaderboard/:game — me + friends, best first, plus my 1-based rank (0 = no score yet).</summary>
    public sealed class ArcadeBoard
    {
        private readonly List<ArcadeEntry> _entries = new List<ArcadeEntry>();

        public bool Ok { get; internal set; }
        /// <summary>"" on success, else BAD_GAME, AUTH_FAIL, OFFLINE, NO_CONNECTION, HTTP_…</summary>
        public string Code { get; internal set; } = "";
        public string Game { get; internal set; } = "";
        public string Me { get; internal set; } = "";
        public int Rank { get; internal set; }
        public IReadOnlyList<ArcadeEntry> Entries => _entries;

        /// <summary>At least one friend (not me) is on the board — the result card only shows its block then.</summary>
        public bool HasFriends
        {
            get
            {
                foreach (var e in _entries) if (e.Code != Me) return true;
                return false;
            }
        }

        public ArcadeEntry Mine
        {
            get
            {
                foreach (var e in _entries) if (e.Code == Me) return e;
                return null;
            }
        }

        /// <summary>The first <paramref name="n"/> lines (me included), already sorted by the server.</summary>
        public List<ArcadeEntry> Top(int n)
        {
            var list = new List<ArcadeEntry>();
            for (var i = 0; i < _entries.Count && i < n; i++) list.Add(_entries[i]);
            return list;
        }

        public static ArcadeBoard Fail(string code, string game = "") => new ArcadeBoard { Code = code ?? "", Game = game ?? "" };

        /// <summary>Parses a leaderboard reply; entries are re-sorted defensively (score desc, earlier first, code).</summary>
        public static ArcadeBoard FromJson(JObject o)
        {
            if (o == null || !NetJson.Bool(o["ok"])) return Fail(NetJson.Str(o?["code"], "BAD_REPLY"));
            var b = new ArcadeBoard
            {
                Ok = true,
                Game = NetJson.Str(o["game"]),
                Me = NetJson.Str(o["me"]),
            };
            if (o["entries"] is JArray arr)
            {
                var seen = new HashSet<string>();
                foreach (var t in arr)
                {
                    var e = ArcadeEntry.FromJson(t as JObject);
                    if (e != null && seen.Add(e.Code)) b._entries.Add(e);
                }
            }
            b._entries.Sort(Compare);
            var rank = 0;
            for (var i = 0; i < b._entries.Count; i++) if (b._entries[i].Code == b.Me) rank = i + 1;
            b.Rank = rank;
            return b;
        }

        private static int Compare(ArcadeEntry a, ArcadeEntry b)
        {
            var c = b.Score.CompareTo(a.Score);
            if (c != 0) return c;
            c = a.At.CompareTo(b.At);
            return c != 0 ? c : string.CompareOrdinal(a.Code, b.Code);
        }

        public override string ToString() => Ok ? Game + " #" + Rank + " [" + string.Join(", ", _entries) + "]" : "FAIL(" + Code + ")";
    }

    /// <summary>Per game for the arcade cards: the best friend (not me) and my own best (-1 = none).</summary>
    public sealed class ArcadeGameBest
    {
        public string Game { get; internal set; } = "";
        public string TopCode { get; internal set; } = "";
        public string TopName { get; internal set; } = "";
        public int TopScore { get; internal set; }
        public int Mine { get; internal set; } = -1;

        /// <summary>A friend has a score for this game.</summary>
        public bool HasTop => TopCode.Length > 0;

        /// <summary>A friend holds the top score of the circle (strictly above mine, or I have none).</summary>
        public bool FriendLeads => HasTop && TopScore > Mine;

        public static ArcadeGameBest FromJson(string game, JObject o)
        {
            if (string.IsNullOrEmpty(game) || o == null) return null;
            var g = new ArcadeGameBest { Game = game };
            if (o["top"] is JObject top && NetJson.Str(top["code"]).Length > 0)
            {
                g.TopCode = NetJson.Str(top["code"]);
                g.TopName = NetJson.Str(top["name"], "?");
                g.TopScore = Math.Max(0, NetJson.Int(top["score"]));
            }
            var mine = o["mine"];
            g.Mine = mine == null || mine.Type == JTokenType.Null ? -1 : Math.Max(0, NetJson.Int(mine));
            return g;
        }

        /// <summary>The same summary derived from a full leaderboard (keeps the card line fresh after a result card).</summary>
        public static ArcadeGameBest FromBoard(ArcadeBoard b)
        {
            if (b == null || !b.Ok || string.IsNullOrEmpty(b.Game)) return null;
            var g = new ArcadeGameBest { Game = b.Game };
            foreach (var e in b.Entries)
            {
                if (e.Code == b.Me)
                {
                    g.Mine = e.Score;
                    continue;
                }
                if (g.HasTop) continue;   // entries are sorted: the first friend is the top friend
                g.TopCode = e.Code;
                g.TopName = e.Name;
                g.TopScore = e.Score;
            }
            return g;
        }

        public override string ToString() => Game + " top=" + (HasTop ? TopName + ":" + TopScore : "-") + " mine=" + Mine;
    }

    /// <summary>GET /api/arcade/best — only games with a friend's or my own score.</summary>
    public sealed class ArcadeBestSet
    {
        private readonly Dictionary<string, ArcadeGameBest> _games = new Dictionary<string, ArcadeGameBest>();

        public bool Ok { get; internal set; }
        public string Code { get; internal set; } = "";
        public string Me { get; internal set; } = "";
        public IReadOnlyDictionary<string, ArcadeGameBest> Games => _games;

        public ArcadeGameBest Find(string game) => !string.IsNullOrEmpty(game) && _games.TryGetValue(game, out var g) ? g : null;

        /// <summary>Games with a friend entry (QA <c>arcadeTop</c>).</summary>
        public int FriendTops
        {
            get
            {
                var n = 0;
                foreach (var g in _games.Values) if (g.HasTop) n++;
                return n;
            }
        }

        internal void Put(ArcadeGameBest g)
        {
            if (g == null || string.IsNullOrEmpty(g.Game)) return;
            if (!g.HasTop && g.Mine < 0) _games.Remove(g.Game);
            else _games[g.Game] = g;
        }

        internal void Clear() => _games.Clear();

        public static ArcadeBestSet Fail(string code) => new ArcadeBestSet { Code = code ?? "" };

        public static ArcadeBestSet FromJson(JObject o)
        {
            if (o == null || !NetJson.Bool(o["ok"])) return Fail(NetJson.Str(o?["code"], "BAD_REPLY"));
            var set = new ArcadeBestSet { Ok = true, Me = NetJson.Str(o["me"]) };
            if (o["games"] is JObject games)
            {
                foreach (var p in games.Properties())
                {
                    if (!ArcadeScores.GameIdOk(p.Name)) continue;
                    set.Put(ArcadeGameBest.FromJson(p.Name, p.Value as JObject));
                }
            }
            return set;
        }
    }

    /// <summary>Outcome of <see cref="SocialClient.SubmitArcadeScore"/> (POST /api/arcade/score → {ok, game, best, stars, improved}).</summary>
    public sealed class ArcadeSubmitResult
    {
        public bool Ok { get; internal set; }
        /// <summary>"" on success, else OFFLINE (nothing sent), BAD_GAME, BAD_SCORE, RATE_LIMIT, NO_CONNECTION, HTTP_…</summary>
        public string Code { get; internal set; } = "";
        public string Game { get; internal set; } = "";
        /// <summary>The stored best after this round (never lower than before).</summary>
        public int Best { get; internal set; }
        public int Stars { get; internal set; }
        /// <summary>This round raised the stored best (or its stars at the same score).</summary>
        public bool Improved { get; internal set; }

        public static ArcadeSubmitResult Fail(string code, string game = "") => new ArcadeSubmitResult { Code = code ?? "", Game = game ?? "" };

        public static ArcadeSubmitResult FromJson(JObject o)
        {
            if (o == null || !NetJson.Bool(o["ok"])) return Fail(NetJson.Str(o?["code"], "BAD_REPLY"));
            return new ArcadeSubmitResult
            {
                Ok = true,
                Game = NetJson.Str(o["game"]),
                Best = Math.Max(0, NetJson.Int(o["best"])),
                Stars = Math.Max(0, Math.Min(ArcadeScores.StarsMax, NetJson.Int(o["stars"]))),
                Improved = NetJson.Bool(o["improved"]),
            };
        }

        public override string ToString() => Ok ? "OK(" + Game + " best=" + Best + (Improved ? " improved" : "") + ")" : "FAIL(" + Code + ")";
    }

    /// <summary>Limits shared with GOOBY-SERVER/src/arcade.js.</summary>
    public static class ArcadeScores
    {
        public const int ScoreMax = 10_000_000;
        public const int StarsMax = 3;
        private static readonly Regex GameRe = new Regex("^[a-zA-Z0-9_]{2,32}$");

        public static bool GameIdOk(string game) => !string.IsNullOrEmpty(game) && GameRe.IsMatch(game);

        public static int ClampScore(int score) => Math.Max(0, Math.Min(ScoreMax, score));

        public static int ClampStars(int stars) => Math.Max(0, Math.Min(StarsMax, stars));
    }

    /// <summary>
    /// Arcade friends leaderboard on top of REST (arcade.js): after every finished round the score goes to
    /// POST /api/arcade/score (best per player and game is kept server-side), the results card pulls
    /// GET /api/arcade/leaderboard/:game (me + friends), the arcade cards GET /api/arcade/best (top friend + mine per game).
    /// Offline nothing is queued — calls answer OFFLINE right away without a request. Replies come back through
    /// <see cref="INetHttp.Pump"/>, i.e. inside <see cref="Pump"/> on the main thread.
    /// </summary>
    public sealed partial class SocialClient
    {
        private ArcadeBestSet _arcadeBest = new ArcadeBestSet();

        /// <summary>Last /api/arcade/best (patched by later leaderboards and submits); empty until loaded.</summary>
        public ArcadeBestSet ArcadeBest => _arcadeBest;

        /// <summary>True once a /api/arcade/best answered on this server.</summary>
        public bool ArcadeBestLoaded { get; private set; }

        private bool ArcadeReachable => Session.IsOnline && Endpoint.Valid;

        /// <summary>Books a finished round (score 0..10 000 000, stars 0..3, clamped). Offline: cb(OFFLINE), nothing sent.</summary>
        public void SubmitArcadeScore(string game, int score, int stars, Action<ArcadeSubmitResult> cb)
        {
            if (!ArcadeScores.GameIdOk(game))
            {
                Callback(cb, ArcadeSubmitResult.Fail("BAD_GAME", game));
                return;
            }
            if (!ArcadeReachable)
            {
                Callback(cb, ArcadeSubmitResult.Fail(NetErrorCodes.Offline, game));
                return;
            }
            var body = new JObject
            {
                ["game"] = game,
                ["score"] = ArcadeScores.ClampScore(score),
                ["stars"] = ArcadeScores.ClampStars(stars),
            };
            _http.Send("POST", Endpoint.HttpBase + "/api/arcade/score", Identity.Bearer, body.ToString(Formatting.None), (status, text) =>
            {
                var o = NetJson.ParseObject(text);
                var r = status == 200 ? ArcadeSubmitResult.FromJson(o) : ArcadeSubmitResult.Fail(HttpCode(status, o), game);
                if (r.Ok)
                {
                    if (r.Game.Length == 0) r.Game = game;
                    var known = _arcadeBest.Find(game);
                    if (known == null || known.Mine < r.Best)
                    {
                        _arcadeBest.Put(new ArcadeGameBest
                        {
                            Game = game,
                            TopCode = known?.TopCode ?? "",
                            TopName = known?.TopName ?? "",
                            TopScore = known?.TopScore ?? 0,
                            Mine = r.Best,
                        });
                        MarkDirty();
                    }
                }
                Callback(cb, r);
            });
        }

        /// <summary>Me + friends for one game, best first. Offline: cb(OFFLINE), nothing sent.</summary>
        public void FetchArcadeLeaderboard(string game, Action<ArcadeBoard> cb)
        {
            if (!ArcadeScores.GameIdOk(game))
            {
                Callback(cb, ArcadeBoard.Fail("BAD_GAME", game));
                return;
            }
            if (!ArcadeReachable)
            {
                Callback(cb, ArcadeBoard.Fail(NetErrorCodes.Offline, game));
                return;
            }
            _http.Send("GET", Endpoint.HttpBase + "/api/arcade/leaderboard/" + Uri.EscapeDataString(game), Identity.Bearer, null, (status, text) =>
            {
                var o = NetJson.ParseObject(text);
                var b = status == 200 ? ArcadeBoard.FromJson(o) : ArcadeBoard.Fail(HttpCode(status, o), game);
                if (b.Ok)
                {
                    if (b.Game.Length == 0) b.Game = game;
                    _arcadeBest.Put(ArcadeGameBest.FromBoard(b));
                    MarkDirty();
                }
                Callback(cb, b);
            });
        }

        /// <summary>Top friend + my best per game (for the arcade cards); replaces <see cref="ArcadeBest"/> on success.</summary>
        public void FetchArcadeBest(Action<ArcadeBestSet> cb)
        {
            if (!ArcadeReachable)
            {
                Callback(cb, ArcadeBestSet.Fail(NetErrorCodes.Offline));
                return;
            }
            _http.Send("GET", Endpoint.HttpBase + "/api/arcade/best", Identity.Bearer, null, (status, text) =>
            {
                var o = NetJson.ParseObject(text);
                var set = status == 200 ? ArcadeBestSet.FromJson(o) : ArcadeBestSet.Fail(HttpCode(status, o));
                if (set.Ok)
                {
                    _arcadeBest = set;
                    ArcadeBestLoaded = true;
                    MarkDirty();
                }
                Callback(cb, set);
            });
        }

        private void ClearArcadeState()
        {
            _arcadeBest = new ArcadeBestSet();
            ArcadeBestLoaded = false;
        }
    }
}
