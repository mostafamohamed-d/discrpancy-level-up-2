namespace EtabsTextModel
{
    /// <summary>
    /// What is assigned to one area, as the file writes it:
    ///
    ///   AREAASSIGN  "F1"  "L01"  SECTION "PT250-C50"  DIAPH  "D1"  OBJMESHTYPE "NOAUTOMESH" ...
    ///
    /// One row per area in the model - a hundred and seventy thousand of them on a real tower -
    /// and the section on each is what the API answers one area at a time.
    ///
    /// Only the four names are read. Everything else on the line is about how the area is
    /// meshed, restrained and stiffness-modified, which is a modelling matter rather than
    /// anything to check a Revit model against, and reading it would cost an allocation an area
    /// for nothing.
    ///
    /// The area is named by its label and its story, the same way <see cref="ShellLoadSetAssignmentRow"/>
    /// names a shell, and turning that into the unique name the ETABS API works in is the same
    /// job.
    /// </summary>
    public struct AreaAssignRow
    {
        public string Label;
        public string Story;

        /// <summary>
        /// The slab, deck or wall property on the area - what ETABS calls its section.
        ///
        /// Handed over as written rather than interpreted. An area that is an opening carries
        /// no real property, and neither of the models this was written against has one, so
        /// what ETABS writes for an opening is not known here and is not guessed at: read the
        /// name and decide.
        /// </summary>
        public string Section;

        /// <summary>The pier the area belongs to, or empty. Walls have one; floors do not.</summary>
        public string Pier;

        /// <summary>The spandrel the area belongs to, or empty.</summary>
        public string Spandrel;

        /// <summary>The diaphragm the area belongs to, or empty.</summary>
        public string Diaphragm;
    }
}
