using System.Windows;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// Asks which of the things decided by hand on this project to forget. The ticks are the
    /// confirmation: nothing starts ticked, the button says how much it is about to do, and
    /// Enter backs out rather than going ahead.
    /// </summary>
    public partial class ForgetWindow : Window
    {
        public ForgetWindow(ForgetChoices choices)
        {
            InitializeComponent();
            DataContext = choices;
        }

        private void OnForgetClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
