using System.Collections.Generic;
using System.IO;
using Gooby.Core.Meta;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The passport photo picker (passport_card.gd PassFotoPicker): the gallery's photos as polaroids, a tap glues one
    /// into the GOOBY-PASS (profile.passfoto); "Zurück zum 3D-Porträt" while a photo is in, "Doch nicht" to leave.
    /// Without photos it points to the camera. Textures live while the sheet is open.
    /// </summary>
    public sealed class PassPhotoPickerScreen : UiScreen
    {
        private readonly GameHost _host;
        private readonly List<Texture2D> _textures = new List<Texture2D>();

        public PassPhotoPickerScreen(GameHost host)
        {
            _host = host;
            Id = "passfoto";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-sheets");
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => _host.Ui.Pop());
            root.Add(scrim);
            var sheet = Ux.El("gb-sheet", "gb-win-sheet");
            sheet.name = "passfoto-picker";
            sheet.style.left = 180; sheet.style.right = 180;
            sheet.style.maxHeight = Length.Percent(88);
            sheet.Add(CounterPanel.Head("camera", new Color(0.86f, 0.92f, 1f), t.T("reisepass.picker_titel"), t.T("reisepass.foto_aendern"), () => _host.Ui.Pop()).Row);
            var body = Ux.Col("gb-win-body");
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-win-scroll");
            var grid = Ux.El("gb-gallery-grid");
            scroll.Add(grid);
            body.Add(scroll);
            var current = Passport.PhotoOf(_host.Store.State);
            var photos = PhotoAlbum.List(_host);
            if (photos.Count == 0) grid.Add(CounterPanel.Empty(t.T("reisepass.picker_titel"), t.T("reisepass.picker_leer"), true));
            for (var i = 0; i < photos.Count && i < GalleryScreen.MaxShown; i++)
            {
                var path = photos[i];
                var tex = PhotoAlbum.Load(path);
                if (tex == null) continue;
                _textures.Add(tex);
                var file = Path.GetFileName(path);
                var card = new Button(() => Pick(file)) { text = "", name = "passfoto-wahl-" + i };
                card.ClearClassList();
                card.AddToClassList("gb-gallery-card");
                card.AddToClassList("gb-arcade-card");
                card.style.rotate = new Rotate((i % 3 - 1) * 1.6f);
                if (file == current)
                {
                    card.style.borderTopColor = card.style.borderBottomColor = card.style.borderLeftColor = card.style.borderRightColor = new Color(0.5f, 0.81f, 0.61f);
                    card.style.borderTopWidth = card.style.borderBottomWidth = card.style.borderLeftWidth = card.style.borderRightWidth = 4;
                }
                var pic = Ux.El("gb-gallery-pic");
                pic.style.backgroundImage = new StyleBackground(tex);
                card.Add(pic);
                grid.Add(card);
            }
            var actions = Ux.Row("gb-win-actions", "gb-win-actions--end");
            if (current.Length > 0)
            {
                var standard = Ux.IconTextBtn("user", t.T("reisepass.picker_standard"), () => Pick(""), "gb-btn-secondary");
                standard.name = "passfoto-standard";
                actions.Add(standard);
            }
            var cancel = Ux.Btn(t.T("reisepass.picker_abbrechen"), () => _host.Ui.Pop(), "gb-btn-primary");
            cancel.name = "passfoto-abbrechen";
            actions.Add(cancel);
            body.Add(actions);
            sheet.Add(body);
            root.Add(sheet);
        }

        private void Pick(string file)
        {
            _host.Store.Update(s => Passport.SetPhoto(s, file), GameHost.TicksMs);
            _host.Store.NotifySliceChanged("profile");
            Sfx.PlayUi(file.Length > 0 ? "soft_confirm" : "soft_back");
            _host.Ui.Toast(_host.Strings.T(file.Length > 0 ? "reisepass.foto_gesetzt" : "reisepass.foto_standard_gesetzt"), "camera");
            Debug.Log("[pass] photo → " + (file.Length > 0 ? file : "portrait"));
            _host.Ui.Pop();
        }

        public override void OnHidden()
        {
            if (_host.Ui.IsMounted(this)) return;
            foreach (var tex in _textures) if (tex != null) Object.Destroy(tex);
            _textures.Clear();
        }
    }
}
