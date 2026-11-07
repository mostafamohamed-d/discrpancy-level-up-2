using System.Runtime.Serialization;

namespace DiscrepancyScanner
{
    [DataContract]
    public enum SectionShape
    {
        Unknown,
        Rectangular,
        Round
    }
}
