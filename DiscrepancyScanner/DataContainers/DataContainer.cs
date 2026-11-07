using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DiscrepancyScanner
{
    public class DataContainer: INotifyPropertyChanged
    {
        private List<Wall_E> walls = new List<Wall_E>();
        private IList<Wall_R> walls_r = new List<Wall_R>();
        private List<PairedWall> pairedWalls = new List<PairedWall>();
        private List<Wall_E> unmatchedEtabsWalls = new List<Wall_E>();
        private List<Wall_R> unmatchedRevitWalls = new List<Wall_R>();
        private List<UncoveredWallPiece> uncoveredRevitWalls = new List<UncoveredWallPiece>();
        private List<CrossPair> crossPairs = new List<CrossPair>();
        private List<Slab_E> slabs = new List<Slab_E>();
        private IList<Slab_R> slabs_r = new List<Slab_R>();
        private IList<SlabZone> slabZones = new List<SlabZone>();
        private IList<SlabZone> slabLoadZones = new List<SlabZone>();
        private List<LineLoadFrame_E> lineLoadFrames = new List<LineLoadFrame_E>();
        private IList<LineLoadResult> lineLoadResults = new List<LineLoadResult>();
        private IList<EtabsLineLoad> unclaimedLineLoads = new List<EtabsLineLoad>();
        private IList<EtabsLineLoad> etabsLineLoads = new List<EtabsLineLoad>();
        private IList<SidlCSource_R> sidlCSources = new List<SidlCSource_R>();
        private IList<LineLoadResult> sidlCResults = new List<LineLoadResult>();
        private IList<EtabsLineLoad> unclaimedSidlC = new List<EtabsLineLoad>();
        private IList<EtabsLineLoad> etabsSidlC = new List<EtabsLineLoad>();
        private IList<SidlCAreaSource_R> sidlCAreaSources = new List<SidlCAreaSource_R>();
        private IList<SidlCAreaResult> sidlCAreaResults = new List<SidlCAreaResult>();
        private IList<SidlCAreaUnclaimed> unclaimedSidlCArea = new List<SidlCAreaUnclaimed>();
        private List<Story_E> stories = new List<Story_E>();
        private List<Column_e> columns = new List<Column_e>();
        private IList<Column_r> columns_r = new List<Column_r>();
        private List<PairedColumn> pairedColumns = new List<PairedColumn>();
        private List<Column_e> unmatchedEtabsColumns = new List<Column_e>();
        private List<Column_r> unmatchedRevitColumns = new List<Column_r>();
        private  List<FrameData_e> unmatchedEtabs = new List<FrameData_e>();
        private  List<FrameData_r> unmatchedRevit = new List<FrameData_r>();
        private  List<PairedFrame> pairedFrames = new List<PairedFrame>();
        private  IList<FrameData_r> frames_r = new List<FrameData_r>();
        private  List<FrameData_e> frames_e = new List<FrameData_e>();
        private static MyTransformation modelTransformation = new MyTransformation();
        public  double locationTolerance=1000;
        public  double lengthTolerance= 1000;

        public event PropertyChangedEventHandler PropertyChanged;

        private static DataContainer dataContainer;

        private DataContainer()
        {

        }
        public static DataContainer getDataContainer()
        {
            if (dataContainer != null)
            {
                return dataContainer;
            }

            dataContainer = new DataContainer();
            return dataContainer;
        }

        public static MyTransformation ModelTransformation { get => modelTransformation;
            set
            {
                modelTransformation= value;
                
            }
             }
        protected void OnPropertyChanged(string propertyName)
        {
            OnPropertyChanged(new PropertyChangedEventArgs(propertyName));
        }
        protected void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
                handler(this, e);
        }
        public  List<FrameData_e> UnmatchedEtabs 
        { get => unmatchedEtabs;
            set
            {
                unmatchedEtabs = value;
                OnPropertyChanged("UnmatchedEtabs");
            }
        }
        public  List<FrameData_r> UnmatchedRevit
        { get => unmatchedRevit;
            set
            {
                unmatchedRevit = value;
                OnPropertyChanged("UnmatchedRevit");
            }
                 }
        public  List<PairedFrame> PairedFrames
        { get => pairedFrames;
            set 
                {
                pairedFrames = value;
                OnPropertyChanged("PairedFrames");
                }
             }
        public  IList<FrameData_r> Frames_r
        { get => frames_r;
            set
            {

                frames_r = value;
                OnPropertyChanged("Frames_r");
               

            }
        }
        public  List<FrameData_e> Frames_e
        { get => frames_e;
            set
            {

                frames_e = value;
                OnPropertyChanged("Frames_e");


            }
        }

        public List<Story_E> Stories { get => stories; set => stories = value; }
        public List<Column_e> Columns { get => columns; set => columns = value; }
        public List<Wall_E> Walls { get => walls; set => walls = value; }

        /// <summary>Walls read from Revit, in the shared coordinate system.</summary>
        public IList<Wall_R> Walls_r
        {
            get => walls_r;
            set
            {
                walls_r = value ?? new List<Wall_R>();
                OnPropertyChanged("Walls_r");
            }
        }

        public List<PairedWall> PairedWalls
        {
            get => pairedWalls;
            set
            {
                pairedWalls = value ?? new List<PairedWall>();
                OnPropertyChanged("PairedWalls");
            }
        }

        public List<Wall_E> UnmatchedEtabsWalls
        {
            get => unmatchedEtabsWalls;
            set
            {
                unmatchedEtabsWalls = value ?? new List<Wall_E>();
                OnPropertyChanged("UnmatchedEtabsWalls");
            }
        }

        public List<Wall_R> UnmatchedRevitWalls
        {
            get => unmatchedRevitWalls;
            set
            {
                unmatchedRevitWalls = value ?? new List<Wall_R>();
                OnPropertyChanged("UnmatchedRevitWalls");
            }
        }

        /// <summary>
        /// The stretches of paired Revit wall that no ETABS panel stands along, where there is
        /// more of them than the buffer allows and they are drawn in their own right.
        /// </summary>
        public List<UncoveredWallPiece> UncoveredRevitWalls
        {
            get => uncoveredRevitWalls;
            set
            {
                uncoveredRevitWalls = value ?? new List<UncoveredWallPiece>();
                OnPropertyChanged("UncoveredRevitWalls");
            }
        }

        /// <summary>
        /// The columns in one model paired with walls in the other, where that pairing is
        /// switched on. Empty otherwise. What is in here has been taken out of the four
        /// unpaired lists. See <see cref="CrossPairEngine"/>.
        /// </summary>
        public List<CrossPair> CrossPairs
        {
            get => crossPairs;
            set
            {
                crossPairs = value ?? new List<CrossPair>();
                OnPropertyChanged("CrossPairs");
            }
        }

        /// <summary>ETABS floor areas, and the openings drawn in them, as imported.</summary>
        public List<Slab_E> Slabs
        {
            get => slabs;
            set
            {
                slabs = value ?? new List<Slab_E>();
                OnPropertyChanged("Slabs");
            }
        }

        /// <summary>Revit floors, in the shared coordinate system.</summary>
        public IList<Slab_R> Slabs_r
        {
            get => slabs_r;
            set
            {
                slabs_r = value ?? new List<Slab_R>();
                OnPropertyChanged("Slabs_r");
            }
        }

        /// <summary>
        /// What the slab check produced: the ETABS mesh, cut only where the two models stop
        /// agreeing, with a verdict on every piece.
        /// </summary>
        public IList<SlabZone> SlabZones
        {
            get => slabZones;
            set
            {
                slabZones = value ?? new List<SlabZone>();
                OnPropertyChanged("SlabZones");
            }
        }

        /// <summary>
        /// What the load check produced: the same ETABS areas, cut along the loading plan's
        /// regions instead of along the Revit floors. A separate mesh from
        /// <see cref="SlabZones"/> because the two are cut for different reasons - see
        /// SlabMeshCompare.Purpose - and empty for every level with no loading plan paired.
        /// </summary>
        public IList<SlabZone> SlabLoadZones
        {
            get => slabLoadZones;
            set
            {
                slabLoadZones = value ?? new List<SlabZone>();
                OnPropertyChanged("SlabLoadZones");
            }
        }

        /// <summary>
        /// What the line load check produced: one verdict per detail line drawn on the loading
        /// plan.
        ///
        /// Keyed on the Revit lines rather than on the ETABS frames, which is the other way
        /// round from every other check here. This one is read against the loading drawing, so
        /// what carries a verdict is the line somebody drew.
        /// </summary>
        public IList<LineLoadResult> LineLoadResults
        {
            get => lineLoadResults;
            set
            {
                lineLoadResults = value ?? new List<LineLoadResult>();
                OnPropertyChanged("LineLoadResults");
            }
        }

        /// <summary>
        /// The stretches of ETABS frame carrying a distributed load that no drawn line asked
        /// for. The only thing the line load plan draws that is not a drawn line, because a
        /// line nobody drew leaves nothing there to colour.
        /// </summary>
        public IList<EtabsLineLoad> UnclaimedLineLoads
        {
            get => unclaimedLineLoads;
            set
            {
                unclaimedLineLoads = value ?? new List<EtabsLineLoad>();
                OnPropertyChanged("UnclaimedLineLoads");
            }
        }

        /// <summary>
        /// Every ETABS frame that could carry a line load, with the distributed loads on it.
        ///
        /// Read apart from the beams because the beam import drops frames whose material reads
        /// as a dummy or "other", which is exactly what a null frame along a slab edge is made
        /// of - see <see cref="EtabsDataContainer.LineLoadFrames"/>.
        /// </summary>
        public List<LineLoadFrame_E> LineLoadFrames
        {
            get => lineLoadFrames;
            set
            {
                lineLoadFrames = value ?? new List<LineLoadFrame_E>();
                OnPropertyChanged("LineLoadFrames");
            }
        }

        /// <summary>
        /// Every stretch of ETABS frame carrying a line load, claimed or not.
        ///
        /// A superset of <see cref="UnclaimedLineLoads"/>, kept so that an acceptance filed on a
        /// stretch while nothing claimed it can be found and taken back once something does.
        /// </summary>
        public IList<EtabsLineLoad> EtabsLineLoads
        {
            get => etabsLineLoads;
            set
            {
                etabsLineLoads = value ?? new List<EtabsLineLoad>();
                OnPropertyChanged("EtabsLineLoads");
            }
        }

        /// <summary>
        /// The Revit walls and fill strips the SIDL_C check weighs - the ones the wall and slab
        /// checks leave out as not structure. See <see cref="SidlCSource_R"/>.
        /// </summary>
        public IList<SidlCSource_R> SidlCSources
        {
            get => sidlCSources;
            set
            {
                sidlCSources = value ?? new List<SidlCSource_R>();
                OnPropertyChanged("SidlCSources");
            }
        }

        /// <summary>
        /// What the SIDL_C check produced: one verdict per wall or fill strip, read as a line
        /// load of its own weight against the SIDL_C loads on the frames. The twin of
        /// <see cref="LineLoadResults"/>, kept apart so the two tabs never mix.
        /// </summary>
        public IList<LineLoadResult> SidlCResults
        {
            get => sidlCResults;
            set
            {
                sidlCResults = value ?? new List<LineLoadResult>();
                OnPropertyChanged("SidlCResults");
            }
        }

        /// <summary>The stretches of frame carrying a SIDL_C load no wall or fill strip accounts for.</summary>
        public IList<EtabsLineLoad> UnclaimedSidlC
        {
            get => unclaimedSidlC;
            set
            {
                unclaimedSidlC = value ?? new List<EtabsLineLoad>();
                OnPropertyChanged("UnclaimedSidlC");
            }
        }

        /// <summary>Every stretch of frame carrying a SIDL_C load, claimed or not.</summary>
        public IList<EtabsLineLoad> EtabsSidlC
        {
            get => etabsSidlC;
            set
            {
                etabsSidlC = value ?? new List<EtabsLineLoad>();
                OnPropertyChanged("EtabsSidlC");
            }
        }

        /// <summary>
        /// The Revit light weight fill floors the SIDL_C area check weighs. See
        /// <see cref="SidlCAreaSource_R"/>.
        /// </summary>
        public IList<SidlCAreaSource_R> SidlCAreaSources
        {
            get => sidlCAreaSources;
            set
            {
                sidlCAreaSources = value ?? new List<SidlCAreaSource_R>();
                OnPropertyChanged("SidlCAreaSources");
            }
        }

        /// <summary>What the SIDL_C area check made of each fill floor.</summary>
        public IList<SidlCAreaResult> SidlCAreaResults
        {
            get => sidlCAreaResults;
            set
            {
                sidlCAreaResults = value ?? new List<SidlCAreaResult>();
                OnPropertyChanged("SidlCAreaResults");
            }
        }

        /// <summary>The ETABS areas carrying SIDL_C with no fill floor over them.</summary>
        public IList<SidlCAreaUnclaimed> UnclaimedSidlCArea
        {
            get => unclaimedSidlCArea;
            set
            {
                unclaimedSidlCArea = value ?? new List<SidlCAreaUnclaimed>();
                OnPropertyChanged("UnclaimedSidlCArea");
            }
        }

        /// <summary>Columns read from Revit, in the shared coordinate system.</summary>
        public IList<Column_r> Columns_r
        {
            get => columns_r;
            set
            {
                columns_r = value;
                OnPropertyChanged("Columns_r");
            }
        }

        public List<PairedColumn> PairedColumns
        {
            get => pairedColumns;
            set
            {
                pairedColumns = value;
                OnPropertyChanged("PairedColumns");
            }
        }

        public List<Column_e> UnmatchedEtabsColumns
        {
            get => unmatchedEtabsColumns;
            set
            {
                unmatchedEtabsColumns = value;
                OnPropertyChanged("UnmatchedEtabsColumns");
            }
        }

        public List<Column_r> UnmatchedRevitColumns
        {
            get => unmatchedRevitColumns;
            set
            {
                unmatchedRevitColumns = value;
                OnPropertyChanged("UnmatchedRevitColumns");
            }
        }

        public enum eLength
        {
            NotApplicable,
            inch,
            ft,
            micron,
            mm,
            cm,
            m

        }


        /// <summary>
        /// What the ETABS side was read from, for the header bar: a saved file's name, or the
        /// running model's. Empty until something has been imported, which is how the header
        /// knows to say the model has not been read yet.
        /// </summary>
        public string EtabsSourceName { get; private set; } = "";

        /// <summary>
        /// False when the design model came off a file saved before the null areas carrying
        /// load were read, so none of them are in it - see
        /// <see cref="EtabsDataContainer.ReadsNullLoadAreas"/>.
        /// </summary>
        public bool EtabsReadsNullLoadAreas { get; private set; } = true;

        public  void getEtabsDataFromFile()
        {
            try
            {
                OpenFileDialog dialog = new OpenFileDialog();
                dialog.RestoreDirectory = true;
                dialog.Filter = EtabsModelFile.FileFilter;
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    LoadEtabsData(EtabsModelFile.Read(dialog.FileName),
                                  Path.GetFileName(dialog.FileName));
                }
            }
            catch (Exception ex)
            {

                MessageBox.Show("!! error" + ex.Message);
            }

        }

        /// <summary>
        /// Puts a whole design model in place, however it was got: read live, or read back off
        /// a file saved earlier. The source name is what the header bar shows, and is the one
        /// thing that differs between the two.
        /// </summary>
        public void LoadEtabsData(EtabsDataContainer model, string sourceName)
        {
            if (model == null)
            {
                return;
            }

            frames_e = model.frames ?? new List<FrameData_e>();
            columns = model.columns ?? new List<Column_e>();
            Stories = model.stories ?? new List<Story_E>();
            walls = model.Walls ?? new List<Wall_E>();
            // Written by a build that did not know about slabs, so it may be absent.
            Slabs = model.Slabs ?? new List<Slab_E>();

            // Empty off a saved file, which carries geometry and not loads: the line load check
            // reads the distributed loads from a live model, so a run against a file says it has
            // nothing to check rather than checking against an empty model and reporting every
            // drawn line as a load ETABS has not got.
            LineLoadFrames = model.LineLoadFrames ?? new List<LineLoadFrame_E>();
            EtabsReadsNullLoadAreas = model.ReadsNullLoadAreas;

            EtabsSourceName = sourceName ?? "";
        }

        /// <summary>
        /// The design model side of what is loaded, gathered back into the shape it is saved
        /// in. This is what makes the save available on the Revit side: over there the model
        /// is read straight into these lists and the container it arrived in is not kept, so
        /// there would otherwise be nothing to hand a writer.
        ///
        /// The lists themselves are handed over rather than copied. Nothing writes to them on
        /// the way out, and a copy of a whole building to serialize it once would be a real
        /// cost for no gain.
        /// </summary>
        public EtabsDataContainer ToEtabsData()
        {
            return new EtabsDataContainer
            {
                frames = frames_e,
                columns = columns,
                stories = stories,
                Walls = walls,
                Slabs = slabs,
                LineLoadFrames = lineLoadFrames,
                ReadsNullLoadAreas = EtabsReadsNullLoadAreas
            };
        }

        /// <summary>
        /// Reads the model the given engine is already attached to.
        ///
        /// The engine is the caller's, and it has to be: it carries which program was attached
        /// to and, for a single level model, which Revit level it stands for. Neither can be
        /// worked out again from here.
        /// </summary>
        internal  void getEtabsDataFromEtabs(ref EtabsEngine et)
        {
            try
            {
                Frames_e = getAllDataFromEtabs(ref et);
                EtabsSourceName = et == null ? "" : et.ModelName;
            }
            catch (Exception ex)
            {

                MessageBox.Show("!! error" + ex.Message);
            }

        }

        /// <summary>
        /// Moves every imported Revit element by a transform, for an alignment changed after
        /// the model was read.
        ///
        /// The alignment is baked into each element as it is built, so a new one used to mean
        /// reading the whole model out of Revit again - minutes of it, to move a model a few
        /// metres. What is handed in here is the difference between the alignment the elements
        /// were placed under and the one now in force, and the result is the same geometry the
        /// re-read would have produced. See <see cref="MyTransformation.Difference"/>.
        ///
        /// The stories are not named again here. Which story an element belongs to follows
        /// from where it now stands, and the caller knows whether the ETABS stories it would
        /// be named against are the ones already loaded.
        /// </summary>
        public void MoveRevitBy(MyTransformation move)
        {
            if (move == null)
            {
                return;
            }

            foreach (FrameData_r frame in frames_r)
            {
                frame.MoveBy(move);
            }

            foreach (Column_r column in columns_r)
            {
                column.MoveBy(move);
            }

            foreach (Wall_R wall in walls_r)
            {
                wall.MoveBy(move);
            }

            foreach (Slab_R slab in slabs_r)
            {
                slab.MoveBy(move);
            }

            foreach (SidlCSource_R source in sidlCSources)
            {
                source.MoveBy(move);
            }

            foreach (SidlCAreaSource_R fill in sidlCAreaSources)
            {
                fill.MoveBy(move);
            }
        }

        /// <summary>
        /// Names every imported Revit element after the ETABS story it sits at. Beams are
        /// matched on the elevation they are drawn at, columns on the elevation they rise to,
        /// which is the story ETABS itself names a column after.
        /// </summary>
        public void AssignRevitStories()
        {
            if (stories.Count == 0)
            {
                return;
            }

            foreach (FrameData_r frame in frames_r)
            {
                Story_E nearest = Story_E.getNearestStory(frame.Start.Z, stories);
                if (nearest != null)
                {
                    frame.Story = nearest.Name;
                }
            }

            foreach (Column_r column in columns_r)
            {
                column.AssignStory(stories);
            }

            // A wall is named after the story it rises to, the same as a column: ETABS assigns
            // a wall area to the story nearest its highest corner.
            foreach (Wall_R wall in walls_r)
            {
                wall.AssignStory(stories);
            }

            // A floor is named after the story its top face sits at, which is the plane an
            // ETABS area is drawn on, so the two line up story by story.
            foreach (Slab_R slab in slabs_r)
            {
                Story_E nearest = Story_E.getNearestStory(slab.TopElevation, stories);
                if (nearest != null)
                {
                    slab.Story = nearest.Name;
                }
            }

            // A wall or fill strip weighed as a load is named after the story it stands on,
            // not the one it rises to: its weight is on the frames under it.
            foreach (SidlCSource_R source in sidlCSources)
            {
                source.AssignStory(stories);
            }

            // A fill is named after the story whose slab it sits on.
            foreach (SidlCAreaSource_R fill in sidlCAreaSources)
            {
                fill.AssignStory(stories);
            }
        }

        /// <summary>
        /// Throws away every Revit element that does not stand at the given level, for a run
        /// against a model that holds one floor.
        ///
        /// Needed because <see cref="AssignRevitStories"/> names an element after the nearest
        /// story, and a SAFE model has exactly one. Nearest to the only story is every story,
        /// so without this the whole Revit building - forty floors of beams, columns and walls
        /// - would be named after the one floor SAFE holds and compared against it. Everything
        /// downstream would then be working on a model that does not exist.
        ///
        /// The band is what makes it usable rather than exact. A floor is rarely drawn exactly
        /// on its level: a slab hung below it, a beam dropped for a upstand, a column that
        /// stops short. So the test is how far off the level an element stands rather than
        /// whether it is on it, and how far is a setting - see
        /// <see cref="PrefrencesData.SingleLevelBand"/>.
        ///
        /// Each kind is measured on the same elevation it would be assigned a story by, so an
        /// element kept here is an element that lands on this level, and one dropped could
        /// never have been named after it.
        /// </summary>
        /// <returns>How many elements were left out, by kind, for the import to report.</returns>
        public string NarrowRevitToLevel(double levelElevationMM, double bandMM)
        {
            double band = Math.Abs(bandMM);

            int beams = frames_r.Count, columns = columns_r.Count;
            int walls = walls_r.Count, slabs = slabs_r.Count;

            frames_r = frames_r.Where(frame => Within(frame.Start.Z, levelElevationMM, band)).ToList();
            columns_r = columns_r.Where(column => Within(column.StoryElevationMM, levelElevationMM, band)).ToList();
            walls_r = walls_r.Where(wall => Within(wall.StoryElevationMM, levelElevationMM, band)).ToList();
            slabs_r = slabs_r.Where(slab => Within(slab.TopElevation, levelElevationMM, band)).ToList();
            sidlCSources = sidlCSources.Where(source => Within(source.BaseElevation, levelElevationMM, band)).ToList();
            sidlCAreaSources = sidlCAreaSources.Where(fill => Within(fill.BaseElevation, levelElevationMM, band)).ToList();

            return $"{beams - frames_r.Count} beam(s), {columns - columns_r.Count} column(s), " +
                   $"{walls - walls_r.Count} wall(s) and {slabs - slabs_r.Count} floor(s) stand more than " +
                   $"{Math.Round(band)} mm off this level and were left out.";
        }

        private static bool Within(double elevationMM, double levelElevationMM, double bandMM)
        {
            return Math.Abs(elevationMM - levelElevationMM) <= bandMM;
        }

        private  List<FrameData_e> getAllDataFromEtabs(ref EtabsEngine et)
        {
            IList<FrameData_e> fr;

            // This used to attach for itself, throwing away the engine it had just been handed.
            // That is no longer merely wasteful: the caller's engine is attached to the model
            // the user picked off the list, and attaching again by name takes whichever
            // instance the running object table offers - undoing the choice without saying so,
            // and losing the level a SAFE model was paired with along with it. An engine is
            // required, and no engine is a fault in the caller rather than something to paper
            // over by guessing at a model.
            if (et == null)
            {
                throw new InvalidOperationException(
                    "No design model connection was given. Attach to a model before reading it.");
            }

            EtabsDataContainer dataFromEtabs = et.getEtabsData();
            frames_e = dataFromEtabs.frames;
            columns = dataFromEtabs.columns;
            Stories = dataFromEtabs.stories;
            walls = dataFromEtabs.Walls;
            Slabs = dataFromEtabs.Slabs;
            // Every frame that can carry a line load, null lines and dummies included. Left
            // behind here, a model read live had nothing for the line load check to run on and
            // it asked for the model to be imported again, however often it was.
            LineLoadFrames = dataFromEtabs.LineLoadFrames;
            EtabsReadsNullLoadAreas = dataFromEtabs.ReadsNullLoadAreas;
            fr = dataFromEtabs.frames;
            return fr.ToList<FrameData_e>();
        }
    }
}
