using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using CosmicTranscriber.Services;
using CosmicTranscriber.Utilities;

namespace CosmicTranscriber;

public partial class RecoveryWindow : Window
{
    private readonly RecoveryReviewService _service;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _busy;

    public RecoveryWindow(string outputDirectory)
    {
        _service = new RecoveryReviewService(outputDirectory);
        InitializeComponent();
        SizeChanged += (_, _) => ObserveGeometry();
        HistoryPathText.Text = "Create-once local history (kept separately from cache): " + _service.HistoryDirectory;
    }
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var area = SystemParameters.WorkArea;
        var availableWidth = Math.Max(320, area.Width - 24);
        var availableHeight = Math.Max(320, area.Height - 24);
        MinWidth = Math.Min(MinWidth, availableWidth);
        MinHeight = Math.Min(MinHeight, availableHeight);
        Width = Math.Min(Width, availableWidth);
        Height = Math.Min(Height, availableHeight);
        Left = Math.Clamp(double.IsFinite(Left) ? Left : area.Left, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = Math.Clamp(double.IsFinite(Top) ? Top : area.Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
        await RefreshAsync();
        ObserveGeometry();
    }
    private long _lastGeometry;
    private void ObserveGeometry()
    {
        var now = Environment.TickCount64;
        if (!IsVisible || now - _lastGeometry < 80) return;
        _lastGeometry = now;
        var dpi=System.Windows.Media.VisualTreeHelper.GetDpi(this);
        StartupDiagnostics.WriteWindow("state_changed",IsVisible,IsActive,WindowState.ToString(),ActualWidth,ActualHeight,dpi.PixelsPerInchX,dpi.PixelsPerInchY,DiagnosticWindowKind.Recovery);
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async Task RefreshAsync()
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            AttemptsGrid.ItemsSource = await _service.InspectAsync(timeout.Token);
            RecoveryStatusText.Text = AttemptsGrid.Items.Count == 0
                ? "No unresolved intents were found in this selected output cache. This is not a billing or refund determination; prior history remains preserved."
                : "Inventory loaded. Select an attempt to inspect its identifiers and recheck local accepted evidence. No request has been sent.";
        }
        catch (Exception error) { ReportFailure(error); }
        finally { SetBusy(false); }
    }
    private async void Recheck_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || AttemptsGrid.SelectedItem is not RecoveryAttempt selected) return;
        SetBusy(true);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var result = await _service.RecheckAsync(selected, timeout.Token);
            if (result.Reconciled)
            {
                AttemptsGrid.ItemsSource = await _service.InspectAsync(timeout.Token);
                AttemptIdentityText.Text = "Reconciled local evidence. Receipt/attempt ID: " + result.ReceiptId?.ToString("N");
            }
            RecoveryStatusText.Text = result.Message;
        }
        catch (Exception error) { ReportFailure(error); }
        finally { SetBusy(false); }
    }
    private void ReportFailure(Exception error)
    {
        StartupDiagnostics.WriteException("Checkpoint", error);
        RecoveryStatusText.Text = "Review is incomplete or could not finish safely. Preserve pending evidence and accepted checkpoints. " +
            "No retry or billing adjustment is authorized by this screen. Check local diagnostic coverage, storage access and any active run before refreshing.";
        // A failed refresh must not leave stale rows looking like a complete/current inventory.
        AttemptsGrid.ItemsSource = null;
        AttemptIdentityText.Text = string.Empty;
    }
    private void Selection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (AttemptsGrid.SelectedItem is RecoveryAttempt item)
            AttemptIdentityText.Text = $"Context: {item.WorkId}\nChunk: {item.Chunk}\nAttempt: {item.AttemptId?.ToString("N") ?? "unavailable"}\nClient request: {item.ClientRequestId ?? "unavailable"}";
        else AttemptIdentityText.Text = string.Empty;
        RecheckRecoveryButton.IsEnabled = !_busy && AttemptsGrid.SelectedItem is RecoveryAttempt;
    }
    private void SetBusy(bool value)
    {
        _busy = value;
        RefreshRecoveryButton.IsEnabled = !value;
        AttemptsGrid.IsEnabled = !value;
        RecheckRecoveryButton.IsEnabled = !value && AttemptsGrid.SelectedItem is RecoveryAttempt;
        CloseRecoveryButton.IsEnabled = !value;
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_busy) { e.Cancel = true; return; }
        _lifetime.Cancel();
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
