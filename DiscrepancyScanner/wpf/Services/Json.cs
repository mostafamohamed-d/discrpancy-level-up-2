using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// The smallest json reader and writer that will carry a settings file: objects, arrays,
    /// strings, numbers, booleans and null, nested as deep as the file goes.
    ///
    /// Hand written rather than taken off a package feed because this assembly is loaded into
    /// Revit's own process, where a second copy of a common serializer is a version clash
    /// waiting to happen. Nothing here is on a hot path - it runs twice per session.
    /// </summary>
    internal static class Json
    {
        #region writing

        /// <summary>
        /// Renders an object as indented json. Values may be a string, a bool, any of the
        /// numeric types, a nested <see cref="IDictionary{TKey,TValue}"/>, a sequence, or null.
        /// </summary>
        public static string Write(IDictionary<string, object> root)
        {
            StringBuilder text = new StringBuilder();
            WriteValue(text, root, 0);
            text.Append(Environment.NewLine);
            return text.ToString();
        }

        private static void WriteValue(StringBuilder text, object value, int depth)
        {
            if (value == null)
            {
                text.Append("null");
                return;
            }

            IDictionary<string, object> map = value as IDictionary<string, object>;
            if (map != null)
            {
                WriteObject(text, map, depth);
                return;
            }

            if (value is string)
            {
                WriteString(text, (string)value);
                return;
            }

            if (value is bool)
            {
                text.Append((bool)value ? "true" : "false");
                return;
            }

            if (value is double || value is float || value is decimal ||
                value is int || value is long || value is short || value is byte)
            {
                text.Append(Convert.ToDouble(value, CultureInfo.InvariantCulture)
                                   .ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            IEnumerable items = value as IEnumerable;
            if (items != null)
            {
                WriteArray(text, items, depth);
                return;
            }

            // Anything else is written as its text, which is all a settings file ever needs.
            WriteString(text, Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static void WriteObject(StringBuilder text, IDictionary<string, object> map, int depth)
        {
            if (map.Count == 0)
            {
                text.Append("{}");
                return;
            }

            text.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, object> entry in map)
            {
                if (!first)
                {
                    text.Append(',');
                }
                first = false;

                text.Append(Environment.NewLine);
                Indent(text, depth + 1);
                WriteString(text, entry.Key);
                text.Append(": ");
                WriteValue(text, entry.Value, depth + 1);
            }

            text.Append(Environment.NewLine);
            Indent(text, depth);
            text.Append('}');
        }

        private static void WriteArray(StringBuilder text, IEnumerable items, int depth)
        {
            List<object> values = new List<object>();
            foreach (object item in items)
            {
                values.Add(item);
            }

            if (values.Count == 0)
            {
                text.Append("[]");
                return;
            }

            text.Append('[');
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0)
                {
                    text.Append(',');
                }

                text.Append(Environment.NewLine);
                Indent(text, depth + 1);
                WriteValue(text, values[i], depth + 1);
            }

            text.Append(Environment.NewLine);
            Indent(text, depth);
            text.Append(']');
        }

        private static void Indent(StringBuilder text, int depth)
        {
            text.Append(' ', depth * 2);
        }

        private static void WriteString(StringBuilder text, string value)
        {
            text.Append('"');
            foreach (char c in value ?? string.Empty)
            {
                switch (c)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\b': text.Append("\\b"); break;
                    case '\f': text.Append("\\f"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            // Everything above the control range goes out as it is; the file is
                            // written utf-8, so a diameter sign stays a diameter sign.
                            text.Append(c);
                        }
                        break;
                }
            }
            text.Append('"');
        }

        #endregion

        #region reading

        /// <summary>
        /// Parses a json object. Anything malformed gives an empty map rather than an
        /// exception, because a settings file that cannot be read is not worth stopping over -
        /// the defaults are right there.
        /// </summary>
        public static Dictionary<string, object> Read(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new Dictionary<string, object>();
            }

            try
            {
                int at = 0;
                SkipSpace(text, ref at);
                object value = ReadValue(text, ref at);
                return value as Dictionary<string, object> ?? new Dictionary<string, object>();
            }
            catch (Exception)
            {
                return new Dictionary<string, object>();
            }
        }

        private static object ReadValue(string text, ref int at)
        {
            SkipSpace(text, ref at);
            if (at >= text.Length)
            {
                throw new FormatException("ran off the end of the file");
            }

            char c = text[at];
            switch (c)
            {
                case '{': return ReadObject(text, ref at);
                case '[': return ReadArray(text, ref at);
                case '"': return ReadString(text, ref at);
                case 't': Expect(text, ref at, "true"); return true;
                case 'f': Expect(text, ref at, "false"); return false;
                case 'n': Expect(text, ref at, "null"); return null;
                default: return ReadNumber(text, ref at);
            }
        }

        private static Dictionary<string, object> ReadObject(string text, ref int at)
        {
            Dictionary<string, object> map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            at++; // past the brace
            SkipSpace(text, ref at);

            if (at < text.Length && text[at] == '}')
            {
                at++;
                return map;
            }

            while (true)
            {
                SkipSpace(text, ref at);
                string key = ReadString(text, ref at);
                SkipSpace(text, ref at);

                if (at >= text.Length || text[at] != ':')
                {
                    throw new FormatException("expected a colon after " + key);
                }
                at++;

                map[key] = ReadValue(text, ref at);
                SkipSpace(text, ref at);

                if (at >= text.Length)
                {
                    throw new FormatException("unterminated object");
                }
                if (text[at] == ',')
                {
                    at++;
                    continue;
                }
                if (text[at] == '}')
                {
                    at++;
                    return map;
                }
                throw new FormatException("expected a comma or a closing brace");
            }
        }

        private static List<object> ReadArray(string text, ref int at)
        {
            List<object> values = new List<object>();
            at++; // past the bracket
            SkipSpace(text, ref at);

            if (at < text.Length && text[at] == ']')
            {
                at++;
                return values;
            }

            while (true)
            {
                values.Add(ReadValue(text, ref at));
                SkipSpace(text, ref at);

                if (at >= text.Length)
                {
                    throw new FormatException("unterminated array");
                }
                if (text[at] == ',')
                {
                    at++;
                    continue;
                }
                if (text[at] == ']')
                {
                    at++;
                    return values;
                }
                throw new FormatException("expected a comma or a closing bracket");
            }
        }

        private static string ReadString(string text, ref int at)
        {
            if (at >= text.Length || text[at] != '"')
            {
                throw new FormatException("expected a string");
            }
            at++;

            StringBuilder value = new StringBuilder();
            while (at < text.Length)
            {
                char c = text[at++];
                if (c == '"')
                {
                    return value.ToString();
                }

                if (c != '\\')
                {
                    value.Append(c);
                    continue;
                }

                if (at >= text.Length)
                {
                    break;
                }

                char escape = text[at++];
                switch (escape)
                {
                    case '"': value.Append('"'); break;
                    case '\\': value.Append('\\'); break;
                    case '/': value.Append('/'); break;
                    case 'b': value.Append('\b'); break;
                    case 'f': value.Append('\f'); break;
                    case 'n': value.Append('\n'); break;
                    case 'r': value.Append('\r'); break;
                    case 't': value.Append('\t'); break;
                    case 'u':
                        if (at + 4 > text.Length)
                        {
                            throw new FormatException("truncated escape");
                        }
                        value.Append((char)int.Parse(text.Substring(at, 4), NumberStyles.HexNumber,
                                                     CultureInfo.InvariantCulture));
                        at += 4;
                        break;
                    default:
                        throw new FormatException("unknown escape \\" + escape);
                }
            }

            throw new FormatException("unterminated string");
        }

        private static double ReadNumber(string text, ref int at)
        {
            int start = at;
            while (at < text.Length && "+-.eE0123456789".IndexOf(text[at]) >= 0)
            {
                at++;
            }

            double number;
            if (at == start || !double.TryParse(text.Substring(start, at - start), NumberStyles.Float,
                                                CultureInfo.InvariantCulture, out number))
            {
                throw new FormatException("expected a number");
            }
            return number;
        }

        private static void Expect(string text, ref int at, string word)
        {
            if (at + word.Length > text.Length ||
                string.CompareOrdinal(text, at, word, 0, word.Length) != 0)
            {
                throw new FormatException("expected " + word);
            }
            at += word.Length;
        }

        private static void SkipSpace(string text, ref int at)
        {
            while (at < text.Length && char.IsWhiteSpace(text[at]))
            {
                at++;
            }
        }

        #endregion

        #region typed lookups

        /// <summary>The nested object under <paramref name="key"/>, or an empty one.</summary>
        public static Dictionary<string, object> Branch(IDictionary<string, object> map, string key)
        {
            object value;
            if (map != null && map.TryGetValue(key, out value))
            {
                Dictionary<string, object> branch = value as Dictionary<string, object>;
                if (branch != null)
                {
                    return branch;
                }
            }
            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }

        public static string GetString(IDictionary<string, object> map, string key, string fallback)
        {
            object value;
            if (map != null && map.TryGetValue(key, out value) && value is string)
            {
                return (string)value;
            }
            return fallback;
        }

        public static bool GetBool(IDictionary<string, object> map, string key, bool fallback)
        {
            object value;
            if (map != null && map.TryGetValue(key, out value) && value is bool)
            {
                return (bool)value;
            }
            return fallback;
        }

        public static double GetDouble(IDictionary<string, object> map, string key, double fallback)
        {
            object value;
            if (map != null && map.TryGetValue(key, out value) && value is double)
            {
                return (double)value;
            }
            return fallback;
        }

        /// <summary>The string array under <paramref name="key"/>, or null when there is none.</summary>
        public static List<string> GetStrings(IDictionary<string, object> map, string key)
        {
            object value;
            if (map == null || !map.TryGetValue(key, out value))
            {
                return null;
            }

            List<object> items = value as List<object>;
            if (items == null)
            {
                return null;
            }

            List<string> strings = new List<string>();
            foreach (object item in items)
            {
                if (item is string)
                {
                    strings.Add((string)item);
                }
            }
            return strings;
        }

        #endregion
    }
}
