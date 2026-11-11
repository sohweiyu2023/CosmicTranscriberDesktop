using System.Windows;
using System.Windows.Input;
using CosmicTranscriber.Models;
using CosmicTranscriber.Utilities;

namespace CosmicTranscriber;

public partial class QuickStartWindow : Window
{
    private readonly QuickStartGuide _guide = new();
    private readonly Action<string>? _showArea;

    public QuickStartWindow(Action<string>? showArea = null)
    {
        InitializeComponent();
        _showArea = showArea;
        ShowAreaButton.IsEnabled = showArea is not null;
        RenderStep();
        Closed += (_, _) => StartupDiagnostics.Write("TUTORIAL", "Guide closed.");
    }

    private void RenderStep()
    {
        StepNumberText.Text = $"QUICK START  {_guide.Index + 1} / {QuickStartGuide.Steps.Count}";
        StepTitleText.Text = _guide.Current.Title;
        StepBodyText.Text = _guide.Current.Body;
        StepTipText.Text = _guide.Current.Tip;
        BackButton.IsEnabled = _guide.CanGoBack;
        NextButton.Content = _guide.IsLast ? "Finish" : "Next";
        GuideViewport.ScrollToTop();
        StartupDiagnostics.Write("TUTORIAL", "Guide step displayed.");
    }

    private void Back_Click(object sender, RoutedEventArgs e) { _guide.Back(); RenderStep(); }
    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_guide.IsLast) { Close(); return; }
        _guide.Next(); RenderStep();
    }
    private void CloseGuide_Click(object sender, RoutedEventArgs e) => Close();
    private void ShowArea_Click(object sender, RoutedEventArgs e) => _showArea?.Invoke(_guide.Current.Area);
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Never let a tutorial key activate Start or cancel a paid operation in its owner.
        if (e.Key == Key.Escape) { e.Handled = true; Close(); }
    }
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Preserve CenterOwner placement on secondary screens. Only an unowned guide
        // uses the primary work area; do not drag an owned guide across monitors.
        if (Owner is not null) { NextButton.Focus(); return; }
        var work = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, Math.Max(240, work.Width - 24));
        MinHeight = Math.Min(MinHeight, Math.Max(240, work.Height - 24));
        Width = Math.Min(Width, Math.Max(240, work.Width - 24));
        Height = Math.Min(Height, Math.Max(240, work.Height - 24));
        Left = Math.Clamp(double.IsFinite(Left) ? Left : work.Left, work.Left, Math.Max(work.Left, work.Right - Width));
        Top = Math.Clamp(double.IsFinite(Top) ? Top : work.Top, work.Top, Math.Max(work.Top, work.Bottom - Height));
        NextButton.Focus();
    }
}
