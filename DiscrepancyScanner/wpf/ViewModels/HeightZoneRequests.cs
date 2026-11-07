using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// What the height zone dialog asks: the height, in metres, and a name to know it by. The
    /// height is typed as it is said on a drawing - "4.5" - and nothing is kept until it reads
    /// as a height above nothing.
    /// </summary>
    public sealed class HeightZoneRequest : ViewModelBase
    {
        private string heightText;
        private string name;

        /// <param name="heading">What the dialog is about: "NEW HEIGHT ZONE ON L31".</param>
        /// <param name="floorHeightMM">The level's own floor to floor height, for saying what is being replaced; zero where not known.</param>
        /// <param name="heightMM">The height the box starts with, or zero to start it empty.</param>
        /// <param name="canName">False when several zones are being changed at once, which keep their own names.</param>
        public HeightZoneRequest(string heading, double floorHeightMM, double heightMM, string name, bool canName = true)
        {
            Heading = heading ?? "";
            FloorHeightMM = floorHeightMM;
            heightText = heightMM > 0 ? (heightMM / 1000d).ToString("0.###", CultureInfo.CurrentCulture) : "";
            this.name = name ?? "";
            CanName = canName;
        }

        public string Heading { get; }

        public double FloorHeightMM { get; }

        public bool CanName { get; }

        /// <summary>What the dialog says under its heading: what the height is, and what it stands in for.</summary>
        public string About => "The floor to floor height line loads drawn mostly inside the zone are worked out "
                               + "from, in place of "
                               + (FloorHeightMM > 0
                                   ? "the level's own " + HeightZone.Metres(FloorHeightMM)
                                   : "the level's own, which is not known here")
                               + ". The allowance for the slab still comes off it. Nothing but the line loads reads it.";

        public string HeightText
        {
            get => heightText;
            set
            {
                if (SetProperty(ref heightText, value ?? ""))
                {
                    OnPropertyChanged(nameof(IsValid));
                    OnPropertyChanged(nameof(Problem));
                    OnPropertyChanged(nameof(HasProblem));
                }
            }
        }

        /// <summary>The name as typed. Trimmed only when it is kept, so a space can be typed into it.</summary>
        public string Name
        {
            get => name;
            set => SetProperty(ref name, value ?? "");
        }

        public string KeptName => (name ?? "").Trim();

        /// <summary>The height as read, in mm; zero while it does not read.</summary>
        public double HeightMM
        {
            get
            {
                double metres;
                return TryMetres(heightText, out metres) && metres > 0 ? Math.Round(metres * 1000d, 1) : 0;
            }
        }

        public bool IsValid => HeightMM > 0;

        public string Problem => IsValid || string.IsNullOrWhiteSpace(heightText)
            ? ""
            : "Give the height in metres, as a number above nothing - 4.5, say.";

        public bool HasProblem => Problem.Length > 0;

        /// <summary>
        /// Metres, as typed in whichever way the machine writes a decimal - or the other way, so a
        /// "4.5" typed on a machine that writes "4,5" still reads. A trailing "m" is allowed.
        /// </summary>
        internal static bool TryMetres(string text, out double metres)
        {
            metres = 0;
            string trimmed = (text ?? "").Trim();
            if (trimmed.EndsWith("m", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 1).Trim();
            }
            if (trimmed.Length == 0)
            {
                return false;
            }

            return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out metres)
                   || double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out metres);
        }
    }

    /// <summary>One level in the list a zone is copied to.</summary>
    public sealed class LevelChoice : ViewModelBase
    {
        private readonly Action changed;
        private bool isSelected;

        public LevelChoice(string name, string detail, Action changed)
        {
            Name = name;
            Detail = detail ?? "";
            this.changed = changed;
        }

        public string Name { get; }

        /// <summary>What is on the level already, so a copy over a level that has zones is seen coming.</summary>
        public string Detail { get; }

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (SetProperty(ref isSelected, value))
                {
                    changed?.Invoke();
                }
            }
        }
    }

    /// <summary>What the copy dialog asks: which levels, ticked out of every other level.</summary>
    public sealed class LevelChoices : ViewModelBase
    {
        public LevelChoices(string heading, string about, IEnumerable<KeyValuePair<string, string>> levels)
        {
            Heading = heading ?? "";
            About = about ?? "";
            Levels = new ObservableCollection<LevelChoice>(
                levels.Select(level => new LevelChoice(level.Key, level.Value, OnChanged)));
            AllCommand = new RelayCommand(_ => SetAll(true));
            NoneCommand = new RelayCommand(_ => SetAll(false));
        }

        public string Heading { get; }
        public string About { get; }
        public ObservableCollection<LevelChoice> Levels { get; }

        public System.Windows.Input.ICommand AllCommand { get; }
        public System.Windows.Input.ICommand NoneCommand { get; }

        public IList<string> Chosen => Levels.Where(level => level.IsSelected).Select(level => level.Name).ToList();

        public bool CanApply => Levels.Any(level => level.IsSelected);

        public string ApplyText
        {
            get
            {
                int count = Levels.Count(level => level.IsSelected);
                return count == 0 ? "COPY" : "COPY TO " + count + (count == 1 ? " LEVEL" : " LEVELS");
            }
        }

        private void SetAll(bool selected)
        {
            foreach (LevelChoice level in Levels)
            {
                level.IsSelected = selected;
            }
        }

        private void OnChanged()
        {
            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(ApplyText));
        }
    }
}
