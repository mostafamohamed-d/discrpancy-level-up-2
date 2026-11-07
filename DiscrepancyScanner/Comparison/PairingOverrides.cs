using System;
using System.Collections.Generic;
using System.Linq;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner
{
    /// <summary>
    /// The pairs a person set by hand, kept per project so they survive the next run.
    ///
    /// The check pairs elements by looking for the nearest counterpart inside a radius, which is
    /// right nearly always and wrong in the cases worth arguing about - two columns a hand's
    /// width apart, a beam the two models split differently. Those are exactly the cases where
    /// someone looks at the model, decides which one it really is, and would have to decide it
    /// again after every run unless the answer is written down.
    ///
    /// A pairing set here is not a hint the pass may overrule: it is consumed before the
    /// automatic pass runs at all, so the elements it names are gone from the pool by the time
    /// anything measures a distance. That is what makes it stick.
    ///
    /// Slabs are deliberately absent. A slab zone is a piece of geometry where two outlines
    /// overlap, not an element paired with another element, so there is no pair here to
    /// override - what a person would want to correct there is the coverage decision, which is
    /// a different mechanism and not this one.
    ///
    /// A pairing holds only while both of its elements stand where they stood when it was set.
    /// The names it is written against do not say where anything is, and an ETABS unique name
    /// can be handed to another element entirely when the model is renumbered - so each
    /// pairing keeps where its two sides were, and <see cref="Check"/> forgets it once either
    /// has moved. See <see cref="PairPlace"/>.
    /// </summary>
    public sealed class PairingOverrides
    {
        /// <summary>Where these sit in the settings file, beside the alignments.</summary>
        public const string Branch = "pairingOverrides";

        /// <summary>
        /// Where the two sides of each pairing stood, kept in a branch of its own rather than
        /// beside the id. A build from before this was kept reads the pairings and drops what
        /// it does not know on its next save; kept apart, what it drops is only the places, and
        /// the pairings come back here as ones set before places were kept.
        /// </summary>
        public const string PlacesBranch = "pairingPlaces";

        /// <summary>Unpaired on purpose: the person said these two do not go together.</summary>
        public const long Unpaired = 0;

        /// <summary>One pairing set by hand, and where its two sides were when it was.</summary>
        private sealed class Pin
        {
            public long RevitId;

            /// <summary>Where the ETABS element stood. Null until it has been seen in a run.</summary>
            public PairPlace EtabsAt;

            /// <summary>Where the Revit element stood. Always null on a pairing with nothing.</summary>
            public PairPlace RevitAt;

            /// <summary>
            /// Set by hand since the settings were read, so made by somebody looking at the
            /// elements as they stand now. Where they stand is taken as it is the first time
            /// the check sees them, with nothing to hold it against.
            /// </summary>
            public bool SetNow;

            /// <summary>True once everything there is to keep about where it stood is kept.</summary>
            public bool IsPlaced
            {
                get { return EtabsAt != null && (RevitId == Unpaired || RevitAt != null); }
            }
        }

        // project -> key -> pairing. The key carries the element kind so a column and a beam
        // that happen to share an ETABS name cannot overwrite each other.
        private readonly Dictionary<string, Dictionary<string, Pin>> byProject =
            new Dictionary<string, Dictionary<string, Pin>>(StringComparer.OrdinalIgnoreCase);

        private readonly List<string> dropped = new List<string>();
        private bool changed;

        /// <summary>
        /// How an ETABS element is named in the store. The kind is part of it because the
        /// unique names are only unique within their own table.
        /// </summary>
        public static string KeyOf(ElementKindKey kind, string etabsUniqueName)
        {
            return kind + "|" + (etabsUniqueName ?? "").Trim();
        }

        public void ReadFrom(IDictionary<string, object> view)
        {
            byProject.Clear();
            dropped.Clear();
            changed = false;

            foreach (KeyValuePair<string, object> project in Json.Branch(view, Branch))
            {
                Dictionary<string, Pin> pairs =
                    new Dictionary<string, Pin>(StringComparer.OrdinalIgnoreCase);

                Dictionary<string, object> stored = project.Value as Dictionary<string, object>;
                if (stored == null)
                {
                    continue;
                }

                foreach (KeyValuePair<string, object> entry in stored)
                {
                    long revitId;
                    if (TryReadId(entry.Value, out revitId))
                    {
                        pairs[entry.Key] = new Pin { RevitId = revitId };
                    }
                }

                if (pairs.Count > 0)
                {
                    byProject[project.Key] = pairs;
                }
            }

            ReadPlaces(Json.Branch(view, PlacesBranch));
        }

        /// <summary>
        /// Puts back where each pairing's two sides stood. A record whose id is not the one the
        /// pairing now names is about a pairing that has since been set again, and is left off.
        /// </summary>
        private void ReadPlaces(Dictionary<string, object> places)
        {
            foreach (KeyValuePair<string, object> project in places)
            {
                Dictionary<string, Pin> pairs;
                Dictionary<string, object> stored = project.Value as Dictionary<string, object>;
                if (stored == null || !byProject.TryGetValue(project.Key, out pairs))
                {
                    continue;
                }

                foreach (KeyValuePair<string, object> entry in stored)
                {
                    Pin pin;
                    Dictionary<string, object> record = entry.Value as Dictionary<string, object>;
                    object id;
                    long revitId;
                    if (record == null || !pairs.TryGetValue(entry.Key, out pin)
                        || !record.TryGetValue("revit", out id) || !TryReadId(id, out revitId)
                        || revitId != pin.RevitId)
                    {
                        continue;
                    }

                    pin.EtabsAt = PairPlace.FromText(Json.GetString(record, "etabs", ""));
                    pin.RevitAt = PairPlace.FromText(Json.GetString(record, "revitAt", ""));
                }
            }
        }

        public Dictionary<string, object> ToJson()
        {
            Dictionary<string, object> root = new Dictionary<string, object>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, Dictionary<string, Pin>> project in byProject)
            {
                if (project.Value.Count == 0)
                {
                    continue;
                }

                Dictionary<string, object> pairs = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, Pin> entry
                         in project.Value.OrderBy(e => e.Key, StringComparer.Ordinal))
                {
                    pairs[entry.Key] = entry.Value.RevitId;
                }
                root[project.Key] = pairs;
            }

            return root;
        }

        /// <summary>Where the two sides of each pairing stood, for <see cref="PlacesBranch"/>.</summary>
        public Dictionary<string, object> PlacesToJson()
        {
            Dictionary<string, object> root = new Dictionary<string, object>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, Dictionary<string, Pin>> project in byProject)
            {
                Dictionary<string, object> places = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, Pin> entry
                         in project.Value.OrderBy(e => e.Key, StringComparer.Ordinal))
                {
                    if (entry.Value.EtabsAt == null && entry.Value.RevitAt == null)
                    {
                        continue;
                    }

                    places[entry.Key] = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        { "revit", entry.Value.RevitId },
                        { "etabs", entry.Value.EtabsAt == null ? "" : entry.Value.EtabsAt.ToText() },
                        { "revitAt", entry.Value.RevitAt == null ? "" : entry.Value.RevitAt.ToText() }
                    };
                }

                if (places.Count > 0)
                {
                    root[project.Key] = places;
                }
            }

            return root;
        }

        /// <summary>
        /// Records that this ETABS element is paired with this Revit element, whatever the
        /// automatic pass would have said. <see cref="Unpaired"/> records the opposite: that it
        /// is paired with nothing, which is a real answer and not the same as having no opinion.
        ///
        /// Where the two stand is not asked for here. It is taken the next time the check sees
        /// them - straight away, when the pairing is set on a model already checked - so what
        /// is kept is what the pass read, measured the same way it will be measured next time.
        /// </summary>
        public void Set(string project, string key, long revitId)
        {
            if (string.IsNullOrEmpty(project) || string.IsNullOrEmpty(key))
            {
                return;
            }

            Dictionary<string, Pin> pairs;
            if (!byProject.TryGetValue(project, out pairs))
            {
                pairs = new Dictionary<string, Pin>(StringComparer.OrdinalIgnoreCase);
                byProject[project] = pairs;
            }
            pairs[key] = new Pin { RevitId = revitId, SetNow = true };
        }

        /// <summary>Forgets a pairing, so the automatic pass decides this one again.</summary>
        public void Clear(string project, string key)
        {
            Dictionary<string, Pin> pairs;
            if (project != null && key != null && byProject.TryGetValue(project, out pairs))
            {
                pairs.Remove(key);
            }
        }

        /// <summary>The Revit element this one was pinned to, or null for no opinion.</summary>
        public long? Find(string project, string key)
        {
            Dictionary<string, Pin> pairs;
            Pin pin;
            if (project != null && key != null
                && byProject.TryGetValue(project, out pairs)
                && pairs.TryGetValue(key, out pin))
            {
                return pin.RevitId;
            }
            return null;
        }

        /// <summary>Every pairing set by hand on this project, for the pre-pass to consume.</summary>
        public IDictionary<string, long> For(string project)
        {
            Dictionary<string, Pin> pairs;
            return project != null && byProject.TryGetValue(project, out pairs)
                ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value.RevitId,
                                     StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }

        public int CountFor(string project)
        {
            Dictionary<string, Pin> pairs;
            return project != null && byProject.TryGetValue(project, out pairs) ? pairs.Count : 0;
        }

        #region holding pairings to where their elements stand

        /// <summary>
        /// How far the pairing picker looks for a counterpart of this kind, in mm, and so how
        /// far apart two elements can be and still be ones somebody paired by picking.
        /// </summary>
        public static double PickerRadius(ElementKindKey kind)
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            switch (kind)
            {
                case ElementKindKey.Wall: return pref.WallSearchRadius;
                case ElementKindKey.Beam: return Math.Max(pref.BeamPlanTolerance * 4, 1500);
                case ElementKindKey.LineLoad: return pref.LineLoadSearchRadius;
                default: return pref.ColumnSearchRadius;
            }
        }

        /// <summary>
        /// Holds this kind's pairings on this project up against where their elements stand
        /// now, and forgets every one whose ETABS or Revit side has moved since it was set.
        /// Run by each pairing pass before it reads a single pairing, so a dropped one is
        /// decided by distance like anything nobody corrected.
        ///
        /// Only the pairings whose ETABS element is in <paramref name="etabsNow"/> are looked
        /// at. One whose element is not in this run - another floor, or deleted for now - is
        /// left exactly as it is, for the run that does have it.
        ///
        /// A pairing not yet known to stand anywhere has where its two sides are now taken as
        /// where they stood. One set by hand this session is taken as it is. One read from the
        /// file was set before places were kept, so nothing says whether it has moved; it is
        /// kept only when its two sides could still be one element - on one story and within
        /// the <see cref="PickerRadius"/> of each other in plan - and dropped otherwise.
        /// </summary>
        /// <param name="etabsNow">Every ETABS element of this kind in the run, by unique name.</param>
        /// <param name="revitNow">Every Revit element of this kind in the run, by id.</param>
        public void Check(string project, ElementKindKey kind,
                          IDictionary<string, PairPlace> etabsNow,
                          IDictionary<long, PairPlace> revitNow)
        {
            Dictionary<string, Pin> pairs;
            if (string.IsNullOrEmpty(project) || etabsNow == null
                || !byProject.TryGetValue(project, out pairs))
            {
                return;
            }

            string prefix = KeyOf(kind, "");
            double reach = PickerRadius(kind);

            foreach (KeyValuePair<string, Pin> entry in pairs.ToList())
            {
                if (!entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string name = entry.Key.Substring(prefix.Length);
                PairPlace etabs;
                if (!etabsNow.TryGetValue(name, out etabs) || etabs == null)
                {
                    continue;
                }

                Pin pin = entry.Value;
                PairPlace revit = null;
                if (pin.RevitId != Unpaired && revitNow != null)
                {
                    revitNow.TryGetValue(pin.RevitId, out revit);
                }

                string why = WhyItNoLongerHolds(pin, etabs, revit, reach);
                if (why != null)
                {
                    pairs.Remove(entry.Key);
                    dropped.Add(Describe(kind, name, pin) + ": dropped - " + why + ".");
                    changed = true;
                    continue;
                }

                if (pin.IsPlaced || (pin.RevitId != Unpaired && revit == null))
                {
                    // Placed already, or its Revit side is not in this run and there is
                    // nothing to take it from. Taken whole when the two are next seen together.
                    continue;
                }

                pin.EtabsAt = pin.EtabsAt ?? etabs;
                pin.RevitAt = pin.RevitId == Unpaired ? null : pin.RevitAt ?? revit;
                changed = true;
            }
        }

        /// <summary>
        /// Why a pairing is no longer about the elements it was set on, or null while it still
        /// is. <paramref name="revit"/> is null when the Revit side is not in this run, which
        /// leaves nothing to hold that side against.
        /// </summary>
        private static string WhyItNoLongerHolds(Pin pin, PairPlace etabs, PairPlace revit, double reach)
        {
            // Said of the element that now has the name, because that is all the check can
            // see: the one it was set on may have moved, or the name may have been handed on.
            if (pin.EtabsAt != null && !etabs.IsWhere(pin.EtabsAt))
            {
                return "the ETABS element with that name is " + Millimetres(etabs.DistanceFrom(pin.EtabsAt))
                       + " from where it stood when the pairing was set";
            }

            if (revit != null && pin.RevitAt != null && !revit.IsWhere(pin.RevitAt))
            {
                return "the Revit element is " + Millimetres(revit.DistanceFrom(pin.RevitAt))
                       + " from where it stood when the pairing was set";
            }

            // Set before places were kept, and seen with its Revit side for the first time
            // since: nothing says whether it has moved, so it has to at least be believable.
            if (revit == null || pin.SetNow || pin.EtabsAt != null || pin.RevitAt != null)
            {
                return null;
            }

            if (!PairPlace.OnOneStory(etabs, revit))
            {
                return "it was set before pairings kept where their elements stood, and the two "
                       + "are now on different stories (ETABS " + etabs.Story + ", Revit "
                       + revit.Story + ")";
            }

            double gap = PairPlace.PlanGap(etabs, revit);
            if (gap > reach)
            {
                return "it was set before pairings kept where their elements stood, and the two "
                       + "are now " + Millimetres(gap) + " apart in plan, further than the "
                       + Millimetres(reach) + " the pairing picker looks";
            }

            return null;
        }

        private static string Describe(ElementKindKey kind, string name, Pin pin)
        {
            string what = kind == ElementKindKey.LineLoad ? "Line load on ETABS frame" : "ETABS " + kind.ToString().ToLowerInvariant();
            return what + " " + name + (pin.RevitId == Unpaired
                ? ", set by hand to pair with nothing"
                : ", paired by hand with Revit " + pin.RevitId);
        }

        private static string Millimetres(double value)
        {
            return double.IsInfinity(value)
                ? "a different shape"
                : Math.Round(value).ToString("#,0", System.Globalization.CultureInfo.InvariantCulture) + " mm";
        }

        /// <summary>
        /// The pairings dropped since this was last asked, each said in a line somebody can
        /// act on, and forgotten here once handed over.
        /// </summary>
        public IList<string> TakeDropped()
        {
            List<string> taken = dropped.ToList();
            dropped.Clear();
            return taken;
        }

        /// <summary>
        /// True when a pass has dropped a pairing or kept where one stands since this was last
        /// asked - that is, when the settings file no longer says what this does.
        /// </summary>
        public bool TakeChanged()
        {
            bool was = changed;
            changed = false;
            return was;
        }

        #endregion

        /// <summary>
        /// Forgets every pairing set by hand on one project, "paired with nothing" included,
        /// so the automatic pass decides all of them again. Answers how many there were.
        /// Other projects are left alone.
        /// </summary>
        public int ClearProject(string project)
        {
            Dictionary<string, Pin> pairs;
            if (project == null || !byProject.TryGetValue(project, out pairs))
            {
                return 0;
            }

            int count = pairs.Count;
            byProject.Remove(project);
            return count;
        }

        private static bool TryReadId(object value, out long revitId)
        {
            revitId = 0;
            if (value == null)
            {
                return false;
            }

            try
            {
                revitId = Convert.ToInt64(value,
                    System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception)
            {
                // A hand-edited file, or one written by a version that stored something else.
                // A pairing that cannot be read is one the automatic pass decides instead.
                return false;
            }
        }
    }

    /// <summary>
    /// Which table an ETABS unique name belongs to. Kept apart from the viewer's ElementKind so
    /// the comparison side does not have to reach into the WPF layer for it.
    /// </summary>
    public enum ElementKindKey
    {
        Column,
        Beam,
        Wall,

        /// <summary>
        /// A drawn line load pinned to an ETABS frame.
        ///
        /// Named by the frame rather than by the run it belongs to, because a run is chained
        /// afresh out of whatever frames stood end to end this time and has no name that would
        /// come back. A frame's unique name does, so a pairing set on one sticks - and the run
        /// it is part of is what the line is then read against.
        /// </summary>
        LineLoad
    }
}
