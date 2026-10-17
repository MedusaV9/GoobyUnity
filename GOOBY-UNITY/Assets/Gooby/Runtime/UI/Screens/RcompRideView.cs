using System;
using System.Collections.Generic;
using Gooby.Core.Minigames;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Minigames.Ranch;
using Gooby.Core.Random;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;
using P = Gooby.Unity.UI.Screens.MgPaint;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The painted riding stage of the RW-5 competitions — comp_lauf.gd, comp_arena.gd, comp_hud.gd and ride_hud.gd as a
    /// top-down chase view that turns with the horse (nose up), so the floating stick steers like Godot's camera behind
    /// the horse: meadow, sand ring with fence, grandstand crowd, flags and trees, the discipline's course, the ghost, race
    /// bots and the rider. HUD: the judge's line, stamina + gait, callouts. Controls: left half = floating stick (only X
    /// steers, 12 % dead zone), right half = swipe up/down or the ▲/▼ buttons for the gait, plus jump / stroke / "Jetzt!"
    /// when the run needs them.
    /// </summary>
    public sealed partial class RcompRideView : IRideCam
    {
        public const float StickRadiusPx = 64f, StickDeadzone = 0.12f, SwipeMinPx = 24f, SwipeMaxSec = 0.25f;

        public readonly P.Canvas Canvas;
        public readonly VisualElement Controls, Hud;
        public IRcompRide Ride { get; private set; }
        public float Steer => _steer;

        private readonly GameHost _host;
        private readonly Juice _fx;
        private readonly VisualElement _field;
        private readonly P.Canvas _stickCanvas;
        private readonly VisualElement _stickZone, _swipeZone, _gaitUp, _gaitDown, _jump, _stroke, _now, _infoChip, _staminaCard, _staminaFill;
        private readonly Label _info, _gait, _callout, _command, _strokeLabel;
        private RcompLauf _bound;
        private V2 _cam;
        private double _camH, _calloutT, _jubel, _anim, _span = 26.0;
        private bool _snap = true;
        private int _stickPointer = -1, _swipePointer = -1;
        private Vector2 _stickStart, _stickNow, _swipeStart;
        private double _swipeStartTime;
        private float _steer;
        private readonly HashSet<int> _abgeworfen = new HashSet<int>();
        private readonly HashSet<int> _verpasst = new HashSet<int>();
        private readonly List<(V2 pos, double age, float size)> _puffs = new List<(V2, double, float)>();
        private double _puffAccu;
        private (V2 pos, float scale)[] _treesFar, _treesNear, _bushes;
        private V2[] _crowd;
        private (V2 a, V2 b, float w)[] _sandStreaks;

        private static readonly Color Outline = MgScenery.Hex(0x4A3B36);
        private static readonly Color Cream = MgScenery.Hex(0xF9EDD6);
        private static readonly Color Grass = MgScenery.Hex(0x8FC66B), GrassLight = MgScenery.Hex(0x9BD177), GrassDark = MgScenery.Hex(0x76AD57);
        private static readonly Color Sand = MgScenery.Hex(0xE2C79A), SandDark = MgScenery.Hex(0xCFAF80), SandTrack = MgScenery.Hex(0xDBBF91);
        private static readonly Color Wood = MgScenery.Hex(0xC98C59), WoodDark = MgScenery.Hex(0x8C613D), Rail = MgScenery.Hex(0xF4EAD5);
        private static readonly Color Gold = MgScenery.Hex(0xFFD34D), Rose = MgScenery.Hex(0xE98CA0), Teal = MgScenery.Hex(0x5FA8A0);
        private static readonly Color BarrelBlue = MgScenery.Hex(0x5C8CC7), PoleRed = MgScenery.Hex(0xE8676B);
        private static readonly Color Shadow = new Color(0.16f, 0.12f, 0.08f, 0.18f);
        private static readonly Color[] FahnenFarben = { MgScenery.Hex(0xE8676B), MgScenery.Hex(0x5C8CC7), MgScenery.Hex(0xFFD34D), MgScenery.Hex(0x7FCF9B) };

        public static readonly Dictionary<string, (Color hell, Color dunkel)> Fell = new Dictionary<string, (Color, Color)>
        {
            ["braun"] = (MgScenery.Hex(0xB98A5E), MgScenery.Hex(0x6E4B2E)),
            ["schwarz"] = (MgScenery.Hex(0x6B6470), MgScenery.Hex(0x3E3944)),
            ["weiss"] = (MgScenery.Hex(0xF2E9DC), MgScenery.Hex(0xC9B79C)),
            ["fuchs"] = (MgScenery.Hex(0xD98E5F), MgScenery.Hex(0x8A4A2E)),
            ["palomino"] = (MgScenery.Hex(0xD9A066), MgScenery.Hex(0x8A5A33)),
            ["schecke"] = (MgScenery.Hex(0xC98BB9), MgScenery.Hex(0x8A5A7A)),
            ["apricot"] = (MgScenery.Hex(0xEFB98A), MgScenery.Hex(0xB4744A)),
            ["rauchgrau"] = (MgScenery.Hex(0x9A93A6), MgScenery.Hex(0x5C5566)),
            ["nebel"] = (MgScenery.Hex(0xB9C4D2), MgScenery.Hex(0xF1F4F8)),
        };

        /// <summary>The coat of a save horse ("farbe"); an unknown colour rides brown.</summary>
        public static (Color hell, Color dunkel) FellVon(string farbe) => Fell.TryGetValue(farbe ?? "", out var f) ? f : Fell["braun"];

        public RcompRideView(GameHost host, VisualElement field, Juice fx)
        {
            _host = host;
            _fx = fx;
            _field = field;
            var t = host.Strings;
            Canvas = new P.Canvas { Paint = Paint, name = "rcomp-stage" };
            Ux.Abs(Canvas, 0, 0, 0, 0);

            Hud = new VisualElement { name = "rcomp-hud", pickingMode = PickingMode.Ignore };
            Ux.Abs(Hud, 0, 0, 0, 0);
            _infoChip = Ux.Row("gb-card");
            _infoChip.name = "rcomp-info";
            _infoChip.pickingMode = PickingMode.Ignore;
            _infoChip.style.position = Position.Absolute;
            _infoChip.style.top = 10;
            _infoChip.style.left = Length.Percent(50);
            _infoChip.style.translate = new Translate(Length.Percent(-50), 0);
            _infoChip.style.paddingLeft = 16; _infoChip.style.paddingRight = 16; _infoChip.style.paddingTop = 6; _infoChip.style.paddingBottom = 6;
            _info = Ux.Text("", "gb-bold", "gb-nowrap");
            _info.pickingMode = PickingMode.Ignore;
            _infoChip.Add(_info);
            Hud.Add(_infoChip);

            _staminaCard = Ux.Col("gb-card");
            _staminaCard.name = "rcomp-stamina";
            _staminaCard.pickingMode = PickingMode.Ignore;
            _staminaCard.style.position = Position.Absolute;
            _staminaCard.style.left = 12; _staminaCard.style.top = 58;
            _staminaCard.style.paddingLeft = 12; _staminaCard.style.paddingRight = 12; _staminaCard.style.paddingTop = 6; _staminaCard.style.paddingBottom = 8;
            _staminaCard.style.width = 170;
            var head = Ux.Row();
            head.pickingMode = PickingMode.Ignore;
            head.style.justifyContent = Justify.SpaceBetween;
            var puste = Ux.Text(t.T("rpferd.reiten.ausdauer"), "gb-sm", "gb-bold");
            puste.pickingMode = PickingMode.Ignore;
            head.Add(puste);
            _gait = Ux.Text("", "gb-sm", "gb-bold");
            _gait.name = "rcomp-gait";
            _gait.pickingMode = PickingMode.Ignore;
            head.Add(_gait);
            _staminaCard.Add(head);
            var track = new VisualElement { pickingMode = PickingMode.Ignore };
            track.style.height = 12; track.style.marginTop = 4;
            track.style.backgroundColor = MgScenery.Hex(0xEADFCB);
            Radius(track, 6f);
            _staminaFill = new VisualElement { name = "rcomp-stamina-fill", pickingMode = PickingMode.Ignore };
            _staminaFill.style.height = Length.Percent(100);
            _staminaFill.style.backgroundColor = MgScenery.Hex(0x7FCF9B);
            Radius(_staminaFill, 6f);
            track.Add(_staminaFill);
            _staminaCard.Add(track);
            Hud.Add(_staminaCard);

            _command = Ux.Text("", "gb-h1", "gb-bold", "gb-nowrap");
            _command.name = "rcomp-command";
            _command.pickingMode = PickingMode.Ignore;
            _command.style.position = Position.Absolute;
            _command.style.top = Length.Percent(17);
            _command.style.left = 0; _command.style.right = 0;
            _command.style.unityTextAlign = TextAnchor.MiddleCenter;
            _command.style.color = Cream;
            _command.style.unityTextOutlineWidth = 3f;
            _command.style.unityTextOutlineColor = Outline;
            _command.style.display = DisplayStyle.None;
            Hud.Add(_command);

            _callout = Ux.Text("", "gb-h1", "gb-bold", "gb-nowrap");
            _callout.name = "rcomp-callout";
            _callout.pickingMode = PickingMode.Ignore;
            _callout.style.position = Position.Absolute;
            _callout.style.top = Length.Percent(28);
            _callout.style.left = 0; _callout.style.right = 0;
            _callout.style.unityTextAlign = TextAnchor.MiddleCenter;
            _callout.style.unityTextOutlineWidth = 3f;
            _callout.style.unityTextOutlineColor = Outline;
            _callout.style.opacity = 0f;
            Hud.Add(_callout);

            Controls = new VisualElement { name = "rcomp-controls", pickingMode = PickingMode.Ignore };
            Ux.Abs(Controls, 0, 0, 0, 0);
            _stickZone = new VisualElement { name = "rcomp-stick-zone" };
            _stickZone.style.position = Position.Absolute;
            _stickZone.style.left = 0; _stickZone.style.top = Length.Percent(18); _stickZone.style.bottom = 0; _stickZone.style.width = Length.Percent(50);
            _stickZone.RegisterCallback<PointerDownEvent>(OnStickDown);
            _stickZone.RegisterCallback<PointerMoveEvent>(OnStickMove);
            _stickZone.RegisterCallback<PointerUpEvent>(OnStickUp);
            _stickZone.RegisterCallback<PointerCaptureOutEvent>(_ => ReleaseStick());
            Controls.Add(_stickZone);
            _swipeZone = new VisualElement { name = "rcomp-swipe-zone" };
            _swipeZone.style.position = Position.Absolute;
            _swipeZone.style.right = 0; _swipeZone.style.top = Length.Percent(18); _swipeZone.style.bottom = 0; _swipeZone.style.width = Length.Percent(50);
            _swipeZone.RegisterCallback<PointerDownEvent>(OnSwipeDown);
            _swipeZone.RegisterCallback<PointerUpEvent>(OnSwipeUp);
            _swipeZone.RegisterCallback<PointerCaptureOutEvent>(_ => _swipePointer = -1);
            Controls.Add(_swipeZone);
            _stickCanvas = new P.Canvas { Paint = PaintStick, name = "rcomp-stick" };
            Ux.Abs(_stickCanvas, 0, 0, 0, 0);
            Controls.Add(_stickCanvas);

            _gaitUp = RoundButton("rcomp-gait-up", "arrow_up", 68f, () => Ride?.GaitUp());
            _gaitUp.style.right = 22; _gaitUp.style.bottom = Length.Percent(44);
            Controls.Add(_gaitUp);
            _gaitDown = RoundButton("rcomp-gait-down", "arrow_down", 68f, () => Ride?.GaitDown());
            _gaitDown.style.right = 22; _gaitDown.style.bottom = Length.Percent(44);
            _gaitDown.style.marginBottom = -84;
            Controls.Add(_gaitDown);

            _jump = PillButton("rcomp-jump", t.T("rpferd.reiten.springen"), MgScenery.Hex(0xFFE7A8), () =>
            {
                if (Ride != null && Ride.Jump()) PlaySfx("game_whoosh", 1.5f, 0.8f);
            });
            _jump.style.right = 110; _jump.style.bottom = 18;
            Controls.Add(_jump);

            _stroke = PillButton("rcomp-stroke", t.T("rpferd.reiten.streicheln"), MgScenery.Hex(0xDFF2CF), () =>
            {
                if (Ride != null && Ride.ZweiterWind())
                {
                    PlaySfx("game_star");
                    Callout(t.T("rpferd.reiten.zweiter_wind"), MgScenery.Hex(0x7FCF9B));
                }
            });
            _strokeLabel = _stroke.Q<Label>();
            _stroke.style.left = Length.Percent(50); _stroke.style.translate = new Translate(Length.Percent(-50), 0); _stroke.style.bottom = 18;
            Controls.Add(_stroke);

            _now = PillButton("rcomp-now", t.T("rcomp.lauf.jetzt"), MgScenery.Hex(0xFFB3C1), () =>
            {
                var e = Ride?.Tippe();
                if (e != null && e.Typ == "zu_frueh")
                {
                    PlaySfx("soft_error");
                    Callout(t.T("rcomp.lauf.zu_frueh"), Teal);
                }
            });
            _now.style.left = Length.Percent(50); _now.style.translate = new Translate(Length.Percent(-50), 0); _now.style.bottom = 24;
            _now.style.width = 200; _now.style.height = 84;
            Controls.Add(_now);
            if (World.MgStage3D.Supported) BuildStage();
        }

        /// <summary>Ends the view: the 3D stage goes, the run's events no longer reach it.</summary>
        public void Dispose()
        {
            if (_bound != null) _bound.Ereignis -= OnEvent;
            _bound = null;
            DropStage();
        }

        // ── binding / per-frame ──

        public void Bind(IRcompRide ride)
        {
            Ride = ride;
            Rebind();
        }

        private void Rebind()
        {
            var lauf = Ride?.Lauf;
            if (ReferenceEquals(lauf, _bound)) return;
            if (_bound != null) _bound.Ereignis -= OnEvent;
            _bound = lauf;
            _abgeworfen.Clear();
            _verpasst.Clear();
            _puffs.Clear();
            _snap = true;
            ReleaseStick();
            _command.style.display = DisplayStyle.None;
            if (lauf == null) return;
            lauf.Ereignis += OnEvent;
            var rng = new GoobyRng(lauf.Seed);
            _treesFar = new (V2, float)[18];
            for (var i = 0; i < 18; i++)
            {
                var winkel = 2.0 * Math.PI * i / 18.0 + rng.Next() * 0.3;
                var radius = 62.0 + rng.Next() * 28.0;
                var pos = new V2(Math.Cos(winkel) * radius, Math.Sin(winkel) * radius * 0.75);
                _treesFar[i] = (pos, (float)(0.8 + rng.Next() * 0.6));
            }
            var near = new GoobyRng(77);
            _treesNear = new (V2, float)[14];
            for (var i = 0; i < 14; i++)
            {
                var winkel = 2.0 * Math.PI * (i + 0.5) / 14.0 + near.Next() * 0.2;
                var pos = new V2(Math.Cos(winkel) * (40.0 + near.Next() * 9.0), Math.Sin(winkel) * (28.0 + near.Next() * 6.0));
                _treesNear[i] = (pos, (float)(0.55 + near.Next() * 0.35));
            }
            var tore = lauf.Richter is RcompRichterGelaende gelaende ? gelaende.Tore : new List<RcompKurs.Tor>();
            var feld = new GoobyRng(91);
            var bushes = new List<(V2, float)>();
            for (var i = 0; i < 70 && bushes.Count < 46; i++)
            {
                var pos = new V2(RcompKurs.FeldX + 4.0 + feld.Next() * (RcompKurs.FeldW - 8.0), RcompKurs.FeldZ + 4.0 + feld.Next() * (RcompKurs.FeldT - 8.0));
                var scale = (float)(0.5 + feld.Next() * 0.5);
                if (Math.Abs(pos.X) < 36f && Math.Abs(pos.Y) < 24f) continue;
                var frei = true;
                foreach (var tor in tore) if ((tor.Pos - pos).Length < 9f) frei = false;
                if (frei && (pos - new V2(0f, 58f)).Length < 8f) frei = false;
                if (frei) bushes.Add((pos, scale));
            }
            _bushes = bushes.ToArray();
            var crowd = new GoobyRng(4711);
            var menge = 18;
            _crowd = new V2[menge];
            for (var i = 0; i < menge; i++)
            {
                var reihe = i % 3;
                var platz = -11.0 + i / 3.0 * (22.0 / Math.Ceiling(menge / 3.0));
                _crowd[i] = new V2(platz + crowd.Next() * 1.4, RcompKurs.ArenaZ - 4.5 - reihe * 1.15 + crowd.Next() * 0.3);
            }
            var sand = new GoobyRng(2412);
            _sandStreaks = new (V2, V2, float)[10];
            for (var i = 0; i < 10; i++)
            {
                var laenge = 5.0 + sand.Next() * 7.0;
                var breite = (float)(0.5 + sand.Next() * 0.4);
                var mitte = new V2(sand.Next() * 42.0 - 21.0, sand.Next() * 24.0 - 12.0);
                _sandStreaks[i] = (mitte - new V2(laenge * 0.5, 0.0), mitte + new V2(laenge * 0.5, 0.0), breite);
            }
        }

        public void Tick(double dt)
        {
            Rebind();
            _anim += dt;
            _jubel = Math.Max(0.0, _jubel - dt);
            var lauf = Ride?.Lauf;
            if (lauf != null)
            {
                var c = lauf.Controller;
                if (c != null)
                {
                    if (_snap) _camH = c.Heading;
                    else _camH += RideFeel.WrapAngle(c.Heading - _camH) * RideFeel.CamFollowFactor(dt) * 1.6;
                    _cam = c.Pos;
                    var weit = lauf.Disziplin == "zeit" || lauf.Disziplin == "gelaende" ? 15.0 : lauf.Disziplin == "rennen" ? 6.0 : 0.0;
                    var ziel = 23.0 + weit + 7.0 * Math.Min(1.0, c.Tempo / RideFeel.Tempo["galopp"]);
                    _span = _snap ? ziel : _span + (ziel - _span) * RideFeel.CamFollowFactor(dt);
                    SpawnDust(c, dt);
                    if (Ride.AcceptsInput) Ride.Steer(_steer);
                }
                else
                {
                    _cam = new V2(0f, 1.2f);
                    _camH = 0.0;
                    _span = 13.0;
                }
                _snap = false;
            }
            for (var i = _puffs.Count - 1; i >= 0; i--)
            {
                var p = _puffs[i];
                p.age += dt;
                if (p.age > 0.7) _puffs.RemoveAt(i);
                else _puffs[i] = p;
            }
            if (_calloutT > 0.0)
            {
                _calloutT -= dt;
                _callout.style.opacity = Mathf.Clamp01((float)(_calloutT / 0.35));
            }
            TickStage(dt);
        }

        public void Render()
        {
            var t = _host.Strings;
            var lauf = Ride?.Lauf;
            var running = lauf != null && !lauf.FertigGemeldet;
            Hud.style.display = running ? DisplayStyle.Flex : DisplayStyle.None;
            Controls.style.display = running ? DisplayStyle.Flex : DisplayStyle.None;
            if (running)
            {
                var c = lauf.Controller;
                var reiten = c != null;
                _staminaCard.style.display = reiten ? DisplayStyle.Flex : DisplayStyle.None;
                _stickZone.style.display = reiten ? DisplayStyle.Flex : DisplayStyle.None;
                _swipeZone.style.display = reiten ? DisplayStyle.Flex : DisplayStyle.None;
                _gaitUp.style.display = reiten ? DisplayStyle.Flex : DisplayStyle.None;
                _gaitDown.style.display = reiten ? DisplayStyle.Flex : DisplayStyle.None;
                _jump.style.display = lauf.Disziplin == "springen" ? DisplayStyle.Flex : DisplayStyle.None;
                _now.style.display = lauf.Disziplin == "schau" ? DisplayStyle.Flex : DisplayStyle.None;
                _stroke.style.display = reiten && c.ZweiterWindBereit && lauf.Laeuft ? DisplayStyle.Flex : DisplayStyle.None;
                if (reiten)
                {
                    var max = Math.Max(1.0, c.AusdauerMaxWert);
                    _staminaFill.style.width = Length.Percent((float)(100.0 * c.Ausdauer / max));
                    _staminaFill.style.backgroundColor = c.Ausdauer < RideFeel.AusdauerGaloppAb ? Rose : c.Gait == "galopp" ? Gold : MgScenery.Hex(0x7FCF9B);
                    _gait.text = t.T("rpferd.gang." + c.Gait);
                    if (lauf.Disziplin == "springen")
                    {
                        var abstand = c.NaechstesHindernisM();
                        var puls = abstand > 0.0 && abstand <= 6.0 ? 1f + 0.08f * Mathf.Sin((float)_anim * 14f) : 1f;
                        _jump.style.scale = new Scale(new Vector3(puls, puls, 1f));
                    }
                }
                if (lauf.Richter is RcompRichterSchau schau)
                {
                    var cd = schau.Countdown;
                    var s = cd > 0.0 && cd < 1.0 ? 1f + 0.14f * (float)(1.0 - cd) : 1f;
                    _now.style.scale = new Scale(new Vector3(s, s, 1f));
                    if (schau.Fertig) _command.style.display = DisplayStyle.None;
                }
                _info.text = InfoText(lauf.HudInfo());
            }
            Canvas.MarkDirtyRepaint();
            _stickCanvas.MarkDirtyRepaint();
        }

        public void Callout(string text, Color color)
        {
            _callout.text = text;
            _callout.style.color = color;
            _callout.style.opacity = 1f;
            _calloutT = 1.3;
        }

        public string InfoText(RcompHudInfo info)
        {
            var t = _host.Strings;
            if (string.IsNullOrEmpty(info.Key)) return "";
            var name = info.Name ?? "";
            if (name != "") name = t.T((info.Key == "rcomp.hud.figur" ? "rcomp.figur." : "rcomp.aufgabe.") + name);
            return t.T(info.Key, ("n", info.N), ("max", info.Max), ("name", name), ("gangart", t.T("rpferd.gang." + (info.Gangart ?? "schritt"))));
        }

        private void OnEvent(RcompEvent e)
        {
            var t = _host.Strings;
            StageEvent(e);
            if (e.Typ == "kommando")
            {
                _command.text = t.T("rcomp.kommando." + e.Id);
                _command.style.display = DisplayStyle.Flex;
                PlaySfx("soft_open");
                return;
            }
            if (e.Typ == "abwurf") _abgeworfen.Add(e.Index);
            if (e.Typ == "tor_verpasst") _verpasst.Add(e.Index);
            if (e.Typ == "treffer" || e.Typ == "daneben") _command.style.display = DisplayStyle.None;
            string key;
            bool gut;
            switch (e.Typ)
            {
                case "perfekt": key = "rcomp.lauf.perfekt"; gut = true; break;
                case "hindernis_ok": key = ""; gut = true; break;
                case "abwurf": key = "rcomp.lauf.abwurf"; gut = false; break;
                case "verweigert": key = "rcomp.lauf.verweigert"; gut = false; break;
                case "tor_ok": key = "rcomp.lauf.tor_ok"; gut = true; break;
                case "tor_verpasst": key = "rcomp.lauf.tor_verpasst"; gut = false; break;
                case "beruehrung": key = "rcomp.lauf.beruehrung"; gut = false; break;
                case "aufgabe_ok": key = "rcomp.lauf.aufgabe_ok"; gut = true; break;
                case "aufgabe_ausgelassen": key = "rcomp.lauf.aufgabe_ausgelassen"; gut = false; break;
                case "tonne_ok": key = "rcomp.lauf.tonne_ok"; gut = true; break;
                case "tonne_um": key = "rcomp.lauf.tonne_um"; gut = false; break;
                case "runde": key = "rcomp.lauf.runde"; gut = true; break;
                case "windschatten": key = "rcomp.lauf.windschatten"; gut = true; break;
                case "figur_ok": key = "rcomp.lauf.figur_ok"; gut = true; break;
                case "gangart_ok": key = "rcomp.lauf.gangart_ok"; gut = true; break;
                case "gangart_wechsel": key = "rcomp.lauf.wechsel_zu"; gut = true; break;
                case "treffer": key = "rcomp.lauf.treffer"; gut = true; break;
                case "daneben": key = "rcomp.lauf.daneben"; gut = false; break;
                case "ziel": key = "rcomp.lauf.ziel"; gut = true; break;
                default: return;
            }
            if (gut && e.Typ != "gangart_wechsel") _jubel = Math.Max(_jubel, 2.2);
            if (key != "")
                Callout(t.T(key, ("n", e.Nummer), ("punkte", (int)Math.Round(e.Punkte)), ("gangart", t.T("rpferd.gang." + (e.Gangart ?? "schritt")))), gut ? Gold : Rose);
            PlaySfx(e.Typ == "perfekt" || e.Typ == "treffer" || e.Typ == "ziel" ? "game_perfect" : gut ? "game_star" : "game_miss", 0f, 0.85f);
            if (gut) Haptics.Tap();
        }

        // ── controls ──

        private void OnStickDown(PointerDownEvent e)
        {
            if (Ride == null || !Ride.AcceptsInput && Ride.Lauf?.Gestartet == true) return;
            _stickPointer = e.pointerId;
            _stickStart = _stickNow = _field.WorldToLocal(e.position);
            _stickZone.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        private void OnStickMove(PointerMoveEvent e)
        {
            if (e.pointerId != _stickPointer) return;
            _stickNow = _field.WorldToLocal(e.position);
            _steer = StickLenkung(_stickStart, _stickNow);
        }

        private void OnStickUp(PointerUpEvent e)
        {
            if (e.pointerId != _stickPointer) return;
            if (_stickZone.HasPointerCapture(e.pointerId)) _stickZone.ReleasePointer(e.pointerId);
            ReleaseStick();
        }

        private void ReleaseStick()
        {
            _stickPointer = -1;
            _steer = 0f;
        }

        /// <summary>ride_touch.gd stick_lenkung: only X steers, 12 % dead zone, linear to the 64 px radius.</summary>
        public static float StickLenkung(Vector2 start, Vector2 now)
        {
            var anteil = Mathf.Clamp((now.x - start.x) / StickRadiusPx, -1f, 1f);
            if (Mathf.Abs(anteil) < StickDeadzone) return 0f;
            return Mathf.Sign(anteil) * Mathf.Clamp01((Mathf.Abs(anteil) - StickDeadzone) / (1f - StickDeadzone));
        }

        private void OnSwipeDown(PointerDownEvent e)
        {
            _swipePointer = e.pointerId;
            _swipeStart = _field.WorldToLocal(e.position);
            _swipeStartTime = Time.realtimeSinceStartupAsDouble;
            _swipeZone.CapturePointer(e.pointerId);
        }

        private void OnSwipeUp(PointerUpEvent e)
        {
            if (e.pointerId != _swipePointer) return;
            if (_swipeZone.HasPointerCapture(e.pointerId)) _swipeZone.ReleasePointer(e.pointerId);
            _swipePointer = -1;
            var weg = _field.WorldToLocal(e.position).y - _swipeStart.y;
            if (Time.realtimeSinceStartupAsDouble - _swipeStartTime > SwipeMaxSec || Mathf.Abs(weg) < SwipeMinPx) return;
            if (weg < 0f) Ride?.GaitUp();
            else Ride?.GaitDown();
            PlaySfx("soft_tap", weg < 0f ? 3f : -3f, 0.7f);
        }

        private void PaintStick(Painter2D p)
        {
            if (_stickPointer < 0) return;
            P.Circle(p, _stickStart, StickRadiusPx + 4f, new Color(1f, 1f, 1f, 0.22f));
            P.Ring(p, _stickStart, StickRadiusPx, 3f, new Color(1f, 1f, 1f, 0.7f));
            var knob = new Vector2(_stickStart.x + _steer * StickRadiusPx, _stickStart.y);
            P.Circle(p, knob, 22f, Outline);
            P.Circle(p, knob, 19f, Cream);
        }

        private VisualElement RoundButton(string name, string icon, float size, Action act)
        {
            var b = Ux.El("gb-card");
            b.name = name;
            b.style.position = Position.Absolute;
            b.style.width = size; b.style.height = size;
            Radius(b, size * 0.5f);
            b.style.alignItems = Align.Center; b.style.justifyContent = Justify.Center;
            var img = Ux.Icon(icon);
            img.style.width = size * 0.5f; img.style.height = size * 0.5f;
            img.pickingMode = PickingMode.Ignore;
            b.Add(img);
            b.RegisterCallback<PointerDownEvent>(e =>
            {
                e.StopPropagation();
                act();
                PlaySfx("soft_tap", 0f, 0.7f);
                _fx?.ScalePop(b, 1.12f);
            });
            return b;
        }

        private VisualElement PillButton(string name, string label, Color color, Action act)
        {
            var b = Ux.El("gb-card");
            b.name = name;
            b.style.position = Position.Absolute;
            b.style.width = 150; b.style.height = 70;
            b.style.backgroundColor = color;
            b.style.alignItems = Align.Center; b.style.justifyContent = Justify.Center;
            var l = Ux.Text(label, "gb-h3", "gb-bold", "gb-nowrap");
            l.pickingMode = PickingMode.Ignore;
            b.Add(l);
            b.RegisterCallback<PointerDownEvent>(e =>
            {
                e.StopPropagation();
                act();
                _fx?.ScalePop(b, 1.08f);
            });
            return b;
        }

        private static void Radius(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r; e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r; e.style.borderBottomRightRadius = r;
        }

        private static void PlaySfx(string id, float semitones = 0f, float volume = 1f) => Sfx.Play(id, semitones, volume);

        // ── projection: metres around the rider, turned so the horse rides up the screen ──

        private float W => _field.contentRect.width;
        private float H => _field.contentRect.height;
        private float Ppm => Mathf.Max(4f, H / (float)_span);
        private Vector2 Anchor => Ride?.Lauf?.Controller != null ? new Vector2(W * 0.5f, H * 0.64f) : new Vector2(W * 0.5f, H * 0.5f);

        public Vector2 Px(V2 w)
        {
            var dx = w.X - _cam.X;
            var dz = w.Y - _cam.Y;
            var sin = Math.Sin(_camH);
            var cos = Math.Cos(_camH);
            var side = dx * cos - dz * sin;
            var fwd = -dx * sin - dz * cos;
            var a = Anchor;
            return new Vector2(a.x + (float)side * Ppm, a.y - (float)fwd * Ppm);
        }

        private Vector2 Px(double x, double z) => Px(new V2(x, z));
        private float M(double metres) => (float)metres * Ppm;

        /// <summary>Screen angle (radians, y down) of a world heading under the current camera.</summary>
        private float ScreenAngle(double heading) => (float)Math.Atan2(-Math.Cos(_camH - heading), Math.Sin(_camH - heading));

        private float ViewRadiusM => Mathf.Sqrt(W * W + H * H) / Ppm * 0.75f + 4f;
        private bool Near(V2 w, double margin = 2.0) => (w - _cam).Length < ViewRadiusM + margin;

        private void Quad(Painter2D p, double x0, double z0, double x1, double z1, Color color) =>
            P.Poly(p, new[] { Px(x0, z0), Px(x1, z0), Px(x1, z1), Px(x0, z1) }, color);

        // ── painting ──

        private void Paint(Painter2D p)
        {
            var lauf = Ride?.Lauf;
            if (lauf == null || W <= 1f) return;
            if (_stage != null)
            {
                PaintOverlay3D(p, lauf);
                return;
            }
            P.Rect(p, Vector2.zero, new Vector2(W, H), Grass);
            var r = ViewRadiusM;
            var x0 = Math.Floor((_cam.X - r) / 8.0) * 8.0;
            for (var x = x0; x < _cam.X + r; x += 8.0)
                if (((int)Math.Floor(x / 8.0) & 1) == 0) Quad(p, x, _cam.Y - r, x + 4.0, _cam.Y + r, GrassLight);
            PaintTufts(p);
            PaintArena(p, lauf);
            PaintCourse(p, lauf);
            PaintFence(p);
            PaintGrandstand(p);
            PaintFlags(p);
            PaintTrees(p, _bushes, 1.1f);
            PaintTrees(p, _treesNear, 1.4f);
            PaintTrees(p, _treesFar, 1.8f);
            PaintMarker(p, lauf);
            PaintGhost(p, lauf);
            for (var i = 0; i < lauf.Bots.Count; i++)
            {
                var bot = lauf.Bots[i];
                PaintHorse(p, bot.Pos, bot.Heading, 0.0, Fell.TryGetValue(bot.Farbe ?? "braun", out var f) ? f : Fell["braun"], lauf.Zeit >= bot.ZeitS ? "schritt" : "galopp", 1f, false);
                PaintBotRider(p, bot.Pos, bot.Heading, FahnenFarben[i % FahnenFarben.Length]);
            }
            foreach (var puff in _puffs)
            {
                var k = (float)(puff.age / 0.7);
                P.Circle(p, Px(puff.pos), M(puff.size * (0.6 + k)), new Color(0.86f, 0.76f, 0.6f, 0.45f * (1f - k)));
            }
            if (lauf.Controller != null) PaintHorse(p, lauf.Controller.Pos, lauf.Controller.Heading, lauf.Controller.Y, FellVon(lauf.Farbe), lauf.Controller.Gait, 1f, true);
            else PaintShow(p, lauf);
        }

        private void PaintTufts(Painter2D p)
        {
            var r = ViewRadiusM;
            const double cell = 3.5;
            var cx0 = (int)Math.Floor((_cam.X - r) / cell);
            var cz0 = (int)Math.Floor((_cam.Y - r) / cell);
            var n = (int)Math.Ceiling(2.0 * r / cell);
            if (n > 40) n = 40;
            for (var i = 0; i <= n; i++)
            for (var j = 0; j <= n; j++)
            {
                var cx = cx0 + i;
                var cz = cz0 + j;
                var h = (uint)(cx * 73856093) ^ (uint)(cz * 19349663);
                h ^= h >> 13;
                h *= 0x5bd1e995;
                var fx = (h & 0xFF) / 255.0;
                var fz = ((h >> 8) & 0xFF) / 255.0;
                var w = new V2((cx + fx) * cell, (cz + fz) * cell);
                if (RcompKurs.InArena(w)) continue;
                var a = Px(w);
                if ((h >> 16) % 9 == 0) P.Circle(p, a, M(0.12), (h >> 20) % 2 == 0 ? Color.white : Gold);
                else
                {
                    P.Line(p, a, a + new Vector2(M(0.05), -M(0.28)), M(0.06), GrassDark);
                    P.Line(p, a + new Vector2(M(0.12), 0f), a + new Vector2(M(0.22), -M(0.24)), M(0.06), GrassDark);
                }
            }
        }

        private void PaintArena(Painter2D p, RcompLauf lauf)
        {
            const double x0 = RcompKurs.ArenaX, z0 = RcompKurs.ArenaZ, x1 = RcompKurs.ArenaX + RcompKurs.ArenaW, z1 = RcompKurs.ArenaZ + RcompKurs.ArenaT;
            Quad(p, x0 - 0.6, z0 - 0.6, x1 + 0.6, z1 + 0.6, SandDark);
            Quad(p, x0, z0, x1, z1, Sand);
            foreach (var seite in new[] { -1.0, 1.0 })
            {
                Quad(p, x0 + 0.5, seite * (z1 - 0.75) - 0.65, x1 - 0.5, seite * (z1 - 0.75) + 0.65, SandTrack);
                Quad(p, seite * (x1 - 0.75) - 0.65, z0 + 2.0, seite * (x1 - 0.75) + 0.65, z1 - 2.0, SandTrack);
            }
            foreach (var s in _sandStreaks) P.Line(p, Px(s.a), Px(s.b), M(s.w * 0.6), new Color(SandTrack.r, SandTrack.g, SandTrack.b, 0.55f));
            if (lauf.Disziplin == "rennen") PaintOval(p);
        }

        private void PaintOval(Painter2D p)
        {
            var umfang = RcompKurs.RennenUmfang();
            var schritte = (int)(umfang / 2.0);
            for (var pass = 0; pass < 2; pass++)
                for (var i = 0; i < schritte; i++)
                {
                    var a = RcompKurs.RennenPunkt(umfang * i / schritte);
                    var b = RcompKurs.RennenPunkt(umfang * (i + 1) / schritte);
                    if (!Near(a, 6.0)) continue;
                    P.Line(p, Px(a), Px(b), M(pass == 0 ? 6.4 : 4.4), pass == 0 ? MgScenery.Hex(0xA7D58A) : MgScenery.Hex(0x9FCF7E));
                }
            for (var i = 0; i < schritte; i += 2)
            {
                var s = umfang * i / schritte;
                var punkt = RcompKurs.RennenPunkt(s);
                if (!Near(punkt, 4.0)) continue;
                var tangente = (RcompKurs.RennenPunkt(s + 0.8) - punkt).Normalized();
                var quer = new V2(-tangente.Y, tangente.X);
                foreach (var seite in new[] { -1.0, 1.0 })
                {
                    var q = Px(punkt + quer * (3.4 * seite));
                    P.Circle(p, q, M(0.14), Rail);
                }
            }
            var start = RcompKurs.RennenPunkt(0.0);
            P.Line(p, Px(start + new V2(0f, -3.2f)), Px(start + new V2(0f, 3.2f)), M(0.5), Cream);
        }

        private void PaintFence(Painter2D p)
        {
            const float x0 = RcompKurs.ArenaX, z0 = RcompKurs.ArenaZ, x1 = RcompKurs.ArenaX + RcompKurs.ArenaW, z1 = RcompKurs.ArenaZ + RcompKurs.ArenaT;
            var rail = M(0.12);
            P.Line(p, Px(x0, z0), Px(x1, z0), rail, Rail);
            P.Line(p, Px(x0, z0), Px(x0, z1), rail, Rail);
            P.Line(p, Px(x1, z0), Px(x1, z1), rail, Rail);
            P.Line(p, Px(x0, z1), Px(-3.0, z1), rail, Rail);
            P.Line(p, Px(3.0, z1), Px(x1, z1), rail, Rail);
            for (var x = x0; x <= x1 + 0.01f; x += 2f)
            {
                Post(p, new V2(x, z0));
                if (Math.Abs(x) > 3f) Post(p, new V2(x, z1));
            }
            for (var z = z0 + 2f; z <= z1 - 1.99f; z += 2f)
            {
                Post(p, new V2(x0, z));
                Post(p, new V2(x1, z));
            }
        }

        private void Post(Painter2D p, V2 w)
        {
            if (!Near(w, 1.0)) return;
            var c = Px(w);
            var s = M(0.13);
            P.Rect(p, c - new Vector2(s, s) + new Vector2(M(0.08), M(0.1)), c + new Vector2(s, s) + new Vector2(M(0.08), M(0.1)), Shadow);
            P.Rect(p, c - new Vector2(s, s), c + new Vector2(s, s), WoodDark);
        }

        private void PaintGrandstand(Painter2D p)
        {
            var basisZ = RcompKurs.ArenaZ - 4.5;
            if (!Near(new V2(0.0, basisZ), 16.0)) return;
            for (var stufe = 0; stufe < 3; stufe++)
            {
                var z = basisZ - stufe * 1.15;
                Quad(p, -13.0, z - 0.6, 13.0, z + 0.6, stufe % 2 == 0 ? Wood : WoodDark);
            }
            var staerke = (float)Math.Min(1.0, _jubel);
            for (var i = 0; i < _crowd.Length; i++)
            {
                var hop = Mathf.Max(0f, Mathf.Sin((float)_anim * 5.6f + i * 1.7f)) * (0.04f + 0.26f * staerke);
                var sway = Mathf.Sin((float)_anim * 2.3f + i * 0.9f) * 0.05f;
                var c = Px(_crowd[i] + new V2(sway * 0.4f, -hop));
                var tint = Color.Lerp(MgScenery.Hex(0x3D2961), FahnenFarben[i % FahnenFarben.Length], 0.45f);
                P.Circle(p, c, M(0.36), Outline);
                P.Circle(p, c, M(0.32), tint);
                foreach (var sd in new[] { -1f, 1f }) P.RotBlob(p, c + new Vector2(sd * M(0.26), -M(0.12)), M(0.1), M(0.2), sd * 0.4f, tint);
                P.Circle(p, c + new Vector2(0f, -M(0.04)), M(0.2), Color.Lerp(tint, Color.white, 0.25f));
            }
            Quad(p, -13.5, basisZ - 3.6, 13.5, basisZ - 2.9, FahnenFarben[0]);
            for (var i = 0; i < 9; i++) Quad(p, -13.5 + i * 3.0, basisZ - 2.9, -12.0 + i * 3.0, basisZ - 2.4, i % 2 == 0 ? FahnenFarben[0] : Cream);
        }

        private void PaintFlags(Painter2D p)
        {
            const float x0 = RcompKurs.ArenaX, z0 = RcompKurs.ArenaZ, x1 = RcompKurs.ArenaX + RcompKurs.ArenaW, z1 = RcompKurs.ArenaZ + RcompKurs.ArenaT;
            var ecken = new[] { new V2(x0, z0), new V2(x1, z0), new V2(x0, z1), new V2(x1, z1) };
            var wind = Mathf.Sin((float)_anim * 3f) * 0.15f;
            foreach (var e in ecken)
            {
                if (!Near(e, 3.0)) continue;
                var c = Px(e);
                P.Circle(p, c + new Vector2(M(0.25), M(0.3)), M(0.16), Shadow);
                P.Circle(p, c, M(0.13), Cream);
                var tip = Px(e + new V2(1.3, 0.35 + wind));
                P.Poly(p, new[] { c, tip, Px(e + new V2(0.0, 0.7)) }, FahnenFarben[2]);
            }
        }

        private void PaintTrees(Painter2D p, (V2 pos, float scale)[] trees, float crown)
        {
            if (trees == null) return;
            foreach (var (pos, scale) in trees)
            {
                if (!Near(pos, crown * 2.0 * scale + 2.0)) continue;
                var c = Px(pos);
                var r = M(crown * scale);
                P.Circle(p, c + new Vector2(r * 0.35f, r * 0.45f), r, Shadow);
                P.Circle(p, c, r + M(0.08), MgScenery.Hex(0x4F8A45));
                P.Circle(p, c, r, MgScenery.Hex(0x66A657));
                P.Circle(p, c + new Vector2(-r * 0.25f, -r * 0.3f), r * 0.55f, MgScenery.Hex(0x7FBF67));
            }
        }

        private void PaintCourse(Painter2D p, RcompLauf lauf)
        {
            switch (lauf.Richter)
            {
                case RcompRichterTonnen t:
                    var start = RcompKurs.TonnenStart;
                    P.Line(p, Px(start + new V2(-5f, 0f)), Px(start + new V2(5f, 0f)), M(0.5), Cream);
                    for (var i = 0; i < t.Tonnen.Length; i++) Barrel(p, t.Tonnen[i], i, t.IstUmgeworfen(i), i == t.Idx);
                    break;
                case RcompRichterGelaende g:
                    for (var i = 0; i < g.Tore.Count; i++) Gate(p, g.Tore[i], i < g.Idx ? (_verpasst.Contains(i) ? 3 : 2) : i == g.Idx ? 1 : 0, i + 1);
                    break;
                case RcompRichterSpringen s:
                    for (var i = 0; i < s.Kurs.Count; i++) Obstacle(p, s.Kurs[i], i == s.Idx, i < s.Idx, _abgeworfen.Contains(i));
                    break;
                case RcompRichterTrail tr:
                    for (var i = 0; i < tr.Stationen.Length; i++) TrailStation(p, tr.Stationen[i], i == tr.Idx, i < tr.Idx);
                    break;
                case RcompRichterDressur d:
                    PaintDressage(p, d);
                    break;
                case RcompRichterSchau _:
                    P.Circle(p, Px(0.0, 0.0), M(4.45), MgScenery.Hex(0xE7D2AE));
                    P.Circle(p, Px(0.0, 0.0), M(4.2), MgScenery.Hex(0xF7E9CF));
                    P.Circle(p, Px(0.0, 0.0), M(2.6), new Color(1f, 0.96f, 0.82f, 0.6f));
                    break;
            }
        }

        private void Barrel(Painter2D p, V2 pos, int i, bool um, bool aktiv)
        {
            if (!Near(pos, 2.0)) return;
            var c = Px(pos);
            if (um)
            {
                var ang = 0.6f + i * 1.1f;
                P.RotBlob(p, c + new Vector2(M(0.15), M(0.2)), M(0.6), M(0.42), ang, Shadow);
                P.RotBlob(p, c, M(0.6), M(0.42), ang, Outline);
                P.RotBlob(p, c, M(0.54), M(0.36), ang, BarrelBlue);
                P.RotBlob(p, c, M(0.2), M(0.36), ang, Cream);
                return;
            }
            if (aktiv)
            {
                var puls = 1f + 0.1f * Mathf.Sin((float)_anim * 5f);
                P.Ring(p, c, M(1.3) * puls, M(0.12), new Color(1f, 0.83f, 0.3f, 0.75f));
                P.Ring(p, c, M(RcompRichterTonnen.RammAbstand), M(0.05), new Color(0.91f, 0.55f, 0.63f, 0.55f));
            }
            P.Circle(p, c + new Vector2(M(0.28), M(0.38)), M(0.62), Shadow);
            P.Circle(p, c, M(0.66), Outline);
            P.Circle(p, c, M(0.59), BarrelBlue);
            P.Ring(p, c, M(0.44), M(0.07), Cream);
            P.Circle(p, c + new Vector2(-M(0.17), -M(0.17)), M(0.15), new Color(1f, 1f, 1f, 0.45f));
        }

        /// <summary>A flag gate: two masts with pennants (drawn larger than life so they read across the meadow) and the
        /// line to ride through — gold for the next gate, sage once passed, rose when skipped.</summary>
        private void Gate(Painter2D p, RcompKurs.Tor tor, int zustand, int nummer)
        {
            if (!Near(tor.Pos, 8.0)) return;
            var halb = RcompRichterGelaende.TorHalbbreite;
            var a = tor.Pos + tor.Quer * halb;
            var b = tor.Pos - tor.Quer * halb;
            var farbe = zustand == 1 ? Gold : zustand == 2 ? MgScenery.Hex(0xBFD9B0) : zustand == 3 ? Rose : PoleRed;
            if (zustand == 1)
            {
                var puls = 1f + 0.12f * Mathf.Sin((float)_anim * 5f);
                P.Line(p, Px(a), Px(b), M(0.9) * puls, new Color(1f, 0.85f, 0.35f, 0.35f));
            }
            for (var i = 0; i < 6; i += 2) P.Line(p, Px(a + (b - a) * (i / 6f)), Px(a + (b - a) * ((i + 1) / 6f)), M(0.16), new Color(1f, 1f, 1f, zustand == 0 || zustand == 1 ? 0.75f : 0.35f));
            var weg = new V2(tor.Quer.Y, -tor.Quer.X);
            foreach (var (fuss, seite) in new[] { (a, 1.0), (b, -1.0) })
            {
                var c = Px(fuss);
                P.Circle(p, c + new Vector2(M(0.35), M(0.45)), M(0.42), Shadow);
                P.Circle(p, c, M(0.36), Outline);
                P.Circle(p, c, M(0.28), Cream);
                var wind = 0.15 * Math.Sin(_anim * 3.0 + nummer);
                P.Poly(p, new[] { c, Px(fuss + tor.Quer * (1.7 * seite) + weg * wind), Px(fuss + tor.Quer * (0.35 * seite) + weg * 0.95) }, Outline);
                P.Poly(p, new[] { c, Px(fuss + tor.Quer * (1.55 * seite) + weg * wind), Px(fuss + tor.Quer * (0.4 * seite) + weg * 0.82) }, farbe);
            }
        }

        private void Obstacle(Painter2D p, RcompKurs.Tor h, bool aktiv, bool vorbei, bool ab)
        {
            if (!Near(h.Pos, 4.0)) return;
            var a = h.Pos + h.Quer * 1.9;
            var b = h.Pos - h.Quer * 1.9;
            if (aktiv)
            {
                var richtung = new V2(Math.Sin(h.Heading), Math.Cos(h.Heading)) * -1.0;
                var vor = h.Pos - richtung * 1.1;
                P.Line(p, Px(vor + h.Quer * 1.5), Px(vor - h.Quer * 1.5), M(0.5), new Color(1f, 0.85f, 0.35f, 0.4f));
            }
            var off = ab ? new V2(0.5f, 0.3f) : V2.Zero;
            P.Line(p, Px(a) + new Vector2(M(0.15), M(0.2)), Px(b) + new Vector2(M(0.15), M(0.2)), M(0.16), Shadow);
            P.Line(p, Px(a + off), Px(b + off), M(0.14), vorbei && !ab ? MgScenery.Hex(0xF2B9B9) : PoleRed);
            for (var i = 1; i < 4; i += 2)
            {
                var s0 = a + (b - a) * (i / 4f) + off;
                var s1 = a + (b - a) * ((i + 1) / 4f) + off;
                P.Line(p, Px(s0), Px(s1), M(0.14), Cream);
            }
            foreach (var post in new[] { a, b })
            {
                var c = Px(post);
                var s = M(0.15);
                P.Rect(p, c - new Vector2(s, s), c + new Vector2(s, s), Wood);
            }
        }

        private void TrailStation(Painter2D p, RcompKurs.TrailStation st, bool aktiv, bool vorbei)
        {
            if (!Near(st.Pos, 6.0)) return;
            var entlang = new V2(Math.Cos(st.Winkel), Math.Sin(st.Winkel));
            var quer = RcompRichterTrail.Quer(st);
            var halb = RcompKurs.TrailLaengeM * 0.5;
            var farbe = aktiv ? Gold : vorbei ? MgScenery.Hex(0xD9C4A0) : Wood;
            if (st.Id == "plane") Quad4(p, st.Pos, entlang, quer, halb * 0.8, st.Breite * 0.9, MgScenery.Hex(0x7FB8FF));
            if (st.Id == "bruecke")
                for (var i = -3; i <= 3; i++)
                {
                    var c = st.Pos + entlang * (i * 0.8);
                    P.Line(p, Px(c + quer * st.Breite), Px(c - quer * st.Breite), M(0.5), i % 2 == 0 ? WoodDark : Wood);
                }
            if (st.Id == "slalom")
                for (var i = -1; i <= 1; i++)
                    P.Circle(p, Px(st.Pos + entlang * (i * 1.8) + quer * (i % 2 == 0 ? 0.0 : 0.0)), M(0.16), PoleRed);
            if (st.Id == "kreisel") P.Ring(p, Px(st.Pos), M(st.Breite * 0.8), M(0.08), Cream);
            foreach (var seite in new[] { -1.0, 1.0 })
            {
                var o = quer * ((st.Breite + 0.35) * seite);
                P.Line(p, Px(st.Pos - entlang * halb + o) + new Vector2(M(0.12), M(0.16)), Px(st.Pos + entlang * halb + o) + new Vector2(M(0.12), M(0.16)), M(0.2), Shadow);
                P.Line(p, Px(st.Pos - entlang * halb + o), Px(st.Pos + entlang * halb + o), M(0.18), farbe);
            }
        }

        private void Quad4(Painter2D p, V2 mitte, V2 entlang, V2 quer, double halbLaenge, double halbBreite, Color color) =>
            P.Poly(p, new[]
            {
                Px(mitte - entlang * halbLaenge - quer * halbBreite), Px(mitte + entlang * halbLaenge - quer * halbBreite),
                Px(mitte + entlang * halbLaenge + quer * halbBreite), Px(mitte - entlang * halbLaenge + quer * halbBreite),
            }, color);

        private void PaintDressage(Painter2D p, RcompRichterDressur d)
        {
            for (var f = 0; f < d.Figuren.Count; f++)
            {
                var figur = d.Figuren[f];
                var aktiv = f == d.FigurIdx;
                for (var i = 0; i < figur.Punkte.Count; i++)
                {
                    var pt = figur.Punkte[i];
                    if (aktiv && i + 1 < figur.Punkte.Count) P.Line(p, Px(pt), Px(figur.Punkte[i + 1]), M(0.12), new Color(1f, 0.95f, 0.75f, 0.55f));
                    var erledigt = f < d.FigurIdx || (aktiv && i < d.PunktIdx);
                    P.Circle(p, Px(pt), M(aktiv ? 0.26 : 0.18), erledigt ? MgScenery.Hex(0xBFD9B0) : aktiv ? MgScenery.Hex(0xFFE9A0) : MgScenery.Hex(0xF5E6C0));
                }
            }
        }

        private void PaintMarker(Painter2D p, RcompLauf lauf)
        {
            if (!(lauf.MarkerZiel() is V2 ziel) || lauf.Controller == null) return;
            var c = Px(ziel);
            var bob = Mathf.Sin((float)_anim * Mathf.PI * 1.6f) * M(0.18);
            var inside = c.x > 30f && c.x < W - 30f && c.y > 50f && c.y < H - 30f;
            if (inside)
            {
                var top = c + new Vector2(0f, -M(2.1) + bob);
                P.Poly(p, new[] { top + new Vector2(-M(0.4), -M(0.55)), top + new Vector2(M(0.4), -M(0.55)), top }, Outline);
                P.Poly(p, new[] { top + new Vector2(-M(0.32), -M(0.5)), top + new Vector2(M(0.32), -M(0.5)), top + new Vector2(0f, -M(0.08)) }, Gold);
                return;
            }
            // off screen: an arrow on the frame where the ray from the rider toward the target leaves the view
            var a = Anchor;
            var dir = (c - a).normalized;
            float x0 = 40f, x1 = W - 40f, y0 = 92f, y1 = H - 40f;
            var tx = dir.x > 1e-4f ? (x1 - a.x) / dir.x : dir.x < -1e-4f ? (x0 - a.x) / dir.x : float.MaxValue;
            var ty = dir.y > 1e-4f ? (y1 - a.y) / dir.y : dir.y < -1e-4f ? (y0 - a.y) / dir.y : float.MaxValue;
            var edge = a + dir * Mathf.Max(0f, Mathf.Min(tx, ty));
            var side = new Vector2(-dir.y, dir.x);
            P.Poly(p, new[] { edge + dir * 22f, edge - dir * 6f + side * 15f, edge - dir * 6f - side * 15f }, Outline);
            P.Poly(p, new[] { edge + dir * 18f, edge - dir * 3f + side * 11f, edge - dir * 3f - side * 11f }, Gold);
        }

        private void PaintGhost(Painter2D p, RcompLauf lauf)
        {
            if (!(lauf.GeistZustand() is RanchCompGhost.Zustand g)) return;
            PaintHorse(p, g.Pos, g.Heading, g.Y, (new Color(0.72f, 0.86f, 0.98f, 0.55f), new Color(0.5f, 0.66f, 0.85f, 0.55f)), g.Gangart, 0.55f, false);
        }

        private void PaintShow(Painter2D p, RcompLauf lauf)
        {
            var aktion = lauf.Richter is RcompRichterSchau s && s.Angesagt ? Mathf.Sin((float)_anim * 8f) * 0.15f : 0f;
            PaintHorse(p, new V2(0f, 0f), Math.PI + aktion, 0.0, FellVon(lauf.Farbe), "stand", 1.25f, false);
            var g = Px(1.6, 1.2);
            P.Circle(p, g + new Vector2(M(0.12), M(0.16)), M(0.42), Shadow);
            PaintGooby(p, g, -Mathf.PI * 0.5f, M(1.3), Mathf.Sin((float)_anim * 3f) * 0.08f);
        }

        private void PaintHorse(Painter2D p, V2 pos, double heading, double y, (Color hell, Color dunkel) fell, string gait, float alpha, bool rider) =>
            RidePaint.Horse(p, this, pos, heading, y, fell, gait, alpha, rider);

        private void PaintBotRider(Painter2D p, V2 pos, double heading, Color jacke) => RidePaint.Rider(p, this, pos, heading, jacke);

        private static void PaintGooby(Painter2D p, Vector2 g, float ang, float scale, float wobble) => RidePaint.Gooby(p, g, ang, scale, wobble);

        float IRideCam.M(double metres) => M(metres);
        float IRideCam.ScreenAngle(double heading) => ScreenAngle(heading);
        bool IRideCam.Near(V2 world, double margin) => Near(world, margin);
        double IRideCam.Anim => _anim;

        private void SpawnDust(RideController c, double dt)
        {
            var anteil = RideFeel.StaubAnteil(c.Gait);
            if (anteil <= 0.0 || c.InLuft || !c.Active) return;
            _puffAccu += dt * 14.0 * anteil;
            while (_puffAccu >= 1.0)
            {
                _puffAccu -= 1.0;
                var hinten = c.Pos - c.Vorwaerts * 1.2;
                var seite = new V2(Math.Cos(c.Heading), -Math.Sin(c.Heading)) * ((_puffs.Count % 2 == 0 ? 1.0 : -1.0) * 0.35);
                _puffs.Add((hinten + seite, 0.0, (float)(0.22 + 0.18 * anteil)));
                if (_puffs.Count > 40) _puffs.RemoveAt(0);
            }
        }
    }
}
