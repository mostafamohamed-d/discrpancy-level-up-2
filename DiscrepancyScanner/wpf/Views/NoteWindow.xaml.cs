using System.Windows;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// A note on one element. Small enough to be a dialog rather than a panel: writing one is
    /// something you stop to do, and it does not want the plan taken away from behind it.
    /// </summary>
    public partial class NoteWindow : Window
    {
        public NoteWindow(string about, string existing)
        {
            InitializeComponent();

            aboutText.Text = about ?? "";
            noteBox.Text = existing ?? "";

            // Ready to type, with the caret past whatever is already there rather than
            // selecting it - an existing note is usually being added to, not replaced.
            Loaded += (sender, e) =>
            {
                noteBox.Focus();
                noteBox.CaretIndex = noteBox.Text.Length;
            };
        }

        /// <summary>What was typed. Empty means the note is being taken back.</summary>
        public string Note { get; private set; } = "";

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            Note = noteBox.Text ?? "";
            DialogResult = true;
        }
    }
}
