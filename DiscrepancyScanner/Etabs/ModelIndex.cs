using System;
using System.Collections.Generic;

namespace DiscrepancyScanner
{
    /// <summary>
    /// The bookkeeping behind reading an ETABS model in a handful of calls instead of a few
    /// hundred thousand.
    ///
    /// ETABS answers two calls - GetAllFrames and GetAllAreas - with everything the import
    /// needs about where things are and what meets what. The engine used to ask again, object
    /// by object, for answers those two calls had already given it. What is left over once the
    /// asking stops is the arithmetic here: turning flat parallel arrays into the questions
    /// the comparison actually puts, which is what makes any of it worth testing.
    ///
    /// None of this touches the ETABS API, which is the point: it can be checked without a
    /// model open, and the interop is left as the thin layer that fetches the arrays.
    /// </summary>
    internal static class ModelIndex
    {
        /// <summary>
        /// Where one area's boundary points start in the flat point arrays.
        ///
        /// ETABS returns every area's points end to end with a delimiter holding the index of
        /// each area's last point, so an area begins one past where the area before it ended,
        /// and the first begins at nought.
        /// </summary>
        public static int BoundaryStart(int area, int[] delimiter)
        {
            return area == 0 ? 0 : delimiter[area - 1] + 1;
        }

        /// <summary>
        /// Every point a wall stands on.
        ///
        /// The merge walks along a line of beams and has to stop where a wall supports it.
        /// That used to be a design orientation asked of ETABS per area per joint; the design
        /// orientation of every area arrives with the area list anyway.
        /// </summary>
        public static HashSet<string> WallPoints(int count, bool[] isWall, int[] delimiter,
                                                 string[] pointNames)
        {
            HashSet<string> onAWall = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (isWall == null || delimiter == null || pointNames == null)
            {
                return onAWall;
            }

            for (int area = 0; area < count && area < isWall.Length && area < delimiter.Length; area++)
            {
                if (!isWall[area])
                {
                    continue;
                }

                for (int at = BoundaryStart(area, delimiter);
                     at <= delimiter[area] && at < pointNames.Length;
                     at++)
                {
                    if (!string.IsNullOrEmpty(pointNames[at]))
                    {
                        onAWall.Add(pointNames[at]);
                    }
                }
            }

            return onAWall;
        }

        /// <summary>
        /// Which frames meet at each point, from the end points the frame list already
        /// returned.
        ///
        /// Built from the whole list rather than from the frames that survive filtering,
        /// because ETABS would have named the ignored ones too - and an ignored frame that
        /// happens to be a column still has to stop a merge running through it.
        /// </summary>
        public static Dictionary<string, List<string>> FramesByPoint(int count, string[] names,
                                                                     string[] point1, string[] point2)
        {
            Dictionary<string, List<string>> byPoint =
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            if (names == null || point1 == null || point2 == null)
            {
                return byPoint;
            }

            for (int i = 0; i < count && i < names.Length && i < point1.Length && i < point2.Length; i++)
            {
                MeetAt(byPoint, point1[i], names[i]);
                MeetAt(byPoint, point2[i], names[i]);
            }

            return byPoint;
        }

        private static void MeetAt(Dictionary<string, List<string>> byPoint, string pointName,
                                   string frameName)
        {
            if (string.IsNullOrEmpty(pointName) || string.IsNullOrEmpty(frameName))
            {
                return;
            }

            List<string> here;
            if (!byPoint.TryGetValue(pointName, out here))
            {
                here = new List<string>(4);
                byPoint[pointName] = here;
            }

            here.Add(frameName);
        }

        /// <summary>
        /// The frames meeting one frame at a joint, or null when the merge must not run
        /// through that joint at all - which is what a wall standing on it or a column landing
        /// on it means.
        ///
        /// This is the same verdict the engine used to reach by asking ETABS for the joint's
        /// connectivity and then for the design orientation of everything hung off it, once
        /// per joint of every frame. The walk is order independent - either something at the
        /// joint stops the merge and the list is thrown away, or nothing does and the whole
        /// list is returned - so losing the API's ordering costs nothing.
        ///
        /// The frame itself is checked along with its neighbours, as it always was: a column
        /// is a column whether it is the one being walked from or one of the ones it meets.
        /// </summary>
        public static List<string> ConnectedFrames(string pointName, string frameName,
                                                   Dictionary<string, List<string>> framesByPoint,
                                                   HashSet<string> pointsOnAWall,
                                                   Func<string, bool> isColumn)
        {
            if (string.IsNullOrEmpty(pointName))
            {
                return new List<string>();
            }

            if (pointsOnAWall != null && pointsOnAWall.Contains(pointName))
            {
                return null;
            }

            List<string> here;
            if (framesByPoint == null || !framesByPoint.TryGetValue(pointName, out here))
            {
                return new List<string>();
            }

            List<string> connected = new List<string>(here.Count);

            foreach (string other in here)
            {
                if (isColumn != null && isColumn(other))
                {
                    return null;
                }

                if (frameName != other)
                {
                    connected.Add(other);
                }
            }

            return connected;
        }

        // ------------------------------------------------------------ column or beam

        /// <summary>
        /// The lean from plumb, in degrees, past which a frame is read as a beam rather than
        /// as a column when the preference has not been set to anything else.
        ///
        /// Thirty degrees sits in the gap the two kinds of member leave between them. A car
        /// ramp runs at six to nine degrees off horizontal and a stair flight at about thirty,
        /// which is sixty degrees or more of lean, well outside; an architectural raking
        /// column leans ten or twenty, well inside. Nothing real sits on the line, so the
        /// number does not have to be argued over - it only has to be between them.
        /// </summary>
        public const double DefaultColumnLeanFromVertical = 30;

        /// <summary>
        /// The most lean the preference is allowed to ask for. Tangent runs away near ninety
        /// and a value up there would call every frame in the model a column, which is the one
        /// mistake this test must not be able to make.
        /// </summary>
        public const double MaximumColumnLeanFromVertical = 60;

        /// <summary>
        /// How much a frame's ends must differ in level, in mm, before its lean is worth
        /// measuring at all.
        ///
        /// A frame flatter than this is a beam whatever its plan run says, which covers the
        /// stepped beam dropped a few millimetres for a screed and the frame drawn with both
        /// ends on one point, whose lean is not defined. This was the whole of the old test.
        /// </summary>
        public const double ColumnRiseFloor = 50;

        /// <summary>
        /// Whether a frame stands up steeply enough to be read as a column rather than a beam.
        ///
        /// ETABS does not label a frame column or beam in anything the whole-model call hands
        /// back, so the import decides from the two ends. It used to decide on the rise alone:
        /// ends more than 50mm apart in level meant column. That reads an inclined beam as a
        /// column - a six metre beam falling sixty millimetres to a drain is a column under
        /// that rule, and so is every ramp and stair beam in the model - because a rise on its
        /// own says nothing without the plan run it is spread over.
        ///
        /// So the two are measured together, as an angle off plumb. A column is a frame that
        /// leans no further than <paramref name="maxLeanFromVerticalDegrees"/>, which keeps
        /// the raking column that genuinely is one and hands everything flatter to the beams.
        /// Written as a comparison against the run rather than as an arc tangent so that a
        /// frame with no rise falls out before any angle is taken.
        /// </summary>
        public static bool IsColumn(double x1, double y1, double z1,
                                    double x2, double y2, double z2,
                                    double maxLeanFromVerticalDegrees)
        {
            double rise = Math.Abs(z2 - z1);
            if (rise <= ColumnRiseFloor)
            {
                return false;
            }

            double acrossX = x2 - x1;
            double acrossY = y2 - y1;
            double run = Math.Sqrt(acrossX * acrossX + acrossY * acrossY);

            double lean = ColumnLeanInRange(maxLeanFromVerticalDegrees);
            return run <= rise * Math.Tan(lean * Math.PI / 180.0);
        }

        /// <summary>
        /// Whether a frame is a null line - drawn with the None property to hang a load on or
        /// to split a slab, and no beam at all.
        ///
        /// ETABS hands such a frame back from the whole-model call with its section named
        /// "None". SAFE hands it back with no section name at all, and only says "None" when
        /// asked about the one frame - so a test for the name alone let every null line in a
        /// SAFE model through as a beam. Both are read as the same thing here.
        /// </summary>
        public static bool IsNullFrame(string sectionName)
        {
            return string.IsNullOrWhiteSpace(sectionName)
                   || string.Equals(sectionName.Trim(), "None", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether an area is a null area - drawn with the None property, which is no slab at
        /// all, to lay a load over part of the slab under it. Named the same way a null line is,
        /// and for the same reason: SAFE can hand one back with no property name at all.
        /// </summary>
        public static bool IsNullArea(string propertyName)
        {
            return IsNullFrame(propertyName);
        }

        /// <summary>
        /// The lean the test will actually be run at: what was asked for, held inside the
        /// range it means anything over. A preference read back from a settings file written
        /// by hand can be anything at all, and a nonsense value here would silently reclassify
        /// the whole model rather than fail.
        /// </summary>
        public static double ColumnLeanInRange(double degrees)
        {
            if (double.IsNaN(degrees) || degrees < 0)
            {
                return 0;
            }

            return degrees > MaximumColumnLeanFromVertical
                 ? MaximumColumnLeanFromVertical
                 : degrees;
        }
    }
}
