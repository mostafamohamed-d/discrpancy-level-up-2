using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>The six colours in the key along the scope bar, one per thing a result can say.</summary>
    public enum VerdictColour
    {
        Failed,
        Review,
        Passed,
        EtabsOnly,
        RevitOnly,
        Accepted
    }

    /// <summary>
    /// The colours the plan draws its verdicts in, which the user can change by right-clicking
    /// the key.
    ///
    /// One store for every place a verdict is shown - the plan, the key, the swatches beside
    /// the switches, the findings list, the card and the chooser - so a colour changed in one
    /// is changed in all of them. A swatch that disagreed with the plan would be saying the
    /// wrong thing about what the plan is showing.
    ///
    /// Only the colours somebody changed are kept in the settings file. The rest are read from
    /// the defaults here, so a default improved in a later version reaches everyone who never
    /// chose one of their own.
    /// </summary>
    public static class VerdictColours
    {
        private static readonly Dictionary<VerdictColour, Color> defaults = new Dictionary<VerdictColour, Color>
        {
            { VerdictColour.Failed, Color.FromRgb(0xD6, 0x1F, 0x26) },
            { VerdictColour.Review, Color.FromRgb(0xE8, 0x82, 0x0C) },
            { VerdictColour.Passed, Color.FromRgb(0x1E, 0xA8, 0x4C) },

            // Raspberry for ETABS and a deep violet for Revit. The key already takes most of the
            // wheel - red, orange, green, violet and the steel blue - and what is left for
            // ETABS is the gap between violet and red. A teal was tried first and on a real plan
            // read as the green of a pass, which hid every beam only ETABS had among the ones
            // that agreed; nothing a finding is drawn in may be taken for a pass.
            { VerdictColour.EtabsOnly, Color.FromRgb(0x98, 0x10, 0x57) },
            { VerdictColour.RevitOnly, Color.FromRgb(0x66, 0x21, 0xB1) },

            // The steel blue the rest of the window is built out of rather than a green: an
            // agreed difference is not a match, and colouring it as one would hide it.
            { VerdictColour.Accepted, Color.FromRgb(0x59, 0x80, 0xA6) }
        };

        private static readonly Dictionary<VerdictColour, Color> chosen = new Dictionary<VerdictColour, Color>();
        private static readonly Dictionary<VerdictColour, Brush> brushes = new Dictionary<VerdictColour, Brush>();

        /// <summary>The six in the order the key shows them.</summary>
        public static IList<VerdictColour> All
        {
            get { return (VerdictColour[])Enum.GetValues(typeof(VerdictColour)); }
        }

        /// <summary>What the key calls each one.</summary>
        public static string NameOf(VerdictColour which)
        {
            switch (which)
            {
                case VerdictColour.Failed: return "Fails";
                case VerdictColour.Review: return "Needs a look";
                case VerdictColour.Passed: return "Agrees";
                case VerdictColour.EtabsOnly: return "ETABS only";
                case VerdictColour.RevitOnly: return "Revit only";
                default: return "Agreed";
            }
        }

        public static Color ColorOf(VerdictColour which)
        {
            Color colour;
            return chosen.TryGetValue(which, out colour) ? colour : defaults[which];
        }

        public static Color DefaultOf(VerdictColour which)
        {
            return defaults[which];
        }

        /// <summary>As the string a swatch binds to: #AARRGGBB.</summary>
        public static string Hex(VerdictColour which)
        {
            return ToHex(ColorOf(which));
        }

        public static string DefaultHex(VerdictColour which)
        {
            return ToHex(defaults[which]);
        }

        /// <summary>
        /// The brush the plan draws with. The same instance until the colour changes, so a
        /// draw of a few thousand elements does not make a few thousand brushes.
        /// </summary>
        public static Brush BrushOf(VerdictColour which)
        {
            Brush brush;
            if (!brushes.TryGetValue(which, out brush))
            {
                brush = new SolidColorBrush(ColorOf(which));
                brush.Freeze();
                brushes[which] = brush;
            }
            return brush;
        }

        public static bool IsDefault(VerdictColour which)
        {
            return !chosen.ContainsKey(which);
        }

        /// <summary>
        /// Uses <paramref name="colour"/> for this verdict from now on. Picking the default
        /// again forgets the choice rather than storing it, which is how a colour is put back.
        /// Always opaque: a see-through verdict would take its colour from whatever it was
        /// drawn over. True when anything changed.
        /// </summary>
        public static bool Set(VerdictColour which, Color colour)
        {
            colour.A = 0xFF;

            if (colour == ColorOf(which))
            {
                return false;
            }

            if (colour == defaults[which])
            {
                chosen.Remove(which);
            }
            else
            {
                chosen[which] = colour;
            }

            brushes.Remove(which);
            return true;
        }

        /// <summary>Puts every colour back to its default.</summary>
        public static void ResetAll()
        {
            chosen.Clear();
            brushes.Clear();
        }

        /// <summary>
        /// The other verdicts already drawn in exactly this colour. Allowed - it is the user's
        /// plan - but worth saying, because two verdicts in one colour cannot be told apart.
        /// </summary>
        public static IList<VerdictColour> SharingColourWith(VerdictColour which)
        {
            Color colour = ColorOf(which);
            return All.Where(other => other != which && ColorOf(other) == colour).ToList();
        }

        /// <summary>
        /// Reads the chosen colours out of the view branch of the settings file, in place of
        /// whatever was chosen before. A name or a value that does not read is left at its
        /// default rather than stopping the rest.
        /// </summary>
        public static void ReadFrom(IDictionary<string, object> view)
        {
            ResetAll();

            foreach (KeyValuePair<string, object> entry in Json.Branch(view, Branch))
            {
                VerdictColour which;
                Color colour;
                if (Enum.TryParse(entry.Key, true, out which) && Enum.IsDefined(typeof(VerdictColour), which)
                    && TryParse(entry.Value as string, out colour))
                {
                    Set(which, colour);
                }
            }
        }

        /// <summary>The chosen colours as they go into the settings file: only the changed ones.</summary>
        public static Dictionary<string, object> ToJson()
        {
            return chosen.OrderBy(entry => entry.Key)
                         .ToDictionary(entry => entry.Key.ToString(),
                                       entry => (object)ToHex(entry.Value),
                                       StringComparer.Ordinal);
        }

        /// <summary>Where the chosen colours sit in the settings file.</summary>
        public const string Branch = "verdictColours";

        private static string ToHex(Color colour)
        {
            return "#" + colour.A.ToString("X2") + colour.R.ToString("X2")
                   + colour.G.ToString("X2") + colour.B.ToString("X2");
        }

        private static bool TryParse(string text, out Color colour)
        {
            colour = default(Color);
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            try
            {
                colour = (Color)ColorConverter.ConvertFromString(text.Trim());
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
