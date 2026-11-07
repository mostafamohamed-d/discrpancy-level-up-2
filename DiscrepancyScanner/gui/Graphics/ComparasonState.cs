using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace DiscrepancyScanner
{
    [DataContract]
    public enum ComparasonState
    {
        Matched,
        EtabsUnmatched,
        RevitUnmatched,
        Uncategorized
            

    }
    
}
