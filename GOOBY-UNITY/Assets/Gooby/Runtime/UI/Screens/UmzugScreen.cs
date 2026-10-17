using Gooby.Core.Net;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// "Account umziehen" (Einstellungen → Konto, Doc C §7 / move.js): the 8-character code the server panel made for the
    /// OLD device moves its online identity — friend code, friends, GoobyPal history — onto this device (MOVE_REDEEM). The
    /// local save stays as it is; the old device is signed off. Explanation, code field, status line, close.
    /// </summary>
    public sealed class UmzugScreen : UiScreen
    {
        private readonly GameHost _host;
        private TextField _code;
        private Button _redeem;
        private Label _status;
        private bool _busy;

        public UmzugScreen(GameHost host)
        {
            _host = host;
            Id = "umzug";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => { if (!_busy) _host.Ui.Pop(); });
            root.Add(scrim);

            var card = Ux.El("gb-card", "gb-panel-weiss");
            card.name = "umzug-card";
            Ux.Abs(card, null, 40);
            card.style.left = Length.Percent(50);
            card.style.translate = new Translate(Length.Percent(-50), 0);
            card.style.width = 640;
            card.style.paddingLeft = 24; card.style.paddingRight = 24; card.style.paddingTop = 16; card.style.paddingBottom = 20;
            root.Add(card);

            var head = Ux.Row();
            head.Add(Ux.IconTile("user", new Color(0.78f, 0.88f, 1f)));
            var hcol = Ux.Col("gb-grow");
            hcol.style.marginLeft = 12;
            hcol.Add(Ux.Text(t.T("umzug.titel"), "gb-h2"));
            var intro = Ux.Text(t.T("umzug.intro"), "gb-sm", "gb-mild");
            intro.style.whiteSpace = WhiteSpace.Normal;
            hcol.Add(intro);
            head.Add(hcol);
            var close = Ux.IconBtn("close", () => { if (!_busy) _host.Ui.Pop(); }, "gb-btn-icon-sm");
            close.name = "umzug-close";
            head.Add(close);
            card.Add(head);

            var explain = Ux.Text(t.T("umzug.erklaerung"), "gb-md");
            explain.style.whiteSpace = WhiteSpace.Normal;
            explain.style.marginTop = 12;
            card.Add(explain);
            var warn = Ux.Text(t.T("umzug.warnung_alt"), "gb-sm", "gb-bold");
            warn.style.color = MgScenery.Hex(0xD08A1E);
            warn.style.marginTop = 6;
            card.Add(warn);

            var label = Ux.Text(t.T("umzug.code_label"), "gb-sm", "gb-bold");
            label.style.marginTop = 14;
            card.Add(label);
            var row = Ux.Row();
            row.style.marginTop = 4;
            row.style.alignItems = Align.Center;
            _code = new TextField { name = "umzug-code", maxLength = 8 };
            _code.AddToClassList("gb-textfield");
            _code.style.width = 220;
            _code.textEdition.placeholder = t.T("umzug.code_hint");
            row.Add(_code);
            _redeem = Ux.IconTextBtn("check", t.T("umzug.einloesen"), Redeem, "gb-btn-primary", "gb-btn-sm");
            _redeem.name = "umzug-redeem";
            _redeem.style.marginLeft = 10;
            row.Add(_redeem);
            card.Add(row);

            _status = Ux.Text("", "gb-md", "gb-bold");
            _status.name = "umzug-status";
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.marginTop = 12;
            card.Add(_status);
        }

        private void Redeem()
        {
            if (_busy) return;
            var t = _host.Strings;
            var raw = (_code.value ?? "").Trim().ToUpperInvariant();
            if (raw.Length != 8)
            {
                Show(t.T("umzug.fehler_kurz"), false);
                return;
            }
            var client = _host.Online.Client;
            if (!client.IsOnline)
            {
                Show(t.T("umzug.fehler_offline"), false);
                return;
            }
            _busy = true;
            _redeem.SetEnabled(false);
            Show(t.T("umzug.laeuft"), null);
            client.RedeemMoveCode(raw, r =>
            {
                _busy = false;
                _redeem?.SetEnabled(true);
                Debug.Log("[umzug] " + (r.Ok ? "moved, now " + r.FriendCode : "failed " + r.Code));
                if (r.Ok)
                {
                    Show(t.T("umzug.erfolg", ("code", r.FriendCode)), true);
                    Sfx.PlayUi("soft_confirm");
                    Haptics.Success();
                    return;
                }
                switch (r.Code)
                {
                    case "INVALID_CODE": Show(t.T("umzug.fehler_code"), false); break;
                    case "EXPIRED": Show(t.T("umzug.fehler_abgelaufen"), false); break;
                    case "BAD_CODE": Show(t.T("umzug.fehler_kurz"), false); break;
                    case NetErrorCodes.Offline: Show(t.T("umzug.fehler_offline"), false); break;
                    default: Show(t.T("umzug.fehler_generisch", ("code", _host.Online.ErrorText(r.Code))), false); break;
                }
            });
        }

        private void Show(string text, bool? ok)
        {
            if (_status == null) return;
            _status.text = text;
            _status.style.color = ok == null ? MgScenery.Hex(0x7A6A60) : ok.Value ? MgScenery.Hex(0x3E8E4E) : MgScenery.Hex(0xC0392B);
            if (ok == false)
            {
                Sfx.PlayUi("soft_error");
                Haptics.Warn();
            }
        }
    }
}
