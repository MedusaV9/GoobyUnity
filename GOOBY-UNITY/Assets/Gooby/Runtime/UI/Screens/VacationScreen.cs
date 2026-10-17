using System.Collections.Generic;
using Gooby.Core.City;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Unity.App;
using Gooby.Unity.World;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Visiting Gooby on holiday (port of urlaubs_ort.gd's UI, W15/URLAUB): the holiday scene fills the screen, Gooby
    /// tells you about his trip in his bubble, and four big buttons sit at the bottom — pet him (he dances, +fun), a
    /// holiday photo (the camera app over the holiday scene), the mini activity (collect shells / find mountain flowers /
    /// star splinters by tapping them in the scene, or order something silly from the café waiter) and the souvenir
    /// spot (one find a day: the chest opens, the find goes to your keepsakes, +5 coins). A visit is only a look — the
    /// trip itself is untouched.
    /// </summary>
    public sealed class VacationScreen : UiScreen
    {
        private readonly GameHost _host;
        private readonly string _dest, _archetyp;
        private Label _coins, _bubbleText, _progress;
        private VisualElement _bubble, _progressPill;
        private Urlaub.TapSpots _spots;
        private int _bestellungen, _line, _soulLine;
        private float _bubbleLeft, _narrateIn = 1.2f, _danceLeft;
        private bool _leaving;

        public override string SoundScene => _dest == "space" ? "space" : "vacation";

        public VacationScreen(GameHost host, string destId)
        {
            _host = host;
            _dest = destId;
            _archetyp = Urlaub.ArchetypFuer(destId);
            Id = "vacation";
            Opaque = true;
        }

        private VacationScene Scene => _host.World?.Vacation;

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.pickingMode = PickingMode.Ignore;
            root.AddToClassList("gb-place-chrome");
            if (_host.World != null && _host.World.Vacation == null) _host.World.EnterVacation(_dest);
            _host.Store.Update(s => Urlaub.MerkeBesuch(s, _dest), GameHost.TicksMs);
            _host.Store.NotifySliceChanged("vacation");

            // taps on the scene: a spot of the mini activity, or Gooby himself (pets him)
            var catcher = new VisualElement { pickingMode = PickingMode.Position, name = "vacation-taps" };
            Ux.Abs(catcher, 0, 0, 0, 0);
            catcher.RegisterCallback<PointerUpEvent>(_ => TapScene(Input.mousePosition));
            root.Add(catcher);

            var back = Ux.IconBtn("back_white", Leave, "gb-place-back");
            back.name = "vacation-leave";
            Ux.Abs(back, 16, 16);
            root.Add(back);
            var head = Ux.El("gb-card", "gb-row");
            Ux.Abs(head, 84, 12);
            head.style.paddingLeft = 8; head.style.paddingTop = 6; head.style.paddingBottom = 6; head.style.paddingRight = 18;
            head.Add(Ux.IconTile(_archetyp == Urlaub.Weltraum ? "star" : _archetyp == Urlaub.Berge ? "sprout" : _archetyp == Urlaub.Stadt ? "teacup" : "sun", new Color(1f, 0.9f, 0.7f)));
            var hcol = Ux.Col();
            hcol.style.marginLeft = 10;
            hcol.Add(Ux.Text(t.T("urlaub.titel." + _archetyp), "gb-place-title"));
            hcol.Add(Ux.Text(t.T("travel.ziel." + _dest), "gb-sm", "gb-mild"));
            head.Add(hcol);
            root.Add(head);

            var tr = Ux.Row("gb-place-top");
            Ux.Abs(tr, null, 12, 16);
            var coinPill = Ux.Pill("coin", Ux.FormatNumber(ShopLogic.Coins(_host.Store)), out _coins);
            tr.Add(coinPill);
            root.Add(tr);
            _host.Store.CoinsChanged -= OnCoins;
            _host.Store.CoinsChanged += OnCoins;

            _bubble = Ux.El("gb-bubble");
            _bubble.name = "vacation-bubble";
            Ux.Abs(_bubble, null, 96);
            _bubble.style.left = Length.Percent(50);
            _bubble.style.translate = new Translate(Length.Percent(-50), 0);
            _bubble.Add(Ux.Portrait(52));
            _bubbleText = Ux.Text("", "gb-lg", "gb-semibold");
            _bubbleText.style.whiteSpace = WhiteSpace.Normal;
            _bubbleText.style.flexShrink = 1;
            _bubble.Add(_bubbleText);
            _bubble.AddToClassList("gb-bubble--fading");
            _bubble.style.opacity = 0f;
            _bubble.RegisterCallback<ClickEvent>(_ => { _bubble.style.opacity = 0f; _bubbleLeft = 0f; });
            root.Add(_bubble);

            _progressPill = Ux.El("gb-card", "gb-row", "gb-vacation-progress");
            _progressPill.name = "vacation-progress";
            Ux.Abs(_progressPill, null, null, null, 166);
            _progressPill.style.left = Length.Percent(50);
            _progressPill.style.translate = new Translate(Length.Percent(-50), 0);
            _progressPill.Add(Ux.Icon(MiniIcon));
            _progress = Ux.Text("", "gb-lg", "gb-bold");
            _progress.style.marginLeft = 8;
            _progressPill.Add(_progress);
            _progressPill.style.display = DisplayStyle.None;
            root.Add(_progressPill);

            var bar = Ux.Row("gb-vacation-bar");
            bar.name = "vacation-actions";
            Ux.Abs(bar, null, null, null, 22);
            bar.style.left = Length.Percent(50);
            bar.style.translate = new Translate(Length.Percent(-50), 0);
            bar.Add(Action("heart", t.T("urlaub.knopf.streicheln"), "vacation-pet", Pet, main: true));
            bar.Add(Action("camera", t.T("urlaub.knopf.foto"), "vacation-photo", Photo));
            bar.Add(Action(MiniIcon, t.T("urlaub.knopf.mini." + _archetyp), "vacation-mini", Mini));
            bar.Add(Action("gift", t.T("urlaub.knopf.souvenir"), "vacation-souvenir", Souvenir));
            root.Add(bar);
            Debug.Log("[urlaub] besuch " + _dest + " (" + _archetyp + ")");
        }

        private string MiniIcon => _archetyp == Urlaub.Berge ? "flower" : _archetyp == Urlaub.Stadt ? "teacup" : _archetyp == Urlaub.Weltraum ? "star" : "sparkle";

        private static VisualElement Action(string icon, string label, string name, System.Action onClick, bool main = false)
        {
            var a = Ux.El("gb-action");
            if (main) a.AddToClassList("gb-action--main");
            a.name = name;
            a.style.marginLeft = 6; a.style.marginRight = 6;
            a.Add(Ux.Icon(icon));
            a.Add(Ux.Text(label));
            a.RegisterCallback<ClickEvent>(_ => onClick());
            return a;
        }

        public override void OnShown()
        {
            _host.Store.CoinsChanged -= OnCoins;
            _host.Store.CoinsChanged += OnCoins;
            OnCoins(ShopLogic.Coins(_host.Store));
        }

        public override void OnHidden()
        {
            if (!_host.Ui.IsMounted(this)) _host.Store.CoinsChanged -= OnCoins;
        }

        private void OnCoins(int coins) { if (_coins != null) _coins.text = Ux.FormatNumber(coins); }

        // ── Gooby talks ──

        private void Say(string text, float seconds = 7f)
        {
            if (_bubbleText == null || string.IsNullOrEmpty(text)) return;
            _bubbleText.text = text;
            _bubble.style.opacity = 1f;
            _bubbleLeft = seconds;
            _host.Audio?.Say(text, "happy");
        }

        /// <summary>His holiday stories, one after the other (urlaub.bubble.&lt;kind&gt;), the first one greets you.</summary>
        private void Narrate()
        {
            var lines = _host.Strings.Items("urlaub.bubble." + _archetyp);
            if (lines.Count == 0) return;
            Say(J.Str(lines[_line % lines.Count]), 8f);
            _line++;
        }

        public override void Update()
        {
            var dt = Time.unscaledDeltaTime;
            if (_bubbleLeft > 0f && (_bubbleLeft -= dt) <= 0f && _bubble != null) _bubble.style.opacity = 0f;
            if (_host.Ui.Current == this)
            {
                _narrateIn -= dt;
                if (_narrateIn <= 0f)
                {
                    _narrateIn = Random.Range(14f, 20f);
                    if (_bubbleLeft <= 0f) Narrate();
                }
            }
            if (_danceLeft > 0f && (_danceLeft -= dt) <= 0f)
            {
                var a = _host.World?.Avatar;
                if (a != null && a.Clip == "dance-loop") a.Play("idle-loop");
            }
        }

        // ── the four doings ──

        private void Pet()
        {
            var t = _host.Strings;
            var a = _host.World?.Avatar;
            if (a != null)
            {
                a.SetEmotion("ecstatic");
                a.Play("dance-loop");
                _danceLeft = 3f;
            }
            _host.Store.Update(s => Urlaub.FunBonus(s, Urlaub.FunStreicheln), GameHost.TicksMs);
            _host.Store.NotifySliceChanged("gooby");
            Sfx.Play("pet_squish", Random.Range(-0.5f, 1.5f));
            Haptics.Tap();
            _host.Ui.Toast(t.T("urlaub.streicheln", ("n", (int)Urlaub.FunStreicheln)), "heart", 2.4f);
            var souls = 0;
            while (t.HasKey("urlaub.soul." + (souls + 1))) souls++;
            if (souls > 0) Say(t.T("urlaub.soul." + (_soulLine++ % souls + 1)));
            Debug.Log("[urlaub] streicheln");
        }

        private void Photo()
        {
            Sfx.PlayUi("soft_open");
            _host.Ui.Push(new PhotoModeScreen(_host));
        }

        private void Mini()
        {
            var t = _host.Strings;
            if (_archetyp == Urlaub.Stadt)
            {
                var orders = _host.Strings.Items("urlaub.bestellung");
                if (orders.Count == 0) return;
                var i = Urlaub.BestellungIndex(_bestellungen++, orders.Count);
                Say(J.Str(orders[i]));
                _host.World?.Avatar?.Play("hop");
                Sfx.PlayUi("soft_confirm");
                _host.Store.Update(s => Urlaub.FunBonus(s, 1.0), GameHost.TicksMs);
                Debug.Log("[urlaub] bestellung " + i);
                return;
            }
            if (_spots != null && !_spots.Fertig) { Say(t.T("urlaub.mini.los." + _archetyp)); return; }
            _spots = new Urlaub.TapSpots();
            Scene?.ShowSpots();
            Say(t.T("urlaub.mini.los." + _archetyp));
            RefreshProgress();
            Sfx.PlayUi("soft_open");
            Debug.Log("[urlaub] mini start");
        }

        private void RefreshProgress()
        {
            if (_spots == null || _spots.Fertig) { _progressPill.style.display = DisplayStyle.None; return; }
            _progress.text = _host.Strings.T("urlaub.mini.fortschritt", ("n", _spots.Gesamt - _spots.Rest), ("gesamt", _spots.Gesamt));
            _progressPill.style.display = DisplayStyle.Flex;
        }

        private void TapScene(Vector2 screenPos)
        {
            var w = _host.World;
            if (w == null || _leaving) return;
            var spot = _spots != null && !_spots.Fertig ? w.TapVacationSpot(screenPos) : -1;
            if (spot >= 0) { TakeSpot(spot); return; }
            if (w.TapVacationGooby(screenPos)) Pet();
        }

        private void TakeSpot(int i)
        {
            if (_spots == null || !_spots.Tippe(i)) return;
            Scene?.TakeSpot(i, _host.Settings.ReducedMotion);
            var found = _spots.Gesamt - _spots.Rest;
            Sfx.Play("soft_collect", found * 1.5f);
            Haptics.Tap();
            RefreshProgress();
            Debug.Log("[urlaub] spot " + i + " (" + found + "/" + _spots.Gesamt + ")");
            if (!_spots.Fertig) return;
            _host.Store.Update(s => Urlaub.FunBonus(s, Urlaub.FunMiniFertig), GameHost.TicksMs);
            _host.Store.NotifySliceChanged("gooby");
            _host.Ui.Toast(_host.Strings.T("urlaub.toast.mini_fertig"), "star", 3f);
            _host.Ui.Confetti(70);
            Sfx.PlayUi("soft_win");
            var a = _host.World?.Avatar;
            if (a != null) { a.SetEmotion("ecstatic"); a.Play("celebrate"); }
        }

        private void Souvenir()
        {
            var t = _host.Strings;
            var ok = false;
            _host.Store.Update(s => ok = Urlaub.SouvenirEinloesen(s, _dest, _host.Store.Clock.NowMs()), GameHost.TicksMs);
            if (!ok)
            {
                Sfx.PlayUi("soft_error");
                _host.Ui.Toast(t.T("urlaub.toast.souvenir_leer"), "gift", 3f);
                return;
            }
            _host.Store.NotifySliceChanged("vacation");
            _host.Store.NotifySliceChanged("inventory");
            Scene?.OpenChest(_host.Settings.ReducedMotion);
            Sfx.PlayUi("soft_sticker");
            _host.Ui.Toast(t.T("urlaub.toast.souvenir", ("name", t.T("urlaub.souvenir." + _dest)), ("coins", Urlaub.SouvenirCoins)), "gift", 3.4f);
            var a = _host.World?.Avatar;
            if (a != null) { a.SetEmotion("ecstatic"); a.Play("wave"); }
            Debug.Log("[urlaub] souvenir " + _dest);
        }

        private void Leave()
        {
            if (_leaving) return;
            _leaving = true;
            Sfx.PlayUi("soft_back");
            _host.Ui.FadeThrough(() =>
            {
                _host.World?.LeaveVacation();
                if (_host.Ui.Current == this) _host.Ui.Pop();
            });
        }

        /// <summary>QA: "urlaub mini|spot n|souvenir|pet|leave".</summary>
        public void Qa(string what, int arg)
        {
            switch (what)
            {
                case "mini": Mini(); break;
                case "spot": TakeSpot(arg); break;
                case "souvenir": Souvenir(); break;
                case "pet": Pet(); break;
                case "leave": Leave(); break;
            }
        }
    }
}
