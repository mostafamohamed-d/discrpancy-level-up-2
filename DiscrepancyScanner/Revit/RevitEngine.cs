using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DiscrepancyScanner
{
    public class RevitEngine
    {
        List<PlacedElement> framesElements_r;
        List<FrameData_r> framesData = new List<FrameData_r>();
        public string warnings = "";
        DocumentSet DocumentSet = null;
        ParameterData[] parameters;
        MyTransformation _transformation;
        // Empty: which beams to leave out is the "Leave out Revit beams" setting, not a list here.
        private static string[] ignoredNameList = { };
        ExternalCommandData commandData = null;
        List<Document> selectedDocuments = null;
        List<Column_r> columnsData = new List<Column_r>();
        private readonly List<string> importIssues = new List<string>();

        /// <summary>
        /// Everything that went wrong during the last import, collected so it can be shown
        /// once at the end instead of interrupting with a dialog per element.
        /// </summary>
        public IList<string> ImportIssues { get => importIssues; }

        /// <summary>
        /// The open document's title, for the header bar. The host document is named rather
        /// than any link read alongside it: the links are chosen per import and can be several,
        /// while the header has room to say which project is open.
        /// </summary>
        public string DocumentTitle
        {
            get
            {
                try
                {
                    return commandData?.Application?.ActiveUIDocument?.Document?.Title ?? "";
                }
                catch (Exception)
                {
                    // A document closed under us between the import and the header asking.
                    return "";
                }
            }
        }

        private void addIssue(string issue)
        {
            importIssues.Add(issue);
            warnings += "\n" + issue;
        }


        #region saying where the read has got to

        /// <summary>
        /// Called as the read works through the model, with a line saying what it is on and
        /// how far through that piece of work it is, nought to one.
        ///
        /// The read has to happen on the thread Revit hands the command, which is the thread
        /// every window in the process draws on, so while it runs nothing repaints - Revit
        /// included. On a building of any size that is minutes of a window that has stopped
        /// answering, which is indistinguishable from one that has crashed. Whoever sets this
        /// is the one that has to let the screen catch up; see the pump on the calling side.
        /// </summary>
        public Action<string, double> ReportProgress { get; set; }

        /// <summary>
        /// Asked, now and then, whether the user has asked for the read to stop. Left unset
        /// the read runs to the end, which is what it has always done.
        ///
        /// A read that stops is abandoned rather than kept: half a model is not a smaller
        /// model, it is a model with an arbitrary part of the building missing, and a check
        /// run against one would report everything missing as an element Revit has not got.
        /// So this throws, the caller catches, and whatever was imported before stands.
        /// </summary>
        public Func<bool> ShouldStop { get; set; }

        /// <summary>
        /// How often the screen is let through. Every element would cost more than the work
        /// being watched; a tenth of a second is beneath noticing and well above the rate a
        /// person can read a changing number at.
        /// </summary>
        private const long ReportIntervalMs = 120;

        private readonly System.Diagnostics.Stopwatch progressWatch =
            System.Diagnostics.Stopwatch.StartNew();
        private long lastReport;

        /// <summary>
        /// Says where the read has got to, and gives the caller its chance to stop it. Both
        /// are throttled together because both cost the same thing - a trip out to the screen
        /// - and there is no sense in one being answered more often than the other.
        ///
        /// The last element of a piece of work always reports, whatever the clock says, so
        /// the line does not sit at "3,900 of 4,000" while the next piece is starting.
        /// </summary>
        private void report(string stage, int done, int total)
        {
            bool isLast = done >= total;
            if (!isLast && progressWatch.ElapsedMilliseconds - lastReport < ReportIntervalMs)
            {
                return;
            }
            lastReport = progressWatch.ElapsedMilliseconds;

            if (ReportProgress != null)
            {
                ReportProgress(total > 0 ? $"{stage} - {done:n0} of {total:n0}" : stage,
                               total > 0 ? (double)done / total : 0);
            }

            if (ShouldStop != null && ShouldStop())
            {
                throw new ImportStoppedException();
            }
        }

        /// <summary>
        /// Every element of one kind across the documents being read, gathered before any of
        /// it is looked at, each with the placement it is being read in.
        ///
        /// Gathered first so the progress line can say how far through the read it is rather
        /// than how far through whichever linked file it happens to be in, which on a model
        /// with six links is six bars that each fill and start again. Nothing that reads these
        /// needs to know which document one came from - every element carries its own - so the
        /// flattening costs nothing but the list, and it is how the beams were already read.
        ///
        /// Gathered once per placement rather than once per document. A link placed three
        /// times - one typical floor copied up a tower, a core dropped into each wing - is three
        /// sets of elements standing in three places, and ETABS has all three modelled. Read
        /// once per document, only the first placement was ever there, and the other two came
        /// back as everything ETABS has that Revit has not.
        /// </summary>
        private List<PlacedElement> collectAcrossDocuments(Func<Document, IEnumerable<Element>> from)
        {
            List<PlacedElement> all = new List<PlacedElement>();

            List<ModelPlacement> selected = getSelectedPlacements();
            foreach (string note in placementNotes.Where(note => !importIssues.Contains(note)))
            {
                addIssue(note);
            }

            foreach (IGrouping<Document, ModelPlacement> placements in selected.GroupBy(placement => placement.Doc))
            {
                // Collected once however many times the document is placed: the elements are
                // the same elements, only where they stand differs.
                List<Element> elements = from(placements.Key).ToList();

                foreach (ModelPlacement placement in placements)
                {
                    foreach (Element element in elements)
                    {
                        PlacedElement placed = new PlacedElement(element, placement);
                        remember(placed);
                        all.Add(placed);
                    }
                }
            }

            return all;
        }

        #endregion

        public RevitEngine(ExternalCommandData commandData, MyTransformation transformation)
        {
            this.commandData = commandData;
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            UIApplication uIApplication = uiDoc.Application;
            Autodesk.Revit.ApplicationServices.Application app = uIApplication.Application;
            DocumentSet = app.Documents;
            _transformation = transformation;
            
        }

        public static string[] IgnoredNameList { get => ignoredNameList; set { UpdateIgnoreList(value); } }
        private static void UpdateIgnoreList(string[] value)
        {
            string mystring = "";
            for (int i = 0; i < value.Length; i++)
            {
                mystring += value[i].Trim(' ', '"');
            }
            mystring = mystring.ToLower();
            mystring = mystring.Replace(" ", "");
            char[] separator = { ',', '\n' };
            ignoredNameList = mystring.Split(separator, StringSplitOptions.RemoveEmptyEntries);
        }
        public enum paraType
        {
            INSTANCE,
            TYPE,
        }
        public struct ParameterData
        {
            public string Name;
            public String Value;
            public paraType type;
            public bool specialAddition;
        }
        private bool isIgnored(string Name)
        {
            Name = Name.ToLower();
            Name = Name.Replace(" ", "");
            for (int i = 0; i < ignoredNameList.Length; i++)
            {

                if (Name.Contains(ignoredNameList[i]))
                {
                    return true;
                }
            }
            return false;


        }
        public struct LinkInfo
        {
            public Document Doc;
            public bool IsNested;
            public bool IsOverlay;
            public bool IsAttached;
            public string Title => Doc?.Title ?? "Current Document";
        }
        public IList<FrameData_r> getFilterdFrames()
        {
            // Reset so the engine can be re-run after the model alignment changes, without
            // asking the user to choose the linked models again.
            framesData = new List<FrameData_r>();
            importIssues.Clear();
            warnings = "";
            refreshBeamSettings();

            // Before the sweep rather than after it. Asking Revit for every beam in the
            // model and its links is itself seconds of nothing happening, and a line saying
            // what is going on is the whole point of these.
            report("Finding the beams", 0, 0);
            getFramesFromRevit();
            storeFramesData();
            reportBeamCounts();

            return framesData;
        }

        private void storeFramesData()
        {
            int total = framesElements_r.Count;
            int done = 0;

            foreach (PlacedElement placed in framesElements_r)
            {
                report("Reading beams", ++done, total);

                Element frame = placed.Element;

                if (isIgnored(frame.Name))
                {
                    continue;
                }

                // The two "leave it out" lists, before any geometry is asked for: a beam that
                // is not being collected is not worth extracting a curve for. The type name
                // goes first because it is the cheaper of the two - the mark costs a walk of
                // the parameters on the type and then on the instance.
                string ignoredBy;
                if (isIgnoredByName(frame, ignoredBeamTypeNames, out ignoredBy))
                {
                    countIgnored(beamsIgnoredByTypeName, ignoredBy);
                    continue;
                }

                string beamMark = readBeamMark(frame);
                if (PrefrencesData.MatchesAnyPhrase(beamMark, ignoredBeamMarks, out ignoredBy))
                {
                    countIgnored(beamsIgnoredByMark, ignoredBy);
                    continue;
                }

                FrameData_r _frameData = getFrameLocationData(placed);
                if (_frameData != null)
                {
                    _frameData.RevitName = frame.Name;
                    _frameData.TypeName = getElementTypeName(frame);
                    _frameData.BeamMark = beamMark;
                    getParametersData(frame, ref _frameData);
                    if (_frameData.width_r != 0 && _frameData.Depth_r != 0)
                    {
                        framesData.Add(_frameData);

                    }
                }
                
            }
        }

        // Parameter names that carry a beam's reinforcement mark, taken from the preferences
        // so the tool can be pointed at whatever a given office's families use.
        private string[] beamMarkParameterNames = new string[0];

        // The names a beam is left out for, split once per import rather than once per beam.
        private string[] ignoredBeamTypeNames = new string[0];
        private string[] ignoredBeamMarks = new string[0];

        /// <summary>
        /// How many beams each name in the two lists took out, so what is reported is the
        /// names that actually did something and not the lists as they were typed.
        /// </summary>
        private readonly Dictionary<string, int> beamsIgnoredByTypeName =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> beamsIgnoredByMark =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private static void countIgnored(Dictionary<string, int> tally, string key)
        {
            int already;
            tally.TryGetValue(key ?? "", out already);
            tally[key ?? ""] = already + 1;
        }

        private void refreshBeamSettings()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            beamMarkParameterNames = PrefrencesData.SplitParameterNames(pref.BeamMarkParameterNames);

            // Phrases rather than words, for the same reason as the wall list: a name to leave
            // out may have a space in it, and splitting on spaces would make two of it.
            ignoredBeamTypeNames = PrefrencesData.SplitTypeNamePhrases(pref.BeamTypeNamesToIgnore);
            ignoredBeamMarks = PrefrencesData.SplitTypeNamePhrases(pref.BeamMarksToIgnore);

            beamsIgnoredByTypeName.Clear();
            beamsIgnoredByMark.Clear();
            beamSizes.Clear();
        }

        /// <summary>
        /// What the beam import left out on purpose. Named as well as counted, because these
        /// are the filters the model can be made to fail: "RB" is two letters matched anywhere
        /// in a name, so it takes a KERB or a CURB type with it, and which name did it is the
        /// whole of the diagnosis.
        /// </summary>
        private void reportBeamCounts()
        {
            reportIgnoredBeams(beamsIgnoredByTypeName, "type is named");
            reportIgnoredBeams(beamsIgnoredByMark, "reinforcement mark reads");
            reportMeasuredSizes("beam", beamSizes);
        }

        #region sizes measured off the solid

        /// <summary>What the measuring did over one import, for the report.</summary>
        private sealed class SizeTally
        {
            /// <summary>Members with no size parameter, sized off their solid instead.</summary>
            public int Measured;
            /// <summary>Members with both, compared.</summary>
            public int Compared;
            /// <summary>Of those, the ones whose parameters are the solid's the other way round.</summary>
            public int Swapped;
            public readonly List<string> SwappedExamples = new List<string>();
            public readonly List<string> Disagreeing = new List<string>();

            /// <summary>How many of each dimension came from where - "width from Type 'b'".</summary>
            public readonly Dictionary<string, int> Sources = new Dictionary<string, int>(StringComparer.Ordinal);

            /// <summary>Mapped parameters found but refused - text, or not a length.</summary>
            public readonly Dictionary<string, int> Rejected = new Dictionary<string, int>(StringComparer.Ordinal);

            public void Count(string property, string source)
            {
                countIgnored(Sources, $"{property} from {source ?? SizeSources.NotFound}");
            }

            public void Clear()
            {
                Measured = Compared = Swapped = 0;
                SwappedExamples.Clear();
                Disagreeing.Clear();
                Sources.Clear();
                Rejected.Clear();
            }
        }

        private readonly SizeTally beamSizes = new SizeTally();
        private readonly SizeTally columnSizes = new SizeTally();

        /// <summary>A parameter and the solid are taken to agree within this, in feet.</summary>
        private const double SizeAgreementFeet = 5 / 304.8;

        private static string Mm(double feet)
        {
            return Math.Round(feet * 304.8).ToString();
        }

        private static void compareSizes(ElementId id, double width, double depth,
                                         double measuredWidth, double measuredDepth, SizeTally tally)
        {
            tally.Compared++;
            if (Math.Abs(width - measuredWidth) <= SizeAgreementFeet && Math.Abs(depth - measuredDepth) <= SizeAgreementFeet)
            {
                return;
            }
            if (Math.Abs(width - measuredDepth) <= SizeAgreementFeet && Math.Abs(depth - measuredWidth) <= SizeAgreementFeet)
            {
                tally.Swapped++;
                if (tally.SwappedExamples.Count < 15)
                {
                    tally.SwappedExamples.Add($"{id} ({Mm(width)}x{Mm(depth)} by parameter, {Mm(measuredWidth)}x{Mm(measuredDepth)} measured)");
                }
                return;
            }
            tally.Disagreeing.Add($"{id} ({Mm(width)}x{Mm(depth)} by parameter, {Mm(measuredWidth)}x{Mm(measuredDepth)} measured)");
        }

        private void reportMeasuredSizes(string kind, SizeTally tally)
        {
            if (tally.Sources.Count > 0)
            {
                addIssue($"Revit {kind} sizes read from: "
                         + string.Join("; ", tally.Sources.OrderBy(entry => entry.Key, StringComparer.Ordinal)
                                                          .Select(entry => $"{entry.Key} {entry.Value}")) + ".");
            }

            if (tally.Rejected.Count > 0)
            {
                addIssue($"Mapped {kind} parameters found but not read, because only a Length parameter is: "
                         + string.Join("; ", tally.Rejected.OrderByDescending(entry => entry.Value)
                                                           .Select(entry => $"{entry.Key} ({entry.Value})"))
                         + ". Fix the mapping under Settings > Revit parameters.");
            }

            if (tally.Measured > 0)
            {
                addIssue($"{tally.Measured} Revit {kind}(s) carry none of the size parameter names under Settings > "
                         + "Revit parameters, so their width and depth were measured off their solid.");
            }

            if (tally.Compared > 0)
            {
                int agree = tally.Compared - tally.Swapped - tally.Disagreeing.Count;
                string line = $"Size parameters agree with the solid within 5 mm on {agree} of {tally.Compared} Revit {kind}(s)";
                if (tally.Swapped > 0)
                {
                    line += $"; {tally.Swapped} agree with width and depth the other way round, and their parameters "
                            + "were used: " + string.Join("; ", tally.SwappedExamples)
                            + (tally.Swapped > tally.SwappedExamples.Count ? $"; and {tally.Swapped - tally.SwappedExamples.Count} more" : "");
                }
                if (tally.Disagreeing.Count > 0)
                {
                    line += $"; {tally.Disagreeing.Count} disagree, and their parameters were used: "
                            + string.Join("; ", tally.Disagreeing.Take(15))
                            + (tally.Disagreeing.Count > 15 ? $"; and {tally.Disagreeing.Count - 15} more" : "");
                }
                addIssue(line + ".");
            }
        }

        /// <summary>
        /// A beam's width and depth off its uncut solid, in feet: flat across it and up
        /// through it for a straight beam, out from the centre and up for one curved in plan.
        /// False for any other curve, and for anything with no solid to measure.
        /// </summary>
        private static bool measureBeam(Element frame, out double width, out double depth)
        {
            width = 0;
            depth = 0;

            FamilyInstance instance = frame as FamilyInstance;
            Curve curve = (frame.Location as LocationCurve)?.Curve;
            if (instance == null || curve == null)
            {
                return false;
            }

            bool cylinder;
            Arc arc = curve as Arc;
            if (arc != null)
            {
                // Curved in plan only: an arc that rises measures its depth on the slope.
                if (Math.Abs(Math.Abs(arc.Normal.Z) - 1) > 1e-3)
                {
                    return false;
                }
                SectionExtents.MeasureAroundCentre(sectionPoints(instance, out cylinder), ToArray(arc.Center),
                                                   out width, out depth);
                return width > 0 && depth > 0;
            }

            Line line = curve as Line;
            if (line == null)
            {
                return false;
            }

            XYZ along = line.GetEndPoint(1) - line.GetEndPoint(0);
            double[] across, up;
            if (!SectionExtents.BeamAxes(new[] { along.X, along.Y, along.Z }, out across, out up))
            {
                return false;
            }

            SectionExtents.Measure(sectionPoints(instance, out cylinder), across, up, out width, out depth);
            return width > 0 && depth > 0;
        }

        /// <summary>
        /// A column's plan size off its uncut solid, along the family's own two axes - the
        /// axes its Width and Depth parameters are drawn along - in feet. Round when the
        /// solid has a curved side and the two come out the same.
        /// </summary>
        private static bool measureColumn(FamilyInstance instance, out double width, out double depth, out bool round)
        {
            width = 0;
            depth = 0;

            Transform transform = instance.GetTransform();
            List<double[]> points = sectionPoints(instance, out round);
            SectionExtents.Measure(points, ToArray(transform.BasisX), ToArray(transform.BasisY), out width, out depth);

            round = round && width > 0 && Math.Abs(width - depth) <= 0.02 * Math.Max(width, depth);
            return width > 0 && depth > 0;
        }

        private static double[] ToArray(XYZ v)
        {
            return new[] { v.X, v.Y, v.Z };
        }

        /// <summary>
        /// Every point on the edges of the member's solid, uncut: before the joins with slabs
        /// and walls and the cut-backs at its ends, which take depth off a beam a slab is
        /// joined into. In the element's own document coordinates.
        /// </summary>
        private static List<double[]> sectionPoints(FamilyInstance instance, out bool hasCylinder)
        {
            hasCylinder = false;

            Options options = new Options
            {
                DetailLevel = ViewDetailLevel.Fine,
                ComputeReferences = false,
                IncludeNonVisibleObjects = false
            };

            GeometryElement original = null;
            try
            {
                original = instance.GetOriginalGeometry(options);
            }
            catch (Exception)
            {
                original = null;
            }

            List<XYZ> points = new List<XYZ>();
            if (original != null)
            {
                collectEdgePoints(original, points, ref hasCylinder);
            }

            // Some families give back no solid uncut. The solid as it stands is the next best:
            // its width is the same, and only a slab joined into its top can take any depth off.
            if (points.Count == 0)
            {
                original = null;
                GeometryElement placed = instance.get_Geometry(options);
                if (placed != null)
                {
                    collectEdgePoints(placed, points, ref hasCylinder);
                }
            }
            if (points.Count == 0)
            {
                return new List<double[]>();
            }

            // Which frame the uncut solid comes back in is not taken on trust: it is put
            // wherever lies nearer the element itself, in its own family frame or placed.
            BoundingBoxXYZ box = original == null ? null : instance.get_BoundingBox(null);
            if (box != null)
            {
                XYZ centre = (box.Min + box.Max) / 2;
                XYZ mean = new XYZ(points.Average(p => p.X), points.Average(p => p.Y), points.Average(p => p.Z));
                Transform transform = instance.GetTransform();
                if (transform.OfPoint(mean).DistanceTo(centre) < mean.DistanceTo(centre))
                {
                    points = points.Select(transform.OfPoint).ToList();
                }
            }

            return points.Select(p => new[] { p.X, p.Y, p.Z }).ToList();
        }

        private static void collectEdgePoints(GeometryElement geometry, List<XYZ> points, ref bool hasCylinder)
        {
            foreach (GeometryObject item in geometry)
            {
                Solid solid = item as Solid;
                if (solid != null && solid.Volume > 1e-9)
                {
                    foreach (Edge edge in solid.Edges)
                    {
                        points.AddRange(edge.Tessellate());
                    }
                    foreach (Face face in solid.Faces)
                    {
                        if (face is CylindricalFace)
                        {
                            hasCylinder = true;
                        }
                    }
                    continue;
                }

                GeometryInstance nested = item as GeometryInstance;
                if (nested != null)
                {
                    collectEdgePoints(nested.GetInstanceGeometry(), points, ref hasCylinder);
                }
            }
        }

        #endregion

        private void reportIgnoredBeams(Dictionary<string, int> tally, string because)
        {
            if (tally.Count == 0)
            {
                return;
            }

            int ignored = tally.Values.Sum();
            string byName = string.Join(", ", tally.OrderByDescending(entry => entry.Value)
                                                   .Select(entry => $"{entry.Key} {entry.Value}"));

            addIssue($"{ignored} Revit beam(s) were left out because their {because} as something to "
                     + $"leave out: {byName}. The names are under Settings > Beam pairing.");
        }

        /// <summary>
        /// The reinforcement mark a beam carries. A beam with none is left blank rather than
        /// reported: the mark is shown for information, so a missing one is not an import
        /// problem.
        /// </summary>
        private string readBeamMark(Element frame)
        {
            ElementType type = frame.Document.GetElement(frame.GetTypeId()) as ElementType;
            return findTextParameter(type, frame, beamMarkParameterNames);
        }

        private void getParametersData(Element frame, ref FrameData_r frameData)
        {
            updateParametersList();
            optainParameters(frame, ref frameData);



        }

        private int optainParameters(Element frame, ref FrameData_r frameData)
        {
            Document doc = frame.Document;

            ElementType type = doc.GetElement(frame.GetTypeId()) as ElementType;

            // Width and depth by the parameters the user mapped to them, on the type, the
            // instance, or either - see ParameterMapping. Nothing is guessed.
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            List<MappedParameter>[] mapped =
            {
                ParameterMapping.Parse(pref.BeamWidthParameterNames),
                ParameterMapping.Parse(pref.BeamDepthParameterNames)
            };
            string[] property = { "width", "depth" };

            double[] values = new double[parameters.Length];
            string[] sources = new string[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                values[i] = readMappedLength(type, frame, mapped[i], out sources[i], beamSizes.Rejected);
            }

            // Whatever the parameters are called, the solid has a size. Measured, it fills in
            // what the mapping did not find; beside a parameter, it checks it.
            double width, depth;
            if (pref.MeasureSizeFromGeometry && measureBeam(frame, out width, out depth))
            {
                if (values[0] > 0 && values[1] > 0)
                {
                    compareSizes(frame.Id, values[0], values[1], width, depth, beamSizes);
                }
                else
                {
                    double[] measured = { width, depth };
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        if (values[i] <= 0)
                        {
                            values[i] = measured[i];
                            sources[i] = SizeSources.Measured;
                        }
                    }
                    beamSizes.Measured++;
                }
            }

            for (int i = 0; i < parameters.Length; i++)
            {
                parameters[i].Value = "";
                if (values[i] <= 0)
                {
                    string tried = mapped[i].Count == 0
                        ? "no parameter is mapped to it"
                        : "tried " + string.Join(", ", mapped[i]);
                    addIssue($"{frame.Id}: cannot read the beam {property[i]} ({tried}; see Settings > Revit parameters)"
                             + (pref.MeasureSizeFromGeometry ? " and its solid could not be measured" : ""));
                    beamSizes.Count(property[i], SizeSources.NotFound);
                    return 0;
                }
                parameters[i].Value = values[i].ToString();
            }

            for (int i = 0; i < parameters.Length; i++)
            {
                beamSizes.Count(property[i], sources[i]);
            }
            frameData.SizeSource = $"Width: {sources[0]}; Depth: {sources[1]}";

            frameData.handleParameters(parameters);
            return 1;
        }

        public static string GetOneParameterValueAsString(Parameter para)
        {
            string value = "";
            try
            {


                switch (para.StorageType)
                {
                    case StorageType.None:
                        value = "";
                        break;
                    case StorageType.Integer:
                        value = para.AsInteger().ToString();
                        break;
                    case StorageType.Double:
                        value = para.AsDouble().ToString();
                        break;
                    case StorageType.String:
                        value = para.AsString();
                        break;
                    case StorageType.ElementId:
                        value = para.AsElementId().ToString();
                        break;
                    default:
                        value = "";
                        break;
                }
            }
            catch
            {
                value = "";
            }
            return value;
        }

        private Parameter GetOneParameter(ElementType elementTypeInstance, Element elementInstance, string name, paraType type)
        {
            Parameter prm = null;
            try
            {
                switch (type)
                {
                    case paraType.INSTANCE:
                        prm = elementInstance.LookupParameter(name);
                        break;
                    case paraType.TYPE:
                        prm = elementTypeInstance.LookupParameter(name);
                        break;
                    default:
                        prm = null;
                        break;
                }

            }
            catch
            {
                addIssue($"{elementInstance.Id}: cannot find parameter '{name}'");
            }
            if (prm == null)
            {
                addIssue($"{elementInstance.Id}: cannot find parameter '{name}'");
            }

            return prm;
        }

        private void updateParametersList()
        {
            // TODO: make a gui option to optain parameters names
            
            string[] paraNames = {"Width" , "Depth"};
            //string[] paraNames = {"Width" , "Depth" , "BEAM MARK" , "BOTTOM RFT_LEFT" , "BOTTOM RFT_RIGHT" , "BOTTOM RFT_LEFT" ,
            //                       "TOP RFT_LEFT" , "TOP RFT_MIDSPAN" , "TOP RFT_RIGHT" , "LINKS_LEFT" , "LINKS_SPAN" , "LINKS_RIGHT"};

            paraType[] types = { paraType.TYPE, paraType.TYPE };
            //paraType[] types = {paraType.TYPE , paraType.TYPE , paraType.INSTANCE, paraType.INSTANCE, paraType.INSTANCE, paraType.INSTANCE,
            //                    paraType.INSTANCE , paraType.INSTANCE , paraType.INSTANCE , paraType.INSTANCE , paraType.INSTANCE , paraType.INSTANCE};
            int n = paraNames.Length;
            parameters = new ParameterData[n];

            for (int i = 0; i < n; i++)
            {
                parameters[i].Name = paraNames[i];
                parameters[i].type = types[i];
                parameters[i].Value = "";
                parameters[i].specialAddition = false;
            }

           // parameters[4].specialAddition = true;
        }

        private FrameData_r getFrameLocationData(PlacedElement placed)
        {
            Element frame = placed.Element;
            try
            {
                FamilyInstance fmInstance = frame as FamilyInstance;
                Family fm = fmInstance.Symbol.Family;
                Transform documnetTransrom = placed.Placement.Transform;

                if (fm.IsInPlace)
                {
                    LocationPoint lpOrigonal = frame.Location as LocationPoint;
                    if (lpOrigonal == null)
                    {
                        addIssue($"{frame.Id}: in-place family has no location point, skipped");
                        return null;
                    }

                    XYZ lp = documnetTransrom != null && documnetTransrom.IsConformal
                        ? documnetTransrom.OfPoint(lpOrigonal.Point)
                        : lpOrigonal.Point;

                    MyPoint pt = myPointFromXYZ(lp);
                    double theta = 0;
                    MyPoint a = new MyPoint(Math.Cos(theta), Math.Sin(theta), 0);
                    MyPoint direction = a;
                    FrameData_r frm = new FrameData_r(placed.RevitId, pt, pt, 1, direction);
                    return frm;
                }
                else
                {
                    LocationCurve locationCurve = frame.Location as LocationCurve;
                    if (locationCurve?.Curve == null)
                    {
                        addIssue($"{frame.Id}: no location curve, skipped");
                        return null;
                    }

                    Curve curve = locationCurve.Curve;
                    if (documnetTransrom != null && documnetTransrom.IsConformal)
                    {
                        curve = curve.CreateTransformed(documnetTransrom);
                    }

                    // A beam may be drawn as an arc or a spline. ETABS frames are straight, so
                    // the curve is compared by its chord and reported, or left out by setting.
                    if (!(curve is Line) && !PrefrencesData.getPrefrencesData().CompareCurvedBeamsByChord)
                    {
                        addIssue($"{frame.Id}: '{frame.Name}' is a {curve.GetType().Name}, left out " +
                                 "(Settings > Beam pairing > Compare curved beams by their chord)");
                        return null;
                    }
                    if (!(curve is Line))
                    {
                        addIssue($"{frame.Id}: '{frame.Name}' is a {curve.GetType().Name}, " +
                                 "compared using its end-to-end chord");
                    }

                    XYZ start = curve.GetEndPoint(0);
                    XYZ end = curve.GetEndPoint(1);
                    XYZ chord = end - start;
                    if (chord.GetLength() < 1e-9)
                    {
                        addIssue($"{frame.Id}: zero length curve, skipped");
                        return null;
                    }

                    MyPoint pt1 = myPointFromXYZ(start);
                    MyPoint pt2 = myPointFromXYZ(end);
                    MyPoint dir = myPointFromXYZ(chord.Normalize());
                    FrameData_r frm = new FrameData_r(placed.RevitId, pt1, pt2, chord.GetLength(), dir);
                    return frm;
                }
            }
            catch (Exception e)
            {
                addIssue($"{frame.Id}: cannot obtain location data - {e.Message}");
                return null;
            }
        }

        #region placements

        /// <summary>
        /// One place a document's elements stand in the host: the host itself, or one
        /// instance of a link.
        /// </summary>
        private sealed class ModelPlacement
        {
            public ModelPlacement(Document doc, Transform transform, long idOffset, ElementId linkInstanceId)
            {
                Doc = doc;
                Transform = transform;
                IdOffset = idOffset;
                LinkInstanceId = linkInstanceId;
            }

            public Document Doc { get; }

            /// <summary>
            /// The link instance in the host this placement is, or null for the host itself -
            /// what a linked element has to be selected through. InvalidElementId for a nested
            /// link, which has no instance in the host to be selected through.
            /// </summary>
            public ElementId LinkInstanceId { get; }

            /// <summary>From the document into the host, or null for the host itself.</summary>
            public Transform Transform { get; }

            /// <summary>
            /// Added to every element id read in this placement. Nought for the host and for
            /// the first instance of each link, so the ids of a link placed once are the ids
            /// Revit shows, as they always were, and saved pairings still find them.
            /// </summary>
            public long IdOffset { get; }
        }

        /// <summary>
        /// An element as it stands in one placement. The same wall in a link placed twice is
        /// two of these, and to the checks two walls.
        /// </summary>
        private struct PlacedElement
        {
            public PlacedElement(Element element, ModelPlacement placement)
            {
                Element = element;
                Placement = placement;
            }

            public Element Element { get; }
            public ModelPlacement Placement { get; }

            /// <summary>
            /// The id the rest of the tool knows the element by. Everything downstream - the
            /// pairings, the plan, the saved overrides - keys on it, so the second placement of
            /// an element cannot carry the same id as the first without the two being taken
            /// for one element.
            /// </summary>
            public long RevitId => Element.Id.Value + Placement.IdOffset;
        }

        /// <summary>
        /// Far above any element id Revit hands out, so an offset element id reads as the
        /// original with the placement's number in front of it: 2000000123456 is element
        /// 123456 in the second extra placement.
        /// </summary>
        private const long PlacementIdStep = 1000000000000L;

        private List<ModelPlacement> selectedPlacements = null;

        /// <summary>
        /// Said with every import rather than only the one that asked for the links, since the
        /// placements are worked out once and the issues are cleared between imports.
        /// </summary>
        private readonly List<string> placementNotes = new List<string>();

        /// <summary>
        /// Every placement of the documents the user picked. The host is placed once, where
        /// it is. A link is placed once per loaded instance of it in the host, each with its
        /// own transform - not once in total at wherever its first instance happened to be.
        /// </summary>
        private List<ModelPlacement> getSelectedPlacements()
        {
            if (selectedPlacements != null)
            {
                return selectedPlacements;
            }

            selectedPlacements = new List<ModelPlacement>();

            List<Document> documents = getSelectedDocuments();
            if (documents.Count == 0)
            {
                return selectedPlacements;
            }

            Document host = commandData.Application.ActiveUIDocument.Document;
            List<LinkPlacement> links = findLinkPlacements(host);

            long extraPlacements = 0;

            foreach (Document doc in documents.Distinct())
            {
                if (!doc.IsLinked)
                {
                    selectedPlacements.Add(new ModelPlacement(doc, null, 0, null));
                    continue;
                }

                // Matched by title, as the link was before: a linked document is loaded once
                // however many instances of it there are, and they all hand back that one.
                List<LinkPlacement> ofThisDocument = links
                    .Where(link => link.Doc.Title == doc.Title)
                    .ToList();

                if (ofThisDocument.Count == 0)
                {
                    // Nothing to place it by. Read where it was drawn, which is what a link
                    // with no instance found for it has always been read as.
                    selectedPlacements.Add(new ModelPlacement(doc, null, 0, null));
                    continue;
                }

                for (int i = 0; i < ofThisDocument.Count; i++)
                {
                    LinkPlacement link = ofThisDocument[i];
                    long offset = i == 0 ? 0 : ++extraPlacements * PlacementIdStep;

                    selectedPlacements.Add(new ModelPlacement(doc, link.Transform, offset, link.HostInstanceId));
                }

                if (ofThisDocument.Count > 1)
                {
                    placementNotes.Add($"'{doc.Title}' is placed {ofThisDocument.Count} times in this model, "
                                       + "and every placement is read. Elements in placements after the first "
                                       + $"carry their Revit id plus a multiple of {PlacementIdStep:n0}, so "
                                       + "each one can be told apart.");
                }
            }

            return selectedPlacements;
        }

        /// <summary>
        /// One place a linked document is seen in the host: an instance of it in the host, or
        /// one inside a link that is itself placed - a nested link.
        /// </summary>
        private sealed class LinkPlacement
        {
            public Document Doc { get; set; }

            /// <summary>From the document into the host, through every link it is nested in.</summary>
            public Transform Transform { get; set; }

            /// <summary>
            /// The instance in the host, or InvalidElementId for a nested link, whose instance
            /// is in the link it is nested in and means nothing to the host.
            /// </summary>
            public ElementId HostInstanceId { get; set; }

            public bool IsNested { get; set; }
            public bool IsOverlay { get; set; }
            public bool IsAttached { get; set; }
        }

        /// <summary>
        /// Deeper than any real model nests its links, and a stop for a model that links back
        /// to one it is linked into.
        /// </summary>
        private const int MaxLinkDepth = 8;

        /// <summary>
        /// Every loaded link placement the host shows, nested ones included.
        ///
        /// A nested link is placed inside the link it is nested in, so it stands in the host
        /// at that link's placement and then its own within it. It used to be looked for among
        /// the instances in the host alone, and was read either where it was drawn or where it
        /// sits inside its parent - right only when the parent sits at the origin - and once
        /// however many times the parent is placed. It is now found by going into each link,
        /// so it lands where the host shows it and once per placement of its parent.
        /// </summary>
        private static List<LinkPlacement> findLinkPlacements(Document host)
        {
            List<LinkPlacement> found = new List<LinkPlacement>();
            addLinkPlacements(host, null, found, new List<string> { host.Title });
            return found;
        }

        private static void addLinkPlacements(Document doc, Transform toHost, List<LinkPlacement> found,
                                              List<string> chain)
        {
            bool nested = toHost != null;

            IEnumerable<RevitLinkInstance> instances = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>();

            foreach (RevitLinkInstance instance in instances)
            {
                Document linkDoc = instance.GetLinkDocument();
                RevitLinkType type = doc.GetElement(instance.GetTypeId()) as RevitLinkType;
                if (linkDoc == null || type == null)
                {
                    continue;
                }

                if (!nested)
                {
                    // A nested link reached from the host is reached again, properly placed,
                    // by going into the link it is nested in below.
                    if (type.IsNestedLink || type.GetLinkedFileStatus() != LinkedFileStatus.Loaded)
                    {
                        continue;
                    }
                }

                if (chain.Contains(linkDoc.Title) || chain.Count > MaxLinkDepth)
                {
                    continue;
                }

                Transform own = instance.GetTransform();
                Transform total = nested ? toHost.Multiply(own) : own;

                found.Add(new LinkPlacement
                {
                    Doc = linkDoc,
                    Transform = total,
                    HostInstanceId = nested ? ElementId.InvalidElementId : instance.Id,
                    IsNested = nested,
                    IsOverlay = type.AttachmentType == AttachmentType.Overlay,
                    IsAttached = type.AttachmentType == AttachmentType.Attachment
                });

                chain.Add(linkDoc.Title);
                addLinkPlacements(linkDoc, total, found, chain);
                chain.RemoveAt(chain.Count - 1);
            }
        }

        /// <summary>
        /// Where each id handed out came from: the link instance it was read through, if any,
        /// and the element's own id in its document. Ids rather than elements, because an
        /// element held past the command it was read in is not safe to touch.
        ///
        /// A list per id, because the host and the first placement of a link both keep their
        /// ids as Revit has them and so can hand out the same number for two elements.
        /// </summary>
        private readonly Dictionary<long, List<KeyValuePair<ElementId, ElementId>>> placedById =
            new Dictionary<long, List<KeyValuePair<ElementId, ElementId>>>();

        private void remember(PlacedElement placed)
        {
            List<KeyValuePair<ElementId, ElementId>> sources;
            if (!placedById.TryGetValue(placed.RevitId, out sources))
            {
                sources = new List<KeyValuePair<ElementId, ElementId>>();
                placedById[placed.RevitId] = sources;
            }

            ElementId link = placed.Placement.LinkInstanceId;
            ElementId element = placed.Element.Id;

            // The same element is collected again by every import, so only kept once.
            if (!sources.Any(source => Equals(source.Key, link) && source.Value == element))
            {
                sources.Add(new KeyValuePair<ElementId, ElementId>(link, element));
            }
        }

        /// <summary>
        /// Selects an imported element in Revit by the id the tool knows it by, whether it is
        /// in the host or in any placement of a link.
        ///
        /// A linked element is selected through a reference to it made through its link
        /// instance, which is the only way Revit will select something inside a link: its
        /// own id means nothing in the host, and handing it to the host selects whatever host
        /// element happens to share the number. Returns false, with why, when nothing could be
        /// selected.
        /// </summary>
        public bool SelectInRevit(long revitId, out string problem)
        {
            return SelectInRevit(new[] { revitId }, out problem);
        }

        /// <summary>
        /// Selects several imported elements in Revit at once, found the same way as one. True
        /// when any of them could be selected; <paramref name="problem"/> then says how many
        /// could not, and is empty when every one was found.
        /// </summary>
        public bool SelectInRevit(IEnumerable<long> revitIds, out string problem)
        {
            problem = "";
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document host = uiDoc.Document;

            List<Reference> references = new List<Reference>();
            List<long> missing = new List<long>();
            HashSet<long> inNestedLinks = new HashSet<long>();
            string lastError = "";

            foreach (long revitId in revitIds)
            {
                int before = references.Count;

                List<KeyValuePair<ElementId, ElementId>> sources;
                if (!placedById.TryGetValue(revitId, out sources) || sources.Count == 0)
                {
                    // Not read through this engine - results kept from before it was made. The
                    // host is the one place an id can still be found without knowing its link.
                    sources = new List<KeyValuePair<ElementId, ElementId>>
                    {
                        new KeyValuePair<ElementId, ElementId>(null, new ElementId(revitId))
                    };
                }

                foreach (KeyValuePair<ElementId, ElementId> source in sources)
                {
                    try
                    {
                        if (source.Key == null)
                        {
                            Element element = host.GetElement(source.Value);
                            if (element != null)
                            {
                                references.Add(new Reference(element));
                            }
                            continue;
                        }

                        if (Equals(source.Key, ElementId.InvalidElementId))
                        {
                            // Read through a nested link, which has no instance in the host to
                            // make a reference through.
                            inNestedLinks.Add(revitId);
                            continue;
                        }

                        RevitLinkInstance instance = host.GetElement(source.Key) as RevitLinkInstance;
                        Element linked = instance?.GetLinkDocument()?.GetElement(source.Value);
                        if (linked != null)
                        {
                            references.Add(new Reference(linked).CreateLinkReference(instance));
                        }
                    }
                    catch (Exception e)
                    {
                        // A link unloaded since the import, or a nested link Revit will not make a
                        // reference through. Said if nothing else could be selected either.
                        lastError = e.Message;
                    }
                }

                if (references.Count == before)
                {
                    missing.Add(revitId);
                }
            }

            if (references.Count == 0 && missing.All(inNestedLinks.Contains))
            {
                problem = (missing.Count == 1 ? $"Revit {missing[0]} is" : "these are")
                          + " in a nested link - a link inside another link - and Revit only selects "
                          + "inside a link placed directly in this model.";
                return false;
            }

            if (references.Count == 0)
            {
                problem = (missing.Count == 1
                              ? $"Revit {missing[0]} is not in the model any more"
                              : $"none of the {missing.Count} elements is in the model any more")
                          + (lastError.Length > 0 ? " - " + lastError : ", or the link has been unloaded.");
                return false;
            }

            if (missing.Count > 0)
            {
                problem = missing.Count + " of them could not be found: Revit "
                          + string.Join(", ", missing) + ".";
            }

            uiDoc.Selection.SetReferences(references);
            return true;
        }

        #endregion

        private MyPoint myPointFromXYZ(XYZ p)
        {
            return new MyPoint(p.X, p.Y, p.Z);
        }

        private void getFramesFromRevit()
        {
            framesElements_r = collectAcrossDocuments(doc =>
                new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance))
                    .WherePasses(new ElementCategoryFilter(BuiltInCategory.OST_StructuralFraming))
                    .ToElements());
        }

        /// <summary>
        /// The titles last picked in the link selection dialog, so asking again starts from
        /// that choice rather than from the guess. Titles rather than the documents, because
        /// a link can be unloaded or reloaded between one import and the next.
        /// </summary>
        private List<string> lastLinkChoice = null;

        /// <summary>
        /// Makes the next import ask which linked models to read again, with the ones picked
        /// last time already ticked. The choice is otherwise kept for the life of the engine,
        /// which is right for a re-read the tool does on its own - a change of alignment - but
        /// not for an import the user asked for, where a link may have been loaded since or
        /// the wrong one picked the first time.
        /// </summary>
        public void ChooseLinksAgain()
        {
            selectedDocuments = null;
            selectedPlacements = null;
            placementNotes.Clear();
        }

        /// <summary>
        /// The documents the user picked in the link selection dialog. The choice is cached
        /// for the life of the engine so importing beams and then columns only asks once.
        ///
        /// Cancelling the dialog stops the read. It used to go on and read nothing, which
        /// replaced whatever model was on the panel with an empty one.
        /// </summary>
        private List<Document> getSelectedDocuments()
        {
            if (selectedDocuments != null)
            {
                return selectedDocuments;
            }

            List<LinkInfo> tempDocs = getDocumentsToBeIncluded(commandData.Application.ActiveUIDocument.Document);

            using (LinkSelectionForm form = new LinkSelectionForm(tempDocs, lastLinkChoice))
            {
                if (form.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    throw new ImportStoppedException();
                }

                selectedDocuments = form.SelectedDocuments;
                lastLinkChoice = form.SelectedTitles;
            }

            return selectedDocuments;
        }
        public List<LinkInfo> getDocumentsToBeIncluded(Document baseDoc)
        {
            List<LinkInfo> docs = new List<LinkInfo>();
            docs.Add(new LinkInfo { Doc = baseDoc, IsNested = false });

            // The links placed directly in the host first: the dialog keeps the first entry
            // for each title, and a file linked both directly and nested inside another link
            // is not a nested link as far as choosing it goes.
            foreach (LinkPlacement link in findLinkPlacements(baseDoc).OrderBy(link => link.IsNested))
            {
                docs.Add(new LinkInfo
                {
                    Doc = link.Doc,
                    IsNested = link.IsNested,
                    IsOverlay = link.IsOverlay,
                    IsAttached = link.IsAttached
                });
            }

            return docs;
        }

        #region columns

        /// <summary>
        /// Reads every structural column in the selected documents with its plan location,
        /// rotation and section dimensions. The section is resolved by family name for the
        /// two known families and by parameter name for everything else; see
        /// <see cref="resolveSection"/>.
        /// </summary>
        public IList<Column_r> getFilterdColumns()
        {
            columnsData = new List<Column_r>();
            refreshSectionParameterNames();

            report("Finding the columns", 0, 0);

            List<PlacedElement> elements = collectAcrossDocuments(doc =>
                new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance))
                    .WherePasses(new ElementCategoryFilter(BuiltInCategory.OST_StructuralColumns))
                    .ToElements());

            int total = elements.Count;
            int done = 0;
            columnSizes.Clear();
            string[] leaveOut = PrefrencesData.SplitTypeNamePhrases(PrefrencesData.getPrefrencesData().ColumnTypeNamesToIgnore);
            Dictionary<string, int> leftOut = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (PlacedElement element in elements)
            {
                report("Reading columns", ++done, total);

                FamilyInstance instance = element.Element as FamilyInstance;
                string matched;
                if (instance != null
                    && RevitLeaveOut.Matches(getTypeName(instance), getFamilyName(instance), leaveOut, out matched))
                {
                    leftOut[matched] = leftOut.TryGetValue(matched, out int count) ? count + 1 : 1;
                    continue;
                }

                Column_r column = readColumn(element);
                if (column != null)
                {
                    columnsData.Add(column);
                }
            }

            if (leftOut.Count > 0)
            {
                addIssue($"{leftOut.Values.Sum()} Revit column(s) were left out by family or type name: " +
                         string.Join(", ", leftOut.Select(pair => pair.Key + " " + pair.Value)) +
                         ". The names are under Settings > Column check.");
            }
            reportMeasuredSizes("column", columnSizes);

            return columnsData;
        }

        private Column_r readColumn(PlacedElement placed)
        {
            Element element = placed.Element;
            FamilyInstance instance = element as FamilyInstance;
            if (instance == null)
            {
                return null;
            }

            string familyName = getFamilyName(instance);

            try
            {
                Transform documentTransform = placed.Placement.Transform;

                XYZ location = getColumnLocationPoint(element);
                if (location == null)
                {
                    addIssue($"{element.Id}: cannot obtain a location point for the column, skipped");
                    return null;
                }

                double rotation = getColumnRotation(element);
                if (documentTransform != null && documentTransform.IsConformal)
                {
                    location = documentTransform.OfPoint(location);
                    // The column's own X axis carried into the host. Adding the link's rotation
                    // to the column's is the same thing for a link that is only turned, but a
                    // mirrored link turns a column the other way.
                    XYZ axis = documentTransform.OfVector(new XYZ(Math.Cos(rotation), Math.Sin(rotation), 0));
                    rotation = Math.Atan2(axis.Y, axis.X);
                }

                Column_r column = new Column_r(placed.RevitId, myPointFromXYZ(location), rotation);
                column.FamilyName = familyName;
                column.TypeName = getTypeName(instance);
                column.TopElevationInFeet = getTopElevation(element, documentTransform);
                column.BottomElevationInFeet = getBottomElevation(element, documentTransform);

                resolveSection(instance, familyName, column);
                column.TypeMark = readTypeMark(instance);

                if (!column.HasValidDimensions)
                {
                    addIssue($"{element.Id}: cannot read the section of '{familyName}' / " +
                             $"'{column.TypeName}' - expected a Diameter parameter, or Width and Depth");
                }

                return column;
            }
            catch (Exception e)
            {
                addIssue($"{element.Id}: cannot read column - {e.Message}");
                return null;
            }
        }

        private static string getFamilyName(FamilyInstance instance)
        {
            try
            {
                return instance.Symbol?.Family?.Name ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static string getTypeName(FamilyInstance instance)
        {
            try
            {
                return instance.Symbol?.Name ?? "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Columns are normally point based, but a slanted column carries a location curve
        /// instead. For those the lower end is used as the plan point.
        /// </summary>
        private static XYZ getColumnLocationPoint(Element element)
        {
            LocationPoint locationPoint = element.Location as LocationPoint;
            if (locationPoint != null)
            {
                return locationPoint.Point;
            }

            LocationCurve locationCurve = element.Location as LocationCurve;
            if (locationCurve?.Curve != null)
            {
                XYZ start = locationCurve.Curve.GetEndPoint(0);
                XYZ end = locationCurve.Curve.GetEndPoint(1);
                return start.Z <= end.Z ? start : end;
            }

            return null;
        }

        private static double getColumnRotation(Element element)
        {
            LocationPoint locationPoint = element.Location as LocationPoint;
            return locationPoint?.Rotation ?? 0d;
        }

        /// <summary>
        /// Top of the element in host coordinates, in feet. ETABS names a column after the
        /// story it rises to, and assigns a wall area to the story its highest corner is
        /// nearest, so this is what the story assignment matches against for both.
        /// </summary>
        private static double getTopElevation(Element element, Transform documentTransform)
        {
            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null)
            {
                XYZ fallback = getColumnLocationPoint(element);
                double z = fallback?.Z ?? 0d;
                return documentTransform != null && documentTransform.IsConformal
                    ? z + documentTransform.Origin.Z
                    : z;
            }

            double topZ = Math.Max(box.Min.Z, box.Max.Z);
            if (documentTransform != null && documentTransform.IsConformal)
            {
                topZ += documentTransform.Origin.Z;
            }
            return topZ;
        }

        /// <summary>The foot of the column, for telling which stories it rises through. NaN when unknown.</summary>
        private static double getBottomElevation(Element element, Transform documentTransform)
        {
            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null)
            {
                return double.NaN;
            }

            double bottomZ = Math.Min(box.Min.Z, box.Max.Z);
            if (documentTransform != null && documentTransform.IsConformal)
            {
                bottomZ += documentTransform.Origin.Z;
            }
            return bottomZ;
        }

        // The parameters the user mapped to a column's plan dimensions, under Settings > Revit
        // parameters. Nothing is guessed: an unmapped dimension is measured or not read.
        private List<MappedParameter> diameterParameters = new List<MappedParameter>();
        private List<MappedParameter> widthParameters = new List<MappedParameter>();
        private List<MappedParameter> depthParameters = new List<MappedParameter>();
        private string[] typeMarkParameterNames = new string[0];

        private void refreshSectionParameterNames()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            diameterParameters = ParameterMapping.Parse(pref.ColumnDiameterParameterNames);
            widthParameters = ParameterMapping.Parse(pref.ColumnWidthParameterNames);
            depthParameters = ParameterMapping.Parse(pref.ColumnDepthParameterNames);
            typeMarkParameterNames = PrefrencesData.SplitParameterNames(pref.ColumnTypeMarkParameterNames);
        }

        /// <summary>
        /// The mark naming the column's type, which the check compares against the ETABS
        /// frame groups. A column that carries none is left blank rather than reported here,
        /// because the comparison itself flags it, on the column, where it can be seen.
        /// </summary>
        private string readTypeMark(FamilyInstance instance)
        {
            ElementType type = instance.Document.GetElement(instance.GetTypeId()) as ElementType;
            return findTextParameter(type, instance, typeMarkParameterNames);
        }

        /// <summary>
        /// Works out whether a column is round or rectangular and reads its dimensions.
        ///
        /// The parameters the user mapped under Settings > Revit parameters decide it: a
        /// mapped diameter makes it round, a mapped width and depth rectangular. A dimension
        /// whose mapping finds nothing is measured off the solid, when that is switched on.
        /// Where each dimension came from is kept on the column, for its card and the import
        /// report. A column whose section cannot be read either way is still returned, with an
        /// Unknown shape, so it appears on the plan and is reported rather than going missing.
        /// </summary>
        private void resolveSection(FamilyInstance instance, string familyName, Column_r column)
        {
            ElementType type = instance.Document.GetElement(instance.GetTypeId()) as ElementType;

            string diameterSource, widthSource, depthSource;
            double diameter = readMappedLength(type, instance, diameterParameters, out diameterSource, columnSizes.Rejected);
            double width = readMappedLength(type, instance, widthParameters, out widthSource, columnSizes.Rejected);
            double depth = readMappedLength(type, instance, depthParameters, out depthSource, columnSizes.Rejected);

            double measuredWidth = 0, measuredDepth = 0;
            bool round = false;
            bool measured = PrefrencesData.getPrefrencesData().MeasureSizeFromGeometry
                            && measureColumn(instance, out measuredWidth, out measuredDepth, out round);

            if (diameter > 0)
            {
                if (measured)
                {
                    double across = Math.Max(measuredWidth, measuredDepth);
                    compareSizes(instance.Id, diameter, diameter, across, across, columnSizes);
                }
                column.SetDimensions(SectionShape.Round, 0, 0, diameter);
                column.SizeSource = "Diameter: " + diameterSource;
                columnSizes.Count("diameter", diameterSource);
                return;
            }

            if (width > 0 && depth > 0)
            {
                if (measured)
                {
                    compareSizes(instance.Id, width, depth, measuredWidth, measuredDepth, columnSizes);
                }
                column.SetDimensions(SectionShape.Rectangular, width, depth, 0);
                column.SizeSource = $"Width: {widthSource}; Depth: {depthSource}";
                columnSizes.Count("width", widthSource);
                columnSizes.Count("depth", depthSource);
                return;
            }

            if (measured)
            {
                columnSizes.Measured++;
                if (round)
                {
                    column.SetDimensions(SectionShape.Round, 0, 0, Math.Max(measuredWidth, measuredDepth));
                    column.SizeSource = "Diameter: " + SizeSources.Measured;
                    columnSizes.Count("diameter", SizeSources.Measured);
                    return;
                }

                // A dimension the mapping found is kept; only the missing one is measured.
                widthSource = width > 0 ? widthSource : SizeSources.Measured;
                depthSource = depth > 0 ? depthSource : SizeSources.Measured;
                column.SetDimensions(SectionShape.Rectangular, width > 0 ? width : measuredWidth,
                                     depth > 0 ? depth : measuredDepth, 0);
                column.SizeSource = $"Width: {widthSource}; Depth: {depthSource}";
                columnSizes.Count("width", widthSource);
                columnSizes.Count("depth", depthSource);
                return;
            }

            column.SetDimensions(SectionShape.Unknown, width, depth, diameter);
            column.SizeSource = $"Width: {widthSource ?? SizeSources.NotFound}; Depth: {depthSource ?? SizeSources.NotFound}";
            columnSizes.Count("size", SizeSources.NotFound);
        }

        /// <summary>
        /// The first of the parameters the user mapped to a property that the member carries
        /// as a length, in Revit internal units (feet), and where it was found - "Type 'b'".
        /// Each is looked for where its mapping says: the type, the instance, or the type and
        /// then the instance. Zero, with a null source, when none is there; nothing else is
        /// tried, so a property nobody mapped is never read off a parameter.
        /// </summary>
        private static double readMappedLength(ElementType type, Element instance, List<MappedParameter> mapped,
                                               out string source, Dictionary<string, int> rejected)
        {
            source = null;
            foreach (MappedParameter parameter in mapped)
            {
                double value;
                if (parameter.Scope != ParameterScope.Instance
                    && tryReadLength(type, parameter.Name, out value, true, rejected, "Type"))
                {
                    source = SizeSources.Parameter(parameter.Name, true);
                    return value;
                }
                if (parameter.Scope != ParameterScope.Type
                    && tryReadLength(instance, parameter.Name, out value, true, rejected, "Instance"))
                {
                    source = SizeSources.Parameter(parameter.Name, false);
                    return value;
                }
            }
            return 0;
        }

        /// <summary>
        /// A parameter of this name holding a length, in feet.
        ///
        /// <paramref name="lengthOnly"/>, for the sizes the user mapped: only a parameter Revit
        /// keeps as a Length is read. Text is never parsed - "400" in a text parameter would be
        /// taken as 400 feet - and a plain number or any other kind of value is not a length.
        /// One found under the name but refused for either reason is counted in
        /// <paramref name="rejected"/>, so the import can say which mapping did not work.
        /// Without it, as the floor thickness fallbacks have always read: any number, or text
        /// that parses as one.
        /// </summary>
        private static bool tryReadLength(Element element, string name, out double value, bool lengthOnly = false,
                                          Dictionary<string, int> rejected = null, string where = null)
        {
            value = 0;
            if (element == null)
            {
                return false;
            }

            foreach (Parameter parameter in element.Parameters)
            {
                if (!string.Equals(parameter.Definition?.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!lengthOnly)
                {
                    if (parameter.StorageType == StorageType.Double)
                    {
                        value = parameter.AsDouble();
                        return value > 0;
                    }

                    double parsed;
                    if (double.TryParse(GetOneParameterValueAsString(parameter), out parsed) && parsed > 0)
                    {
                        value = parsed;
                        return true;
                    }
                    continue;
                }

                string refusal = null;
                if (parameter.StorageType != StorageType.Double)
                {
                    refusal = "is not a number";
                }
                else if (!isLength(parameter))
                {
                    refusal = "is not a length";
                }

                if (refusal != null)
                {
                    if (rejected != null)
                    {
                        countIgnored(rejected, $"{where ?? "Parameter"} '{name}' {refusal}");
                    }
                    continue;
                }

                value = parameter.AsDouble();
                if (value > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool isLength(Parameter parameter)
        {
            try
            {
                return parameter.Definition.GetDataType() == SpecTypeId.Length;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// First non-empty text value among the candidate parameter names. The type is
        /// searched before the instance, and names are matched without regard to case.
        /// Returns an empty string when nothing matches.
        /// </summary>
        private static string findTextParameter(ElementType type, Element instance, string[] candidateNames)
        {
            foreach (string name in candidateNames)
            {
                string value;
                if (tryReadText(type, name, out value) || tryReadText(instance, name, out value))
                {
                    return value;
                }
            }
            return "";
        }

        private static bool tryReadText(Element element, string name, out string value)
        {
            value = "";
            if (element == null)
            {
                return false;
            }

            foreach (Parameter parameter in element.Parameters)
            {
                if (!string.Equals(parameter.Definition?.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string text = (GetOneParameterValueAsString(parameter) ?? "").Trim();
                if (text.Length > 0)
                {
                    value = text;
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region walls

        private List<Wall_R> wallsData = new List<Wall_R>();

        private int wallsNotStraight;
        private int wallsWithoutThickness;
        private int wallsTooShort;
        private int wallsNotStructural;
        private int wallsSnappedToCorners;
        /// <summary>
        /// How many walls each name in the ignore list took out, so what is reported is the
        /// names that actually matched rather than the whole list. A name that turns out to
        /// appear in half the wall types in a project is the mistake this has to make visible,
        /// and it cannot be seen in a count next to a list of five names, four of which
        /// matched nothing.
        /// </summary>
        private readonly Dictionary<string, int> wallsIgnoredByName =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Reads every wall in the selected documents as the line it was drawn on and the
        /// thickness of structure in it.
        ///
        /// One collector pass over the wall class, and everything the check needs comes off the
        /// element and its type: the location line, the layers the type declares, the level the
        /// wall rises to. No solid is asked for and no geometry is extracted - a wall check is a
        /// plan check, and pulling the geometry of every wall in a tower is what turns an import
        /// into a wait.
        /// </summary>
        public IList<Wall_R> getFilterdWalls()
        {
            wallsData = new List<Wall_R>();
            wallsNotStraight = 0;
            wallsWithoutThickness = 0;
            wallsTooShort = 0;
            wallsNotStructural = 0;
            wallsSnappedToCorners = 0;
            wallsIgnoredByName.Clear();

            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            // Phrases rather than words, so a name with a space in it - "Second Cast" - stays
            // one thing to leave out. Split on spaces it would be "Cast" on its own, which
            // takes every cast in situ wall in the model out of the check with it.
            string[] ignoredTypeNames = PrefrencesData.SplitTypeNamePhrases(pref.WallTypeNamesToIgnore);
            string[] markNames = PrefrencesData.SplitParameterNames(pref.WallTypeMarkParameterNames);

            report("Finding the walls", 0, 0);

            List<PlacedElement> elements = collectAcrossDocuments(doc =>
                new FilteredElementCollector(doc).OfClass(typeof(Wall)).ToElements());

            int total = elements.Count;
            int done = 0;

            foreach (PlacedElement element in elements)
            {
                report("Reading walls", ++done, total);

                Wall_R wall = readWall(element, pref, ignoredTypeNames, markNames);
                if (wall != null)
                {
                    wallsData.Add(wall);
                }
            }

            // Last, because a corner needs both of its walls read before it is a corner.
            snapWallsToCorners(pref);

            reportWallCounts(pref);

            return wallsData;
        }

        /// <summary>
        /// Reads the walls that meet as running to the point their centre lines cross, which
        /// is where ETABS meshes the joint - see <see cref="WallCorners"/>.
        /// </summary>
        private void snapWallsToCorners(PrefrencesData pref)
        {
            if (!pref.SnapWallsToCorners)
            {
                return;
            }

            wallsSnappedToCorners = WallCorners.SnapToJunctions(wallsData, pref.WallCornerBuffer);

            // The shortest wall worth checking is asked again afterwards, because the length
            // it was let through on is not the length it now has. What a corner moves is
            // bounded by half a wall thickness, so anything this takes out was a sliver caught
            // in a junction rather than a wall, and it goes out with the rest of them.
            int before = wallsData.Count;
            wallsData = wallsData.Where(wall => wall.Length >= pref.WallMinimumLength).ToList();
            wallsTooShort += before - wallsData.Count;
        }

        /// <summary>
        /// What the wall import left out, counted rather than named one by one: a curtain wall
        /// facade is hundreds of elements, and hundreds of lines saying the same thing is a
        /// list nobody reads.
        /// </summary>
        private void reportWallCounts(PrefrencesData pref)
        {
            // Said rather than done quietly, because it is the one thing here that changes a
            // wall instead of leaving it out: a length read off the plan afterwards is the
            // length to the corner, not the length the wall was drawn to, and somebody
            // measuring the two against each other has to know which they are looking at.
            if (wallsSnappedToCorners > 0)
            {
                addIssue($"{wallsSnappedToCorners} Revit wall(s) meet another wall and were read as "
                         + "running to the point their centre lines cross, which is where ETABS "
                         + "meshes the joint, rather than to the face they were drawn to. Turn off "
                         + "\"Read walls as running to the corner\" under Settings > Wall check to "
                         + "compare the lines as drawn.");
            }

            if (wallsNotStraight > 0)
            {
                addIssue($"{wallsNotStraight} Revit wall(s) are drawn on a curve. An ETABS wall panel is "
                         + "flat, so there is nothing to compare a curved wall against and they are left "
                         + "out of the wall check.");
            }

            if (wallsWithoutThickness > 0)
            {
                addIssue($"{wallsWithoutThickness} Revit wall(s) have a type this could read no thickness "
                         + "from, so the thickness check on them will say so rather than pass or fail.");
            }

            if (wallsTooShort > 0)
            {
                addIssue($"{wallsTooShort} Revit wall(s) are shorter than {pref.WallMinimumLength}mm and "
                         + "were left out of the wall check as jambs and fins rather than walls.");
            }

            // Said outright, and with the way out of it, because a model that never sets the
            // structural usage on its walls would otherwise hand back nothing at all and give
            // no reason for it - which reads as a tool that does not work rather than a filter
            // doing exactly what it was asked.
            if (wallsNotStructural > 0)
            {
                addIssue($"{wallsNotStructural} Revit wall(s) are not marked as bearing or shear walls "
                         + "and were left out. If the walls in this model do not carry that mark, turn "
                         + "off \"Structural walls only\" under Settings > Wall check and import again.");
            }

            // Named outright as well as counted, because this filter is the one the model can
            // be made to fail: a name typed in the box that turns out to appear in half the
            // wall types in the project takes them all out quietly otherwise. Which name did
            // it is the whole of the diagnosis, so it is the names that matched that are
            // listed, each with what it took, and not the list as it was typed.
            if (wallsIgnoredByName.Count > 0)
            {
                int ignored = wallsIgnoredByName.Values.Sum();
                string byName = string.Join(", ", wallsIgnoredByName
                                                  .OrderByDescending(entry => entry.Value)
                                                  .Select(entry => $"{entry.Key} {entry.Value}"));

                addIssue($"{ignored} Revit wall(s) are named as something to leave out and were not "
                         + $"checked: {byName}. The names are under Settings > Wall check.");
            }
        }

        /// <summary>
        /// One Revit wall, or null for one that is not this check's business: a curtain or
        /// stacked wall, a partition where only structure is being checked, a type named as
        /// something to leave out, or a wall too short or too curved to compare.
        /// </summary>
        private Wall_R readWall(PlacedElement placed, PrefrencesData pref, string[] ignoredTypeNames, string[] markNames)
        {
            Wall wall = placed.Element as Wall;
            if (wall == null)
            {
                return null;
            }

            try
            {
                WallType wallType = wall.WallType;

                // Only a wall built as a stack of layers has a thickness worth comparing: a
                // curtain wall is glass and mullions, and a stacked wall carries no width on
                // its own type at all - its parts do, and they are walls in their own right.
                if (wallType != null && wallType.Kind != WallKind.Basic)
                {
                    return null;
                }

                string ignoredBy;
                if (isIgnoredByName(wall, ignoredTypeNames, out ignoredBy))
                {
                    int already;
                    wallsIgnoredByName.TryGetValue(ignoredBy, out already);
                    wallsIgnoredByName[ignoredBy] = already + 1;
                    return null;
                }

                if (pref.StructuralWallsOnly && !isStructuralWall(wall))
                {
                    wallsNotStructural++;
                    return null;
                }

                LocationCurve locationCurve = wall.Location as LocationCurve;
                Line line = locationCurve == null ? null : locationCurve.Curve as Line;
                if (line == null)
                {
                    wallsNotStraight++;
                    return null;
                }

                XYZ start = line.GetEndPoint(0);
                XYZ end = line.GetEndPoint(1);

                Transform documentTransform = placed.Placement.Transform;
                if (documentTransform != null && documentTransform.IsConformal)
                {
                    start = documentTransform.OfPoint(start);
                    end = documentTransform.OfPoint(end);
                }

                Wall_R read = new Wall_R(placed.RevitId, myPointFromXYZ(start), myPointFromXYZ(end));
                read.TypeName = getElementTypeName(wall);
                read.ElementName = wall.Name;
                read.TypeMark = findTextParameter(wallType, wall, markNames);
                read.TopElevationInFeet = getTopElevation(wall, documentTransform);
                read.SetThickness(readWallThickness(wall, pref));

                if (read.Length < pref.WallMinimumLength)
                {
                    wallsTooShort++;
                    return null;
                }

                if (!read.HasValidThickness)
                {
                    wallsWithoutThickness++;
                }

                return read;
            }
            catch (Exception e)
            {
                addIssue($"{wall.Id}: cannot read wall - {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// True when Revit says the wall is holding something up. A partition drawn in the
        /// structural model would otherwise read as a wall ETABS is missing, one per room.
        /// </summary>
        private static bool isStructuralWall(Wall wall)
        {
            try
            {
                return wall.StructuralUsage != Autodesk.Revit.DB.Structure.StructuralWallUsage.NonBearing;
            }
            catch (Exception)
            {
                // A wall that will not answer is kept rather than dropped: leaving out
                // something structural is the worse of the two mistakes.
                return true;
            }
        }

        /// <summary>
        /// The concrete in a wall, in mm.
        ///
        /// The structural layers of the type by default, because a wall type carries its
        /// finishes as layers too and the whole width would read those as structure - a 200
        /// wall with 20 of plaster either side reads as 240 and fails against the ETABS 200
        /// every time. The whole width is the fallback, and the setting to go straight to it.
        /// </summary>
        private static double readWallThickness(Wall wall, PrefrencesData pref)
        {
            if (pref.ReadWallThicknessFromStructuralLayers)
            {
                bool varies;
                double structural = readStructuralThickness(wall, out varies);
                if (structural > 0)
                {
                    return structural;
                }
            }

            try
            {
                return Math.Round(wall.Width * 304.8d);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        #endregion

        #region SIDL_C loads

        /// <summary>
        /// Reads the walls and fill strips the SIDL_C check weighs: every wall whose name is on
        /// the SIDL_C list, and every floor whose name is on the fill list and is narrow enough
        /// to be a strip.
        ///
        /// A read of its own rather than a share of the wall and floor reads, because these are
        /// exactly what those reads leave out - by name, by not being structure, by being fill -
        /// and because what is wanted of one is different: how much it weighs per metre and the
        /// floor it stands on, not its thickness against an ETABS panel. Cheap next to them: the
        /// walls are read off their location line and bounding box, and only the floors whose
        /// name already says fill have their geometry pulled.
        /// </summary>
        public IList<SidlCSource_R> getSidlCSources()
        {
            List<SidlCSource_R> sources = new List<SidlCSource_R>();
            PrefrencesData pref = PrefrencesData.getPrefrencesData();

            string[] wallNames = PrefrencesData.SplitTypeNamePhrases(pref.SidlCWallNames);
            string[] fillNames = PrefrencesData.SplitTypeNamePhrases(pref.SidlCFillSlabNames);

            if (wallNames.Length > 0)
            {
                report("Finding the SIDL_C walls", 0, 0);
                List<PlacedElement> walls = collectAcrossDocuments(doc =>
                    new FilteredElementCollector(doc).OfClass(typeof(Wall)).ToElements());

                int done = 0;
                foreach (PlacedElement placed in walls)
                {
                    report("Reading SIDL_C walls", ++done, walls.Count);

                    sources.AddRange(readSidlCWall(placed, pref, wallNames));
                }
            }

            if (fillNames.Length > 0)
            {
                report("Finding the fill strips", 0, 0);
                List<PlacedElement> floors = collectAcrossDocuments(doc => collectFloors(doc, pref));

                int done = 0;
                int tooWide = 0;
                int notStraight = 0;
                foreach (PlacedElement placed in floors)
                {
                    report("Reading fill strips", ++done, floors.Count);

                    string matched;
                    if (!RevitLeaveOut.Matches(getElementTypeName(placed.Element), placed.Element.Name,
                                               fillNames, out matched))
                    {
                        continue;
                    }

                    SidlCSource_R strip = readFillStrip(placed, pref, matched, ref tooWide, ref notStraight);
                    if (strip != null)
                    {
                        sources.Add(strip);
                    }
                }

                if (notStraight > 0)
                {
                    addIssue($"{notStraight} fill floor(s) are not one straight strip - an L, a bend, a "
                             + "patch - so they have no single line to load and were left out of the "
                             + "SIDL_C check.");
                }

                if (tooWide > 0)
                {
                    addIssue($"{tooWide} fill floor(s) are {Math.Round(pref.SidlCFillSlabMaximumWidth)} mm "
                             + "wide or wider, so they are an area of fill rather than a strip and were "
                             + "left out of the SIDL_C check.");
                }
            }

            return sources;
        }

        /// <summary>
        /// One wall named on the SIDL_C list, as the line it runs along, its thickness and its
        /// height. Empty for a wall not on the list, or one with nothing to weigh.
        ///
        /// The height is read off the wall's elevation, place by place, not off its bounding
        /// box: an upstand whose profile was edited - stepped, sloped, notched - is as high as
        /// it is at each place, and is cut into a load per length at one height. See
        /// <see cref="WallProfile"/>. Where the elevation cannot be read, the bounding box.
        /// </summary>
        private List<SidlCSource_R> readSidlCWall(PlacedElement placed, PrefrencesData pref, string[] names)
        {
            List<SidlCSource_R> sources = new List<SidlCSource_R>();
            Wall wall = placed.Element as Wall;
            if (wall == null)
            {
                return sources;
            }

            try
            {
                // A curtain wall is glass and mullions, and a stacked wall's weight is its
                // parts', which are walls in their own right and read as such.
                WallType wallType = wall.WallType;
                if (wallType != null && wallType.Kind != WallKind.Basic)
                {
                    return sources;
                }

                string matched;
                if (!RevitLeaveOut.Matches(getElementTypeName(wall), wall.Name, names, out matched))
                {
                    return sources;
                }

                LocationCurve location = wall.Location as LocationCurve;
                if (location == null || location.Curve == null)
                {
                    return sources;
                }

                Transform documentTransform = placed.Placement.Transform;
                bool moved = documentTransform != null && documentTransform.IsConformal;

                // Tessellated rather than taken end to end, so a curved planter wall is laid on
                // the frames as the curve it is.
                List<MyPoint> line = location.Curve.Tessellate()
                    .Select(point => moved ? documentTransform.OfPoint(point) : point)
                    .Select(point => DataContainer.ModelTransformation.ApplyToPointInMM(
                        myPointFromXYZ(point) * 304.8d))
                    .ToList();

                BoundingBoxXYZ box = wall.get_BoundingBox(null);
                if (box == null)
                {
                    addIssue($"{wall.Id}: '{wall.Name}' has no extent to read a height from, left out of the SIDL_C check");
                    return sources;
                }

                double bottom = Math.Min(box.Min.Z, box.Max.Z);
                double top = Math.Max(box.Min.Z, box.Max.Z);
                if (moved)
                {
                    bottom += documentTransform.Origin.Z;
                    top += documentTransform.Origin.Z;
                }

                double height = (top - bottom) * 304.8d;
                double thickness = readWallThickness(wall, pref);
                if (thickness <= 0 || height <= 0)
                {
                    addIssue($"{wall.Id}: '{wall.Name}' has no thickness or no height to weigh, left out of the SIDL_C check");
                    return sources;
                }

                double length = location.Curve.Length * 304.8d;
                List<WallProfile.Part> parts = readWallProfile(wall, location.Curve, pref);
                if (parts == null || parts.Count == 0)
                {
                    parts = new List<WallProfile.Part> { new WallProfile.Part(0, length, height, false) };
                }

                double planLength = 0;
                for (int i = 0; i < line.Count - 1; i++)
                {
                    planLength += WallGeometry.PlanLength(line[i], line[i + 1]);
                }
                double toPlan = length > 0 ? planLength / length : 1;

                for (int i = 0; i < parts.Count; i++)
                {
                    WallProfile.Part part = parts[i];
                    bool whole = part.From < 1 && part.To > length - 1;
                    List<MyPoint> piece = whole ? line : WallProfile.Cut(line, part.From * toPlan, part.To * toPlan);
                    if (piece.Count < 2)
                    {
                        continue;
                    }

                    sources.Add(new SidlCSource_R(SidlCSourceKind.Wall, placed.RevitId, piece, thickness, part.Height,
                                                  bottom * 304.8d + DataContainer.ModelTransformation.Translation.Z)
                    {
                        TypeName = getElementTypeName(wall),
                        ElementName = wall.Name ?? "",
                        MatchedName = matched ?? "",
                        Comment = readInstanceComment(wall),
                        Part = parts.Count > 1 ? i + 1 : 0,
                        Parts = parts.Count > 1 ? parts.Count : 0,
                        SizeVaries = part.Varies
                    });
                }
                return sources;
            }
            catch (Exception e)
            {
                addIssue($"{wall.Id}: cannot read wall for the SIDL_C check - {e.Message}");
                return new List<SidlCSource_R>();
            }
        }

        /// <summary>
        /// The wall cut into lengths at one height each, off both its side faces: every loop of
        /// each, flattened into the wall's own elevation - along it from its start, and up - in
        /// mm. Null where the faces cannot be read, and the bounding box is then all there is.
        /// </summary>
        private List<WallProfile.Part> readWallProfile(Wall wall, Curve curve, PrefrencesData pref)
        {
            try
            {
                XYZ start = curve.GetEndPoint(0);
                Line straight = curve as Line;
                XYZ along = straight != null
                    ? new XYZ(straight.Direction.X, straight.Direction.Y, 0).Normalize()
                    : null;
                double length = curve.Length;

                List<List<IList<double[]>>> faces = new List<List<IList<double[]>>>();
                foreach (ShellLayerType side in new[] { ShellLayerType.Exterior, ShellLayerType.Interior })
                {
                    List<IList<double[]>> loops = new List<IList<double[]>>();
                    foreach (Reference reference in HostObjectUtils.GetSideFaces(wall, side))
                    {
                        Face face = wall.GetGeometryObjectFromReference(reference) as Face;
                        if (face == null)
                        {
                            continue;
                        }

                        foreach (EdgeArray loop in face.EdgeLoops)
                        {
                            List<double[]> points = new List<double[]>();
                            foreach (Edge edge in loop)
                            {
                                IList<XYZ> run = edge.AsCurveFollowingFace(face).Tessellate();
                                for (int i = 0; i < run.Count - 1; i++)
                                {
                                    XYZ point = run[i];
                                    double s = along != null
                                        ? (point - start).DotProduct(along)
                                        : curve.ComputeNormalizedParameter(
                                              curve.Project(new XYZ(point.X, point.Y, start.Z)).Parameter) * length;
                                    points.Add(new[] { s * 304.8d, point.Z * 304.8d });
                                }
                            }
                            if (points.Count >= 3)
                            {
                                loops.Add(points);
                            }
                        }
                    }
                    faces.Add(loops);
                }

                return WallProfile.Parts(faces, length * 304.8d, pref.LineLoadMinimumIssueLength);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// One fill floor read as a strip: the line down its middle, its width and its depth.
        /// Null - and counted - where it is too wide to be a strip, or not straight enough to
        /// have one line down it.
        /// </summary>
        private SidlCSource_R readFillStrip(PlacedElement placed, PrefrencesData pref, string matched,
                                            ref int tooWide, ref int notStraight)
        {
            Slab_R slab = readSlab(placed, pref, structuralOrNot: true);
            if (slab == null || slab.Plan == null)
            {
                return null;
            }

            MyPoint start, end;
            double width;
            if (!SidlCSource_R.StripAxis(slab.Plan.Outer.Points, slab.Plan.Area, out start, out end, out width))
            {
                notStraight++;
                return null;
            }

            if (width >= pref.SidlCFillSlabMaximumWidth)
            {
                tooWide++;
                return null;
            }

            double thickness = slab.NominalThickness > 0 ? slab.NominalThickness : slab.MaximumThickness;
            if (thickness <= 0)
            {
                return null;
            }

            return new SidlCSource_R(SidlCSourceKind.FillSlab, placed.RevitId, new[] { start, end },
                                     thickness, width, slab.BottomElevation)
            {
                TypeName = slab.TypeName,
                ElementName = slab.ElementName,
                MatchedName = matched ?? "",
                Comment = readInstanceComment(placed.Element)
            };
        }

        /// <summary>
        /// The element's instance Comments, or empty. Always read, so that switching the SIDL_C
        /// check over to the comments needs no import.
        /// </summary>
        private static string readInstanceComment(Element element)
        {
            try
            {
                Parameter comment = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                return comment == null || !comment.HasValue ? "" : comment.AsString() ?? "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>
        /// Reads the light weight fill floors the SIDL_C area check weighs: every floor whose
        /// name is on the list, structure or not, as the plan it covers and the volume of it.
        ///
        /// The volume is what is wanted of a fill. A ramp, or a fill shape edited to falls, has
        /// no one depth, and its volume over its plan area is its average depth whatever shape
        /// its top is. The plan is its top faces merged into one region - a shape edited fill
        /// comes back as a facet per slope.
        /// </summary>
        public IList<SidlCAreaSource_R> getSidlCAreaSources()
        {
            List<SidlCAreaSource_R> sources = new List<SidlCAreaSource_R>();
            PrefrencesData pref = PrefrencesData.getPrefrencesData();

            string[] names = PrefrencesData.SplitTypeNamePhrases(pref.SidlCAreaFloorNames);
            if (names.Length == 0)
            {
                return sources;
            }

            report("Finding the light weight fill floors", 0, 0);
            List<PlacedElement> floors = collectAcrossDocuments(doc => collectFloors(doc, pref));

            int done = 0;
            foreach (PlacedElement placed in floors)
            {
                report("Reading light weight fill floors", ++done, floors.Count);

                string matched;
                if (!RevitLeaveOut.Matches(getElementTypeName(placed.Element), placed.Element.Name, names, out matched))
                {
                    continue;
                }

                SidlCAreaSource_R fill = readFillArea(placed, pref, matched);
                if (fill != null)
                {
                    sources.Add(fill);
                }
            }

            return sources;
        }

        private SidlCAreaSource_R readFillArea(PlacedElement placed, PrefrencesData pref, string matched)
        {
            Element element = placed.Element;

            Slab_R slab = readSlab(placed, pref, structuralOrNot: true);
            if (slab == null || slab.Parts.Count == 0)
            {
                return null;
            }

            try
            {
                // Revit's own figure for the whole floor, every piece of it; failing that, the
                // largest solid, which for a fill is nearly always all of it.
                double volumeCubicFeet = 0;
                Parameter volume = element.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
                if (volume != null && volume.HasValue)
                {
                    volumeCubicFeet = volume.AsDouble();
                }
                if (volumeCubicFeet <= 0)
                {
                    Solid solid = getLargestSolid(element);
                    volumeCubicFeet = solid == null ? 0 : solid.Volume;
                }

                if (volumeCubicFeet <= 0)
                {
                    addIssue($"{element.Id}: '{element.Name}' has no volume to weigh, left out of the SIDL_C area check");
                    return null;
                }

                IList<PlanPolygon> plan = slab.Parts;
                if (plan.Count > 1)
                {
                    try
                    {
                        plan = PlanRegionMath.UnionWhatWillMerge(plan);
                    }
                    catch (Exception)
                    {
                        plan = slab.Parts;
                    }
                }

                SidlCAreaSource_R fill = new SidlCAreaSource_R(placed.RevitId, plan, volumeCubicFeet * Math.Pow(304.8d, 3),
                                                               slab.MinimumThickness, slab.MaximumThickness, slab.BottomElevation)
                {
                    TypeName = slab.TypeName,
                    ElementName = slab.ElementName,
                    MatchedName = matched ?? ""
                };

                // Its faces, so its depth can be told place by place: what each band of SIDL_C
                // under a ramp is checked against is the fill over that band.
                Solid faces = getLargestSolid(element);
                if (faces != null)
                {
                    List<List<List<MyPoint>>> topFaces = new List<List<List<MyPoint>>>();
                    List<List<MyPoint>> bottomLoops = new List<List<MyPoint>>();
                    readHorizontalFaceLoops(faces, placed.Placement.Transform, topFaces, bottomLoops);
                    fill.SetFaces(topFaces.SelectMany(face => face), bottomLoops);
                }

                return fill;
            }
            catch (Exception e)
            {
                addIssue($"{element.Id}: cannot read the fill for the SIDL_C area check - {e.Message}");
                return null;
            }
        }

        #endregion

        #region slabs

        private List<Slab_R> slabsData = new List<Slab_R>();

        /// <summary>
        /// Reads every floor in the selected documents as the plan region its top face covers
        /// and the depth of concrete under it.
        ///
        /// Both faces are read, and each answers a different question. The top face says where
        /// the floor is and where its openings are: it is the face that lines up with the plane
        /// an ETABS area is drawn on, and its inner loops are the openings, already cut out of
        /// the geometry by Revit. The bottom face says how deep the concrete runs.
        ///
        /// Taking the depth as top minus bottom, rather than off the floor type, is what makes
        /// a drop panel visible whichever way it was modelled - as a thicker floor, by editing
        /// the slab shape, or as a separate floor underneath, which the slab check then stacks
        /// back onto the slab above it.
        /// </summary>
        public IList<Slab_R> getFilterdSlabs()
        {
            slabsData = new List<Slab_R>();
            PrefrencesData pref = PrefrencesData.getPrefrencesData();

            // A file per import, not per session. Starting one closes any still open, so a
            // second import writes a new file rather than adding to the first - which made
            // the two runs impossible to tell apart, and left the last one with no end to it.
            if (pref.WriteGeometryDump)
            {
                string dump = GeometryDump.Start(GeometryDump.DefaultFolder(), "Revit floor import");
                addIssue(dump == null
                    ? "The geometry dump could not be opened, so this run is not being recorded."
                    : "Geometry dump: " + dump);
            }

            string[] ignoredNames = PrefrencesData.SplitTypeNameWords(pref.SlabTypeNamesToIgnore);
            int inSeveralPieces = 0;
            int ignored = 0;
            floorsFromSketch = 0;
            sketchChecked = 0;
            sketchAgreed = 0;
            sketchDisagreed.Clear();

            // Floors whose type declares no thickness have to be measured instead, and a
            // measured floor that is not flat has no single thickness, so it comes out of the
            // check as something a person has to look at rather than as right or wrong. That
            // is the usual reason for a plan full of amber, and it is a fact about the floor
            // types rather than about the two models disagreeing - so it is worth saying.
            Dictionary<string, int> measuredByType = new Dictionary<string, int>();

            // The slowest of the four by a wide margin: every floor has both its faces
            // pulled out of Revit's geometry, so this is the pass a large model appears to
            // hang in and the one the count on screen is really for.
            report("Finding the floors", 0, 0);
            List<PlacedElement> floorElements = collectAcrossDocuments(doc => collectFloors(doc, pref));

            int total = floorElements.Count;
            int done = 0;

            try
            {
                foreach (PlacedElement placed in floorElements)
                {
                    report("Reading floors", ++done, total);

                    if (isIgnoredByName(placed.Element, ignoredNames))
                    {
                        ignored++;
                        continue;
                    }

                    Slab_R slab = readSlab(placed, pref);
                    if (slab != null)
                    {
                        slabsData.Add(slab);

                        if (slab.Parts.Count > 1)
                        {
                            inSeveralPieces++;
                        }

                        if (pref.ReadSlabThicknessFromType
                            && slab.StructuralThickness <= 0
                            && slab.HasVariableThickness)
                        {
                            string typeName = string.IsNullOrEmpty(slab.TypeName) ? "(no type)" : slab.TypeName;
                            int already;
                            measuredByType.TryGetValue(typeName, out already);
                            measuredByType[typeName] = already + 1;
                        }
                    }
                }
            }
            catch (ImportStoppedException)
            {
                // The dump is a file, opened above and closed at the end of this method - and
                // a read that stops never reaches the end of this method. Closed here so a
                // stopped read leaves a readable file behind rather than an open handle.
                GeometryDump.Stop();
                throw;
            }

            reportSketchFloors();

            if (inSeveralPieces > 0)
            {
                // Worth saying out loud, because until now only the largest piece of one of
                // these was checked and the rest of the floor read as concrete nobody had
                // modelled. That is the whole mesh coming back one colour with only the drop
                // panels found on it.
                addIssue($"{inSeveralPieces} floor(s) cover more than one separate region - a shape " +
                         "edited floor, or one with an opening reaching its edge - and every region " +
                         "of each is checked.");
            }

            if (ignored > 0)
            {
                // One line rather than one per floor: on a real model this is hundreds of
                // elements, and the useful fact is the count and the words that did it.
                addIssue($"{ignored} floor(s) left out of the slab check because their type name " +
                         $"contains {string.Join(" or ", ignoredNames.Select(name => $"'{name}'"))}.");
            }

            if (measuredByType.Count > 0)
            {
                int floors = measuredByType.Values.Sum();
                string worst = string.Join(", ", measuredByType
                    .OrderByDescending(pair => pair.Value)
                    .Take(5)
                    .Select(pair => $"{pair.Key} ({pair.Value})"));

                addIssue($"{floors} floor(s) across {measuredByType.Count} type(s) declare no thickness " +
                         "on their type, so they were measured instead - and their faces are not " +
                         "parallel, so they have no single thickness and come out as review rather " +
                         $"than as right or wrong. Give these types a structural layer to settle them: {worst}");
            }

            if (GeometryDump.IsOpen)
            {
                int faults = GeometryDump.Entries;
                string where = GeometryDump.FilePath;
                GeometryDump.Stop();

                addIssue(faults == 0
                    ? "The geometry dump recorded nothing: every floor was read without complaint."
                    : $"The geometry dump recorded {faults} fault(s) in {where}");
            }

            return slabsData;
        }

        /// <summary>
        /// True when the floor's name carries one of the words that keep it out of the slab
        /// check. The type name is what carries the intent in practice - a fill, a screed, a
        /// finish is named as one - so both the element's own name and its type name are
        /// looked at, and the match is anywhere in the name and ignores case.
        /// </summary>
        private bool isIgnoredByName(Element element, string[] ignoredNames)
        {
            string matched;
            return isIgnoredByName(element, ignoredNames, out matched);
        }

        /// <summary>
        /// The same test, saying which of the names it was that matched. One name answers for
        /// the element - the first to be found in its type name, or failing that the first to
        /// be found in its own name - because an element named for two of them was going to be
        /// left out either way, and one name against it is what makes the count add up.
        ///
        /// Both names are looked at because on a wall or a floor they differ and either can
        /// carry the name. On a family instance - a beam, a column - they are the same string,
        /// since an instance is named for its symbol, so the type name settles it.
        /// </summary>
        private bool isIgnoredByName(Element element, string[] ignoredNames, out string matched)
        {
            // The same match a check makes again on what was kept - see RevitLeaveOut - so
            // the cheap test comes first and the type is only looked up when there is a list.
            matched = null;

            if (ignoredNames == null || ignoredNames.Length == 0)
            {
                return false;
            }

            return RevitLeaveOut.Matches(getElementTypeName(element), element.Name, ignoredNames, out matched);
        }

        private IEnumerable<Element> collectFloors(Document doc, PrefrencesData pref)
        {
            List<Element> floors = new FilteredElementCollector(doc)
                .OfClass(typeof(Floor))
                .WherePasses(new ElementCategoryFilter(BuiltInCategory.OST_Floors))
                .ToElements()
                .ToList();

            if (pref.IncludeStructuralFoundations)
            {
                // Only the slab shaped foundations: an isolated footing is a family instance
                // and is not a floor by any reading.
                floors.AddRange(new FilteredElementCollector(doc)
                    .OfClass(typeof(Floor))
                    .WherePasses(new ElementCategoryFilter(BuiltInCategory.OST_StructuralFoundation))
                    .ToElements());
            }

            return floors;
        }

        private Slab_R readSlab(PlacedElement placed, PrefrencesData pref, bool structuralOrNot = false)
        {
            Element element = placed.Element;
            try
            {
                bool isStructural = readIsStructural(element);
                if (pref.StructuralFloorsOnly && !isStructural && !structuralOrNot)
                {
                    return null;
                }

                Solid solid = getLargestSolid(element);
                if (solid == null)
                {
                    string why = whyNoSolid(element);
                    if (pref.FloorSketchFallback)
                    {
                        // Says for itself what it did, read or not.
                        return readSlabFromSketch(placed, why, isStructural);
                    }
                    addIssue($"{element.Id}: '{element.Name}' has no solid geometry ({why}), left out of the slab check");
                    return null;
                }

                Transform documentTransform = placed.Placement.Transform;

                List<List<List<MyPoint>>> topFaces = new List<List<List<MyPoint>>>();
                List<List<MyPoint>> bottomLoops = new List<List<MyPoint>>();
                FaceSplit split = readHorizontalFaceLoops(solid, documentTransform, topFaces, bottomLoops);

                List<List<MyPoint>> topLoops = topFaces.SelectMany(face => face).ToList();

                if (topLoops.Count == 0 || bottomLoops.Count == 0)
                {
                    addIssue($"{element.Id}: '{element.Name}' has no face facing up or none facing down, " +
                             $"left out of the slab check - {split}");
                    return null;
                }

                // Says for itself why, when it cannot build one.
                IList<PlanPolygon> parts = buildFloorPlan(element, topFaces);
                if (parts.Count == 0)
                {
                    return null;
                }

                PlanPolygon plan = parts.OrderByDescending(part => part.Area).First();

                double topHigh = topLoops.SelectMany(loop => loop).Max(point => point.Z);
                double topLow = topLoops.SelectMany(loop => loop).Min(point => point.Z);
                double bottomHigh = bottomLoops.SelectMany(loop => loop).Max(point => point.Z);
                double bottomLow = bottomLoops.SelectMany(loop => loop).Min(point => point.Z);

                Slab_R slab = new Slab_R(placed.RevitId, plan, topHigh, bottomLow)
                {
                    Parts = parts,
                    TypeName = getElementTypeName(element),
                    ElementName = element.Name ?? "",
                    FamilyName = element.Category == null ? "" : element.Category.Name,
                    CategoryName = element.Category == null ? "" : element.Category.Name,
                    IsStructural = isStructural,
                    NominalThickness = readFloorTypeThickness(element)
                };

                bool typeVaries;
                slab.StructuralThickness = readStructuralThickness(element, out typeVaries);
                slab.HasVariableTypeThickness = typeVaries;

                // While the sketch fallback is on trial, every floor that does have a solid is
                // also checked against what its sketch would have given - the one way to know
                // the sketch's elevations can be trusted where there is no solid to check them by.
                if (pref.FloorSketchFallback)
                {
                    checkSketchAgainstSolid(placed, slab, topHigh - topLow <= 1d && bottomHigh - bottomLow <= 1d);
                }

                // A floor whose two faces are not both flat and level - sloped, warped, or
                // shape edited - has a depth that changes across it, so the check is told to
                // put those zones up for review rather than call them right or wrong.
                bool flat = topHigh - topLow <= 1d && bottomHigh - bottomLow <= 1d;
                slab.MinimumThickness = flat ? topHigh - bottomLow : Math.Max(0, topLow - bottomHigh);
                slab.MaximumThickness = topHigh - bottomLow;

                // A floor that reads as varying when the model says it does not is nearly
                // always a face taken for the top or the bottom that is really neither, so the
                // faces are written out one by one with which way each looks and how far it
                // reaches. The offender shows up as a face spanning the whole thickness.
                // Only where the measurement is what the check will actually use. A ramp has
                // faces that are parallel but not level, so it always measures as varying, and
                // where its type declares a structural thickness that measurement is never
                // read - the type answers. Writing those out anyway put a hundred and nine
                // notes in the dump that nothing was ever going to act on, and buried the three
                // dozen entries that mattered underneath them.
                if (slab.HasVariableThickness && slab.ComparedThicknessVaries)
                {
                    List<string> detail = new List<string>
                    {
                        $"type            : {slab.TypeName}",
                        $"type thickness  : {slab.StructuralThickness} mm structural, " +
                        $"{slab.NominalThickness} mm overall",
                        $"measured        : {Math.Round(slab.MinimumThickness, 1)} .. " +
                        $"{Math.Round(slab.MaximumThickness, 1)} mm  -> reads as varying",
                        $"top faces       : z {Math.Round(topLow, 1)} .. {Math.Round(topHigh, 1)} mm",
                        $"bottom faces    : z {Math.Round(bottomLow, 1)} .. {Math.Round(bottomHigh, 1)} mm",
                        $"faces           : {split}",
                        "each face (a top or bottom face spanning the whole thickness is the fault):"
                    };

                    foreach (FaceRead read in split.Faces)
                    {
                        detail.Add("    " + read);
                    }

                    GeometryDump.Note($"Revit floor {element.Id} ({slab.TypeName})",
                                      "measuring its thickness between its two faces", detail);
                }

                return slab;
            }
            catch (Exception e)
            {
                addIssue($"{element.Id}: cannot read the floor geometry - {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// The plan region of the floor. A flat floor has one face looking up, and its loops
        /// are used exactly as Revit gave them; a shape edited one has a facet per slope, and
        /// those are put back together into the single region they cover.
        /// </summary>
        private IList<PlanPolygon> buildFloorPlan(Element element, List<List<List<MyPoint>>> topFaces)
        {
            // One region per face looking up. Whose loop is the outline and whose are the
            // openings is only ever asked within a single face, because that is the only
            // place the question has an answer: the loops of one face really are an outline
            // with its holes in it, while the loops of two different faces are two pieces of
            // slab lying side by side. Reading a whole shape edited floor as one flat list
            // made every facet but the largest into a hole in that largest one, and a hole
            // that runs along the edge of its own outline is a shape the kernel refuses -
            // which is where the floors that could not be built were coming from.
            List<PlanPolygon> faces = new List<PlanPolygon>();

            // Loops are classified the way SRV does it: by which way they wind. A loop that
            // runs counter clockwise seen from above encloses slab, one that runs clockwise
            // is a hole in whatever encloses it. Revit winds its face loops that way already,
            // so this reads what the model says rather than guessing from area - and it is
            // what lets one face carry two separate pieces of slab, which taking the largest
            // ring as the boundary can never see.
            foreach (List<List<MyPoint>> loops in topFaces)
            {
                // Any loop that touches itself is cut into the simple rings it is really
                // made of first. Revit hands back an opening that reaches the slab edge as
                // part of the outer loop, joined to it by a slit of no width, and a boundary
                // like that is one the kernel will not accept - which is what was throwing
                // out the largest floors in the model, the ones with the most openings in
                // them and so the most chances to have one touch an edge.
                List<PlanRing> rings = new List<PlanRing>();

                foreach (List<MyPoint> loop in loops)
                {
                    PlanRing ring = PlanRing.FromPoints(loop);
                    if (ring == null)
                    {
                        continue;
                    }

                    foreach (PlanRing piece in ring.SplitAtRepeatedPoints())
                    {
                        if (piece != null)
                        {
                            rings.Add(piece);
                        }
                    }
                }

                if (rings.Count == 0)
                {
                    continue;
                }

                foreach (PlanPolygon region in RegionsFromRings(rings))
                {
                    faces.Add(region);
                }
            }

            if (faces.Count == 0)
            {
                addIssue($"{element.Id}: '{element.Name}' has a top face that does not enclose an area, " +
                         "left out of the slab check");
                return new List<PlanPolygon>();
            }

            // Every region the floor covers, kept apart.
            //
            // These used to be merged into one outline with the geometry kernel, and that is
            // what emptied the largest floors in the model out of the check. The merge had to
            // extrude each region into a prism first, and a storey slab with sixty or a hundred
            // openings in it is exactly the outline the kernel turns down - so the region was
            // dropped, on the reasoning that one facet of a shape edited floor is no great loss.
            // The regions being dropped were not facets. They were whole floors: 9721 m2,
            // 9213 m2, 8016 m2, one per storey, every one of them the main slab. What was left
            // was the drop panels, which are small and simple and always built, and the check
            // duly reported a building with nothing but drop panels in it.
            //
            // Nothing downstream wants them merged. A floor is asked whether it covers a point
            // and cut against the ETABS areas by triangulation, and regions are free to overlap
            // in both. So the merge is gone, and with it the last thing on the Revit side of
            // the import that could refuse.
            return validated(element, faces);
        }

        /// <summary>
        /// Rings sorted into regions the way SRV sorts them: every counter clockwise ring
        /// seeds a region of its own, and every clockwise ring is a hole attached to whichever
        /// region encloses it.
        ///
        /// The winding is the model's own answer to which is which, and it carries information
        /// that area does not. A face holding two separate pieces of slab gives two counter
        /// clockwise rings, and reading the larger as the boundary and the other as a hole in
        /// it - which is what taking the largest ring does - turns a second piece of slab into
        /// a void in the first. A ring wound clockwise with nothing around it is dropped: it is
        /// a hole in nothing, and keeping it would invert the region.
        /// </summary>
        private static IEnumerable<PlanPolygon> RegionsFromRings(IList<PlanRing> rings)
        {
            List<PlanRing> outers = rings.Where(ring => ring.IsCounterClockwise).ToList();
            List<PlanRing> holes = rings.Where(ring => !ring.IsCounterClockwise).ToList();

            if (outers.Count == 0)
            {
                // Nothing wound the right way round, so the model's winding cannot be relied
                // on here and the largest ring is taken as the boundary instead.
                PlanRing biggest = rings.OrderByDescending(ring => ring.Area).FirstOrDefault();
                if (biggest != null)
                {
                    PlanPolygon only = PlanPolygon.Create(
                        biggest, rings.Where(ring => ring != biggest && biggest.Surrounds(ring)));
                    if (only != null)
                    {
                        yield return only;
                    }
                }
                yield break;
            }

            // Smallest first, so a hole inside two nested boundaries is given to the inner one.
            List<PlanRing> byArea = outers.OrderBy(ring => ring.Area).ToList();
            Dictionary<PlanRing, List<PlanRing>> attached = new Dictionary<PlanRing, List<PlanRing>>();

            foreach (PlanRing outer in byArea)
            {
                attached[outer] = new List<PlanRing>();
            }

            foreach (PlanRing hole in holes)
            {
                foreach (PlanRing outer in byArea)
                {
                    if (outer.Surrounds(hole))
                    {
                        attached[outer].Add(hole);
                        break;
                    }
                }
                // A hole no boundary encloses is dropped, exactly as SRV drops it.
            }

            foreach (PlanRing outer in byArea)
            {
                PlanPolygon region = PlanPolygon.Create(outer, attached[outer]);
                if (region != null)
                {
                    yield return region;
                }
            }
        }

        /// <summary>
        /// The region if the geometry kernel will accept it, and the best thing left if it
        /// will not.
        ///
        /// Asked once here rather than left to be discovered later, because a region the
        /// kernel refuses does not fail quietly: it throws on every comparison it takes part
        /// in, and one bad floor fills the whole run with the same message. Where the outline
        /// on its own is sound, that is used and the openings are given up, which is a floor
        /// checked slightly wrong rather than a floor that breaks everything near it.
        /// </summary>
        private IList<PlanPolygon> validated(Element element, IList<PlanPolygon> regions)
        {
            // The outline is only recorded here, never rejected on it.
            //
            // This used to ask whether the geometry kernel could extrude the outline into a
            // prism, and threw the openings away - or the whole floor - when it could not. The
            // slab check no longer puts a Revit floor through the kernel at all: a floor is
            // asked whether it covers a point, which is arithmetic, and cut against the ETABS
            // areas by triangulation, which cannot refuse. So an outline the kernel dislikes is
            // now only worth writing down, and the floors that were being lost to it - the big
            // ones, with the most openings and so the most chances to have one touch an edge -
            // are checked like any other.
            foreach (PlanPolygon region in regions)
            {
                string reason;
                if (!PlanRegionMath.CanBuild(region, out reason))
                {
                    GeometryDump.Fault($"Revit floor {element.Id} ({getElementTypeName(element)})",
                                       "turning its plan outline into a prism, which is no longer " +
                                       "asked of it", reason, region);
                }
            }

            return regions;
        }

        /// <summary>
        /// Splits the solid's horizontal faces into the ones looking up and the ones looking
        /// down, and fills in their loops in the shared coordinate system, in mm. A floor cut
        /// by an opening has that opening as an inner loop of its top face, so the openings
        /// arrive with the outline and need no pass of their own.
        ///
        /// Returns how many faces were found looking up, which is one for an ordinary floor
        /// and one per facet for a shape edited one.
        /// </summary>
        private FaceSplit readHorizontalFaceLoops(Solid solid, Transform documentTransform,
                                                  List<List<List<MyPoint>>> topFaces,
                                                  List<List<MyPoint>> bottomLoops)
        {
            FaceSplit split = new FaceSplit();

            foreach (Face face in solid.Faces)
            {
                split.TotalFaces++;

                XYZ normal = readFaceNormal(face);
                if (normal == null)
                {
                    split.UnreadableFaces++;
                    continue;
                }

                if (documentTransform != null && documentTransform.IsConformal)
                {
                    normal = documentTransform.OfVector(normal);
                }

                List<List<MyPoint>> loops = readFaceLoops(face, documentTransform);

                FaceRead read = new FaceRead
                {
                    NormalZ = Math.Round(normal.Z, 4),
                    Planar = face is PlanarFace,
                    MinZ = loops.Count == 0 ? 0 : loops.SelectMany(l => l).Min(pt => pt.Z),
                    MaxZ = loops.Count == 0 ? 0 : loops.SelectMany(l => l).Max(pt => pt.Z)
                };
                split.Faces.Add(read);

                if (normal.Z > HorizontalFaceFloor)
                {
                    read.Kind = "up";
                    split.UpwardFaces++;

                    // Kept as this face's own group. Which loop is the outline and which are
                    // the openings only means anything inside one face.
                    if (loops.Count > 0)
                    {
                        topFaces.Add(loops);
                    }
                }
                else if (normal.Z < -HorizontalFaceFloor)
                {
                    read.Kind = "down";
                    split.DownwardFaces++;
                    bottomLoops.AddRange(loops);
                }
                else
                {
                    read.Kind = "sideways";
                    split.SidewaysFaces++;
                }
            }

            return split;
        }

        /// <summary>
        /// How far from vertical a face has to lean before it counts as the top or the bottom
        /// of a floor rather than its edge.
        ///
        /// This used to be a hundredth, which is a face leaning less than a degree off
        /// vertical - a slab edge in all but name. An edge taken for the top brings the whole
        /// side of the slab with it, so the lowest point of the "top" became the bottom of the
        /// floor, the two faces read as far apart at one place and touching at another, and a
        /// floor of one constant thickness was reported as varying. A half admits a ramp up to
        /// sixty degrees, which is steeper than anything anyone builds, and admits no edge.
        /// </summary>
        private const double HorizontalFaceFloor = 0.5d;

        /// <summary>
        /// What the faces of one floor turned out to be, kept so a floor that could not be
        /// read can say why rather than only that it could not.
        /// </summary>
        private sealed class FaceRead
        {
            public double NormalZ;
            public double MinZ;
            public double MaxZ;
            public string Kind = "unreadable";
            public bool Planar;

            public override string ToString()
            {
                return $"{Kind,-9} nz={NormalZ,8} z {Math.Round(MinZ, 1)}..{Math.Round(MaxZ, 1)} mm" +
                       (Planar ? "" : "  (not planar)");
            }
        }

        private sealed class FaceSplit
        {
            public int TotalFaces;
            public int UpwardFaces;
            public int DownwardFaces;
            public int SidewaysFaces;
            public int UnreadableFaces;
            public readonly List<FaceRead> Faces = new List<FaceRead>();

            public override string ToString()
            {
                return $"{TotalFaces} face(s): {UpwardFaces} up, {DownwardFaces} down, " +
                       $"{SidewaysFaces} sideways, {UnreadableFaces} whose direction could not be read";
            }
        }

        /// <summary>
        /// Which way a face generally looks, or null when that cannot be worked out.
        ///
        /// A flat face answers this outright. A warped one does not have a single normal at
        /// all, so it is sampled across its face and averaged - and warped faces are what this
        /// is really for. A floor given a variable thickness layer, a fall to a drain, or a
        /// ramp built by editing the slab shape comes back from Revit with surfaces that are
        /// ruled rather than planar, and reading only the planar ones left those floors with
        /// no top face and no bottom face, so the whole floor dropped out of the check.
        /// </summary>
        private XYZ readFaceNormal(Face face)
        {
            PlanarFace planar = face as PlanarFace;
            if (planar != null)
            {
                return planar.FaceNormal;
            }

            try
            {
                BoundingBoxUV domain = face.GetBoundingBox();
                if (domain == null)
                {
                    return null;
                }

                double x = 0, y = 0, z = 0;
                int taken = 0;

                // A grid inside the face rather than one point at its middle: the middle of
                // the parameter box of an L shaped or a holed face can fall outside the face
                // itself, and one badly placed sample would decide the whole floor.
                for (int i = 1; i <= 3; i++)
                {
                    for (int j = 1; j <= 3; j++)
                    {
                        UV at = new UV(domain.Min.U + (domain.Max.U - domain.Min.U) * i / 4d,
                                       domain.Min.V + (domain.Max.V - domain.Min.V) * j / 4d);

                        XYZ sampled;
                        try
                        {
                            sampled = face.ComputeNormal(at);
                        }
                        catch (Exception)
                        {
                            continue;
                        }

                        if (sampled == null)
                        {
                            continue;
                        }

                        x += sampled.X;
                        y += sampled.Y;
                        z += sampled.Z;
                        taken++;
                    }
                }

                if (taken == 0)
                {
                    return null;
                }

                XYZ average = new XYZ(x / taken, y / taken, z / taken);
                return average.GetLength() < 1e-9 ? null : average.Normalize();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private List<List<MyPoint>> readFaceLoops(Face face, Transform documentTransform)
        {
            List<List<MyPoint>> loops = new List<List<MyPoint>>();

            foreach (CurveLoop loop in face.GetEdgesAsCurveLoops())
            {
                List<MyPoint> points = new List<MyPoint>();
                foreach (Curve curve in loop)
                {
                    // Tessellating rather than reading the two ends keeps a curved slab edge
                    // as the shape it is, which ETABS can only have drawn as a chain of
                    // straight segments anyway.
                    IList<XYZ> vertices = curve.Tessellate();
                    for (int i = 0; i < vertices.Count - 1; i++)
                    {
                        points.Add(toSharedPoint(vertices[i], documentTransform));
                    }
                }

                if (points.Count >= 3)
                {
                    // A mirrored link turns every loop the other way round, and which way a
                    // loop runs is how a floor's outline is told from its openings - see
                    // RegionsFromRings. Left as it came, a mirrored floor was read as its
                    // openings with the slab thrown away, so it is put back the way it was drawn.
                    if (documentTransform != null && documentTransform.IsConformal && documentTransform.HasReflection)
                    {
                        points.Reverse();
                    }

                    loops.Add(points);
                }
            }

            return loops;
        }

        /// <summary>
        /// A Revit point brought into the shared (ETABS) coordinate system, in mm: out of the
        /// link it was drawn in first, then through the model alignment.
        /// </summary>
        private MyPoint toSharedPoint(XYZ point, Transform documentTransform)
        {
            XYZ placed = documentTransform != null && documentTransform.IsConformal
                ? documentTransform.OfPoint(point)
                : point;

            MyPoint inMillimetres = new MyPoint(placed.X * 304.8d, placed.Y * 304.8d, placed.Z * 304.8d);
            return DataContainer.ModelTransformation.ApplyToPointInMM(inMillimetres);
        }

        #region floors read from their sketch

        /// <summary>Floors read off their sketch in this import, for the closing count.</summary>
        private int floorsFromSketch;

        /// <summary>Floors with a solid whose sketch reading was checked against it, and how many agreed.</summary>
        private int sketchChecked;
        private int sketchAgreed;
        private readonly List<string> sketchDisagreed = new List<string>();

        /// <summary>A sketch's elevations agree with the solid's within this, in mm.</summary>
        private const double SketchAgreementMM = 5;

        /// <summary>
        /// Why Revit gave no solid for an element, as exactly as can be said: no geometry at
        /// all, geometry with no solid in it, or solids of no volume - and what it is joined
        /// to or cut by, which is the usual reason a floor's solid is consumed.
        /// </summary>
        private static string whyNoSolid(Element element)
        {
            string what;
            try
            {
                Options options = new Options
                {
                    DetailLevel = ViewDetailLevel.Fine,
                    ComputeReferences = false,
                    IncludeNonVisibleObjects = false
                };
                GeometryElement geometry = element.get_Geometry(options);
                if (geometry == null)
                {
                    what = "Revit returned no geometry for it";
                }
                else
                {
                    int solids = 0, empty = 0, other = 0;
                    countGeometry(geometry, ref solids, ref empty, ref other);
                    what = solids + empty == 0
                        ? $"its geometry holds no solid, only {other} other object(s)"
                        : $"its {solids + empty} solid(s) have no volume";
                }
            }
            catch (Exception e)
            {
                what = "reading its geometry failed: " + e.Message;
            }

            try
            {
                int joined = JoinGeometryUtils.GetJoinedElements(element.Document, element).Count;
                if (joined > 0)
                {
                    what += $", joined to {joined} element(s)";
                }
                int voids = InstanceVoidCutUtils.GetCuttingVoidInstances(element).Count;
                if (voids > 0)
                {
                    what += $", cut by {voids} void(s)";
                }
            }
            catch (Exception)
            {
                // What it is joined to only adds to the reason; the reason stands without it.
            }

            return what;
        }

        private static void countGeometry(GeometryElement geometry, ref int solids, ref int empty, ref int other)
        {
            foreach (GeometryObject item in geometry)
            {
                Solid solid = item as Solid;
                if (solid != null)
                {
                    if (solid.Volume > 1e-6 && solid.Faces.Size > 0)
                    {
                        solids++;
                    }
                    else
                    {
                        empty++;
                    }
                    continue;
                }

                GeometryInstance nested = item as GeometryInstance;
                if (nested != null)
                {
                    countGeometry(nested.GetInstanceGeometry(), ref solids, ref empty, ref other);
                    continue;
                }

                other++;
            }
        }

        /// <summary>
        /// Why a floor cannot be read off its sketch, or null when it can. Only a flat floor
        /// can: a sketch is drawn flat, so a floor sloped by an arrow or a sloping edge, or
        /// shape edited, would be read at one level where it is at many.
        /// </summary>
        private static string sketchRefusal(Floor floor, out Sketch sketch)
        {
            sketch = null;
            Document doc = floor.Document;

            try
            {
                SlabShapeEditor editor = floor.GetSlabShapeEditor();
                if (editor != null && editor.IsEnabled)
                {
                    return "it is shape edited";
                }
            }
            catch (Exception)
            {
                // A floor that cannot say is taken as not shape edited; the slope test follows.
            }

            sketch = doc.GetElement(floor.SketchId) as Sketch;
            if (sketch == null)
            {
                return "it has no sketch";
            }

            foreach (ElementId id in sketch.GetAllElements())
            {
                Element part = doc.GetElement(id);
                if (part == null)
                {
                    continue;
                }
                if (part.get_Parameter(BuiltInParameter.SLOPE_START_HEIGHT) != null
                    || part.get_Parameter(BuiltInParameter.SLOPE_END_HEIGHT) != null)
                {
                    return "it is sloped by a slope arrow";
                }
                Parameter definesSlope = part.LookupParameter("Defines Slope");
                if (definesSlope != null && definesSlope.StorageType == StorageType.Integer && definesSlope.AsInteger() == 1)
                {
                    return "it has an edge that defines a slope";
                }
            }

            if (sketch.Profile == null || sketch.Profile.Size == 0)
            {
                return "its sketch has no boundary";
            }

            return null;
        }

        /// <summary>
        /// Top of the floor's concrete as the sketch places it - its level plus its height
        /// offset - in feet, in its own document; null when it has no level.
        /// </summary>
        private static double? sketchTopFeet(Floor floor)
        {
            Level level = floor.Document.GetElement(floor.LevelId) as Level;
            if (level == null)
            {
                return null;
            }

            Parameter offset = floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
            return level.Elevation + (offset != null && offset.HasValue ? offset.AsDouble() : 0);
        }

        /// <summary>The loops of a sketch, tessellated, in the shared frame in mm.</summary>
        private List<IList<MyPoint>> sketchLoops(Sketch sketch, Transform documentTransform)
        {
            List<IList<MyPoint>> loops = new List<IList<MyPoint>>();
            foreach (CurveArray loop in sketch.Profile)
            {
                loops.Add(curvePoints(loop.Cast<Curve>(), documentTransform));
            }
            return loops;
        }

        private List<MyPoint> curvePoints(IEnumerable<Curve> curves, Transform documentTransform)
        {
            List<MyPoint> points = new List<MyPoint>();
            foreach (Curve curve in curves)
            {
                IList<XYZ> vertices = curve.Tessellate();
                for (int i = 0; i < vertices.Count - 1; i++)
                {
                    points.Add(toSharedPoint(vertices[i], documentTransform));
                }
            }
            return points;
        }

        /// <summary>
        /// The outlines of the openings that cut this floor and that its sketch does not show:
        /// the openings hosted by it, and the shafts whose height takes in its depth. Anything
        /// else that cuts it - a void family - cannot be outlined here, and is counted instead.
        /// </summary>
        private List<IList<MyPoint>> openingOutlines(Floor floor, double topFeet, double bottomFeet,
                                                     Transform documentTransform)
        {
            List<IList<MyPoint>> outlines = new List<IList<MyPoint>>();

            foreach (Opening opening in new FilteredElementCollector(floor.Document).OfClass(typeof(Opening)).Cast<Opening>())
            {
                bool hosted = opening.Host != null && opening.Host.Id == floor.Id;
                if (!hosted)
                {
                    // A shaft has no host: it cuts whatever its height passes through.
                    if (opening.Host != null)
                    {
                        continue;
                    }
                    BoundingBoxXYZ box = opening.get_BoundingBox(null);
                    if (box == null || box.Min.Z > topFeet || box.Max.Z < bottomFeet)
                    {
                        continue;
                    }
                }

                List<MyPoint> outline;
                if (opening.IsRectBoundary && opening.BoundaryRect != null && opening.BoundaryRect.Count >= 2)
                {
                    XYZ a = opening.BoundaryRect[0], b = opening.BoundaryRect[1];
                    outline = new List<MyPoint>
                    {
                        toSharedPoint(new XYZ(a.X, a.Y, topFeet), documentTransform),
                        toSharedPoint(new XYZ(b.X, a.Y, topFeet), documentTransform),
                        toSharedPoint(new XYZ(b.X, b.Y, topFeet), documentTransform),
                        toSharedPoint(new XYZ(a.X, b.Y, topFeet), documentTransform)
                    };
                }
                else if (opening.BoundaryCurves != null)
                {
                    outline = curvePoints(opening.BoundaryCurves.Cast<Curve>(), documentTransform);
                }
                else
                {
                    continue;
                }

                if (outline.Count >= 3)
                {
                    outlines.Add(outline);
                }
            }

            return outlines;
        }

        /// <summary>
        /// A floor Revit gave no solid for, read off its sketch instead: the outline from the
        /// sketch, the openings cut through it taken out, the thickness from the type's
        /// structural layers, and the top at its level plus its offset. Only a flat floor, and
        /// only when <see cref="PrefrencesData.FloorSketchFallback"/> is on. Anything cutting it
        /// that cannot be taken out of the sketch puts it up for review - its zones are never
        /// passed or failed - rather than read as slab where there may be none.
        /// </summary>
        private Slab_R readSlabFromSketch(PlacedElement placed, string whyNoSolidText, bool isStructural)
        {
            Element element = placed.Element;
            string head = $"{element.Id}: '{element.Name}' has no solid geometry ({whyNoSolidText})";

            Floor floor = element as Floor;
            if (floor == null)
            {
                addIssue(head + "; it is not a floor, so it is not read from a sketch - left out of the slab check");
                return null;
            }

            Sketch sketch;
            string refusal = sketchRefusal(floor, out sketch);
            double? topFeet = refusal == null ? sketchTopFeet(floor) : null;
            if (refusal == null && topFeet == null)
            {
                refusal = "it has no level";
            }

            bool varies;
            double structural = readStructuralThickness(floor, out varies);
            double nominal = readFloorTypeThickness(floor);
            double depthMM = nominal > 0 ? nominal : structural;
            if (refusal == null && depthMM <= 0)
            {
                refusal = "its type gives no thickness";
            }

            if (refusal != null)
            {
                addIssue(head + $"; not read from its sketch because {refusal} - left out of the slab check");
                return null;
            }

            Transform documentTransform = placed.Placement.Transform;
            List<PlanPolygon> regions = SketchFloor.Regions(sketchLoops(sketch, documentTransform));
            if (regions.Count == 0)
            {
                addIssue(head + "; its sketch makes no closed outline - left out of the slab check");
                return null;
            }

            double bottomFeet = topFeet.Value - depthMM / 304.8d;
            int cut, notCut;
            regions = SketchFloor.CutOpenings(regions, openingOutlines(floor, topFeet.Value, bottomFeet, documentTransform),
                                              out cut, out notCut);

            int voids = 0;
            try
            {
                voids = InstanceVoidCutUtils.GetCuttingVoidInstances(floor).Count;
            }
            catch (Exception)
            {
                voids = 0;
            }
            bool uncertain = notCut > 0 || voids > 0;

            // The top through the same conversion every other point goes through, so a link's
            // placement and the alignment's height move it exactly as they move a solid's top.
            XYZ anchor = sketch.Profile.get_Item(0).get_Item(0).GetEndPoint(0);
            double top = toSharedPoint(new XYZ(anchor.X, anchor.Y, topFeet.Value), documentTransform).Z;
            double bottom = top - depthMM;

            PlanPolygon plan = regions.OrderByDescending(region => region.Area).First();
            Slab_R slab = new Slab_R(placed.RevitId, plan, top, bottom)
            {
                Parts = regions,
                TypeName = getElementTypeName(element),
                ElementName = element.Name ?? "",
                FamilyName = element.Category == null ? "" : element.Category.Name,
                CategoryName = element.Category == null ? "" : element.Category.Name,
                IsStructural = isStructural,
                NominalThickness = nominal,
                StructuralThickness = structural,
                HasVariableTypeThickness = varies,
                GeometrySource = Slab_R.FromSketch,
                OpeningsUncertain = uncertain
            };

            floorsFromSketch++;
            double area = regions.Sum(region => region.Area) / 1e6;
            string openings = $"{cut} opening(s) cut out of it"
                              + (notCut > 0 ? $", {notCut} that could not be cut out" : "")
                              + (voids > 0 ? $", and cut by {voids} void(s) its sketch does not show" : "");
            addIssue(head + $"; read from its sketch instead: {Math.Round(area, 2)} m2 in {regions.Count} region(s), "
                     + $"top {Math.Round(top)} mm, bottom {Math.Round(bottom)} mm, {Math.Round(structural)} mm structural "
                     + $"({Math.Round(nominal)} mm overall); {openings}"
                     + (uncertain ? " - so its zones are put up for review rather than passed or failed." : "."));
            return slab;
        }

        /// <summary>
        /// For a flat floor that does have a solid: the top and bottom its sketch would have
        /// given, against the solid's. Counted for the import report; nothing is changed.
        /// </summary>
        private void checkSketchAgainstSolid(PlacedElement placed, Slab_R slab, bool flat)
        {
            Floor floor = placed.Element as Floor;
            if (floor == null || !flat)
            {
                return;
            }

            try
            {
                Sketch sketch;
                if (sketchRefusal(floor, out sketch) != null)
                {
                    return;
                }
                double? topFeet = sketchTopFeet(floor);
                bool varies;
                double nominal = readFloorTypeThickness(floor);
                double depthMM = nominal > 0 ? nominal : readStructuralThickness(floor, out varies);
                if (topFeet == null || depthMM <= 0)
                {
                    return;
                }

                XYZ anchor = sketch.Profile.get_Item(0).get_Item(0).GetEndPoint(0);
                double top = toSharedPoint(new XYZ(anchor.X, anchor.Y, topFeet.Value), placed.Placement.Transform).Z;
                double bottom = top - depthMM;

                sketchChecked++;
                if (Math.Abs(top - slab.TopElevation) <= SketchAgreementMM
                    && Math.Abs(bottom - slab.BottomElevation) <= SketchAgreementMM)
                {
                    sketchAgreed++;
                }
                else if (sketchDisagreed.Count < 10)
                {
                    sketchDisagreed.Add($"{floor.Id} (sketch {Math.Round(top)}/{Math.Round(bottom)}, "
                                        + $"solid {Math.Round(slab.TopElevation)}/{Math.Round(slab.BottomElevation)})");
                }
            }
            catch (Exception)
            {
                // A check of the fallback; a floor it cannot check is left out of the count.
            }
        }

        private void reportSketchFloors()
        {
            if (floorsFromSketch > 0)
            {
                addIssue($"{floorsFromSketch} floor(s) had no solid and were read from their sketch instead "
                         + "(Settings > Slab check > Read floors with no solid from their sketch).");
            }

            if (sketchChecked > 0)
            {
                addIssue($"Sketch check: on {sketchAgreed} of {sketchChecked} flat floor(s) that do have a solid, the "
                         + $"top and bottom read from the sketch agree with the solid within {SketchAgreementMM} mm"
                         + (sketchDisagreed.Count > 0 ? "; they differ on " + string.Join("; ", sketchDisagreed) : "")
                         + ".");
            }
        }

        #endregion

        private Solid getLargestSolid(Element element)
        {
            Options options = new Options
            {
                DetailLevel = ViewDetailLevel.Fine,
                ComputeReferences = false,
                IncludeNonVisibleObjects = false
            };

            GeometryElement geometry = element.get_Geometry(options);
            if (geometry == null)
            {
                return null;
            }

            Solid largest = null;
            foreach (Solid solid in enumerateSolids(geometry))
            {
                if (largest == null || solid.Volume > largest.Volume)
                {
                    largest = solid;
                }
            }

            return largest;
        }

        private static IEnumerable<Solid> enumerateSolids(GeometryElement geometry)
        {
            foreach (GeometryObject item in geometry)
            {
                Solid solid = item as Solid;
                if (solid != null && solid.Volume > 1e-6 && solid.Faces.Size > 0)
                {
                    yield return solid;
                    continue;
                }

                GeometryInstance instance = item as GeometryInstance;
                if (instance != null)
                {
                    foreach (Solid nested in enumerateSolids(instance.GetInstanceGeometry()))
                    {
                        yield return nested;
                    }
                }
            }
        }

        private static bool readIsStructural(Element element)
        {
            Parameter parameter = element.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL);
            return parameter != null && parameter.HasValue && parameter.AsInteger() == 1;
        }

        private static string getElementTypeName(Element element)
        {
            ElementType type = element.Document.GetElement(element.GetTypeId()) as ElementType;
            return type == null ? element.Name ?? "" : type.Name ?? "";
        }

        /// <summary>
        /// The thickness the floor type is authored at, in mm. Reported next to the measured
        /// depth so a floor whose geometry disagrees with its own type is readable; the check
        /// itself always goes by the geometry.
        /// </summary>
        private static double readFloorTypeThickness(Element element)
        {
            try
            {
                FloorType floorType = element.Document.GetElement(element.GetTypeId()) as FloorType;
                CompoundStructure structure = floorType == null ? null : floorType.GetCompoundStructure();
                if (structure != null)
                {
                    return Math.Round(structure.GetWidth() * 304.8d);
                }
            }
            catch (Exception)
            {
                // Falls through to the parameter below.
            }

            double thicknessInFeet;
            if (tryReadLength(element, "Thickness", out thicknessInFeet))
            {
                return Math.Round(thicknessInFeet * 304.8d);
            }
            return 0;
        }

        /// <summary>
        /// The concrete the floor type declares, in mm, and whether the type says that
        /// thickness varies across the floor.
        ///
        /// Only the layers marked as structure are counted, which is what separates the slab
        /// from a screed or a finish carried in the same floor - the whole type thickness
        /// would read the lot as slab and report the floor as deeper than ETABS has it. A type
        /// that marks nothing as structure has its whole thickness taken, since a floor with
        /// no structural layer named is far more often a single concrete layer left unflagged
        /// than a floor with no concrete in it.
        ///
        /// Falls back to the floor's own thickness parameter when the type has no compound
        /// structure at all, which is how a foundation slab and a few authored families come.
        /// Returns zero when nothing can be read, and the caller then measures instead.
        /// </summary>
        private static double readStructuralThickness(Element element, out bool varies)
        {
            varies = false;

            try
            {
                HostObjAttributes hostType = element.Document.GetElement(element.GetTypeId())
                                             as HostObjAttributes;
                CompoundStructure structure = hostType == null ? null : hostType.GetCompoundStructure();

                if (structure != null)
                {
                    // A layer the type itself declares as varying is a floor that genuinely
                    // has no single thickness, as opposed to one whose faces merely slope.
                    varies = structure.VariableLayerIndex >= 0;

                    double structuralFeet = 0;
                    IList<CompoundStructureLayer> layers = structure.GetLayers();

                    if (layers != null)
                    {
                        foreach (CompoundStructureLayer layer in layers)
                        {
                            if (layer.Function == MaterialFunctionAssignment.Structure)
                            {
                                structuralFeet += layer.Width;
                            }
                        }
                    }

                    if (structuralFeet <= 0)
                    {
                        structuralFeet = structure.GetWidth();
                    }

                    if (structuralFeet > 0)
                    {
                        return Math.Round(structuralFeet * 304.8d);
                    }
                }
            }
            catch (Exception)
            {
                // Falls through to the parameters below.
            }

            try
            {
                Parameter thickness = element.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM);
                if (thickness != null && thickness.HasValue && thickness.AsDouble() > 0)
                {
                    return Math.Round(thickness.AsDouble() * 304.8d);
                }
            }
            catch (Exception)
            {
                // Falls through to the named parameter below.
            }

            double namedInFeet;
            if (tryReadLength(element, "Thickness", out namedInFeet) && namedInFeet > 0)
            {
                return Math.Round(namedInFeet * 304.8d);
            }

            return 0;
        }

        #endregion

        internal static int getNumberOfBranches(double width)
        {
            int numberOfBranches =0;
            if (width < 400)
            {
                numberOfBranches = 2;
            }
            else if (width < 600)
            {
                numberOfBranches = 4;
            }
            else if (width < 800)
            {
                numberOfBranches = 6;
            }
            else if (width < 1000)
            {
                numberOfBranches = 8;
            }
            else if (width < 1200)
            {
                numberOfBranches = 10;
            }
            else if (width < 1400)
            {
                numberOfBranches = 12;
            }
            else if (width < 1600)
            {
                numberOfBranches = 14;
            }
            else if (width < 1800)
            {
                numberOfBranches = 16;
            }
            else if (width < 2000)
            {
                numberOfBranches = 18;
            }
            return numberOfBranches;
        }

        #region the loading plan

        /// <summary>
        /// Every view in the project a loading plan could have been drawn in: the plan views
        /// that are not templates. Sorted by name, because that is how anyone looking for one
        /// will look for it.
        ///
        /// Plan views only, and that is not a shortcut. A filled region sketched in a plan
        /// view sits on the view's own plane, so its boundary comes back in model coordinates
        /// and lands where the slab is. A drafting view is not in model space at all - its
        /// coordinates are wherever the region was drawn on the sheet - so a loading plan
        /// drawn in one would produce a coverage that overlaps the model nowhere, or worse
        /// somewhere plausible, with nothing anywhere to say it had gone wrong.
        ///
        /// Read from the host document alone. A loading plan is a view, and a view inside a
        /// linked file is not reachable from here whatever else the link brings with it.
        /// </summary>
        public IList<LoadingPlanView> getLoadingPlanViews()
        {
            List<LoadingPlanView> views = new List<LoadingPlanView>();

            try
            {
                Document doc = commandData.Application.ActiveUIDocument.Document;

                foreach (Autodesk.Revit.DB.View view in new FilteredElementCollector(doc)
                             .OfClass(typeof(Autodesk.Revit.DB.View))
                             .Cast<Autodesk.Revit.DB.View>())
                {
                    if (view.IsTemplate)
                    {
                        continue;
                    }

                    switch (view.ViewType)
                    {
                        case ViewType.FloorPlan:
                        case ViewType.CeilingPlan:
                        case ViewType.AreaPlan:
                        case ViewType.EngineeringPlan:
                            break;
                        default:
                            continue;
                    }

                    views.Add(new LoadingPlanView(view.Id.Value, view.Name, view.ViewType.ToString()));
                }
            }
            catch (Exception e)
            {
                addIssue("The list of views could not be read - " + e.Message);
            }

            return views.OrderBy(view => view.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>
        /// Every level in the project, for pairing a single level design model with the floor
        /// it stands for.
        ///
        /// The elevation comes back in millimetres as Revit gives it, before the alignment
        /// has had anything to say about it: the alignment can be changed after a level has
        /// been paired, so it is added where the level is used rather than baked in here. Read
        /// off the level rather than off the elements, because the point of picking one is to
        /// say where the floor belongs even when nothing has been imported yet.
        /// </summary>
        public IList<RevitLevel> getLevels()
        {
            List<RevitLevel> levels = new List<RevitLevel>();

            try
            {
                Document doc = commandData.Application.ActiveUIDocument.Document;

                foreach (Level level in new FilteredElementCollector(doc)
                             .OfClass(typeof(Level))
                             .Cast<Level>())
                {
                    levels.Add(new RevitLevel(level.Id.Value, level.Name, level.Elevation * 304.8d,
                                              level.ProjectElevation * 304.8d));
                }
            }
            catch (Exception e)
            {
                addIssue("The list of levels could not be read - " + e.Message);
            }

            return levels.OrderBy(level => level.ElevationMM).ToList();
        }

        /// <summary>
        /// The filled regions drawn in one view, each with the area load number written on it.
        ///
        /// A filled region is view specific, so the view is the whole of the filter: what is
        /// wanted is what is drawn on this loading plan, not every region in the project. Its
        /// boundary comes back as curve loops in the same shape as a floor's top face - an
        /// outer loop with its holes - so it goes through the same ring sorting, and a region
        /// drawn as several islands comes out as several pieces, all of them the region.
        ///
        /// The number is read off whichever of the named parameters the region carries first,
        /// instance before type, so a project that keeps it on the type reads as easily as one
        /// that keeps it on each region.
        /// </summary>
        public IList<LoadRegion_R> getLoadRegions(long viewId)
        {
            List<LoadRegion_R> regions = new List<LoadRegion_R>();

            string[] parameterNames = PrefrencesData.SplitParameterNames(
                PrefrencesData.getPrefrencesData().LoadRegionNumberParameterNames);

            if (parameterNames.Length == 0)
            {
                addIssue("No parameter name is set for the area load number, so no loading region can " +
                         "be read. Set one under Settings, in Revit parameters.");
                return regions;
            }

            Document doc = commandData.Application.ActiveUIDocument.Document;

            Autodesk.Revit.DB.View view =
                doc.GetElement(new ElementId(viewId)) as Autodesk.Revit.DB.View;
            if (view == null)
            {
                addIssue($"The loading plan view (id {viewId}) is not in this project any more, so the " +
                         "load check has nothing to read. Pick the view again.");
                return regions;
            }

            IList<Element> found;
            try
            {
                found = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(FilledRegion))
                    .ToElements();
            }
            catch (Exception e)
            {
                addIssue($"The filled regions on '{view.Name}' could not be read - {e.Message}");
                return regions;
            }

            // Named, not counted. A region that does not make it into the check used to be
            // one of "3 filled region(s) could not be read", which is no help at all when the
            // plan has four hundred of them on it: the one thing needed to go and look at it
            // is the one thing that was not said.
            List<string> unreadable = new List<string>();
            List<string> unnumbered = new List<string>();

            foreach (Element element in found)
            {
                FilledRegion region = element as FilledRegion;
                if (region == null)
                {
                    continue;
                }

                try
                {
                    LoadRegion_R read = readLoadRegion(region, parameterNames);
                    if (read == null)
                    {
                        unreadable.Add($"{region.Id} ({getElementTypeName(region)})");
                        continue;
                    }

                    if (!read.HasNumber)
                    {
                        unnumbered.Add($"{region.Id} carries " + describeNumberText(read.NumberText));
                    }

                    regions.Add(read);
                }
                catch (Exception e)
                {
                    unreadable.Add($"{region.Id} - {e.Message}");
                }
            }

            if (unreadable.Count > 0)
            {
                addIssue($"{unreadable.Count} filled region(s) on '{view.Name}' have a boundary that does " +
                         $"not enclose an area, so they are left out of the load check: {list(unreadable)}");
            }

            if (unnumbered.Count > 0)
            {
                addIssue($"{unnumbered.Count} filled region(s) on '{view.Name}' carry no area load number " +
                         $"or label, so the slab under them is put up for review " +
                         $"rather than checked: {list(unnumbered)}. The number is read from " +
                         $"{describeParameterNames(parameterNames)}, instance before type.");
            }

            return regions;
        }

        /// <summary>
        /// The line loads drawn on one view: the detail lines whose line style the settings
        /// name as a load, each with the schedule number off the end of its style name.
        ///
        /// Read off the same view the filled regions are, because that is where a loading plan
        /// puts them - one drawing carrying the area loads as regions and the line loads as
        /// lines over them. So pairing a story with its loading plan pairs it with both at
        /// once, and nothing extra has to be set up per floor.
        ///
        /// Detail lines only. A detail line is view specific, which is what makes "the lines on
        /// this loading plan" a question with an answer; a model line is in the project and
        /// would be read on every plan it showed through. A model line carrying a load style is
        /// reported rather than read, because somebody drawing one meant it as a load and it
        /// will otherwise go silently uncounted.
        ///
        /// The curve is tessellated rather than taken as its two ends, so an arc is measured as
        /// the length it really is - which matters, because the length is what the total force
        /// is worked out from.
        /// </summary>
        public IList<LineLoad_R> getLineLoads(long viewId, string story)
        {
            List<LineLoad_R> loads = new List<LineLoad_R>();

            PrefrencesData pref = PrefrencesData.getPrefrencesData();

            // Empty is not a fault here, it is the escape hatch: every detail line on the view
            // is read, so a project whose styles are named in a way nobody anticipated can be
            // looked at rather than guessed at. Said out loud, because the plan it produces is
            // covered in lines carrying no number and that has to read as deliberate.
            if (PrefrencesData.SplitParameterNames(pref.LineLoadStyleNamePatterns).Length == 0)
            {
                addIssue("No line style pattern is set, so every detail line on the loading plan is "
                         + "being read as a line load. That is the diagnostic setting: most of them "
                         + "will carry no load number and come back for review. Put the drawing's own "
                         + "wording into \"A line load is drawn in a style named\" under Settings, "
                         + "Line loads, once the dump has said what the styles are called.");
            }

            Document doc = commandData.Application.ActiveUIDocument.Document;

            Autodesk.Revit.DB.View view =
                doc.GetElement(new ElementId(viewId)) as Autodesk.Revit.DB.View;
            if (view == null)
            {
                addIssue($"The loading plan view (id {viewId}) is not in this project any more, so the "
                         + "line load check has nothing to read. Pick the view again.");
                return loads;
            }

            IList<Element> found;
            try
            {
                found = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(CurveElement))
                    .WhereElementIsNotElementType()
                    .ToElements();
            }
            catch (Exception e)
            {
                addIssue($"The detail lines on '{view.Name}' could not be read - {e.Message}");
                return loads;
            }

            List<string> unnumbered = new List<string>();
            List<string> unreadable = new List<string>();
            List<string> modelLines = new List<string>();

            foreach (Element element in found)
            {
                CurveElement curve = element as CurveElement;
                if (curve == null)
                {
                    continue;
                }

                string styleName = lineStyleName(curve);
                if (!LineLoad_R.IsLineLoadStyle(styleName, pref.LineLoadStyleNamePatterns))
                {
                    continue;
                }

                if (curve.CurveElementType != CurveElementType.DetailCurve)
                {
                    modelLines.Add($"{curve.Id} ({styleName})");
                    continue;
                }

                try
                {
                    LineLoad_R read = readLineLoad(curve, styleName, story);
                    if (read == null || !read.HasLine)
                    {
                        unreadable.Add($"{curve.Id} ({styleName})");
                        continue;
                    }

                    if (!read.HasNumber)
                    {
                        unnumbered.Add($"{curve.Id} on \"{styleName}\"");
                    }

                    loads.Add(read);
                }
                catch (Exception e)
                {
                    unreadable.Add($"{curve.Id} - {e.Message}");
                }
            }

            if (modelLines.Count > 0)
            {
                addIssue($"{modelLines.Count} line(s) on '{view.Name}' carry a line load style and are "
                         + $"model lines rather than detail lines, so they are left out: {list(modelLines)}. "
                         + "A model line shows on every view it passes through, so it cannot be read as "
                         + "belonging to this loading plan - redraw them as detail lines.");
            }

            if (unreadable.Count > 0)
            {
                addIssue($"{unreadable.Count} line load(s) on '{view.Name}' have no length in plan, so "
                         + $"there is nothing to check them against: {list(unreadable)}");
            }

            if (unnumbered.Count > 0)
            {
                addIssue($"{unnumbered.Count} line load(s) on '{view.Name}' are drawn in a style whose "
                         + $"name ends in no number, so nothing says which load they are: "
                         + $"{list(unnumbered)}. The number is the digits on the end of the line style "
                         + "name, as in \"Line Load-TL1\".");
            }

            // A line drawn twice in the same place would put its load on the frame twice.
            List<KeyValuePair<LineLoad_R, LineLoad_R>> copies;
            loads = LineLoad_R.WithoutDuplicates(loads, out copies);

            if (copies.Count > 0)
            {
                addIssue($"{copies.Count} line load(s) on '{view.Name}' are copies of another line in the "
                         + "same style at exactly the same place and length, so only one of each is "
                         + "checked: "
                         + list(copies.Select(copy => $"{copy.Key.RevitID} (copy of {copy.Value.RevitID}, "
                                                      + $"\"{copy.Key.StyleName}\")").ToList())
                         + ". Delete the copies in Revit so the plan matches what is checked.");
            }

            return loads;
        }

        /// <summary>
        /// Writes down every line in a view, whatever style it is drawn in, so a run that read
        /// no line loads at all can be explained rather than guessed at.
        ///
        /// The check reads a line as a load only when its line style name says so, which means
        /// the single most likely reason for an empty result is that the styles on the drawing
        /// are not named the way the setting expects - a different prefix, a trailing space, a
        /// different language, or the lines being model lines rather than detail lines. None of
        /// that can be seen from the plan, and none of it can be guessed at from here: the only
        /// way to know is to look at what is actually on the view.
        ///
        /// So this says nothing about what is right. It lists what is there, tallied by style
        /// name with the name printed between quotes so a stray space shows, and says for each
        /// one what the check made of it and why.
        /// </summary>
        public string dumpLinesInView(long viewId, string path)
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            string patterns = pref.LineLoadStyleNamePatterns;

            StringBuilder report = new StringBuilder();

            Document doc = commandData.Application.ActiveUIDocument.Document;

            Autodesk.Revit.DB.View view =
                doc.GetElement(new ElementId(viewId)) as Autodesk.Revit.DB.View;

            report.AppendLine("LINES ON THE LOADING PLAN");
            report.AppendLine(new string('=', 78));
            report.AppendLine("Written " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine("Project      : " + doc.Title);
            report.AppendLine("View         : " + (view == null
                ? "id " + viewId + " is not in this project any more"
                : $"{view.Name} (id {viewId}, {view.ViewType})"));
            report.AppendLine("Style must say: " + describeParameterNames(
                PrefrencesData.SplitParameterNames(patterns)));
            report.AppendLine();

            if (view == null)
            {
                return Write(path, report);
            }

            List<Element> found;
            try
            {
                found = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(CurveElement))
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .ToList();
            }
            catch (Exception e)
            {
                report.AppendLine("The lines on this view could not be read - " + e.Message);
                return Write(path, report);
            }

            DumpTally(report, found, patterns);
            DumpEveryLine(report, found, patterns);
            DumpStylesInProject(report, doc, patterns);

            return Write(path, report);
        }

        /// <summary>
        /// The tally, which is the part worth reading first: every distinct line style on the
        /// view, how many lines carry it, and what the check made of it. On a real loading plan
        /// this is a dozen rows and the answer is usually in the first of them.
        /// </summary>
        private static void DumpTally(StringBuilder report, List<Element> found, string patterns)
        {
            Dictionary<string, int[]> byStyle = new Dictionary<string, int[]>(StringComparer.Ordinal);

            foreach (Element element in found)
            {
                CurveElement curve = element as CurveElement;
                if (curve == null)
                {
                    continue;
                }

                string style = lineStyleName(curve);

                int[] counts;
                if (!byStyle.TryGetValue(style, out counts))
                {
                    counts = new int[2];
                    byStyle[style] = counts;
                }

                counts[0]++;
                if (curve.CurveElementType == CurveElementType.DetailCurve)
                {
                    counts[1]++;
                }
            }

            report.AppendLine($"WHAT IS ON THE VIEW - {found.Count} line(s), "
                              + $"{byStyle.Count} distinct style(s)");
            report.AppendLine(new string('-', 78));

            if (byStyle.Count == 0)
            {
                report.AppendLine("Nothing. This view has no lines on it at all, so it is either the "
                                  + "wrong view or the loading plan is drawn some other way.");
                report.AppendLine();
                return;
            }

            report.AppendLine(string.Format("{0,6}  {1,7}  {2,-9}  {3,-6}  {4}",
                                            "lines", "detail", "read as", "number", "style name"));

            foreach (KeyValuePair<string, int[]> entry in byStyle.OrderByDescending(e => e.Value[0]))
            {
                bool isLoad = LineLoad_R.IsLineLoadStyle(entry.Key, patterns);
                int number = LineLoad_R.NumberFromStyleName(entry.Key);

                report.AppendLine(string.Format("{0,6}  {1,7}  {2,-9}  {3,-6}  \"{4}\"",
                                                entry.Value[0],
                                                entry.Value[1],
                                                isLoad ? "LOAD" : "ignored",
                                                isLoad ? (number > 0 ? number.ToString() : "NONE") : "",
                                                entry.Key));
            }

            report.AppendLine();

            int loadStyles = byStyle.Keys.Count(style => LineLoad_R.IsLineLoadStyle(style, patterns));

            if (loadStyles == 0)
            {
                report.AppendLine("NOTHING ON THIS VIEW IS READ AS A LINE LOAD.");
                report.AppendLine("None of the style names above says any of the patterns the setting");
                report.AppendLine("names. These are the wordings that would pick out lines on this view,");
                report.AppendLine("each scored by what it would catch - put the one you want into");
                report.AppendLine("\"A line load is drawn in a style named\" under Settings, Line loads.");
                report.AppendLine();

                LineLoadStyleSuggestion.Write(
                    report, byStyle.ToDictionary(entry => entry.Key, entry => entry.Value[0]));

                report.AppendLine("A single * in that box means every line on the view, which is the way");
                report.AppendLine("to see everything at once while the wording is being settled.");
                report.AppendLine();
            }

            report.AppendLine();
        }

        /// <summary>
        /// Every line, one per row. Longer than anybody wants to read, and the point of it is
        /// that it can be searched: a line somebody can see on the drawing and cannot find in
        /// the check is looked up here by its element id.
        /// </summary>
        private void DumpEveryLine(StringBuilder report, List<Element> found, string patterns)
        {
            report.AppendLine("EVERY LINE, IN THE ORDER REVIT LISTED THEM");
            report.AppendLine(new string('-', 78));
            report.AppendLine(string.Format("{0,-10}  {1,-12}  {2,-9}  {3,8}  {4}",
                                            "id", "kind", "read as", "length", "style / ends (mm)"));

            foreach (Element element in found)
            {
                CurveElement curve = element as CurveElement;
                if (curve == null)
                {
                    continue;
                }

                string style = lineStyleName(curve);
                bool isLoad = LineLoad_R.IsLineLoadStyle(style, patterns);
                bool isDetail = curve.CurveElementType == CurveElementType.DetailCurve;

                string verdict = !isLoad
                    ? "ignored"
                    : !isDetail ? "MODEL" : "LOAD";

                string ends = "";
                string length = "";

                try
                {
                    Curve geometry = curve.GeometryCurve;
                    if (geometry != null)
                    {
                        MyPoint start = toSharedPoint(geometry.GetEndPoint(0), null);
                        MyPoint end = toSharedPoint(geometry.GetEndPoint(1), null);

                        length = Math.Round(geometry.Length * 304.8d).ToString("0");
                        ends = $"({Math.Round(start.X)}, {Math.Round(start.Y)}) to "
                               + $"({Math.Round(end.X)}, {Math.Round(end.Y)})";
                    }
                }
                catch (Exception e)
                {
                    ends = "geometry could not be read - " + e.Message;
                }

                report.AppendLine(string.Format("{0,-10}  {1,-12}  {2,-9}  {3,8}  \"{4}\"  {5}",
                                                curve.Id, curve.CurveElementType, verdict,
                                                length, style, ends));
            }

            report.AppendLine();
        }

        /// <summary>
        /// Every line style the project has, whether or not anything on this view is drawn in
        /// one. This is what answers "the styles are there, so why is the view empty" - the
        /// lines are on another view, or on no view the check was pointed at.
        /// </summary>
        private static void DumpStylesInProject(StringBuilder report, Document doc, string patterns)
        {
            report.AppendLine("EVERY LINE STYLE IN THE PROJECT");
            report.AppendLine(new string('-', 78));

            try
            {
                Category lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);

                if (lines == null)
                {
                    report.AppendLine("This project has no Lines category, which should not happen.");
                    return;
                }

                List<string> names = new List<string>();

                foreach (Category style in lines.SubCategories)
                {
                    names.Add(style.Name ?? "");
                }

                foreach (string name in names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase))
                {
                    report.AppendLine(string.Format("  {0,-9}  \"{1}\"",
                                                    LineLoad_R.IsLineLoadStyle(name, patterns)
                                                        ? "LOAD"
                                                        : "ignored",
                                                    name));
                }

                report.AppendLine();
                report.AppendLine($"{names.Count} style(s), of which "
                                  + $"{names.Count(n => LineLoad_R.IsLineLoadStyle(n, patterns))} "
                                  + "would be read as line loads.");
            }
            catch (Exception e)
            {
                report.AppendLine("The line styles could not be listed - " + e.Message);
            }

            report.AppendLine();
        }

        /// <summary>Writes the report out and hands back where it went, or why it did not.</summary>
        private static string Write(string path, StringBuilder report)
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.WriteAllText(path, report.ToString());
                return path;
            }
            catch (Exception e)
            {
                return "the dump could not be written - " + e.Message;
            }
        }

        /// <summary>
        /// One detail line as a line load: its style name, the number off the end of it, and
        /// the curve walked out into a chain of straight pieces in the shared system.
        /// </summary>
        private LineLoad_R readLineLoad(CurveElement curve, string styleName, string story)
        {
            Curve geometry = curve.GeometryCurve;
            if (geometry == null)
            {
                return null;
            }

            List<MyPoint> points = new List<MyPoint>();

            foreach (XYZ vertex in geometry.Tessellate())
            {
                points.Add(toSharedPoint(vertex, null));
            }

            return new LineLoad_R(curve.Id.Value, styleName,
                                  points, LineLoad_R.NumberFromStyleName(styleName), story);
        }

        /// <summary>
        /// The name of the line style a curve is drawn in, which is what names its load. Empty
        /// where the curve carries none, which a line in a family or an imported one can.
        /// </summary>
        private static string lineStyleName(CurveElement curve)
        {
            try
            {
                GraphicsStyle style = curve.LineStyle as GraphicsStyle;
                return style == null ? "" : (style.Name ?? "");
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>
        /// What a region's number parameter actually held, for the report. The difference
        /// between the parameter being missing or empty and it being left at nought is the
        /// difference between two quite different things to go and fix.
        /// </summary>
        private static string describeNumberText(string text)
        {
            return string.IsNullOrWhiteSpace(text)
                ? "no such parameter, or an empty one"
                : $"'{text}', which does not read as a load label";
        }

        private static string describeParameterNames(string[] names)
        {
            return names.Length == 1 ? $"'{names[0]}'" : string.Join(" or ", names.Select(n => $"'{n}'"));
        }

        /// <summary>The first few of a list, so one bad plan cannot bury the rest of the report.</summary>
        private static string list(List<string> items)
        {
            const int most = 12;

            return items.Count <= most
                ? string.Join("; ", items)
                : string.Join("; ", items.Take(most)) + $"; and {items.Count - most} more";
        }

        private LoadRegion_R readLoadRegion(FilledRegion region, string[] parameterNames)
        {
            List<PlanRing> rings = new List<PlanRing>();

            foreach (CurveLoop loop in region.GetBoundaries())
            {
                List<MyPoint> points = new List<MyPoint>();

                foreach (Curve curve in loop)
                {
                    IList<XYZ> vertices = curve.Tessellate();
                    for (int i = 0; i < vertices.Count - 1; i++)
                    {
                        points.Add(toSharedPoint(vertices[i], null));
                    }
                }

                PlanRing ring = PlanRing.FromPoints(points);
                if (ring == null)
                {
                    continue;
                }

                foreach (PlanRing piece in ring.SplitAtRepeatedPoints())
                {
                    if (piece != null)
                    {
                        rings.Add(piece);
                    }
                }
            }

            if (rings.Count == 0)
            {
                return null;
            }

            // Sorted by what sits inside what, not by winding. A sketch does not promise which
            // way round any of its loops was drawn, and a real loading plan has regions whose
            // openings are wound both ways at once - sorted by winding, the ones wound with
            // their outline stop being openings and become solid pieces on top of it. Only the
            // drafted regions go this way: a face off a solid comes back with its winding
            // meant, and is read by RegionsFromRings as before.
            List<PlanPolygon> parts = PlanRegions.FromDraftedRings(rings).ToList();
            if (parts.Count == 0)
            {
                return null;
            }

            // The first of them is the number; all of them go with the region, so the loading
            // plan dump can say where it came from and what else the region was carrying.
            List<KeyValuePair<string, string>> found = namedParameterValues(region, parameterNames);
            string text = found.Count == 0 ? "" : found[0].Value;

            return new LoadRegion_R(region.Id.Value, parts, text)
            {
                TypeName = getElementTypeName(region),
                NumberSources = found.Select(pair => pair.Key + " = " + pair.Value).ToList()
            };
        }

        /// <summary>
        /// Every one of the named parameters the element actually carries, instance before type,
        /// in the order the names are given, as "name (instance)" or "name (type)" against its
        /// text. The first is the one a single value is read from. Empty when it carries none
        /// of them, which is a region with no load number on it rather than an error.
        /// </summary>
        private static List<KeyValuePair<string, string>> namedParameterValues(Element element, string[] names)
        {
            List<KeyValuePair<string, string>> found = new List<KeyValuePair<string, string>>();
            ElementType type = element.Document.GetElement(element.GetTypeId()) as ElementType;

            foreach (string name in names)
            {
                foreach (Element source in new Element[] { element, type })
                {
                    if (source == null)
                    {
                        continue;
                    }

                    Parameter parameter = source.LookupParameter(name);
                    if (parameter == null || !parameter.HasValue)
                    {
                        continue;
                    }

                    string value = parameterText(parameter);
                    if (!string.IsNullOrEmpty(value))
                    {
                        found.Add(new KeyValuePair<string, string>(
                            name + (ReferenceEquals(source, element) ? " (instance)" : " (type)"), value));
                    }
                }
            }

            return found;
        }

        private static string parameterText(Parameter parameter)
        {
            try
            {
                switch (parameter.StorageType)
                {
                    case StorageType.Integer:
                        return parameter.AsInteger()
                                        .ToString(System.Globalization.CultureInfo.InvariantCulture);
                    case StorageType.Double:
                        return parameter.AsDouble()
                                        .ToString(System.Globalization.CultureInfo.InvariantCulture);
                    case StorageType.String:
                        return (parameter.AsString() ?? "").Trim();
                    case StorageType.ElementId:
                        return (parameter.AsValueString() ?? "").Trim();
                    default:
                        return "";
                }
            }
            catch (Exception)
            {
                return "";
            }
        }

        #endregion
    }

    /// <summary>
    /// One Revit view a loading plan could have been drawn in, as the picker lists it. Held by
    /// element id rather than by name so a view that is renamed is still the same view, with
    /// the name kept beside it for when the id no longer resolves.
    /// </summary>
    /// <summary>
    /// One Revit level, for saying which floor a single level design model stands for. The id
    /// is what finds it again and the name is what the panel can say when the id no longer
    /// resolves, the same bargain <see cref="LoadingPlanView"/> makes.
    /// </summary>
    public sealed class RevitLevel
    {
        public RevitLevel(long id, string name, double elevationMM, double? projectElevationMM = null)
        {
            Id = id;
            Name = name ?? "";
            ElevationMM = elevationMM;
            ProjectElevationMM = projectElevationMM ?? elevationMM;
        }

        public long Id { get; }
        public string Name { get; }

        /// <summary>In mm, in the same frame the imported elements are named after.</summary>
        public double ElevationMM { get; }

        /// <summary>
        /// In mm from Revit's internal origin, whatever the level's elevation base is set to -
        /// the frame element geometry is read in, and so the one the alignment's height has to
        /// be worked out in. The same as <see cref="ElevationMM"/> unless the project base
        /// point has been moved up or down.
        /// </summary>
        public double ProjectElevationMM { get; }

        public string DisplayName
        {
            get { return $"{Name}  ({Math.Round(ElevationMM / 1000d, 3)} m)"; }
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    public sealed class LoadingPlanView
    {
        public LoadingPlanView(long id, string name, string kind)
        {
            Id = id;
            Name = name ?? "";
            Kind = kind ?? "";
        }

        public long Id { get; }
        public string Name { get; }

        /// <summary>Floor plan, drafting view and so on, so two views of the same name are readable.</summary>
        public string Kind { get; }

        public string DisplayName { get { return $"{Name}  ({Kind})"; } }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    /// <summary>
    /// Thrown out of a Revit read that the user asked to stop.
    ///
    /// A type of its own rather than an operation cancelled, so the one catch that means to
    /// swallow it cannot swallow a cancellation that came from somewhere else and report the
    /// read as stopped when something in Revit went wrong.
    /// </summary>
    public class ImportStoppedException : Exception
    {
        public ImportStoppedException()
            : base("The import was stopped.")
        {
        }
    }
}

