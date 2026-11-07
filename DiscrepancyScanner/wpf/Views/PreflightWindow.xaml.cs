using System.Linq;
using System.Windows;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// The sheet that comes up when the check is asked for before it is ready.
    ///
    /// It closes with one of three answers: run, do the missing things and then run, or leave
    /// it. The dialog result carries the first two; the caller cannot tell them apart, and does
    /// not need to - by the time the sheet closes the fixing has already happened.
    /// </summary>
    public partial class PreflightWindow : Window
    {
        private readonly PreflightReport report;

        public PreflightWindow(PreflightReport report)
        {
            InitializeComponent();
            this.report = report;
            DataContext = report;
        }

        /// <summary>
        /// Does every blocker that can do itself, in the order they are listed, and then runs.
        ///
        /// In order rather than all at once because the order is the dependency: the Revit
        /// import is built against the ETABS story list, so reading Revit before ETABS would
        /// leave every Revit element on no level at all.
        /// </summary>
        private void OnFixEverythingClick(object sender, RoutedEventArgs e)
        {
            foreach (PreflightItem item in report.Items
                                                 .Where(i => i.Level == PreflightLevel.Blocker
                                                             && i.FixesItself && i.Action != null)
                                                 .ToList())
            {
                if (item.Action.CanExecute(null))
                {
                    item.Action.Execute(null);
                }
            }

            DialogResult = true;
        }

        private void OnRunAnywayClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void OnNotNowClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
