using Gooby.Core.Json;
using Gooby.Core.Meta;
using Gooby.Unity.App;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The guided first quarter hour on the home screen (port of onboarding_guide.gd over <see cref="OnboardingGuide"/>):
    /// a soft card at the top centre — step n/9, title, Gooby's line — that explains the basics by having them done: pet
    /// him, feed him, wash him, earn the first coins, play a round, buy something, get a sticker. Explaining steps carry
    /// "Los geht's!"/"Alles klar!", doing steps "Überspringen"; the × ends the tour for good. A done step turns the card
    /// into "Geschafft!" with confetti and Gooby's voice, then the next step slides in. Progress is saved, so the tour
    /// resumes after a restart; Gooby's own "Was nun?" tips wait until it is over.
    /// </summary>
    public sealed partial class HomeHudScreen
    {
        private const float GuideCelebrateS = 1.6f, GuidePollS = 1f;
        private VisualElement _guide;
        private Label _guideStep, _guideTitle, _guideText;
        private Button _guideNext, _guideSkip;
        private bool _guideDecided, _guideCelebrating;
        private float _guidePoll, _guideCelebrateLeft, _guideDoneLeft;

        private JObject GuideState => _host.Store.State;

        private (string, object) Nickname => ("nickname", J.Str(J.Path(_host.Store.State, "meta", "goobyNickname"), "Gooby"));

        private float _whatsNewIn = -1f;

        /// <summary>Once after an update: the highlights sheet as soon as the home is free (after the daily package).</summary>
        private void TickWhatsNew(float dt)
        {
            if (_whatsNewIn < 0f) return;
            if (_host.Ui.Current != this || _host.Ui.Fading || _host.Ui.Celebrating) return;
            _whatsNewIn -= dt;
            if (_whatsNewIn > 0f) return;
            _whatsNewIn = -1f;
            if (WhatsNewScreen.Due(_host)) _host.Ui.Push(new WhatsNewScreen(_host));
        }

        /// <summary>Decides once the player has moved in (start, resume or quietly done), then watches the step's counters.</summary>
        private void TickGuide(float dt)
        {
            TickWhatsNew(dt);
            if (_guideDoneLeft > 0f)
            {
                _guideDoneLeft -= dt;
                if (_guideDoneLeft <= 0f) RemoveGuide();
                return;
            }
            if (!_guideDecided)
            {
                if (!Onboarded || _host.Ui.Current != this || _host.Ui.Fading) return;
                _guideDecided = true;
                var decision = OnboardingGuide.Decide(GuideState);
                Debug.Log("[guide] " + decision);
                // "Neu in GOOBY" is for players who already live with Gooby; a guided newcomer has seen it all fresh
                if (decision == OnboardingGuide.DecideStart || decision == OnboardingGuide.DecideResume) _host.Settings.SetWhatsNewSeen(WhatsNewScreen.Version);
                else if (WhatsNewScreen.Due(_host) && WhatsNewScreen.MayOfferByItself()) _whatsNewIn = 3f;
                if (decision == OnboardingGuide.DecideQuiet)
                {
                    _host.Store.Update(s => OnboardingGuide.MarkDoneQuietly(s), GameHost.TicksMs);
                    _host.Store.NotifySliceChanged("onboarding");
                }
                else if (decision == OnboardingGuide.DecideStart || decision == OnboardingGuide.DecideResume)
                {
                    _host.Store.Update(s => OnboardingGuide.Begin(s), GameHost.TicksMs);
                    BuildGuide();
                    ShowGuideStep(decision == OnboardingGuide.DecideStart);
                }
                return;
            }
            if (_guide == null) return;
            if (_guideCelebrating)
            {
                _guideCelebrateLeft -= dt;
                if (_guideCelebrateLeft <= 0f) AdvanceGuide();
                return;
            }
            _guidePoll -= dt;
            if (_guidePoll > 0f) return;
            _guidePoll = GuidePollS;
            var step = OnboardingGuide.Current(GuideState);
            if (step == null) { RemoveGuide(); return; }
            if (step.Manual) return;
            if (OnboardingGuide.Satisfied(step.Id, J.Path(GuideState, "onboarding", "guide", "base") as JObject, GuideState)) CelebrateGuide(step.Id);
        }

        private void BuildGuide()
        {
            var t = _host.Strings;
            // one compact row under the toast lane (the toasts own the top centre): Gooby, then step pill + title + ×,
            // his line and the one action — short enough to stay above his head in the home camera
            _guide = Ux.Row("gb-guide");
            _guide.name = "guide-card";
            _guide.Add(Ux.Portrait(50));
            var texts = Ux.Col("gb-guide-texts");
            var head = Ux.Row("gb-guide-head");
            _guideStep = Ux.Text("", "gb-guide-step");
            head.Add(_guideStep);
            _guideTitle = Ux.Text("", "gb-guide-title");
            head.Add(_guideTitle);
            var close = Ux.IconBtn("close", EndGuide, "gb-guide-close");
            close.name = "guide-close";
            close.tooltip = t.T("onboarding.guide.beenden");
            head.Add(close);
            texts.Add(head);
            _guideText = Ux.Text("", "gb-guide-text");
            texts.Add(_guideText);
            var actions = Ux.Row("gb-guide-actions");
            _guideSkip = Ux.Btn(t.T("nl.guide.ueberspringen"), SkipGuideStep, "gb-btn-secondary", "gb-btn-sm");
            _guideSkip.name = "guide-skip";
            actions.Add(_guideSkip);
            _guideNext = Ux.Btn("", NextGuideStep, "gb-btn-primary", "gb-btn-sm");
            _guideNext.name = "guide-next";
            actions.Add(_guideNext);
            texts.Add(actions);
            _guide.Add(texts);
            Root.Add(_guide);
        }

        private void ShowGuideStep(bool arrival)
        {
            var step = OnboardingGuide.Current(GuideState);
            if (step == null || _guide == null) { RemoveGuide(); return; }
            var t = _host.Strings;
            _guideCelebrating = false;
            _guide.RemoveFromClassList("gb-guide--done");
            _guideStep.text = t.T("onboarding.guide.schritt", ("n", OnboardingGuide.StepIndex(GuideState) + 1), ("gesamt", OnboardingGuide.Steps.Count));
            _guideTitle.text = t.T("onboarding.guide.s." + step.Id + ".titel", Nickname);
            _guideText.text = t.T("onboarding.guide.s." + step.Id + ".text", Nickname);
            _guideNext.text = t.T(step.Id == "ankunft" ? "onboarding.guide.los" : "onboarding.guide.alles_klar");
            _guideNext.style.display = step.Manual ? DisplayStyle.Flex : DisplayStyle.None;
            _guideSkip.style.display = step.Manual ? DisplayStyle.None : DisplayStyle.Flex;
            _guide.AddToClassList("gb-guide--pop");
            _guide.schedule.Execute(() => _guide?.RemoveFromClassList("gb-guide--pop")).StartingIn(220);
            _guidePoll = 0.4f;
            _host.Audio?.Say(_guideText.text, "happy");
            if (arrival)
            {
                Sfx.PlayUi("soft_open");
                _host.Ui.Confetti(36);
            }
            Debug.Log("[guide] step " + step.Id);
        }

        private void CelebrateGuide(string stepId)
        {
            var t = _host.Strings;
            _guideCelebrating = true;
            _guideCelebrateLeft = GuideCelebrateS;
            _guide.AddToClassList("gb-guide--done");
            _guideTitle.text = t.T("onboarding.guide.geschafft");
            _guideText.text = t.T("onboarding.guide.s." + stepId + ".feier", Nickname);
            _guideNext.style.display = DisplayStyle.None;
            _guideSkip.style.display = DisplayStyle.None;
            Sfx.PlayUi("soft_collect");
            _host.Ui.Confetti(28);
            _host.Audio?.Say(_guideText.text, "ecstatic");
            Debug.Log("[guide] done " + stepId);
        }

        private void AdvanceGuide()
        {
            OnboardingGuide.Step next = null;
            _host.Store.Update(s => next = OnboardingGuide.Advance(s), GameHost.TicksMs);
            _host.Store.NotifySliceChanged("onboarding");
            if (next == null)
            {
                // the whole tour: a big confetti and the card goes
                _guideCelebrating = false;
                Sfx.PlayUi("soft_win");
                _host.Ui.Confetti(60);
                if (_guide != null) _guide.style.display = DisplayStyle.None;
                _guideDoneLeft = 1.5f;
                Debug.Log("[guide] finished");
                return;
            }
            ShowGuideStep(false);
        }

        private void NextGuideStep()
        {
            if (_guideCelebrating) return;
            Sfx.PlayUi("soft_confirm");
            AdvanceGuide();
        }

        private void SkipGuideStep()
        {
            if (_guideCelebrating) return;
            Sfx.PlayUi("soft_back");
            AdvanceGuide();
        }

        /// <summary>The ×: the tour ends for good (skipped and done).</summary>
        private void EndGuide()
        {
            Sfx.PlayUi("soft_back");
            _host.Store.Update(s => OnboardingGuide.Skip(s), GameHost.TicksMs);
            _host.Store.NotifySliceChanged("onboarding");
            Debug.Log("[guide] ended");
            RemoveGuide();
        }

        private void RemoveGuide()
        {
            _guide?.RemoveFromHierarchy();
            _guide = null;
            _guideCelebrating = false;
            _guideDoneLeft = 0f;
        }

        /// <summary>QA: "guide start" puts a fresh tour on (moved in, step 0), "guide end" closes it.</summary>
        public void QaGuide(string arg)
        {
            if (arg == "end") { EndGuide(); return; }
            _host.Store.Update(s =>
            {
                J.EnsureDict(s, "onboarding")["done"] = true;
                ((JObject)s["onboarding"]).Remove("guide");
            }, GameHost.TicksMs);
            RemoveGuide();
            _guideDecided = false;
        }
    }
}
