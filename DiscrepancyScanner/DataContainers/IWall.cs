namespace DiscrepancyScanner
{
    /// <summary>
    /// The placement and section data the wall comparison and the viewer need, implemented by
    /// both the ETABS and the Revit wall containers so they can be compared and drawn through
    /// one contract.
    ///
    /// A wall is a centre line and a thickness. Height is not part of it: the two models are
    /// compared story by story on a plan, exactly as the columns are, and what a wall does
    /// between two levels is not a plan question.
    /// </summary>
    public interface IWall : IPlanLine
    {
        /// <summary>Wall thickness in mm.</summary>
        double Thickness { get; }

        /// <summary>The ETABS section property or the Revit wall type, used to report the wall.</summary>
        string SectionName { get; }

        /// <summary>ETABS unique name or Revit element id, used to report the wall.</summary>
        string Identifier { get; }
    }
}
