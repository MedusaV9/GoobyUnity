using System.Collections.Generic;
using System.IO;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The photo gallery (port of ui/galerie): the camera app's pictures as a grid of polaroid cards, newest first; a tap
    /// opens the photo big with "Löschen" and "Zurück". Empty: Gooby with a hint to take the first photo. Textures are
    /// loaded when the sheet opens and freed when it closes.
    /// </summary>
    public sealed class GalleryScreen : UiScreen
    {
        private readonly GameHost _host;
        private readonly List<Texture2D> _textures = new List<Texture2D>();
        private VisualElement _grid, _viewer;
        /// <summary>The newest photos shown at once (each one is a full texture in memory).</summary>
        public const int MaxShown = 24;

        public GalleryScreen(GameHost host)
        {
            _host = host;
            Id = "gallery";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-sheets");
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => _host.Ui.Pop());
            root.Add(scrim);
            var sheet = Ux.El("gb-sheet", "gb-win-sheet");
            sheet.style.left = 140; sheet.style.right = 140;
            sheet.style.maxHeight = Length.Percent(90);
            var head = CounterPanel.Head("camera", new Color(0.86f, 0.92f, 1f), t.T("nl.galerie.titel"), t.T("nl.galerie.untertitel"), () => _host.Ui.Pop());
            sheet.Add(head.Row);
            var body = Ux.Col("gb-win-body");
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-win-scroll");
            _grid = Ux.El("gb-gallery-grid");
            scroll.Add(_grid);
            body.Add(scroll);
            sheet.Add(body);
            root.Add(sheet);
            _viewer = Ux.El("gb-gallery-viewer");
            _viewer.style.display = DisplayStyle.None;
            root.Add(_viewer);
            Fill();
        }

        private void Fill()
        {
            var t = _host.Strings;
            _grid.Clear();
            FreeTextures();
            var photos = PhotoAlbum.List(_host);
            if (photos.Count == 0)
            {
                var empty = Ux.Col("gb-gallery-empty");
                var img = Ux.El("gb-gallery-empty-gooby");
                var tex = Resources.Load<Texture2D>("gooby-ui/Images/gooby_sitting");
                if (tex != null) img.style.backgroundImage = new StyleBackground(tex);
                empty.Add(img);
                empty.Add(Ux.Text(t.T("nl.galerie.leer"), "gb-lg", "gb-bold"));
                empty.Add(Ux.Text(t.T("nl.galerie.leer_tipp"), "gb-md", "gb-mild"));
                _grid.Add(empty);
                return;
            }
            var i = 0;
            foreach (var path in photos)
            {
                if (i >= MaxShown) break;
                var tex = PhotoAlbum.Load(path);
                if (tex == null) continue;
                _textures.Add(tex);
                var p = path;
                var card = new Button(() => Open(p, tex)) { text = "", name = "gallery-photo-" + i };
                card.ClearClassList();
                card.AddToClassList("gb-gallery-card");
                card.AddToClassList("gb-arcade-card");
                card.style.rotate = new Rotate((i % 3 - 1) * 1.6f);
                var pic = Ux.El("gb-gallery-pic");
                pic.style.backgroundImage = new StyleBackground(tex);
                card.Add(pic);
                card.Add(Ux.Text(When(path), "gb-gallery-date"));
                _grid.Add(card);
                i++;
            }
        }

        private static string When(string path)
        {
            var n = Path.GetFileNameWithoutExtension(path);
            // yyyyMMdd_HHmmss_fff (UTC) → "dd.MM. HH:mm" local
            if (n.Length < 15 || !System.DateTime.TryParseExact(n.Substring(0, 15), "yyyyMMdd_HHmmss", null, System.Globalization.DateTimeStyles.AssumeUniversal, out var d)) return "";
            return d.ToLocalTime().ToString("dd.MM. HH:mm");
        }

        private void Open(string path, Texture2D tex)
        {
            var t = _host.Strings;
            Sfx.PlayUi("soft_open");
            _viewer.Clear();
            _viewer.style.display = DisplayStyle.Flex;
            var shade = Ux.El("gb-gallery-shade");
            shade.RegisterCallback<ClickEvent>(_ => CloseViewer());
            _viewer.Add(shade);
            var frame = Ux.El("gb-gallery-big");
            var pic = Ux.El("gb-gallery-big-pic");
            pic.style.backgroundImage = new StyleBackground(tex);
            frame.Add(pic);
            var row = Ux.Row("gb-gallery-actions");
            var del = Ux.IconTextBtn("trash", t.T("nl.galerie.loeschen"), () =>
            {
                PhotoAlbum.Delete(path);
                Sfx.PlayUi("soft_back");
                CloseViewer();
                Fill();
            }, "gb-btn-secondary");
            del.name = "gallery-delete";
            row.Add(del);
            var back = Ux.IconTextBtn("check", t.T("nl.allgemein.schliessen"), CloseViewer, "gb-btn-primary");
            back.style.marginLeft = 12;
            row.Add(back);
            frame.Add(row);
            _viewer.Add(frame);
        }

        private void CloseViewer()
        {
            _viewer.style.display = DisplayStyle.None;
            _viewer.Clear();
        }

        private void FreeTextures()
        {
            foreach (var tex in _textures) if (tex != null) Object.Destroy(tex);
            _textures.Clear();
        }

        public override void OnHidden()
        {
            if (!_host.Ui.IsMounted(this)) FreeTextures();
        }
    }
}
