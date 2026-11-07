using System;
using System.Collections.Generic;
using System.Reflection;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// One setting, as a row the settings window can draw and search.
    ///
    /// The row reads and writes the matching field on <see cref="PrefrencesData"/> directly, so
    /// there is one copy of every value and no separate model to keep in step. Which editor is
    /// drawn is chosen off <see cref="EditorKind"/> as a plain string rather than off the type
    /// of the row, because the markup compiler in this project cannot resolve a local type -
    /// see the note in CheckWindow.xaml.
    /// </summary>
    public sealed class SettingItem : ViewModelBase
    {
        /// <summary>A tick box.</summary>
        public const string BoolEditor = "Bool";

        /// <summary>A narrow box taking one number, with its unit beside it.</summary>
        public const string NumberEditor = "Number";

        /// <summary>A full width box taking free text, usually a comma separated list.</summary>
        public const string TextEditor = "Text";

        /// <summary>
        /// A box that takes the next key pressed in it rather than typed text: a keyboard
        /// shortcut. Held as text on the preferences, in the form <see cref="Shortcuts"/> reads.
        /// </summary>
        public const string KeyEditor = "Key";

        // When a change to a setting is actually picked up. A setting read while the models
        // are being read cannot affect the models already in memory, and saying so on the row
        // is the difference between "this does not work" and "import it again".
        public const string OnRevitImport = "Takes effect when the Revit model is imported again";
        public const string OnEtabsImport = "Takes effect when the ETABS model is imported again";
        public const string OnComparison = "Takes effect on the next comparison";

        // The leave-out lists. Every element a longer list drops is already in hand, so the
        // next check drops it; what a shorter list would bring back was never read.
        public const string OnCheckOrRevitImport =
            "A name added takes effect on the next check; a name taken out needs the Revit model imported again";

        // A setting that says what the plan draws out of results that have already been worked
        // out. Nothing to import again and nothing to re-run, so the plan follows the tick.
        public const string OnRedraw = "Takes effect on the plan straight away";

        // A setting about working the window rather than about the plan: a key, which is read
        // afresh every time one is pressed.
        public const string Immediately = "Takes effect straight away";

        private readonly FieldInfo field;
        private readonly PrefrencesData target;
        // Not readonly: what counts as this setting's default changes when the user saves
        // their own, and every row has to be told rather than rebuilt.
        private object defaultValue;
        private readonly Func<string> noteProvider;
        private readonly Action onChanged;

        private string textValue;
        private bool hasError;
        private string note;

        public SettingItem(string category, string key, string label, string description,
                           string applies, string unit = null, string keywords = null,
                           Func<string> noteProvider = null, Action onChanged = null,
                           string editor = null)
        {
            Category = category;
            Key = key;
            Label = label;
            Description = description;
            Applies = applies;
            Unit = unit;
            Keywords = keywords;
            this.noteProvider = noteProvider;
            this.onChanged = onChanged;

            target = PrefrencesData.getPrefrencesData();
            field = typeof(PrefrencesData).GetField(key,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (field == null)
            {
                throw new ArgumentException("PrefrencesData has no field called " + key, nameof(key));
            }

            defaultValue = field.GetValue(UserSettings.Defaults());

            EditorKind = editor
                         ?? (field.FieldType == typeof(bool)
                             ? BoolEditor
                             : field.FieldType == typeof(string) ? TextEditor : NumberEditor);

            textValue = UserSettings.ToText(field.GetValue(target));
            hasError = !Reads(textValue);
            note = noteProvider == null ? null : noteProvider();
        }

        /// <summary>Which group down the side of the window this row sits under.</summary>
        public string Category { get; }

        /// <summary>The field name on <see cref="PrefrencesData"/>, and the key in the settings file.</summary>
        public string Key { get; }

        public string Label { get; }

        /// <summary>What the setting does, in the plain words the old panel used.</summary>
        public string Description { get; }

        /// <summary>
        /// When a change here is picked up: on the next import of one model or the other, or
        /// on the next comparison. One of the three constants at the top of this class.
        /// </summary>
        public string Applies { get; }

        /// <summary>What the number is measured in, drawn beside the box. Null for the rest.</summary>
        public string Unit { get; }

        /// <summary>Extra words the search box matches on, for the terms nobody put in the label.</summary>
        public string Keywords { get; }

        /// <summary>
        /// One of <see cref="BoolEditor"/>, <see cref="NumberEditor"/>, <see cref="TextEditor"/>
        /// and <see cref="KeyEditor"/>.
        /// </summary>
        public string EditorKind { get; }

        public bool IsBool => EditorKind == BoolEditor;
        public bool IsNumber => EditorKind == NumberEditor;
        public bool IsText => EditorKind == TextEditor;
        public bool IsKey => EditorKind == KeyEditor;

        public bool HasDescription => !string.IsNullOrEmpty(Description);
        public bool HasApplies => !string.IsNullOrEmpty(Applies);
        public bool HasUnit => !string.IsNullOrEmpty(Unit);

        #region the value

        /// <summary>The value as typed. Used by the text and the number editors.</summary>
        public string TextValue
        {
            get => textValue;
            set
            {
                if (textValue == value)
                {
                    return;
                }

                textValue = value;
                OnPropertyChanged();

                object converted;
                if (Reads(value)
                    && UserSettings.TryConvert(value ?? string.Empty, field.FieldType, out converted))
                {
                    HasError = false;
                    Store(converted);
                }
                else
                {
                    // The typed value is kept on screen so it can be corrected rather than
                    // snapping back, but nothing is written until it reads as a number.
                    HasError = true;
                }
            }
        }

        /// <summary>The value as a tick. Used by the bool editor.</summary>
        public bool BoolValue
        {
            get => field.FieldType == typeof(bool) && (bool)field.GetValue(target);
            set
            {
                if (BoolValue == value)
                {
                    return;
                }
                Store(value);
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether what is in the box can be kept. Anything a text or number box converts to its
        /// field's type, and for a key box a key - or nothing, which takes the key away.
        /// </summary>
        private bool Reads(string value)
        {
            if (!IsKey || string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            // One key or two in a row - the reading that takes both, not the one-key reading,
            // which turned "A, A" away as not a key at all.
            IList<KeyStroke> keys;
            return Shortcuts.TryParse(value, out keys);
        }

        /// <summary>True while the box holds something that does not read as its setting.</summary>
        public bool HasError
        {
            get => hasError;
            private set => SetProperty(ref hasError, value);
        }

        /// <summary>A live line under the row - what a file is called, how many faults it holds.</summary>
        public string Note
        {
            get => note;
            private set
            {
                if (SetProperty(ref note, value))
                {
                    OnPropertyChanged(nameof(HasNote));
                }
            }
        }

        public bool HasNote => !string.IsNullOrEmpty(Note);

        /// <summary>True while this setting is away from the value the tool ships with.</summary>
        public bool IsChanged => !Equals(field.GetValue(target), defaultValue);

        private void Store(object value)
        {
            field.SetValue(target, value);
            OnPropertyChanged(nameof(IsChanged));
            RefreshNote();
            onChanged?.Invoke();
        }

        #endregion

        /// <summary>Puts the shipped value back and shows it.</summary>
        /// <summary>
        /// Re-reads what this setting's default is, after the user has saved their own or
        /// dropped them. The value itself is untouched - only what it is being measured
        /// against changes.
        /// </summary>
        public void RefreshDefault()
        {
            defaultValue = field.GetValue(UserSettings.Defaults());
            OnPropertyChanged(nameof(IsChanged));
        }

        public void RestoreDefault()
        {
            field.SetValue(target, defaultValue);
            Reread();
            onChanged?.Invoke();
        }

        /// <summary>Re-reads the field, for when something other than this row has changed it.</summary>
        public void Reread()
        {
            textValue = UserSettings.ToText(field.GetValue(target));
            hasError = !Reads(textValue);

            OnPropertyChanged(nameof(TextValue));
            OnPropertyChanged(nameof(BoolValue));
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(IsChanged));
            RefreshNote();
        }

        public void RefreshNote()
        {
            if (noteProvider != null)
            {
                Note = noteProvider();
            }
        }

        /// <summary>Whether the row answers to what has been typed in the search box.</summary>
        public bool Matches(string term)
        {
            if (string.IsNullOrEmpty(term))
            {
                return true;
            }

            return Contains(Label, term)
                   || Contains(Category, term)
                   || Contains(Keywords, term)
                   || Contains(Unit, term)
                   || Contains(Key, term)
                   || Contains(Applies, term)
                   // The value itself, so "where is the 500?" finds the two buffers set to it.
                   || (!IsBool && Contains(textValue, term))
                   || Contains(Description, term);
        }

        private static bool Contains(string haystack, string needle)
        {
            return !string.IsNullOrEmpty(haystack)
                   && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
