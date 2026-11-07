using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// A sheet of the loading schedule, shown and edited inside the tool.
    ///
    /// The table's columns are made here rather than in the xaml, one per column of the sheet,
    /// because how many there are is only known once the sheet has been read - and a sheet
    /// picked from the list can be wider or narrower than the last one.
    /// </summary>
    public partial class ScheduleSheetWindow : Window
    {
        private static readonly Brush PendingBrush = Frozen(Color.FromRgb(0xFF, 0xE2, 0xA8));
        private static readonly Brush KeptBrush = Frozen(Color.FromRgb(0xD6, 0xEB, 0xFF));
        private static readonly Brush FormulaBrush = Frozen(Color.FromRgb(0xED, 0xED, 0xED));
        private static readonly Brush RowNumberBrush = Frozen(Color.FromRgb(0xF2, 0xF2, 0xF3));

        // The columns the check reads, each in a colour of its own so the heading over it and
        // every value down it say which it is.
        private static readonly Brush NumberRoleBrush = Frozen(Color.FromRgb(0xE6, 0xDE, 0xF8));
        private static readonly Brush DescriptionRoleBrush = Frozen(Color.FromRgb(0xEE, 0xF1, 0xF5));
        private static readonly Brush SidlRoleBrush = Frozen(Color.FromRgb(0xD4, 0xF0, 0xD8));
        private static readonly Brush SidlBreakdownRoleBrush = Frozen(Color.FromRgb(0xEA, 0xF7, 0xEC));
        private static readonly Brush LiveRoleBrush = Frozen(Color.FromRgb(0xFB, 0xDD, 0xE6));
        private static readonly Brush LiveKindRoleBrush = Frozen(Color.FromRgb(0xFD, 0xEE, 0xF2));

        private readonly ScheduleSheetViewModel viewModel;

        public ScheduleSheetWindow(ScheduleSheetViewModel viewModel)
        {
            InitializeComponent();

            this.viewModel = viewModel;
            viewModel.AskWhereToSave = AskWhereToSave;
            viewModel.Confirm = Confirm;
            viewModel.Report = Report;
            viewModel.ColumnsChanged += () => BuildColumns(viewModel.ColumnCount);
            DataContext = viewModel;

            BuildColumns(viewModel.ColumnCount);
        }

        private void BuildColumns(int count)
        {
            grid.Columns.Clear();

            Style numberStyle = new Style(typeof(DataGridCell));
            numberStyle.Setters.Add(new Setter(BackgroundProperty, RowNumberBrush));
            numberStyle.Setters.Add(new Setter(ForegroundProperty, Brushes.DimGray));
            numberStyle.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0)));

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "",
                Binding = new Binding(nameof(ScheduleRowViewModel.Number)),
                IsReadOnly = true,
                Width = 44,
                CellStyle = numberStyle
            });

            for (int i = 0; i < count; i++)
            {
                string cell = $"Cells[{i}]";

                string role;
                viewModel.ColumnRoles.TryGetValue(i + 1, out role);
                Brush roleBrush = RoleBrush(role);
                bool byHand = viewModel.HandRoleOf(i + 1).HasValue;

                string header = ScheduleSheetViewModel.ColumnName(i + 1);
                if (role != null)
                {
                    header += " · " + role;
                }
                if (byHand)
                {
                    header += " ✎";
                }

                grid.Columns.Add(new DataGridTextColumn
                {
                    Header = header,
                    HeaderStyle = HeaderStyle(roleBrush, ColumnMenu(i + 1), byHand),
                    Binding = new Binding(cell + "." + nameof(ScheduleCellViewModel.Text))
                    {
                        Mode = BindingMode.TwoWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.LostFocus
                    },
                    MinWidth = 50,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
                    MaxWidth = 360,
                    CellStyle = CellStyle(cell, roleBrush)
                });
            }
        }

        private static Brush RoleBrush(string role)
        {
            if (string.IsNullOrEmpty(role))
            {
                return null;
            }
            if (role.EndsWith(" breakdown"))
            {
                return SidlBreakdownRoleBrush;
            }
            if (role == LoadScheduleReader.RoleNumber)
            {
                return NumberRoleBrush;
            }
            if (role == LoadScheduleReader.RoleDescription)
            {
                return DescriptionRoleBrush;
            }
            if (role == LoadScheduleReader.RoleLive)
            {
                return LiveRoleBrush;
            }
            if (role == LoadScheduleReader.RoleLiveKind)
            {
                return LiveKindRoleBrush;
            }
            return role.StartsWith(LoadScheduleReader.RoleSidl) ? SidlRoleBrush : null;
        }

        /// <summary>
        /// The menu on a column's heading: what the column holds, picked by hand for a sheet
        /// whose columns the headings did not give right, and kept with the project.
        /// </summary>
        private ContextMenu ColumnMenu(int column)
        {
            ContextMenu menu = new ContextMenu();

            if (!viewModel.CanSetColumns)
            {
                menu.Items.Add(new MenuItem
                {
                    Header = "Open a project to say what the columns hold - it is kept per project.",
                    IsEnabled = false
                });
                return menu;
            }

            menu.Items.Add(new MenuItem
            {
                Header = $"Column {ScheduleSheetViewModel.ColumnName(column)} holds:",
                IsEnabled = false,
                FontWeight = FontWeights.SemiBold
            });

            ScheduleRole? held = viewModel.HandRoleOf(column);

            foreach (var choice in ScheduleSheetViewModel.RoleChoices)
            {
                ScheduleRole role = choice.Key;
                MenuItem item = new MenuItem
                {
                    Header = choice.Value,
                    IsCheckable = true,
                    IsChecked = held == role
                };
                item.Click += (sender, e) => viewModel.SetColumnRole(column, role);
                menu.Items.Add(item);
            }

            menu.Items.Add(new Separator());

            MenuItem automatic = new MenuItem
            {
                Header = "Let the headings decide",
                IsEnabled = held.HasValue
            };
            automatic.Click += (sender, e) => viewModel.SetColumnRole(column, null);
            menu.Items.Add(automatic);

            return menu;
        }

        /// <summary>A heading over a column the check reads is filled in its colour and set in bold.</summary>
        private static Style HeaderStyle(Brush roleBrush, ContextMenu menu, bool byHand)
        {
            Style style = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
            style.Setters.Add(new Setter(ContextMenuProperty, menu));
            style.Setters.Add(new Setter(ToolTipProperty, byHand
                ? "Said by hand for this project. Right-click to change it, or to let the headings decide."
                : "Right-click to say what this column holds, if the headings got it wrong."));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 3, 6, 3)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.LightGray));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));

            if (roleBrush != null)
            {
                style.Setters.Add(new Setter(Control.BackgroundProperty, roleBrush));
                style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Bold));
            }
            else
            {
                style.Setters.Add(new Setter(Control.BackgroundProperty, RowNumberBrush));
            }
            return style;
        }

        /// <summary>
        /// The colour of a cell says where its value stands: typed and not saved, kept in the
        /// tool only, or worked out by a formula and not to be typed over.
        /// </summary>
        private static Style CellStyle(string cell, Brush roleBrush)
        {
            Style style = new Style(typeof(DataGridCell));

            // The column's own colour underneath; the three states below paint over it, because
            // a change not saved matters more than which column it is in.
            if (roleBrush != null)
            {
                style.Setters.Add(new Setter(BackgroundProperty, roleBrush));
            }

            style.Setters.Add(new Setter(ToolTipProperty,
                                         new Binding(cell + "." + nameof(ScheduleCellViewModel.Tip))));

            style.Triggers.Add(Trigger(cell, nameof(ScheduleCellViewModel.IsFormula), FormulaBrush, FontStyles.Italic));
            style.Triggers.Add(Trigger(cell, nameof(ScheduleCellViewModel.IsKept), KeptBrush, null));
            style.Triggers.Add(Trigger(cell, nameof(ScheduleCellViewModel.IsPending), PendingBrush, null));

            // A selected cell keeps the colour that says what it is, with a border to say it is
            // selected, rather than going blue and hiding which of the three it was.
            Trigger selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(ForegroundProperty, Brushes.Black));
            selected.Setters.Add(new Setter(BorderBrushProperty, Brushes.SteelBlue));
            selected.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(1.5)));
            style.Triggers.Add(selected);

            return style;
        }

        private static DataTrigger Trigger(string cell, string flag, Brush background, FontStyle? fontStyle)
        {
            DataTrigger trigger = new DataTrigger { Binding = new Binding(cell + "." + flag), Value = true };
            trigger.Setters.Add(new Setter(BackgroundProperty, background));
            if (fontStyle.HasValue)
            {
                trigger.Setters.Add(new Setter(FontStyleProperty, fontStyle.Value));
            }
            return trigger;
        }

        /// <summary>A formula's cell is shown and never edited from here.</summary>
        private void OnBeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            ScheduleRowViewModel row = e.Row.Item as ScheduleRowViewModel;
            int index = e.Column.DisplayIndex - 1;

            if (row == null || index < 0 || index >= row.Cells.Count || row.Cells[index].IsFormula)
            {
                e.Cancel = true;
            }
        }

        private ScheduleSaveTarget AskWhereToSave(int differing)
        {
            // Whatever is being typed into is committed first, so it is among what is saved.
            grid.CommitEdit(DataGridEditingUnit.Row, true);

            MessageBoxResult answer = MessageBox.Show(this,
                $"Where should the changes go?\n\n"
                + $"YES - write them into {viewModel.WorkbookName}. {differing} cell(s) on "
                + $"{viewModel.SelectedSheet} will say something new. A copy of the workbook as it is now "
                + "is kept first. Excel must not have the file open.\n\n"
                + "NO - keep them in this tool only. The workbook is left exactly as it is, and the check "
                + "reads the changed values, saying so on every run.\n\n"
                + "CANCEL - go back to the sheet.",
                "Save the loading schedule",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);

            switch (answer)
            {
                case MessageBoxResult.Yes:
                    return ScheduleSaveTarget.Workbook;
                case MessageBoxResult.No:
                    return ScheduleSaveTarget.ToolOnly;
                default:
                    return ScheduleSaveTarget.Cancel;
            }
        }

        private bool Confirm(string question)
        {
            return MessageBox.Show(this, question, "Loading schedule",
                                   MessageBoxButton.YesNo, MessageBoxImage.Question,
                                   MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        private void Report(string message)
        {
            MessageBox.Show(this, message, "Loading schedule", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>Closing with changes not saved asks what to do with them rather than dropping them.</summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            grid.CommitEdit(DataGridEditingUnit.Row, true);

            if (viewModel.PendingCount > 0)
            {
                MessageBoxResult answer = MessageBox.Show(this,
                    $"{viewModel.PendingCount} change(s) are not saved. Save them before closing?",
                    "Loading schedule", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                if (answer == MessageBoxResult.Cancel
                    || (answer == MessageBoxResult.Yes && !viewModel.Save()))
                {
                    e.Cancel = true;
                }
            }

            base.OnClosing(e);
        }

        private static Brush Frozen(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
