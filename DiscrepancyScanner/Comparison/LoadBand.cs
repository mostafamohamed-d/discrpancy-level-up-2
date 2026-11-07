namespace DiscrepancyScanner
{
    /// <summary>
    /// How far what ETABS carries may sit from what is asked for and still be the same load,
    /// below and above separately.
    ///
    /// The line loads off the loading plan take one buffer either way, and that is the
    /// symmetric band. The SIDL_C check does not: a wall's weight modelled a little heavy is on
    /// the safe side and is let through by a good margin, while one modelled light is a load
    /// short and is let through by very little.
    /// </summary>
    public sealed class LoadBand
    {
        public LoadBand(double below, double above)
        {
            Below = below < 0 ? 0 : below;
            Above = above < 0 ? 0 : above;
        }

        /// <summary>The same buffer either side, which is how the line loads off the plan are read.</summary>
        public static LoadBand Symmetric(double buffer)
        {
            return new LoadBand(buffer, buffer);
        }

        /// <summary>How far under what is asked for ETABS may carry, in kN/m.</summary>
        public double Below { get; }

        /// <summary>How far over what is asked for ETABS may carry, in kN/m.</summary>
        public double Above { get; }

        public bool IsSymmetric { get { return Below == Above; } }

        /// <summary>
        /// Whether what ETABS carries is what is asked for, within the band - the edge of it
        /// included. Allowed a hair over it for floating point: a block wall of 26 and a GRC
        /// panel of 5.4 against the 31.6 on the frame is 0.2 apart on paper and
        /// 0.20000000000000284 as doubles, which a buffer of 0.2 turned away.
        /// </summary>
        public bool Agrees(double wanted, double carried)
        {
            return carried >= wanted - Below - 1e-9 && carried <= wanted + Above + 1e-9;
        }

        public override string ToString()
        {
            return IsSymmetric
                ? "±" + LoadSet.Text(Below) + " kN/m"
                : "-" + LoadSet.Text(Below) + " / +" + LoadSet.Text(Above) + " kN/m";
        }
    }
}
