using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Raised when the geometry kernel cannot carry out a region operation. The slab check
    /// catches this per ETABS face, so one awkward outline costs that one face its result
    /// rather than the whole run.
    /// </summary>
    public class PlanRegionException : Exception
    {
        public PlanRegionException(string message, Exception inner = null) : base(message, inner)
        {
        }
    }

    /// <summary>
    /// Intersection, difference and union on plan regions, which is what lets the slab check
    /// cut the ETABS mesh exactly where the two models stop agreeing instead of colouring
    /// whole shells.
    ///
    /// The work is handed to the Revit geometry kernel rather than done here: every region is
    /// extruded into a prism of the same height, the prisms are combined, and the base of the
    /// result is read back as regions. A prism boolean is a plan boolean, and going through
    /// the kernel means holes, disjoint pieces and edges that very nearly line up are all
    /// handled by code that is already trusted to get them right - and adds no dependency,
    /// since this only ever runs inside Revit.
    ///
    /// Regions are in model mm on the way in and on the way out; feet are an internal detail,
    /// used because the kernel's own tolerances are stated in them.
    /// </summary>
    public static class PlanRegionMath
    {
        private const double feetToMM = 304.8d;

        /// <summary>
        /// Prism height, in feet. Only has to be well clear of the kernel's tolerances; the
        /// plan result is the same whatever it is.
        /// </summary>
        private const double PrismHeightInFeet = 1d;

        /// <summary>
        /// Where the other operand of a boolean starts and how tall it is, in feet.
        ///
        /// It is deliberately not the same prism as the region being cut. Two prisms built on
        /// the same plane and to the same height meet face to face top and bottom, and a
        /// boolean between two solids whose horizontal faces are coplanar is the case the
        /// kernel is least able to do - which is exactly what it says when it refuses:
        /// coincident edges, more than two faces meeting along one edge. Standing the cutter
        /// clear above and below leaves no coplanar pair, and since it spans the other prism
        /// completely the answer in plan is the one it always was.
        /// </summary>
        private const double CutterBaseInFeet = -1d;
        private const double CutterHeightInFeet = 3d;

        /// <summary>
        /// A second size for the cutter, tried when the first is still refused. Failures here
        /// turn on how two outlines happen to land against each other, so simply presenting
        /// the same question at a different scale is often enough.
        /// </summary>
        private const double RetryCutterBaseInFeet = -2.5d;
        private const double RetryCutterHeightInFeet = 6d;

        /// <summary>
        /// Smallest prism volume worth reading back, in cubic feet. Below this the result is
        /// numerical noise from two outlines that were meant to coincide.
        /// </summary>
        private const double MinimumVolumeInCubicFeet = 1e-7;

        #region operations

        /// <summary>The part of <paramref name="a"/> that <paramref name="b"/> also covers.</summary>
        public static IList<PlanPolygon> Intersect(PlanPolygon a, PlanPolygon b)
        {
            if (a == null || b == null || !a.Bounds.IntersectsWith(b.Bounds))
            {
                return new List<PlanPolygon>();
            }

            // Two regions that miss each other share nothing, and one that holds the other
            // completely shares all of it. Both are the whole answer, and both are settled on
            // the outlines in a fraction of what a boolean costs - which matters because on a
            // real floor most pairs are one or the other, and only the few that genuinely cut
            // across each other need the kernel at all.
            if (PlanRegionQuick.Disjoint(a, b))
            {
                CheckTiming.SkippedAsDisjoint++;
                return new List<PlanPolygon>();
            }

            if (PlanRegionQuick.Covers(b, a))
            {
                CheckTiming.SkippedAsContained++;
                return new List<PlanPolygon> { a };
            }

            if (PlanRegionQuick.Covers(a, b))
            {
                CheckTiming.SkippedAsContained++;
                return new List<PlanPolygon> { b };
            }

            return Combine(a, new[] { b }, BooleanOperationsType.Intersect);
        }

        /// <summary>
        /// What is left of <paramref name="a"/> once every region in <paramref name="others"/>
        /// has been taken off it.
        /// </summary>
        public static IList<PlanPolygon> Subtract(PlanPolygon a, IEnumerable<PlanPolygon> others)
        {
            if (a == null)
            {
                return new List<PlanPolygon>();
            }

            List<PlanPolygon> overlapping = new List<PlanPolygon>();

            foreach (PlanPolygon other in others ?? Enumerable.Empty<PlanPolygon>())
            {
                if (other == null || !a.Bounds.IntersectsWith(other.Bounds))
                {
                    continue;
                }

                // Takes nothing away, so it is not worth a boolean.
                if (PlanRegionQuick.Disjoint(a, other))
                {
                    CheckTiming.SkippedAsDisjoint++;
                    continue;
                }

                // Takes everything away, so nothing is left and the rest need not be asked.
                if (PlanRegionQuick.Covers(other, a))
                {
                    CheckTiming.SkippedAsContained++;
                    return new List<PlanPolygon>();
                }

                overlapping.Add(other);
            }

            if (overlapping.Count == 0)
            {
                return new List<PlanPolygon> { a };
            }

            return Combine(a, overlapping, BooleanOperationsType.Difference);
        }

        /// <summary>
        /// Everything the given regions cover between them, with the seams where they meet
        /// taken out. Regions that do not touch come back as separate pieces.
        /// </summary>
        public static IList<PlanPolygon> Union(IEnumerable<PlanPolygon> regions)
        {
            List<PlanPolygon> all = (regions ?? Enumerable.Empty<PlanPolygon>())
                .Where(region => region != null)
                .ToList();

            if (all.Count <= 1)
            {
                return all;
            }

            // Regions whose boxes do not even touch cannot merge into one piece, so they are
            // never put through the kernel together. Merging a whole story into a single
            // solid and reading it back gives the same answer, but every step of it carries
            // the weight of every region already folded in, including the far side of the
            // building that was never going to join.
            List<PlanPolygon> merged = new List<PlanPolygon>();

            foreach (List<PlanPolygon> cluster in ClusterByBounds(all))
            {
                if (cluster.Count == 1)
                {
                    merged.Add(cluster[0]);
                    continue;
                }

                foreach (PlanPolygon piece in UnionCluster(cluster))
                {
                    merged.Add(piece);
                }
            }

            return merged;
        }

        /// <summary>
        /// Groups regions so that two that could possibly touch are in the same group. Boxes
        /// are generous - an L shaped slab claims the corner it does not cover - so a group
        /// may hold regions that turn out not to meet, which costs a little work and no
        /// accuracy. Missing a pair that does meet would cost an answer, and cannot happen:
        /// regions that overlap have overlapping boxes.
        /// </summary>
        private static IEnumerable<List<PlanPolygon>> ClusterByBounds(IList<PlanPolygon> regions)
        {
            int[] parent = new int[regions.Count];
            for (int i = 0; i < parent.Length; i++)
            {
                parent[i] = i;
            }

            for (int i = 0; i < regions.Count; i++)
            {
                for (int j = i + 1; j < regions.Count; j++)
                {
                    if (regions[i].Bounds.IntersectsWith(regions[j].Bounds))
                    {
                        Join(parent, i, j);
                    }
                }
            }

            Dictionary<int, List<PlanPolygon>> clusters = new Dictionary<int, List<PlanPolygon>>();
            for (int i = 0; i < regions.Count; i++)
            {
                int root = Root(parent, i);
                List<PlanPolygon> cluster;
                if (!clusters.TryGetValue(root, out cluster))
                {
                    cluster = new List<PlanPolygon>();
                    clusters[root] = cluster;
                }
                cluster.Add(regions[i]);
            }

            return clusters.Values;
        }

        private static int Root(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        private static void Join(int[] parent, int a, int b)
        {
            int rootA = Root(parent, a);
            int rootB = Root(parent, b);
            if (rootA != rootB)
            {
                parent[rootB] = rootA;
            }
        }

        /// <summary>
        /// Merges regions that may touch, in pairs and then in pairs of those, rather than
        /// one at a time onto a growing pile. Both ways reach the same shape, but folding one
        /// at a time makes every step read the whole of what came before, which is what made
        /// merging a busy story take longer than everything else in the check put together.
        /// </summary>
        private static IList<PlanPolygon> UnionCluster(IList<PlanPolygon> cluster)
        {
            try
            {
                List<Solid> level = new List<Solid>();
                foreach (PlanPolygon region in cluster)
                {
                    Solid solid = ToSolid(region);
                    if (solid != null)
                    {
                        level.Add(solid);
                    }
                }

                if (level.Count == 0)
                {
                    return new List<PlanPolygon>();
                }

                while (level.Count > 1)
                {
                    List<Solid> next = new List<Solid>();

                    for (int i = 0; i < level.Count; i += 2)
                    {
                        if (i + 1 == level.Count)
                        {
                            // Odd one out; it waits for the next round.
                            next.Add(level[i]);
                            continue;
                        }

                        Solid joined = BooleanOperationsUtils.ExecuteBooleanOperation(
                            level[i], level[i + 1], BooleanOperationsType.Union);

                        if (joined == null)
                        {
                            // Dropping the pair would quietly lose two regions, and putting
                            // them back would only pair them again. The caller is told, and
                            // every caller has a way of carrying on without the merge.
                            throw new PlanRegionException(
                                "two plan regions would not merge into one outline");
                        }

                        next.Add(joined);
                    }

                    level = next;
                }

                return FromSolid(level[0]);
            }
            catch (PlanRegionException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new PlanRegionException(
                    $"the union of {cluster.Count} plan regions could not be worked out: {e.Message}", e);
            }
        }

        /// <summary>
        /// Drops the pieces too small or too thin to mean anything. Two outlines that were
        /// drawn to the same grid line but land a fraction of a millimetre apart leave a
        /// hairline behind, and without this every such edge in the model would be reported
        /// as a mismatch.
        /// </summary>
        public static IList<PlanPolygon> DropSlivers(IEnumerable<PlanPolygon> regions,
                                                     double minimumArea, double minimumWidth)
        {
            return (regions ?? Enumerable.Empty<PlanPolygon>())
                .Where(region => region != null
                                 && region.Area >= minimumArea
                                 && region.TypicalWidth >= minimumWidth)
                .ToList();
        }

        /// <summary>
        /// Everything the given regions cover, merging whatever the kernel will merge and
        /// leaving the rest as separate pieces rather than giving up on the lot.
        ///
        /// <see cref="Union"/> is all or nothing, which is right where the caller has a way of
        /// carrying on without the merge. Reading a shape edited floor does not: its facets
        /// have to become the region the floor covers, and those facets meet along shared
        /// edges, which is the arrangement the kernel is least able to work with. One pair it
        /// will not join should cost that seam, not the floor.
        /// </summary>
        public static IList<PlanPolygon> UnionWhatWillMerge(IEnumerable<PlanPolygon> regions)
        {
            List<PlanPolygon> all = (regions ?? Enumerable.Empty<PlanPolygon>())
                .Where(region => region != null)
                .ToList();

            if (all.Count <= 1)
            {
                return all;
            }

            List<PlanPolygon> merged = new List<PlanPolygon>();

            foreach (List<PlanPolygon> cluster in ClusterByBounds(all))
            {
                if (cluster.Count == 1)
                {
                    merged.Add(cluster[0]);
                    continue;
                }

                foreach (PlanPolygon piece in MergeWhatWillMerge(cluster))
                {
                    merged.Add(piece);
                }
            }

            return merged;
        }

        private static IList<PlanPolygon> MergeWhatWillMerge(IList<PlanPolygon> cluster)
        {
            List<Solid> level = new List<Solid>();

            foreach (PlanPolygon region in cluster)
            {
                try
                {
                    Solid solid = ToSolid(region);
                    if (solid != null)
                    {
                        level.Add(solid);
                    }
                }
                catch (PlanRegionException)
                {
                    // One facet that will not build is one facet lost, not the floor.
                }
            }

            // Pairs, then pairs of those. A round that joins nothing means the rest will not
            // join either, so the pieces left are returned as they are and the walk ends.
            while (level.Count > 1)
            {
                List<Solid> next = new List<Solid>();
                bool joinedAny = false;

                for (int i = 0; i < level.Count; i += 2)
                {
                    if (i + 1 == level.Count)
                    {
                        next.Add(level[i]);
                        continue;
                    }

                    Solid joined = null;
                    try
                    {
                        joined = BooleanOperationsUtils.ExecuteBooleanOperation(
                            level[i], level[i + 1], BooleanOperationsType.Union);
                    }
                    catch (Exception)
                    {
                        joined = null;
                    }

                    if (joined == null)
                    {
                        next.Add(level[i]);
                        next.Add(level[i + 1]);
                        continue;
                    }

                    next.Add(joined);
                    joinedAny = true;
                }

                level = next;

                if (!joinedAny)
                {
                    break;
                }
            }

            List<PlanPolygon> regions = new List<PlanPolygon>();
            foreach (Solid solid in level)
            {
                foreach (PlanPolygon region in FromSolid(solid))
                {
                    regions.Add(region);
                }
            }

            return regions;
        }

        /// <summary>
        /// Whether the kernel will accept this region at all, with the reason when it will not.
        ///
        /// Worth asking once, when a region is first built, because a region that cannot be
        /// turned into a solid does not fail quietly later - it throws on every operation it
        /// is ever part of, and one of them is enough to fill a run with the same message.
        /// The answer is cached with the region, so asking costs nothing that was not going to
        /// be spent anyway.
        /// </summary>
        public static bool CanBuild(PlanPolygon region, out string reason)
        {
            reason = null;

            if (region == null)
            {
                reason = "there is no region";
                return false;
            }

            try
            {
                ToSolid(region);
                return true;
            }
            catch (PlanRegionException e)
            {
                reason = e.Message;
                return false;
            }
        }

        /// <summary>Total area, in mm squared, of a set of regions.</summary>
        public static double TotalArea(IEnumerable<PlanPolygon> regions)
        {
            return (regions ?? Enumerable.Empty<PlanPolygon>())
                .Where(region => region != null)
                .Sum(region => region.Area);
        }

        #endregion

        #region kernel

        private static IList<PlanPolygon> Combine(PlanPolygon first, IEnumerable<PlanPolygon> rest,
                                                  BooleanOperationsType operation)
        {
            List<PlanPolygon> others = (rest ?? Enumerable.Empty<PlanPolygon>()).ToList();

            try
            {
                return CombineAt(first, others, operation, 1d);
            }
            catch (PlanRegionException)
            {
                // Everything at the size the model is drawn was refused. The whole operation
                // is done again on a copy ten times bigger, where the edges that were too
                // close to tell apart are plainly apart, and the result is brought back down.
                // Scaling moves no edge relative to any other, so this is the same question,
                // only asked where the kernel can answer it.
                try
                {
                    IList<PlanPolygon> won = CombineAt(first, others, operation, BooleanRetryScale);
                    CheckTiming.BooleansWonByScalingUp++;
                    return won;
                }
                catch (PlanRegionException)
                {
                    CheckTiming.BooleansLost++;

                    // One record for the operation, not one for every region it was given.
                    // A difference against five hundred cutters that fails is one failure;
                    // writing it five hundred times turned a readable file into half a
                    // gigabyte and buried the eighty odd real ones.
                    GeometryDump.Fault($"one plan region against {others.Count} other(s)",
                                       $"{operation.ToString().ToLower()}, at two prism sizes " +
                                       $"and again {BooleanRetryScale}x larger",
                                       "the kernel refused every attempt", first);

                    // Said in full, so a refusal names every way it was asked rather than
                    // only the first, which read as though the larger attempt never ran.
                    throw new PlanRegionException(
                        $"the {operation.ToString().ToLower()} of two plan regions could not be worked out: " +
                        $"the geometry kernel refused it at two prism sizes and again " +
                        $"{BooleanRetryScale}x larger");
                }
            }
        }

        private static IList<PlanPolygon> CombineAt(PlanPolygon first, IList<PlanPolygon> rest,
                                                    BooleanOperationsType operation, double scale)
        {
            bool scaled = Math.Abs(scale - 1d) > 1e-9;

            try
            {
                Solid accumulated = scaled
                    ? Built(BuildSolid(first, 0d, PrismHeightInFeet, scale))
                    : ToSolid(first);
                if (accumulated == null)
                {
                    return new List<PlanPolygon>();
                }

                foreach (PlanPolygon region in rest)
                {
                    // The prism already built for this region, standing on the same plane as
                    // the one being cut. Two prisms that meet face to face are the case the
                    // kernel likes least, but it is only actually refused about one time in a
                    // hundred - and building a second, taller prism for every region to avoid
                    // that cost more than the refusals did. So the cheap one is tried first
                    // and the tall one built only where it is needed.
                    Solid other = scaled
                        ? Built(BuildSolid(region, CutterBaseInFeet, CutterHeightInFeet, scale))
                        : ToSolid(region);

                    if (other == null)
                    {
                        // Too small to enclose anything, so it takes nothing away and adds
                        // nothing. A region with real area that cannot be built throws from
                        // inside ToCutterSolid rather than being passed over here, because
                        // quietly skipping one would give a confident wrong answer.
                        continue;
                    }

                    Solid combined = Attempt(accumulated, other, operation);

                    if (combined == null && !scaled)
                    {
                        // Refused with both prisms on the same plane, which is the case the
                        // kernel struggles with. Now the cutter is built standing clear above
                        // and below, so no two horizontal faces are coplanar, and asked again.
                        CheckTiming.BooleansRefusedFirstTry++;

                        Solid tall = ToCutterSolid(region);
                        if (tall != null)
                        {
                            combined = Attempt(accumulated, tall, operation);
                        }

                        if (combined == null)
                        {
                            CheckTiming.BooleansRefusedAfterResize++;
                        }
                    }

                    if (combined == null)
                    {
                        throw new PlanRegionException(
                            $"the {operation.ToString().ToLower()} of two plan regions could not be worked " +
                            "out: the geometry kernel refused it at two different prism sizes");
                    }

                    accumulated = combined;
                    if (accumulated.Volume < MinimumVolumeInCubicFeet * scale * scale * scale)
                    {
                        // Nothing left to take from, and nothing left to report.
                        return new List<PlanPolygon>();
                    }
                }

                return FromSolid(accumulated, scale);
            }
            catch (PlanRegionException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new PlanRegionException(
                    $"the {operation.ToString().ToLower()} of two plan regions could not be worked out: {e.Message}", e);
            }
        }

        /// <summary>The solid a build produced, or the reason it did not, as an exception.</summary>
        private static Solid Built(SolidOf built)
        {
            return Unwrap(built);
        }

        /// <summary>
        /// One boolean, with a refusal reported as null rather than thrown, so the caller can
        /// present the same question a second way before giving up on it.
        /// </summary>
        private static Solid Attempt(Solid accumulated, Solid other, BooleanOperationsType operation)
        {
            CheckTiming.BooleansTried++;
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                return BooleanOperationsUtils.ExecuteBooleanOperation(accumulated, other, operation);
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                watch.Stop();
                CheckTiming.MillisecondsInBooleans += watch.ElapsedMilliseconds;
            }
        }

        /// <summary>
        /// The region as a prism standing on the z = 0 plane.
        ///
        /// Null means the region encloses nothing worth speaking of, which the callers are
        /// right to treat as empty. A region that does enclose something and still cannot be
        /// built - self intersecting, or an outline the kernel will not accept - throws
        /// instead, because passing over one of those would quietly turn "I cannot tell" into
        /// "there is nothing there", and the check would report a difference that is not real.
        /// </summary>
        private static Solid ToSolid(PlanPolygon region)
        {
            if (region == null || region.Area < NegligibleAreaInSquareMM)
            {
                return null;
            }

            CheckTiming.SolidsAsked++;
            return Unwrap(solidCache.GetValue(region, buildSolid));
        }

        /// <summary>
        /// The region as the other operand of a boolean: the same plan, standing clear above
        /// and below the prism it is being measured against. See <see cref="CutterBaseInFeet"/>
        /// for why it is not simply the same prism.
        /// </summary>
        private static Solid ToCutterSolid(PlanPolygon region)
        {
            if (region == null || region.Area < NegligibleAreaInSquareMM)
            {
                return null;
            }

            CheckTiming.SolidsAsked++;
            return Unwrap(cutterCache.GetValue(region, buildCutterSolid));
        }

        private static Solid Unwrap(SolidOf built)
        {
            if (built.Failure != null)
            {
                throw built.Inner == null
                    ? new PlanRegionException(built.Failure)
                    : new PlanRegionException(built.Failure, built.Inner);
            }

            return built.Solid;
        }

        /// <summary>
        /// The prism a region was last turned into, or the reason it could not be.
        ///
        /// A region takes part in as many operations as there are things near it - a floor is
        /// measured against every area over it, and again on every story its stack reaches -
        /// and extruding it afresh each time is most of what the check used to spend its time
        /// on. Building it once is the same answer for a fraction of the work, and is sound
        /// because a region never changes once made and the kernel leaves the operands of a
        /// boolean alone.
        /// </summary>
        private sealed class SolidOf
        {
            public Solid Solid;
            public string Failure;
            public Exception Inner;
        }

        /// <summary>
        /// Keyed on the region itself and holding it no longer than the rest of the check
        /// does, so the pieces a boolean throws off take their prisms with them when they go
        /// rather than piling up for the length of the run.
        /// </summary>
        private static readonly ConditionalWeakTable<PlanPolygon, SolidOf> solidCache
            = new ConditionalWeakTable<PlanPolygon, SolidOf>();

        private static readonly ConditionalWeakTable<PlanPolygon, SolidOf> cutterCache
            = new ConditionalWeakTable<PlanPolygon, SolidOf>();

        private static readonly ConditionalWeakTable<PlanPolygon, SolidOf>.CreateValueCallback buildSolid
            = region => BuildSolid(region, 0d, PrismHeightInFeet);

        private static readonly ConditionalWeakTable<PlanPolygon, SolidOf>.CreateValueCallback buildCutterSolid
            = region => BuildSolid(region, CutterBaseInFeet, CutterHeightInFeet);

        /// <summary>
        /// Builds the prism, recording rather than throwing what went wrong: the failure has
        /// to be remembered along with the success, or an outline the kernel will not accept
        /// is retried every time it comes up.
        /// </summary>
        private static SolidOf BuildSolid(PlanPolygon region, double baseZ, double height)
        {
            return BuildSolid(region, baseZ, height, 1d);
        }

        private static SolidOf BuildSolid(PlanPolygon region, double baseZ, double height, double scale)
        {
            CheckTiming.SolidsBuilt++;
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                return BuildSolidCore(region, baseZ, height, scale);
            }
            finally
            {
                watch.Stop();
                CheckTiming.MillisecondsBuildingSolids += watch.ElapsedMilliseconds;
            }
        }

        private static SolidOf BuildSolidCore(PlanPolygon region, double baseZ, double height, double scale)
        {
            CurveLoop outer = ToCurveLoop(region.Outer, baseZ, scale);
            if (outer == null)
            {
                return new SolidOf
                {
                    Failure = $"an outline of {Math.Round(region.Area / 1e6, 2)} m2 could not be " +
                              "turned into a closed loop"
                };
            }

            List<CurveLoop> loops = new List<CurveLoop> { outer };
            foreach (PlanRing hole in region.Holes)
            {
                CurveLoop holeLoop = ToCurveLoop(hole, baseZ, scale);
                if (holeLoop != null)
                {
                    loops.Add(holeLoop);
                }
            }

            try
            {
                return new SolidOf
                {
                    Solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                        loops, XYZ.BasisZ, height)
                };
            }
            catch (Exception e)
            {
                GeometryDump.Fault($"outline of {Math.Round(region.Area / 1e6, 2)} m2",
                                   $"extruding it into a prism {height} ft tall from z {baseZ} ft",
                                   e.Message, region);

                return new SolidOf
                {
                    Failure = $"an outline of {Math.Round(region.Area / 1e6, 2)} m2 could not be built - " +
                              "it may cross itself or double back on itself: " + e.Message,
                    Inner = e
                };
            }
        }

        /// <summary>
        /// An area below this encloses nothing in any real sense: a square a tenth of a
        /// millimetre on a side. Regions this small come from arithmetic, not from a model.
        /// </summary>
        private const double NegligibleAreaInSquareMM = 0.01d;

        /// <summary>
        /// How much bigger the geometry is made for the second try at a boolean.
        ///
        /// The kernel's tolerances are absolute, so how well it copes depends on how large
        /// the model's features are against them, not on their shape. Two outlines drawn to
        /// the same gridline that land a fifth of a millimetre apart are the hard case: too
        /// far apart to be the same edge, too close to be safely different, and the boolean
        /// between them is refused. Building the same two shapes ten times bigger puts that
        /// gap at two millimetres, which is plainly two edges, and the answer scales back
        /// down exactly because scaling is a similarity - it moves no edge relative to
        /// another.
        /// </summary>
        private const double BooleanRetryScale = 10d;

        private static CurveLoop ToCurveLoop(PlanRing ring, double atZ)
        {
            return ToCurveLoop(ring, atZ, 1d);
        }

        private static CurveLoop ToCurveLoop(PlanRing ring, double atZ, double scale)
        {
            if (ring == null || ring.Points.Count < 3)
            {
                return null;
            }

            List<XYZ> all = ring.Points
                .Select(point => new XYZ(point.X / feetToMM * scale, point.Y / feetToMM * scale, atZ))
                .ToList();

            // A point too close to the one before it is dropped, and the ring then runs from
            // the point before straight to the point after. Dropping the EDGE instead, which
            // is what used to happen here, left the ring open: the edge from the point before
            // was thrown away and the next one started a vertex further on, so the loop had a
            // gap in it the width of the edge that went. A gap under the kernel's own
            // tolerance is small enough for CurveLoop to accept and still too much for the
            // extrusion, which came back saying only that the loops did not satisfy its
            // requirements - the shape was never closed in the first place.
            List<XYZ> vertices = new List<XYZ>();
            foreach (XYZ point in all)
            {
                if (vertices.Count == 0
                    || vertices[vertices.Count - 1].DistanceTo(point) >= ShortCurveToleranceInFeet)
                {
                    vertices.Add(point);
                }
            }

            // The closing edge is subject to the same rule, so a last point that lands on the
            // first is dropped rather than closing the ring with an edge too short to exist.
            while (vertices.Count > 2
                   && vertices[vertices.Count - 1].DistanceTo(vertices[0]) < ShortCurveToleranceInFeet)
            {
                vertices.RemoveAt(vertices.Count - 1);
            }

            if (vertices.Count < 3)
            {
                return null;
            }

            CurveLoop loop = new CurveLoop();
            for (int i = 0; i < vertices.Count; i++)
            {
                try
                {
                    loop.Append(Line.CreateBound(vertices[i], vertices[(i + 1) % vertices.Count]));
                }
                catch (Exception)
                {
                    return null;
                }
            }

            return loop.NumberOfCurves() < 3 ? null : loop;
        }

        /// <summary>
        /// Revit rejects a curve shorter than this outright. Kept as a constant rather than
        /// read off the application, because this runs where no Application is to hand and
        /// the value has not moved in many releases.
        /// </summary>
        private const double ShortCurveToleranceInFeet = 1d / 384d;

        /// <summary>
        /// Reads the plan regions back off the base of a prism. A boolean can leave several
        /// pieces that do not touch, and each one has a base face of its own, so this returns
        /// a region per piece.
        /// </summary>
        private static IList<PlanPolygon> FromSolid(Solid solid)
        {
            return FromSolid(solid, 1d);
        }

        private static IList<PlanPolygon> FromSolid(Solid solid, double scale)
        {
            List<PlanPolygon> regions = new List<PlanPolygon>();
            if (solid == null || solid.Volume < MinimumVolumeInCubicFeet)
            {
                return regions;
            }

            foreach (Face face in solid.Faces)
            {
                // The base of the prism, which is the plan region. Tested by which way the
                // face looks rather than by an exact match against -Z, so a normal that a
                // boolean left a fraction off does not lose the whole region.
                PlanarFace planar = face as PlanarFace;
                if (planar == null || planar.FaceNormal.Z > -0.99)
                {
                    continue;
                }

                List<PlanRing> rings = planar.GetEdgesAsCurveLoops()
                    .Select(loop => ToRing(loop, scale))
                    .Where(ring => ring != null)
                    .ToList();

                if (rings.Count == 0)
                {
                    continue;
                }

                // Revit reports the outer loop first, but taking the largest ring instead is
                // free and does not depend on that holding.
                PlanRing outline = rings.OrderByDescending(ring => ring.Area).First();
                PlanPolygon region = PlanPolygon.Create(outline, rings.Where(ring => ring != outline));
                if (region != null)
                {
                    regions.Add(region);
                }
            }

            return regions;
        }

        private static PlanRing ToRing(CurveLoop loop, double scale)
        {
            if (loop == null)
            {
                return null;
            }

            List<MyPoint> points = new List<MyPoint>();
            foreach (Curve curve in loop)
            {
                // Tessellating rather than reading the two ends keeps a curved slab edge -
                // which Revit allows and ETABS does not - as the shape it actually is.
                IList<XYZ> vertices = curve.Tessellate();
                for (int i = 0; i < vertices.Count - 1; i++)
                {
                    points.Add(new MyPoint(vertices[i].X * feetToMM / scale,
                                           vertices[i].Y * feetToMM / scale));
                }
            }

            return PlanRing.FromPoints(points);
        }

        #endregion
    }
}
