using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The camera app (port of foto_modus.gd / foto_rahmen.gd / foto_werkzeuge.gd): the HUD steps aside, a viewfinder with
    /// soft corner brackets frames the room, Gooby strikes a pose on request (wave, cheer, dance, sit), a frame style is
    /// chosen (none, Polaroid, hearts), and the big shutter takes the picture — white flash, click, the photo flies into a
    /// little card at the corner that opens the gallery. The picture is the world camera alone (no UI on it).
    /// </summary>
    public sealed class PhotoModeScreen : UiScreen
    {
        private readonly GameHost _host;
        private VisualElement _flash, _shotCard, _shotThumb;
        private Texture2D _shotTex;
        private string _frame = "polaroid";
        private float _flashT, _cardT;
        private int _taken;

        public PhotoModeScreen(GameHost host)
        {
            _host = host;
            Id = "photo-mode";
            Opaque = true;
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-photo");
            // the viewfinder: four corner brackets and a faint rule-of-thirds grid
            var finder = Ux.El("gb-photo-finder");
            finder.pickingMode = PickingMode.Ignore;
            foreach (var c in new[] { "tl", "tr", "bl", "br" }) finder.Add(Ux.El("gb-photo-corner", "gb-photo-corner--" + c));
            for (var i = 1; i <= 2; i++)
            {
                var v = Ux.El("gb-photo-third"); v.style.left = Length.Percent(i * 33.33f); v.style.top = 0; v.style.bottom = 0; v.style.width = 1;
                var h = Ux.El("gb-photo-third"); h.style.top = Length.Percent(i * 33.33f); h.style.left = 0; h.style.right = 0; h.style.height = 1;
                finder.Add(v); finder.Add(h);
            }
            root.Add(finder);
            // top: close + title pill
            var close = Ux.IconBtn("close", Close, "gb-mgw-close");
            close.name = "photo-close";
            Ux.Abs(close, 24, 20);
            root.Add(close);
            var title = Ux.Row("gb-photo-title");
            title.Add(Ux.Icon("camera"));
            title.Add(Ux.Text(t.T("nl.foto.titel"), "gb-lg", "gb-bold"));
            title.pickingMode = PickingMode.Ignore;
            root.Add(title);
            // left: frame styles
            var frames = Ux.Col("gb-photo-side", "gb-photo-side--left");
            frames.Add(Ux.Text(t.T("nl.foto.rahmen"), "gb-photo-kicker"));
            foreach (var (id, key) in new[] { ("ohne", "nl.foto.rahmen_ohne"), ("polaroid", "nl.foto.rahmen_polaroid"), ("herzen", "nl.foto.rahmen_herzen") })
            {
                var local = id;
                var chip = new Button(() => { _frame = local; Sfx.PlayUi("soft_chip"); RefreshChips(frames); }) { text = t.T(key), name = "photo-frame-" + id };
                chip.ClearClassList();
                chip.AddToClassList("gb-chip");
                chip.AddToClassList("gb-photo-chip");
                chip.userData = id;
                frames.Add(chip);
            }
            RefreshChips(frames);
            root.Add(frames);
            // right: Gooby's poses
            var poses = Ux.Col("gb-photo-side", "gb-photo-side--right");
            poses.Add(Ux.Text(t.T("nl.foto.pose"), "gb-photo-kicker"));
            foreach (var (clip, key, emotion) in new[] { ("wave", "nl.foto.pose_winken", "happy"), ("celebrate", "nl.foto.pose_jubeln", "ecstatic"), ("dance-loop", "nl.foto.pose_tanzen", "happy"), ("sit-loop", "nl.foto.pose_sitzen", "happy") })
            {
                var c = clip; var e = emotion;
                var b = new Button(() => Pose(c, e)) { text = t.T(key), name = "photo-pose-" + clip };
                b.ClearClassList();
                b.AddToClassList("gb-chip");
                b.AddToClassList("gb-photo-chip");
                poses.Add(b);
            }
            root.Add(poses);
            // the shutter
            var shutter = new Button(Shoot) { text = "", name = "photo-shutter" };
            shutter.ClearClassList();
            shutter.AddToClassList("gb-photo-shutter");
            shutter.AddToClassList("gb-arcade-card");
            shutter.Add(Ux.El("gb-photo-shutter-core"));
            root.Add(shutter);
            // the last photo flies into this card (tap: gallery)
            _shotCard = Ux.El("gb-photo-shot");
            _shotCard.name = "photo-last";
            _shotThumb = Ux.El("gb-photo-shot-img");
            _shotCard.Add(_shotThumb);
            _shotCard.Add(Ux.Text(t.T("nl.foto.gespeichert"), "gb-photo-shot-text"));
            _shotCard.style.display = DisplayStyle.None;
            _shotCard.RegisterCallback<ClickEvent>(_ => { Sfx.PlayUi("soft_open"); _host.Ui.Push(new GalleryScreen(_host)); });
            root.Add(_shotCard);
            _flash = Ux.El("gb-photo-flash");
            _flash.pickingMode = PickingMode.Ignore;
            root.Add(_flash);
            Debug.Log("[photo] mode open");
        }

        private void RefreshChips(VisualElement frames)
        {
            foreach (var c in frames.Children())
                if (c is Button b && b.userData is string id) b.EnableInClassList("gb-chip--active", id == _frame);
        }

        private void Pose(string clip, string emotion)
        {
            var g = _host.World?.Avatar;
            Sfx.PlayUi("soft_tap");
            if (g == null) return;
            g.Play(clip);
            g.SetEmotion(emotion);
            _host.Audio?.Say(clip == "wave" ? "Hallo!" : clip == "celebrate" ? "Juhu!" : "Lalala", emotion);
        }

        private void Shoot()
        {
            if (_flashT > 0f) return;
            var cam = _host.World?.Camera;
            var path = PhotoAlbum.Take(_host, cam, tex => PhotoAlbum.Frame(tex, _frame));
            _taken++;
            Sfx.Play("game_pop", 8f);
            Sfx.PlayUi("soft_collect");
            Haptics.Tap();
            _flashT = 0.35f;
            _flash.style.opacity = 1f;
            if (_shotTex != null) Object.Destroy(_shotTex);
            _shotTex = PhotoAlbum.Load(path);
            if (_shotTex != null) _shotThumb.style.backgroundImage = new StyleBackground(_shotTex);
            _shotCard.style.display = DisplayStyle.Flex;
            _shotCard.RemoveFromClassList("gb-photo-shot--in");
            _cardT = 0.05f;
            _host.Store.Update(s =>
            {
                var meta = Gooby.Core.Json.J.EnsureDict(s, "meta");
                meta["photosTaken"] = Gooby.Core.Json.J.Int(meta["photosTaken"]) + 1;
            });
        }

        public override void Update()
        {
            var dt = Time.unscaledDeltaTime;
            if (_flashT > 0f)
            {
                _flashT = Mathf.Max(0f, _flashT - dt);
                _flash.style.opacity = _flashT / 0.35f;
            }
            if (_cardT > 0f && (_cardT -= dt) <= 0f) _shotCard.AddToClassList("gb-photo-shot--in");
        }

        public override void OnHidden()
        {
            if (_host.Ui.IsMounted(this)) return;
            if (_shotTex != null) Object.Destroy(_shotTex);
            _shotTex = null;
            var g = _host.World?.Avatar;
            if (g != null) { g.Play("idle-loop"); g.SetEmotion("happy"); }
        }

        private void Close()
        {
            Sfx.PlayUi("soft_close");
            if (_host.Ui.Current == this) _host.Ui.Pop();
            if (_taken > 0) _host.Ui.Toast(_host.Strings.T(_taken == 1 ? "nl.foto.toast_eins" : "nl.foto.toast", ("n", _taken)), "camera");
        }
    }
}
