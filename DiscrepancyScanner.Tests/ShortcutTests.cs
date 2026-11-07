using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The keys of the check window, as set under Settings: how a key is written, how key
    /// presses - one key, or two one after the other - are matched against them, and what the
    /// settings page says about two actions that get in each other's way.
    /// </summary>
    [TestClass]
    public class ShortcutTests
    {
        private static readonly DateTime Start = new DateTime(2026, 9, 25, 9, 0, 0);

        private PrefrencesData preferences;
        private Dictionary<ShortcutAction, string> asFound;

        [TestInitialize]
        public void KeepTheKeys()
        {
            preferences = PrefrencesData.getPrefrencesData();
            asFound = Shortcuts.All.ToDictionary(action => action, action => Shortcuts.KeyOf(action));
            SetDefaults();
        }

        /// <summary>The preferences are one object for the whole run, so every key goes back.</summary>
        [TestCleanup]
        public void PutTheKeysBack()
        {
            foreach (KeyValuePair<ShortcutAction, string> key in asFound)
            {
                Set(key.Key, key.Value);
            }
        }

        #region the defaults

        /// <summary>
        /// Out of the box the keys are the ones the window has always had: Page Up and Page Down
        /// for the levels and 1 to 6 for the tabs, with R for the raw data.
        /// </summary>
        [TestMethod]
        public void TheDefaultKeysAreTheOnesTheWindowHadBefore()
        {
            Assert.AreEqual(ShortcutAction.LevelUp, PressOne(Key.PageUp));
            Assert.AreEqual(ShortcutAction.LevelDown, PressOne(Key.PageDown));
            Assert.AreEqual(ShortcutAction.Beams, PressOne(Key.D1));
            Assert.AreEqual(ShortcutAction.LineLoads, PressOne(Key.D6));
            Assert.AreEqual(ShortcutAction.RawData, PressOne(Key.R));

            Assert.AreEqual(ShortcutAction.HeightZones, PressOne(Key.D7), "the seventh tab is the height zones");
            Assert.AreEqual(ShortcutAction.SidlC, PressOne(Key.D8), "the eighth is the SIDL_C check");
            Assert.AreEqual(ShortcutAction.SidlCArea, PressOne(Key.D9), "the ninth is the SIDL_C area check");
            Assert.IsNull(PressOne(Key.D0), "and there is no tenth");
        }

        /// <summary>
        /// The digits on the number pad are the same keys as the ones along the top: nobody
        /// setting "4" means only one of the two.
        /// </summary>
        [TestMethod]
        public void TheNumberPadDigitsAreTheSameKeys()
        {
            Assert.AreEqual(ShortcutAction.Slabs, PressOne(Key.NumPad4));
            Assert.AreEqual("4", Shortcuts.TextOf(Key.NumPad4, ModifierKeys.None));
        }

        /// <summary>
        /// A key set without a modifier is not pressed by that key with one held, so Ctrl+1 and
        /// the like stay free for whatever else wants them.
        /// </summary>
        [TestMethod]
        public void AModifierHeldIsADifferentKey()
        {
            Assert.IsNull(PressOne(Key.D1, ModifierKeys.Control));
            Assert.IsNull(PressOne(Key.R, ModifierKeys.Shift));
        }

        #endregion

        #region two keys one after the other

        /// <summary>
        /// A pressed twice accepts and P pressed twice pairs. Once is nothing - held on to, in
        /// case the second is coming - so a stray A never accepts anything.
        /// </summary>
        [TestMethod]
        public void ALetterPressedTwiceAcceptsAndPairs()
        {
            ShortcutMatcher keys = new ShortcutMatcher();
            bool taken;

            Assert.IsNull(keys.Press(Key.A, ModifierKeys.None, false, Start, out taken), "one A is nothing yet");
            Assert.IsTrue(taken, "but it is held on to, not let through");
            Assert.AreEqual(ShortcutAction.Accept,
                            keys.Press(Key.A, ModifierKeys.None, false, Start.AddSeconds(0.4), out taken));

            Assert.IsNull(keys.Press(Key.P, ModifierKeys.None, false, Start.AddSeconds(2), out taken));
            Assert.AreEqual(ShortcutAction.Pair,
                            keys.Press(Key.P, ModifierKeys.None, false, Start.AddSeconds(2.3), out taken));
        }

        /// <summary>
        /// The second key has to follow the first. One that comes after a long pause is a key
        /// of its own - the start of the pair again, not the end of it.
        /// </summary>
        [TestMethod]
        public void TheSecondKeyHasToFollowStraightOn()
        {
            ShortcutMatcher keys = new ShortcutMatcher();
            bool taken;

            keys.Press(Key.A, ModifierKeys.None, false, Start, out taken);
            Assert.IsNull(keys.Press(Key.A, ModifierKeys.None, false, Start.AddSeconds(5), out taken),
                          "too long after: a first A again");
            Assert.AreEqual(ShortcutAction.Accept,
                            keys.Press(Key.A, ModifierKeys.None, false, Start.AddSeconds(5.5), out taken));
        }

        /// <summary>
        /// A key held down repeats, and a repeat is not a second press: held, A would otherwise
        /// accept the moment it repeated. A repeat can still do again what one press does - held
        /// down, Page Down goes on down the levels.
        /// </summary>
        [TestMethod]
        public void AKeyHeldDownIsNotPressedTwice()
        {
            ShortcutMatcher keys = new ShortcutMatcher();
            bool taken;

            keys.Press(Key.A, ModifierKeys.None, false, Start, out taken);
            Assert.IsNull(keys.Press(Key.A, ModifierKeys.None, true, Start.AddSeconds(0.1), out taken));
            Assert.IsNull(keys.Press(Key.A, ModifierKeys.None, true, Start.AddSeconds(0.2), out taken));

            Assert.AreEqual(ShortcutAction.LevelDown,
                            keys.Press(Key.PageDown, ModifierKeys.None, true, Start.AddSeconds(1), out taken));
        }

        /// <summary>
        /// A first key that turns out to go nowhere is dropped, and the key that ended it is read
        /// on its own: P and then 1 still switches the beams.
        /// </summary>
        [TestMethod]
        public void AKeyAfterAFalseStartStillWorks()
        {
            ShortcutMatcher keys = new ShortcutMatcher();
            bool taken;

            keys.Press(Key.P, ModifierKeys.None, false, Start, out taken);
            Assert.AreEqual(ShortcutAction.Beams,
                            keys.Press(Key.D1, ModifierKeys.None, false, Start.AddSeconds(0.3), out taken));

            keys.Press(Key.P, ModifierKeys.None, false, Start.AddSeconds(1), out taken);
            Assert.IsNull(keys.Press(Key.A, ModifierKeys.None, false, Start.AddSeconds(1.3), out taken),
                          "P then A is neither pair, but the A may be the start of one");
            Assert.IsTrue(taken);
            Assert.AreEqual(ShortcutAction.Accept,
                            keys.Press(Key.A, ModifierKeys.None, false, Start.AddSeconds(1.6), out taken));
        }

        /// <summary>A key that is no shortcut and starts none goes through untouched.</summary>
        [TestMethod]
        public void AKeyThatIsNoShortcutIsLetThrough()
        {
            ShortcutMatcher keys = new ShortcutMatcher();
            bool taken;

            Assert.IsNull(keys.Press(Key.Q, ModifierKeys.None, false, Start, out taken));
            Assert.IsFalse(taken);
        }

        /// <summary>Two keys are written with a comma between, and read back the same way.</summary>
        [TestMethod]
        public void TwoKeysAreWrittenWithACommaBetween()
        {
            IList<KeyStroke> strokes;

            Assert.IsTrue(Shortcuts.TryParse("A, A", out strokes));
            Assert.AreEqual(2, strokes.Count);
            Assert.AreEqual("A, A", Shortcuts.TextOf(strokes));

            Assert.IsTrue(Shortcuts.TryParse("ctrl+k,ctrl+c", out strokes));
            Assert.AreEqual("Ctrl+K, Ctrl+C", Shortcuts.TextOf(strokes));

            Assert.IsFalse(Shortcuts.TryParse("A, A, A", out strokes), "two keys at most");
            Assert.IsFalse(Shortcuts.TryParse("A,", out strokes));
            Assert.AreEqual("Comma", Shortcuts.TextOf(Key.OemComma, ModifierKeys.None),
                            "the comma key is spelt out, since a comma joins keys");
        }

        #endregion

        #region changing them

        [TestMethod]
        public void AKeyChangedIsTheKeyThatWorks()
        {
            Set(ShortcutAction.Beams, "Ctrl+B");

            Assert.AreEqual(ShortcutAction.Beams, PressOne(Key.B, ModifierKeys.Control));
            Assert.IsNull(PressOne(Key.D1), "1 does nothing now");
            Assert.IsNull(PressOne(Key.B), "and B alone is not Ctrl+B");
        }

        /// <summary>Accept and pair can be set to anything, one key or two.</summary>
        [TestMethod]
        public void AcceptAndPairCanBeChanged()
        {
            Set(ShortcutAction.Accept, "Ctrl+Enter");
            Set(ShortcutAction.Pair, "G, P");

            Assert.AreEqual(ShortcutAction.Accept, PressOne(Key.Enter, ModifierKeys.Control));

            ShortcutMatcher keys = new ShortcutMatcher();
            bool taken;
            keys.Press(Key.G, ModifierKeys.None, false, Start, out taken);
            Assert.AreEqual(ShortcutAction.Pair,
                            keys.Press(Key.P, ModifierKeys.None, false, Start.AddSeconds(0.5), out taken));

            keys.Press(Key.A, ModifierKeys.None, false, Start.AddSeconds(3), out taken);
            Assert.IsFalse(taken, "A starts nothing any more");
        }

        [TestMethod]
        public void AnActionWithNoKeyIsNeverPressed()
        {
            Set(ShortcutAction.RawData, "");

            Assert.IsNull(PressOne(Key.R));
        }

        /// <summary>
        /// The tooltips name the key as it is set now, so a key just changed is the one they
        /// tell you to press - and a tab with no key does not tell you to press anything.
        /// </summary>
        [TestMethod]
        public void TheTabTooltipsFollowTheKeys()
        {
            FindingTally tally = new FindingTally(() => { });

            Set(ShortcutAction.Columns, "C");
            Set(ShortcutAction.Walls, "");
            tally.ShowKeys();

            StringAssert.Contains(Tab(tally, ViewMode.Columns).ToolTip, "Press C");
            Assert.IsFalse(Tab(tally, ViewMode.Walls).ToolTip.Contains("Press"));
        }

        #endregion

        #region writing a key down

        [TestMethod]
        public void AKeyPressIsWrittenTheWayItReadsBack()
        {
            AssertRoundTrip(Key.R, ModifierKeys.Control | ModifierKeys.Shift, "Ctrl+Shift+R");
            AssertRoundTrip(Key.PageUp, ModifierKeys.None, "Page Up");
            AssertRoundTrip(Key.F5, ModifierKeys.None, "F5");
            AssertRoundTrip(Key.D7, ModifierKeys.Alt, "Alt+7");
            AssertRoundTrip(Key.Add, ModifierKeys.None, "Num Plus");
            AssertRoundTrip(Key.Enter, ModifierKeys.None, "Enter");
        }

        /// <summary>
        /// Page Up is also called Prior by Windows, and Enter is also Return. Whichever of the
        /// two names the key comes in under, it is written the way it is printed on the key.
        /// </summary>
        [TestMethod]
        public void AKeyWithTwoNamesIsWrittenByTheOneOnTheKey()
        {
            Assert.AreEqual("Page Up", Shortcuts.TextOf(Key.Prior, ModifierKeys.None));
            Assert.AreEqual("Enter", Shortcuts.TextOf(Key.Return, ModifierKeys.None));
        }

        /// <summary>A modifier on its own is not a key yet: the box waits for the key it goes with.</summary>
        [TestMethod]
        public void AModifierOnItsOwnIsNotAKey()
        {
            Assert.AreEqual("", Shortcuts.TextOf(Key.LeftCtrl, ModifierKeys.Control));
            Assert.AreEqual("", Shortcuts.TextOf(Key.RightShift, ModifierKeys.Shift));
        }

        /// <summary>
        /// A key typed into the settings file by hand is read however it is spaced or cased,
        /// and what is not a key at all is not read as one.
        /// </summary>
        [TestMethod]
        public void OnlyAKeyReadsAsAKey()
        {
            Key key;
            ModifierKeys modifiers;

            Assert.IsTrue(Shortcuts.TryParse("pageup", out key, out modifiers));
            Assert.AreEqual(Key.PageUp, key);
            Assert.IsTrue(Shortcuts.TryParse(" ctrl + r ", out key, out modifiers));
            Assert.AreEqual(ModifierKeys.Control, modifiers);

            Assert.IsFalse(Shortcuts.TryParse("", out key, out modifiers));
            Assert.IsFalse(Shortcuts.TryParse("12", out key, out modifiers),
                           "two digits are not a key, whatever the enumeration would make of them");
            Assert.IsFalse(Shortcuts.TryParse("-1", out key, out modifiers));
            Assert.IsFalse(Shortcuts.TryParse("A, B", out key, out modifiers), "two keys are not one key");
            Assert.IsFalse(Shortcuts.TryParse("Ctrl+", out key, out modifiers));
            Assert.IsFalse(Shortcuts.TryParse("Hyper+R", out key, out modifiers));
            Assert.IsFalse(Shortcuts.TryParse("Banana", out key, out modifiers));
        }

        #endregion

        #region the settings page

        /// <summary>
        /// Every key is a row on its own page, taking a key rather than text, and a key given to
        /// two actions is said on both of them.
        /// </summary>
        [TestMethod]
        public void TheKeysAreOnTheSettingsPageAndAClashIsSaid()
        {
            SettingsViewModel settings = new SettingsViewModel(() => { }, () => "");
            settings.SelectedCategory = settings.Categories.Single(category => category.Name == "Keyboard shortcuts");

            List<SettingItem> rows = settings.Groups.SelectMany(group => group.Items).ToList();
            Assert.AreEqual(Shortcuts.All.Length, rows.Count);
            Assert.IsTrue(rows.All(row => row.IsKey));
            Assert.IsTrue(rows.All(row => !row.HasNote), "no clashes among the defaults");

            // The two-key defaults were once read as one key, found wanting, and the settings
            // window then refused to close over "A, A" and "P, P".
            Assert.IsFalse(settings.HasErrors, "every default reads: "
                           + string.Join(", ", settings.ErrorLabels));
            SettingItem pair = rows.Single(row => row.Key == "ShortcutPair");
            pair.TextValue = "G, H";
            Assert.IsFalse(pair.HasError, "a pair typed in reads too");
            Assert.AreEqual("G, H", Shortcuts.KeyOf(ShortcutAction.Pair));

            SettingItem walls = rows.Single(row => row.Key == "ShortcutWalls");
            walls.TextValue = "1";

            Assert.AreEqual(ShortcutAction.Beams, PressOne(Key.D1), "the action higher up the page keeps the key");
            StringAssert.Contains(walls.Note, "Beams tab");
            StringAssert.Contains(rows.Single(row => row.Key == "ShortcutBeams").Note, "Walls tab",
                                  "the row that did not change is told as well");

            settings.Revert();
            Assert.AreEqual("3", Shortcuts.KeyOf(ShortcutAction.Walls), "cancel puts the key back");
        }

        /// <summary>
        /// A key that is the start of another's pair is pressed the moment it is pressed, so the
        /// pair can never be finished. Both rows say so.
        /// </summary>
        [TestMethod]
        public void AKeyThatStartsAnothersPairIsSaid()
        {
            Set(ShortcutAction.RawData, "A");

            string raw = Shortcuts.ClashNote(ShortcutAction.RawData, preferences);
            string accept = Shortcuts.ClashNote(ShortcutAction.Accept, preferences);

            StringAssert.Contains(raw, "Accept begins with this key");
            StringAssert.Contains(accept, "begins with the key for Raw data");
            Assert.AreEqual(ShortcutAction.RawData, PressOne(Key.A), "the single key goes first");
            Assert.IsNull(Shortcuts.ClashNote(ShortcutAction.Pair, preferences));
        }

        /// <summary>
        /// A settings file edited by hand can hold something that is not a key. The row says so,
        /// rather than showing it as if it worked.
        /// </summary>
        [TestMethod]
        public void AKeyRowHoldingSomethingThatIsNotAKeySaysSo()
        {
            Set(ShortcutAction.RawData, "Banana");

            SettingsViewModel settings = new SettingsViewModel(() => { }, () => "");
            settings.SearchText = "Raw data";
            SettingItem raw = settings.Groups.SelectMany(group => group.Items)
                                      .Single(row => row.Key == "ShortcutRawData");

            Assert.IsTrue(raw.HasError);

            raw.TextValue = "F9";
            Assert.IsFalse(raw.HasError);
            Assert.AreEqual(ShortcutAction.RawData, PressOne(Key.F9));
        }

        /// <summary>
        /// A key box takes the key pressed in it, and a second key pressed straight after is
        /// added to it - which is how a pair is put in. A third starts again, and so does a
        /// key after a pause.
        /// </summary>
        [TestMethod]
        public void AKeyBoxTakesOneKeyOrTwo()
        {
            ShortcutRecorder recorder = new ShortcutRecorder();
            object box = new object();

            string text = recorder.Press(box, "Page Up", Key.G, ModifierKeys.None, Start);
            Assert.AreEqual("G", text);
            text = recorder.Press(box, text, Key.G, ModifierKeys.None, Start.AddSeconds(0.5));
            Assert.AreEqual("G, G", text);
            text = recorder.Press(box, text, Key.H, ModifierKeys.None, Start.AddSeconds(0.8));
            Assert.AreEqual("H", text, "a third key starts again");

            text = recorder.Press(box, text, Key.J, ModifierKeys.None, Start.AddSeconds(5));
            Assert.AreEqual("J", text, "after a pause, a key of its own");

            Assert.AreEqual("J", recorder.Press(box, text, Key.LeftCtrl, ModifierKeys.Control, Start.AddSeconds(5.2)),
                            "a modifier on its own changes nothing");
            Assert.AreEqual("J, Ctrl+K", recorder.Press(box, text, Key.K, ModifierKeys.Control, Start.AddSeconds(5.4)));

            Assert.AreEqual("L", recorder.Press(new object(), "J", Key.L, ModifierKeys.None, Start.AddSeconds(5.6)),
                            "another box starts afresh");
        }

        #endregion

        /// <summary>One key pressed on its own, with nothing pressed before it.</summary>
        private static ShortcutAction? PressOne(Key key, ModifierKeys modifiers = ModifierKeys.None)
        {
            bool taken;
            return new ShortcutMatcher().Press(key, modifiers, false, Start, out taken);
        }

        private static void AssertRoundTrip(Key key, ModifierKeys modifiers, string written)
        {
            Assert.AreEqual(written, Shortcuts.TextOf(key, modifiers));

            Key readKey;
            ModifierKeys readModifiers;
            Assert.IsTrue(Shortcuts.TryParse(written, out readKey, out readModifiers), written);
            Assert.AreEqual(key, readKey, written);
            Assert.AreEqual(modifiers, readModifiers, written);
        }

        private void SetDefaults()
        {
            PrefrencesData defaults = UserSettings.Defaults();
            foreach (ShortcutAction action in Shortcuts.All)
            {
                Set(action, Shortcuts.KeyOf(action, defaults));
            }
        }

        private void Set(ShortcutAction action, string key)
        {
            typeof(PrefrencesData).GetField(Shortcuts.FieldOf(action)).SetValue(preferences, key);
        }

        private static ScopeTab Tab(FindingTally tally, ViewMode mode)
        {
            return tally.Tabs.Single(tab => tab.Mode == mode);
        }
    }
}
