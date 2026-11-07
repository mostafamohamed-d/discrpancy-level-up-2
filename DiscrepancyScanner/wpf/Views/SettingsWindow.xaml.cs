using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// Every setting the check has, grouped and searchable, in a window of its own rather than
    /// stacked down the side of the plan where they used to be.
    ///
    /// The rows write through to the live preferences as they are typed, so what makes Cancel
    /// mean cancel is the snapshot the view model takes on the way in. Anything that closes the
    /// window other than OK - the Cancel button, escape, the cross - puts that snapshot back.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly SettingsViewModel viewModel;

        public SettingsWindow(SettingsViewModel viewModel)
        {
            InitializeComponent();

            this.viewModel = viewModel;
            DataContext = viewModel;

            // Keeping the settings on the way out, because leaving for the alignment window
            // is not cancelling.
            viewModel.RequestClose = () => DialogResult = true;

            Loaded += (sender, e) => searchBox.Focus();
            Closing += OnClosing;
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            if (viewModel.HasErrors)
            {
                List<string> labels = viewModel.ErrorLabels.ToList();
                MessageBox.Show(this,
                    "These do not read as what they are meant to be - a number, or a key - so the "
                    + "settings cannot be kept yet:"
                    + Environment.NewLine + Environment.NewLine
                    + string.Join(Environment.NewLine, labels),
                    "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        }

        /// <summary>
        /// A key pressed in a shortcut box is the key that box is set to: written down as the
        /// key with the modifiers held, and nothing typed into it. A second key pressed straight
        /// after the first is added to it, which is how "A, A" is put in.
        ///
        /// Tab and Escape are left to do what they always do, moving on and cancelling the
        /// window, so they cannot be set as a shortcut - which is as well, since in the check
        /// window they are taken already. A modifier on its own waits for the key it goes with.
        /// </summary>
        private void OnShortcutBoxKeyDown(object sender, KeyEventArgs e)
        {
            SettingItem item = (sender as FrameworkElement)?.DataContext as SettingItem;
            if (item == null)
            {
                return;
            }

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            ModifierKeys held = Keyboard.Modifiers & ~ModifierKeys.Windows;

            if ((key == Key.Tab || key == Key.Escape) && held == ModifierKeys.None
                || key == Key.Tab && held == ModifierKeys.Shift)
            {
                return;
            }

            e.Handled = true;

            // A key held down repeats, and a repeat is not a second key: held, A would be "A, A".
            if (Shortcuts.IsModifier(key) || e.IsRepeat)
            {
                return;
            }

            if ((key == Key.Back || key == Key.Delete) && held == ModifierKeys.None)
            {
                item.TextValue = "";
                recorder.Reset();
                return;
            }

            item.TextValue = recorder.Press(sender, item.TextValue, key, held, DateTime.Now);
        }

        /// <summary>Puts a second key after the first, where it follows straight on - "A, A".</summary>
        private readonly ShortcutRecorder recorder = new ShortcutRecorder();

        /// <summary>
        /// Anything but OK puts the settings back as they were found. Doing it here rather than
        /// on the Cancel button covers escape and the cross as well, which is what a user
        /// closing a settings window expects either way.
        /// </summary>
        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (DialogResult != true)
            {
                viewModel.Revert();
            }
        }
    }
}
