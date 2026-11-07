using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DiscrepancyScanner
{
    public class PairedFrame:IDrawAble, IHasResultFields
    {

        public FrameData_e etabsFrame;
        public FrameData_r revitFrame;

        private readonly List<ResultField> resultFields = new List<ResultField>();
        private readonly List<CheckOutcome> outcomes = new List<CheckOutcome>();

        private string etabsUniqueName;
        private long revitID;
        private MyPoint etabsMidPoint;
        private MyPoint revitMidPoint;

        private double E_topLeft;
        private double R_topLeft;
        private double t1_Ratio;

        private double E_topMid;
        private double R_topMid;
        private double t2_Ratio;

        private double E_topRight;
        private double R_topRight;
        private double t3_Ratio;


        private double E_bottomLeft;
        private double R_bottomLeft;
        private double b1_Ratio;

        private double E_bottom;
        private double R_bottom;
        private double b2_Ratio;

        private double E_bottomRight;
        private double R_bottomRight;
        private double b3_Ratio;
        private designState myState = designState.ignore;
        string note;


        private double l_leftRatio;
        private double l_rightRatio;
        private double l_midRatio;




        public string EtabsUniqueName { get => etabsUniqueName;  }
        public long RevitID { get => revitID; }
     
        public double ET_topLeft { get => E_topLeft; set => E_topLeft = value; }
        public double RVT_topLeft { get => R_topLeft; set => R_topLeft = value; }
        public double T1_Ratio { get => t1_Ratio; set => t1_Ratio = value; }
        public double ET_topMid { get => E_topMid; set => E_topMid = value; }
        public double RVT_topMid { get => R_topMid; set => R_topMid = value; }
        public double T2_Ratio { get => t2_Ratio; set => t2_Ratio = value; }
        public double ET_topRight { get => E_topRight; set => E_topRight = value; }
        public double RVT_topRight { get => R_topRight; set => R_topRight = value; }
        public double T3_Ratio { get => t3_Ratio; set => t3_Ratio = value; }
        public double ET_bottomLeft { get => E_bottomLeft; set => E_bottomLeft = value; }
        public double RVT_bottomLeft { get => R_bottomLeft; set => R_bottomLeft = value; }
        public double B1_Ratio { get => b1_Ratio; set => b1_Ratio = value; }
        public double ET_bottom { get => E_bottom; set => E_bottom = value; }
        public double RVT_bottom { get => R_bottom; set => R_bottom = value; }
        public double B2_Ratio { get => b2_Ratio; set => b2_Ratio = value; }
        public double ET_bottomRight { get => E_bottomRight; set => E_bottomRight = value; }
        public double RVT_bottomRight { get => R_bottomRight; set => R_bottomRight = value; }
        public double B3_Ratio { get => b3_Ratio; set => b3_Ratio = value; }

      

        public MyPoint EtabsMidPoint { get => etabsMidPoint; set => etabsMidPoint = value; }
        public MyPoint RevitMidPoint { get => revitMidPoint; set => revitMidPoint = value; }
        public double L_leftRatio { get => l_leftRatio; set => l_leftRatio = value; }
        public double L_rightRatio { get => l_rightRatio; set => l_rightRatio = value; }
        public double L_midRatio { get => l_midRatio; set => l_midRatio = value; }

        private static PrefrencesData pref = null;
        private static int[] linkSizes = { 8, 10, 12, 16, 18, 22, 25, 32, 48, 52 };



        public PairedFrame(FrameData_e etFrame , FrameData_r rFrame ):base(ComparasonState.Matched,etFrame.Start_e,etFrame.END_e)
        {
            pref = PrefrencesData.getPrefrencesData();
            this.Story = etFrame.Story;
            this.etabsFrame = etFrame;
            this.revitFrame = rFrame;
            GeneratePropatiesRequired();
            checkRFT();
            checkConc();
            CollectOutcomes();
            BuildResultFields();

        }

        public IList<ResultField> ResultFields { get => resultFields; }

        /// <summary>
        /// What each thing the beam check compared concluded, one result apiece. The viewer
        /// shows and hides these one at a time, and prints each one's two numbers side by side.
        /// </summary>
        public IList<CheckOutcome> Outcomes { get => outcomes; }

        /// <summary>Note collected by the reinforcement and concrete checks.</summary>
        public string Note { get => note; }

        /// <summary>
        /// Records what the beam check found, one result per question it asked.
        ///
        /// It used to report a single verdict for the whole beam - "Check_Concrete" - which
        /// said that something about it disagreed but never which thing, nor which end of it.
        /// Every question now stands on its own, named after the place it was asked and
        /// carrying the two numbers it compared, so the card says where to go and look the way
        /// the column and wall checks already do.
        /// </summary>
        private void CollectOutcomes()
        {
            outcomes.Clear();

            AddSizeOutcome("Width", "Width", etabsFrame._width, revitFrame._width);
            AddSizeOutcome("Depth", "Depth", etabsFrame._depth, revitFrame._depth);

            if (HasRevitReinforcement)
            {
                AddSteelOutcome("TopLeft", "Top steel, left end",
                    E_topLeft, R_topLeft, t1_Ratio, pref.t1Factor);
                AddSteelOutcome("TopMid", "Top steel, midspan",
                    E_topMid, R_topMid, t2_Ratio, pref.t2Factor);
                AddSteelOutcome("TopRight", "Top steel, right end",
                    E_topRight, R_topRight, t3_Ratio, pref.t3Factor);

                AddSteelOutcome("BotLeft", "Bottom steel, left end",
                    E_bottomLeft, R_bottomLeft, b1_Ratio, pref.b1Factor);
                AddSteelOutcome("BotMid", "Bottom steel, midspan",
                    E_bottom, R_bottom, b2_Ratio, pref.b2Factor);
                AddSteelOutcome("BotRight", "Bottom steel, right end",
                    E_bottomRight, R_bottomRight, b3_Ratio, pref.b3Factor);

                AddSteelOutcome("LinksLeft", "Links, left end",
                    etabsFrame.AsLinksLeft_e, revitFrame.AsLinksLeft_r,
                    l_leftRatio, pref.LinksRatio);
                AddSteelOutcome("LinksMid", "Links, midspan",
                    etabsFrame.AsLinksMid_e, revitFrame.AsLinksMid_r,
                    l_midRatio, pref.LinksRatio);
                AddSteelOutcome("LinksRight", "Links, right end",
                    etabsFrame.AsLinksRight_e, revitFrame.AsLinksRight_r,
                    l_rightRatio, pref.LinksRatio);
            }

            CheckSwitches.RemoveSwitchedOff(outcomes);
        }

        /// <summary>
        /// Whether the Revit model gave any reinforcement at all to compare against.
        ///
        /// The beam parameters carrying it are not being read at the moment - see
        /// <see cref="FrameData_r.handleParameters"/>, where those reads are commented out - so
        /// every provided area is zero and every ratio is NaN. Nine stations all reporting the
        /// same nothing would bury the two questions the check does answer, so it says nothing
        /// about the reinforcement until there is some to say something about.
        /// </summary>
        private bool HasRevitReinforcement
        {
            get
            {
                return R_topLeft > 0 || R_topMid > 0 || R_topRight > 0
                       || R_bottomLeft > 0 || R_bottom > 0 || R_bottomRight > 0
                       || revitFrame.AsLinksLeft_r > 0 || revitFrame.AsLinksMid_r > 0
                       || revitFrame.AsLinksRight_r > 0;
            }
        }

        /// <summary>One of the two section dimensions, judged by the beam size buffer.</summary>
        private void AddSizeOutcome(string token, string label, double etabs, double revit)
        {
            bool matched = SizeMatches(etabs, revit);
            outcomes.Add(new CheckOutcome(CheckKind.Beam,
                matched ? ResultLevel.Passed : ResultLevel.Failed,
                $"{label.ToLower()} {Math.Round(revit)} in Revit vs {Math.Round(etabs)} in ETABS",
                etabs: $"{Math.Round(etabs)}", revit: $"{Math.Round(revit)}",
                label: label, token: token));
        }

        /// <summary>
        /// One reinforcement station: what ETABS asks for there against what Revit provides.
        /// A station with nothing on the Revit side is reported as it stands rather than left
        /// out - the row is the answer to "where exactly", and a station missing from the list
        /// answers nothing - and it keeps the verdict the check gives it, which is a pass.
        /// </summary>
        private void AddSteelOutcome(string token, string label, double etabs, double revit,
                                     double ratio, double limit)
        {
            bool failed = !double.IsNaN(ratio) && ratio > limit;
            outcomes.Add(new CheckOutcome(CheckKind.Beam,
                failed ? ResultLevel.Failed : ResultLevel.Passed,
                SteelSummary(label, etabs, revit, ratio),
                etabs: Steel(etabs), revit: Steel(revit),
                label: label, token: token));
        }

        private static string SteelSummary(string label, double etabs, double revit, double ratio)
        {
            if (double.IsNaN(ratio))
            {
                return $"{label}: nothing in Revit to compare with";
            }
            return $"{label}: ETABS asks for {Steel(etabs)}, Revit has {Steel(revit)} " +
                   $"(x{Math.Round(ratio, 2)})";
        }

        /// <summary>An area of steel in mm2, or a dash where the model gives none.</summary>
        private static string Steel(double area)
        {
            return area > 0 ? $"{Math.Round(area)}" : "-";
        }

        private void BuildResultFields()
        {
            resultFields.Clear();

            bool failed = MyDesignState != designState.ignore;
            resultFields.Add(new ResultField(FrameResultKeys.State, "Check state", MyDesignState.ToString(), failed));
            resultFields.Add(new ResultField(FrameResultKeys.RevitId, "Revit id", revitID.ToString()));
            resultFields.Add(new ResultField(FrameResultKeys.EtabsName, "ETABS unique name", etabsUniqueName));
            resultFields.Add(new ResultField(FrameResultKeys.EtabsLabel, "ETABS beam label", etabsFrame.EtabsLabel));
            resultFields.Add(new ResultField(FrameResultKeys.BeamMark, "Beam Reinf. Mark", revitFrame.BeamMark));
            if (!string.IsNullOrEmpty(revitFrame.SizeSource))
            {
                resultFields.Add(new ResultField(FrameResultKeys.SizeSource, "Revit size from", revitFrame.SizeSource));
            }

            AddRatioField(FrameResultKeys.TopLeft, "Top left ratio", t1_Ratio, pref.t1Factor);
            AddRatioField(FrameResultKeys.TopMid, "Top mid ratio", t2_Ratio, pref.t2Factor);
            AddRatioField(FrameResultKeys.TopRight, "Top right ratio", t3_Ratio, pref.t3Factor);
            AddRatioField(FrameResultKeys.BotLeft, "Bottom left ratio", b1_Ratio, pref.b1Factor);
            AddRatioField(FrameResultKeys.BotMid, "Bottom mid ratio", b2_Ratio, pref.b2Factor);
            AddRatioField(FrameResultKeys.BotRight, "Bottom right ratio", b3_Ratio, pref.b3Factor);
            AddRatioField(FrameResultKeys.LinksLeft, "Links left ratio", l_leftRatio, pref.LinksRatio);
            AddRatioField(FrameResultKeys.LinksMid, "Links mid ratio", l_midRatio, pref.LinksRatio);
            AddRatioField(FrameResultKeys.LinksRight, "Links right ratio", l_rightRatio, pref.LinksRatio);

            var ratios = new List<double> { t1_Ratio, t2_Ratio, t3_Ratio, b1_Ratio, b2_Ratio, b3_Ratio };
            ratios.RemoveAll(r => double.IsNaN(r));
            double maxRatio = ratios.Count > 0 ? ratios.Max() : 0;
            resultFields.Add(ResultField.Number(FrameResultKeys.MaxRatio, "Max PMM ratio", maxRatio, 2, failed));

            // Given as two sides as well as combined: the card prints them under its ETABS and
            // Revit headings, and "R / E" is only readable once you know which way round it is.
            resultFields.Add(new ResultField(FrameResultKeys.Width, "Width",
                $"{revitFrame._width} / {etabsFrame._width}",
                !SizeMatches(etabsFrame._width, revitFrame._width),
                etabs: $"{etabsFrame._width}", revit: $"{revitFrame._width}"));
            resultFields.Add(new ResultField(FrameResultKeys.Depth, "Depth",
                $"{revitFrame._depth} / {etabsFrame._depth}",
                !SizeMatches(etabsFrame._depth, revitFrame._depth),
                etabs: $"{etabsFrame._depth}", revit: $"{revitFrame._depth}"));

            resultFields.Add(new ResultField(FrameResultKeys.Note, "Note", note ?? "", failed));
        }

        private void AddRatioField(string key, string label, double ratio, double limit)
        {
            if (double.IsNaN(ratio))
            {
                resultFields.Add(new ResultField(key, label, "n/a"));
                return;
            }
            resultFields.Add(ResultField.Number(key, label, ratio, 2, ratio > limit));
        }

        private void checkConc()
        {
         
            if (!SizeMatches(etabsFrame._width, revitFrame._width))
            {
                MyDesignState = designState.Check_Concrete;
                note += "check Width, ";
            }

            if (!SizeMatches(etabsFrame._depth, revitFrame._depth))
            {
                MyDesignState = designState.Check_Concrete;
                note += "check Height, ";
            }

        }

        /// <summary>A width or a depth the two models agree on, inside the beam size buffer.</summary>
        private bool SizeMatches(double etabs, double revit)
        {
            return closeTo(etabs, revit, Math.Max(0, pref.BeamSizeTolerance));
        }

        private bool closeTo(double v1, double v2, double tolerance)
        {
            if (Math.Abs(v1-v2)> tolerance) 
            {
                return false;
            }
            return true;
        }

        private void checkRFT()
        {
            if (!double.IsNaN(t1_Ratio) && t1_Ratio > pref.t1Factor)
            {
                MyDesignState = designState.isUnsafe;
                note += "check top left, ";
            }
            if (!double.IsNaN(t2_Ratio) && t2_Ratio > pref.t2Factor)
            {
                MyDesignState = designState.isUnsafe;
                note += "check top mid, ";
            }
            if (!double.IsNaN(t3_Ratio) && t3_Ratio > pref.t3Factor)
            {
                MyDesignState = designState.isUnsafe;
                note += "check top right, ";
            }
            if (!double.IsNaN(b1_Ratio) && b1_Ratio > pref.b1Factor)
            {
                MyDesignState = designState.isUnsafe;
                note += "check bot left, ";
            }
            if (!double.IsNaN(b2_Ratio) && b2_Ratio > pref.b2Factor)
            {
                MyDesignState = designState.isUnsafe;
                note += "check bot mid, ";
            }
            if (!double.IsNaN(b3_Ratio) && b3_Ratio > pref.b3Factor)
            {
                MyDesignState = designState.isUnsafe;
                note += "check bot right, ";
            }
            if (!double.IsNaN(L_leftRatio) && L_leftRatio > pref.LinksRatio)
            {
                MyDesignState = designState.isUnsafe;
                note += "Check Links Left, ";
            }
            if (!double.IsNaN(L_midRatio) && L_midRatio > pref.LinksRatio)
            {
                MyDesignState = designState.isUnsafe;
                note += "Check Links Mid, ";
            }
            if (!double.IsNaN(L_rightRatio) && L_rightRatio > pref.LinksRatio)
            {
                MyDesignState = designState.isUnsafe;
                note += "Check Links Right, ";
            }

        }

        private void GeneratePropatiesRequired()
        {
            AddMessage(MessagePoint, etabsUniqueName, MessageType.EtabsUnquiName);
            AddMessage(MessagePoint, revitID.ToString(), MessageType.RevtID);


            // fillingEtabs Data

             etabsUniqueName = etabsFrame.etabsUname;
             
            
             E_topLeft = etabsFrame.AsTopLeft;
             E_topMid = etabsFrame.AsTopMid;
             E_topRight = etabsFrame.AsTopRight;
             E_bottomLeft = etabsFrame.AsBotLeft;
             E_bottom = etabsFrame.AsBotMid;  
             E_bottomRight = etabsFrame.AsBotRight;

            // fillign revit  Data

            revitID = revitFrame.RevitID;
            R_bottom = revitFrame.AsBotMid;
            R_bottomLeft = revitFrame.AsBotLeft;
            R_bottomRight = revitFrame.AsBotRight;
            R_topMid = revitFrame.AsTopMid;
            R_topLeft = revitFrame.AsTopLeft;
            
            R_topRight = revitFrame.AsTopRight;


            t1_Ratio = calculateRatio (E_topLeft , R_topLeft);
            t2_Ratio = calculateRatio(E_topMid, R_topMid);
            t3_Ratio = calculateRatio(E_topRight, R_topRight);
            b1_Ratio = calculateRatio(E_bottomLeft,R_bottomLeft);
            b2_Ratio = calculateRatio(E_bottom,R_bottom);
            b3_Ratio = calculateRatio(E_bottomRight, R_bottomRight);

            L_leftRatio = calculateRatio(etabsFrame.AsLinksLeft_e, revitFrame.AsLinksLeft_r);
            L_rightRatio = calculateRatio(etabsFrame.AsLinksRight_e, revitFrame.AsLinksRight_r);
            L_midRatio = calculateRatio(etabsFrame.AsLinksMid_e, revitFrame.AsLinksMid_r);

            AddPMMRatioMessage(t1_Ratio, MessageType.PMMRatio1);
            AddPMMRatioMessage(t2_Ratio, MessageType.PMMRatio2); 
            AddPMMRatioMessage(t3_Ratio, MessageType.PMMRatio3);

            AddPMMRatioMessage(b1_Ratio, MessageType.PMMRatio4);
            AddPMMRatioMessage(b2_Ratio, MessageType.PMMRatio5);
            AddPMMRatioMessage(b3_Ratio, MessageType.PMMRatio6);

            var ratios = new List<Double> { t1_Ratio, t2_Ratio, t3_Ratio, b1_Ratio, b2_Ratio, b3_Ratio };
            ratios.RemoveAll(r => double.IsNaN(r));
            double PmmMax = ratios.Count > 0 ? ratios.Max() : 0;

            AddPMMRatioMessage(PmmMax, MessageType.PMMRatioMax);

        }

   
        private void AddPMMRatioMessage(double t1_Ratio, MessageType pMMRatio1)
        {
            AddMessage(MessagePoint, t1_Ratio.ToString(), pMMRatio1);
        }

        private double calculateRatio(double e, double r)
        {
            if (r==0)
            {
                return Double.NaN;
            }
            return Math.Round( e / r,3);
        }

        internal string[] getEtabsRequiredLinksString()
        {
            int numberOfBranches = RevitEngine.getNumberOfBranches(revitFrame.width_r);
            string[] result = new string[3];
            result[0] = getlinksString(etabsFrame.AsLinksLeft_e,revitFrame.linksBarSizeLeft, numberOfBranches);
            result[1] = getlinksString(etabsFrame.AsLinksMid_e, revitFrame.linksBarSizeMid, numberOfBranches);
            result[2] = getlinksString(etabsFrame.AsLinksRight_e, revitFrame.linksBarSizeRight, numberOfBranches);
            return result;
        }
        internal string[] getRevitRequiredLinksString()
        {
            int numberOfBranches = RevitEngine.getNumberOfBranches(revitFrame.width_r);
            string[] result = new string[3];
            result[0] = getlinksString(revitFrame.AsLinksLeft_r, revitFrame.linksBarSizeLeft, numberOfBranches);
            result[1] = getlinksString(revitFrame.AsLinksMid_r, revitFrame.linksBarSizeMid, numberOfBranches);
            result[2] = getlinksString(revitFrame.AsLinksRight_r, revitFrame.linksBarSizeRight, numberOfBranches);
            return result;
        }

        private string getlinksString(double asLinks, int linkBarSize, int numberOfBranches)
        {
            if (asLinks ==0)
            {
                return "Min.";
            }
            string numberOfBranText = adjustNumberOfBranchesText(numberOfBranches);

            double revercedNormalizationFactor = 1;
            if (linkBarSize != 8)
            {
                revercedNormalizationFactor = 1 / TextHandling.getLinksNormalizationFactor(linkBarSize);
            }
           
            double AdjustedAsLinks = asLinks*revercedNormalizationFactor*(1/pref.LinksRatio);
            double BarArea = linkBarSize * linkBarSize * Math.PI / 4;
            double numberOfLinksRequired = AdjustedAsLinks / numberOfBranches / BarArea;


            int spacing;
            if (numberOfLinksRequired<5 && (linkBarSize == 8))
            {
                spacing = 200;
            }
            else
            {
                spacing = getSpacingFromNumberOfLinks(numberOfLinksRequired);
            }
           


            
            if (spacing>200|| spacing<100)
            {
                reAdjustStirrups(asLinks, numberOfBranches, ref spacing, ref linkBarSize);
            }

            string BarSizeIdentifer = getBarSizeIdentifer(linkBarSize);
            return $"{BarSizeIdentifer}{linkBarSize}@{spacing}{numberOfBranText}";

        }

        private void reAdjustStirrups(double asLinks, int numberOfBranches, ref int spacing, ref int linkBarSize)
        {
            if (asLinks ==0)
            {
                return;
            }
            int linkSizeIndex = 0;
            try
            {

                linkSizeIndex = Array.IndexOf( linkSizes, linkBarSize);
            }
            catch (Exception e)
            {
                return;
            }
           

           if(spacing<100)
            {
                if (linkSizeIndex == linkSizes.Length-1)
                {
                    spacing = getNewSpacing(asLinks, numberOfBranches, linkBarSize);
                    linkBarSize = linkSizes[linkSizeIndex] ;
                    return;
                }
                linkSizeIndex++;
                linkBarSize = linkSizes[linkSizeIndex];
                spacing = getNewSpacing(  asLinks, numberOfBranches,  linkBarSize);
                reAdjustStirrups(asLinks, numberOfBranches, ref spacing, ref linkBarSize);

            }
           else if (spacing>200)
            {
                if (linkSizeIndex == 0)
                {
                    spacing = 200;
                    linkBarSize = linkSizes[linkSizeIndex];
                    return;
                }
                linkSizeIndex--;
                linkBarSize = linkSizes[linkSizeIndex];
                spacing = getNewSpacing( asLinks, numberOfBranches,  linkBarSize);

                if (spacing < 100)
                {
                    spacing = 200;
                    linkBarSize = linkSizes[(linkSizeIndex + 1)];
                }
                else
                {
                    reAdjustStirrups(asLinks, numberOfBranches, ref spacing, ref linkBarSize);
                }
                
            }
        }

        private int getNewSpacing( double asLinks, int numberOfBranches, int linkBarSize)
        {
            double revercedNormalizationFactor = 1;
            if (linkBarSize!= 8)
            {
                revercedNormalizationFactor = 1 / TextHandling.getLinksNormalizationFactor(linkBarSize);
            }
            asLinks = asLinks * revercedNormalizationFactor;
            double BarArea = linkBarSize * linkBarSize * Math.PI / 4;
            double numberOfLinksRequired = asLinks / numberOfBranches / BarArea;



            int spacing = getSpacingFromNumberOfLinks(numberOfLinksRequired);
            

            return spacing;
        }

        private int getSpacingFromNumberOfLinks(double numberOfLinksRequired)
        {
            return ((int)(1000 / (numberOfLinksRequired) / 25) * 25);
        }

        private string adjustNumberOfBranchesText(int numberOfBranches)
        {
            if (numberOfBranches == 2)
            {
                return "";
            }
            else
            {
                return $"-({numberOfBranches} Legs)";
            }
        }

        private string getBarSizeIdentifer(int linkBarSize)
        {
            if (linkBarSize > 8)
            {
                return "T";
            }
            return "Y";
        }
    }

    /// <summary>Field keys produced by the beam check, so the viewer can name them.</summary>
    public static class FrameResultKeys
    {
        public const string State = "Frame.State";
        public const string RevitId = "Frame.RevitId";
        public const string EtabsName = "Frame.EtabsName";
        public const string EtabsLabel = "Frame.EtabsLabel";
        public const string BeamMark = "Frame.BeamMark";
        public const string TopLeft = "Frame.TopLeft";
        public const string TopMid = "Frame.TopMid";
        public const string TopRight = "Frame.TopRight";
        public const string BotLeft = "Frame.BotLeft";
        public const string BotMid = "Frame.BotMid";
        public const string BotRight = "Frame.BotRight";
        public const string LinksLeft = "Frame.LinksLeft";
        public const string LinksMid = "Frame.LinksMid";
        public const string LinksRight = "Frame.LinksRight";
        public const string MaxRatio = "Frame.MaxRatio";
        public const string Width = "Frame.Width";
        public const string Depth = "Frame.Depth";
        public const string SizeSource = "Frame.SizeSource";
        public const string Note = "Frame.Note";
    }
}
