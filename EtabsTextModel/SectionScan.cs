using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EtabsTextModel
{
    /// <summary>What to do with one data line of a section that was asked for.</summary>
    internal delegate void SectionLine(TextLine line);

    /// <summary>
    /// One walk down the file, handing each wanted section's lines to whoever asked for it.
    ///
    /// The whole shape of the file is this:
    ///
    ///   $ SHELL UNIFORM LOAD SETS                &lt;- a header: a dollar in the first column
    ///     SHELLUNIFORMLOADSET "T1-RESIDENTIAL" ..  &lt;- its data: always indented
    ///
    /// so a section ends where the next line starts with a dollar. That is the whole rule, and
    /// it is worth saying why it needs to be no more than that: a dollar can appear inside a
    /// name, but a name is inside quotes and quotes are never in the first column, so no
    /// quote counting is needed to tell a header from its data. It also means a section this
    /// reader has never heard of ends the one before it correctly, which is what keeps a
    /// version of ETABS that adds a section from being able to break this.
    ///
    /// Nothing is loaded. The file is walked through a buffer, and a line is only turned into
    /// a string where a handler asks for a piece of it - on a large model the section holding
    /// the load set assignments is half a million lines and all but a few per cent of them are
    /// thrown away without allocating anything at all.
    /// </summary>
    internal static class SectionScan
    {
        /// <summary>
        /// A megabyte at a time. Big enough that a 65 MB file is 65 reads rather than
        /// thousands, small enough to stay well clear of the large object heap.
        /// </summary>
        private const int BufferSize = 1 << 20;

        /// <summary>
        /// Walks the file, and returns the headers that were both asked for and found - so a
        /// caller can tell a table that was empty from one the model does not have.
        ///
        /// Reading stops as soon as the last wanted section has gone by. A caller after
        /// nothing but the units is reading the first dozen lines of the file rather than all
        /// of it.
        /// </summary>
        public static HashSet<string> Run(string path, IDictionary<string, SectionLine> handlers)
        {
            HashSet<string> found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (handlers == null || handlers.Count == 0)
            {
                return found;
            }

            TextLine line = new TextLine();
            SectionLine handler = null;

            byte[] buffer = new byte[BufferSize];
            byte[] carry = new byte[4096];
            int carried = 0;

            // The file is opened as sharing everything, because ETABS may well have it open
            // itself: this reads a file the program that wrote it is still sitting on.
            using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read,
                                                    FileShare.ReadWrite, BufferSize,
                                                    FileOptions.SequentialScan))
            {
                int read;
                bool done = false;

                while (!done && (read = file.Read(buffer, 0, buffer.Length)) > 0)
                {
                    int from = 0;

                    for (int at = 0; at < read; at++)
                    {
                        if (buffer[at] != Newline)
                        {
                            continue;
                        }

                        if (carried > 0)
                        {
                            Append(ref carry, ref carried, buffer, from, at - from);
                            done = Take(line, carry, 0, carried, handlers, found, ref handler);
                            carried = 0;
                        }
                        else
                        {
                            done = Take(line, buffer, from, at - from, handlers, found, ref handler);
                        }

                        from = at + 1;

                        if (done)
                        {
                            break;
                        }
                    }

                    if (!done && from < read)
                    {
                        Append(ref carry, ref carried, buffer, from, read - from);
                    }
                }

                // A last line with no newline after it. ETABS ends the file with one, but a
                // file that was cut short does not, and its last line is still a line.
                if (!done && carried > 0)
                {
                    Take(line, carry, 0, carried, handlers, found, ref handler);
                }
            }

            return found;
        }

        /// <summary>
        /// One line. Returns true when there is no reason to read any further: every section
        /// that was asked for has been seen and the last of them has just ended.
        /// </summary>
        private static bool Take(TextLine line, byte[] bytes, int offset, int length,
                                 IDictionary<string, SectionLine> handlers,
                                 HashSet<string> found, ref SectionLine handler)
        {
            // Trailing carriage return and the padding ETABS leaves on the end of its lines.
            while (length > 0 && IsBlank(bytes[offset + length - 1]))
            {
                length--;
            }

            if (length == 0)
            {
                return false;
            }

            if (bytes[offset] != Dollar)
            {
                if (handler != null)
                {
                    line.Reset(bytes, offset, length);
                    handler(line);
                }

                return false;
            }

            // A header. Whatever section was being read has ended here.
            bool leftAWantedSection = handler != null;

            string header = Encoding.UTF8.GetString(bytes, offset, length).TrimEnd();

            SectionLine wanted;
            if (handlers.TryGetValue(header, out wanted))
            {
                handler = wanted;
                found.Add(header);
                return false;
            }

            handler = null;

            return leftAWantedSection && found.Count == handlers.Count;
        }

        private static void Append(ref byte[] carry, ref int carried, byte[] bytes, int offset, int length)
        {
            if (length <= 0)
            {
                return;
            }

            if (carried + length > carry.Length)
            {
                Array.Resize(ref carry, Math.Max(carry.Length * 2, carried + length));
            }

            Buffer.BlockCopy(bytes, offset, carry, carried, length);
            carried += length;
        }

        private static bool IsBlank(byte b)
        {
            return b == (byte)'\r' || b == (byte)' ' || b == (byte)'\t';
        }

        private const byte Newline = (byte)'\n';
        private const byte Dollar = (byte)'$';
    }
}
