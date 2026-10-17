using System;
using System.Collections.Generic;
using System.IO;
using Gooby.Core.City;
using Gooby.Core.Cosmetics;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.Minigames.Ranch;
using Gooby.Core.Minigames.Ranch.Mp;
using Gooby.Core.Net;
using Gooby.Unity.UI;
using Gooby.Unity.UI.Screens;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Gooby.Unity.App
{
    /// <summary>
    /// The game's side of multiplayer: owns the engine-free <see cref="SocialClient"/>, feeds it the server address the
    /// player typed in (Einstellungen → Konto → Mehrspieler, or <c>-gooby-server host:port</c> for QA), pumps it every
    /// frame and turns its events into game effects — coins of a GoobyPal gift, gifts unpacked from letters, postage,
    /// presence from the current screen, toasts and the visit prompt on every screen, and the house visit itself
    /// (guest stages the host's snapshot, host waits in the living room).
    /// </summary>
    public sealed class OnlineService
    {
        /// <summary>Postage per letter (mail_sheet.gd PORTO — Frau Zettel stamps every letter).</summary>
        public const int Porto = 5;

        public SocialClient Client { get; }

        /// <summary>Schiffe versenken with friends (BOARD_* relay of boardgames.js) on the same session.</summary>
        public BoardClient Board { get; }

        /// <summary>GOB NOM's online coop (gobnommp.js: invites, level vote, lockstep relay) on the same session.</summary>
        public GobnomNetClient Gobnom { get; }

        /// <summary>Goobys vs Zombies online PvP (gvzmp.js: challenges, start handshake, lockstep relay, coin reward).</summary>
        public GvzNetClient Gvz { get; }

        /// <summary>Ranch multiplayer rounds (ranchmp.js: Wettrennen, Parcours-Duell, Fangen with 2–4 friends).</summary>
        public RanchMpClient RanchMp { get; }

        /// <summary>Raised once per frame when anything online changed (lists, status, unread count).</summary>
        public event Action Changed;

        /// <summary>Decoded photos of letters and InstantGooby posts by photoId.</summary>
        public PhotoTextureCache Photos { get; } = new PhotoTextureCache();

        private readonly GameHost _host;
        private readonly string _scratchDir;
        private string _cliServer, _cliName, _cliSecret;
        private float _presenceAccum = 10f;
        private VisitScreen _visitScreen;
        private Action _closeVisitPrompt;
        /// <summary>The open table: <see cref="BattleshipScreen"/> or <see cref="ChessScreen"/> (by <see cref="BoardClient.Game"/>).</summary>
        private UiScreen _boardScreen;
        private readonly Dictionary<string, Action> _boardPrompts = new Dictionary<string, Action>();
        private readonly Dictionary<string, Action> _gobnomPrompts = new Dictionary<string, Action>();
        private readonly Dictionary<string, Action> _gvzPrompts = new Dictionary<string, Action>();
        private readonly Dictionary<string, Action> _ranchPrompts = new Dictionary<string, Action>();
        private bool _boardInviteOut;

        public OnlineService(GameHost host)
        {
            _host = host;
            NetLog.OnWarning = m => Debug.LogWarning(m);
            NetLog.OnError = m => Debug.LogError(m);
            var dir = Path.Combine(host.DataDir, "net");
            _scratchDir = Path.Combine(dir, "visit");
            Client = SocialClient.CreateDefault(dir);
            Client.AppVersion = "unity-" + Application.version;
            Board = new BoardClient(Client);
            Gobnom = new GobnomNetClient(Client);
            Gvz = new GvzNetClient(Client);
            RanchMp = new RanchMpClient(Client);
            CoopDrive = new CoopDriveClient(Client);
            ReadCommandLine();
            Wire();
            WireBoard();
            WireGobnom();
            WireGvz();
            WireRanch();
            WireCoopDrive();
        }

        /// <summary>The ride along through town during a house visit (coop_drive.gd).</summary>
        public CoopDriveClient CoopDrive { get; }

        private System.Action _coopPrompt;

        /// <summary>
        /// The host's "Ausfahrt": the invitation goes over the visit relay, then he drives off; the guest gets a question
        /// card and rides along on the passenger screen. The radio follows whoever presses; a passenger whose driver
        /// stops is back in the visited house, a driver whose passenger gets out drives on.
        /// </summary>
        public void StartCoopDrive()
        {
            var t = _host.Strings;
            if (!Client.VisitActive || Client.VisitRole != VisitRole.Host) return;
            CoopDrive.StartAsDriver(true, PlayerName, (ok, code) =>
            {
                Debug.Log("[coop] driver start ok=" + ok + " code=" + code + " room=" + CoopDrive.RoomId);
                if (!ok) { _host.Ui.Toast(t.T("nl.coop.geht_nicht"), "car"); return; }
                var guest = Client.FindFriend(Client.PeerCode);
                _host.Ui.Toast(t.T("nl.coop.warte", ("name", guest != null ? guest.Name : "")), "car", 4f);
                _host.Ui.Push(new CityDriveScreen(_host));
            });
        }

        private void WireCoopDrive()
        {
            var t = _host.Strings;
            CoopDrive.InviteReceived += inv =>
            {
                Debug.Log("[coop] invite from " + inv.Von + " room=" + inv.Room);
                _coopPrompt?.Invoke();
                Sfx.PlayUi("soft_toast");
                Haptics.Tap();
                _coopPrompt = _host.Ui.Prompt("car", t.T("nl.coop.einladung_titel", ("name", inv.Von)), t.T("nl.coop.einladung_text", ("name", inv.Von)),
                    t.T("nl.coop.mitfahren"), () => { _coopPrompt = null; AcceptCoopDrive(); },
                    t.T("nl.coop.nein"), () => { _coopPrompt = null; CoopDrive.Decline(); }, 28f);
            };
            CoopDrive.InviteDeclined += () =>
            {
                var guest = Client.FindFriend(Client.PeerCode);
                _host.Ui.Toast(t.T("nl.coop.abgelehnt", ("name", guest != null ? guest.Name : "")), "car");
            };
            CoopDrive.PeerJoined += name =>
            {
                Debug.Log("[coop] passenger in: " + name);
                if (CoopDrive.Role == CoopDriveClient.RoleDriver) _host.Ui.Toast(t.T("nl.coop.eingestiegen", ("name", name)), "car");
            };
            CoopDrive.PeerLeft += () =>
            {
                Debug.Log("[coop] passenger out");
                _host.Ui.Toast(t.T("nl.coop.ausgestiegen", ("name", CoopDrive.PartnerName)), "car");
            };
            CoopDrive.RadioSet += (station, offset) =>
            {
                Debug.Log("[coop] radio " + (station.Length > 0 ? station : "aus") + " offset=" + offset.ToString("0.0"));
                if (_host.Audio == null) return;
                if (station.Length == 0) _host.Audio.RadioOff();
                else _host.Audio.PlayRadio(station, 99);
            };
            CoopDrive.Ended += reason =>
            {
                Debug.Log("[coop] ended " + reason);
                if (_host.Ui.Current is RideAlongScreen ride) ride.Close(reason);
            };
        }

        public void AcceptCoopDrive()
        {
            var t = _host.Strings;
            CoopDrive.Accept((ok, code) =>
            {
                Debug.Log("[coop] passenger accept ok=" + ok + " code=" + code);
                if (!ok) { _host.Ui.Toast(t.T("nl.coop.geht_nicht"), "car"); return; }
                _host.Ui.FadeThrough(() => _host.Ui.Push(new RideAlongScreen(_host)));
            });
        }

        // ── configuration ───────────────────────────────────────────────────

        /// <summary>QA: <c>-gooby-server host:port</c> (not persisted), <c>-gooby-name Lena</c>, <c>-gooby-secret s</c>.</summary>
        private void ReadCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-gooby-server") _cliServer = args[i + 1];
                if (args[i] == "-gooby-name") _cliName = args[i + 1];
                if (args[i] == "-gooby-secret") _cliSecret = args[i + 1];
            }
            if (!string.IsNullOrEmpty(_cliName) && string.IsNullOrEmpty(PlayerNameRaw))
                SetPlayerName(_cliName, apply: false);
        }

        /// <summary>The address in use: the QA override, else the saved setting.</summary>
        public string ServerAddress => !string.IsNullOrEmpty(_cliServer) ? _cliServer : _host.Settings.ServerAddress;

        private string Secret => _cliSecret ?? _host.Settings.ServerSecret;

        private string PlayerNameRaw => J.Str(J.Path(_host.Store.State, "meta", "playerName"));

        /// <summary>Name friends see (meta.playerName; a friendly fallback until the player picks one).</summary>
        public string PlayerName
        {
            get
            {
                var n = PlayerNameRaw.Trim();
                return n.Length > 0 ? n : _host.Strings.T("online.name_fallback", ("gooby", GoobyName));
            }
        }

        public string GoobyName => J.Str(J.Path(_host.Store.State, "meta", "goobyNickname"), "Gooby");

        public void SetPlayerName(string name, bool apply = true)
        {
            var clean = (name ?? "").Trim();
            if (clean.Length > 24) clean = clean.Substring(0, 24);
            _host.Store.Update(s => J.EnsureDict(s, "meta")["playerName"] = clean);
            if (apply) Apply(connect: false);
        }

        /// <summary>Configures the client from the settings; connects when an address is set and auto-connect is on (or <paramref name="connect"/>).</summary>
        public void Apply(bool connect)
        {
            var ep = NetAddress.Parse(ServerAddress);
            Client.Configure(ep, PlayerName, GoobyName, Secret);
            if (!ep.Valid) { Client.Disconnect(); return; }
            if (connect || _host.Settings.OnlineAuto || !string.IsNullOrEmpty(_cliServer)) Client.Connect();
        }

        /// <summary>Saves a new server (settings) and connects to it right away.</summary>
        public void ConnectTo(string address, string secret)
        {
            _cliServer = null; // the player's choice wins over a QA override
            _host.Settings.SetServer(address, secret);
            Apply(connect: true);
        }

        public void GoOffline() => Client.Disconnect();

        // ── per frame ───────────────────────────────────────────────────────

        public void Update(float dt)
        {
            Client.Pump();
            // our board invite ran out (30 s, no answer): the friends list re-enables its "Spielen" pill
            var inviteOut = Board.OutgoingInvite.Length > 0;
            if (inviteOut != _boardInviteOut)
            {
                _boardInviteOut = inviteOut;
                Changed?.Invoke();
            }
            _presenceAccum += dt;
            if (_presenceAccum >= 1.5f)
            {
                _presenceAccum = 0f;
                if (Client.IsOnline) Client.SetPresence(CurrentPresence());
            }
            if (_houseDirty && Time.unscaledTime >= _houseRetryAt && _host.World != null && _host.World.Build == null && Client.IsOnline) UploadBuiltHouse();
        }

        public void OnPause(bool paused)
        {
            // the socket usually dies in the background; a fresh connect on resume beats a 60 s heartbeat timeout
            if (!paused && Client.Endpoint != null && Client.Endpoint.Valid && Client.Status == NetStatus.Offline && (_host.Settings.OnlineAuto || !string.IsNullOrEmpty(_cliServer)))
                Client.Connect();
        }

        /// <summary>Presence kind from what is on screen (presence.gd kind_for_route).</summary>
        private string CurrentPresence()
        {
            var w = _host.World;
            var cur = _host.Ui.Current;
            // no chess kind on the server (presence.js): a chess table reports "board" like Schiffe versenken
            if (Board.HasGame && _boardScreen != null && _host.Ui.IsOpen(_boardScreen)) return PresenceKinds.Board;
            if (cur is ChessScreen practice && practice.IsPractice) return PresenceKinds.Minigame("Schach");
            if (w != null && (w.Visiting || w.Hosting)) return PresenceKinds.Visit;
            if (cur is RanchMpScreen) return PresenceKinds.ForRanch(RanchMp.Mode, RanchMp.Host == RanchMp.MyCode);
            if (Travel.IstWeg(_host.Store)) return PresenceKinds.Vacation;
            if (cur is MinigameHostScreen && cur.Id.StartsWith("minigame-")) return PresenceKinds.Minigame(cur.Id.Substring("minigame-".Length));
            if (cur is CityDriveScreen) return PresenceKinds.Drive;
            if (w != null && w.Interior != null) return PresenceKinds.City;
            if (w != null && w.InGarden) return PresenceKinds.Garden;
            if (Sleep.IsSleeping(Sleep.FlatOf(_host.Store.State))) return PresenceKinds.Sleep;
            return PresenceKinds.Home;
        }

        // ── events → game ───────────────────────────────────────────────────

        private void Wire()
        {
            var t = _host.Strings;
            Client.Changed += () => Changed?.Invoke();
            Client.StatusChanged += s =>
            {
                Changed?.Invoke();
                Debug.Log("[online] status " + s + (Client.LastErrorCode.Length > 0 ? " (" + Client.LastErrorCode + ")" : "") + " server=" + Client.Endpoint?.Display);
                if (s == NetStatus.Online)
                {
                    Client.SyncCoins(ShopLogic.Coins(_host.Store));
                    _presenceAccum = 10f;
                }
            };
            _host.Store.CoinsChanged += c => Client.SyncCoins(c);
            Client.FriendRequestIncoming += r =>
            {
                Sfx.PlayUi("soft_toast");
                _host.Ui.Toast(t.T("online.toast.anfrage", ("name", r.Name)), "friends");
            };
            Client.FriendAdded += f => _host.Ui.Toast(t.T("online.toast.freund_neu", ("name", f.Name)), "heart");
            Client.FriendOnlineChanged += f =>
            {
                if (f.Online) _host.Ui.Toast(t.T("online.toast.freund_online", ("name", f.Name), ("gooby", f.GoobyName)), "friends", 2.4f);
            };
            Client.MailArrived += m =>
            {
                Sfx.PlayUi("soft_open");
                Haptics.Tap();
                _host.Ui.Toast(t.T("online.toast.brief", ("name", m.FromName)), "mail");
            };
            Client.MailBounced += (to, item, code) =>
            {
                Refund(item);
                var f = Client.FindFriend(to);
                _host.Ui.Toast(t.T("online.toast.zurueck", ("name", f != null ? f.Name : to), ("grund", ErrorText(code))), "mail", 4f);
            };
            Client.PalReceived += (id, from, amount) =>
            {
                _host.Store.Update(s => Economy.Award(J.EnsureDict(s, "economy"), amount, "goobypal"));
                var f = Client.FindFriend(from);
                Sfx.PlayUi("soft_coins");
                _host.Ui.Toast(t.T("online.toast.pal", ("name", f != null ? f.Name : from), ("n", amount)), "coin");
            };
            Client.InstantArrived += p =>
            {
                Sfx.PlayUi("soft_toast");
                _host.Ui.Toast(t.T("online.instant.toast.neu", ("name", p.FromName.Length > 0 ? p.FromName : p.From)), "camera");
            };
            Client.InstantLiked += (id, by, byName, likes) =>
            {
                Sfx.PlayUi("soft_collect");
                _host.Ui.Toast(t.T("online.instant.toast.like", ("name", byName.Length > 0 ? byName : by), ("n", likes)), "carrot");
            };
            Client.ServerEvent += e => _host.Ui.Toast(t.T("net.server_event.toast", ("name", J.Str(e["type"], "?"))), "sparkle");
            Client.GoingDown += reason =>
            {
                if (reason == NetErrorCodes.Replaced) _host.Ui.Toast(t.T("online.toast.ersetzt"), "user", 4f);
                else if (reason == NetErrorCodes.Shutdown) _host.Ui.Toast(t.T("online.toast.server_pause"), "moon", 4f);
                // the device that redeemed the move reconnects with the adopted identity; only the old one says goodbye
                else if (reason == NetErrorCodes.Moved && !(_host.Ui.Current is UmzugScreen)) _host.Ui.Toast(t.T("nl.umzug.weg"), "user", 5f);
            };

            // visits
            Client.VisitIncoming += (from, name, gooby) =>
            {
                Sfx.PlayUi("soft_toast");
                _closeVisitPrompt?.Invoke();
                _closeVisitPrompt = _host.Ui.Prompt("house", t.T("online.besuch.frage_titel"), t.T("online.besuch.frage_text", ("name", name), ("gooby", gooby)),
                    t.T("online.besuch.reinlassen"), () => AcceptVisit(from), t.T("online.besuch.spaeter"), () => Client.DeclineVisit(from, null));
            };
            Client.VisitDenied += from =>
            {
                var f = Client.FindFriend(from);
                _host.Ui.Toast(t.T("online.besuch.abgelehnt", ("name", f != null ? f.Name : from)), "house");
            };
            Client.VisitReady += (room, hostCode, guest, rev) => OnVisitReady(hostCode);
            Client.VisitEnded += (room, by) => EndVisitLocally(true);
            Client.PeerPos += (x, z, anim, roomId) => _host.World?.SetPeerPose(x, z, anim, roomId);
            Client.PeerEmote += id => { _host.World?.PeerEmote(id); _visitScreen?.OnPeerEmote(id); };
            Client.PeerNap += (on, boden) => { _host.World?.SetPeerCouchNap(on && !boden); _visitScreen?.OnPeerNap(on, boden); };
            Client.PeerJoined += (code, name, gooby) =>
            {
                SendLook();
                _visitScreen?.OnPeerJoined(name);
            };
            Client.PeerLeft += code => _visitScreen?.OnPeerLeft();
            Client.RoomMessage += (kind, body) =>
            {
                switch (kind)
                {
                    case "LOOK":
                        if (!(body?["equipped"] is JObject eq)) return;
                        var map = new Dictionary<string, string>();
                        foreach (var p in eq.Properties()) map[p.Name] = J.Str(p.Value);
                        _host.World?.SetPeerLook(map);
                        break;
                    case VisitBuild.KindStart:
                        if (Client.VisitRole != VisitRole.Guest) return;
                        Debug.Log("[visit] build warning from the host");
                        _host.Ui.Toast(t.T("social.visit.build_warning"), "toolbox", 4f);
                        _visitScreen?.OnPeerBuilding();
                        break;
                    case VisitBuild.KindDelta:
                        if (Client.VisitRole != VisitRole.Guest || _host.World == null || !_host.World.Visiting) return;
                        _host.World.ApplyVisitDelta(body);
                        _visitScreen?.OnPeerBuilding();
                        break;
                }
            };
        }

        // ── letters ─────────────────────────────────────────────────────────

        /// <summary>
        /// Sends a letter: postage and the gift leave the inventory BEFORE the request (mail_sheet.gd, transactional),
        /// a definitive failure books both back. Offline the letter waits in the outbox and still counts as sent.
        /// </summary>
        public void SendLetter(string to, string text, string giftTyp, string giftId, int menge, Action<MailSendResult, string> done) =>
            SendLetter(to, text, giftTyp, giftId, menge, null, done);

        /// <summary>
        /// A letter with an optional photo (REST /api/mail with photoB64). Offline the photo stays home: the letter waits
        /// in the outbox as text (<see cref="MailSendResult.PhotoDropped"/>), a photo-only letter fails and is refunded.
        /// </summary>
        public void SendLetter(string to, string text, string giftTyp, string giftId, int menge, byte[] photo, Action<MailSendResult, string> done)
        {
            var store = _host.Store;
            var hasGift = !string.IsNullOrEmpty(giftTyp) && !string.IsNullOrEmpty(giftId) && menge > 0;
            if (!Economy.CanAfford(J.Dict(store.State["economy"]), Porto)) { done(null, "NO_PORTO"); return; }
            if (hasGift && InventoryCount(giftTyp, giftId) < menge) { done(null, "NO_ITEM"); return; }
            store.Update(s =>
            {
                Economy.Spend(J.EnsureDict(s, "economy"), Porto, "mailPorto");
                if (hasGift) AddInventory(s, giftTyp, giftId, -menge);
            });
            if (hasGift) store.NotifySliceChanged("inventory");
            Client.SendPhotoMail(to, text, hasGift ? giftTyp : null, hasGift ? giftId : null, hasGift ? menge : 0, photo, r =>
            {
                if (!r.Ok && !r.Queued) Refund(hasGift ? new JObject { ["typ"] = giftTyp, ["id"] = giftId, ["menge"] = menge } : null);
                if (r.Ok || r.Queued) Sfx.PlayUi("soft_confirm");
                done(r, r.Ok || r.Queued ? "" : r.Code);
            });
        }

        // ── photos + InstantGooby ───────────────────────────────────────────

        /// <summary>
        /// Snapshot of the game view (world camera → 320×240 JPEG, UI not in it). Headless QA players get the generated
        /// placeholder picture (<paramref name="placeholder"/> = true).
        /// </summary>
        public byte[] TakePhoto(out bool placeholder)
        {
            var seed = (Client.FriendCode + PlayerName).GetHashCode() ^ Time.frameCount;
            var bytes = PhotoCapture.Capture(_host.World != null ? _host.World.Camera : null, seed, out placeholder);
            Sfx.PlayUi("soft_confirm");
            return bytes;
        }

        /// <summary>Texture of a letter photo (<paramref name="instant"/> false) or feed photo; cached. done(tex|null, code).</summary>
        public void LoadPhoto(bool instant, string photoId, Action<Texture2D, string> done)
        {
            if (string.IsNullOrEmpty(photoId)) { done(null, "NOT_FOUND"); return; }
            var cached = Photos.Get(photoId);
            if (cached != null) { done(cached, ""); return; }
            void Loaded(bool ok, byte[] bytes, string code)
            {
                if (!ok) { done(null, code); return; }
                var tex = Photos.Get(photoId) ?? PhotoCapture.ToTexture(bytes);
                if (tex == null) { done(null, NetPhoto.BadPhoto); return; }
                Photos.Put(photoId, tex);
                done(tex, "");
            }
            if (instant) Client.FetchInstantPhoto(photoId, Loaded);
            else Client.FetchMailPhoto(photoId, Loaded);
        }

        /// <summary>Posts a snapshot to all friends' InstantGooby feeds (online only, shares the daily letter quota).</summary>
        public void PostInstant(string caption, byte[] photo, Action<InstantPostResult> done)
        {
            Client.PostInstant((caption ?? "").Trim(), photo, r =>
            {
                Sfx.PlayUi(r.Ok ? "soft_confirm" : "soft_error");
                Debug.Log("[instant] post " + r);
                done(r);
            });
        }

        // ── arcade friends leaderboard (arcade.js) ──────────────────────────

        /// <summary>
        /// A finished arcade round (MinigameHostScreen.BookRound): POST /api/arcade/score, then the friends leaderboard
        /// of that game for the results card. Offline nothing is sent or queued and <paramref name="board"/> gets null.
        /// </summary>
        public void SubmitArcadeRound(string game, int score, int stars, Action<ArcadeBoard> board)
        {
            if (!Client.IsOnline)
            {
                board?.Invoke(null);
                return;
            }
            Client.SubmitArcadeScore(game, score, stars, r =>
            {
                Debug.Log("[arcade] score " + game + " " + score + "★" + stars + " → " + r);
                if (r.Code == NetErrorCodes.Offline || r.Code == "BAD_GAME")
                {
                    board?.Invoke(null);
                    return;
                }
                // a refused submit (rate limit) still leaves a board worth showing
                Client.FetchArcadeLeaderboard(game, b =>
                {
                    Debug.Log("[arcade] board " + b);
                    board?.Invoke(b.Ok ? b : null);
                });
            });
        }

        /// <summary>Refreshes <see cref="SocialClient.ArcadeBest"/> for the arcade cards (online only); done(true) when it changed.</summary>
        public void RefreshArcadeBest(Action<bool> done)
        {
            if (!Client.IsOnline)
            {
                done?.Invoke(false);
                return;
            }
            Client.FetchArcadeBest(set => done?.Invoke(set.Ok));
        }

        /// <summary>The friend holding the top score of <paramref name="game"/> (above mine), or null.</summary>
        public ArcadeGameBest ArcadeFriendTop(string game)
        {
            var g = Client.ArcadeBest.Find(game);
            return g != null && g.FriendLeads ? g : null;
        }

        /// <summary>Unpacks the gift of a letter: only after the server's OK does it land in the inventory (MAIL_CLAIM is one-shot).</summary>
        public void ClaimGift(MailInfo m, Action<bool, string> done)
        {
            if (m == null || !m.HasItem || m.Claimed) { done(false, "ALREADY_CLAIMED"); return; }
            Client.ClaimMail(m.Id, r =>
            {
                if (!r.Ok) { done(false, r.Code); return; }
                var item = r.Data["item"] as JObject;
                var typ = J.Str(item?["typ"], m.ItemTyp);
                var id = J.Str(item?["id"], m.ItemId);
                var n = Math.Max(1, J.Int(item?["menge"], m.ItemMenge));
                _host.Store.Update(s => AddInventory(s, typ, id, n));
                _host.Store.NotifySliceChanged("inventory");
                Sfx.PlayUi("soft_collect");
                done(true, "");
            });
        }

        private void Refund(JObject item)
        {
            _host.Store.Update(s =>
            {
                Economy.Award(J.EnsureDict(s, "economy"), Porto, "mailPorto");
                if (item != null) AddInventory(s, J.Str(item["typ"]), J.Str(item["id"]), Math.Max(1, J.Int(item["menge"], 1)));
            });
            if (item != null) _host.Store.NotifySliceChanged("inventory");
        }

        public int InventoryCount(string typ, string id) => (int)J.Num(J.Path(_host.Store.State, "inventory", typ == "food" ? "food" : "items", id));

        private static void AddInventory(JObject s, string typ, string id, int delta)
        {
            if (string.IsNullOrEmpty(id)) return;
            var inv = J.EnsureDict(s, "inventory");
            var bag = J.EnsureDict(inv, typ == "food" ? "food" : "items");
            var n = Math.Max(0, (int)J.Num(bag[id]) + delta);
            if (n == 0 && typ == "food") bag.Remove(id);
            else bag[id] = n;
        }

        /// <summary>What can travel in an envelope: food from the fridge plus medicine/fertilizer (server: typ food|items).</summary>
        public List<(string typ, string id, int count)> GiftChoices()
        {
            var list = new List<(string, string, int)>();
            foreach (var e in FoodCatalog.InventoryEntries(_host.Store.State)) list.Add(("food", e.Id, e.Count));
            foreach (var id in new[] { "medicine", "fertilizer" })
            {
                var n = InventoryCount("items", id);
                if (n > 0) list.Add(("items", id, n));
            }
            return list;
        }

        // ── coins between friends ───────────────────────────────────────────

        /// <summary>GoobyPal: the server books the gift, then the coins leave our purse (client stays authoritative).</summary>
        public void GiftCoins(string to, int amount, Action<bool, string> done)
        {
            if (!Economy.CanAfford(J.Dict(_host.Store.State["economy"]), amount)) { done(false, "NO_COINS"); return; }
            Client.SendPal(to, amount, r =>
            {
                if (r.Ok) _host.Store.Update(s => Economy.Spend(J.EnsureDict(s, "economy"), amount, "goobypal"));
                done(r.Ok, r.Code);
            });
        }

        // ── visits ──────────────────────────────────────────────────────────

        /// <summary>Our house for the guest: the home slice's rooms in the Godot layout format (visit_snapshot.gd).</summary>
        public JObject BuildHouseSnapshot()
        {
            var rooms = new JObject();
            if (J.Path(_host.Store.State, "home", "rooms") is JObject own)
                foreach (var p in own.Properties())
                {
                    var room = new JObject { ["items"] = ((p.Value as JObject)?["items"] as JArray ?? new JArray()).DeepClone() };
                    // additive: the garlands under the ceiling come along (an older guest just ignores the key)
                    if ((p.Value as JObject)?["girlanden"] is JArray girlanden && girlanden.Count > 0) room["girlanden"] = girlanden.DeepClone();
                    rooms[p.Name] = room;
                }
            return new JObject { ["v"] = 1, ["goobyName"] = GoobyName, ["rooms"] = rooms };
        }

        public void RequestVisit(FriendInfo f, Action<bool, string> done)
        {
            if (_host.World != null && (_host.World.Visiting || _host.World.Hosting)) { done(false, "BUSY"); return; }
            Client.RequestVisit(f.Code, r =>
            {
                if (r.Ok) _host.Ui.Toast(_host.Strings.T("online.besuch.angefragt", ("name", f.Name)), "house");
                done(r.Ok, r.Code);
            });
        }

        private void AcceptVisit(string guest)
        {
            _closeVisitPrompt = null;
            Client.AcceptVisit(guest, BuildHouseSnapshot(), r =>
            {
                if (!r.Ok) _host.Ui.Toast(_host.Strings.T("online.besuch.fehler", ("grund", ErrorText(r.Code))), "house");
            });
        }

        private void OnVisitReady(string hostCode)
        {
            var t = _host.Strings;
            _buildWarned = false;
            if (Client.VisitRole == VisitRole.Host)
            {
                _host.Ui.PopToRoot();
                _host.World?.BeginHosting();
                HookBuildRelay();
                OpenVisitScreen(host: true);
                return;
            }
            Client.FetchHouse(hostCode, (ok, layout, code) =>
            {
                if (!ok || !Client.VisitActive)
                {
                    _host.Ui.Toast(t.T("online.besuch.laden_fehler", ("grund", ErrorText(code))), "house", 4f);
                    Client.EndVisit(null);
                    return;
                }
                _host.Ui.PopToRoot();
                _host.Ui.FadeThrough(() =>
                {
                    if (_host.World == null || !_host.World.EnterVisit(layout, _scratchDir))
                    {
                        _host.Ui.Toast(t.T("online.besuch.laden_fehler", ("grund", "LAYOUT")), "house", 4f);
                        Client.EndVisit(null);
                        return;
                    }
                    OpenVisitScreen(host: false);
                }, 0.45f);
            });
        }

        private void OpenVisitScreen(bool host)
        {
            if (_visitScreen != null && _host.Ui.IsOpen(_visitScreen)) return;
            _visitScreen = new VisitScreen(_host, this, host);
            _host.Ui.Push(_visitScreen);
            SendLook();
            var (x, z, anim) = _host.World != null ? _host.World.OwnPose : (0f, 0f, "idle");
            Client.SendVisitPos(x, z, anim, _host.World?.RoomId ?? "living", true);
        }

        /// <summary>Our outfit to the peer (additive LOOK relay; older clients ignore unknown kinds).</summary>
        private void SendLook()
        {
            if (!Client.VisitActive) return;
            var eq = new JObject();
            foreach (var kv in CosmeticsState.EquippedMap(J.Dict(_host.Store.State["cosmetics"])))
                if (!string.IsNullOrEmpty(kv.Value)) eq[kv.Key] = kv.Value;
            Client.SendRoomMessage("LOOK", new JObject { ["equipped"] = eq });
        }

        /// <summary>The player ends the visit (button); the server's VISIT_ENDED then tears the stage down.</summary>
        public void EndVisit()
        {
            if (Client.VisitActive) Client.EndVisit(r => { if (!r.Ok) EndVisitLocally(true); });
            else EndVisitLocally(true);
        }

        private bool _ending;

        /// <summary>Back to the own house (guest) / alone again (host); pops the visit screen.</summary>
        public void EndVisitLocally(bool toast)
        {
            if (_ending) return;
            var w = _host.World;
            if (w == null || (!w.Visiting && !w.Hosting && _visitScreen == null)) return;
            _ending = true;
            _buildWarned = false;
            var screen = _visitScreen;
            _visitScreen = null;
            void TearDown()
            {
                if (w.Visiting) w.LeaveVisit();
                if (w.Hosting) w.EndHosting();
                if (screen != null && _host.Ui.IsOpen(screen)) _host.Ui.PopToRoot();
                _ending = false;
            }
            if (w.Visiting && !_host.Ui.Fading) _host.Ui.FadeThrough(TearDown, 0.4f);
            else TearDown();
            if (toast) _host.Ui.Toast(_host.Strings.T("online.besuch.vorbei"), "house");
        }

        // ── building while a friend visits (visit_scene.gd, Doc C §3.4 point 5) ──

        private bool _buildWarned, _houseDirty;
        private float _houseRetryAt;
        private Gooby.Unity.World.SimpleWorld _buildWorld;

        /// <summary>Host: the visit HUD offers "Dekorieren" (the guest only watches).</summary>
        public bool CanHostBuild => Client.VisitActive && Client.VisitRole == VisitRole.Host && _host.World != null && _host.World.Hosting;

        /// <summary>Host: build mode opens during a visit — the warning reaches both sides once per visit (visit_scene.gd _set_build_active).</summary>
        public void BeginHostBuild()
        {
            if (!CanHostBuild || _buildWarned) return;
            _buildWarned = true;
            HookBuildRelay();
            Client.SendRoomMessage(VisitBuild.KindStart, new JObject());
            _host.Ui.Toast(_host.Strings.T("social.visit.build_warning"), "toolbox", 4f);
            Debug.Log("[visit] build start sent");
        }

        private void HookBuildRelay()
        {
            var w = _host.World;
            if (w == null || w == _buildWorld) return;
            if (_buildWorld != null) _buildWorld.HostBuildDelta -= OnHostBuildDelta;
            _buildWorld = w;
            w.HostBuildDelta += OnHostBuildDelta;
        }

        /// <summary>Every grid change of the host's build session goes to the guest; the lawn is not part of a visit.</summary>
        private void OnHostBuildDelta(string roomId, string op, string item, Cell cell, int rot)
        {
            if (!CanHostBuild || roomId == "garden") return;
            BeginHostBuild();
            var sent = Client.SendRoomMessage(VisitBuild.KindDelta, VisitBuild.Delta(roomId, op, item, cell, rot));
            _houseDirty = true;
            Debug.Log("[visit] build delta out " + op + " " + item + " " + roomId + " " + cell.X + "," + cell.Y + " rot=" + rot + (sent ? "" : " (not sent)"));
        }

        /// <summary>After building, the server's snapshot catches up — a guest who reconnects (or the next one) sees the new room.</summary>
        private void UploadBuiltHouse()
        {
            _houseDirty = false;
            Client.UploadHouse(BuildHouseSnapshot(), (ok, rev, code) =>
            {
                if (ok) { Debug.Log("[visit] house re-uploaded rev=" + rev); return; }
                _houseDirty = true;
                _houseRetryAt = Time.unscaledTime + 20f;
                Debug.LogWarning("[visit] house re-upload failed: " + code);
            });
        }

        // ── board games (Schiffe versenken, Schach) ─────────────────────────

        /// <summary>
        /// Invites wait on every screen as a question card naming the game (Mitspielen / Später, the server drops them
        /// after 30 s); BOARD_START — our invite accepted or theirs accepted by us, also a rematch — opens
        /// <see cref="BattleshipScreen"/> or <see cref="ChessScreen"/>. An invite that arrives mid-game is declined with a toast.
        /// </summary>
        private void WireBoard()
        {
            var t = _host.Strings;
            Board.Changed += () => Changed?.Invoke();
            Board.InviteIncoming += inv =>
            {
                CloseBoardPrompt(inv.From);
                if (Board.IsActive)
                {
                    Board.Decline(inv.From);
                    _host.Ui.Toast(t.T("online.board.beschaeftigt", ("name", inv.Name)), "gamepad", 3.6f);
                    return;
                }
                Sfx.PlayUi("soft_toast");
                Haptics.Tap();
                var from = inv.From;
                var seconds = Mathf.Clamp(inv.ExpiresInMs / 1000f - 2f, 5f, 28f);
                var chess = inv.Game == BoardClient.GameChess;
                _boardPrompts[from] = _host.Ui.Prompt(chess ? "grid" : "gamepad", t.T(chess ? "chess.frage_titel" : "online.board.frage_titel"),
                    t.T(chess ? "chess.frage_text" : "online.board.frage_text", ("name", inv.Name), ("gooby", inv.GoobyName)),
                    t.T("online.board.annehmen"), () => { _boardPrompts.Remove(from); AcceptBoard(from); },
                    t.T("online.board.ablehnen"), () => { _boardPrompts.Remove(from); Board.Decline(from); }, seconds);
            };
            Board.InviteDeclined += by =>
            {
                var f = Client.FindFriend(by);
                var chess = Board.LastDeclinedGame == BoardClient.GameChess;
                _host.Ui.Toast(t.T(chess ? "chess.declined_by" : "online.board.abgelehnt", ("name", f != null ? f.Name : by)), chess ? "grid" : "gamepad");
            };
            Board.GameStarted += () =>
            {
                Debug.Log("[board] start game=" + Board.Game + " room=" + Board.RoomId + " seed=" + Board.Seed + " first=" + Board.FirstCode + " me=" + Board.MyCode + " vs=" + Board.OpponentCode);
                OpenBoardScreen();
            };
            Board.ShotResult += (cell, hit, sunk, cells) => Debug.Log("[board] shot " + cell.Ref + " → " + (sunk ? "sunk" : hit ? "hit" : "miss"));
            Board.OpponentShot += (cell, o) => Debug.Log("[board] incoming " + cell.Ref + " → " + (o.Sunk ? "sunk" : o.Hit ? "hit" : "miss"));
            Board.SendRejected += (kind, code) => Debug.Log("[board] rejected " + kind + " " + code);
            Board.OpponentMoved += uci => Debug.Log("[board] chess incoming " + uci);
            Board.GameOver += (winner, iWon, reason) => Debug.Log("[board] over winner=" + winner + " iWon=" + iWon + " reason=" + reason);
        }

        /// <summary>BOARD_INVITE from the friends list; the start arrives as BOARD_START once the friend accepts.</summary>
        public void InviteToBoard(FriendInfo f, Action<bool, string> done) => InviteToBoard(f, BoardClient.GameBattleship, done);

        /// <summary>BOARD_INVITE for <paramref name="game"/> ("battleship" | "chess").</summary>
        public void InviteToBoard(FriendInfo f, string game, Action<bool, string> done)
        {
            if (f == null) { done(false, "NOT_FOUND"); return; }
            var chess = game == BoardClient.GameChess;
            Board.Invite(f.Code, game, r =>
            {
                if (r.Ok)
                {
                    Sfx.PlayUi("soft_confirm");
                    _host.Ui.Toast(chess ? _host.Strings.T("chess.invite_sent", ("name", f.Name))
                        : _host.Strings.T("online.board.eingeladen", ("name", f.Name), ("gooby", f.GoobyName)), chess ? "grid" : "gamepad");
                }
                done(r.Ok, r.Code);
            });
        }

        private void AcceptBoard(string from)
        {
            Board.Accept(from, r =>
            {
                if (!r.Ok) _host.Ui.Toast(_host.Strings.T("online.board.fehler", ("grund", BoardErrorText(r.Code))), "gamepad", 3.2f);
            });
        }

        private void CloseBoardPrompt(string from)
        {
            if (string.IsNullOrEmpty(from) || !_boardPrompts.TryGetValue(from, out var close)) return;
            _boardPrompts.Remove(from);
            close?.Invoke();
        }

        /// <summary>The table for the running game (a rematch keeps the open screen, it resets itself).</summary>
        private void OpenBoardScreen()
        {
            CloseBoardPrompt(Board.OpponentCode);
            var chess = Board.IsChess;
            if (_boardScreen != null && _host.Ui.IsOpen(_boardScreen))
            {
                if ((_boardScreen is ChessScreen) == chess) return;
                // the other game's finished table is still open: swap it (its OnHidden does not leave the new room)
                if (_host.Ui.Current == _boardScreen) _host.Ui.Pop();
                else _host.Ui.PopToRoot();
            }
            if (_host.Ui.Current is MinigameHostScreen) _host.Ui.PopToRoot(); // no round ticking under the table
            if (_host.Ui.Current is ChessScreen practice && practice.IsPractice) _host.Ui.Pop(); // the real table wins over practice
            _boardScreen = chess ? new ChessScreen(_host, this) : (UiScreen)new BattleshipScreen(_host, this);
            _host.Ui.Push(_boardScreen);
            Sfx.PlayUi("soft_open");
        }

        // ── GOB NOM online coop ─────────────────────────────────────────────

        /// <summary>
        /// Coop invites wait on every screen as a question card (an open GOB NOM lists them in its select panel instead);
        /// as soon as a pairing stands (we accepted, or our invite was accepted) GOB NOM opens right in the coop select of
        /// both players — there they vote a level together. The round itself is <see cref="GobnomScreen"/>'s business.
        /// </summary>
        private void WireGobnom()
        {
            var t = _host.Strings;
            Gobnom.Changed += () => Changed?.Invoke();
            Gobnom.InviteIncoming += inv =>
            {
                CloseGobnomPrompt(inv.From);
                if (Gobnom.IsPaired || Gvz.IsPaired || Board.IsActive)
                {
                    Gobnom.Decline(inv.From);
                    _host.Ui.Toast(t.T("online.board.beschaeftigt", ("name", inv.Name)), "candy", 3.6f);
                    return;
                }
                Sfx.PlayUi("soft_toast");
                Haptics.Tap();
                if (_host.Ui.Current is GobnomScreen g && g.ShowsNetPanel) return; // the coop select lists the invite itself
                var from = inv.From;
                var seconds = Mathf.Clamp(inv.ExpiresInMs / 1000f - 2f, 5f, 28f);
                _gobnomPrompts[from] = _host.Ui.Prompt("candy", t.T("nl.gobnom.frage_titel"), t.T("gobnom.netz.laedt_ein", ("name", inv.GoobyName)),
                    t.T("gobnom.netz.annehmen"), () => { _gobnomPrompts.Remove(from); AcceptGobnom(from); },
                    t.T("gobnom.netz.ablehnen"), () => { _gobnomPrompts.Remove(from); Gobnom.Decline(from); }, seconds);
            };
            Gobnom.InviteDeclined += by =>
            {
                var f = Client.FindFriend(by);
                _host.Ui.Toast(t.T("gobnom.netz.abgelehnt", ("name", f != null ? f.GoobyName : by)), "candy");
            };
            Gobnom.SessionReady += () =>
            {
                CloseGobnomPrompt(Gobnom.PartnerCode);
                Debug.Log("[gobnom] paired room=" + Gobnom.RoomId + " side=" + Gobnom.MySide + " partner=" + Gobnom.PartnerCode);
                OpenGobnomCoop();
            };
            Gobnom.GameStarted += () => Debug.Log("[gobnom] start level=" + Gobnom.Level + " seed=" + Gobnom.Seed + " side=" + Gobnom.MySide + " room=" + Gobnom.RoomId);
            Gobnom.VotesChanged += () => Debug.Log("[gobnom] votes me=" + Gobnom.MyVote + " partner=" + Gobnom.PartnerVote);
            Gobnom.ResultConfirmed += r => Debug.Log("[gobnom] result outcome=" + r.Outcome + " level=" + r.Level + " jars=" + r.Jars + " tick=" + r.Tick + " reward=" + r.RewardId);
            Gobnom.Aborted += (reason, by) => Debug.Log("[gobnom] aborted reason=" + reason + " by=" + by);
            Gobnom.DesyncReported += tick => Debug.Log("[gobnom] desync tick=" + tick);
            Gobnom.PeerConnectionChanged += (down, ms) => Debug.Log("[gobnom] peer " + (down ? "down" : "up"));
            Gobnom.SnapshotReceived += d => Debug.Log("[gobnom] snapshot phase=" + J.Str(d["phase"]) + " level=" + J.Int(d["level"]));
        }

        /// <summary>A coop invite from the GOB NOM select panel or the end card ("Nochmal mit …").</summary>
        public void InviteToGobnom(string code, string goobyName, Action<bool, string> done)
        {
            Gobnom.Invite(code, r =>
            {
                if (r.Ok)
                {
                    Sfx.PlayUi("soft_confirm");
                    _host.Ui.Toast(_host.Strings.T("gobnom.netz.eingeladen", ("name", goobyName)), "candy");
                }
                done?.Invoke(r.Ok, r.Code);
            });
        }

        /// <summary>"Annehmen" on the question card (or in the GOB NOM panel): the READY pairing opens GOB NOM.</summary>
        public void AcceptGobnom(string from)
        {
            CloseGobnomPrompt(from);
            Gobnom.Accept(from, r =>
            {
                if (!r.Ok) _host.Ui.Toast(_host.Strings.T("gobnom.netz.fehler", ("code", BoardErrorText(r.Code))), "candy", 3.2f);
            });
        }

        /// <summary>GOB NOM for the coop lobby: an open GOB NOM switches to its coop select, else a fresh round starts without the pregame.</summary>
        public void OpenGobnomCoop()
        {
            if (_host.Ui.Current is GobnomScreen open && open.ShowCoopLobby()) return;
            if (_host.Ui.Current is MinigameHostScreen) _host.Ui.PopToRoot(); // no other round ticking underneath
            if (!(MinigameHostScreen.For(_host, "gobnom") is GobnomScreen screen)) return;
            _host.Ui.Push(screen);
            screen.StartRound(screen.Root);
            screen.ShowCoopLobby();
        }

        private void CloseGobnomPrompt(string from)
        {
            if (string.IsNullOrEmpty(from) || !_gobnomPrompts.TryGetValue(from, out var close)) return;
            _gobnomPrompts.Remove(from);
            close?.Invoke();
        }

        // ── Goobys vs Zombies online PvP ────────────────────────────────────

        /// <summary>
        /// PvP challenges wait on every screen as a question card (an open GvZ select lists them in its panel instead); a
        /// standing pairing opens GvZ's select for both players, where both confirm the start. Every confirmed result books
        /// its coins exactly once — also one that arrives with the WELCOME while GvZ is closed.
        /// </summary>
        private void WireGvz()
        {
            var t = _host.Strings;
            Gvz.Changed += () => Changed?.Invoke();
            Gvz.InviteIncoming += inv =>
            {
                CloseGvzPrompt(inv.From);
                if (Gvz.IsPaired || Gobnom.IsPaired || Board.IsActive)
                {
                    Gvz.Decline(inv.From);
                    _host.Ui.Toast(t.T("online.board.beschaeftigt", ("name", inv.Name)), "zombie", 3.6f);
                    return;
                }
                Sfx.PlayUi("soft_toast");
                Haptics.Tap();
                if (_host.Ui.Current is GvzScreen g && g.ShowsNetPanel) return; // the select lists the challenge itself
                var from = inv.From;
                var seconds = Mathf.Clamp(inv.ExpiresInMs / 1000f - 2f, 5f, 28f);
                _gvzPrompts[from] = _host.Ui.Prompt("zombie", t.T("nl.gvz.frage_titel"), t.T("gvz.netz.laedt_ein", ("name", inv.GoobyName)),
                    t.T("gvz.netz.annehmen"), () => { _gvzPrompts.Remove(from); AcceptGvz(from); },
                    t.T("gvz.netz.ablehnen"), () => { _gvzPrompts.Remove(from); Gvz.Decline(from); }, seconds);
            };
            Gvz.InviteDeclined += by =>
            {
                var f = Client.FindFriend(by);
                _host.Ui.Toast(t.T("gvz.netz.abgelehnt", ("name", f != null ? f.GoobyName : by)), "zombie");
            };
            Gvz.SessionReady += () =>
            {
                CloseGvzPrompt(Gvz.PartnerCode);
                Debug.Log("[gvz] paired room=" + Gvz.RoomId + " side=" + Gvz.MySide + " partner=" + Gvz.PartnerCode);
                OpenGvzLobby();
            };
            Gvz.GameStarted += () => Debug.Log("[gvz] start seed=" + Gvz.Seed + " side=" + Gvz.MySide + " room=" + Gvz.RoomId);
            Gvz.ResultConfirmed += r =>
            {
                var booked = Gooby.Core.Minigames.Gvz.GvzProgress.BookNetResult(_host.Store, r.RewardId, r.Won, r.Coins);
                Debug.Log("[gvz] result winner=" + r.Winner + " side=" + r.Side + " won=" + r.Won + " coins=" + r.Coins + " booked=" + booked + " reward=" + r.RewardId);
            };
            Gvz.Aborted += (reason, by) => Debug.Log("[gvz] aborted reason=" + reason + " by=" + by);
            Gvz.DesyncReported += tick => Debug.Log("[gvz] desync tick=" + tick);
            Gvz.PeerConnectionChanged += (down, ms) => Debug.Log("[gvz] peer " + (down ? "down" : "up"));
        }

        /// <summary>A PvP challenge from the GvZ select panel or the end card ("Revanche mit …").</summary>
        public void InviteToGvz(string code, string goobyName, Action<bool, string> done)
        {
            Gvz.Invite(code, r =>
            {
                if (r.Ok)
                {
                    Sfx.PlayUi("soft_confirm");
                    _host.Ui.Toast(_host.Strings.T("gvz.netz.eingeladen", ("name", goobyName)), "zombie");
                }
                done?.Invoke(r.Ok, r.Code);
            });
        }

        /// <summary>"Annehmen" on the question card (or in the GvZ panel): the READY pairing opens GvZ.</summary>
        public void AcceptGvz(string from)
        {
            CloseGvzPrompt(from);
            Gvz.Accept(from, r =>
            {
                if (!r.Ok) _host.Ui.Toast(_host.Strings.T("gvz.netz.fehler", ("code", BoardErrorText(r.Code))), "zombie", 3.2f);
            });
        }

        /// <summary>GvZ for the PvP lobby: an open GvZ switches to its select, else a fresh round starts without the pregame.</summary>
        public void OpenGvzLobby()
        {
            if (_host.Ui.Current is GvzScreen open && open.ShowNetLobby()) return;
            if (_host.Ui.Current is MinigameHostScreen) _host.Ui.PopToRoot();
            if (!(MinigameHostScreen.For(_host, "gvz") is GvzScreen screen)) return;
            _host.Ui.Push(screen);
            screen.StartRound(screen.Root);
            screen.ShowNetLobby();
        }

        private void CloseGvzPrompt(string from)
        {
            if (string.IsNullOrEmpty(from) || !_gvzPrompts.TryGetValue(from, out var close)) return;
            _gvzPrompts.Remove(from);
            close?.Invoke();
        }

        // ── ranch multiplayer (Wettrennen, Parcours-Duell, Fangen) ──────────

        /// <summary>
        /// Ranch invitations wait on every screen as a question card (Mitmachen / Jetzt nicht) unless the Ranch-Treffen menu
        /// lists them itself; an invite while another online game runs is declined with a toast. Every result (also the
        /// pending ones of the WELCOME) is booked once into ranch.mp and acknowledged.
        /// </summary>
        private void WireRanch()
        {
            var t = _host.Strings;
            RanchMp.Changed += () => Changed?.Invoke();
            RanchMp.OwnHorse = () =>
            {
                var state = _host.Store.State;
                return RmpPferdLook.Von(state, RanchHof.ReitPferd(state));
            };
            RanchMp.InviteIncoming += inv =>
            {
                CloseRanchPrompt(inv.From);
                var spiel = t.T("ranch_mp.menu." + inv.Mode);
                if (RanchMp.IsActive || Gvz.IsPaired || Gobnom.IsPaired || Board.IsActive || _host.World != null && (_host.World.Visiting || _host.World.Hosting))
                {
                    RanchMp.Decline(inv.From);
                    _host.Ui.Toast(t.T("nl.rmp.beschaeftigt", ("name", inv.Name), ("spiel", spiel)), "horseshoe", 3.6f);
                    return;
                }
                Sfx.PlayUi("soft_toast");
                Haptics.Tap();
                if (_host.Ui.Current is RanchMpScreen r && r.ShowsMenu) return;
                var from = inv.From;
                var seconds = Mathf.Clamp(inv.ExpiresInMs / 1000f - 2f, 5f, 28f);
                _ranchPrompts[from] = _host.Ui.Prompt("horseshoe", t.T("nl.rmp.frage_titel"), t.T("ranch_mp.menu.invite_von", ("name", inv.Name), ("spiel", spiel)),
                    t.T("ranch_mp.menu.annehmen"), () => { _ranchPrompts.Remove(from); AcceptRanch(from); },
                    t.T("ranch_mp.menu.ablehnen"), () => { _ranchPrompts.Remove(from); RanchMp.Decline(from); }, seconds);
            };
            RanchMp.InviteDeclined += by =>
            {
                var f = Client.FindFriend(by);
                _host.Ui.Toast(t.T("ranch_mp.menu.abgelehnt", ("name", f != null ? f.Name : by)), "horseshoe");
            };
            RanchMp.SessionReady += () =>
            {
                foreach (var p in RanchMp.Players) CloseRanchPrompt(p.Code);
                Debug.Log("[rmp] session room=" + RanchMp.RoomId + " mode=" + RanchMp.Mode + " kurs=" + RanchMp.Kurs + " players=" + RanchMp.Players.Count + (RanchMp.Rematch ? " (rematch)" : "") + (RanchMp.IsFree ? " host=" + RanchMp.Host + " rev=" + RanchMp.HostMetaRev : ""));
                LoadRanchVisitMeta();
                OpenRanchMp();
            };
            RanchMp.ReactionReceived += (kind, from, body) => Debug.Log("[rmp] reaction " + kind + " from " + from + " " + body.ToString(Newtonsoft.Json.Formatting.None));
            RanchMp.PeerJoined += (code, name) => Debug.Log("[rmp] peer joined " + code);
            RanchMp.ResultReceived += r =>
            {
                var booked = false;
                _host.Store.Update(s => booked = RmpState.ErgebnisVerbuchen(s, r, _host.Store.Clock.LocalDay()));
                RanchMp.AckResult(r.RewardId);
                Debug.Log("[rmp] result mode=" + r.Mode + " rank=" + r.Rank + " zeitMs=" + r.ZeitMs + " dnf=" + r.Dnf + " ranked=" + r.Ranked + " booked=" + booked + " reward=" + r.RewardId);
                // a ranked finish on a live course is a server-measured time: it goes on the friends board of that course
                if (booked && r.Ranked && !r.Dnf && RanchBoards.IstLive(r.Kurs) && RanchBoards.WertOk(r.Kurs, r.ZeitMs))
                    Client.SubmitRanchScore(r.Kurs, r.ZeitMs, s => Debug.Log("[rmp-board] score " + r.Kurs + " " + r.ZeitMs + " → " + s));
            };
            RanchMp.Ended += by =>
            {
                Debug.Log("[rmp] ended by=" + by);
                RanchVisitMeta = null;
            };
            RanchMp.PeerConnectionChanged += (code, down) => Debug.Log("[rmp] peer " + code + (down ? " down" : " up"));
        }

        /// <summary>
        /// "Einladen" in the Ranch-Treffen menu (rennen | parcours | fangen — the server picks the course — or the free rides
        /// ausritt | besuch). A visit is to the own ranch: its display metadata goes up first (PUT /api/ranch), so the
        /// guest sees the horses and trophies the moment RANCH_READY names the revision.
        /// </summary>
        public void InviteToRanch(string code, string mode, Action<bool, string> done)
        {
            void Invite() => RanchMp.Invite(code, mode, "", r =>
            {
                if (r.Ok) Sfx.PlayUi("soft_confirm");
                Debug.Log("[rmp] invite " + code + " " + mode + " → " + (r.Ok ? "ok" : r.Code));
                done?.Invoke(r.Ok, r.Code);
            });
            if (mode != RmpKurse.Besuch)
            {
                Invite();
                return;
            }
            var meta = RmpRanchMeta.Build(_host.Store.State, Client.PlayerName, Client.GoobyName);
            Client.UploadRanchMeta(meta, (ok, rev, err) =>
            {
                Debug.Log("[rmp] ranch meta upload → " + (ok ? "rev " + rev + ", " + ((JArray)meta["pferde"]).Count + " horses" : err));
                if (ok) Invite();
                else done?.Invoke(false, err);
            });
        }

        /// <summary>Besuch as the guest: the host's ranch (healed metadata) once it is loaded, else null.</summary>
        public JObject RanchVisitMeta { get; private set; }
        public event Action RanchVisitMetaChanged;

        private void LoadRanchVisitMeta()
        {
            RanchVisitMeta = null;
            if (RanchMp.Mode != RmpKurse.Besuch || RanchMp.Host.Length == 0 || RanchMp.Host == RanchMp.MyCode) return;
            var room = RanchMp.RoomId;
            Client.FetchRanchMeta(RanchMp.Host, (meta, err) =>
            {
                if (RanchMp.RoomId != room) return;
                RanchVisitMeta = meta;
                Debug.Log("[rmp] visit meta of " + RanchMp.Host + " → " + (meta != null ? ((JArray)meta["pferde"]).Count + " horses" : err));
                RanchVisitMetaChanged?.Invoke();
            });
        }

        /// <summary>"Mitmachen" on the question card or in the menu: RANCH_READY opens the Ranch-Treffen lobby.</summary>
        public void AcceptRanch(string from)
        {
            CloseRanchPrompt(from);
            RanchMp.Accept(from, r =>
            {
                if (!r.Ok) _host.Ui.Toast(RanchErrorText(r.Code), "horseshoe", 3.2f);
            });
        }

        /// <summary>The Ranch-Treffen screen (menu without a session, else lobby / ride / result).</summary>
        public void OpenRanchMp()
        {
            if (_ranchScreen != null && _host.Ui.IsOpen(_ranchScreen)) return;
            if (_host.Ui.Current is MinigameHostScreen) _host.Ui.PopToRoot();
            _ranchScreen = new RanchMpScreen(_host, this);
            _host.Ui.Push(_ranchScreen);
        }

        private RanchMpScreen _ranchScreen;

        private void CloseRanchPrompt(string from)
        {
            if (string.IsNullOrEmpty(from) || !_ranchPrompts.TryGetValue(from, out var close)) return;
            _ranchPrompts.Remove(from);
            close?.Invoke();
        }

        // ── ranch friends leaderboards + ghosts (ranchmp.js REST) ──────────

        private readonly Dictionary<string, (string name, string b64)> _ranchFriendGhosts = new Dictionary<string, (string name, string b64)>();

        /// <summary>
        /// A finished tournament run (RanchTurnierScreen): its value goes on the rw5_ board of the discipline, and a new
        /// personal best run also as the board's ghost (the server keeps only the best one). Offline nothing is sent.
        /// </summary>
        public void SubmitRanchRun(string disziplin, double wert, string geistB64, bool neuerBestlauf)
        {
            if (!Client.IsOnline) return;
            var kurs = RanchBoards.KursFuer(disziplin);
            var v = RanchBoards.WertFuer(disziplin, wert);
            if (v == null)
            {
                Debug.Log("[rmp-board] run " + kurs + " wert=" + wert + " outside the server window — not sent");
                return;
            }
            Client.SubmitRanchScore(kurs, v.Value, s => Debug.Log("[rmp-board] score " + kurs + " " + v.Value + " → " + s));
            if (neuerBestlauf && RanchBoards.GeistMoeglich(kurs) && !string.IsNullOrEmpty(geistB64))
                Client.UploadRanchGhost(kurs, v.Value, geistB64, g => Debug.Log("[rmp-board] ghost " + kurs + " " + v.Value + " → " + g));
        }

        /// <summary>The friends board of a course or discipline (null when offline or refused).</summary>
        public void FetchRanchBoard(string kurs, Action<RanchBoard> done)
        {
            Client.FetchRanchLeaderboard(kurs, b =>
            {
                Debug.Log("[rmp-board] board " + b);
                done?.Invoke(b);
            });
        }

        /// <summary>
        /// "Geist laden" on a board line: fetches the friend's best run; a valid G5 ghost replaces the own one in the next
        /// tournament run of that discipline. done(ok, code).
        /// </summary>
        public void LoadRanchGhost(string kurs, string friendCode, string name, Action<bool, string> done)
        {
            Client.FetchRanchGhost(kurs, friendCode, g =>
            {
                var ok = g.Ok && Gooby.Core.Minigames.Ranch.RanchCompGhost.FromB64(g.B64).Ok;
                if (ok) _ranchFriendGhosts[RanchBoards.Disziplin(kurs)] = (name ?? friendCode, g.B64);
                Debug.Log("[rmp-board] load ghost " + kurs + " from " + friendCode + " → " + (ok ? "ok wert=" + g.Wert : g.Code.Length > 0 ? g.Code : "BAD_GHOST"));
                done?.Invoke(ok, ok ? "" : g.Code.Length > 0 ? g.Code : "BAD_GHOST");
            });
        }

        /// <summary>The loaded friend's ghost of a discipline (G5 Base64), or null.</summary>
        public string RanchFriendGhost(string disziplin) =>
            disziplin != null && _ranchFriendGhosts.TryGetValue(disziplin, out var g) ? g.b64 : null;

        /// <summary>Whose ghost the next run of a discipline races ("" = none loaded).</summary>
        public string RanchFriendGhostName(string disziplin) =>
            disziplin != null && _ranchFriendGhosts.TryGetValue(disziplin, out var g) ? g.name : "";

        public IEnumerable<string> RanchFriendGhostDisziplinen => _ranchFriendGhosts.Keys;

        /// <summary>Ranch multiplayer error text (ranch_mp.fehler.*), else <see cref="ErrorText"/>.</summary>
        public string RanchErrorText(string code)
        {
            var c = (code ?? "").ToLowerInvariant();
            if (c.Length > 0 && _host.Strings.HasKey("ranch_mp.fehler." + c)) return _host.Strings.T("ranch_mp.fehler." + c);
            return ErrorText(code);
        }

        /// <summary>Error text for board codes (online.board.err.*: NOT_YOUR_TURN, BAD_TURN_N, TOMATO_LIMIT …), else <see cref="ErrorText"/>.</summary>
        public string BoardErrorText(string code)
        {
            var c = (code ?? "").ToLowerInvariant();
            if (c.Length > 0 && _host.Strings.HasKey("online.board.err." + c)) return _host.Strings.T("online.board.err." + c);
            return ErrorText(code);
        }

        // ── texts ───────────────────────────────────────────────────────────

        /// <summary>Player-facing text for a net/server error code (mail.err.*, netset.mp.fehler.*, online.err.*).</summary>
        public string ErrorText(string code)
        {
            var t = _host.Strings;
            var c = (code ?? "").ToLowerInvariant();
            if (c.Length == 0) return t.T("online.err.unbekannt", ("code", "?"));
            if (t.HasKey("online.err." + c)) return t.T("online.err." + c);
            if (t.HasKey("nl.err." + c)) return t.T("nl.err." + c);
            if (t.HasKey("netset.mp.fehler." + c)) return t.T("netset.mp.fehler." + c);
            if (t.HasKey("mail.err." + c)) return t.T("mail.err." + c);
            return t.T("online.err.unbekannt", ("code", code));
        }

        /// <summary>One-line status for chips: online as GOOBY-XXXX / connecting / offline (+ reason).</summary>
        public string StatusText()
        {
            var t = _host.Strings;
            if (!NetAddress.Parse(ServerAddress).Valid) return t.T("online.status.kein_server");
            switch (Client.Status)
            {
                case NetStatus.Online: return t.T("online.status.online", ("code", Client.FriendCode), ("server", Client.Endpoint.Display));
                case NetStatus.Connecting: return t.T("online.status.verbinde", ("server", Client.Endpoint.Display));
                default:
                    return Client.LastErrorCode.Length > 0
                        ? t.T("online.status.offline_grund", ("grund", ErrorText(Client.LastErrorCode)))
                        : t.T("online.status.offline");
            }
        }
    }
}
