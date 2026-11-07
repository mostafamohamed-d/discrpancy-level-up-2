using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>How badly one line of the preflight wants dealing with.</summary>
    public enum PreflightLevel
    {
        /// <summary>The run cannot say anything useful until this is done.</summary>
        Blocker,

        /// <summary>The run will work, but part of it will be answered from stale or absent input.</summary>
        Warning,

        /// <summary>Already dealt with. Listed so the sheet says what is ready, not only what is not.</summary>
        Done
    }

    /// <summary>
    /// One line of the sheet that comes up when the check is asked for before it is ready.
    ///
    /// Each line carries its own fix, because being told what is missing and then having to go
    /// and find where it is set is most of the work of being told.
    /// </summary>
    public sealed class PreflightItem
    {
        public PreflightItem(PreflightLevel level, string title, string detail,
                             string actionText = null, ICommand action = null,
                             bool fixesItself = false)
        {
            Level = level;
            Title = title ?? "";
            Detail = detail ?? "";
            ActionText = actionText ?? "";
            Action = action;
            FixesItself = fixesItself;
        }

        public PreflightLevel Level { get; }
        public string Title { get; }
        public string Detail { get; }
        public string ActionText { get; }
        public ICommand Action { get; }

        /// <summary>
        /// True where pressing the button finishes the job rather than opening the place it is
        /// done. Reading a model finishes it; aligning two models does not, because somebody
        /// has to pick two points in Revit. It is the difference between a sheet that can offer
        /// to deal with everything at once and one that can only point.
        /// </summary>
        public bool FixesItself { get; }

        /// <summary>Set by the report so the numbers read 1, 2 down the blockers only.</summary>
        public string Number { get; internal set; } = "";

        public bool HasAction => Action != null && ActionText.Length > 0;
        public bool IsDone => Level == PreflightLevel.Done;

        /// <summary>
        /// The three marks down the left, as three flags rather than one - the row template
        /// picks between them by visibility, which is the only thing XAML can do without a
        /// converter carrying a copy of this rule.
        ///
        /// A blocker takes a number, so the headline's count can be followed down the list. A
        /// warning takes a bare mark: numbering it would make "two things are missing" name
        /// four rows, and it is not one of the two.
        /// </summary>
        public bool IsNumbered => Level == PreflightLevel.Blocker;
        public bool IsWarning => Level == PreflightLevel.Warning;

        /// <summary>The colour of the mark down the left. Matches the plan's own key.</summary>
        public string Swatch
        {
            get
            {
                switch (Level)
                {
                    case PreflightLevel.Blocker: return "#FFE8820C";
                    case PreflightLevel.Warning: return "#FFE8820C";
                    default: return "#FF1EA84C";
                }
            }
        }
    }

    /// <summary>
    /// What stands between pressing the button and a run worth reading.
    ///
    /// It is a sheet rather than a refusal on purpose. A run against an unaligned model is not
    /// nonsense - it says every element is unpaired, which for somebody who has just opened two
    /// models is a real answer - so the check is never blocked outright where it can still say
    /// something. What it must not do is come back with two thousand unpaired elements and let
    /// them be read as a finding.
    /// </summary>
    public sealed class PreflightReport
    {
        public PreflightReport(IEnumerable<PreflightItem> items, bool canRunAnyway)
        {
            Items = items.ToList();
            CanRunAnyway = canRunAnyway;

            int number = 0;
            foreach (PreflightItem item in Items.Where(i => i.Level == PreflightLevel.Blocker))
            {
                item.Number = (++number).ToString();
            }

            BlockerCount = number;
            WarningCount = Items.Count(i => i.Level == PreflightLevel.Warning);
        }

        public IList<PreflightItem> Items { get; }
        public int BlockerCount { get; }
        public int WarningCount { get; }

        /// <summary>Nothing to say: the run goes ahead without a word.</summary>
        public bool IsClear => BlockerCount == 0 && WarningCount == 0;

        /// <summary>
        /// True where every blocker is one the sheet can deal with on its own, which is what
        /// lets it offer to do the lot and then run.
        /// </summary>
        public bool CanFixEverything =>
            BlockerCount > 0
            && Items.Where(i => i.Level == PreflightLevel.Blocker).All(i => i.FixesItself);

        /// <summary>
        /// Whether running as things stand would say anything at all. With no model read there
        /// is nothing to compare, so the offer is not made - a button that does nothing is
        /// worse than no button.
        /// </summary>
        public bool CanRunAnyway { get; }

        public string Kicker => BlockerCount > 0 ? "Before this can run" : "Worth knowing first";

        public string Headline
        {
            get
            {
                int outstanding = BlockerCount + WarningCount;
                if (BlockerCount == 0)
                {
                    return outstanding == 1 ? "One thing is worth a look" : Spell(outstanding) + " things are worth a look";
                }
                return BlockerCount == 1 ? "One thing is missing" : Spell(BlockerCount) + " things are missing";
            }
        }

        public string Subhead
        {
            get
            {
                if (BlockerCount == 0)
                {
                    return "Nothing is stopping the run. These are the parts of it that will be "
                           + "answered from what is already there.";
                }
                if (!CanFixEverything)
                {
                    return "Everything else is ready. This one needs you in Revit, so it cannot be "
                           + "done from here.";
                }
                return BlockerCount == 1
                    ? "Everything else is ready. Deal with it and the run starts on its own."
                    : "Everything else is ready. Deal with these and the run starts on its own.";
            }
        }

        public string FixEverythingText =>
            BlockerCount == 1 ? "DO IT, THEN RUN" : "DO IT ALL, THEN RUN";

        /// <summary>What running as things stand would actually produce.</summary>
        public string RunAnywayNote { get; set; } = "";

        /// <summary>
        /// Small counts written out, because the headline is a sentence rather than a tally.
        /// Past four it stops being read as a sentence anyway and the digit is clearer.
        /// </summary>
        private static string Spell(int count)
        {
            switch (count)
            {
                case 2: return "Two";
                case 3: return "Three";
                case 4: return "Four";
                default: return count.ToString();
            }
        }
    }
}
