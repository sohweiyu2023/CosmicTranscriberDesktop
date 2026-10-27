namespace CosmicTranscriber.Utilities;

// Numeric geometry and fixed source identifiers only; never control text/content.
internal sealed record FocusViewportTrace(string Stage, string ControlId, string ViewportId,
    long Ticket, bool EventHandled)
{
    public double? OffsetX { get; init; }
    public double? OffsetY { get; init; }
    public double? DesiredX { get; init; }
    public double? DesiredY { get; init; }
    public double? TargetLeft { get; init; }
    public double? TargetTop { get; init; }
    public double? TargetWidth { get; init; }
    public double? TargetHeight { get; init; }
    public double? ViewportWidth { get; init; }
    public double? ViewportHeight { get; init; }
    public bool ExpansionFactorResolved { get; init; }
    public double? ExpansionFactor { get; init; }
    public double? ObservationElapsedMs { get; init; }
    public double? StabilityAgeMs { get; init; }
    public double? CombinedVisibleFraction { get; init; }
    public double? VisibleFraction { get; init; }
}
