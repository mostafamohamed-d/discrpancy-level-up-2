using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// Which of the open models to read.
    ///
    /// Asked rather than assumed. Attaching by program name alone takes whichever instance the
    /// running object table hands over, and with a tower open in one ETABS and a podium in
    /// another that is a choice being made without the user seeing it made - and a whole run
    /// against the wrong model looks exactly like a run against the right one until the
    /// findings stop making sense.
    ///
    /// Shown even when only one model is open, because the point is as much confirming which
    /// model is about to be read as choosing between two; the single row is preselected, so it
    /// costs one Enter.
    /// </summary>
    public partial class ModelChooserWindow : Window
    {
        public ModelChooserWindow()
        {
            InitializeComponent();
            Refresh();
        }

        /// <summary>The model the user picked, or null where they cancelled.</summary>
        public CsiInstance Chosen { get; private set; }

        private void Refresh()
        {
            string problem;
            IList<CsiInstance> running = CsiProducts.RunningInstances(out problem);

            instanceList.ItemsSource = running;
            instanceList.SelectedItem = running.FirstOrDefault();

            problemText.Text = problem ?? "";
            problemText.Visibility = string.IsNullOrEmpty(problem) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            Refresh();
        }

        private void OnChooseClick(object sender, RoutedEventArgs e)
        {
            Choose();
        }

        private void OnListDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Choose();
        }

        private void Choose()
        {
            CsiInstance picked = instanceList.SelectedItem as CsiInstance;
            if (picked == null)
            {
                return;
            }

            Chosen = picked;
            DialogResult = true;
        }
    }
}
