using Gooby.Core.Json;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// First start (port of onboarding_flow.gd / onboarding_logic.gd without the face editor Unity does not render): a
    /// warm card over the room with the player's 3D Gooby waving beside it — the player's name (required, 24 characters),
    /// then Gooby's nickname (empty keeps "Gooby") — and the arrival is celebrated. The result lands exactly where
    /// game_state.gd apply_onboarding_profile puts it (meta.playerName, meta.goobyNickname, onboarding.done), so saves stay
    /// interchangeable with Godot. BootFlow opens it once for a fresh save; the profile card reopens it as a rename
    /// (<c>renameOnly</c>: just the nickname step, a toast instead of the arrival).
    /// </summary>
    public sealed class OnboardingScreen : UiScreen
    {
        public const int NameMaxLength = 24;
        public const string DefaultNickname = "Gooby";

        private readonly GameHost _host;
        private readonly bool _renameOnly;
        private int _step;
        private string _playerName = "", _nickname = DefaultNickname;
        private VisualElement _content;
        private World.MgBuddy3D _buddy;

        public OnboardingScreen(GameHost host, bool renameOnly = false)
        {
            _host = host;
            _renameOnly = renameOnly;
            Id = renameOnly ? "rename" : "onboarding";
        }

        protected override void Build(VisualElement root)
        {
            root.AddToClassList("gb-sheets");
            var scrim = Ux.El("gb-scrim");
            if (_renameOnly) scrim.RegisterCallback<ClickEvent>(_ => _host.Ui.Pop());
            root.Add(scrim);
            var meta = J.Dict(_host.Store.State["meta"]);
            _playerName = J.Str(meta["playerName"], "");
            _nickname = J.Str(meta["goobyNickname"], DefaultNickname);
            var card = Ux.El("gb-mgw", "gb-onb-card");
            Ux.Abs(card, null, null);
            card.style.left = Length.Percent(50); card.style.top = Length.Percent(50);
            card.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
            card.style.width = 760;
            card.style.maxWidth = Length.Percent(94);
            var row = Ux.Row();
            row.style.alignItems = Align.Stretch;
            if (World.MgStage3D.Supported)
            {
                try
                {
                    var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(J.Dict(_host.Store.State["cosmetics"]));
                    _buddy = new World.MgBuddy3D(outfit, slot: 2);
                    var col = Ux.El("gb-mg-buddy-col");
                    col.pickingMode = PickingMode.Ignore;
                    col.Add(Ux.El("gb-mg-buddy-rug"));
                    var view = Ux.El("gb-mg-buddy-view");
                    view.style.width = 190; view.style.height = 256;
                    col.Add(view);
                    _buddy.Bind(view, World.MgBuddy3D.Framing.Full, 190f / 256f);
                    row.Add(col);
                    card.RegisterCallback<GeometryChangedEvent>(_ =>
                    {
                        var w = card.resolvedStyle.width;
                        if (!float.IsNaN(w) && w > 1f) col.style.display = w >= 640f ? DisplayStyle.Flex : DisplayStyle.None;
                    });
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[onboarding] 3D Gooby: " + e.Message);
                    _buddy = null;
                }
            }
            _content = Ux.Col("gb-onb-content", "gb-grow");
            _content.style.flexShrink = 1;
            row.Add(_content);
            card.Add(row);
            root.Add(card);
            ShowStep(_renameOnly ? 1 : 0);
            _buddy?.Wave();
        }

        public override void Update() => _buddy?.Tick(Mathf.Min(Time.unscaledDeltaTime, 0.25f));

        public override void OnHidden()
        {
            if (_host.Ui.IsMounted(this)) return;
            _buddy?.Destroy();
            _buddy = null;
        }

        private void ShowStep(int step)
        {
            var t = _host.Strings;
            _step = step;
            _content.Clear();
            var first = step == 0;
            _content.Add(Ux.Text(first ? t.T("nl.onboarding.welcome_titel") : t.T("nl.onboarding.nickname_titel"), "gb-onb-title"));
            var text = Ux.Text(first ? t.T("nl.onboarding.welcome_text") : t.T("nl.onboarding.nickname_text"), "gb-onb-text");
            text.style.whiteSpace = WhiteSpace.Normal;
            _content.Add(text);
            var field = new TextField { maxLength = NameMaxLength, name = first ? "onb-name" : "onb-nickname" };
            field.AddToClassList("gb-textfield");
            field.AddToClassList("gb-onb-field");
            field.textEdition.placeholder = first ? t.T("nl.onboarding.name_placeholder") : DefaultNickname;
            field.value = first ? _playerName : (_nickname == DefaultNickname ? "" : _nickname);
            _content.Add(field);
            var hint = Ux.Text(t.T("nl.onboarding.name_pflicht"), "gb-onb-hint");
            hint.style.visibility = Visibility.Hidden;
            _content.Add(hint);
            var actions = Ux.Row("gb-onb-actions");
            actions.Add(Ux.El("gb-grow"));
            var label = first ? t.T("nl.onboarding.weiter") : _renameOnly ? t.T("nl.onboarding.rename_button") : t.T("nl.onboarding.done_button");
            if (_renameOnly)
            {
                var cancel = Ux.Btn(t.T("nl.allgemein.schliessen"), () => _host.Ui.Pop(), "gb-btn-secondary", "gb-onb-next");
                cancel.style.minWidth = 140;
                cancel.style.marginRight = 12;
                actions.Add(cancel);
            }
            var next = Ux.IconTextBtn("play", label, () => Submit(field, hint), "gb-btn-primary", "gb-onb-next");
            next.name = first ? "onb-next" : "onb-done";
            actions.Add(next);
            _content.Add(actions);
            field.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Submit(field, hint); });
            field.schedule.Execute(() => field.Focus()).StartingIn(60);
        }

        private void Submit(TextField field, Label hint)
        {
            var value = (field.value ?? "").Trim();
            if (value.Length > NameMaxLength) value = value.Substring(0, NameMaxLength);
            if (_step == 0)
            {
                if (value.Length == 0)
                {
                    hint.style.visibility = Visibility.Visible;
                    Sfx.PlayUi("soft_error");
                    _buddy?.Wince(false);
                    return;
                }
                _playerName = value;
                Sfx.PlayUi("soft_confirm");
                _buddy?.Cheer(1, 2);
                ShowStep(1);
                return;
            }
            _nickname = value.Length == 0 ? DefaultNickname : value;
            if (_renameOnly) Rename();
            else Finish();
        }

        private void Rename()
        {
            var nick = _nickname;
            _host.Store.Update(s => J.EnsureDict(s, "meta")["goobyNickname"] = nick, GameHost.TicksMs);
            _host.Store.NotifySliceChanged("meta");
            Sfx.PlayUi("soft_confirm");
            _host.Ui.Pop();
            _host.Ui.Toast(_host.Strings.T("nl.onboarding.rename_toast", ("nickname", nick)), "heart");
        }

        /// <summary>apply_onboarding_profile: the names into meta, onboarding.done — then the arrival is celebrated.</summary>
        private void Finish()
        {
            var t = _host.Strings;
            var player = _playerName;
            var nick = _nickname;
            _host.Store.Update(s =>
            {
                var meta = J.EnsureDict(s, "meta");
                if (player.Length > 0) meta["playerName"] = player;
                meta["goobyNickname"] = nick;
                J.EnsureDict(s, "onboarding")["done"] = true;
            }, GameHost.TicksMs);
            _host.Store.NotifySliceChanged("meta");
            _host.Store.NotifySliceChanged("onboarding");
            Debug.Log("[onboarding] done: " + nick);
            _host.Ui.Pop();
            _host.Ui.Celebrate(t.T("nl.onboarding.done_titel", ("nickname", nick)), t.T("nl.onboarding.done_text", ("nickname", nick)), "heart", 4.5f);
            _host.Audio?.PlayStinger("stinger-levelup");
        }
    }
}
