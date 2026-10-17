using System.Collections.Generic;
using Gooby.Core.Meta;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// "Was nun?" (port of whats_next_hint.gd over <see cref="WhatsNextAdvisor"/>, the REST-2 red thread), spoken by Gooby
    /// instead of an extra card on an already busy home: when life at home has been calm for a while he suggests the one
    /// next step — a reward waiting, the almost-done daily quest, the fridge or a bath when a stat runs low, the ranch, the
    /// next level, a round in the arcade. A tap on his bubble does it. Each suggestion comes back at most every seven
    /// minutes and three times a day, so it guides without nagging.
    /// </summary>
    public sealed partial class HomeHudScreen
    {
        private const float CoachFirstS = 40f, CoachEveryS = 150f, CoachRepeatS = 420f;
        private const int CoachPerDay = 3;
        private float _coachIn = CoachFirstS;
        private string _coachId, _coachText, _coachDay = "";
        private readonly Dictionary<string, float> _coachShownAt = new Dictionary<string, float>();
        private readonly Dictionary<string, int> _coachToday = new Dictionary<string, int>();

        /// <summary>Counts down while home is calm; true when Gooby just voiced a suggestion (the life tick then rests).</summary>
        private bool TickCoach(float dt)
        {
            _coachIn -= dt;
            if (_coachIn > 0f || _host.Quests == null) return false;
            // the guided first quarter hour already says what is next
            if (_guide != null || OnboardingGuide.Active(_host.Store.State)) { _coachIn = CoachEveryS; return false; }
            _coachIn = CoachEveryS;
            var s = CoachSuggestion();
            if (s == null) return false;
            var day = _host.Store.Clock.LocalDay();
            if (day != _coachDay) { _coachDay = day; _coachToday.Clear(); }
            var now = Time.realtimeSinceStartup;
            if (_coachShownAt.TryGetValue(s.Id, out var at) && now - at < CoachRepeatS) return false;
            _coachToday.TryGetValue(s.Id, out var shown);
            if (shown >= CoachPerDay) return false;
            _coachShownAt[s.Id] = now;
            _coachToday[s.Id] = shown + 1;
            var text = CoachText(s);
            Say(text);
            _coachId = s.Id;
            _coachText = text;
            return true;
        }

        private WhatsNextAdvisor.Suggestion CoachSuggestion()
        {
            var rows = new List<WhatsNextAdvisor.QuestRow>();
            foreach (var r in _host.Quests.Board())
                rows.Add(new WhatsNextAdvisor.QuestRow(r.Def.Id, r.Def.TitleKey, r.Progress, r.Target, r.Complete, r.Claimed));
            return WhatsNextAdvisor.Suggest(_host.Store.State, rows, _host.Quests.ClaimableCount(), _host.Quests.AllClaimed());
        }

        private string CoachText(WhatsNextAdvisor.Suggestion s)
        {
            var t = _host.Strings;
            if (s.Args.TryGetValue("titel_key", out var key)) return t.T(s.TextKey, ("titel", t.T((string)key)));
            if (s.Args.TryGetValue("level", out var level)) return t.T(s.TextKey, ("level", level));
            return t.T(s.TextKey);
        }

        /// <summary>A tap on the bubble while it still shows the suggestion does the step; false when it shows something else.</summary>
        private bool RunCoach()
        {
            if (_coachId == null || _bubbleText == null || _bubbleText.text != _coachText) return false;
            var id = _coachId;
            _coachId = null;
            Debug.Log("[hud] coach " + id);
            if (id == "abholen" || id.StartsWith("quest_"))
            {
                _tasksOpen = true;
                ApplyTasksOpen();
            }
            else if (id.StartsWith("pflege_")) _host.Ui.Push(new CareSheetScreen(_host, Say));
            else if (id == "ranch") _host.Ui.Push(new RanchHofScreen(_host));
            else _host.Ui.Push(new ArcadeScreen(_host));
            return true;
        }
    }
}
