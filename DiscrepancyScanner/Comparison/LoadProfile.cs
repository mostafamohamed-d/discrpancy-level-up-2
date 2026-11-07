using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// What a line carries at every point along it, as the sum of the stretches put on it.
    ///
    /// The one-dimensional twin of <see cref="LoadCoverage"/>, and the same bargain: nothing
    /// is cut against anything, because nothing ever asks where the seam between two stretches
    /// is. It only asks what is at a point, and where two stretches cover the same point the
    /// answer is both of them added - two line loads drawn along the same beam is two loads on
    /// that beam, not one of them winning.
    ///
    /// That summing is what lets one ETABS frame hold what the drawing shows as several line
    /// loads. The frame carries one number where they overlap and several stretches where they
    /// do not, and neither side has to know which of the two it is looking at.
    ///
    /// Distances are chainage in mm along whatever line this is a profile of; values are kN/m.
    /// </summary>
    internal sealed class LoadProfile
    {
        private struct Span
        {
            public double From;
            public double To;
            public double AtFrom;
            public double AtTo;
        }

        private readonly List<Span> spans = new List<Span>();

        /// <summary>True when nothing has been put on this line at all.</summary>
        public bool IsEmpty { get { return spans.Count == 0; } }

        /// <summary>
        /// Puts a stretch on the line. The two values are the load at each end, which are the
        /// same on the uniform load a drawing always means and differ on a trapezoid somebody
        /// has tapered along a frame.
        /// </summary>
        public void Add(double from, double to, double atFrom, double atTo)
        {
            if (to < from)
            {
                double swapDistance = from; from = to; to = swapDistance;
                double swapValue = atFrom; atFrom = atTo; atTo = swapValue;
            }

            if (to - from <= 0)
            {
                return;
            }

            spans.Add(new Span { From = from, To = to, AtFrom = atFrom, AtTo = atTo });
        }

        /// <summary>
        /// Every chainage where the answer can change: the two ends of each stretch. Sorted,
        /// and with the ones on top of each other collapsed, so walking them gives the pieces
        /// the line reads the same all the way along.
        /// </summary>
        public IEnumerable<double> Breaks(double sameWithin)
        {
            List<double> breaks = new List<double>();

            foreach (Span span in spans)
            {
                breaks.Add(span.From);
                breaks.Add(span.To);
            }

            breaks.Sort();

            double last = double.NegativeInfinity;

            foreach (double at in breaks)
            {
                if (at - last > Math.Max(sameWithin, 0))
                {
                    yield return at;
                    last = at;
                }
            }
        }

        /// <summary>
        /// What the line carries at one point, in kN/m: every stretch covering it, added.
        ///
        /// A point exactly on the seam between two stretches counts both, which is the right
        /// answer for a total and the wrong one for a single point - so this is only ever
        /// asked partway along a piece rather than at its ends.
        /// </summary>
        public double At(double chainage)
        {
            double total = 0;

            foreach (Span span in spans)
            {
                if (chainage < span.From || chainage > span.To)
                {
                    continue;
                }

                double width = span.To - span.From;
                double k = width <= 0 ? 0 : (chainage - span.From) / width;
                total += span.AtFrom + (span.AtTo - span.AtFrom) * k;
            }

            return total;
        }

        /// <summary>
        /// What the line carries just inside each end of a piece. A piece lies between breaks,
        /// so a uniform load reads the same at both and a tapered one reads its two real values
        /// there - never the one at its middle, which is the taper's average.
        /// </summary>
        public double[] AtEnds(double from, double to)
        {
            double inset = Math.Min(0.5, (to - from) / 4d);
            return new[] { At(from + inset), At(to - inset) };
        }

        /// <summary>
        /// The force on a stretch of the line, in kN.
        ///
        /// Worked out piece by piece between the breaks and measured at the middle of each,
        /// which is exact rather than approximate: between two breaks every stretch covering
        /// the piece runs straight, so their sum does too, and the middle of a straight line
        /// is its average.
        /// </summary>
        public double TotalForce(double from, double to)
        {
            if (to <= from)
            {
                return 0;
            }

            List<double> cuts = new List<double> { from, to };

            foreach (double at in Breaks(0))
            {
                if (at > from && at < to)
                {
                    cuts.Add(at);
                }
            }

            cuts.Sort();

            double force = 0;

            for (int i = 0; i < cuts.Count - 1; i++)
            {
                double width = cuts[i + 1] - cuts[i];
                if (width <= 0)
                {
                    continue;
                }

                force += At((cuts[i] + cuts[i + 1]) / 2d) * width / 1000d;
            }

            return force;
        }

        /// <summary>
        /// The pieces one stretch of the line breaks into, given what is on this profile and
        /// what is on another. Each piece reads the same all the way along on both, which is
        /// what makes it comparable.
        ///
        /// Pieces shorter than <paramref name="shortestWorthHaving"/> are folded into the one
        /// before them rather than reported. Two drawn lines meeting end to end leave a sliver
        /// a few millimetres long where their ends do not quite agree, and a finding on every
        /// one of those is noise standing in front of the work.
        /// </summary>
        public static IList<double[]> Pieces(double from, double to, double shortestWorthHaving,
                                             params LoadProfile[] profiles)
        {
            List<double[]> pieces = new List<double[]>();

            if (to <= from)
            {
                return pieces;
            }

            List<double> cuts = new List<double> { from };

            foreach (LoadProfile profile in profiles.Where(one => one != null))
            {
                foreach (double at in profile.Breaks(0))
                {
                    if (at > from && at < to)
                    {
                        cuts.Add(at);
                    }
                }
            }

            cuts.Add(to);
            cuts.Sort();

            double shortest = Math.Max(shortestWorthHaving, 0);
            double openFrom = cuts[0];

            for (int i = 1; i < cuts.Count; i++)
            {
                // Held open until the piece is long enough to be worth having. The last one is
                // always closed, so the far end of the stretch is never lost.
                if (cuts[i] - openFrom < shortest && i < cuts.Count - 1)
                {
                    continue;
                }

                if (cuts[i] > openFrom)
                {
                    pieces.Add(new[] { openFrom, cuts[i] });
                    openFrom = cuts[i];
                }
            }

            // The last piece is closed whatever its length, so it is the one that can come out
            // too short: a stretch whose final break falls a few centimetres from its end
            // leaves that sliver standing alone. Which is not a corner case at all - a drawn
            // line and the frame ETABS meshed under it never end at exactly the same place, so
            // most lines on a real plan have one. Left alone it reads as a stretch of line
            // carrying no load, and the plan fills up with findings a few centimetres long.
            //
            // Folded backwards rather than forwards because by now there is nothing ahead of
            // it: it is the end of the stretch.
            if (pieces.Count > 1 && pieces[pieces.Count - 1][1] - pieces[pieces.Count - 1][0] < shortest)
            {
                pieces[pieces.Count - 2][1] = pieces[pieces.Count - 1][1];
                pieces.RemoveAt(pieces.Count - 1);
            }

            return pieces;
        }
    }
}
