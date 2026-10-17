using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Gooby.Unity.UI.Screens;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.App
{
    /// <summary>
    /// QA remote for multi-client playtests (tools/playtest/mp_tour.sh): with <c>-gooby-qa-cmds &lt;file&gt;</c> the player
    /// follows the lines appended to that file — open screens, tap named UI elements, type into text fields, walk,
    /// take screenshots and log its online state as <c>[qa] state {…}</c>. It drives the same UI a player taps, so a
    /// tour through friends, letters and visits exercises the real screens on several clients at once.
    ///
    /// Commands: <c>push friends|mailbox|instant|settings[:tab]|arcade|home</c> · <c>pop</c> · <c>click &lt;name&gt;</c> ·
    /// <c>type &lt;name&gt; &lt;text…&gt;</c> · <c>walk &lt;x&gt; &lt;y&gt; &lt;sec&gt;</c> · <c>shot &lt;file.png&gt;</c> · <c>state</c> ·
    /// <c>server &lt;address&gt;</c> · <c>offline</c> · <c>bs next|auto|off</c> (Schiffe versenken: next untried enemy cell,
    /// keep firing on our turn) · <c>ch move &lt;uci&gt;</c> (Schach: taps from/to square + promotion piece),
    /// <c>ch auto on|off</c> (plays the practice AI's move via the same taps on our turn), <c>ch practice [1-3] [white|black]</c>
    /// (offline practice against the bot) · <c>quit</c>.
    /// </summary>
    public sealed class QaDriver
    {
        private readonly GameHost _host;
        private readonly string _file;
        private long _offset;
        private float _poll, _walkLeft;

        private QaDriver(GameHost host, string file)
        {
            _host = host;
            _file = file;
        }

        /// <summary>A driver without a command file: the developer toolbox runs the same commands from its buttons and command line.</summary>
        public static QaDriver ForDevMenu(GameHost host) => new QaDriver(host, null);

        /// <summary>Runs one command line now (dev toolbox); false when it threw.</summary>
        public bool RunCommand(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            try
            {
                Debug.Log("[dev] > " + line);
                Run(line.Trim());
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[dev] " + line + " failed: " + e.Message);
                return false;
            }
        }

        public static QaDriver FromArgs(GameHost host)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
                if (args[i] == "-gooby-qa-cmds") { Debug.Log("[qa] reading commands from " + args[i + 1]); return new QaDriver(host, args[i + 1]); }
            return null;
        }

        private double _tickAt = -1;
        private int _tickFrame;

        public void Update(float dt)
        {
            // frame-rate trace for the software-rendered QA player: one line every 5 s of real time
            var now = Time.realtimeSinceStartupAsDouble;
            if (_tickAt < 0) { _tickAt = now; _tickFrame = Time.frameCount; }
            else if (now - _tickAt >= 5.0)
            {
                Debug.Log($"[qa] tick frames={Time.frameCount - _tickFrame} in {now - _tickAt:F1}s screen={_host.Ui?.Current?.GetType().Name}");
                _tickAt = now; _tickFrame = Time.frameCount;
            }
            if (_walkLeft > 0f)
            {
                _walkLeft -= dt;
                if (_walkLeft <= 0f) _host.World?.SetMove(Vector2.zero);
            }
            if (_host.Boot == null || !_host.Boot.IsDone) return;
            BattleshipAutoTick(dt);
            ChessAutoTick(dt);
            if (_pending.Count > 0) RunPending(dt);
            _poll += dt;
            if (_poll < 0.25f) return;
            _poll = 0f;
            if (!File.Exists(_file)) return;
            string chunk;
            try
            {
                using (var fs = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Length <= _offset) return;
                    fs.Seek(_offset, SeekOrigin.Begin);
                    using (var sr = new StreamReader(fs))
                    {
                        chunk = sr.ReadToEnd();
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[qa] read failed: " + e.Message); return; }
            var lastNl = chunk.LastIndexOf('\n');
            if (lastNl < 0) return; // wait for a complete line
            var complete = chunk.Substring(0, lastNl + 1);
            _offset += System.Text.Encoding.UTF8.GetByteCount(complete);
            foreach (var raw in complete.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line == "state" && _pending.Count > 0)
                {
                    Run(line);
                    continue;
                }
                _pending.Enqueue(line);
            }
            RunPending(0f);
        }

        private readonly System.Collections.Generic.Queue<string> _pending = new System.Collections.Generic.Queue<string>();
        private float _waitLeft;

        /// <summary>Runs queued commands; "wait &lt;s&gt;" holds the rest for that many real seconds (timed screenshots).</summary>
        private void RunPending(float dt)
        {
            _waitLeft -= dt;
            while (_waitLeft <= 0f && _pending.Count > 0)
            {
                var line = _pending.Dequeue();
                if (line.StartsWith("wait ", StringComparison.Ordinal))
                {
                    _waitLeft = float.Parse(line.Substring(5).Trim(), CultureInfo.InvariantCulture);
                    Debug.Log("[qa] > " + line);
                    continue;
                }
                try { Run(line); }
                catch (Exception e) { Debug.LogWarning("[qa] '" + line + "' failed: " + e.Message); }
            }
        }

        private void Run(string line)
        {
            var parts = line.Split(new[] { ' ' }, 3);
            var cmd = parts[0];
            var a1 = parts.Length > 1 ? parts[1] : "";
            var rest = parts.Length > 2 ? parts[2] : "";
            Debug.Log("[qa] > " + line);
            switch (cmd)
            {
                case "push":
                    Push(a1);
                    break;
                case "pop":
                    _host.Ui.Pop();
                    break;
                case "home":
                    _host.Ui.PopToRoot();
                    break;
                case "click":
                    Click(a1);
                    break;
                case "press":
                {
                    // pointer down + up on an element that listens to the pointer itself (round ride buttons, sticks)
                    var el = Find(a1);
                    if (el == null) { Debug.LogWarning("[qa] press: no element " + a1); break; }
                    using (var down = PointerDownEvent.GetPooled()) { down.target = el; el.SendEvent(down); }
                    using (var up = PointerUpEvent.GetPooled()) { up.target = el; el.SendEvent(up); }
                    Debug.Log("[qa] pressed " + a1);
                    break;
                }
                case "type":
                    Type(a1, rest);
                    break;
                case "scroll":
                {
                    // scroll 0..1: the first ScrollView of the top screen, as a fraction of its scrollable height
                    var sv = _host.Ui.Current?.Root?.Q<ScrollView>();
                    if (sv == null) { Debug.LogWarning("[qa] scroll: no ScrollView on " + _host.Ui.Current?.Id); break; }
                    var f = Mathf.Clamp01(float.Parse(a1, CultureInfo.InvariantCulture));
                    // after the grids have fitted their columns: an early offset is clamped to the settled height
                    sv.schedule.Execute(() => sv.scrollOffset = new Vector2(sv.scrollOffset.x, f * Mathf.Max(0f, sv.verticalScroller.highValue))).StartingIn(400);
                    break;
                }
                case "walk":
                {
                    var p = (a1 + " " + rest).Split(' ');
                    var x = float.Parse(p[0], CultureInfo.InvariantCulture);
                    var y = float.Parse(p[1], CultureInfo.InvariantCulture);
                    _walkLeft = p.Length > 2 ? float.Parse(p[2], CultureInfo.InvariantCulture) : 1f;
                    _host.World?.SetMove(new Vector2(x, y));
                    break;
                }
                case "shot":
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(a1)) ?? ".");
                    ScreenCapture.CaptureScreenshot(a1);
                    break;
                case "state":
                    Debug.Log("[qa] state " + State().ToString(Newtonsoft.Json.Formatting.None));
                    break;
                case "coins":
                {
                    // "coins 600": the purse at exactly that many coins (shop and travel tours)
                    var n = int.Parse(a1, CultureInfo.InvariantCulture);
                    _host.Store.Update(s => Gooby.Core.Json.J.EnsureDict(s, "economy")["coins"] = n, GameHost.TicksMs);
                    _host.Store.NotifySliceChanged("economy");
                    Debug.Log("[qa] coins " + n);
                    break;
                }
                case "krank":
                {
                    // "krank healthy|queasy|sick": Gooby's health state for the vet and pharmacy tours
                    var st = a1 == "sick" || a1 == "queasy" ? a1 : "healthy";
                    var at = _host.Store.Clock.NowMs();
                    _host.Store.Update(s =>
                    {
                        var g = Gooby.Core.Json.J.EnsureDict(s, "gooby");
                        var h = Gooby.Core.Life.Health.Normalize(g["health"]);
                        h["state"] = st;
                        h["since"] = (double)at;
                        g["health"] = h;
                    }, GameHost.TicksMs);
                    _host.Store.NotifySliceChanged("gooby");
                    _host.World?.RefreshCare();
                    Debug.Log("[qa] krank " + st);
                    break;
                }
                case "energie":
                case "gewicht":
                {
                    // "energie <0..100>" / "gewicht <5..95>": tired eyes and waddle, body width (care look tours)
                    if (!double.TryParse(a1, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) break;
                    var energy = cmd == "energie";
                    _host.Store.Update(s =>
                    {
                        var g = Gooby.Core.Json.J.EnsureDict(s, "gooby");
                        if (energy) Gooby.Core.Json.J.EnsureDict(g, "stats")["energy"] = v;
                        else g["weight"] = Gooby.Core.Life.Weight.ClampWeight(v);
                    }, GameHost.TicksMs);
                    _host.Store.NotifySliceChanged("gooby");
                    _host.World?.RefreshCare();
                    Debug.Log("[qa] " + cmd + " " + v);
                    break;
                }
                case "szene":
                    // "szene finden|streicheln|wickeln": the thunderstorm and mummy events' scene on the event sheet
                    if (_host.Ui.Current is EventRescueScreen rescue) { rescue.QaAction(a1); Debug.Log("[qa] szene " + a1); }
                    else Debug.LogWarning("[qa] szene: event sheet not open");
                    break;
                case "tanztiming":
                {
                    // "tanztiming open|start|tap|done": the Tanzparty's metronome calibration on its pregame card
                    if (!(_host.Ui.Current is DancePartyScreen dance)) { Debug.LogWarning("[qa] tanztiming: Tanzparty not open"); break; }
                    if (a1 == "open") dance.OpenCalibration();
                    else if (a1 == "start") dance.StartCalibration();
                    else if (a1 == "tap") dance.TapCalibration();
                    else if (a1 == "done") dance.CloseCalibration();
                    Debug.Log("[qa] tanztiming " + a1);
                    break;
                }
                case "uhrzeit":
                {
                    // "uhrzeit <hour>": the dev clock offset moves the game clock forward to that local hour (evening tests)
                    if (!double.TryParse(a1, NumberStyles.Float, CultureInfo.InvariantCulture, out var target)) break;
                    var current = _host.World?.LocalHour ?? 12.0;
                    var hours = ((target - current) % 24.0 + 24.0) % 24.0;
                    DevTools.AddOffset((long)(hours * DevTools.HourMs));
                    Debug.Log("[qa] uhrzeit " + target + " (+" + hours.ToString("F2", CultureInfo.InvariantCulture) + " h)");
                    break;
                }
                case "selfie":
                    // "selfie": Snap A Gooby! on the visit screen (both Goobys pose, countdown, photo letter to the friend)
                    Debug.Log("[qa] selfie " + (_host.Ui.Current is VisitScreen selfieVisit && selfieVisit.StartSelfie()));
                    break;
                case "schuetteln":
                {
                    // "schuetteln <seconds> [strength m/s²]": the phone shaken that long (alternating ±strength on x over gravity)
                    if (_host.World == null) break;
                    var secs = float.TryParse(a1, NumberStyles.Float, CultureInfo.InvariantCulture, out var s0) ? s0 : 4f;
                    var strength = float.TryParse(rest.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s1) ? s1 : 14f;
                    var end = Time.time + secs;
                    var frame = 0;
                    _host.World.ShakeSource = () =>
                    {
                        frame++;
                        return Time.time < end ? new Vector3(strength * (frame % 2 == 0 ? 1f : -1f), -9.8f, 0f) : new Vector3(0f, -9.8f, 0f);
                    };
                    Debug.Log("[qa] schuetteln " + secs + "s ±" + strength + " blockers=" + _host.World.ShakeBlockers());
                    break;
                }
                case "pflegemoment":
                    // "pflegemoment hatschi|gaehnen": the sneeze or yawn right now
                    if (_host.World?.Avatar == null) break;
                    if (a1 == "gaehnen") _host.World.Avatar.Yawn();
                    else _host.World.Avatar.Sneeze(false);
                    Debug.Log("[qa] pflegemoment " + a1);
                    break;
                case "gesicht":
                {
                    // "gesicht <x> <y>": how far forward (avatar-local z) the skin reaches near that point — for props on the face
                    var av = _host.World?.Avatar;
                    var skin = av != null ? av.GetComponentInChildren<SkinnedMeshRenderer>() : null;
                    if (skin == null) break;
                    var p = (a1 + " " + rest).Split(' ');
                    var qx = float.Parse(p[0], CultureInfo.InvariantCulture);
                    var qy = float.Parse(p[1], CultureInfo.InvariantCulture);
                    var baked = new Mesh();
                    skin.BakeMesh(baked, true);
                    var best = float.MinValue;
                    foreach (var v in baked.vertices)
                    {
                        var local = av.transform.InverseTransformPoint(skin.transform.TransformPoint(v));
                        if (Mathf.Abs(local.x - qx) < 0.012f && Mathf.Abs(local.y - qy) < 0.012f) best = Mathf.Max(best, local.z);
                    }
                    UnityEngine.Object.Destroy(baked);
                    Debug.Log("[qa] gesicht x=" + qx.ToString("F3") + " y=" + qy.ToString("F3") + " z=" + (best > float.MinValue ? best.ToString("F3") : "-"));
                    break;
                }
                case "pflegeblick":
                {
                    var av = _host.World?.Avatar;
                    Debug.Log("[qa] pflegeblick grade=" + (av?.CareGrade ?? -1) + " nase=" + (av?.SniffleNoseShown ?? false) + " augenringe=" + (av?.EyeBagsShown ?? false)
                        + " breite=" + (av?.BodyWidth ?? 0f).ToString("F2") + " tempo=" + (_host.World?.CareSpeed ?? 0f).ToString("F2"));
                    break;
                }
                case "ort":
                {
                    // "ort <placeId>": parked at the place with its interior open (like -gooby-open, from any screen)
                    var place = Gooby.Core.City.DriveModel.PlaceById(a1);
                    if (place == null) { Debug.LogWarning("[qa] ort: unknown place " + a1); break; }
                    _host.Ui.PopToRoot();
                    var drive = new CityDriveScreen(_host);
                    _host.Ui.Push(drive);
                    if (_host.World.Drive != null) { _host.World.Drive.X = place.X; _host.World.Drive.Z = place.Z; _host.World.Drive.Kmh = 0.0; }
                    drive.Braking = true;
                    _host.Ui.Push(PlaceScreen.For(_host, place));
                    break;
                }
                case "postkarten":
                {
                    // "postkarten demo": three past trips in the save (beach 3 days, space 4, harbor 3) with their postcards and
                    // the bakery visited too — the cork wall, the souvenir shelf and the archive show real cards; the room redraws
                    var now = _host.Store.Clock.NowMs();
                    _host.Store.Update(s =>
                    {
                        var v = Gooby.Core.Life.Vacation.SliceOf(s);
                        var archive = new JArray();
                        var start = now - 20L * Gooby.Core.Life.Vacation.MsPerDay;
                        foreach (var (dest, days) in new[] { ("beach", 3), ("space", 4), ("harbor", 3) })
                        {
                            for (var k = 1; k < days; k++)
                                archive.Add(new JObject { ["destId"] = dest, ["dayIndex"] = k, ["variant"] = Gooby.Core.Life.Postcards.VariantOf(dest, start, k), ["atMs"] = start + k * Gooby.Core.Life.Vacation.MsPerDay });
                            start += (days + 2) * Gooby.Core.Life.Vacation.MsPerDay;
                        }
                        v["archive"] = archive;
                        v["visited"] = new JObject { ["beach"] = true, ["space"] = true, ["harbor"] = true, ["bakery"] = true };
                        v["trips"] = 4;
                        s["vacation"] = v;
                    }, GameHost.TicksMs);
                    _host.Store.NotifySliceChanged("vacation");
                    _host.World?.Room?.Refresh(_host.Store);
                    Debug.Log("[qa] postkarten " + a1 + " → " + Gooby.Core.Life.Postcards.ArchiveOf(_host.Store.State).Count + " cards");
                    break;
                }
                case "spielzeug":
                {
                    // "spielzeug gib <toy> [n]": POW! toys into Gooby's box (play them via push pflege → toy-<id>)
                    if (a1 == "gib" && rest.Trim().Length > 0)
                    {
                        var giveArgs = rest.Trim().Split(' ');
                        var count = giveArgs.Length > 1 && int.TryParse(giveArgs[1], out var n) ? n : 1;
                        _host.Store.Update(s =>
                        {
                            var items = Gooby.Core.Json.J.EnsureDict(Gooby.Core.Json.J.EnsureDict(s, "inventory"), "items");
                            items[giveArgs[0]] = Gooby.Core.Json.J.Int(items[giveArgs[0]]) + count;
                        }, GameHost.TicksMs);
                        _host.Store.NotifySliceChanged("inventory");
                    }
                    // "spielzeug spass <0-100>": Gooby's fun, so a tour can play toy after toy
                    if (a1 == "spass" && double.TryParse(rest.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var fun))
                    {
                        _host.Store.Update(s => Gooby.Core.Json.J.EnsureDict(Gooby.Core.Json.J.EnsureDict(s, "gooby"), "stats")["fun"] = fun, GameHost.TicksMs);
                        _host.Store.NotifySliceChanged("gooby");
                    }
                    Debug.Log("[qa] spielzeug " + string.Join(",", Gooby.Core.Life.Spielzeug.Kiste(_host.Store.State).Select(k => k.Def.Id + "×" + k.Anzahl)));
                    break;
                }
                case "storage":
                    if (a1 == "add" && rest.Trim().Length > 0)
                    {
                        // "storage add <itemId> [n]": pieces straight into the build storage (place them with "bau place")
                        var addArgs = rest.Trim().Split(' ');
                        var count = addArgs.Length > 1 && int.TryParse(addArgs[1], out var n) ? n : 1;
                        _host.Store.Update(s =>
                        {
                            var home = Gooby.Core.Json.J.EnsureDict(s, Gooby.Core.Home.HomeState.SliceId);
                            if (!(home["storage"] is JArray storage)) home["storage"] = storage = new JArray();
                            for (var i = 0; i < count; i++) Gooby.Core.Home.StorageLogic.Add(storage, addArgs[0]);
                        }, GameHost.TicksMs);
                        _host.Store.NotifySliceChanged(Gooby.Core.Home.HomeState.SliceId);
                    }
                    Debug.Log("[qa] storage " + Gooby.Core.Home.HomeState.Storage(_host.Store).ToString(Newtonsoft.Json.Formatting.None)
                        + " food " + (Gooby.Core.Json.J.Path(_host.Store.State, "inventory", "food")?.ToString(Newtonsoft.Json.Formatting.None) ?? "{}"));
                    break;
                case "toast":
                    _host.Ui.Toast(line.Substring(5).Trim());
                    break;
                case "server":
                    _host.Online.ConnectTo(a1, rest);
                    break;
                case "offline":
                    _host.Online.GoOffline();
                    break;
                case "online":
                    _host.Online.Apply(connect: true);
                    break;
                case "cam":
                    if (_host.World?.Camera != null) _host.World.Camera.enabled = a1 != "off";
                    break;
                case "campose":
                {
                    // "campose x y z pitch yaw [fov]": the world camera at a fixed pose for close-ups (the next stage change resets it)
                    var v = (a1 + " " + rest).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                    var wc = _host.World?.Camera;
                    if (wc == null || v.Length < 5) { Debug.LogWarning("[qa] campose x y z pitch yaw [fov]"); break; }
                    wc.transform.SetPositionAndRotation(new Vector3(v[0], v[1], v[2]), Quaternion.Euler(v[3], v[4], 0f));
                    if (v.Length > 5) wc.fieldOfView = v[5];
                    break;
                }
                case "ssao":
                {
                    // "ssao on|off": the renderer's ambient-occlusion feature (artefact checks)
                    var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
                    var n = 0;
                    if (urp != null)
                        foreach (var data in urp.rendererDataList)
                            if (data != null)
                                foreach (var feature in data.rendererFeatures)
                                    if (feature != null && feature.name.Contains("AmbientOcclusion")) { feature.SetActive(a1 != "off"); n++; }
                    Debug.Log("[qa] ssao " + a1 + " (" + n + " features)");
                    break;
                }
                case "licht":
                    // "licht" lists the lights; "licht <name|*> on|off|nocookie|noshadow|bias <depth> <normal>" changes them
                    // (bias: the light's own shadow bias instead of the pipeline asset's — artefact checks)
                    foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    {
                        var how = rest.Trim().Split(' ');
                        if (a1.Length > 0 && (a1 == "*" || l.name == a1))
                        {
                            if (how[0] == "nocookie") l.cookie = null;
                            else if (how[0] == "noshadow") l.shadows = LightShadows.None;
                            else if (how[0] == "bias" && how.Length >= 3)
                            {
                                UnityEngine.Rendering.Universal.LightExtensions.GetUniversalAdditionalLightData(l).usePipelineSettings = false;
                                l.shadowBias = float.Parse(how[1], CultureInfo.InvariantCulture);
                                l.shadowNormalBias = float.Parse(how[2], CultureInfo.InvariantCulture);
                            }
                            else l.enabled = how[0] != "off";
                        }
                        Debug.Log("[qa] licht " + l.name + " " + l.type + " on=" + (l.enabled && l.gameObject.activeInHierarchy) + " i=" + l.intensity.ToString("F2")
                            + " cookie=" + (l.cookie != null ? l.cookie.name : "-") + " shadows=" + l.shadows + " bias=" + l.shadowBias.ToString("F2") + "/" + l.shadowNormalBias.ToString("F2")
                            + " at=" + l.transform.position.ToString("F1"));
                    }
                    break;
                case "outfitbilder":
                    // "outfitbilder <dir>": the wardrobe pictures baked so far as PNGs
                    Debug.Log("[qa] outfitbilder " + Gooby.Unity.World.CosmeticPreview.Dump(string.IsNullOrEmpty(a1) ? "/tmp/outfitbilder" : a1));
                    break;
                case "camgooby":
                {
                    // "camgooby [metres]": the world camera in front of Gooby's face, looking at his head (the next stage change resets it)
                    var w = _host.World;
                    if (w?.Gooby == null || w.Camera == null) { Debug.LogWarning("[qa] camgooby: no Gooby on stage"); break; }
                    var dist = a1.Length > 0 ? float.Parse(a1, CultureInfo.InvariantCulture) : 1.2f;
                    var head = w.Avatar?.Bone("head");
                    var target = head != null ? head.position : w.Gooby.position + Vector3.up * 0.8f;
                    var front = w.Gooby.forward;
                    front.y = 0f;
                    var from = target + (front.sqrMagnitude > 0.01f ? front.normalized : Vector3.back) * dist + Vector3.up * dist * 0.15f;
                    w.Camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from));
                    Debug.Log("[qa] camgooby head=" + target.ToString("F2") + " cam=" + from.ToString("F2"));
                    break;
                }
                case "shadows":
                    foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) l.shadows = a1 == "off" ? LightShadows.None : LightShadows.Soft;
                    break;
                case "post":
                    if (_host.World?.Camera != null) UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(_host.World.Camera).renderPostProcessing = a1 != "off";
                    break;
                case "bs":
                    Battleship(a1);
                    break;
                case "ch":
                    Chess(a1, rest);
                    break;
                case "bau":
                    Bau(a1, rest);
                    break;
                case "presets":
                {
                    // "presets" opens the sheet from build mode; "presets save <slot> <name> | apply <slot> | delete <slot>"
                    if (_host.Ui.Current is BuildModeScreen bm) { bm.OpenPresets(); break; }
                    if (!(_host.Ui.Current is PresetSheetScreen sheet)) { Debug.LogWarning("[qa] presets: build mode not open"); break; }
                    var pp = rest.Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    var slot = pp.Length > 0 && int.TryParse(pp[0], out var sl) ? sl : 0;
                    if (a1 == "save")
                    {
                        var field = sheet.Root?.Q<UnityEngine.UIElements.TextField>("preset-name");
                        if (field != null) field.value = pp.Length > 1 ? pp[1] : "QA";
                        sheet.Save(slot);
                    }
                    else if (a1 == "apply") sheet.Apply(slot, confirmed: true);
                    else if (a1 == "delete") sheet.Delete(slot, confirmed: true);
                    break;
                }
                case "rmp":
                    Rmp(a1, rest);
                    break;
                case "mgend":
                    if (_host.Ui.Current is MinigameHostScreen mg) mg.QaEndRound();
                    break;
                case "mod":
                {
                    // arcade bonus events: "mod force <gameId> <type>" starts one now (level locks bypassed), "mod clear" drops it
                    var now = _host.Store.Clock.NowMs();
                    var modArgs = rest.Trim().Split(' ');
                    var ok = false;
                    if (a1 == "force" && modArgs.Length >= 2) _host.Store.Update(s => ok = Gooby.Core.Minigames.ModifierEngine.ForceEvent(s, modArgs[0], modArgs[1], now), GameHost.TicksMs);
                    else if (a1 == "clear") _host.Store.Update(s => ok = Gooby.Core.Minigames.ModifierEngine.ClearEvent(s), GameHost.TicksMs);
                    Debug.Log("[qa] mod " + a1 + " " + rest.Trim() + " → " + (ok ? "ok" : "refused"));
                    break;
                }
                case "ereignis":
                {
                    // starts a random event now ("ereignis sockensuche"); the HUD card shows it on its next look
                    var def = Gooby.Core.Life.RandomEvents.DefById(Gooby.Core.Life.RandomEvents.Defs(), a1);
                    if (def == null) { Debug.LogWarning("[qa] ereignis: no def " + a1); break; }
                    _host.Store.Update(s => { Gooby.Core.Life.RandomEvents.Slice(s)["active"] = new JObject(); Gooby.Core.Life.RandomEvents.Activate(s, def, _host.Store.Clock.NowMs()); }, GameHost.TicksMs);
                    _host.Store.NotifySliceChanged(Gooby.Core.Life.RandomEvents.SliceId);
                    Debug.Log("[qa] ereignis " + a1);
                    break;
                }
                case "room":
                    // stages a room of the house (living | kitchen | bathroom | bedroom), Gooby at its door
                    Debug.Log("[qa] room " + a1 + " → " + (_host.World != null && _host.World.EnterRoom(a1) ? "ok" : "refused"));
                    break;
                case "brake":
                    // holds the street car like the BREMSE button ("brake on|off")
                    if (_host.Ui.Current is CityDriveScreen brakeDrive) brakeDrive.Braking = a1 != "off";
                    else Debug.LogWarning("[qa] brake: not on the street");
                    break;
                case "zaehmen":
                    // taming at the forest edge: "zaehmen schleichen on|off", "zaehmen ducken", "zaehmen tap"
                    if (_host.Ui.Current is RanchZaehmScreen zaehm)
                    {
                        if (a1 == "schleichen") zaehm.QaSchleichen(rest.Trim().EndsWith("on"));
                        else if (a1 == "ducken") zaehm.QaDucken();
                        else if (a1 == "tap") zaehm.QaBeruhigen();
                        else if (a1 == "auto") zaehm.QaAutoTap = true;
                        else if (a1 == "wetter") zaehm.QaWetter(rest.Trim().Split(' ').Last());
                        Debug.Log("[qa] zaehmen " + a1 + " → " + zaehm.Phase);
                    }
                    else Debug.LogWarning("[qa] zaehmen: not at the forest edge");
                    break;
                case "tuer":
                    // the door gag: "tuer klemmt" (the next door sticks), "tuer gehe" (Gooby uses a door), "tuer tap" (one mash tap)
                    if (a1 == "klemmt" && HomeHudScreen.Instance != null) HomeHudScreen.Instance.QaDoorStuck = true;
                    else if (a1 == "gehe") Debug.Log("[qa] tuer gehe → " + (_host.World != null && _host.World.QaUseDoor()));
                    else if (a1 == "tap") HomeHudScreen.Instance?.QaDoorTap();
                    break;
                case "warp":
                    // street drive: the car stands just before a place's kerb ("warp ranch", "warp tierarzt")
                    if (_host.Ui.Current is CityDriveScreen warpDrive) warpDrive.QaWarp(Gooby.Core.City.DriveModel.PlaceById(a1));
                    else Debug.LogWarning("[qa] warp: not on the street");
                    break;
                case "gps":
                    // GPS destination of the street drive: "gps <place-id>" or "gps off" (the tours tap the pins for real)
                    if (_host.Ui.Current is CityDriveScreen gpsDrive) gpsDrive.SetZiel(a1 == "off" ? null : Gooby.Core.City.DriveModel.PlaceById(a1));
                    else Debug.LogWarning("[qa] gps: not on the street");
                    break;
                case "park":
                {
                    // Funkelpark: "park ride wheel|karussell|coaster|scooter" (taps the ride button), "park stop", "park snacks"
                    var parkScreen = _host.Ui.Current as FunkelparkScreen;
                    if (parkScreen == null) { Debug.LogWarning("[qa] park: not in the Funkelpark"); break; }
                    if (a1 == "stop") _host.World?.StopParkRide();
                    else if (a1 == "ride" || a1 == "snacks") parkScreen.QaTap(a1 == "snacks" ? "snacks" : rest.Trim());
                    break;
                }
                case "leaf":
                    // picks a fallen leaf on the lawn by id (state leaves.lying) — the tours tap them for real
                    Debug.Log("[qa] leaf " + a1 + " → " + (_host.World != null && _host.World.PickLeaf(a1) ? "ok" : "refused"));
                    break;
                case "tapfurn":
                    // taps the first piece of a kind in the staged room: "tapfurn dusche|klo|zahnputz|fernseher|licht|spiegel|sitzen"
                    Debug.Log("[qa] tapfurn " + a1 + " → " + (_host.World != null && _host.World.QaTapFurniture(a1) ? "ok" : "refused"));
                    break;
                case "ball":
                    Debug.Log("[qa] ball → " + (_host.World != null && _host.World.QaThrowBall() ? "thrown" : "refused"));
                    break;
                case "urlaub":
                {
                    // the holiday: "urlaub buchen <ziel>" (booked, away at once), "urlaub besuch", "urlaub mini|souvenir|pet|leave",
                    // "urlaub spot <n>", "urlaub zurueck" (returnReady + picked up → home), "urlaub heimkehr" (HUD reunion now)
                    var now = _host.Store.Clock.NowMs();
                    switch (a1)
                    {
                        case "buchen":
                            var ziel = parts.Length > 2 ? parts[2] : "beach";
                            _host.Store.Update(s => Gooby.Core.Json.J.EnsureDict(s, "economy")["coins"] = Gooby.Core.Json.J.Int(Gooby.Core.Json.J.Path(s, "economy", "coins")) + 1000, GameHost.TicksMs);
                            Debug.Log("[qa] urlaub buchen " + ziel + " → " + Gooby.Core.City.Travel.Buchen(_host.Store, ziel, now));
                            break;
                        case "besuch":
                            if (HomeHudScreen.Instance != null) HomeHudScreen.Instance.OpenVacation();
                            break;
                        case "zurueck":
                            _host.Store.Update(s =>
                            {
                                var v = Gooby.Core.Life.Vacation.SliceOf(s);
                                v["returnAt"] = now - 1000;
                                v["pickupBy"] = now + Gooby.Core.Life.Vacation.PickupWindowMs;
                                s["vacation"] = v;
                            }, GameHost.TicksMs);
                            Debug.Log("[qa] urlaub zurueck → " + Gooby.Core.City.Travel.Abholen(_host.Store, now).Ok);
                            break;
                        default:
                            if (_host.Ui.Current is VacationScreen vs) vs.Qa(a1, parts.Length > 2 ? int.Parse(parts[2]) : 0);
                            else Debug.LogWarning("[qa] urlaub " + a1 + ": not on holiday");
                            break;
                    }
                    break;
                }
                case "mcgooby":
                    // "mcgooby bot [n]": plays the next n actions of the shift well
                    for (var i = 0; i < (parts.Length > 2 ? int.Parse(parts[2]) : 1); i++)
                        if (_host.Ui.Current is McGoobyScreen shift) shift.QaBot();
                    break;
                case "gestalten":
                {
                    // outside parts: "gestalten teil zaun metall [farbe]" | "gestalten nummer 12"
                    var g = (rest ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (!(_host.Ui.Current is CustomizeScreen cs)) { Debug.LogWarning("[qa] gestalten: panel not open"); break; }
                    if (a1 == "nummer" && g.Length > 0) cs.QaNummer(int.Parse(g[0]));
                    else if (a1 == "teil" && g.Length > 1) cs.QaTeil(g[0], g[1], g.Length > 2 ? g[2] : null);
                    Debug.Log("[qa] gestalten " + a1 + " " + rest);
                    break;
                }
                case "goobea":
                    // the furniture store: "goobea kat sitzen | suche sofa | waehle loungeSofa | farbe rose | kaufen"
                    if (_host.Ui.Current is GoobeaScreen moebel) moebel.Qa(parts);
                    else Debug.LogWarning("[qa] goobea: store not open");
                    break;
                case "goobye":
                    // the corner store: "goobye fuellen | oeffnen | schnell | abrechnen | feierabend | grossmarkt | korb apple 3 |
                    // losfahren | ankunft | ausladen | eil | angebot obst | modul | lieferwagen | slot 2 | einraeumen 0 apple | kaufen | ok"
                    if (_host.Ui.Current is GoobyeScreen laden) laden.Qa(parts);
                    else Debug.LogWarning("[qa] goobye: store not open");
                    break;
                case "ranch":
                    // the ranch farm: "ranch kaufen | pferd wolke | pflege heu | maehen | pfluecken 1 | heu 5 | ausbau boxen2 |
                    // gear sattel rot | hof | reiten | tor | zu | tap trog"
                    if (_host.Ui.Current is RanchHofScreen hof) hof.Qa(parts);
                    else Debug.LogWarning("[qa] ranch: farm not open");
                    break;
                case "dorf":
                    // Hufingen: "dorf laden futterhof | kaufen [poolId] | verkaufen wolke | futter hafer | ankauf apfel 2 |
                    // schmiede hufeisen_gold | anlegen [ware] | moebel table | gear sattel rot | brunnen | zu | heim"
                    if (_host.Ui.Current is RanchDorfScreen dorf) dorf.Qa(parts);
                    else Debug.LogWarning("[qa] dorf: village not open");
                    break;
                case "welt":
                    // the open ranch region: "welt zone see | fund wasserfall | reite hufingen | dahin hoehle | stick 0 1 |
                    // galopp | karte | tippe alter_baum | aktion | wetter regen | zu"
                    if (_host.Ui.Current is RanchWeltScreen welt) welt.Qa(parts);
                    else Debug.LogWarning("[qa] welt: region not open");
                    break;
                case "rbau":
                    // building the ranch: "rbau kat deko | waehle reithalle | ghost 5 11 [rot] | drehen | bauen | linie 0 6 9 6 |
                    // tippe 2 8 | ausbauen | verschieben | abriss | zone nord | weidezaun | kamera 0 54 24 30 | effekte | zu"
                    if (_host.Ui.Current is RanchBauScreen rbau) rbau.Qa(parts);
                    else Debug.LogWarning("[qa] rbau: build mode not open");
                    break;
                case "nougat":
                    // the kitchen's chocolate dispenser: "nougat haengen" hangs one on a free wall, "nougat" cranks it
                    if (HomeHudScreen.Instance != null) HomeHudScreen.Instance.QaNougat(a1);
                    else Debug.LogWarning("[qa] nougat: no home HUD");
                    break;
                case "quest":
                    // "quest done": the first unclaimed daily quest that a counter measures counts as finished
                    Debug.Log("[qa] quest " + a1 + " → " + (_host.Quests != null && _host.Quests.QaFinishFirst() ? "ok" : "nothing"));
                    break;
                case "auge":
                    // "auge an|aus": the HUD eye — badges over every interactable piece of the room
                    Debug.Log("[qa] auge " + a1 + " → " + (HomeHudScreen.Instance != null ? HomeHudScreen.Instance.QaSpotlight(a1 == "an") + " badges" : "no home HUD"));
                    break;
                case "einstellung":
                {
                    // "einstellung farbsehen deutan | kontrast an | hinweise lang | bewegung aus": the accessibility block
                    var v = parts.Length > 2 ? parts[2] : "";
                    var s = _host.Settings;
                    switch (a1)
                    {
                        case "farbsehen": s.SetColorVision(v); break;
                        case "kontrast": s.SetHighContrast(v == "an"); break;
                        case "hinweise": s.SetHintDuration(v); break;
                        case "bewegung": s.SetReducedMotion(v == "aus"); break;
                    }
                    Debug.Log("[qa] einstellung " + a1 + " " + v);
                    break;
                }
                case "coop":
                    // the ride along: "coop los" (host invites and drives), "coop ja" (guest accepts the waiting invitation)
                    if (a1 == "los") _host.Online.StartCoopDrive();
                    else if (a1 == "ja") _host.Online.AcceptCoopDrive();
                    Debug.Log("[qa] coop " + a1 + " role=" + _host.Online.CoopDrive.Role);
                    break;
                case "beet":
                    // "beet 2 1": a tap on that bed of the garden field (the HUD opens its sheet)
                    _host.World?.TapBed(new Gooby.Core.Home.Cell(int.Parse(a1, CultureInfo.InvariantCulture), int.Parse(parts.Length > 2 ? parts[2] : "0", CultureInfo.InvariantCulture)));
                    Debug.Log("[qa] beet " + a1);
                    break;
                case "garage":
                {
                    // "garage kaufen" (first free 2×3 spot of the field, the purse topped up), "garage tor", "garage los"
                    if (a1 == "kaufen")
                    {
                        var grid = Gooby.Core.Garden.GardenState.Grid(_host.Store);
                        var spot = (Gooby.Core.Home.Cell?)null;
                        for (var y = 0; y < grid.Height && spot == null; y++)
                        for (var x = 0; x < grid.Width && spot == null; x++)
                            if (grid.CanPlaceStructure(Gooby.Core.Garden.GarageLogic.Kind, new Gooby.Core.Home.Cell(x, y)).Ok) spot = new Gooby.Core.Home.Cell(x, y);
                        if (spot == null) { Debug.LogWarning("[qa] garage: no free spot"); break; }
                        _host.Store.Update(s =>
                        {
                            var econ = Gooby.Core.Json.J.EnsureDict(s, "economy");
                            econ["coins"] = System.Math.Max(Gooby.Core.Json.J.Int(econ["coins"]), Gooby.Core.Garden.GarageLogic.Preis);
                        }, GameHost.TicksMs);
                        var bought = Gooby.Core.Garden.GarageLogic.Kaufen(_host.Store, spot.Value);
                        _host.World?.RefreshBeds();
                        Debug.Log("[qa] garage kaufen at " + spot.Value.X + "," + spot.Value.Y + " → " + (bought.Ok ? "ok" : bought.Reason));
                    }
                    else if (a1 == "tor") Debug.Log("[qa] garage tor → " + (_host.World != null && _host.World.ToggleGarageDoor() ? _host.World.Beds.GarageZustand : "no garage"));
                    else if (a1 == "tippen")
                    {
                        // a finger on the garage: the world raycast, its BedMarker, the HUD's sheet + door toggle
                        var p = _host.World?.GarageScreenPoint();
                        var hit = p.HasValue && _host.World.TapWorld(p.Value);
                        Debug.Log("[qa] garage tippen at " + (p.HasValue ? p.Value.ToString() : "-") + " → " + (hit ? _host.World.Beds.GarageZustand : "missed"));
                    }
                    else if (a1 == "los") { Debug.Log("[qa] garage los (in view: " + (_host.World?.GarageInView ?? false) + ")"); HomeHudScreen.DriveOff(_host); }
                    break;
                }
                case "guide":
                    // the guided first quarter hour: "guide start" (fresh tour from step 1), "guide end"
                    if (HomeHudScreen.Instance != null) HomeHudScreen.Instance.QaGuide(a1);
                    else Debug.LogWarning("[qa] guide: no home HUD");
                    break;
                case "sammlung":
                {
                    // "sammlung fill fish [n]": the album's collections — the first n entries of a set found once
                    var sp = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    Debug.Log("[qa] sammlung " + a1 + " " + rest + " → " + (a1 == "fill" && sp.Length > 0
                        ? AlbumScreen.QaFill(_host, sp[0], sp.Length > 1 ? int.Parse(sp[1], CultureInfo.InvariantCulture) : 99)
                        : "usage: sammlung fill <set> [n]"));
                    break;
                }
                case "knoepfe":
                    // the one-time "Deine Knöpfe" card: "knoepfe" (forget it was seen → it comes on a quiet home), "knoepfe ok"
                    Debug.Log("[qa] knoepfe " + a1 + " → " + (HomeHudScreen.Instance != null ? HomeHudScreen.Instance.QaButtonsCard(a1) : "no home HUD"));
                    break;
                case "seele":
                    // Gooby's own life: "seele tap 3 | idle idle_fenster | absicht | ueberraschung sup_turm | fund |
                    // plaudern | gefuehl donner | geburtstag | chips gruss_morgen | wasnun | aus | an"
                    if (HomeHudScreen.Instance != null) HomeHudScreen.Instance.QaSeele(parts);
                    else Debug.LogWarning("[qa] seele: no home HUD");
                    break;
                case "brush":
                    if (_host.Ui.Current is BrushScreen brushing) brushing.QaRub();
                    else Debug.LogWarning("[qa] brush: not brushing");
                    break;
                case "spot":
                {
                    // picks a workbench find on the bed field: "spot x y" or "spot" for the first one lying
                    var open = Gooby.Core.Garden.GardenSpots.Offene(_host.Store);
                    var at = parts.Length >= 3 ? new Gooby.Core.Home.Cell(int.Parse(parts[1]), int.Parse(parts[2])) : open.Count > 0 ? open[0].At : new Gooby.Core.Home.Cell(-1, -1);
                    Debug.Log("[qa] spot " + at.X + "," + at.Y + " (" + open.Count + " lying) → " + (_host.World != null && _host.World.PickSpot(at) ? "ok" : "refused"));
                    break;
                }
                case "patch":
                {
                    // merges one JSON object into the save (arrays replaced), e.g. horses for a ranch visit tour:
                    // patch {"ranch":{"tiere":{"pferde":{…}}}}
                    var patch = JObject.Parse(line.Substring(cmd.Length).Trim());
                    _host.Store.Update(s => s.Merge(patch, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace }), GameHost.TicksMs);
                    foreach (var slice in patch.Properties()) _host.Store.NotifySliceChanged(slice.Name);
                    Debug.Log("[qa] patch " + string.Join(",", patch.Properties().Select(p => p.Name)));
                    break;
                }
                case "mgbot":
                    if (_host.Ui.Current is MinigameHostScreen bothost)
                    {
                        // "mgbot hold": the tournament bot rides but stays on the result card (screenshots, checks)
                        if (bothost is RanchTurnierScreen holdTurnier && holdTurnier.Game != null) holdTurnier.Game.BotBleibtBeimErgebnis = a1 == "hold";
                        bothost.QaBot = a1 != "off";
                        Debug.Log("[qa] mgbot " + (bothost.QaBot ? "on" : "off") + (bothost.QaBotAvailable ? "" : " (this engine has no ApplyBot)"));
                    }
                    break;
                case "fieldtap":
                {
                    // fieldtap fx fy: a touch on the running minigame's play field at fractions of its box
                    var p = (a1 + " " + rest).Split(' ');
                    var ok = _host.Ui.Current is MinigameHostScreen tapHost
                        && tapHost.QaTapField(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture));
                    Debug.Log("[qa] fieldtap " + a1 + " " + rest + (ok ? "" : " (no field)"));
                    break;
                }
                case "perf":
                    LogPerf();
                    break;
                case "quit":
                    _host.SaveAll();
                    Application.Quit();
                    break;
                default:
                    Debug.LogWarning("[qa] unknown command " + cmd);
                    break;
            }
        }

        /// <summary>Leaves: balance, today's picks and the leaves on the lawn with their screen point (top-left origin, for taps).</summary>
        private JObject LeavesState()
        {
            var gs = _host.Store;
            var day = gs.Clock.LocalDay();
            var lying = new JArray();
            var cam = _host.World?.Camera;
            if (_host.World?.Leaves != null && _host.World.Leaves.Root.activeInHierarchy)
                foreach (var (spot, at) in _host.World.Leaves.Lying)
                {
                    var p = cam != null ? cam.WorldToScreenPoint(at) : Vector3.zero;
                    lying.Add(new JObject
                    {
                        ["id"] = spot.Id, ["golden"] = spot.Golden, ["x"] = Mathf.RoundToInt(p.x), ["y"] = Mathf.RoundToInt(Screen.height - p.y),
                        ["wx"] = Math.Round(at.x, 2), ["wz"] = Math.Round(at.z, 2),
                    });
                }
            var g = _host.World?.Gooby;
            return new JObject
            {
                ["gooby"] = g == null ? new JArray() : new JArray(Math.Round(g.position.x, 2), Math.Round(g.position.z, 2)),
                ["visible"] = _host.World?.Leaves != null && _host.World.Leaves.Root.activeInHierarchy,
                ["count"] = Gooby.Core.Garden.GardenLeaves.Count(gs),
                ["total"] = Gooby.Core.Garden.GardenLeaves.Total(gs),
                ["picked"] = Gooby.Core.Garden.GardenLeaves.Picked(gs, day).Count,
                ["perDay"] = Gooby.Core.Garden.GardenLeaves.CountFor(day),
                ["care"] = Gooby.Core.Json.J.Int(Gooby.Core.Json.J.Path(gs.State, "leaves", "care")),
                ["lying"] = lying,
            };
        }

        private void Push(string what)
        {
            var parts = what.Split(':');
            switch (parts[0])
            {
                case "friends": _host.Ui.Push(new FriendsScreen(_host)); break;
                case "mailbox": _host.Ui.Push(new MailboxScreen(_host, parts.Length > 1 ? parts[1] : null)); break;
                case "instant": _host.Ui.Push(new InstantScreen(_host)); break;
                case "settings": _host.Ui.Push(new SettingsScreen(_host, parts.Length > 1 ? parts[1] : "spiel")); break;
                case "arcade": _host.Ui.Push(new ArcadeScreen(_host)); break;
                case "album": _host.Ui.Push(new AlbumScreen(_host, parts.Length > 1 ? parts[1] : null)); break;
                case "achievements": _host.Ui.Push(new AchievementsScreen(_host)); break;
                case "profile": _host.Ui.Push(new ProfileScreen(_host)); break;
                case "boarding": _host.Ui.Push(new BoardingScreen(_host)); break;
                case "gfree": _host.Ui.Push(new GoobyFreeScreen(_host)); break;
                case "postkarten": _host.Ui.Push(new PostcardsScreen(_host)); break;
                case "onboarding": _host.Ui.Push(new OnboardingScreen(_host)); break;
                case "radio": _host.Ui.Push(new RadioScreen(_host)); break;
                case "blaetter": _host.Ui.Push(new LeafShopScreen(_host)); break;
                case "codes": _host.Ui.Push(new CodesScreen(_host)); break;
                case "shop": _host.Ui.Push(new ShopScreen(_host)); break;
                case "inventory": _host.Ui.Push(new InventoryScreen(_host)); break;
                case "outfit": _host.Ui.Push(new OutfitScreen(_host)); break;
                case "package": _host.Ui.Push(new PackageScreen(_host, line => Debug.Log("[qa] say " + line))); break;
                case "neu": _host.Ui.Push(new WhatsNewScreen(_host)); break;
                case "quests": _host.Ui.Push(new QuestSheetScreen(_host)); break;
                case "dev": _host.Ui.Push(new DevScreen(_host)); break;
                case "talk": _host.Ui.Push(new TalkScreen(_host, line => Debug.Log("[qa] say " + line))); break;
                case "gooberando": _host.Ui.Push(new GooberandoScreen(_host)); break;
                case "phone": _host.Ui.Push(new PhoneScreen(_host)); break;
                case "kamera": _host.Ui.Push(new PhotoModeScreen(_host)); break;
                case "galerie": _host.Ui.Push(new GalleryScreen(_host)); break;
                case "taxi": _host.Ui.Push(new TaxiScreen(_host)); break;
                case "geschichte": _host.Ui.Push(new StoryTimeScreen(_host, line => Debug.Log("[qa] say " + line), null)); break;
                case "umzug": _host.Ui.Push(new UmzugScreen(_host)); break;
                case "werkstatt": _host.Ui.Push(new CraftScreen(_host)); break;
                case "pflege": _host.Ui.Push(new CareSheetScreen(_host, line => Debug.Log("[qa] says " + line))); break;
                case "stand": _host.Ui.Push(new MarktStandScreen(_host)); break;
                case "dlc": _host.Ui.Push(new DlcScreen(_host)); break;
                case "mcgooby": _host.Ui.Push(new McGoobyScreen(_host, parts.Length > 2 ? long.Parse(parts[2]) : 4711)); break;
                case "goobay": _host.Ui.Push(new GoobayScreen(_host)); break;
                case "goobye": _host.Ui.Push(new GoobyeScreen(_host)); break;
                case "goobea": _host.Ui.Push(new GoobeaScreen(_host)); break;
                case "ranchhof": _host.Ui.Push(new RanchHofScreen(_host)); break;
                case "ranchbau": _host.Ui.Push(new RanchBauScreen(_host)); break;
                case "ranchwelt": _host.Ui.Push(new RanchWeltScreen(_host, Gooby.Core.Minigames.Ranch.RanchHof.PferdIds(_host.Store.State).FirstOrDefault(), parts.Length > 1 ? parts[1] : "hof")); break;
                case "gestalten": _host.Ui.Push(new CustomizeScreen(_host)); break;
                case "recap": _host.Ui.Push(new RecapScreen(_host, parts.Length > 1 ? int.Parse(parts[1]) : 5)); break;
                case "markttag": _host.Ui.Push(new MarktTagScreen(_host, watch: parts.Length < 2 || parts[1] != "abrechnen")); break;
                case "ranchmp": _host.Online.OpenRanchMp(); break;
                case "bau": _host.Ui.Push(new BuildModeScreen(_host, parts.Length > 1 ? parts[1] : _host.World?.RoomId ?? "living")); break;
                case "game":
                {
                    var id = parts.Length > 1 ? parts[1] : "carrotCatch";
                    var game = MinigameHostScreen.For(_host, id);
                    if (game != null) _host.Ui.Push(game);
                    else Debug.LogWarning("[qa] no Unity host for game " + id);
                    break;
                }
                default: Debug.LogWarning("[qa] unknown screen " + what); break;
            }
        }

        private VisualElement Find(string name)
        {
            var root = _host.Ui.Document.rootVisualElement;
            // topmost first: the newest screen / notice wins over an element of the same name underneath;
            // "letter-*" matches the first element whose name starts with "letter-"
            var all = name.EndsWith("*")
                ? root.Query<VisualElement>().Where(e => e.name != null && e.name.StartsWith(name.TrimEnd('*'))).ToList()
                : root.Query<VisualElement>(name).ToList();
            if (name.EndsWith("*")) return all.FirstOrDefault(e => e.resolvedStyle.display != DisplayStyle.None && e.enabledInHierarchy);
            return all.LastOrDefault(e => e.resolvedStyle.display != DisplayStyle.None && e.enabledInHierarchy) ?? all.LastOrDefault();
        }

        private void Click(string name)
        {
            VisualElement el = null;
            if (name.StartsWith("text:"))
            {
                // a button by its visible label (notice cards, dialog buttons)
                var label = name.Substring(5).Replace('_', ' ');
                el = _host.Ui.Document.rootVisualElement.Query<Button>().ToList()
                    .LastOrDefault(b => b.text == label || b.Q<Label>()?.text == label);
            }
            else el = Find(name);
            if (el == null) { Debug.LogWarning("[qa] click: no element " + name); return; }
            if (el is Button b)
            {
                using (var ev = NavigationSubmitEvent.GetPooled()) { ev.target = b; b.SendEvent(ev); }
            }
            else
            {
                using (var ev = ClickEvent.GetPooled()) { ev.target = el; el.SendEvent(ev); }
            }
            Debug.Log("[qa] clicked " + name);
        }

        private void Type(string name, string text)
        {
            var tf = Find(name) as TextField;
            if (tf == null) { Debug.LogWarning("[qa] type: no text field " + name); return; }
            tf.value = text;
            if (tf.name == "player-name") _host.Online.SetPlayerName(text);
        }

        /// <summary>What the frame draws: active renderers/triangles per top-level root, particles, lights, shadow casters.</summary>
        private static void LogPerf()
        {
            var byRoot = new System.Collections.Generic.Dictionary<string, (int n, long tris, int casters)>();
            long total = 0;
            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                long tris = 0;
                if (r is MeshRenderer mr && mr.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null) tris = Tris(mf.sharedMesh);
                else if (r is SkinnedMeshRenderer sk && sk.sharedMesh != null) tris = Tris(sk.sharedMesh);
                var t = r.transform;
                var path = t.root.name;
                if (t.root != t && t.parent != null)
                {
                    var p = t;
                    while (p.parent != null && p.parent != t.root) p = p.parent;
                    path += "/" + p.name;
                }
                byRoot.TryGetValue(path, out var e);
                byRoot[path] = (e.n + 1, e.tris + tris, e.casters + (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off ? 1 : 0));
                total += tris;
            }
            var ps = UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Count(p => p.gameObject.activeInHierarchy);
            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Count(l => l.isActiveAndEnabled);
            Debug.Log("[qa] perf renderers=" + byRoot.Values.Sum(v => v.n) + " tris=" + total + " particles=" + ps + " lights=" + lights);
            foreach (var kv in byRoot.OrderByDescending(k => k.Value.tris).Take(15))
                Debug.Log("[qa] perf   " + kv.Key + " n=" + kv.Value.n + " tris=" + kv.Value.tris + " casters=" + kv.Value.casters);
        }

        private static long Tris(Mesh m)
        {
            long n = 0;
            for (var i = 0; i < m.subMeshCount; i++) n += (long)m.GetIndexCount(i);
            return n / 3;
        }

        private JObject State()
        {
            var c = _host.Online.Client;
            var w = _host.World;
            var o = new JObject
            {
                ["status"] = c.Status.ToString(),
                ["error"] = c.LastErrorCode,
                ["serverSaved"] = _host.Settings.ServerAddress ?? "",
                ["code"] = c.FriendCode,
                ["name"] = c.PlayerName,
                ["screen"] = _host.Ui.Current?.GetType().Name,
                ["frame"] = Time.frameCount,
                ["music"] = _host.Audio != null ? _host.Audio.Track : "",
                ["park"] = w?.Park == null ? null : new JObject { ["ride"] = w.Park.ActiveRide, ["bumps"] = w.Park.Bumps, ["visits"] = Gooby.Core.Json.J.Int(Gooby.Core.City.ParkLogic.SliceOf(_host.Store.State)["visits"]) },
                ["fps"] = Math.Round(1.0 / Math.Max(0.001, Time.smoothDeltaTime), 1),
                ["coins"] = Gooby.Core.City.ShopLogic.Coins(_host.Store),
                ["mailUnread"] = c.MailUnread,
                ["mail"] = c.Mail.Count,
                ["outbox"] = c.OutboxCount,
                ["mailPhotos"] = c.Mail.Count(m => m.PhotoId.Length > 0),
                ["instantUnseen"] = c.InstantUnseen,
                // arcade friends leaderboard: games with a friend entry (last /api/arcade/best, patched by result cards)
                ["arcadeTop"] = c.ArcadeBest.FriendTops,
                ["feed"] = new JArray(c.Feed.Select(p => p.Id + ":" + p.Likes + (p.Liked ? ":liked" : "") + (p.Mine ? ":mine" : ""))),
                ["requests"] = new JArray(c.Requests.Select(r => r.FromCode)),
                ["friends"] = new JArray(c.Friends.Select(f => f.Code + ":" + (f.Online ? "on" : "off") + ":" + f.ActivityKind)),
                ["visit"] = c.VisitRole.ToString(),
                ["visitRoom"] = c.VisitRoom,
                ["peerInRoom"] = c.PeerInRoom,
                ["visiting"] = w != null && w.Visiting,
                ["hosting"] = w != null && w.Hosting,
                ["room"] = w?.RoomId,
                ["peerVisible"] = w != null && w.Peer != null,
                ["peerNapping"] = w != null && w.PeerNapping,
                ["crateNap"] = w != null && w.CrateNapping,
                ["couchNap"] = w != null && w.NappingOnCouch,
                ["prompts"] = _host.Ui.Document.rootVisualElement.Query(className: "gb-notice-wrap").ToList().Count,
                ["build"] = w?.Build?.RoomId ?? "",
                ["buildMsg"] = w?.Build?.LastMessage ?? "",
                // the staged room as item@x,y/rot (host: own save, guest: the copy the BUILD_DELTAs keep in sync)
                ["roomItems"] = new JArray(w != null && w.RoomId != "garden" ? w.RoomItemsOf(w.RoomId) : new string[0]),
                ["roomViews"] = w?.Room?.ItemViewCount ?? 0,
                // the ride along: role, an invitation waiting, poses received, the shared station and our own radio
                ["coop"] = _host.Online.CoopDrive.Role,
                ["coopPending"] = _host.Online.CoopDrive.Pending != null,
                ["coopPos"] = _host.Online.CoopDrive.PositionsReceived,
                ["coopRadio"] = _host.Online.CoopDrive.RadioStation,
                ["radio"] = _host.Audio?.RadioStation ?? "",
            };
            if (w?.Drive != null) o["carPos"] = new JArray(Math.Round(w.Drive.X, 2), Math.Round(w.Drive.Z, 2));
            if (w?.Gooby != null) o["pos"] = new JArray(Math.Round(w.Gooby.position.x, 2), Math.Round(w.Gooby.position.z, 2));
            if (w?.Peer != null) o["peerPos"] = new JArray(Math.Round(w.Peer.position.x, 2), Math.Round(w.Peer.position.z, 2));
            o["inventoryMedicine"] = Gooby.Core.Json.J.Int(Gooby.Core.Json.J.Path(_host.Store.State, "inventory", "items", "medicine"));
            var b = _host.Online.Board;
            o["board"] = new JObject
            {
                ["room"] = b.RoomId,
                ["active"] = b.IsActive,
                ["locked"] = b.FleetLocked,
                ["peer"] = b.PeerInRoom,
                ["myTurn"] = b.MyTurn,
                ["awaiting"] = b.AwaitingResult,
                ["shots"] = b.ShotsFired,
                ["hits"] = b.HitsLanded,
                ["sunk"] = b.ShipsSunk,
                ["left"] = b.ShipsLeft,
                ["finished"] = b.Finished,
                ["iWon"] = b.IWon,
                ["reason"] = b.EndReason.ToString(),
                ["invite"] = b.OutgoingInvite,
                ["incoming"] = b.IncomingInvites.Count,
            };
            // Schach: the online table, else an open practice board
            var practice = _host.Ui.Current as ChessScreen;
            var chess = b.IsChess ? b.Chess : practice != null && practice.IsPractice ? practice.Logic : null;
            var board = (JObject)o["board"];
            board["game"] = b.IsChess || practice == null || !practice.IsPractice ? b.Game : "chess-practice";
            board["chessFen"] = chess?.ToFen() ?? "";
            board["chessOver"] = b.IsChess ? b.Finished : practice != null && practice.Over;
            board["chessResult"] = b.IsChess ? b.ResultId : practice != null ? practice.ResultId : "";
            board["chessColor"] = chess == null ? "" : (b.IsChess ? b.MyColor : practice.PlayerColor) == Gooby.Core.Minigames.ChessLogic.White ? "white" : "black";
            board["chessMoves"] = chess?.Moves.Count ?? 0;
            var gn = _host.Online.Gobnom;
            var gscreen = _host.Ui.Current as GobnomScreen;
            var ggame = gscreen?.Game;
            o["gobnom"] = new JObject
            {
                ["paired"] = gn.IsPaired,
                ["room"] = gn.RoomId,
                ["side"] = gn.IsPaired ? gn.MySide : "",
                ["partner"] = gn.PartnerCode,
                ["myVote"] = gn.MyVote,
                ["partnerVote"] = gn.PartnerVote,
                ["running"] = gn.Running,
                ["rejoining"] = gn.Rejoining,
                ["invite"] = gn.OutgoingInvite,
                ["incoming"] = gn.IncomingInvites.Count,
                ["phase"] = ggame?.Phase ?? "",
                ["track"] = ggame?.Track ?? "",
                ["level"] = ggame?.LevelId ?? 0,
                ["online"] = ggame != null && ggame.OnlineRun,
                ["tick"] = ggame?.Run?.Tick ?? -1,
                ["waiting"] = ggame != null && ggame.NetWaiting,
                ["lastWon"] = ggame != null && ggame.LastWon,
            };
            var vn = _host.Online.Gvz;
            var vgame = (_host.Ui.Current as GvzScreen)?.Game;
            o["gvz"] = new JObject
            {
                ["paired"] = vn.IsPaired,
                ["room"] = vn.RoomId,
                ["side"] = vn.IsPaired ? vn.MySide : "",
                ["partner"] = vn.PartnerCode,
                ["startRequested"] = vn.StartRequested,
                ["running"] = vn.Running,
                ["invite"] = vn.OutgoingInvite,
                ["incoming"] = vn.IncomingInvites.Count,
                ["phase"] = vgame?.Phase ?? "",
                ["online"] = vgame != null && vgame.OnlineRun,
                ["zombie"] = vgame != null && vgame.NetZombie,
                ["tick"] = vgame?.Run?.Tick ?? -1,
                ["secondsLeft"] = vgame?.Net?.SecondsLeft() ?? -1,
                ["resource"] = vgame?.Resource ?? 0,
                ["towers"] = vgame?.Run?.Towers.Count ?? 0,
                ["zombies"] = vgame?.Run?.Zombies.Count ?? 0,
                ["winner"] = vgame?.Net?.Winner ?? "",
                ["lastWon"] = vgame != null && vgame.LastWon,
                ["netWins"] = Gooby.Core.Minigames.Gvz.GvzProgress.NetWins(_host.Store.State),
                ["netLosses"] = Gooby.Core.Minigames.Gvz.GvzProgress.NetLosses(_host.Store.State),
            };
            var rc = _host.Online.RanchMp;
            var rscreen = _host.Ui.Current as RanchMpScreen;
            var rlink = rscreen?.Link;
            var rlauf = rlink?.Lauf;
            var rres = rlink?.LastResult;
            var (rTeil, rSiege) = Gooby.Core.Minigames.Ranch.Mp.RmpState.Statistik(_host.Store.State, rscreen?.MenuMode ?? "rennen");
            o["rmp"] = new JObject
            {
                ["active"] = rc.IsActive,
                ["room"] = rc.RoomId,
                ["mode"] = rc.Mode,
                ["kurs"] = rc.Kurs,
                ["phase"] = rc.Phase,
                ["players"] = rc.Players.Count,
                ["ready"] = new JArray(rc.Ready),
                ["it"] = rc.ItCode,
                ["iAmIt"] = rc.IAmIt,
                ["countdownMs"] = rc.StartAtMs > 0 ? rc.CountdownMs : 0,
                ["running"] = rlink != null && rlink.Running,
                ["bot"] = rlink != null && rlink.Bot,
                ["next"] = rlauf?.NextCheckpoint ?? -1,
                ["inZiel"] = rlauf != null && rlauf.InZiel,
                ["gait"] = rlauf?.Controller.Gait ?? "",
                ["pos"] = rlauf == null ? null : new JArray(Math.Round(rlauf.Controller.Pos.X, 1), Math.Round(rlauf.Controller.Pos.Y, 1)),
                ["peers"] = rlauf?.Remotes.Count ?? 0,
                ["invite"] = rc.OutgoingInvite,
                ["incoming"] = rc.IncomingInvites.Count,
                ["rematch"] = new JArray(rc.RematchWanted),
                ["rank"] = rres?.Rank ?? 0,
                ["zeitMs"] = rres?.ZeitMs ?? 0,
                ["ranked"] = rres != null && rres.Ranked,
                ["results"] = new JArray((rres?.All ?? new System.Collections.Generic.List<Gooby.Core.Net.RmpResult>()).Select(r => r.FriendCode + ":" + r.Rank + ":" + r.ZeitMs + (r.Dnf ? ":dnf" : ""))),
                ["menuMode"] = rscreen?.MenuMode ?? "",
                ["siege"] = rSiege,
                ["teilnahmen"] = rTeil,
                ["boardOpen"] = rscreen != null && rscreen.BoardOpen,
                ["boardKurs"] = rscreen?.BoardKurs ?? "",
                ["board"] = new JArray((rscreen?.Board?.Entries ?? new System.Collections.Generic.List<Gooby.Core.Net.RanchBoardEntry>()).Select(e => e.ToString())),
                ["boardRank"] = rscreen?.Board?.Rank ?? 0,
                ["ghosts"] = new JArray(_host.Online.RanchFriendGhostDisziplinen.Select(d => d + ":" + _host.Online.RanchFriendGhostName(d))),
                ["stage"] = rscreen?.StageName ?? "",
                ["host"] = rc.Host,
                ["hostRev"] = rc.HostMetaRev,
                ["playerCodes"] = new JArray(rc.Players.Select(p => p.Code)),
                ["peerCodes"] = new JArray(rlauf?.Remotes.Keys.ToArray() ?? new string[0]),
                // friend code → "name:coat:saddle/pad/halter" of the horse each friend rides (PFERD)
                ["peerHorses"] = new JObject(rc.PeerHorses.Select(kv => new JProperty(kv.Key,
                    Gooby.Core.Json.J.Str(kv.Value["name"]) + ":" + Gooby.Core.Minigames.Ranch.RanchBelohnungen.FellId(kv.Value) + ":"
                    + string.Join("/", Gooby.Core.Minigames.Ranch.RanchHof.GearSlots.Select(s => Gooby.Core.Minigames.Ranch.Mp.RmpPferdLook.Gear(kv.Value, s) ?? "-"))))),
                ["entdeckt"] = new JArray(rlauf?.Entdeckt.ToArray() ?? new string[0]),
                ["yardHorses"] = rscreen?.View?.BesuchPferdeAnzahl ?? 0,
                ["visitHorses"] = (_host.Online.RanchVisitMeta?["pferde"] as JArray)?.Count ?? -1,
                ["folgeMir"] = new JArray(rscreen?.View?.FolgeMirCodes.ToArray() ?? new string[0]),
                ["reactionsIn"] = rscreen == null ? new JObject() : JObject.FromObject(rscreen.ReactionsIn),
                ["dazuOpen"] = rscreen != null && rscreen.DazuOpen,
            };
            var localNow = DateTimeOffset.FromUnixTimeMilliseconds(_host.Store.Clock.NowMs()).ToLocalTime();
            o["weather"] = Gooby.Core.Life.SoulWetter.Zustand(localNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), localNow.Hour + localNow.Minute / 60.0).Typ;
            o["weatherIcon"] = HomeHudScreen.WeatherIcon(localNow);
            var wfx = _host.World?.WeatherView;
            o["weatherFx"] = new JObject
            {
                ["stage"] = wfx?.Where.ToString() ?? "",
                ["typ"] = _host.World?.Weather?.Typ ?? "",
                ["rain"] = wfx != null && wfx.Raining,
                ["splash"] = wfx != null && wfx.Splashing,
                ["snow"] = wfx != null && wfx.Snowing,
                ["flashes"] = wfx?.Flashes ?? 0,
                ["loop"] = Math.Round(wfx?.LoopVolume ?? 0f, 2),
            };
            o["leaves"] = LeavesState();
            var gps = _host.Ui.Current as CityDriveScreen;
            var gpsPins = new JArray();
            JObject pfeil = null;
            if (gps != null)
                foreach (var (id, px) in gps.PinScreenPoints())
                {
                    var e = new JObject { ["id"] = id, ["x"] = Mathf.RoundToInt(px.x), ["y"] = Mathf.RoundToInt(px.y) };
                    if (id == "@pfeil") pfeil = e; else gpsPins.Add(e);
                }
            o["gps"] = new JObject
            {
                ["ziel"] = gps?.GpsZiel ?? "", ["pfeil"] = gps != null && gps.GpsPfeilSichtbar,
                ["pfeilX"] = pfeil?["x"] ?? -1, ["pfeilY"] = pfeil?["y"] ?? -1, ["winkel"] = Math.Round(gps?.GpsPfeilWinkel ?? 0f, 1),
                ["pins"] = gpsPins,
            };
            var mod = Gooby.Core.Minigames.ModifierEngine.ActiveEvent(_host.Store.State, _host.Store.Clock.NowMs());
            o["mod"] = new JObject
            {
                ["game"] = mod?.GameId ?? "",
                ["type"] = mod?.Type ?? "",
                ["plays"] = mod?.RemainingPlays ?? 0,
                ["round"] = (_host.Ui.Current as MinigameHostScreen)?.RoundModifier?["type"]?.ToString() ?? "",
                ["energy"] = Math.Round(Gooby.Core.Json.J.Num(Gooby.Core.Json.J.Path(_host.Store.State, "gooby", "stats", "energy")), 1),
                ["coins"] = Gooby.Core.Json.J.Int(Gooby.Core.Json.J.Path(_host.Store.State, "economy", "coins")),
            };
            var turnier = (_host.Ui.Current as RanchTurnierScreen)?.Game;
            o["turnier"] = new JObject
            {
                ["phase"] = turnier?.Phase ?? "",
                ["disziplin"] = turnier?.Disziplin ?? "",
                ["freundGeist"] = turnier != null && turnier.LaufMitFreundGeist,
                ["gespielt"] = turnier?.Gespielt.Count ?? 0,
                ["wert"] = turnier?.LastErgebnis != null ? Math.Round(turnier.LastErgebnis.Wert, 2) : 0.0,
                ["pferd"] = turnier?.PferdName ?? "",
                ["farbe"] = turnier?.Lauf?.Farbe ?? "",
                ["gait"] = turnier?.Lauf?.Controller?.Gait ?? "",
                ["ausdauerMax"] = Math.Round(turnier?.Lauf?.Controller?.AusdauerMaxWert ?? 0.0, 2),
                ["pferdXp"] = Math.Round(turnier?.LastPferdXp?.Gewinn ?? 0.0, 2),
                ["pferdLevel"] = Gooby.Core.Json.J.Int(turnier?.LastPferdXp?.Pferd?["level"]),
                ["levelUps"] = new JArray(turnier?.LastPferdXp?.LevelUps.ToArray() ?? new int[0]),
            };
            o["hof"] = (_host.Ui.Current as RanchHofScreen)?.QaState() ?? new JObject();
            o["dorf"] = (_host.Ui.Current as RanchDorfScreen)?.QaState() ?? new JObject();
            o["welt"] = (_host.Ui.Current as RanchWeltScreen)?.QaState() ?? new JObject();
            o["rbau"] = (_host.Ui.Current as RanchBauScreen)?.QaState() ?? new JObject();
            return o;
        }

        // ── ranch multiplayer: "rmp mode <mode>", "rmp bot on|off", free rides: "rmp herz|folge|geste <id>" ──

        private void Rmp(string arg, string rest)
        {
            var screen = _host.Ui.Current as RanchMpScreen;
            if (screen == null) { Debug.LogWarning("[qa] rmp: no Ranch-Treffen screen on top"); return; }
            switch (arg)
            {
                case "mode":
                    screen.SelectMode(rest.Trim());
                    Debug.Log("[qa] rmp mode " + screen.MenuMode);
                    break;
                case "bot":
                    screen.Link.Bot = rest.Trim() != "off";
                    Debug.Log("[qa] rmp bot " + (screen.Link.Bot ? "on" : "off"));
                    break;
                case "board":
                    if (rest.Trim() == "off") screen.CloseBoard();
                    else screen.OpenBoard(rest.Trim());
                    Debug.Log("[qa] rmp board " + (screen.BoardOpen ? screen.BoardKurs : "off"));
                    break;
                case "ghost":
                    screen.LoadGhost(rest.Trim().ToUpperInvariant());
                    Debug.Log("[qa] rmp ghost " + screen.BoardKurs + " " + rest.Trim());
                    break;
                case "herz":
                    screen.SendHerz();
                    Debug.Log("[qa] rmp herz");
                    break;
                case "folge":
                    screen.ToggleFolgeMir();
                    Debug.Log("[qa] rmp folge " + (screen.FolgeMir ? "an" : "aus"));
                    break;
                case "geste":
                    screen.SendGeste(rest.Trim());
                    Debug.Log("[qa] rmp geste " + rest.Trim());
                    break;
                default:
                    Debug.LogWarning("[qa] rmp: mode <rennen|parcours|fangen|ausritt|besuch> | bot on|off | board <kurs>|off | ghost <code> | herz | folge | geste streicheln|fuettern");
                    break;
            }
        }

        // ── build mode: "bau place|onpeer <item|floor> [x y] [rot]", "bau tap x y", "bau move [x y]", "bau rotate|store|undo" ──

        /// <summary>
        /// Drives the open build session through its own API — the same calls the build-mode taps make, so a visit tour
        /// exercises the host's BUILD_DELTA relay. "floor" picks the first floor piece in storage; without x y the piece goes
        /// to the first free spot clear of the visiting Gooby, "onpeer" puts it right over that Gooby (the guest must end up
        /// at the door). A placed piece is focused afterwards (like a player tapping it), "move" without x y finds it a new spot.
        /// </summary>
        private void Bau(string arg, string rest)
        {
            var s = _host.World?.Build;
            if (s == null) { Debug.LogWarning("[qa] bau: no build session"); return; }
            var p = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int Arg(int i, int fallback) => p.Length > i && int.TryParse(p[i], out var v) ? v : fallback;
            var peerCells = new System.Collections.Generic.HashSet<Gooby.Core.Home.Cell>();
            if (_host.World.Peer != null)
            {
                var pc = s.CellAt(_host.World.Peer.position);
                for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++) peerCells.Add(new Gooby.Core.Home.Cell(pc.X + dx, pc.Y + dy));
            }
            bool ClearOfPeer(Gooby.Core.Home.FurnitureDef d, Gooby.Core.Home.Cell at, int r) =>
                !Gooby.Core.Home.GridData.CellsFor(at, d.FootprintW, d.FootprintH, r).Any(peerCells.Contains);
            void Tap(Gooby.Core.Home.Cell cell) => s.TapAt(new Ray(s.CellCenter(cell, 1, 1, 0) + Vector3.up * 6f, Vector3.down));
            switch (arg)
            {
                case "place":
                case "onpeer":
                {
                    var item = p.Length > 0 ? p[0] : "floor";
                    if (item == "floor")
                        item = Gooby.Core.Home.HomeState.Storage(_host.Store).Select(r => Gooby.Core.Json.J.Str(r["item"]))
                            .FirstOrDefault(id => Gooby.Core.Home.FurnitureCatalog.Def(id)?.Layer == Gooby.Core.Home.GridLayer.Floor) ?? "";
                    var def = Gooby.Core.Home.FurnitureCatalog.Def(item);
                    if (def == null) { Debug.LogWarning("[qa] bau: nothing to place (" + item + ")"); return; }
                    s.Select(item);
                    var rot = (((arg == "place" ? Arg(3, 0) : Arg(1, 0)) % 4) + 4) % 4;
                    while (s.Rotation != rot) s.Rotate();
                    Gooby.Core.Home.Cell? at = null;
                    if (arg == "place" && p.Length >= 3) at = new Gooby.Core.Home.Cell(Arg(1, 0), Arg(2, 0));
                    else if (arg == "place")
                        at = s.Grid.FreeCells().Cast<Gooby.Core.Home.Cell?>()
                            .FirstOrDefault(c => s.Grid.CanPlace(def, c.Value, s.Rotation).Ok && ClearOfPeer(def, c.Value, s.Rotation));
                    else if (_host.World.Peer != null)
                    {
                        var peer = s.CellAt(_host.World.Peer.position);
                        var (fw, fh) = Gooby.Core.Home.GridData.RotatedFootprint(def.FootprintW, def.FootprintH, s.Rotation);
                        for (var dy = 0; dy < fh && at == null; dy++)
                            for (var dx = 0; dx < fw && at == null; dx++)
                            {
                                var c = new Gooby.Core.Home.Cell(peer.X - dx, peer.Y - dy);
                                if (s.Grid.CanPlace(def, c, s.Rotation).Ok) at = c;
                            }
                        Debug.Log("[qa] bau onpeer peerCell=" + peer.X + "," + peer.Y);
                    }
                    var placed = at != null && s.Place(at.Value);
                    s.Select(null);
                    if (placed) Tap(at.Value);
                    Debug.Log(placed ? "[qa] bau placed " + item + "@" + at.Value.X + "," + at.Value.Y + "/" + rot : "[qa] bau " + arg + " " + item + " refused: " + s.LastMessage);
                    break;
                }
                case "tap":
                {
                    var cell = new Gooby.Core.Home.Cell(Arg(0, 0), Arg(1, 0));
                    Tap(cell);
                    Debug.Log("[qa] bau tap " + cell.X + "," + cell.Y + " → " + s.LastMessage);
                    break;
                }
                case "move":
                {
                    var f = s.Focused;
                    if (f == null) { Debug.LogWarning("[qa] bau move: nothing focused"); return; }
                    Gooby.Core.Home.Cell? to = p.Length >= 2 ? new Gooby.Core.Home.Cell(Arg(0, 0), Arg(1, 0)) : (Gooby.Core.Home.Cell?)null;
                    if (to == null)
                        to = s.Grid.FreeCells().Cast<Gooby.Core.Home.Cell?>()
                            .FirstOrDefault(c => Math.Abs(c.Value.X - f.At.X) + Math.Abs(c.Value.Y - f.At.Y) >= 3
                                && s.Grid.CanPlace(f.Def, c.Value, f.Rot, f.Uid).Ok && ClearOfPeer(f.Def, c.Value, f.Rot));
                    var moved = to != null && s.MoveFocused(to.Value);
                    Debug.Log(moved ? "[qa] bau moved " + f.Def.Id + "@" + to.Value.X + "," + to.Value.Y : "[qa] bau move refused: " + s.LastMessage);
                    break;
                }
                case "rotate":
                    Debug.Log("[qa] bau rotate → " + (s.RotateFocused() ? s.LastMessage + " " + s.Focused?.Def.Id + "/" + s.Focused?.Rot : "refused " + s.LastMessage));
                    break;
                case "store":
                    Debug.Log("[qa] bau store → " + (s.StoreFocused() ? s.LastMessage : "refused " + s.LastMessage));
                    break;
                case "undo":
                    Debug.Log("[qa] bau undo → " + (s.Undo() ? s.LastMessage : "nothing"));
                    break;
                case "select":
                    s.Select(p.Length > 0 ? p[0] : null);
                    Debug.Log("[qa] bau select " + s.SelectedItem);
                    break;
                case "waende":
                {
                    // every wall slot (0.5 m, in WallProps' order): '.' free, '#' door or window, the first letter of what hangs there
                    var probe = Gooby.Core.Home.FurnitureCatalog.Def("pictureFrameWall");
                    foreach (var wall in new[] { "N", "W", "E" })
                    {
                        var row = new System.Text.StringBuilder();
                        for (var i = 0; i < s.Grid.WallWidth(wall); i++)
                        {
                            var uid = s.Grid.WallItemAt(wall, i);
                            row.Append(uid != "" ? (s.Grid.GetItem(uid)?.Def.Id ?? "?")[0] : probe != null && s.Grid.CanPlaceWall(probe, wall, i).Ok ? '.' : '#');
                        }
                        Debug.Log("[qa] bau wand " + wall + " " + row);
                    }
                    break;
                }
                case "wandtipp":
                {
                    // "bau wandtipp <N|W|E> <metres along> [height]": a tap from the camera onto that spot of the wall —
                    // places the selected wall piece there, focuses the picture hanging there, or moves the focused one
                    var cam = _host.World.Camera;
                    var wall = p.Length > 0 ? p[0] : "N";
                    var along = p.Length > 1 ? float.Parse(p[1], CultureInfo.InvariantCulture) : 1f;
                    var height = p.Length > 2 ? float.Parse(p[2], CultureInfo.InvariantCulture) : 1.6f;
                    var w = s.Grid.Width * (float)Gooby.Core.Home.GridData.CellSize;
                    var d = s.Grid.Height * (float)Gooby.Core.Home.GridData.CellSize;
                    var at = wall == "W" ? new Vector3(s.Origin.x, height, s.Origin.z + d - along)
                        : wall == "E" ? new Vector3(s.Origin.x + w, height, s.Origin.z + d - along)
                        : new Vector3(s.Origin.x + along, height, s.Origin.z + d);
                    s.TapAt(new Ray(cam.transform.position, at - cam.transform.position));
                    Debug.Log("[qa] bau wandtipp " + wall + " " + along + " → " + s.LastMessage + " focused=" + (s.Focused?.Def.Id ?? "-") + (s.Focused?.Wall != null ? "@" + s.Focused.Wall + "/" + s.Focused.At.X : ""));
                    break;
                }
                default:
                    Debug.LogWarning("[qa] bau: place <item|floor> [x y] [rot] | onpeer <item|floor> [rot] | tap x y | move [x y] | rotate | store | undo | select <item> | wandtipp <N|W|E> <m> [h]");
                    break;
            }
        }

        // ── Schiffe versenken: "bs next" taps the next untried enemy cell, "bs auto on" keeps doing it on our turn ──

        private bool _bsAuto;
        private float _bsCooldown;

        private void Battleship(string arg)
        {
            switch (arg)
            {
                case "auto": case "on": _bsAuto = true; break;
                case "off": _bsAuto = false; break;
                default: BattleshipNext(true); break;
            }
        }

        private void BattleshipAutoTick(float dt)
        {
            if (!_bsAuto) return;
            _bsCooldown -= dt;
            if (_bsCooldown > 0f) return;
            _bsCooldown = 0.08f;
            BattleshipNext(false);
        }

        // ── Schach: "ch move e2e4" taps from + to (+ promotion), "ch auto on" plays the AI's move on our turn ──

        private bool _chAuto;
        private float _chCooldown;
        private Gooby.Core.Minigames.ChessAi _chAi;

        private void Chess(string arg, string rest)
        {
            switch (arg)
            {
                case "move":
                    ChessMove(rest.Trim(), true);
                    break;
                case "auto":
                    _chAuto = rest.Trim() != "off";
                    Debug.Log("[qa] ch auto " + (_chAuto ? "on" : "off"));
                    break;
                case "practice":
                {
                    var p = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    var strength = p.Length > 0 && int.TryParse(p[0], out var s) ? s : ChessScreen.DefaultStrength;
                    var color = p.Length > 1 && p[1] == "black" ? Gooby.Core.Minigames.ChessLogic.Black : Gooby.Core.Minigames.ChessLogic.White;
                    _host.Ui.Push(ChessScreen.PracticeGame(_host, strength, color));
                    break;
                }
                default:
                    Debug.LogWarning("[qa] ch: move <uci> | auto on|off | practice [1-3] [white|black]");
                    break;
            }
        }

        /// <summary>Plays a UCI move through the UI: tap the from square, the to square, then the promotion piece.</summary>
        private bool ChessMove(string uci, bool verbose)
        {
            var screen = _host.Ui.Current as ChessScreen;
            if (screen == null || screen.Logic == null || !screen.MyTurnNow || uci.Length < 4)
            {
                if (verbose) Debug.Log("[qa] ch: not my turn / no chess screen");
                return false;
            }
            var from = uci.Substring(0, 2);
            var to = uci.Substring(2, 2);
            Click(ChessScreen.SquarePrefix + from);
            Click(ChessScreen.SquarePrefix + to);
            if (screen.PromoOpen) Click(ChessScreen.PromoPrefix + (uci.Length > 4 ? uci.Substring(4, 1) : "q"));
            return true;
        }

        private void ChessAutoTick(float dt)
        {
            if (!_chAuto) return;
            _chCooldown -= dt;
            if (_chCooldown > 0f) return;
            _chCooldown = 0.4f;
            var screen = _host.Ui.Current as ChessScreen;
            if (screen == null || screen.Logic == null || !screen.MyTurnNow) return;
            if (_chAi == null) _chAi = new Gooby.Core.Minigames.ChessAi(new Gooby.Core.Random.GoobyRng(2024));
            var uci = _chAi.PickUci(screen.Logic, ChessScreen.DefaultStrength);
            if (uci.Length > 0 && ChessMove(uci, false)) Debug.Log("[qa] ch auto " + uci);
        }

        /// <summary>Checkerboard first (every ship ≥ 2 cells long sits on a black square), then the rest — like a player.</summary>
        private void BattleshipNext(bool verbose)
        {
            var b = _host.Online.Board;
            if (!(_host.Ui.Current is BattleshipScreen) || !b.MyTurn || b.AwaitingResult || b.Tracker == null)
            {
                if (verbose) Debug.Log("[qa] bs: not my turn / no board screen");
                return;
            }
            const int n = Gooby.Core.Minigames.BattleshipLogic.Grid;
            for (var pass = 0; pass < 2; pass++)
                for (var y = 0; y < n; y++)
                    for (var x = 0; x < n; x++)
                    {
                        if ((x + y) % 2 != pass) continue;
                        if (b.Tracker.TryGetShot(new Gooby.Core.Minigames.BattleshipCell(x, y), out _)) continue;
                        Click("bs-cell-" + x + "-" + y);
                        return;
                    }
        }
    }
}
