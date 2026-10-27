namespace CosmicTranscriber.Utilities;

internal static class FocusViewportVisibility
{
    // Coordinates are physical-scroll DIPs, relative to the viewport after layout.
    internal static double RequiredOffset(double current, double viewportHeight,
        double top, double bottom, double extentHeight)
    {
        if (!double.IsFinite(current) || !double.IsFinite(viewportHeight) ||
            !double.IsFinite(top) || !double.IsFinite(bottom) || !double.IsFinite(extentHeight) ||
            viewportHeight <= 0 || extentHeight < 0 || bottom < top) return current;
        const double padding = 2;
        var desired = current;
        if (bottom - top > Math.Max(0, viewportHeight - 2 * padding)) desired += top;
        else if (top < padding) desired += top - padding;
        else if (bottom > viewportHeight - padding) desired += bottom - viewportHeight + padding;
        return Math.Clamp(desired, 0, Math.Max(0, extentHeight - viewportHeight));
    }
}
