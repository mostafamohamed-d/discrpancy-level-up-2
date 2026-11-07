using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The section measured off a member's solid, for families whose size parameters carry
    /// names nobody listed in the settings.
    /// </summary>
    [TestClass]
    public class SectionExtentsTests
    {
        /// <summary>The eight corners of a box along a direction, b wide and h deep.</summary>
        private static List<double[]> Beam(double[] start, double[] along, double length, double b, double h)
        {
            double[] across, up;
            Assert.IsTrue(SectionExtents.BeamAxes(along, out across, out up));
            double norm = Math.Sqrt(along[0] * along[0] + along[1] * along[1] + along[2] * along[2]);
            List<double[]> corners = new List<double[]>();
            foreach (double s in new[] { 0d, length })
            foreach (double w in new[] { -b / 2, b / 2 })
            foreach (double d in new[] { -h, 0d })
            {
                corners.Add(new[]
                {
                    start[0] + along[0] / norm * s + across[0] * w + up[0] * d,
                    start[1] + along[1] / norm * s + across[1] * w + up[1] * d,
                    start[2] + along[2] / norm * s + across[2] * w + up[2] * d
                });
            }
            return corners;
        }

        [TestMethod]
        public void ABeamAtAnAngleInPlanMeasuresItsOwnWidthAndDepth()
        {
            double[] along = { Math.Cos(0.6), Math.Sin(0.6), 0 };
            List<double[]> points = Beam(new[] { 1000d, 2000d, 4720d }, along, 6000, 400, 800);

            double[] across, up;
            SectionExtents.BeamAxes(along, out across, out up);
            double width, depth;
            SectionExtents.Measure(points, across, up, out width, out depth);

            Assert.AreEqual(400, width, 1e-6);
            Assert.AreEqual(800, depth, 1e-6);
        }

        [TestMethod]
        public void ASlopedBeamIsMeasuredSquareToItsSlope()
        {
            // A ramp beam rising 1 in 5: measured straight up, its depth would read as the
            // vertical cut through it, deeper than the beam. Square to the slope it is 800.
            double[] along = { 5, 0, 1 };
            List<double[]> points = Beam(new[] { 0d, 0d, 0d }, along, 6000, 300, 800);

            double[] across, up;
            SectionExtents.BeamAxes(along, out across, out up);
            double width, depth;
            SectionExtents.Measure(points, across, up, out width, out depth);

            Assert.AreEqual(300, width, 1e-6);
            Assert.AreEqual(800, depth, 1e-6);
        }

        [TestMethod]
        public void AColumnTurnedInPlanIsMeasuredAlongItsOwnAxes()
        {
            // 400 along the family's X and 1200 along its Y, turned 30 degrees.
            double angle = Math.PI / 6;
            double[] x = { Math.Cos(angle), Math.Sin(angle), 0 };
            double[] y = { -Math.Sin(angle), Math.Cos(angle), 0 };
            List<double[]> points = new List<double[]>();
            foreach (double u in new[] { -200d, 200d })
            foreach (double v in new[] { -600d, 600d })
            foreach (double z in new[] { 0d, 4500d })
            {
                points.Add(new[] { 5000 + x[0] * u + y[0] * v, 7000 + x[1] * u + y[1] * v, z });
            }

            double width, depth;
            SectionExtents.Measure(points, x, y, out width, out depth);

            Assert.AreEqual(400, width, 1e-6);
            Assert.AreEqual(1200, depth, 1e-6);
        }

        [TestMethod]
        public void ACurvedBeamIsMeasuredOutFromItsCentreNotAcrossItsChord()
        {
            // A 400 wide, 800 deep beam on a 10 m radius, a quarter circle round (0, 0). Across
            // its chord it spreads by the whole bow of the arc; out from the centre it is 400.
            List<double[]> points = new List<double[]>();
            for (int i = 0; i <= 20; i++)
            {
                double a = Math.PI / 2 * i / 20;
                foreach (double r in new[] { 9800d, 10200d })
                foreach (double z in new[] { 4720d, 3920d })
                {
                    points.Add(new[] { r * Math.Cos(a), r * Math.Sin(a), z });
                }
            }

            double width, depth;
            SectionExtents.MeasureAroundCentre(points, new[] { 0d, 0d, 0d }, out width, out depth);

            Assert.AreEqual(400, width, 1e-6);
            Assert.AreEqual(800, depth, 1e-6);
        }

        [TestMethod]
        public void AVerticalMemberHasNoBeamAxes()
        {
            double[] across, up;
            Assert.IsFalse(SectionExtents.BeamAxes(new[] { 0d, 0d, 3000d }, out across, out up));
        }

        [TestMethod]
        public void NoPointsMeasureNothing()
        {
            double width, depth;
            SectionExtents.Measure(new List<double[]>(), new[] { 1d, 0, 0 }, new[] { 0d, 1, 0 }, out width, out depth);
            Assert.AreEqual(0, width);
            Assert.AreEqual(0, depth);
        }
    }
}
