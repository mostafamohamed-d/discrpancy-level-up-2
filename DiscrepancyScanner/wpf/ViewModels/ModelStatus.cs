using System;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// One of the two models, as the header bar reads it: which file, and whether it has been
    /// read yet.
    ///
    /// The header exists to answer the question that used to be answered by scrolling the
    /// status line - "am I looking at the model I think I am?" - before a run rather than
    /// after it, so a check is never read against a model that was replaced this morning.
    /// </summary>
    public sealed class ModelSource : ViewModelBase
    {
        private string name = "";
        private bool isRead;
        private bool isStale;

        public ModelSource(string label)
        {
            Label = label;
        }

        /// <summary>"ETABS" or "Revit".</summary>
        public string Label { get; }

        /// <summary>The file or document name, once something has been read.</summary>
        public string Name
        {
            get => name;
            private set
            {
                if (SetProperty(ref name, value))
                {
                    OnPropertyChanged(nameof(Description));
                    OnPropertyChanged(nameof(Dot));
                }
            }
        }

        public bool IsRead
        {
            get => isRead;
            private set
            {
                if (SetProperty(ref isRead, value))
                {
                    OnPropertyChanged(nameof(Description));
                    OnPropertyChanged(nameof(Dot));
                }
            }
        }

        /// <summary>
        /// The model has been read, but something has happened since that makes the reading
        /// worth repeating - so far, an alignment change, which is baked into every element as
        /// it is built and so cannot be applied to elements already imported.
        /// </summary>
        public bool IsStale
        {
            get => isStale;
            set
            {
                if (SetProperty(ref isStale, value))
                {
                    OnPropertyChanged(nameof(Description));
                    OnPropertyChanged(nameof(Dot));
                }
            }
        }

        /// <summary>
        /// The dot beside the name: green once the model is in, amber while the reading is
        /// worth repeating, grey before it has been read at all. A string rather than a brush,
        /// the way the filter swatches already do it - WPF converts it on the way into Fill,
        /// and it keeps the XAML free of a local type reference.
        /// </summary>
        public string Dot
        {
            get
            {
                if (!isRead)
                {
                    return "#FFB7B7BA";
                }
                return isStale ? "#FFE8820C" : "#FF1EA84C";
            }
        }

        /// <summary>What the header prints beside the tick.</summary>
        public string Description
        {
            get
            {
                if (!isRead)
                {
                    return Label + " not read yet";
                }
                return Label + " " + (string.IsNullOrEmpty(name) ? "read" : name)
                       + (isStale ? " — worth reading again" : "");
            }
        }

        public void MarkRead(string sourceName)
        {
            Name = sourceName ?? "";
            IsRead = true;
            IsStale = false;
        }
    }

    /// <summary>
    /// What the header bar says about the run: the two models, and when the check was last
    /// done. Kept apart from the comparison view model because it is only ever read, never
    /// acted on - nothing here changes what a check does, it only says what is loaded.
    /// </summary>
    public sealed class ModelStatus : ViewModelBase
    {
        private DateTime? lastRun;

        public ModelSource Etabs { get; } = new ModelSource("ETABS");
        public ModelSource Revit { get; } = new ModelSource("Revit");

        /// <summary>True once both models are in, which is when a run can say anything.</summary>
        public bool CanRun => Etabs.IsRead && Revit.IsRead;

        public void MarkRun()
        {
            lastRun = DateTime.Now;
            OnPropertyChanged(nameof(LastRunDescription));
            OnPropertyChanged(nameof(HasRun));
        }

        public bool HasRun => lastRun.HasValue;

        /// <summary>
        /// "Last run today 09:14", or the date as well once it is not today's run any more -
        /// a check read back the next morning should not look like it was just done.
        /// </summary>
        public string LastRunDescription
        {
            get
            {
                if (!lastRun.HasValue)
                {
                    return "Not run yet";
                }

                DateTime when = lastRun.Value;
                return when.Date == DateTime.Today
                    ? "Last run today " + when.ToString("HH:mm")
                    : "Last run " + when.ToString("d MMM HH:mm");
            }
        }

        public void Refresh()
        {
            OnPropertyChanged(nameof(CanRun));
        }
    }
}
