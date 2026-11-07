using System.Windows;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// A height zone's height and name. Asked as the zone is drawn, and again to change it; the
    /// height box has the focus with what is in it selected, so a new number is typed straight
    /// over the old one.
    /// </summary>
    public partial class HeightZoneWindow : Window
    {
        public HeightZoneWindow(HeightZoneRequest request)
        {
            InitializeComponent();
            DataContext = request;

            Loaded += (sender, e) =>
            {
                heightBox.Focus();
                heightBox.SelectAll();
            };
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (((HeightZoneRequest)DataContext).IsValid)
            {
                DialogResult = true;
            }
        }
    }
}
