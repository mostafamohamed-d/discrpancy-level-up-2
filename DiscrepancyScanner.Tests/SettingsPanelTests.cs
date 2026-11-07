using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The settings panel is built from a list of field names typed out by hand, and both ways
    /// it can be wrong throw only when the window is opened: a key that names no field on the
    /// preferences, and a category with no blurb written for it. Neither shows up in a build,
    /// so a row added with a typo in it ships as a window that will not open.
    /// </summary>
    [TestClass]
    public class SettingsPanelTests
    {
        [TestMethod]
        public void EverySettingRowNamesARealPreferenceAndACategoryWithABlurb()
        {
            SettingsViewModel settings = new SettingsViewModel(() => { }, () => "");

            Assert.IsTrue(settings.Categories.Count > 0, "the panel should have built some categories");
        }

        /// <summary>
        /// The line load rows in particular, because they are the newest and the whole check is
        /// unreachable without them.
        /// </summary>
        [TestMethod]
        public void TheLineLoadSettingsAreAllOnThePanel()
        {
            SettingsViewModel settings = new SettingsViewModel(() => { }, () => "");

            Assert.IsTrue(settings.Categories.Any(category => category.Name == "Line loads"),
                          "the line load settings should have a page of their own rather than "
                          + "sitting under the slab loads");
        }

        /// <summary>
        /// Every preference the line load check reads has to be editable, or it is a number
        /// nobody can change without a rebuild.
        /// </summary>
        [TestMethod]
        public void EveryLineLoadPreferenceHasARowOnThePanel()
        {
            SettingsViewModel settings = new SettingsViewModel(() => { }, () => "");

            string[] wanted =
            {
                "RunLineLoadCheck", "LineLoadStyleNamePatterns", "LineLoadPatternNames",
                "LineLoadHeightReduction", "LineLoadRoundUpTo",
                "FullHeightLineLoadNames", "LineLoadTolerance",
                "LineLoadLocationTolerance", "LineLoadSearchRadius", "LineLoadAngleTolerance",
                "LineLoadMinimumIssueLength", "LineLoadMinimumCountedLength",
                "LineLoadCheckInSteps", "LineLoadStepLength"
            };

            // Through the search, because Groups otherwise holds only the page on show. The
            // category name is one of the things a row is matched on, so this is every line
            // load row and nothing else.
            settings.SearchText = "Line loads";

            string[] onPanel = settings.Groups.SelectMany(group => group.Items)
                                              .Select(item => item.Key)
                                              .ToArray();

            foreach (string key in wanted)
            {
                Assert.IsTrue(onPanel.Contains(key), key + " has no row on the settings panel");
            }
        }
    }
}
