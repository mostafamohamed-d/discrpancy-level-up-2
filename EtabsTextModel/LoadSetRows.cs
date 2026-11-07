namespace EtabsTextModel
{
    /// <summary>
    /// One line of a shell uniform load set definition, as the file writes it:
    ///
    ///   SHELLUNIFORMLOADSET "T1-RESIDENTIAL"  LOADPAT "SIDL"  VALUE 4
    ///
    /// A set is several of these, one per load pattern it puts on. They are handed back in the
    /// order the file has them rather than gathered by set, because gathering is the caller's
    /// business and a table reader's job is to give back the table.
    /// </summary>
    public struct ShellUniformLoadSetRow
    {
        public string Name;
        public string LoadPattern;

        /// <summary>In the file's own force over length squared - see <see cref="ModelUnits"/>.</summary>
        public double Value;
    }

    /// <summary>
    /// Which uniform load set one shell carries, as the file writes it:
    ///
    ///   AREALOAD  "F1"  "L01"  TYPE "UNIFLOADSET"  "T2-BALCONY"
    ///
    /// Note what names the shell: its label and its story, not its unique name. A label is
    /// only unique on its own story - the F1 on L01 and the F1 on L02 are two different areas -
    /// so the pair is the identity, and turning that into the unique name the ETABS API works
    /// in is a job for whoever has the API open.
    /// </summary>
    public struct ShellLoadSetAssignmentRow
    {
        public string Label;
        public string Story;
        public string LoadSet;
    }
}
