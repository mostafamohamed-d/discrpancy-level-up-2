using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace DiscrepancyScanner
{
    [DataContract]
    public enum MessageType
    {
        EtabsUnquiName,
        EtabsSectionName,
        RevtID,
        RevittypeName,
        PMMRatio1,
        PMMRatio2,
        PMMRatio3,
        PMMRatio4,
        PMMRatio5,
        PMMRatio6,
        PMMRatioMax,
        State,
        Unknown
    }
}
