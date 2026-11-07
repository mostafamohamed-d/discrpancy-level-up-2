using System.Windows;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// Which ETABS load pattern is which load of the loading plan, and what the schedule calls
    /// each of them. Edits copies; OK hands them back to be written into the settings.
    /// </summary>
    public partial class LoadPatternsWindow : Window
    {
        public LoadPatternsWindow(LoadPatternsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
