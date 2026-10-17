using Gooby.Core.Home;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// HUD of a house visit (both sides): a board-08 capsule "Zu Besuch bei …" / "… ist zu Besuch", the end button, the
    /// joystick, three emotes (Herz, Winken, Hüpfen) and a name tag pinned over the other Gooby. Our pose goes out over
    /// the POS relay every frame (the client throttles to 5 Hz, a room change is sent at once).
    /// </summary>
    public sealed class VisitScreen : UiScreen
    {
        private readonly GameHost _host;

        public override string SoundScene => "home/living";
        private readonly OnlineService _online;
        private readonly bool _isHost;
        private Label _title, _sub, _tagLabel, _zzzOwn, _zzzPeer;
        private VisualElement _tag, _toastLane, _emoteBubble, _subDot;
        private Joystick _joy;
        private string _lastRoom = "";
        private float _emoteBubbleLeft, _zzzPhase, _peerBuildingLeft;

        /// <summary>How long after the host's last BUILD_* the guest's capsule says "… dekoriert gerade".</summary>
        private const float PeerBuildingHold = 15f;

        public VisitScreen(GameHost host, OnlineService online, bool isHost)
        {
            _host = host;
            _online = online;
            _isHost = isHost;
            Id = "visit";
            Opaque = true; // the home HUD hides; the world keeps rendering behind
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.pickingMode = PickingMode.Ignore;
            root.AddToClassList("gb-online");

            var catcher = new VisualElement { pickingMode = PickingMode.Position, name = "world-taps" };
            Ux.Abs(catcher, 0, 0, 0, 0);
            catcher.RegisterCallback<PointerUpEvent>(_ => _host.World?.TapWorld(Input.mousePosition));
            root.Add(catcher);

            // name tag over the other Gooby: white pill with a heart, centred on the head (translate -50 %), little tail
            _tag = Ux.El("gb-visit-tag");
            _tag.pickingMode = PickingMode.Ignore;
            _tag.Add(Ux.Icon("heart"));
            _tagLabel = Ux.Text("", "gb-nowrap");
            _tagLabel.pickingMode = PickingMode.Ignore;
            _tag.Add(_tagLabel);
            var tail = Ux.El("gb-visit-tag-tail");
            tail.pickingMode = PickingMode.Ignore;
            _tag.Add(tail);
            _tag.style.display = DisplayStyle.None;
            root.Add(_tag);
            _emoteBubble = Ux.El("gb-visit-emote");
            _emoteBubble.pickingMode = PickingMode.Ignore;
            _emoteBubble.style.display = DisplayStyle.None;
            root.Add(_emoteBubble);
            // board 08 nap in the crate: the home HUD's Zzz is hidden under this screen, so the visit draws its own
            _zzzOwn = Zzz("visit-zzz-own");
            root.Add(_zzzOwn);
            _zzzPeer = Zzz("visit-zzz-peer");
            root.Add(_zzzPeer);

            // top-left: who visits whom — board 08 charcoal capsule (portrait, BESUCH tag, title, status dot + line)
            var cap = Ux.El("gb-visit-cap");
            Ux.Abs(cap, 16, 12);
            cap.Add(Ux.Portrait(64));
            var col = Ux.Col("gb-visit-cap-text");
            col.Add(Ux.Text(t.T("online.besuch.kicker"), "gb-visit-kicker"));
            _title = Ux.Text("", "gb-visit-title", "gb-nowrap");
            col.Add(_title);
            var subRow = Ux.Row("gb-visit-sub");
            _subDot = Ux.El("gb-status-dot");
            subRow.Add(_subDot);
            _sub = Ux.Text("", "gb-nowrap");
            subRow.Add(_sub);
            col.Add(subRow);
            cap.Add(col);
            root.Add(cap);

            // top-right: leave (strawberry pill with a white rim so it reads on any room)
            var end = Ux.IconTextBtn("home", t.T("online.besuch.beenden"), () => { Sfx.PlayUi("soft_back"); _online.EndVisit(); }, "gb-btn-primary", "gb-visit-end");
            end.name = "visit-end";
            Ux.Abs(end, null, 16, 16);
            root.Add(end);

            // bottom-left: joystick
            _joy = new Joystick();
            Ux.Abs(_joy, 40, null, null, 28);
            _joy.Changed += v =>
            {
                // a firm push of the stick wakes a napping guest first
                if (_host.World != null && _host.World.NappingOnCouch) { if (v.magnitude > 0.35f) WakeFromNap(); return; }
                _host.World?.SetMove(v);
            };
            root.Add(_joy);

            // guest: "Aufwecken" while our Gooby naps on the host's couch
            _wake = Ux.IconTextBtn("sun", t.T("social.nap.wake_button"), WakeFromNap, "gb-btn-primary");
            _wake.name = "visit-wake";
            Ux.Abs(_wake, null, null, 28, 150);
            _wake.style.display = DisplayStyle.None;
            root.Add(_wake);

            // bottom-right: emotes (board 08 action discs, the heart is the big one); the host may also decorate
            var emotes = Ux.Row("gb-visit-emotes");
            Ux.Abs(emotes, null, null, 28, 34);
            if (_isHost) emotes.Add(Decorate(t.T("nl.hud.aktion_dekorieren")));
            if (_isHost) emotes.Add(RideOut(t.T("nl.coop.ausfahrt")));
            emotes.Add(Selfie(t.T("online.board.emote.selfie")));
            emotes.Add(Emote("heart", "heart", t.T("online.emote.herz"), main: true));
            emotes.Add(Emote("hand", "wave", t.T("online.emote.winken")));
            emotes.Add(Emote("sparkle", "hop", t.T("online.emote.huepfen")));
            root.Add(emotes);

            // "Snap A Gooby!": hint under the capsule, the big countdown, the shutter flash
            _selfieHint = Ux.Text("", "gb-selfie-hint");
            _selfieHint.name = "selfie-hint";
            _selfieHint.pickingMode = PickingMode.Ignore;
            _selfieHint.style.position = Position.Absolute;
            _selfieHint.style.top = 104;
            _selfieHint.style.left = Length.Percent(50);
            _selfieHint.style.translate = new Translate(Length.Percent(-50), 0);
            _selfieHint.style.display = DisplayStyle.None;
            root.Add(_selfieHint);
            _selfieCount = Ux.Text("", "gb-selfie-count");
            _selfieCount.name = "selfie-count";
            _selfieCount.pickingMode = PickingMode.Ignore;
            _selfieCount.style.position = Position.Absolute;
            _selfieCount.style.top = Length.Percent(30);
            _selfieCount.style.left = Length.Percent(50);
            _selfieCount.style.translate = new Translate(Length.Percent(-50), 0);
            _selfieCount.style.display = DisplayStyle.None;
            root.Add(_selfieCount);
            _selfieFlash = new VisualElement { name = "selfie-flash", pickingMode = PickingMode.Ignore };
            Ux.Abs(_selfieFlash, 0, 0, 0, 0);
            _selfieFlash.style.backgroundColor = Color.white;
            _selfieFlash.style.opacity = 0f;
            root.Add(_selfieFlash);

            _toastLane = Ux.Col("gb-notice-lane");
            Ux.Abs(_toastLane, null, 104);
            _toastLane.style.left = Length.Percent(50);
            _toastLane.style.translate = new Translate(Length.Percent(-50), 0);
            _toastLane.pickingMode = PickingMode.Ignore;
            root.Add(_toastLane);

            RefreshTexts();
        }

        private static Label Zzz(string name)
        {
            var z = Ux.Text("z z Z", "gb-zzz");
            z.name = name;
            z.pickingMode = PickingMode.Ignore;
            z.style.position = Position.Absolute;
            z.style.display = DisplayStyle.None;
            return z;
        }

        /// <summary>Bobs a Zzz over a head (world → panel), offset in panel pixels so it clears the name tag.</summary>
        private void PlaceZzz(Label z, bool show, Vector3 head, Camera cam, float dx, float dy)
        {
            if (!show || z.panel == null || cam == null)
            {
                if (z.style.display != DisplayStyle.None) z.style.display = DisplayStyle.None;
                return;
            }
            var bob = Mathf.Sin(_zzzPhase * 1.6f) * 0.08f;
            var p = RuntimePanelUtils.CameraTransformWorldToPanel(z.panel, head + new Vector3(0.04f, bob, 0.06f), cam);
            z.style.display = DisplayStyle.Flex;
            z.style.left = p.x + dx;
            z.style.top = p.y + dy;
            z.style.opacity = 0.65f + 0.35f * Mathf.Sin(_zzzPhase * 2.4f);
        }

        private VisualElement Emote(string icon, string id, string label, bool main = false)
        {
            var a = Ux.El("gb-action");
            if (main) a.AddToClassList("gb-action--main");
            a.name = "emote-" + id;
            a.Add(Ux.Icon(icon));
            a.Add(Ux.Text(label));
            a.RegisterCallback<ClickEvent>(_ =>
            {
                Sfx.PlayUi("soft_tap");
                _host.World?.OwnEmote(id);
                _online.Client.SendVisitEmote(id);
            });
            return a;
        }

        private Label _selfieHint, _selfieCount;
        private VisualElement _selfieFlash;
        private Gooby.Core.Net.SelfieAblauf _selfie;
        private float _selfieFlashT, _selfieHintLeft, _selfieCountT;

        /// <summary>"Snap A Gooby!" (snap_a_gooby.gd): both Goobys pose, 3-2-1, click — into the gallery and as a photo letter to the friend.</summary>
        private VisualElement Selfie(string label)
        {
            var a = Ux.El("gb-action");
            a.name = "visit-selfie";
            a.Add(Ux.Icon("camera"));
            a.Add(Ux.Text(label));
            a.RegisterCallback<ClickEvent>(_ => StartSelfie());
            return a;
        }

        public bool StartSelfie()
        {
            if (_selfie != null || _host.World == null || _host.Ui.Current != this) return false;
            Sfx.PlayUi("soft_open");
            _selfie = new Gooby.Core.Net.SelfieAblauf();
            _host.World.SelfiePose(4.6f);
            _online.Client.SendVisitEmote(Gooby.Core.Net.SelfieAblauf.RelayEmote);
            _selfieHint.text = _host.Strings.T("social.selfie.hinweis");
            _selfieHint.style.display = DisplayStyle.Flex;
            _selfieHintLeft = 0f;
            Debug.Log("[selfie] pose");
            return true;
        }

        private void TickSelfie(float dt)
        {
            if (_selfieFlashT > 0f) { _selfieFlashT = Mathf.Max(0f, _selfieFlashT - dt); _selfieFlash.style.opacity = _selfieFlashT / 0.35f * 0.8f; }
            if (_selfieHintLeft > 0f && (_selfieHintLeft -= dt) <= 0f) _selfieHint.style.display = DisplayStyle.None;
            if (_selfieCountT > 0f)
            {
                _selfieCountT -= dt;
                var pop = 1f + Mathf.Max(0f, _selfieCountT - 0.75f) * 1.6f;
                _selfieCount.style.scale = new Scale(new Vector3(pop, pop, 1f));
            }
            if (_selfie == null) return;
            foreach (var e in _selfie.Takt(dt))
            {
                if (e.StartsWith("zeige_", System.StringComparison.Ordinal))
                {
                    _selfieCount.text = e.Substring(6);
                    _selfieCount.style.display = DisplayStyle.Flex;
                    _selfieCountT = 1f;
                    Sfx.PlayUi("soft_tick");
                }
                else if (e == "ausloesen") ShootSelfie();
            }
        }

        private void ShootSelfie()
        {
            var t = _host.Strings;
            _selfie = null;
            _selfieCount.style.display = DisplayStyle.None;
            var cam = _host.World?.Camera;
            var path = PhotoAlbum.Take(_host, cam, tex => PhotoAlbum.Frame(tex, "polaroid"));
            var bytes = _online.TakePhoto(out _);
            Sfx.Play("game_pop", 8f);
            Sfx.PlayUi("soft_collect");
            Haptics.Tap();
            if (!_host.Settings.ReducedMotion) { _selfieFlashT = 0.35f; _selfieFlash.style.opacity = 0.8f; }
            _host.Store.Update(s =>
            {
                var meta = Gooby.Core.Json.J.EnsureDict(s, "meta");
                meta["photosTaken"] = Gooby.Core.Json.J.Int(meta["photosTaken"]) + 1;
            });
            _host.World?.SelfieRelax();
            var name = PeerName;
            var peer = _online.Client.PeerCode;
            if (string.IsNullOrEmpty(path) && bytes == null)
            {
                SelfieHint(t.T("social.selfie.fehler"));
                return;
            }
            if (string.IsNullOrEmpty(peer) || bytes == null)
            {
                SelfieHint(t.T("social.selfie.nur_lokal"));
                return;
            }
            SelfieHint(t.T("social.selfie.hinweis"));
            _online.Client.SendPhotoMail(peer, t.T("social.selfie.mail_text"), null, null, 0, bytes, r =>
            {
                var sent = r != null && (r.Ok || r.Queued);
                SelfieHint(sent ? t.T("social.selfie.gesendet", ("name", name)) : t.T("social.selfie.nur_lokal"));
                Debug.Log("[selfie] photo " + (sent ? "sent to " + peer : "kept local (" + r?.Code + ")") + " album=" + System.IO.Path.GetFileName(path));
            });
        }

        private void SelfieHint(string text)
        {
            _selfieHint.text = text;
            _selfieHint.style.display = DisplayStyle.Flex;
            _selfieHintLeft = 3.2f;
        }

        /// <summary>Host: board-08 "Dekorieren" disc — opens the build mode for the room we stand in, the guest sees each change.</summary>
        /// <summary>Host: "Ausfahrt" — the guest is invited to ride along and the host drives into town (coop_drive.gd).</summary>
        private VisualElement RideOut(string label)
        {
            var a = Ux.El("gb-action");
            a.name = "visit-drive";
            a.Add(Ux.Icon("car"));
            a.Add(Ux.Text(label));
            a.RegisterCallback<ClickEvent>(_ =>
            {
                if (_host.Ui.Current != this || _online.CoopDrive.Active) return;
                Sfx.PlayUi("soft_tap");
                _online.StartCoopDrive();
            });
            return a;
        }

        private VisualElement Decorate(string label)
        {
            var a = Ux.El("gb-action");
            a.name = "visit-decorate";
            a.Add(Ux.Icon("toolbox"));
            a.Add(Ux.Text(label));
            a.RegisterCallback<ClickEvent>(_ =>
            {
                if (_host.World == null || !_online.CanHostBuild || _host.Ui.Current != this) return;
                Sfx.PlayUi("soft_tap");
                _online.BeginHostBuild();
                _host.Ui.Push(new BuildModeScreen(_host, _host.World.RoomId));
            });
            return a;
        }

        public override void OnShown()
        {
            if (_host.World != null)
            {
                _host.World.DoorReached -= OnDoor; _host.World.DoorReached += OnDoor;
                _host.World.RoomChanged -= OnRoom; _host.World.RoomChanged += OnRoom;
                _host.World.BuildDeltaApplied -= OnBuildDelta; _host.World.BuildDeltaApplied += OnBuildDelta;
            }
            RefreshTexts();
        }

        public override void OnHidden()
        {
            if (_host.World == null) return;
            _host.World.DoorReached -= OnDoor;
            _host.World.RoomChanged -= OnRoom;
            _host.World.BuildDeltaApplied -= OnBuildDelta;
            _host.World.SetMove(Vector2.zero);
        }

        /// <summary>Guest: the host's build warning or a delta arrived — the capsule says so for a while.</summary>
        public void OnPeerBuilding()
        {
            if (_isHost) return;
            _peerBuildingLeft = PeerBuildingHold;
            RefreshTexts();
        }

        private void OnBuildDelta(string roomId, bool teleported)
        {
            if (!teleported) return;
            Toast(_host.Strings.T("nl.besuch.bau_tuer", ("name", PeerName)), "toolbox");
            SendPose(force: true);
        }

        private string PeerName
        {
            get
            {
                var c = _online.Client;
                return c.PeerName.Length > 0 ? c.PeerName : c.FindFriend(c.PeerCode)?.Name ?? c.PeerCode;
            }
        }

        private void OnDoor(RoomDefs.Door door)
        {
            if (_host.Ui.Fading) return;
            _host.Ui.FadeThrough(() => _host.World.PassDoor(door));
        }

        private void OnRoom(string roomId)
        {
            var def = RoomDefs.Get(roomId);
            Toast(_host.Strings.T(def?.NameKey ?? "home.raum." + roomId), "house");
            SendPose(force: true);
            RefreshTexts();
        }

        public void OnPeerJoined(string name)
        {
            Toast(_host.Strings.T("online.besuch.angekommen", ("name", name)), "heart");
            SendPose(force: true);
            RefreshTexts();
        }

        public void OnPeerLeft()
        {
            Toast(_host.Strings.T("online.besuch.gegangen", ("name", _online.Client.PeerName)), "house");
            RefreshTexts();
        }

        public void OnPeerEmote(string id)
        {
            _emoteBubble.Clear();
            _emoteBubble.Add(Ux.Icon(id == "heart" ? "heart" : id == "wave" ? "hand" : id == Gooby.Core.Net.SelfieAblauf.RelayEmote ? "camera" : "sparkle"));
            if (id == Gooby.Core.Net.SelfieAblauf.RelayEmote) Toast(_host.Strings.T("social.selfie.emote"), "camera");
            _emoteBubbleLeft = 1.6f;
            Sfx.PlayUi(id == "heart" ? "soft_sticker" : "soft_tap");
        }

        private void RefreshTexts()
        {
            if (_title == null) return;
            var t = _host.Strings;
            var c = _online.Client;
            var peer = PeerName;
            var gooby = c.PeerGoobyName.Length > 0 ? c.PeerGoobyName : "Gooby";
            _title.text = _isHost ? t.T("online.besuch.titel_host", ("name", peer)) : t.T("online.besuch.titel_gast", ("name", peer));
            var here = c.PeerInRoom && _host.World != null && _host.World.Peer != null;
            if (!c.PeerInRoom) _sub.text = t.T("online.besuch.peer_wartet", ("name", peer));
            else if (_peerBuildingLeft > 0f) _sub.text = t.T("nl.besuch.dekoriert", ("name", peer));
            else if (here && _host.World.PeerNapping) _sub.text = t.T("online.besuch.peer_nickerchen", ("gooby", gooby));
            else if (here) _sub.text = t.T("online.besuch.peer_hier", ("gooby", gooby));
            else _sub.text = t.T("online.besuch.peer_weg", ("gooby", gooby));
            _subDot.EnableInClassList("gb-status-dot--on", here);
            _subDot.EnableInClassList("gb-status-dot--wait", !here);
            _tagLabel.text = peer;
        }

        private void SendPose(bool force)
        {
            var w = _host.World;
            if (w == null) return;
            var (x, z, anim) = w.OwnPose;
            // the couch rule: both send their Gooby's energy, only the host its hour (its evening counts)
            var energy = Gooby.Core.Json.J.Num(Gooby.Core.Json.J.Path(_host.Store.State, "gooby", "stats", "energy"));
            _online.Client.SendVisitPos(x, z, anim, w.RoomId, force, energy, _isHost ? (int)w.LocalHour : -1);
        }

        // ── the couch rule (couch_logic.gd): late at the host's and both worn out → the guest's Gooby naps on the couch ──

        private VisualElement _wake;
        private bool _napArmed = true;
        private float _napCheckIn = 2f;

        private void TickCouchRule(float dt)
        {
            if (_isHost || _host.World == null || (_napCheckIn -= dt) > 0f) return;
            _napCheckIn = 2f;
            var w = _host.World;
            var energy = Gooby.Core.Json.J.Num(Gooby.Core.Json.J.Path(_host.Store.State, "gooby", "stats", "energy"));
            var soll = Gooby.Core.Net.CouchLogic.SollSchlafen(_online.Client.PeerHour, energy, _online.Client.PeerEnergy);
            // the rule off again re-arms it (no nap loop after waking while evening and tiredness last)
            if (!soll) { _napArmed = true; return; }
            if (!_napArmed || w.NappingOnCouch || _selfie != null || !_online.Client.PeerInRoom) return;
            _napArmed = false;
            var (cell, floor) = w.StartCouchNap();
            _online.Client.SendVisitNap(true, cell, floor);
            Toast(_host.Strings.T(floor ? "social.nap.guest_bubble_boden" : "social.nap.guest_bubble"), "moon");
            _wake.style.display = DisplayStyle.Flex;
            Debug.Log("[visit] couch nap " + (floor ? "on the floor" : "on the couch " + cell.X + "," + cell.Y));
        }

        /// <summary>Guest: "Aufwecken" (or the stick) — he gets up, the host hears NAP off.</summary>
        public void WakeFromNap()
        {
            var w = _host.World;
            if (w == null || !w.NappingOnCouch) return;
            w.EndCouchNap();
            _online.Client.SendVisitNap(false, new Cell(0, 0), false);
            _wake.style.display = DisplayStyle.None;
            Sfx.PlayUi("soft_toggle");
            Toast(_host.Strings.T("social.nap.wake_toast_guest"), "sun");
            Debug.Log("[visit] couch nap woken");
        }

        /// <summary>Host: the visiting Gooby fell asleep on our couch (or the floor), or woke up.</summary>
        public void OnPeerNap(bool on, bool floor)
        {
            Debug.Log("[visit] peer nap " + (on ? floor ? "on the floor" : "on our couch" : "over"));
            Toast(_host.Strings.T(!on ? "social.nap.host_wake" : floor ? "social.nap.host_bubble_boden" : "social.nap.host_bubble"), on ? "moon" : "sun");
            RefreshTexts();
        }

        private float _textAccum;

        public override void Update()
        {
            var w = _host.World;
            if (w == null) return;
            if (!_online.Client.VisitActive) { _online.EndVisitLocally(true); return; }
            var force = w.RoomId != _lastRoom;
            _lastRoom = w.RoomId;
            SendPose(force);
            TickSelfie(Time.unscaledDeltaTime);
            TickCouchRule(Time.unscaledDeltaTime);
            if (_peerBuildingLeft > 0f) _peerBuildingLeft -= Time.unscaledDeltaTime;
            _textAccum += Time.unscaledDeltaTime;
            if (_textAccum > 0.5f) { _textAccum = 0f; RefreshTexts(); }
            _zzzPhase += Time.unscaledDeltaTime;
            var current = _host.Ui.Current == this;
            PlaceZzz(_zzzOwn, current && w.CrateNapping, w.OwnHead, w.Camera, 0f, -40f);

            var peer = w.Peer;
            if (peer == null || w.Camera == null || _tag.panel == null || !current)
            {
                _tag.style.display = DisplayStyle.None;
                _emoteBubble.style.display = DisplayStyle.None;
                PlaceZzz(_zzzPeer, false, Vector3.zero, null, 0f, 0f);
                return;
            }
            var p = RuntimePanelUtils.CameraTransformWorldToPanel(_tag.panel, w.PeerHead, w.Camera);
            _tag.style.display = DisplayStyle.Flex;
            _tag.style.left = p.x; // .gb-visit-tag centres itself (translate -50 %), so long names stay on the head
            _tag.style.top = p.y - 54f;
            PlaceZzz(_zzzPeer, w.PeerNapping, w.PeerHead + Vector3.down * 0.08f, w.Camera, 64f, -96f);
            if (_emoteBubbleLeft > 0f)
            {
                _emoteBubbleLeft -= Time.unscaledDeltaTime;
                _emoteBubble.style.display = DisplayStyle.Flex;
                _emoteBubble.style.left = p.x + 40f;
                _emoteBubble.style.top = p.y - 124f - (1.6f - _emoteBubbleLeft) * 24f;
                _emoteBubble.style.opacity = Mathf.Clamp01(_emoteBubbleLeft * 2f);
            }
            else _emoteBubble.style.display = DisplayStyle.None;
        }

        private void Toast(string text, string icon)
        {
            var toast = UiRoot.BuildNotice(text, icon);
            _toastLane.Add(toast);
            UiRoot.AnimateNotice(toast, 2.6f);
        }
    }
}
