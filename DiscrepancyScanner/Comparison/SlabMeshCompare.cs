using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>One face of the ETABS base mesh: an area, or what is left of one.</summary>
    internal sealed class EtabsFace
    {
        public Slab_E Source;
        public PlanPolygon Plan;
        public bool IsWholeArea;

        /// <summary>
        /// The shells that outrank this one where they overlap it, and the openings over it:
        /// the plan inside any of them is not this shell's to answer for. Empty for a shell
        /// nothing is drawn over, which is nearly all of them.
        ///
        /// Kept as outlines and settled point by point in the triangulation, rather than cut
        /// out of the shell beforehand. The cutting was done by the geometry kernel, and a slab
        /// with a few hundred drops, stiff areas and load shells drawn over it asked one
        /// boolean to take all of them off at once: one refusal among them and the whole slab
        /// was checked as drawn, on top of every shell over it. See SlabCompareEngine.BuildBaseMesh.
        /// </summary>
        public IList<PlanPolygon> CoveredBy = new List<PlanPolygon>();

        /// <summary>
        /// The shells ranked below this one that it is drawn over: the slab under a drop, the
        /// slab under a load shell. They own none of this face's plan, but their loads act on
        /// it all the same - loads add, nothing overrides - so the load pass cuts the face
        /// wherever the stack under it changes. A drop across two slabs carrying different loads
        /// is two pieces with two sums.
        ///
        /// On the load pass the null load areas laid over it are here too: no slab, so never
        /// faces of their own, and their load is added wherever they reach.
        /// </summary>
        public IList<Slab_E> StandsOn = new List<Slab_E>();

        /// <summary>
        /// Whether this face answers for a point: inside its outline and under none of the
        /// shells or openings that outrank it.
        /// </summary>
        public bool Owns(double x, double y)
        {
            if (!Plan.Bounds.Contains(x, y) || !Plan.Contains(x, y))
            {
                return false;
            }

            for (int i = 0; i < CoveredBy.Count; i++)
            {
                PlanPolygon over = CoveredBy[i];
                if (over.Bounds.Contains(x, y) && over.Contains(x, y))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// A stiff area stacked on the slab: the column or wall standing there rather than
        /// slab. It still takes its piece of plan, so the shells it stands in are cut around it
        /// and the Revit floor under it is spoken for, but the slab pass says nothing about it.
        /// See SlabCompareEngine.StackedStiffAreas.
        /// </summary>
        public bool IsSupport;
    }

    /// <summary>
    /// The slab check, done the way SRV does it: by cutting the ETABS areas along the Revit
    /// slab edges with a triangulation, rather than by asking the geometry kernel to intersect
    /// one outline with another.
    ///
    /// Two parts, and they are the two halves of the same question.
    ///
    ///   Part A, inside the ETABS mesh. Every ETABS area survives. An area that a slab edge
    ///   runs across is triangulated locally with that edge put in as a constraint, so the
    ///   thickness change lands exactly on the slab edge instead of smearing across the area.
    ///   An area sitting wholly inside one floor - or wholly outside every floor - is not cut
    ///   at all and is reported as drawn, which is what nearly all of them do.
    ///
    ///   Part B, Revit floor the ETABS mesh does not reach. Each floor is triangulated with
    ///   the ETABS outlines put in as constraints, and the triangles left standing inside the
    ///   floor and outside every ETABS area are what only Revit has.
    ///
    /// The reason to do it this way is that a triangulation cannot refuse. The kernel could,
    /// and did: on this model it turned down eighty odd booleans outright, every one of them a
    /// piece of basement slab that then went unchecked. It was also where the check spent its
    /// time, because two outlines that overlap have to be intersected, and the answer costs the
    /// same whether they differ or not. Here the cost is in cutting, and areas that agree with
    /// the model are never cut.
    /// </summary>
    internal sealed class SlabMeshCompare
    {
        /// <summary>
        /// Points this close together are the same point to the triangulation. SRV works in
        /// feet and snaps at a millionth of one; this is the same distance in mm.
        /// </summary>
        public double SnapTolerance = 0.0003d;

        /// <summary>
        /// How far to each side of a slab edge the pair of constraints is placed, in mm.
        ///
        /// A slab edge goes in as two lines rather than one, just inside the floor and just
        /// outside it. Put in as a single line, the triangles either side of it share its
        /// vertices, and a vertex on the edge belongs to the floor and to the space beyond it
        /// at once - so the thickness has to ramp across the whole area rather than step at the
        /// edge. The pair makes the step a strip a third of a millimetre wide, which reads as
        /// a sharp edge and measures as nothing.
        /// </summary>
        public double BoundaryOffset = 0.3d;

        /// <summary>
        /// Points the floor or loading plan edges may add to one triangulation before the
        /// face is sampled instead of cut - see <see cref="AddSamplePoints"/>.
        ///
        /// A raft with hundreds of openings under a mesh of thousands of ETABS areas can hand
        /// one triangulation more work than the whole rest of the story, and slow without end
        /// is what this check was already found guilty of once. But the cap was 6,000, and a
        /// real loading plan is far busier than that: on one podium the paving region alone,
        /// drawn as the background with a hole for everything else, had 9,748 points, and the
        /// slab under the whole floor needed about 30,000 to be cut. Past the cap it used to be
        /// answered from a single point - 3,750 m2 of parking held against paving because that
        /// is where the one point fell. Measured on a single shell, 22,000 points cut in about
        /// 2 s, 49,000 in 15 s and 101,000 in 70 s, so this is twice the busiest real floor seen
        /// and still well under half a minute.
        /// </summary>
        public int MaximumPointsPerTriangulation = 60000;

        /// <summary>
        /// Closest the sample points of a face too busy to cut are put, in mm. A face small
        /// enough to need them closer than this is not the kind that is too busy to cut.
        /// </summary>
        public double MinimumSampleSpacing = 250d;

        /// <summary>
        /// About how many sample points a face too busy to cut is given - see
        /// <see cref="AddSamplePoints"/>. Half the cap, so sampling costs no more than cutting
        /// was allowed to: a floor-wide slab of 4,800 m2 is answered every 400 mm.
        /// </summary>
        public int SampleBudget = 30000;

        public double ThicknessTolerance;
        public double MinimumZoneArea;
        public double MinimumZoneWidth;
        public double EdgeTolerance;

        /// <summary>
        /// Smallest patch this pass's finding is counted as work on, in mm squared, or zero to
        /// count every piece however small. Handed to each zone as it is built - see
        /// <see cref="SlabZone.IsTooSmallToCount"/>, which is where it does its work, and which
        /// is about what gets added up rather than about what gets reported.
        /// </summary>
        public double MinimumIssueArea;

        /// <summary>
        /// What the loading plan says the slab carries, or null when no loading plan is
        /// paired with this level and the load check is off.
        /// </summary>
        public LoadCoverage Loads;

        /// <summary>
        /// Every ETABS or SAFE shell on the story being cut, so a piece of plan with more than
        /// one shell over it is read as carrying all of them. Null leaves each piece carrying
        /// only the shell it was cut from, which is what the thickness pass wants.
        /// </summary>
        public IList<Slab_E> ShellsOnStory;

        /// <summary>
        /// Which of the two questions this pass is asking of the slab, and so which edges are
        /// worth cutting it along.
        ///
        /// The two are run as separate passes over the same areas because they are cut apart
        /// for different reasons. The slab pass cuts where the Revit floors say the answer
        /// changes - an edge, a drop panel, an opening. The load pass cuts where the loading
        /// plan's regions say it changes. Running them together, as this once did, cut every
        /// area along both sets of edges at once: a slab of one depth came back in pieces
        /// because a loading region boundary happened to cross it, and a shell under one
        /// loading region came back in pieces because a Revit floor edge did. Each piece was
        /// then carrying a verdict about a question its own shape had nothing to do with.
        ///
        /// Cut separately, each mesh is the shape of its own answer, and each triangulation
        /// takes one set of constraint edges rather than two - so this is cheaper as well as
        /// truer, and the point cap is reached less often, not more.
        /// </summary>
        public SlabMeshPurpose Purpose = SlabMeshPurpose.Slab;

        /// <summary>True while this pass is cutting for the Revit floors.</summary>
        private bool CuttingForSlab => Purpose == SlabMeshPurpose.Slab;

        /// <summary>How far a load may differ from the schedule, in kN/m2.</summary>
        public double LoadTolerance;

        /// <summary>The ETABS load pattern names, mapped onto the four the check compares.</summary>
        public LoadPatternMap.Mapping LoadPatterns;

        public Func<bool> ShouldStop;

        private readonly List<SlabZone> zones;
        private readonly List<string> issues;

        public SlabMeshCompare(List<SlabZone> zones, List<string> issues)
        {
            this.zones = zones;
            this.issues = issues;
        }

        private bool Stop()
        {
            return ShouldStop != null && ShouldStop();
        }

        #region pieces too small to report on their own

        /// <summary>
        /// A piece small enough to be dropped, held back until it is known whether the answer
        /// it belongs to carries on into the next face.
        /// </summary>
        private sealed class Provisional
        {
            public SlabZone Zone;
            public string Key;
        }

        private readonly List<Provisional> provisional = new List<Provisional>();

        /// <summary>
        /// The pieces reported in their own right, bucketed by plan position so a held back
        /// piece asks the few that could touch it rather than every zone on the story. A finely
        /// subdivided mesh is where held back pieces come from in the first place, and it is
        /// also where there are most zones to compare them against, so the walk has to be over
        /// the neighbourhood rather than over the story.
        /// </summary>
        private readonly Dictionary<long, List<Provisional>> reported =
            new Dictionary<long, List<Provisional>>();

        /// <summary>Bucket size in mm, as used by the face and coverage grids.</summary>
        private const double ReportedCellSize = 2000d;

        /// <summary>
        /// How far apart two pieces may be and still count as carrying on into each other.
        ///
        /// Pieces cut out of neighbouring faces meet exactly on the boundary the two faces
        /// share, so nothing here has to bridge a real gap - this only has to be wider than
        /// the arithmetic. A couple of <see cref="PlanRing.PointTolerance"/> is that, and is
        /// far narrower than any modelling gap the check is meant to report.
        /// </summary>
        private const double ContinuesGap = 2d * PlanRing.PointTolerance;

        /// <summary>Whether a piece is too small to be worth reporting on its own.</summary>
        private bool IsTooSmall(PlanPolygon piece)
        {
            return piece == null
                   || piece.Area < MinimumZoneArea
                   || piece.TypicalWidth < MinimumZoneWidth;
        }

        /// <summary>
        /// Files a zone as reported, or holds it back when the piece is too small to stand on
        /// its own. <paramref name="key"/> says what answer the piece carries, so a piece is
        /// only ever joined to a neighbour that says the same thing.
        /// </summary>
        private void Emit(SlabZone zone, string key, bool tooSmall)
        {
            zones.Add(zone);

            Provisional piece = new Provisional { Zone = zone, Key = key };
            if (tooSmall)
            {
                provisional.Add(piece);
            }
            else
            {
                File(piece);
            }
        }

        private void File(Provisional piece)
        {
            if (piece.Zone.Plan == null)
            {
                return;
            }

            PlanBounds bounds = piece.Zone.Plan.Bounds;

            for (long x = Cell(bounds.MinX); x <= Cell(bounds.MaxX); x++)
            {
                for (long y = Cell(bounds.MinY); y <= Cell(bounds.MaxY); y++)
                {
                    long key = CellKey(x, y);
                    List<Provisional> bucket;
                    if (!reported.TryGetValue(key, out bucket))
                    {
                        bucket = new List<Provisional>();
                        reported[key] = bucket;
                    }
                    bucket.Add(piece);
                }
            }
        }

        private static long Cell(double value)
        {
            return (long)Math.Floor(value / ReportedCellSize);
        }

        private static long CellKey(long x, long y)
        {
            return (x << 32) ^ (y & 0xFFFFFFFFL);
        }

        /// <summary>
        /// Settles the pieces that were held back, and takes off the ones that were right to
        /// drop.
        ///
        /// The smallest-zone sizes exist to swallow the hairline two outlines leave behind
        /// when they were drawn to the same line and land a fraction of a millimetre apart.
        /// Measured on one piece at a time, they cannot tell that hairline from one cell's
        /// share of a disagreement that is metres long: an ETABS mesh cuts a mismatch into a
        /// piece per cell, and the pieces at its tapering ends are small however large the
        /// mismatch is. Dropping those left the plan blank exactly where the mismatch ran
        /// out, and made the answer depend on how finely the mesh happened to be subdivided.
        ///
        /// So a piece under the sizes is kept when the same answer carries on into a piece
        /// that was reported in its own right, and is repeated until nothing more joins on,
        /// which is what carries a taper that runs out over several cells. A piece that joins
        /// nothing is the hairline the sizes were written for, and still goes.
        /// </summary>
        private void SettleHeldBack()
        {
            bool joinedAny = true;
            while (joinedAny && provisional.Count > 0)
            {
                joinedAny = false;

                for (int i = provisional.Count - 1; i >= 0; i--)
                {
                    if (!ContinuesIntoReported(provisional[i]))
                    {
                        continue;
                    }

                    File(provisional[i]);
                    provisional.RemoveAt(i);
                    joinedAny = true;
                }
            }

            if (provisional.Count > 0)
            {
                HashSet<SlabZone> dropped = new HashSet<SlabZone>(provisional.Select(p => p.Zone));
                zones.RemoveAll(zone => dropped.Contains(zone));
            }

            provisional.Clear();
            reported.Clear();
        }

        private bool ContinuesIntoReported(Provisional piece)
        {
            if (piece.Zone.Plan == null)
            {
                return false;
            }

            PlanBounds bounds = piece.Zone.Plan.Bounds;

            // The gap widens the search by well under a bucket, so the buckets the piece itself
            // falls in are the only ones that can hold anything touching it.
            for (long x = Cell(bounds.MinX - ContinuesGap); x <= Cell(bounds.MaxX + ContinuesGap); x++)
            {
                for (long y = Cell(bounds.MinY - ContinuesGap); y <= Cell(bounds.MaxY + ContinuesGap); y++)
                {
                    List<Provisional> bucket;
                    if (!reported.TryGetValue(CellKey(x, y), out bucket))
                    {
                        continue;
                    }

                    foreach (Provisional other in bucket)
                    {
                        if (other.Key == piece.Key
                            && other.Zone.Plan != null
                            && other.Zone.Plan.Bounds.IntersectsWith(bounds, ContinuesGap))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        #endregion

        #region part A - the ETABS areas

        /// <summary>
        /// Every ETABS area against the Revit floors under it, cut only where the answer
        /// changes.
        /// </summary>
        public void CompareFaces(string storyName, IList<EtabsFace> faces, SlabCoverage coverage)
        {
            foreach (EtabsFace face in faces)
            {
                if (Stop())
                {
                    // Whatever was held back is settled on the way out too, so stopping early
                    // leaves the zones already worked out complete rather than short.
                    SettleHeldBack();
                    return;
                }

                try
                {
                    CompareFace(storyName, face, coverage);
                }
                catch (Exception e)
                {
                    issues.Add($"{storyName}: {face.Source.Description} could not be checked - {e.Message}");

                    SlabZone zone = new SlabZone(face.Plan, SlabZoneState.Unresolved, storyName)
                    {
                        Purpose = this.Purpose,
                        EtabsName = face.Source.UniqueName,
                        EtabsProperty = face.Source.PropertyName,
                        EtabsThickness = face.Source.ComparedThickness,
                        IsWholeEtabsArea = face.IsWholeArea,
                        Note = e.Message
                    };
                    zone.BuildResults();
                    zones.Add(zone);
                }
            }

            SettleHeldBack();
        }

        private void CompareFace(string storyName, EtabsFace face, SlabCoverage coverage)
        {
            if (face.IsSupport && CuttingForSlab)
            {
                // A column or wall has no slab thickness, outline or opening to agree or
                // disagree about. The load pass still reads it: what the shells stacked there
                // carry is load on the slab all the same.
                return;
            }

            PlanBounds bounds = face.Plan.Bounds;

            // Another shell or an opening is drawn over part of it, so it can never go through
            // as drawn: the triangulation has to be cut along those outlines to find the part
            // that is this shell's.
            bool shared = face.CoveredBy.Count > 0;

            // In the load pass a face drawn over other shells has to be cut too, wherever the
            // stack under it changes - see EtabsFace.StandsOn.
            bool mustCut = shared || (!CuttingForSlab && face.StandsOn.Count > 0);

            // The load pass never looks at the Revit floors: where they run has nothing to
            // say about what the slab is asked to carry, and cutting along them would put
            // boundaries into the load mesh that no loading region drew.
            List<SlabCoverage.Region> candidates = CuttingForSlab
                ? coverage.Regions
                    .Where(region => region.Plan != null && bounds.IntersectsWith(region.Plan.Bounds))
                    .ToList()
                : new List<SlabCoverage.Region>();

            if (candidates.Count == 0 && CuttingForSlab && !shared)
            {
                // No floor comes anywhere near it, so the whole area is missing in Revit.
                EmitWhole(storyName, face, Verdict(face.Source, new SlabCoverage.Reading()),
                          new SlabCoverage.Reading(), LoadAt(face.Plan.InteriorPoint()));
                return;
            }

            ConstrainedDelaunay cdt = StartTriangulation(face);

            // The cap is on what the floors and the loading plan add. The outlines of what is
            // drawn over the shell are not optional - without them it cannot be told apart from
            // the shells over it - so they are not what gives the triangulation up.
            int ownOutlines = cdt.Points.Count - face.Plan.Outer.Points.Count
                              - face.Plan.Holes.Sum(hole => hole.Points.Count);

            bool cut = mustCut;
            string tooBusy = null;

            foreach (SlabCoverage.Region region in candidates)
            {
                WalkRingDoubled(region.Plan.Outer, region.Plan, face, cdt, ref cut);
                foreach (PlanRing hole in region.Plan.Holes)
                {
                    WalkRingDoubled(hole, region.Plan, face, cdt, ref cut);
                }

                if (cdt.Points.Count - ownOutlines > MaximumPointsPerTriangulation)
                {
                    tooBusy = "Revit floor edges";
                    break;
                }
            }

            // The loading regions go in the same way and for the same reason: where one of
            // their edges runs across the area, the answer changes there.
            if (Loads != null && tooBusy == null)
            {
                foreach (LoadCoverage.Region region in Loads.Regions)
                {
                    if (!bounds.IntersectsWith(region.Plan.Bounds))
                    {
                        continue;
                    }

                    WalkRingDoubled(region.Plan.Outer, region.Plan, face, cdt, ref cut);
                    foreach (PlanRing hole in region.Plan.Holes)
                    {
                        WalkRingDoubled(hole, region.Plan, face, cdt, ref cut);
                    }

                    if (cdt.Points.Count - ownOutlines > MaximumPointsPerTriangulation)
                    {
                        tooBusy = "loading plan edges";
                        break;
                    }
                }
            }

            if (!mustCut && tooBusy == null && !cut)
            {
                // The floors overlap its bounding box but no edge actually crosses it, so the
                // area has one answer all the way across and goes through as drawn.
                MyPoint inside = face.Plan.InteriorPoint();
                SlabCoverage.Reading reading = FloorAt(coverage, inside);
                EmitWhole(storyName, face, Verdict(face.Source, reading), reading, LoadAt(inside));
                return;
            }

            if (tooBusy != null)
            {
                // Too busy to cut along every edge, so it starts again with only the outlines
                // that say which part of the plan is its own, and points scattered over it:
                // each small piece is answered where it lies. It used to be answered from one
                // point for the whole shell, which on a floor-wide slab is a whole floor
                // answered by whichever region that one point happened to fall in.
                cdt = StartTriangulation(face);
                double spacing = AddSamplePoints(cdt, face, SampleBudget);

                issues.Add($"{storyName}: {face.Source.Description} has more {tooBusy} running " +
                           $"across it than can be cut along, so it is answered at points " +
                           $"{Math.Round(spacing)} mm apart instead: where the answer changes across " +
                           $"it is right to within that.");
            }

            cdt.Triangulate();

            List<MyPoint> points = cdt.Points.Select(p => new MyPoint(p.X, p.Y)).ToList();
            int real = cdt.RealPointCount;

            Dictionary<string, Group> groups = new Dictionary<string, Group>();

            for (int t = 0; t < cdt.Triangles.Length; t++)
            {
                int[] triangle = cdt.Triangles[t];
                if (triangle == null || triangle.Length != 3)
                {
                    continue;
                }
                if (triangle[0] >= real || triangle[1] >= real || triangle[2] >= real)
                {
                    continue;
                }

                MyPoint centre = Centroid(points, triangle);
                if (!face.Owns(centre.X, centre.Y))
                {
                    // Outside the area itself - the triangulation fills its convex hull, so
                    // a concave area and every hole in one come back covered in triangles
                    // that were never part of it - or under a shell that outranks it there.
                    continue;
                }

                MyPoint at = centre;

                SlabCoverage.Reading reading = FloorAt(coverage, at);
                SlabZoneState state = Verdict(face.Source, reading);
                LoadCoverage.Reading loads = LoadAt(at);

                Group group;
                string key = GroupKey(state, reading, loads) + (CuttingForSlab ? "" : StackUnder(face, at));
                if (!groups.TryGetValue(key, out group))
                {
                    group = new Group { State = state, Reading = reading, Loads = loads };
                    groups[key] = group;
                }
                group.Triangles.Add(triangle);
            }

            if (groups.Count == 0)
            {
                if (shared)
                {
                    // Every part of it lies under something that outranks it. Sometimes that is
                    // the truth, but a blank on the plan is worth a sentence either way.
                    issues.Add($"{storyName}: {face.Source.Description} is covered all over by the " +
                               $"area(s) drawn over it, so nothing of it is checked on its own. If the " +
                               $"plan is blank there, this is why.");
                }
                return;
            }

            if (groups.Count == 1 && !shared)
            {
                // One answer across the whole area after all, so it is reported as it was
                // drawn rather than as the triangles it was cut into.
                Group only = groups.Values.First();

                // A whole shell standing in a hole in the Revit floor - a small one, meshed to
                // the opening - is that opening, not a patch past the floor's edge.
                SlabZoneState whole = only.State == SlabZoneState.MissingInRevit && InARevitHole(face.Plan, coverage)
                    ? SlabZoneState.OpeningInRevitOnly
                    : only.State;
                EmitWhole(storyName, face, whole, only.Reading, only.Loads);
                return;
            }

            foreach (KeyValuePair<string, Group> entry in groups)
            {
                Group group = entry.Value;

                foreach (PlanPolygon piece in Merged(points, group.Triangles))
                {
                    SlabZoneState state = group.State;

                    if (state == SlabZoneState.MissingInRevit
                        && (!TouchesOutline(piece, face) || InARevitHole(piece, coverage)))
                    {
                        // Not along the slab edge, so it is a hole in the middle of the area:
                        // an opening Revit has and ETABS does not. Or along the edge of this
                        // shell but in a hole in the Revit floor all the same - a finely meshed
                        // slab puts a shell edge through most openings, and the opening is no
                        // less an opening for it.
                        state = SlabZoneState.OpeningInRevitOnly;
                    }

                    // The verdict decided just above is part of what makes two pieces the same
                    // answer, so it goes in the key rather than the group's own state: an
                    // opening only Revit has does not carry on into the slab edge beside it.
                    Emit(BuildZone(storyName, face, state, group.Reading, group.Loads, piece, false),
                         (int)state + "/" + entry.Key,
                         IsTooSmall(piece));
                }
            }
        }

        /// <summary>
        /// A triangulation holding the face's own outline and the outlines of whatever outranks
        /// it, which is where its own part of the plan begins and ends.
        ///
        /// Doubled like a floor edge, and for a reason of their own: a shell laid over part of
        /// another is usually drawn to the same edges, so its outline starts and ends exactly
        /// on the outline of the shell under it, and a line put in as it runs was lost there -
        /// neither end is properly inside, and nothing is crossed. A third of a millimetre
        /// either side, one of the pair always reaches in.
        /// </summary>
        private ConstrainedDelaunay StartTriangulation(EtabsFace face)
        {
            ConstrainedDelaunay cdt = new ConstrainedDelaunay();
            AddRing(cdt, face.Plan.Outer);
            foreach (PlanRing hole in face.Plan.Holes)
            {
                AddRing(cdt, hole);
            }

            bool unused = false;
            foreach (PlanPolygon over in face.CoveredBy)
            {
                WalkRingDoubled(over.Outer, over, face, cdt, ref unused);
                foreach (PlanRing hole in over.Holes)
                {
                    WalkRingDoubled(hole, over, face, cdt, ref unused);
                }
            }

            // The load pass also needs to know where the stack under it changes; the slab
            // pass does not, since what is underneath owns none of this plan.
            if (!CuttingForSlab)
            {
                foreach (Slab_E under in face.StandsOn)
                {
                    WalkRingDoubled(under.Plan.Outer, under.Plan, face, cdt, ref unused);
                    foreach (PlanRing hole in under.Plan.Holes)
                    {
                        WalkRingDoubled(hole, under.Plan, face, cdt, ref unused);
                    }
                }
            }

            return cdt;
        }

        /// <summary>
        /// Points on a square grid over the face's own part of the plan, for a face with too
        /// many edges across it to cut along: the triangles between them are small, and each is
        /// answered where it lies, so where the answer changes is right to within one spacing.
        /// The spacing is what spreads about <paramref name="budget"/> points over the face, and
        /// never closer than <see cref="MinimumSampleSpacing"/>. Hands back the spacing used.
        /// </summary>
        private double AddSamplePoints(ConstrainedDelaunay cdt, EtabsFace face, int budget)
        {
            PlanBounds bounds = face.Plan.Bounds;
            double spacing = Math.Max(MinimumSampleSpacing,
                                      Math.Sqrt(Math.Max(1d, face.Plan.Area) / Math.Max(1, budget)));

            for (double x = bounds.MinX + spacing / 2; x < bounds.MaxX; x += spacing)
            {
                for (double y = bounds.MinY + spacing / 2; y < bounds.MaxY; y += spacing)
                {
                    if (face.Owns(x, y))
                    {
                        cdt.AddOrSnapPoint(x, y, SnapTolerance);
                    }
                }
            }

            return spacing;
        }

        /// <summary>
        /// Which of the shells under the face stand under this point, as a key: two pieces
        /// only carry the same load when the same shells are stacked under both.
        /// </summary>
        private static string StackUnder(EtabsFace face, MyPoint at)
        {
            if (face.StandsOn.Count == 0)
            {
                return "";
            }

            List<string> names = new List<string>();
            foreach (Slab_E under in face.StandsOn)
            {
                if (under.Plan.Bounds.Contains(at.X, at.Y) && under.Plan.Contains(at.X, at.Y))
                {
                    names.Add(under.UniqueName);
                }
            }

            return "|S" + string.Join(",", names);
        }

        /// <summary>
        /// Whether a piece runs along the edge of the shell's own part of the plan - its
        /// outline, or the outline of a shell or opening drawn over it - rather than sitting
        /// inside it like a hole.
        /// </summary>
        /// <summary>True where a piece lies in a hole in a Revit floor - see <see cref="SlabCoverage.InAHole"/>.</summary>
        private static bool InARevitHole(PlanPolygon piece, SlabCoverage coverage)
        {
            MyPoint inside = piece.InteriorPoint();
            return inside != null && coverage != null && coverage.InAHole(inside.X, inside.Y);
        }

        private bool TouchesOutline(PlanPolygon piece, EtabsFace face)
        {
            if (piece.TouchesOutlineOf(face.Plan, EdgeTolerance))
            {
                return true;
            }

            foreach (PlanPolygon over in face.CoveredBy)
            {
                if (over.Bounds.IntersectsWith(piece.Bounds, EdgeTolerance)
                    && piece.TouchesOutlineOf(over, EdgeTolerance))
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class Group
        {
            public SlabZoneState State;
            public SlabCoverage.Reading Reading;
            public LoadCoverage.Reading Loads;
            public readonly List<int[]> Triangles = new List<int[]>();
        }

        /// <summary>
        /// What the Revit floors say at a point - on the slab pass only. A piece of the load
        /// mesh is answered by the loading plan, not by a floor, and reading one here stamped a
        /// piece hundreds of square metres across with whichever floor happened to lie under the
        /// first triangle of it: a single drop panel's type, named as what the piece was paired
        /// with and what SELECT IN REVIT picked.
        /// </summary>
        private SlabCoverage.Reading FloorAt(SlabCoverage coverage, MyPoint at)
        {
            return CuttingForSlab ? coverage.At(at.X, at.Y) : new SlabCoverage.Reading { TypeName = "" };
        }

        /// <summary>
        /// What the loading plan says at a point, or an uncovered reading when no loading plan
        /// is paired with this level.
        /// </summary>
        private LoadCoverage.Reading LoadAt(MyPoint point)
        {
            return Loads == null ? new LoadCoverage.Reading() : Loads.At(point.X, point.Y);
        }

        /// <summary>
        /// Two triangles belong together when they say the same thing about the slab, which
        /// means the same verdict and, where Revit has a floor, the same depth to within half
        /// a millimetre.
        /// </summary>
        private string GroupKey(SlabZoneState state, SlabCoverage.Reading reading,
                               LoadCoverage.Reading loads)
        {
            // On the load pass the loading plan's answer is the whole key: two pieces belong
            // together when the plan asks the same of both, whatever the Revit floors above
            // them happen to do.
            if (!CuttingForSlab)
            {
                return "L|" + LoadCoverage.KeyOf(loads);
            }

            // On the slab pass the loads are not in the key at all, which is what stops a
            // loading region boundary cutting a slab of one depth into pieces.
            if (!reading.Covered)
            {
                return ((int)state).ToString();
            }
            return (int)state + "|" + Math.Round(reading.Thickness * 2) + "|" + reading.PrimaryRevitId;
        }

        private SlabZoneState Verdict(Slab_E etabsSlab, SlabCoverage.Reading reading)
        {
            if (!CuttingForSlab)
            {
                // The load pass says nothing about thickness or outline - it was not cut along
                // the edges those answers change at, so any verdict it reached would be about
                // the wrong shape. The zones it makes carry a load verdict and nothing else.
                return SlabZoneState.NotChecked;
            }

            if (!reading.Covered)
            {
                return SlabZoneState.MissingInRevit;
            }
            if (reading.Variable)
            {
                return SlabZoneState.ThicknessReview;
            }
            return Math.Abs(reading.Thickness - etabsSlab.ComparedThickness) <= ThicknessTolerance
                ? SlabZoneState.Matched
                : SlabZoneState.ThicknessMismatch;
        }

        /// <summary>
        /// An area with one answer all the way across, reported as it was drawn. It is never
        /// held back however small it is - an ETABS area is a thing the engineer drew, not a
        /// piece this check cut off something - but it can still be what a held back piece
        /// beside it carries on into.
        /// </summary>
        private void EmitWhole(string storyName, EtabsFace face, SlabZoneState state,
                               SlabCoverage.Reading reading, LoadCoverage.Reading loads)
        {
            Emit(BuildZone(storyName, face, state, reading, loads, face.Plan, true),
                 (int)state + "/" + GroupKey(state, reading, loads),
                 false);
        }

        private SlabZone BuildZone(string storyName, EtabsFace face, SlabZoneState state,
                                   SlabCoverage.Reading reading, LoadCoverage.Reading loads,
                                   PlanPolygon plan, bool wholeFace)
        {
            SlabZone zone = new SlabZone(plan, state, storyName)
            {
                Purpose = this.Purpose,
                MinimumIssueArea = this.MinimumIssueArea,
                EtabsName = face.Source.UniqueName,
                EtabsProperty = face.Source.PropertyName,
                EtabsThickness = face.Source.ComparedThickness,
                IsWholeEtabsArea = wholeFace && face.IsWholeArea
            };

            if (reading.Covered)
            {
                zone.RevitId = reading.PrimaryRevitId;
                zone.RevitType = reading.TypeName;
                zone.RevitSource = reading.Source;
                zone.RevitThickness = reading.Thickness;
                zone.IsStackedInRevit = reading.Stacked;

                if (reading.Stacked)
                {
                    // Where floors overlap, which one answered is worth saying outright. A
                    // thickness nobody expects is nearly always a drop panel overlapping the
                    // slab, and this names the floor it came from and the one it displaced.
                    SlabCoverage.Region next = reading.Next;
                    zone.Note = $"{Math.Round(reading.Thickness)} mm, the thickest of " +
                                $"{reading.CoveringCount} floors here (floor {reading.PrimaryRevitId}, " +
                                $"top {Math.Round(reading.TopElevation)} mm to bottom " +
                                $"{Math.Round(reading.BottomElevation)} mm). Next thickest is " +
                                (next == null
                                    ? $"{Math.Round(reading.NextThickness)} mm."
                                    : $"floor {next.RevitId} '{next.TypeName}', {Math.Round(next.Thickness)} mm " +
                                      $"(top {Math.Round(next.TopElevation)} mm to bottom {Math.Round(next.BottomElevation)} mm).");
                }
            }

            if (Loads != null && !CuttingForSlab)
            {
                ApplyLoadVerdict(zone, face.Source, loads);
            }

            zone.BuildResults();
            return zone;
        }

        /// <summary>
        /// The load verdict on one zone: the four loads the loading plan asks for against the
        /// four the ETABS shell carries.
        ///
        /// Everything that stops the two being comparable - no region over this piece, a
        /// region nobody numbered, a number the schedule does not define, two regions that
        /// disagree - comes out as a review rather than as a failure, because none of them is
        /// the ETABS model being wrong. Only four numbers that were compared and differ is.
        /// </summary>
        /// <summary>
        /// Adds a sentence to whatever the zone already says, so a note written by the
        /// thickness pass is not thrown away by one the load pass wants to add.
        /// </summary>
        private static string AppendNote(string existing, string addition)
        {
            if (string.IsNullOrWhiteSpace(addition))
            {
                return existing ?? "";
            }
            return string.IsNullOrWhiteSpace(existing) ? addition : existing.TrimEnd() + " " + addition;
        }

        private void ApplyLoadVerdict(SlabZone zone, Slab_E area, LoadCoverage.Reading loads)
        {
            zone.LoadNumber = loads.Number ?? "";
            zone.LoadDescription = loads.Description ?? "";
            zone.LoadRegionId = loads.RevitId;
            zone.PlannedLoads = loads.Loads;

            // What this piece of plan carries, which is not always what its own shell carries.
            // Where a second shell is drawn over it, both put load on the same slab and the
            // piece carries the sum; read one shell at a time the other one's load would go
            // missing without a word.
            MyPoint at = zone.Plan == null ? null : zone.Plan.InteriorPoint();
            IList<AreaLoad_E> acting = StackedShells.LoadsAt(area, ShellsOnStory, at);
            IList<Slab_E> stacked = StackedShells.ShellsAt(area, ShellsOnStory, at);
            int shells = stacked.Count;
            zone.LoadShells = stacked.Select(shell => shell.UniqueName).ToList();

            AreaLoadSummary summary = AreaLoadSummary.From(acting, LoadPatterns);
            zone.EtabsLoads = summary.Loads;

            if (shells > 1)
            {
                // Worth saying outright. A load that reads as twice what the plan asks for is
                // nearly always two shells rather than a number typed wrong, and whoever is
                // looking at the finding should not have to go and find that out.
                zone.Note = AppendNote(zone.Note,
                    $"{shells} shells are drawn over this piece of plan, so the load on it is "
                    + "the sum of what all of them carry.");
            }

            if (!loads.Covered)
            {
                zone.LoadState = SlabLoadState.NoLoadingRegion;
                return;
            }
            if (loads.Conflicting)
            {
                zone.LoadState = SlabLoadState.ConflictingRegions;
                zone.LoadNote = loads.Others;
                return;
            }
            if (string.IsNullOrEmpty(loads.Number))
            {
                zone.LoadState = SlabLoadState.NoLoadNumber;
                zone.LoadNote = string.IsNullOrEmpty(loads.NumberText)
                    ? ""
                    : $"the parameter held \"{loads.NumberText}\", which does not read as a load label";
                return;
            }
            if (!loads.InSchedule)
            {
                zone.LoadState = SlabLoadState.NotInSchedule;
                return;
            }
            if (summary.IsEmpty)
            {
                zone.LoadState = SlabLoadState.NoEtabsLoad;
                return;
            }

            List<LoadCategory> wrong = new List<LoadCategory>();
            foreach (LoadCategory category in LoadSet.Categories)
            {
                if (Math.Abs(zone.PlannedLoads[category] - summary.Loads[category]) > LoadTolerance)
                {
                    wrong.Add(category);
                }
            }

            zone.MismatchedLoads = wrong;
            zone.LoadState = wrong.Count == 0 ? SlabLoadState.Matched : SlabLoadState.Mismatch;

            List<string> notes = new List<string>();
            if (summary.UnmappedPatterns.Count > 0)
            {
                notes.Add("load pattern(s) the settings do not name as one of the four, so their values " +
                          "are not counted: " + string.Join(", ", summary.UnmappedPatterns));
            }
            if (summary.NonGravityLoads.Count > 0)
            {
                notes.Add($"{summary.NonGravityLoads.Count} load(s) on this shell do not act downwards " +
                          "and are not counted");
            }

            if (notes.Count > 0)
            {
                zone.LoadNote = string.Join("; ", notes);
            }
        }

        #endregion

        #region part B - the Revit floor nothing is drawn over

        /// <summary>
        /// The Revit floor the ETABS model has nothing over. Same reading as on the ETABS side,
        /// the other way round: along the floor edge the two outlines do not agree, and
        /// anywhere else it is a hole ETABS has and Revit does not.
        /// </summary>
        public void ReportRevitOnly(string storyName, IList<EtabsFace> faces, SlabCoverage coverage)
        {
            if (coverage.Regions.Count == 0)
            {
                return;
            }

            FaceGrid grid = new FaceGrid(faces);

            foreach (SlabCoverage.Region region in coverage.Regions)
            {
                if (Stop())
                {
                    SettleHeldBack();
                    return;
                }

                if (!region.AtStoryPlane)
                {
                    continue;
                }

                try
                {
                    ReportRevitOnly(storyName, region, grid, coverage);
                }
                catch (Exception e)
                {
                    issues.Add($"{storyName}: Revit floor {region.RevitId} could not be measured " +
                               $"against the ETABS areas - {e.Message}");
                }
            }

            SettleHeldBack();
        }

        private void ReportRevitOnly(string storyName, SlabCoverage.Region region, FaceGrid grid,
                                     SlabCoverage coverage)
        {
            ConstrainedDelaunay cdt = new ConstrainedDelaunay();
            AddRing(cdt, region.Plan.Outer);
            foreach (PlanRing hole in region.Plan.Holes)
            {
                AddRing(cdt, hole);
            }

            List<EtabsFace> overlapping = grid.Overlapping(region.Plan.Bounds).ToList();

            // Only the outline of the mesh goes in, not every shell in it. A bare patch can only
            // be bounded by a shell edge with nothing on the other side of it; an edge two shells
            // share has shell on both sides, and cutting along it changes no answer. Putting
            // every shell in used to take a finely divided floor past the point cap, and the
            // whole floor was then given up on - every hole in it along with it.
            foreach (KeyValuePair<MyPoint, MyPoint> edge in OpenEdges(overlapping))
            {
                int from = cdt.AddOrSnapPoint(edge.Key.X, edge.Key.Y, SnapTolerance);
                int to = cdt.AddOrSnapPoint(edge.Value.X, edge.Value.Y, SnapTolerance);
                if (from != to)
                {
                    cdt.AddConstraint(from, to);
                }

                if (cdt.Points.Count > MaximumPointsPerTriangulation)
                {
                    issues.Add($"{storyName}: Revit floor {region.RevitId} has more ETABS areas over " +
                               $"it than can be cut apart, so any part of it the ETABS model does not " +
                               $"reach is not reported.");
                    return;
                }
            }

            if (overlapping.Count == 0)
            {
                // Nothing is drawn anywhere near it, so the whole floor is missing in ETABS.
                // A whole floor is reported however small it is, the same as a whole ETABS
                // area is: it is a thing the engineer drew, not a piece cut off one.
                Report(storyName, region, region.Plan, wholeFloor: true);
                return;
            }

            cdt.Triangulate();

            List<MyPoint> points = cdt.Points.Select(p => new MyPoint(p.X, p.Y)).ToList();
            int real = cdt.RealPointCount;
            List<int[]> kept = new List<int[]>();

            for (int t = 0; t < cdt.Triangles.Length; t++)
            {
                int[] triangle = cdt.Triangles[t];
                if (triangle == null || triangle.Length != 3)
                {
                    continue;
                }
                if (triangle[0] >= real || triangle[1] >= real || triangle[2] >= real)
                {
                    continue;
                }

                MyPoint centre = Centroid(points, triangle);

                if (!region.Plan.Contains(centre.X, centre.Y))
                {
                    continue;
                }
                if (grid.Covers(centre.X, centre.Y))
                {
                    // Inside the ETABS mesh, so part A has already answered for it.
                    continue;
                }

                // Where floors are stacked every one of them covers this spot, and each would
                // report the same bare patch. The floor the depth reading picks is the one that
                // reports it - picked among the floors at the plane only, because those are the
                // only ones that report at all, and a drop panel hung under the slab would
                // otherwise be picked and then stay silent.
                SlabCoverage.Reading reading = coverage.At(centre.X, centre.Y, atStoryPlaneOnly: true);
                if (reading.Covered && reading.PrimaryRevitId != region.RevitId)
                {
                    continue;
                }

                kept.Add(triangle);
            }

            foreach (PlanPolygon piece in Merged(points, kept))
            {
                Report(storyName, region, piece);
            }
        }

        private void Report(string storyName, SlabCoverage.Region region, PlanPolygon piece,
                            bool wholeFloor = false)
        {
            SlabZoneState state = piece.TouchesOutlineOf(region.Plan, EdgeTolerance)
                ? SlabZoneState.MissingInEtabs
                : SlabZoneState.OpeningInEtabsOnly;

            // A floor read off its sketch with cuts the sketch could not show may have no slab
            // here at all, so what it alone has is not called missing from ETABS: it is put up
            // as something the check could not settle.
            string note = "";
            if (region.OpeningsUncertain)
            {
                state = SlabZoneState.Unresolved;
                note = "Revit floor read from its sketch, which does not show everything cutting it - "
                       + "check in Revit whether there is slab here";
            }

            SlabZone zone = new SlabZone(piece, state, storyName)
            {
                Purpose = this.Purpose,
                MinimumIssueArea = this.MinimumIssueArea,
                RevitId = region.RevitId,
                RevitType = region.TypeName,
                RevitSource = region.Source,
                RevitThickness = region.Thickness,
                Note = note
            };
            zone.BuildResults();

            // Bare patches of one floor are pieces of one answer the same way the ETABS side's
            // are, so a piece too small to stand alone is held back until it is known whether
            // the patch carries on past it.
            Emit(zone, (int)state + "/R" + region.RevitId, !wholeFloor && IsTooSmall(piece));
        }

        /// <summary>
        /// The edges of these shells that no other shell among them shares - the outline of the
        /// mesh and of every hole in it. Two shells share an edge when they were divided from
        /// the same slab and meet at the same joints, which is the ordinary case.
        ///
        /// Ends are matched to the snap tolerance. Two ends that land either side of a step are
        /// taken as different, and so is an edge where one side is split by a joint the other
        /// side does not have; both are kept, which costs a constraint and never an answer.
        ///
        /// The outlines drawn over the shells go in as well, every one of them. An opening
        /// over two stacked shells is named by both, so counting its edges would read it as
        /// shared and leave it out - and it is exactly where a bare patch can begin.
        /// </summary>
        private IEnumerable<KeyValuePair<MyPoint, MyPoint>> OpenEdges(IList<EtabsFace> faces)
        {
            List<KeyValuePair<MyPoint, MyPoint>> open = OutlineEdges(faces);
            HashSet<EdgeKey> taken = new HashSet<EdgeKey>(open.Select(edge => KeyOf(edge.Key, edge.Value)));

            foreach (PlanPolygon over in faces.SelectMany(face => face.CoveredBy).Distinct())
            {
                foreach (PlanRing ring in new[] { over.Outer }.Concat(over.Holes))
                {
                    IList<MyPoint> points = ring.Points;
                    for (int k = 0; k < points.Count; k++)
                    {
                        MyPoint a = points[k], b = points[(k + 1) % points.Count];
                        if (taken.Add(KeyOf(a, b)))
                        {
                            open.Add(new KeyValuePair<MyPoint, MyPoint>(a, b));
                        }
                    }
                }
            }

            return open;
        }

        private EdgeKey KeyOf(MyPoint a, MyPoint b)
        {
            return new EdgeKey(Step(a.X), Step(a.Y), Step(b.X), Step(b.Y));
        }

        private List<KeyValuePair<MyPoint, MyPoint>> OutlineEdges(IList<EtabsFace> faces)
        {
            Dictionary<EdgeKey, KeyValuePair<MyPoint, MyPoint>> edges =
                new Dictionary<EdgeKey, KeyValuePair<MyPoint, MyPoint>>();
            Dictionary<EdgeKey, int> uses = new Dictionary<EdgeKey, int>();

            foreach (EtabsFace face in faces)
            {
                foreach (PlanRing ring in new[] { face.Plan.Outer }.Concat(face.Plan.Holes))
                {
                    if (ring == null || ring.Points.Count < 3)
                    {
                        continue;
                    }

                    IList<MyPoint> points = ring.Points;
                    for (int k = 0; k < points.Count; k++)
                    {
                        MyPoint a = points[k], b = points[(k + 1) % points.Count];
                        EdgeKey key = new EdgeKey(Step(a.X), Step(a.Y), Step(b.X), Step(b.Y));

                        int count;
                        uses.TryGetValue(key, out count);
                        uses[key] = count + 1;
                        if (count == 0)
                        {
                            edges[key] = new KeyValuePair<MyPoint, MyPoint>(a, b);
                        }
                    }
                }
            }

            return edges.Where(edge => uses[edge.Key] == 1).Select(edge => edge.Value).ToList();
        }

        private long Step(double value)
        {
            return (long)Math.Round(value / SnapTolerance);
        }

        /// <summary>An edge by its two ends, whichever way round it was walked.</summary>
        private struct EdgeKey : IEquatable<EdgeKey>
        {
            private readonly long ax, ay, bx, by;

            public EdgeKey(long x1, long y1, long x2, long y2)
            {
                bool swap = x1 > x2 || (x1 == x2 && y1 > y2);
                ax = swap ? x2 : x1;
                ay = swap ? y2 : y1;
                bx = swap ? x1 : x2;
                by = swap ? y1 : y2;
            }

            public bool Equals(EdgeKey other)
            {
                return ax == other.ax && ay == other.ay && bx == other.bx && by == other.by;
            }

            public override bool Equals(object obj)
            {
                return obj is EdgeKey && Equals((EdgeKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    long hash = ax;
                    hash = hash * 31 + ay;
                    hash = hash * 31 + bx;
                    hash = hash * 31 + by;
                    return (int)(hash ^ (hash >> 32));
                }
            }
        }

        /// <summary>
        /// The ETABS areas bucketed by plan position, so a triangle asks the few that could
        /// cover it rather than every area on the story.
        /// </summary>
        private sealed class FaceGrid
        {
            private readonly Dictionary<long, List<EtabsFace>> cells =
                new Dictionary<long, List<EtabsFace>>();
            private readonly List<EtabsFace> all;

            private const double CellSize = 2000d;

            public FaceGrid(IList<EtabsFace> faces)
            {
                all = faces.Where(face => face != null && face.Plan != null).ToList();

                foreach (EtabsFace face in all)
                {
                    PlanBounds bounds = face.Plan.Bounds;
                    for (long x = Cell(bounds.MinX); x <= Cell(bounds.MaxX); x++)
                    {
                        for (long y = Cell(bounds.MinY); y <= Cell(bounds.MaxY); y++)
                        {
                            long key = Key(x, y);
                            List<EtabsFace> bucket;
                            if (!cells.TryGetValue(key, out bucket))
                            {
                                bucket = new List<EtabsFace>();
                                cells[key] = bucket;
                            }
                            bucket.Add(face);
                        }
                    }
                }
            }

            public bool Covers(double x, double y)
            {
                List<EtabsFace> bucket;
                if (!cells.TryGetValue(Key(Cell(x), Cell(y)), out bucket))
                {
                    return false;
                }

                // Owned rather than merely inside, so an opening drawn over a shell stays a hole:
                // the shells stacked over a point own it, and an opening is owned by nobody.
                for (int i = 0; i < bucket.Count; i++)
                {
                    if (bucket[i].Owns(x, y))
                    {
                        return true;
                    }
                }

                return false;
            }

            public IEnumerable<EtabsFace> Overlapping(PlanBounds bounds)
            {
                HashSet<EtabsFace> seen = new HashSet<EtabsFace>();

                for (long x = Cell(bounds.MinX); x <= Cell(bounds.MaxX); x++)
                {
                    for (long y = Cell(bounds.MinY); y <= Cell(bounds.MaxY); y++)
                    {
                        List<EtabsFace> bucket;
                        if (!cells.TryGetValue(Key(x, y), out bucket))
                        {
                            continue;
                        }

                        foreach (EtabsFace face in bucket)
                        {
                            if (face.Plan.Bounds.IntersectsWith(bounds) && seen.Add(face))
                            {
                                yield return face;
                            }
                        }
                    }
                }
            }

            private static long Cell(double value)
            {
                return (long)Math.Floor(value / CellSize);
            }

            private static long Key(long x, long y)
            {
                return (x << 32) ^ (y & 0xFFFFFFFFL);
            }
        }

        #endregion

        #region putting edges into a triangulation

        /// <summary>Adds a whole ring as a closed run of constraints.</summary>
        private void AddRing(ConstrainedDelaunay cdt, PlanRing ring)
        {
            if (ring == null || ring.Points.Count < 3)
            {
                return;
            }

            int count = ring.Points.Count;
            int[] index = new int[count];

            for (int k = 0; k < count; k++)
            {
                index[k] = cdt.AddOrSnapPoint(ring.Points[k].X, ring.Points[k].Y, SnapTolerance);
            }

            for (int k = 0; k < count; k++)
            {
                if (index[k] != index[(k + 1) % count])
                {
                    cdt.AddConstraint(index[k], index[(k + 1) % count]);
                }
            }
        }

        /// <summary>
        /// Puts a slab edge into an area as a pair of constraints, one just inside the floor
        /// and one just outside it, rather than as the single line the edge really is. See
        /// <see cref="BoundaryOffset"/> for why.
        /// </summary>
        private void WalkRingDoubled(PlanRing ring, PlanPolygon owner, EtabsFace face,
                                     ConstrainedDelaunay cdt, ref bool cut)
        {
            if (ring == null || ring.Points.Count < 3)
            {
                return;
            }

            IList<MyPoint> points = ring.Points;
            int count = points.Count;

            // The longest edge decides which side of the ring the floor is on - a short edge,
            // or one that doubles back, would answer for the wrong side.
            int longest = -1;
            double best = 0;

            for (int k = 0; k < count; k++)
            {
                MyPoint a = points[k], b = points[(k + 1) % count];
                double length = (b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y);
                if (length > best)
                {
                    best = length;
                    longest = k;
                }
            }

            if (longest < 0 || best < 1e-12)
            {
                return;
            }

            {
                MyPoint a = points[longest], b = points[(longest + 1) % count];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double length = Math.Sqrt(dx * dx + dy * dy);
                double nx = -dy / length, ny = dx / length;
                double mx = (a.X + b.X) * 0.5, my = (a.Y + b.Y) * 0.5;
                double probe = BoundaryOffset * 10d;

                bool leftInside = owner.Contains(mx + nx * probe, my + ny * probe);
                double sign = leftInside ? 1d : -1d;

                WalkRing(Offset(points, sign * BoundaryOffset), face, cdt, ref cut);
                WalkRing(Offset(points, -sign * BoundaryOffset), face, cdt, ref cut);
            }
        }

        private void WalkRing(List<MyPoint> ring, EtabsFace face, ConstrainedDelaunay cdt, ref bool cut)
        {
            for (int k = 0; k < ring.Count; k++)
            {
                WalkSegment(ring[k], ring[(k + 1) % ring.Count], face, cdt, ref cut);
            }
        }

        /// <summary>
        /// Moves a closed ring sideways by <paramref name="distance"/>, each vertex along the
        /// bisector of its two edges so the corners still meet. Sharp corners are stopped from
        /// spiking out by capping how far a vertex may move.
        /// </summary>
        private static List<MyPoint> Offset(IList<MyPoint> ring, double distance)
        {
            int count = ring.Count;
            List<MyPoint> moved = new List<MyPoint>(count);

            for (int i = 0; i < count; i++)
            {
                MyPoint previous = ring[(i - 1 + count) % count];
                MyPoint current = ring[i];
                MyPoint next = ring[(i + 1) % count];

                double inX = current.X - previous.X, inY = current.Y - previous.Y;
                double inLength = Math.Sqrt(inX * inX + inY * inY);
                double outX = next.X - current.X, outY = next.Y - current.Y;
                double outLength = Math.Sqrt(outX * outX + outY * outY);

                if (inLength < 1e-9 || outLength < 1e-9)
                {
                    moved.Add(current);
                    continue;
                }

                inX /= inLength; inY /= inLength;
                outX /= outLength; outY /= outLength;

                double bx = -inY + -outY, by = inX + outX;
                double square = bx * bx + by * by;

                double dx, dy;
                if (square < 1e-9)
                {
                    // The ring doubles back on itself here, so there is no bisector to follow.
                    dx = -outY * distance;
                    dy = outX * distance;
                }
                else
                {
                    double scale = 2d * distance / square;
                    dx = bx * scale;
                    dy = by * scale;

                    double cap = 4d * Math.Abs(distance);
                    double reach = Math.Sqrt(dx * dx + dy * dy);
                    if (reach > cap && reach > 1e-12)
                    {
                        dx *= cap / reach;
                        dy *= cap / reach;
                    }
                }

                moved.Add(new MyPoint(current.X + dx, current.Y + dy));
            }

            return moved;
        }

        /// <summary>
        /// Traces one slab edge segment across an ETABS area and puts the part that lies inside
        /// it into the triangulation: the ends where they fall inside, and a shared vertex
        /// wherever it crosses the area outline, so the edge is reproduced exactly.
        /// </summary>
        private void WalkSegment(MyPoint from, MyPoint to, EtabsFace face,
                                 ConstrainedDelaunay cdt, ref bool cut)
        {
            PlanBounds bounds = face.Plan.Bounds;

            if (Math.Max(from.X, to.X) < bounds.MinX || Math.Min(from.X, to.X) > bounds.MaxX)
            {
                return;
            }
            if (Math.Max(from.Y, to.Y) < bounds.MinY || Math.Min(from.Y, to.Y) > bounds.MaxY)
            {
                return;
            }

            bool fromInside = face.Plan.Contains(from.X, from.Y);
            bool toInside = face.Plan.Contains(to.X, to.Y);

            int fromIndex = fromInside ? cdt.AddOrSnapPoint(from.X, from.Y, SnapTolerance) : -1;
            int toIndex = toInside ? cdt.AddOrSnapPoint(to.X, to.Y, SnapTolerance) : -1;

            if (fromInside || toInside)
            {
                cut = true;
            }

            List<KeyValuePair<double, int>> crossings = new List<KeyValuePair<double, int>>(4);

            Cross(from, to, face.Plan.Outer, cdt, crossings, ref cut);
            foreach (PlanRing hole in face.Plan.Holes)
            {
                Cross(from, to, hole, cdt, crossings, ref cut);
            }

            List<int> path = new List<int>(crossings.Count + 2);
            if (fromInside)
            {
                path.Add(fromIndex);
            }

            crossings.Sort((a, b) => a.Key.CompareTo(b.Key));
            foreach (KeyValuePair<double, int> crossing in crossings)
            {
                path.Add(crossing.Value);
            }

            if (toInside)
            {
                path.Add(toIndex);
            }

            for (int i = 0; i + 1 < path.Count; i++)
            {
                if (path[i] != path[i + 1])
                {
                    cdt.AddConstraint(path[i], path[i + 1]);
                }
            }
        }

        private void Cross(MyPoint from, MyPoint to, PlanRing ring, ConstrainedDelaunay cdt,
                           List<KeyValuePair<double, int>> crossings, ref bool cut)
        {
            IList<MyPoint> points = ring.Points;

            for (int j = 0; j < points.Count; j++)
            {
                MyPoint a = points[j];
                MyPoint b = points[(j + 1) % points.Count];

                double x, y, along, across;
                if (!Meet(from, to, a, b, out x, out y, out along, out across))
                {
                    continue;
                }

                // Only a strict crossing counts. Meeting exactly at an end is the ring already
                // sharing that point, and adding it again would put a zero length constraint in.
                if (along <= 1e-9 || along >= 1d - 1e-9)
                {
                    continue;
                }
                if (across <= 1e-9 || across >= 1d - 1e-9)
                {
                    continue;
                }

                crossings.Add(new KeyValuePair<double, int>(along,
                    cdt.AddOrSnapPoint(x, y, SnapTolerance)));
                cut = true;
            }
        }

        private static bool Meet(MyPoint a, MyPoint b, MyPoint c, MyPoint d,
                                 out double x, out double y, out double t, out double s)
        {
            x = 0; y = 0; t = 0; s = 0;

            double abx = b.X - a.X, aby = b.Y - a.Y;
            double cdx = d.X - c.X, cdy = d.Y - c.Y;
            double denominator = abx * cdy - aby * cdx;

            if (Math.Abs(denominator) < 1e-15)
            {
                return false;
            }

            double acx = c.X - a.X, acy = c.Y - a.Y;
            t = (acx * cdy - acy * cdx) / denominator;
            s = (acx * aby - acy * abx) / denominator;

            if (t < -1e-9 || t > 1d + 1e-9 || s < -1e-9 || s > 1d + 1e-9)
            {
                return false;
            }

            x = a.X + t * abx;
            y = a.Y + t * aby;
            return true;
        }

        #endregion

        private static IList<PlanPolygon> Merged(IList<MyPoint> points, IList<int[]> triangles)
        {
            return TriangleMerge.Merge(points, triangles);
        }

        private static MyPoint Centroid(IList<MyPoint> points, int[] triangle)
        {
            MyPoint a = points[triangle[0]], b = points[triangle[1]], c = points[triangle[2]];
            return new MyPoint((a.X + b.X + c.X) / 3d, (a.Y + b.Y + c.Y) / 3d);
        }
    }
}
