using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI
{
    /// <summary>
    /// A photo as a slightly tilted polaroid (white frame, wide bottom edge with a caption) for photo letters and
    /// InstantGooby posts (gooby-instant.uss). The picture loads lazily; while it loads, offline or when it is gone the
    /// frame shows a camera and a short note instead, so a letter never breaks over a missing photo.
    /// </summary>
    public static class Polaroid
    {
        public sealed class View
        {
            public VisualElement Root;
            public VisualElement Photo;
            public VisualElement Placeholder;
            public Label Status;
            public Label Caption;
        }

        /// <param name="width">Photo width in px (height = ¾ of it, the 320×240 capture aspect).</param>
        /// <param name="tilt">Rotation in degrees (polaroids on a table are never straight).</param>
        public static View Create(float width, string caption, float tilt = 0f, params string[] classes)
        {
            var v = new View { Root = Ux.El("gb-polaroid") };
            foreach (var c in classes) v.Root.AddToClassList(c);
            if (Mathf.Abs(tilt) > 0.01f) v.Root.style.rotate = new Rotate(new Angle(tilt, AngleUnit.Degree));
            v.Photo = Ux.El("gb-polaroid-photo");
            v.Photo.style.width = width;
            v.Photo.style.height = Mathf.Round(width * 0.75f);
            v.Placeholder = Ux.Col("gb-polaroid-empty");
            v.Placeholder.pickingMode = PickingMode.Ignore;
            v.Placeholder.Add(Ux.Icon("camera", "gb-polaroid-empty-icon"));
            v.Status = Ux.Text("", "gb-polaroid-status");
            v.Status.pickingMode = PickingMode.Ignore;
            v.Placeholder.Add(v.Status);
            v.Photo.Add(v.Placeholder);
            v.Root.Add(v.Photo);
            v.Caption = Ux.Text(caption ?? "", "gb-polaroid-caption");
            v.Caption.style.maxWidth = width;
            v.Caption.style.display = string.IsNullOrEmpty(caption) ? DisplayStyle.None : DisplayStyle.Flex;
            v.Root.Add(v.Caption);
            return v;
        }

        /// <summary>Shows a texture (null → the camera placeholder with <paramref name="note"/>).</summary>
        public static void Show(View v, Texture2D tex, string note = "")
        {
            if (v == null) return;
            if (tex != null)
            {
                v.Photo.style.backgroundImage = new StyleBackground(tex);
                v.Placeholder.style.display = DisplayStyle.None;
                v.Root.AddToClassList("gb-polaroid--loaded");
            }
            else
            {
                v.Photo.style.backgroundImage = StyleKeyword.None;
                v.Placeholder.style.display = DisplayStyle.Flex;
                v.Status.text = note ?? "";
                v.Root.RemoveFromClassList("gb-polaroid--loaded");
            }
        }

        /// <summary>Fetches a server photo into the frame (letter: <paramref name="instant"/> false; feed post: true).</summary>
        public static void Load(GameHost host, View v, bool instant, string photoId)
        {
            var t = host.Strings;
            var online = host.Online;
            var cached = online.Photos.Get(photoId);
            if (cached != null) { Show(v, cached); return; }
            if (!online.Client.IsOnline && online.Client.CachedPhoto(photoId) == null)
            {
                Show(v, null, t.T("online.foto.offline"));
                return;
            }
            Show(v, null, t.T("online.foto.laedt"));
            online.LoadPhoto(instant, photoId, (tex, code) => Show(v, tex, tex != null ? "" : t.T("online.foto.fehlt")));
        }
    }
}
