using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The colours in the key, which the user can change by right-clicking it.
    ///
    /// The same colours are shown in six places - the plan, the key, the swatches beside the
    /// switches, the findings list, the card and the chooser under the cursor - and a place
    /// that drifts from the plan says the wrong thing about what the plan is showing. So they
    /// are all read from one store, and these tests hold every place to it, before and after a
    /// colour has been changed.
    /// </summary>
    [TestClass]
    public class OneModelColourTests
    {
        [TestCleanup]
        public void PutTheColoursBack()
        {
            VerdictColours.ResetAll();
        }

        [TestMethod]
        public void ThePlanDrawsEachModelInItsOwnColour()
        {
            Assert.AreEqual(VerdictColours.ColorOf(VerdictColour.EtabsOnly), ColourOf(ViewPalette.UnpairedEtabs));
            Assert.AreEqual(VerdictColours.ColorOf(VerdictColour.RevitOnly), ColourOf(ViewPalette.UnpairedRevit));
            Assert.AreNotEqual(VerdictColours.Hex(VerdictColour.EtabsOnly), VerdictColours.Hex(VerdictColour.RevitOnly));

            Assert.AreSame(ViewPalette.UnpairedRevit, ViewPalette.ForState(Unpaired(ElementSource.UnmatchedRevit)));
            Assert.AreSame(ViewPalette.UnpairedEtabs, ViewPalette.ForState(Unpaired(ElementSource.UnmatchedEtabs)));
        }

        /// <summary>
        /// The findings list and the chooser, which bind a colour as text rather than as a brush.
        /// The chooser used to show every unpaired element in the ETABS colour, which went
        /// unnoticed only while the two colours were both purple.
        /// </summary>
        [TestMethod]
        public void TheListsShowTheSameColourAsThePlan()
        {
            ElementViewModel revit = Unpaired(ElementSource.UnmatchedRevit);
            ElementViewModel etabs = Unpaired(ElementSource.UnmatchedEtabs);

            Assert.AreEqual(VerdictColours.Hex(VerdictColour.RevitOnly),
                            CheckNaming.SwatchOf(CheckKind.Pairing, ResultLevel.Failed, revit));
            Assert.AreEqual(VerdictColours.Hex(VerdictColour.EtabsOnly),
                            CheckNaming.SwatchOf(CheckKind.Pairing, ResultLevel.Failed, etabs));

            Assert.AreEqual(VerdictColours.Hex(VerdictColour.RevitOnly),
                            CheckNaming.SwatchOfState(revit.DisplayState, revit.Source));
            Assert.AreEqual(VerdictColours.Hex(VerdictColour.EtabsOnly),
                            CheckNaming.SwatchOfState(etabs.DisplayState, etabs.Source));
        }

        /// <summary>
        /// Nothing a finding is drawn in may be taken for a pass. The ETABS colour was a teal
        /// first, and on a real plan the beams only ETABS had read as ones that agreed.
        /// </summary>
        [TestMethod]
        public void NoFindingIsDrawnInAColourNearThePass()
        {
            Color pass = VerdictColours.DefaultOf(VerdictColour.Passed);

            foreach (VerdictColour finding in new[]
                     { VerdictColour.Failed, VerdictColour.Review, VerdictColour.EtabsOnly, VerdictColour.RevitOnly })
            {
                double hue = HueGap(VerdictColours.DefaultOf(finding), pass);
                Assert.IsTrue(hue >= 60, finding + " is only " + Math.Round(hue) + " degrees of hue from the pass");
            }
        }

        /// <summary>
        /// A colour picked off the key reaches every place it is shown: the plan, the swatch in
        /// the findings list, the chooser and the switches.
        /// </summary>
        [TestMethod]
        public void AColourPickedOffTheKeyIsUsedEverywhere()
        {
            Color picked = Color.FromRgb(0x10, 0x20, 0x30);
            ElementViewModel etabs = Unpaired(ElementSource.UnmatchedEtabs);
            CheckFilters switches = new CheckFilters(() => { });

            Assert.IsTrue(VerdictColours.Set(VerdictColour.EtabsOnly, picked));

            Assert.AreEqual(picked, ColourOf(ViewPalette.ForState(etabs)));
            Assert.AreEqual("#FF102030", CheckNaming.SwatchOf(CheckKind.Pairing, ResultLevel.Failed, etabs));
            Assert.AreEqual("#FF102030", CheckNaming.SwatchOfState(etabs.DisplayState, etabs.Source));
            Assert.IsTrue(switches.Groups.SelectMany(group => group.Options)
                                  .Where(option => option.SwatchColour == VerdictColour.EtabsOnly)
                                  .All(option => option.Swatch == "#FF102030"));
            Assert.AreEqual("#FF102030", new VerdictKeyEntry(VerdictColour.EtabsOnly).Swatch);

            Assert.AreEqual(VerdictColours.DefaultHex(VerdictColour.RevitOnly), VerdictColours.Hex(VerdictColour.RevitOnly),
                            "the others are left as they were");
        }

        /// <summary>
        /// Kept between sessions, and only the colours that were changed: a default improved in
        /// a later version then reaches everyone who never picked one of their own.
        /// </summary>
        [TestMethod]
        public void OnlyTheChangedColoursAreSavedAndTheyComeBack()
        {
            VerdictColours.Set(VerdictColour.Failed, Color.FromRgb(0xFF, 0x00, 0xFF));

            Dictionary<string, object> saved = VerdictColours.ToJson();
            CollectionAssert.AreEquivalent(new[] { "Failed" }, saved.Keys.ToList());

            Dictionary<string, object> view = new Dictionary<string, object> { { VerdictColours.Branch, saved } };
            IDictionary<string, object> read = Json.Read(Json.Write(view));

            VerdictColours.ResetAll();
            VerdictColours.ReadFrom(read);

            Assert.AreEqual("#FFFF00FF", VerdictColours.Hex(VerdictColour.Failed));
            Assert.IsTrue(VerdictColours.IsDefault(VerdictColour.Passed));
        }

        /// <summary>Picking a colour's own default again forgets the choice rather than storing it.</summary>
        [TestMethod]
        public void PickingTheDefaultAgainPutsItBack()
        {
            VerdictColours.Set(VerdictColour.Review, Color.FromRgb(1, 2, 3));
            VerdictColours.Set(VerdictColour.Review, VerdictColours.DefaultOf(VerdictColour.Review));

            Assert.IsTrue(VerdictColours.IsDefault(VerdictColour.Review));
            Assert.AreEqual(0, VerdictColours.ToJson().Count);
        }

        /// <summary>
        /// A settings file edited by hand, or written by a later version with a verdict this one
        /// has not heard of, loses the entry that does not read and keeps the rest.
        /// </summary>
        [TestMethod]
        public void AColourThatDoesNotReadIsLeftAtItsDefault()
        {
            Dictionary<string, object> view = new Dictionary<string, object>
            {
                {
                    VerdictColours.Branch, new Dictionary<string, object>
                    {
                        { "Failed", "not a colour" },
                        { "SomethingNew", "#FF00FF00" },
                        { "Accepted", "#FF112233" }
                    }
                }
            };

            VerdictColours.ReadFrom(view);

            Assert.IsTrue(VerdictColours.IsDefault(VerdictColour.Failed));
            Assert.AreEqual("#FF112233", VerdictColours.Hex(VerdictColour.Accepted));
        }

        /// <summary>A see-through verdict would take its colour from whatever it was drawn over.</summary>
        [TestMethod]
        public void APickedColourIsAlwaysOpaque()
        {
            VerdictColours.Set(VerdictColour.Passed, Color.FromArgb(0x40, 0x10, 0x80, 0x10));

            Assert.AreEqual(0xFF, VerdictColours.ColorOf(VerdictColour.Passed).A);
        }

        [TestMethod]
        public void TwoVerdictsInOneColourAreNoticed()
        {
            VerdictColours.Set(VerdictColour.EtabsOnly, VerdictColours.DefaultOf(VerdictColour.Passed));

            CollectionAssert.AreEqual(new[] { VerdictColour.Passed },
                                      VerdictColours.SharingColourWith(VerdictColour.EtabsOnly).ToList());
        }

        /// <summary>
        /// The one copy that cannot be read off the store: the chrome's own brushes, which are
        /// XAML and are kept in step with the defaults by hand. This is the hand.
        /// </summary>
        [TestMethod]
        public void TheThemeCarriesTheDefaultColours()
        {
            string theme = File.ReadAllText(Path.Combine(RepositoryRoot(), "DiscrepancyScanner", "wpf",
                                                         "Theme", "Industry.xaml"));

            Assert.AreEqual(VerdictColours.DefaultOf(VerdictColour.Passed), Parse(BrushIn(theme, "VerdictPassed")));
            Assert.AreEqual(VerdictColours.DefaultOf(VerdictColour.Failed), Parse(BrushIn(theme, "VerdictFailed")));
            Assert.AreEqual(VerdictColours.DefaultOf(VerdictColour.Review), Parse(BrushIn(theme, "VerdictReview")));
            Assert.AreEqual(VerdictColours.DefaultOf(VerdictColour.EtabsOnly), Parse(BrushIn(theme, "VerdictEtabsOnly")));
            Assert.AreEqual(VerdictColours.DefaultOf(VerdictColour.RevitOnly), Parse(BrushIn(theme, "VerdictRevitOnly")));
        }

        private static ElementViewModel Unpaired(ElementSource source)
        {
            return new LineLoadElementViewModel(source, CheckState.Unpaired, "line", "L03",
                                                new ResultField[0], new MyPoint[0])
            {
                DisplayState = CheckState.Unpaired
            };
        }

        private static Color ColourOf(Brush brush)
        {
            return ((SolidColorBrush)brush).Color;
        }

        private static Color Parse(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }

        /// <summary>How far apart two colours are round the colour wheel, in degrees of HSV hue.</summary>
        private static double HueGap(Color a, Color b)
        {
            double gap = Math.Abs(Hue(a) - Hue(b)) % 360;
            return Math.Min(gap, 360 - gap);
        }

        private static double Hue(Color colour)
        {
            return System.Drawing.Color.FromArgb(colour.R, colour.G, colour.B).GetHue();
        }

        private static string BrushIn(string xaml, string key)
        {
            Match found = Regex.Match(xaml, "x:Key=\"" + key + "\"\\s+Color=\"(#[0-9A-Fa-f]{6,8})\"");
            Assert.IsTrue(found.Success, key + " is not in the theme as a plain brush");
            return found.Groups[1].Value;
        }

        /// <summary>Up from wherever the tests were built to, as far as the solution file.</summary>
        private static string RepositoryRoot()
        {
            DirectoryInfo at = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);

            while (at != null && !at.GetFiles("DiscrepancyScanner.sln").Any())
            {
                at = at.Parent;
            }

            Assert.IsNotNull(at, "the tests are not running from inside the repository");
            return at.FullName;
        }
    }
}
