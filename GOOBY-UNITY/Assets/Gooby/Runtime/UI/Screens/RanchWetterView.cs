using System;
using System.Globalization;
using System.Linq;
using Gooby.Core.Audio;
using Gooby.Core.Life;
using Gooby.Core.Minigames.Ranch;
using Gooby.Unity.App;
using Gooby.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The weather and the sound of a ranch place (the farm, Hufingen) — ranch_wetter_controller.gd and ranch_audio.gd for a
    /// set: the save's day plan (<see cref="RanchWeltState"/>) at the local hour on the 3D set (<see cref="StageWeather"/>),
    /// a header chip with its icon and name (a tap tells how long it lasts and what comes next), the evening rainbow over
    /// the field after rain, and the ambience layers with the day or night music (<see cref="RanchKlang"/>). QA can force
    /// a weather like the Godot override.
    /// </summary>
    public sealed class RanchWetterView
    {
        public const float RefreshSec = 5f;

        private readonly GameHost _host;
        private readonly string _place;
        private readonly StageWeather _stage;
        private readonly VisualElement _rainbow, _icon;
        private readonly Label _label;
        private string _forced = "", _scene;
        private float _refreshIn = RefreshSec;
        private bool _rainbowSeen;
        private static Texture2D _bogen;

        public readonly Button Chip;
        /// <summary>The region zone of the ambience mix (the open region changes it as you ride).</summary>
        public string Zone { get; set; }
        /// <summary>Metres to the brook (≥ 0 sets the brook layer by distance; null = by zone).</summary>
        public Func<double> WasserAbstand { get; set; }
        /// <summary>The set's own weather (the open region's rain rides with its camera).</summary>
        public Action<WetterFx.Plan> PlanAn { get; set; }
        public RanchWetter.Zustand Zustand { get; private set; }
        /// <summary>The sound scene of the place now ("ranch/hof", "ranch/hof/nacht" …).</summary>
        public string Scene => _scene;
        public StageWeather Stage => _stage;
        public bool RainbowShown => _rainbow.ClassListContains("gb-ranch-regenbogen--an");

        /// <param name="zone">The region zone for the ambience mix ("hof", "hufingen").</param>
        /// <param name="place">The place's sound scene under "ranch/" ("hof", "dorf").</param>
        public RanchWetterView(GameHost host, string zone, string place, StageWeather stage, VisualElement field)
        {
            _host = host;
            Zone = zone;
            _place = place;
            _stage = stage;
            Chip = new Button(Erklaeren) { name = "ranch-wetter" };
            Chip.ClearClassList();
            Chip.AddToClassList("gb-ranch-wetter");
            _icon = Ux.Icon("sun");
            _icon.pickingMode = PickingMode.Ignore;
            Chip.Add(_icon);
            _label = Ux.Text("", "gb-ranch-wetter-text");
            _label.pickingMode = PickingMode.Ignore;
            Chip.Add(_label);
            _rainbow = new VisualElement { name = "ranch-regenbogen", pickingMode = PickingMode.Ignore };
            _rainbow.AddToClassList("gb-ranch-regenbogen");
            _rainbow.style.backgroundImage = new StyleBackground(Bogen());
            field.Add(_rainbow);
            Refresh();
        }

        private long Now => _host.Store.Clock.NowMs();

        private DateTimeOffset Lokal()
        {
            var now = Now;
            return DateTimeOffset.FromUnixTimeMilliseconds(now).ToOffset(TimeSpan.FromMinutes(_host.Store.Clock.UtcOffsetMinutes(now)));
        }

        private static string Datum(DateTimeOffset local) => local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static double Stunde(DateTimeOffset local) => local.Hour + local.Minute / 60.0;

        public void Tick(float dt)
        {
            _stage?.Tick(dt);
            _refreshIn -= dt;
            if (_refreshIn > 0f) return;
            _refreshIn = RefreshSec;
            Refresh();
        }

        /// <summary>The moment's weather onto the set, the chip, the rainbow and the ambience.</summary>
        public void Refresh()
        {
            var local = Lokal();
            var stunde = Stunde(local);
            Zustand = StageWeather.Erzwungen(_forced) ?? RanchWeltState.Wetter(_host.Store.State, Datum(local), stunde);
            _stage?.Update(Zustand, _host.Settings.ReducedMotion);
            PlanAn?.Invoke(WetterFx.Fuer(new SoulWetter.Lage { Typ = Zustand.Typ, Regen = Zustand.Regnet, Basis = Zustand }, false, _host.Settings.ReducedMotion));
            var nacht = RanchKlang.IstNacht(stunde);
            var tex = Ux.IconTexture(Icon(Zustand.Typ, nacht));
            if (tex != null) _icon.style.backgroundImage = new StyleBackground(tex);
            _label.text = Name(Zustand, nacht);
            Chip.EnableInClassList("gb-ranch-wetter--bogen", Zustand.Regenbogen);
            _rainbow.EnableInClassList("gb-ranch-regenbogen--an", Zustand.Regenbogen);
            if (Zustand.Regenbogen && !_rainbowSeen)
            {
                _rainbowSeen = true;
                _host.Ui.Toast(_host.Strings.T("rhof.wetter.regenbogen_toast"), "sparkle");
                Sfx.PlayUi("soft_sticker");
                Debug.Log("[ranch-wetter] regenbogen");
            }
            var scene = "ranch/" + _place + (nacht ? "/nacht" : "");
            if (scene != _scene && _scene != null) _host.Audio?.SetBeds(_scene, null);
            _scene = scene;
            _host.Audio?.SetBeds(_scene, RanchKlang.Betten(Zone, Zustand, stunde, WasserAbstand?.Invoke() ?? -1.0));
        }

        /// <summary>Covered by another screen: the set's weather rests (the studio and the fog go back to clear).</summary>
        public void SetActive(bool on)
        {
            _stage?.SetActive(on);
            if (on) Refresh();
        }

        public void Destroy()
        {
            _stage?.SetActive(false);
            if (_scene != null) _host.Audio?.SetBeds(_scene, null);
        }

        /// <summary>QA: "regen", "gewitter", "nebel", "regenbogen" … ("" = the day plan again).</summary>
        public void Erzwingen(string typ)
        {
            _forced = typ == "aus" || typ == "plan" ? "" : typ ?? "";
            Refresh();
            Debug.Log("[ranch-wetter] " + (_forced.Length > 0 ? _forced : "plan") + " → " + Zustand.Typ + " nass " + Zustand.Naesse.ToString("0.00", CultureInfo.InvariantCulture));
        }

        public Newtonsoft.Json.Linq.JObject QaState() => new Newtonsoft.Json.Linq.JObject
        {
            ["typ"] = Zustand?.Typ ?? "",
            ["naesse"] = Math.Round(Zustand?.Naesse ?? 0.0, 2),
            ["regenbogen"] = RainbowShown,
            ["regnet"] = _stage != null && _stage.Raining,
            ["pfuetzen"] = _stage?.PuddlesShown ?? 0,
            ["blitze"] = _stage?.Flashes ?? 0,
            ["nebel"] = _stage != null && _stage.Foggy,
            ["szene"] = _scene ?? "",
            ["regenEbene"] = Math.Round(_host.Audio != null ? _host.Audio.BedTarget("ranch_regen") : 0f, 3),
            ["voegelEbene"] = Math.Round(_host.Audio != null ? _host.Audio.BedTarget("ranch_voegel") : 0f, 3),
            ["grillenEbene"] = Math.Round(_host.Audio != null ? _host.Audio.BedTarget("ranch_grillen") : 0f, 3),
            ["musik"] = _host.Audio?.Track ?? "",
        };

        private string Name(RanchWetter.Zustand z, bool nacht)
        {
            if (z.Regenbogen) return _host.Strings.T("rwelt.wetter.regenbogen");
            if (z.Typ == "sonne" && nacht) return _host.Strings.T("rhof.wetter.klar");
            return _host.Strings.T(z.NameKey ?? "rwelt.wetter." + z.Typ);
        }

        private static string Icon(string typ, bool nacht)
        {
            switch (typ)
            {
                case "niesel": case "regen": case "gewitter": return "rain";
                case "wolken": case "nebel": return "cloud";
                default: return nacht ? "moon" : "sun";
            }
        }

        /// <summary>A tap on the chip: until when this weather lasts and what follows (the day plan's next segment).</summary>
        private void Erklaeren()
        {
            Sfx.PlayUi("soft_tap");
            var local = Lokal();
            var stunde = Stunde(local);
            var nacht = RanchKlang.IstNacht(stunde);
            var jetzt = Name(Zustand, nacht);
            string text;
            if (_forced.Length > 0) text = _host.Strings.T("rhof.wetter.bleibt", ("wetter", jetzt));
            else
            {
                var plan = RanchWetter.Tagesplan(Datum(local), RanchWeltState.WetterSeed(_host.Store.State));
                var i = plan.FindIndex(s => stunde < s.Bis);
                if (i < 0 || i + 1 >= plan.Count) text = _host.Strings.T("rhof.wetter.bleibt", ("wetter", jetzt));
                else
                {
                    var bis = plan[i].Bis;
                    var zeit = ((int)bis).ToString("00") + ":" + ((int)Math.Round((bis - Math.Floor(bis)) * 60.0) % 60).ToString("00");
                    var danach = plan[i + 1].Typ == "sonne" && RanchKlang.IstNacht(bis) ? _host.Strings.T("rhof.wetter.klar") : _host.Strings.T("rwelt.wetter." + plan[i + 1].Typ);
                    text = _host.Strings.T("rhof.wetter.bis", ("wetter", jetzt), ("zeit", zeit), ("danach", danach));
                }
            }
            if (Zustand.Naesse > 0.3 && !Zustand.Regnet) text += " " + _host.Strings.T("rhof.wetter.regen_tipp");
            _host.Ui.Toast(text, Icon(Zustand.Typ, nacht), 4.2f);
        }

        /// <summary>The rainbow arc (ranch_wetter_controller.gd REGENBOGEN_FARBEN, outside → inside), soft at the edges and the feet.</summary>
        private static Texture2D Bogen()
        {
            if (_bogen != null) return _bogen;
            const int w = 512, h = 256;
            var farben = new[]
            {
                new Color(0.94f, 0.42f, 0.42f), new Color(0.96f, 0.65f, 0.38f), new Color(0.97f, 0.88f, 0.45f),
                new Color(0.55f, 0.83f, 0.50f), new Color(0.45f, 0.66f, 0.92f), new Color(0.62f, 0.50f, 0.88f),
            };
            const float aussen = 0.97f, innen = 0.73f;
            _bogen = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "fx_regenbogen", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var dx = (x + 0.5f - w * 0.5f) / (w * 0.5f);
                    var dy = (y + 0.5f) / h;
                    var r = Mathf.Sqrt(dx * dx + dy * dy);
                    var t = Mathf.InverseLerp(aussen, innen, r);
                    var band = Mathf.Clamp(t * farben.Length - 0.5f, 0f, farben.Length - 1.001f);
                    var c = Color.Lerp(farben[(int)band], farben[(int)band + 1], band - (int)band);
                    var kante = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(r - innen, aussen - r) / 0.035f));
                    var fuss = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(dy / 0.45f));
                    var a = r > aussen || r < innen ? 0f : kante * fuss * 0.85f;
                    px[y * w + x] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(a * 255f));
                }
            _bogen.SetPixels32(px);
            _bogen.Apply(false, true);
            return _bogen;
        }
    }
}
