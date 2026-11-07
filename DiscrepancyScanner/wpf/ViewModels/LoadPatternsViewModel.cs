using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>One load pattern the imported ETABS model carries, and how much of the model carries it.</summary>
    public sealed class ModelLoadPattern
    {
        public ModelLoadPattern(string name, int shells, int frames)
        {
            Name = name ?? "";
            Shells = shells;
            Frames = frames;
        }

        public string Name { get; }

        /// <summary>How many shells carry an area load in this pattern.</summary>
        public int Shells { get; }

        /// <summary>How many frames carry a distributed load in this pattern.</summary>
        public int Frames { get; }

        /// <summary>
        /// Every pattern the imported model loads its shells and frames in, busiest first. Read
        /// off what the import brought in, so a pattern nothing is loaded in is not offered.
        /// </summary>
        public static List<ModelLoadPattern> From(IEnumerable<Slab_E> slabs, IEnumerable<LineLoadFrame_E> frames)
        {
            Dictionary<string, int[]> counts = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);

            foreach (Slab_E slab in slabs ?? new Slab_E[0])
            {
                foreach (string pattern in (slab?.Loads ?? new AreaLoad_E[0]).Select(load => load.Pattern).Distinct())
                {
                    Count(counts, pattern)[0]++;
                }
            }

            foreach (LineLoadFrame_E frame in frames ?? new LineLoadFrame_E[0])
            {
                foreach (string pattern in (frame?.Loads ?? new LineLoad_E[0]).Select(load => load.Pattern).Distinct())
                {
                    Count(counts, pattern)[1]++;
                }
            }

            return counts.Where(entry => entry.Key.Length > 0)
                         .Select(entry => new ModelLoadPattern(entry.Key, entry.Value[0], entry.Value[1]))
                         .OrderByDescending(pattern => pattern.Shells + pattern.Frames)
                         .ThenBy(pattern => pattern.Name, StringComparer.OrdinalIgnoreCase)
                         .ToList();
        }

        private static int[] Count(Dictionary<string, int[]> counts, string pattern)
        {
            string name = (pattern ?? "").Trim();
            int[] count;
            if (!counts.TryGetValue(name, out count))
            {
                count = new int[2];
                counts[name] = count;
            }
            return count;
        }
    }

    /// <summary>One choice in a row's "read as" picker.</summary>
    public sealed class LoadChoice
    {
        public LoadChoice(LoadCategory category, string name)
        {
            Category = category;
            Name = name;
        }

        public LoadCategory Category { get; }

        public string Name { get; }

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// One ETABS pattern in the table: what it is read as now, and the pickers that change it.
    /// Changing a picker changes the lists of names in the window, and every row is then read
    /// again from those lists - so what a row says is always what the check would do.
    /// </summary>
    public sealed class LoadPatternRow : ViewModelBase
    {
        private readonly LoadPatternsViewModel owner;
        private LoadChoice areaChoice;
        private bool lineRead;
        private bool sidlCRead;
        private string note = "";

        internal LoadPatternRow(LoadPatternsViewModel owner, ModelLoadPattern pattern)
        {
            this.owner = owner;
            Pattern = pattern;
        }

        public ModelLoadPattern Pattern { get; }

        public string Name { get { return Pattern.Name; } }

        public string Shells { get { return Pattern.Shells == 0 ? "" : Pattern.Shells.ToString(); } }

        public string Frames { get { return Pattern.Frames == 0 ? "" : Pattern.Frames.ToString(); } }

        public IList<LoadChoice> Choices { get { return owner.Choices; } }

        /// <summary>What the area load check reads this pattern as.</summary>
        public LoadChoice AreaChoice
        {
            get => areaChoice;
            set
            {
                if (value == null || value == areaChoice || owner.Refreshing)
                {
                    return;
                }

                owner.SetArea(Name, value.Category);
            }
        }

        /// <summary>Whether the line load check reads loads in this pattern.</summary>
        public bool LineRead
        {
            get => lineRead;
            set
            {
                if (value == lineRead || owner.Refreshing)
                {
                    return;
                }

                owner.SetLine(Name, value);
            }
        }

        /// <summary>How the answer was arrived at, where that is not obvious.</summary>
        public string Note { get { return note; } }

        /// <summary>Whether the SIDL_C checks read this pattern, which keeps it from being the SIDL.</summary>
        public bool SidlCRead { get { return sidlCRead; } }

        /// <summary>True for a pattern the model loads in that no check reads.</summary>
        public bool IsUnused { get { return areaChoice != null && areaChoice.Category == LoadCategory.None && !lineRead && !sidlCRead; } }

        internal void Show(LoadChoice area, bool line, bool sidlC, string why)
        {
            areaChoice = area;
            lineRead = line;
            sidlCRead = sidlC;
            note = why ?? "";

            OnPropertyChanged(nameof(AreaChoice));
            OnPropertyChanged(nameof(LineRead));
            OnPropertyChanged(nameof(Note));
            OnPropertyChanged(nameof(SidlCRead));
            OnPropertyChanged(nameof(IsUnused));
        }
    }

    /// <summary>
    /// Which ETABS load pattern is which load of the loading plan, and what the loading
    /// schedule calls each of them - the two halves of the one mapping, side by side.
    ///
    /// The ETABS half used to be four boxes of names in the settings, typed blind: nothing
    /// said what patterns the model actually had or what the names would catch. Here the
    /// model's own patterns are listed, each with what it is read as, and picking a load for
    /// one writes its name into the right list. The lists are still there to be typed into,
    /// and a row reads whatever they now say, so the two ways of editing cannot disagree.
    ///
    /// The schedule half is what the workbook heads its columns with and how it marks its live
    /// loads - the SIDL heading being the one most often written some other way.
    ///
    /// Nothing is written into the settings until OK: this edits copies.
    /// </summary>
    public sealed class LoadPatternsViewModel : ViewModelBase
    {
        private string sdl, live, reducible, mep, never, line, sidlC;
        private string sidlHeading, totalHeading, liveHeading, liveMarking, reducibleMarking, mepMarking;

        public LoadPatternsViewModel(PrefrencesData preferences, IEnumerable<ModelLoadPattern> modelPatterns)
        {
            sdl = preferences.SdlLoadPatternNames ?? "";
            live = preferences.LiveLoadPatternNames ?? "";
            reducible = preferences.ReducibleLiveLoadPatternNames ?? "";
            mep = preferences.MepLoadPatternNames ?? "";
            never = preferences.ExcludedLoadPatternNames ?? "";
            line = preferences.LineLoadPatternNames ?? "";
            sidlC = preferences.SidlCLoadPatternNames ?? "";

            sidlHeading = preferences.ScheduleSidlHeadingNames ?? "";
            totalHeading = preferences.ScheduleTotalHeadingNames ?? "";
            liveHeading = preferences.ScheduleLiveHeadingNames ?? "";
            liveMarking = preferences.ScheduleLiveMarkings ?? "";
            reducibleMarking = preferences.ScheduleReducibleLiveMarkings ?? "";
            mepMarking = preferences.ScheduleMepMarkings ?? "";

            Choices = new List<LoadChoice>
            {
                new LoadChoice(LoadCategory.Sdl, "SIDL"),
                new LoadChoice(LoadCategory.Ll, "Live (LL)"),
                new LoadChoice(LoadCategory.Lr, "Reducible live (LR)"),
                new LoadChoice(LoadCategory.Mep, "Mechanical (MEP)"),
                new LoadChoice(LoadCategory.None, "Not counted")
            };

            foreach (ModelLoadPattern pattern in modelPatterns ?? new ModelLoadPattern[0])
            {
                Rows.Add(new LoadPatternRow(this, pattern));
            }

            Refresh();
        }

        /// <summary>The loads a pattern can be read as, for every row's picker.</summary>
        public IList<LoadChoice> Choices { get; }

        public ObservableCollection<LoadPatternRow> Rows { get; } = new ObservableCollection<LoadPatternRow>();

        public bool HasPatterns { get { return Rows.Count > 0; } }

        public bool HasNoPatterns { get { return Rows.Count == 0; } }

        /// <summary>True while the rows are being read again, so the pickers being set does not read as the user picking.</summary>
        internal bool Refreshing { get; private set; }

        public string Summary
        {
            get
            {
                if (Rows.Count == 0)
                {
                    return "";
                }

                int unused = Rows.Count(row => row.IsUnused);
                return $"{Rows.Count} pattern(s) in the model"
                       + (unused == 0 ? ", every one of them read by a check." : $", {unused} of them read by neither check.");
            }
        }

        #region the ETABS names

        public string SdlNames { get => sdl; set => SetList(ref sdl, value); }
        public string LiveNames { get => live; set => SetList(ref live, value); }
        public string ReducibleNames { get => reducible; set => SetList(ref reducible, value); }
        public string MepNames { get => mep; set => SetList(ref mep, value); }
        public string NeverNames { get => never; set => SetList(ref never, value); }
        public string LineNames { get => line; set => SetList(ref line, value); }

        /// <summary>
        /// The SIDL_C checks' patterns. Not picked for here - they are the SIDL_C settings' - but
        /// a pattern on it is none of the four and not a line load, so every row has to know it.
        /// </summary>
        public string SidlCNames { get => sidlC; set => SetList(ref sidlC, value); }

        private void SetList(ref string field, string value, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
        {
            if (SetProperty(ref field, value ?? "", name))
            {
                Refresh();
            }
        }

        #endregion

        #region the schedule names

        public string SidlHeading { get => sidlHeading; set => SetProperty(ref sidlHeading, value ?? ""); }
        public string TotalHeading { get => totalHeading; set => SetProperty(ref totalHeading, value ?? ""); }
        public string LiveHeading { get => liveHeading; set => SetProperty(ref liveHeading, value ?? ""); }
        public string LiveMarking { get => liveMarking; set => SetProperty(ref liveMarking, value ?? ""); }
        public string ReducibleMarking { get => reducibleMarking; set => SetProperty(ref reducibleMarking, value ?? ""); }
        public string MepMarking { get => mepMarking; set => SetProperty(ref mepMarking, value ?? ""); }

        #endregion

        #region picking a load for a pattern

        /// <summary>
        /// Makes the area load check read a pattern as one load. Its name is taken out of every
        /// list and written, whole, into the one picked - a whole name beats a name found inside
        /// a longer one, so it is then read as that whatever else the lists say.
        ///
        /// Picked as not counted, it is written into the never list only when some other name
        /// would still find it. The never list holds for the line load check as well, which the
        /// row then says.
        ///
        /// Picked as a load, a SIDL_C pattern stops being one: a pattern of its own is never the
        /// SIDL, so picking it as the SIDL says it is not a pattern of its own after all.
        /// </summary>
        public void SetArea(string pattern, LoadCategory category)
        {
            if (category != LoadCategory.None)
            {
                sidlC = Without(sidlC, pattern);
            }

            sdl = Without(sdl, pattern);
            live = Without(live, pattern);
            reducible = Without(reducible, pattern);
            mep = Without(mep, pattern);
            never = Without(never, pattern);

            switch (category)
            {
                case LoadCategory.Sdl: sdl = With(sdl, pattern); break;
                case LoadCategory.Ll: live = With(live, pattern); break;
                case LoadCategory.Lr: reducible = With(reducible, pattern); break;
                case LoadCategory.Mep: mep = With(mep, pattern); break;
            }

            if (category == LoadCategory.None && Mapping().Categorise(pattern) != LoadCategory.None)
            {
                never = With(never, pattern);
            }

            RaiseLists();
            Refresh();
        }

        /// <summary>
        /// Makes the line load check read a pattern, or stop reading it. An empty list reads
        /// every pattern, so stopping one there writes out every other pattern in the model -
        /// which is what "all of them but this one" says as a list.
        /// </summary>
        public void SetLine(string pattern, bool read)
        {
            line = Without(line, pattern);

            if (read)
            {
                line = With(line, pattern);
                never = Without(never, pattern);
                sidlC = Without(sidlC, pattern);
            }
            else
            {
                if (PrefrencesData.SplitParameterNames(line).Length == 0)
                {
                    foreach (LoadPatternRow row in Rows.Where(r => r.LineRead && !SameName(r.Name, pattern)))
                    {
                        line = With(line, row.Name);
                    }
                }

                if (LineLoadPatterns.From(line, NotSidl).Matches(pattern) && PrefrencesData.SplitParameterNames(line).Length > 0)
                {
                    never = With(never, pattern);
                }
            }

            RaiseLists();
            Refresh();
        }

        #endregion

        /// <summary>Writes every name in the window into the settings.</summary>
        public void Apply(PrefrencesData preferences)
        {
            preferences.SdlLoadPatternNames = sdl;
            preferences.LiveLoadPatternNames = live;
            preferences.ReducibleLiveLoadPatternNames = reducible;
            preferences.MepLoadPatternNames = mep;
            preferences.ExcludedLoadPatternNames = never;
            preferences.LineLoadPatternNames = line;
            preferences.SidlCLoadPatternNames = sidlC;

            preferences.ScheduleSidlHeadingNames = sidlHeading;
            preferences.ScheduleTotalHeadingNames = totalHeading;
            preferences.ScheduleLiveHeadingNames = liveHeading;
            preferences.ScheduleLiveMarkings = liveMarking;
            preferences.ScheduleReducibleLiveMarkings = reducibleMarking;
            preferences.ScheduleMepMarkings = mepMarking;
        }

        /// <summary>Reads every row again from the lists as they now stand.</summary>
        private void Refresh()
        {
            LoadPatternMap.Mapping mapping = Mapping();
            LineLoadPatterns lines = LineLoadPatterns.From(line, NotSidl);
            LineLoadPatterns sidlCs = LineLoadPatterns.Exactly(sidlC);

            Refreshing = true;
            try
            {
                foreach (LoadPatternRow row in Rows)
                {
                    LoadCategory category = mapping.Categorise(row.Name);
                    bool read = lines.Matches(row.Name);
                    bool sidlCRead = !sidlCs.IsEmpty && sidlCs.Matches(row.Name);

                    row.Show(Choices.First(choice => choice.Category == category), read, sidlCRead,
                             Why(row.Name, category, read, sidlCRead));
                }
            }
            finally
            {
                Refreshing = false;
            }

            OnPropertyChanged(nameof(Summary));
        }

        /// <summary>
        /// Says how a row came to be read the way it is, where a name typed for something else
        /// caught it - which is the answer a person looking at a surprising row wants.
        /// </summary>
        private string Why(string pattern, LoadCategory category, bool lineRead, bool sidlCRead)
        {
            List<string> notes = new List<string>();

            if (sidlCRead)
            {
                notes.Add("read by the SIDL_C checks, so not as the SIDL");
            }
            else if (Named(never, pattern))
            {
                notes.Add("on the never list, so neither check reads it");
            }
            else if (category != LoadCategory.None && !Named(ListFor(category), pattern))
            {
                string by = PrefrencesData.SplitParameterNames(ListFor(category))
                                          .FirstOrDefault(name => Key(pattern).Contains(Key(name)));
                notes.Add($"caught by \"{by}\" inside its name");
            }

            if (lineRead && PrefrencesData.SplitParameterNames(line).Length == 0)
            {
                notes.Add("line loads: none named, so every pattern is read");
            }
            else if (lineRead && !Named(line, pattern))
            {
                string by = PrefrencesData.SplitParameterNames(line).FirstOrDefault(name => Key(pattern).Contains(Key(name)));
                notes.Add($"line loads: caught by \"{by}\" inside its name");
            }

            return string.Join("; ", notes);
        }

        private string ListFor(LoadCategory category)
        {
            switch (category)
            {
                case LoadCategory.Sdl: return sdl;
                case LoadCategory.Ll: return live;
                case LoadCategory.Lr: return reducible;
                case LoadCategory.Mep: return mep;
                default: return "";
            }
        }

        private LoadPatternMap.Mapping Mapping()
        {
            return LoadPatternMap.From(sdl, live, reducible, mep, NotSidl);
        }

        /// <summary>The never list as the checks read it: with the SIDL_C patterns on it as well.</summary>
        private string NotSidl { get { return PrefrencesData.NotSidl(never, sidlC); } }

        private void RaiseLists()
        {
            OnPropertyChanged(nameof(SdlNames));
            OnPropertyChanged(nameof(LiveNames));
            OnPropertyChanged(nameof(ReducibleNames));
            OnPropertyChanged(nameof(MepNames));
            OnPropertyChanged(nameof(NeverNames));
            OnPropertyChanged(nameof(LineNames));
            OnPropertyChanged(nameof(SidlCNames));
        }

        #region lists of names

        /// <summary>The list with a name added, unless it is on it already.</summary>
        internal static string With(string names, string name)
        {
            if (Named(names, name))
            {
                return names;
            }

            List<string> list = PrefrencesData.SplitParameterNames(names).ToList();
            list.Add(name.Trim());
            return string.Join(", ", list);
        }

        /// <summary>The list with a name taken off it, however it was written there.</summary>
        internal static string Without(string names, string name)
        {
            if (!Named(names, name))
            {
                return names ?? "";
            }

            return string.Join(", ", PrefrencesData.SplitParameterNames(names).Where(n => !SameName(n, name)));
        }

        private static bool Named(string names, string name)
        {
            return PrefrencesData.SplitParameterNames(names).Any(n => SameName(n, name));
        }

        private static bool SameName(string a, string b)
        {
            string key = Key(a);
            return key.Length > 0 && key == Key(b);
        }

        /// <summary>A name the way the pattern matching compares it: upper case, letters and digits only.</summary>
        private static string Key(string name)
        {
            return new string((name ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        }

        #endregion
    }
}
