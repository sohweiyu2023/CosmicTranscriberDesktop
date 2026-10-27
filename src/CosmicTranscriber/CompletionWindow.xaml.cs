using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using CosmicTranscriber.Utilities;

namespace CosmicTranscriber;

public partial class CompletionWindow : Window
{
    private readonly string _outputDirectory;

    public CompletionWindow(
        string outputDirectory,
        int successCount,
        int failureCount,
        int skippedCount,
        int outputFileCount,
        TimeSpan elapsed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        if (successCount < 0 || failureCount < 0 || skippedCount < 0 || outputFileCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(successCount), "Completion counts cannot be negative.");
        }

        InitializeComponent();
        _outputDirectory = Path.GetFullPath(outputDirectory);

        var completedWithErrors = failureCount > 0 || skippedCount > 0;
        Title = completedWithErrors ? "Transcription completed with issues" : "Transcription complete";
        CompletionTitleText.Text = completedWithErrors ? "Run completed with issues" : "Transcription complete";
        CompletionSubtitleText.Text = RunPresentation.CompletionSubtitle(outputFileCount, completedWithErrors);
        IconText.Text = completedWithErrors ? "!" : "✓";
        IconText.Foreground = (Brush)FindResource(completedWithErrors ? "WarningBrush" : "SuccessBrush");
        IconBorder.BorderBrush = IconText.Foreground;

        var elapsedText = elapsed.TotalHours >= 1
            ? elapsed.ToString(@"h\:mm\:ss")
            : elapsed.ToString(@"m\:ss");
        SummaryText.Text =
            $"{successCount} succeeded  •  {failureCount} failed  •  {skippedCount} skipped\n" +
            $"{outputFileCount} output file(s)  •  {elapsedText} elapsed";
        OutputPathTextBox.Text = _outputDirectory;
        OpenFolderButton.IsEnabled = Directory.Exists(_outputDirectory);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        var availableWidth = Math.Max(360, workArea.Width - 24);
        var availableHeight = Math.Max(360, workArea.Height - 24);
        MinWidth = Math.Min(MinWidth, availableWidth);
        MinHeight = Math.Min(MinHeight, availableHeight);
        Width = Math.Min(Width, availableWidth);
        Height = Math.Min(Height, availableHeight);

        var desiredLeft = double.IsFinite(Left)
            ? Left
            : workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
        var desiredTop = double.IsFinite(Top)
            ? Top
            : workArea.Top + Math.Max(0, (workArea.Height - Height) / 2);
        Left = Math.Max(workArea.Left, Math.Min(desiredLeft, workArea.Right - Width));
        Top = Math.Max(workArea.Top, Math.Min(desiredTop, workArea.Bottom - Height));
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Directory.Exists(_outputDirectory))
            {
                OpenFolderButton.IsEnabled = false;
                MessageBox.Show(this,
                    "The output folder no longer exists. Return to the main window and choose another output folder.",
                    "Output folder unavailable",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = _outputDirectory,
                UseShellExecute = true
            });
            StartupDiagnostics.Write("COMPLETION", "Opened output folder from the completion dialog.");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException("COMPLETION OPEN OUTPUT", ex);
            MessageBox.Show(this,
                "Windows could not open the output folder.\n\n" + ex.Message,
                "Cannot open output folder",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();
}
