using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace DiscrepancyScanner
{
    [DataContract]
    public class Story_E
    {
        [DataMember]
        string _name;
        [DataMember]
        double _elevation;
        private string v1;
        private double v2;

       

        public Story_E(string name, double elevation, Form1.eLength unit)
        {
            this._name = name;
            this._elevation = FrameData_e.getLengthFromUnit( unit, elevation);
        }

        public string Name { get => _name; set => _name = value; }
        public double Elevation { get => _elevation; set => _elevation = value; }

        [DataMember]
        string _similarTo;

        /// <summary>
        /// The master story ETABS says this one is similar to - a typical floor's levels all
        /// name the one they were built from. Empty for a master story, a story similar to
        /// nothing, and a model saved before this was read.
        /// </summary>
        public string SimilarTo { get => _similarTo ?? ""; set => _similarTo = value; }

        public override string ToString()
        {
            return $"{Name}: ({Elevation})";
        }

        /// <summary>
        /// Name and level for the story picker, with the level in meters. The elevation itself
        /// stays in mm like every other length in the tool; this is only how it is read out.
        /// </summary>
        public string DisplayName
        {
            get { return $"{Name}: {Math.Round(Elevation / 1000d, 3)} m"; }
        }

        internal double DistanceTo(double z)
        {
            return Math.Abs(Elevation - z);
        }
        public static Story_E getNearestStory(double z , List<Story_E> stories)
        {
            if (stories.Count < 1)
            {
                return null;
            }

            Story_E nearestStory = stories[0];
            double distance = nearestStory.DistanceTo(z);
            foreach (var item in stories)
            {
                if (item.DistanceTo(z) <= distance)
                {
                    nearestStory = item;
                    distance = nearestStory.DistanceTo(z);
                }
            }
            return nearestStory;
        }

    }
}
