namespace EtabsTextModel
{
    /// <summary>
    /// The units the whole file is written in, off the one UNITS line in $ CONTROLS:
    ///
    ///   UNITS  "KN"  "M"  "C"
    ///
    /// Every number in the file is in these, and there is no second set anywhere - unlike the
    /// database tables, which hand their numbers over in whatever the model is being shown in
    /// at the time and say so per column.
    ///
    /// They are handed back as they were written rather than converted. What a kilonewton per
    /// square metre is worth is a question about loads, and this is a reader of a text file;
    /// keeping the conversion out of here is what lets the whole project stand on its own with
    /// nothing to reference.
    /// </summary>
    public sealed class ModelUnits
    {
        internal ModelUnits(string force, string length, string temperature)
        {
            Force = force ?? "";
            Length = length ?? "";
            Temperature = temperature ?? "";
        }

        /// <summary>As written: "KN", "kip", "tonf", and so on.</summary>
        public string Force { get; }

        /// <summary>As written: "M", "mm", "ft", and so on.</summary>
        public string Length { get; }

        public string Temperature { get; }

        /// <summary>
        /// False for a file with no UNITS line the reader could make out, which means every
        /// number read out of it is a number with no scale and cannot be used for anything.
        /// </summary>
        public bool AreKnown
        {
            get { return Force.Length > 0 && Length.Length > 0; }
        }

        public override string ToString()
        {
            return AreKnown ? Force + ", " + Length : "(not given)";
        }

        internal static readonly ModelUnits Unknown = new ModelUnits("", "", "");
    }
}
