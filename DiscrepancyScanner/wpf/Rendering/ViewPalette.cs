using System.Windows.Media;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// The single place the viewer decides what colour an element is drawn in.
    /// Anything that passed every check is green, anything that failed one is red, and
    /// anything that passed but still wants a look - a column in more ETABS groups than the
    /// one carrying its type mark - is orange.
    ///
    /// Those colours, and the other three in the key, are the user's to change - see
    /// <see cref="VerdictColours"/>, which is where they are read from. Everything else here
    /// is fixed.
    /// </summary>
    public static class ViewPalette
    {
        public static Brush Passed => VerdictColours.BrushOf(VerdictColour.Passed);
        public static Brush Failed => VerdictColours.BrushOf(VerdictColour.Failed);
        public static Brush Review => VerdictColours.BrushOf(VerdictColour.Review);

        /// <summary>Something only one model has, in that model's colour.</summary>
        public static Brush UnpairedEtabs => VerdictColours.BrushOf(VerdictColour.EtabsOnly);
        public static Brush UnpairedRevit => VerdictColours.BrushOf(VerdictColour.RevitOnly);

        public static readonly Brush EtabsOnly = Freeze(new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)));
        public static readonly Brush RevitOnly = Freeze(new SolidColorBrush(Color.FromRgb(0x1F, 0x6F, 0xD6)));

        // Kept away from the orange a reviewed column is drawn in, so a selected element is
        // never mistaken for a flagged one.
        public static readonly Brush Selected = Freeze(new SolidColorBrush(Color.FromRgb(0x00, 0xBC, 0xD4)));

        /// <summary>
        /// The halo behind every selected element.
        ///
        /// A halo rather than a colour of its own, because a selected element has to keep the
        /// colour that says which model it came from. Deciding whether a selection makes sense
        /// - two elements about to be paired, twenty about to be accepted - is a matter of
        /// seeing which colours are in it, and recolouring them all alike would take that away.
        /// </summary>
        public static readonly Brush SelectionHalo =
            Freeze(new SolidColorBrush(Color.FromArgb(0x66, 0x00, 0xBC, 0xD4)));

        // The rectangle dragged out to select with. Blue and solid for a window, which takes
        // only what is wholly inside it; green and dashed for a crossing, which takes anything
        // it touches. The same two colours Revit and AutoCAD use, so the rule is read off the
        // colour rather than remembered.
        public static readonly Brush WindowFill =
            Freeze(new SolidColorBrush(Color.FromArgb(0x22, 0x00, 0x78, 0xD7)));
        public static readonly Brush WindowEdge = Freeze(new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD7)));
        public static readonly Brush CrossingFill =
            Freeze(new SolidColorBrush(Color.FromArgb(0x22, 0x1E, 0xA8, 0x4C)));
        public static readonly Brush CrossingEdge = Freeze(new SolidColorBrush(Color.FromRgb(0x1E, 0xA8, 0x4C)));

        /// <summary>
        /// A difference somebody has looked at and agreed. Only ever seen after a run, which is
        /// what keeps it clear of <see cref="RevitOnly"/>.
        /// </summary>
        public static Brush Accepted => VerdictColours.BrushOf(VerdictColour.Accepted);

        /// <summary>An element still on the plan whose result the filters have switched off.</summary>
        public static readonly Brush Muted = Freeze(new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)));

        /// <summary>
        /// The other check's elements, drawn behind the one being looked at so the floor still
        /// reads as a floor. Lighter than <see cref="Muted"/>, which is a result being hidden
        /// rather than something that was never this mode's business.
        /// </summary>
        public static readonly Brush Context = Freeze(new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)));

        /// <summary>
        /// The other model's outline, drawn under a checked element so the difference between
        /// the two can be seen: the ETABS section beneath a column Revit has somewhere else.
        ///
        /// The colour the ETABS model is drawn in on its own, because that is what this is. The
        /// two are never on the plan together - <see cref="EtabsOnly"/> is only seen before a
        /// comparison has run and this only after one - so sharing the colour costs nothing and
        /// says the right thing. Dark, because it has to read under the filled colour on top of
        /// it, which <see cref="Context"/> is deliberately too pale to do.
        /// </summary>
        public static readonly Brush Counterpart = EtabsOnly;

        /// <summary>
        /// A height zone drawn by hand. A deep blue that none of the verdicts, the two models
        /// or the selection use, so a zone laid over the line loads is never read as one of them.
        /// </summary>
        public static readonly Brush HeightZone = Freeze(new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)));

        private static readonly System.Collections.Generic.Dictionary<string, Brush> zoneBrushes =
            new System.Collections.Generic.Dictionary<string, Brush>(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// One zone's own colour, "#RRGGBB", as a brush - made once per colour. A zone with no
        /// colour, or one that does not read as a colour, is drawn in <see cref="HeightZone"/>.
        /// </summary>
        public static Brush ZoneBrush(string colour)
        {
            if (string.IsNullOrEmpty(colour))
            {
                return HeightZone;
            }

            Brush brush;
            if (zoneBrushes.TryGetValue(colour, out brush))
            {
                return brush;
            }

            try
            {
                brush = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(colour)));
            }
            catch (System.FormatException)
            {
                brush = HeightZone;
            }

            zoneBrushes[colour] = brush;
            return brush;
        }

        public static readonly Brush LabelText =Freeze(new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)));
        public static readonly Brush LabelFailureText = Freeze(new SolidColorBrush(Color.FromRgb(0xB3, 0x0F, 0x14)));
        public static readonly Brush LabelBackground = Freeze(new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)));

        public static readonly Brush Background = Freeze(new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)));

        /// <summary>
        /// The crosshair and the snap marker drawn while a point is being picked off the plan.
        /// A colour nothing else on the plan uses, because the whole job of the marker is to
        /// say "here, not there" over geometry drawn in every other colour there is.
        /// </summary>
        public static readonly Brush Picking = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0x0C, 0x8A)));

        /// <summary>Drawn under the marker so it reads over dark geometry as well as light.</summary>
        public static readonly Brush PickingHalo =
            Freeze(new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)));

        /// <summary>The band across the top of the plan saying which point is being asked for.</summary>
        public static readonly Brush PickBanner =
            Freeze(new SolidColorBrush(Color.FromArgb(0xF2, 0x14, 0x1A, 0x21)));

        public static readonly Brush PickBannerText = Freeze(new SolidColorBrush(Colors.White));

        /// <summary>The quieter half of the banner: the snap caught, and where the point is.</summary>
        public static readonly Brush PickBannerNote =
            Freeze(new SolidColorBrush(Color.FromRgb(0xA8, 0xB4, 0xC0)));

        /// <summary>
        /// The box a measurement's length is written in. Nearly solid, unlike an element's
        /// label, because a measurement is usually taken across the busiest part of the plan.
        /// </summary>
        public static readonly Brush MeasureLabel =
            Freeze(new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)));

        /// <summary>The across and up parts of a measurement, quieter than its length.</summary>
        public static readonly Brush MeasureNote =
            Freeze(new SolidColorBrush(Color.FromRgb(0x5A, 0x64, 0x70)));

        public static Brush ForState(ElementViewModel element)
        {
            if (element.IsSelected)
            {
                return Selected;
            }

            // The other model's section, under the element it was drawn to explain. Before the
            // context test rather than after it, because it is context too - not this check's
            // to pick or to label - and the backdrop grey would lose it under its own column.
            if (element.IsCounterpart)
            {
                return Counterpart;
            }

            // Belongs to the other check, so it is only here to say where things are.
            if (element.IsDrawnAsContext)
            {
                return Context;
            }

            // Drawn by hand and belonging to neither model, so in a colour of its own - each zone
            // its own, so two beside or over each other are told apart.
            if (element.Kind == ElementKind.HeightZone)
            {
                return ZoneBrush((element as HeightZoneElementViewModel)?.Colour);
            }

            // The raw data carries no verdict, so it is drawn in the colour of the model it was
            // read from: the same two colours the key gives to what only one model has, since
            // that is what each of them is until something pairs it.
            if (element.IsRaw)
            {
                return element.Source == ElementSource.RevitOnly ? UnpairedRevit : UnpairedEtabs;
            }

            // A filtered-out result leaves the element on the plan in grey rather than
            // removing it, so what is drawn stays a complete floor either way.
            if (element.IsMuted)
            {
                return Muted;
            }

            // Agreed differences come after the filters and before the verdicts: something
            // switched off is grey whatever anybody said about it, but something on show that
            // has been accepted is settled and must stop reading as a failure.
            if (element.IsAccepted)
            {
                return Accepted;
            }

            // Drawn by the result still on show, which is not always the element's overall
            // verdict: a column whose mismatch has been filtered out is drawn by whatever it
            // did pass, and one with nothing left on show is grey.
            switch (element.DisplayState)
            {
                case CheckState.Passed:
                    return Passed;
                case CheckState.Failed:
                    return Failed;
                case CheckState.Review:
                    return Review;
                case CheckState.Unpaired:
                    return element.Source == ElementSource.UnmatchedRevit ? UnpairedRevit : UnpairedEtabs;
                default:
                    return element.Source == ElementSource.RevitOnly ? RevitOnly : EtabsOnly;
            }
        }

        private static Brush Freeze(Brush brush)
        {
            brush.Freeze();
            return brush;
        }
    }
}
