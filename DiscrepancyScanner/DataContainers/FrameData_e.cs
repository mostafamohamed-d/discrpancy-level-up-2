
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Windows.Forms;
using static DiscrepancyScanner.Form1;

namespace DiscrepancyScanner
{
    [DataContract]
    public class FrameData_e : FrameData

    {

        [DataMember]
        public string etabsSecName;
        [DataMember]
        private string etabsLabel = "";
        [DataMember]
        private SectionShape shape = SectionShape.Rectangular;
        [DataMember]
        private double diameter;
        [DataMember]
        private double[] totalTopSteel = { 0 };
        [DataMember]
        private double[] totalBotSteel = { 0 };
        [DataMember]
        private double[] totalLinks = { 0 };

        [DataMember]
        private double[] steelLocations = { 0 };
        [DataMember]
        private List<string> groups = new List<string>();
        [DataMember]
        public List<Joint> Joints { get; internal set; }

        /// <summary>
        /// The label ETABS shows for this frame, which is the name repeated on every story
        /// rather than the unique name. Empty when ETABS reports none.
        /// </summary>
        public string EtabsLabel
        {
            get => etabsLabel ?? "";
            internal set => etabsLabel = value ?? "";
        }

        /// <summary>Names of the ETABS groups this frame is assigned to.</summary>
        public List<string> Groups
        {
            get => groups ?? (groups = new List<string>());
            internal set => groups = value ?? new List<string>();
        }


        public enum mergeCondittion
        {
            StartToStart,
            StartToEnd,
            EndToStart,
            EndToEnd
        }



        internal double[] TotalTopSteel { get => totalTopSteel; }


        internal double[] TotalBotSteel { get => totalBotSteel; }

        internal double[] SteelLocations { get => steelLocations; }

        public FrameData_e(string etabsID)
        {
            AddMessage(new MyMessage(MessagePoint, etabsID, MessageType.EtabsUnquiName));
            etabsUname = etabsID;
        }

        internal void setTotalTopSteel(double[] _totalTopSteel, Form1.eLength etabsDbUnites)
        {
            totalTopSteel = new double[_totalTopSteel.Length];
            double unitesMultipler = 1;
            unitesMultipler = getLengthFromUnit(etabsDbUnites, unitesMultipler);
            for (int i = 0; i < _totalTopSteel.Length; i++)
            {
                totalTopSteel[i] = Math.Round(_totalTopSteel[i] * unitesMultipler * unitesMultipler);
            }
        }
        internal void setTotalBotSteel(double[] _totalBotSteel, Form1.eLength etabsDbUnites)
        {
            totalBotSteel = new double[_totalBotSteel.Length];
            double unitesMultipler = 1;
            unitesMultipler = getLengthFromUnit(etabsDbUnites, unitesMultipler);
            for (int i = 0; i < _totalBotSteel.Length; i++)
            {
                totalBotSteel[i] = Math.Round(_totalBotSteel[i] * unitesMultipler * unitesMultipler);
            }
        }
        internal void setSteelLocations(double[] _steelLocations, Form1.eLength etabsDbUnites)
        {
            steelLocations = new double[_steelLocations.Length];
            double unitesMultipler = 1;
            unitesMultipler = getLengthFromUnit(etabsDbUnites, unitesMultipler);
            for (int i = 0; i < _steelLocations.Length; i++)
            {
                steelLocations[i] = Math.Round(_steelLocations[i] * unitesMultipler);
            }

        }
        internal void setHeight_e(double v, Form1.eLength etabsDbUnites)
        {
            _depth = getLengthFromUnit(etabsDbUnites, v);
        }
        internal void setRFT_e(double topLeft, double topMid, double topRight, double botLeft, double botMid, double botRight, double asS1, double asS2, double asS3, Form1.eLength etabsDbUnites)
        {
            double unitesMultipler = 1;
            unitesMultipler = getLengthFromUnit(etabsDbUnites, unitesMultipler);

            AsTopLeft = Math.Round(topLeft * unitesMultipler * unitesMultipler);
            AsTopMid = Math.Round(topMid * unitesMultipler * unitesMultipler);
            AsTopRight = Math.Round(topRight * unitesMultipler * unitesMultipler);

            AsBotLeft = Math.Round(botLeft * unitesMultipler * unitesMultipler);
            AsBotMid = Math.Round(botMid * unitesMultipler * unitesMultipler);
            AsBotRight = Math.Round(botRight * unitesMultipler * unitesMultipler);

            AsLinksLeft_e = Math.Round(asS1);
            AsLinksMid_e = Math.Round(asS2);
            AsLinksRight_e = Math.Round(asS3);


        }

        public static double getLengthFromUnit(Form1.eLength uni, double v)
        {
            switch (uni)
            {
                case Form1.eLength.NotApplicable:
                    return v;
                case Form1.eLength.inch:
                    return v * 25.4d;
                case Form1.eLength.ft:
                    return v * 304.8d;
                case Form1.eLength.micron:
                    return v * 0.001;
                case Form1.eLength.mm:
                    return v;
                case Form1.eLength.cm:
                    return v * 10d;
                case Form1.eLength.m:
                    return v * 1000d;
                default:
                    return v;

            }

        }

        internal Column_e asColumn_e()
        {
            Column_e col = new Column_e(this);
            return col;

        }

        internal void calcuateDir()
        {
            Direction = (Start_e - END_e).Normalize();
        }

        public static MyPoint getPointFromEtabs(Form1.eLength uni, double x, double y, double z)
        {
            x = getLengthFromUnit(uni, x);
            y = getLengthFromUnit(uni, y);
            z = getLengthFromUnit(uni, z);
            return new MyPoint(x, y, z);

        }

        internal void setWidth_e(double v, Form1.eLength etabsDbUnites)
        {
            _width = getLengthFromUnit(etabsDbUnites, v);
        }

        /// <summary>Section shape of the ETABS frame property assigned to this frame.</summary>
        public SectionShape Shape { get => shape; internal set => shape = value; }

        /// <summary>Section diameter in mm, only meaningful for a round section.</summary>
        public double Diameter { get => diameter; }

        internal void setDiameter_e(double v, Form1.eLength etabsDbUnites)
        {
            diameter = getLengthFromUnit(etabsDbUnites, v);
            shape = SectionShape.Round;
            // A round section has no distinct width and depth; keep both at the diameter so
            // any code that only understands rectangles still sees the right bounding size.
            _width = diameter;
            _depth = diameter;
        }

        internal void calculateLength()
        {
            _approxLength = Start_e.DistanceTo(END_e);
        }

        internal FrameData_e combineWith(FrameData_e extendableFrame)
        {

            FrameData_e MergedFrame = new FrameData_e($"{this.etabsUname}+{extendableFrame.EtabsUname}");
            MergedFrame.etabsSecName = this.etabsSecName;
            MergedFrame.EtabsLabel = this.EtabsLabel;
            MergedFrame.Story = this.Story;
            this.ReAdjustStartAndEnd();
            extendableFrame.ReAdjustStartAndEnd();

            mergeCondittion mergeType = mergeCondittion.StartToEnd;

            CompareEngine comp = CompareEngine.GetCompairEngine();

            extendableFrame.Joints = comp.getJoints(extendableFrame);
            bool baseIsLeft = this.isOnLeftOf(extendableFrame);
            // Selecting common joint updating start and end 

            if (baseIsLeft)
            {

                if (this.Start_e.DistanceTo(extendableFrame.Start_e) < 10d)
                {
                    // start is the common joint
                    mergeType = mergeCondittion.StartToStart;

                    MergedFrame.Start_e = this.END_e;
                    MergedFrame.END_e = extendableFrame.END_e;
                    MergedFrame.Joints = new List<Joint>();
                    MergedFrame.Joints.Add(this.Joints[1]);
                    MergedFrame.Joints.Add(extendableFrame.Joints[1]);

                }
                else if (this.Start_e.DistanceTo(extendableFrame.END_e) < 10)
                {
                    mergeType = mergeCondittion.StartToEnd;
                    MergedFrame.END_e = extendableFrame.Start_e;
                    MergedFrame.Start_e = this.END_e;

                    MergedFrame.Joints = new List<Joint>();
                    MergedFrame.Joints.Add(this.Joints[1]);
                    MergedFrame.Joints.Add(extendableFrame.Joints[0]);

                }
                else if (this.END_e.DistanceTo(extendableFrame.Start_e) < 10)
                {
                    mergeType = mergeCondittion.EndToStart;
                    MergedFrame.Start_e = this.Start_e;
                    MergedFrame.END_e = extendableFrame.END_e;

                    MergedFrame.Joints = new List<Joint>();
                    MergedFrame.Joints.Add(this.Joints[0]);
                    MergedFrame.Joints.Add(extendableFrame.Joints[1]);

                }
                else if (this.END_e.DistanceTo(extendableFrame.END_e) < 10)
                {
                    mergeType = mergeCondittion.EndToEnd;
                    MergedFrame.Start_e = this.Start_e;
                    MergedFrame.END_e = extendableFrame.Start_e;

                    MergedFrame.Joints = new List<Joint>();
                    MergedFrame.Joints.Add(this.Joints[0]);
                    MergedFrame.Joints.Add(extendableFrame.Joints[0]);


                }

            }

            else

            {

                if (this.Start_e.DistanceTo(extendableFrame.Start_e) < 10d)
                {
                    // start is the common joint
                    mergeType = mergeCondittion.StartToStart;

                    MergedFrame.Start_e = extendableFrame.END_e;
                    MergedFrame.END_e = this.END_e;
                    MergedFrame.Joints = new List<Joint>();
                    MergedFrame.Joints.Add(extendableFrame.Joints[1]);
                    MergedFrame.Joints.Add(this.Joints[1]);

                }
                else if (this.Start_e.DistanceTo(extendableFrame.END_e) < 10)
                {
                    mergeType = mergeCondittion.StartToEnd;
                    MergedFrame.END_e = this.END_e;
                    MergedFrame.Start_e = extendableFrame.Start_e;

                    MergedFrame.Joints = new List<Joint>();
                    MergedFrame.Joints.Add(extendableFrame.Joints[0]);
                    MergedFrame.Joints.Add(this.Joints[1]);

                }
                else if (this.END_e.DistanceTo(extendableFrame.Start_e) < 10)
                {
                    mergeType = mergeCondittion.EndToStart;
                    MergedFrame.Start_e = extendableFrame.END_e;
                    MergedFrame.END_e = this.Start_e;

                    MergedFrame.Joints = new List<Joint>();
                    MergedFrame.Joints.Add(extendableFrame.Joints[1]);
                    MergedFrame.Joints.Add(this.Joints[0]);

                }
                else if (this.END_e.DistanceTo(extendableFrame.END_e) < 10)
                {
                    mergeType = mergeCondittion.EndToEnd;
                    MergedFrame.Start_e = extendableFrame.Start_e;
                    MergedFrame.END_e = this.Start_e;

                    MergedFrame.Joints = new List<Joint>();
                    MergedFrame.Joints.Add(extendableFrame.Joints[0]);
                    MergedFrame.Joints.Add(this.Joints[0]);


                }

            }


            if (MergedFrame.Start_e != null)
            {
                MergedFrame._width = this._width;
                MergedFrame._depth = this._depth;
                combineRFT(this, extendableFrame, ref MergedFrame, mergeType, baseIsLeft);
                return MergedFrame;
            }
            return null;
        }

        private bool isOnLeftOf(FrameData_e extendableFrame)
        {
            this.calculateMid();
            extendableFrame.calculateMid();

            double myAngle = Math.Atan2(Math.Abs(this.Direction.Y), Math.Abs(Direction.X)); // angle in radians using only +ve values for cheching

            bool isOnLeft = false;

            if (myAngle >= 1.4 && myAngle <= 1.75)
            {
                // the bottom value is the start >> readjust for thet

                // if the existing start is the smaller no change required

                if (this.Mid.Y < extendableFrame.Mid.Y)
                {
                    isOnLeft = true;
                }
                else
                {

                    isOnLeft = false;
                }
            }
            else
            {
                // the left value is the start readjust for that 

                // if the existing start is the smaller value > no change required

                if (this.Mid.X < extendableFrame.Mid.X)
                {
                    isOnLeft = true;
                }
                else
                {
                    isOnLeft = false;
                }


            }

            return isOnLeft;


        }

        internal void setTotalLinks(double[] vmajor)
        {
            totalLinks = vmajor;
        }

        private void combineRFT(FrameData_e baseFrame, FrameData_e extendedFrame, ref FrameData_e resultedMergedFrame, mergeCondittion mergingType, bool baseIsLeft)
        {
            double[] mergedAsLocations;
            double[] mergedTopAs;
            double[] mergedBotAs;
            double[] mergedLinks;

            if (baseIsLeft)
            {
                switch (mergingType)
                {
                    case mergeCondittion.StartToStart:
                        mergedBotAs = merge2Arays(reverceCopyArray(baseFrame.TotalBotSteel), extendedFrame.TotalBotSteel);
                        mergedLinks = merge2Arays(reverceCopyArray(baseFrame.totalLinks), extendedFrame.totalLinks);
                        mergedTopAs = merge2Arays(reverceCopyArray(baseFrame.TotalTopSteel), extendedFrame.TotalTopSteel);
                        mergedAsLocations = mergeLocationsArray(baseFrame.SteelLocations, extendedFrame.SteelLocations);
                        break;

                    case mergeCondittion.StartToEnd:
                        mergedBotAs = merge2Arays(reverceCopyArray(baseFrame.TotalBotSteel), reverceCopyArray(extendedFrame.TotalBotSteel));
                        mergedLinks = merge2Arays(reverceCopyArray(baseFrame.totalLinks), reverceCopyArray(extendedFrame.totalLinks));
                        mergedTopAs = merge2Arays(reverceCopyArray(baseFrame.TotalTopSteel), reverceCopyArray(extendedFrame.TotalTopSteel));
                        mergedAsLocations = mergeLocationsArray(baseFrame.SteelLocations, extendedFrame.SteelLocations);
                        break;

                    case mergeCondittion.EndToStart:
                        mergedBotAs = merge2Arays(baseFrame.TotalBotSteel, extendedFrame.TotalBotSteel);
                        mergedLinks = merge2Arays(baseFrame.totalLinks, extendedFrame.totalLinks);
                        mergedTopAs = merge2Arays(baseFrame.TotalTopSteel, extendedFrame.TotalTopSteel);
                        mergedAsLocations = mergeLocationsArray(baseFrame.SteelLocations, extendedFrame.SteelLocations);
                        break;

                    case mergeCondittion.EndToEnd:
                        mergedBotAs = merge2Arays(baseFrame.TotalBotSteel, reverceCopyArray(extendedFrame.TotalBotSteel));
                        mergedLinks = merge2Arays(baseFrame.totalLinks, reverceCopyArray(extendedFrame.totalLinks));
                        mergedTopAs = merge2Arays(baseFrame.TotalTopSteel, reverceCopyArray(extendedFrame.TotalTopSteel));
                        mergedAsLocations = mergeLocationsArray(baseFrame.SteelLocations, extendedFrame.SteelLocations);
                        break;
                    default:
                        mergedBotAs = merge2Arays(baseFrame.TotalBotSteel, extendedFrame.TotalBotSteel);
                        mergedTopAs = merge2Arays(baseFrame.TotalTopSteel, extendedFrame.TotalTopSteel);
                        mergedLinks = merge2Arays(baseFrame.totalLinks, extendedFrame.totalLinks);
                        mergedAsLocations = mergeLocationsArray(baseFrame.SteelLocations, extendedFrame.SteelLocations);
                        break;
                }

            }
            else
            {
                switch (mergingType)
                {
                    case mergeCondittion.StartToStart:
                        mergedBotAs = merge2Arays(reverceCopyArray(extendedFrame.TotalBotSteel), (baseFrame.TotalBotSteel));
                        mergedLinks = merge2Arays(reverceCopyArray(extendedFrame.totalLinks), (baseFrame.totalLinks));
                        mergedTopAs = merge2Arays(reverceCopyArray(extendedFrame.TotalTopSteel), (baseFrame.TotalTopSteel));
                        mergedAsLocations = mergeLocationsArray(extendedFrame.SteelLocations, baseFrame.SteelLocations);
                        break;

                    case mergeCondittion.StartToEnd:
                        mergedBotAs = merge2Arays(extendedFrame.TotalBotSteel, baseFrame.TotalBotSteel);
                        mergedLinks= merge2Arays(extendedFrame.totalLinks, baseFrame.totalLinks);
                        mergedTopAs = merge2Arays(extendedFrame.TotalTopSteel, baseFrame.TotalTopSteel);
                        mergedAsLocations = mergeLocationsArray(extendedFrame.SteelLocations, baseFrame.SteelLocations);
                        break;

                    case mergeCondittion.EndToStart:
                        mergedBotAs = merge2Arays(reverceCopyArray(extendedFrame.TotalBotSteel), reverceCopyArray(baseFrame.TotalBotSteel));
                        mergedLinks = merge2Arays(reverceCopyArray(extendedFrame.totalLinks), reverceCopyArray(baseFrame.totalLinks));
                        mergedTopAs = merge2Arays(reverceCopyArray(extendedFrame.TotalTopSteel), reverceCopyArray(baseFrame.TotalTopSteel));
                        mergedAsLocations = mergeLocationsArray(extendedFrame.SteelLocations, baseFrame.SteelLocations);
                        break;

                    case mergeCondittion.EndToEnd:
                        mergedLinks = merge2Arays(extendedFrame.totalLinks, reverceCopyArray(baseFrame.totalLinks));
                        mergedBotAs = merge2Arays(extendedFrame.TotalBotSteel, reverceCopyArray(baseFrame.TotalBotSteel));
                        mergedTopAs = merge2Arays(extendedFrame.TotalTopSteel, reverceCopyArray(baseFrame.TotalTopSteel));
                        mergedAsLocations = mergeLocationsArray(extendedFrame.SteelLocations, baseFrame.SteelLocations);
                        break;
                    default:
                        mergedLinks = merge2Arays(extendedFrame.totalLinks, baseFrame.totalLinks);
                        mergedBotAs = merge2Arays(extendedFrame.TotalBotSteel, baseFrame.TotalBotSteel);
                        mergedTopAs = merge2Arays(extendedFrame.TotalTopSteel, baseFrame.TotalTopSteel);
                        mergedAsLocations = mergeLocationsArray(extendedFrame.SteelLocations, baseFrame.SteelLocations);
                        break;
                }


            }



            resultedMergedFrame.ReAdjustStartAndEnd();
            resultedMergedFrame.steelLocations = mergedAsLocations;
            resultedMergedFrame.totalTopSteel = mergedTopAs;
            resultedMergedFrame.totalBotSteel = mergedBotAs;
            resultedMergedFrame.totalLinks =mergedLinks;


        }

        /// <summary>
        /// A force in the given unit, in newtons. Takes a double rather than an int so an
        /// area load - a force over a length squared - can be converted through it as well.
        /// </summary>
        internal static double getForcefromUnit(Form1.eForce eForceUnit, double v)
        {
            switch (eForceUnit)
            {
                case eForce.NotApplicable:
                    return v * 1;

                case eForce.lb:
                    return v * 4.4482189159;
                case eForce.kip:
                    return v * 4448.2216;

                case eForce.N:
                    return v;

                case eForce.kN:
                    return v * 1000;
                case eForce.kgf:
                    return 9.80665 * v;

                case eForce.tonf:
                    return 9806.65 * v;

                default:
                    return v;
            }
        }

        internal void UpdateRftRequired()
        {

            double asT1 = EtabsEngine.getMaxAs(EtabsEngine.asLocation.left, this.TotalTopSteel, this.SteelLocations);
            double asT2 = EtabsEngine.getMaxAs(EtabsEngine.asLocation.mid, this.TotalTopSteel, this.SteelLocations);
            double asT3 = EtabsEngine.getMaxAs(EtabsEngine.asLocation.right, this.TotalTopSteel, this.SteelLocations);
            double asb1 = EtabsEngine.getMaxAs(EtabsEngine.asLocation.left, this.totalBotSteel, this.steelLocations);
            double asb2 = EtabsEngine.getMaxAs(EtabsEngine.asLocation.mid, this.totalBotSteel, this.steelLocations);
            double asb3 = EtabsEngine.getMaxAs(EtabsEngine.asLocation.right, this.totalBotSteel, this.steelLocations);

            double asS1 = EtabsEngine.getMaxAs(EtabsEngine.asLocation.left, this.totalLinks, this.steelLocations);
            double asS2 = EtabsEngine.getMaxAs(EtabsEngine.asLocation.mid, this.totalLinks, this.steelLocations);
            double asS3 = EtabsEngine.getMaxAs(EtabsEngine.asLocation.right, this.totalLinks, this.steelLocations);

            this.setRFT_e(asT1, asT2, asT3, asb1, asb2, asb3, asS1, asS2, asS3, Form1.eLength.mm);
        }

        private double[] reverceCopyArray(double[] _arr)
        {
            double[] arrayToBeRev = new double[_arr.Length];
            Array.Copy(_arr, arrayToBeRev, _arr.Length);

            Array.Reverse(arrayToBeRev);
            return arrayToBeRev;
        }

        private double[] mergeLocationsArray(double[] baseSteelLocationsArr, double[] steelLocations2)
        {
            double[] updated2ndArray = new double[steelLocations2.Length];
            Array.Copy(steelLocations2, updated2ndArray, steelLocations2.Length);
            double inetialValue = baseSteelLocationsArr[baseSteelLocationsArr.Length - 1];
            for (int i = 0; i < updated2ndArray.Length; i++)
            {
                updated2ndArray[i] += inetialValue;
            }

            return merge2Arays(baseSteelLocationsArr, updated2ndArray);


        }

        private double[] merge2Arays(double[] _arr1, double[] _arr2)
        {
            int totalSize = _arr1.Length + _arr2.Length;
            double[] MergedArray = new double[totalSize];
            for (int i = 0; i < _arr1.Length; i++)
            {
                MergedArray[i] = _arr1[i];
            }
            for (int i = _arr1.Length; i < (totalSize); i++)
            {
                MergedArray[i] = _arr2[i - _arr1.Length];
            }

            return MergedArray;
        }


        internal void ReAdjustStartAndEnd()
        {
            this.calcuateDir(); // recalculating direction to insure that it is updated.
            double angle = Math.Atan2(Math.Abs(this.Direction.Y), Math.Abs(Direction.X)); // angle in radians using only +ve values for cheching
            if (angle >= 1.4 && angle <= 1.75)
            {
                // the bottom value is the start >> readjust for thet

                // if the existing start is the smaller no change required

                if (Start_e.Y < END_e.Y)
                {
                    // no chage
                }
                else
                {
                    // reverce every thing !!!
                    reverseBeamData();
                }
            }
            else
            {
                // the left value is the start readjust for that 

                // if the existing start is the smaller value > no change required

                if (Start_e.X < END_e.X)
                {
                    // no change required
                }
                else
                {
                    reverseBeamData();
                }


            }
        }

        private void reverseBeamData()
        {
            // Swaping Start And End
            var tempLocation = Start_e;
            Start_e = END_e;
            END_e = tempLocation;

            this.Joints.Reverse();

            totalTopSteel = reverceCopyArray(totalTopSteel);
            totalBotSteel = reverceCopyArray(totalBotSteel);
            totalLinks = reverceCopyArray(totalLinks);
            steelLocations = reverceLocationsArray(SteelLocations);

            UpdateRftRequired();
        }

        private double[] reverceLocationsArray(double[] steelLocations)
        {
            calculateLength(); // recalculating length for concurancy;
            double[] revercedArray = new double[steelLocations.Length];
            for (int i = 0; i < steelLocations.Length - 1; i++)
            {
                revercedArray[i] = this._approxLength - steelLocations[i];
                if (revercedArray[i] < 0.1) // location cant be -ve
                {
                    return steelLocations;
                }

            }
            Array.Reverse(revercedArray);
            return revercedArray;
        }
    }
}

