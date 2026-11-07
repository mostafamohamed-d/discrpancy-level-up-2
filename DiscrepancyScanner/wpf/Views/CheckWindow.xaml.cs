using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Autodesk.Revit.UI;

namespace DiscrepancyScanner.Wpf
{
    public partial class CheckWindow : Window
    {
        private readonly ModelCanvas canvas = new ModelCanvas();
        private readonly ComparisonViewModel viewModel;

        public CheckWindow(ExternalCommandData commandData, RevitRequestHandler requestHandler)
        {
            InitializeComponent();

            viewModel = new ComparisonViewModel(commandData, requestHandler);
            // Get the window out of the way while the user picks a point in Revit.
            viewModel.Alignment.HideShell = Hide;
            viewModel.Alignment.ShowShell = Show;
            // The other way of picking: on the plan this window already draws, where both
            // models are on screen together and a matched pair is two clicks.
            viewModel.Alignment.PickOnPlan = BeginPlanPick;
            viewModel.Alignment.StopPlanPick = canvas.CancelPointPick;
            viewModel.ShowAlignment = ShowAlignment;
            viewModel.ShowIssues = ShowIssueList;
            viewModel.ShowSettings = ShowSettings;
            viewModel.ShowLoadingPlans = ShowLoadingPlans;
            viewModel.PickModel = PickModel;
            viewModel.AskForNote = AskForNote;
            viewModel.AskWhatToForget = AskWhatToForget;
            viewModel.AskForHeightZone = AskForHeightZone;
            viewModel.AskForLevels = AskForLevels;
            // A zone finished on the plan goes to the view model, which asks for its height.
            canvas.ZoneDrawn = corners => viewModel.AddHeightZone(corners);
            // And one half drawn goes with the tools, when the tab they belong to is switched off.
            viewModel.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(ComparisonViewModel.ShowsHeightZoneTools)
                    && !viewModel.ShowsHeightZoneTools)
                {
                    canvas.EndZone();
                }
            };
            // Accepting a difference changes a property on an element the canvas is already
            // holding, and the canvas only redraws when the collection itself changes.
            viewModel.RedrawPlan = canvas.InvalidateVisual;
            viewModel.ShowPreflight = ShowPreflight;
            canvas.ChooseFrom = ChooseElement;
            canvas.SelectOnlyRequested = viewModel.SelectOnly;
            canvas.WindowSelected = viewModel.AddToSelection;
            canvas.DeselectRequested = viewModel.RemoveFromSelection;
            canvas.SelectionCleared = () => viewModel.SelectedElement = null;
            // The plan puts the tool down itself - a right click, escape, an alignment pick -
            // and the button has to come up with it.
            canvas.MeasuringChanged = measuring => measureButton.IsChecked = measuring;
            viewModel.CollectWindowState = CollectWindowState;
            viewModel.VerdictColoursChanged = ApplyVerdictBrushes;
            ApplyVerdictBrushes();
            DataContext = viewModel;

            PreviewKeyDown += OnShortcutKey;
            // A first key held on to goes with the window: coming back to it later, a single A
            // is not the second half of a pair begun before somebody went off to Revit.
            Deactivated += (sender, e) => shortcuts.Reset();

            HookMaximiseBounds();
            RestoreWindowState(viewModel.SavedWindowState);
            Closing += (sender, e) => viewModel.SaveUserSettings();

            HostCanvas();
            OwnToRevit(commandData);
        }

        #region keys

        /// <summary>
        /// The keys that work anywhere in the window: by default Page Up and Page Down step a
        /// level up and down by height, as the arrows beside the level name do, 1 to 6 switch
        /// the tabs along the top on and off in the order they are drawn - 1 for beams, 2 for
        /// columns - R switches the raw data on and off, A pressed twice accepts what is
        /// selected and P pressed twice pairs it. All of them are set under Settings, Keyboard
        /// shortcuts; see <see cref="Shortcuts"/>.
        ///
        /// Taken before whatever has the focus sees them, so they mean the same thing whether
        /// the plan, the level rail or a button was clicked last. Not while something is being
        /// typed, though: a digit in a note or an element id is that digit, and Page Up in a box
        /// of text moves the caret.
        /// </summary>
        private void OnShortcutKey(object sender, KeyEventArgs e)
        {
            if (e.Handled || IsTyping())
            {
                return;
            }

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;

            // Delete takes selected height zones away - the one thing on the plan that is there
            // to be taken away. It asks first. Left alone while a zone is being drawn, where the
            // plan has keys of its own.
            if (key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None && !canvas.IsDrawingZone
                && Run(viewModel.DeleteHeightZonesCommand))
            {
                e.Handled = true;
                return;
            }

            bool taken;
            ShortcutAction? action = shortcuts.Press(key, Keyboard.Modifiers, e.IsRepeat, DateTime.Now, out taken);
            if (taken)
            {
                e.Handled = true;
            }
            if (action == null)
            {
                return;
            }

            switch (action.Value)
            {
                case ShortcutAction.LevelUp:
                    Run(viewModel.StoryUpCommand);
                    break;
                case ShortcutAction.LevelDown:
                    Run(viewModel.StoryDownCommand);
                    break;
                case ShortcutAction.RawData:
                    viewModel.ShowRawData = !viewModel.ShowRawData;
                    break;
                case ShortcutAction.Accept:
                    if (!Run(viewModel.ToggleAcceptedCommand))
                    {
                        viewModel.Status = "Nothing to accept: select a finding on the plan first - one that "
                                           + "differs between the two models.";
                    }
                    break;
                case ShortcutAction.Pair:
                    if (!Run(viewModel.PairSelectionCommand))
                    {
                        viewModel.Status = "Nothing to pair: select two elements of one kind first, one from "
                                           + "each model - drag a window over them.";
                    }
                    break;
                default:
                    viewModel.Findings.ToggleTab(Shortcuts.TabOf(action.Value).Value);
                    break;
            }
        }

        /// <summary>Follows the keys pressed, for the shortcuts that are two keys one after the other.</summary>
        private readonly ShortcutMatcher shortcuts = new ShortcutMatcher();

        /// <summary>True while the focus is somewhere text is typed into.</summary>
        private static bool IsTyping()
        {
            object focused = Keyboard.FocusedElement;
            return focused is System.Windows.Controls.Primitives.TextBoxBase
                   || focused is System.Windows.Controls.PasswordBox
                   || (focused is System.Windows.Controls.ComboBox && ((System.Windows.Controls.ComboBox)focused).IsEditable);
        }

        /// <summary>Runs a command where it can be run, and says whether it could.</summary>
        private static bool Run(System.Windows.Input.ICommand command)
        {
            if (command == null || !command.CanExecute(null))
            {
                return false;
            }

            command.Execute(null);
            return true;
        }

        #endregion

        #region verdict colours

        /// <summary>
        /// Right-click on the key: picks a new colour for that verdict.
        ///
        /// The Windows colour dialog rather than one of our own, because it is the one people
        /// already know. The six default colours are put in its custom colours, so putting one
        /// back is a click rather than a hex value somebody has to remember.
        /// </summary>
        private void OnVerdictKeyRightClick(object sender, MouseButtonEventArgs e)
        {
            VerdictKeyEntry entry = (sender as FrameworkElement)?.DataContext as VerdictKeyEntry;
            if (entry == null || entry.IsFixed)
            {
                return;
            }
            e.Handled = true;

            System.Windows.Media.Color current = VerdictColours.ColorOf(entry.Colour);

            using (System.Windows.Forms.ColorDialog dialog = new System.Windows.Forms.ColorDialog
            {
                AllowFullOpen = true,
                FullOpen = true,
                AnyColor = true,
                Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B),
                CustomColors = VerdictColours.All.Select(colour => ColourRef(VerdictColours.DefaultOf(colour)))
                                                 .ToArray()
            })
            {
                if (dialog.ShowDialog(new DialogOwner(new WindowInteropHelper(this).Handle))
                    != System.Windows.Forms.DialogResult.OK)
                {
                    return;
                }

                viewModel.ChangeVerdictColour(entry.Colour, System.Windows.Media.Color.FromRgb(
                    dialog.Color.R, dialog.Color.G, dialog.Color.B));
            }
        }

        /// <summary>
        /// Puts the chosen verdict colours over the theme's, for this window's own chrome - the
        /// tab counts, the level bars, the swatches beside the switches, the pills on the card.
        /// Set on the window rather than in the theme so the theme's values stay the defaults.
        /// </summary>
        private void ApplyVerdictBrushes()
        {
            Resources["VerdictPassed"] = ViewPalette.Passed;
            Resources["VerdictFailed"] = ViewPalette.Failed;
            Resources["VerdictReview"] = ViewPalette.Review;
            Resources["VerdictEtabsOnly"] = ViewPalette.UnpairedEtabs;
            Resources["VerdictRevitOnly"] = ViewPalette.UnpairedRevit;
            Resources["VerdictAccepted"] = ViewPalette.Accepted;
        }

        /// <summary>A colour as the colour dialog's custom colours hold it: 0x00BBGGRR.</summary>
        private static int ColourRef(System.Windows.Media.Color colour)
        {
            return colour.R | (colour.G << 8) | (colour.B << 16);
        }

        /// <summary>This window as the owner of a Windows Forms dialog, so it opens over it.</summary>
        private sealed class DialogOwner : System.Windows.Forms.IWin32Window
        {
            public DialogOwner(IntPtr handle)
            {
                Handle = handle;
            }

            public IntPtr Handle { get; }
        }

        #endregion

        /// <summary>
        /// Puts up the sheet that says what is missing before a run, and answers whether the
        /// run should go ahead. A sheet nobody answered - closed on the cross - reads as no.
        /// </summary>
        private bool ShowPreflight(PreflightReport report)
        {
            PreflightWindow sheet = new PreflightWindow(report) { Owner = this };
            return sheet.ShowDialog() == true;
        }

        /// <summary>
        /// Puts the model canvas in place and binds it to the view model. Done here rather
        /// than in the XAML because the markup compiler cannot resolve a local type in this
        /// project; see the note in CheckWindow.xaml.
        /// </summary>
        private void HostCanvas()
        {
            BindingOperations.SetBinding(canvas, ModelCanvas.ElementsProperty,
                new Binding(nameof(ComparisonViewModel.Elements)));
            BindingOperations.SetBinding(canvas, ModelCanvas.LabelKeysProperty,
                new Binding(nameof(ComparisonViewModel.SelectedLabelKeys)));
            BindingOperations.SetBinding(canvas, ModelCanvas.SelectedElementProperty,
                new Binding(nameof(ComparisonViewModel.SelectedElement)) { Mode = BindingMode.TwoWay });

            canvasHost.Content = canvas;
        }

        /// <summary>
        /// Parents the window to the Revit main window so it stays in front of Revit rather
        /// than getting lost behind it.
        /// </summary>
        private void OwnToRevit(ExternalCommandData commandData)
        {
            try
            {
                IntPtr revitHandle = commandData.Application.MainWindowHandle;
                if (revitHandle != IntPtr.Zero)
                {
                    new WindowInteropHelper(this) { Owner = revitHandle };
                }
            }
            catch
            {
                // Older Revit versions do not expose the handle; the window simply floats free.
            }
        }

        /// <summary>
        /// Ctrl+click found several elements under the cursor; asks which one was meant.
        /// </summary>
        private void ChooseElement(IList<PickHit> under)
        {
            ElementChooserWindow window = new ElementChooserWindow(under) { Owner = this };
            if (window.ShowDialog() == true && window.Chosen != null)
            {
                viewModel.SelectedElement = window.Chosen;
            }
        }

        /// <summary>
        /// Asks for a note on an element. Answers null when the person backed out, which is
        /// different from an empty string: an empty one clears the note, and backing out leaves
        /// whatever was there.
        /// </summary>
        private string AskForNote(string about, string existing)
        {
            NoteWindow window = new NoteWindow(about, existing) { Owner = this };
            return window.ShowDialog() == true ? window.Note : null;
        }

        /// <summary>
        /// Asks which of the things decided by hand to forget. Owned by the settings window the
        /// button is on where that is the window in front, so it opens over it and not behind.
        /// </summary>
        private bool AskWhatToForget(ForgetChoices choices)
        {
            Window owner = OwnedWindows.OfType<Window>().FirstOrDefault(window => window.IsActive) ?? this;
            ForgetWindow dialog = new ForgetWindow(choices) { Owner = owner };
            return dialog.ShowDialog() == true;
        }

        #region maximising without a frame

        /// <summary>
        /// A window with no frame of its own maximises to the whole monitor rather than to the
        /// work area, and Windows then oversizes it by the resize border on every side. The
        /// result is a window whose edges sit off-screen: the left panel loses the first few
        /// characters of every line and the drawer along the bottom is pushed under the
        /// taskbar, which is what made the findings list look like it had disappeared.
        ///
        /// The fix is to answer WM_GETMINMAXINFO ourselves with the work area of whichever
        /// monitor the window is on, so maximised means exactly the space that is actually
        /// free. Done in Win32 because the message arrives before WPF has a say.
        /// </summary>
        private void HookMaximiseBounds()
        {
            SourceInitialized += (sender, e) =>
            {
                HwndSource source = (HwndSource)PresentationSource.FromVisual(this);
                source?.AddHook(WindowProc);
            };
        }

        private const int WmGetMinMaxInfo = 0x0024;
        private const int MonitorDefaultToNearest = 0x00000002;

        private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam,
                                         ref bool handled)
        {
            if (msg != WmGetMinMaxInfo)
            {
                return IntPtr.Zero;
            }

            IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            MonitorInfo info = new MonitorInfo();
            info.cbSize = Marshal.SizeOf(typeof(MonitorInfo));
            if (!GetMonitorInfo(monitor, ref info))
            {
                return IntPtr.Zero;
            }

            MinMaxInfo minMax = (MinMaxInfo)Marshal.PtrToStructure(lParam, typeof(MinMaxInfo));

            // Positions are relative to the monitor, not the desktop, which is what makes this
            // work on a second screen sitting left of or above the primary one.
            minMax.ptMaxPosition.x = info.rcWork.left - info.rcMonitor.left;
            minMax.ptMaxPosition.y = info.rcWork.top - info.rcMonitor.top;
            minMax.ptMaxSize.x = info.rcWork.right - info.rcWork.left;
            minMax.ptMaxSize.y = info.rcWork.bottom - info.rcWork.top;

            Marshal.StructureToPtr(minMax, lParam, true);
            handled = true;
            return IntPtr.Zero;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int cbSize;
            public NativeRect rcMonitor;
            public NativeRect rcWork;
            public int dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public NativePoint ptReserved;
            public NativePoint ptMaxSize;
            public NativePoint ptMaxPosition;
            public NativePoint ptMinTrackSize;
            public NativePoint ptMaxTrackSize;
        }

        #endregion

        /// <summary>
        /// Puts the settings window up over this one. Answers true when it was closed on OK,
        /// which is the view model's cue to write the settings file.
        /// </summary>
        // The window has no caption bar of its own, so these stand in for its buttons. Drag and
        // resize are handled by WindowChrome; only these two need code.

        private void OnMinimiseClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void OnMaximiseClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        /// <summary>
        /// Keeps the middle caption button saying what it will do. With no frame of its own the
        /// window has to draw and label this itself, and a maximise button on an already
        /// maximised window is the kind of small lie that makes the rest look untrustworthy.
        /// </summary>
        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);

            if (maximiseButton == null)
            {
                return;
            }

            bool maximised = WindowState == WindowState.Maximized;
            maximiseButton.Content = maximised ? "❐" : "□";
            maximiseButton.ToolTip = maximised ? "Restore down" : "Maximise";
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private bool ShowSettings(SettingsViewModel settings)
        {
            SettingsWindow window = new SettingsWindow(settings) { Owner = this };
            return window.ShowDialog() == true;
        }

        /// <summary>
        /// The alignment window, kept to one. Two of them on the same alignment would each be
        /// showing what the other last did.
        /// </summary>
        private AlignWindow alignWindow;

        /// <summary>
        /// Puts the alignment window up beside this one, and not as a dialog: a dialog would
        /// disable every window in the process, Revit's own included, and picking a point in
        /// Revit is half of what this window is for.
        /// </summary>
        private void ShowAlignment()
        {
            if (alignWindow != null)
            {
                alignWindow.Activate();
                return;
            }

            alignWindow = new AlignWindow(viewModel.Alignment) { Owner = this };
            alignWindow.Closed += (sender, e) => alignWindow = null;
            alignWindow.Show();
        }

        /// <summary>
        /// Hands a point pick to the plan. This window comes forward so the plan can be seen;
        /// the alignment window is owned by it and so stays on top of it, which is what lets
        /// the prompt be read while the plan underneath takes the clicks.
        /// </summary>
        private void BeginPlanPick(PlanPickRequest request)
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
            canvas.BeginPointPick(request);
        }

        /// <summary>
        /// Puts the loading plans window up over this one. Nothing comes back from it: the
        /// pairings are edited in place as they are made, and the view model writes them out
        /// once this returns.
        /// </summary>
        private void ShowLoadingPlans(LoadingPlansViewModel pairings)
        {
            LoadingPlansWindow window = new LoadingPlansWindow(pairings) { Owner = this };
            window.ShowDialog();
        }

        /// <summary>
        /// Asks which of the models open on the machine to read. Null when the user cancels,
        /// which the import takes as "read nothing" rather than as a failure.
        /// </summary>
        private CsiInstance PickModel()
        {
            ModelChooserWindow window = new ModelChooserWindow { Owner = this };
            return window.ShowDialog() == true ? window.Chosen : null;
        }

        #region where the window was left

        /// <summary>The size and position, for the view branch of the settings file.</summary>
        private Dictionary<string, object> CollectWindowState()
        {
            // A maximised window reports the screen rather than the size it would go back to,
            // so what is written is the restore bounds and the maximised state beside them.
            Rect bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;

            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "left", bounds.Left },
                { "top", bounds.Top },
                { "width", bounds.Width },
                { "height", bounds.Height },
                { "maximised", WindowState == WindowState.Maximized }
            };
        }

        /// <summary>
        /// Puts the window back where it was left. The position is only used when it still
        /// lands on a screen that is there today, so unplugging a second monitor does not
        /// leave the window opening somewhere it cannot be reached.
        /// </summary>
        private void RestoreWindowState(IDictionary<string, object> state)
        {
            if (state == null || state.Count == 0)
            {
                return;
            }

            double width = Json.GetDouble(state, "width", Width);
            double height = Json.GetDouble(state, "height", Height);
            if (width >= MinWidth && height >= MinHeight && width > 0 && height > 0)
            {
                Width = width;
                Height = height;
            }

            double left = Json.GetDouble(state, "left", double.NaN);
            double top = Json.GetDouble(state, "top", double.NaN);
            if (!double.IsNaN(left) && !double.IsNaN(top) && IsOnAScreen(left, top, Width, Height))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left;
                Top = top;
            }

            if (Json.GetBool(state, "maximised", false))
            {
                WindowState = WindowState.Maximized;
            }
        }

        /// <summary>True while enough of the window would land on the desktop to grab hold of.</summary>
        private static bool IsOnAScreen(double left, double top, double width, double height)
        {
            Rect desktop = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                                    SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            Rect window = new Rect(left, top, width, height);
            Rect seen = Rect.Intersect(desktop, window);

            return !seen.IsEmpty && seen.Width >= 120 && seen.Height >= 60;
        }

        #endregion

        private void ShowIssueList(string title, IList<string> issues)
        {
            IssueListWindow window = new IssueListWindow(title, issues) { Owner = this };
            window.ShowDialog();
        }

        private void OnZoomToFitClick(object sender, RoutedEventArgs e)
        {
            canvas.ZoomToFit();
        }

        private void OnDrawZoneRectangleClick(object sender, RoutedEventArgs e)
        {
            canvas.BeginZone(ZoneShape.Rectangle);
        }

        private void OnDrawZonePolygonClick(object sender, RoutedEventArgs e)
        {
            canvas.BeginZone(ZoneShape.Polygon);
        }

        /// <summary>Asks for a height zone's height and name.</summary>
        private bool AskForHeightZone(HeightZoneRequest request)
        {
            HeightZoneWindow dialog = new HeightZoneWindow(request) { Owner = this };
            return dialog.ShowDialog() == true;
        }

        /// <summary>Asks which levels a zone is copied to.</summary>
        private bool AskForLevels(LevelChoices choices)
        {
            LevelsWindow dialog = new LevelsWindow(choices) { Owner = this };
            return dialog.ShowDialog() == true;
        }

        private void OnMeasureClick(object sender, RoutedEventArgs e)
        {
            if (measureButton.IsChecked == true)
            {
                canvas.BeginMeasuring();
            }
            else
            {
                canvas.EndMeasuring();
            }
        }
    }
}
