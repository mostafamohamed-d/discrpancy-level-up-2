using System;
using System.Globalization;
using System.Text;

namespace EtabsTextModel
{
    /// <summary>
    /// One line of the file, read where it lies.
    ///
    /// A data line looks like
    ///   AREALOAD  "F1"  "L01"  TYPE "UNIFLOADSET"  "T2-BALCONY"
    /// and the parts wanted out of it are quoted strings, sometimes announced by a keyword and
    /// sometimes not. Nothing here splits the line or copies it: the cursor walks the bytes as
    /// they sit in the read buffer and a string is only made for the pieces actually asked for.
    ///
    /// That matters because the section holding the load set assignments is most of the file -
    /// half a million lines on a large model - and all but a few per cent of them are rejected
    /// on <see cref="Contains"/> without a single allocation.
    ///
    /// One of these is made per read and reset per line, so it is not thread safe and holds no
    /// claim on the buffer once the line has been dealt with.
    /// </summary>
    internal sealed class TextLine
    {
        private byte[] bytes = new byte[0];
        private int start;
        private int end;
        private int cursor;

        /// <summary>Where the word <see cref="NextWord"/> last stopped on lies.</summary>
        private int wordStart;
        private int wordEnd;

        public void Reset(byte[] buffer, int offset, int length)
        {
            bytes = buffer;
            start = offset;
            end = offset + length;
            cursor = offset;
        }

        /// <summary>Back to the first byte, for a second look at the same line.</summary>
        public void Rewind()
        {
            cursor = start;
        }

        /// <summary>
        /// Whether the line's first word is this keyword. What follows it has to be a space or
        /// a quote, so "UNITS" does not match a line that begins "UNITSCALE".
        ///
        /// This is how a line is told apart from its neighbours in a section that holds more
        /// than one kind of line, and it is stricter than looking for the keyword anywhere:
        /// a title reading "UNITS AS DRAWN" is not a units line.
        /// </summary>
        public bool StartsWith(byte[] keyword)
        {
            int at = start;

            while (at < end && (bytes[at] == Space || bytes[at] == Tab))
            {
                at++;
            }

            if (end - at < keyword.Length)
            {
                return false;
            }

            for (int i = 0; i < keyword.Length; i++)
            {
                if (bytes[at + i] != keyword[i])
                {
                    return false;
                }
            }

            int after = at + keyword.Length;

            return after >= end || bytes[after] == Space || bytes[after] == Tab
                   || bytes[after] == Quote;
        }

        /// <summary>Whether the keyword appears anywhere on the line. Costs no allocation.</summary>
        public bool Contains(byte[] keyword)
        {
            return Find(keyword, start) >= 0;
        }

        /// <summary>
        /// Moves the cursor to just after the keyword, so what follows can be read. False
        /// leaves the cursor alone, which is what a line missing an optional part looks like.
        /// </summary>
        public bool SeekPast(byte[] keyword)
        {
            int at = Find(keyword, cursor);
            if (at < 0)
            {
                return false;
            }

            cursor = at + keyword.Length;
            return true;
        }

        /// <summary>
        /// The next quoted string on the line, or null once there are no more.
        ///
        /// The closing quote is the next one after the opening quote. ETABS does not write a
        /// quote inside a name and gives no way to escape one, so there is nothing else it
        /// could be.
        /// </summary>
        public string NextQuoted()
        {
            while (cursor < end && bytes[cursor] != Quote)
            {
                cursor++;
            }

            if (cursor >= end)
            {
                return null;
            }

            int from = ++cursor;

            while (cursor < end && bytes[cursor] != Quote)
            {
                cursor++;
            }

            if (cursor >= end)
            {
                // An unterminated quote: a truncated line, or a file that stopped being written
                // half way through. Nothing to read out of it.
                return null;
            }

            string text = Decode(from, cursor - from);
            cursor++;
            return text;
        }

        /// <summary>
        /// The number the cursor is sitting in front of, skipping the spaces before it. False
        /// for anything that is not a number, which is how a line written in a shape this does
        /// not know is turned away rather than half read.
        /// </summary>
        public bool NextNumber(out double value)
        {
            value = 0;

            while (cursor < end && (bytes[cursor] == Space || bytes[cursor] == Tab))
            {
                cursor++;
            }

            int from = cursor;

            while (cursor < end && bytes[cursor] != Space && bytes[cursor] != Tab)
            {
                cursor++;
            }

            if (cursor == from)
            {
                return false;
            }

            // Always invariant: the file is written by ETABS with a full stop for a decimal
            // point whatever the machine's own idea of one is, so reading it any other way
            // turns 2.375 into 2375 on a machine that writes 2,375.
            return double.TryParse(Decode(from, cursor - from), NumberStyles.Float,
                                   CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// Moves to the next bare word - a run of anything but spaces, taken from outside the
        /// quotes. Quoted strings are stepped over whole, so what is read is only ever one of
        /// the file's own keywords or a bare number, never a piece of somebody's name.
        ///
        /// This is for the lines that carry keywords whose values could hold those same
        /// keywords. An area assign line goes
        ///
        ///   AREAASSIGN  "F1"  "L01"  SECTION "PIER300-C40"  PIER  "P3"  DIAPH  "D1"
        ///
        /// and looking for "PIER" anywhere after the story would find it inside the section
        /// name and read the section as the pier. Walking the words instead cannot: the section
        /// name is inside quotes, so it is never a word. It is also one pass along the line
        /// rather than one per keyword, which on a section with a row per area is the
        /// difference worth having.
        /// </summary>
        public bool NextWord()
        {
            while (cursor < end)
            {
                byte b = bytes[cursor];

                if (b == Space || b == Tab)
                {
                    cursor++;
                    continue;
                }

                if (b == Quote)
                {
                    cursor++;

                    while (cursor < end && bytes[cursor] != Quote)
                    {
                        cursor++;
                    }

                    if (cursor < end)
                    {
                        cursor++;
                    }

                    continue;
                }

                wordStart = cursor;

                while (cursor < end && bytes[cursor] != Space && bytes[cursor] != Tab
                       && bytes[cursor] != Quote)
                {
                    cursor++;
                }

                wordEnd = cursor;
                return true;
            }

            return false;
        }

        /// <summary>Whether the word <see cref="NextWord"/> just stopped on is this keyword.</summary>
        public bool WordIs(byte[] keyword)
        {
            if (wordEnd - wordStart != keyword.Length)
            {
                return false;
            }

            for (int i = 0; i < keyword.Length; i++)
            {
                if (bytes[wordStart + i] != keyword[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The whole line, for a message about one that could not be read.</summary>
        public override string ToString()
        {
            return Decode(start, end - start);
        }

        #region the bytes themselves

        private const byte Quote = (byte)'"';
        private const byte Space = (byte)' ';
        private const byte Tab = (byte)'\t';

        /// <summary>
        /// Plain forward search. The keywords are short and distinctive - "UNIFLOADSET",
        /// "LOADPAT" - so the first byte throws out nearly every position before anything else
        /// is looked at, which is as good as anything cleverer at these lengths.
        /// </summary>
        private int Find(byte[] keyword, int from)
        {
            int last = end - keyword.Length;
            byte first = keyword[0];

            for (int at = from; at <= last; at++)
            {
                if (bytes[at] != first)
                {
                    continue;
                }

                int i = 1;
                while (i < keyword.Length && bytes[at + i] == keyword[i])
                {
                    i++;
                }

                if (i == keyword.Length)
                {
                    return at;
                }
            }

            return -1;
        }

        /// <summary>
        /// ETABS writes the file as UTF-8 without a byte order mark, so that is read first and
        /// strictly. Anything that is not valid UTF-8 was written by something else in a single
        /// byte code page, and is read as Latin-1 rather than turned into question marks: a name
        /// that comes back mangled the same way on both sides of a join still joins, but one
        /// that comes back as "?" collides with every other name that had a character in it.
        /// </summary>
        private string Decode(int from, int length)
        {
            if (length <= 0)
            {
                return "";
            }

            try
            {
                return StrictUtf8.GetString(bytes, from, length);
            }
            catch (DecoderFallbackException)
            {
                return Latin1.GetString(bytes, from, length);
            }
        }

        private static readonly Encoding StrictUtf8 =
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        #endregion
    }
}
