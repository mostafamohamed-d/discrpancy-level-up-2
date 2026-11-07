using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// Shows everything that went wrong during an import as one list, rather than
    /// interrupting the user with a dialog per element.
    /// </summary>
    public partial class IssueListWindow : Window
    {
        private readonly IList<string> allIssues;

        public IssueListWindow(string title, IList<string> issueList)
        {
            InitializeComponent();

            allIssues = issueList ?? new List<string>();
            Title = title;
            headline.Text = $"{allIssues.Count} element(s) reported a problem during the import.";
            issues.ItemsSource = allIssues;
        }

        private void OnCopyClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(string.Join("\r\n", allIssues.ToArray()));
            }
            catch
            {
                // Another application can hold the clipboard open; nothing useful to do.
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
