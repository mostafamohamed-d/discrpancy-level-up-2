using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// One line load off a Revit loading plan: the line it is drawn as and the schedule number
    /// its line style is named for.
    ///
    /// A loading plan draws its line loads as detail lines with a line style per load type -
    /// "MRG_STR-Line Load-TL1" is number one on the sheet - so the style name is the whole of
    /// the tie between the drawing and the schedule, exactly as the number written on a filled
    /// region ties it to the area schedule.
    ///
    /// Held as a chain of straight pieces rather than as one line, because a detail line can
    /// be an arc and an arc has to be measured as what it is. The pieces are what the check
    /// projects onto the ETABS frames; the element is what gets a verdict and what is selected
    /// back in Revit.
    ///
    /// Like a filled region, a detail line is view specific and carries no level of its own -
    /// the view it was drawn in is what says which floor it belongs to, which is why the user
    /// pairs a view with a story rather than the tool guessing.
    /// </summary>
    public sealed class LineLoad_R
    {
        private readonly List<Piece> pieces = new List<Piece>();

        public LineLoad_R(long revitId, string styleName, IEnumerable<MyPoint> polyline,
                          int number, string story)
        {
            RevitID = revitId;
            StyleName = styleName ?? "";
            Number = number;
            Story = story ?? "";

            if (polyline == null)
            {
                return;
            }

            List<MyPoint> points = polyline.Where(point => point != null).ToList();

            for (int i = 0; i < points.Count - 1; i++)
            {
                Piece piece = new Piece(this, points[i], points[i + 1]);

                // A tessellated arc can hand back two points at the same place, and a piece
                // with no length has no direction, so it would poison every angle taken off it.
                if (piece.Length > 0)
                {
                    pieces.Add(piece);
                }
            }
        }

        public long RevitID { get; }

        /// <summary>The line style the detail line carries, which is what names its load.</summary>
        public string StyleName { get; }

        /// <summary>
        /// The schedule number read off the end of the style name, or zero when the name ends
        /// in no number. A line whose style carries none is still read and still reported,
        /// because a load line nobody numbered is a gap in the plan rather than a line with no
        /// load on it.
        /// </summary>
        public int Number { get; }

        public bool HasNumber { get { return Number > 0; } }

        /// <summary>The story the view this was drawn in is paired with.</summary>
        public string Story { get; }

        /// <summary>
        /// True when the loading plan this was drawn on is paired with more than one story, so
        /// this same Revit line is checked once on each of them - see <see cref="OnStory"/>.
        ///
        /// Worth knowing past the read, because the Revit id stops being enough to tell the
        /// copies apart: a review of the line on one floor is not a review of it on the next,
        /// and a pairing set by hand on one floor says nothing about which floor it is on.
        /// </summary>
        public bool IsOnSeveralStories { get; private set; }

        /// <summary>
        /// This same drawn line, checked on another story.
        ///
        /// A typical floor is drawn once and its loading plan paired with every level built to
        /// it, so one view stands for several stories. It is read once, but each story it is
        /// paired with has to be handed lines of its own - the check sorts lines onto stories
        /// by <see cref="Story"/>, and a line carrying the first story's name is compared on the
        /// first story as many times as there are stories and on the rest not at all.
        /// </summary>
        public LineLoad_R OnStory(string story, bool severalStories)
        {
            IEnumerable<MyPoint> polyline = pieces.Count == 0
                ? new MyPoint[0]
                : new[] { pieces[0].PlanStart }.Concat(pieces.Select(piece => piece.PlanEnd));

            return new LineLoad_R(RevitID, StyleName, polyline, Number, story)
            {
                IsOnSeveralStories = severalStories,
                OwnPerMetre = OwnPerMetre,
                OwnHow = OwnHow,
                Part = Part
            };
        }

        /// <summary>
        /// A line load that says what it is worth itself rather than through a schedule: a wall
        /// or a fill strip read as the weight it puts on the frame under it. See
        /// <see cref="SidlCSource_R"/>.
        ///
        /// Everything after the value is the same as for a line off the loading plan - the same
        /// pieces, laid over the same frames, walked in the same steps - which is the point of
        /// it being a line load at all.
        /// </summary>
        public static LineLoad_R Weighed(long revitId, string name, IEnumerable<MyPoint> polyline,
                                         double perMetre, string how, string story, int part = 0)
        {
            return new LineLoad_R(revitId, name, polyline, 0, story)
            {
                OwnPerMetre = perMetre,
                OwnHow = how ?? "",
                Part = part
            };
        }

        /// <summary>
        /// Which length of its element this is, from 1, where one wall is several loads - its
        /// height changes along it - and so the Revit id no longer tells them apart. Nought for
        /// a line that is the whole of its element.
        /// </summary>
        public int Part { get; private set; }

        /// <summary>
        /// What the line is worth in its own right, in kN/m, or zero on a line off the loading
        /// plan - whose value is on the schedule its number points to.
        /// </summary>
        public double OwnPerMetre { get; private set; }

        public bool HasOwnValue { get { return OwnPerMetre > 0; } }

        /// <summary>How <see cref="OwnPerMetre"/> was worked out, for the card.</summary>
        public string OwnHow { get; private set; } = "";

        /// <summary>
        /// What the line is called in a sentence: "load 3" off the loading plan, and the element
        /// it stands for - "wall Upstand-200" - where it carries its own value.
        /// </summary>
        public string LoadName
        {
            get { return HasOwnValue ? StyleName : "load " + Number; }
        }

        /// <summary>The straight pieces this line is made of, in the order it was drawn.</summary>
        public IList<Piece> Pieces { get { return pieces; } }

        /// <summary>
        /// How long the line is, in mm, measured along it. This is the true length, arcs and
        /// corners included, which is what the load acts over - and so what the total force is
        /// worked out from.
        /// </summary>
        public double Length { get { return pieces.Sum(piece => piece.Length); } }

        /// <summary>True when there is anything here to compare at all.</summary>
        public bool HasLine { get { return pieces.Count > 0; } }

        /// <summary>
        /// True when the line is not straight: more than one piece, and they do not all run
        /// the same way. This is the shape that cannot be compared stretch by stretch against
        /// a straight frame, and the check has to fall back on the total force instead.
        /// </summary>
        public bool IsBent(double angleToleranceDegrees)
        {
            if (pieces.Count < 2)
            {
                return false;
            }

            Piece first = pieces[0];

            return pieces.Skip(1).Any(piece => WallGeometry.AngleBetween(first, piece) > angleToleranceDegrees);
        }

        /// <summary>The middle of the line, by length, which is where its label is anchored.</summary>
        public MyPoint Midpoint
        {
            get
            {
                if (pieces.Count == 0)
                {
                    return MyPoint.Zero;
                }

                double half = Length / 2d;
                double walked = 0;

                foreach (Piece piece in pieces)
                {
                    if (walked + piece.Length >= half)
                    {
                        double k = (half - walked) / piece.Length;
                        return new MyPoint(
                            piece.PlanStart.X + (piece.PlanEnd.X - piece.PlanStart.X) * k,
                            piece.PlanStart.Y + (piece.PlanEnd.Y - piece.PlanStart.Y) * k);
                    }

                    walked += piece.Length;
                }

                return WallGeometry.Midpoint(pieces[pieces.Count - 1]);
            }
        }

        public override string ToString()
        {
            if (HasOwnValue)
            {
                return $"{StyleName}, Revit id {RevitID}";
            }

            return HasNumber
                ? $"line load {RevitID}, number {Number} ({StyleName})"
                : $"line load {RevitID}, no number on \"{StyleName}\"";
        }

        /// <summary>
        /// The schedule number a line style name carries.
        ///
        /// Taken as the first run of digits after the last dash, which is what reads every
        /// shape these names actually come in. The reference project alone has three:
        ///
        ///   MRG_STR-Line Load-TL1          the number on the end
        ///   MRG_STR-Line Load-L10 Podium   the number with a word after it
        ///   MRG_STR-Line Load-STL2         a longer prefix in front of it
        ///
        /// The trailing digits alone would have read the first and the third and called the
        /// second unnumbered - which is every podium style in that project, silently sent for
        /// review. Going after the last dash rather than searching the whole name is what keeps
        /// a number in the prefix - an office code, a year - from being read as the load.
        ///
        /// What comes before the digits says which office drew it and which family of sheets it
        /// belongs to, but the story is already paired with a sheet, so it decides nothing here
        /// and is kept only so a line can be named in a report.
        ///
        /// Zero where there is no number to be had, which is a line style somebody meant as a
        /// load and did not number.
        /// </summary>
        public static int NumberFromStyleName(string styleName)
        {
            if (string.IsNullOrEmpty(styleName))
            {
                return 0;
            }

            int dash = styleName.LastIndexOf('-');

            string tail = dash >= 0 && dash < styleName.Length - 1
                ? styleName.Substring(dash + 1)
                : styleName;

            int at = 0;
            while (at < tail.Length && !char.IsDigit(tail[at]))
            {
                at++;
            }

            int from = at;
            while (at < tail.Length && char.IsDigit(tail[at]))
            {
                at++;
            }

            if (at == from)
            {
                return 0;
            }

            int number;
            return int.TryParse(tail.Substring(from, at - from), NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out number) && number > 0
                ? number
                : 0;
        }

        /// <summary>
        /// True when a line style is one the loading plan draws its line loads with, matched
        /// on the patterns the settings name.
        ///
        /// A loading plan view has ordinary detail lines on it too - hatch boundaries, leaders,
        /// the odd bit of annotation - and reading those as loads would put a finding on every
        /// one of them. The naming convention is a drawing office's rather than a fact about
        /// Revit, which is why it is a setting.
        ///
        /// Left empty, every detail line on the view is read. That is the way out of the one
        /// trap this setting sets: a project whose styles are named in some way nobody
        /// anticipated gets a plan with nothing on it, and no amount of staring at an empty
        /// drawing says which. Reading everything turns that into a plan covered in lines
        /// carrying no number, which is noisy and is an answer.
        /// </summary>
        public static bool IsLineLoadStyle(string styleName, string patterns)
        {
            string[] wanted = PrefrencesData.SplitParameterNames(patterns);

            // Empty, or a wildcard on its own. A person told to widen this setting types a star
            // before they think of clearing the box, and a star matched as a literal looks for
            // an asterisk inside the style name and finds one nowhere - which reads exactly like
            // the check being broken. Both spellings of "everything" mean everything.
            if (wanted.Length == 0 || wanted.Any(IsWildcard))
            {
                return true;
            }

            if (string.IsNullOrEmpty(styleName))
            {
                return false;
            }

            string folded = LineLoadHeight.Fold(styleName);

            return wanted.Select(LineLoadHeight.Fold)
                         .Any(pattern => pattern.Length > 0 && folded.Contains(pattern));
        }

        /// <summary>
        /// How close, in mm, two lines' points have to be for the lines to count as one drawn
        /// twice. Revit will not draw a line shorter than about 0.8 mm, so nothing a person
        /// meant as a separate line sits closer than this to another.
        /// </summary>
        public const double DuplicateTolerance = 1.0;

        /// <summary>
        /// The lines with every copy of the same line taken out, so a load drawn twice in the
        /// same place is weighed once.
        ///
        /// A copy-paste that lands in place, or a view's lines pasted over themselves, leaves
        /// two detail lines that look like one on the sheet - and each would put its full load
        /// on the frame under it, so the plan would read as twice what was drawn. A copy is the
        /// same style over exactly the same line: the same ends, the same length, and the same
        /// points along it, drawn either way round. Overlapping lines that differ anywhere are
        /// two lines and are both kept, because that is a drawing to look at rather than a copy.
        ///
        /// The copy with the lowest Revit id is the one kept, so the same plan keeps the same
        /// line from one run to the next. <paramref name="dropped"/> pairs each line left out
        /// with the line it is a copy of.
        /// </summary>
        public static List<LineLoad_R> WithoutDuplicates(IEnumerable<LineLoad_R> lines,
                                                         out List<KeyValuePair<LineLoad_R, LineLoad_R>> dropped)
        {
            dropped = new List<KeyValuePair<LineLoad_R, LineLoad_R>>();

            List<LineLoad_R> all = (lines ?? Enumerable.Empty<LineLoad_R>())
                .Where(line => line != null)
                .ToList();

            List<LineLoad_R> kept = new List<LineLoad_R>();
            HashSet<LineLoad_R> copies = new HashSet<LineLoad_R>();

            foreach (IGrouping<string, LineLoad_R> style in all.GroupBy(line => line.StyleName + "\u0001" + line.Story))
            {
                List<LineLoad_R> lowestFirst = style.OrderBy(line => line.RevitID).ToList();

                for (int i = 0; i < lowestFirst.Count; i++)
                {
                    LineLoad_R line = lowestFirst[i];
                    if (copies.Contains(line))
                    {
                        continue;
                    }

                    for (int j = i + 1; j < lowestFirst.Count; j++)
                    {
                        LineLoad_R other = lowestFirst[j];
                        if (!copies.Contains(other) && line.IsSameLineAs(other))
                        {
                            copies.Add(other);
                            dropped.Add(new KeyValuePair<LineLoad_R, LineLoad_R>(other, line));
                        }
                    }
                }
            }

            // In the order they came in, so dropping a copy moves nothing else around.
            kept.AddRange(all.Where(line => !copies.Contains(line)));

            return kept;
        }

        /// <summary>
        /// True when this and another line run over exactly the same points in plan, in either
        /// direction, to within <see cref="DuplicateTolerance"/>.
        /// </summary>
        public bool IsSameLineAs(LineLoad_R other)
        {
            if (other == null || !HasLine || !other.HasLine || pieces.Count != other.pieces.Count)
            {
                return false;
            }

            if (Math.Abs(Length - other.Length) > DuplicateTolerance)
            {
                return false;
            }

            List<MyPoint> mine = Vertices();
            List<MyPoint> theirs = other.Vertices();

            bool forward = true;
            bool backward = true;

            for (int i = 0; i < mine.Count && (forward || backward); i++)
            {
                forward = forward && SamePlace(mine[i], theirs[i]);
                backward = backward && SamePlace(mine[i], theirs[theirs.Count - 1 - i]);
            }

            return forward || backward;
        }

        private List<MyPoint> Vertices()
        {
            List<MyPoint> points = new List<MyPoint> { pieces[0].PlanStart };
            points.AddRange(pieces.Select(piece => piece.PlanEnd));
            return points;
        }

        private static bool SamePlace(MyPoint a, MyPoint b)
        {
            return WallGeometry.PlanLength(a, b) <= DuplicateTolerance;
        }

        /// <summary>True for a pattern that is nothing but stars, which means everything.</summary>
        private static bool IsWildcard(string pattern)
        {
            string trimmed = (pattern ?? "").Trim();

            return trimmed.Length > 0 && trimmed.All(letter => letter == '*' || letter == '%');
        }

        /// <summary>
        /// One straight piece of a drawn load line.
        ///
        /// A plan line in its own right, so the projection maths the wall check is built on
        /// measures it without knowing what it is - which is the whole point of
        /// <see cref="IPlanLine"/>.
        /// </summary>
        public sealed class Piece : IPlanLine
        {
            public Piece(LineLoad_R line, MyPoint start, MyPoint end)
            {
                Line = line;
                PlanStart = start;
                PlanEnd = end;
            }

            /// <summary>The drawn line this is a piece of, which is what carries the verdict.</summary>
            public LineLoad_R Line { get; }

            public MyPoint PlanStart { get; }

            public MyPoint PlanEnd { get; }

            public string Story { get { return Line == null ? "" : Line.Story; } }

            public double Length { get { return WallGeometry.PlanLength(PlanStart, PlanEnd); } }
        }
    }
}
