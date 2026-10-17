using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Minigames.Ranch;
using Gooby.Unity.App;
using Gooby.Unity.World;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Building the ranch (port of ranch_bau_mode.gd, mobile first): the farm's build ground in 3D (<see cref="RanchBau3D"/>)
    /// with a drawer of facilities, decoration, paths and fences at the bottom. Picking a piece puts its ghost on the
    /// nearest free spot — tap or drag it where it should go, turn it, then "Bauen"; paths are painted with the finger,
    /// fences drawn from corner to corner, several at once. Tapping a placed piece shows what it does at each level, its
    /// next upgrade, moving (free) and tearing it down (half back). Locked zones carry their price as a chip. The world
    /// stays in view: the drawer folds away while placing. Every booking goes through <see cref="RanchBauState"/>.
    /// </summary>
    public sealed partial class RanchBauScreen : UiScreen
    {
        private enum Modus { Schauen, Setzen, Linie, Verschieben }

        private readonly GameHost _host;
        private readonly Juice _juice = new Juice();
        private RanchBau3D _welt;
        private RanchWetterView _wetter;
        private VisualElement _field;
        private RanchGridData _grid;
        private JObject _bau;
        private Modus _modus;
        private RanchBauDef _def;
        private Zelle _ghostAt;
        private int _ghostRot;
        private string _auswahlUid, _verschiebenUid;
        private readonly List<Zelle> _linieZellen = new List<Zelle>();
        private readonly List<(Zelle cell, string seite)> _linieKanten = new List<(Zelle, string)>();
        private readonly List<(Zelle cell, string seite)> _strich = new List<(Zelle, string)>();
        private readonly Dictionary<int, Vector2> _zeiger = new Dictionary<int, Vector2>();
        private int _hauptZeiger = -1;
        private Vector2 _downPos, _lastPos;
        private bool _gezogen, _ziehtGhost, _malt, _radiert, _schwenkt, _weltKamera = true;
        private (int x, int z) _strichStart;
        private float _pinchAbstand;

        public RanchBauScreen(GameHost host)
        {
            _host = host;
            Id = "ranchbau";
            Opaque = true;
        }

        public override string SoundScene => _wetter?.Scene ?? "ranch/bau";

        private JObject State => _host.Store.State;
        private string T(string key, params (string, object)[] args) => _host.Strings.T(key, args);
        private static JObject Bal => RanchBauKatalog.Balance;
        private int Coins => J.Int(J.Path(State, "economy", "coins"));
        public RanchBau3D Welt => _welt;

        protected override void Build(VisualElement root)
        {
            root.AddToClassList("gb-rbau");
            _field = new VisualElement { name = "rbau-field" };
            Ux.Abs(_field, 0, 0, 0, 0);
            _field.style.backgroundColor = ArtLibrary.Hex("#BFE3F7");
            _field.RegisterCallback<PointerDownEvent>(Down);
            _field.RegisterCallback<PointerMoveEvent>(Bewegt);
            _field.RegisterCallback<PointerUpEvent>(Hoch);
            _field.RegisterCallback<PointerCancelEvent>(e => Loslassen(e.pointerId));
            _field.RegisterCallback<WheelEvent>(e => { _welt?.Zoom(e.delta.y > 0 ? 1.12f : 1f / 1.12f); e.StopPropagation(); });
            root.Add(_field);
            var migriert = false;
            var geliefert = new List<string>();
            _host.Store.Update(s =>
            {
                migriert = RanchBauState.MigriereBestand(s);
                geliefert = RanchBelohnungen.Liefern(s);
            }, GameHost.TicksMs);
            if (migriert || geliefert.Count > 0) Melde();
            Lade();
            if (MgStage3D.Supported)
            {
                _welt = new RanchBau3D(_field) { Schleifen = RanchBelohnungen.Wandschleifen(State) };
                _welt.Sync(_grid, _bau, Bal);
                WeltKamera(false);
            }
            _wetter = new RanchWetterView(_host, "hof", "bau", null, _field);
            _wetter.Refresh();
            BaueUi(root);
            root.Add(_juice.Layer);
            RefreshAll();
            if (migriert && RanchBauState.AnlageStufe(_bau, "stallboxen") > 0)
                root.schedule.Execute(() => _host.Ui.Toast(T("rbau.ui.migriert"), "hammer", 4f)).StartingIn(700);
            Debug.Log("[ranchbau] auf: teile=" + _grid.Items.Count + " zonen=" + string.Join(",", ((JArray)_bau["zonen"]).Select(t => J.Str(t))) + (migriert ? " (migriert)" : ""));
        }

        private void Lade()
        {
            _bau = RanchBauState.Lese(State);
            _grid = RanchBauState.GridAus(_bau, Bal).grid;
        }

        private void Melde()
        {
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            _host.Store.NotifySliceChanged("economy");
        }

        /// <summary>After any booking: the save into the grid, the grid into the 3D, the HUD up to date.</summary>
        private void Neu()
        {
            Melde();
            Lade();
            _welt?.Sync(_grid, _bau, Bal);
            RefreshAll();
        }

        private void WeltKamera(bool on)
        {
            var cam = _host.World != null ? _host.World.Camera : null;
            if (cam == null) return;
            if (!on) _weltKamera = cam.enabled;
            cam.enabled = on && _weltKamera;
        }

        private float FeldW => _field.layout.width;
        private float FeldH => _field.layout.height;

        private bool AufBoden(Vector2 feld, out double gx, out double gz)
        {
            gx = gz = 0;
            return _welt != null && _welt.Boden(feld, FeldW, FeldH, out gx, out gz);
        }

        // ── pointers: tap, drag the ghost, paint, pan, pinch ──

        private void Down(PointerDownEvent e)
        {
            Vector2 pos = e.localPosition;
            _zeiger[e.pointerId] = pos;
            _field.CapturePointer(e.pointerId);
            if (_zeiger.Count == 2)
            {
                // a second finger: pinch and pan instead of whatever the first one started
                AbbruchStrich();
                _ziehtGhost = false;
                _gezogen = true;
                var p = _zeiger.Values.ToArray();
                _pinchAbstand = Vector2.Distance(p[0], p[1]);
                _lastPos = (p[0] + p[1]) * 0.5f;
                return;
            }
            if (_zeiger.Count > 2) return;
            _hauptZeiger = e.pointerId;
            _downPos = _lastPos = pos;
            _gezogen = false;
            _ziehtGhost = false;
            _malt = false;
            _schwenkt = e.button == 1 || e.button == 2;
            if (_schwenkt || !AufBoden(pos, out var gx, out var gz)) return;
            if (_modus == Modus.Setzen || _modus == Modus.Verschieben)
            {
                var c = RanchGridData.CellOf(gx, gz);
                var fp = RanchGridData.RotatedFootprint(_def.Footprint, _ghostRot);
                // grabbing the ghost itself (or right beside it) drags it; anywhere else drags the view
                _ziehtGhost = c.X >= _ghostAt.X - 1 && c.X < _ghostAt.X + fp.X + 1 && c.Y >= _ghostAt.Y - 1 && c.Y < _ghostAt.Y + fp.Y + 1;
            }
            else if (_modus == Modus.Linie)
            {
                _malt = true;
                if (_def.Kante) _strichStart = ((int)Math.Round(gx / RanchGridData.CellSize), (int)Math.Round(gz / RanchGridData.CellSize));
                else
                {
                    var c = RanchGridData.CellOf(gx, gz);
                    _radiert = _linieZellen.Contains(c);
                    Male(c);
                }
            }
        }

        private void Bewegt(PointerMoveEvent e)
        {
            if (!_zeiger.ContainsKey(e.pointerId)) return;
            Vector2 pos = e.localPosition;
            _zeiger[e.pointerId] = pos;
            if (_zeiger.Count >= 2)
            {
                var p = _zeiger.Values.Take(2).ToArray();
                var abstand = Vector2.Distance(p[0], p[1]);
                var mitte = (p[0] + p[1]) * 0.5f;
                if (_pinchAbstand > 10f && abstand > 10f) _welt?.Zoom(_pinchAbstand / abstand);
                _welt?.Pan(mitte - _lastPos, FeldH);
                _pinchAbstand = abstand;
                _lastPos = mitte;
                return;
            }
            if (e.pointerId != _hauptZeiger) return;
            if (!_gezogen && (pos - _downPos).sqrMagnitude > 64f) _gezogen = true;
            if (_ziehtGhost)
            {
                if (AufBoden(pos, out var gx, out var gz)) SetzeGhost(AnkerUnter(gx, gz));
            }
            else if (_malt)
            {
                if (AufBoden(pos, out var gx, out var gz))
                {
                    if (_def.Kante) Strich(gx, gz);
                    else MaleBis(gx, gz);
                }
            }
            else if (_gezogen) _welt?.Pan(pos - _lastPos, FeldH);
            _lastPos = pos;
        }

        private void Hoch(PointerUpEvent e)
        {
            if (!_zeiger.ContainsKey(e.pointerId)) return;
            var tippen = e.pointerId == _hauptZeiger && _zeiger.Count == 1 && !_gezogen && !_schwenkt;
            Vector2 pos = e.localPosition;
            if (e.pointerId == _hauptZeiger && _malt && _def != null && _def.Kante)
            {
                if (_strich.Count > 0) UebernimmStrich();
                else if (tippen && AufBoden(pos, out var gx, out var gz)) KanteUmschalten(RanchGridData.NearestKante(gx, gz));
                else AbbruchStrich();
            }
            else if (tippen && !_malt) Tippen(pos);
            Loslassen(e.pointerId);
        }

        private void Loslassen(int pointerId)
        {
            if (_field.HasPointerCapture(pointerId)) _field.ReleasePointer(pointerId);
            _zeiger.Remove(pointerId);
            if (pointerId == _hauptZeiger || _zeiger.Count == 0)
            {
                _hauptZeiger = -1;
                _malt = false;
                _ziehtGhost = false;
            }
            if (_zeiger.Count == 1)
            {
                // one finger stays after a pinch: it pans from where it is now
                var rest = _zeiger.First();
                _hauptZeiger = rest.Key;
                _lastPos = _downPos = rest.Value;
                _gezogen = true;
            }
        }

        private void Tippen(Vector2 pos)
        {
            if (!AufBoden(pos, out var gx, out var gz)) return;
            switch (_modus)
            {
                case Modus.Setzen:
                case Modus.Verschieben:
                    SetzeGhost(AnkerUnter(gx, gz));
                    Sfx.PlayUi("soft_tick");
                    break;
                case Modus.Linie:
                    break;
                default:
                    TippeWelt(gx, gz);
                    break;
            }
        }

        /// <summary>Looking around: a tap picks a fence (near its edge), then a building or decoration, then a path; a locked cell offers its zone.</summary>
        private void TippeWelt(double gx, double gz)
        {
            var uid = UidBei(gx, gz);
            if (uid.Length > 0) { Waehle(uid); return; }
            var c = RanchGridData.CellOf(gx, gz);
            if (_grid.InBounds(c) && !_grid.IstFrei(c))
            {
                var zone = RanchBauKatalog.Zonen(Bal).FirstOrDefault(z => z.Rect.Enthaelt(c) && !RanchBauState.ZoneFrei(_bau, z.Id));
                if (zone != null) { FrageZone(zone.Id); return; }
            }
            Waehle(null);
        }

        private string UidBei(double gx, double gz)
        {
            var kante = RanchGridData.NearestKante(gx, gz);
            var kantenUid = _grid.KanteItemAt(kante.cell, kante.seite);
            if (kantenUid.Length > 0)
            {
                var w = RanchGridData.KanteWorld(kante.cell, kante.seite);
                if (Math.Sqrt((gx - w.x) * (gx - w.x) + (gz - w.z) * (gz - w.z)) <= 0.9) return kantenUid;
            }
            var c = RanchGridData.CellOf(gx, gz);
            var uid = _grid.ItemAt(c, RanchGridData.Layer.Objekt);
            return uid.Length > 0 ? uid : _grid.ItemAt(c, RanchGridData.Layer.Boden);
        }

        // ── placing one piece ──

        private RanchBauDef Def(string id) => RanchBauKatalog.Def(Bal, id);

        private static string UidVon(JObject bau, string itemId) =>
            (bau["items"] as JArray ?? new JArray()).OfType<JObject>().Where(e => J.Str(e["item"]) == itemId).Select(e => J.Str(e["uid"])).FirstOrDefault();

        /// <summary>A card of the drawer: a built facility is shown and selected, anything else gets its ghost.</summary>
        private void Karte(string id)
        {
            var def = Def(id);
            if (def == null) return;
            Haptics.Tap();
            if (def.Upgrade) { FrageWeidezaun(); return; }
            if (def.Belohnung && RanchBauState.LagerAnzahl(_bau, id) <= 0 && UidVon(_bau, id) == null)
            {
                // still to earn: the card says where
                var quelle = RanchBelohnungen.Quelle(RanchBelohnungen.VonZiel(id)?.Id);
                Sfx.PlayUi("soft_tap");
                _host.Ui.Toast(quelle != null ? T("rquest.ui.frei_quelle", ("quest", RanchQuestUi.Titel(_host, quelle)), ("name", RanchQuestUi.Name(_host, quelle.Geber))) : T("rbau.fehler.nurBelohnung"), "star", 3.5f);
                return;
            }
            if ((def.Kategorie == "anlage" && RanchBauState.AnlageStufe(_bau, id) > 0) || (def.Belohnung && RanchBauState.LagerAnzahl(_bau, id) <= 0))
            {
                var uid = UidVon(_bau, id);
                if (uid != null)
                {
                    Waehle(uid);
                    var item = _grid.GetItem(uid);
                    if (item != null) { var c = RanchGridData.WorldCenter(item.At, item.Def.Footprint, item.Rot); _welt?.Fokus(c.x, c.z); }
                    return;
                }
            }
            StarteSetzen(def);
        }

        private void StarteSetzen(RanchBauDef def)
        {
            BeendeModus(false);
            Waehle(null);
            _def = def;
            _ghostRot = 0;
            if (def.Kante || def.Kategorie == "boden")
            {
                _modus = Modus.Linie;
                _linieZellen.Clear();
                _linieKanten.Clear();
            }
            else
            {
                _modus = Modus.Setzen;
                SetzeGhost(FreierPlatz(def, 0));
            }
            Sfx.PlayUi("soft_open");
            RefreshModus();
            Debug.Log("[ranchbau] waehle " + def.Id + " → " + _modus);
        }

        /// <summary>The free spot for a new piece nearest to the middle of the view, or to <paramref name="nahe"/> (cells in rings around it).</summary>
        private Zelle FreierPlatz(RanchBauDef def, int rot, Zelle? nahe = null)
        {
            var mitte = nahe ?? new Zelle(7, 10);
            if (nahe == null && AufBoden(new Vector2(FeldW * 0.5f, FeldH * 0.45f), out var gx, out var gz)) mitte = AnkerUnter(gx, gz, def, rot);
            for (var r = 0; r < 16; r++)
                for (var dy = -r; dy <= r; dy++)
                    for (var dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                        var at = new Zelle(mitte.X + dx, mitte.Y + dy);
                        if (_grid.CanPlace(def, at, rot, _verschiebenUid ?? "").ok) return at;
                    }
            return mitte;
        }

        private Zelle AnkerUnter(double gx, double gz) => AnkerUnter(gx, gz, _def, _ghostRot);

        /// <summary>The anchor that centres the footprint on a ground point.</summary>
        private static Zelle AnkerUnter(double gx, double gz, RanchBauDef def, int rot)
        {
            var fp = RanchGridData.RotatedFootprint(def.Footprint, rot);
            return new Zelle((int)Math.Round(gx / RanchGridData.CellSize - fp.X * 0.5), (int)Math.Round(gz / RanchGridData.CellSize - fp.Y * 0.5));
        }

        private bool GhostOk => _def != null && _grid.CanPlace(_def, _ghostAt, _ghostRot, _verschiebenUid ?? "").ok;

        private void SetzeGhost(Zelle at)
        {
            if (_def == null) return;
            var neu = at != _ghostAt;
            _ghostAt = at;
            var stufe = _modus == Modus.Verschieben ? Math.Max(1, RanchBauState.AnlageStufe(_bau, _def.Id)) : 1;
            _welt?.GhostZelle(_def, stufe, _ghostAt, _ghostRot, GhostOk);
            if (neu && _ziehtGhost) Sfx.PlayUi("soft_tick");
            RefreshAktion();
        }

        private void Drehen()
        {
            if (_def == null || _def.Kante || (_modus != Modus.Setzen && _modus != Modus.Verschieben)) return;
            var fp = RanchGridData.RotatedFootprint(_def.Footprint, _ghostRot);
            var mitte = (x: _ghostAt.X + fp.X * 0.5, z: _ghostAt.Y + fp.Y * 0.5);
            _ghostRot = (_ghostRot + 1) % 4;
            var neu = RanchGridData.RotatedFootprint(_def.Footprint, _ghostRot);
            SetzeGhost(new Zelle((int)Math.Round(mitte.x - neu.X * 0.5), (int)Math.Round(mitte.z - neu.Y * 0.5)));
            Sfx.PlayUi("soft_toggle");
        }

        private int Preis => _def == null ? 0 : _modus == Modus.Verschieben ? 0 : RanchBauState.LagerAnzahl(_bau, _def.Id) > 0 ? 0 : _def.Kosten;

        private void Bauen()
        {
            if (_def == null) return;
            if (_modus == Modus.Linie) { BaueLinie(); return; }
            var def = _def;
            RanchBauState.Ergebnis res = null;
            var stilVorher = RanchBauEffekte.DekoStilpunkte(_bau, Bal);
            if (_modus == Modus.Verschieben)
            {
                var uid = _verschiebenUid;
                _host.Store.Update(s => res = RanchBauState.Verschiebe(s, uid, _ghostAt, _ghostRot), GameHost.TicksMs);
                if (!res.Ok) { Fehler(res.Fehler); return; }
                BeendeModus(false);
                Neu();
                _welt?.Pop(uid);
                Sfx.PlayUi("soft_confirm");
                Sfx.Play("build_hammer", 0f, 0.5f);
                _host.Ui.Toast(T("rbau.ui.verschoben"), "move");
                Waehle(uid);
                Debug.Log("[ranchbau] verschoben " + uid + " → " + _ghostAt + " rot " + _ghostRot);
                return;
            }
            var at = _ghostAt;
            var rot = _ghostRot;
            _host.Store.Update(s => res = RanchBauState.Platziere(s, def.Id, at, rot), GameHost.TicksMs);
            if (!res.Ok) { Fehler(res.Fehler); return; }
            Neu();
            Gebaut(def, res.Uid, res.Kosten);
            if (def.Kategorie == "anlage")
            {
                BeendeModus(false);
                Waehle(res.Uid);
            }
            else
            {
                // decoration in a row: the ghost moves on to the next free spot beside the last one
                _welt?.GhostWeg();
                SetzeGhost(FreierPlatz(def, rot, at));
            }
            var stil = RanchBauEffekte.DekoStilpunkte(_bau, Bal);
            if (stil > stilVorher)
                Root.schedule.Execute(() =>
                {
                    Sfx.PlayUi("soft_sticker");
                    _host.Ui.Toast(T("rbau.ui.stil_punkt", ("n", stil), ("max", RanchBauEffekte.DekoStilMax(Bal))), "sparkle");
                }).StartingIn(600);
            Debug.Log("[ranchbau] gebaut " + def.Id + " " + res.Uid + " @" + at + " rot " + rot + " -" + res.Kosten);
        }

        private void Gebaut(RanchBauDef def, string uid, int kosten)
        {
            _welt?.Pop(uid);
            Sfx.Play("build_hammer", 0f, 0.8f);
            Sfx.PlayUi(kosten > 0 ? "soft_buy" : "soft_confirm");
            Haptics.Success();
            var item = _grid.GetItem(uid);
            if (item != null && _welt != null && kosten > 0)
            {
                var c = Mitte(item);
                _host.Ui.FloatText(_welt.ToField(c.x, 3.0, c.z, FeldW, FeldH), "-" + kosten, new Color(0.98f, 0.8f, 0.3f), 34f);
            }
            if (def.Kategorie == "anlage")
            {
                _juice.Confetti(40);
                _host.Ui.Toast(T("rbau.gebaut") + " " + T(def.NameKey), "hammer");
            }
        }

        /// <summary>Grid metres of a piece's middle (a fence: its edge's).</summary>
        private static (double x, double z) Mitte(RanchGridData.Item item)
        {
            if (item.Kante == null) return RanchGridData.WorldCenter(item.At, item.Def.Footprint, item.Rot);
            var w = RanchGridData.KanteWorld(item.At, item.Kante);
            return (w.x, w.z);
        }

        private void Fehler(string fehler)
        {
            Sfx.PlayUi("soft_error");
            Haptics.Tap();
            _host.Ui.Toast(T("rbau.fehler." + fehler), "hammer");
            if (_bauenBtn != null) _juice.ScalePop(_bauenBtn, 1.12f, 0.2f);
            Debug.Log("[ranchbau] fehler " + fehler);
        }

        private void Abbrechen()
        {
            if (_modus == Modus.Schauen) { Waehle(null); return; }
            var uid = _verschiebenUid;
            BeendeModus(true);
            if (uid != null) Waehle(uid);
            Sfx.PlayUi("soft_back");
        }

        private void BeendeModus(bool abgebrochen)
        {
            _modus = Modus.Schauen;
            _def = null;
            _linieZellen.Clear();
            _linieKanten.Clear();
            _strich.Clear();
            _welt?.GhostWeg();
            _welt?.VorschauWeg();
            if (_verschiebenUid != null) _welt?.Verstecke(null);
            _verschiebenUid = null;
            RefreshModus();
        }

        // ── moving a placed piece ──

        private void Verschieben()
        {
            var item = _grid.GetItem(_auswahlUid);
            if (item == null || item.Kante != null) return;
            var uid = item.Uid;
            Waehle(null);
            _modus = Modus.Verschieben;
            _def = item.Def;
            _verschiebenUid = uid;
            _ghostRot = item.Rot;
            _welt?.Verstecke(uid);
            SetzeGhost(item.At);
            Sfx.PlayUi("soft_open");
            RefreshModus();
        }

        // ── paths and fences: painted and drawn ──

        private void Male(Zelle c)
        {
            if (!_grid.InBounds(c)) return;
            if (_radiert) { if (!_linieZellen.Remove(c)) return; }
            else
            {
                if (_linieZellen.Contains(c)) return;
                _linieZellen.Add(c);
                Sfx.PlayUi("soft_tick");
            }
            ZeigeLinie();
        }

        /// <summary>Fills every cell between the last painted one and the finger (no gaps at speed).</summary>
        private void MaleBis(double gx, double gz)
        {
            var ziel = RanchGridData.CellOf(gx, gz);
            var von = _linieZellen.Count > 0 && !_radiert ? _linieZellen[_linieZellen.Count - 1] : ziel;
            var schritte = Math.Max(Math.Abs(ziel.X - von.X), Math.Abs(ziel.Y - von.Y));
            for (var i = 1; i <= schritte; i++)
                Male(new Zelle(von.X + (int)Math.Round((ziel.X - von.X) * i / (double)schritte), von.Y + (int)Math.Round((ziel.Y - von.Y) * i / (double)schritte)));
            if (schritte == 0) Male(ziel);
        }

        /// <summary>A fence stroke from the start corner straight along the longer axis to the corner under the finger.</summary>
        private void Strich(double gx, double gz)
        {
            var ex = (int)Math.Round(gx / RanchGridData.CellSize);
            var ez = (int)Math.Round(gz / RanchGridData.CellSize);
            _strich.Clear();
            var (sx, sz) = _strichStart;
            if (Math.Abs(ex - sx) >= Math.Abs(ez - sz))
                for (var x = Math.Min(sx, ex); x < Math.Max(sx, ex); x++) _strich.Add((new Zelle(x, sz), "N"));
            else
                for (var z = Math.Min(sz, ez); z < Math.Max(sz, ez); z++) _strich.Add((new Zelle(sx, z), "W"));
            ZeigeLinie();
        }

        private void UebernimmStrich()
        {
            foreach (var k in _strich)
                if (!_linieKanten.Contains(k)) _linieKanten.Add(k);
            _strich.Clear();
            Sfx.PlayUi("soft_tick");
            ZeigeLinie();
        }

        private void AbbruchStrich()
        {
            if (_strich.Count == 0) return;
            _strich.Clear();
            ZeigeLinie();
        }

        private void KanteUmschalten((Zelle cell, string seite) k)
        {
            if (!_linieKanten.Remove(k)) _linieKanten.Add(k);
            Sfx.PlayUi("soft_tick");
            ZeigeLinie();
        }

        private List<(Zelle cell, bool ok)> LinieZellen() => _linieZellen.Select(c => (c, _grid.CanPlace(_def, c, 0).ok)).ToList();

        private List<(Zelle cell, string seite, bool ok)> LinieKanten() =>
            _linieKanten.Concat(_strich.Where(k => !_linieKanten.Contains(k))).Select(k => (k.cell, k.seite, _grid.CanPlaceKante(_def, k.cell, k.seite).ok)).ToList();

        private int LinieAnzahl => _def == null ? 0 : _def.Kante ? LinieKanten().Count(k => k.ok) : LinieZellen().Count(c => c.ok);

        private void ZeigeLinie()
        {
            if (_welt != null && _def != null)
            {
                if (_def.Kante) _welt.VorschauKanten(RanchBauState.AnlageStufe(_bau, "weidezaun"), LinieKanten());
                else _welt.Vorschau(_def, LinieZellen());
            }
            RefreshAktion();
        }

        private void BaueLinie()
        {
            var def = _def;
            var kanten = def.Kante ? LinieKanten().Where(k => k.ok).Select(k => (k.cell, k.seite)).ToList() : null;
            var zellen = def.Kante ? null : LinieZellen().Where(c => c.ok).Select(c => c.cell).ToList();
            if ((kanten?.Count ?? zellen.Count) == 0) { Fehler(RanchGridData.ReasonOccupied); return; }
            var gebaut = new List<string>();
            var zuTeuer = 0;
            var kosten = 0;
            _host.Store.Update(s =>
            {
                foreach (var (cell, seite) in kanten ?? new List<(Zelle, string)>())
                {
                    var r = RanchBauState.PlatziereKante(s, def.Id, cell, seite);
                    if (r.Ok) { gebaut.Add(r.Uid); kosten += r.Kosten; }
                    else if (r.Fehler == RanchBauState.FehlerZuTeuer) zuTeuer++;
                }
                foreach (var cell in zellen ?? new List<Zelle>())
                {
                    var r = RanchBauState.Platziere(s, def.Id, cell, 0);
                    if (r.Ok) { gebaut.Add(r.Uid); kosten += r.Kosten; }
                    else if (r.Fehler == RanchBauState.FehlerZuTeuer) zuTeuer++;
                }
            }, GameHost.TicksMs);
            if (gebaut.Count == 0) { Fehler(RanchBauState.FehlerZuTeuer); return; }
            _linieZellen.Clear();
            _linieKanten.Clear();
            Neu();
            foreach (var uid in gebaut.Take(24)) _welt?.Pop(uid);
            Sfx.Play("build_hammer", 0f, 0.8f);
            Sfx.PlayUi("soft_buy");
            Haptics.Success();
            _host.Ui.Toast(zuTeuer > 0 ? T("rbau.ui.teils", ("n", gebaut.Count)) : T("rbau.ui.gebaut_n", ("n", gebaut.Count)), def.Kante ? "fence" : "path");
            ZeigeLinie();
            Debug.Log("[ranchbau] linie " + def.Id + " ×" + gebaut.Count + " -" + kosten + (zuTeuer > 0 ? " (zu teuer: " + zuTeuer + ")" : ""));
        }

        // ── upgrades, demolition, zones ──

        private void Ausbauen()
        {
            var item = _grid.GetItem(_auswahlUid);
            if (item == null) return;
            var id = item.Def.Id;
            var uid = item.Uid;
            RanchBauState.Ergebnis res = null;
            _host.Store.Update(s => res = RanchBauState.Ausbauen(s, id), GameHost.TicksMs);
            if (!res.Ok) { Fehler(res.Fehler); return; }
            Neu();
            _welt?.Pop(uid, true);
            Sfx.PlayUi("soft_levelup");
            Sfx.Play("build_hammer", 0f, 0.7f);
            Haptics.Success();
            _juice.Confetti(70);
            _host.Ui.Toast(T("rbau.ausgebaut", ("name", T(item.Def.NameKey)), ("n", res.Stufe)), "star");
            Waehle(uid);
            Debug.Log("[ranchbau] ausgebaut " + id + " → " + res.Stufe + " -" + res.Kosten);
        }

        private void Abreissen(string uid)
        {
            var item = _grid.GetItem(uid);
            if (item == null) return;
            var mitte = Mitte(item);
            RanchBauState.Ergebnis res = null;
            _host.Store.Update(s => res = RanchBauState.Entferne(s, uid), GameHost.TicksMs);
            if (!res.Ok) { Fehler(res.Fehler); return; }
            Waehle(null);
            Neu();
            _welt?.Puff(mitte.x, mitte.z, 14, 2.2f);
            Sfx.PlayUi("soft_back");
            if (res.Erstattung > 0 && _welt != null)
            {
                var from = _welt.ToField(mitte.x, 2.0, mitte.z, FeldW, FeldH);
                _host.Ui.FlyIcons(from, _coinPill, "coin", Math.Min(8, Math.Max(2, res.Erstattung / 40)), _ => Sfx.PlayUi("soft_collect"));
            }
            if (res.InsLager) _host.Ui.Toast(T("rbau.ui.ins_lager", ("name", T(item.Def.NameKey))), "star");
            else _host.Ui.Toast(T("rbau.abriss_erstattung", ("n", res.Erstattung)), "coin");
            Debug.Log("[ranchbau] abriss " + uid + " +" + res.Erstattung + (res.InsLager ? " (ins Lager)" : ""));
        }

        private void ZoneFrei(string zoneId)
        {
            RanchBauState.Ergebnis res = null;
            _host.Store.Update(s => res = RanchBauState.ZoneFreischalten(s, zoneId), GameHost.TicksMs);
            if (!res.Ok) { Fehler(res.Fehler); return; }
            Neu();
            var zone = RanchBauKatalog.ZoneVon(Bal, zoneId);
            var cx = (zone.Rect.X + zone.Rect.W * 0.5) * RanchGridData.CellSize;
            var cz = (zone.Rect.Y + zone.Rect.H * 0.5) * RanchGridData.CellSize;
            _welt?.Fokus(cx, cz);
            _welt?.Funkeln(cx, cz);
            Sfx.PlayUi("soft_win");
            Haptics.Success();
            _juice.Confetti(80);
            _host.Ui.Toast(T("rbau.zone_frei", ("name", T("rbau.zone." + zoneId))), "sparkle", 3.5f);
            Debug.Log("[ranchbau] zone " + zoneId + " -" + res.Kosten);
        }

        private void WeidezaunAusbauen()
        {
            RanchBauState.Ergebnis res = null;
            _host.Store.Update(s => res = RanchBauState.Ausbauen(s, "weidezaun"), GameHost.TicksMs);
            if (!res.Ok) { Fehler(res.Fehler); return; }
            Neu();
            foreach (var item in _grid.Items.Values.Where(i => i.Kante != null).Take(20)) _welt?.Pop(item.Uid);
            Sfx.PlayUi("soft_levelup");
            _juice.Confetti(50);
            _host.Ui.Toast(T("rbau.ausgebaut", ("name", T("rbau.item.weidezaun")), ("n", res.Stufe)), "fence");
            Debug.Log("[ranchbau] weidezaun → " + res.Stufe);
        }

        // ── per frame ──

        public override void Update()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _juice.Tick(dt);
            _welt?.Tick(dt);
            _wetter?.Tick(dt);
            PlatziereChips();
            Tasten();
        }

        private void Tasten()
        {
            if (_welt == null || _modal != null) return;
            if (Input.GetKeyDown(KeyCode.R)) Drehen();
            if (Input.GetKeyDown(KeyCode.Escape)) Abbrechen();
            if (Input.GetKeyDown(KeyCode.Q)) Kamera(-1);
            if (Input.GetKeyDown(KeyCode.E)) Kamera(1);
            var pan = Vector2.zero;
            if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) pan.x += 1f;
            if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) pan.x -= 1f;
            if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W)) pan.y += 1f;
            if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S)) pan.y -= 1f;
            if (pan != Vector2.zero) _welt.Pan(pan * 600f * Time.unscaledDeltaTime, FeldH);
        }

        private void Kamera(int richtung)
        {
            _welt?.Drehe(richtung);
            Sfx.PlayUi("soft_toggle");
        }

        public override void OnShown()
        {
            if (Root == null) return;
            _welt?.SetVisible(true);
            _wetter?.SetActive(true);
            WeltKamera(false);
            Lade();
            _welt?.Sync(_grid, _bau, Bal);
            RefreshAll();
        }

        private void Quit()
        {
            Sfx.PlayUi("soft_back");
            if (_host.Ui.Current == this) _host.Ui.Pop();
        }

        public override void OnHidden()
        {
            foreach (var id in _zeiger.Keys.ToList()) Loslassen(id);
            if (_host.Ui.IsMounted(this))
            {
                _welt?.SetVisible(false);
                _wetter?.SetActive(false);
                WeltKamera(true);
                return;
            }
            _wetter?.Destroy();
            _welt?.Destroy();
            _welt = null;
            WeltKamera(true);
        }
    }
}
