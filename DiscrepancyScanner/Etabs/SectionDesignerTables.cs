using System;
using System.Collections.Generic;
using System.Globalization;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Reading the size of a Section Designer frame section out of the model's own tables.
    ///
    /// A column drawn in Section Designer has a frame property of type SDSection, and the API
    /// will not answer GetRectangle or GetCircle for it: the call fails, the size comes back as
    /// nothing and so does the material. The column then either imported as a point with no
    /// section or, with the material null, stopped the import altogether.
    ///
    /// The shapes are in the tables - one table per kind of concrete shape, a row per shape
    /// (or, for a polygon, a row per corner), keyed by section type and section name. These are
    /// the same tables and the same field keys the Advanced Section Designer reads and writes.
    ///
    /// A section is reduced to what the column check understands. One circle is a round column
    /// of that diameter. Anything else is measured by the box around all of its concrete; when
    /// that box is the section - one rectangle, or one polygon that is a rectangle, square to
    /// the section axes - it is a rectangular column, and otherwise the size is kept but the
    /// shape is said to be unknown, rather than an L or a T being passed off as its box.
    ///
    /// Section Designer draws the section with its X axis along the local 3 axis and its Y axis
    /// along the local 2 axis, so the Y extent is the depth (t3) and the X extent the width
    /// (t2) - the same two numbers GetRectangle hands back for an ordinary section.
    ///
    /// Nothing here touches the ETABS API. What arrives is the flat array a table comes back
    /// as, with how many millimetres one unit of its lengths is.
    /// </summary>
    internal static class SectionDesignerTables
    {
        public const string PolygonTable = "Section Designer Shapes - Concrete Polygon";
        public const string RectangleTable = "Section Designer Shapes - Concrete Rectangle";
        public const string CircleTable = "Section Designer Shapes - Concrete Circle";

        /// <summary>The section type a frame section carries; piers and spandrels are walls.</summary>
        private const string FrameSectionType = "Frame";

        /// <summary>How close to square a rotation or an edge has to be to count, in degrees and mm.</summary>
        private const double AngleTolerance = 0.01;
        private const double LengthTolerance = 0.5;

        /// <summary>One Section Designer section, as the column check will see it. Lengths in mm.</summary>
        public sealed class Outline
        {
            public SectionShape Shape = SectionShape.Unknown;
            public double Width;
            public double Depth;
            public double Diameter;
            public string Material = "";
            public int ShapeCount;
        }

        /// <summary>One table as it came back, and what one unit of its lengths is in mm.</summary>
        public struct Table
        {
            public string[] FieldKeys;
            public string[] Data;
            public int Records;
            public double MillimetresPerUnit;
        }

        /// <summary>A section's concrete as it is gathered, before it is reduced to an outline.</summary>
        private sealed class Gathered
        {
            public double MinX = double.MaxValue, MaxX = double.MinValue;
            public double MinY = double.MaxValue, MaxY = double.MinValue;
            public int Shapes;
            public int Circles;
            public double CircleDiameter;
            /// <summary>Whether every shape so far is a rectangle square to the axes.</summary>
            public bool AllSquareRectangles = true;
            public string Material = "";

            public void Take(double x, double y)
            {
                MinX = Math.Min(MinX, x);
                MaxX = Math.Max(MaxX, x);
                MinY = Math.Min(MinY, y);
                MaxY = Math.Max(MaxY, y);
            }

            public void TakeMaterial(string material)
            {
                if (Material.Length == 0 && !string.IsNullOrWhiteSpace(material))
                {
                    Material = material.Trim();
                }
            }
        }

        /// <summary>
        /// Every Section Designer frame section in the three tables, by section name. A table
        /// that did not come back is simply passed as empty; a section only in it is then not
        /// found, and is reported by the caller as not read rather than read as nothing.
        /// </summary>
        public static Dictionary<string, Outline> Read(Table rectangles, Table circles, Table polygons,
                                                       IList<string> issues)
        {
            Dictionary<string, Gathered> sections =
                new Dictionary<string, Gathered>(StringComparer.OrdinalIgnoreCase);

            ReadRectangles(rectangles, sections, issues);
            ReadCircles(circles, sections, issues);
            ReadPolygons(polygons, sections, issues);

            Dictionary<string, Outline> outlines =
                new Dictionary<string, Outline>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, Gathered> section in sections)
            {
                outlines[section.Key] = Reduce(section.Value);
            }

            return outlines;
        }

        private static Outline Reduce(Gathered g)
        {
            Outline outline = new Outline { Material = g.Material, ShapeCount = g.Shapes };

            if (g.Shapes == 0 || g.MaxX < g.MinX || g.MaxY < g.MinY)
            {
                return outline;
            }

            if (g.Shapes == 1 && g.Circles == 1)
            {
                outline.Shape = SectionShape.Round;
                outline.Diameter = g.CircleDiameter;
                outline.Width = g.CircleDiameter;
                outline.Depth = g.CircleDiameter;
                return outline;
            }

            outline.Width = g.MaxX - g.MinX;
            outline.Depth = g.MaxY - g.MinY;
            outline.Shape = g.Shapes == 1 && g.AllSquareRectangles
                ? SectionShape.Rectangular
                : SectionShape.Unknown;
            return outline;
        }

        private static void ReadRectangles(Table table, Dictionary<string, Gathered> sections, IList<string> issues)
        {
            if (!HasRows(table))
            {
                return;
            }

            int x = Column(table, "XCenter");
            int y = Column(table, "YCenter");
            int height = Column(table, "Height");
            int width = Column(table, "Width");
            int rotation = Column(table, "Rotation");
            int material = Column(table, "Material");

            if (x < 0 || y < 0 || height < 0 || width < 0 || !HasNameColumns(table))
            {
                Complain(issues, RectangleTable, table.FieldKeys);
                return;
            }

            foreach (string[] row in LoadSetTables.Rows(table.FieldKeys, table.Data, table.Records))
            {
                Gathered section = SectionOf(table, row, sections);
                double cx, cy, h, w;
                if (section == null
                    || !TryLength(row[x], table, out cx) || !TryLength(row[y], table, out cy)
                    || !TryLength(row[height], table, out h) || !TryLength(row[width], table, out w))
                {
                    continue;
                }

                double degrees;
                if (rotation < 0 || !TryNumber(row[rotation], out degrees))
                {
                    degrees = 0;
                }

                double radians = degrees * Math.PI / 180d;
                double cos = Math.Cos(radians), sin = Math.Sin(radians);

                foreach (double dx in new[] { -w / 2, w / 2 })
                {
                    foreach (double dy in new[] { -h / 2, h / 2 })
                    {
                        section.Take(cx + dx * cos - dy * sin, cy + dx * sin + dy * cos);
                    }
                }

                section.Shapes++;
                section.AllSquareRectangles &= IsSquareAngle(degrees);
                if (material >= 0)
                {
                    section.TakeMaterial(row[material]);
                }
            }
        }

        private static void ReadCircles(Table table, Dictionary<string, Gathered> sections, IList<string> issues)
        {
            if (!HasRows(table))
            {
                return;
            }

            int x = Column(table, "XCenter");
            int y = Column(table, "YCenter");
            int diameter = Column(table, "Diameter");
            int material = Column(table, "Material");

            if (x < 0 || y < 0 || diameter < 0 || !HasNameColumns(table))
            {
                Complain(issues, CircleTable, table.FieldKeys);
                return;
            }

            foreach (string[] row in LoadSetTables.Rows(table.FieldKeys, table.Data, table.Records))
            {
                Gathered section = SectionOf(table, row, sections);
                double cx, cy, d;
                if (section == null
                    || !TryLength(row[x], table, out cx) || !TryLength(row[y], table, out cy)
                    || !TryLength(row[diameter], table, out d))
                {
                    continue;
                }

                section.Take(cx - d / 2, cy - d / 2);
                section.Take(cx + d / 2, cy + d / 2);
                section.Shapes++;
                section.Circles++;
                section.CircleDiameter = d;
                section.AllSquareRectangles = false;
                if (material >= 0)
                {
                    section.TakeMaterial(row[material]);
                }
            }
        }

        /// <summary>
        /// A row per corner; a new shape starts where the section or shape name changes. The
        /// shape's own attributes - the material among them - are on its first row only.
        /// </summary>
        private static void ReadPolygons(Table table, Dictionary<string, Gathered> sections, IList<string> issues)
        {
            if (!HasRows(table))
            {
                return;
            }

            int x = Column(table, "X");
            int y = Column(table, "Y");
            int shapeName = Column(table, "ShapeName");
            int material = Column(table, "Material");

            if (x < 0 || y < 0 || !HasNameColumns(table))
            {
                Complain(issues, PolygonTable, table.FieldKeys);
                return;
            }

            string currentKey = null;
            Gathered current = null;
            List<double[]> corners = new List<double[]>();

            foreach (string[] row in LoadSetTables.Rows(table.FieldKeys, table.Data, table.Records))
            {
                string key = Cell(row, Column(table, "SectionType")) + "|" + Cell(row, Column(table, "SectionName"))
                             + "|" + Cell(row, shapeName);

                if (key != currentKey)
                {
                    FinishPolygon(current, corners);
                    corners.Clear();
                    currentKey = key;
                    current = SectionOf(table, row, sections);
                    if (current != null)
                    {
                        current.Shapes++;
                        if (material >= 0)
                        {
                            current.TakeMaterial(row[material]);
                        }
                    }
                }

                double px, py;
                if (current != null && TryLength(row[x], table, out px) && TryLength(row[y], table, out py))
                {
                    current.Take(px, py);
                    corners.Add(new[] { px, py });
                }
            }

            FinishPolygon(current, corners);
        }

        /// <summary>A polygon counts as a rectangle when it has four corners, each on an edge of its box.</summary>
        private static void FinishPolygon(Gathered section, List<double[]> corners)
        {
            if (section == null)
            {
                return;
            }

            section.AllSquareRectangles &= IsSquareRectangle(corners);
        }

        internal static bool IsSquareRectangle(List<double[]> corners)
        {
            // A closed polygon may repeat its first corner as its last.
            int count = corners.Count;
            if (count == 5 && Near(corners[0][0], corners[4][0]) && Near(corners[0][1], corners[4][1]))
            {
                count = 4;
            }

            if (count != 4)
            {
                return false;
            }

            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            for (int i = 0; i < 4; i++)
            {
                minX = Math.Min(minX, corners[i][0]);
                maxX = Math.Max(maxX, corners[i][0]);
                minY = Math.Min(minY, corners[i][1]);
                maxY = Math.Max(maxY, corners[i][1]);
            }

            for (int i = 0; i < 4; i++)
            {
                bool onX = Near(corners[i][0], minX) || Near(corners[i][0], maxX);
                bool onY = Near(corners[i][1], minY) || Near(corners[i][1], maxY);
                if (!onX || !onY)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsSquareAngle(double degrees)
        {
            double folded = Math.Abs(degrees) % 90d;
            return folded < AngleTolerance || 90d - folded < AngleTolerance;
        }

        private static bool Near(double a, double b)
        {
            return Math.Abs(a - b) < LengthTolerance;
        }

        /// <summary>
        /// The section a row belongs to, or null when it is not a frame section. A table with
        /// no section type column is read as all frame sections.
        /// </summary>
        private static Gathered SectionOf(Table table, string[] row, Dictionary<string, Gathered> sections)
        {
            int typeColumn = Column(table, "SectionType");
            string type = Cell(row, typeColumn);
            if (typeColumn >= 0 && !string.Equals(type, FrameSectionType, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string name = Cell(row, Column(table, "SectionName"));
            if (name.Length == 0)
            {
                return null;
            }

            Gathered section;
            if (!sections.TryGetValue(name, out section))
            {
                section = new Gathered();
                sections[name] = section;
            }

            return section;
        }

        private static bool HasRows(Table table)
        {
            return table.FieldKeys != null && table.Data != null && table.Records > 0;
        }

        private static bool HasNameColumns(Table table)
        {
            return Column(table, "SectionName") >= 0;
        }

        private static int Column(Table table, string fieldKey)
        {
            return LoadSetTables.ColumnOf(table.FieldKeys, fieldKey);
        }

        private static string Cell(string[] row, int column)
        {
            return column < 0 || column >= row.Length ? "" : (row[column] ?? "").Trim();
        }

        private static bool TryLength(string text, Table table, out double millimetres)
        {
            double value;
            millimetres = 0;
            if (!TryNumber(text, out value))
            {
                return false;
            }

            millimetres = value * table.MillimetresPerUnit;
            return true;
        }

        private static bool TryNumber(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                   || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private static void Complain(IList<string> issues, string table, string[] fieldKeys)
        {
            if (issues == null)
            {
                return;
            }

            issues.Add($"The ETABS table \"{table}\" does not have the columns this expects, so the " +
                       "Section Designer columns drawn with it were not sized. Its columns are: " +
                       (fieldKeys == null ? "(none)" : string.Join(", ", fieldKeys)));
        }

        /// <summary>
        /// How many mm one unit of a table's length columns is, from the unit the table puts
        /// beside them, or -1 when it is not one this knows.
        /// </summary>
        public static double MillimetresPer(string unit)
        {
            switch (LoadSetTables.Normalise(unit))
            {
                case "MM": return 1d;
                case "CM": return 10d;
                case "M": return 1000d;
                case "IN": case "INCH": return 25.4d;
                case "FT": return 304.8d;
                default: return -1;
            }
        }
    }
}
