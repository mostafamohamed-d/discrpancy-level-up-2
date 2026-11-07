using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// Turns the comparison containers into the drawable, annotatable elements the viewer
    /// works with. Everything the canvas can label comes out of here, so a new check only
    /// has to publish result fields to become visible on the model.
    /// </summary>
    public static class ElementViewModelFactory
    {
        public static ElementViewModel FromPairedColumn(PairedColumn column)
        {
            ElementViewModel element = new ColumnElementViewModel(
                ElementSource.Paired, StateOf(column), column.revitColumn.RevitID.ToString(),
                column.Story, column.ResultFields, column.revitColumn);
            element.Outcomes = column.Outcomes;
            element.EtabsUniqueName = column.etabsColumn.EtabsUniqueName;
            element.Counterpart = EtabsCounterpart(column);
            SetRevitId(element, column.revitColumn.RevitID);
            return element;
        }

        /// <summary>
        /// The ETABS section under a paired column, for the columns the two models disagree
        /// about geometrically. Null for the rest, which is the ordinary answer.
        ///
        /// What is drawn for a paired column is the Revit section, coloured by how it did. Red
        /// says a check failed and stops there: a column 300mm out of place, a column turned
        /// thirty degrees and a column 100mm too deep are the same red rectangle. Drawing the
        /// ETABS section under it as an outline makes the gap between the two the answer.
        ///
        /// Only where a geometric check failed. A type mark that does not match its groups is
        /// not a difference two outlines could show, and a column that passed everything has two
        /// outlines on top of each other, which is twice the ink for nothing.
        /// </summary>
        private static ElementViewModel EtabsCounterpart(PairedColumn column)
        {
            if (!PrefrencesData.getPrefrencesData().DrawBothColumnsOnMismatch
                || !column.Outcomes.Any(IsDrawableColumnFailure))
            {
                return null;
            }

            Column_e etabs = column.etabsColumn;

            List<ResultField> fields = new List<ResultField>
            {
                new ResultField(ColumnResultKeys.EtabsName, "ETABS name", etabs.EtabsUniqueName),
                new ResultField(ColumnResultKeys.EtabsSection, "ETABS section", etabs.SectionName)
            };

            // Context, so it takes no part in the picking, the labels, the chip counts or the
            // findings list: it carries no verdict of its own, and the column it is drawn under
            // already carries the one they share. Its colour comes from IsCounterpart instead.
            //
            // Left with no Revit id and no ETABS name of its own on purpose - both of those are
            // how an element is filed when somebody reviews it, and there is nothing here to
            // file that the column above it does not already answer for.
            ElementViewModel outline = new ColumnElementViewModel(
                ElementSource.EtabsOnly, CheckState.NotChecked,
                etabs.EtabsUniqueName, etabs.Story, fields, etabs)
            {
                IsContext = true,
                IsCounterpart = true
            };
            return outline;
        }

        /// <summary>
        /// The failures worth drawing a second outline for: where the column stands, how it is
        /// turned, and what section it is. Shape is in as well as size because a check switched
        /// off publishes nothing, so a project checking the shape but not the dimensions would
        /// otherwise see a round column drawn square with nothing on the plan to say so.
        /// </summary>
        private static bool IsDrawableColumnFailure(CheckOutcome outcome)
        {
            if (outcome.Level != ResultLevel.Failed)
            {
                return false;
            }
            return outcome.Kind == CheckKind.Location
                || outcome.Kind == CheckKind.Orientation
                || outcome.Kind == CheckKind.Shape
                || outcome.Kind == CheckKind.Dimensions;
        }

        /// <summary>The single result an element that never went through the checks carries.</summary>
        private static List<CheckOutcome> OneOutcome(CheckKind kind, ResultLevel level, string summary)
        {
            return new List<CheckOutcome> { new CheckOutcome(kind, level, summary) };
        }

        /// <summary>
        /// The verdict on an element, read off the results it carries rather than kept beside
        /// them. A second verdict worked out some other way is how the colour on the plan and
        /// the rows on the card come to say different things about the same element.
        /// </summary>
        private static CheckState StateOf(IList<CheckOutcome> outcomes)
        {
            if (outcomes.Any(outcome => outcome.Level == ResultLevel.Failed))
            {
                return CheckState.Failed;
            }
            if (outcomes.Any(outcome => outcome.Level == ResultLevel.Review))
            {
                return CheckState.Review;
            }

            // Nothing left to say, because the check is switched off. Drawn grey rather than
            // green: a check that was never made is not a check that agreed.
            return outcomes.Count == 0 ? CheckState.NotChecked : CheckState.Passed;
        }

        private static CheckState StateOf(PairedColumn column)
        {
            switch (column.State)
            {
                case ColumnCheckState.Matched:
                    return CheckState.Passed;
                case ColumnCheckState.Review:
                    return CheckState.Review;
                default:
                    return CheckState.Failed;
            }
        }

        public static ElementViewModel FromEtabsColumn(Column_e column, bool compared)
        {
            List<ResultField> fields = new List<ResultField>
            {
                new ResultField(ColumnResultKeys.EtabsName, "ETABS name", column.EtabsUniqueName),
                new ResultField(ColumnResultKeys.EtabsSection, "ETABS section", column.SectionName),
                new ResultField(ColumnResultKeys.EtabsGroups, "ETABS groups",
                    string.Join(", ", column.Groups.ToArray())),
                new ResultField(ColumnResultKeys.State, "Check state",
                    compared ? "No Revit column" : "Not checked", compared)
            };
            AddSectionFields(fields, column);

            ElementViewModel element = new ColumnElementViewModel(
                compared ? ElementSource.UnmatchedEtabs : ElementSource.EtabsOnly,
                compared ? CheckState.Unpaired : CheckState.NotChecked,
                column.EtabsUniqueName, column.Story, fields, column);
            element.Outcomes = PairingOutcome(compared, "no Revit column");
            element.EtabsUniqueName = column.EtabsUniqueName;
            return element;
        }

        /// <summary>
        /// The one result an element carries when it was never paired: either it has no
        /// counterpart, or nothing has been compared yet.
        /// </summary>
        private static List<CheckOutcome> PairingOutcome(bool compared, string summary)
        {
            return OneOutcome(CheckKind.Pairing,
                compared ? ResultLevel.Failed : ResultLevel.NotChecked,
                compared ? summary : "not checked");
        }

        public static ElementViewModel FromRevitColumn(Column_r column, bool compared)
        {
            List<ResultField> fields = new List<ResultField>
            {
                new ResultField(ColumnResultKeys.RevitId, "Revit id", column.RevitID.ToString()),
                new ResultField(ColumnResultKeys.RevitType, "Revit type", column.TypeName),
                new ResultField(ColumnResultKeys.SizeSource, "Revit size from", column.SizeSource ?? ""),
                new ResultField(ColumnResultKeys.RevitFamily, "Revit family", column.FamilyName),
                new ResultField(ColumnResultKeys.TypeMark, "Column Type Mark", column.TypeMark),
                new ResultField(ColumnResultKeys.State, "Check state",
                    compared ? "No ETABS column" : "Not checked", compared)
            };
            AddSectionFields(fields, column);

            ElementViewModel element = new ColumnElementViewModel(
                compared ? ElementSource.UnmatchedRevit : ElementSource.RevitOnly,
                compared ? CheckState.Unpaired : CheckState.NotChecked,
                column.RevitID.ToString(), column.Story, fields, column);
            element.Outcomes = PairingOutcome(compared, "no ETABS column");
            SetRevitId(element, column.RevitID);
            return element;
        }

        public static ElementViewModel FromPairedFrame(PairedFrame frame)
        {
            ElementViewModel element = new BeamElementViewModel(
                ElementSource.Paired, StateOf(frame.Outcomes), frame.RevitID.ToString(),
                frame.Story, frame.ResultFields, frame.Start, frame.End);
            // Every question the beam check asked, each carrying the two numbers behind it.
            // They share one check kind, and so one switch in the filter panel, and are told
            // apart by the place each of them names.
            element.Outcomes = frame.Outcomes;
            element.EtabsUniqueName = frame.etabsFrame?.EtabsUname ?? "";
            element.PlaceFingerprint = BeamPlace(frame.etabsFrame, frame.revitFrame);
            SetRevitId(element, frame.RevitID);
            return element;
        }

        /// <summary>
        /// Where a beam stands and how big it is, in both models if it is in both, so that an
        /// acceptance or a review of it goes stale when either model moves it or resizes it.
        ///
        /// Each side in its own model's terms - ETABS as read, Revit before the alignment is
        /// put on it - so realigning the two models is not taken for the beam moving. Whole
        /// millimetres, ends in a fixed order so a beam drawn again from its other end has not
        /// moved.
        /// </summary>
        private static string BeamPlace(FrameData_e etabs, FrameData_r revit)
        {
            List<string> sides = new List<string>();

            if (etabs?.Start != null && etabs.End != null)
            {
                sides.Add("E" + Ends(etabs.Start, etabs.End) + "@" + Size(etabs));
            }

            if (revit?.Start != null && revit.End != null)
            {
                const double feetToMM = 304.8;
                MyPoint start = revit.revitStart == null ? revit.Start : revit.revitStart * feetToMM;
                MyPoint end = revit.revitEnd == null ? revit.End : revit.revitEnd * feetToMM;
                sides.Add("R" + Ends(start, end) + "@" + Size(revit));
            }

            return string.Join(" ", sides);
        }

        private static string Ends(MyPoint start, MyPoint end)
        {
            string first = Whole(start), second = Whole(end);
            return string.CompareOrdinal(first, second) <= 0
                ? first + ";" + second
                : second + ";" + first;
        }

        private static string Whole(MyPoint point)
        {
            return string.Join(",", Whole(point.X), Whole(point.Y), Whole(point.Z));
        }

        private static string Size(FrameData frame)
        {
            return Whole(frame._width) + "x" + Whole(frame._depth);
        }

        private static string Whole(double value)
        {
            return Math.Round(value).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static ElementViewModel FromEtabsFrame(FrameData_e frame, bool compared)
        {
            List<ResultField> fields = new List<ResultField>
            {
                new ResultField(FrameResultKeys.EtabsName, "ETABS unique name", frame.etabsUname),
                new ResultField(FrameResultKeys.EtabsLabel, "ETABS beam label", frame.EtabsLabel),
                new ResultField(FrameResultKeys.State, "Check state",
                    compared ? "No Revit beam" : "Not checked", compared),
                new ResultField(FrameResultKeys.Width, "Width R / E", $"- / {frame._width}"),
                new ResultField(FrameResultKeys.Depth, "Depth R / E", $"- / {frame._depth}")
            };

            ElementViewModel element = new BeamElementViewModel(
                compared ? ElementSource.UnmatchedEtabs : ElementSource.EtabsOnly,
                compared ? CheckState.Unpaired : CheckState.NotChecked,
                frame.etabsUname, frame.Story, fields, frame.Start, frame.End);
            element.Outcomes = PairingOutcome(compared, "no Revit beam");
            element.EtabsUniqueName = frame.etabsUname;
            element.PlaceFingerprint = BeamPlace(frame, null);
            return element;
        }

        public static ElementViewModel FromRevitFrame(FrameData_r frame, bool compared)
        {
            List<ResultField> fields = new List<ResultField>
            {
                new ResultField(FrameResultKeys.RevitId, "Revit id", frame.RevitID.ToString()),
                new ResultField(FrameResultKeys.BeamMark, "Beam Reinf. Mark", frame.BeamMark),
                new ResultField(FrameResultKeys.State, "Check state",
                    compared ? "No ETABS beam" : "Not checked", compared),
                new ResultField(FrameResultKeys.Width, "Width R / E", $"{frame._width} / -"),
                new ResultField(FrameResultKeys.Depth, "Depth R / E", $"{frame._depth} / -"),
                new ResultField(FrameResultKeys.SizeSource, "Revit size from", frame.SizeSource ?? "")
            };

            ElementViewModel element = new BeamElementViewModel(
                compared ? ElementSource.UnmatchedRevit : ElementSource.RevitOnly,
                compared ? CheckState.Unpaired : CheckState.NotChecked,
                frame.RevitID.ToString(), frame.Story, fields, frame.Start, frame.End);
            element.Outcomes = PairingOutcome(compared, "no ETABS beam");
            element.PlaceFingerprint = BeamPlace(null, frame);
            SetRevitId(element, frame.RevitID);
            return element;
        }

        #region walls

        /// <summary>
        /// One checked wall, drawn where ETABS has it. The pair is one ETABS panel against one
        /// Revit wall, so a Revit wall with several ETABS panels along it appears once per
        /// panel - each with its own verdict, which is the only way a run that is right at one
        /// end and wrong at the other can be read off the plan.
        /// </summary>
        public static ElementViewModel FromPairedWall(PairedWall wall)
        {
            ElementViewModel element = new WallElementViewModel(
                ElementSource.Paired, StateOf(wall), wall.revitWall.RevitID.ToString(),
                wall.Story, wall.ResultFields, wall.PlanStart, wall.PlanEnd, wall.Thickness);

            element.Outcomes = wall.Outcomes;
            element.EtabsUniqueName = wall.etabsWall.UniqueName;
            SetRevitId(element, wall.revitWall.RevitID);
            return element;
        }

        /// <summary>
        /// The panels standing on one line of wall, drawn as the one wall they are.
        ///
        /// A wall meshed up its height puts several panels in exactly the same place in plan.
        /// Drawn one at a time they are indistinguishable from a single wall, the last one
        /// drawn hides the colour of the ones under it, and only the first of them can be
        /// picked - so a failed panel under a passed one is invisible and unreachable at once.
        ///
        /// Folded, the wall is drawn once, in the colour of the worst panel on it, and the
        /// panel names are on the wall so it is clear what has been folded. Nothing is
        /// averaged: every number reported is the worst of the panels, because a wall is only
        /// as right as its worst piece.
        /// </summary>
        public static ElementViewModel FromPairedWallStack(IList<PairedWall> stack)
        {
            if (stack.Count == 1)
            {
                return FromPairedWall(stack[0]);
            }

            PairedWall worst = PairedWall.Worst(stack);
            Wall_R revitWall = worst.revitWall;

            List<ResultField> fields = new List<ResultField>
            {
                new ResultField(WallResultKeys.State, "Check state",
                    worst.State.ToString(), worst.State != WallCheckState.Matched),
                new ResultField(WallResultKeys.RevitId, "Revit id", revitWall.RevitID.ToString()),
                new ResultField(WallResultKeys.EtabsName, "ETABS name",
                    Join(stack.Select(wall => wall.etabsWall.UniqueName))),
                new ResultField(WallResultKeys.PanelCount, "ETABS panels here",
                    stack.Count.ToString()),
                new ResultField(WallResultKeys.RevitType, "Revit wall type", revitWall.TypeName),
                new ResultField(WallResultKeys.EtabsProperty, "ETABS property",
                    Join(stack.Select(wall => wall.etabsWall.PropertyName))),
                new ResultField(WallResultKeys.TypeMark, "Wall Type Mark", revitWall.TypeMark),

                new ResultField(WallResultKeys.Thickness, "Thickness R / E",
                    $"{Math.Round(revitWall.Thickness)} / " +
                    Join(stack.Select(wall => Math.Round(wall.etabsWall.Thickness).ToString())),
                    stack.Any(wall => !wall.ThicknessMatched), CheckKind.WallThickness),
                ResultField.Number(WallResultKeys.ThicknessDelta, "Thickness delta (mm)",
                    stack.Max(wall => wall.ThicknessDelta), 0,
                    stack.Any(wall => !wall.ThicknessMatched), CheckKind.WallThickness),

                ResultField.Number(WallResultKeys.LocationDelta, "Line offset (mm)",
                    stack.Max(wall => wall.LocationDelta), 0,
                    stack.Any(wall => !wall.LocationMatched), CheckKind.WallLocation),
                ResultField.Number(WallResultKeys.AngleDelta, "Angle delta (deg)",
                    stack.Max(wall => wall.AngleDelta), 1,
                    stack.Any(wall => !wall.LocationMatched), CheckKind.WallLocation),
                ResultField.Number(WallResultKeys.Overlap, "Runs alongside (mm)",
                    stack.Max(wall => wall.OverlapLength), 0, false, CheckKind.WallLocation),

                new ResultField(WallResultKeys.Coverage, "ETABS length / Revit length",
                    $"{Math.Round(worst.CoveredLength)} / {Math.Round(revitWall.Length)}",
                    stack.Any(wall => !wall.LengthMatched), CheckKind.WallLength),

                new ResultField(WallResultKeys.Note, "Note",
                    Join(stack.Select(wall => wall.Note)), worst.State != WallCheckState.Matched)
            };

            ElementViewModel element = new WallElementViewModel(
                ElementSource.Paired, StateOf(worst), revitWall.RevitID.ToString(),
                worst.Story, fields, worst.PlanStart, worst.PlanEnd, worst.Thickness);

            // Every panel's results, so the filters work on the folded wall exactly as they do
            // on a single one: hiding the failures leaves the passes on the other panels, and
            // the wall keeps its place on the plan either way.
            element.Outcomes = Merged(stack.SelectMany(wall => wall.Outcomes));
            // Any panel of the stack names it: the engine spreads a pairing across every panel
            // on the same line, so which one is written down does not matter.
            element.EtabsUniqueName = worst.etabsWall.UniqueName;

            // The panels themselves, so ctrl+click can look through the folded wall and reach
            // the one that actually failed rather than only the wall it was folded into.
            element.Parts = stack.Select(FromPairedWall).ToList();
            foreach (ElementViewModel part in element.Parts)
            {
                part.PartOf = element;
            }

            SetRevitId(element, revitWall.RevitID);
            return element;
        }

        /// <summary>The panels of one wall that were never paired, drawn as one wall.</summary>
        public static ElementViewModel FromEtabsWallStack(IList<Wall_E> stack, bool compared)
        {
            if (stack.Count == 1)
            {
                return FromEtabsWall(stack[0], compared);
            }

            Wall_E first = stack[0];

            List<ResultField> fields = new List<ResultField>
            {
                new ResultField(WallResultKeys.EtabsName, "ETABS name",
                    Join(stack.Select(wall => wall.UniqueName))),
                new ResultField(WallResultKeys.PanelCount, "ETABS panels here",
                    stack.Count.ToString()),
                new ResultField(WallResultKeys.EtabsProperty, "ETABS property",
                    Join(stack.Select(wall => wall.PropertyName))),
                new ResultField(WallResultKeys.Thickness, "Thickness R / E",
                    "- / " + Join(stack.Select(wall => Math.Round(wall.Thickness).ToString()))),
                new ResultField(WallResultKeys.State, "Check state",
                    compared ? "No Revit wall" : "Not checked", compared)
            };

            ElementViewModel element = new WallElementViewModel(
                compared ? ElementSource.UnmatchedEtabs : ElementSource.EtabsOnly,
                compared ? CheckState.Unpaired : CheckState.NotChecked,
                first.UniqueName, first.Story, fields,
                first.PlanStart, first.PlanEnd, stack.Max(wall => wall.Thickness));

            element.Outcomes = PairingOutcome(compared, "no Revit wall");
            element.EtabsUniqueName = first.UniqueName;
            element.Parts = stack.Select(panel => FromEtabsWall(panel, compared)).ToList();
            foreach (ElementViewModel part in element.Parts)
            {
                part.PartOf = element;
            }
            return element;
        }

        /// <summary>
        /// One result per check per verdict. Three panels that all passed their thickness say
        /// so once; one that failed keeps its own result beside the others' pass.
        /// </summary>
        private static List<CheckOutcome> Merged(IEnumerable<CheckOutcome> outcomes)
        {
            return outcomes.GroupBy(outcome => outcome.Kind + "|" + outcome.Level)
                           .Select(group => group.First())
                           .ToList();
        }

        /// <summary>The distinct values among the panels, in the order they were met.</summary>
        private static string Join(IEnumerable<string> values)
        {
            return string.Join(", ", values.Where(value => !string.IsNullOrEmpty(value))
                                           .Distinct()
                                           .ToArray());
        }

        private static CheckState StateOf(PairedWall wall)
        {
            switch (wall.State)
            {
                case WallCheckState.Matched:
                    return CheckState.Passed;
                case WallCheckState.Review:
                    return CheckState.Review;
                default:
                    return CheckState.Failed;
            }
        }

        public static ElementViewModel FromEtabsWall(Wall_E wall, bool compared)
        {
            List<ResultField> fields = new List<ResultField>
            {
                new ResultField(WallResultKeys.EtabsName, "ETABS name", wall.UniqueName),
                new ResultField(WallResultKeys.EtabsProperty, "ETABS property", wall.PropertyName),
                new ResultField(WallResultKeys.Thickness, "Thickness R / E",
                    $"- / {Math.Round(wall.Thickness)}"),
                new ResultField(WallResultKeys.State, "Check state",
                    compared ? "No Revit wall" : "Not checked", compared)
            };

            ElementViewModel element = new WallElementViewModel(
                compared ? ElementSource.UnmatchedEtabs : ElementSource.EtabsOnly,
                compared ? CheckState.Unpaired : CheckState.NotChecked,
                wall.UniqueName, wall.Story, fields,
                wall.PlanStart, wall.PlanEnd, wall.Thickness);

            element.Outcomes = PairingOutcome(compared, "no Revit wall");
            element.EtabsUniqueName = wall.UniqueName;
            return element;
        }

        public static ElementViewModel FromRevitWall(Wall_R wall, bool compared)
        {
            List<ResultField> fields = new List<ResultField>
            {
                new ResultField(WallResultKeys.RevitId, "Revit id", wall.RevitID.ToString()),
                new ResultField(WallResultKeys.RevitType, "Revit wall type", wall.TypeName),
                new ResultField(WallResultKeys.TypeMark, "Wall Type Mark", wall.TypeMark),
                new ResultField(WallResultKeys.Thickness, "Thickness R / E",
                    $"{Math.Round(wall.Thickness)} / -"),
                new ResultField(WallResultKeys.State, "Check state",
                    compared ? "No ETABS wall" : "Not checked", compared)
            };

            ElementViewModel element = new WallElementViewModel(
                compared ? ElementSource.UnmatchedRevit : ElementSource.RevitOnly,
                compared ? CheckState.Unpaired : CheckState.NotChecked,
                wall.RevitID.ToString(), wall.Story, fields,
                wall.PlanStart, wall.PlanEnd, wall.Thickness);

            element.Outcomes = PairingOutcome(compared, "no ETABS wall");
            SetRevitId(element, wall.RevitID);
            return element;
        }

        /// <summary>
        /// A stretch of Revit wall with no ETABS wall along it, drawn as the piece it is.
        ///
        /// The plan is the ETABS model and a paired Revit wall is not on it: where ETABS stops
        /// short, the panel says so as a warning. Past the buffer a warning is not enough, and
        /// what goes on the plan is the missing stretch alone - not the whole Revit wall, which
        /// would only be laid over the panels already drawn. So what is seen is what ETABS has,
        /// and then, in the colour of a wall Revit has on its own, the part it does not.
        /// </summary>
        public static ElementViewModel FromUncoveredWallPiece(UncoveredWallPiece piece)
        {
            Wall_R wall = piece.Wall;

            List<ResultField> fields = new List<ResultField>
            {
                new ResultField(WallResultKeys.RevitId, "Revit id", wall.RevitID.ToString()),
                new ResultField(WallResultKeys.RevitType, "Revit wall type", wall.TypeName),
                new ResultField(WallResultKeys.TypeMark, "Wall Type Mark", wall.TypeMark),
                new ResultField(WallResultKeys.Thickness, "Thickness R / E",
                    $"{Math.Round(wall.Thickness)} / -"),
                new ResultField(WallResultKeys.Coverage, "ETABS length / Revit length",
                    $"{Math.Round(piece.Covered)} / {Math.Round(wall.Length)}", true),
                new ResultField(WallResultKeys.LengthShortfall, "Length ETABS does not reach (mm)",
                    Math.Round(piece.Uncovered).ToString(), true),
                new ResultField(WallResultKeys.State, "Check state",
                    "No ETABS wall along this stretch", true)
            };

            ElementViewModel element = new WallElementViewModel(
                ElementSource.UnmatchedRevit, CheckState.Unpaired,
                wall.RevitID.ToString(), wall.Story, fields,
                piece.Start, piece.End, wall.Thickness);

            element.Outcomes = OneOutcome(CheckKind.Pairing, ResultLevel.Failed,
                $"{Math.Round(piece.Length)}mm of this Revit wall has no ETABS wall along it");
            SetRevitId(element, wall.RevitID);
            return element;
        }

        #endregion

        #region columns and walls paired across the kinds

        /// <summary>
        /// An ETABS column that Revit has as a wall, drawn as the ETABS column - the plan is the
        /// ETABS model - with the results of weighing it against the wall.
        /// </summary>
        public static ElementViewModel FromCrossPair(CrossPair pair)
        {
            Column_e column = pair.EtabsColumn;

            ElementViewModel element = new ColumnElementViewModel(
                ElementSource.Paired, StateOf(pair.Outcomes), pair.RevitID.ToString(),
                pair.Story, pair.ResultFields, column);
            element.Outcomes = pair.Outcomes;
            element.EtabsUniqueName = column.EtabsUniqueName;
            SetRevitId(element, pair.RevitID);
            return element;
        }

        /// <summary>
        /// ETABS walls that Revit has as columns, the panels standing on one line drawn as the
        /// one wall they are, exactly as the paired walls are.
        /// </summary>
        public static IEnumerable<ElementViewModel> FromCrossPairWalls(IEnumerable<CrossPair> pairs)
        {
            List<CrossPair> list = pairs.ToList();
            Dictionary<Wall_E, CrossPair> byPanel = list.ToDictionary(pair => pair.EtabsWall);

            foreach (List<Wall_E> stack in WallStacks.Group(list.Select(pair => pair.EtabsWall)))
            {
                List<ElementViewModel> panels = stack.Select(panel => FromCrossPairWall(byPanel[panel])).ToList();
                if (panels.Count == 1)
                {
                    yield return panels[0];
                    continue;
                }

                // Coloured and carded by the worst panel, with every panel's results on it so
                // the filters treat it as they treat a folded paired wall.
                ElementViewModel worst = panels.OrderByDescending(panel => Severity(panel.State)).First();
                CrossPair worstPair = byPanel[stack[panels.IndexOf(worst)]];
                Wall_E first = worstPair.EtabsWall;

                List<ResultField> fields = worstPair.ResultFields.ToList();
                fields.Insert(1, new ResultField(WallResultKeys.PanelCount, "ETABS panels here",
                    stack.Count.ToString()));

                ElementViewModel element = new WallElementViewModel(
                    ElementSource.Paired, worst.State, worstPair.RevitID.ToString(), worstPair.Story,
                    fields, first.PlanStart, first.PlanEnd, stack.Max(panel => panel.Thickness));
                element.Outcomes = Merged(stack.SelectMany(panel => byPanel[panel].Outcomes));
                element.EtabsUniqueName = first.UniqueName;
                element.Parts = panels;
                foreach (ElementViewModel part in panels)
                {
                    part.PartOf = element;
                }
                SetRevitId(element, worstPair.RevitID);
                yield return element;
            }
        }

        private static ElementViewModel FromCrossPairWall(CrossPair pair)
        {
            Wall_E wall = pair.EtabsWall;

            ElementViewModel element = new WallElementViewModel(
                ElementSource.Paired, StateOf(pair.Outcomes), pair.RevitID.ToString(),
                pair.Story, pair.ResultFields, wall.PlanStart, wall.PlanEnd, wall.Thickness);
            element.Outcomes = pair.Outcomes;
            element.EtabsUniqueName = wall.UniqueName;
            SetRevitId(element, pair.RevitID);
            return element;
        }

        private static int Severity(CheckState state)
        {
            switch (state)
            {
                case CheckState.Failed: return 2;
                case CheckState.Review: return 1;
                default: return 0;
            }
        }

        #endregion

        #region slabs

        /// <summary>
        /// One piece of the checked slab. Normally that is a whole ETABS area drawn as it was
        /// modelled; where the two models stopped agreeing part way across an area it is the
        /// shape of the disagreement.
        /// </summary>
        #region line loads

        /// <summary>
        /// One drawn load line, coloured by what the frames under it had to say.
        ///
        /// The Revit element is what is drawn here, which is this tab's whole point: it is
        /// read against the loading drawing, so what wants colouring is the line somebody drew
        /// rather than the frame it landed on.
        /// </summary>
        /// <summary>
        /// Everything one drawn line puts on the plan: the line whole, or - where it was
        /// checked in steps and does not read the same all the way along - one element per
        /// stretch, so the part that is wrong is the part drawn red and the rest stays green.
        /// </summary>
        public static IEnumerable<ElementViewModel> FromLineLoadResult(LineLoadResult result)
        {
            return FromLineLoadResult(result, false);
        }

        /// <summary>
        /// The same, for the line load tab or - <paramref name="sidlC"/> - the SIDL_C tab, whose
        /// lines are walls and fill strips weighed as load. Built the same way and told apart
        /// by the tab they answer to, the check they report and the name they are filed under.
        /// </summary>
        public static IEnumerable<ElementViewModel> FromLineLoadResult(LineLoadResult result, bool sidlC)
        {
            if (result.Stretches.Count <= 1)
            {
                return new[] { FromLineLoad(result, sidlC) };
            }

            return result.Stretches.Select(stretch => FromLineLoadStretch(result, stretch, sidlC)).ToList();
        }

        /// <summary>
        /// One stretch of a drawn line checked in steps: what the plan asks for there, what
        /// ETABS carries there, and where along the line it is.
        ///
        /// Filed under the line and the stretch's place along it, so a review of one stretch
        /// is not taken as a review of the whole line - and moves with it only while the line
        /// reads the same, the same fragile bargain a piece of slab makes.
        /// </summary>
        public static ElementViewModel FromLineLoadStretch(LineLoadResult result, LineLoadStretch stretch,
                                                           bool sidlC = false)
        {
            LineLoad_R line = result.Line;

            List<ResultField> fields = new List<ResultField>
            {
                new ResultField(LineLoadResultKeys.RevitId, "Revit id", line.RevitID.ToString()),
                new ResultField(LineLoadResultKeys.Style, line.HasOwnValue ? "Revit element" : "Line style",
                                line.StyleName),
                new ResultField(LineLoadResultKeys.Number, "Load number",
                                line.HasNumber ? line.Number.ToString() : ""),
                new ResultField(LineLoadResultKeys.Description, "Schedule",
                                result.Entry == null ? "" : result.Entry.Summary),
                new ResultField(LineLoadResultKeys.Stretch, "This stretch",
                                stretch.Where + " (" + Metres(stretch.Length) + " of "
                                + Metres(line.Length) + ")"),
                new ResultField(LineLoadResultKeys.Frames, "ETABS frames",
                                stretch.Frames.Length > 0 ? stretch.Frames : result.Frames),
                new ResultField(LineLoadResultKeys.Expected,
                                line.HasOwnValue ? "Its weight" : "Plan asks for here",
                                LoadSet.Text(stretch.Wanted) + " kN/m"),
                new ResultField(LineLoadResultKeys.Actual, "ETABS carries here",
                                stretch.Carried > 0 ? LoadSet.Text(stretch.Carried) + " kN/m" : "nothing",
                                stretch.IsFailure),
                new ResultField(LineLoadResultKeys.Working, "Worked out as", result.How)
            };

            // Only on a line a zone drawn by hand gave its height to: the level's own height is
            // the ordinary case and needs no saying.
            if (result.HeightZone.Length > 0)
            {
                fields.Add(new ResultField(LineLoadResultKeys.HeightZone, "Height from", result.HeightZone));
            }

            if (result.Alongside.Length > 0)
            {
                fields.Add(new ResultField(LineLoadResultKeys.Alongside, "Alongside it",
                                           result.Alongside, true));
            }

            bool inRevitOnly = stretch.State == LineLoadState.NoEtabsLoad
                               || stretch.State == LineLoadState.NoEtabsFrame
                               || stretch.State == LineLoadState.TakenByOther;
            bool tooShort = TooShortToCount(stretch.Length, stretch.Level);

            ElementViewModel element = new LineLoadElementViewModel(
                inRevitOnly ? ElementSource.UnmatchedRevit : ElementSource.RevitOnly,
                inRevitOnly ? CheckState.Unpaired : StateOf(stretch.Level),
                line + ", " + Metres(stretch.From) + " to " + Metres(stretch.To),
                line.Story, fields, PointsAlong(line, stretch.From, stretch.To))
            {
                Result = result
            };

            element.Outcomes = new List<CheckOutcome>
            {
                new CheckOutcome(LineKind(inRevitOnly, sidlC),
                                 stretch.Level,
                                 StretchSummary(result, stretch) + NotCountedAside(stretch.Length, tooShort),
                                 etabs: stretch.Carried > 0 ? LoadSet.Text(stretch.Carried) + " kN/m" : "nothing",
                                 revit: LoadSet.Text(stretch.Wanted) + " kN/m")
            };

            CheckSwitches.RemoveSwitchedOff(element.Outcomes as List<CheckOutcome>);
            SetRevitId(element, line.RevitID);
            element.IsTooSmallToCount = tooShort;

            element.ZoneKey = LineName(line) + "|"
                              + Math.Round(stretch.From / 10d) * 10 + "-"
                              + Math.Round(stretch.To / 10d) * 10;

            element.PairedWith = stretch.State == LineLoadState.NoEtabsFrame || result.Run == null
                ? "Nothing in ETABS runs under this part of the line."
                : "Read against ETABS " + (stretch.Frames.Length > 0 ? stretch.Frames : result.Frames) + ".";

            FileOnTab(element, sidlC);
            return element;
        }

        /// <summary>Which check a line load result reports, on the tab it is built for.</summary>
        private static CheckKind LineKind(bool inRevitOnly, bool sidlC)
        {
            if (sidlC)
            {
                return inRevitOnly ? CheckKind.SidlCMissingInEtabs : CheckKind.SidlC;
            }
            return inRevitOnly ? CheckKind.LineLoadMissingInEtabs : CheckKind.LineLoads;
        }

        /// <summary>What the review names on the SIDL_C tab start with.</summary>
        public const string SidlCKeyPrefix = "SIDLC|";

        /// <summary>
        /// Says which of the two line load tabs an element answers to, and on the SIDL_C one
        /// files it under a name of its own: a frame carrying both a SIDL and a SIDL_C load is
        /// one frame with two findings on it, and accepting one must not accept the other.
        /// Called last, once everything the review name is made of has been set.
        /// </summary>
        private static void FileOnTab(ElementViewModel element, bool sidlC)
        {
            element.Tab = sidlC ? ViewMode.SidlC : ViewMode.LineLoads;

            if (sidlC && element.ReviewKey.Length > 0)
            {
                element.ZoneKey = SidlCKeyPrefix + element.ReviewKey;
            }
        }

        /// <summary>
        /// What a drawn line is filed under when someone reviews it: its Revit id, which is
        /// what it always was - and its story too where its loading plan is shared by several.
        ///
        /// A shared plan puts the one Revit line on every story paired with it, and each of
        /// those is a finding of its own that can come out differently. Filed under the id
        /// alone, accepting the line on one floor would accept it on all of them, and the next
        /// floor reading differently would take the acceptance back. The story is added only
        /// there, so a line on a plan of its own keeps the name its reviews were filed under.
        /// </summary>
        private static string LineName(LineLoad_R line)
        {
            // A wall cut where its height changes is a line per length, all on the one id.
            return ElementKind.LineLoad + "|R" + line.RevitID
                   + (line.Part > 0 ? "#" + line.Part : "")
                   + (line.IsOnSeveralStories ? "@" + line.Story : "");
        }

        /// <summary>
        /// Files a whole drawn line under its story as well as its id when it is on several, and
        /// under its part when it is one length of a wall - see <see cref="LineName"/>. Left
        /// alone otherwise, filed by its Revit id as ever.
        /// </summary>
        private static void FileSharedLine(ElementViewModel element, LineLoad_R line)
        {
            if (line.IsOnSeveralStories || line.Part > 0)
            {
                element.ZoneKey = LineName(line);
            }
        }

        /// <summary>
        /// The drawn line between two distances along it, as the points to draw: where it
        /// starts, every corner of the line in between, and where it ends. So a stretch of a
        /// curve is drawn as the curve, not as the chord across it.
        /// </summary>
        private static IEnumerable<MyPoint> PointsAlong(LineLoad_R line, double from, double to)
        {
            List<MyPoint> points = new List<MyPoint>();
            double walked = 0;

            foreach (LineLoad_R.Piece piece in line.Pieces)
            {
                double length = piece.Length;
                double start = walked;
                double end = walked + length;
                walked = end;

                if (end < from || start > to || length <= 0)
                {
                    continue;
                }

                double t0 = Math.Max(0, (from - start) / length);
                double t1 = Math.Min(1, (to - start) / length);

                if (points.Count == 0)
                {
                    points.Add(Lerp(piece, t0));
                }
                points.Add(Lerp(piece, t1));
            }

            return points;
        }

        private static MyPoint Lerp(LineLoad_R.Piece piece, double t)
        {
            return new MyPoint(piece.PlanStart.X + (piece.PlanEnd.X - piece.PlanStart.X) * t,
                               piece.PlanStart.Y + (piece.PlanEnd.Y - piece.PlanStart.Y) * t,
                               piece.PlanStart.Z + (piece.PlanEnd.Z - piece.PlanStart.Z) * t);
        }

        #region too short a finding to be work

        /// <summary>
        /// True for a line load finding too short to be counted as work - see
        /// <see cref="PrefrencesData.LineLoadMinimumCountedLength"/>. Only a finding: a stretch
        /// that agrees is not work whatever its length, so there is nothing to leave out.
        ///
        /// A finding exactly on the length is counted, the same as a patch of slab exactly on
        /// its size: a length typed as the shortest thing worth chasing should chase it.
        /// </summary>
        private static bool TooShortToCount(double length, ResultLevel level)
        {
            double shortest = ShortestCounted;
            return shortest > 0 && length < shortest
                   && (level == ResultLevel.Failed || level == ResultLevel.Review);
        }

        /// <summary>The setting, in the mm everything along a line is measured in.</summary>
        private static double ShortestCounted
        {
            get { return Math.Max(0, PrefrencesData.getPrefrencesData().LineLoadMinimumCountedLength) * 1000d; }
        }

        /// <summary>
        /// How much of a line drawn whole is wrong, in mm: the part that differs where the check
        /// said where, the part with nothing under it on a line only partly carried, and
        /// otherwise the line. A line drawn whole is drawn in one colour, but a seven metre line
        /// that is out over thirty centimetres of it has a thirty centimetre finding.
        /// </summary>
        private static double FailingLength(LineLoadResult result)
        {
            switch (result.State)
            {
                case LineLoadState.ValueMismatch:
                    return result.MismatchLength > 0 ? result.MismatchLength : result.Length;
                case LineLoadState.PartlyCovered:
                    return Math.Max(0, result.Length - result.CoveredLength);
                default:
                    return result.Length;
            }
        }

        /// <summary>
        /// Said on the end of the verdict, so a line coloured on the plan and absent from every
        /// count explains the difference itself rather than looking like a miscount.
        /// </summary>
        private static string NotCountedAside(double length, bool tooShort)
        {
            return tooShort
                ? $" - not counted: {Metres(length)} is under the {Metres(ShortestCounted)} this check counts from"
                : "";
        }

        #endregion

        private static string StretchSummary(LineLoadResult result, LineLoadStretch stretch)
        {
            string number = result.Line.HasOwnValue || result.Line.HasNumber ? result.Line.LoadName : "this load";
            string asks = result.Line.HasOwnValue
                ? $"its weight is {LoadSet.Text(stretch.Wanted)} kN/m"
                : $"the plan asks for {LoadSet.Text(stretch.Wanted)} kN/m";

            switch (stretch.State)
            {
                case LineLoadState.Matched:
                    return $"{number} matches from {stretch.Where}: {LoadSet.Text(stretch.Carried)} kN/m";

                case LineLoadState.NoEtabsLoad:
                    return $"{number}, {stretch.Where}: {asks} and the ETABS frame under it carries nothing";

                case LineLoadState.NoEtabsFrame:
                    return $"{number}, {stretch.Where}: {asks} and no ETABS frame runs along this part of it";

                case LineLoadState.TakenByOther:
                    return $"{number}, {stretch.Where}: {asks}, but the ETABS load here "
                           + $"({LoadSet.Text(stretch.Carried)} kN/m) is what {stretch.TakenBy} asks for, "
                           + "so that line takes it - and no other frame along this part carries this one";

                default:
                    return $"{number}, {stretch.Where}: {asks} and ETABS carries "
                           + $"{LoadSet.Text(stretch.Carried)} kN/m";
            }
        }

        public static ElementViewModel FromLineLoad(LineLoadResult result)
        {
            return FromLineLoad(result, false);
        }

        public static ElementViewModel FromLineLoad(LineLoadResult result, bool sidlC)
        {
            LineLoad_R line = result.Line;

            List<ResultField> fields = new List<ResultField>
            {
                new ResultField(LineLoadResultKeys.RevitId, "Revit id", line.RevitID.ToString()),
                new ResultField(LineLoadResultKeys.Style, line.HasOwnValue ? "Revit element" : "Line style",
                                line.StyleName),
                new ResultField(LineLoadResultKeys.Number, "Load number",
                                line.HasNumber ? line.Number.ToString() : ""),
                new ResultField(LineLoadResultKeys.Description, "Schedule",
                                result.Entry == null ? "" : result.Entry.Summary),
                new ResultField(LineLoadResultKeys.Length, line.HasOwnValue ? "Length" : "Drawn length",
                                Metres(line.Length)),
                new ResultField(LineLoadResultKeys.Frames, "ETABS frames", result.Frames),
                new ResultField(LineLoadResultKeys.Expected, line.HasOwnValue ? "Its weight" : "Schedule asks for",
                                result.Expected > 0 ? LoadSet.Text(result.Expected) + " kN/m" : ""),
                // Each frame's own value, never averaged across them.
                new ResultField(LineLoadResultKeys.Actual, "ETABS carries", result.CarriedByFrameText),
                new ResultField(LineLoadResultKeys.Working, "Worked out as", result.How)
            };

            // Only on a line a zone drawn by hand gave its height to: the level's own height is
            // the ordinary case and needs no saying.
            if (result.HeightZone.Length > 0)
            {
                fields.Add(new ResultField(LineLoadResultKeys.HeightZone, "Height from", result.HeightZone));
            }

            // Only where there is something to say: a label with nothing after it reads as a
            // value that failed to come through.
            if (result.MismatchText.Length > 0)
            {
                fields.Add(new ResultField(LineLoadResultKeys.Mismatch, "Where it differs",
                                           result.MismatchText, true));
            }
            if (result.Alongside.Length > 0)
            {
                fields.Add(new ResultField(LineLoadResultKeys.Alongside, "Alongside it",
                                           result.Alongside, true));
            }

            // A line ETABS has no load for at all is not a disagreement about a number - one
            // model has a load here and the other has nothing - so it is reported as the same
            // kind of finding an unpaired beam is, and drawn in the same "Revit only" colour
            // rather than red. Red is kept for the two that really did differ: a load that is
            // there and wrong, and a load that is there along only part of the line.
            bool inRevitOnly = result.State == LineLoadState.NoEtabsFrame
                               || result.State == LineLoadState.NoEtabsLoad
                               || result.State == LineLoadState.TakenByOther;
            double failing = FailingLength(result);
            bool tooShort = TooShortToCount(failing, result.Level);

            ElementViewModel element = new LineLoadElementViewModel(
                inRevitOnly ? ElementSource.UnmatchedRevit : ElementSource.RevitOnly,
                inRevitOnly ? CheckState.Unpaired : StateOf(result.Level),
                line.ToString(), line.Story,
                fields, line.Pieces.Select(piece => piece.PlanStart)
                                   .Concat(line.Pieces.Count == 0
                                               ? new MyPoint[0]
                                               : new[] { line.Pieces[line.Pieces.Count - 1].PlanEnd }))
            {
                Result = result
            };

            element.Outcomes = new List<CheckOutcome>
            {
                new CheckOutcome(LineKind(inRevitOnly, sidlC),
                                 result.Level, result.Summary + NotCountedAside(failing, tooShort),
                                 etabs: result.CarriedText,
                                 revit: result.Expected > 0 ? LoadSet.Text(result.Expected) + " kN/m" : "")
            };

            CheckSwitches.RemoveSwitchedOff(element.Outcomes as List<CheckOutcome>);
            SetRevitId(element, line.RevitID);
            FileSharedLine(element, line);
            element.IsTooSmallToCount = tooShort;

            // Said by the element rather than worked out by the card, because what a line load
            // is read against is a run of frames rather than a single element.
            element.PairedWith = result.Run == null
                ? "Nothing in ETABS runs under this line."
                  + (result.Note.Length > 0 ? " " + Sentence(result.Note) : "")
                : "Read against ETABS " + result.Frames
                  + (result.Bundled
                      ? ", as a whole rather than stretch by stretch - the line does not run "
                        + "along the frames."
                      : ".");

            FileOnTab(element, sidlC);
            return element;
        }

        /// <summary>
        /// A stretch of ETABS frame carrying a load nothing on the loading plan asked for.
        ///
        /// The one thing on this tab that is not a drawn line, and it has to be: a line nobody
        /// drew leaves nothing on the drawing to colour, so the frame is put on the plan in its
        /// place. Drawn as unmatched ETABS, the same colour an ETABS element with no Revit
        /// counterpart is given everywhere else.
        /// </summary>
        public static ElementViewModel FromUnclaimedLineLoad(EtabsLineLoad unclaimed)
        {
            return FromUnclaimedLineLoad(unclaimed, false);
        }

        public static ElementViewModel FromUnclaimedLineLoad(EtabsLineLoad unclaimed, bool sidlC)
        {
            List<ResultField> fields = new List<ResultField>
            {
                new ResultField(LineLoadResultKeys.Frames, "ETABS frames",
                                unclaimed.Run == null ? "" : unclaimed.Run.Identifier),
                new ResultField(LineLoadResultKeys.Actual, "ETABS carries",
                                LoadSet.Text(unclaimed.PerMetre) + " kN/m"),
                new ResultField(LineLoadResultKeys.Length, "Over", Metres(unclaimed.Length)),
                new ResultField(LineLoadResultKeys.Force, "Which comes to",
                                LoadSet.Text(unclaimed.Force) + " kN")
            };

            if (unclaimed.Alongside.Length > 0)
            {
                fields.Add(new ResultField(LineLoadResultKeys.Alongside, "Alongside it",
                                           unclaimed.Alongside, true));
            }

            ElementViewModel element = new LineLoadElementViewModel(
                ElementSource.UnmatchedEtabs, CheckState.Unpaired,
                "ETABS line load on " + (unclaimed.Run == null ? "" : unclaimed.Run.Identifier),
                unclaimed.Story, fields, new[] { unclaimed.Start, unclaimed.End });

            bool tooShort = TooShortToCount(unclaimed.Length, ResultLevel.Failed);

            element.Outcomes = new List<CheckOutcome>
            {
                sidlC
                    ? new CheckOutcome(CheckKind.SidlCMissingInRevit, ResultLevel.Failed,
                                       unclaimed.SidlCSummary + NotCountedAside(unclaimed.Length, tooShort),
                                       etabs: LoadSet.Text(unclaimed.PerMetre) + " kN/m", revit: "no wall")
                    : new CheckOutcome(CheckKind.LineLoadMissingInRevit, ResultLevel.Failed,
                                       unclaimed.Summary + NotCountedAside(unclaimed.Length, tooShort),
                                       etabs: LoadSet.Text(unclaimed.PerMetre) + " kN/m", revit: "nothing drawn")
            };

            CheckSwitches.RemoveSwitchedOff(element.Outcomes as List<CheckOutcome>);
            element.IsTooSmallToCount = tooShort;

            element.PairedWith = sidlC
                ? "No wall or fill strip in Revit stands along this load."
                : "Nothing on the loading plan asks for this load.";

            // The frame this stretch lies on. It is what a review is filed under and what a
            // pairing set by hand is written against, and both need a name that comes back next
            // run - see EtabsLineLoad.FrameName.
            element.EtabsUniqueName = unclaimed.Key;

            FileOnTab(element, sidlC);
            return element;
        }

        /// <summary>
        /// One run of ETABS frames, drawn under the lines that claimed it.
        ///
        /// Carries no verdict of its own - the drawn line over it already says what the check
        /// found - so it goes on the plan grey and counts towards nothing. What it is there for
        /// is to be seen and to be clicked: without it a line that paired with the wrong run
        /// could be looked at and not corrected, because the run it should have gone to had
        /// nothing on the plan to point at.
        ///
        /// Named by one of its frames, which is what a pairing set on it is written against.
        /// </summary>
        public static ElementViewModel FromLineLoadRun(LineLoadRun run)
        {
            return FromLineLoadRun(run, false);
        }

        public static ElementViewModel FromLineLoadRun(LineLoadRun run, bool sidlC)
        {
            bool oneFrame = run.Frames.Count == 1;

            List<ResultField> fields = new List<ResultField>
            {
                new ResultField(LineLoadResultKeys.Frames, "ETABS frames", run.Identifier),
                new ResultField(LineLoadResultKeys.Length, oneFrame ? "Frame length" : "Run length",
                                Metres(run.Length))
            };

            if (run.Carries.Length > 0)
            {
                fields.Add(new ResultField(LineLoadResultKeys.Actual, "ETABS carries", run.Carries));
            }

            ElementViewModel element = new LineLoadElementViewModel(
                ElementSource.EtabsOnly, CheckState.NotChecked,
                (oneFrame ? "ETABS frame " : "ETABS run ") + run.Identifier, run.Story,
                fields, new[] { run.PlanStart, run.PlanEnd });

            // No outcomes at all, which is what keeps it off the counts and out of the drawer.
            element.Outcomes = new List<CheckOutcome>();
            element.EtabsUniqueName = run.FrameNames.FirstOrDefault() ?? "";
            element.PairedWith = sidlC
                ? "The walls and fill strips standing over it."
                : "The line loads drawn over it. Select a drawn line and this "
                  + "together - drag a window over both - to pair them by hand.";

            FileOnTab(element, sidlC);
            return element;
        }

        /// <summary>A note written as its own sentence, for putting after another one.</summary>
        private static string Sentence(string note)
        {
            if (note.Length == 0)
            {
                return "";
            }

            string first = char.ToUpperInvariant(note[0]) + note.Substring(1);
            return first.EndsWith(".") ? first : first + ".";
        }

        private static CheckState StateOf(ResultLevel level)
        {
            switch (level)
            {
                case ResultLevel.Passed: return CheckState.Passed;
                case ResultLevel.Failed: return CheckState.Failed;
                case ResultLevel.Review: return CheckState.Review;
                default: return CheckState.NotChecked;
            }
        }

        private static string Metres(double millimetres)
        {
            return System.Math.Round(millimetres / 1000d, 2).ToString("0.##") + " m";
        }

        #endregion

        #region SIDL_C area

        /// <summary>What the review names on the SIDL_C area tab start with.</summary>
        public const string SidlCAreaKeyPrefix = "SIDLCA|";

        /// <summary>
        /// One light weight fill floor, coloured by the SIDL_C ETABS carries under it. One
        /// element per separate piece of the fill, each saying the same thing: the fill is
        /// weighed whole, and its pieces only drawn apart.
        ///
        /// A fill with no SIDL_C, or no slab, under it is one model having something the other
        /// does not, and is drawn in the Revit only colour the way an unpaired beam is.
        /// </summary>
        public static IEnumerable<ElementViewModel> FromSidlCAreaResult(SidlCAreaResult result)
        {
            SidlCAreaSource_R fill = result.Source;
            bool inRevitOnly = result.IsMissingInEtabs;
            List<ResultField> fields = SidlCAreaFields(result);

            List<ElementViewModel> built = new List<ElementViewModel>();
            for (int i = 0; i < fill.Plan.Count; i++)
            {
                ElementViewModel element = new SlabElementViewModel(
                    inRevitOnly ? ElementSource.UnmatchedRevit : ElementSource.RevitOnly,
                    inRevitOnly ? CheckState.Unpaired : StateOf(result.Level),
                    fill.ToString(), fill.Story, fields, fill.Plan[i]);

                element.Outcomes = new List<CheckOutcome>
                {
                    new CheckOutcome(inRevitOnly ? CheckKind.SidlCAreaMissingInEtabs : CheckKind.SidlCArea,
                                     result.Level, result.Summary,
                                     etabs: result.IsMissingInEtabs || result.State == SidlCAreaState.Misplaced
                                         ? "nothing under it"
                                         : LoadSet.Text(result.Actual) + " kN/m²",
                                     revit: LoadSet.Text(result.Expected) + " kN/m²")
                };

                CheckSwitches.RemoveSwitchedOff(element.Outcomes as List<CheckOutcome>);
                SetRevitId(element, fill.RevitID);
                element.PairedWith = PairedWith(result);
                element.Tab = ViewMode.SidlCArea;
                element.ZoneKey = SidlCAreaKeyPrefix + fill.RevitID + (i == 0 ? "" : "|" + i);
                built.Add(element);
            }

            return built;
        }

        /// <summary>
        /// One piece of the SIDL_C area plan, cut along the edges of the fill and of the SIDL_C
        /// areas, coloured the way the slab load check colours its pieces: fill standing on
        /// SIDL_C in the fill's verdict, fill with none under it in the Revit only colour, and
        /// SIDL_C outside the fill in the ETABS only colour.
        /// </summary>
        public static ElementViewModel FromSidlCAreaPiece(SidlCAreaPiece piece)
        {
            SidlCAreaResult result = piece.Fill;
            SidlCAreaSource_R fill = result.Source;

            List<ResultField> fields = SidlCAreaFields(result);
            fields.Insert(0, new ResultField(SidlCAreaResultKeys.Here, "SIDL_C here",
                                             piece.Load != 0 ? LoadSet.Text(piece.Load) + " kN/m²" : "nothing",
                                             piece.Kind != SidlCAreaPieceKind.OnFill || piece.Level != ResultLevel.Passed));
            if (piece.Band != null)
            {
                fields.Insert(1, new ResultField(SidlCAreaResultKeys.WeighsHere, "Fill weighs here",
                                                 LoadSet.Text(piece.Band.Weighs) + " kN/m² ("
                                                 + System.Math.Round(piece.Band.Depth) + " mm deep on average)"));
            }
            fields.Insert(1, new ResultField(SidlCAreaResultKeys.PieceArea, "This piece",
                                             LoadSet.Text(piece.Plan.Area / 1e6) + " m²"
                                             + (piece.Kind == SidlCAreaPieceKind.OutsideFill ? ", outside the fill" : "")));

            ElementSource source;
            CheckState state;
            CheckKind kind;
            ResultLevel level;
            string etabs;
            string revit;
            switch (piece.Kind)
            {
                case SidlCAreaPieceKind.OnFill:
                    source = ElementSource.Paired;
                    level = piece.Level;
                    state = StateOf(level);
                    kind = CheckKind.SidlCArea;
                    etabs = LoadSet.Text(piece.Load) + " kN/m²";
                    revit = LoadSet.Text(piece.Band == null ? result.Expected : piece.Band.Weighs) + " kN/m²";
                    break;

                case SidlCAreaPieceKind.FillBare:
                    source = ElementSource.UnmatchedRevit;
                    state = CheckState.Unpaired;
                    kind = CheckKind.SidlCAreaMissingInEtabs;
                    level = ResultLevel.Failed;
                    etabs = "nothing";
                    revit = LoadSet.Text(result.Expected) + " kN/m²";
                    break;

                default:
                    source = ElementSource.UnmatchedEtabs;
                    state = CheckState.Unpaired;
                    kind = CheckKind.SidlCAreaMissingInRevit;
                    level = ResultLevel.Failed;
                    etabs = LoadSet.Text(piece.Load) + " kN/m²";
                    revit = "no fill";
                    break;
            }

            ElementViewModel element = new SlabElementViewModel(source, state, fill.ToString(), fill.Story,
                                                                fields, piece.Plan);
            element.Outcomes = new List<CheckOutcome>
            {
                new CheckOutcome(kind, level, piece.Summary, etabs: etabs, revit: revit)
            };

            CheckSwitches.RemoveSwitchedOff(element.Outcomes as List<CheckOutcome>);
            SetRevitId(element, fill.RevitID);
            if (piece.Kind == SidlCAreaPieceKind.OutsideFill && piece.Shells.Count > 0)
            {
                element.EtabsUniqueName = piece.Shells[0];
            }
            element.IsTooSmallToCount = piece.IsTooSmallToCount;
            element.PairedWith = PairedWith(result);
            element.Tab = ViewMode.SidlCArea;
            element.ZoneKey = SidlCAreaKeyPrefix + piece.Key;
            return element;
        }

        private static string PairedWith(SidlCAreaResult result)
        {
            return result.Shells.Count > 0
                ? "Read against the SIDL_C on ETABS " + string.Join(", ", result.Shells) + "."
                : result.Misplaced.Count > 0
                    ? "No ETABS area under this fill carries SIDL_C; " + string.Join(", ", result.Misplaced)
                      + " near it does, with no fill over it."
                    : "No ETABS area under this fill carries SIDL_C.";
        }

        /// <summary>What every element of one fill says about the fill as a whole.</summary>
        private static List<ResultField> SidlCAreaFields(SidlCAreaResult result)
        {
            SidlCAreaSource_R fill = result.Source;

            return new List<ResultField>
            {
                new ResultField(SidlCAreaResultKeys.RevitId, "Revit id", fill.RevitID.ToString()),
                new ResultField(SidlCAreaResultKeys.Type, "Revit floor", fill.Description),
                new ResultField(SidlCAreaResultKeys.Area, "Plan area",
                                LoadSet.Text(fill.Area / 1e6) + " m²"),
                new ResultField(SidlCAreaResultKeys.Depth, "Depth",
                                System.Math.Abs(fill.MaximumThickness - fill.MinimumThickness) > 1
                                    ? $"{System.Math.Round(fill.MinimumThickness)} to {System.Math.Round(fill.MaximumThickness)} mm, "
                                      + $"{System.Math.Round(fill.AverageThickness)} average"
                                    : $"{System.Math.Round(fill.AverageThickness)} mm"),
                new ResultField(SidlCAreaResultKeys.Expected, "Its weight",
                                LoadSet.Text(result.Expected) + " kN/m²"),
                new ResultField(SidlCAreaResultKeys.How, "Worked out as", result.How),
                new ResultField(SidlCAreaResultKeys.Actual, "ETABS carries, on average",
                                result.IsMissingInEtabs
                                    ? "nothing"
                                    : LoadSet.Text(result.Actual) + " kN/m²"
                                      + (result.AveragedOverWholeFill ? " over the whole fill" : ""),
                                result.Level == ResultLevel.Failed),
                new ResultField(SidlCAreaResultKeys.Bands, "Bands under it", result.BandText),
                new ResultField(SidlCAreaResultKeys.Total, "Total force R / E",
                                $"{System.Math.Round(result.RevitForce)} / {System.Math.Round(result.EtabsForce)} kN"
                                + (result.EtabsLoadedArea > 0
                                    ? $" (ETABS over {LoadSet.Text(result.EtabsLoadedArea / 1e6)} m²)"
                                    : ""),
                                result.State == SidlCAreaState.ValueMismatch),
                new ResultField(SidlCAreaResultKeys.Shells, "ETABS areas",
                                string.Join(", ", result.Shells.Concat(result.Misplaced)))
            };
        }

        /// <summary>An ETABS area carrying SIDL_C with no fill floor over it, in the ETABS only colour.</summary>
        public static ElementViewModel FromSidlCAreaUnclaimed(SidlCAreaUnclaimed unclaimed)
        {
            Slab_E shell = unclaimed.Shell;
            List<ResultField> fields = new List<ResultField>
            {
                new ResultField(SidlCAreaResultKeys.EtabsName, "ETABS area", shell.UniqueName),
                new ResultField(SidlCAreaResultKeys.Type, "ETABS property",
                                shell.PropertyName.Length > 0 ? shell.PropertyName : "None"),
                new ResultField(SidlCAreaResultKeys.Actual, "SIDL_C", LoadSet.Text(unclaimed.Load) + " kN/m²", true)
            };

            ElementViewModel element = new SlabElementViewModel(
                ElementSource.UnmatchedEtabs, CheckState.Unpaired,
                shell.UniqueName, shell.Story, fields, shell.Plan);

            element.Outcomes = new List<CheckOutcome>
            {
                new CheckOutcome(CheckKind.SidlCAreaMissingInRevit, ResultLevel.Failed, unclaimed.Summary,
                                 etabs: LoadSet.Text(unclaimed.Load) + " kN/m²", revit: "no fill")
            };

            CheckSwitches.RemoveSwitchedOff(element.Outcomes as List<CheckOutcome>);
            element.EtabsUniqueName = shell.UniqueName;
            element.PairedWith = "No light weight fill floor in Revit stands over this area.";
            element.Tab = ViewMode.SidlCArea;
            element.ZoneKey = SidlCAreaKeyPrefix + "E|" + shell.UniqueName;
            return element;
        }

        #endregion

        public static ElementViewModel FromSlabZone(SlabZone zone)
        {
            ElementViewModel element = new SlabElementViewModel(
                SourceOf(zone.State), StateOf(zone), zone.Identifier, zone.Story,
                zone.ResultFields, zone.Plan);

            element.Outcomes = zone.Outcomes;
            element.ZoneKey = zone.ReviewKey;
            element.IsTooSmallToCount = zone.IsTooSmallToCount;
            element.IsAutoAccepted = zone.IsAcceptedAsInsideSupport;
            element.IsAccepted = element.IsAutoAccepted;

            if (zone.Purpose == SlabMeshPurpose.Loads)
            {
                // A piece of the load cut answers to the loading plan, so that is what it is
                // paired with and what SELECT IN REVIT picks: the region that asked for the load.
                SetRevitId(element, zone.LoadRegionId);
                element.PairedWith = zone.LoadRegionId > 0
                    ? $"Loading region {zone.LoadRegionId}" +
                      (string.IsNullOrEmpty(zone.LoadNumber) ? "" : $", area load {zone.LoadNumber}") +
                      (zone.LoadDescription.Length > 0 ? $" ({zone.LoadDescription})" : "")
                    : "Nothing on the loading plan is drawn over this piece of slab.";
                return element;
            }

            SetRevitId(element, zone.RevitId);
            return element;
        }

        /// <summary>
        /// Which model a zone belongs to, which is what gives the ones only one model has
        /// their colour: the ETABS one where ETABS has slab and Revit has not, the Revit one
        /// the other way round, the same as an unpaired beam or column.
        /// </summary>
        private static ElementSource SourceOf(SlabZoneState state)
        {
            switch (state)
            {
                case SlabZoneState.MissingInRevit:
                case SlabZoneState.OpeningInRevitOnly:
                    return ElementSource.UnmatchedEtabs;
                case SlabZoneState.MissingInEtabs:
                case SlabZoneState.OpeningInEtabsOnly:
                    return ElementSource.UnmatchedRevit;
                default:
                    return ElementSource.Paired;
            }
        }

        /// <summary>
        /// The verdict on a piece of slab: what the slab check found, and - where that found
        /// nothing wrong, or never looked - whatever the piece's other results say.
        ///
        /// The zone's own state is about the slab itself: its thickness, its outline, its
        /// openings. A piece of the load cut is never asked those questions, so its state reads
        /// "not checked" however wrong its loads are, and everything that goes by the verdict
        /// took it at its word: ACCEPT stayed greyed out on a piece failing its SDL, because as
        /// far as the verdict knew there was nothing there to accept.
        ///
        /// Only raised, never lowered, and never over a piece only one model has: that says
        /// something no load result can take back, and an acceptance filed while a piece had no
        /// counterpart is told apart by it.
        /// </summary>
        private static CheckState StateOf(SlabZone zone)
        {
            CheckState state = StateOf(zone.State);
            if (state != CheckState.NotChecked && state != CheckState.Passed)
            {
                return state;
            }

            IEnumerable<CheckOutcome> said = zone.Outcomes.Where(outcome => outcome.Kind != CheckKind.None);
            if (said.Any(outcome => outcome.Level == ResultLevel.Failed))
            {
                return CheckState.Failed;
            }
            if (said.Any(outcome => outcome.Level == ResultLevel.Review))
            {
                return CheckState.Review;
            }
            if (state == CheckState.NotChecked && said.Any(outcome => outcome.Level == ResultLevel.Passed))
            {
                return CheckState.Passed;
            }
            return state;
        }

        private static CheckState StateOf(SlabZoneState state)
        {
            switch (state)
            {
                case SlabZoneState.Matched:
                    return CheckState.Passed;
                case SlabZoneState.ThicknessMismatch:
                    return CheckState.Failed;
                case SlabZoneState.ThicknessReview:
                case SlabZoneState.Unresolved:
                    return CheckState.Review;
                case SlabZoneState.NotChecked:
                    return CheckState.NotChecked;
                default:
                    return CheckState.Unpaired;
            }
        }

        /// <summary>An ETABS slab area on its own, before anything has been compared.</summary>
        public static ElementViewModel FromEtabsSlab(Slab_E slab, bool compared)
        {
            List<ResultField> fields = new List<ResultField>
            {
                new ResultField(SlabResultKeys.EtabsName, "ETABS area", slab.UniqueName),
                new ResultField(SlabResultKeys.EtabsProperty, "ETABS property", slab.PropertyName),
                new ResultField(SlabResultKeys.Thickness, "Thickness R / E",
                    $"- / {Math.Round(slab.ComparedThickness)}"),
                new ResultField(SlabResultKeys.State, "Slab check",
                    compared ? "No Revit floor" : "Not checked", compared)
            };

            ElementViewModel element = new SlabElementViewModel(
                compared ? ElementSource.UnmatchedEtabs : ElementSource.EtabsOnly,
                compared ? CheckState.Unpaired : CheckState.NotChecked,
                slab.UniqueName, slab.Story, fields, slab.Plan);

            element.Outcomes = PairingOutcome(compared, "no Revit floor");
            return element;
        }

        /// <summary>A Revit floor on its own, before anything has been compared.</summary>
        public static ElementViewModel FromRevitSlab(Slab_R slab, bool compared)
        {
            List<ResultField> fields = new List<ResultField>
            {
                new ResultField(SlabResultKeys.RevitId, "Revit id", slab.RevitID.ToString()),
                new ResultField(SlabResultKeys.RevitType, "Revit floor type", slab.TypeName),
                new ResultField(SlabResultKeys.Thickness, "Thickness R / E",
                    $"{Math.Round(slab.Thickness)} / -"),
                new ResultField(SlabResultKeys.State, "Slab check",
                    compared ? "No ETABS slab" : "Not checked", compared)
            };

            ElementViewModel element = new SlabElementViewModel(
                compared ? ElementSource.UnmatchedRevit : ElementSource.RevitOnly,
                compared ? CheckState.Unpaired : CheckState.NotChecked,
                slab.RevitID.ToString(), slab.Story, fields, slab.Plan);

            element.Outcomes = PairingOutcome(compared, "no ETABS slab");
            SetRevitId(element, slab.RevitID);
            return element;
        }

        /// <summary>
        /// The same element, marked as belonging to the other check: drawn grey behind what is
        /// being looked at, and out of the way of the picking and the labels.
        /// </summary>
        public static ElementViewModel AsContext(ElementViewModel element)
        {
            element.IsContext = true;
            return element;
        }

        #endregion

        private static void AddSectionFields(List<ResultField> fields, IColumn column)
        {
            if (column.Shape == SectionShape.Round)
            {
                fields.Add(ResultField.Number(ColumnResultKeys.Diameter, "Diameter", column.Diameter));
                return;
            }
            fields.Add(ResultField.Number(ColumnResultKeys.LongSide, "Long side",
                ColumnGeometry.LongSide(column)));
            fields.Add(ResultField.Number(ColumnResultKeys.ShortSide, "Short side",
                ColumnGeometry.ShortSide(column)));
        }

        private static void SetRevitId(ElementViewModel element, long revitId)
        {
            element.RevitId = revitId;
        }
    }
}
