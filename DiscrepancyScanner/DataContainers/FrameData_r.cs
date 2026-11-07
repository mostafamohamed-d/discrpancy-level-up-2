using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;


namespace DiscrepancyScanner
{
    [DataContract]
    public class FrameData_r : FrameData

    {
        public MyPoint revitStart;
        public MyPoint revitEnd;

        public int linksBarSizeLeft = 0;
        public int linksBarSizeMid = 0;
        public int linksBarSizeRight = 0;

        private string revitName;
        private string beamMark = "";
        private string typeName = "";


        public FrameData_r(long my_revitID, MyPoint start, MyPoint end, double length, MyPoint direction) : base(start, end, length, direction, ComparasonState.RevitUnmatched)
        {
            
            revitStart = start;
            revitEnd = end;

            // The base constructor has already converted feet to mm; now bring the ends into
            // the ETABS coordinate system, rotation included.
            MyTransformation transformation = DataContainer.ModelTransformation;
            setLocationInMM(transformation.ApplyToPointInMM(Start_r),
                            transformation.ApplyToPointInMM(END_r));
            calculateDirectionRevit();

            RevitID = my_revitID;
            AddMessage(new MyMessage(MessagePoint, RevitID.ToString(), MessageType.RevtID));
        }

        /// <summary>
        /// Moves the beam by a transform, for when the alignment changes after it was read.
        /// The ends are already in the shared frame, so what is handed in is the difference
        /// between the two alignments - see <see cref="MyTransformation.Difference"/>.
        /// </summary>
        public void MoveBy(MyTransformation move)
        {
            if (move == null)
            {
                return;
            }

            setLocationInMM(move.ApplyToPointInMM(Start), move.ApplyToPointInMM(End));
            calculateDirectionRevit();
        }

        public string RevitName { get => revitName; set => revitName = value; }

        /// <summary>
        /// The name of the beam's type, which the list of beam types to leave out is matched
        /// against - kept so a check can apply that list again.
        /// </summary>
        public string TypeName { get => typeName; set => typeName = value ?? ""; }

        /// <summary>
        /// Where the width and depth came from - "Width: Type 'b'; Depth: measured from the
        /// solid" - for the element card. Empty for a beam read before this was kept.
        /// </summary>
        [DataMember]
        public string SizeSource { get; set; } = "";

        /// <summary>
        /// The reinforcement mark the beam carries in Revit, read from whichever of the
        /// parameter names in the preferences the family actually uses. Empty when the beam
        /// carries none.
        /// </summary>
        public string BeamMark { get => beamMark; set => beamMark = value ?? ""; }

        internal void handleParameters(RevitEngine.ParameterData[] _parameters)
        {
            try
            {
                width_r = Double.Parse(_parameters[0].Value);
                Depth_r = Double.Parse(_parameters[1].Value);
               // mark = _parameters[2].Value;


            }
            catch (Exception)
            {
                width_r = 0;
                Depth_r = 0;
                mark = "";
               
            }

            //AsBotLeft = TextHandling.getAsFromText(_parameters[3].Value);
            //AsBotMid = TextHandling.getAsFromText(_parameters[4].Value);
            //AsBotRight = TextHandling.getAsFromText(_parameters[5].Value);
            
            //// TODO: make a genral treatment with added rft
            //AsBotMid += AsBotLeft;

            //AsTopLeft = TextHandling.getAsFromText(_parameters[6].Value);
            //AsTopMid = TextHandling.getAsFromText(_parameters[7].Value);
            //AsTopRight = TextHandling.getAsFromText(_parameters[8].Value);

            //AsLinksLeft_r = TextHandling.getAsForLinks(_parameters[9].Value,width: width_r,out linksBarSizeLeft);
            //AsLinksMid_r = TextHandling.getAsForLinks(_parameters[10].Value,width: width_r, out linksBarSizeMid);
            //AsLinksRight_r = TextHandling.getAsForLinks(_parameters[11].Value, width: width_r, out linksBarSizeRight);

        }
    }

}
