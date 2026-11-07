using System.Windows;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// Choosing which levels get one level's loading plan pairing. Apply copies it; Cancel
    /// copies nothing.
    /// </summary>
    public partial class LoadingPlanCopyWindow : Window
    {
        public LoadingPlanCopyWindow(LoadingPlanCopyViewModel viewModel)
        {
            InitializeComponent();

            viewModel.Close = applied => DialogResult = applied;
            DataContext = viewModel;
        }
    }
}
