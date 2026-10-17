using System;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Net
{
    /// <summary>Outcome of an account move (MOVE_REDEEM → MOVE_RESULT).</summary>
    public sealed class MoveRedeemResult
    {
        public bool Ok { get; internal set; }
        /// <summary>INVALID_CODE | EXPIRED | RATE_LIMIT | OFFLINE | BAD_CODE | …</summary>
        public string Code { get; internal set; } = "";
        /// <summary>The friend code this device answers to after the move.</summary>
        public string FriendCode { get; internal set; } = "";

        internal static MoveRedeemResult Fail(string code) => new MoveRedeemResult { Ok = false, Code = code ?? "ERROR" };
    }

    /// <summary>Outcome of an online promo code (POST /api/codes/redeem).</summary>
    public sealed class CodeRedeemResult
    {
        public bool Ok { get; internal set; }
        /// <summary>UNKNOWN | INACTIVE | NOT_YET_VALID | EXPIRED | EXHAUSTED | ALREADY_REDEEMED | BAD_CODE | RATE_LIMIT | OFFLINE …</summary>
        public string Code { get; internal set; } = "";
        /// <summary>The panel's free reward object ({"coins":500, "sticker":"sonne", …}); applied by the game's reward path.</summary>
        public JObject Reward { get; internal set; } = new JObject();

        internal static CodeRedeemResult Fail(string code) => new CodeRedeemResult { Ok = false, Code = code ?? "ERROR" };
    }

    public sealed partial class SocialClient
    {
        private static readonly Regex MoveCodeRe = new Regex("^[A-HJ-NP-Z2-9]{8}$");
        private bool _moveReconnect;

        /// <summary>
        /// Account move (move.js, Doc C §7): the 8-character code from the server panel moves the OLD device's server
        /// identity — friend code, friends, GoobyPal history — onto this device. The server rotates the secret, closes both
        /// connections (4008 "MOVED"); this device saves the adopted identity and reconnects with it on the next pump. The
        /// local save stays where it is (it is no save transfer).
        /// </summary>
        public void RedeemMoveCode(string code, Action<MoveRedeemResult> cb)
        {
            var clean = (code ?? "").Trim().ToUpperInvariant();
            if (!MoveCodeRe.IsMatch(clean)) { Callback(cb, MoveRedeemResult.Fail("BAD_CODE")); return; }
            if (!Session.IsOnline) { Callback(cb, MoveRedeemResult.Fail(NetErrorCodes.Offline)); return; }
            Session.Request("MOVE_REDEEM", new JObject { ["code"] = clean }, r =>
            {
                if (!r.Ok) { Callback(cb, MoveRedeemResult.Fail(r.Code)); return; }
                var d = r.Data;
                if (!NetJson.Bool(d["ok"])) { Callback(cb, MoveRedeemResult.Fail(NetJson.Str(d["code"], "ERROR"))); return; }
                var id = d["identity"] as JObject;
                var friendCode = NetJson.Str(id?["friendCode"]);
                if (!Identity.Adopt(NetJson.Str(id?["deviceId"]), NetJson.Str(id?["deviceSecret"]), friendCode))
                {
                    Callback(cb, MoveRedeemResult.Fail(NetErrorCodes.BadMessage));
                    return;
                }
                _friends.Clear();
                _moveReconnect = true;
                MarkDirty();
                Callback(cb, new MoveRedeemResult { Ok = true, FriendCode = friendCode });
            });
        }

        /// <summary>The old device of a move: a fresh guest identity, no friends of the moved account any more.</summary>
        private void RenewAfterMove()
        {
            Identity.Renew();
            _friends.Clear();
            _requests.Clear();
            MarkDirty();
        }

        /// <summary>
        /// Online promo code (codes.js): asked when the built-in/pack codes do not know the word — once per device, max uses,
        /// validity window are the server's business. Offline: cb(OFFLINE), nothing sent.
        /// </summary>
        public void RedeemOnlineCode(string code, Action<CodeRedeemResult> cb)
        {
            var clean = (code ?? "").Trim().ToUpperInvariant();
            if (clean.Length == 0) { Callback(cb, CodeRedeemResult.Fail("BAD_CODE")); return; }
            if (!Session.IsOnline || !Endpoint.Valid) { Callback(cb, CodeRedeemResult.Fail(NetErrorCodes.Offline)); return; }
            var body = new JObject { ["code"] = clean };
            _http.Send("POST", Endpoint.HttpBase + "/api/codes/redeem", Identity.Bearer, body.ToString(Formatting.None), (status, text) =>
            {
                var o = NetJson.ParseObject(text);
                if (status == 200 && o != null && NetJson.Bool(o["ok"]))
                    Callback(cb, new CodeRedeemResult { Ok = true, Reward = o["reward"] as JObject ?? new JObject() });
                else
                    Callback(cb, CodeRedeemResult.Fail(status == 200 ? NetJson.Str(o?["code"], "ERROR") : HttpCode(status, o)));
            });
        }
    }
}
