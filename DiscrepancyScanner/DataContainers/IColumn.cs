namespace DiscrepancyScanner
{
    /// <summary>
    /// The section / placement data the column comparison and the viewer need,
    /// implemented by both the ETABS and the Revit column containers so they can be
    /// compared and drawn through one contract.
    /// </summary>
    public interface IColumn
    {
        /// <summary>Section dimension across the local 2 axis, in mm. Zero for round sections.</summary>
        double Width { get; }

        /// <summary>Section dimension across the local 3 axis, in mm. Zero for round sections.</summary>
        double Depth { get; }

        /// <summary>Section diameter in mm. Zero for rectangular sections.</summary>
        double Diameter { get; }

        SectionShape Shape { get; }

        string SectionName { get; }

        /// <summary>Rotation of the section local axes measured from global X, in degrees.</summary>
        double AngleDegrees { get; }

        /// <summary>Plan insertion point, in mm, in the shared (ETABS) coordinate system.</summary>
        MyPoint PlanPoint { get; }

        string Story { get; }

        /// <summary>ETABS unique name or Revit element id, used to report the column.</summary>
        string Identifier { get; }
    }
}
