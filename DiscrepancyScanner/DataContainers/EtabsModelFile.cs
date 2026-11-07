using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace DiscrepancyScanner
{
    /// <summary>
    /// The saved design model: everything read out of ETABS or SAFE, as one text file.
    ///
    /// Written so that a check can be run without the design model open at all - handed to
    /// somebody who has Revit and no ETABS licence, or kept beside the job as what the model
    /// said on the day. Reading it back is the "read from a saved file" import, and what comes
    /// out of it is what the live read would have produced, so nothing downstream can tell
    /// which of the two it got.
    ///
    /// The format is the JSON the ETABS side has always written, kept exactly as it was so
    /// that files saved by older builds still open. Both sides now come through here rather
    /// than each spelling the serializer out for itself, which is what stops the two drifting
    /// apart - they had already begun to, the reader having learnt to cope with a file written
    /// before slabs existed and the writer knowing nothing of it.
    /// </summary>
    public static class EtabsModelFile
    {
        /// <summary>
        /// What the file dialogs offer. Json first because that is what it is; txt as well
        /// because the file has always been described as a text file and a good few of them
        /// have been saved under that name.
        /// </summary>
        public const string FileFilter =
            "Design model file (*.json;*.txt)|*.json;*.txt|All files (*.*)|*.*";

        public const string DefaultExtension = ".json";

        /// <summary>
        /// Writes the model out. Throws if it cannot, which is the caller's to report: the
        /// two callers say it differently and both say it where the user is looking.
        /// </summary>
        public static void Write(string path, EtabsDataContainer model)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("No file name was given to save to.", "path");
            }

            if (model == null)
            {
                throw new ArgumentNullException("model");
            }

            using (MemoryStream stream = new MemoryStream())
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(typeof(EtabsDataContainer));
                serializer.WriteObject(stream, model);
                byte[] bytes = stream.ToArray();
                File.WriteAllText(path, Encoding.UTF8.GetString(bytes, 0, bytes.Length));
            }
        }

        /// <summary>
        /// Reads one back. Never returns null: a file that holds nothing this understands is
        /// a fault rather than an empty model, because an empty model imported silently is a
        /// run that finds every element in the building unpaired and does not say why.
        /// </summary>
        public static EtabsDataContainer Read(string path)
        {
            string text = File.ReadAllText(path);

            EtabsDataContainer model;
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(text)))
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(typeof(EtabsDataContainer));
                model = serializer.ReadObject(stream) as EtabsDataContainer;
            }

            if (model == null)
            {
                throw new InvalidDataException(
                    "\"" + Path.GetFileName(path) + "\" is not a design model file.");
            }

            // Lists a build that did not know about them left out. Filled here rather than at
            // each of the places that read them, so an old file is a whole model from the
            // moment it is opened.
            model.frames = model.frames ?? new List<FrameData_e>();
            model.columns = model.columns ?? new List<Column_e>();
            model.stories = model.stories ?? new List<Story_E>();
            model.Walls = model.Walls ?? new List<Wall_E>();
            model.Slabs = model.Slabs ?? new List<Slab_E>();
            model.LineLoadFrames = model.LineLoadFrames ?? new List<LineLoadFrame_E>();

            // The build that first kept the None areas did not yet write the mark, and a file
            // with one of them in it was plainly written by a build that keeps them.
            model.ReadsNullLoadAreas = model.ReadsNullLoadAreas || model.Slabs.Any(slab => slab != null && slab.IsLoadOnly);

            return model;
        }

        /// <summary>
        /// What is in it, for the line that says the save worked. Counted off the model rather
        /// than off the file so it is the same sentence whichever side wrote it.
        /// </summary>
        public static string Describe(EtabsDataContainer model)
        {
            if (model == null)
            {
                return "nothing";
            }

            // The line loads are counted out loud rather than folded into the beams, because
            // whether a file has them is the one thing that decides if the line load check can
            // run against it at all - and a file saved before they existed has none.
            int lineLoads = model.LineLoadFrames == null
                ? 0
                : model.LineLoadFrames.Sum(frame => frame.Loads.Count);

            return Count(model.frames) + " beams, "
                   + Count(model.columns) + " columns, "
                   + Count(model.Walls) + " wall areas, "
                   + Count(model.Slabs) + " slab areas across "
                   + Count(model.stories) + " levels, "
                   + lineLoads + " line loads";
        }

        private static int Count<T>(List<T> items)
        {
            return items == null ? 0 : items.Count;
        }

        /// <summary>
        /// True when there is anything worth saving. Saving an empty model is not an error but
        /// it is almost always a mistake - the read has not been done yet - so the callers ask
        /// this first and say so instead.
        /// </summary>
        public static bool IsEmpty(EtabsDataContainer model)
        {
            return model == null
                   || (Count(model.frames) == 0 && Count(model.columns) == 0
                       && Count(model.Walls) == 0 && Count(model.Slabs) == 0);
        }
    }
}
