using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace CosmicTranscriber.Utilities;

/// <summary>
/// Keeps an open selector list attached to its field while a surrounding page scrolls.
/// Opening a partly hidden selector moves focus to it, and the focus revealer then scrolls the
/// field into view. WPF positions a Popup when it opens and does not follow later scrolling,
/// so without this the list stayed where the field used to be, detached from it.
/// </summary>
internal static class DropDownPlacement
{
    internal static void Attach(ComboBox comboBox, params ScrollViewer[] viewers)
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        foreach (var viewer in viewers.Distinct())
        {
            var source = viewer;
            viewer.ScrollChanged += (_, e) =>
            {
                // ScrollChanged bubbles; react only to this page viewer, not text boxes inside it.
                if (ReferenceEquals(e.OriginalSource, source) && comboBox.IsDropDownOpen && (e.VerticalChange != 0 || e.HorizontalChange != 0))
                    Reposition(comboBox);
            };
        }
    }

    private static void Reposition(ComboBox comboBox)
    {
        if (comboBox.Template?.FindName("PART_Popup", comboBox) is not Popup { IsOpen: true } popup) return;
        // Changing an offset makes Popup recompute its placement against the target's current
        // position; restoring it immediately leaves the configured offset unchanged.
        var offset = popup.HorizontalOffset;
        popup.HorizontalOffset = offset + 1;
        popup.HorizontalOffset = offset;
        StartupDiagnostics.WriteDropDown(comboBox.Name, "popup_repositioned", comboBox.SelectedIndex,
            double.NaN, double.NaN, comboBox.ActualWidth, comboBox.ActualHeight);
    }
}
