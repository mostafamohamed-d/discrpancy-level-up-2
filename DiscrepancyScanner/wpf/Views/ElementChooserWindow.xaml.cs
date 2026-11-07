using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>One of the elements under the cursor, as the chooser lists it.</summary>
    public sealed class ElementChoice
    {
        public ElementChoice(PickHit hit)
        {
            ElementViewModel element = hit.Element;
            Element = element;

            Title = CheckNaming.KindOf(element) + " "
                    + (string.IsNullOrEmpty(element.Identifier) ? "—" : element.Identifier);

            // Two of the same kind in the same place is the case this window exists for, so the
            // line under the name has to carry everything that tells them apart: which model
            // has it, and - where it is named on the ETABS side under a different name from the
            // one on show - what it is called there.
            List<string> parts = new List<string>();
            if (!string.IsNullOrEmpty(element.Story))
            {
                parts.Add(element.Story);
            }
            parts.Add(CheckNaming.ModelOf(element));

            // A folded wall and one of its panels sit at the same place and read almost the
            // same, so each says which it is rather than leaving it to be worked out.
            if (element.HasParts)
            {
                parts.Add("the whole wall, " + element.Parts.Count + " panels");
            }
            else if (element.PartOf != null)
            {
                parts.Add("one panel");
            }

            if (element.EtabsUniqueName.Length > 0
                && element.EtabsUniqueName != element.Identifier)
            {
                parts.Add("ETABS " + element.EtabsUniqueName);
            }
            Note = string.Join(" · ", parts);

            Swatch = CheckNaming.SwatchOfState(element.DisplayState, element.Source);
            DistanceText = Math.Round(hit.DistanceMM) + " mm";
        }

        public ElementViewModel Element { get; }
        public string Title { get; }
        public string Note { get; }
        public string Swatch { get; }

        /// <summary>
        /// How far the cursor was from it. Often the only thing separating two elements of the
        /// same kind lying on top of each other.
        /// </summary>
        public string DistanceText { get; }
    }

    /// <summary>
    /// Which of the elements under the cursor was meant.
    ///
    /// A column standing where two beams meet puts several answers under one pixel, and a plain
    /// click has to guess. This is where the guess is taken back: everything within picking
    /// distance, nearest first, so the one that was wanted can be said rather than hunted for by
    /// zooming in until the others move out of the way.
    ///
    /// Nothing is filtered by kind. Two columns in the same place, or an unpaired ETABS beam
    /// lying along the Revit beam it should have paired with, are exactly the cases worth
    /// asking about - and both are two of the same kind.
    /// </summary>
    public partial class ElementChooserWindow : Window
    {
        public ElementChooserWindow(IEnumerable<PickHit> under)
        {
            InitializeComponent();

            list.ItemsSource = under.Select(hit => new ElementChoice(hit)).ToList();
            list.SelectedIndex = 0;
            Loaded += (sender, e) => list.Focus();
        }

        /// <summary>The element that was picked, or null when the window was closed.</summary>
        public ElementViewModel Chosen { get; private set; }

        private void OnSelectClick(object sender, RoutedEventArgs e)
        {
            ElementChoice choice = list.SelectedItem as ElementChoice;
            if (choice == null)
            {
                return;
            }

            Chosen = choice.Element;
            DialogResult = true;
        }
    }
}
