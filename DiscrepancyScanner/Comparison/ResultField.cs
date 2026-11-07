using System;
using System.Collections.Generic;

namespace DiscrepancyScanner
{
    /// <summary>
    /// One named piece of comparison output for an element. The viewer lists every key it
    /// finds across the visible elements and lets the user pick which ones get drawn on top
    /// of the beam or column, so adding a new check here is all it takes to make that check
    /// displayable.
    /// </summary>
    public sealed class ResultField
    {
        public ResultField(string key, string label, string value, bool isFailure = false,
                           CheckKind kind = CheckKind.None,
                           string etabs = null, string revit = null)
        {
            Key = key;
            Label = label;
            Value = value ?? "";
            IsFailure = isFailure;
            Kind = kind;
            Etabs = etabs ?? "";
            Revit = revit ?? "";
        }

        /// <summary>Stable identifier used to select the field in the viewer.</summary>
        public string Key { get; }

        /// <summary>Human readable name shown in the field picker.</summary>
        public string Label { get; }

        /// <summary>Formatted value drawn on the model.</summary>
        public string Value { get; }

        /// <summary>True when this particular value is the reason the element failed.</summary>
        public bool IsFailure { get; }

        // A value that is really one number per model - a beam's width, a slab's depth - can
        // give the two separately as well as in <see cref="Value"/>. The card then prints them
        // under its ETABS and Revit headings, which is where two numbers are actually compared;
        // Value keeps the combined form because that is what gets stamped on the drawing, where
        // there is only room for one string.

        public string Etabs { get; }
        public string Revit { get; }

        /// <summary>True where the two sides are worth printing in their own columns.</summary>
        public bool HasSides => Etabs.Length > 0 || Revit.Length > 0;

        /// <summary>True where only the combined value is available.</summary>
        public bool ShowsValue => !HasSides;

        /// <summary>
        /// The check this value came out of, so hiding that check's result also stops its
        /// value being stamped on the model. <see cref="CheckKind.None"/> for the fields that
        /// only identify the element, which are never hidden.
        /// </summary>
        public CheckKind Kind { get; }

        public static ResultField Number(string key, string label, double value, int decimals = 0,
                                         bool isFailure = false, CheckKind kind = CheckKind.None)
        {
            return new ResultField(key, label, Math.Round(value, decimals).ToString(), isFailure, kind);
        }

        public override string ToString()
        {
            return $"{Label}: {Value}";
        }
    }

    /// <summary>
    /// Implemented by anything the viewer can annotate: paired frames, paired columns and
    /// the unmatched elements from either model.
    /// </summary>
    public interface IHasResultFields
    {
        IList<ResultField> ResultFields { get; }
    }
}
