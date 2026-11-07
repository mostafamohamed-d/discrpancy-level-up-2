using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>The kinds of thing decided by hand on a project that can be forgotten.</summary>
    public enum ForgetWhat
    {
        Acceptances,
        Reviews,
        Notes,
        HandPairings,
        LoadingPlans,
        HeightZones
    }

    /// <summary>One line of the forget dialog: a kind of decision, how many there are, and a tick.</summary>
    public sealed class ForgetOption : ViewModelBase
    {
        private readonly Action onChanged;
        private bool isSelected;

        public ForgetOption(ForgetWhat what, string label, int count, string detail, Action onChanged)
        {
            What = what;
            Label = label;
            Count = count;
            Detail = detail;
            this.onChanged = onChanged;
        }

        public ForgetWhat What { get; }
        public string Label { get; }
        public int Count { get; }

        /// <summary>What forgetting it does, in a line under the tick.</summary>
        public string Detail { get; }

        /// <summary>Nothing to forget, so nothing to tick: the line is there, greyed, saying none.</summary>
        public bool HasAny => Count > 0;

        public string CountText => Count == 0 ? "none" : Count.ToString();

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (SetProperty(ref isSelected, value && HasAny))
                {
                    onChanged?.Invoke();
                }
            }
        }
    }

    /// <summary>
    /// What the forget button asks: which of the things decided by hand on this project to
    /// forget - the acceptances, the reviews, the notes, the pairings set by hand, the loading
    /// plans the levels were paired with - or all of them.
    ///
    /// It asks rather than forgetting the lot because they are decisions of very different
    /// weight. Starting the acceptances again after the model has been reissued is routine;
    /// losing forty levels' worth of loading plan pairings along with them is an afternoon's
    /// work gone. Nothing starts ticked, so whatever is forgotten was chosen.
    /// </summary>
    public sealed class ForgetChoices : ViewModelBase
    {
        private bool settling;

        public ForgetChoices(int acceptances, int reviews, int notes, int handPairings, int loadingPlans,
                             int heightZones = 0)
        {
            Options = new List<ForgetOption>
            {
                new ForgetOption(ForgetWhat.Acceptances, "Acceptances", acceptances,
                    "Elements accepted as a real difference. They count as open findings again.",
                    OnOptionChanged),
                new ForgetOption(ForgetWhat.Reviews, "Marked reviewed", reviews,
                    "Elements marked as looked at. Their findings stay as they are.",
                    OnOptionChanged),
                new ForgetOption(ForgetWhat.Notes, "Notes", notes,
                    "What was written on elements. Kept unless this is ticked.",
                    OnOptionChanged),
                new ForgetOption(ForgetWhat.HandPairings, "Pairings set by hand", handPairings,
                    "Elements paired, or kept apart, by hand. The check pairs them on its own the next time it runs.",
                    OnOptionChanged),
                new ForgetOption(ForgetWhat.LoadingPlans, "Loading plan pairings", loadingPlans,
                    "Which view and schedule sheet each level's loads are read from. The load checks have nothing to read until levels are paired again.",
                    OnOptionChanged),
                new ForgetOption(ForgetWhat.HeightZones, "Height zones", heightZones,
                    "Zones drawn on the levels where the line loads take a height of their own. The line loads go back to the floor to floor height.",
                    OnOptionChanged)
            };
        }

        public IList<ForgetOption> Options { get; }

        /// <summary>True when there is anything at all on this project to forget.</summary>
        public bool HasAny => Options.Any(option => option.HasAny);

        /// <summary>
        /// Everything there is to forget, ticked or not. Null while only some of it is ticked,
        /// which the box shows filled in; clicking it then ticks the lot.
        /// </summary>
        public bool? Everything
        {
            get
            {
                List<ForgetOption> any = Options.Where(option => option.HasAny).ToList();
                if (any.Count == 0 || any.All(option => !option.IsSelected))
                {
                    return false;
                }
                return any.All(option => option.IsSelected) ? true : (bool?)null;
            }
            set
            {
                settling = true;
                try
                {
                    foreach (ForgetOption option in Options)
                    {
                        option.IsSelected = value == true;
                    }
                }
                finally
                {
                    settling = false;
                }
                OnOptionChanged();
            }
        }

        public bool CanForget => Options.Any(option => option.IsSelected);

        public bool IsChosen(ForgetWhat what)
        {
            return Options.Any(option => option.What == what && option.IsSelected);
        }

        /// <summary>The button says how much it is about to do.</summary>
        public string ForgetButtonText => Everything == true ? "FORGET EVERYTHING" : "FORGET";

        private void OnOptionChanged()
        {
            if (settling)
            {
                return;
            }

            OnPropertyChanged(nameof(Everything));
            OnPropertyChanged(nameof(CanForget));
            OnPropertyChanged(nameof(ForgetButtonText));
        }
    }
}
