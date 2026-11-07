using System;
using System.Collections.Generic;
using System.Linq;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner
{
    /// <summary>What a person decided about an element they looked at.</summary>
    public enum HumanVerdict
    {
        /// <summary>Nobody has said anything.</summary>
        None,

        /// <summary>
        /// Someone has looked at it. It is still an open discrepancy - being seen is not the
        /// same as being settled, and a finding that is merely acknowledged still has work in it.
        /// </summary>
        Reviewed,

        /// <summary>
        /// Looked at and settled: the two models differ and that is agreed to be all right.
        /// A slab thicker in Revit because of a screed the analysis leaves out, a beam mark the
        /// design team has confirmed. It stays on the plan because it is still a real difference
        /// between the two models, but it stops counting as work.
        /// </summary>
        Accepted
    }

    /// <summary>What a person said about one element, and what it was true of.</summary>
    public sealed class ReviewNote
    {
        /// <summary>What was decided about it.</summary>
        public HumanVerdict Verdict;

        /// <summary>Someone looked at this - reviewed or accepted, either counts as seen.</summary>
        public bool Reviewed => Verdict != HumanVerdict.None;

        /// <summary>Settled, so it no longer counts as open work.</summary>
        public bool Accepted => Verdict == HumanVerdict.Accepted;

        /// <summary>Whatever they wanted to say about it. Kept whether or not it was reviewed.</summary>
        public string Note = "";

        /// <summary>
        /// The verdicts the review was made against, as one string.
        ///
        /// This is the whole reason a review is safe to keep. "I looked at this and it is fine"
        /// is only true of the thing that was looked at: if someone then fixes the Revit model,
        /// or a tolerance moves, the element is saying something different and the old review
        /// would quietly mark a brand new finding as already handled. Recording what was true at
        /// the time is what lets a stale review be shown as stale rather than as clean.
        /// </summary>
        public string Fingerprint = "";

        /// <summary>When it was said, so a reader can weigh how old the opinion is.</summary>
        public DateTime When;

        public ReviewNote Copy()
        {
            return new ReviewNote
            {
                Verdict = Verdict,
                Note = Note,
                Fingerprint = Fingerprint,
                When = When
            };
        }

        public bool IsEmpty => Verdict == HumanVerdict.None && Note.Length == 0;
    }

    /// <summary>
    /// The reviews and notes people have left on elements, per project.
    ///
    /// Kept apart from <see cref="PairingOverrides"/> because the two do different things: a
    /// pairing changes what the check computes, while a review changes only how a result is
    /// read. Mixing them would mean a note could not be written without the risk of moving a
    /// pairing, and a pairing could not be cleared without losing what someone wrote about it.
    ///
    /// Reviews are per element rather than per check, because both places you can leave one -
    /// the button on the card, and a row in the findings list - are looking at an element. The
    /// fingerprint therefore covers every verdict the element carries: change any of them and
    /// the review is stale, which is the safe way round.
    /// </summary>
    public sealed class ReviewNotes
    {
        /// <summary>Where these sit in the settings file.</summary>
        public const string Branch = "reviews";

        private readonly Dictionary<string, Dictionary<string, ReviewNote>> byProject =
            new Dictionary<string, Dictionary<string, ReviewNote>>(StringComparer.OrdinalIgnoreCase);

        public void ReadFrom(IDictionary<string, object> view)
        {
            byProject.Clear();

            foreach (KeyValuePair<string, object> project in Json.Branch(view, Branch))
            {
                Dictionary<string, object> elements = project.Value as Dictionary<string, object>;
                if (elements == null)
                {
                    continue;
                }

                Dictionary<string, ReviewNote> notes =
                    new Dictionary<string, ReviewNote>(StringComparer.OrdinalIgnoreCase);

                foreach (KeyValuePair<string, object> element in elements)
                {
                    Dictionary<string, object> stored = element.Value as Dictionary<string, object>;
                    if (stored == null)
                    {
                        continue;
                    }

                    DateTime when;
                    DateTime.TryParse(Json.GetString(stored, "when", ""),
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind, out when);

                    notes[element.Key] = new ReviewNote
                    {
                        Verdict = ReadVerdict(stored),
                        Note = Json.GetString(stored, "note", ""),
                        Fingerprint = Json.GetString(stored, "against", ""),
                        When = when
                    };
                }

                if (notes.Count > 0)
                {
                    byProject[project.Key] = notes;
                }
            }
        }

        public Dictionary<string, object> ToJson()
        {
            Dictionary<string, object> root = new Dictionary<string, object>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, Dictionary<string, ReviewNote>> project in byProject)
            {
                Dictionary<string, object> elements =
                    new Dictionary<string, object>(StringComparer.Ordinal);

                foreach (KeyValuePair<string, ReviewNote> entry
                         in project.Value.OrderBy(e => e.Key, StringComparer.Ordinal))
                {
                    // An element nobody said anything about any more is dropped rather than
                    // written as an empty record, so the file does not grow with the ghosts of
                    // reviews that were taken back.
                    if (entry.Value == null || entry.Value.IsEmpty)
                    {
                        continue;
                    }

                    elements[entry.Key] = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        { "verdict", entry.Value.Verdict.ToString() },
                        { "note", entry.Value.Note },
                        { "against", entry.Value.Fingerprint },
                        { "when", entry.Value.When.ToString("o",
                            System.Globalization.CultureInfo.InvariantCulture) }
                    };
                }

                if (elements.Count > 0)
                {
                    root[project.Key] = elements;
                }
            }

            return root;
        }

        /// <summary>What was said about this element, or null if nothing was.</summary>
        public ReviewNote Find(string project, string key)
        {
            Dictionary<string, ReviewNote> notes;
            ReviewNote note;
            if (project != null && key != null
                && byProject.TryGetValue(project, out notes)
                && notes.TryGetValue(key, out note))
            {
                return note;
            }
            return null;
        }

        /// <summary>
        /// Marks an element reviewed against the verdicts it is carrying now. Passing the
        /// fingerprint in rather than working it out here keeps this store free of any opinion
        /// about what an element is.
        /// </summary>
        public void SetVerdict(string project, string key, HumanVerdict verdict, string fingerprint)
        {
            ReviewNote note = Edit(project, key);
            if (note == null)
            {
                return;
            }

            note.Verdict = verdict;
            note.Fingerprint = verdict == HumanVerdict.None ? "" : fingerprint ?? "";
            note.When = DateTime.Now;
            Prune(project, key, note);
        }

        /// <summary>
        /// Reads what was decided. A file written before there was anything to decide but
        /// "reviewed" carries a bool, and comes back as a review rather than as nothing.
        /// </summary>
        private static HumanVerdict ReadVerdict(IDictionary<string, object> stored)
        {
            HumanVerdict verdict;
            if (Enum.TryParse(Json.GetString(stored, "verdict", ""), true, out verdict)
                && Enum.IsDefined(typeof(HumanVerdict), verdict))
            {
                return verdict;
            }
            return Json.GetBool(stored, "reviewed", false)
                ? HumanVerdict.Reviewed
                : HumanVerdict.None;
        }

        public void SetNote(string project, string key, string text)
        {
            ReviewNote note = Edit(project, key);
            if (note == null)
            {
                return;
            }

            note.Note = (text ?? "").Trim();
            note.When = DateTime.Now;
            Prune(project, key, note);
        }

        /// <summary>
        /// Takes back every decision on one project - accepted and reviewed alike - and keeps
        /// the notes, which are what somebody wrote rather than what they decided. Answers how
        /// many decisions there were. Other projects are left alone.
        /// </summary>
        public int ClearVerdicts(string project)
        {
            return ClearWhere(project, note => note.Verdict != HumanVerdict.None, TakeBackVerdict);
        }

        /// <summary>
        /// Takes back one kind of decision on one project - the acceptances, say, leaving what
        /// was only marked reviewed - and keeps the notes. Answers how many there were.
        /// </summary>
        public int ClearVerdicts(string project, HumanVerdict verdict)
        {
            if (verdict == HumanVerdict.None)
            {
                return 0;
            }
            return ClearWhere(project, note => note.Verdict == verdict, TakeBackVerdict);
        }

        /// <summary>
        /// Rubs out every note written on one project and keeps the decisions they were written
        /// beside. Answers how many notes there were.
        /// </summary>
        public int ClearNotes(string project)
        {
            return ClearWhere(project, note => note.Note.Length > 0, note => note.Note = "");
        }

        public int CountFor(string project, HumanVerdict verdict)
        {
            Dictionary<string, ReviewNote> notes;
            return byProject.TryGetValue(project ?? "", out notes)
                ? notes.Values.Count(note => note.Verdict == verdict)
                : 0;
        }

        /// <summary>How many elements of one project have a note written on them.</summary>
        public int CountNotes(string project)
        {
            Dictionary<string, ReviewNote> notes;
            return byProject.TryGetValue(project ?? "", out notes)
                ? notes.Values.Count(note => note.Note.Length > 0)
                : 0;
        }

        private static void TakeBackVerdict(ReviewNote note)
        {
            note.Verdict = HumanVerdict.None;
            note.Fingerprint = "";
        }

        /// <summary>
        /// Clears one thing off every entry of a project that has it, and drops an entry left
        /// with nothing on it at all. Answers how many entries had it.
        /// </summary>
        private int ClearWhere(string project, Func<ReviewNote, bool> has, Action<ReviewNote> clear)
        {
            Dictionary<string, ReviewNote> notes;
            if (project == null || !byProject.TryGetValue(project, out notes))
            {
                return 0;
            }

            int count = 0;
            foreach (KeyValuePair<string, ReviewNote> pair in notes.ToList())
            {
                if (!has(pair.Value))
                {
                    continue;
                }

                clear(pair.Value);
                count++;

                if (pair.Value.IsEmpty)
                {
                    notes.Remove(pair.Key);
                }
            }

            return count;
        }

        private ReviewNote Edit(string project, string key)
        {
            if (string.IsNullOrEmpty(project) || string.IsNullOrEmpty(key))
            {
                return null;
            }

            Dictionary<string, ReviewNote> notes;
            if (!byProject.TryGetValue(project, out notes))
            {
                notes = new Dictionary<string, ReviewNote>(StringComparer.OrdinalIgnoreCase);
                byProject[project] = notes;
            }

            ReviewNote note;
            if (!notes.TryGetValue(key, out note))
            {
                note = new ReviewNote();
                notes[key] = note;
            }
            return note;
        }

        private void Prune(string project, string key, ReviewNote note)
        {
            if (note.IsEmpty)
            {
                byProject[project].Remove(key);
            }
        }
    }
}
