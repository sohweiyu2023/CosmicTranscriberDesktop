using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CosmicTranscriber.Utilities;

/// <summary>One bounded inner-to-outer transaction per settings focus change.</summary>
internal sealed class FocusedViewportRevealer : IDisposable
{
    private readonly ScrollViewer _settings;
    private readonly ScrollViewer[] _viewports;
    private readonly Func<IInputElement?> _focusedElement;
    private readonly Func<FrameworkElement, bool> _visible;
    private readonly Action<FocusViewportTrace>? _trace;
    private UIElement? _eventRoot;
    private long _ticket;
    private bool _disposed;
    private readonly bool _observeRender;
    private readonly bool _automaticRender;
    private FrameworkElement? _renderTarget;
    private DispatcherTimer? _renderDeadline;
    private long _renderTicket;
    private int _renderCorrections;
    private double[]? _lastGeometry;
    private double _stableSince;
    private readonly Func<double>? _testElapsedMilliseconds;
    private const double ObservationDeadlineMs = 1000;
    private const double GeometryQuietMs = 80;
    private bool _transactionPending;
    private long _renderStarted;
    private bool _lateTransaction;

    // Optional delegates let off-screen tests model focus without HWNDs or input injection.
    internal FocusedViewportRevealer(ScrollViewer settings, ScrollViewer workspace,
        Action<FocusViewportTrace>? trace = null, Func<IInputElement?>? focusedElement = null,
        Func<FrameworkElement, bool>? visible = null, bool observeRender = true, bool automaticRender = true, Func<double>? elapsedMilliseconds = null)
    {
        _settings = settings;
        _viewports = ReferenceEquals(settings, workspace) ? [settings] : [settings, workspace];
        _trace = trace;
        _focusedElement = focusedElement ?? (() => Keyboard.FocusedElement);
        _visible = visible ?? (element => element.IsVisible);
        _observeRender = observeRender; _automaticRender = automaticRender; _testElapsedMilliseconds = elapsedMilliseconds;
    }

    internal void Attach(UIElement root)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FocusedViewportRevealer));
        if (_eventRoot is not null) throw new InvalidOperationException("Already attached.");
        _eventRoot = root;
        root.AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnFocus), true);
        root.AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnWheel), true);
        root.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(OnPointer), true);
        root.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnScrollKey), true);
    }

    private void OnFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is FrameworkElement target && target.IsDescendantOf(_settings))
        {
            Emit("handler_enter", target, _settings, _ticket + 1, e.Handled);
            Request(target, e.Handled);
        }
        else { ++_ticket; StopRenderObservation(); } // Moving elsewhere cancels outstanding work.
    }

    private void OnWheel(object sender, MouseWheelEventArgs e) { ++_ticket; StopRenderObservation(); }

    private void OnPointer(object sender, MouseButtonEventArgs e) { ++_ticket; StopRenderObservation(); }

    private void OnScrollKey(object sender, KeyEventArgs e) => CancelForScrollKey(e.Key);
    internal void CancelForScrollKey(Key key)
    {
        if (key is Key.Up or Key.Down or Key.Left or Key.Right or Key.PageUp or Key.PageDown or Key.Home or Key.End)
        { ++_ticket; StopRenderObservation(); }
    }

    internal void Request(FrameworkElement target, bool eventHandled = false)
    {
        var ticket = ++_ticket;
        StopRenderObservation();
        _transactionPending = true; _lateTransaction = false;
        if (_observeRender)
        {
            _renderStarted = Stopwatch.GetTimestamp();
            _renderTarget = target; _renderTicket = ticket; _renderCorrections = 0; _lastGeometry = null; _stableSince = 0;
            if (_automaticRender) CompositionTarget.Rendering += OnRendering;
            _renderDeadline = new DispatcherTimer(DispatcherPriority.ContextIdle, target.Dispatcher)
                { Interval = TimeSpan.FromMilliseconds(ObservationDeadlineMs) };
            _renderDeadline.Tick += OnRenderDeadline;
            _renderDeadline.Start();
        }
        Emit("scheduled", target, _settings, ticket, eventHandled);
        Queue(target, ticket, eventHandled, () => Visit(target, ticket, eventHandled, 0));
    }

    private double ElapsedMilliseconds => _testElapsedMilliseconds?.Invoke() ?? Stopwatch.GetElapsedTime(_renderStarted).TotalMilliseconds;

    private void OnRendering(object? sender, EventArgs e) => ObservePostRender();
    private void OnRenderDeadline(object? sender, EventArgs e) => ExpireRenderObservation();
    internal void ExpireRenderObservation() { if (_lateTransaction) ++_ticket; StopRenderObservation(); }

    // Also invoked explicitly by off-screen tests: no synthetic OS focus or HWND required.
    internal void ObservePostRender()
    {
        var target = _renderTarget;
        if (target is null) return;
        if (ElapsedMilliseconds >= ObservationDeadlineMs) { ExpireRenderObservation(); return; }
        if (!Current(target, _renderTicket)) { StopRenderObservation(); return; }
        if (_transactionPending) return;
        foreach (var viewer in _viewports) Emit("post_render", target, viewer, _renderTicket, false);
        try
        {
            var geometry = Geometry(target, out var expanding);
            var now = ElapsedMilliseconds;
            if (_lastGeometry is null || geometry.Length != _lastGeometry.Length ||
                geometry.Where((value, index) => !double.IsFinite(value) || Math.Abs(value - _lastGeometry[index]) > .25).Any())
            { _lastGeometry = geometry; _stableSince = now; return; }
            if (expanding || now - _stableSince < GeometryQuietMs) return;
            if (CombinedFraction(target) >= .98)
            { Emit("stable_visible", target, _settings, _renderTicket, false); StopRenderObservation(); return; }
            if (_renderCorrections >= 2)
            { Emit("stable_unresolved", target, _settings, _renderTicket, false); StopRenderObservation(); return; }
            ++_renderCorrections; _transactionPending = true; _lateTransaction = true; _lastGeometry = null;
            var ticket = _renderTicket;
            Queue(target, ticket, false, () => Visit(target, ticket, false, 0));
        }
        catch (InvalidOperationException)
        { Emit("render_detached", target, _settings, _renderTicket, false); StopRenderObservation(); }
    }

    // Normalize away our scroll offsets so animation/content movement, not scrolling, gates correction.
    private double[] Geometry(FrameworkElement target, out bool expanding)
    {
        var values = new List<double>(); expanding = false;
        foreach (var viewer in _viewports)
        {
            var presenter = Presenter(viewer);
            if (presenter is null) throw new InvalidOperationException();
            var rect = target.TransformToAncestor(presenter).TransformBounds(new Rect(target.RenderSize));
            values.AddRange([rect.Left + viewer.HorizontalOffset, rect.Top + viewer.VerticalOffset,
                rect.Width, rect.Height, presenter.ActualWidth, presenter.ActualHeight, viewer.ExtentWidth, viewer.ExtentHeight]);
        }
        for (DependencyObject? node = target; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is Expander expander && expander.IsExpanded &&
                expander.Template?.FindName("AnimationFactorBorder", expander) is Border factor)
            {
                values.Add(factor.Width);
                // .NET Fluent's expansion factor runs from 1 to 0 while translating its content.
                expanding |= factor.Width > .0001;
            }
        }
        return values.ToArray();
    }

    private static (bool Resolved, double? Factor) ExpansionFactor(FrameworkElement target)
    {
        bool resolved = false; double? factor = null;
        for (DependencyObject? node = target; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is Expander expander && expander.IsExpanded &&
                expander.Template?.FindName("AnimationFactorBorder", expander) is Border border)
            { resolved = true; factor = Math.Max(factor ?? 0, border.Width); }
        return (resolved, factor);
    }

    private void StopRenderObservation()
    {
        CompositionTarget.Rendering -= OnRendering;
        if (_renderDeadline is not null)
        {
            _renderDeadline.Stop(); _renderDeadline.Tick -= OnRenderDeadline; _renderDeadline = null;
        }
        _renderTarget = null;
    }

    internal static double CombinedFraction(FrameworkElement target)
    {
        FrameworkElement root = target;
        while (VisualTreeHelper.GetParent(root) is FrameworkElement parent) root = parent;
        var bounds = target.TransformToAncestor(root).TransformBounds(new Rect(target.RenderSize));
        var visible = Rect.Intersect(bounds, new Rect(root.RenderSize));
        for (DependencyObject? node = target; node is not null && node != root; node = VisualTreeHelper.GetParent(node))
        {
            if (node is not Visual visual) continue;
            var clip = VisualTreeHelper.GetClip(visual);
            if (clip is not null) visible.Intersect(visual.TransformToAncestor(root).TransformBounds(clip.Bounds));
            if (node is ScrollContentPresenter || node is UIElement { ClipToBounds: true })
                visible.Intersect(visual.TransformToAncestor(root).TransformBounds(new Rect(((UIElement)node).RenderSize)));
        }
        var area = bounds.Width * bounds.Height;
        return area > 0 && !visible.IsEmpty ? visible.Width * visible.Height / area : 0;
    }

    private bool Current(FrameworkElement target, long ticket) => !_disposed && ticket == _ticket &&
        (!_lateTransaction || ElapsedMilliseconds < ObservationDeadlineMs) &&
        ReferenceEquals(_focusedElement(), target) && _visible(target) && target.IsDescendantOf(_settings);

    private void Queue(FrameworkElement target, long ticket, bool handled, Action continuation)
    {
        if (target.Dispatcher.HasShutdownStarted || target.Dispatcher.HasShutdownFinished) return;
        target.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (!Current(target, ticket)) { Emit("cancelled", target, _settings, ticket, handled); return; }
            continuation();
        }));
    }

    private void Visit(FrameworkElement target, long ticket, bool handled, int index)
    {
        if (index == _viewports.Length)
        {
            _transactionPending = false;
            foreach (var viewport in _viewports) Emit("complete", target, viewport, ticket, handled);
            return;
        }
        var viewer = _viewports[index];
        if (viewer.CanContentScroll)
        {
            Emit("logical_scroll_skipped", target, viewer, ticket, handled);
            Visit(target, ticket, handled, index + 1);
            return;
        }
        try
        {
            var presenter = Presenter(viewer);
            if (presenter is null || !target.IsDescendantOf(presenter))
            {
                Emit("template_missing", target, viewer, ticket, handled);
                Visit(target, ticket, handled, index + 1);
                return;
            }
            var rect = target.TransformToAncestor(presenter).TransformBounds(new Rect(target.RenderSize));
            var x = FocusViewportVisibility.RequiredOffset(viewer.HorizontalOffset, presenter.ActualWidth,
                rect.Left, rect.Right, viewer.ExtentWidth);
            var y = FocusViewportVisibility.RequiredOffset(viewer.VerticalOffset, presenter.ActualHeight,
                rect.Top, rect.Bottom, viewer.ExtentHeight);
            Emit("before", target, viewer, ticket, handled, x, y);
            if (double.IsFinite(x) && Math.Abs(x - viewer.HorizontalOffset) > .01) viewer.ScrollToHorizontalOffset(x);
            if (double.IsFinite(y) && Math.Abs(y - viewer.VerticalOffset) > .01) viewer.ScrollToVerticalOffset(y);
            Emit("requested", target, viewer, ticket, handled, x, y);
            // ScrollViewer queues its commands. The next idle callback sees their layout result.
            Queue(target, ticket, handled, () =>
            {
                Emit("settled", target, viewer, ticket, handled);
                Visit(target, ticket, handled, index + 1);
            });
        }
        catch (InvalidOperationException)
        {
            Emit("detached", target, viewer, ticket, handled);
        }
    }

    private static FrameworkElement? Presenter(ScrollViewer viewer) =>
        viewer.Template?.FindName("PART_ScrollContentPresenter", viewer) as FrameworkElement;

    private void Emit(string stage, FrameworkElement target, ScrollViewer viewer, long ticket,
        bool handled, double? desiredX = null, double? desiredY = null)
    {
        if (_trace is null) return;
        try
        {
            var expansion = ExpansionFactor(target);
            var sameObservation = ticket == _renderTicket && ReferenceEquals(target, _renderTarget);
            var sample = new FocusViewportTrace(stage, string.IsNullOrEmpty(target.Name) ? "unnamed" : target.Name,
                string.IsNullOrEmpty(viewer.Name) ? "unnamed" : viewer.Name, ticket, handled)
            { OffsetX = viewer.HorizontalOffset, OffsetY = viewer.VerticalOffset,
              DesiredX = desiredX, DesiredY = desiredY, CombinedVisibleFraction = CombinedFraction(target),
              ExpansionFactorResolved = expansion.Resolved, ExpansionFactor = expansion.Factor,
              ObservationElapsedMs = !sameObservation ? null : ElapsedMilliseconds,
              StabilityAgeMs = !sameObservation || _lastGeometry is null ? null : Math.Max(0, ElapsedMilliseconds - _stableSince) };
            var presenter = Presenter(viewer);
            if (presenter is not null && target.IsDescendantOf(presenter))
            {
                var rect = target.TransformToAncestor(presenter).TransformBounds(new Rect(target.RenderSize));
                var clip = Rect.Intersect(rect, new Rect(presenter.RenderSize));
                var area = rect.Width * rect.Height;
                sample = sample with { TargetLeft = rect.Left, TargetTop = rect.Top,
                    TargetWidth = rect.Width, TargetHeight = rect.Height,
                    ViewportWidth = presenter.ActualWidth, ViewportHeight = presenter.ActualHeight,
                    VisibleFraction = area > 0 && !clip.IsEmpty ? clip.Width * clip.Height / area : 0 };
            }
            _trace(sample);
        }
        catch { /* Geometry diagnostics cannot change focus or prevent navigation. */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; ++_ticket;
        StopRenderObservation();
        if (_eventRoot is not null)
        {
            _eventRoot.RemoveHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnFocus));
            _eventRoot.RemoveHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnWheel));
            _eventRoot.RemoveHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(OnPointer));
            _eventRoot.RemoveHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnScrollKey));
            _eventRoot = null;
        }
    }
}
