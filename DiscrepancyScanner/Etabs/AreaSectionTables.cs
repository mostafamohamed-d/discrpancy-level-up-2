using System;
using System.Collections.Generic;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Reading the section property assigned to each area out of the model's own tables.
    ///
    /// The API has <c>AreaObj.GetProperty</c>, which answers for one area, and every call is a
    /// COM round trip. The import needs the answer for every area in the model - a wall needs
    /// it for its thickness and a floor needs it to be a floor at all - so on a large model
    /// that is thousands of crossings for something one table read covers.
    ///
    /// Nothing here touches the ETABS API. What arrives is the flat array a table comes back
    /// as, which is the part worth checking, and the columns are found by their own field keys
    /// rather than by counting along the row, so a table that gains a column still reads.
    /// </summary>
    internal static class AreaSectionTables
    {
        /// <summary>
        /// Which property each area carries, by the area's unique name - which is what the rest
        /// of the import knows an area by. The label and the story are in the table too and are
        /// not used: two areas on different stories share a label, and only the unique name is
        /// unique.
        ///
        /// Empty when the columns are not the ones expected, which the caller reads as "this
        /// table was no use" and falls back to asking per area.
        /// </summary>
        public static Dictionary<string, string> Assignments(string[] fieldKeys, string[] data, int records)
        {
            Dictionary<string, string> byArea =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            int areaColumn = LoadSetTables.ColumnOf(fieldKeys, "UniqueName", "Unique Name", "AreaName");

            // Deliberately not "Name": this table names the area as well as its property, and a
            // column called Name could be either. Binding to the wrong one would not fail, it
            // would quietly hand back area names as property names and every wall would then
            // read as having no thickness.
            int propertyColumn = LoadSetTables.ColumnOf(fieldKeys, "SectionProperty", "Section Property",
                                                        "AreaSection", "SectProp", "Section", "Property");

            if (areaColumn < 0 || propertyColumn < 0)
            {
                return byArea;
            }

            foreach (string[] row in LoadSetTables.Rows(fieldKeys, data, records))
            {
                string area = (row[areaColumn] ?? "").Trim();
                string property = (row[propertyColumn] ?? "").Trim();

                if (area.Length > 0 && property.Length > 0)
                {
                    byArea[area] = property;
                }
            }

            return byArea;
        }
    }
}
