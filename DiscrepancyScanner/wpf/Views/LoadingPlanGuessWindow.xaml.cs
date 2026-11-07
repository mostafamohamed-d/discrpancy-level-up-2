using System.Windows;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// Choosing which levels "guess by name" pairs, what a loading plan view is called, and
    /// which schedule sheets the guessed levels read. Apply pairs them; Cancel pairs nothing.
    /// </summary>
    public partial class LoadingPlanGuessWindow : Window
    {
        public LoadingPlanGuessWindow(LoadingPlanGuessViewModel viewModel)
        {
            InitializeComponent();

            viewModel.PickWorkbookFile = PickWorkbookFile;
            viewModel.ReportProblem = ReportProblem;
            viewModel.Close = applied => DialogResult = applied;
            DataContext = viewModel;
        }

        private string PickWorkbookFile()
        {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Pick the loading schedule workbook",
                Filter = "Excel workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                CheckFileExists = true
            };

            return dialog.ShowDialog(this) == true ? dialog.FileName : null;
        }

        private void ReportProblem(string message)
        {
            MessageBox.Show(this, message, "Loading schedule",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
