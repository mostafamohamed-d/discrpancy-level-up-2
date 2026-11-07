using System;
using System.Collections.Generic;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Self-contained 2-D constrained Delaunay triangulation.
    ///
    /// Pipeline:
    ///   1. <see cref="PreSplitCrossingConstraints"/> — every strict crossing
    ///      between two constraint segments becomes a shared vertex; both
    ///      segments are split there.
    ///   2. <see cref="PreSplitConstraintsAtPoints"/> — every point that lies
    ///      on the interior of a constraint segment splits that segment.
    ///   3. <see cref="BuildBowyerWatson"/> — incremental Bowyer-Watson
    ///      insertion produces an (unconstrained) Delaunay triangulation of
    ///      the resolved point set.
    ///   4. <see cref="RecoverConstraints"/> — Sloan-style edge flipping
    ///      converts each constraint segment into an explicit edge of the
    ///      triangulation. Constrained edges are locked against future flips
    ///      so subsequent recovery passes preserve earlier work.
    ///   5. <see cref="ExtractFinalTriangles"/> — drops the super-triangle
    ///      and emits a compact triangle index list.
    ///
    /// Predicates use plain double precision; tolerance is <see cref="EPS"/>.
    /// At the scale of CSI slab meshes (feet) and detail-item regions
    /// (metres of bar) this comfortably resolves the vertex spacing without
    /// needing extended-precision arithmetic.
    /// </summary>
    internal sealed class ConstrainedDelaunay
    {
        public const double EPS = 1e-9;

        // ── Inputs ───────────────────────────────────────────────────────

        /// <summary>Input + intersection points; populated by <see cref="AddPoint"/> / <see cref="AddOrSnapPoint"/>.</summary>
        public List<(double X, double Y)> Points { get; } = new List<(double X, double Y)>();

        /// <summary>Constraint segments as (point-index, point-index). Order within a pair is irrelevant.</summary>
        public List<(int A, int B)> Constraints { get; } = new List<(int A, int B)>();

        // ── Outputs ──────────────────────────────────────────────────────

        /// <summary>Triangle indices (3 per row) after a successful <see cref="Triangulate"/> run.</summary>
        public int[][] Triangles { get; private set; }

        /// <summary>Packed (min,max) edge keys that survived as constrained edges.</summary>
        public HashSet<long> ConstrainedEdges { get; } = new HashSet<long>();

        /// <summary>
        /// Number of real (non-super-triangle) points. After <see cref="Triangulate"/>
        /// the three super-triangle vertices live at the tail of <see cref="Points"/>;
        /// this is the count up to but not including them.
        /// </summary>
        public int RealPointCount { get; private set; }

        // ── Point input ──────────────────────────────────────────────────

        public int AddPoint(double x, double y)
        {
            Points.Add((x, y));
            return Points.Count - 1;
        }

        /// <summary>
        /// Returns the index of an existing point within <paramref name="tol"/>,
        /// or appends a new point and returns its index.
        ///
        /// Small point sets use a plain linear scan; past
        /// <see cref="SnapGridThreshold"/> points a lazily-built spatial hash
        /// takes over (cell = 2·tol, 3×3 neighbourhood query), because the
        /// linear scan is O(N) per call and went quadratic on Part-B CDTs
        /// seeded with thousands of CSI boundary segments.
        /// </summary>
        public int AddOrSnapPoint(double x, double y, double tol)
        {
            if (_snapCells == null && Points.Count < SnapGridThreshold)
            {
                double tolSq = tol * tol;
                for (int i = 0; i < Points.Count; i++)
                {
                    double dx = Points[i].X - x;
                    double dy = Points[i].Y - y;
                    if (dx * dx + dy * dy <= tolSq) return i;
                }
                return AddPoint(x, y);
            }

            if (_snapCells == null)
            {
                // Cell = 2·tol: any point within tol of the query lands in the
                // same cell or an immediate neighbour, so the 3×3 probe below
                // is exhaustive. The grid is only valid for one tolerance —
                // callers use a fixed snap tolerance per CDT instance.
                _snapCellSize = Math.Max(tol, 1e-12) * 2.0;
                _snapCells    = new Dictionary<(long, long), List<int>>();
                _snapIndexed  = 0;
            }

            // Lazily index points appended since the last call (AddPoint
            // inserts directly, e.g. element-corner seeds).
            for (; _snapIndexed < Points.Count; _snapIndexed++)
            {
                var p   = Points[_snapIndexed];
                var key = ((long)Math.Floor(p.X / _snapCellSize),
                           (long)Math.Floor(p.Y / _snapCellSize));
                if (!_snapCells.TryGetValue(key, out var list))
                    _snapCells[key] = list = new List<int>(2);
                list.Add(_snapIndexed);
            }

            double tSq = tol * tol;
            long cx = (long)Math.Floor(x / _snapCellSize);
            long cy = (long)Math.Floor(y / _snapCellSize);
            for (long gy = cy - 1; gy <= cy + 1; gy++)
            for (long gx = cx - 1; gx <= cx + 1; gx++)
            {
                if (!_snapCells.TryGetValue((gx, gy), out var list)) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    var p = Points[list[i]];
                    double dx = p.X - x;
                    double dy = p.Y - y;
                    if (dx * dx + dy * dy <= tSq) return list[i];
                }
            }
            return AddPoint(x, y);   // indexed lazily on the next call
        }

        private const int SnapGridThreshold = 64;
        private Dictionary<(long, long), List<int>> _snapCells;
        private double _snapCellSize;
        private int    _snapIndexed;

        public void AddConstraint(int a, int b)
        {
            if (a == b) return;
            Constraints.Add((a, b));
        }

        // ── Entry point ──────────────────────────────────────────────────

        public void Triangulate()
        {
            if (Points.Count < 3)
            {
                Triangles      = new int[0][];
                RealPointCount = Points.Count;
                return;
            }

            PreSplitCrossingConstraints();
            PreSplitConstraintsAtPoints();

            // Freeze the real-point count before BuildBowyerWatson appends
            // the three super-triangle vertices.
            RealPointCount = Points.Count;

            BuildBowyerWatson();
            RecoverConstraints();
            ExtractFinalTriangles();
        }

        // ─────────────────────────────────────────────────────────────────
        //  Pre-processing
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Replaces every pair of strictly-crossing constraint segments with
        /// four sub-segments meeting at a shared intersection point. Uses a
        /// uniform grid bucket over constraint AABBs so each pass costs
        /// O(C · neighbours) instead of O(C²). Each pass processes many
        /// splits at once (modified constraints are skipped within the pass
        /// but revisited in the next), so total cost is O((C + splits) · log)
        /// for inputs of practical scale.
        /// </summary>
        private void PreSplitCrossingConstraints()
        {
            const int MAX_PASSES = 32;
            var modified = new HashSet<int>();

            for (int pass = 0; pass < MAX_PASSES; pass++)
            {
                int initialCount = Constraints.Count;
                var grid = BuildConstraintGrid(initialCount);

                bool changed = false;
                modified.Clear();
                var bucket = new List<int>();

                for (int i = 0; i < initialCount; i++)
                {
                    if (modified.Contains(i)) continue;

                    var (ia, ib) = Constraints[i];
                    var pa = Points[ia];
                    var pb = Points[ib];

                    double aMinX = Math.Min(pa.X, pb.X), aMaxX = Math.Max(pa.X, pb.X);
                    double aMinY = Math.Min(pa.Y, pb.Y), aMaxY = Math.Max(pa.Y, pb.Y);

                    bucket.Clear();
                    grid.QueryAabb(aMinX, aMinY, aMaxX, aMaxY, bucket);

                    for (int bk = 0; bk < bucket.Count; bk++)
                    {
                        int j = bucket[bk];
                        if (j <= i || j >= initialCount) continue;
                        if (modified.Contains(j)) continue;

                        var (ic, id) = Constraints[j];
                        if (ic == ia || ic == ib || id == ia || id == ib) continue;

                        var pc = Points[ic];
                        var pd = Points[id];

                        if (!TrySegmentIntersect(
                                pa.X, pa.Y, pb.X, pb.Y,
                                pc.X, pc.Y, pd.X, pd.Y,
                                out double ix, out double iy,
                                out double t,  out double s))
                            continue;

                        if (t <= EPS || t >= 1.0 - EPS) continue;
                        if (s <= EPS || s >= 1.0 - EPS) continue;

                        int ix2 = AddOrSnapPoint(ix, iy, EPS);
                        Constraints[i] = (ia, ix2);
                        Constraints.Add((ix2, ib));
                        Constraints[j] = (ic, ix2);
                        Constraints.Add((ix2, id));
                        modified.Add(i);
                        modified.Add(j);
                        changed = true;
                        break;   // i is now split; the new sub-segments are
                                 // appended and will be processed next pass.
                    }
                }

                if (!changed) return;
            }
        }

        /// <summary>
        /// Splits any constraint segment whose interior contains another
        /// point at that point. Uses a uniform grid bucket over points so
        /// each pass costs O(C · log) rather than O(C · N).
        /// </summary>
        private void PreSplitConstraintsAtPoints()
        {
            const int MAX_PASSES = 12;
            var modified = new HashSet<int>();

            for (int pass = 0; pass < MAX_PASSES; pass++)
            {
                int initialCount = Constraints.Count;
                var grid = BuildPointGrid();

                bool changed = false;
                modified.Clear();
                var bucket = new List<int>();

                for (int i = 0; i < initialCount; i++)
                {
                    if (modified.Contains(i)) continue;

                    var (ia, ib) = Constraints[i];
                    var pa = Points[ia];
                    var pb = Points[ib];
                    double dx = pb.X - pa.X;
                    double dy = pb.Y - pa.Y;
                    double L2 = dx * dx + dy * dy;
                    if (L2 < EPS * EPS) continue;

                    double aMinX = Math.Min(pa.X, pb.X) - EPS;
                    double aMaxX = Math.Max(pa.X, pb.X) + EPS;
                    double aMinY = Math.Min(pa.Y, pb.Y) - EPS;
                    double aMaxY = Math.Max(pa.Y, pb.Y) + EPS;

                    bucket.Clear();
                    grid.QueryAabb(aMinX, aMinY, aMaxX, aMaxY, bucket);

                    for (int bk = 0; bk < bucket.Count; bk++)
                    {
                        int k = bucket[bk];
                        if (k == ia || k == ib) continue;

                        var pk = Points[k];
                        double t = ((pk.X - pa.X) * dx + (pk.Y - pa.Y) * dy) / L2;
                        if (t <= EPS || t >= 1.0 - EPS) continue;

                        double projX = pa.X + t * dx;
                        double projY = pa.Y + t * dy;
                        double ex = pk.X - projX;
                        double ey = pk.Y - projY;
                        if (ex * ex + ey * ey > EPS * EPS) continue;

                        Constraints[i] = (ia, k);
                        Constraints.Add((k, ib));
                        modified.Add(i);
                        changed = true;
                        break;
                    }
                }

                if (!changed) return;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  Uniform spatial grid (used by pre-split passes)
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Simple uniform grid bucket. Items are indexed by integer IDs; each
        /// cell holds the list of IDs whose AABB touches it. Query returns
        /// every ID whose AABB overlaps the queried AABB — fast O(1) lookups
        /// once built. Built fresh by the pre-split passes; not reused
        /// elsewhere.
        /// </summary>
        private sealed class SpatialGrid
        {
            private readonly int _nx, _ny;
            private readonly double _minX, _minY, _cellW, _cellH;
            private readonly List<int>[,] _cells;

            public SpatialGrid(double minX, double minY, double maxX, double maxY, int side)
            {
                _nx = Math.Max(1, side);
                _ny = Math.Max(1, side);
                _minX = minX; _minY = minY;
                double w = maxX - minX, h = maxY - minY;
                _cellW = (w > 0 ? w : 1.0) / _nx + 1e-12;
                _cellH = (h > 0 ? h : 1.0) / _ny + 1e-12;
                _cells = new List<int>[_nx, _ny];
            }

            public void Insert(int id, double x, double y)
            {
                int cx, cy;
                ToCell(x, y, out cx, out cy);
                var list = _cells[cx, cy];
                if (list == null) { list = new List<int>(4); _cells[cx, cy] = list; }
                list.Add(id);
            }

            public void InsertAabb(int id, double minX, double minY, double maxX, double maxY)
            {
                int cx0, cy0, cx1, cy1;
                ToCell(minX, minY, out cx0, out cy0);
                ToCell(maxX, maxY, out cx1, out cy1);
                for (int cy = cy0; cy <= cy1; cy++)
                    for (int cx = cx0; cx <= cx1; cx++)
                    {
                        var list = _cells[cx, cy];
                        if (list == null) { list = new List<int>(4); _cells[cx, cy] = list; }
                        list.Add(id);
                    }
            }

            public void QueryAabb(double minX, double minY, double maxX, double maxY,
                                  List<int> output)
            {
                int cx0, cy0, cx1, cy1;
                ToCell(minX, minY, out cx0, out cy0);
                ToCell(maxX, maxY, out cx1, out cy1);
                for (int cy = cy0; cy <= cy1; cy++)
                    for (int cx = cx0; cx <= cx1; cx++)
                    {
                        var list = _cells[cx, cy];
                        if (list == null) continue;
                        for (int i = 0; i < list.Count; i++)
                            output.Add(list[i]);
                    }
            }

            private void ToCell(double x, double y, out int cx, out int cy)
            {
                cx = (int)Math.Floor((x - _minX) / _cellW);
                cy = (int)Math.Floor((y - _minY) / _cellH);
                if (cx < 0) cx = 0; else if (cx >= _nx) cx = _nx - 1;
                if (cy < 0) cy = 0; else if (cy >= _ny) cy = _ny - 1;
            }
        }

        private SpatialGrid BuildPointGrid()
        {
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            for (int i = 0; i < Points.Count; i++)
            {
                var p = Points[i];
                if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
            }
            int side = Math.Max(4, (int)Math.Sqrt(Points.Count));
            var grid = new SpatialGrid(minX, minY, maxX, maxY, side);
            for (int i = 0; i < Points.Count; i++)
                grid.Insert(i, Points[i].X, Points[i].Y);
            return grid;
        }

        private SpatialGrid BuildConstraintGrid(int count)
        {
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            for (int i = 0; i < count; i++)
            {
                var (a, b) = Constraints[i];
                var pa = Points[a]; var pb = Points[b];
                if (pa.X < minX) minX = pa.X; if (pa.X > maxX) maxX = pa.X;
                if (pa.Y < minY) minY = pa.Y; if (pa.Y > maxY) maxY = pa.Y;
                if (pb.X < minX) minX = pb.X; if (pb.X > maxX) maxX = pb.X;
                if (pb.Y < minY) minY = pb.Y; if (pb.Y > maxY) maxY = pb.Y;
            }
            int side = Math.Max(4, (int)Math.Sqrt(count));
            var grid = new SpatialGrid(minX, minY, maxX, maxY, side);
            for (int i = 0; i < count; i++)
            {
                var (a, b) = Constraints[i];
                var pa = Points[a]; var pb = Points[b];
                double aMinX = Math.Min(pa.X, pb.X), aMaxX = Math.Max(pa.X, pb.X);
                double aMinY = Math.Min(pa.Y, pb.Y), aMaxY = Math.Max(pa.Y, pb.Y);
                grid.InsertAabb(i, aMinX, aMinY, aMaxX, aMaxY);
            }
            return grid;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Bowyer-Watson incremental Delaunay
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Triangle record carrying point indices and pointers to the three
        /// neighbours opposite each vertex. <c>Nab</c> is the neighbour
        /// across edge (A,B) — i.e. the triangle on the far side of C.
        /// </summary>
        private sealed class T
        {
            public int A, B, C;
            public T   Nab, Nbc, Nca;
            public bool Dead;
            public int  Stamp;

            public T(int a, int b, int c) { A = a; B = b; C = c; }
        }

        private List<T> _tris;
        private int     _superA, _superB, _superC;
        private int     _stamp;

        /// <summary>
        /// Cached "last touched" triangle for the walking point-location used
        /// by <see cref="WalkToContaining"/>. Insertions and flips update this
        /// to a live triangle near the most recent activity so each subsequent
        /// query walks only a handful of triangles.
        /// </summary>
        private T _lastTriangle;

        /// <summary>
        /// Incident-triangles map: vertex index → live triangles containing
        /// that vertex. Maintained incrementally by every triangle create /
        /// destroy / flip path so Sloan-style constraint recovery can scan
        /// only the local neighbourhood instead of the entire mesh.
        /// </summary>
        private readonly Dictionary<int, List<T>> _incident
            = new Dictionary<int, List<T>>();

        private void IncidentAdd(T t)
        {
            IncidentAddOne(t.A, t);
            IncidentAddOne(t.B, t);
            IncidentAddOne(t.C, t);
        }

        private void IncidentAddOne(int v, T t)
        {
            if (!_incident.TryGetValue(v, out var list))
            {
                list = new List<T>(8);
                _incident[v] = list;
            }
            list.Add(t);
        }

        private void IncidentRemove(T t)
        {
            IncidentRemoveOne(t.A, t);
            IncidentRemoveOne(t.B, t);
            IncidentRemoveOne(t.C, t);
        }

        private void IncidentRemoveOne(int v, T t)
        {
            if (_incident.TryGetValue(v, out var list))
                list.Remove(t);   // O(degree); degree is O(1) for Delaunay
        }

        private void BuildBowyerWatson()
        {
            // ── Super-triangle that comfortably encloses the AABB ────────
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            for (int i = 0; i < Points.Count; i++)
            {
                if (Points[i].X < minX) minX = Points[i].X;
                if (Points[i].X > maxX) maxX = Points[i].X;
                if (Points[i].Y < minY) minY = Points[i].Y;
                if (Points[i].Y > maxY) maxY = Points[i].Y;
            }

            double dx = maxX - minX;
            double dy = maxY - minY;
            double D  = Math.Max(dx, dy);
            if (D < EPS) D = 1.0;
            double cx = 0.5 * (minX + maxX);
            double cy = 0.5 * (minY + maxY);
            double R  = 20.0 * D;

            _superA = AddPoint(cx - R,        cy - R);
            _superB = AddPoint(cx + R,        cy - R);
            _superC = AddPoint(cx,            cy + R);

            _tris = new List<T>();
            _stamp = 0;
            _incident.Clear();
            var super = new T(_superA, _superB, _superC);
            _tris.Add(super);
            IncidentAdd(super);
            _lastTriangle = super;

            // Insert every non-super point in input order
            int nInput = Points.Count - 3;
            for (int i = 0; i < nInput; i++)
                InsertPoint(i);
        }

        private void InsertPoint(int pIdx)
        {
            // ── Find one bad triangle via walk from the last touched tri ──
            // Any triangle that contains pIdx in its interior has its
            // circumcircle containing pIdx (chord-of-circle property), so
            // the located triangle is guaranteed to be "bad" — a valid
            // seed for the BFS below.
            T seed = WalkToContaining(pIdx);
            if (seed == null) return;   // numerical edge-case; skip

            // ── BFS over bad triangles ───────────────────────────────────
            _stamp++;
            var bad   = new List<T>();
            var stack = new Stack<T>();
            seed.Stamp = _stamp;
            stack.Push(seed);
            bad.Add(seed);

            while (stack.Count > 0)
            {
                T cur = stack.Pop();
                TryEnqueueIfBad(cur.Nab, pIdx, bad, stack);
                TryEnqueueIfBad(cur.Nbc, pIdx, bad, stack);
                TryEnqueueIfBad(cur.Nca, pIdx, bad, stack);
            }

            // ── Collect cavity boundary (edges with non-bad outside) ─────
            // Each entry: (vA, vB, outside-neighbour)
            var cavity = new List<(int Va, int Vb, T Outside)>();
            for (int i = 0; i < bad.Count; i++)
            {
                T b = bad[i];
                if (b.Nab == null || b.Nab.Stamp != _stamp) cavity.Add((b.A, b.B, b.Nab));
                if (b.Nbc == null || b.Nbc.Stamp != _stamp) cavity.Add((b.B, b.C, b.Nbc));
                if (b.Nca == null || b.Nca.Stamp != _stamp) cavity.Add((b.C, b.A, b.Nca));
            }

            // ── Kill bad triangles ───────────────────────────────────────
            for (int i = 0; i < bad.Count; i++)
            {
                bad[i].Dead = true;
                IncidentRemove(bad[i]);
            }

            // ── Stitch cavity with pIdx as apex ──────────────────────────
            // The cavity boundary is a closed CCW polygon around pIdx.
            // For each boundary edge (va → vb), one new triangle (va, vb, pIdx)
            // is created. Each cavity vertex appears as the START of exactly
            // one boundary edge and the END of exactly one other, so a pair
            // of (vertex → triangle) lookups uniquely identifies the two
            // cavity-internal neighbours of every new triangle.
            var newTris    = new List<T>(cavity.Count);
            var startToTri = new Dictionary<int, T>(cavity.Count);
            var endToTri   = new Dictionary<int, T>(cavity.Count);

            for (int i = 0; i < cavity.Count; i++)
            {
                var (va, vb, outside) = cavity[i];
                var nt = new T(va, vb, pIdx);
                _tris.Add(nt);
                newTris.Add(nt);
                IncidentAdd(nt);

                // Outside neighbour shares edge (va, vb); rewire both sides.
                nt.Nab = outside;
                if (outside != null) ReplaceNeighbour(outside, va, vb, nt);

                startToTri[va] = nt;
                endToTri  [vb] = nt;
            }

            // Wire the two cavity-internal edges of each new triangle.
            //   nt's edge (B, C) = (vb, pIdx) is shared with the triangle
            //   whose boundary edge STARTS at vb.
            //   nt's edge (C, A) = (pIdx, va) is shared with the triangle
            //   whose boundary edge ENDS at va.
            for (int i = 0; i < newTris.Count; i++)
            {
                T nt = newTris[i];
                startToTri.TryGetValue(nt.B, out T tBc);
                endToTri  .TryGetValue(nt.A, out T tCa);
                nt.Nbc = (tBc != nt) ? tBc : null;
                nt.Nca = (tCa != nt) ? tCa : null;
            }

            // Seed future walks from a known-live triangle in the just-filled
            // cavity. Locality keeps walks near O(1) when input points arrive
            // in spatially-coherent order.
            if (newTris.Count > 0) _lastTriangle = newTris[0];
        }

        private void TryEnqueueIfBad(T t, int pIdx, List<T> bad, Stack<T> stack)
        {
            if (t == null || t.Stamp == _stamp || t.Dead) return;
            if (!InCircumcircle(t, pIdx)) return;
            t.Stamp = _stamp;
            bad.Add(t);
            stack.Push(t);
        }

        /// <summary>
        /// Walks from <see cref="_lastTriangle"/> across the CCW Bowyer-Watson
        /// mesh toward the query point, stepping into the neighbour across
        /// any edge that the point lies on the "outside" of. Each step removes
        /// at least one of the three edge-rejection conditions, so the walk
        /// terminates in O(sqrt(N)) steps on average for a Delaunay mesh.
        /// All triangles encountered during Bowyer-Watson insertion are CCW
        /// (constraint recovery — which can leave CW triangles after flips —
        /// runs strictly after this phase).
        /// </summary>
        private T WalkToContaining(int pIdx)
        {
            var p = Points[pIdx];
            T cur = _lastTriangle;
            if (cur == null || cur.Dead)
            {
                // Defensive: pick the newest live triangle if our cache rotted.
                for (int i = _tris.Count - 1; i >= 0; i--)
                {
                    if (!_tris[i].Dead) { cur = _tris[i]; break; }
                }
                if (cur == null) return null;
            }

            const int MAX_STEPS = 1 << 22;
            for (int step = 0; step < MAX_STEPS; step++)
            {
                var pa = Points[cur.A]; var pb = Points[cur.B]; var pc = Points[cur.C];

                double oab = Orient2D(pa.X, pa.Y, pb.X, pb.Y, p.X, p.Y);
                if (oab < -EPS)
                {
                    if (cur.Nab == null || cur.Nab.Dead) return null;
                    cur = cur.Nab;
                    continue;
                }
                double obc = Orient2D(pb.X, pb.Y, pc.X, pc.Y, p.X, p.Y);
                if (obc < -EPS)
                {
                    if (cur.Nbc == null || cur.Nbc.Dead) return null;
                    cur = cur.Nbc;
                    continue;
                }
                double oca = Orient2D(pc.X, pc.Y, pa.X, pa.Y, p.X, p.Y);
                if (oca < -EPS)
                {
                    if (cur.Nca == null || cur.Nca.Dead) return null;
                    cur = cur.Nca;
                    continue;
                }
                return cur;   // p is inside or on the boundary of cur
            }
            return null;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Sloan-style constraint recovery
        // ─────────────────────────────────────────────────────────────────

        private void RecoverConstraints()
        {
            // Two passes: the first recovers most constraints; the second retries
            // any that were silently abandoned because an already-constrained edge
            // blocked the flip path from vertex a. In complex meshes with many
            // overlapping constraints (multiple bars + kink lines) the order of
            // recovery can trap the algorithm — a second pass succeeds once the
            // blocking constraint is itself recovered.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < Constraints.Count; i++)
                {
                    var (a, b) = Constraints[i];
                    if (a == b) continue;
                    if (ConstrainedEdges.Contains(PackEdge(a, b))) continue;
                    if (TryMarkExistingEdge(a, b)) continue;
                    RecoverOneConstraint(a, b);
                }
            }
        }

        private bool TryMarkExistingEdge(int a, int b)
        {
            T t = FindTriangleWithEdge(a, b);
            if (t == null) return false;
            ConstrainedEdges.Add(PackEdge(a, b));
            return true;
        }

        /// <summary>
        /// Recovers constraint (a, b) with Sloan-style ordered edge flipping:
        /// walk the triangle strip from <paramref name="a"/> to
        /// <paramref name="b"/> collecting every triangulation edge that
        /// strictly crosses the segment, then flip those edges (deferring the
        /// momentarily non-convex ones) until the segment is edge-clear and
        /// (a, b) appears as an edge.
        ///
        /// Why ordered, not the incident-fan flipper: the edges crossing (a, b)
        /// lie in the channel BETWEEN a and b, and most are not incident to a.
        /// Flipping only a's fan stalls as soon as every fan triangle is
        /// non-convex or locked, silently abandoning the constraint — which left
        /// triangles spanning a region boundary (the smeared-gradient artefact
        /// in dense meshes with overlapping detail items). Sloan's result
        /// guarantees at least one crossing edge is always convex-flippable, so
        /// rotating past the non-convex ones always makes progress.
        ///
        /// Falls back to the incident-fan flipper when the strip can't be built
        /// (a constrained edge crosses (a, b), or a vertex lies on it — both
        /// degenerate cases that pre-splitting normally removes).
        /// </summary>
        private void RecoverOneConstraint(int a, int b)
        {
            var pa = Points[a]; var pb = Points[b];

            var crossing = new List<(int U, int V)>();
            if (!CollectCrossingEdges(a, b, pa, pb, crossing))
            {
                FanRecover(a, b, pa, pb);
                return;
            }
            // Flip the crossing edges with deferral. A hard iteration cap is the
            // termination guarantee: a flip whose new diagonal STILL crosses
            // (a, b) is re-queued (Sloan-style), which in floating point can
            // ping-pong — the constraint passing through a quad whose BOTH
            // diagonals cross it flips back and forth. The cap bounds that
            // pathology so a degenerate edge is abandoned (left for a later pass
            // or the fan fallback) instead of hanging.
            int idx = 0;
            int guard = 0;
            int maxGuard = 64 * (crossing.Count + 1) + 1024;
            while (crossing.Count > 0)
            {
                if (guard++ > maxGuard) break;   // bounded — give up cleanly

                var (u, v) = crossing[idx];

                // A constrained edge crossing (a, b) cannot be flipped — recovery
                // is impossible (pre-split should have removed this crossing).
                if (ConstrainedEdges.Contains(PackEdge(u, v))) break;

                T t1 = FindTriangleWithEdge(u, v);
                T t2 = (t1 != null) ? NeighbourAcrossEdge(t1, u, v) : null;
                int c1 = (t1 != null) ? OppositeVertex(t1, u, v) : -1;
                int c2 = (t2 != null) ? OppositeVertex(t2, u, v) : -1;
                if (t1 == null || t2 == null || c1 < 0 || c2 < 0)
                {
                    crossing.RemoveAt(idx);                 // stale edge — drop it
                    if (crossing.Count > 0) idx %= crossing.Count;
                    continue;
                }

                if (!IsQuadrilateralConvex(u, v, c1, c2))
                {
                    idx = (idx + 1) % crossing.Count;       // defer — try another edge
                    continue;
                }

                FlipEdge(t1, t2, u, v, c1, c2);   // new diagonal (c1, c2)
                crossing.RemoveAt(idx);

                // The new diagonal may itself still cross (a, b); re-queue it.
                if (c1 != a && c1 != b && c2 != a && c2 != b && SegCross(c1, c2, pa, pb))
                    crossing.Add((c1, c2));

                if (crossing.Count > 0) idx %= crossing.Count; else break;
            }

            TryMarkExistingEdge(a, b);
        }

        /// <summary>
        /// Walks the triangle strip the segment (a, b) passes through, appending
        /// each crossed edge to <paramref name="outList"/> as a vertex pair
        /// (stable across flips, unlike a triangle reference). Returns false when
        /// the strip can't be built: no first crossing found, a constrained edge
        /// crosses the segment, a vertex lies on it, or a numerical dead-end.
        /// </summary>
        private bool CollectCrossingEdges(
            int a, int b, (double X, double Y) pa, (double X, double Y) pb,
            List<(int U, int V)> outList)
        {
            if (!_incident.TryGetValue(a, out var fan) || fan.Count == 0)
                return false;

            // First crossing: the triangle in a's fan whose edge opposite a
            // strictly crosses (a, b). Label its endpoints so that `u` is on the
            // LEFT of directed line a→b and `v` on the RIGHT; the walk keeps that
            // invariant so it can route purely by the apex's side.
            int u = -1, v = -1;
            T cur = null;
            for (int i = 0; i < fan.Count; i++)
            {
                T t = fan[i];
                if (t.Dead) continue;
                int eu, ev;
                if      (t.A == a) { eu = t.B; ev = t.C; }
                else if (t.B == a) { eu = t.C; ev = t.A; }
                else if (t.C == a) { eu = t.A; ev = t.B; }
                else continue;
                if (eu == b || ev == b) return true;   // (a, b) already an edge
                if (SegCross(eu, ev, pa, pb))
                {
                    // Orient2D(a,b,p) > 0 ⇒ p left of a→b.
                    if (Side(pa, pb, eu) > 0) { u = eu; v = ev; } else { u = ev; v = eu; }
                    cur = t;
                    break;
                }
            }
            if (cur == null) return false;

            int safety = 0, maxSafety = _tris.Count + 16;
            while (true)
            {
                if (safety++ > maxSafety) return false;
                if (ConstrainedEdges.Contains(PackEdge(u, v))) return false;
                outList.Add((u, v));

                T next = NeighbourAcrossEdge(cur, u, v);
                if (next == null) return false;
                int w = OppositeVertex(next, u, v);
                if (w < 0) return false;
                if (w == b) return true;   // reached b — strip complete

                // Route by the apex's side of directed line a→b (robust — does
                // not depend on a full segment-segment straddle that can fail
                // mid-strip). u stays left, v stays right.
                double sw = Side(pa, pb, w);
                if      (sw > EPS)  u = w;   // apex left  → exit edge (v, w); w is the new left
                else if (sw < -EPS) v = w;   // apex right → exit edge (u, w); w is the new right
                else                return false;   // apex on segment → vertex-on-seg degeneracy
                cur = next;
            }
        }

        private bool SegCross(int p, int q, (double X, double Y) pa, (double X, double Y) pb)
        {
            var pp = Points[p]; var pq = Points[q];
            return SegmentsStrictlyCross(
                pa.X, pa.Y, pb.X, pb.Y, pp.X, pp.Y, pq.X, pq.Y);
        }

        /// <summary>Signed side of vertex <paramref name="p"/> relative to the
        /// directed line pa→pb (&gt;0 left, &lt;0 right, ≈0 on the line).</summary>
        private double Side((double X, double Y) pa, (double X, double Y) pb, int p)
        {
            var pp = Points[p];
            return Orient2D(pa.X, pa.Y, pb.X, pb.Y, pp.X, pp.Y);
        }

        /// <summary>
        /// Fallback recovery using the incremental incident-fan flipper (tries
        /// from a, then from b). Used only when <see cref="CollectCrossingEdges"/>
        /// can't build the strip; may still leave (a, b) unrecovered, which a
        /// later <see cref="RecoverConstraints"/> pass can pick up.
        /// </summary>
        private void FanRecover(
            int a, int b, (double X, double Y) pa, (double X, double Y) pb)
        {
            const int MAX_STEPS = 32768;
            for (int step = 0; step < MAX_STEPS; step++)
            {
                if (TryMarkExistingEdge(a, b)) return;
                if (!TryFlipIncidentCrossing(a, b, pa, pb))
                    if (!TryFlipIncidentCrossing(b, a, pb, pa)) return;
            }
        }

        private bool TryFlipIncidentCrossing(
            int a, int b,
            (double X, double Y) pa, (double X, double Y) pb)
        {
            if (!_incident.TryGetValue(a, out var listA) || listA.Count == 0)
                return false;

            // Iterate over a snapshot because FlipEdge mutates the list. The
            // mutation only adds/removes a couple of entries near the active
            // triangle, so a stale snapshot is still mostly accurate; we
            // simply re-enter on the next outer-loop pass if a snapshot entry
            // turned dead between iterations.
            int n = listA.Count;
            for (int i = 0; i < n; i++)
            {
                T t = listA[i];
                if (t.Dead) continue;

                // Edge of t opposite vertex a.
                int u, v, oppositeSide;
                if      (t.A == a) { u = t.B; v = t.C; oppositeSide = 1; }   // Nbc
                else if (t.B == a) { u = t.C; v = t.A; oppositeSide = 2; }   // Nca
                else if (t.C == a) { u = t.A; v = t.B; oppositeSide = 0; }   // Nab
                else continue;   // stale entry — shouldn't happen

                if (u == b || v == b) continue;
                if (ConstrainedEdges.Contains(PackEdge(u, v))) continue;

                var pu = Points[u]; var pv = Points[v];
                if (!SegmentsStrictlyCross(
                        pa.X, pa.Y, pb.X, pb.Y,
                        pu.X, pu.Y, pv.X, pv.Y)) continue;

                T other;
                switch (oppositeSide)
                {
                    case 0:  other = t.Nab; break;
                    case 1:  other = t.Nbc; break;
                    default: other = t.Nca; break;
                }
                if (other == null || other.Dead) continue;

                int c2 = OppositeVertex(other, u, v);
                if (c2 < 0) continue;
                if (c2 == a) continue;   // degenerate — segment endpoint reached

                if (!IsQuadrilateralConvex(u, v, a, c2)) continue;

                FlipEdge(t, other, u, v, a, c2);
                return true;
            }
            return false;
        }

        private static int OppositeVertex(T t, int u, int v)
        {
            if ((t.A == u && t.B == v) || (t.A == v && t.B == u)) return t.C;
            if ((t.B == u && t.C == v) || (t.B == v && t.C == u)) return t.A;
            if ((t.C == u && t.A == v) || (t.C == v && t.A == u)) return t.B;
            return -1;
        }

        private static void GetEdgeVertices(T t, int side, out int u, out int v, out int opposite)
        {
            switch (side)
            {
                case 0: u = t.A; v = t.B; opposite = t.C; return;
                case 1: u = t.B; v = t.C; opposite = t.A; return;
                case 2: u = t.C; v = t.A; opposite = t.B; return;
                default: u = v = opposite = -1; return;
            }
        }

        /// <summary>
        /// The quadrilateral formed by triangles (u, v, c1) and (u, v, c2)
        /// sharing edge (u, v) is convex ⇔ the two diagonals strictly cross,
        /// i.e. segment (u, v) crosses segment (c1, c2). When this holds,
        /// flipping is well-defined; otherwise the would-be flipped diagonal
        /// (c1, c2) lies outside the quad and creates degenerate triangles.
        /// </summary>
        private bool IsQuadrilateralConvex(int u, int v, int c1, int c2)
        {
            var pu  = Points[u];  var pv  = Points[v];
            var pc1 = Points[c1]; var pc2 = Points[c2];
            return SegmentsStrictlyCross(
                pu.X,  pu.Y,  pv.X,  pv.Y,
                pc1.X, pc1.Y, pc2.X, pc2.Y);
        }

        /// <summary>
        /// Replaces the diagonal (u,v) — shared by triangles t1 and t2 with
        /// apexes c1, c2 respectively — by the opposite diagonal (c1,c2).
        /// Updates neighbour pointers on both new triangles and on the
        /// outer ring of four neighbours, and keeps the incident-triangle
        /// map in sync with the new vertex sets.
        /// </summary>
        private void FlipEdge(T t1, T t2, int u, int v, int c1, int c2)
        {
            // Outer neighbours around the quadrilateral.
            T n1uc1 = NeighbourAcrossEdge(t1, u, c1);   // shares (u, c1) with t1
            T n1c1v = NeighbourAcrossEdge(t1, c1, v);   // shares (c1, v) with t1
            T n2vc2 = NeighbourAcrossEdge(t2, v, c2);   // shares (v, c2) with t2
            T n2c2u = NeighbourAcrossEdge(t2, c2, u);   // shares (c2, u) with t2

            // Detach t1/t2 from the incident map before changing their vertex
            // sets — otherwise the entries under the OLD vertices would leak.
            IncidentRemove(t1);
            IncidentRemove(t2);

            // Rebuild t1 = (c1, c2, u),  t2 = (c1, v, c2)  — CCW preserved
            // by virtue of the convex quad (c1, u, c2, v).
            t1.A = c1; t1.B = c2; t1.C = u;
            t2.A = c1; t2.B = v;  t2.C = c2;

            IncidentAdd(t1);
            IncidentAdd(t2);
            _lastTriangle = t1;

            // New neighbour pointers (each Nxy is the neighbour across edge (x, y)).
            t1.Nab = t2;       // edge (c1, c2)
            t1.Nbc = n2c2u;    // edge (c2, u)
            t1.Nca = n1uc1;    // edge (u, c1)

            t2.Nab = n1c1v;    // edge (c1, v)
            t2.Nbc = n2vc2;    // edge (v, c2)
            t2.Nca = t1;       // edge (c2, c1)

            if (n1uc1 != null) ReplaceNeighbour(n1uc1, u, c1, t1);
            if (n1c1v != null) ReplaceNeighbour(n1c1v, c1, v, t2);
            if (n2vc2 != null) ReplaceNeighbour(n2vc2, v, c2, t2);
            if (n2c2u != null) ReplaceNeighbour(n2c2u, c2, u, t1);
        }

        private static T NeighbourAcrossEdge(T t, int x, int y)
        {
            if ((t.A == x && t.B == y) || (t.A == y && t.B == x)) return t.Nab;
            if ((t.B == x && t.C == y) || (t.B == y && t.C == x)) return t.Nbc;
            if ((t.C == x && t.A == y) || (t.C == y && t.A == x)) return t.Nca;
            return null;
        }

        private static void ReplaceNeighbour(T t, int x, int y, T newN)
        {
            if ((t.A == x && t.B == y) || (t.A == y && t.B == x)) { t.Nab = newN; return; }
            if ((t.B == x && t.C == y) || (t.B == y && t.C == x)) { t.Nbc = newN; return; }
            if ((t.C == x && t.A == y) || (t.C == y && t.A == x)) { t.Nca = newN; return; }
        }

        private T FindTriangleWithEdge(int a, int b)
        {
            // Use the incident map — degree(a) is O(1) for a Delaunay mesh,
            // so this is O(1) instead of O(T). Critical when constraint
            // recovery checks "does this edge already exist?" for every one
            // of ~3·N CSI element edges.
            if (!_incident.TryGetValue(a, out var list)) return null;
            for (int i = 0; i < list.Count; i++)
            {
                T t = list[i];
                if (t.Dead) continue;
                if ((t.A == a && t.B == b) || (t.A == b && t.B == a)) return t;
                if ((t.B == a && t.C == b) || (t.B == b && t.C == a)) return t;
                if ((t.C == a && t.A == b) || (t.C == b && t.A == a)) return t;
            }
            return null;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Final extraction (drop super-triangle tris)
        // ─────────────────────────────────────────────────────────────────

        private void ExtractFinalTriangles()
        {
            var list = new List<int[]>(_tris.Count);
            for (int i = 0; i < _tris.Count; i++)
            {
                T t = _tris[i];
                if (t.Dead) continue;
                if (t.A == _superA || t.A == _superB || t.A == _superC) continue;
                if (t.B == _superA || t.B == _superB || t.B == _superC) continue;
                if (t.C == _superA || t.C == _superB || t.C == _superC) continue;

                // Normalise to CCW so downstream renderers see consistent
                // winding (Sloan flips can leave triangles in CW order).
                var pa = Points[t.A]; var pb = Points[t.B]; var pc = Points[t.C];
                double o = Orient2D(pa.X, pa.Y, pb.X, pb.Y, pc.X, pc.Y);
                list.Add(o >= 0 ? new[] { t.A, t.B, t.C }
                                : new[] { t.A, t.C, t.B });
            }
            Triangles = list.ToArray();
        }

        // ─────────────────────────────────────────────────────────────────
        //  Geometry predicates
        // ─────────────────────────────────────────────────────────────────

        public static long PackEdge(int a, int b)
        {
            int lo = a < b ? a : b;
            int hi = a < b ? b : a;
            return ((long)lo << 32) | (uint)hi;
        }

        private bool InCircumcircle(T t, int pIdx)
        {
            var pa = Points[t.A];
            var pb = Points[t.B];
            var pc = Points[t.C];
            var pd = Points[pIdx];

            // Force CCW ordering so the determinant sign is meaningful.
            double o = Orient2D(pa.X, pa.Y, pb.X, pb.Y, pc.X, pc.Y);
            if (Math.Abs(o) < EPS) return false;   // degenerate triangle

            double ax = pa.X - pd.X, ay = pa.Y - pd.Y;
            double bx = pb.X - pd.X, by = pb.Y - pd.Y;
            double cx = pc.X - pd.X, cy = pc.Y - pd.Y;

            double aSq = ax * ax + ay * ay;
            double bSq = bx * bx + by * by;
            double cSq = cx * cx + cy * cy;

            double det = ax * (by * cSq - bSq * cy)
                       - ay * (bx * cSq - bSq * cx)
                       + aSq * (bx * cy - by * cx);

            return (o > 0) ? det > EPS : det < -EPS;
        }

        private static double Orient2D(
            double ax, double ay, double bx, double by, double cx, double cy)
            => (bx - ax) * (cy - ay) - (cx - ax) * (by - ay);

        private static bool SegmentsStrictlyCross(
            double ax, double ay, double bx, double by,
            double cx, double cy, double dx, double dy)
        {
            double o1 = Orient2D(ax, ay, bx, by, cx, cy);
            double o2 = Orient2D(ax, ay, bx, by, dx, dy);
            double o3 = Orient2D(cx, cy, dx, dy, ax, ay);
            double o4 = Orient2D(cx, cy, dx, dy, bx, by);
            return (o1 > EPS && o2 < -EPS || o1 < -EPS && o2 > EPS)
                && (o3 > EPS && o4 < -EPS || o3 < -EPS && o4 > EPS);
        }

        private static bool TrySegmentIntersect(
            double ax, double ay, double bx, double by,
            double cx, double cy, double dx, double dy,
            out double ix, out double iy, out double t, out double s)
        {
            ix = iy = 0; t = s = 0;
            double abx = bx - ax, aby = by - ay;
            double cdx = dx - cx, cdy = dy - cy;
            double den = abx * cdy - aby * cdx;
            if (Math.Abs(den) < 1e-15) return false;
            double acx = cx - ax, acy = cy - ay;
            t = (acx * cdy - acy * cdx) / den;
            s = (acx * aby - acy * abx) / den;
            if (t < -EPS || t > 1.0 + EPS) return false;
            if (s < -EPS || s > 1.0 + EPS) return false;
            ix = ax + t * abx;
            iy = ay + t * aby;
            return true;
        }
    }

}
