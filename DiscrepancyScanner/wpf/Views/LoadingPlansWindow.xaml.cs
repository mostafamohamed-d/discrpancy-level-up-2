using System.Windows;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// Pairing the ETABS levels with the loading plans they are checked against.
    ///
    /// There is no Cancel. Every edit goes straight into the pairing store as it is made, and
    /// the store is written to the settings file when this closes, so nothing here is held
    /// pending: pairing a level is one act and unpairing it is another, both of them visible
    /// in the table the whole time.
    /// </summary>
    public partial class LoadingPlansWindow : Window
    {
        public LoadingPlansWindow(LoadingPlansViewModel viewModel)
        {
            InitializeComponent();

            viewModel.PickWorkbookFile = PickWorkbookFile;
            viewModel.ReportProblem = ReportProblem;
            viewModel.ShowGuessWindow = ShowGuessWindow;
            viewModel.ShowCopyWindow = ShowCopyWindow;
            viewModel.ShowScheduleSheet = ShowScheduleSheet;
            viewModel.ShowLoadPatterns = ShowLoadPatterns;
            DataContext = viewModel;

            Loaded += (sender, e) => searchBox.Focus();
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

        private bool ShowGuessWindow(LoadingPlanGuessViewModel guess)
        {
            LoadingPlanGuessWindow window = new LoadingPlanGuessWindow(guess) { Owner = this };
            return window.ShowDialog() == true;
        }

        private bool ShowCopyWindow(LoadingPlanCopyViewModel copy)
        {
            return new LoadingPlanCopyWindow(copy) { Owner = this }.ShowDialog() == true;
        }

        private bool ShowLoadPatterns(LoadPatternsViewModel patterns)
        {
            return new LoadPatternsWindow(patterns) { Owner = this }.ShowDialog() == true;
        }

        private void ShowScheduleSheet(ScheduleSheetViewModel sheet)
        {
            new ScheduleSheetWindow(sheet) { Owner = this }.ShowDialog();
        }

        private void ReportProblem(string message)
        {
            MessageBox.Show(this, message, "Loading schedule",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void OnDoneClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
