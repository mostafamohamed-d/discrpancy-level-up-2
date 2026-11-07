using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// The values an element drawn from the raw data carries, for the label picker and the
    /// card. Named by kind, because the picker lists a kind's values together and a key seen
    /// under one kind is not offered again under another.
    ///
    /// The ETABS and the Revit element of a kind publish the same value under the same key -
    /// a beam's size is "Size" whichever model it came from - so ticking it once labels both
    /// models, which is what reading the two side by side needs.
    /// </summary>
    public static class RawResultKeys
    {
        public const string ColumnName = "Raw.Column.Name";
        public const string ColumnSection = "Raw.Column.Section";
        public const string ColumnSize = "Raw.Column.Size";
        public const string ColumnAngle = "Raw.Column.Angle";
        public const string ColumnGroups = "Raw.Column.Groups";
        public const string ColumnTypeMark = "Raw.Column.TypeMark";
        public const string ColumnFamily = "Raw.Column.Family";

        public const string BeamName = "Raw.Beam.Name";
        public const string BeamLabel = "Raw.Beam.Label";
        public const string BeamMark = "Raw.Beam.Mark";
        public const string BeamSection = "Raw.Beam.Section";
        public const string BeamSize = "Raw.Beam.Size";
        public const string BeamLength = "Raw.Beam.Length";
        public const string BeamLevels = "Raw.Beam.Levels";

        public const string WallName = "Raw.Wall.Name";
        public const string WallSection = "Raw.Wall.Section";
        public const string WallThickness = "Raw.Wall.Thickness";
        public const string WallLength = "Raw.Wall.Length";
        public const string WallPanels = "Raw.Wall.Panels";
        public const string WallTypeMark = "Raw.Wall.TypeMark";

        public const string SlabName = "Raw.Slab.Name";
        public const string SlabSection = "Raw.Slab.Section";
        public const string SlabThickness = "Raw.Slab.Thickness";
        public const string SlabDepth = "Raw.Slab.Depth";
        public const string SlabKind = "Raw.Slab.Kind";
        public const string SlabArea = "Raw.Slab.Area";
        public const string SlabLoads = "Raw.Slab.Loads";
        public const string SlabLoadNumber = "Raw.Slab.LoadNumber";
        public const string SlabDescription = "Raw.Slab.Description";

        public const string LineLoadName = "Raw.LineLoad.Name";
        public const string LineLoadLabel = "Raw.LineLoad.Label";
        public const string LineLoadNumber = "Raw.LineLoad.Number";
        public const string LineLoadLoad = "Raw.LineLoad.Load";
        public const string LineLoadStyle = "Raw.LineLoad.Style";
        public const string LineLoadLength = "Raw.LineLoad.Length";
    }

    /// <summary>
    /// The elements the plan draws while Raw data is on: each model's elements as they were
    /// read, with nothing paired and nothing judged.
    ///
    /// The check answers whether the two models agree, and there is an earlier question it
    /// cannot answer: what did each of them actually get. A pairing that went wrong, a level
    /// paired to the wrong view, a section read off the wrong parameter - every one of them
    /// comes out of the check as a finding about something else, and every one is plain the
    /// moment the two models are laid over each other unjudged and labelled with what was read.
    ///
    /// So every element here carries no results at all, only what it is: its name, its
    /// section, its size, its loads. It is drawn in its model's colour, and its values are what
    /// the labels offer.
    /// </summary>
    public static class RawElements
    {
        #region columns

        public static ElementViewModel EtabsColumn(Column_e column)
        {
            List<ResultField> fields = new List<ResultField>
            {
                Field(RawResultKeys.ColumnName, "Name / id", column.EtabsUniqueName),
                Field(RawResultKeys.ColumnSection, "Section / type", column.SectionName),
                Field(RawResultKeys.ColumnSize, "Size", SizeOf(column)),
                Field(RawResultKeys.ColumnAngle, "Angle (deg)", Number(column.AngleDegrees, 1)),
                Field(RawResultKeys.ColumnGroups, "ETABS groups", string.Join(", ", column.Groups.ToArray()))
            };

            return Raw(new ColumnElementViewModel(ElementSource.EtabsOnly, CheckState.NotChecked,
                                                  column.EtabsUniqueName, column.Story, fields, column),
                       "ETABS", "column");
        }

        public static ElementViewModel RevitColumn(Column_r column)
        {
            List<ResultField> fields = new List<ResultField>
            {
                Field(RawResultKeys.ColumnName, "Name / id", column.RevitID.ToString()),
                Field(RawResultKeys.ColumnSection, "Section / type", column.TypeName),
                Field(RawResultKeys.ColumnSize, "Size", SizeOf(column)),
                Field(RawResultKeys.ColumnAngle, "Angle (deg)", Number(column.AngleDegrees, 1)),
                Field(RawResultKeys.ColumnTypeMark, "Type mark", column.TypeMark),
                Field(RawResultKeys.ColumnFamily, "Revit family", column.FamilyName)
            };

            ElementViewModel element = new ColumnElementViewModel(
                ElementSource.RevitOnly, CheckState.NotChecked,
                column.RevitID.ToString(), column.Story, fields, column);
            element.RevitId = column.RevitID;
            return Raw(element, "Revit", "column");
        }

        /// <summary>"600 x 300", or "Ø600" for a round one; empty where nothing could be read.</summary>
        private static string SizeOf(IColumn column)
        {
            if (column.Shape == SectionShape.Round)
            {
                return column.Diameter > 0 ? "Ø" + Number(column.Diameter, 0) : "";
            }
            return column.Width > 0 || column.Depth > 0
                ? Number(column.Width, 0) + " x " + Number(column.Depth, 0)
                : "";
        }

        #endregion

        #region beams

        public static ElementViewModel EtabsBeam(FrameData_e frame)
        {
            List<ResultField> fields = new List<ResultField>
            {
                Field(RawResultKeys.BeamName, "Name / id", frame.etabsUname),
                Field(RawResultKeys.BeamLabel, "ETABS label", frame.EtabsLabel),
                Field(RawResultKeys.BeamSection, "Section / type", frame.etabsSecName),
                Field(RawResultKeys.BeamSize, "Size (w x d)", SizeOf(frame)),
                Field(RawResultKeys.BeamLength, "Length (m)", Metres(Length(frame.Start, frame.End))),
                Field(RawResultKeys.BeamLevels, "End levels (mm)", LevelsOf(frame.Start, frame.End))
            };

            return Raw(new BeamElementViewModel(ElementSource.EtabsOnly, CheckState.NotChecked,
                                                frame.etabsUname, frame.Story, fields, frame.Start, frame.End),
                       "ETABS", "beam");
        }

        public static ElementViewModel RevitBeam(FrameData_r frame)
        {
            List<ResultField> fields = new List<ResultField>
            {
                Field(RawResultKeys.BeamName, "Name / id", frame.RevitID.ToString()),
                Field(RawResultKeys.BeamMark, "Beam mark", frame.BeamMark),
                Field(RawResultKeys.BeamSection, "Section / type", frame.RevitName),
                Field(RawResultKeys.BeamSize, "Size (w x d)", SizeOf(frame)),
                Field(RawResultKeys.BeamLength, "Length (m)", Metres(Length(frame.Start, frame.End))),
                Field(RawResultKeys.BeamLevels, "End levels (mm)", LevelsOf(frame.Start, frame.End))
            };

            ElementViewModel element = new BeamElementViewModel(
                ElementSource.RevitOnly, CheckState.NotChecked,
                frame.RevitID.ToString(), frame.Story, fields, frame.Start, frame.End);
            element.RevitId = frame.RevitID;
            return Raw(element, "Revit", "beam");
        }

        private static string SizeOf(FrameData frame)
        {
            return frame._width > 0 || frame._depth > 0
                ? Number(frame._width, 0) + " x " + Number(frame._depth, 0)
                : "";
        }

        /// <summary>Where the two ends stand, or the one level where they stand level.</summary>
        private static string LevelsOf(MyPoint start, MyPoint end)
        {
            if (start == null || end == null)
            {
                return "";
            }

            string first = Number(start.Z, 0);
            string second = Number(end.Z, 0);
            return first == second ? first : first + " / " + second;
        }

        private static double Length(MyPoint start, MyPoint end)
        {
            if (start == null || end == null)
            {
                return 0;
            }

            double dx = end.X - start.X, dy = end.Y - start.Y, dz = end.Z - start.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        #endregion

        #region walls

        /// <summary>
        /// The ETABS panels standing on one line of wall, drawn as the one wall they are - the
        /// same folding the check does, and for the same reason: panels meshed up a wall's
        /// height lie exactly on top of each other in plan, and drawn one by one their labels
        /// would be printed over each other too.
        /// </summary>
        public static ElementViewModel EtabsWall(IList<Wall_E> stack)
        {
            Wall_E first = stack[0];

            List<ResultField> fields = new List<ResultField>
            {
                Field(RawResultKeys.WallName, "Name / id", Join(stack.Select(wall => wall.UniqueName))),
                Field(RawResultKeys.WallSection, "Section / type", Join(stack.Select(wall => wall.PropertyName))),
                Field(RawResultKeys.WallThickness, "Thickness (mm)",
                      Join(stack.Select(wall => Number(wall.Thickness, 0)))),
                Field(RawResultKeys.WallLength, "Length (m)", Metres(first.Length))
            };

            if (stack.Count > 1)
            {
                fields.Add(Field(RawResultKeys.WallPanels, "ETABS panels here", stack.Count.ToString()));
            }

            return Raw(new WallElementViewModel(ElementSource.EtabsOnly, CheckState.NotChecked,
                                                first.UniqueName, first.Story, fields,
                                                first.PlanStart, first.PlanEnd,
                                                stack.Max(wall => wall.Thickness)),
                       "ETABS", "wall");
        }

        public static ElementViewModel RevitWall(Wall_R wall)
        {
            List<ResultField> fields = new List<ResultField>
            {
                Field(RawResultKeys.WallName, "Name / id", wall.RevitID.ToString()),
                Field(RawResultKeys.WallSection, "Section / type", wall.TypeName),
                Field(RawResultKeys.WallThickness, "Thickness (mm)", Number(wall.Thickness, 0)),
                Field(RawResultKeys.WallLength, "Length (m)", Metres(wall.Length)),
                Field(RawResultKeys.WallTypeMark, "Type mark", wall.TypeMark)
            };

            ElementViewModel element = new WallElementViewModel(
                ElementSource.RevitOnly, CheckState.NotChecked,
                wall.RevitID.ToString(), wall.Story, fields,
                wall.PlanStart, wall.PlanEnd, wall.Thickness);
            element.RevitId = wall.RevitID;
            return Raw(element, "Revit", "wall");
        }

        #endregion

        #region slabs and their loads

        /// <summary>
        /// One ETABS floor area, with its thickness and the area loads on it as ETABS holds
        /// them - one entry per load pattern, before any of it is added up into the four loads
        /// the check compares.
        ///
        /// Shown under both slab tabs, since it is the same area either way: what differs
        /// between them is what it is laid over, the Revit floors or the loading plan.
        /// </summary>
        public static ElementViewModel EtabsSlab(Slab_E slab)
        {
            List<ResultField> fields = new List<ResultField>
            {
                Field(RawResultKeys.SlabName, "Name / id", slab.UniqueName),
                Field(RawResultKeys.SlabSection, "Section / type", slab.PropertyName),
                Field(RawResultKeys.SlabThickness, "Thickness (mm)", Number(slab.Thickness, 0)),
                Field(RawResultKeys.SlabKind, "ETABS slab kind", slab.Kind.ToString()),
                Field(RawResultKeys.SlabArea, "Area (m²)", Area(slab.Plan)),
                Field(RawResultKeys.SlabLoads, "Loads (kN/m²)", LoadsOf(slab.Loads))
            };

            if (slab.OverallDepth > slab.Thickness + 0.5)
            {
                fields.Insert(3, Field(RawResultKeys.SlabDepth, "Overall depth (mm)", Number(slab.OverallDepth, 0)));
            }

            return Raw(new SlabElementViewModel(ElementSource.EtabsOnly, CheckState.NotChecked,
                                                slab.UniqueName, slab.Story, fields, slab.Plan),
                       "ETABS", "slab area");
        }

        /// <summary>
        /// One ETABS null area carrying load, under the slab load tab only: no slab, so nothing
        /// for the slab tab to show, but its load is added to the slab under it and is worth
        /// seeing on its own.
        /// </summary>
        public static ElementViewModel EtabsLoadArea(Slab_E area)
        {
            List<ResultField> fields = new List<ResultField>
            {
                Field(RawResultKeys.SlabName, "Name / id", area.UniqueName),
                Field(RawResultKeys.SlabSection, "Section / type", area.PropertyName.Length > 0 ? area.PropertyName : "None"),
                Field(RawResultKeys.SlabKind, "ETABS slab kind", "Load area (null, no slab)"),
                Field(RawResultKeys.SlabArea, "Area (m²)", Area(area.Plan)),
                Field(RawResultKeys.SlabLoads, "Loads (kN/m²)", LoadsOf(area.Loads))
            };

            ElementViewModel element = new SlabElementViewModel(ElementSource.EtabsOnly, CheckState.NotChecked,
                                                                area.UniqueName, area.Story, fields, area.Plan);
            element.Tab = ViewMode.SlabLoads;
            return Raw(element, "ETABS", "load area");
        }

        /// <summary>One Revit floor, under the slab tab.</summary>
        public static ElementViewModel RevitSlab(Slab_R slab)
        {
            List<ResultField> fields = new List<ResultField>
            {
                Field(RawResultKeys.SlabName, "Name / id", slab.RevitID.ToString()),
                Field(RawResultKeys.SlabSection, "Section / type", slab.TypeName),
                Field(RawResultKeys.SlabThickness, "Thickness (mm)", Number(slab.Thickness, 0)),
                Field(RawResultKeys.SlabArea, "Area (m²)", Area(slab.Plan))
            };

            ElementViewModel element = new SlabElementViewModel(
                ElementSource.RevitOnly, CheckState.NotChecked,
                slab.RevitID.ToString(), slab.Story, fields, slab.Plan);
            element.RevitId = slab.RevitID;
            element.Tab = ViewMode.Slabs;
            return Raw(element, "Revit", "floor");
        }

        /// <summary>
        /// One filled region off a level's loading plan, under the slab load tab: its number,
        /// and what the loading schedule says that number carries.
        /// </summary>
        public static ElementViewModel LoadRegion(LoadCoverage.Region region, string story)
        {
            string number = region.NumberText.Length > 0
                ? region.NumberText
                : region.Number;

            string loads = region.InSchedule
                ? region.Loads.ToString()
                : number.Length > 0 ? "not in the schedule" : "no number";

            List<ResultField> fields = new List<ResultField>
            {
                Field(RawResultKeys.SlabName, "Name / id", region.RevitId.ToString()),
                Field(RawResultKeys.SlabLoadNumber, "Load number", number),
                Field(RawResultKeys.SlabLoads, "Loads (kN/m²)", loads),
                Field(RawResultKeys.SlabDescription, "Description", region.Description),
                Field(RawResultKeys.SlabArea, "Area (m²)", Area(region.Plan))
            };

            ElementViewModel element = new SlabElementViewModel(
                ElementSource.RevitOnly, CheckState.NotChecked,
                "Load " + (number.Length > 0 ? number : "?") + " - " + region.RevitId, story,
                fields, region.Plan);
            element.RevitId = region.RevitId;
            element.Tab = ViewMode.SlabLoads;
            return Raw(element, "Revit", "loading region");
        }

        /// <summary>
        /// The area loads on a shell as ETABS holds them, one per pattern: "SDL 1.5, LL 2".
        /// A load that does not act straight down says which way it does act.
        /// </summary>
        private static string LoadsOf(IEnumerable<AreaLoad_E> loads)
        {
            return string.Join(", ", (loads ?? new AreaLoad_E[0])
                .Where(load => load != null)
                .Select(load => load.Pattern + " " + LoadSet.Text(load.Value)
                                + (load.IsGravity ? "" : " (direction " + load.Direction + ")")
                                + (load.IsToFrame ? " to frames" : "")));
        }

        private static string Area(PlanPolygon plan)
        {
            return plan == null ? "" : Number(plan.Area / 1e6, 2);
        }

        #endregion

        #region line loads

        /// <summary>
        /// One line drawn on a loading plan, as it was read: the number its style gives it and
        /// the load per metre the schedule gives that number. Nothing about the frames under it.
        /// </summary>
        public static ElementViewModel RevitLineLoad(LineLoadResult result)
        {
            LineLoad_R line = result.Line;

            string load = result.Expected > 0
                ? LoadSet.Text(result.Expected) + " kN/m"
                : line.HasNumber ? "not in the schedule" : "no number";

            List<ResultField> fields = new List<ResultField>
            {
                Field(RawResultKeys.LineLoadName, "Name / id", line.RevitID.ToString()),
                Field(RawResultKeys.LineLoadNumber, "Load number", line.HasNumber ? line.Number.ToString() : ""),
                Field(RawResultKeys.LineLoadLoad, "Load", load),
                Field(RawResultKeys.LineLoadStyle, "Line style", line.StyleName),
                Field(RawResultKeys.LineLoadLength, "Length (m)", Metres(line.Length))
            };

            IEnumerable<MyPoint> polyline = line.Pieces.Select(piece => piece.PlanStart)
                .Concat(line.Pieces.Count == 0
                            ? new MyPoint[0]
                            : new[] { line.Pieces[line.Pieces.Count - 1].PlanEnd });

            ElementViewModel element = new LineLoadElementViewModel(
                ElementSource.RevitOnly, CheckState.NotChecked,
                line.ToString(), line.Story, fields, polyline);
            element.RevitId = line.RevitID;
            element.Tab = ViewMode.LineLoads;
            return Raw(element, "Revit", "line load");
        }

        /// <summary>
        /// One wall or fill strip the SIDL_C check weighs, as it was read: what it is, how thick
        /// and how high or wide, and the weight per metre that comes to. Nothing about the
        /// frames under it. Read with the Revit model, so it is here before any check has run.
        /// </summary>
        public static ElementViewModel SidlCSource(SidlCSource_R source)
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            LineLoad_R line = source.ToLineLoad(pref.SidlCConcreteDensity, pref.LineLoadRoundUpTo,
                                                SidlCSource_R.ParseIntervals(pref.SidlCLoadIntervals),
                                                pref.SidlCLoadFromComment);

            List<ResultField> fields = new List<ResultField>
            {
                Field(RawResultKeys.LineLoadName, "Name / id", source.RevitID.ToString()),
                Field(RawResultKeys.LineLoadStyle, "Revit element", source.Description),
                Field(RawResultKeys.LineLoadLoad, "Load", LoadSet.Text(line.OwnPerMetre) + " kN/m - " + line.OwnHow),
                Field(RawResultKeys.LineLoadLength, "Length (m)", Metres(source.Length))
            };

            ElementViewModel element = new LineLoadElementViewModel(
                ElementSource.RevitOnly, CheckState.NotChecked,
                line.ToString(), source.Story, fields, source.PlanLine);
            element.RevitId = source.RevitID;
            element.Tab = ViewMode.SidlC;
            return Raw(element, "Revit", source.Kind == SidlCSourceKind.Wall ? "wall" : "fill strip");
        }

        /// <summary>
        /// One light weight fill floor the SIDL_C area check weighs, as it was read: its depth,
        /// its area and what that weighs. One element per separate piece of it.
        /// </summary>
        public static IEnumerable<ElementViewModel> SidlCAreaSource(SidlCAreaSource_R fill)
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            double weight = fill.PerSquareMetre(pref.SidlCAreaLightweightDensity, pref.SidlCAreaToppingDensity,
                                                pref.SidlCAreaToppingThickness, pref.SidlCAreaRoundUpTo);
            string how = fill.How(pref.SidlCAreaLightweightDensity, pref.SidlCAreaToppingDensity,
                                  pref.SidlCAreaToppingThickness, pref.SidlCAreaRoundUpTo);

            List<ResultField> fields = new List<ResultField>
            {
                Field(RawResultKeys.SlabName, "Name / id", fill.RevitID.ToString()),
                Field(RawResultKeys.SlabSection, "Section / type", fill.Description),
                Field(RawResultKeys.SlabArea, "Area (m²)", Number(fill.Area / 1e6, 2)),
                Field(RawResultKeys.SlabLoads, "Load (kN/m²)", LoadSet.Text(weight) + " - " + how)
            };

            foreach (PlanPolygon part in fill.Plan)
            {
                ElementViewModel element = new SlabElementViewModel(ElementSource.RevitOnly, CheckState.NotChecked,
                                                                    fill.ToString(), fill.Story, fields, part);
                element.RevitId = fill.RevitID;
                element.Tab = ViewMode.SidlCArea;
                yield return Raw(element, "Revit", "light weight fill");
            }
        }

        /// <summary>
        /// One ETABS frame carrying distributed loads, as ETABS holds them: every load on it,
        /// by pattern, with the stretch of the frame it covers where that is not all of it.
        /// </summary>
        public static ElementViewModel EtabsLineLoad(LineLoadFrame_E frame)
        {
            List<ResultField> fields = new List<ResultField>
            {
                Field(RawResultKeys.LineLoadName, "Name / id", frame.UniqueName),
                Field(RawResultKeys.LineLoadLabel, "ETABS label", frame.Label),
                Field(RawResultKeys.LineLoadLoad, "Load",
                      string.Join("; ", frame.Loads.Where(one => one != null).Select(one => one.ToString()))),
                Field(RawResultKeys.LineLoadLength, "Length (m)", Metres(frame.PlanLength))
            };

            return Raw(new LineLoadElementViewModel(ElementSource.EtabsOnly, CheckState.NotChecked,
                                                    "ETABS frame " + frame.Identifier, frame.Story, fields,
                                                    new[] { frame.PlanStart, frame.PlanEnd }),
                       "ETABS", "frame");
        }

        /// <summary>The ETABS frames worth drawing as line loads: a line in plan, and a load on it.</summary>
        public static bool CarriesALineLoad(LineLoadFrame_E frame)
        {
            return frame != null && frame.HasPlanLine && frame.Loads.Any(load => load != null);
        }

        #endregion

        /// <summary>Marks an element as raw data and says so on its card.</summary>
        private static ElementViewModel Raw(ElementViewModel element, string model, string what)
        {
            element.IsRaw = true;
            element.Outcomes = new List<CheckOutcome>();
            element.PairedWith = "Raw data: the " + model + " " + what + " as it was read. Nothing is "
                                 + "paired or compared while Raw data is on.";
            return element;
        }

        private static ResultField Field(string key, string label, string value)
        {
            return new ResultField(key, label, value ?? "");
        }

        /// <summary>The distinct values, in the order met, for the panels folded into one wall.</summary>
        private static string Join(IEnumerable<string> values)
        {
            return string.Join(", ", values.Where(value => !string.IsNullOrEmpty(value)).Distinct().ToArray());
        }

        private static string Number(double value, int decimals)
        {
            return Math.Round(value, decimals).ToString(decimals == 0 ? "0" : "0.##", CultureInfo.CurrentCulture);
        }

        private static string Metres(double millimetres)
        {
            return Number(millimetres / 1000d, 2);
        }
    }
}
