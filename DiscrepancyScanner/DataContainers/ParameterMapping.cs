using System;
using System.Collections.Generic;

namespace DiscrepancyScanner
{
    /// <summary>Where a mapped parameter is looked for.</summary>
    public enum ParameterScope
    {
        /// <summary>On the type, then on the instance.</summary>
        TypeThenInstance,
        Type,
        Instance
    }

    /// <summary>One parameter the user named for a property: its name and where to look.</summary>
    public sealed class MappedParameter
    {
        public MappedParameter(string name, ParameterScope scope)
        {
            Name = name;
            Scope = scope;
        }

        public string Name { get; }
        public ParameterScope Scope { get; }

        public override string ToString()
        {
            switch (Scope)
            {
                case ParameterScope.Type: return "Type:" + Name;
                case ParameterScope.Instance: return "Instance:" + Name;
                default: return Name;
            }
        }
    }

    /// <summary>
    /// The parameters the user mapped to one property - a column's width, a beam's depth - as
    /// typed in the settings. Nothing is guessed: an empty setting maps nothing, and the
    /// property is then measured off the solid or not read at all.
    ///
    /// "b" looks on the type and then the instance, "Type:b" on the type only and
    /// "Instance:b" on the instance only. Several are tried in the order they are typed,
    /// separated by commas, semicolons or new lines.
    /// </summary>
    public static class ParameterMapping
    {
        private const string TypePrefix = "Type:";
        private const string InstancePrefix = "Instance:";

        public static List<MappedParameter> Parse(string setting)
        {
            List<MappedParameter> mapped = new List<MappedParameter>();

            foreach (string entry in PrefrencesData.SplitParameterNames(setting))
            {
                ParameterScope scope = ParameterScope.TypeThenInstance;
                string name = entry;

                if (entry.StartsWith(TypePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    scope = ParameterScope.Type;
                    name = entry.Substring(TypePrefix.Length).Trim();
                }
                else if (entry.StartsWith(InstancePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    scope = ParameterScope.Instance;
                    name = entry.Substring(InstancePrefix.Length).Trim();
                }

                if (name.Length > 0)
                {
                    mapped.Add(new MappedParameter(name, scope));
                }
            }

            return mapped;
        }
    }

    /// <summary>
    /// Where one dimension of a member came from, for the element card and the import report:
    /// "Type 'b'", "Instance 'h'", "measured", or "not found".
    /// </summary>
    public static class SizeSources
    {
        public const string Measured = "measured from the solid";
        public const string NotFound = "not found";

        public static string Parameter(string name, bool onType)
        {
            return (onType ? "Type '" : "Instance '") + name + "'";
        }
    }
}
