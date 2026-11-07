using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// Two shells drawn one on top of the other, which is how both programs are used and how a
    /// load goes missing.
    ///
    /// A slab with a second shell over part of it - an overlay in SAFE, a drop panel in ETABS -
    /// is two area objects covering one piece of plan, and both of them can carry a load. The
    /// mesh hands each piece of plan to one shell, so read shell by shell the piece under both
    /// reports whichever one won and the other one's load is never counted. What the concrete
    /// under there actually carries is the sum, and that is what these pin down.
    /// </summary>
    [TestClass]
    public class StackedShellTests
    {
        private const double Size = 10000;

        [TestMethod]
        public void ShellsOverOneAnotherAddTheirLoads()
        {
            Slab_E slab = Area("SLAB", 0, 0, Size, Size, Load("SDL", 3.0));
            Slab_E overlay = Area("OVERLAY", 0, 0, Size / 2, Size / 2, Load("SDL", 2.0));

            IList<AreaLoad_E> acting = StackedShells.LoadsAt(
                overlay, new List<Slab_E> { slab, overlay }, new MyPoint(1000, 1000, 0));

            Assert.AreEqual(5.0, acting.Sum(load => load.Value), 1e-9,
                "the piece under both shells carries what both of them put there");
        }

        [TestMethod]
        public void AShellSomewhereElseOnTheFloorAddsNothing()
        {
            Slab_E here = Area("HERE", 0, 0, Size, Size, Load("SDL", 3.0));
            Slab_E elsewhere = Area("ELSEWHERE", Size * 2, Size * 2, Size * 3, Size * 3,
                                    Load("SDL", 9.0));

            IList<AreaLoad_E> acting = StackedShells.LoadsAt(
                here, new List<Slab_E> { here, elsewhere }, new MyPoint(1000, 1000, 0));

            Assert.AreEqual(3.0, acting.Sum(load => load.Value), 1e-9,
                "a shell that does not cover this point is not on top of it");
        }

        [TestMethod]
        public void AnOpeningOverTheSlabAddsNothing()
        {
            Slab_E slab = Area("SLAB", 0, 0, Size, Size, Load("SDL", 3.0));
            Slab_E hole = Area("HOLE", 0, 0, Size / 2, Size / 2, Load("SDL", 9.0), opening: true);

            IList<AreaLoad_E> acting = StackedShells.LoadsAt(
                slab, new List<Slab_E> { slab, hole }, new MyPoint(1000, 1000, 0));

            Assert.AreEqual(3.0, acting.Sum(load => load.Value), 1e-9,
                "an opening is the model saying there is no slab there, so it carries nothing");
        }

        [TestMethod]
        public void AShellIsNeverAddedToItself()
        {
            // The face's own shell arrives twice: once as the face's source, and again in the
            // list of everything on the story. Counted twice it would double every load in the
            // model, which would read as every shell being wrong by exactly a factor of two.
            Slab_E slab = Area("SLAB", 0, 0, Size, Size, Load("SDL", 3.0));

            IList<AreaLoad_E> acting = StackedShells.LoadsAt(
                slab, new List<Slab_E> { slab }, new MyPoint(1000, 1000, 0));

            Assert.AreEqual(3.0, acting.Sum(load => load.Value), 1e-9);
            Assert.AreEqual(1, StackedShells.CountAt(slab, new List<Slab_E> { slab },
                                                     new MyPoint(1000, 1000, 0)));
        }

        [TestMethod]
        public void TheNumberOfShellsOverAPointIsReported()
        {
            Slab_E slab = Area("SLAB", 0, 0, Size, Size, Load("SDL", 3.0));
            Slab_E overlay = Area("OVERLAY", 0, 0, Size / 2, Size / 2, Load("SDL", 2.0));
            Slab_E third = Area("THIRD", 0, 0, Size / 4, Size / 4, Load("SDL", 1.0));

            List<Slab_E> story = new List<Slab_E> { slab, overlay, third };

            Assert.AreEqual(3, StackedShells.CountAt(third, story, new MyPoint(1000, 1000, 0)),
                "three shells stand over the corner");
            Assert.AreEqual(1, StackedShells.CountAt(slab, story, new MyPoint(9000, 9000, 0)),
                "and only the slab reaches the far corner");
        }

        [TestMethod]
        public void LoadsOnDifferentPatternsAreAllKept()
        {
            // Summed does not mean merged. The two shells carry different patterns, and each
            // has to reach the category mapping on its own or a load would be added into the
            // wrong one of the four.
            Slab_E slab = Area("SLAB", 0, 0, Size, Size, Load("SDL", 3.0));
            Slab_E overlay = Area("OVERLAY", 0, 0, Size / 2, Size / 2, Load("LL", 2.0));

            IList<AreaLoad_E> acting = StackedShells.LoadsAt(
                overlay, new List<Slab_E> { slab, overlay }, new MyPoint(1000, 1000, 0));

            Assert.AreEqual(2, acting.Count);
            Assert.IsTrue(acting.Any(load => load.Pattern == "SDL" && Math.Abs(load.Value - 3.0) < 1e-9));
            Assert.IsTrue(acting.Any(load => load.Pattern == "LL" && Math.Abs(load.Value - 2.0) < 1e-9));
        }

        #region building the shells

        private static AreaLoad_E Load(string pattern, double value)
        {
            return new AreaLoad_E(pattern, value, 10, true, false);
        }

        private static Slab_E Area(string name, double x1, double y1, double x2, double y2,
                                   AreaLoad_E load, bool opening = false)
        {
            List<Joint> joints = new List<MyPoint>
            {
                new MyPoint(x1, y1, 0),
                new MyPoint(x2, y1, 0),
                new MyPoint(x2, y2, 0),
                new MyPoint(x1, y2, 0)
            }.Select(point => new Joint("", point, Form1.eLength.mm)).ToList();

            Slab_E area = new Slab_E(name, joints, "L03", "SLAB200", SlabKind.Slab, 200, 200, opening);
            area.SetLoads(new List<AreaLoad_E> { load });
            return area;
        }

        #endregion
    }
}
