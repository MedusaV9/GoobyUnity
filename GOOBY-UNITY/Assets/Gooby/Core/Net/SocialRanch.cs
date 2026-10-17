using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Ranch.Mp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Net
{
    /// <summary>One line of a ranch friends leaderboard (ranchmp.js: {friendCode, name, goobyName, wert, at, hatGhost}).</summary>
    public sealed class RanchBoardEntry
    {
        public string Code { get; internal set; } = "";
        public string Name { get; internal set; } = "";
        public string Gooby { get; internal set; } = "";
        /// <summary>Milliseconds on time boards, points on points boards.</summary>
        public int Wert { get; internal set; }
        public long At { get; internal set; }
        /// <summary>The server keeps this player's best run as a ghost (RW-5 disciplines only).</summary>
        public bool HatGhost { get; internal set; }

        public static RanchBoardEntry FromJson(JObject o)
        {
            if (o == null) return null;
            var code = NetJson.Str(o["friendCode"]);
            if (code.Length == 0) return null;
            return new RanchBoardEntry
            {
                Code = code,
                Name = NetJson.Str(o["name"], "?"),
                Gooby = NetJson.Str(o["goobyName"], "Gooby"),
                Wert = Math.Max(0, NetJson.Int(o["wert"])),
                At = NetJson.Long(o["at"]),
                HatGhost = NetJson.Bool(o["hatGhost"]),
            };
        }

        public override string ToString() => Code + ":" + Wert + (HatGhost ? "+g" : "");
    }

    /// <summary>GET /api/rmp/leaderboard/:kurs — me + friends, best first in the board's direction.</summary>
    public sealed class RanchBoard
    {
        private readonly List<RanchBoardEntry> _entries = new List<RanchBoardEntry>();

        public bool Ok { get; internal set; }
        /// <summary>"" on success, else BAD_KURS, AUTH_FAIL, OFFLINE, NO_CONNECTION, HTTP_…</summary>
        public string Code { get; internal set; } = "";
        public string Kurs { get; internal set; } = "";
        /// <summary>"ab" = smaller wins (times), "auf" = larger wins (points).</summary>
        public string Richtung { get; internal set; } = RanchBoards.Ab;
        public string Me { get; internal set; } = "";
        public int Rank { get; internal set; }
        public IReadOnlyList<RanchBoardEntry> Entries => _entries;

        public bool HasFriends
        {
            get
            {
                foreach (var e in _entries) if (e.Code != Me) return true;
                return false;
            }
        }

        public RanchBoardEntry Mine
        {
            get
            {
                foreach (var e in _entries) if (e.Code == Me) return e;
                return null;
            }
        }

        public static RanchBoard Fail(string code, string kurs = "") => new RanchBoard { Code = code ?? "", Kurs = kurs ?? "" };

        /// <summary>Parses a leaderboard reply; entries are re-sorted defensively (direction, earlier first, code).</summary>
        public static RanchBoard FromJson(JObject o)
        {
            if (o == null || !NetJson.Bool(o["ok"])) return Fail(NetJson.Str(o?["code"], "BAD_REPLY"));
            var b = new RanchBoard
            {
                Ok = true,
                Kurs = NetJson.Str(o["kurs"]),
                Richtung = NetJson.Str(o["richtung"]) == RanchBoards.Auf ? RanchBoards.Auf : RanchBoards.Ab,
                Me = NetJson.Str(o["me"]),
            };
            if (o["entries"] is JArray arr)
            {
                var seen = new HashSet<string>();
                foreach (var t in arr)
                {
                    var e = RanchBoardEntry.FromJson(t as JObject);
                    if (e != null && seen.Add(e.Code)) b._entries.Add(e);
                }
            }
            var auf = b.Richtung == RanchBoards.Auf;
            b._entries.Sort((x, y) =>
            {
                var c = auf ? y.Wert.CompareTo(x.Wert) : x.Wert.CompareTo(y.Wert);
                if (c != 0) return c;
                c = x.At.CompareTo(y.At);
                return c != 0 ? c : string.CompareOrdinal(x.Code, y.Code);
            });
            for (var i = 0; i < b._entries.Count; i++) if (b._entries[i].Code == b.Me) b.Rank = i + 1;
            return b;
        }

        public override string ToString() => Ok ? Kurs + " #" + Rank + " [" + string.Join(", ", _entries) + "]" : "FAIL(" + Code + ")";
    }

    /// <summary>POST /api/rmp/score → {ok, ranked, verbessert, best} or PUT /api/rmp/ghost → {ok, gespeichert, best}.</summary>
    public sealed class RanchScoreResult
    {
        public bool Ok { get; internal set; }
        public string Code { get; internal set; } = "";
        public string Kurs { get; internal set; } = "";
        public bool Ranked { get; internal set; }
        /// <summary>The score raised the stored best, or the ghost replaced the stored one.</summary>
        public bool Improved { get; internal set; }
        public int Best { get; internal set; }

        public static RanchScoreResult Fail(string code, string kurs = "") => new RanchScoreResult { Code = code ?? "", Kurs = kurs ?? "" };

        public static RanchScoreResult FromJson(JObject o, string kurs)
        {
            if (o == null || !NetJson.Bool(o["ok"])) return Fail(NetJson.Str(o?["code"], "BAD_REPLY"), kurs);
            return new RanchScoreResult
            {
                Ok = true,
                Kurs = kurs ?? "",
                Ranked = o["ranked"] == null || NetJson.Bool(o["ranked"]),
                Improved = NetJson.Bool(o["verbessert"]) || NetJson.Bool(o["gespeichert"]),
                Best = Math.Max(0, NetJson.Int(o["best"])),
            };
        }

        public override string ToString() => Ok ? "OK(" + Kurs + " best=" + Best + (Improved ? " improved" : "") + ")" : "FAIL(" + Code + ")";
    }

    /// <summary>GET /api/rmp/ghost/:kurs/:friendCode — RW-5 ghosts carry the G5 binary as Base64 (RanchCompGhost).</summary>
    public sealed class RanchGhostReply
    {
        public bool Ok { get; internal set; }
        public string Code { get; internal set; } = "";
        public string Kurs { get; internal set; } = "";
        public string FriendCode { get; internal set; } = "";
        public int Wert { get; internal set; }
        public string B64 { get; internal set; } = "";

        public static RanchGhostReply Fail(string code, string kurs = "", string friendCode = "") =>
            new RanchGhostReply { Code = code ?? "", Kurs = kurs ?? "", FriendCode = friendCode ?? "" };

        public static RanchGhostReply FromJson(JObject o, string kurs, string friendCode)
        {
            if (o == null || !NetJson.Bool(o["ok"])) return Fail(NetJson.Str(o?["code"], "BAD_REPLY"), kurs, friendCode);
            var b64 = NetJson.Str(o["b64"]);
            if (b64.Length == 0) return Fail("NO_GHOST", kurs, friendCode);
            return new RanchGhostReply
            {
                Ok = true,
                Kurs = NetJson.Str(o["kurs"], kurs ?? ""),
                FriendCode = friendCode ?? "",
                Wert = Math.Max(0, NetJson.Int(o["wert"])),
                B64 = b64,
            };
        }
    }

    /// <summary>
    /// The leaderboards of ranchmp.js: the two live courses of the Ranch-Treffen (server times) and the eight RW-5
    /// disciplines (rw5_*, times in ms or points). Value windows, directions and ghost rules mirror the server.
    /// </summary>
    public static class RanchBoards
    {
        public const string Ab = "ab", Auf = "auf", Rw5Prefix = "rw5_";
        public const int MaxZeitMs = 30 * 60_000, MinRw5ZeitMs = 3_000, MaxPunkte = 100_000;
        /// <summary>Server limit of a stored ghost (cfg.limits.ghostBytes = 32 KB JSON).</summary>
        public const int MaxGhostB64 = 30 * 1024;

        public static readonly string[] LiveKurse = { "grasbahn", "hof_parcours" };
        public static readonly string[] Rw5Disziplinen = { "springen", "dressur", "gelaende", "rennen", "trail", "schau", "tonnen", "zeit" };
        private static readonly HashSet<string> Rw5Ab = new HashSet<string> { "gelaende", "rennen", "tonnen", "zeit" };

        /// <summary>Every board in menu order: live courses first, then the RW-5 disciplines.</summary>
        public static IEnumerable<string> Alle()
        {
            foreach (var k in LiveKurse) yield return k;
            foreach (var d in Rw5Disziplinen) yield return Rw5Prefix + d;
        }

        public static string KursFuer(string disziplin) => Rw5Prefix + (disziplin ?? "");

        /// <summary>The discipline of an rw5_* board ("" for the live courses).</summary>
        public static string Disziplin(string kurs) =>
            kurs != null && kurs.StartsWith(Rw5Prefix, StringComparison.Ordinal) ? kurs.Substring(Rw5Prefix.Length) : "";

        public static bool IstLive(string kurs) => Array.IndexOf(LiveKurse, kurs) >= 0;

        /// <summary>"ab" / "auf" like ranchmp.js richtungFuer; "" = unknown board.</summary>
        public static string Richtung(string kurs)
        {
            if (IstLive(kurs)) return Ab;
            var d = Disziplin(kurs);
            if (Array.IndexOf(Rw5Disziplinen, d) < 0) return "";
            return Rw5Ab.Contains(d) ? Ab : Auf;
        }

        public static bool IstZeit(string kurs) => Richtung(kurs) == Ab;

        /// <summary>RW-5 boards keep ghosts except Schau and Rennen (RcompLauf records none there).</summary>
        public static bool GeistMoeglich(string kurs)
        {
            var d = Disziplin(kurs);
            return d.Length > 0 && Array.IndexOf(Rw5Disziplinen, d) >= 0 && d != "schau" && d != "rennen";
        }

        /// <summary>
        /// The server's value of a finished RW-5 run: seconds → ms on time boards, points rounded on points boards;
        /// null when the server would refuse it (outside its plausibility window).
        /// </summary>
        public static int? WertFuer(string disziplin, double wert)
        {
            var kurs = KursFuer(disziplin);
            var richtung = Richtung(kurs);
            if (richtung.Length == 0 || double.IsNaN(wert) || double.IsInfinity(wert)) return null;
            var v = richtung == Ab
                ? (long)Math.Round(wert * 1000.0, MidpointRounding.AwayFromZero)
                : (long)Math.Round(wert, MidpointRounding.AwayFromZero);
            return WertOk(kurs, v) ? (int)v : (int?)null;
        }

        /// <summary>ranchmp.js wertOk: live courses ≥ their physical minimum time, RW-5 times ≥ 3 s, points 1..100 000.</summary>
        public static bool WertOk(string kurs, long wert)
        {
            if (IstLive(kurs))
            {
                var k = RmpKurse.Get(kurs);
                if (k == null) return false;
                var min = Math.Max(1_000L, (long)k.MinSegmentMs * Math.Max(1, k.Checkpoints.Length));
                return wert >= min && wert <= MaxZeitMs;
            }
            var richtung = Richtung(kurs);
            if (richtung == Ab) return wert >= MinRw5ZeitMs && wert <= MaxZeitMs;
            if (richtung == Auf) return wert >= 1 && wert <= MaxPunkte;
            return false;
        }

        /// <summary>
        /// Board line value: seconds with hundredths on time boards (close races differ by a few ms; "24,36" in German),
        /// the points otherwise.
        /// </summary>
        public static string Anzeige(string kurs, int wert, System.Globalization.CultureInfo culture = null)
        {
            culture = culture ?? System.Globalization.CultureInfo.InvariantCulture;
            return IstZeit(kurs) ? (wert / 1000.0).ToString("0.00", culture) : wert.ToString(culture);
        }
    }

    /// <summary>
    /// Ranch friends leaderboards on top of REST (ranchmp.js): finished runs go to POST /api/rmp/score (the server keeps the
    /// best per player and board), best RW-5 runs to PUT /api/rmp/ghost/:kurs as G5 Base64, the board pulls
    /// GET /api/rmp/leaderboard/:kurs (me + friends) and a friend's ghost comes from GET /api/rmp/ghost/:kurs/:code.
    /// Offline nothing is queued — calls answer OFFLINE right away. Godot had these calls but never posted a run.
    /// </summary>
    public sealed partial class SocialClient
    {
        private bool RanchReachable => Session.IsOnline && Endpoint.Valid;

        /// <summary>Books a finished run (ms on time boards, points otherwise). Offline: cb(OFFLINE), nothing sent.</summary>
        public void SubmitRanchScore(string kurs, int wert, Action<RanchScoreResult> cb)
        {
            if (RanchBoards.Richtung(kurs).Length == 0) { Callback(cb, RanchScoreResult.Fail("BAD_KURS", kurs)); return; }
            if (!RanchBoards.WertOk(kurs, wert)) { Callback(cb, RanchScoreResult.Fail("BAD_ZEIT", kurs)); return; }
            if (!RanchReachable) { Callback(cb, RanchScoreResult.Fail(NetErrorCodes.Offline, kurs)); return; }
            var body = new JObject { ["kurs"] = kurs, ["wert"] = wert };
            _http.Send("POST", Endpoint.HttpBase + "/api/rmp/score", Identity.Bearer, body.ToString(Formatting.None), (status, text) =>
            {
                var o = NetJson.ParseObject(text);
                Callback(cb, status == 200 ? RanchScoreResult.FromJson(o, kurs) : RanchScoreResult.Fail(HttpCode(status, o), kurs));
            });
        }

        /// <summary>Me + friends for one board, best first. Offline: cb(OFFLINE), nothing sent.</summary>
        public void FetchRanchLeaderboard(string kurs, Action<RanchBoard> cb)
        {
            if (RanchBoards.Richtung(kurs).Length == 0) { Callback(cb, RanchBoard.Fail("BAD_KURS", kurs)); return; }
            if (!RanchReachable) { Callback(cb, RanchBoard.Fail(NetErrorCodes.Offline, kurs)); return; }
            _http.Send("GET", Endpoint.HttpBase + "/api/rmp/leaderboard/" + Uri.EscapeDataString(kurs), Identity.Bearer, null, (status, text) =>
            {
                var o = NetJson.ParseObject(text);
                var b = status == 200 ? RanchBoard.FromJson(o) : RanchBoard.Fail(HttpCode(status, o), kurs);
                if (b.Ok && b.Kurs.Length == 0) b.Kurs = kurs;
                Callback(cb, b);
            });
        }

        /// <summary>Stores the run as the board's ghost when it beats the stored one (RW-5 G5 Base64 only).</summary>
        public void UploadRanchGhost(string kurs, int wert, string b64, Action<RanchScoreResult> cb)
        {
            if (!RanchBoards.GeistMoeglich(kurs)) { Callback(cb, RanchScoreResult.Fail("BAD_KURS", kurs)); return; }
            if (string.IsNullOrEmpty(b64) || b64.Length > RanchBoards.MaxGhostB64 || !RanchBoards.WertOk(kurs, wert))
            {
                Callback(cb, RanchScoreResult.Fail("BAD_GHOST", kurs));
                return;
            }
            if (!RanchReachable) { Callback(cb, RanchScoreResult.Fail(NetErrorCodes.Offline, kurs)); return; }
            var body = new JObject { ["wert"] = wert, ["b64"] = b64 };
            _http.Send("PUT", Endpoint.HttpBase + "/api/rmp/ghost/" + Uri.EscapeDataString(kurs), Identity.Bearer, body.ToString(Formatting.None), (status, text) =>
            {
                var o = NetJson.ParseObject(text);
                Callback(cb, status == 200 ? RanchScoreResult.FromJson(o, kurs) : RanchScoreResult.Fail(HttpCode(status, o), kurs));
            });
        }

        /// <summary>
        /// The own ranch metadata for a visit (PUT /api/ranch, validated first — <see cref="Gooby.Core.Minigames.Ranch.Mp.RmpRanchMeta"/>).
        /// cb(ok, rev, code).
        /// </summary>
        public void UploadRanchMeta(JObject meta, Action<bool, int, string> cb)
        {
            var (ok, reason) = Gooby.Core.Minigames.Ranch.Mp.RmpRanchMeta.Validate(meta);
            if (!ok) { Callback3(cb, false, 0, reason); return; }
            if (!RanchReachable) { Callback3(cb, false, 0, NetErrorCodes.Offline); return; }
            _http.Send("PUT", Endpoint.HttpBase + "/api/ranch", Identity.Bearer, meta.ToString(Formatting.None), (status, text) =>
            {
                var o = NetJson.ParseObject(text);
                if (status == 200 && NetJson.Bool(o?["ok"])) Callback3(cb, true, NetJson.Int(o["rev"]), "");
                else Callback3(cb, false, 0, HttpCode(status, o));
            });
        }

        /// <summary>A friend's ranch metadata for the visit, healed (GET /api/ranch/:code; friends or self only). cb(meta or null, code).</summary>
        public void FetchRanchMeta(string friendCode, Action<JObject, string> cb)
        {
            if (string.IsNullOrEmpty(friendCode)) { cb?.Invoke(null, "NOT_FOUND"); return; }
            if (!RanchReachable) { cb?.Invoke(null, NetErrorCodes.Offline); return; }
            _http.Send("GET", Endpoint.HttpBase + "/api/ranch/" + Uri.EscapeDataString(Upper(friendCode)), Identity.Bearer, null, (status, text) =>
            {
                var o = NetJson.ParseObject(text);
                if (status == 200 && NetJson.Bool(o?["ok"])) cb?.Invoke(Gooby.Core.Minigames.Ranch.Mp.RmpRanchMeta.Normalize(o["meta"]), "");
                else cb?.Invoke(null, HttpCode(status, o));
            });
        }

        private static void Callback3(Action<bool, int, string> cb, bool ok, int rev, string code) => cb?.Invoke(ok, rev, code);

        /// <summary>A friend's (or my own) stored ghost of an RW-5 board.</summary>
        public void FetchRanchGhost(string kurs, string friendCode, Action<RanchGhostReply> cb)
        {
            if (!RanchBoards.GeistMoeglich(kurs) || string.IsNullOrEmpty(friendCode)) { Callback(cb, RanchGhostReply.Fail("NOT_FOUND", kurs, friendCode)); return; }
            if (!RanchReachable) { Callback(cb, RanchGhostReply.Fail(NetErrorCodes.Offline, kurs, friendCode)); return; }
            var url = Endpoint.HttpBase + "/api/rmp/ghost/" + Uri.EscapeDataString(kurs) + "/" + Uri.EscapeDataString(Upper(friendCode));
            _http.Send("GET", url, Identity.Bearer, null, (status, text) =>
            {
                var o = NetJson.ParseObject(text);
                Callback(cb, status == 200 ? RanchGhostReply.FromJson(o, kurs, friendCode) : RanchGhostReply.Fail(HttpCode(status, o), kurs, friendCode));
            });
        }
    }
}
