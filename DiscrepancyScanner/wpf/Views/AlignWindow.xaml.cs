using System;
using System.Windows;
using System.Windows.Input;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// The alignment panel, in a window of its own and shown without a dialog.
    ///
    /// It stays open beside the check window rather than over it, because that is where the
    /// two models are drawn and where the points are now picked: the window is owned by the
    /// check window, so it floats above the plan while the plan takes the clicks.
    /// </summary>
    public partial class AlignWindow : Window
    {
        private readonly AlignmentViewModel viewModel;

        public AlignWindow(AlignmentViewModel viewModel)
        {
            InitializeComponent();

            this.viewModel = viewModel;
            DataContext = viewModel;

            // Picking in Revit needs both of these out of the way, not just the plan behind:
            // a prompt in Revit answered through a window on top of it is answered nowhere.
            viewModel.HideShell += Hide;
            viewModel.ShowShell += Show;

            // The stories and levels are read as the window comes up rather than once: an
            // ETABS model imported, or a level added in Revit, since the last time it was open
            // has to be in the lists.
            viewModel.RefreshHeightChoices();
            Activated += OnActivated;

            Closed += OnClosed;
        }

        private void OnActivated(object sender, EventArgs e)
        {
            // Coming back from a pick in Revit is an activation too, and nothing about the
            // stories or levels can have changed in the middle of one.
            if (viewModel.CanPick)
            {
                viewModel.RefreshHeightChoicesIfStale();
            }
        }

        /// <summary>
        /// Escape calls off a pick that is running, and closes the window when none is. The
        /// plan answers escape as well, but only while it has the keyboard, and the person
        /// who just pressed a button in here has it instead.
        /// </summary>
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            if (e.Key != Key.Escape)
            {
                return;
            }

            if (viewModel.CanPick)
            {
                Close();
            }
            else
            {
                viewModel.StopPlanPick?.Invoke();
            }

            e.Handled = true;
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            // A pick left running would go on prompting over a plan with nobody to answer it.
            viewModel.StopPlanPick?.Invoke();

            // The view model outlives this window, and a closed window cannot be shown again,
            // so it must stop being one of the windows a Revit pick hides and brings back.
            viewModel.HideShell -= Hide;
            viewModel.ShowShell -= Show;
            Activated -= OnActivated;
        }
    }
}
