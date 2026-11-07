using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The saved design model: the file the ETABS side has always been able to write, now
    /// written from the Revit side as well.
    ///
    /// The whole worth of it is that a check can be run with no ETABS on the machine at all,
    /// so what these tests are really about is that the file is the same file whichever side
    /// wrote it and that what comes back out of it is what went in. The one thing that would
    /// quietly ruin it is a model that reads back short - a list that arrives empty because
    /// the build that wrote it did not have that list yet - because nothing downstream can
    /// tell that from a building that really has no walls in it, and the run then reports
    /// every wall in the Revit model as one ETABS has not got.
    /// </summary>
    [TestClass]
    public class SavedModelFileTests
    {
        private const string Story = "L03";

        /// <summary>
        /// The line loads on the frames have to survive the file, because most runs are made
        /// against a saved model rather than a live ETABS session. Left out of the file the
        /// line load check silently did nothing on every one of those runs, and the plan it
        /// drew was indistinguishable from a drawing with no line loads on it.
        /// </summary>
        [TestMethod]
        public void TheDistributedLoadsOnTheFramesSurviveTheFile()
        {
            LineLoadFrame_E frame = new LineLoadFrame_E(
                "B12", "B12", Story, new MyPoint(0, 0, 0), new MyPoint(6000, 0, 0));

            frame.Add(new LineLoad_E("SIDL", 13.44, 13.44, 0, 1, 10, true, false));
            frame.Add(new LineLoad_E("WIND", 4.2, 6.3, 0.25, 0.75, 6, true, false));

            EtabsDataContainer written = new EtabsDataContainer
            {
                frames = new List<FrameData_e>(),
                columns = new List<Column_e>(),
                stories = new List<Story_E>(),
                Walls = new List<Wall_E>(),
                Slabs = new List<Slab_E>(),
                LineLoadFrames = new List<LineLoadFrame_E> { frame }
            };

            EtabsModelFile.Write(file, written);
            EtabsDataContainer read = EtabsModelFile.Read(file);

            Assert.AreEqual(1, read.LineLoadFrames.Count, "the frame should have come back");

            LineLoadFrame_E back = read.LineLoadFrames[0];

            Assert.AreEqual("B12", back.UniqueName);
            Assert.AreEqual(Story, back.Story);
            Assert.AreEqual(6000, back.PlanLength, 1e-6, "its line in plan should have come back");
            Assert.AreEqual(2, back.Loads.Count);

            Assert.AreEqual("SIDL", back.Loads[0].Pattern);
            Assert.AreEqual(13.44, back.Loads[0].ValueAtStart, 1e-9);
            Assert.IsTrue(back.Loads[0].IsGravity);

            // The trapezoid and the part length matter as much as the value: a load read back
            // as uniform over the whole frame is a different load.
            Assert.AreEqual(4.2, back.Loads[1].ValueAtStart, 1e-9);
            Assert.AreEqual(6.3, back.Loads[1].ValueAtEnd, 1e-9);
            Assert.AreEqual(0.25, back.Loads[1].RelativeFrom, 1e-9);
            Assert.AreEqual(0.75, back.Loads[1].RelativeTo, 1e-9);
        }

        /// <summary>A file written before line loads existed is a model with none, not a fault.</summary>
        [TestMethod]
        public void AFileWrittenBeforeLineLoadsExistedReadsAsHavingNone()
        {
            EtabsModelFile.Write(file, new EtabsDataContainer
            {
                frames = new List<FrameData_e>(),
                columns = new List<Column_e>(),
                stories = new List<Story_E>(),
                Walls = new List<Wall_E>(),
                Slabs = new List<Slab_E>(),
                LineLoadFrames = null
            });

            EtabsDataContainer read = EtabsModelFile.Read(file);

            Assert.IsNotNull(read.LineLoadFrames);
            Assert.AreEqual(0, read.LineLoadFrames.Count);
        }

        /// <summary>
        /// A None area carrying load goes through the file as what it is, with its load, and the
        /// file says it was written by a build that keeps them.
        /// </summary>
        [TestMethod]
        public void ALoadAreaAndTheMarkThatTheyAreReadSurviveTheFile()
        {
            Slab_E area = new Slab_E("N1", new[]
            {
                new MyPoint(0, 0, 0), new MyPoint(4000, 0, 0), new MyPoint(4000, 4000, 0), new MyPoint(0, 4000, 0)
            }.Select(point => new Joint("", point, Form1.eLength.mm)).ToList(), Story, "None", SlabKind.LoadOnly, 0, 0, false);
            area.SetLoads(new List<AreaLoad_E> { new AreaLoad_E("SIDL", 3.0, 10, true, false) });

            EtabsModelFile.Write(file, new EtabsDataContainer
            {
                frames = new List<FrameData_e>(),
                columns = new List<Column_e>(),
                stories = new List<Story_E>(),
                Walls = new List<Wall_E>(),
                Slabs = new List<Slab_E> { area },
                ReadsNullLoadAreas = true
            });

            EtabsDataContainer read = EtabsModelFile.Read(file);

            Assert.IsTrue(read.ReadsNullLoadAreas);
            Slab_E back = read.Slabs.Single();
            Assert.IsTrue(back.IsLoadOnly);
            Assert.AreEqual(3.0, back.Loads.Single().Value, 1e-9);
        }

        /// <summary>
        /// A file written before the None areas were kept has none of them in it, and says so by
        /// not carrying the mark - which is what lets the load check warn rather than report every
        /// slab under one as carrying its own load alone.
        /// </summary>
        [TestMethod]
        public void AFileWrittenBeforeLoadAreasWereKeptSaysSo()
        {
            File.WriteAllText(file, "{\"frames\":[],\"columns\":[],\"stories\":[],\"Walls\":[],\"Slabs\":[]}");

            Assert.IsFalse(EtabsModelFile.Read(file).ReadsNullLoadAreas);
        }

        private string file;

        [TestInitialize]
        public void MakeAFileName()
        {
            file = Path.Combine(Path.GetTempPath(),
                                "discrepancy scanner test " + Guid.NewGuid().ToString("N") + ".json");
        }

        [TestCleanup]
        public void RemoveIt()
        {
            try
            {
                if (file != null && File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
                // A temp file left behind is not a failing test.
            }
        }

        /// <summary>
        /// Out and back with every kind of thing in it. Counted rather than compared element
        /// by element: the shapes each have their own tests, and what is being asked here is
        /// whether all five lists survive the trip.
        /// </summary>
        [TestMethod]
        public void EveryListSurvivesTheRoundTrip()
        {
            EtabsModelFile.Write(file, WholeModel());

            EtabsDataContainer read = EtabsModelFile.Read(file);

            Assert.AreEqual(2, read.frames.Count, "beams");
            Assert.AreEqual(1, read.columns.Count, "columns");
            Assert.AreEqual(2, read.stories.Count, "levels");
            Assert.AreEqual(1, read.Walls.Count, "walls");
            Assert.AreEqual(1, read.Slabs.Count, "slabs");
        }

        /// <summary>
        /// The slab keeps what the check reads off it. A file that came back with the story
        /// or the thickness missing would import without complaint and then fail everything
        /// on the level, which is the failure this is here to keep out.
        /// </summary>
        [TestMethod]
        public void ASlabKeepsWhatTheCheckReadsOffIt()
        {
            EtabsModelFile.Write(file, WholeModel());

            Slab_E slab = EtabsModelFile.Read(file).Slabs.Single();

            Assert.AreEqual("A1", slab.UniqueName);
            Assert.AreEqual(Story, slab.Story);
            Assert.AreEqual(200, slab.Thickness, 0.001);
            Assert.IsFalse(slab.IsOpening);
        }

        /// <summary>
        /// A wall keeps its joints, and works its centre line out again from them. The line
        /// itself is not in the file - it is derived - so a wall that arrived without one
        /// would have no length and would pair with nothing.
        /// </summary>
        [TestMethod]
        public void AWallComesBackWithItsLine()
        {
            EtabsModelFile.Write(file, WholeModel());

            Wall_E wall = EtabsModelFile.Read(file).Walls.Single();
            wall.generateWallDrawingPoints();

            Assert.AreEqual("w1", wall.UniqueName);
            Assert.IsTrue(wall.HasCentreLine, "the centre line is worked out from the joints");
            Assert.AreEqual(4000, wall.Length, 1);
        }

        /// <summary>
        /// A file written before slabs existed has no slab list in it at all. It reads back as
        /// a model with no slabs rather than as a null nothing can count, which is what lets
        /// an old file still be opened.
        /// </summary>
        [TestMethod]
        public void AFileFromBeforeSlabsStillOpens()
        {
            File.WriteAllText(file, @"{""frames"":[],""columns"":[],""stories"":[],""Walls"":[]}");

            EtabsDataContainer read = EtabsModelFile.Read(file);

            Assert.IsNotNull(read.Slabs, "an absent list reads as an empty one");
            Assert.AreEqual(0, read.Slabs.Count);
        }

        /// <summary>
        /// Something that is not one of these files is a fault said out loud. Silently reading
        /// as an empty model is the one outcome that must not happen: it looks exactly like a
        /// successful import of a building with nothing in it.
        /// </summary>
        [TestMethod]
        public void SomethingElseEntirelyIsRefused()
        {
            File.WriteAllText(file, "this is not a model");

            try
            {
                EtabsModelFile.Read(file);
                Assert.Fail("reading a file that is not a model should have said so");
            }
            catch (AssertFailedException)
            {
                throw;
            }
            catch (Exception)
            {
                // Which exception the serializer raises is its business; that it refuses is ours.
            }
        }

        /// <summary>
        /// What the container holds is what gets saved. This is the join the Revit side needs
        /// and the ETABS side never did: over there the model is still in the container it was
        /// read into, while here it has been unpacked into the data container and has to be
        /// gathered back up before it can be written.
        /// </summary>
        [TestMethod]
        public void WhatIsLoadedIsWhatIsSaved()
        {
            DataContainer data = DataContainer.getDataContainer();
            data.LoadEtabsData(WholeModel(), "tower.EDB");

            EtabsModelFile.Write(file, data.ToEtabsData());
            EtabsDataContainer read = EtabsModelFile.Read(file);

            Assert.AreEqual("tower.EDB", data.EtabsSourceName, "the header says where it came from");
            Assert.AreEqual(2, read.frames.Count, "beams");
            Assert.AreEqual(1, read.columns.Count, "columns");
            Assert.AreEqual(2, read.stories.Count, "levels");
            Assert.AreEqual(1, read.Walls.Count, "walls");
            Assert.AreEqual(1, read.Slabs.Count, "slabs");
        }

        /// <summary>
        /// A model read from a file and saved straight back out again is the same model. The
        /// file gets passed around, so it gets opened and re-saved, and each trip through has
        /// to be lossless or a model degrades as it is handed on.
        /// </summary>
        [TestMethod]
        public void SavingWhatWasJustOpenedChangesNothing()
        {
            EtabsModelFile.Write(file, WholeModel());
            EtabsDataContainer once = EtabsModelFile.Read(file);

            string second = file + ".again.json";
            try
            {
                EtabsModelFile.Write(second, once);
                EtabsDataContainer twice = EtabsModelFile.Read(second);

                Assert.AreEqual(File.ReadAllText(file), File.ReadAllText(second),
                                "the same model writes the same file");
                Assert.AreEqual(once.Slabs.Count, twice.Slabs.Count);
                Assert.AreEqual(once.Walls.Count, twice.Walls.Count);
            }
            finally
            {
                if (File.Exists(second))
                {
                    File.Delete(second);
                }
            }
        }

        /// <summary>
        /// Nothing read yet is not something to save. The buttons ask this first and say so
        /// instead, because a file holding an empty model is indistinguishable from a file
        /// holding a building once it has been handed to somebody else.
        /// </summary>
        [TestMethod]
        public void AnEmptyModelIsRecognisedAsNothingToSave()
        {
            Assert.IsTrue(EtabsModelFile.IsEmpty(null));
            Assert.IsTrue(EtabsModelFile.IsEmpty(new EtabsDataContainer()));
            Assert.IsTrue(EtabsModelFile.IsEmpty(new EtabsDataContainer
            {
                // Levels on their own are not a model: every ETABS model has levels.
                stories = new List<Story_E> { new Story_E(Story, 0, Form1.eLength.mm) }
            }));
            Assert.IsFalse(EtabsModelFile.IsEmpty(WholeModel()));
        }

        /// <summary>The line the save puts up says what went into the file.</summary>
        [TestMethod]
        public void TheLineSaysWhatWasSaved()
        {
            string said = EtabsModelFile.Describe(WholeModel());

            StringAssert.Contains(said, "2 beams");
            StringAssert.Contains(said, "1 columns");
            StringAssert.Contains(said, "1 wall areas");
            StringAssert.Contains(said, "1 slab areas");
            StringAssert.Contains(said, "2 levels");
        }

        #region a small building

        private static EtabsDataContainer WholeModel()
        {
            return new EtabsDataContainer
            {
                frames = new List<FrameData_e> { new FrameData_e("b1"), new FrameData_e("b2") },
                columns = new List<Column_e> { new Column_e(new FrameData_e("c1")) },
                stories = new List<Story_E>
                {
                    new Story_E(Story, 10000, Form1.eLength.mm),
                    new Story_E("L02", 7000, Form1.eLength.mm)
                },
                Walls = new List<Wall_E>
                {
                    new Wall_E("w1",
                               new[] { At(0, 0, 0), At(4000, 0, 0), At(4000, 0, 3000), At(0, 0, 3000) },
                               Story, 200, "W200")
                },
                Slabs = new List<Slab_E> { OneSlab() }
            };
        }

        private static Slab_E OneSlab()
        {
            List<Joint> joints = new List<Joint>
            {
                At(0, 0, 10000), At(6000, 0, 10000), At(6000, 6000, 10000), At(0, 6000, 10000)
            };

            return new Slab_E("A1", joints, Story, "SLAB200", SlabKind.Slab, 200, 200, false);
        }

        private static Joint At(double x, double y, double z)
        {
            return new Joint("p", new MyPoint(x, y, z), Form1.eLength.mm);
        }

        #endregion
    }
}
