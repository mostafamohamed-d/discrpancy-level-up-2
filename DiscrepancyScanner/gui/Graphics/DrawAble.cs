using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiscrepancyScanner
{
    public class DrawAble:IDrawAble
    {
        public DrawAble(MyPoint start, MyPoint end): base (ComparasonState.Uncategorized,start,end)
        {

        }
    }
}
