using System.Collections.Generic;
using DiscrepancyScanner;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// Sizing Section Designer columns off the ETABS shape tables.
    ///
    /// The API will not answer GetRectangle for an SD section, so these columns used to come
    /// in with no size and no material. The field keys here are the ones ETABS 22 hands back
    /// from the editing and display arrays.
    /// </summary>
    [TestClass]
    public class SectionDesignerTableTests
    {
        private static readonly string[] RectangleFields =
        {
            "SectionType", "SectionName", "ShapeName", "Material", "XCenter", "YCenter",
            "Rotation", "Height", "Width", "Reinforcing", "RebarMat", "Color", "ZOrder"
        };

        private static readonly string[] CircleFields =
        {
            "SectionType", "SectionName", "ShapeName", "Material", "XCenter", "YCenter",
            "Diameter", "Reinforcing", "Color", "ZOrder"
        };

        private static readonly string[] PolygonFields =
        {
            "SectionType", "SectionName", "ShapeName", "Material", "X", "Y", "Reinforcing", "Color", "ZOrder"
        };

        private static SectionDesignerTables.Table Table(string[] fields, double mmPerUnit, params string[] data)
        {
            return new SectionDesignerTables.Table
            {
                FieldKeys = fields,
                Data = data,
                Records = data.Length / fields.Length,
                MillimetresPerUnit = mmPerUnit
            };
        }

        private static SectionDesignerTables.Table None()
        {
            return new SectionDesignerTables.Table();
        }

        [TestMethod]
        public void ARectangleIsARectangularColumn_YExtentIsTheDepth()
        {
            var rectangles = Table(RectangleFields, 1000,
                "Frame", "C1-SD", "Rect1", "C40", "0", "0", "0", "0.8", "0.3", "Yes", "A615", "Gray", "0");

            var issues = new List<string>();
            var outlines = SectionDesignerTables.Read(rectangles, None(), None(), issues);

            var c1 = outlines["C1-SD"];
            Assert.AreEqual(SectionShape.Rectangular, c1.Shape);
            Assert.AreEqual(800, c1.Depth, 1e-6);
            Assert.AreEqual(300, c1.Width, 1e-6);
            Assert.AreEqual("C40", c1.Material);
            Assert.AreEqual(0, issues.Count);
        }

        [TestMethod]
        public void ARectangleTurnedAQuarterStaysRectangular_SidesSwapped()
        {
            var rectangles = Table(RectangleFields, 1,
                "Frame", "C2", "R", "C40", "50", "-20", "90", "800", "300", "No", "", "", "0");

            var c2 = SectionDesignerTables.Read(rectangles, None(), None(), null)["C2"];
            Assert.AreEqual(SectionShape.Rectangular, c2.Shape);
            Assert.AreEqual(300, c2.Depth, 1e-6);
            Assert.AreEqual(800, c2.Width, 1e-6);
        }

        [TestMethod]
        public void ACircleIsARoundColumn()
        {
            var circles = Table(CircleFields, 1,
                "Frame", "R600", "Circle1", "C50", "0", "0", "600", "Yes", "", "0");

            var r = SectionDesignerTables.Read(None(), circles, None(), null)["R600"];
            Assert.AreEqual(SectionShape.Round, r.Shape);
            Assert.AreEqual(600, r.Diameter, 1e-6);
        }

        [TestMethod]
        public void ARectangularPolygonIsRectangular_AnLShapeIsNot()
        {
            var polygons = Table(PolygonFields, 1,
                "Frame", "RECT", "P1", "C40", "-200", "-400", "Yes", "", "0",
                "Frame", "RECT", "P1", "", "200", "-400", "", "", "",
                "Frame", "RECT", "P1", "", "200", "400", "", "", "",
                "Frame", "RECT", "P1", "", "-200", "400", "", "", "",
                "Frame", "L", "P1", "C40", "0", "0", "Yes", "", "0",
                "Frame", "L", "P1", "", "600", "0", "", "", "",
                "Frame", "L", "P1", "", "600", "200", "", "", "",
                "Frame", "L", "P1", "", "200", "200", "", "", "",
                "Frame", "L", "P1", "", "200", "500", "", "", "",
                "Frame", "L", "P1", "", "0", "500", "", "", "");

            var outlines = SectionDesignerTables.Read(None(), None(), polygons, null);

            Assert.AreEqual(SectionShape.Rectangular, outlines["RECT"].Shape);
            Assert.AreEqual(400, outlines["RECT"].Width, 1e-6);
            Assert.AreEqual(800, outlines["RECT"].Depth, 1e-6);
            Assert.AreEqual("C40", outlines["RECT"].Material);

            Assert.AreEqual(SectionShape.Unknown, outlines["L"].Shape);
            Assert.AreEqual(600, outlines["L"].Width, 1e-6);
            Assert.AreEqual(500, outlines["L"].Depth, 1e-6);
        }

        [TestMethod]
        public void PiersAreNotFrameSections()
        {
            var rectangles = Table(RectangleFields, 1,
                "Pier", "P1", "R", "C40", "0", "0", "0", "3000", "300", "No", "", "", "0");

            var outlines = SectionDesignerTables.Read(rectangles, None(), None(), null);
            Assert.IsFalse(outlines.ContainsKey("P1"));
        }

        [TestMethod]
        public void TwoShapesAreSizedByTheirBox_AndNotCalledRectangular()
        {
            var rectangles = Table(RectangleFields, 1,
                "Frame", "TWO", "A", "C40", "0", "0", "0", "400", "400", "No", "", "", "0",
                "Frame", "TWO", "B", "C40", "400", "0", "0", "400", "400", "No", "", "", "0");

            var two = SectionDesignerTables.Read(rectangles, None(), None(), null)["TWO"];
            Assert.AreEqual(SectionShape.Unknown, two.Shape);
            Assert.AreEqual(800, two.Width, 1e-6);
            Assert.AreEqual(400, two.Depth, 1e-6);
        }

        [TestMethod]
        public void MissingColumnsAreSaid()
        {
            var rectangles = Table(new[] { "SectionName", "Width" }, 1, "C1", "300");

            var issues = new List<string>();
            SectionDesignerTables.Read(rectangles, None(), None(), issues);
            Assert.AreEqual(1, issues.Count);
        }

        [TestMethod]
        public void LengthUnits()
        {
            Assert.AreEqual(1, SectionDesignerTables.MillimetresPer("mm"));
            Assert.AreEqual(1000, SectionDesignerTables.MillimetresPer("m"));
            Assert.AreEqual(25.4, SectionDesignerTables.MillimetresPer("in"));
            Assert.AreEqual(-1, SectionDesignerTables.MillimetresPer("furlong"));
        }
    }
}
