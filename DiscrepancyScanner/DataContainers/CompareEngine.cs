using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiscrepancyScanner
{
    [Serializable]
    public class CompareEngine
    {
        EtabsEngine Etabs;

        
        List<FrameData_r> unmatchedRevit = new List<FrameData_r>();
        
        List<FrameData_r> allRevitFrames;
        List<FrameData_e> allEtabsFrames;
        List<FrameData_e> remainingEtabsframes;
        List<PairedFrame> pairedFrames = new List<PairedFrame>();
        double currentLocationTolerance = 500;
        double currentLengthTolerance = 1000;
        double locationTolerance = 500;
        double lengthTolerance = 1000;
        private static CompareEngine compairEngine;
        int maxMergingNumber = 10;
        int maxToleranceIteration = 10;



       
        public List<FrameData_r> UnmatchedRevit { get => unmatchedRevit; }
        public List<PairedFrame> PairedFrames { get => pairedFrames;  }
        public List<FrameData_e> RemainingEtabsframes { get => remainingEtabsframes; }

        struct elementsNearThisetabsFrame
        {
            public FrameData_r revitFrame;
            /// <summary>Where the frame sits in the list it was found in, so ties settle the same way twice.</summary>
            public int revitIndex;
            public double distance;
        }

        /// <summary>One ETABS frame and one Revit frame that the gates allow to be paired.</summary>
        internal struct candidatePair
        {
            public int etabsIndex;
            public int revitIndex;
            public double distance;
        }

        public static CompareEngine GetCompairEngine () // should not use this function at all

        {
            if (compairEngine != null)
            {
                return compairEngine;
            }

            compairEngine = new CompareEngine (500, 1000);
            return compairEngine;
        }
        public static CompareEngine GetCompareEngine ()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            if (compairEngine != null)
            {
               
                compairEngine.lengthTolerance = pref.LengthTolerance;
                compairEngine.locationTolerance = pref.LocationTolerance;
                return compairEngine;
            }
            compairEngine =new CompareEngine(pref.LengthTolerance, pref.LocationTolerance);
            return compairEngine;
        }
        
        private CompareEngine(double LengthTolerance , double LocationTolerance)
        {
            lengthTolerance= LengthTolerance;
            locationTolerance = LocationTolerance;
        }

        /// <summary>
        /// Pairs what is left of the two models at the tolerance this iteration has reached.
        ///
        /// Every pair the gates allow is collected first and then taken closest pair first,
        /// rather than each ETABS frame in turn claiming whatever is nearest to it. Both ways
        /// give a frame its nearest partner; they only part company when two ETABS frames want
        /// the same Revit frame. Walking the list, whichever frame ETABS happened to hand over
        /// first took it and the other went unpaired, so the answer moved with the order the
        /// frames arrived in. Taking the closest pair first gives it to the tighter of the two
        /// and lets the looser one look again, which is the same answer whatever the order.
        ///
        /// This does not make the pairing as a whole the best one there is - a frame can still
        /// be left with nothing because a closer pair took its partner - but it is an answer
        /// the model decides rather than one the list order decides.
        /// </summary>
        public void compair ()
        {
            List<candidatePair> candidates = new List<candidatePair>();

            for (int e = 0; e < remainingEtabsframes.Count; e++)
            {
                foreach (elementsNearThisetabsFrame near in GetAllNearByRevitFrames(unmatchedRevit, remainingEtabsframes[e]))
                {
                    candidatePair candidate;
                    candidate.etabsIndex = e;
                    candidate.revitIndex = near.revitIndex;
                    candidate.distance = near.distance;
                    candidates.Add(candidate);
                }
            }

            bool[] etabsTaken = new bool[remainingEtabsframes.Count];
            bool[] revitTaken = new bool[unmatchedRevit.Count];

            foreach (candidatePair taken in TakeClosestPairsFirst(candidates))
            {
                etabsTaken[taken.etabsIndex] = true;
                revitTaken[taken.revitIndex] = true;
                pairedFrames.Add(new PairedFrame(remainingEtabsframes[taken.etabsIndex],
                                                 unmatchedRevit[taken.revitIndex]));
            }

            // Rebuilt rather than dropped one at a time: both lists are read from by index
            // while the pairs are being made, and taking a few thousand frames out of a list
            // by value costs a walk of the list each time.
            remainingEtabsframes = KeepUntaken(remainingEtabsframes, etabsTaken);
            unmatchedRevit = KeepUntaken(unmatchedRevit, revitTaken);
        }

        /// <summary>
        /// Picks which of the candidate pairs to make, closest pair first, dropping any whose
        /// ETABS or Revit frame has already been taken by a closer one. Every frame ends up in
        /// at most one pair, and which pairs come out does not depend on the order the
        /// candidates were handed over.
        /// </summary>
        internal static List<candidatePair> TakeClosestPairsFirst(List<candidatePair> candidates)
        {
            List<candidatePair> inOrder = new List<candidatePair>(candidates);
            inOrder.Sort(CompareCandidates);

            HashSet<int> etabsTaken = new HashSet<int>();
            HashSet<int> revitTaken = new HashSet<int>();
            List<candidatePair> taken = new List<candidatePair>();

            foreach (candidatePair candidate in inOrder)
            {
                if (etabsTaken.Contains(candidate.etabsIndex) || revitTaken.Contains(candidate.revitIndex))
                {
                    continue;
                }

                etabsTaken.Add(candidate.etabsIndex);
                revitTaken.Add(candidate.revitIndex);
                taken.Add(candidate);
            }

            return taken;
        }

        /// <summary>
        /// Orders candidate pairs by how far apart the two frames are, and anything still
        /// level by where the frames sit in the lists, so a second run over the same model
        /// gives the same pairs. Two candidates exactly the same distance away go to the
        /// earlier frame, which is what <see cref="getTheNearest"/> does on the merged-frame
        /// path as well.
        /// </summary>
        private static int CompareCandidates(candidatePair a, candidatePair b)
        {
            int byDistance = a.distance.CompareTo(b.distance);
            if (byDistance != 0)
            {
                return byDistance;
            }

            int byEtabsFrame = a.etabsIndex.CompareTo(b.etabsIndex);
            if (byEtabsFrame != 0)
            {
                return byEtabsFrame;
            }

            return a.revitIndex.CompareTo(b.revitIndex);
        }

        /// <summary>What was left unpaired, in the order it was already in.</summary>
        private static List<T> KeepUntaken<T>(List<T> items, bool[] taken)
        {
            List<T> left = new List<T>();
            for (int i = 0; i < items.Count; i++)
            {
                if (!taken[i])
                {
                    left.Add(items[i]);
                }
            }
            return left;
        }

        private List<elementsNearThisetabsFrame> GetAllNearByRevitFrames(List<FrameData_r> frameData_Rs, FrameData_e eframe)
        {
           
            List<elementsNearThisetabsFrame> matches = new List<elementsNearThisetabsFrame>();
            for (int i = 0; i < frameData_Rs.Count; i++)
            {
                FrameData_r rFrame = frameData_Rs[i];

                if (isNearPoint(eframe.Mid, rFrame.Mid) && isSameDir(eframe.Direction, rFrame.Direction)
                    && isSameLength(eframe.ApproxLength_r, rFrame.ApproxLength_r))
                {
                    elementsNearThisetabsFrame elementsNear;
                    elementsNear.revitFrame = rFrame;
                    elementsNear.revitIndex = i;
                    elementsNear.distance = eframe.Mid.DistanceTo(rFrame.Mid);
                    matches.Add(elementsNear);
                }
            }

            return matches;

        }

        private bool isSameLength(double l1, double l2)
        {

            if (Math.Abs( l1-l2) < currentLengthTolerance)
            {
                return true;
            }
            return false;
        }

        private FrameData_r getTheNearest(List<elementsNearThisetabsFrame> matches)
        {
            if (matches.Count> 0)
            {
                double nearestDistance = matches[0].distance;
                FrameData_r nearestFrame= matches[0].revitFrame;
                foreach (var frame in matches)
                {
                    // Strictly nearer, so two candidates exactly the same distance away leave
                    // the earlier one standing - and GetAllNearByRevitFrames collects them in
                    // the order they sit in the list it searched, so that is the earlier Revit
                    // frame. ColumnCompareEngine settles a tie the same way. Before this the
                    // last candidate won here, which moved the pairing with the read order.
                    if (frame.distance < nearestDistance )
                    {
                        nearestDistance = frame.distance;
                        nearestFrame = frame.revitFrame;
                    }
                }

                return nearestFrame;    
 
            }

            return null;
            
        }

        internal void startComparison(IList<FrameData_r> frames_r, List<FrameData_e> frames_e, ref EtabsEngine engine)
        {
            allRevitFrames = frames_r.ToList();
            allEtabsFrames = frames_e.ToList();

            inetData(allRevitFrames, allEtabsFrames);
            unmatchedRevit = allRevitFrames;
            remainingEtabsframes = allEtabsFrames;

            
            for (int i = 1; i <= maxToleranceIteration; i++)
            {
                currentLocationTolerance = locationTolerance * i / maxToleranceIteration;
                currentLengthTolerance = lengthTolerance * i / maxToleranceIteration;
                compair();

                deepCompair(ref engine);
            }

            UpdateMEssageForUnmatched();
        }

        private void UpdateMEssageForUnmatched()
        {
            foreach (var item in remainingEtabsframes)
            {
                item.AddMessage(item.MessagePoint, "Un Mached Etabs Frame", MessageType.State);
            }

            foreach (var item in unmatchedRevit)
            {
                item.AddMessage(item.MessagePoint, "Un Mached Revit Frame", MessageType.State);
            }
        }

        private bool isSameDir(MyPoint d1, MyPoint d2)
        {
            d1 = new MyPoint (Math.Abs(d1.X), Math.Abs(d1.Y), Math.Abs(d1.Z));
            d2 = new MyPoint(Math.Abs(d2.X), Math.Abs(d2.Y), Math.Abs(d2.Z));
            if (d1.IsAlmostEqualTo(d2, 0.2))
            { 
                return true;
            }
            return false;
        }

        private bool isNearPoint(MyPoint p1, MyPoint p2)
        {
           
            if (closeTo(p1.X,p2.X)&& closeTo(p1.Y, p2.Y)&& closeTo(p1.Z, p2.Z))
            {
                return true;

            }
            return false;
        }

        public bool closeTo(double x1, double x2)
        {
            
            if ((Math.Abs (x1-x2))<=currentLocationTolerance)
            {
                return true;
            }
            return false;
        }


        private void inetData(IList<FrameData_r> rFrames, IList<FrameData_e> eFrames)
        {
            inetMid( rFrames,  eFrames);
            pairedFrames = new List<PairedFrame>();
          
           
            unmatchedRevit = new List<FrameData_r>();
        }

        private void inetMid(IList<FrameData_r> rFrames, IList<FrameData_e> eFrames)
        {

           
            foreach (var f in eFrames)
            {
               
                f.calcuateDir();
                f.calculateLength();
            }
        }

        //internal IList<PairedFrame> deepCompair(IList<FrameData_e> unpaired_e, IList<FrameData_r> unpaired_r, EtabsEngine engine)
        internal  void deepCompair(ref EtabsEngine engine)
        {
           
            Etabs = engine;
            List<FrameData_e> mergedFrames = new List<FrameData_e>();
            foreach (FrameData_e eframe in remainingEtabsframes)
            {
                if (mergedFrames.Contains(eframe))
                {
                    continue;
                }
                else
                {
                    List<FrameData_e> framesToBeMerged = new List<FrameData_e>();
                    framesToBeMerged.Add(eframe);
                    int iteration = 0;
                    findNextBeam(eframe, iteration, framesToBeMerged, ref mergedFrames);

                }
               
                
            }

            foreach (var frame in mergedFrames)
            {
                remainingEtabsframes.Remove(frame);
            }

        }

        private void findNextBeam(FrameData_e eframe, int iteration, List<FrameData_e> framesToBeMerged, ref List<FrameData_e> mergedFrames)
        {
           
            foreach (Joint j in eframe.Joints)
            {
                FrameData_e extendableFrame = new FrameData_e("");

                if (canExtendFrame(j, eframe, ref extendableFrame))
                {
                    if (framesToBeMerged.Contains(extendableFrame))
                    {
                        continue;
                    }
                    FrameData_e CandidateMergedE_Frame = merge2EtabsFrames(eframe, extendableFrame);
                    if (CandidateMergedE_Frame != null)
                    {
                        framesToBeMerged.Add(extendableFrame);
                        List<elementsNearThisetabsFrame> nearbyRevitFrames = new List<elementsNearThisetabsFrame>();
                        nearbyRevitFrames = GetAllNearByRevitFrames(unmatchedRevit, CandidateMergedE_Frame);
                        FrameData_r NearsetRevitFrame = getTheNearest(nearbyRevitFrames);

                        if (NearsetRevitFrame == null)
                        {
                            if (iteration < maxMergingNumber)
                            {
                                iteration++;
                                findNextBeam(CandidateMergedE_Frame, iteration, framesToBeMerged, ref mergedFrames );
                            }
                          
                        }
                        else
                        {
                            foreach (var frame in framesToBeMerged)
                            {
                                mergedFrames.Add(frame);   
                            }
                            // found Pair in revit
                            unmatchedRevit.Remove(NearsetRevitFrame);
                            pairedFrames.Add(new PairedFrame(CandidateMergedE_Frame, NearsetRevitFrame));
                            break;
                        }

                    }
                }

            }
        }

        private FrameData_e merge2EtabsFrames(FrameData_e baseFrame, FrameData_e extendableFrame)
        {
            FrameData_e mergedFrame = baseFrame.combineWith(extendableFrame);
            if (mergedFrame != null)
            {
                mergedFrame.calculateMid();
                mergedFrame.calcuateDir();
                mergedFrame.calculateLength();
                mergedFrame.ReAdjustStartAndEnd();
                mergedFrame.UpdateRftRequired();
                return mergedFrame;
            }

            return null;
        }

        private bool canExtendFrame(Joint j, FrameData_e eframe ,ref FrameData_e extendableFrame )
        {
          
            List<FrameData_e> connectedFrames = getConnectedElementsFromJoint(j, eframe.etabsUname);
            if (connectedFrames == null)
            {
                return false;
            }
            else
            {
                foreach (var Connectedframe in connectedFrames)
                {
                    if (Connectedframe.Direction.IsAlmostEqualTo(eframe.Direction, 0.2))
                    {
                        extendableFrame = Connectedframe;
                        return true;
                    }

                }
            }
            return false;
        }

        private List<FrameData_e> getConnectedElements(Joint j, string uniquename)
        {
            List<FrameData_e> connectedFrames = new List<FrameData_e>();
            List<string> connectedElementsNames = Etabs.getConnectedframes(j, uniquename);
            if (connectedElementsNames == null)
            {
                return null;
            }
            for (int i = 0; i < connectedElementsNames.Count; i++)
            {
                FrameData_e adjecentFrame = remainingEtabsframes.Find(x => x.etabsUname == connectedElementsNames[i]);
                if (adjecentFrame != null)
                {
                    connectedFrames.Add(adjecentFrame);
                }
                
            }

            return connectedFrames;
                       
        }
        private  List<FrameData_e> getConnectedElementsFromJoint(Joint j, string uniquename)
        {
            List<FrameData_e> connectedFrames = new List<FrameData_e>();
            List<string> connectedElementsNames = j.connectedFrames;
            if (connectedElementsNames == null)
            {
                return null;
            }
            for (int i = 0; i < connectedElementsNames.Count; i++)
            {
                FrameData_e adjecentFrame = remainingEtabsframes.Find(x => x.etabsUname == connectedElementsNames[i]);
                if (adjecentFrame != null)
                {
                    connectedFrames.Add(adjecentFrame);
                }
            }
            return connectedFrames;

        }



        public List<Joint> getJoints(FrameData_e eframe)
        {
             
                
               
            if (Etabs != null)
            {
                return Etabs.getJoints(eframe.etabsUname);
            }
            else if (eframe.Joints != null)
            {
                return eframe.Joints;
            }
            else
            {
                return new List<Joint>();
            }
                

        }
    }
}
