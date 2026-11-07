using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EtabsTextModel
{
    /// <summary>
    /// Reads tables straight out of an ETABS model text file - the .$et that sits beside the
    /// .EDB, written whenever the model is saved.
    ///
    /// Why bother, when ETABS will hand a table over through its own API: because it hands it
    /// over one string at a time across a COM boundary. The table saying which shell carries
    /// which load set has a row per shell, and on a real tower that is a hundred and fifty
    /// thousand rows. The same rows come off the disk in a fraction of a second.
    ///
    /// What this is not: it is not the model. The text file is written on save, so it is as old
    /// as the last save, and it may not be there at all. Deciding whether that is good enough
    /// is the caller's business - this reads the file it is given and says plainly what was in
    /// it.
    ///
    /// Nothing here knows about ETABS the program, or about Revit, or about loads. It is a
    /// reader of a file format, it references nothing, and it can be pointed at a 65 MB file in
    /// a test.
    /// </summary>
    public static class EtabsTextFile
    {
        /// <summary>What ETABS calls the file: the model's own name, with this on the end.</summary>
        public const string Extension = ".$et";

        /// <summary>
        /// Where the text file for a model sits - beside it, named after it. Null for a model
        /// that has not been saved anywhere yet, which has no name to go on.
        /// </summary>
        public static string Beside(string modelFilePath)
        {
            if (string.IsNullOrEmpty(modelFilePath))
            {
                return null;
            }

            try
            {
                return Path.ChangeExtension(modelFilePath, Extension);
            }
            catch (ArgumentException)
            {
                // A path with characters Windows will not have. Nothing to go looking for.
                return null;
            }
        }

        /// <summary>Every table this reader knows.</summary>
        public static EtabsTextTables Read(string path)
        {
            return Read(path, EtabsTable.ShellUniformLoadSets
                              | EtabsTable.ShellLoadSetAssignments
                              | EtabsTable.AreaAssigns);
        }

        /// <summary>
        /// The tables asked for, in one walk down the file.
        ///
        /// One walk whatever is asked for: the load set assignments sit at the end of a section
        /// that is most of the file, so a reader that went back to the top per table would pay
        /// for the whole file again each time.
        /// </summary>
        public static EtabsTextTables Read(string path, EtabsTable tables)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("A model text file to read is needed.", "path");
            }

            List<ShellUniformLoadSetRow> loadSets = new List<ShellUniformLoadSetRow>();

            // Sized for a real model rather than grown from four. A tower has a load set on
            // most of its shells, and this is the one list that gets big.
            List<ShellLoadSetAssignmentRow> assignments = new List<ShellLoadSetAssignmentRow>(1024);

            // One row per area in the model, which is the same order of size again.
            List<AreaAssignRow> areaAssigns = new List<AreaAssignRow>(1024);

            // The names on those rows repeat enormously - a whole tower shares a few dozen
            // section names between a hundred and seventy thousand areas - so one string is
            // kept per name rather than one per row.
            StringPool names = new StringPool();

            List<string> issues = new List<string>();
            ModelUnits units = ModelUnits.Unknown;

            Dictionary<string, SectionLine> handlers =
                new Dictionary<string, SectionLine>(StringComparer.OrdinalIgnoreCase);

            // Always read, asked for or not: every number in every other table is in these.
            handlers[Sections.Controls] = line =>
            {
                ModelUnits read = ReadUnits(line);
                if (read != null)
                {
                    units = read;
                }
            };

            if ((tables & EtabsTable.ShellUniformLoadSets) != 0)
            {
                handlers[Sections.ShellUniformLoadSets] = line => ReadLoadSet(line, loadSets, issues);
            }

            if ((tables & EtabsTable.ShellLoadSetAssignments) != 0)
            {
                handlers[Sections.ShellObjectLoads] = line => ReadAssignment(line, assignments, names);
            }

            if ((tables & EtabsTable.AreaAssigns) != 0)
            {
                handlers[Sections.AreaAssigns] = line => ReadAreaAssign(line, areaAssigns, names);
            }

            HashSet<string> found = SectionScan.Run(path, handlers);

            EtabsTable present = EtabsTable.None;

            if (found.Contains(Sections.Controls))
            {
                present |= EtabsTable.Controls;
            }

            if (found.Contains(Sections.ShellUniformLoadSets))
            {
                present |= EtabsTable.ShellUniformLoadSets;
            }

            if (found.Contains(Sections.ShellObjectLoads))
            {
                present |= EtabsTable.ShellLoadSetAssignments;
            }

            if (found.Contains(Sections.AreaAssigns))
            {
                present |= EtabsTable.AreaAssigns;
            }

            return new EtabsTextTables(path, units, present, loadSets, assignments, areaAssigns, issues);
        }

        #region one line of each table

        /// <summary>
        ///   UNITS  "KN"  "M"  "C"
        ///
        /// One line among several in $ CONTROLS, so it is picked out by its first word rather
        /// than by looking for "UNITS" anywhere on the line - a title with the word in it is
        /// not this.
        /// </summary>
        private static ModelUnits ReadUnits(TextLine line)
        {
            if (!line.StartsWith(Units))
            {
                return null;
            }

            string force = line.NextQuoted();
            string length = line.NextQuoted();
            string temperature = line.NextQuoted();

            return string.IsNullOrEmpty(force) || string.IsNullOrEmpty(length)
                ? null
                : new ModelUnits(force, length, temperature);
        }

        /// <summary>
        ///   SHELLUNIFORMLOADSET "T1-RESIDENTIAL"  LOADPAT "SIDL"  VALUE 4
        ///
        /// The pattern and the value are found by the keyword announcing them rather than by
        /// counting along the line, so a version of ETABS that writes another field between
        /// them still reads.
        /// </summary>
        private static void ReadLoadSet(TextLine line, IList<ShellUniformLoadSetRow> into,
                                        IList<string> issues)
        {
            if (!line.StartsWith(ShellUniformLoadSet))
            {
                return;
            }

            string name = line.NextQuoted();
            string pattern = line.SeekPast(LoadPat) ? line.NextQuoted() : null;

            double value = 0;
            bool hasValue = line.SeekPast(Value) && line.NextNumber(out value);

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(pattern) || !hasValue)
            {
                Complain(issues, line);
                return;
            }

            into.Add(new ShellUniformLoadSetRow
            {
                Name = name,
                LoadPattern = pattern,
                Value = value
            });
        }

        /// <summary>
        ///   AREALOAD  "F1"  "L01"  TYPE "UNIFLOADSET"  "T2-BALCONY"
        ///
        /// This is the hot one. It sits in a section holding every load on every shell -
        /// temperature, uniform, and these - which on a large model is most of the file, and
        /// all but a few per cent of those lines are thrown out on the first line of this
        /// method, by a search through the raw bytes, before anything becomes a string.
        ///
        /// The type is then read back and checked rather than trusted from that search, so a
        /// line of another kind that happens to have the word inside a name is not read as one
        /// of these.
        ///
        /// The shell is named by its label and its story. That is not what the ETABS API calls
        /// it, and turning the pair into a unique name is left to whoever has the API open.
        /// </summary>
        private static void ReadAssignment(TextLine line, IList<ShellLoadSetAssignmentRow> into,
                                           StringPool names)
        {
            if (!line.Contains(UniformLoadSetType) || !line.StartsWith(AreaLoad))
            {
                return;
            }

            string label = line.NextQuoted();
            string story = line.NextQuoted();

            if (!line.SeekPast(Type))
            {
                return;
            }

            string type = line.NextQuoted();
            if (!string.Equals(type, UniformLoadSetTypeName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string set = line.NextQuoted();

            if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(story) || string.IsNullOrEmpty(set))
            {
                return;
            }

            into.Add(new ShellLoadSetAssignmentRow
            {
                // The label is different on every row and is not worth pooling. The storey and
                // the set name are two dozen values across a hundred thousand rows.
                Label = label,
                Story = names.Share(story),
                LoadSet = names.Share(set)
            });
        }

        /// <summary>
        ///   AREAASSIGN  "F1"  "L01"  SECTION "PT250-C50"  DIAPH  "D1"  OBJMESHTYPE "NOAUTOMESH" ...
        ///
        /// A row per area in the model, and the one field worth having on each of them is the
        /// section: that is what the API otherwise answers one area at a time.
        ///
        /// Read by walking the line's words rather than by looking for each keyword in it. The
        /// section comes before the pier on the line, so a section named "PIER300-C40" would be
        /// found by a search for "PIER" and read as the pier - and the value it would put there
        /// is a real name, so nothing downstream would notice. Words cannot go wrong that way,
        /// because a name is inside quotes and a quoted string is never a word; and one walk
        /// along the line is cheaper than one per keyword besides.
        /// </summary>
        private static void ReadAreaAssign(TextLine line, IList<AreaAssignRow> into, StringPool names)
        {
            if (!line.StartsWith(AreaAssign))
            {
                return;
            }

            string label = line.NextQuoted();
            string story = line.NextQuoted();

            if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(story))
            {
                return;
            }

            AreaAssignRow row = new AreaAssignRow
            {
                Label = label,
                Story = names.Share(story)
            };

            while (line.NextWord())
            {
                if (line.WordIs(Section))
                {
                    row.Section = names.Share(line.NextQuoted());
                }
                else if (line.WordIs(Pier))
                {
                    row.Pier = names.Share(line.NextQuoted());
                }
                else if (line.WordIs(Spandrel))
                {
                    row.Spandrel = names.Share(line.NextQuoted());
                }
                else if (line.WordIs(Diaphragm))
                {
                    row.Diaphragm = names.Share(line.NextQuoted());
                }
            }

            into.Add(row);
        }

        /// <summary>
        /// A line in a shape this does not know. Said once each up to a handful, because a file
        /// written differently throughout would otherwise say so a hundred thousand times.
        /// </summary>
        private static void Complain(IList<string> issues, TextLine line)
        {
            const int Most = 10;

            if (issues.Count > Most)
            {
                return;
            }

            issues.Add(issues.Count == Most
                ? "More lines of the model text file could not be read; the rest are not listed."
                : "This line of the model text file could not be read: " + line);
        }

        private static readonly byte[] Units = Ascii("UNITS");
        private static readonly byte[] ShellUniformLoadSet = Ascii("SHELLUNIFORMLOADSET");
        private static readonly byte[] LoadPat = Ascii("LOADPAT");
        private static readonly byte[] Value = Ascii("VALUE");
        private static readonly byte[] AreaLoad = Ascii("AREALOAD");
        private static readonly byte[] Type = Ascii("TYPE");
        private static readonly byte[] UniformLoadSetType = Ascii("UNIFLOADSET");
        private static readonly byte[] AreaAssign = Ascii("AREAASSIGN");
        private static readonly byte[] Section = Ascii("SECTION");
        private static readonly byte[] Pier = Ascii("PIER");
        private static readonly byte[] Spandrel = Ascii("SPANDREL");
        private static readonly byte[] Diaphragm = Ascii("DIAPH");

        private const string UniformLoadSetTypeName = "UNIFLOADSET";

        private static byte[] Ascii(string keyword)
        {
            return Encoding.ASCII.GetBytes(keyword);
        }

        #endregion
    }
}
