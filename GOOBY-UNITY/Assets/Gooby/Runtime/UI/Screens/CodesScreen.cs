using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Gooby.Core.Meta;
using Gooby.Core.Net;
using Gooby.Unity.App;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// "Aktionscodes" (codes_screen.gd, Einstellungen → Konto): a code word goes to the built-in/pack codes first
    /// (<see cref="CodesEngine"/>: attempt window + lockout, once-latch, coins / double-coins buff / sticker / unlock flag);
    /// a word they do not know is asked at the server (codes.js, once per device) when online and its reward runs through the
    /// same effect path. Below: the running double-coins buff and the history of redeemed codes.
    /// </summary>
    public sealed class CodesScreen : UiScreen
    {
        private readonly GameHost _host;
        private readonly List<long> _attempts = new List<long>();
        private TextField _input;
        private Button _redeem;
        private Label _feedback, _buff;
        private VisualElement _history;
        private bool _asking;

        public CodesScreen(GameHost host)
        {
            _host = host;
            Id = "codes";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => _host.Ui.Pop());
            root.Add(scrim);

            var card = Ux.El("gb-card", "gb-panel-weiss");
            card.name = "codes-card";
            Ux.Abs(card, null, 40);
            card.style.left = Length.Percent(50);
            card.style.translate = new Translate(Length.Percent(-50), 0);
            card.style.width = 640;
            card.style.paddingLeft = 24; card.style.paddingRight = 24; card.style.paddingTop = 16; card.style.paddingBottom = 20;
            root.Add(card);

            var head = Ux.Row();
            head.Add(Ux.IconTile("gift", new Color(1f, 0.9f, 0.62f)));
            var hcol = Ux.Col("gb-grow");
            hcol.style.marginLeft = 12;
            hcol.Add(Ux.Text(t.T("codes.titel"), "gb-h2"));
            var hint = Ux.Text(t.T("codes.hinweis"), "gb-sm", "gb-mild");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hcol.Add(hint);
            head.Add(hcol);
            var close = Ux.IconBtn("close", () => _host.Ui.Pop(), "gb-btn-icon-sm");
            close.name = "codes-close";
            head.Add(close);
            card.Add(head);

            var row = Ux.Row();
            row.style.marginTop = 16;
            row.style.alignItems = Align.Center;
            _input = new TextField { name = "codes-input", maxLength = 32 };
            _input.AddToClassList("gb-textfield");
            _input.style.flexGrow = 1;
            _input.textEdition.placeholder = t.T("codes.platzhalter");
            _input.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Redeem(); });
            row.Add(_input);
            _redeem = Ux.IconTextBtn("check", t.T("codes.einloesen"), Redeem, "gb-btn-primary", "gb-btn-sm");
            _redeem.name = "codes-redeem";
            _redeem.style.marginLeft = 10;
            row.Add(_redeem);
            card.Add(row);

            _feedback = Ux.Text("", "gb-md", "gb-bold");
            _feedback.name = "codes-feedback";
            _feedback.style.whiteSpace = WhiteSpace.Normal;
            _feedback.style.marginTop = 10;
            card.Add(_feedback);
            _buff = Ux.Text("", "gb-sm", "gb-bold");
            _buff.name = "codes-buff";
            _buff.style.color = MgScenery.Hex(0xD08A1E);
            _buff.style.marginTop = 6;
            card.Add(_buff);

            var histHead = Ux.Text(t.T("codes.verlauf.titel"), "gb-lg", "gb-bold");
            histHead.style.marginTop = 16;
            card.Add(histHead);
            _history = Ux.Col();
            _history.name = "codes-history";
            _history.style.marginTop = 4;
            card.Add(_history);
            RefreshHistory();
            RefreshLock();
        }

        public override void Update()
        {
            RefreshLock();
            if (_buff == null) return;
            var rest = CodesEngine.RemainingMs(_host.Store.State, _host.Store.Clock.NowMs());
            _buff.style.display = rest > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (rest > 0) _buff.text = _host.Strings.T("codes.buff_aktiv", ("rest", MmSs(rest)));
        }

        /// <summary>QA/tests: type + redeem through the same path as the button.</summary>
        public void RedeemText(string text)
        {
            if (_input != null) _input.value = text ?? "";
            Redeem();
        }

        private void Redeem()
        {
            if (_asking || _input == null) return;
            var t = _host.Strings;
            var raw = _input.value ?? "";
            var result = CodesEngine.RedeemAndApply(_host.Store, raw, _attempts);
            if (result.Ok)
            {
                Celebrate(result.Code);
                return;
            }
            switch (result.Reason)
            {
                case "empty": Fail(t.T("codes.fehler.leer")); return;
                case "already": Fail(t.T("codes.fehler.schon")); return;
                case "locked": Fail(LockText()); return;
            }
            // unknown to the built-in and pack codes: maybe a code from the server panel
            var client = _host.Online?.Client;
            if (client == null || !client.IsOnline)
            {
                Fail(CodesEngine.LockRemainingMs(_host.Store.State, _host.Store.Clock.NowMs()) > 0 ? LockText() : t.T("codes.fehler.unbekannt"));
                return;
            }
            _asking = true;
            _redeem.SetEnabled(false);
            _feedback.style.color = MgScenery.Hex(0x7A6A60);
            _feedback.text = t.T("nl.codes.online_pruefe");
            client.RedeemOnlineCode(raw, r =>
            {
                _asking = false;
                _redeem?.SetEnabled(true);
                if (_feedback == null) return;
                if (r.Ok)
                {
                    Celebrate(CodesEngine.ApplyServerReward(_host.Store, raw, r.Reward));
                    return;
                }
                Debug.Log("[codes] online redeem failed: " + r.Code);
                Fail(ServerError(r.Code));
            });
        }

        private string ServerError(string code)
        {
            var t = _host.Strings;
            switch (code)
            {
                case "ALREADY_REDEEMED": return t.T("codes.fehler.schon");
                case "EXPIRED": return t.T("nl.codes.fehler_abgelaufen");
                case "EXHAUSTED": return t.T("nl.codes.fehler_aufgebraucht");
                case "INACTIVE":
                case "NOT_YET_VALID": return t.T("nl.codes.fehler_inaktiv");
                case "RATE_LIMIT": return LockText();
                default:
                    return CodesEngine.LockRemainingMs(_host.Store.State, _host.Store.Clock.NowMs()) > 0 ? LockText() : t.T("codes.fehler.unbekannt");
            }
        }

        private void Celebrate(CodeDef code)
        {
            var t = _host.Strings;
            _input.value = "";
            var effect = code?.Effect ?? new JObject();
            var lines = new List<string> { t.T("codes.erfolg.titel") };
            if (effect["coins"] != null) lines.Add(t.T("codes.erfolg.muenzen", ("n", J.Int(effect["coins"]))));
            if (effect["sticker"] != null) lines.Add(t.T("codes.erfolg.sticker"));
            if (J.Str(effect["buff"]) == "doubleCoins") lines.Add(t.T("codes.erfolg.buff", ("min", J.Int(effect["minutes"]))));
            if (effect["unlock_flag"] != null && t.HasKey("codes.erfolg.flag." + code.Id)) lines.Add(t.T("codes.erfolg.flag." + code.Id));
            _feedback.style.color = MgScenery.Hex(0x3E8E4E);
            _feedback.text = string.Join("  ", lines);
            Sfx.PlayUi("soft_confirm");
            Haptics.Success();
            Debug.Log("[codes] redeemed " + code?.Id);
            RefreshHistory();
        }

        private void Fail(string text)
        {
            _feedback.style.color = MgScenery.Hex(0xC0392B);
            _feedback.text = text;
            Sfx.PlayUi("soft_error");
            Haptics.Warn();
            RefreshLock();
        }

        private string LockText()
        {
            var s = (int)Math.Ceiling(CodesEngine.LockRemainingMs(_host.Store.State, _host.Store.Clock.NowMs()) / 1000.0);
            return _host.Strings.T("codes.fehler.gesperrt", ("s", Math.Max(1, s)));
        }

        private void RefreshLock()
        {
            if (_redeem == null || _asking) return;
            var locked = CodesEngine.LockRemainingMs(_host.Store.State, _host.Store.Clock.NowMs()) > 0;
            _redeem.SetEnabled(!locked);
        }

        private void RefreshHistory()
        {
            if (_history == null) return;
            var t = _host.Strings;
            _history.Clear();
            var entries = CodesEngine.RedeemedEntries(_host.Store.State);
            if (entries.Count == 0)
            {
                _history.Add(Ux.Text(t.T("codes.verlauf.leer"), "gb-sm", "gb-mild"));
                return;
            }
            foreach (var (id, at) in entries)
            {
                var name = id.StartsWith("srv:", StringComparison.Ordinal) ? t.T("nl.codes.eintrag_online", ("code", id.Substring(4)))
                    : t.HasKey("codes.name." + id) ? t.T("codes.name." + id) : id;
                var when = DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(0L, at)).ToLocalTime().ToString("dd.MM.yyyy");
                var line = Ux.Text(t.T("codes.verlauf.eintrag", ("name", name), ("datum", when)), "gb-sm");
                line.name = "codes-entry";
                _history.Add(line);
            }
        }

        private static string MmSs(long ms)
        {
            var s = (int)(ms / 1000);
            return (s / 60) + ":" + (s % 60).ToString("00");
        }
    }
}
