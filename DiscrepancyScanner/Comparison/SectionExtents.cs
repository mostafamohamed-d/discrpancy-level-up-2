using System;
using System.Collections.Generic;

namespace DiscrepancyScanner
{
    /// <summary>
    /// How wide a member's solid is along two directions across it: the section measured off
    /// the geometry, for a family that keeps its size under parameter names nobody listed.
    ///
    /// Plain numbers in and out, so the measuring can be tested without Revit. The caller
    /// hands over the points of the solid and the two directions across the member - for a
    /// beam, flat across it and straight up; for a column, its own two plan axes - and gets
    /// back how far the points spread along each.
    /// </summary>
    public static class SectionExtents
    {
        /// <summary>
        /// The spread of the points along each direction, in the points' own units. The
        /// directions need not be unit length; they are normalised here. Zero for a direction
        /// with no length or for no points at all.
        /// </summary>
        public static void Measure(IEnumerable<double[]> points, double[] first, double[] second,
                                   out double alongFirst, out double alongSecond)
        {
            alongFirst = 0;
            alongSecond = 0;

            double[] a = Unit(first);
            double[] b = Unit(second);
            if (a == null || b == null || points == null)
            {
                return;
            }

            double minA = double.MaxValue, maxA = double.MinValue;
            double minB = double.MaxValue, maxB = double.MinValue;
            bool any = false;

            foreach (double[] p in points)
            {
                if (p == null || p.Length < 3)
                {
                    continue;
                }

                double onA = p[0] * a[0] + p[1] * a[1] + p[2] * a[2];
                double onB = p[0] * b[0] + p[1] * b[1] + p[2] * b[2];
                minA = Math.Min(minA, onA);
                maxA = Math.Max(maxA, onA);
                minB = Math.Min(minB, onB);
                maxB = Math.Max(maxB, onB);
                any = true;
            }

            if (any)
            {
                alongFirst = maxA - minA;
                alongSecond = maxB - minB;
            }
        }

        /// <summary>
        /// The two directions across a beam running along <paramref name="along"/>: flat
        /// across it, then up through it. Null for a beam standing straight up, which has no
        /// flat direction across it to measure a width on.
        /// </summary>
        public static bool BeamAxes(double[] along, out double[] across, out double[] up)
        {
            across = null;
            up = null;

            double[] a = Unit(along);
            if (a == null)
            {
                return false;
            }

            // World up crossed with the beam is the flat direction across it.
            across = Unit(new[] { -a[1], a[0], 0d });
            if (across == null)
            {
                return false;
            }

            // The beam crossed with that is up through the section, tilted with a sloped beam.
            up = new[]
            {
                a[1] * across[2] - a[2] * across[1],
                a[2] * across[0] - a[0] * across[2],
                a[0] * across[1] - a[1] * across[0]
            };
            return true;
        }

        /// <summary>
        /// A curved beam's width and depth: how far its points spread out from the centre of
        /// its arc, in plan, and how far they spread up. Measured across a chord, a curved
        /// beam's width would take in the whole bow of the curve.
        /// </summary>
        public static void MeasureAroundCentre(IEnumerable<double[]> points, double[] centre,
                                               out double width, out double depth)
        {
            width = 0;
            depth = 0;
            if (points == null || centre == null || centre.Length < 2)
            {
                return;
            }

            double minR = double.MaxValue, maxR = double.MinValue;
            double minZ = double.MaxValue, maxZ = double.MinValue;
            bool any = false;

            foreach (double[] p in points)
            {
                if (p == null || p.Length < 3)
                {
                    continue;
                }

                double dx = p[0] - centre[0], dy = p[1] - centre[1];
                double r = Math.Sqrt(dx * dx + dy * dy);
                minR = Math.Min(minR, r);
                maxR = Math.Max(maxR, r);
                minZ = Math.Min(minZ, p[2]);
                maxZ = Math.Max(maxZ, p[2]);
                any = true;
            }

            if (any)
            {
                width = maxR - minR;
                depth = maxZ - minZ;
            }
        }

        private static double[] Unit(double[] v)
        {
            if (v == null || v.Length < 3)
            {
                return null;
            }

            double length = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            return length < 1e-9 ? null : new[] { v[0] / length, v[1] / length, v[2] / length };
        }
    }
}
