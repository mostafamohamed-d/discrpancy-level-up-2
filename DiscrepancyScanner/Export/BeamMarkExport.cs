using System.Collections.Generic;

namespace DiscrepancyScanner
{
    /// <summary>
    /// The beam name table the viewer exports: one row per beam, with the ETABS unique name
    /// and label of the beam next to the reinforcement mark its Revit counterpart carries.
    ///
    /// Every beam is listed, whatever story it is on and whether or not it was paired, so a
    /// beam missing from the other model is visible in the sheet as a blank half of a row
    /// rather than by its absence.
    /// </summary>
    public static class BeamMarkExport
    {
        public static readonly string[] Header =
        {
            "Story", "Pairing", "ETABS unique name", "ETABS beam label", "Revit id", "Beam Reinf. Mark"
        };

        /// <summary>
        /// Builds the sheet, header row first. Before a comparison has run there is nothing to
        /// pair the two models by, so each beam is listed on its own with the other model's
        /// columns left blank.
        /// </summary>
        public static IList<string[]> BuildRows(DataContainer data, bool hasCompared)
        {
            List<string[]> rows = new List<string[]> { Header };

            if (hasCompared)
            {
                foreach (PairedFrame frame in data.PairedFrames)
                {
                    rows.Add(row(frame.Story, "Paired",
                                 frame.etabsFrame.etabsUname, frame.etabsFrame.EtabsLabel,
                                 frame.RevitID.ToString(), frame.revitFrame.BeamMark));
                }
                foreach (FrameData_e frame in data.UnmatchedEtabs)
                {
                    rows.Add(etabsRow(frame, "ETABS only"));
                }
                foreach (FrameData_r frame in data.UnmatchedRevit)
                {
                    rows.Add(revitRow(frame, "Revit only"));
                }
                return rows;
            }

            foreach (FrameData_e frame in data.Frames_e)
            {
                rows.Add(etabsRow(frame, "Not checked"));
            }
            foreach (FrameData_r frame in data.Frames_r)
            {
                rows.Add(revitRow(frame, "Not checked"));
            }
            return rows;
        }

        private static string[] etabsRow(FrameData_e frame, string pairing)
        {
            return row(frame.Story, pairing, frame.etabsUname, frame.EtabsLabel, "", "");
        }

        private static string[] revitRow(FrameData_r frame, string pairing)
        {
            return row(frame.Story, pairing, "", "", frame.RevitID.ToString(), frame.BeamMark);
        }

        private static string[] row(string story, string pairing, string etabsName, string etabsLabel,
                                    string revitId, string beamMark)
        {
            return new[] { story ?? "", pairing, etabsName ?? "", etabsLabel ?? "", revitId, beamMark ?? "" };
        }
    }
}
