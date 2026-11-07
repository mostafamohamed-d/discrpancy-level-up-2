using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace DiscrepancyScanner
{
    [DataContract]
    public class EtabsDataContainer
    {
        [DataMember]
        public List<FrameData_e> frames;
        [DataMember]
        public List<Story_E> stories;
        [DataMember]
        public List<Column_e> columns;

        [DataMember]
        public List<Wall_E> Walls;

        /// <summary>The floor areas, which are the base mesh the slab check works from.</summary>
        [DataMember]
        public List<Slab_E> Slabs;

        /// <summary>
        /// Every frame that could carry a line load, with the distributed loads on it.
        ///
        /// A list of its own rather than the beams in <see cref="frames"/>, and not for tidiness.
        /// The beam import drops frames whose section material is a dummy or "other", and a null
        /// frame drawn along a slab edge to hang a cladding load on is exactly such a frame - so
        /// checking line loads against the imported beams would report every edge load in the
        /// building as sitting on nothing.
        ///
        /// Saved with the rest of the model, and it has to be: most runs are made against a
        /// saved file rather than a live ETABS session, and a load left out of the file is a
        /// check that cannot run at all on one. Left unsaved it silently did nothing on every
        /// run that was not made with ETABS open, which looked exactly like the drawing having
        /// no line loads on it.
        ///
        /// A file written before this existed comes back with none, which reads as a model
        /// whose loads were never imported - true, and worth saying rather than guessing at.
        /// </summary>
        [DataMember]
        public List<LineLoadFrame_E> LineLoadFrames = new List<LineLoadFrame_E>();

        /// <summary>
        /// True when the model was read by a build that keeps the null areas carrying load - see
        /// <see cref="SlabKind.LoadOnly"/>. A file written before then comes back false, and its
        /// None areas are simply not in it: they were thrown away on the way in, before the file
        /// was ever written, so nothing reading it can get them back. The slab load check says so
        /// rather than reporting every slab under one as carrying its own load alone.
        /// </summary>
        [DataMember]
        public bool ReadsNullLoadAreas;
    }
}
