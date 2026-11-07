using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>The two ways a height zone is drawn on the plan.</summary>
    public enum ZoneShape
    {
        /// <summary>Two opposite corners.</summary>
        Rectangle,

        /// <summary>Every corner in turn, closed back to the first.</summary>
        Polygon
    }

    /// <summary>
    /// One element under the cursor and how far the cursor was from it.
    ///
    /// The distance is what makes a list of several readable. Two columns in the same place
    /// carry the same kind and often nearly the same name, so "12 mm" against "180 mm" is
    /// frequently the only thing that says which one is under the pointer.
    /// </summary>
    public sealed class PickHit
    {
        public PickHit(ElementViewModel element, double distanceMM)
        {
            Element = element;
            DistanceMM = distanceMM;
        }

        public ElementViewModel Element { get; }
        public double DistanceMM { get; }
    }

    /// <summary>
    /// The model viewport. It owns nothing but the camera: everything it draws comes from
    /// the <see cref="Elements"/> it is bound to, and the text it stamps on top of each beam
    /// or column is whatever the view model put in <see cref="LabelKeys"/>.
    ///
    /// Rendering is done directly against the drawing context rather than through a panel of
    /// child controls, because a floor plate is tens of thousands of elements and one visual
    /// per element does not keep up with panning.
    /// </summary>
    public class ModelCanvas : FrameworkElement
    {
        private const double GeometryPenWidthPixels = 1.6;
        private const double SelectedPenWidthPixels = 3.0;
        private const double MarkHaloWidthPixels = 7.0;
        private const double LabelFontSize = 11;
        private const double LabelLineHeight = 13;
        private const double MinimumScale = 1e-6;
        private const double MaximumScale = 10;
        private const double ZoomStep = 1.2;
        private const double PickToleranceInPixels = 12;

        // How far a point picked off the plan will reach for something to land on. Wider than
        // the pick tolerance above: that one asks which element was clicked and can afford to
        // miss, this one is placing a coordinate and a miss means typing it in by hand.
        private const double SnapToleranceInPixels = 18;
        private const double SnapMarkerRadiusInPixels = 7;
        private const double CrosshairArmInPixels = 26;

        private static readonly Typeface LabelTypeface =
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        private double scale = 0.05;
        private Point origin = new Point(0, 0);
        private bool isPanning;

        // Ctrl+drag pans and ctrl+click asks which of the elements under the cursor was meant,
        // so which one it was cannot be known until the button comes back up. Anything that
        // moved further than this was a drag; anything less was a hand that did not hold still.
        private const double DragThresholdInPixels = 4;
        private Point pressedAt;
        private bool pressWasCtrl;

        // A plain or shift left press waits to find out whether it is a click or a window: a
        // click selects on the way up, a drag past the threshold draws a rectangle instead.
        private bool selectPressPending;
        private bool selectPressWasShift;
        private bool isWindowing;
        private Point windowEnd;
        private bool movedWhilePanning;
        private bool pendingZoomToFit;
        private Point lastMousePosition;

        // A point being picked off the plan for the alignment. Panning and zooming carry on
        // under it, so the view can be got right before the point is committed.
        private PlanPickRequest pickRequest;
        private SnapResult pickPreview;

        // The measure tool. Measurements are kept in model mm, so they stay on the geometry
        // they were taken from through any amount of panning and zooming, and they stay on the
        // plan after the tool is put down - until an element is clicked or the plan is rebuilt.
        private bool isMeasuring;
        private Point? measureStart;
        private SnapResult measurePreview;
        private readonly List<Measurement> measurements = new List<Measurement>();

        // A height zone being drawn: which shape, the corners placed so far in model mm, and
        // where the next one would land.
        private ZoneShape? drawingZone;
        private readonly List<Point> zoneCorners = new List<Point>();
        private SnapResult zonePreview;

        private struct Measurement
        {
            public Measurement(Point from, Point to)
            {
                From = from;
                To = to;
            }

            public Point From { get; }
            public Point To { get; }
        }

        public ModelCanvas()
        {
            ClipToBounds = true;
            Focusable = true;
            Background = ViewPalette.Background;
        }

        #region dependency properties

        public static readonly DependencyProperty ElementsProperty = DependencyProperty.Register(
            nameof(Elements), typeof(IEnumerable<ElementViewModel>), typeof(ModelCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnElementsChanged));

        public IEnumerable<ElementViewModel> Elements
        {
            get => (IEnumerable<ElementViewModel>)GetValue(ElementsProperty);
            set => SetValue(ElementsProperty, value);
        }

        public static readonly DependencyProperty LabelKeysProperty = DependencyProperty.Register(
            nameof(LabelKeys), typeof(IEnumerable<string>), typeof(ModelCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnLabelKeysChanged));

        /// <summary>Result field keys to stamp on top of each element, in order.</summary>
        public IEnumerable<string> LabelKeys
        {
            get => (IEnumerable<string>)GetValue(LabelKeysProperty);
            set => SetValue(LabelKeysProperty, value);
        }

        public static readonly DependencyProperty SelectedElementProperty = DependencyProperty.Register(
            nameof(SelectedElement), typeof(ElementViewModel), typeof(ModelCanvas),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender,
                OnSelectedElementChanged));

        public ElementViewModel SelectedElement
        {
            get => (ElementViewModel)GetValue(SelectedElementProperty);
            set => SetValue(SelectedElementProperty, value);
        }

        public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
            nameof(Background), typeof(Brush), typeof(ModelCanvas),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush Background
        {
            get => (Brush)GetValue(BackgroundProperty);
            set => SetValue(BackgroundProperty, value);
        }

        private static void OnElementsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ModelCanvas canvas = (ModelCanvas)d;
            canvas.HookCollection(e.OldValue as INotifyCollectionChanged, e.NewValue as INotifyCollectionChanged,
                                  canvas.OnElementsCollectionChanged);
            canvas.DropMeasurementsWithThePlan();
            canvas.ZoomToFit();
        }

        private static void OnLabelKeysChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ModelCanvas canvas = (ModelCanvas)d;
            canvas.HookCollection(e.OldValue as INotifyCollectionChanged, e.NewValue as INotifyCollectionChanged,
                                  canvas.OnBoundCollectionChanged);
            canvas.InvalidateVisual();
        }

        private static void OnSelectedElementChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is ElementViewModel previous)
            {
                previous.Drawn.IsSelected = false;
            }
            if (e.NewValue is ElementViewModel current)
            {
                current.Drawn.IsSelected = true;
            }

            // Clicking an element is where a measurement has done its job - on the plan or
            // in the list down the side, which comes in through here as well.
            ModelCanvas canvas = (ModelCanvas)d;
            canvas.ClearMeasurements();
            canvas.InvalidateVisual();
        }

        private void HookCollection(INotifyCollectionChanged oldCollection, INotifyCollectionChanged newCollection,
                                    NotifyCollectionChangedEventHandler handler)
        {
            if (oldCollection != null)
            {
                oldCollection.CollectionChanged -= handler;
            }
            if (newCollection != null)
            {
                newCollection.CollectionChanged += handler;
            }
        }

        private void OnBoundCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            InvalidateVisual();
        }

        /// <summary>
        /// The plan is rebuilt in place - another level, another tab, a switch under SHOW, a
        /// run - so what was measured on it was measured on a different drawing.
        /// </summary>
        private void OnElementsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            DropMeasurementsWithThePlan();
            InvalidateVisual();
        }

        #endregion

        #region camera

        /// <summary>Model millimetres to device pixels.</summary>
        private Matrix ModelToScreen
        {
            get
            {
                // Model Y points up, screen Y points down.
                Matrix matrix = new Matrix(scale, 0, 0, -scale, 0, 0);
                matrix.Translate(origin.X, origin.Y);
                return matrix;
            }
        }

        private Point ScreenToModel(Point screenPoint)
        {
            Matrix inverse = ModelToScreen;
            inverse.Invert();
            return inverse.Transform(screenPoint);
        }

        /// <summary>Frames every visible element, leaving a small margin.</summary>
        public void ZoomToFit()
        {
            if (ActualWidth < 1 || ActualHeight < 1)
            {
                // Laid out too early to know the viewport size; retry once we have one.
                pendingZoomToFit = true;
                return;
            }

            Rect bounds = GetModelBounds();
            if (bounds.IsEmpty || bounds.Width <= 0 && bounds.Height <= 0)
            {
                return;
            }

            double availableWidth = ActualWidth;
            double availableHeight = ActualHeight;

            double scaleX = bounds.Width > 0 ? availableWidth / bounds.Width : double.MaxValue;
            double scaleY = bounds.Height > 0 ? availableHeight / bounds.Height : double.MaxValue;

            scale = Clamp(Math.Min(scaleX, scaleY) * 0.9, MinimumScale, MaximumScale);

            Point modelCenter = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
            origin = new Point(availableWidth / 2 - modelCenter.X * scale,
                               availableHeight / 2 + modelCenter.Y * scale);

            InvalidateVisual();
        }

        private Rect GetModelBounds()
        {
            if (Elements == null)
            {
                return Rect.Empty;
            }

            Rect bounds = Rect.Empty;
            foreach (ElementViewModel element in Elements)
            {
                bounds.Union(element.Geometry.Bounds);
            }
            return bounds;
        }

        private void ZoomAt(Point screenPoint, double factor)
        {
            Point modelPoint = ScreenToModel(screenPoint);
            double newScale = Clamp(scale * factor, MinimumScale, MaximumScale);
            if (Math.Abs(newScale - scale) < double.Epsilon)
            {
                return;
            }

            scale = newScale;
            // Keep the point under the cursor pinned while the scale changes.
            origin = new Point(screenPoint.X - modelPoint.X * scale,
                               screenPoint.Y + modelPoint.Y * scale);
            InvalidateVisual();
        }

        private static double Clamp(double value, double min, double max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        #endregion

        #region rendering

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Background, null, new Rect(RenderSize));

            if (Elements != null)
            {
                List<ElementViewModel> elements = Elements.ToList();
                Matrix modelToScreen = ModelToScreen;
                Rect visibleModelRect = GetVisibleModelRect(modelToScreen);

                DrawGeometry(dc, elements, modelToScreen, visibleModelRect);
                DrawLabels(dc, elements, modelToScreen, visibleModelRect);
            }

            DrawMeasurements(dc);
            DrawSelectionWindow(dc);

            // Last, over everything, and drawn even on an empty plan: it is a question being
            // asked rather than a part of the model.
            DrawPickOverlay(dc);
            DrawMeasureOverlay(dc);
            DrawZoneOverlay(dc);
        }

        private void DrawSelectionWindow(DrawingContext dc)
        {
            if (!isWindowing)
            {
                return;
            }

            bool crossing = windowEnd.X < pressedAt.X;
            Pen edge = new Pen(crossing ? ViewPalette.CrossingEdge : ViewPalette.WindowEdge, 1);
            if (crossing)
            {
                edge.DashStyle = new DashStyle(new double[] { 4, 3 }, 0);
            }
            edge.Freeze();

            dc.DrawRectangle(crossing ? ViewPalette.CrossingFill : ViewPalette.WindowFill, edge,
                             new Rect(pressedAt, windowEnd));
        }

        private Rect GetVisibleModelRect(Matrix modelToScreen)
        {
            Matrix inverse = modelToScreen;
            inverse.Invert();
            Point topLeft = inverse.Transform(new Point(0, 0));
            Point bottomRight = inverse.Transform(new Point(ActualWidth, ActualHeight));
            return new Rect(topLeft, bottomRight);
        }

        private void DrawGeometry(DrawingContext dc, List<ElementViewModel> elements,
                                  Matrix modelToScreen, Rect visibleModelRect)
        {
            dc.PushTransform(new MatrixTransform(modelToScreen));

            // Pens are specified in model units, so dividing by the scale keeps the drawn
            // line a constant thickness on screen at any zoom level.
            Dictionary<Brush, Pen> pens = new Dictionary<Brush, Pen>();
            Pen PenFor(Brush brush, bool selected)
            {
                double width = (selected ? SelectedPenWidthPixels : GeometryPenWidthPixels) / scale;
                if (selected)
                {
                    Pen selectedPen = new Pen(brush, width);
                    selectedPen.Freeze();
                    return selectedPen;
                }
                if (!pens.TryGetValue(brush, out Pen pen))
                {
                    pen = new Pen(brush, width);
                    pen.Freeze();
                    pens[brush] = pen;
                }
                return pen;
            }

            // One pen for every halo, made once: the halo is drawn in its own colour rather than
            // the element's, so unlike the selected pen it does not vary from element to element.
            Pen haloPen = new Pen(ViewPalette.SelectionHalo, MarkHaloWidthPixels / scale);
            haloPen.Freeze();

            foreach (ElementViewModel element in elements)
            {
                Geometry geometry = element.Geometry;
                if (!geometry.Bounds.IsEmpty && !visibleModelRect.IntersectsWith(geometry.Bounds))
                {
                    continue;
                }

                Brush brush = ViewPalette.ForState(element);
                Brush fill = element.IsFilled ? MakeFill(brush, FillAlphaFor(element)) : null;

                // Under the element, not over it, so the colour that says which model this came
                // from is still the colour on top.
                if (element.IsSelected)
                {
                    dc.DrawGeometry(null, haloPen, geometry);
                }

                dc.DrawGeometry(fill, PenFor(brush, element.IsSelected), geometry);
            }

            dc.Pop();
        }

        /// <summary>
        /// A slab zone covers a whole bay rather than a section outline, so it is filled far
        /// more lightly: at the density a column is filled at, one storey of slab would drown
        /// everything drawn on top of it.
        /// </summary>
        private static byte FillAlphaFor(ElementViewModel element)
        {
            // A height zone lighter still: it is laid over the lines it changes, which have to
            // read through it.
            if (element.Kind == ElementKind.HeightZone)
            {
                return 0x2A;
            }
            return element.Kind == ElementKind.Slab ? (byte)0x3A : (byte)0x55;
        }

        private readonly Dictionary<int, Brush> fillCache = new Dictionary<int, Brush>();

        /// <summary>A translucent version of the outline colour, so overlapping shapes stay readable.</summary>
        private Brush MakeFill(Brush stroke, byte alpha)
        {
            SolidColorBrush solid = stroke as SolidColorBrush;
            if (solid == null)
            {
                return stroke;
            }

            Color color = solid.Color;
            int key = color.R << 24 | color.G << 16 | color.B << 8 | alpha;
            if (fillCache.TryGetValue(key, out Brush cached))
            {
                return cached;
            }

            SolidColorBrush translucent = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            translucent.Freeze();
            fillCache[key] = translucent;
            return translucent;
        }

        private void DrawLabels(DrawingContext dc, List<ElementViewModel> elements,
                                Matrix modelToScreen, Rect visibleModelRect)
        {
            List<string> keys = LabelKeys?.ToList() ?? new List<string>();

            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            foreach (ElementViewModel element in elements)
            {
                // An element whose result the filters switched off is drawn grey and left
                // unlabelled, so nothing about the hidden check is stamped on the plan. So is
                // context, which carries no result of this check to stamp.
                //
                // A caption is written whatever the label keys: it is what the element is - a
                // height zone's height - not a result. Never on context, though: a zone whose tab
                // is off is not on the plan at all, and nothing grey says anything.
                bool labelled = keys.Count > 0 && !element.IsMuted && !element.IsDrawnAsContext;
                string caption = element.IsDrawnAsContext ? null : element.Caption;
                if (!labelled && caption == null)
                {
                    continue;
                }

                Point anchor = element.LabelAnchor;
                if (!visibleModelRect.Contains(anchor))
                {
                    continue;
                }

                // A value belonging to a check the filters switched off is not stamped, so
                // hiding a result hides it on the model as well as in the colour.
                List<ResultField> fields = labelled
                    ? keys.Select(key => element.ResultFields.FirstOrDefault(f => f.Key == key))
                          .Where(field => field != null && !string.IsNullOrEmpty(field.Value))
                          .Where(field => element.IsFieldShown(field))
                          .ToList()
                    : new List<ResultField>();

                if (caption != null)
                {
                    fields.Insert(0, new ResultField("Caption", "", caption));
                }

                if (fields.Count == 0)
                {
                    continue;
                }

                DrawLabelBlock(dc, element, modelToScreen, fields, pixelsPerDip);
            }
        }

        /// <summary>How far a label is set off whatever it is beside, in pixels.</summary>
        private const double LabelGapPixels = 3;

        /// <summary>
        /// One element's label: its values one under another, on a pale ground.
        ///
        /// On anything drawn as a line - a beam, a line load, a wall - the block is turned to
        /// run along it and set beside it, the way a beam is tagged on a drawing: read across a
        /// plan full of beams at every angle, a label written level belongs to whichever beam it
        /// happens to land on. It is turned to read left to right, or upwards on a line straight
        /// up the screen, so no label is ever upside down.
        ///
        /// Which side is the model's. An ETABS element and a Revit one very often lie on the
        /// same line - they are meant to - and two labels in the same place hide each other, so
        /// the ETABS one goes on one side and the Revit one on the other. A paired element is
        /// one element, and takes the first side; one that is not a line is labelled over its
        /// middle, or just above or below it for the one model it came from.
        /// </summary>
        private void DrawLabelBlock(DrawingContext dc, ElementViewModel element, Matrix modelToScreen,
                                    List<ResultField> fields, double pixelsPerDip)
        {
            List<FormattedText> lines = fields
                .Select(field => new FormattedText(
                    field.Value,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    LabelTypeface,
                    LabelFontSize,
                    field.IsFailure ? ViewPalette.LabelFailureText : ViewPalette.LabelText,
                    pixelsPerDip))
                .ToList();

            double blockWidth = lines.Max(line => line.Width);
            double blockHeight = lines.Count * LabelLineHeight;

            Point anchor = modelToScreen.Transform(element.LabelAnchor);
            double angle;
            double top;
            PlaceLabel(element, modelToScreen, blockHeight, out angle, out top);

            double left = -blockWidth / 2;

            dc.PushTransform(new TranslateTransform(anchor.X, anchor.Y));
            dc.PushTransform(new RotateTransform(angle));

            dc.DrawRectangle(ViewPalette.LabelBackground, null,
                new Rect(left - 3, top - 2, blockWidth + 6, blockHeight + 4));

            for (int i = 0; i < lines.Count; i++)
            {
                dc.DrawText(lines[i], new Point(left, top + i * LabelLineHeight));
            }

            dc.Pop();
            dc.Pop();
        }

        /// <summary>
        /// Where a label goes, in screen pixels about the element's anchor: how far it is turned,
        /// in degrees clockwise, and where its top edge sits across the line once it is - above
        /// the line is negative. The block is centred along the line either way.
        /// </summary>
        /// <param name="blockHeight">How tall the label is, all its lines together.</param>
        internal static void PlaceLabel(ElementViewModel element, Matrix modelToScreen, double blockHeight,
                                        out double angle, out double top)
        {
            bool revitSide = element.Source == ElementSource.RevitOnly
                             || element.Source == ElementSource.UnmatchedRevit;

            angle = 0;

            Vector? along = element.LabelDirection;
            Vector onScreen = along.HasValue ? modelToScreen.Transform(along.Value) : new Vector();

            if (onScreen.Length > 1e-9)
            {
                // Pointing right, or straight up the screen where it is upright, so the label
                // is never read upside down whichever end the element was drawn from.
                if (onScreen.X < -1e-9 || (Math.Abs(onScreen.X) <= 1e-9 && onScreen.Y > 0))
                {
                    onScreen = -onScreen;
                }
                angle = Math.Atan2(onScreen.Y, onScreen.X) * 180 / Math.PI;

                // Clear of what is drawn - the whole thickness of a wall, not only its middle.
                // The matrix's own scale, so it is right at whatever zoom the plan is at.
                double pixelsPerMillimetre = Math.Abs(modelToScreen.M11);
                double clearance = element.LabelClearance * pixelsPerMillimetre + LabelGapPixels;
                top = revitSide ? clearance : -clearance - blockHeight;
            }
            else if (element.Source == ElementSource.Paired)
            {
                top = -blockHeight / 2;
            }
            else
            {
                top = revitSide ? LabelGapPixels : -LabelGapPixels - blockHeight;
            }
        }

        private static readonly Pen PickHaloPen = Frozen(new Pen(ViewPalette.PickingHalo, 4.5));
        private static readonly Pen PickInkPen = Frozen(new Pen(ViewPalette.Picking, 2));
        private static readonly Pen CrosshairPen = Frozen(new Pen(ViewPalette.Picking, 1));

        private static Pen Frozen(Pen pen)
        {
            pen.Freeze();
            return pen;
        }

        /// <summary>
        /// What a pick in progress puts on the plan: the band saying which point is being
        /// asked for, and under the cursor the crosshair and a marker saying what it has
        /// caught hold of.
        /// </summary>
        private void DrawPickOverlay(DrawingContext dc)
        {
            if (pickRequest == null)
            {
                return;
            }

            string note = pickPreview == null
                ? "Left click to place the point. Escape or right click stops. Ctrl+drag still pans."
                : pickPreview.Description + " at " + InMetres(pickPreview.At)
                  + "  -  left click to place it, escape to stop";

            DrawBanner(dc, pickRequest.Prompt, note, VisualTreeHelper.GetDpi(this).PixelsPerDip);

            if (pickPreview == null)
            {
                return;
            }

            Point at = ModelToScreen.Transform(pickPreview.At);
            DrawCrosshair(dc, at);
            DrawSnapMarker(dc, at, pickPreview.Kind);
        }

        private void DrawBanner(DrawingContext dc, string heading, string note, double pixelsPerDip)
        {
            FormattedText prompt = Written(heading, 13, FontWeights.SemiBold,
                                           ViewPalette.PickBannerText, pixelsPerDip);
            FormattedText detail = Written(note, 11, FontWeights.Normal,
                                           ViewPalette.PickBannerNote, pixelsPerDip);

            double width = Math.Max(prompt.Width, detail.Width) + 28;
            double height = prompt.Height + detail.Height + 20;
            double left = Math.Max(8, (ActualWidth - width) / 2);
            const double top = 12;

            dc.DrawRoundedRectangle(ViewPalette.PickBanner, null,
                                    new Rect(left, top, width, height), 4, 4);
            dc.DrawText(prompt, new Point(left + 14, top + 9));
            dc.DrawText(detail, new Point(left + 14, top + 11 + prompt.Height));
        }

        private static FormattedText Written(string text, double size, FontWeight weight,
                                             Brush brush, double pixelsPerDip)
        {
            return new FormattedText(text ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
                size, brush, pixelsPerDip);
        }

        /// <summary>The plan is drawn in millimetres; an alignment is talked about in metres.</summary>
        private static string InMetres(Point modelPoint)
        {
            return Math.Round(modelPoint.X / 1000d, 3).ToString("0.###", CultureInfo.CurrentCulture)
                   + ", "
                   + Math.Round(modelPoint.Y / 1000d, 3).ToString("0.###", CultureInfo.CurrentCulture)
                   + " m";
        }

        /// <summary>Four arms with a gap in the middle, so the marker itself stays readable.</summary>
        private static void DrawCrosshair(DrawingContext dc, Point at)
        {
            const double gap = SnapMarkerRadiusInPixels + 4;
            const double arm = CrosshairArmInPixels;

            dc.DrawLine(CrosshairPen, new Point(at.X - arm, at.Y), new Point(at.X - gap, at.Y));
            dc.DrawLine(CrosshairPen, new Point(at.X + gap, at.Y), new Point(at.X + arm, at.Y));
            dc.DrawLine(CrosshairPen, new Point(at.X, at.Y - arm), new Point(at.X, at.Y - gap));
            dc.DrawLine(CrosshairPen, new Point(at.X, at.Y + gap), new Point(at.X, at.Y + arm));
        }

        /// <summary>
        /// A different shape for each kind of snap, drawn over a white halo so it reads on
        /// dark geometry as well as on light. Which shape means what is the convention every
        /// engineer using this already has from CAD: a square is an end, a circle a centre,
        /// a cross a crossing.
        /// </summary>
        private static void DrawSnapMarker(DrawingContext dc, Point at, SnapKind kind)
        {
            const double r = SnapMarkerRadiusInPixels;

            switch (kind)
            {
                case SnapKind.Endpoint:
                case SnapKind.Corner:
                    Rect square = new Rect(at.X - r, at.Y - r, r * 2, r * 2);
                    dc.DrawRectangle(null, PickHaloPen, square);
                    dc.DrawRectangle(null, PickInkPen, square);
                    break;

                case SnapKind.Centre:
                    dc.DrawEllipse(null, PickHaloPen, at, r, r);
                    dc.DrawEllipse(null, PickInkPen, at, r, r);
                    dc.DrawEllipse(ViewPalette.Picking, null, at, 1.6, 1.6);
                    break;

                case SnapKind.Intersection:
                    foreach (Pen pen in new[] { PickHaloPen, PickInkPen })
                    {
                        dc.DrawLine(pen, new Point(at.X - r, at.Y - r), new Point(at.X + r, at.Y + r));
                        dc.DrawLine(pen, new Point(at.X - r, at.Y + r), new Point(at.X + r, at.Y - r));
                    }
                    break;

                case SnapKind.Midpoint:
                    DrawShape(dc, PickHaloPen, at, new Point(0, -r), new Point(r, r), new Point(-r, r));
                    DrawShape(dc, PickInkPen, at, new Point(0, -r), new Point(r, r), new Point(-r, r));
                    break;

                case SnapKind.Edge:
                    DrawShape(dc, PickHaloPen, at, new Point(0, -r), new Point(r, 0),
                              new Point(0, r), new Point(-r, 0));
                    DrawShape(dc, PickInkPen, at, new Point(0, -r), new Point(r, 0),
                              new Point(0, r), new Point(-r, 0));
                    break;

                default:
                    // Nothing was in reach, so there is only the point itself to show.
                    dc.DrawEllipse(ViewPalette.PickingHalo, null, at, 3.4, 3.4);
                    dc.DrawEllipse(ViewPalette.Picking, null, at, 1.8, 1.8);
                    break;
            }
        }

        /// <summary>A closed outline given as offsets from the marker's centre.</summary>
        private static void DrawShape(DrawingContext dc, Pen pen, Point at, params Point[] offsets)
        {
            Point[] points = offsets.Select(offset => new Point(at.X + offset.X, at.Y + offset.Y)).ToArray();

            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(points[0], false, true);
                context.PolyLineTo(points.Skip(1).ToArray(), true, true);
            }
            geometry.Freeze();

            dc.DrawGeometry(null, pen, geometry);
        }

        // Measurements are drawn in the tool colour, the one nothing in either model is drawn
        // in, so a dimension laid over a beam is never read as part of the beam.
        private const double MeasureTickInPixels = 7;
        private const double MeasureLabelGapInPixels = 8;
        private static readonly Pen MeasureHaloPen = Frozen(new Pen(ViewPalette.PickingHalo, 5));
        private static readonly Pen MeasureInkPen = Frozen(new Pen(ViewPalette.Picking, 1.6));
        private static readonly Pen MeasurePreviewPen = Frozen(new Pen(ViewPalette.Picking, 1.6)
        {
            DashStyle = new DashStyle(new double[] { 4, 3 }, 0)
        });
        private static readonly Pen MeasureLabelEdge = Frozen(new Pen(ViewPalette.Picking, 1));

        /// <summary>Every measurement taken, kept on the plan until an element is clicked.</summary>
        private void DrawMeasurements(DrawingContext dc)
        {
            if (measurements.Count == 0)
            {
                return;
            }

            Matrix modelToScreen = ModelToScreen;
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            foreach (Measurement measurement in measurements)
            {
                DrawMeasurement(dc, modelToScreen, measurement.From, measurement.To, false, pixelsPerDip);
            }
        }

        /// <summary>
        /// One measurement, drawn the way a dimension is on a drawing: the line between the two
        /// points, a tick across each end, and the length beside the middle of it.
        /// </summary>
        private void DrawMeasurement(DrawingContext dc, Matrix modelToScreen, Point from, Point to,
                                     bool preview, double pixelsPerDip)
        {
            Point a = modelToScreen.Transform(from);
            Point b = modelToScreen.Transform(to);
            Vector along = b - a;
            double length = along.Length;

            // Square to the line, and turned to point up the screen, which is the side the
            // length is written on.
            Vector across = length > 1e-6 ? new Vector(-along.Y / length, along.X / length) : new Vector(0, -1);
            if (across.Y > 0 || (Math.Abs(across.Y) < 1e-9 && across.X < 0))
            {
                across = -across;
            }

            dc.DrawLine(MeasureHaloPen, a, b);
            dc.DrawLine(preview ? MeasurePreviewPen : MeasureInkPen, a, b);

            foreach (Point end in new[] { a, b })
            {
                Point one = end + across * MeasureTickInPixels;
                Point other = end - across * MeasureTickInPixels;
                dc.DrawLine(MeasureHaloPen, one, other);
                dc.DrawLine(MeasureInkPen, one, other);
            }

            DrawMeasureLabel(dc, new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2), across, from, to, pixelsPerDip);
        }

        /// <summary>
        /// The length, and under it how far across and how far up the plan that is when the
        /// line runs at an angle - the two numbers a setting out is checked against. Written
        /// level whatever the line does, beside its middle on the side facing up the screen.
        /// </summary>
        private static void DrawMeasureLabel(DrawingContext dc, Point middle, Vector across,
                                             Point from, Point to, double pixelsPerDip)
        {
            double dx = Math.Abs(to.X - from.X);
            double dy = Math.Abs(to.Y - from.Y);

            FormattedText length = Written(InMillimetres(Math.Sqrt(dx * dx + dy * dy)), 13, FontWeights.SemiBold,
                                           ViewPalette.LabelText, pixelsPerDip);
            FormattedText parts = dx >= 1 && dy >= 1
                ? Written("ΔX " + InMillimetres(dx) + "   ΔY " + InMillimetres(dy), 11,
                          FontWeights.Normal, ViewPalette.MeasureNote, pixelsPerDip)
                : null;

            double width = Math.Max(length.Width, parts?.Width ?? 0) + 12;
            double height = length.Height + (parts?.Height ?? 0) + 6;

            // Far enough out along the square that the box clears the line whichever way the
            // line runs: half its height for a level line, half its width for an upright one.
            double reach = Math.Abs(across.X) * width / 2 + Math.Abs(across.Y) * height / 2 + MeasureLabelGapInPixels;
            Point centre = middle + across * reach;
            Rect box = new Rect(centre.X - width / 2, centre.Y - height / 2, width, height);

            dc.DrawRoundedRectangle(ViewPalette.MeasureLabel, MeasureLabelEdge, box, 3, 3);
            dc.DrawText(length, new Point(box.Left + (width - length.Width) / 2, box.Top + 3));
            if (parts != null)
            {
                dc.DrawText(parts, new Point(box.Left + (width - parts.Width) / 2, box.Top + 3 + length.Height));
            }
        }

        /// <summary>What the measure tool puts over the plan while it is on.</summary>
        private void DrawMeasureOverlay(DrawingContext dc)
        {
            if (!isMeasuring)
            {
                return;
            }

            Matrix modelToScreen = ModelToScreen;
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            if (measureStart.HasValue && measurePreview != null)
            {
                DrawMeasurement(dc, modelToScreen, measureStart.Value, measurePreview.At, true, pixelsPerDip);
            }
            else if (measureStart.HasValue)
            {
                DrawSnapMarker(dc, modelToScreen.Transform(measureStart.Value), SnapKind.Free);
            }

            string heading = measureStart.HasValue
                ? "Measure - click the second point"
                : "Measure - click the first point";

            string note;
            if (measurePreview == null)
            {
                note = "Snaps to ends, corners, centres, midpoints and crossings in either model. "
                       + "Right click or escape stops. Ctrl+drag still pans.";
            }
            else
            {
                note = measurePreview.Description + " at " + InMetres(measurePreview.At);
                note += measureStart.HasValue
                    ? "  -  right click or escape drops the first point"
                    : "  -  right click or escape stops";
            }

            DrawBanner(dc, heading, note, pixelsPerDip);

            if (measurePreview != null)
            {
                Point at = modelToScreen.Transform(measurePreview.At);
                DrawCrosshair(dc, at);
                DrawSnapMarker(dc, at, measurePreview.Kind);
            }
        }

        /// <summary>A length on the plan, in the millimetres both models are drawn in.</summary>
        private static string InMillimetres(double millimetres)
        {
            return Math.Round(millimetres).ToString("N0", CultureInfo.CurrentCulture) + " mm";
        }

        #endregion

        #region interaction

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            ZoomAt(e.GetPosition(this), e.Delta > 0 ? ZoomStep : 1 / ZoomStep);

            // The cursor has not moved but the model under it has, so what it would snap to
            // is a different point now.
            if (pickRequest != null)
            {
                UpdatePickPreview(e.GetPosition(this));
            }
            if (isMeasuring)
            {
                UpdateMeasurePreview(e.GetPosition(this));
            }
            if (drawingZone != null)
            {
                UpdateZonePreview(e.GetPosition(this));
            }

            e.Handled = true;
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            // A pick in progress takes the plain left click and the right button. Ctrl+drag
            // is left alone so the view can still be panned while the point is lined up.
            if (pickRequest != null && (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
            {
                if (e.ChangedButton == MouseButton.Right)
                {
                    CancelPointPick();
                    e.Handled = true;
                    return;
                }

                if (e.ChangedButton == MouseButton.Left)
                {
                    CommitPointPick(e.GetPosition(this));
                    e.Handled = true;
                    return;
                }
            }

            // The measure tool takes the same buttons the same way: a left click places a
            // point, a right click steps back, and ctrl+drag and the middle button still pan.
            if (isMeasuring && (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
            {
                if (e.ChangedButton == MouseButton.Right)
                {
                    StepBackMeasuring();
                    e.Handled = true;
                    return;
                }

                if (e.ChangedButton == MouseButton.Left)
                {
                    PlaceMeasurePoint(e.GetPosition(this));
                    e.Handled = true;
                    return;
                }
            }

            // Drawing a height zone takes them the same way again. A double click finishes a
            // polygon: its first click has already put the last corner down.
            if (drawingZone != null && (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
            {
                if (e.ChangedButton == MouseButton.Right)
                {
                    StepBackZone();
                    e.Handled = true;
                    return;
                }

                if (e.ChangedButton == MouseButton.Left)
                {
                    if (e.ClickCount >= 2 && drawingZone == ZoneShape.Polygon)
                    {
                        FinishPolygon();
                    }
                    else
                    {
                        PlaceZoneCorner(e.GetPosition(this));
                    }
                    e.Handled = true;
                    return;
                }
            }

            if (e.ChangedButton == MouseButton.Middle ||
                (e.ChangedButton == MouseButton.Left && Keyboard.Modifiers == ModifierKeys.Control))
            {
                isPanning = true;
                pressWasCtrl = e.ChangedButton == MouseButton.Left;
                movedWhilePanning = false;
                pressedAt = e.GetPosition(this);
                lastMousePosition = pressedAt;
                CaptureMouse();
                Cursor = Cursors.SizeAll;
                e.Handled = true;
                return;
            }

            // Plain and shift presses are decided on the way up: held still it was a click,
            // dragged it was a window. Shift takes out of the selection rather than adding to it.
            if (e.ChangedButton == MouseButton.Left)
            {
                selectPressPending = true;
                selectPressWasShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                isWindowing = false;
                pressedAt = e.GetPosition(this);
                windowEnd = pressedAt;
                CaptureMouse();
                e.Handled = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (pickRequest != null)
            {
                UpdatePickPreview(e.GetPosition(this));
            }
            if (isMeasuring)
            {
                UpdateMeasurePreview(e.GetPosition(this));
            }
            if (drawingZone != null)
            {
                UpdateZonePreview(e.GetPosition(this));
            }

            if (selectPressPending)
            {
                windowEnd = e.GetPosition(this);
                double wx = windowEnd.X - pressedAt.X;
                double wy = windowEnd.Y - pressedAt.Y;
                if (isWindowing || Math.Sqrt(wx * wx + wy * wy) > DragThresholdInPixels)
                {
                    isWindowing = true;
                    InvalidateVisual();
                }
                return;
            }

            if (!isPanning)
            {
                return;
            }

            Point current = e.GetPosition(this);

            double dx = current.X - pressedAt.X;
            double dy = current.Y - pressedAt.Y;
            if (Math.Sqrt(dx * dx + dy * dy) > DragThresholdInPixels)
            {
                movedWhilePanning = true;
            }

            origin = new Point(origin.X + (current.X - lastMousePosition.X),
                               origin.Y + (current.Y - lastMousePosition.Y));
            lastMousePosition = current;
            InvalidateVisual();
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);

            if (selectPressPending && e.ChangedButton == MouseButton.Left)
            {
                bool remove = selectPressWasShift;
                bool wasWindow = isWindowing;
                Point at = e.GetPosition(this);

                EndSelectPress();

                // Any click that selects - on an element or on nothing, a window, a shift to
                // take out - is the plan being used for something else now.
                ClearMeasurements();

                if (wasWindow)
                {
                    IList<ElementViewModel> caught = InWindow(pressedAt, at);
                    if (remove)
                    {
                        DeselectRequested?.Invoke(caught);
                    }
                    else
                    {
                        WindowSelected?.Invoke(caught);
                    }
                }
                else if (remove)
                {
                    // The nearest selected element under the cursor, not the nearest of all:
                    // where two lie on top of each other only one of them may be selected, and
                    // a shift+click that landed on the other would take nothing out.
                    ElementViewModel hit = PickAll(at, lookInsideFoldedWalls: true)
                        .Select(h => h.Element)
                        .FirstOrDefault(element => element.IsSelected);
                    if (hit != null)
                    {
                        DeselectRequested?.Invoke(new[] { hit });
                    }
                }
                else
                {
                    SelectOnly(Pick(at));
                }

                e.Handled = true;
                return;
            }

            if (isPanning)
            {
                isPanning = false;
                ReleaseMouseCapture();
                Cursor = Cursors.Arrow;

                // Ctrl and the hand held still: this was a question about what is under the
                // cursor, not a pan. Not while measuring, where the click is for a point.
                if (pressWasCtrl && !movedWhilePanning && !isMeasuring && drawingZone == null)
                {
                    AskWhichElement(e.GetPosition(this));
                }
                pressWasCtrl = false;
            }
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);

            // Focus taken away mid drag - a dialog, alt+tab - drops the window rather than
            // leaving a rectangle stuck to the plan.
            if (selectPressPending)
            {
                EndSelectPress();
            }
        }

        private void EndSelectPress()
        {
            bool wasWindowing = isWindowing;
            selectPressPending = false;
            isWindowing = false;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }
            if (wasWindowing)
            {
                InvalidateVisual();
            }
        }

        /// <summary>
        /// Selects one element on its own, or nothing. Through the view model where one is
        /// listening, because a selection of several is not something the one element property
        /// can say - clearing it when it is already clear would change nothing and leave the
        /// several selected.
        /// </summary>
        private void SelectOnly(ElementViewModel element)
        {
            if (SelectOnlyRequested != null)
            {
                SelectOnlyRequested(element);
            }
            else
            {
                SelectedElement = element;
            }
        }

        /// <summary>
        /// What a window dragged between two screen points takes in.
        ///
        /// Dragged left to right it is a window and takes only what lies wholly inside it;
        /// right to left it is a crossing and takes anything it touches. The rule every CAD
        /// user already has in their hands, and the one that lets a single beam be picked out
        /// of a crowd - or a crowd be picked up without first finding a clear place to start.
        /// </summary>
        private IList<ElementViewModel> InWindow(Point screenFrom, Point screenTo)
        {
            List<ElementViewModel> caught = new List<ElementViewModel>();
            if (Elements == null)
            {
                return caught;
            }

            Rect window = new Rect(ScreenToModel(screenFrom), ScreenToModel(screenTo));
            bool crossing = screenTo.X < screenFrom.X;

            RectangleGeometry windowGeometry = new RectangleGeometry(window);
            windowGeometry.Freeze();
            Pen thin = new Pen(Brushes.Black, 1 / scale);
            thin.Freeze();

            foreach (ElementViewModel element in Elements)
            {
                // The same things a click will not pick, for the same reason.
                if (element.IsDrawnAsContext)
                {
                    continue;
                }

                Rect bounds = element.Geometry.Bounds;
                if (bounds.IsEmpty)
                {
                    continue;
                }

                if (window.Contains(bounds))
                {
                    caught.Add(element);
                    continue;
                }

                if (!crossing || !window.IntersectsWith(bounds))
                {
                    continue;
                }

                // The bounds only say it might be touched: a diagonal beam's box crosses far
                // more of the plan than the beam does. The outline, and for a filled shape
                // what it encloses, say whether it really is.
                Geometry geometry = element.Geometry;
                if (Touches(geometry.StrokeContainsWithDetail(thin, windowGeometry))
                    || (element.IsFilled && Touches(geometry.FillContainsWithDetail(windowGeometry))))
                {
                    caught.Add(element);
                }
            }

            return caught;
        }

        private static bool Touches(IntersectionDetail detail)
        {
            return detail != IntersectionDetail.Empty && detail != IntersectionDetail.NotCalculated;
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo info)
        {
            base.OnRenderSizeChanged(info);
            if (pendingZoomToFit)
            {
                pendingZoomToFit = false;
                ZoomToFit();
            }
            InvalidateVisual();
        }

        /// <summary>
        /// Asked when several elements sit under the cursor and the person wants to say which
        /// one they meant. Given every hit, nearest first; a plain click cannot do this because
        /// it has to answer immediately and can only guess.
        /// </summary>
        public Action<IList<PickHit>> ChooseFrom { get; set; }

        /// <summary>A plain click: this element on its own, or null for a click on nothing.</summary>
        public Action<ElementViewModel> SelectOnlyRequested { get; set; }

        /// <summary>A window dragged without shift: these join the selection.</summary>
        public Action<IList<ElementViewModel>> WindowSelected { get; set; }

        /// <summary>Shift+click or shift+drag: these leave the selection.</summary>
        public Action<IList<ElementViewModel>> DeselectRequested { get; set; }

        /// <summary>Escape with nothing else to call off: the selection goes.</summary>
        public Action SelectionCleared { get; set; }

        private void AskWhichElement(Point screenPoint)
        {
            IList<PickHit> under = PickAll(screenPoint, lookInsideFoldedWalls: true);
            if (under.Count == 0)
            {
                return;
            }

            // One thing under the cursor needs no question asked about it.
            if (under.Count == 1 || ChooseFrom == null)
            {
                SelectOnly(under[0].Element);
                return;
            }

            ChooseFrom(under);
        }

        /// <summary>Nearest element within a screen-space tolerance of the cursor, or null.</summary>
        private ElementViewModel Pick(Point screenPoint)
        {
            return PickAll(screenPoint, lookInsideFoldedWalls: false)
                   .Select(hit => hit.Element).FirstOrDefault();
        }

        /// <summary>
        /// Everything within picking distance of the cursor, nearest first.
        ///
        /// A column standing where two beams meet, or a wall drawn over the beam beside it, puts
        /// several answers under one pixel. The nearest is the right guess for a plain click and
        /// the wrong one often enough that there has to be a way to say which was meant.
        /// </summary>
        private IList<PickHit> PickAll(Point screenPoint, bool lookInsideFoldedWalls)
        {
            List<PickHit> found = new List<PickHit>();
            if (Elements == null)
            {
                return found;
            }

            Point modelPoint = ScreenToModel(screenPoint);
            double toleranceInModelUnits = PickToleranceInPixels / scale;

            List<PickHit> hits = new List<PickHit>();

            foreach (ElementViewModel element in Elements)
            {
                // Context belongs to the other check, and a slab under the cursor is a hit
                // everywhere inside it, so leaving it selectable would put a grey backdrop in
                // front of every beam on the floor.
                if (element.IsDrawnAsContext)
                {
                    continue;
                }

                double distance = element.DistanceTo(modelPoint);
                if (distance > toleranceInModelUnits)
                {
                    continue;
                }

                // A wall drawn as one thing but standing for several panels offers the panels
                // instead of itself: the folded wall is what the plan draws, and the panels are
                // what a person is asking about when they ctrl+click through it.
                if (element.HasParts && lookInsideFoldedWalls)
                {
                    // The wall as a whole first - it is what the plan draws and what a plain
                    // click gives, and its card carries the panels' results merged - then the
                    // panels themselves for whoever wanted one of those instead.
                    hits.Add(new PickHit(element, distance));
                    foreach (ElementViewModel part in element.Parts)
                    {
                        hits.Add(new PickHit(part, distance));
                    }
                    continue;
                }

                hits.Add(new PickHit(element, distance));
            }

            // Nearest first. Nothing is filtered by kind: two columns in the same place, or an
            // unpaired ETABS beam lying on the Revit beam it should have paired with, are
            // exactly the cases this exists for - and they are the same kind as each other.
            found.AddRange(hits.OrderBy(hit => hit.DistanceMM));
            return found;
        }

        #endregion

        #region picking a point off the plan

        /// <summary>True while the plan is waiting for a point to be picked on it.</summary>
        public bool IsPickingPoint => pickRequest != null;

        /// <summary>
        /// Asks for a point on the plan, snapped to the model the request names. Panning,
        /// zooming and the wheel all carry on working underneath, because the point wanted is
        /// often not on screen yet when the pick starts. A left click answers it; escape or a
        /// right click calls it off.
        /// </summary>
        public void BeginPointPick(PlanPickRequest request)
        {
            if (request == null)
            {
                return;
            }

            // A second request replaces the first rather than queueing behind it: two prompts
            // on one plan and there would be no saying which click answered which. The measure
            // tool and a zone being drawn are put down for the same reason.
            CancelPointPick();
            EndMeasuring();
            EndZone();

            pickRequest = request;
            pickPreview = null;
            Cursor = Cursors.Cross;
            Focus();
            InvalidateVisual();
        }

        /// <summary>Calls off a pick in progress. Does nothing when there is none.</summary>
        public void CancelPointPick()
        {
            PlanPickRequest request = pickRequest;
            if (request == null)
            {
                return;
            }

            EndPointPick();
            request.OnCancelled?.Invoke();
        }

        private void CommitPointPick(Point screenPoint)
        {
            PlanPickRequest request = pickRequest;
            SnapResult snapped = Snap(screenPoint, request);
            EndPointPick();
            request?.OnPicked?.Invoke(snapped.At);
        }

        private void EndPointPick()
        {
            pickRequest = null;
            pickPreview = null;
            Cursor = Cursors.Arrow;
            InvalidateVisual();
        }

        private void UpdatePickPreview(Point screenPoint)
        {
            pickPreview = Snap(screenPoint, pickRequest);
            InvalidateVisual();
        }

        /// <summary>Where a click at this screen point would land, in model mm.</summary>
        private SnapResult Snap(Point screenPoint, PlanPickRequest request)
        {
            Point modelPoint = ScreenToModel(screenPoint);
            if (request == null)
            {
                return new SnapResult(modelPoint, SnapKind.Free);
            }

            // The reach is fixed on screen rather than in the model, so it stays the same
            // distance under the hand however far the plan is zoomed out.
            return PlanSnap.Nearest(Elements, modelPoint, SnapToleranceInPixels / scale, request.Side);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Key == Key.Escape && pickRequest != null)
            {
                CancelPointPick();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape && isMeasuring)
            {
                StepBackMeasuring();
                e.Handled = true;
                return;
            }

            if (drawingZone != null)
            {
                if (e.Key == Key.Escape)
                {
                    EndZone();
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Back)
                {
                    StepBackZone();
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Enter && drawingZone == ZoneShape.Polygon)
                {
                    FinishPolygon();
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Escape)
            {
                if (selectPressPending)
                {
                    EndSelectPress();
                }
                else
                {
                    ClearMeasurements();
                    SelectionCleared?.Invoke();
                }
                e.Handled = true;
            }
        }

        #endregion

        #region measuring

        /// <summary>True while clicks on the plan are placing measure points.</summary>
        public bool IsMeasuring => isMeasuring;

        /// <summary>Told when the tool goes on or off, including when the plan puts it down itself.</summary>
        public Action<bool> MeasuringChanged { get; set; }

        /// <summary>
        /// Picks up the measure tool: two clicks make a measurement, snapped to the model, and
        /// the tool stays up for the next until it is put down. A right click or escape drops a
        /// first point; with none placed, it puts the tool down.
        /// </summary>
        public void BeginMeasuring()
        {
            if (isMeasuring)
            {
                return;
            }

            // One tool at a time: a click cannot both place an alignment point and a measure one.
            CancelPointPick();
            EndZone();
            if (selectPressPending)
            {
                EndSelectPress();
            }

            isMeasuring = true;
            measureStart = null;
            measurePreview = null;
            Cursor = Cursors.Cross;
            Focus();
            InvalidateVisual();
            MeasuringChanged?.Invoke(true);
        }

        /// <summary>
        /// Puts the measure tool down. What has been measured stays on the plan; a first point
        /// with no second goes.
        /// </summary>
        public void EndMeasuring()
        {
            if (!isMeasuring)
            {
                return;
            }

            isMeasuring = false;
            measureStart = null;
            measurePreview = null;
            Cursor = Cursors.Arrow;
            InvalidateVisual();
            MeasuringChanged?.Invoke(false);
        }

        /// <summary>Takes every finished measurement off the plan.</summary>
        public void ClearMeasurements()
        {
            if (measurements.Count == 0)
            {
                return;
            }

            measurements.Clear();
            InvalidateVisual();
        }

        /// <summary>
        /// The plan has been rebuilt, so what was measured - and a first point waiting for its
        /// second - was measured on a drawing that is not there any more. The tool stays up.
        /// </summary>
        private void DropMeasurementsWithThePlan()
        {
            measurements.Clear();
            measureStart = null;
            measurePreview = null;

            // So do the corners of a zone half drawn - on another level, say, where they would
            // be the corners of a zone nobody drew there.
            zoneCorners.Clear();
            zonePreview = null;
        }

        private void PlaceMeasurePoint(Point screenPoint)
        {
            SnapResult snapped = SnapForMeasuring(screenPoint);

            if (!measureStart.HasValue)
            {
                measureStart = snapped.At;
            }
            else
            {
                // The same point twice measures nothing, and would leave a "0 mm" on the plan.
                if ((snapped.At - measureStart.Value).Length > 1e-6)
                {
                    measurements.Add(new Measurement(measureStart.Value, snapped.At));
                }
                measureStart = null;
            }

            measurePreview = snapped;
            InvalidateVisual();
        }

        /// <summary>Drops the first point if there is one, otherwise puts the tool down.</summary>
        private void StepBackMeasuring()
        {
            if (measureStart.HasValue)
            {
                measureStart = null;
                InvalidateVisual();
                return;
            }

            EndMeasuring();
        }

        private void UpdateMeasurePreview(Point screenPoint)
        {
            measurePreview = SnapForMeasuring(screenPoint);
            InvalidateVisual();
        }

        /// <summary>
        /// Where a measure click at this screen point lands. Snapped to both models, unlike an
        /// alignment point: the gap between an ETABS element and the Revit one it should sit on
        /// is one of the things most worth measuring.
        /// </summary>
        private SnapResult SnapForMeasuring(Point screenPoint)
        {
            return PlanSnap.Nearest(Elements, ScreenToModel(screenPoint), SnapToleranceInPixels / scale, null);
        }

        #endregion

        #region drawing a height zone

        private static readonly Pen ZoneEdgePen = Frozen(new Pen(ViewPalette.HeightZone, 2)
        {
            DashStyle = new DashStyle(new double[] { 5, 3 }, 0)
        });

        private static readonly Brush ZoneFill = Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0x15, 0x65, 0xC0)));

        private static Brush Frozen(Brush brush)
        {
            brush.Freeze();
            return brush;
        }

        /// <summary>True while clicks on the plan are placing the corners of a height zone.</summary>
        public bool IsDrawingZone => drawingZone != null;

        /// <summary>A zone finished, as its corners in model mm, in the order they were placed.</summary>
        public Action<IList<Point>> ZoneDrawn { get; set; }

        /// <summary>Told when drawing starts or stops, including when the plan stops it itself.</summary>
        public Action<bool> DrawingZoneChanged { get; set; }

        /// <summary>
        /// Starts drawing a height zone: a rectangle by two opposite corners, or a polygon by as
        /// many corners as it takes, finished by clicking the first corner again, a double click
        /// or Enter. Every corner snaps to the model the way a measure point does, so a zone can
        /// be drawn to the grid of walls and beams it is meant to follow.
        /// </summary>
        public void BeginZone(ZoneShape shape)
        {
            CancelPointPick();
            EndMeasuring();
            if (selectPressPending)
            {
                EndSelectPress();
            }

            drawingZone = shape;
            zoneCorners.Clear();
            zonePreview = null;
            Cursor = Cursors.Cross;
            Focus();
            InvalidateVisual();
            DrawingZoneChanged?.Invoke(true);
        }

        /// <summary>Stops drawing, keeping nothing of a zone not finished.</summary>
        public void EndZone()
        {
            if (drawingZone == null)
            {
                return;
            }

            drawingZone = null;
            zoneCorners.Clear();
            zonePreview = null;
            Cursor = Cursors.Arrow;
            InvalidateVisual();
            DrawingZoneChanged?.Invoke(false);
        }

        private void PlaceZoneCorner(Point screenPoint)
        {
            SnapResult snapped = SnapForMeasuring(screenPoint);
            Point at = snapped.At;

            if (drawingZone == ZoneShape.Rectangle)
            {
                if (zoneCorners.Count == 0)
                {
                    zoneCorners.Add(at);
                }
                else
                {
                    Point first = zoneCorners[0];
                    if (Math.Abs(at.X - first.X) > 1 && Math.Abs(at.Y - first.Y) > 1)
                    {
                        Finish(RectangleCorners(first, at));
                        return;
                    }
                }
            }
            else
            {
                // Back on the first corner closes it, the way a polyline is closed in any
                // drawing package - measured on screen, so it is as easy at any zoom.
                if (zoneCorners.Count >= 3
                    && (ModelToScreen.Transform(zoneCorners[0]) - ModelToScreen.Transform(at)).Length
                       <= SnapToleranceInPixels)
                {
                    FinishPolygon();
                    return;
                }

                if (zoneCorners.Count == 0 || (at - zoneCorners[zoneCorners.Count - 1]).Length > 1)
                {
                    zoneCorners.Add(at);
                }
            }

            zonePreview = snapped;
            InvalidateVisual();
        }

        private void FinishPolygon()
        {
            if (drawingZone != ZoneShape.Polygon || zoneCorners.Count < 3)
            {
                return;
            }
            Finish(zoneCorners.ToList());
        }

        private void Finish(IList<Point> corners)
        {
            EndZone();
            ZoneDrawn?.Invoke(corners);
        }

        /// <summary>Takes the last corner back; with none placed, stops drawing.</summary>
        private void StepBackZone()
        {
            if (zoneCorners.Count > 0)
            {
                zoneCorners.RemoveAt(zoneCorners.Count - 1);
                InvalidateVisual();
                return;
            }

            EndZone();
        }

        private void UpdateZonePreview(Point screenPoint)
        {
            zonePreview = SnapForMeasuring(screenPoint);
            InvalidateVisual();
        }

        /// <summary>The four corners of the rectangle two opposite corners stand for, in order round it.</summary>
        internal static IList<Point> RectangleCorners(Point one, Point opposite)
        {
            return new List<Point>
            {
                one,
                new Point(opposite.X, one.Y),
                opposite,
                new Point(one.X, opposite.Y)
            };
        }

        /// <summary>The zone so far, with the next corner where the cursor is, and the band saying what to do.</summary>
        private void DrawZoneOverlay(DrawingContext dc)
        {
            if (drawingZone == null)
            {
                return;
            }

            Matrix modelToScreen = ModelToScreen;
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            List<Point> shape = new List<Point>(zoneCorners);
            if (drawingZone == ZoneShape.Rectangle && zoneCorners.Count == 1 && zonePreview != null)
            {
                shape = RectangleCorners(zoneCorners[0], zonePreview.At).ToList();
            }
            else if (zonePreview != null && zoneCorners.Count > 0)
            {
                shape.Add(zonePreview.At);
            }

            if (shape.Count >= 2)
            {
                StreamGeometry outline = new StreamGeometry();
                using (StreamGeometryContext context = outline.Open())
                {
                    context.BeginFigure(modelToScreen.Transform(shape[0]), true, shape.Count >= 3);
                    context.PolyLineTo(shape.Skip(1).Select(point => modelToScreen.Transform(point)).ToArray(),
                                       true, true);
                }
                outline.Freeze();
                dc.DrawGeometry(shape.Count >= 3 ? ZoneFill : null, ZoneEdgePen, outline);
            }

            foreach (Point corner in zoneCorners)
            {
                Point at = modelToScreen.Transform(corner);
                dc.DrawRectangle(ViewPalette.HeightZone, null, new Rect(at.X - 3, at.Y - 3, 6, 6));
            }

            string heading;
            if (drawingZone == ZoneShape.Rectangle)
            {
                heading = zoneCorners.Count == 0
                    ? "Height zone - click one corner of the rectangle"
                    : "Height zone - click the opposite corner";
            }
            else
            {
                heading = zoneCorners.Count < 3
                    ? "Height zone - click the corners in order round it"
                    : "Height zone - click the next corner, or the first one again to finish";
            }

            string note = zonePreview == null
                ? "Snaps to ends, corners, centres, midpoints and crossings. "
                : zonePreview.Description + " at " + InMetres(zonePreview.At) + "  -  ";
            note += drawingZone == ZoneShape.Polygon
                ? "Double click or Enter finishes. Backspace or right click takes the last corner back, escape stops."
                : "Backspace or right click takes the corner back, escape stops.";

            DrawBanner(dc, heading, note, pixelsPerDip);

            if (zonePreview != null)
            {
                Point at = modelToScreen.Transform(zonePreview.At);
                DrawCrosshair(dc, at);
                DrawSnapMarker(dc, at, zonePreview.Kind);
            }
        }

        #endregion
    }
}
