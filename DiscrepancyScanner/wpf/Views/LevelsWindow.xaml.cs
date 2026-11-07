using System.Windows;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// Which levels to copy height zones to: every other level, top of the building first, with
    /// the ones that already have zones saying so. Enter backs out, as it does in the forget
    /// dialog - nothing is copied by a key pressed out of habit.
    /// </summary>
    public partial class LevelsWindow : Window
    {
        public LevelsWindow(LevelChoices choices)
        {
            InitializeComponent();
            DataContext = choices;
        }

        private void OnCopyClick(object sender, RoutedEventArgs e)
        {
            if (((LevelChoices)DataContext).CanApply)
            {
                DialogResult = true;
            }
        }
    }
}
