using System.Diagnostics;
using System.Windows;
using CosmicTranscriber.Utilities;

namespace CosmicTranscriber;

public partial class AboutWindow : Window
{
    private const string WebsiteUrl = "https://www.awakeningtoreality.com/";
    private const string PayPalEmail = "sohweiyu@outlook.com";

    public AboutWindow()
    {
        InitializeComponent();
        AboutVersionText.Text = $"Version {AppVersion.DisplayVersion}";
        Title = $"About Cosmic Transcriber {AppVersion.DisplayVersion}";
    }


    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        var availableWidth = Math.Max(320, workArea.Width - 24);
        var availableHeight = Math.Max(320, workArea.Height - 24);
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

    private void OpenWebsite_Click(object sender, RoutedEventArgs e)
        => OpenExternalUri(WebsiteUrl, "Could not open the Awakening to Reality website.");

    private void CopyPayPalEmail_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(PayPalEmail);
            MessageBox.Show(this,
                "The PayPal email address was copied to the clipboard.",
                "Copied",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException("ABOUT", ex);
            MessageBox.Show(this,
                "The email address could not be copied. You can copy it manually:\n\n" +
                PayPalEmail + "\n\n" + ex.Message,
                "Clipboard unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();

    private void OpenExternalUri(string uri, string failureMessage)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException("ABOUT", ex);
            MessageBox.Show(this,
                failureMessage + "\n\n" + uri + "\n\n" + ex.Message,
                "Cannot open link",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
