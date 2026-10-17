using System;
using Gooby.Core.Json;
using Gooby.Core.Save;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// "Spielstand übertragen" (docs/godot-rewrite/SAVE-TRANSFER.md ways A + B): shows a save found on the device
    /// (Godot 5 file or the old web app's preferences), lets the player paste raw JSON / a GOOBY5 code, previews
    /// level · coins · stickers · outfits and applies it after backing up the current save. The current state can
    /// be copied as a GOOBY5 code for the way back (Godot accepts the same code).
    /// </summary>
    public sealed class TransferScreen : UiScreen
    {
        private readonly GameHost _host;
        private readonly LegacySaves _legacy;
        private LegacySaves.Candidate _candidate;
        private SaveTransfer.Result _pending;
        private VisualElement _preview, _status;
        private Label _previewTitle, _previewSummary, _previewNotes, _statusText;
        private TextField _input;
        private Button _apply;

        public TransferScreen(GameHost host, LegacySaves.Candidate candidate = null)
        {
            _host = host;
            _legacy = host.Legacy;
            _candidate = candidate;
            Id = "transfer";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => _host.Ui.Pop());
            root.Add(scrim);

            var card = Ux.El("gb-card", "gb-panel-weiss", "gb-transfer");
            Ux.Abs(card, null, 40);
            card.style.left = Length.Percent(50);
            card.style.translate = new Translate(Length.Percent(-50), 0);
            card.style.width = 720;
            card.style.paddingLeft = 24; card.style.paddingRight = 24; card.style.paddingTop = 16; card.style.paddingBottom = 20;
            root.Add(card);

            var head = Ux.Row();
            head.Add(Ux.IconTile("books", new Color(1f, 0.93f, 0.7f)));
            var hcol = Ux.Col("gb-grow");
            hcol.style.marginLeft = 12;
            hcol.Add(Ux.Text(t.T("nl.transfer.titel"), "gb-h2"));
            hcol.Add(Ux.Text(t.T("nl.transfer.untertitel"), "gb-sm", "gb-mild"));
            head.Add(hcol);
            head.Add(Ux.IconBtn("close", () => _host.Ui.Pop(), "gb-btn-icon-sm"));
            card.Add(head);

            // way A: found on this device
            var found = Ux.Row();
            found.style.marginTop = 14;
            var search = Ux.IconTextBtn("search", t.T("nl.transfer.suchen"), () => { _candidate = _legacy.Find(_host.Store.Clock.NowMs()); ShowCandidate(); }, "gb-btn-secondary", "gb-btn-sm");
            found.Add(search);
            var export = Ux.IconTextBtn("copy", t.T("nl.transfer.exportieren"), CopyCode, "gb-btn-teal", "gb-btn-sm");
            export.style.marginLeft = 10;
            found.Add(export);
            card.Add(found);

            // way B: paste
            _input = new TextField { multiline = true };
            _input.AddToClassList("gb-textfield");
            _input.style.height = 96;
            _input.style.marginTop = 14;
            _input.textEdition.placeholder = t.T("nl.transfer.einfuegen_hint");
            card.Add(_input);
            var check = Ux.IconTextBtn("check_brown", t.T("nl.transfer.pruefen"), CheckPasted, "gb-btn-secondary", "gb-btn-sm");
            check.style.marginTop = 10;
            check.style.alignSelf = Align.FlexStart;
            card.Add(check);

            // preview card
            _preview = Ux.El("gb-card", "gb-card-gruen");
            _preview.style.marginTop = 14;
            _preview.style.display = DisplayStyle.None;
            _previewTitle = Ux.Text("", "gb-lg", "gb-bold");
            _preview.Add(_previewTitle);
            _previewSummary = Ux.Text("", "gb-md");
            _previewSummary.style.marginTop = 4;
            _preview.Add(_previewSummary);
            _previewNotes = Ux.Text("", "gb-sm", "gb-mild");
            _previewNotes.style.whiteSpace = WhiteSpace.Normal;
            _previewNotes.style.marginTop = 4;
            _preview.Add(_previewNotes);
            var prow = Ux.Row();
            prow.style.marginTop = 10;
            _apply = Ux.IconTextBtn("check", t.T("nl.transfer.uebernehmen"), Apply, "gb-btn-confirm", "gb-btn-sm");
            prow.Add(_apply);
            var hint = Ux.Text(t.T("nl.transfer.backup_hint"), "gb-sm", "gb-mild");
            hint.style.marginLeft = 12; hint.style.whiteSpace = WhiteSpace.Normal; hint.style.flexShrink = 1;
            prow.Add(hint);
            _preview.Add(prow);
            card.Add(_preview);

            // status / error line
            _status = Ux.El("gb-card", "gb-card-rosa");
            _status.style.marginTop = 12;
            _status.style.display = DisplayStyle.None;
            _statusText = Ux.Text("", "gb-md", "gb-bold");
            _statusText.style.whiteSpace = WhiteSpace.Normal;
            _status.Add(_statusText);
            card.Add(_status);

            if (_candidate != null) ShowCandidate();
        }

        private void ShowCandidate()
        {
            var t = _host.Strings;
            if (_candidate == null)
            {
                _preview.style.display = DisplayStyle.None;
                Status(t.T("nl.transfer.nichts_gefunden"), error: false);
                return;
            }
            _pending = _candidate.Preview;
            _previewTitle.text = t.T("nl.transfer.gefunden_titel") + " — " + t.T(_candidate.Source == "godot" ? "nl.transfer.quelle_godot" : "nl.transfer.quelle_web");
            FillPreview();
        }

        private void CheckPasted()
        {
            var t = _host.Strings;
            var res = _legacy.Preview(_input.value, _host.Store.Clock.NowMs());
            if (!res.Ok)
            {
                _preview.style.display = DisplayStyle.None;
                Status(t.T("nl.transfer.fehler", ("error", res.Error)), error: true);
                return;
            }
            _pending = res;
            _previewTitle.text = t.T("nl.transfer.vorschau");
            FillPreview();
        }

        private void FillPreview()
        {
            var t = _host.Strings;
            _status.style.display = DisplayStyle.None;
            _previewSummary.text = LegacySaves.Summary(_pending.Report, (key, args) => t.T(key, args));
            var notes = J.Path(_pending.State, "migration", "notes") as Newtonsoft.Json.Linq.JArray;
            var lost = J.Path(_pending.State, "migration", "lost") as Newtonsoft.Json.Linq.JArray;
            var n = (notes?.Count ?? 0) + (lost?.Count ?? 0);
            _previewNotes.text = n > 0 ? t.T("nl.transfer.hinweise", ("n", n)) : "";
            _preview.style.display = DisplayStyle.Flex;
        }

        private void Apply()
        {
            var t = _host.Strings;
            if (!_legacy.Apply(_pending, out var error))
            {
                Status(t.T("nl.transfer.fehler", ("error", error)), error: true);
                return;
            }
            _preview.style.display = DisplayStyle.None;
            Status(t.T("nl.transfer.erfolg"), error: false);
            _host.World?.Bounce();
            _host.LegacyCandidate = null;
            Root.schedule.Execute(() => _host.Ui.Show(new HomeHudScreen(_host))).StartingIn(1400);
        }

        private void CopyCode()
        {
            GUIUtility.systemCopyBuffer = _legacy.ExportCode();
            Status(_host.Strings.T("nl.transfer.kopiert"), error: false);
        }

        private void Status(string text, bool error)
        {
            _status.EnableInClassList("gb-card-rosa", error);
            _status.EnableInClassList("gb-card-gruen", !error);
            _statusText.text = text;
            _status.style.display = DisplayStyle.Flex;
        }
    }
}
