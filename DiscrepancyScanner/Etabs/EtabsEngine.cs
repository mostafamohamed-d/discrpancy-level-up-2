
using CSiAPIv1;
using EtabsTextModel;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DiscrepancyScanner
{
    [Serializable]
    public class EtabsEngine
    {
       
        eFramePropType[] myType = { };
        Form1.eForce eForceUnit =Form1.eForce.kgf;
        List<FrameData_e> framesFromEtabs = new List<FrameData_e>();
        List<Column_e> columnsFromEtabs = new List<Column_e>();
        cSapModel sp;
        Form1.eLength etabsDbUnites;
        private Dictionary<string, double> SectionsRebarMaterial = new Dictionary<string, double>(200);
        private Dictionary<string, SectionShape> sectionShapes = new Dictionary<string, SectionShape>(200);

        /// <summary>The old form's ignore box, kept writing to the setting it now lives in.</summary>
        public static string[] IgnoredNameList
        {
            get => PrefrencesData.SplitTypeNamePhrases(PrefrencesData.getPrefrencesData().EtabsSectionsToSkip);
            set => PrefrencesData.getPrefrencesData().EtabsSectionsToSkip = string.Join(",", value ?? new string[0]);
        }

        private bool isIgnored(string Name)
        {
            return PrefrencesData.MatchesAnyPhrase(Name, IgnoredNameList, out _);
        }

        private bool isNoDesignMaterial(FrameData frame)
        {
            string[] materials = PrefrencesData.SplitTypeNamePhrases(PrefrencesData.getPrefrencesData().EtabsMaterialsToSkip);
            return PrefrencesData.MatchesAnyPhrase(frame.Material ?? "", materials, out _);
        }

        public enum asLocation
        {
            left,
            mid,
            right
        }


        /// <summary>Reads the model open in ETABS, which is what every existing caller means.</summary>
        public EtabsEngine() : this(CsiProduct.Etabs, null)
        {
        }

        /// <summary>
        /// Reads the model open in the given program.
        ///
        /// <paramref name="level"/> is what a SAFE model is missing and an ETABS model has no
        /// use for: SAFE holds one floor and says nothing about which floor of which building
        /// it is, so the level the alignment paired it with is handed in, to name its one story
        /// after. Null for ETABS, and null for a SAFE model nobody has paired yet - which still
        /// imports, onto a story named after the program, so its story can be picked in the
        /// alignment.
        /// </summary>
        public EtabsEngine(CsiProduct csiProduct, SafeLevelLink level)
        {
            product = csiProduct;
            singleLevel = level;

            string problem;
            sp = CsiProducts.Attach(csiProduct, out problem);

            if (sp == null)
            {
                Form1.showMassage(problem);
                return;
            }

            initData(sp);
        }

        /// <summary>
        /// Reads the one model the user picked off the list of what is open.
        ///
        /// Which program it is comes from the instance rather than being asked for again: the
        /// picked model is an ETABS model or a SAFE one, and it is not open to interpretation.
        /// </summary>
        public EtabsEngine(CsiInstance instance, SafeLevelLink level)
        {
            product = instance == null ? CsiProduct.Etabs : instance.Product;
            singleLevel = level;

            string problem;
            sp = CsiProducts.Attach(instance, out problem);

            if (sp == null)
            {
                Form1.showMassage(problem);
                return;
            }

            initData(sp);
        }

        private void initData(cSapModel sp)
        {
            eLength tempLengthUnites = eLength.m;
            var eForceUnit_holder = eForce.kgf;
            eTemperature t_holder = eTemperature.C;
            sp.GetDatabaseUnits_2(ref eForceUnit_holder, ref tempLengthUnites, ref t_holder);
            eForceUnit = getEforceFromEtabsEnum(eForceUnit_holder);
            etabsDbUnites = getUnitesFromEunites(tempLengthUnites);
        }

        private Form1.eForce getEforceFromEtabsEnum(eForce eForceUnit_holder)
        {
            Form1.eForce myForce = Form1.eForce.N;
            switch (eForceUnit_holder)
            {
                case eForce.NotApplicable:
                    myForce = Form1.eForce.NotApplicable;
                    break;
                case eForce.lb:
                    myForce = Form1.eForce.lb;
                    break;
                case eForce.kip:
                    myForce = Form1.eForce.kip;
                    break;
                case eForce.N:
                    myForce = Form1.eForce.N;
                    break;
                case eForce.kN:
                    myForce= Form1.eForce.kN;
                    break;
                case eForce.kgf:
                    myForce=Form1.eForce.kgf;
                    break;
                case eForce.tonf:
                    myForce = Form1.eForce.tonf;
                    break;
                default:
                    break;
            }
            return myForce;
        }

        private Form1.eLength getUnitesFromEunites(eLength tempLengthUnites)
        {
            Form1.eLength myeUnites;
            switch (tempLengthUnites)
            {
                case eLength.NotApplicable:
                    myeUnites = Form1.eLength.NotApplicable;
                    break;
                case eLength.inch:
                    myeUnites = Form1.eLength.inch;
                    break;
                case eLength.ft:
                    myeUnites = Form1.eLength.ft;
                    break;
                case eLength.micron:
                    myeUnites = Form1.eLength.micron;
                    break;
                case eLength.mm:
                    myeUnites = Form1.eLength.mm;
                    break;
                case eLength.cm:
                    myeUnites = Form1.eLength.cm;
                    break;
                case eLength.m:
                    myeUnites = Form1.eLength.m;
                    break;
                default:
                    myeUnites = Form1.eLength.m;
                    break;

            }
            return myeUnites;
        }

        public EtabsEngine(cSapModel sapModel) : this(sapModel, CsiProduct.Etabs, null)
        {
        }

        public EtabsEngine(cSapModel sapModel, CsiProduct csiProduct, SafeLevelLink level)
        {
            try
            {
                product = csiProduct;
                singleLevel = level;
                sp = sapModel;
                initData(sp);

            }
            catch (Exception)
            {
                Form1.showMassage("cannot find " + CsiProducts.Name(csiProduct) + " instance");

            }

        }

        #region which program, and where its one floor stands

        private CsiProduct product = CsiProduct.Etabs;

        /// <summary>Null unless this is a SAFE model that has been paired with a Revit level.</summary>
        private SafeLevelLink singleLevel;

        /// <summary>Which program this model was read out of.</summary>
        public CsiProduct Product { get { return product; } }

        /// <summary>
        /// True when the model holds one floor rather than a building, so the stories have to
        /// be made up rather than read.
        /// </summary>
        private bool IsSingleLevel { get { return CsiProducts.IsSingleLevel(product); } }

        /// <summary>
        /// What the one story of a single level model is called: the Revit level it was paired
        /// with, so every story-keyed thing downstream - the level rail, the loading plan
        /// pairing, the story a Revit floor is assigned to - names the same floor in both
        /// models.
        /// </summary>
        private string SingleLevelName
        {
            get
            {
                return singleLevel != null && singleLevel.IsPaired
                     ? singleLevel.LevelName
                     : CsiProducts.Name(product);
            }
        }

        /// <summary>
        /// The one story a single level model is given, at the height its floor is drawn at.
        ///
        /// Where the floor is drawn rather than where the Revit level is: the model is not
        /// moved, the alignment is - its story and level pairing works out the Z that puts the
        /// Revit level on this story, exactly as it does for an ETABS story. So the story has
        /// to be where the slabs really are, which is nought for a model drawn about its own
        /// origin and the real elevation for one drawn at its level.
        /// </summary>
        private Story_E singleLevelStory()
        {
            return new Story_E(SingleLevelName, FloorElevation(shellPointZ), etabsDbUnites);
        }

        /// <summary>
        /// The height a one floor model's floor is drawn at, in whatever units the Zs are in:
        /// the Z the most area corners share. A slab's corners all sit at it, a wall hung under
        /// the slab has only its top two there, and a column has none, so the floor wins on
        /// any model with a slab in it. Nought for a model with no areas at all, which is
        /// where SAFE starts one.
        /// </summary>
        public static double FloorElevation(double[] z)
        {
            if (z == null || z.Length == 0)
            {
                return 0;
            }

            // Counted to a thousandth of a unit, so the same height read back as two doubles a
            // rounding error apart is still one height.
            return z.GroupBy(value => Math.Round(value, 3))
                    .OrderByDescending(group => group.Count())
                    .ThenByDescending(group => group.Key)
                    .First()
                    .Key;
        }

        /// <summary>
        /// Puts every object in a single level model on the one story it has.
        ///
        /// A frame carries the story name the program gave it, and SAFE gives it whatever a
        /// one floor model calls its floor - which is not the name of the Revit level it is
        /// being compared against, and in some models is nothing at all. Left alone, every
        /// beam and column would land on a story the level rail has never heard of and would
        /// be filtered out of every view. The areas are done for the same reason, and it also
        /// spares the wall import falling back to the geometry and reporting that it did.
        /// </summary>
        private void nameSingleLevelStory()
        {
            if (!IsSingleLevel)
            {
                return;
            }

            string name = SingleLevelName;

            for (int i = 0; i < frameStoryNames.Length; i++)
            {
                frameStoryNames[i] = name;
            }

            // Every shell, not just the ones the model happened to name. SAFE has no stories
            // to report, so the label list comes back empty and the walls would each fall back
            // to working their story out from their highest corner - which lands on the right
            // answer, there being only one, but raises an import issue saying the model would
            // not say where its areas are on every single run. Stating it here answers the
            // question outright and leaves the fallback for the models that really need it.
            for (int i = 0; i < shellCount && i < shellNames.Length; i++)
            {
                if (!string.IsNullOrEmpty(shellNames[i]))
                {
                    areaStories[shellNames[i]] = name;
                }
            }
        }

        #endregion



        #region the model, read once

        /// <summary>
        /// Everything the two whole-model calls hand back, kept so the rest of the import can
        /// answer from memory rather than asking ETABS again once per object.
        ///
        /// Every call across the interop boundary is a COM round trip, and the cost is in the
        /// crossing rather than in the answer: asking for one frame costs about what asking
        /// for all of them costs. The import used to ask per object - a label here, two end
        /// coordinates there, a connectivity list per joint and a design orientation per
        /// connection - which on a tower of ten thousand frames is a few hundred thousand
        /// round trips, and the wait the user was sitting through.
        ///
        /// Almost none of it needed asking. GetAllFrames already carries the section, the
        /// story, both end point names and their coordinates; GetAllAreas already carries the
        /// design orientation and the boundary points. The answers were arriving and being
        /// thrown away, then bought again one at a time.
        /// </summary>
        private void readModel()
        {
            if (sp == null)
            {
                return;
            }

            readAllFrames();
            readAllShells();

            // The sections are read afresh each import, so a section changed in ETABS between
            // two reads through the same engine is not answered from the first read.
            sectionShapes.Clear();
            sectionDimensions.Clear();
            sectionDesignerSections.Clear();
            sectionDesignerOutlines = null;

            readFrameLabels();
            readAreaStories();
            nameSingleLevelStory();
        }

        /// <summary>
        /// The story ETABS itself puts each area on, for the whole model in one call.
        ///
        /// Worth asking rather than working out. A wall is commonly subdivided between two
        /// floors as well as along its length, and a panel cut half way up tops out exactly
        /// between the level it belongs to and the one below, so deriving the story from its
        /// highest corner comes down to which way a tie falls - and the panel is then checked
        /// against, and drawn on, the wrong floor. The model holds the answer outright, and
        /// says it for every area at once, which is what the frames already do for their own.
        /// </summary>
        private Dictionary<string, string> areaStories =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private void readAreaStories()
        {
            areaStories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            int count = 0;
            string[] names = { "" }, labels = { "" }, stories = { "" };

            try
            {
                if (sp.AreaObj.GetLabelNameList(ref count, ref names, ref labels, ref stories) != 0
                    || names == null || stories == null)
                {
                    return;
                }
            }
            catch (Exception)
            {
                return;
            }

            for (int i = 0; i < count && i < names.Length && i < stories.Length; i++)
            {
                if (!string.IsNullOrEmpty(names[i]) && !string.IsNullOrEmpty(stories[i]))
                {
                    areaStories[names[i]] = stories[i];
                }
            }
        }

        #region frames

        private int frameCount;
        private string[] frameNames = new string[0];
        private string[] framePropNames = new string[0];
        private string[] frameStoryNames = new string[0];
        private double[] frameAngles = new double[0];
        private double[] frameP1X = new double[0], frameP1Y = new double[0], frameP1Z = new double[0];
        private double[] frameP2X = new double[0], frameP2Y = new double[0], frameP2Z = new double[0];

        /// <summary>Both ends of one frame, as GetAllFrames gave them.</summary>
        private sealed class FrameEnds
        {
            public string Point1 = "";
            public string Point2 = "";
            public double X1, Y1, Z1;
            public double X2, Y2, Z2;
        }

        private Dictionary<string, FrameEnds> frameEnds =
            new Dictionary<string, FrameEnds>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Which frames meet at each point. This is the half of PointObj.GetConnectivity the
        /// import actually reads, worked out from the end points GetAllFrames already
        /// returned instead of asked for once per joint.
        ///
        /// Built from the raw list rather than from the frames that survive filtering, because
        /// ETABS would have named the ignored ones too, and an ignored frame that happens to be
        /// a column still has to stop a merge running through it.
        /// </summary>
        private Dictionary<string, List<string>> framesByPoint =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The design orientation of a frame, asked for once and then remembered.</summary>
        private Dictionary<string, bool> frameIsColumn =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, string> frameLabels =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private void readAllFrames()
        {
            frameCount = 0;
            frameEnds = new Dictionary<string, FrameEnds>(StringComparer.OrdinalIgnoreCase);
            framesByPoint = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            frameIsColumn = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            string[] pointName1 = { "" }, pointName2 = { "" };
            double[] offset1X = { 0 }, offset2X = { 0 }, offset1Y = { 0 };
            double[] offset2Y = { 0 }, offset1Z = { 0 }, offset2Z = { 0 };
            int[] cardinalPoint = { 0 };

            int count = 0;
            string[] names = { "" }, props = { "" }, stories = { "" };
            double[] p1x = { 0 }, p1y = { 0 }, p1z = { 0 };
            double[] p2x = { 0 }, p2y = { 0 }, p2z = { 0 };
            double[] angles = { 0 };

            if (sp.FrameObj.GetAllFrames(ref count, ref names, ref props, ref stories,
                                         ref pointName1, ref pointName2,
                                         ref p1x, ref p1y, ref p1z, ref p2x, ref p2y, ref p2z,
                                         ref angles, ref offset1X, ref offset2X, ref offset1Y,
                                         ref offset2Y, ref offset1Z, ref offset2Z,
                                         ref cardinalPoint) != 0 || names == null)
            {
                return;
            }

            frameCount = count;
            frameNames = names;
            framePropNames = props;
            frameStoryNames = stories;
            frameAngles = angles;
            frameP1X = p1x; frameP1Y = p1y; frameP1Z = p1z;
            frameP2X = p2x; frameP2Y = p2y; frameP2Z = p2z;

            for (int i = 0; i < frameCount && i < names.Length; i++)
            {
                frameEnds[names[i]] = new FrameEnds
                {
                    Point1 = pointName1[i],
                    Point2 = pointName2[i],
                    X1 = p1x[i], Y1 = p1y[i], Z1 = p1z[i],
                    X2 = p2x[i], Y2 = p2y[i], Z2 = p2z[i]
                };
            }

            framesByPoint = ModelIndex.FramesByPoint(frameCount, names, pointName1, pointName2);
        }

        /// <summary>
        /// The label ETABS shows for every frame, in one call rather than one per frame.
        /// Falls back to asking per frame if the model will not answer for the whole list, so
        /// a build that does not carry the call still imports.
        /// </summary>
        private void readFrameLabels()
        {
            frameLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            int count = 0;
            string[] names = { "" }, labels = { "" }, stories = { "" };

            try
            {
                if (sp.FrameObj.GetLabelNameList(ref count, ref names, ref labels, ref stories) != 0
                    || names == null || labels == null)
                {
                    return;
                }
            }
            catch (Exception)
            {
                return;
            }

            for (int i = 0; i < count && i < names.Length && i < labels.Length; i++)
            {
                frameLabels[names[i]] = labels[i] ?? "";
            }
        }

        /// <summary>
        /// Whether ETABS calls this frame a column. Asked once per frame at most: the same
        /// frame is a neighbour of every frame around it, and used to be asked about once for
        /// each of them.
        /// </summary>
        private bool isColumnFrame(string etabsUname)
        {
            bool known;
            if (frameIsColumn.TryGetValue(etabsUname, out known))
            {
                return known;
            }

            eFrameDesignOrientation orientation = eFrameDesignOrientation.Other;
            bool isColumn = false;

            try
            {
                if (sp.FrameObj.GetDesignOrientation(etabsUname, ref orientation) == 0)
                {
                    isColumn = orientation == eFrameDesignOrientation.Column;
                }
            }
            catch (Exception)
            {
                isColumn = false;
            }

            frameIsColumn[etabsUname] = isColumn;
            return isColumn;
        }

        #endregion

        #region shells

        private int shellCount;
        private string[] shellNames = new string[0];
        private eAreaDesignOrientation[] shellOrientations = new eAreaDesignOrientation[0];
        private int[] shellPointDelimiter = new int[0];
        private string[] shellPointNames = new string[0];
        private double[] shellPointX = new double[0];
        private double[] shellPointY = new double[0];
        private double[] shellPointZ = new double[0];

        /// <summary>
        /// Every point a wall stands on. The merge walks along a line of beams and has to stop
        /// where a wall supports it; that used to be a GetDesignOrientation per area per joint,
        /// and the design orientation of every area arrives with GetAllAreas anyway.
        /// </summary>
        private HashSet<string> pointsOnAWall = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void readAllShells()
        {
            shellCount = 0;
            pointsOnAWall = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int count = 0;
            string[] names = { "" };
            eAreaDesignOrientation[] orientations = { eAreaDesignOrientation.Wall };
            int boundaryPoints = 0;
            int[] delimiter = { 0 };
            string[] pointNames = { "" };
            double[] x = { 0 }, y = { 0 }, z = { 0 };

            if (sp.AreaObj.GetAllAreas(ref count, ref names, ref orientations, ref boundaryPoints,
                                       ref delimiter, ref pointNames, ref x, ref y, ref z) != 0
                || names == null)
            {
                return;
            }

            shellCount = count;
            shellNames = names;
            shellOrientations = orientations;
            shellPointDelimiter = delimiter;
            shellPointNames = pointNames;
            shellPointX = x;
            shellPointY = y;
            shellPointZ = z;

            bool[] isWall = new bool[count];
            for (int i = 0; i < count && i < orientations.Length; i++)
            {
                isWall[i] = orientations[i] == eAreaDesignOrientation.Wall;
            }

            pointsOnAWall = ModelIndex.WallPoints(count, isWall, delimiter, pointNames);
        }

        #endregion

        #endregion

        private List<FrameData_e> getAllbeams()
        {
            
            if (sp == null)
            {
                return new List<FrameData_e>();
            }

            // Both lists start empty, because reading twice through one engine is a real path:
            // the ETABS form keeps its engine for the life of the form, so re-importing to pick
            // up a changed setting - the column lean, say - used to come back with the second
            // read's frames added to the first read's rather than in place of them. The slabs
            // were already read this way; these two were not.
            framesFromEtabs = new List<FrameData_e>();
            columnsFromEtabs = new List<Column_e>();

            getNameList();
            retreveFramesDimentions(ref framesFromEtabs);
            filterNoDesignFrames();
            // Read before the columns are split out, so a column carries its groups with it.
            readGroupAssignments();
            filterColumnsAndBeams();
            getDesignRFt();
            foreach (var EtabsFrame in framesFromEtabs)
            {
                EtabsFrame.Joints = getJoints(EtabsFrame.etabsUname);
                EtabsFrame.ReAdjustStartAndEnd();
            }
            return framesFromEtabs.ToList<FrameData_e>();
        }

        /// <summary>
        /// Records which ETABS groups every frame belongs to. A column's groups are what the
        /// Revit column type mark is checked against; see <see cref="ColumnGroupCheck"/>.
        /// </summary>
        private void readGroupAssignments()
        {
            Dictionary<string, List<string>> byFrame = readAssignmentsByGroup();

            foreach (FrameData_e frame in framesFromEtabs)
            {
                List<string> groups;
                frame.Groups = byFrame != null
                    ? (byFrame.TryGetValue(frame.etabsUname, out groups) ? groups : new List<string>())
                    : getGroupAssignments(frame.etabsUname);
            }
        }

        /// <summary>
        /// Which groups every frame belongs to, read the other way round: once per group
        /// rather than once per frame. A model has a handful of groups and thousands of
        /// frames, so this is the same answer for a fraction of the round trips.
        ///
        /// Returns null if the group list cannot be read, which puts the import back on
        /// asking frame by frame rather than quietly reporting that nothing is in a group -
        /// the column check reads these, and an empty answer there is a wrong answer.
        /// </summary>
        private Dictionary<string, List<string>> readAssignmentsByGroup()
        {
            int numberGroups = 0;
            string[] groupNames = { "" };

            try
            {
                if (sp.GroupDef.GetNameList(ref numberGroups, ref groupNames) != 0 || groupNames == null)
                {
                    return null;
                }
            }
            catch (Exception)
            {
                return null;
            }

            Dictionary<string, List<string>> byFrame =
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < numberGroups && i < groupNames.Length; i++)
            {
                int numberItems = 0;
                int[] objectTypes = { 0 };
                string[] objectNames = { "" };

                try
                {
                    if (sp.GroupDef.GetAssignments(groupNames[i], ref numberItems, ref objectTypes,
                                                   ref objectNames) != 0 || objectNames == null)
                    {
                        continue;
                    }
                }
                catch (Exception)
                {
                    // A group with nothing in it is reported by some ETABS builds as an error.
                    continue;
                }

                for (int at = 0; at < numberItems && at < objectNames.Length; at++)
                {
                    // A group holds points and areas as readily as frames; only frames are
                    // being asked about here.
                    if (objectTypes[at] != GroupFrameObject || string.IsNullOrEmpty(objectNames[at]))
                    {
                        continue;
                    }

                    List<string> groups;
                    if (!byFrame.TryGetValue(objectNames[at], out groups))
                    {
                        groups = new List<string>(2);
                        byFrame[objectNames[at]] = groups;
                    }

                    groups.Add(groupNames[i]);
                }
            }

            return byFrame;
        }

        /// <summary>What GroupDef.GetAssignments calls a frame object.</summary>
        private const int GroupFrameObject = 2;

        /// <summary>
        /// The groups one frame is in, asked for directly. Only used when the group list
        /// itself could not be read.
        /// </summary>
        private List<string> getGroupAssignments(string etabsUname)
        {
            int numberGroups = 0;
            string[] groupNames = { "" };

            try
            {
                int ret = sp.FrameObj.GetGroupAssign(etabsUname, ref numberGroups, ref groupNames);
                if (ret != 0 || groupNames == null)
                {
                    return new List<string>();
                }

                List<string> assigned = new List<string>(numberGroups);
                for (int i = 0; i < numberGroups && i < groupNames.Length; i++)
                {
                    if (!string.IsNullOrEmpty(groupNames[i]))
                    {
                        assigned.Add(groupNames[i]);
                    }
                }
                return assigned;
            }
            catch (Exception)
            {
                // A frame with no group assignment at all is reported by some ETABS builds as
                // an error rather than an empty list; either way it simply has no groups.
                return new List<string>();
            }
        }

        /// <summary>
        /// Splits the frames ETABS handed over into the ones the column check will run on and
        /// the ones the beam check will. Every frame goes to one side or the other, so a frame
        /// wrongly called a column is not only checked as the wrong thing, it is missing from
        /// the other check as well.
        /// </summary>
        private void filterColumnsAndBeams()
        {
            var toBeRemoved = new List<FrameData_e>();

            foreach (var frame in framesFromEtabs)
            {
                if (isColumnShaped(frame))
                {
                    columnsFromEtabs.Add(frame.asColumn_e());
                    toBeRemoved.Add(frame);
                }
            }
            framesFromEtabs.RemoveAll(x => toBeRemoved.Contains(x));
           
        }

        private void filterNoDesignFrames()
        {
            var toBeRemoved = new List<FrameData_e>();

            foreach (var frame in framesFromEtabs)
            {
                if (isNoDesignMaterial(frame))
                {
                    toBeRemoved.Add(frame);
                }
            }
            framesFromEtabs.RemoveAll(x => toBeRemoved.Contains(x));
        }

        /// <summary>
        /// Whether this frame stands up steeply enough to be a column, measured as the angle
        /// its two ends make off plumb. The arithmetic and the reason it is not the rise on
        /// its own are in <see cref="ModelIndex.IsColumn"/>; the angle it is cut at is
        /// <see cref="PrefrencesData.ColumnMaxLeanFromVertical"/>.
        /// </summary>
        private bool isColumnShaped(FrameData_e frame)
        {
            if (frame == null || frame.Start == null || frame.End == null)
            {
                return false;
            }

            return ModelIndex.IsColumn(frame.Start.X, frame.Start.Y, frame.Start.Z,
                                       frame.End.X, frame.End.Y, frame.End.Z,
                                       PrefrencesData.getPrefrencesData().ColumnMaxLeanFromVertical);
        }

        public EtabsDataContainer getEtabsData ()
        {
            EtabsDataContainer data = new EtabsDataContainer();

            // A whole read starts with nothing said. The Revit side builds a fresh engine every
            // import and so never noticed, but the ETABS form keeps one for the life of the
            // form: without this, reading twice would report the first read's lines again.
            importIssues.Clear();
            areaProperties = null;

            readModel();
            data.frames = getAllbeams();
            data.columns = columnsFromEtabs;
            data.stories = getAllStories();
            data.Walls = getAllWalls(data.stories);
            // Filled by the same pass that read the walls, since both come out of one call.
            data.Slabs = slabsFromEtabs;
            data.ReadsNullLoadAreas = true;
            data.LineLoadFrames = getLineLoadFrames();
            return data;
        }

        #region line loads on frames

        /// <summary>
        /// Every frame that could carry a line load, with the distributed loads on it.
        ///
        /// Built straight off the arrays <see cref="readAllFrames"/> filled rather than off the
        /// beams the beam check imported, and that is a correctness point. The beam import
        /// throws away frames whose material reads as a dummy or "other", which is precisely
        /// what a null frame drawn along a slab edge to hang a cladding load on is made of - so
        /// the line loads that matter most would have nothing left to sit on.
        ///
        /// Columns fall out on their own: a vertical frame collapses to a point in plan, and a
        /// frame with no line in plan has nothing for a drawn line to be compared against.
        /// </summary>
        private List<LineLoadFrame_E> getLineLoadFrames()
        {
            // Read on every import rather than only when the line load check is switched on.
            // It used to be gated on the switch, which cost nothing to run and a great deal to
            // use: a person who turned the check on and pressed Compare got "import the ETABS
            // model again" and no way to tell that from the check being broken. The saving was
            // not worth having either - the loads come back for the whole model in one call,
            // the same bargain the area loads are read on.
            List<LineLoadFrame_E> frames = new List<LineLoadFrame_E>();

            Dictionary<string, LineLoadFrame_E> byName =
                new Dictionary<string, LineLoadFrame_E>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < frameCount && i < frameNames.Length; i++)
            {
                MyPoint start = FrameData_e.getPointFromEtabs(etabsDbUnites, frameP1X[i], frameP1Y[i], frameP1Z[i]);
                MyPoint end = FrameData_e.getPointFromEtabs(etabsDbUnites, frameP2X[i], frameP2Y[i], frameP2Z[i]);

                LineLoadFrame_E frame = new LineLoadFrame_E(frameNames[i], getFrameLabel(frameNames[i]),
                                                            frameStoryNames[i], start, end);

                if (!frame.HasPlanLine)
                {
                    continue;
                }

                frames.Add(frame);
                byName[frameNames[i]] = frame;
            }

            readDistributedLoads(byName);

            return frames;
        }

        /// <summary>
        /// The distributed loads on every frame in the model, in one call.
        ///
        /// The same bargain the area loads are read on: the API answers for a group as readily
        /// as for one object and ETABS keeps a group holding everything, so a model with ten
        /// thousand frames costs one round trip rather than ten thousand.
        ///
        /// The stretch each load covers is kept as the relative distances rather than the
        /// actual ones, because those are fractions of the frame and so cannot be got wrong by
        /// a model built in feet.
        /// </summary>
        private void readDistributedLoads(Dictionary<string, LineLoadFrame_E> byName)
        {
            int count = 0;
            string[] frameNamesOut = { "" };
            string[] patterns = { "" };
            int[] types = { 0 };
            string[] systems = { "" };
            int[] directions = { 0 };
            double[] rd1 = { 0 }, rd2 = { 0 };
            double[] dist1 = { 0 }, dist2 = { 0 };
            double[] value1 = { 0 }, value2 = { 0 };

            try
            {
                if (sp.FrameObj.GetLoadDistributed(AllObjectsGroup, ref count, ref frameNamesOut,
                                                   ref patterns, ref types, ref systems, ref directions,
                                                   ref rd1, ref rd2, ref dist1, ref dist2,
                                                   ref value1, ref value2, eItemType.Group) != 0)
                {
                    return;
                }
            }
            catch (Exception e)
            {
                importIssues.Add("The distributed loads could not be read out of ETABS, so the line "
                                 + $"loads were not checked - {e.Message}");
                return;
            }

            double factor = lineLoadFactor();

            for (int i = 0; i < count && i < frameNamesOut.Length; i++)
            {
                LineLoadFrame_E frame;
                if (!byName.TryGetValue(frameNamesOut[i] ?? "", out frame))
                {
                    continue;
                }

                frame.Add(ToLineLoad(patterns[i], types[i], directions[i], rd1[i], rd2[i],
                                     value1[i], value2[i], factor));
            }
        }

        /// <summary>One distributed load as ETABS gives it, brought into kN/m and turned the right way up.</summary>
        private static LineLoad_E ToLineLoad(string pattern, int type, int direction, double rd1, double rd2,
                                             double value1, double value2, double factor)
        {
            double sign;
            bool gravity = LineLoad_E.IsDownwardDirection(direction, out sign);

            // Type 2 is a distributed moment, which is not a line load. It is kept so it
            // can be reported rather than dropped, because a frame carrying one is a frame
            // the check is only telling half the story about.
            bool moment = type == 2;

            double at1 = value1 * factor;
            double at2 = value2 * factor;

            return new LineLoad_E(pattern,
                                  gravity ? sign * at1 : at1,
                                  gravity ? sign * at2 : at2,
                                  rd1, rd2, direction, gravity, moment);
        }

        /// <summary>
        /// Every distributed load on one frame, exactly as the model holds it now - read live
        /// rather than taken off the import, because what is written back replaces the loads
        /// the frame carries and the import may be hours old. Null, with the reason, when the
        /// frame cannot be read; empty for a frame carrying nothing.
        /// </summary>
        public List<FrameDistributedLoad> ReadFrameLoads(string frameUniqueName, out string problem)
        {
            problem = null;
            List<FrameDistributedLoad> loads = new List<FrameDistributedLoad>();

            int count = 0;
            string[] names = { "" };
            string[] patterns = { "" };
            int[] types = { 0 };
            string[] systems = { "" };
            int[] directions = { 0 };
            double[] rd1 = { 0 }, rd2 = { 0 };
            double[] dist1 = { 0 }, dist2 = { 0 };
            double[] value1 = { 0 }, value2 = { 0 };

            try
            {
                if (sp.FrameObj.GetLoadDistributed(frameUniqueName, ref count, ref names, ref patterns, ref types,
                                                   ref systems, ref directions, ref rd1, ref rd2,
                                                   ref dist1, ref dist2, ref value1, ref value2,
                                                   eItemType.Objects) != 0)
                {
                    problem = $"{CsiProducts.Name(product)} has no frame {frameUniqueName}.";
                    return null;
                }
            }
            catch (Exception e)
            {
                problem = $"The loads on frame {frameUniqueName} could not be read - {e.Message}";
                return null;
            }

            for (int i = 0; i < count && i < patterns.Length; i++)
            {
                loads.Add(new FrameDistributedLoad(patterns[i], types[i], systems[i], directions[i],
                                                   rd1[i], rd2[i], value1[i], value2[i]));
            }

            return loads;
        }

        /// <summary>The same loads as the line load check reads them, in kN/m.</summary>
        public List<LineLoad_E> AsLineLoads(IEnumerable<FrameDistributedLoad> loads)
        {
            double factor = lineLoadFactor();
            return loads.Select(load => ToLineLoad(load.Pattern, load.LoadType, load.Direction,
                                                   load.RelativeFrom, load.RelativeTo,
                                                   load.ValueFrom, load.ValueTo, factor))
                        .ToList();
        }

        /// <summary>The label of a frame as the model has it now, empty when it has none.</summary>
        public string FrameLabelNow(string frameUniqueName)
        {
            frameLabels.Remove(frameUniqueName ?? "");
            return getFrameLabel(frameUniqueName ?? "");
        }

        /// <summary>Every load pattern defined in the model.</summary>
        public List<string> LoadPatternNames()
        {
            int count = 0;
            string[] names = { "" };

            try
            {
                if (sp.LoadPatterns.GetNameList(ref count, ref names) != 0)
                {
                    return new List<string>();
                }
            }
            catch (Exception)
            {
                return new List<string>();
            }

            return names.Take(count).Where(name => !string.IsNullOrEmpty(name)).ToList();
        }

        /// <summary>
        /// True while the model holds analysis results, when nothing in it can be changed until
        /// it is unlocked - which throws the results away.
        /// </summary>
        public bool IsModelLocked
        {
            get
            {
                try
                {
                    return sp.GetModelIsLocked();
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public bool UnlockModel(out string problem)
        {
            problem = null;
            try
            {
                if (sp.SetModelIsLocked(false) == 0)
                {
                    return true;
                }
            }
            catch (Exception e)
            {
                problem = e.Message;
                return false;
            }

            problem = $"{CsiProducts.Name(product)} would not unlock the model.";
            return false;
        }

        /// <summary>What 1 kN/m is in the model's own units.</summary>
        public double ModelUnitsPerKilonewtonMetre
        {
            get
            {
                double factor = lineLoadFactor();
                return factor <= 0 ? 1 : 1 / factor;
            }
        }

        /// <summary>
        /// Puts the given loads on one frame, replacing everything each named pattern held on
        /// it. A pattern is cleared and then assigned load by load; if any assignment is turned
        /// down, every pattern touched is put back to what <paramref name="before"/> says it
        /// held, so a failure leaves the frame as it was rather than half written.
        /// </summary>
        public bool WriteFrameLoads(string frameUniqueName, IDictionary<string, List<FrameDistributedLoad>> byPattern,
                                    IList<FrameDistributedLoad> before, out string problem)
        {
            problem = null;

            try
            {
                foreach (KeyValuePair<string, List<FrameDistributedLoad>> pattern in byPattern)
                {
                    if (!Assign(frameUniqueName, pattern.Key, pattern.Value))
                    {
                        problem = $"{CsiProducts.Name(product)} turned down a load in {pattern.Key} on frame "
                                  + $"{frameUniqueName}. The frame was put back as it was.";
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                problem = $"Writing the load on frame {frameUniqueName} failed - {e.Message}. "
                          + "The frame was put back as it was.";
            }

            if (problem == null)
            {
                return true;
            }

            try
            {
                foreach (string pattern in byPattern.Keys)
                {
                    Assign(frameUniqueName, pattern, before.Where(load => string.Equals(
                               load.Pattern, pattern, StringComparison.OrdinalIgnoreCase)).ToList());
                }
            }
            catch (Exception)
            {
                problem = problem.Replace(" The frame was put back as it was.", "")
                          + " Putting the frame back failed too: check its loads in "
                          + CsiProducts.Name(product) + ".";
            }

            return false;
        }

        /// <summary>Clears one pattern off a frame and assigns the loads given, in that pattern.</summary>
        private bool Assign(string frameUniqueName, string pattern, IList<FrameDistributedLoad> loads)
        {
            sp.FrameObj.DeleteLoadDistributed(frameUniqueName, pattern, eItemType.Objects);

            foreach (FrameDistributedLoad load in loads)
            {
                if (sp.FrameObj.SetLoadDistributed(frameUniqueName, pattern, load.LoadType, load.Direction,
                                                   load.RelativeFrom, load.RelativeTo,
                                                   load.ValueFrom, load.ValueTo, load.CoordinateSystem,
                                                   true, false, eItemType.Objects) != 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Redraws the model's windows so what was written can be seen there.</summary>
        public void RefreshView()
        {
            try
            {
                sp.View.RefreshView(0, false);
            }
            catch (Exception)
            {
                // Only the picture is out of date; the model has the loads.
            }
        }

        /// <summary>
        /// What one unit of distributed load in the model's database units is in kN/m.
        ///
        /// A force over a length, so both are converted: the force to newtons, the length to
        /// millimetres. A newton per millimetre is a kilonewton per metre exactly, which is
        /// why the thousands cancel and this is shorter than
        /// <see cref="areaLoadFactor"/> rather than differently shaped. A model in kN and m
        /// comes out unchanged, which is the case worth checking by eye.
        /// </summary>
        private double lineLoadFactor()
        {
            double forceInNewtons = FrameData_e.getForcefromUnit(eForceUnit, 1d);
            double lengthInMillimetres = FrameData_e.getLengthFromUnit(etabsDbUnites, 1d);

            return lengthInMillimetres <= 0 ? 1 : forceInNewtons / lengthInMillimetres;
        }

        #endregion

        private List<Wall_E> getAllWalls(List<Story_E> stories)
        {
            List<Wall_E> wallsFromEtabs = new List<Wall_E>();

            wallsWithoutThickness = 0;
            wallsWithoutLine = 0;
            wallsWithoutAssignedStory = 0;

            optainAllShellsData(ref wallsFromEtabs, stories);

            // Counted rather than named one by one: a core drawn against a property this cannot
            // read is hundreds of areas, and hundreds of lines saying the same thing is a list
            // nobody reads. What matters is that it happened and to how many.
            if (wallsWithoutThickness > 0)
            {
                importIssues.Add($"{wallsWithoutThickness} ETABS wall area(s) have no wall section property "
                                 + "this could read, so their thickness is not known and the thickness "
                                 + "check on them will say so rather than pass or fail.");
            }

            if (wallsWithoutLine > 0)
            {
                importIssues.Add($"{wallsWithoutLine} ETABS wall area(s) have every corner at the same place "
                                 + "in plan, so they have no wall line and are left out of the wall check.");
            }

            if (wallsWithoutAssignedStory > 0)
            {
                // Told apart on purpose. Every wall in the model falling back says the model
                // would not answer at all, which is a different thing from a handful of areas
                // with no story on them, and reading the second where the first happened sends
                // whoever is looking into the model to find a fault that is not in it.
                importIssues.Add(areaStories.Count == 0
                    ? "This model would not say which story its areas are on, so the story of every wall "
                      + "panel was worked out from its highest corner instead. A panel subdivided part way "
                      + "up a story can land on the level below by that route."
                    : $"{wallsWithoutAssignedStory} ETABS wall area(s) do not say which story they are on, "
                      + "or name one this model has no story for, so it was worked out from the highest "
                      + "corner of the panel instead. A panel subdivided part way up a story can land on "
                      + "the level below by that route.");
            }

            return wallsFromEtabs;
        }

        private int wallsWithoutThickness;
        private int wallsWithoutLine;
        private int wallsWithoutAssignedStory;

        /// <summary>
        /// Which story a wall panel belongs to: what the model says, where it says anything.
        ///
        /// The geometry is the fallback and not the first answer, because it cannot tell which
        /// floor a panel cut half way up a story belongs to - see <see cref="readAreaStories"/>.
        /// A story name the story list has never heard of is no use either, so that falls back
        /// too rather than putting the panel on a floor the viewer cannot show.
        /// </summary>
        private string wallStory(string areaName, Joint[] joints, List<Story_E> stories)
        {
            string assigned;
            if (areaStories.TryGetValue(areaName, out assigned)
                && stories.Any(story => string.Equals(story.Name, assigned, StringComparison.OrdinalIgnoreCase)))
            {
                return assigned;
            }

            wallsWithoutAssignedStory++;
            return getWallStory(joints, stories);
        }

        /// <summary>
        /// One ETABS wall area, with the section property assigned to it read for its thickness
        /// and the story it rises to worked out from its highest corner - which is the story
        /// ETABS itself names a wall after.
        ///
        /// Nothing is refused here. An area whose property cannot be read still comes in, with
        /// a thickness of nought, because a wall left out of the list is a wall the check can
        /// say nothing at all about; what the check does with an unknown thickness is report it
        /// as unknown, which is the honest answer.
        /// </summary>
        private Wall_E readWall(string areaName, int startIndex, int lastIndex, string[] pointNames,
                                double[] pointX, double[] pointY, double[] pointZ, List<Story_E> stories)
        {
            Joint[] joints = getAllJointsConnectedToWall(startIndex, lastIndex, pointNames,
                                                         pointX, pointY, pointZ);
            if (joints.Length < 2)
            {
                wallsWithoutLine++;
                return null;
            }

            string propertyName = areaPropertyName(areaName);
            double thickness = wallThicknessOf(propertyName);
            string story = wallStory(areaName, joints, stories);

            Wall_E wall = new Wall_E(areaName, joints, story, thickness, propertyName);

            if (!wall.HasCentreLine)
            {
                wallsWithoutLine++;
            }
            else if (thickness <= 0)
            {
                wallsWithoutThickness++;
            }

            return wall;
        }

        private void optainAllShellsData(ref List<Wall_E> wallsFromEtabs, List<Story_E> stories)
        {
            int NumberNames = 0;
            string[] MyName = { "" };
            eAreaDesignOrientation[] DesignOrientation = { eAreaDesignOrientation.Wall };
            int[] PointDelimiter = { 0 };
            string[] PointNames = {""};
            double[] PointX = { 0 };
            double[] PointY = { 0 };
            double[] PointZ = { 0 };

            slabsFromEtabs = new List<Slab_E>();
            nullAreasCarryingLoad = 0;
            nullAreasCarryingNothing = 0;
            nullAreasStandingUp = 0;
            areasOnOtherProperties.Clear();
            readAllAreaLoads();

            // The area list was read once with the rest of the model; see readAllShells.
            NumberNames = shellCount;
            MyName = shellNames;
            DesignOrientation = shellOrientations;
            PointDelimiter = shellPointDelimiter;
            PointNames = shellPointNames;
            PointX = shellPointX;
            PointY = shellPointY;
            PointZ = shellPointZ;

            for (int i = 0; i < NumberNames; i++)
            {
                int startIndex = ModelIndex.BoundaryStart(i, PointDelimiter);

                if (DesignOrientation[i] == eAreaDesignOrientation.Wall)
                {
                    Wall_E wall = readWall(MyName[i], startIndex, PointDelimiter[i],
                                           PointNames, PointX, PointY, PointZ, stories);
                    if (wall != null)
                    {
                        wallsFromEtabs.Add(wall);
                    }
                }
                else
                {
                    // Everything that is not a wall is offered to the slab reader, which
                    // decides for itself: an area with no slab or deck property, or one too
                    // far off flat to be a floor, is not one and is left out there.
                    Slab_E slab = readSlab(MyName[i], startIndex, PointDelimiter[i],
                                           PointNames, PointX, PointY, PointZ, stories);
                    if (slab != null)
                    {
                        slabsFromEtabs.Add(slab);
                    }
                }

            }

            reportAreasLeftOut();
        }

        #region slabs

        private List<Slab_E> slabsFromEtabs = new List<Slab_E>();

        private readonly List<string> importIssues = new List<string>();

        /// <summary>
        /// Anything the ETABS import could not make sense of, collected so it can be shown
        /// once at the end rather than as a dialog per area.
        /// </summary>
        public IList<string> ImportIssues { get { return importIssues; } }

        /// <summary>
        /// One ETABS floor area, with the property assigned to it read for its thickness.
        /// Returns null for an area that is not a floor at all: no slab or deck property on
        /// it, too few points to enclose anything, or sloped, which is a ramp rather than a
        /// slab and cannot be checked on a plan outline against a vertical thickness.
        /// </summary>
        private Slab_E readSlab(string areaName, int startIndex, int lastIndex, string[] pointNames,
                                double[] pointX, double[] pointY, double[] pointZ, List<Story_E> stories)
        {
            Joint[] joints = getAllJointsConnectedToWall(startIndex, lastIndex, pointNames, pointX, pointY, pointZ);
            if (joints.Length < 3)
            {
                return null;
            }

            bool isOpening = false;
            sp.AreaObj.GetOpening(areaName, ref isOpening);

            string propertyName = "";
            SlabKind kind = SlabKind.Opening;
            double thickness = 0;
            double overallDepth = 0;

            if (!isOpening)
            {
                propertyName = areaPropertyName(areaName);
                if (ModelIndex.IsNullArea(propertyName))
                {
                    return readLoadArea(areaName, joints, propertyName, stories);
                }
                if (!readAreaSection(propertyName, ref kind, ref thickness, ref overallDepth))
                {
                    // No slab or deck property: not a floor, so not this check's business. Counted
                    // by property all the same - a load area drawn on a property that is not called
                    // None is the one way a load can still go missing here, and the name says so.
                    int seen;
                    areasOnOtherProperties.TryGetValue(propertyName, out seen);
                    areasOnOtherProperties[propertyName] = seen + 1;
                    return null;
                }
            }

            Story_E story = Story_E.getNearestStory(getHeighestJointElevationOfWall(joints.ToList()), stories);
            Slab_E slab = new Slab_E(areaName, joints, story == null ? "" : story.Name,
                                     propertyName, kind, thickness, overallDepth, isOpening);

            if (slab.Plan == null)
            {
                importIssues.Add($"ETABS area {areaName}: the boundary does not enclose an area, left out of the slab check.");
                return null;
            }

            double flatnessTolerance = PrefrencesData.getPrefrencesData().SlabFlatnessTolerance;
            if (!slab.IsFlat(flatnessTolerance))
            {
                importIssues.Add($"ETABS area {areaName} is not flat, so it reads as a ramp rather than a " +
                                 $"slab and is left out of the slab check.");
                return null;
            }

            if (!isOpening)
            {
                slab.SetLoads(loadsByArea(areaName));
            }

            return slab;
        }

        /// <summary>
        /// A null area carrying load: no slab, but how a load that acts over part of a floor is
        /// drawn - the slab carries what acts everywhere, and a None area laid over it carries
        /// the paving, the plant, the truck route. Left out as not being a floor, every one of
        /// those loads went missing, and the slab under them read as carrying its own load
        /// alone wherever the plan asked for more.
        ///
        /// Kept only when it is flat and carries something. One carrying nothing is a meshing
        /// aid or a diaphragm, and one standing up is a wind or cladding load on a wall; neither
        /// is a load on a floor, and neither is worth a line in the issues.
        /// </summary>
        private Slab_E readLoadArea(string areaName, Joint[] joints, string propertyName,
                                    List<Story_E> stories)
        {
            List<AreaLoad_E> loads = loadsByArea(areaName);
            if (loads.Count == 0)
            {
                nullAreasCarryingNothing++;
                return null;
            }

            Story_E story = Story_E.getNearestStory(getHeighestJointElevationOfWall(joints.ToList()), stories);
            Slab_E area = new Slab_E(areaName, joints, story == null ? "" : story.Name,
                                     propertyName, SlabKind.LoadOnly, 0, 0, false);

            if (area.Plan == null || !area.IsFlat(PrefrencesData.getPrefrencesData().SlabFlatnessTolerance))
            {
                nullAreasStandingUp++;
                return null;
            }

            area.SetLoads(loads);
            nullAreasCarryingLoad++;
            return area;
        }

        /// <summary>What became of the null areas and the areas on no floor property, told once
        /// at the end of the read rather than an area at a time.</summary>
        private int nullAreasCarryingLoad;
        private int nullAreasCarryingNothing;
        private int nullAreasStandingUp;
        private readonly Dictionary<string, int> areasOnOtherProperties =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Says how many None areas were read for their load and how many were left out and
        /// why, and names any property that kept an area out of both checks. A load area the
        /// import drops says nothing on the plan - the slab under it just reads light - so this
        /// is the one place it can be seen to have happened.
        /// </summary>
        private void reportAreasLeftOut()
        {
            int nullAreas = nullAreasCarryingLoad + nullAreasCarryingNothing + nullAreasStandingUp;
            if (nullAreas > 0)
            {
                List<string> leftOut = new List<string>();
                if (nullAreasCarryingNothing > 0)
                {
                    leftOut.Add($"{nullAreasCarryingNothing} carrying no load");
                }
                if (nullAreasStandingUp > 0)
                {
                    leftOut.Add($"{nullAreasStandingUp} standing up, which is a load on a wall rather than a floor");
                }

                importIssues.Add($"None areas: {nullAreasCarryingLoad} carrying load were read for the slab load " +
                                 "check, each one's load added to the slab under it" +
                                 (leftOut.Count == 0 ? "." : "; " + string.Join(" and ", leftOut) + " were left out."));
            }

            if (areasOnOtherProperties.Count > 0)
            {
                importIssues.Add($"{areasOnOtherProperties.Values.Sum()} area(s) carry a property that is neither a " +
                                 "slab, a deck nor None, so they are in neither the slab check nor the load check: " +
                                 string.Join(", ", areasOnOtherProperties.OrderByDescending(pair => pair.Value)
                                                                         .Take(10)
                                                                         .Select(pair => $"{pair.Key} ({pair.Value})")) +
                                 (areasOnOtherProperties.Count > 10 ? ", ..." : "") + ".");
            }
        }

        #region the loads on an area

        /// <summary>
        /// Every uniform load in the model, by the area it is on, in kN/m2 and positive
        /// downwards.
        ///
        /// Read in two calls for the whole model rather than two per area. The API will answer
        /// for a group as readily as for one object, and ETABS keeps a group called "All" that
        /// holds every object in the model and cannot be deleted, so the whole model is one
        /// call. On a model with thousands of areas that is the difference between two interop
        /// round trips and several thousand of them, paid on every import whether the load
        /// check is going to be used or not.
        ///
        /// Both kinds are read. A load assigned to the shell is what a slab meshed for
        /// analysis carries, and a load assigned "to frames" is what a membrane slab hands
        /// straight to the beams around it - which of the two a model uses is a modelling
        /// choice rather than a difference in what the slab is designed for, and reading only
        /// one of them would report half the models in the world as carrying nothing.
        ///
        /// A load set assigned in the ETABS interface is not a thing the API knows about: it
        /// arrives here as the uniform loads the set puts on, one per pattern, which is what
        /// the slab actually carries and so the right thing to compare.
        /// </summary>
        private void readAllAreaLoads()
        {
            areaLoads = new Dictionary<string, List<AreaLoad_E>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                readUniformLoads();
                readUniformToFrameLoads();
                readLoadSetLoads();
            }
            catch (Exception e)
            {
                importIssues.Add("The area loads could not be read out of ETABS, so the slabs are " +
                                 $"checked for thickness as usual and left out of the load check - {e.Message}");
            }
        }

        private List<AreaLoad_E> loadsByArea(string areaName)
        {
            List<AreaLoad_E> loads;
            return areaLoads != null && areaLoads.TryGetValue(areaName, out loads)
                ? loads
                : new List<AreaLoad_E>();
        }

        private void record(string areaName, AreaLoad_E load)
        {
            if (load == null || string.IsNullOrEmpty(areaName))
            {
                return;
            }

            List<AreaLoad_E> loads;
            if (!areaLoads.TryGetValue(areaName, out loads))
            {
                loads = new List<AreaLoad_E>();
                areaLoads[areaName] = loads;
            }

            loads.Add(load);
        }

        private void readUniformLoads()
        {
            int count = 0;
            string[] areaNames = { "" };
            string[] patterns = { "" };
            string[] systems = { "" };
            int[] directions = { 0 };
            double[] values = { 0 };

            if (sp.AreaObj.GetLoadUniform(AllObjectsGroup, ref count, ref areaNames, ref patterns,
                                          ref systems, ref directions, ref values,
                                          eItemType.Group) != 0)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                record(areaNames[i], buildAreaLoad(patterns[i], directions[i], values[i], false));
            }
        }

        private void readUniformToFrameLoads()
        {
            int count = 0;
            string[] areaNames = { "" };
            string[] patterns = { "" };
            string[] systems = { "" };
            int[] directions = { 0 };
            double[] values = { 0 };
            int[] distributionTypes = { 0 };

            if (sp.AreaObj.GetLoadUniformToFrame(AllObjectsGroup, ref count, ref areaNames, ref patterns,
                                                 ref systems, ref directions, ref values,
                                                 ref distributionTypes, eItemType.Group) != 0)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                record(areaNames[i], buildAreaLoad(patterns[i], directions[i], values[i], true));
            }
        }

        /// <summary>
        /// The group ETABS keeps holding every object in the model. It is made with the model
        /// and cannot be deleted, so asking for it is a safe way to say "all of them" to an
        /// API that has no word for it.
        /// </summary>
        #region load sets

        /// <summary>
        /// The uniform load sets assigned to shells, read out of the model's tables and added
        /// to whatever each shell already carries.
        ///
        /// Added, not substituted: a shell can carry a load set and its own uniform loads at
        /// once, and it carries the sum of them.
        ///
        /// This goes through the tables because there is nowhere else for it to go. The whole
        /// of ETABSv1 has no member that mentions a load set, so a shell carrying one used to
        /// read as carrying nothing - and be reported as failing its loading plan, which is a
        /// wrong answer rather than a missing one.
        /// </summary>
        private void readLoadSetLoads()
        {
            LoadSetRead fromFile = PrefrencesData.getPrefrencesData().ReadFromEtabsTextFile
                ? readLoadSetsFromTextFile()
                : null;

            // The tables are read only when the file was not, or had nothing to say. They are
            // the same answer reached the slow way - on a tower the difference is half a second
            // against a quarter of an hour - so they are the fallback rather than the route.
            LoadSetRead chosen = fromFile != null && fromFile.Answered
                ? fromFile
                : readLoadSetsFromModelTables();

            // Only ever one of them is put on the shells. Reading both and adding both would
            // give every shell twice the load it carries.
            if (chosen != null && chosen.Answered)
            {
                applyLoadSets(chosen);
            }
        }

        /// <summary>
        /// What one route to the load sets came back with.
        ///
        /// The two routes are made to hand back the same thing so that either can be put on the
        /// shells without the rest of the import knowing which one it was.
        /// </summary>
        private sealed class LoadSetRead
        {
            public Dictionary<string, List<LoadSetTables.PatternValue>> Sets;
            public Dictionary<string, string> Assigned;
            public double Factor;

            /// <summary>
            /// Whether this route got anywhere. A model with no load sets in it answers "no"
            /// from both routes, which is a true answer rather than a failure.
            /// </summary>
            public bool Answered
            {
                get { return Sets != null && Assigned != null && Sets.Count > 0 && Assigned.Count > 0; }
            }
        }

        /// <summary>
        /// The same two tables, out of the model's own tables through the API.
        ///
        /// This is what runs when there is no text file to read, or when the setting asking for
        /// one is off. It is the slower of the two by a long way on a large model - every cell
        /// of a table with a row per shell comes across the COM boundary as its own string -
        /// but it is reading the model that is open rather than the last one that was saved,
        /// so it is the one that is always right.
        /// </summary>
        private LoadSetRead readLoadSetsFromModelTables()
        {
            LoadSetRead read = new LoadSetRead();

            string definitionsKey = tableKeyFor(LoadSetTables.DefinitionsTable);
            string assignmentsKey = tableKeyFor(LoadSetTables.AssignmentsTable);

            if (definitionsKey == null || assignmentsKey == null)
            {
                // No load sets defined in this model at all is the usual reason, and is not
                // worth a word. A model that has them and will not hand them over is.
                return read;
            }

            string units;
            double factor = loadSetValueFactor(definitionsKey, out units);
            if (factor <= 0)
            {
                return read;
            }

            string[] definitionFields, definitionData;
            int definitionCount;
            if (!readTable(definitionsKey, out definitionFields, out definitionData, out definitionCount))
            {
                return read;
            }

            string[] assignmentFields, assignmentData;
            int assignmentCount;
            if (!readTable(assignmentsKey, out assignmentFields, out assignmentData, out assignmentCount))
            {
                return read;
            }

            read.Sets = LoadSetTables.Definitions(definitionFields, definitionData, definitionCount,
                                                  importIssues);

            read.Assigned = LoadSetTables.Assignments(assignmentFields, assignmentData, assignmentCount,
                                                      importIssues);

            read.Factor = factor;

            return read;
        }

        /// <summary>
        /// Puts the load a set carries onto every shell that carries the set, whichever of the
        /// two sources the sets and the assignments were read from.
        ///
        /// Added to whatever the shell already carries, not substituted for it: a shell can
        /// carry a load set and its own uniform loads at once, and it carries the sum.
        ///
        /// The assignments arriving here are already keyed by the shell's unique name, because
        /// that is what the rest of the import knows a shell by. Reaching that from the text
        /// file takes a step the tables do not need, and it is taken before this is called.
        /// </summary>
        private void applyLoadSets(LoadSetRead read)
        {
            HashSet<string> undefined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, string> assignment in read.Assigned)
            {
                List<LoadSetTables.PatternValue> lines;
                if (!read.Sets.TryGetValue(assignment.Value, out lines))
                {
                    undefined.Add(assignment.Value);
                    continue;
                }

                foreach (LoadSetTables.PatternValue line in lines)
                {
                    // A load set puts its load straight down; the definition carries no
                    // direction because there is nothing else it could mean.
                    record(assignment.Key, new AreaLoad_E(line.Pattern, line.Value * read.Factor,
                                                          GravityDirection, true, false));
                }
            }

            if (undefined.Count > 0)
            {
                importIssues.Add($"{undefined.Count} load set(s) are assigned to shells but not defined: " +
                                 string.Join(", ", undefined.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)));
            }
        }

        #region out of the model text file

        /// <summary>
        /// The same two tables, read off the disk instead.
        ///
        /// ETABS writes a text file beside the model every time it is saved - the .$et - and
        /// the whole model is in it, load sets and all. Reading it is the difference between a
        /// table with a row per shell coming across the COM boundary a cell at a time and a
        /// sequential read of a file: on a tower with a hundred and fifty thousand shells the
        /// file takes a fraction of a second.
        ///
        /// The catch is that it is a snapshot. It is as old as the last save, so a load set
        /// changed since then is not in it, and it may not be there at all. What is done about
        /// that is the setting: this is only reached when it is on, it hands back to the tables
        /// whenever it cannot answer, and it says how old the file was so that a number that
        /// looks wrong can be explained rather than puzzled over.
        ///
        /// A read that did not answer hands back a <see cref="LoadSetRead"/> with nothing in it,
        /// and the tables are asked instead.
        /// </summary>
        private LoadSetRead readLoadSetsFromTextFile()
        {
            LoadSetRead read = new LoadSetRead();

            string modelPath = modelFilePath();
            string textPath = EtabsTextFile.Beside(modelPath);

            if (textPath == null || !File.Exists(textPath))
            {
                // A model that has never been saved, or one saved by a version that does not
                // write the file. Usual enough not to be worth a word.
                return read;
            }

            EtabsTextTables tables;

            try
            {
                tables = EtabsTextFile.Read(textPath, EtabsTable.ShellUniformLoadSets
                                                      | EtabsTable.ShellLoadSetAssignments);
            }
            catch (Exception e)
            {
                importIssues.Add($"The ETABS text file \"{textPath}\" could not be read, so the load " +
                                 $"sets were read from the model tables instead - {e.Message}");
                return read;
            }

            foreach (string issue in tables.Issues)
            {
                importIssues.Add(issue);
            }

            Dictionary<string, List<LoadSetTables.PatternValue>> sets = definitionsFrom(tables);

            if (sets.Count == 0)
            {
                // Either this model has no load sets - in which case the tables will say the
                // same thing in a moment and no harm is done - or the file is old enough to
                // predate the ones it has. Handing back covers both, and it is the one case
                // where a stale file could quietly cost a whole check.
                return read;
            }

            if (tables.ShellLoadSetAssignments.Count == 0)
            {
                // The file defines load sets and puts none of them on anything. Either no shell
                // in this model carries one - in which case the tables are about to say the same
                // and nothing is lost but a moment - or the section is written in a shape this
                // reader does not know, and every shell would read as carrying nothing. There is
                // no telling the two apart from here, and only one of them is safe to assume.
                return read;
            }

            double factor = LoadSetTables.AreaLoadFactorFrom(tables.Units.Force, tables.Units.Length);
            if (factor <= 0)
            {
                importIssues.Add(tables.Units.AreKnown
                    ? $"The ETABS text file is written in \"{tables.Units}\", which is not a unit this " +
                      "can convert, so the load sets were read from the model tables instead."
                    : "The ETABS text file does not say what units it is written in, so the load sets " +
                      "were read from the model tables instead.");
                return read;
            }

            int unmatched;
            Dictionary<string, string> assigned = assignmentsByAreaName(tables, out unmatched);

            if (assigned == null)
            {
                return read;
            }

            if (assigned.Count == 0 && unmatched > 0)
            {
                // Every shell the file names is one this model has not got, which means the
                // file is describing a different model altogether rather than an older version
                // of this one. Nothing in it can be trusted onto anything here.
                importIssues.Add($"None of the {unmatched} shells named in the ETABS text file are in " +
                                 "this model, so it is describing a different model and the load sets " +
                                 "were read from the model tables instead. Save the model in ETABS to " +
                                 "write a text file that matches it.");
                return read;
            }

            read.Sets = sets;
            read.Assigned = assigned;
            read.Factor = factor;

            reportUnmatchedShells(unmatched, assigned.Count);

            return read;
        }

        /// <summary>
        /// The definitions from the file, gathered by set name into the shape the two sources
        /// share. A set names one row per load pattern it puts on.
        /// </summary>
        internal static Dictionary<string, List<LoadSetTables.PatternValue>> definitionsFrom(
            EtabsTextTables tables)
        {
            Dictionary<string, List<LoadSetTables.PatternValue>> sets =
                new Dictionary<string, List<LoadSetTables.PatternValue>>(StringComparer.OrdinalIgnoreCase);

            foreach (ShellUniformLoadSetRow row in tables.ShellUniformLoadSets)
            {
                List<LoadSetTables.PatternValue> lines;
                if (!sets.TryGetValue(row.Name, out lines))
                {
                    lines = new List<LoadSetTables.PatternValue>(4);
                    sets[row.Name] = lines;
                }

                lines.Add(new LoadSetTables.PatternValue
                {
                    Pattern = row.LoadPattern,
                    Value = row.Value
                });
            }

            return sets;
        }

        /// <summary>
        /// The assignments from the file, turned from what the file calls a shell into what the
        /// rest of the import calls one.
        ///
        /// This is the one thing the text file cannot do on its own. It names a shell by its
        /// label and its story - "F1" on "L01" - because that is what an engineer sees; the API
        /// names it by a unique name, and a label on its own is not unique, since the F1 on L01
        /// and the F1 on L02 are two different shells. ETABS will hand over all three for every
        /// area in one call, so the join costs one round trip however big the model is.
        ///
        /// Null when that call fails, which leaves nothing to join against and so nothing this
        /// can do; the count of assignments naming a shell the model has not got is handed back
        /// instead, because that is the honest measure of whether the file still describes this
        /// model.
        /// </summary>
        private Dictionary<string, string> assignmentsByAreaName(EtabsTextTables tables, out int unmatched)
        {
            unmatched = 0;

            int count = 0;
            string[] names = { "" };
            string[] labels = { "" };
            string[] stories = { "" };

            try
            {
                if (sp.AreaObj.GetLabelNameList(ref count, ref names, ref labels, ref stories) != 0
                    || names == null || labels == null || stories == null)
                {
                    importIssues.Add("ETABS would not say which area each label belongs to, so the load " +
                                     "sets were read from the model tables instead.");
                    return null;
                }
            }
            catch (Exception e)
            {
                importIssues.Add("ETABS would not say which area each label belongs to, so the load " +
                                 $"sets were read from the model tables instead - {e.Message}");
                return null;
            }

            return joinToAreaNames(tables.ShellLoadSetAssignments, count, names, labels, stories,
                                   out unmatched);
        }

        /// <summary>
        /// The join itself, with the ETABS call already made and its three arrays handed in.
        ///
        /// Kept apart from the call that fetches them because this is the one piece of the text
        /// file route that can quietly give a wrong answer rather than no answer - a join that
        /// misses everything leaves every shell reading as carrying nothing - and apart from
        /// ETABS it can be tested.
        /// </summary>
        internal static Dictionary<string, string> joinToAreaNames(
            IList<ShellLoadSetAssignmentRow> rows,
            int count, string[] names, string[] labels, string[] stories, out int unmatched)
        {
            unmatched = 0;

            Dictionary<string, string> areaNames =
                new Dictionary<string, string>(count, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < count && i < names.Length && i < labels.Length && i < stories.Length; i++)
            {
                areaNames[labelAndStory(labels[i], stories[i])] = names[i];
            }

            Dictionary<string, string> assigned =
                new Dictionary<string, string>(rows.Count, StringComparer.OrdinalIgnoreCase);

            foreach (ShellLoadSetAssignmentRow row in rows)
            {
                string areaName;
                if (!areaNames.TryGetValue(labelAndStory(row.Label, row.Story), out areaName))
                {
                    unmatched++;
                    continue;
                }

                assigned[areaName] = row.LoadSet;
            }

            return assigned;
        }

        /// <summary>
        /// The two together, as one key. Joined on a character that cannot be in either name,
        /// so a story called "A" with a label "B-C" cannot collide with a story "A-B" and a
        /// label "C".
        /// </summary>
        private static string labelAndStory(string label, string story)
        {
            return (label ?? "") + "\0" + (story ?? "");
        }

        /// <summary>
        /// How much of the file still fits the model.
        ///
        /// The file is as old as the last save, and how old that is says nothing on its own: a
        /// file written a week ago that still joins onto every shell is describing this model,
        /// and one written a minute ago that joins onto none of them is describing another. The
        /// count of assignments naming shells this model has not got is the measure that
        /// separates the two, so it is the one said out loud - and only when there are any.
        /// </summary>
        private void reportUnmatchedShells(int unmatched, int matched)
        {
            if (unmatched > 0)
            {
                importIssues.Add($"{unmatched} load set assignment(s) in the ETABS text file name a shell " +
                                 $"this model has not got, against {matched} that were matched. The file is " +
                                 "describing an older model - those shells read as carrying no load set. " +
                                 "Save the model in ETABS to bring the file up to date.");
            }
        }

        /// <summary>
        /// Where the open model is saved, or null for one that has never been saved and so has
        /// no text file beside it either.
        /// </summary>
        /// <summary>
        /// The open model's file name for the header bar, or a word saying there is none. The
        /// whole path is not wanted up there - the header says which model is loaded, not where
        /// it lives.
        /// </summary>
        public string ModelName
        {
            get
            {
                string path = modelFilePath();
                return string.IsNullOrEmpty(path)
                    ? "Unsaved model"
                    : System.IO.Path.GetFileName(path);
            }
        }

        private string modelFilePath()
        {
            try
            {
                string path = sp.GetModelFilename(true);
                return string.IsNullOrEmpty(path) ? null : path;
            }
            catch (Exception)
            {
                return null;
            }
        }

        #endregion

        /// <summary>What ETABS calls gravity: straight down, whatever the axes are doing.</summary>
        private const int GravityDirection = 10;

        /// <summary>
        /// The key this model uses for a table, matched on the name rather than assumed, so a
        /// version that spaces or punctuates it differently is still found. Null when the model
        /// does not have that table, which is what a model with no load sets in it looks like.
        /// </summary>
        private string tableKeyFor(string tableName)
        {
            int count = 0;
            string[] keys = { "" };
            string[] names = { "" };
            int[] importTypes = { 0 };

            try
            {
                if (sp.DatabaseTables.GetAvailableTables(ref count, ref keys, ref names,
                                                         ref importTypes) != 0 || keys == null)
                {
                    return null;
                }
            }
            catch (Exception)
            {
                return null;
            }

            string wanted = LoadSetTables.Normalise(tableName);

            for (int i = 0; i < count && i < keys.Length; i++)
            {
                if (LoadSetTables.Normalise(keys[i]) == wanted
                    || (names != null && i < names.Length && LoadSetTables.Normalise(names[i]) == wanted))
                {
                    return keys[i];
                }
            }

            return null;
        }

        /// <summary>
        /// How many kN/m2 one unit of the load value column is.
        ///
        /// Asked of the table rather than worked out from the database units, because a table
        /// hands its numbers over in whatever units the model is being shown in and says which
        /// those are. Nought when they cannot be made out, which is reported and stops the
        /// read: a load set scaled by a thousand is worse than one that was never read.
        /// </summary>
        private double loadSetValueFactor(string tableKey, out string units)
        {
            units = "";

            int tableVersion = 0;
            int count = 0;
            string[] fieldKeys = { "" };
            string[] fieldNames = { "" };
            string[] descriptions = { "" };
            string[] unitsStrings = { "" };
            bool[] isImportable = { false };

            try
            {
                if (sp.DatabaseTables.GetAllFieldsInTable(tableKey, ref tableVersion, ref count,
                                                          ref fieldKeys, ref fieldNames,
                                                          ref descriptions, ref unitsStrings,
                                                          ref isImportable) != 0 || fieldKeys == null)
                {
                    return -1;
                }
            }
            catch (Exception)
            {
                return -1;
            }

            int valueColumn = LoadSetTables.ColumnOf(fieldKeys, "LoadValue", "Value", "Load");
            if (valueColumn < 0 || unitsStrings == null || valueColumn >= unitsStrings.Length)
            {
                return -1;
            }

            units = unitsStrings[valueColumn] ?? "";
            double factor = LoadSetTables.AreaLoadFactorFrom(units);

            if (factor <= 0)
            {
                importIssues.Add($"The load set values in this model are in \"{units}\", which is not a " +
                                 "unit this can convert, so the load sets were not read and the shells " +
                                 "carrying them will read as carrying nothing.");
                return -1;
            }

            return factor;
        }

        /// <summary>One table, whole.</summary>
        private bool readTable(string tableKey, out string[] fieldKeys, out string[] data, out int records)
        {
            string[] wantedFields = new string[0];
            int tableVersion = 0;

            fieldKeys = new string[0];
            data = new string[0];
            records = 0;

            try
            {
                return sp.DatabaseTables.GetTableForDisplayArray(tableKey, ref wantedFields, "",
                                                                 ref tableVersion, ref fieldKeys,
                                                                 ref records, ref data) == 0
                       && fieldKeys != null && data != null;
            }
            catch (Exception e)
            {
                importIssues.Add($"The ETABS table \"{tableKey}\" could not be read - {e.Message}");
                return false;
            }
        }

        #endregion

        private const string AllObjectsGroup = "All";

        /// <summary>Every uniform load in the model, by the area object it sits on.</summary>
        private Dictionary<string, List<AreaLoad_E>> areaLoads;

        private AreaLoad_E buildAreaLoad(string pattern, int direction, double value, bool toFrame)
        {
            double sign;
            bool gravity = AreaLoad_E.IsDownwardDirection(direction, out sign);

            double inKilonewtonsPerSquareMetre = value * areaLoadFactor();

            return new AreaLoad_E(pattern,
                                  gravity ? sign * inKilonewtonsPerSquareMetre : inKilonewtonsPerSquareMetre,
                                  direction, gravity, toFrame);
        }

        /// <summary>
        /// What one unit of area load in the model's database units is in kN/m2.
        ///
        /// The database units are whatever the model was built in, and a force over a length
        /// squared has to have both converted: the force to newtons and then to kilonewtons,
        /// the length to millimetres and then to metres. A model in kN and m comes out
        /// unchanged, which is the case worth checking by eye.
        /// </summary>
        private double areaLoadFactor()
        {
            double forceInNewtons = FrameData_e.getForcefromUnit(eForceUnit, 1d);
            double lengthInMillimetres = FrameData_e.getLengthFromUnit(etabsDbUnites, 1d);

            if (lengthInMillimetres <= 0)
            {
                return 1;
            }

            return forceInNewtons * 1000d / (lengthInMillimetres * lengthInMillimetres);
        }

        #endregion

        /// <summary>
        /// Thickness of an area property in mm. Slab properties are tried first and deck
        /// properties after, and anything that is neither says so by returning false.
        ///
        /// A ribbed or a waffle property carries two depths, and which one to compare against
        /// Revit depends on how the ribs were modelled there, so both are read and the choice
        /// is left to <see cref="PrefrencesData.CompareRibbedSlabsByOverallDepth"/>.
        /// </summary>
        /// <summary>What one slab or deck property turned out to be, kept per property name.</summary>
        private sealed class AreaSection
        {
            public bool IsFloor;
            public SlabKind Kind;
            public double Thickness;
            public double OverallDepth;
        }

        private readonly Dictionary<string, AreaSection> areaSections =
            new Dictionary<string, AreaSection>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The slab or deck behind one area property. A model has a few slab properties and
        /// thousands of areas on them, so each property is read once and the answer kept: the
        /// property does not change while the model is being read.
        /// </summary>
        private bool readAreaSection(string propertyName, ref SlabKind kind, ref double thickness,
                                     ref double overallDepth)
        {
            AreaSection cached;
            if (!areaSections.TryGetValue(propertyName, out cached))
            {
                SlabKind readKind = SlabKind.Unknown;
                double readThickness = 0;
                double readDepth = 0;

                bool isFloor = readAreaSectionFromEtabs(propertyName, ref readKind, ref readThickness,
                                                        ref readDepth);

                cached = new AreaSection
                {
                    IsFloor = isFloor,
                    Kind = readKind,
                    Thickness = readThickness,
                    OverallDepth = readDepth
                };

                areaSections.Add(propertyName, cached);
            }

            if (!cached.IsFloor)
            {
                return false;
            }

            kind = cached.Kind;
            thickness = cached.Thickness;
            overallDepth = cached.OverallDepth;
            return true;
        }

        private bool readAreaSectionFromEtabs(string propertyName, ref SlabKind kind, ref double thickness,
                                     ref double overallDepth)
        {
            eSlabType slabType = eSlabType.Slab;
            eShellType shellType = eShellType.ShellThin;
            string matProp = "";
            double propertyThickness = 0;
            int color = 0;
            string notes = "";
            string guid = "";

            if (sp.PropArea.GetSlab(propertyName, ref slabType, ref shellType, ref matProp,
                                    ref propertyThickness, ref color, ref notes, ref guid) == 0)
            {
                kind = slabKindFrom(slabType);
                thickness = FrameData_e.getLengthFromUnit(etabsDbUnites, propertyThickness);
                overallDepth = thickness;

                double sectionDepth = 0;
                double toppingThickness = 0;
                if (readRibbedSection(propertyName, slabType, ref sectionDepth, ref toppingThickness))
                {
                    overallDepth = FrameData_e.getLengthFromUnit(etabsDbUnites, sectionDepth);
                    thickness = FrameData_e.getLengthFromUnit(etabsDbUnites, toppingThickness);
                }

                return true;
            }

            eDeckType deckType = eDeckType.Filled;
            if (sp.PropArea.GetDeck(propertyName, ref deckType, ref shellType, ref matProp,
                                    ref propertyThickness, ref color, ref notes, ref guid) == 0)
            {
                kind = SlabKind.Deck;
                thickness = FrameData_e.getLengthFromUnit(etabsDbUnites, propertyThickness);
                overallDepth = thickness;
                return true;
            }

            return false;
        }

        private bool readRibbedSection(string propertyName, eSlabType slabType,
                                       ref double overallDepth, ref double toppingThickness)
        {
            double stemWidthTop = 0;
            double stemWidthBottom = 0;
            double ribSpacing = 0;

            if (slabType == eSlabType.Ribbed)
            {
                int ribsParallelTo = 0;
                return sp.PropArea.GetSlabRibbed(propertyName, ref overallDepth, ref toppingThickness,
                                                 ref stemWidthTop, ref stemWidthBottom, ref ribSpacing,
                                                 ref ribsParallelTo) == 0;
            }

            if (slabType == eSlabType.Waffle)
            {
                double ribSpacingDir2 = 0;
                return sp.PropArea.GetSlabWaffle(propertyName, ref overallDepth, ref toppingThickness,
                                                 ref stemWidthTop, ref stemWidthBottom, ref ribSpacing,
                                                 ref ribSpacingDir2) == 0;
            }

            return false;
        }

        private static SlabKind slabKindFrom(eSlabType slabType)
        {
            switch (slabType)
            {
                case eSlabType.Slab:
                    return SlabKind.Slab;
                case eSlabType.Drop:
                    return SlabKind.Drop;
                case eSlabType.Ribbed:
                    return SlabKind.Ribbed;
                case eSlabType.Waffle:
                    return SlabKind.Waffle;
                case eSlabType.Mat:
                    return SlabKind.Mat;
                case eSlabType.Footing:
                    return SlabKind.Footing;
                default:
                    // The stiff type is called Stiff_DO_NOT_USE in the ETABS copy of the API,
                    // and SAFE draws one under every column. Read by name, so it is known
                    // whichever copy of the assembly the build picked up.
                    return slabType.ToString().StartsWith("Stiff", StringComparison.OrdinalIgnoreCase)
                        ? SlabKind.Stiff
                        : SlabKind.Unknown;
            }
        }

        #endregion

        #region the section property on each area

        /// <summary>
        /// Which section property each area carries, read for the whole model in one go.
        ///
        /// The API answers this one area at a time, and every one of those is a COM round trip:
        /// on a tower whose floors and cores are drawn as thousands of areas that is thousands
        /// of crossings for something the model's own table hands over in a single read. The
        /// table is a fast path and nothing depends on it - a model whose ETABS names it
        /// differently, or whose columns are not the ones expected, falls back to asking per
        /// area exactly as before, and the answer is the same either way.
        /// </summary>
        private Dictionary<string, string> areaProperties;

        /// <summary>
        /// What this table has been called. Matched on the name rather than assumed, and tried
        /// in turn, because the wording has moved between versions.
        /// </summary>
        private static readonly string[] AreaSectionTableNames =
        {
            "Area Assignments - Section Assignments",
            "Area Section Assignments",
            "Shell Assignments - Section Properties"
        };

        private string areaPropertyName(string areaName)
        {
            if (areaProperties == null)
            {
                areaProperties = readAreaProperties();
            }

            string assigned;
            if (areaProperties.TryGetValue(areaName, out assigned))
            {
                return assigned;
            }

            // Not in the table, or there was no table: ask for this one area.
            string propertyName = "";
            if (sp.AreaObj.GetProperty(areaName, ref propertyName) != 0)
            {
                return "";
            }
            return propertyName ?? "";
        }

        private Dictionary<string, string> readAreaProperties()
        {
            Dictionary<string, string> empty =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string tableName in AreaSectionTableNames)
            {
                string tableKey = tableKeyFor(tableName);
                if (tableKey == null)
                {
                    continue;
                }

                string[] fieldKeys;
                string[] data;
                int records;
                if (!readTable(tableKey, out fieldKeys, out data, out records))
                {
                    continue;
                }

                Dictionary<string, string> assignments =
                    AreaSectionTables.Assignments(fieldKeys, data, records);

                if (assignments.Count > 0 && agreesWithTheApi(assignments, tableName))
                {
                    return assignments;
                }
            }

            return empty;
        }

        /// <summary>
        /// Checks the table against the API on a handful of areas before the import is let
        /// anywhere near it.
        ///
        /// A table read is a guess about a name and a set of columns, and the way it fails is
        /// the bad way: not an error, but plausible answers that are quietly the wrong ones - a
        /// column of area names read as property names would leave every wall without a
        /// thickness and every floor not read as a floor at all. Five calls settle it, and the
        /// import goes back to asking per area if they do not agree.
        /// </summary>
        private bool agreesWithTheApi(Dictionary<string, string> assignments, string tableName)
        {
            const int sampleSize = 5;

            // How far to look for five areas the table has something to say about. An opening
            // carries no property and so cannot settle anything; a run of them at the top of
            // the list should not turn the check into a walk over every area in the model.
            const int scanLimit = 1000;

            int checked_ = 0;

            for (int i = 0; i < shellCount && i < scanLimit && checked_ < sampleSize; i++)
            {
                string areaName = shellNames[i];
                string fromTable;

                // An area the table has nothing for - an opening carries no property - says
                // nothing either way, so it is passed over rather than counted as a difference.
                if (string.IsNullOrEmpty(areaName) || !assignments.TryGetValue(areaName, out fromTable))
                {
                    continue;
                }

                string fromApi = "";
                if (sp.AreaObj.GetProperty(areaName, ref fromApi) != 0)
                {
                    continue;
                }

                checked_++;

                if (!string.Equals((fromApi ?? "").Trim(), fromTable, StringComparison.OrdinalIgnoreCase))
                {
                    importIssues.Add($"The ETABS table \"{tableName}\" does not say what the model says: "
                                     + $"area {areaName} carries \"{fromApi}\" but the table has "
                                     + $"\"{fromTable}\". The table was left alone and the section "
                                     + "properties were read one area at a time instead, which is slower "
                                     + "and right.");
                    return false;
                }
            }

            return checked_ > 0;
        }

        #endregion

        /// <summary>
        /// The thickness one wall section property is worth, in mm. The property is read once
        /// however many areas carry it - a core is one property and hundreds of areas.
        ///
        /// Nought where the area has no wall property, or one this cannot read, which the
        /// caller reports rather than passing off as a wall with no thickness.
        /// </summary>
        private double wallThicknessOf(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
            {
                return 0;
            }

            return FrameData_e.getLengthFromUnit(etabsDbUnites, getWallPropertyThickness(propertyName));
        }

        private readonly Dictionary<string, double> wallThicknesses =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        private double getWallPropertyThickness(string propertyName)
        {
            double cached;
            if (wallThicknesses.TryGetValue(propertyName, out cached))
            {
                return cached;
            }

            eWallPropType WallPropType = eWallPropType.AutoSelectList;
            eShellType ShellType = eShellType.ShellThin;
            string MatProp = "";
            double Thickness = 0;
            int color = 0;
            string notes = "";
            string GUID = "";

            sp.PropArea.GetWall(propertyName, ref WallPropType, ref ShellType, ref MatProp, ref Thickness, ref color, ref notes, ref GUID);

            wallThicknesses.Add(propertyName, Thickness);
            return Thickness;
        }

        private string getWallStory(Joint[] wallJoints, List<Story_E> stories)
        {
            double heighestJointElevationOfWall = getHeighestJointElevationOfWall(wallJoints.ToList());
            Story_E nearest = Story_E.getNearestStory(heighestJointElevationOfWall, stories);
            return nearest == null ? "" : nearest.Name;
        }

        public static double getHeighestJointElevationOfWall(List<Joint> wallJoints)
        {
            double v = wallJoints[0].Point.Z;
            foreach (var joint in wallJoints)
            {
                if (joint.Point.Z >= v)
                {
                    v = joint.point.Z;
                }
            }
            return v;
        }

        

        private Joint[] getAllJointsConnectedToWall(int startIndex, int lastIndex, string[] pointNames, double[] pointX, double[] pointY, double[] pointZ)
        {
            int n = lastIndex - startIndex+1;
            Joint[] joints = new Joint[n];

            for (int i = 0; i < n; i++) // should be checked
            {
                MyPoint PT = new MyPoint(pointX[lastIndex - i], pointY[lastIndex - i], pointZ[lastIndex - i]);
                joints[i] = new Joint(pointNames[lastIndex - i], PT, etabsDbUnites);
               
            }
            return joints;
        }

        private List<Story_E> getAllStories()
        {
            // One floor, and nothing in the model that says which floor it is. The story is
            // the one the user paired it with rather than anything read out of the program.
            if (IsSingleLevel)
            {
                return new List<Story_E> { singleLevelStory() };
            }

            int NumberStories = 0;
            string[] StoryNames = { "" };
            double[] StoryHeights = { 0 };
            double[] StoryElevations = { 0 };
            bool[] IsMasterStory = { true };
            string[] SimilarToStory = { "" };
            bool[] SpliceAbove = { true };
            double[] SpliceHeight = { 0 };


            sp.Story.GetStories(ref NumberStories, ref StoryNames, ref StoryElevations, ref StoryHeights, 

                                  ref IsMasterStory,ref SimilarToStory, ref SpliceAbove, ref SpliceHeight);
            List<Story_E> stories = new List<Story_E>();
            for (int i = 0; i <= NumberStories; i++)
            {
                Story_E story = new Story_E(StoryNames[i], StoryElevations[i], etabsDbUnites);

                // Which master a typical floor was built from, for guessing its loading plan
                // when the view is named after the master alone.
                bool master = IsMasterStory == null || i >= IsMasterStory.Length || IsMasterStory[i];
                string similar = SimilarToStory != null && i < SimilarToStory.Length
                    ? (SimilarToStory[i] ?? "").Trim()
                    : "";
                if (!master && similar.Length > 0
                    && !string.Equals(similar, "None", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(similar, StoryNames[i], StringComparison.OrdinalIgnoreCase))
                {
                    story.SimilarTo = similar;
                }

                stories.Add(story);
            }

            return stories;
        }

        private void getDesignRFt()
        {
            readAllBeamDesignResults();

            int designed = 0;

            foreach (var frame in framesFromEtabs)
            {
                double[] TopArea = { 0 };
                double[] BotArea = { 0 };
                double[] Vmajor = { 0 };
                double[] loc = { 0 };

                int ret = OptainAsFromEtabs(frame.etabsUname, ref TopArea , ref BotArea ,ref Vmajor, ref loc);
                if (ret == 0)
                {
                    designed++;
                    frame.setTotalTopSteel(TopArea,etabsDbUnites);
                    frame.setTotalBotSteel(BotArea, etabsDbUnites);
                    frame.setTotalLinks(Vmajor);

                    frame.setSteelLocations( loc,etabsDbUnites);

                    double asT1 = getMaxAs(asLocation.left, TopArea , loc);
                    double asT2 = getMaxAs(asLocation.mid, TopArea , loc);
                    double asT3 = getMaxAs(asLocation.right, TopArea, loc);

                    double asb1 = getMaxAs(asLocation.left, BotArea, loc);
                    double asb2 = getMaxAs(asLocation.mid, BotArea, loc);
                    double asb3 = getMaxAs(asLocation.right, BotArea, loc);


                    double asS1 = getMaxAs(asLocation.left, Vmajor, loc);
                    double asS2 = getMaxAs(asLocation.mid, Vmajor, loc);
                    double asS3 = getMaxAs(asLocation.right, Vmajor, loc);

                    frame.setRFT_e(asT1, asT2, asT3, asb1, asb2, asb3,asS1,asS2,asS3, etabsDbUnites);
                }
            }

            // No beam anywhere in the model came back with steel in it. The beams still import
            // and are still checked for everything geometric; what cannot be checked is the
            // reinforcement mark, which has nothing to be compared against. Said outright,
            // because a mark check that quietly reports nothing looks exactly like a mark check
            // that found nothing wrong.
            //
            // The ordinary cause is the concrete design not having been run. For SAFE it is
            // the ordinary state of affairs: SAFE designs slabs, and its beam design is not the
            // ETABS beam design this reads.
            if (framesFromEtabs.Count > 0 && designed == 0)
            {
                importIssues.Add(
                    $"No beam in this {CsiProducts.Name(product)} model returned design "
                    + "reinforcement, so the beam mark check has nothing to compare against and "
                    + "every beam will read as not checked. "
                    + (IsSingleLevel
                        ? "SAFE designs slabs rather than beams, so this is expected for a SAFE model."
                        : "Run the concrete design in ETABS and import again."));
            }
        }

        #region the design results

        /// <summary>The design summary for one beam: a row per station along it.</summary>
        private sealed class BeamDesign
        {
            public readonly List<double> Location = new List<double>();
            public readonly List<double> TopArea = new List<double>();
            public readonly List<double> BotArea = new List<double>();
            public readonly List<double> VMajorArea = new List<double>();
        }

        private Dictionary<string, BeamDesign> beamDesign;

        /// <summary>
        /// The concrete design summary for every beam in the model, in one call.
        ///
        /// The API answers for a group as readily as for one frame, and ETABS keeps a group
        /// called "All" holding every object in the model that cannot be deleted - the same
        /// trick the area loads are read with. On a tower this is one round trip in place of
        /// one per beam.
        ///
        /// If the model will not answer for the group - the design has not been run, or the
        /// build does not take a group here - this is left null and every beam is asked about
        /// on its own, exactly as before.
        /// </summary>
        private void readAllBeamDesignResults()
        {
            beamDesign = null;

            int numberItems = 0;
            string[] frameName = { "" };
            double[] location = { 0 };
            string[] topCombo = { "" };
            double[] topArea = { 0 };
            string[] botCombo = { "" };
            double[] botArea = { 0 };
            string[] vMajorCombo = { "" };
            double[] vMajorArea = { 0 };
            string[] tlCombo = { "" };
            double[] tlArea = { 0 };
            string[] ttCombo = { "" };
            double[] ttArea = { 0 };
            string[] errorSummary = { "" };
            string[] warningSummary = { "" };

            try
            {
                if (sp.DesignConcrete.GetSummaryResultsBeam(AllObjectsGroup, ref numberItems, ref frameName,
                        ref location, ref topCombo, ref topArea, ref botCombo, ref botArea,
                        ref vMajorCombo, ref vMajorArea, ref tlCombo, ref tlArea, ref ttCombo,
                        ref ttArea, ref errorSummary, ref warningSummary,
                        eItemType.Group) != 0 || frameName == null)
                {
                    return;
                }
            }
            catch (Exception)
            {
                return;
            }

            Dictionary<string, BeamDesign> byFrame =
                new Dictionary<string, BeamDesign>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < numberItems && i < frameName.Length; i++)
            {
                if (string.IsNullOrEmpty(frameName[i]))
                {
                    continue;
                }

                BeamDesign design;
                if (!byFrame.TryGetValue(frameName[i], out design))
                {
                    design = new BeamDesign();
                    byFrame[frameName[i]] = design;
                }

                design.Location.Add(location[i]);
                design.TopArea.Add(topArea[i]);
                design.BotArea.Add(botArea[i]);
                design.VMajorArea.Add(vMajorArea[i]);
            }

            // Nothing at all coming back is not an answer, it is the group call not working:
            // the design may not have been run, or this build may not take a group here. Left
            // null so every beam is asked about on its own rather than the whole model quietly
            // reporting that no beam has any steel in it.
            beamDesign = byFrame.Count > 0 ? byFrame : null;
        }

        #endregion

        public static double getMaxAs(asLocation location, double[] AreaSteel, double[] loc)
        {
            if (loc == null || loc.Length < 2 || AreaSteel == null || AreaSteel.Length < 2)
            {
                return 0;
            }

            int start = 0;
            int end = 0;
            double quarter = (loc[loc.Length - 1] - loc[0])/4;
            double leftLoc = loc[0] + quarter;
            double rightLoc = loc[loc.Length - 1] - quarter;
            int leftBound = justBefore(loc, leftLoc);
            int rightBound = justAfter(loc, rightLoc);



            int number = AreaSteel.Length;
            try
            {
                switch (location)
                {
                    case asLocation.left:
                        start = 0;
                        end = leftBound ;
                        break;
                    case asLocation.mid:
                        start = leftBound+1;
                        end = rightBound-1;
                        break;
                    case asLocation.right:
                        end = number - 1;
                        start = rightBound;
                        break;
                    default:
                        return 0;
                       
                }
                double max = getMaxBetween(start, end, AreaSteel);


                return max;
            }
            catch (Exception)
            {

                return 0;
            }
      
        }

        private static int justAfter(double[] loc, double rightLoc)
        {
            for (int i = loc.Length-1; i > 0; i--)
            {
                if (loc[i] < rightLoc)
                {
                    return i + 1;
                }
            }
            return 0;
        }

        private static int justBefore(double[] loc, double leftLoc)
        {
            for (int i = 0; i < loc.Length; i++)
            {
                if (loc[i]> leftLoc)
                {
                    return i - 1;
                }
            }
            return 0;
        }

        private static double getMaxBetween(int start, int end, double[] areaSteel)
        {
            if (start < 0 || end < 0 || start >= areaSteel.Length || end >= areaSteel.Length || start > end)
            {
                return 0;
            }
            double _max = 0;
            for (int i = start; i <= end; i++)
            {
                if (areaSteel[i]>=_max)
                {
                    _max = areaSteel[i];
                }

            }
            return _max;
        }

        private int OptainAsFromEtabs(string etabsUname, ref double[] TArea , ref double[] BArea, ref double[] Vmajor,ref double[] loc)
        {
            double[] Location;
            double[] TopArea;
            double[] BotArea;
            double[] VMajorArea;

            BeamDesign design;
            if (beamDesign != null)
            {
                if (!beamDesign.TryGetValue(etabsUname, out design))
                {
                    // Designed as something other than a beam, or not designed at all.
                    return 1;
                }

                Location = design.Location.ToArray();
                TopArea = design.TopArea.ToArray();
                BotArea = design.BotArea.ToArray();
                VMajorArea = design.VMajorArea.ToArray();
            }
            else if (!readOneBeamDesign(etabsUname, out Location, out TopArea, out BotArea, out VMajorArea))
            {
                return 1;
            }

            // A single station says nothing about how the steel runs along the beam, so it is
            // no more use than no result at all. Same test as before, now on the frame's own
            // rows rather than on whatever the one-frame call happened to return.
            if (TopArea.Length != 1 && BotArea.Length != 1)
            {
                TArea = TopArea;
                BArea = BotArea;
                loc = Location;
                Vmajor = getNormalizedStirrupsAreas(etabsUname, VMajorArea);
                return 0;
            }

            return 1;
        }

        /// <summary>The design summary for one beam, for when the whole-model read is unavailable.</summary>
        private bool readOneBeamDesign(string etabsUname, out double[] Location, out double[] TopArea,
                                       out double[] BotArea, out double[] VMajorArea)
        {
            int NumberItems = 0;
            string[] FrameName = { "" };
            string[] TopCombo = { "" };
            string[] BotCombo = { "" };
            string[] VMajorCombo = { "" };
            string[] TLCombo = { "" };
            double[] TLArea = { 0 };// not needed
            string[] TTCombo = { "" };
            double[] TTArea = { 0 };
            string[] ErrorSummary = { "" };
            string[] WarningSummary = { "" };

            Location = new double[] { 0 };
            TopArea = new double[] { 0 };
            BotArea = new double[] { 0 };
            VMajorArea = new double[] { 0 };

            int ret = sp.DesignConcrete.GetSummaryResultsBeam(etabsUname, ref NumberItems, ref FrameName,
                ref Location, ref TopCombo, ref TopArea, ref BotCombo, ref BotArea,
                ref VMajorCombo, ref VMajorArea, ref TLCombo, ref TLArea, ref TTCombo, ref TTArea,
                ref ErrorSummary, ref WarningSummary);

            return ret == 0;
        }

        private double[] getNormalizedStirrupsAreas(string etabsUname, double[] vMajorArea)
        {
            

            
            double areaNormalizationFactor = getAreaSteelNormalizationFactor(etabsUname);
            double uniteTransferFactor = areaNormalizationFactor *FrameData_e.getLengthFromUnit(etabsDbUnites, 1) * FrameData_e.getLengthFromUnit(etabsDbUnites, 1);

            for (int i = 0; i < vMajorArea.Length; i++)
            {
                vMajorArea[i] = vMajorArea[i] * uniteTransferFactor;
            }

            return vMajorArea;

        }

        private double getnormalizationFactor(double vYeildStrength)
        {
            
            double lengthFactor = FrameData_e. getLengthFromUnit(etabsDbUnites, 1);
            double forceFactor = FrameData_e.getForcefromUnit(eForceUnit, 1);


            return vYeildStrength * forceFactor / lengthFactor / lengthFactor / 240;
        }

        private double getAreaSteelNormalizationFactor(string etabsUname)
        {
            double fy = 0;
            string designSection = "";
            double normalizationFactor = 0;

            int ret = sp.DesignConcrete.GetDesignSection(etabsUname, ref designSection);
            if (!SectionsRebarMaterial.TryGetValue(designSection, out normalizationFactor))
            {
                string RebarMaterial = getRebarMaterial(designSection);
                fy= extractYeildstrength(RebarMaterial);
                normalizationFactor = getnormalizationFactor(fy);
                
                SectionsRebarMaterial.Add(designSection, normalizationFactor);
                
            }

            return normalizationFactor;

        }

        private double extractYeildstrength(string rebarMaterial)
        {
            

            double Fy=0;

            double Fu =0;

            double EFy =0;

            double EFu =0;

            int SSType =0;

             int SSHysType =0;

            double StrainAtHardening =0;

            double StrainUltimate =0;

            double FinalSlope =0;

            bool UseCaltransSSDefaults=false;

            double Temp = 0;

            sp.PropMaterial.GetORebar_1(rebarMaterial,ref Fy , ref Fu, ref EFy, ref EFu, ref SSType , ref SSHysType, ref StrainAtHardening, ref StrainUltimate
                ,ref FinalSlope, ref UseCaltransSSDefaults);
            
                return Fy;
            
        }

        private string getRebarMaterial(string designSection)

        {

            string MatPropLong = "";
            string MatPropConfine = "";
            double CoverTop = 0;
            sp.PropFrame.GetRebarBeam(designSection,ref MatPropLong,ref MatPropConfine, ref CoverTop,
                ref CoverTop, ref CoverTop, ref CoverTop, ref CoverTop, ref CoverTop);
            if (MatPropConfine != "")
            {
                return MatPropConfine;
            }
            return null;
        }

        /// <summary>
        /// The frames meeting this one at a joint, or null when the merge must not run through
        /// that joint at all - which is what a wall standing on it or a column landing on it
        /// means.
        ///
        /// Answered from the model index rather than from ETABS. The old version asked for the
        /// joint's connectivity and then asked for the design orientation of every object hung
        /// off it, once per joint of every frame, so a joint where four beams meet was asked
        /// about eight times over and each of those frames asked about four times over. The
        /// verdict here is the same one: same design orientations, from the same properties.
        ///
        /// The walk over the connections is order independent, so dropping the API's ordering
        /// costs nothing. Either something at the joint stops the merge and the list is thrown
        /// away, or nothing does and the whole list is returned.
        /// </summary>
        /// <summary>
        /// The frames meeting this one at a joint, or null when the merge must not run through
        /// that joint. Answered from the model index rather than from ETABS; see
        /// <see cref="ModelIndex.ConnectedFrames"/> for what the verdict is and why it is the
        /// same one the API used to be asked for.
        /// </summary>
        internal List<string> getConnectedframes(Joint j, string unName)
        {
            if (j == null)
            {
                return new List<string>();
            }

            return ModelIndex.ConnectedFrames(j.uniqueName, unName, framesByPoint, pointsOnAWall,
                                              isColumnFrame);
        }

        /// <summary>
        /// The two ends of a frame, with what meets them.
        ///
        /// The coordinates come from the frame list rather than from a lookup per point. A
        /// frame that is not in that list - a merged one, say - is still asked about directly.
        /// </summary>
        internal List<Joint> getJoints(string etabsUname)
        {
            FrameEnds ends;
            if (!frameEnds.TryGetValue(etabsUname, out ends))
            {
                return readJoints(etabsUname);
            }

            Joint j1 = new Joint(ends.Point1, new MyPoint(ends.X1, ends.Y1, ends.Z1), etabsDbUnites);
            Joint j2 = new Joint(ends.Point2, new MyPoint(ends.X2, ends.Y2, ends.Z2), etabsDbUnites);

            j1.connectedFrames = getConnectedframes(j1, etabsUname);
            j2.connectedFrames = getConnectedframes(j2, etabsUname);

            return new List<Joint> { j1, j2 };
        }

        /// <summary>Asks ETABS for a frame's ends, for the frames the model index does not hold.</summary>
        private List<Joint> readJoints(string etabsUname)
        {
            string point1Name = "";
            string point2Name = "";
            sp.FrameObj.GetPoints(etabsUname, ref point1Name, ref point2Name);

            MyPoint pt1 = new MyPoint();
            sp.PointObj.GetCoordCartesian(point1Name, ref pt1.X, ref pt1.Y, ref pt1.Z);
            Joint j1 = new Joint(point1Name, pt1, etabsDbUnites);

            MyPoint pt2 = new MyPoint();
            sp.PointObj.GetCoordCartesian(point2Name, ref pt2.X, ref pt2.Y, ref pt2.Z);
            Joint j2 = new Joint(point2Name, pt2, etabsDbUnites);

            j1.connectedFrames = getConnectedframes(j1, etabsUname);
            j2.connectedFrames = getConnectedframes(j2, etabsUname);

            return new List<Joint> { j1, j2 };
        }

        private void retreveFramesDimentions( ref List<FrameData_e> sections)
        {
            foreach ( FrameData_e f in sections )
            {
                switch (getSectionShape(f.etabsSecName))
                {
                    case SectionShape.Round:
                        retreveCircularSection(f);
                        break;

                    default:
                        retreveRectangularSection(f);
                        break;
                }
            }
        }

        /// <summary>
        /// Reads the frame property type so a round column is not silently read as a
        /// rectangle. Anything that is not a recognised circle is treated as rectangular,
        /// which is what the previous behaviour assumed for every section.
        /// </summary>
        private SectionShape getSectionShape(string sectionName)
        {
            // A model reuses the same handful of sections across thousands of frames, so the
            // answer is cached rather than asking ETABS once per frame.
            SectionShape cached;
            if (sectionShapes.TryGetValue(sectionName, out cached))
            {
                return cached;
            }

            SectionShape shape = readSectionShape(sectionName);
            sectionShapes.Add(sectionName, shape);
            return shape;
        }

        private SectionShape readSectionShape(string sectionName)
        {
            eFramePropType propType = eFramePropType.Rectangular;
            int ret = sp.PropFrame.GetTypeOAPI(sectionName, ref propType);
            if (ret != 0)
            {
                return SectionShape.Unknown;
            }

            switch (propType)
            {
                case eFramePropType.SD:
                    sectionDesignerSections.Add(sectionName);
                    SectionDesignerTables.Outline outline = getSectionDesignerOutline(sectionName);
                    return outline == null ? SectionShape.Unknown : outline.Shape;

                case eFramePropType.Circle:
                case eFramePropType.ConcretePipe:
                case eFramePropType.EncasedCircle:
                    return SectionShape.Round;

                case eFramePropType.Rectangular:
                case eFramePropType.EncasedRectangle:
                    return SectionShape.Rectangular;

                default:
                    return SectionShape.Unknown;
            }
        }

        /// <summary>
        /// The dimensions of one frame section, read from ETABS the first time it is asked
        /// for and remembered after that.
        ///
        /// A model reuses a handful of sections across thousands of frames - the shape was
        /// already cached for exactly that reason, and the dimensions behind it were not, so
        /// the saving was being given back one GetRectangle at a time.
        /// </summary>
        private sealed class SectionDimensions
        {
            public SectionShape Shape;
            public double Depth;
            public double Width;
            public double Diameter;
            public string Material = "";
            /// <summary>Read off a table already converted to mm, rather than in the database units.</summary>
            public bool InMillimetres;
        }

        private readonly Dictionary<string, SectionDimensions> sectionDimensions =
            new Dictionary<string, SectionDimensions>(200);

        private SectionDimensions getSectionDimensions(string sectionName)
        {
            SectionDimensions cached;
            if (sectionDimensions.TryGetValue(sectionName, out cached))
            {
                return cached;
            }

            SectionShape shape = getSectionShape(sectionName);
            SectionDimensions read = sectionDesignerSections.Contains(sectionName)
                ? readSectionDesignerSection(sectionName)
                : shape == SectionShape.Round
                    ? readCircularSection(sectionName)
                    : readRectangularSection(sectionName);

            sectionDimensions.Add(sectionName, read);
            return read;
        }

        private SectionDimensions readRectangularSection(string sectionName)
        {
            string unusedSTR = "";
            string materials = "";
            int unusedINT = 0;
            double depth = 0;
            double width = 0;

            sp.PropFrame.GetRectangle(sectionName, ref unusedSTR, ref materials, ref depth, ref width, ref unusedINT, ref unusedSTR, ref unusedSTR);

            return new SectionDimensions
            {
                Shape = SectionShape.Rectangular,
                Depth = depth,
                Width = width,
                Material = materials ?? ""
            };
        }

        #region Section Designer sections

        /// <summary>The frame sections this model draws in Section Designer, found as their type is read.</summary>
        private readonly HashSet<string> sectionDesignerSections =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Every Section Designer frame section in the model, read off its tables the first
        /// time one is met and not at all for a model that has none. Lengths are in mm.
        /// </summary>
        private Dictionary<string, SectionDesignerTables.Outline> sectionDesignerOutlines;

        private SectionDesignerTables.Outline getSectionDesignerOutline(string sectionName)
        {
            if (sectionDesignerOutlines == null)
            {
                sectionDesignerOutlines = SectionDesignerTables.Read(
                    readSectionDesignerTable(SectionDesignerTables.RectangleTable, "Width"),
                    readSectionDesignerTable(SectionDesignerTables.CircleTable, "Diameter"),
                    readSectionDesignerTable(SectionDesignerTables.PolygonTable, "X"),
                    importIssues);
            }

            SectionDesignerTables.Outline outline;
            return sectionDesignerOutlines.TryGetValue(sectionName, out outline) ? outline : null;
        }

        /// <summary>
        /// One Section Designer shape table, with the unit its lengths are in. Empty when the
        /// model has no such table - a model with no circles in Section Designer has none - or
        /// when its lengths are in a unit this does not know, which is said.
        /// </summary>
        private SectionDesignerTables.Table readSectionDesignerTable(string tableName, string lengthField)
        {
            SectionDesignerTables.Table empty = new SectionDesignerTables.Table();

            string tableKey = tableKeyFor(tableName);
            if (tableKey == null)
            {
                return empty;
            }

            string unit = tableFieldUnit(tableKey, lengthField);
            double millimetresPerUnit = SectionDesignerTables.MillimetresPer(unit);
            if (millimetresPerUnit <= 0)
            {
                importIssues.Add($"The lengths in the ETABS table \"{tableName}\" are in \"{unit}\", which " +
                                 "is not a unit this can convert, so the Section Designer columns drawn " +
                                 "with it were not sized.");
                return empty;
            }

            string[] fieldKeys;
            string[] data;
            int records;
            if (!readTable(tableKey, out fieldKeys, out data, out records))
            {
                return empty;
            }

            return new SectionDesignerTables.Table
            {
                FieldKeys = fieldKeys,
                Data = data,
                Records = records,
                MillimetresPerUnit = millimetresPerUnit
            };
        }

        /// <summary>The unit a table puts beside one of its fields, or "" when it cannot be read.</summary>
        private string tableFieldUnit(string tableKey, string fieldKey)
        {
            int tableVersion = 0;
            int count = 0;
            string[] fieldKeys = { "" };
            string[] fieldNames = { "" };
            string[] descriptions = { "" };
            string[] unitsStrings = { "" };
            bool[] isImportable = { false };

            try
            {
                if (sp.DatabaseTables.GetAllFieldsInTable(tableKey, ref tableVersion, ref count,
                                                          ref fieldKeys, ref fieldNames,
                                                          ref descriptions, ref unitsStrings,
                                                          ref isImportable) != 0 || fieldKeys == null)
                {
                    return "";
                }
            }
            catch (Exception)
            {
                return "";
            }

            int column = LoadSetTables.ColumnOf(fieldKeys, fieldKey);
            return column < 0 || unitsStrings == null || column >= unitsStrings.Length
                ? ""
                : unitsStrings[column] ?? "";
        }

        /// <summary>
        /// A Section Designer section's size, already in mm. One the tables do not hold is
        /// said, and comes back with no size and no shape rather than as a rectangle of nothing.
        /// </summary>
        private SectionDimensions readSectionDesignerSection(string sectionName)
        {
            SectionDesignerTables.Outline outline = getSectionDesignerOutline(sectionName);

            if (outline == null || outline.ShapeCount == 0)
            {
                importIssues.Add($"The Section Designer section \"{sectionName}\" has no concrete shape " +
                                 "that could be read, so the frames using it import with no size.");
                return new SectionDimensions { Shape = SectionShape.Unknown, InMillimetres = true };
            }

            if (outline.Shape == SectionShape.Unknown)
            {
                importIssues.Add($"The Section Designer section \"{sectionName}\" is neither a single " +
                                 "rectangle nor a single circle, so the frames using it are sized by the box " +
                                 $"around it ({Math.Round(outline.Width)} x {Math.Round(outline.Depth)} mm) " +
                                 "and a column using it is not checked for shape.");
            }

            return new SectionDimensions
            {
                Shape = outline.Shape,
                Depth = outline.Depth,
                Width = outline.Width,
                Diameter = outline.Diameter,
                Material = outline.Material ?? "",
                InMillimetres = true
            };
        }

        #endregion

        private SectionDimensions readCircularSection(string sectionName)
        {
            string unusedSTR = "";
            string materials = "";
            int unusedINT = 0;
            double diameter = 0;

            sp.PropFrame.GetCircle(sectionName, ref unusedSTR, ref materials, ref diameter, ref unusedINT, ref unusedSTR, ref unusedSTR);

            return new SectionDimensions
            {
                Shape = SectionShape.Round,
                Diameter = diameter,
                Material = materials ?? ""
            };
        }

        private void retreveRectangularSection(FrameData_e f)
        {
            SectionDimensions section = getSectionDimensions(f.etabsSecName);
            Form1.eLength units = section.InMillimetres ? Form1.eLength.mm : etabsDbUnites;

            // Rectangular for every ordinary section, as it always was; a Section Designer
            // section that is not a plain rectangle keeps the shape it was read as.
            f.Shape = section.Shape == SectionShape.Unknown && section.InMillimetres
                ? SectionShape.Unknown
                : SectionShape.Rectangular;
            f.setHeight_e(section.Depth, units);
            f.setWidth_e(section.Width, units);
            f.Material = section.Material;
        }

        private void retreveCircularSection(FrameData_e f)
        {
            SectionDimensions section = getSectionDimensions(f.etabsSecName);

            f.setDiameter_e(section.Diameter, section.InMillimetres ? Form1.eLength.mm : etabsDbUnites);
            f.Material = section.Material;
        }

        private void getNameList()
        {
            for (int i = 0; i < frameCount && i < frameNames.Length; i++)
            {
                // A null line is not a beam, whichever program drew it - SAFE gives it no
                // section name at all, so the ignore list never saw it. It is still read for
                // the line loads it carries; see getLineLoadFrames.
                if (ModelIndex.IsNullFrame(framePropNames[i]) || isIgnored(framePropNames[i]))
                {
                    continue;
                }

                MyPoint pt1 = FrameData_e.getPointFromEtabs(etabsDbUnites, frameP1X[i], frameP1Y[i], frameP1Z[i]);
                MyPoint pt2 = FrameData_e.getPointFromEtabs(etabsDbUnites, frameP2X[i], frameP2Y[i], frameP2Z[i]);

                FrameData_e frame = new FrameData_e(frameNames[i]);
                frame.etabsSecName = framePropNames[i];
                frame.EtabsLabel = getFrameLabel(frameNames[i]);
                frame.Story = frameStoryNames[i];
                frame.Start_e = pt1;
                frame.END_e = pt2;
                frame.Rotaion = frameAngles[i];

                framesFromEtabs.Add(frame);
            }
        }

        /// <summary>
        /// The label ETABS shows for a frame, as opposed to the unique name the API works
        /// with. Read out of the list fetched for the whole model; a frame that was not in it
        /// is asked about on its own, and one ETABS cannot give a label for simply has none.
        /// </summary>
        private string getFrameLabel(string etabsUname)
        {
            string known;
            if (frameLabels.TryGetValue(etabsUname, out known))
            {
                return known;
            }

            string label = "";
            string storyName = "";

            try
            {
                int ret = sp.FrameObj.GetLabelFromName(etabsUname, ref label, ref storyName);
                if (ret != 0 || label == null)
                {
                    label = "";
                }
            }
            catch (Exception)
            {
                label = "";
            }

            frameLabels[etabsUname] = label;
            return label;
        }
    }
}