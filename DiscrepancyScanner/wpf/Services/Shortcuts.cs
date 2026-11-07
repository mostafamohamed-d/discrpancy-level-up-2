using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>What a key in the check window can be set to do.</summary>
    public enum ShortcutAction
    {
        LevelUp,
        LevelDown,
        Beams,
        Columns,
        Walls,
        Slabs,
        SlabLoads,
        LineLoads,
        HeightZones,
        SidlC,
        SidlCArea,
        RawData,
        Accept,
        Pair
    }

    /// <summary>
    /// One key as pressed: the key and the modifiers held with it. A number pad digit is the
    /// digit it is, and the Windows key is never part of it.
    /// </summary>
    public struct KeyStroke : IEquatable<KeyStroke>
    {
        public KeyStroke(Key key, ModifierKeys modifiers)
        {
            Key = Shortcuts.Normalised(key);
            Modifiers = modifiers & ~ModifierKeys.Windows;
        }

        public Key Key { get; }
        public ModifierKeys Modifiers { get; }

        public bool Equals(KeyStroke other)
        {
            return Key == other.Key && Modifiers == other.Modifiers;
        }

        public override bool Equals(object obj)
        {
            return obj is KeyStroke && Equals((KeyStroke)obj);
        }

        public override int GetHashCode()
        {
            return (int)Key * 16 + (int)Modifiers;
        }

        public override string ToString()
        {
            return Shortcuts.TextOf(Key, Modifiers);
        }
    }

    /// <summary>
    /// The keys of the check window, as they are set under Settings, and how key presses are
    /// matched against them.
    ///
    /// A shortcut is one key, or two pressed one after the other - "A, A" accepts, the way a
    /// letter pressed twice is hard to do by accident and easy to do on purpose. It is held as
    /// the text it is shown as - "Page Up", "1", "Ctrl+Shift+R", "P, P" - on the preferences,
    /// one field per action, so it is saved, restored and put back to its default by the same
    /// machinery as every other setting. The digits along the top of the keyboard and the ones
    /// on the number pad are the same key here: nobody setting "4" means only one of the two.
    /// </summary>
    public static class Shortcuts
    {
        /// <summary>Every action, in the order the settings page lists them.</summary>
        public static readonly ShortcutAction[] All =
        {
            ShortcutAction.LevelUp, ShortcutAction.LevelDown,
            ShortcutAction.Beams, ShortcutAction.Columns, ShortcutAction.Walls,
            ShortcutAction.Slabs, ShortcutAction.SlabLoads, ShortcutAction.LineLoads,
            ShortcutAction.HeightZones, ShortcutAction.SidlC, ShortcutAction.SidlCArea,
            ShortcutAction.RawData, ShortcutAction.Accept, ShortcutAction.Pair
        };

        /// <summary>The most keys one shortcut is pressed as.</summary>
        public const int MostKeys = 2;

        /// <summary>
        /// How long the second key of a two-key shortcut may be after the first. Longer, and it
        /// is a key of its own - which is also what taking a key back to type it again relies on.
        /// </summary>
        public static readonly TimeSpan Pause = TimeSpan.FromSeconds(1.5);

        /// <summary>The field on <see cref="PrefrencesData"/> that holds an action's key.</summary>
        public static string FieldOf(ShortcutAction action)
        {
            return "Shortcut" + action;
        }

        /// <summary>What an action is called on the settings page and in a clash.</summary>
        public static string NameOf(ShortcutAction action)
        {
            switch (action)
            {
                case ShortcutAction.LevelUp: return "Level above";
                case ShortcutAction.LevelDown: return "Level below";
                case ShortcutAction.Beams: return "Beams tab";
                case ShortcutAction.Columns: return "Columns tab";
                case ShortcutAction.Walls: return "Walls tab";
                case ShortcutAction.Slabs: return "Slabs tab";
                case ShortcutAction.SlabLoads: return "Slab loads tab";
                case ShortcutAction.LineLoads: return "Line loads tab";
                case ShortcutAction.HeightZones: return "Height zones tab";
                case ShortcutAction.SidlC: return "SIDL_C Line tab";
                case ShortcutAction.SidlCArea: return "SIDL_C Area tab";
                case ShortcutAction.RawData: return "Raw data";
                case ShortcutAction.Accept: return "Accept";
                default: return "Pair";
            }
        }

        /// <summary>The tab an action switches, or null for the actions that are not a tab.</summary>
        public static ViewMode? TabOf(ShortcutAction action)
        {
            switch (action)
            {
                case ShortcutAction.Beams: return ViewMode.Beams;
                case ShortcutAction.Columns: return ViewMode.Columns;
                case ShortcutAction.Walls: return ViewMode.Walls;
                case ShortcutAction.Slabs: return ViewMode.Slabs;
                case ShortcutAction.SlabLoads: return ViewMode.SlabLoads;
                case ShortcutAction.LineLoads: return ViewMode.LineLoads;
                case ShortcutAction.HeightZones: return ViewMode.HeightZones;
                case ShortcutAction.SidlC: return ViewMode.SidlC;
                case ShortcutAction.SidlCArea: return ViewMode.SidlCArea;
                default: return null;
            }
        }

        /// <summary>The action that switches a tab.</summary>
        public static ShortcutAction ActionOf(ViewMode tab)
        {
            return All.First(action => TabOf(action) == tab);
        }

        /// <summary>The key an action is set to, as written; empty where it has none.</summary>
        public static string KeyOf(ShortcutAction action)
        {
            return KeyOf(action, PrefrencesData.getPrefrencesData());
        }

        public static string KeyOf(ShortcutAction action, PrefrencesData preferences)
        {
            object value = typeof(PrefrencesData).GetField(FieldOf(action))?.GetValue(preferences);
            return (value as string ?? "").Trim();
        }

        #region matching

        /// <summary>
        /// What the keys pressed so far amount to: the action they complete, or null - and in
        /// that case whether they are the start of one, which is worth waiting on.
        ///
        /// The first action in <see cref="All"/> wins where two are set to the same keys. A
        /// shortcut that is the start of another wins over it, since it is complete the moment
        /// it is pressed and there is no telling whether a second key was coming. The settings
        /// page warns about both.
        /// </summary>
        public static ShortcutAction? Find(IList<KeyStroke> pressed, PrefrencesData preferences, out bool begun)
        {
            begun = false;
            if (pressed == null || pressed.Count == 0)
            {
                return null;
            }

            foreach (ShortcutAction action in All)
            {
                IList<KeyStroke> wanted;
                if (!TryParse(KeyOf(action, preferences), out wanted))
                {
                    continue;
                }

                if (wanted.SequenceEqual(pressed))
                {
                    return action;
                }
                if (wanted.Count > pressed.Count && wanted.Take(pressed.Count).SequenceEqual(pressed))
                {
                    begun = true;
                }
            }
            return null;
        }

        /// <summary>The other actions that get in this one's way, or that it gets in the way of.</summary>
        public static IList<ShortcutAction> ClashesWith(ShortcutAction action, PrefrencesData preferences)
        {
            IList<KeyStroke> mine;
            if (!TryParse(KeyOf(action, preferences), out mine))
            {
                return new List<ShortcutAction>();
            }

            return All.Where(other => other != action && Relation(mine, KeyOf(other, preferences)) != Overlap.None)
                      .ToList();
        }

        /// <summary>
        /// What the settings page says under an action whose keys get in another's way, or null
        /// where they get in nobody's.
        /// </summary>
        public static string ClashNote(ShortcutAction action, PrefrencesData preferences)
        {
            IList<KeyStroke> mine;
            if (!TryParse(KeyOf(action, preferences), out mine))
            {
                return null;
            }

            List<string> same = new List<string>();
            List<string> longer = new List<string>();
            List<string> shorter = new List<string>();

            foreach (ShortcutAction other in All.Where(other => other != action))
            {
                switch (Relation(mine, KeyOf(other, preferences)))
                {
                    case Overlap.Same: same.Add(NameOf(other)); break;
                    case Overlap.StartsTheOther: longer.Add(NameOf(other)); break;
                    case Overlap.StartsWithTheOther: shorter.Add(NameOf(other)); break;
                }
            }

            List<string> notes = new List<string>();
            if (same.Count > 0)
            {
                notes.Add("Also set for " + string.Join(", ", same)
                          + ". The key does whichever of them is higher up this page.");
            }
            if (longer.Count > 0)
            {
                notes.Add(string.Join(", ", longer) + " begins with this key, and this goes first, so "
                          + (longer.Count == 1 ? "it never gets its turn." : "they never get their turn."));
            }
            if (shorter.Count > 0)
            {
                notes.Add("This begins with the key for " + string.Join(", ", shorter)
                          + ", which goes first, so this never gets its turn.");
            }

            return notes.Count == 0 ? null : string.Join(" ", notes);
        }

        private enum Overlap
        {
            None,
            Same,
            StartsTheOther,
            StartsWithTheOther
        }

        private static Overlap Relation(IList<KeyStroke> mine, string otherText)
        {
            IList<KeyStroke> other;
            if (!TryParse(otherText, out other))
            {
                return Overlap.None;
            }

            if (mine.SequenceEqual(other))
            {
                return Overlap.Same;
            }
            if (other.Count > mine.Count && other.Take(mine.Count).SequenceEqual(mine))
            {
                return Overlap.StartsTheOther;
            }
            if (mine.Count > other.Count && mine.Take(other.Count).SequenceEqual(other))
            {
                return Overlap.StartsWithTheOther;
            }
            return Overlap.None;
        }

        #endregion

        #region reading and writing

        /// <summary>
        /// Reads a shortcut as written: one key, or up to <see cref="MostKeys"/> of them one
        /// after the other with a comma between - "A, A". Each key is its modifiers first,
        /// joined with "+", then the key itself - "Ctrl+Shift+R", "Page Up", "F5", "7". Case and
        /// spaces do not matter, so "pageup" reads as well as "Page Up". False for empty text and
        /// for anything that is not keys.
        /// </summary>
        public static bool TryParse(string text, out IList<KeyStroke> strokes)
        {
            strokes = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            // No key is named with a comma in it - the comma key is "Comma" - so every comma is
            // a joint between one key and the next.
            string[] parts = text.Split(',');
            if (parts.Length > MostKeys)
            {
                return false;
            }

            List<KeyStroke> read = new List<KeyStroke>();
            foreach (string part in parts)
            {
                Key key;
                ModifierKeys modifiers;
                if (!TryStroke(part, out key, out modifiers))
                {
                    return false;
                }
                read.Add(new KeyStroke(key, modifiers));
            }

            strokes = read;
            return true;
        }

        /// <summary>The same, for a shortcut that is one key: false for two keys or none.</summary>
        public static bool TryParse(string text, out Key key, out ModifierKeys modifiers)
        {
            key = Key.None;
            modifiers = ModifierKeys.None;

            IList<KeyStroke> strokes;
            if (!TryParse(text, out strokes) || strokes.Count != 1)
            {
                return false;
            }

            key = strokes[0].Key;
            modifiers = strokes[0].Modifiers;
            return true;
        }

        /// <summary>
        /// How a key press is written: the modifiers held and then the key, in the form
        /// <see cref="TryParse(string, out IList{KeyStroke})"/> reads back. Empty for a modifier
        /// pressed on its own, which is not a key yet.
        /// </summary>
        public static string TextOf(Key key, ModifierKeys modifiers)
        {
            if (IsModifier(key) || key == Key.None)
            {
                return "";
            }

            List<string> parts = new List<string>();
            if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
            parts.Add(NameOfKey(Normalised(key)));
            return string.Join("+", parts);
        }

        /// <summary>Keys one after the other, written the way they are read back: "A, A".</summary>
        public static string TextOf(IEnumerable<KeyStroke> strokes)
        {
            return string.Join(", ", strokes.Select(stroke => TextOf(stroke.Key, stroke.Modifiers)));
        }

        /// <summary>A shortcut as it reads in a tooltip, tidied the way it is written; empty where there is none.</summary>
        public static string Describe(string text)
        {
            IList<KeyStroke> strokes;
            return TryParse(text, out strokes) ? TextOf(strokes) : "";
        }

        /// <summary>The ones that only ever change another key.</summary>
        public static bool IsModifier(Key key)
        {
            return key == Key.LeftCtrl || key == Key.RightCtrl
                   || key == Key.LeftShift || key == Key.RightShift
                   || key == Key.LeftAlt || key == Key.RightAlt
                   || key == Key.LWin || key == Key.RWin
                   || key == Key.System;
        }

        /// <summary>A number pad digit as the digit it is.</summary>
        internal static Key Normalised(Key key)
        {
            if (key >= Key.NumPad0 && key <= Key.NumPad9)
            {
                return Key.D0 + (key - Key.NumPad0);
            }
            return key;
        }

        /// <summary>One key as written: "Ctrl+Shift+R".</summary>
        private static bool TryStroke(string text, out Key key, out ModifierKeys modifiers)
        {
            key = Key.None;
            modifiers = ModifierKeys.None;

            // No key is named with a plus sign in it either - the one on the number pad is
            // "Num Plus" - so every plus is a joint between a modifier and what follows it.
            string[] parts = text.Split('+').Select(part => part.Trim()).ToArray();

            for (int i = 0; i < parts.Length - 1; i++)
            {
                ModifierKeys modifier;
                if (!TryModifier(parts[i], out modifier))
                {
                    return false;
                }
                modifiers |= modifier;
            }

            return TryKey(parts[parts.Length - 1], out key);
        }

        private static bool TryModifier(string text, out ModifierKeys modifier)
        {
            switch (text.Replace(" ", "").ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifier = ModifierKeys.Control;
                    return true;
                case "shift":
                    modifier = ModifierKeys.Shift;
                    return true;
                case "alt":
                    modifier = ModifierKeys.Alt;
                    return true;
                default:
                    modifier = ModifierKeys.None;
                    return false;
            }
        }

        // The keys whose name in the Key enumeration is not the name on the key, and the ones
        // that share a value with another name there - Prior is Page Up, Return is Enter - so
        // asking the enumeration for the name would give whichever one it happened to pick. The
        // punctuation is spelt out, because a comma and a plus sign are what join keys together.
        private static readonly Dictionary<Key, string> Names = new Dictionary<Key, string>
        {
            { Key.PageUp, "Page Up" },
            { Key.PageDown, "Page Down" },
            { Key.Enter, "Enter" },
            { Key.Back, "Backspace" },
            { Key.CapsLock, "Caps Lock" },
            { Key.Space, "Space" },
            { Key.OemPlus, "Equals" },
            { Key.OemMinus, "Minus" },
            { Key.OemComma, "Comma" },
            { Key.OemPeriod, "Period" },
            { Key.Add, "Num Plus" },
            { Key.Subtract, "Num Minus" },
            { Key.Multiply, "Num Times" },
            { Key.Divide, "Num Divide" }
        };

        private static string NameOfKey(Key key)
        {
            if (key >= Key.D0 && key <= Key.D9)
            {
                return ((int)(key - Key.D0)).ToString();
            }

            string name;
            return Names.TryGetValue(key, out name) ? name : key.ToString();
        }

        private static bool TryKey(string text, out Key key)
        {
            key = Key.None;
            string squashed = text.Replace(" ", "");
            if (squashed.Length == 0)
            {
                return false;
            }

            if (squashed.All(char.IsDigit))
            {
                // One digit is that digit's key. More than one is not a key at all, and must
                // not reach the enumeration below, which would read "12" as its twelfth entry.
                if (squashed.Length != 1)
                {
                    return false;
                }
                key = Key.D0 + (squashed[0] - '0');
                return true;
            }

            foreach (KeyValuePair<Key, string> name in Names)
            {
                if (string.Equals(name.Value.Replace(" ", ""), squashed, StringComparison.OrdinalIgnoreCase))
                {
                    key = name.Key;
                    return true;
                }
            }

            // Only a name the enumeration has: it would otherwise take "-1" and hand back a
            // value that is no key at all.
            Key parsed;
            if (Enum.TryParse(squashed, true, out parsed) && Enum.IsDefined(typeof(Key), parsed)
                && parsed != Key.None && !IsModifier(parsed))
            {
                key = Normalised(parsed);
                return true;
            }
            return false;
        }

        #endregion
    }

    /// <summary>
    /// Follows the keys pressed in the check window and says when they add up to a shortcut.
    ///
    /// A key that could be the first of a two-key shortcut is held on to rather than let
    /// through - pressing A once does nothing, pressing it again accepts. One that turns out
    /// not to go anywhere is dropped, and the key that ended it is read again on its own, so
    /// P followed by 1 still switches the beams. The second key has to come within
    /// <see cref="Shortcuts.Pause"/> of the first.
    /// </summary>
    public sealed class ShortcutMatcher
    {
        private readonly Func<PrefrencesData> preferences;
        private readonly List<KeyStroke> pressed = new List<KeyStroke>();
        private DateTime lastPressed;

        public ShortcutMatcher(Func<PrefrencesData> preferences = null)
        {
            this.preferences = preferences ?? PrefrencesData.getPrefrencesData;
        }

        /// <summary>
        /// One key pressed. Answers the action it completes, or null; and says whether the key
        /// was taken - a whole shortcut, or the start of one - so it goes no further.
        ///
        /// A key held down repeats, and a repeat can do again what one press did - hold Page
        /// Down to go down a level at a time - but it never makes a pair: held down, A would
        /// otherwise accept the moment it repeated.
        /// </summary>
        public ShortcutAction? Press(Key key, ModifierKeys modifiers, bool isRepeat, DateTime at, out bool taken)
        {
            taken = false;
            if (Shortcuts.IsModifier(key) || key == Key.None)
            {
                return null;
            }

            KeyStroke stroke = new KeyStroke(key, modifiers);
            PrefrencesData read = preferences();
            bool begun;

            if (isRepeat)
            {
                pressed.Clear();
                ShortcutAction? again = Shortcuts.Find(new[] { stroke }, read, out begun);
                taken = again != null || begun;
                return again;
            }

            if (pressed.Count > 0 && at - lastPressed > Shortcuts.Pause)
            {
                pressed.Clear();
            }
            pressed.Add(stroke);
            lastPressed = at;

            ShortcutAction? done = Shortcuts.Find(pressed, read, out begun);
            if (done == null && !begun && pressed.Count > 1)
            {
                // What was begun went nowhere. The key that ended it may start something of its
                // own, or be a whole shortcut by itself.
                pressed.Clear();
                pressed.Add(stroke);
                done = Shortcuts.Find(pressed, read, out begun);
            }

            if (done != null)
            {
                pressed.Clear();
                taken = true;
                return done;
            }

            if (begun)
            {
                taken = true;
                return null;
            }

            pressed.Clear();
            return null;
        }

        /// <summary>Forgets a key held on to, for when the window loses the keyboard.</summary>
        public void Reset()
        {
            pressed.Clear();
        }
    }

    /// <summary>
    /// What a shortcut box on the settings page is set to as keys are pressed in it: the key,
    /// or - where a second follows within <see cref="Shortcuts.Pause"/> - the two of them, which
    /// is how a pair like "A, A" is put in. A third key starts again.
    /// </summary>
    public sealed class ShortcutRecorder
    {
        private object lastBox;
        private DateTime lastPressed;
        private bool waitingForSecond;

        /// <summary>The box's text after this key; unchanged for a modifier pressed on its own.</summary>
        public string Press(object box, string current, Key key, ModifierKeys modifiers, DateTime at)
        {
            string stroke = Shortcuts.TextOf(key, modifiers);
            if (stroke.Length == 0)
            {
                return current;
            }

            bool second = waitingForSecond && ReferenceEquals(box, lastBox)
                          && at - lastPressed <= Shortcuts.Pause;

            lastBox = box;
            lastPressed = at;
            waitingForSecond = !second;

            return second ? current + ", " + stroke : stroke;
        }

        /// <summary>Starts again, for a key taken away with Backspace.</summary>
        public void Reset()
        {
            waitingForSecond = false;
        }
    }
}
