using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Minigames.Ranch;
using Gooby.Unity.App;
using Gooby.Unity.World;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using P = Gooby.Unity.UI.Screens.MgPaint;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The explorer map of the ranch region (port of ranch_karte_screen.gd over <see cref="RanchEntdeckerKarte"/>): a
    /// parchment card with every zone — ridden ones in their mood pastel and named, the rest under a soft fog with "???"
    /// — the lakes, the brook, the known paths, the sixteen places (found ones with their icon and a NEU badge the first
    /// time, hidden ones as "?" on a coarse grid), and you as a pulsing dot with your heading. A tap on a place tells its
    /// story or the "Dahin!" hint; a ridden zone or a found place offers "Dahin reiten" (the autopilot along the paths).
    /// </summary>
    public sealed class RanchKarteView : VisualElement
    {
        private readonly GameHost _host;
        private readonly Func<(double x, double z, float heading)> _spieler;
        private readonly Action<string, string> _dahin;
        private readonly P.Canvas _canvas;
        private readonly VisualElement _feld, _ich, _detail;
        private readonly Label _fortschritt;
        private readonly List<(VisualElement el, V2d pos)> _marken = new List<(VisualElement, V2d)>();
        private float _scale = 1f, _ox, _oy, _puls;

        private static readonly Color Pergament = new Color(0.99f, 0.96f, 0.89f), Rand = new Color(0.86f, 0.78f, 0.64f);
        private static readonly Color Wasser = new Color(0.62f, 0.83f, 0.93f), WasserRand = new Color(0.45f, 0.68f, 0.82f);
        private static readonly Color Weg = new Color(0.8f, 0.66f, 0.46f), Nebel = new Color(0.89f, 0.87f, 0.84f);

        private JObject State => _host.Store.State;
        private string T(string key, params (string, object)[] args) => _host.Strings.T(key, args);

        /// <param name="spieler">The rider's region position and heading (0 = north).</param>
        /// <param name="dahin">"zone"/"fund" + id: ride there.</param>
        public RanchKarteView(GameHost host, Func<(double x, double z, float heading)> spieler, Action<string, string> dahin, Action schliessen)
        {
            _host = host;
            _spieler = spieler;
            _dahin = dahin;
            name = "ranchwelt-karte";
            AddToClassList("gb-rkarte");
            var card = Ux.Col("gb-card", "gb-rkarte-card");
            Add(card);
            var head = Ux.Row("gb-rkarte-head");
            head.Add(Ux.IconTile("map", new Color(0.98f, 0.86f, 0.62f)));
            var col = Ux.Col();
            col.style.marginLeft = 10;
            col.style.flexGrow = 1;
            col.Add(Ux.Text(T("rkarte.titel"), "gb-h2"));
            _fortschritt = Ux.Text("", "gb-sm", "gb-mild");
            _fortschritt.name = "rkarte-fortschritt";
            col.Add(_fortschritt);
            head.Add(col);
            var zu = Ux.IconBtn("close", () => { Sfx.PlayUi("soft_back"); schliessen(); }, "gb-btn-icon");
            zu.name = "rkarte-zu";
            head.Add(zu);
            card.Add(head);
            _feld = new VisualElement { name = "rkarte-feld" };
            _feld.AddToClassList("gb-rkarte-feld");
            card.Add(_feld);
            _canvas = new P.Canvas { Paint = Male };
            Ux.Abs(_canvas, 0, 0, 0, 0);
            _feld.Add(_canvas);
            _ich = new VisualElement { name = "rkarte-ich", pickingMode = PickingMode.Ignore };
            _ich.AddToClassList("gb-rkarte-ich");
            _detail = Ux.Col("gb-card", "gb-rkarte-detail");
            _detail.name = "rkarte-detail";
            _detail.style.display = DisplayStyle.None;
            Add(_detail);
            _feld.RegisterCallback<GeometryChangedEvent>(_ => Aufbauen());
        }

        /// <summary>Rebuild pins and labels for the save as it is now (on open and after a find).</summary>
        public void Aufbauen()
        {
            var w = _feld.layout.width;
            var h = _feld.layout.height;
            if (float.IsNaN(w) || w < 10f) return;
            var g = RanchKarte.Grenzen;
            _scale = Mathf.Min(w / (float)g.W, h / (float)g.D) * 0.96f;
            _ox = (w - (float)g.W * _scale) * 0.5f;
            _oy = (h - (float)g.D * _scale) * 0.5f;
            foreach (var (el, _) in _marken) el.RemoveFromHierarchy();
            _marken.Clear();
            var bereist = RanchWeltState.EntdeckteZonen(State);
            foreach (var zone in RanchKarte.Zonen)
            {
                var an = bereist.Contains(zone.Id);
                var label = Ux.Text(an ? T(zone.NameKey) : T("rkarte.unbekannt"), "gb-rkarte-zone");
                label.EnableInClassList("gb-rkarte-zone--nebel", !an);
                if (an)
                {
                    var zid = zone.Id;
                    label.pickingMode = PickingMode.Position;
                    label.RegisterCallback<PointerDownEvent>(e => { e.StopPropagation(); ZeigeZone(zid); });
                }
                else label.pickingMode = PickingMode.Ignore;
                label.name = "rkarte-zone-" + zone.Id;
                Setze(label, zone.Rect.Mitte, 0f);
            }
            foreach (var pin in RanchEntdeckerKarte.Fundorte(State))
            {
                var p = pin;
                var b = new Button(() => ZeigeFund(p)) { name = "rkarte-pin-" + pin.Ort.Id };
                b.ClearClassList();
                b.AddToClassList("gb-rkarte-pin");
                b.EnableInClassList("gb-rkarte-pin--da", pin.Entdeckt);
                if (pin.Entdeckt)
                {
                    var ic = Ux.Icon(Icon(pin.Ort.Id));
                    ic.pickingMode = PickingMode.Ignore;
                    b.Add(ic);
                }
                else
                {
                    var q = Ux.Text("?", "gb-rkarte-pin-q");
                    q.pickingMode = PickingMode.Ignore;
                    b.Add(q);
                }
                if (pin.Neu)
                {
                    var neu = Ux.Text(T("rkarte.neu"), "gb-rkarte-neu");
                    neu.pickingMode = PickingMode.Ignore;
                    b.Add(neu);
                }
                Setze(b, pin.ZeigPos, 0f);
            }
            _feld.Add(_ich);
            var f = RanchEntdeckerKarte.Fortschritt(State);
            _fortschritt.text = T("rkarte.fortschritt", ("funde", f.funde), ("funde_gesamt", f.fundeGesamt), ("zonen", f.zonen), ("zonen_gesamt", f.zonenGesamt));
            _canvas.MarkDirtyRepaint();
            Tick(0f);
        }

        private void Setze(VisualElement el, V2d pos, float _)
        {
            el.style.position = Position.Absolute;
            _feld.Add(el);
            _marken.Add((el, pos));
            el.RegisterCallback<GeometryChangedEvent>(__ => Platziere(el, pos));
            Platziere(el, pos);
        }

        private void Platziere(VisualElement el, V2d pos)
        {
            var p = Px(pos.X, pos.Z);
            var w = float.IsNaN(el.layout.width) ? 0f : el.layout.width;
            var h = float.IsNaN(el.layout.height) ? 0f : el.layout.height;
            el.style.left = p.x - w * 0.5f;
            el.style.top = p.y - h * 0.5f;
        }

        private Vector2 Px(double x, double z)
        {
            var g = RanchKarte.Grenzen;
            return new Vector2(_ox + (float)(x - g.X) * _scale, _oy + (float)(z - g.Z) * _scale);
        }

        /// <summary>The pulsing "you are here" (call each frame while open).</summary>
        public void Tick(float dt)
        {
            _puls += dt;
            var (x, z, heading) = _spieler();
            var p = Px(x, z);
            var s = 1f + 0.12f * Mathf.Sin(_puls * 4f);
            _ich.style.left = p.x - 14f;
            _ich.style.top = p.y - 14f;
            _ich.style.scale = new Scale(new Vector3(s, s, 1f));
            _ich.style.rotate = new Rotate(heading * Mathf.Rad2Deg);
        }

        private void Male(Painter2D p)
        {
            var g = RanchKarte.Grenzen;
            var a = Px(g.X, g.Z);
            var b = Px(g.EndX, g.EndZ);
            P.RoundRect(p, a - new Vector2(6f, 6f), b + new Vector2(6f, 6f), 18f, Rand);
            P.RoundRect(p, a, b, 14f, Pergament);
            var bereist = RanchWeltState.EntdeckteZonen(State);
            foreach (var zone in RanchKarte.Zonen)
            {
                var an = bereist.Contains(zone.Id);
                var c = an ? ArtLibrary.Hex(RanchEntdeckerKarte.ZonenFarbe(zone.Stimmung)) : Nebel;
                P.RoundRect(p, Px(zone.Rect.X, zone.Rect.Z), Px(zone.Rect.EndX, zone.Rect.EndZ), 12f * _scale * 6f, c);
            }
            foreach (var zone in RanchKarte.Zonen)
                foreach (var key in new[] { "see", "bergsee", "bucht" })
                {
                    if (!(zone.Daten?[key + "_mitte"] is JArray)) continue;
                    var m = zone.Punkt(key + "_mitte");
                    var r = (float)zone.Zahl(key + "_radius") * _scale;
                    P.Circle(p, Px(m.X, m.Z), r + 2f, WasserRand);
                    P.Circle(p, Px(m.X, m.Z), r, Wasser);
                }
            var bach = RanchKarte.BachPunkte;
            for (var i = 0; i + 1 < bach.Count; i++) P.Line(p, Px(bach[i].X, bach[i].Z), Px(bach[i + 1].X, bach[i + 1].Z), 3.5f, WasserRand);
            foreach (var w in RanchKarte.Wege)
            {
                var bekannt = bereist.Contains(w.Von) || bereist.Contains(w.Nach);
                var c = bekannt ? Weg : new Color(Weg.r, Weg.g, Weg.b, 0.3f);
                for (var i = 0; i + 1 < w.Punkte.Count; i++)
                {
                    // dashed: a field path drawn like a hand map
                    var s0 = Px(w.Punkte[i].X, w.Punkte[i].Z);
                    var s1 = Px(w.Punkte[i + 1].X, w.Punkte[i + 1].Z);
                    var len = Vector2.Distance(s0, s1);
                    var n = Mathf.Max(1, Mathf.RoundToInt(len / 9f));
                    for (var k = 0; k < n; k += 2)
                        P.Line(p, Vector2.Lerp(s0, s1, (float)k / n), Vector2.Lerp(s0, s1, Mathf.Min(1f, (k + 1f) / n)), 2.6f, c);
                }
            }
        }

        private static string Icon(string id)
        {
            if (id.StartsWith("truhe") || id == "ruine_schatz") return "gift";
            if (id.StartsWith("aussicht")) return "camera";
            switch (id)
            {
                case "wasserfall": case "bergsee_perle": return "rain";
                case "hoehle": case "moor_irrlicht": return "sparkle";
                case "alter_baum": return "leaf";
                case "steinkreis": return "star";
                case "gipfelkreuz": return "trophy";
                case "strand_muschel": return "sun";
                case "lavendel_bienen": return "flower";
                case "kornkreis": return "sprout";
                default: return "star";
            }
        }

        private void ZeigeFund(RanchEntdeckerKarte.Pin pin)
        {
            Sfx.PlayUi("soft_tap");
            if (pin.Neu) _host.Store.Update(s => RanchEntdeckerKarte.MarkiereGesehen(s, new[] { pin.Ort.Id }), GameHost.TicksMs);
            string titel, text;
            var dahin = false;
            if (pin.Entdeckt)
            {
                titel = T(pin.Ort.NameKey);
                text = T("rkarte.beschr." + pin.Ort.Id);
                dahin = true;
            }
            else
            {
                titel = T("rkarte.unbekannt");
                var (x, z, _) = _spieler();
                var von = new V2d(x, z);
                var richtung = RanchEntdeckerKarte.RichtungKey(von, pin.ZeigPos);
                var zone = T("rwelt.zone." + RanchEntdeckerKarte.HinweisZone(pin.ZeigPos));
                text = T("rkarte.geheimnis") + "\n" + (von.Abstand(pin.ZeigPos) < 160.0 || richtung.Length == 0
                    ? T("rkarte.dahin_nah", ("zone", zone))
                    : T("rkarte.dahin", ("richtung", T("rkarte.richtung." + richtung)), ("zone", zone)));
            }
            Detail(titel, text, dahin ? () => _dahin("fund", pin.Ort.Id) : (Action)null);
            if (pin.Neu) schedule.Execute(Aufbauen).StartingIn(50);
        }

        private void ZeigeZone(string zoneId)
        {
            Sfx.PlayUi("soft_tap");
            Detail(T("rwelt.zone." + zoneId), T("rwelt.ui.zone_text"), () => _dahin("zone", zoneId));
        }

        private void Detail(string titel, string text, Action dahin)
        {
            _detail.Clear();
            _detail.Add(Ux.Text(titel, "gb-h2"));
            var t = Ux.Text(text, "gb-sm");
            t.style.whiteSpace = WhiteSpace.Normal;
            t.style.marginTop = 6;
            _detail.Add(t);
            var row = Ux.Row("gb-rkarte-detail-row");
            if (dahin != null)
            {
                var go = Ux.IconTextBtn("horseshoe", T("rwelt.ui.dahin_reiten"), () => { Sfx.PlayUi("soft_confirm"); _detail.style.display = DisplayStyle.None; dahin(); }, "gb-btn-primary", "gb-btn-sm");
                go.name = "rkarte-dahin";
                row.Add(go);
            }
            var ok = Ux.Btn(T("rkarte.schliessen"), () => { Sfx.PlayUi("soft_back"); _detail.style.display = DisplayStyle.None; }, "gb-btn-secondary", "gb-btn-sm");
            ok.name = "rkarte-ok";
            row.Add(ok);
            _detail.Add(row);
            _detail.style.display = DisplayStyle.Flex;
        }

        /// <summary>QA: tap a pin or a zone label by id.</summary>
        public void QaTippe(string id)
        {
            var pin = RanchEntdeckerKarte.Fundorte(State).FirstOrDefault(p => p.Ort.Id == id);
            if (pin != null) ZeigeFund(pin);
            else if (RanchKarte.ZoneVon(id) != null) ZeigeZone(id);
        }
    }
}
