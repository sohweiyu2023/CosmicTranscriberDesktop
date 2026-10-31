using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CosmicTranscriber.Models;
using CosmicTranscriber.Services;
using CosmicTranscriber.Utilities;
using Microsoft.Win32;

namespace CosmicTranscriber;

public partial class MainWindow : Window
{
    private const int MaximumLogCharacters = 200_000;
    private const int MaximumQueueFiles = 10_000;
    private const int MaximumMergedFiles = 1_000;
    private const long MaximumMergedTranscriptCharacters = TranscriptionCoordinator.MaximumTranscriptCharactersPerFile;
    private static readonly TimeSpan SettingsAutoSaveDelay = TimeSpan.FromMilliseconds(900);
    private readonly AppSettingsService _settingsService = new();
    private readonly FocusedViewportRevealer _focusRevealer;
    private readonly ApiKeyStore _apiKeyStore;
    private readonly OpenAiTranscriptionClient _openAiClient = new();
    private readonly TranscriptionCoordinator _coordinator;
    private readonly TranscriptOutputWriter _outputWriter = new();
    private readonly DispatcherTimer _settingsAutoSaveTimer;
    private AppSettings _settings = new();
    private CancellationTokenSource? _runCancellation;
    private bool _isRunning;
    private bool _isImporting;
    private bool _isTestingKey;
    private bool _synchronizingKey;
    private bool _isClosing;
    private bool _xamlInitialized;
    private bool _constructionCompleted;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private string _apiKeySource = "Not configured";
    private bool _dragDropUnavailableBecauseElevated;
    private bool _dragDropCompatibilityFlagDetected;
    private string _dragDropCompatibilityFlagSource = string.Empty;
    private bool _firstExternalDragLogged;
    private bool _closeRequestedAfterUnwind;
    private bool _allowFinalClose;
    private bool _settingsSaveFailureLogged;

    private bool IsUiIdle => !_isRunning && !_isImporting && !_isTestingKey && !_isClosing;

    public MainWindow()
    {
        StartupDiagnostics.Write("MAINWINDOW", "Constructor entered; creating application services.");
        _apiKeyStore = new ApiKeyStore(_settingsService.SettingsDirectory);
        _coordinator = new TranscriptionCoordinator(new Mp3Chunker(), _openAiClient);

        StartupDiagnostics.Write("MAINWINDOW", "Initializing XAML.");
        InitializeComponent();
        _focusRevealer = new FocusedViewportRevealer(SettingsViewport, WorkspaceViewport, StartupDiagnostics.WriteViewportFocus);
        _focusRevealer.Attach(this);
        Closed += (_, _) => _focusRevealer.Dispose();
        _settingsAutoSaveTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = SettingsAutoSaveDelay
        };
        _settingsAutoSaveTimer.Tick += SettingsAutoSaveTimer_Tick;
        RegisterHandledDragDropRoutes();
        Title = $"Cosmic Transcriber {AppVersion.DisplayVersion}";
        HeaderVersionText.Text = $"v{AppVersion.DisplayVersion}";
        _xamlInitialized = true;
        StartupDiagnostics.Write("MAINWINDOW", "InitializeComponent completed.");
        DataContext = this;

        StartupDiagnostics.Write("MAINWINDOW", $"Loading settings from {_settingsService.SettingsPath}.");
        _settings = _settingsService.Load();

        FitWindowToWorkArea();

        StartupDiagnostics.Write("MAINWINDOW", "Applying settings to controls.");
        ApplySettingsToControls();
        StartupDiagnostics.Write("MAINWINDOW", "Loading initial API-key state.");
        LoadInitialApiKey();
        Files.CollectionChanged += (_, _) => UpdateQueueSummary();
        UpdateQueueSummary();
        AppendLog("Application ready. No network request is made until you test the key or start transcription.");
        AppendLog("Persistent diagnostic log: " + StartupDiagnostics.LogPath);
        _constructionCompleted = true;
        RegisterSettingsAutoSaveHandlers();
        AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(DiagnosticFocusChanged), true);
        Activated += (_, _) => DiagnosticWindowState("activated");
        Deactivated += (_, _) => DiagnosticWindowState("deactivated");
        StateChanged += (_, _) => DiagnosticWindowState("state_changed");
        SetSettingsSaveStatus("Settings loaded · autosave on", "SuccessBrush");
        StartupDiagnostics.Write("MAINWINDOW", "Constructor completed.");
    }

    public ObservableCollection<AudioJobItem> Files { get; } = [];

    private static App CurrentApp
        => Application.Current as App
            ?? throw new InvalidOperationException("The WPF application instance is unavailable.");

    private void RegisterHandledDragDropRoutes()
    {
        // Child controls can mark drag routed events handled before they reach ordinary
        // Window XAML handlers. handledEventsToo keeps the whole idle window usable as
        // a drop target, including over the queue and settings controls.
        AddHandler(DragDrop.PreviewDragEnterEvent, new DragEventHandler(Window_PreviewDragOver), handledEventsToo: true);
        AddHandler(DragDrop.PreviewDragOverEvent, new DragEventHandler(Window_PreviewDragOver), handledEventsToo: true);
        AddHandler(DragDrop.PreviewDragLeaveEvent, new DragEventHandler(Window_PreviewDragLeave), handledEventsToo: true);
        AddHandler(DragDrop.PreviewDropEvent, new DragEventHandler(Window_PreviewDrop), handledEventsToo: true);
    }

    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning || _isImporting || _isTestingKey || _isClosing)
        {
            return;
        }

        string[] selectedPaths;
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Choose MP3 files",
                Filter = "MP3 audio (*.mp3)|*.mp3|All files (*.*)|*.*",
                Multiselect = true,
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            selectedPaths = dialog.FileNames;
        }
        catch (Exception ex)
        {
                StartupDiagnostics.WriteException("Startup", ex);
            if (!_isClosing)
            {
                AppendLog("Could not open the file picker: " + InputValidation.SingleLineForLog(ex.Message));
                MessageBox.Show(this, ex.Message, "Cannot choose audio files",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return;
        }

        await AddInputPathsAsync(selectedPaths);
    }

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning || _isImporting || _isTestingKey || _isClosing)
        {
            return;
        }

        string selectedPath;
        try
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Choose a folder containing MP3 files",
                Multiselect = false
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            selectedPath = dialog.FolderName;
        }
        catch (Exception ex)
        {
                StartupDiagnostics.WriteException("Startup", ex);
            if (!_isClosing)
            {
                AppendLog("Could not open the folder picker: " + InputValidation.SingleLineForLog(ex.Message));
                MessageBox.Show(this, ex.Message, "Cannot choose an audio folder",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return;
        }

        await AddInputPathsAsync([selectedPath]);
    }

    private async void PastePaths_Click(object sender, RoutedEventArgs e)
    {
        if (!IsUiIdle)
        {
            return;
        }

        try
        {
            if (!Clipboard.ContainsFileDropList())
            {
                MessageBox.Show(this,
                    "No copied files or folders were found. In File Explorer, select MP3 files or folders, press Ctrl+C, then choose Paste here.",
                    "Nothing to paste", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var paths = Clipboard.GetFileDropList()
                .Cast<string>()
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToArray();
            if (paths.Length == 0)
            {
                return;
            }

            AppendLog($"Pasting {paths.Length} copied file or folder path(s) into the audio queue.");
            await AddInputPathsAsync(paths);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException("CLIPBOARD", ex);
            if (!_isClosing)
            {
                AppendLog("Could not read copied file paths: " + InputValidation.SingleLineForLog(ex.Message));
                MessageBox.Show(this,
                    "Windows could not read the copied file list. Copy the files again in File Explorer, or use the Files / Folder buttons.\n\n" + ex.Message,
                    "Cannot paste files", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void RestartNormally_Click(object sender, RoutedEventArgs e)
    {
        if (!IsUiIdle)
        {
            return;
        }

        var confirmation = MessageBox.Show(this,
            "Windows blocks drag-and-drop from normal File Explorer into an administrator-level app. " +
            "Cosmic Transcriber can ask the normal Explorer shell to reopen it without administrator privileges.\n\n" +
            "The current window will close. Continue?",
            "Restart normally",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        if (_dragDropCompatibilityFlagDetected
            && _dragDropCompatibilityFlagSource.Contains("machine-wide", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this,
                "A machine-wide Windows Compatibility rule is forcing this executable to run as administrator. " +
                "Cosmic Transcriber will not alter a machine-wide rule automatically.\n\n" +
                "Open executable Properties > Compatibility > Change settings for all users, clear " +
                "'Run this program as an administrator', then launch the app normally.",
                "Machine-wide administrator setting",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!WindowsSecurity.TryClearCurrentUserRunAsAdminCompatibilityFlag(out var compatibilityChanged, out var compatibilityError))
        {
            AppendLog("Could not clear the current-user administrator compatibility option: " +
                      InputValidation.SingleLineForLog(compatibilityError));
            MessageBox.Show(this,
                "Cosmic Transcriber could not clear the current-user 'Run this program as an administrator' setting.\n\n" +
                compatibilityError + "\n\nClear it manually under executable Properties > Compatibility, then launch the app normally.",
                "Cannot remove administrator compatibility",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!WindowsSecurity.TryLaunchCurrentProcessFromExplorer(out var launchError))
        {
            AppendLog("Normal-privilege relaunch failed: " + InputValidation.SingleLineForLog(launchError));
            MessageBox.Show(this,
                "Windows could not restart Cosmic Transcriber through the normal Explorer shell.\n\n" +
                launchError + "\n\nClose this window and start the EXE by double-clicking it in File Explorer.",
                "Cannot restart normally",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        AppendLog(compatibilityChanged
            ? "Cleared the current-user administrator compatibility option and requested a normal relaunch."
            : "Requested a normal relaunch through Windows Explorer.");
        _allowFinalClose = true;
        Close();
    }

    private void DragDropHelp_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "Windows intentionally blocks drag-and-drop between applications running at different privilege levels.\n\n" +
            "Use Restart normally in the warning banner. If that does not work:\n" +
            "1. Close Cosmic Transcriber.\n" +
            "2. Right-click CosmicTranscriber.exe or its shortcut, choose Properties, then Compatibility.\n" +
            "3. Clear 'Run this program as an administrator'.\n" +
            "4. Start the app by double-clicking it in File Explorer.\n\n" +
            "Fallback: copy MP3 files or folders in File Explorer, then press Ctrl+V or choose Paste.",
            "Drag-and-drop help", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ClearQueue_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning || _isImporting || _isTestingKey)
        {
            return;
        }

        Files.Clear();
        OverallProgressBar.IsIndeterminate = false;
        OverallProgressBar.Value = 0;
        ProgressPercentText.Text = "0%";
        CurrentActionText.Text = "Waiting";
        AppendLog("Queue cleared.");
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning || _isImporting || _isTestingKey || sender is not Button { Tag: AudioJobItem item })
        {
            return;
        }

        Files.Remove(item);
        AppendLog($"Removed {item.FileName} from the queue.");
    }

    private void MoveItemUp_Click(object sender, RoutedEventArgs e)
        => MoveSelectedItem(-1);

    private void MoveItemDown_Click(object sender, RoutedEventArgs e)
        => MoveSelectedItem(1);

    private void MoveSelectedItem(int offset)
    {
        if (_isRunning || _isImporting || _isTestingKey || FilesListView.SelectedItem is not AudioJobItem item)
        {
            return;
        }

        var oldIndex = Files.IndexOf(item);
        var newIndex = oldIndex + offset;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= Files.Count)
        {
            return;
        }

        Files.Move(oldIndex, newIndex);
        FilesListView.SelectedItem = item;
        FilesListView.ScrollIntoView(item);
        AppendLog($"Moved {item.FileName} to queue position {newIndex + 1}.");
    }

    private bool CanAcceptFileDrop(DragEventArgs e, out string[] paths)
    {
        paths = [];
        return IsUiIdle
            && (e.AllowedEffects & DragDropEffects.Copy) != 0
            && TryGetFileDropPaths(e.Data, out paths);
    }

    private static bool TryGetFileDropPaths(IDataObject dataObject, out string[] paths)
    {
        paths = [];
        try
        {
            if (!dataObject.GetDataPresent(DataFormats.FileDrop, true)
                || dataObject.GetData(DataFormats.FileDrop, true) is not string[] rawPaths)
            {
                return false;
            }

            paths = rawPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToArray();
            return paths.Length > 0;
        }
        catch
        {
            // Shell/OLE data objects can fail while a drag is in progress. Never let a
            // malformed external drag crash the UI or repeatedly display dialogs.
            return false;
        }
    }

    private void SetDropZoneFeedback(bool active)
    {
        DropZoneRectangle.StrokeThickness = active ? 2.4 : 1.4;
        DropZoneRectangle.Opacity = active ? 1.0 : 0.86;
        DropZoneIconText.Text = active ? "↓" : "⇩";

        if (active)
        {
            DropZoneTitleText.Text = "Release anywhere in this window to add audio";
            DropZoneHelpText.Text = "MP3 files and folders will be added to the queue";
            return;
        }

        if (_dragDropUnavailableBecauseElevated || _dragDropCompatibilityFlagDetected)
        {
            DropZoneTitleText.Text = "Windows is blocking drag-and-drop at this privilege level";
            DropZoneHelpText.Text = "Choose Restart normally below, or copy files in Explorer and choose Paste";
            return;
        }

        DropZoneTitleText.Text = "Drop MP3 files or folders anywhere in this window";
        DropZoneHelpText.Text = "Or copy them in File Explorer and choose Paste";
    }

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        var accepted = CanAcceptFileDrop(e, out var paths);
        e.Effects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        SetDropZoneFeedback(accepted);
        if (!_firstExternalDragLogged)
        {
            _firstExternalDragLogged = true;
            AppendLog(accepted
                ? $"External drag detected with {paths.Length} file or folder path(s); Copy effect accepted."
                : "External drag detected, but it did not contain an accessible Windows file-drop list or the app was busy.");
        }
        e.Handled = true;
    }

    private void Window_PreviewDragLeave(object sender, DragEventArgs e)
    {
        SetDropZoneFeedback(false);
        e.Handled = true;
    }

    private async void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var accepted = CanAcceptFileDrop(e, out var paths);
        e.Effects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        SetDropZoneFeedback(false);
        if (!accepted)
        {
            return;
        }

        try
        {
            AppendLog($"Dropped {paths.Length} file or folder path(s) into the application window.");
            await AddInputPathsAsync(paths);
        }
        catch (OperationCanceledException) when (_isClosing)
        {
        }
        catch (Exception ex)
        {
            if (!_isClosing)
            {
                AppendLog("Could not read dropped file paths: " + InputValidation.SingleLineForLog(ex.Message));
                MessageBox.Show(this, ex.Message, "Cannot add dropped audio files",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async Task AddInputPathsAsync(IEnumerable<string> inputPaths)
    {
        if (_isRunning || _isImporting || _isTestingKey || _isClosing)
        {
            return;
        }

        var remainingCapacity = MaximumQueueFiles - Files.Count;
        if (remainingCapacity <= 0)
        {
            MessageBox.Show(this, $"The queue is limited to {MaximumQueueFiles:N0} files.",
                "Queue limit reached", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _isImporting = true;
        UpdateInteractiveState();
        HeaderStatusText.Text = "Scanning for MP3 files";
        CurrentActionText.Text = "Scanning dropped or selected paths…";
        OverallProgressBar.IsIndeterminate = true;
        Mouse.OverrideCursor = Cursors.Wait;
        var cancellationToken = _lifetimeCancellation.Token;

        try
        {
            string? excludedCacheRoot = null;
            try
            {
                excludedCacheRoot = CheckpointCacheService.GetCacheRoot(ResolveOutputPath());
            }
            catch
            {
                // An invalid output path should not block audio import; run validation reports it later.
            }

            var candidates = await Task.Run(() => ExpandMp3Paths(inputPaths, excludedCacheRoot, cancellationToken)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(remainingCapacity + 1)
                .OrderBy(path => path, NaturalStringComparer.Instance)
                .ToArray(), cancellationToken);

            if (candidates.Length > remainingCapacity)
            {
                MessageBox.Show(this,
                    $"The selection contains more files than the {MaximumQueueFiles:N0}-file queue limit. " +
                    $"Only the first {remainingCapacity:N0} candidates will be inspected.",
                    "Large folder selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                candidates = candidates.Take(remainingCapacity).ToArray();
            }

            if (candidates.Length == 0)
            {
                MessageBox.Show(this,
                    "No MP3 files were found. This desktop version imports MP3 audio files only. " +
                    "For a video or another audio format, first export or convert its audio to MP3, then add that file.",
                    "Cosmic Transcriber",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            HeaderStatusText.Text = "Inspecting audio";
            OverallProgressBar.IsIndeterminate = false;
            OverallProgressBar.Value = 0;
            ProgressPercentText.Text = "0%";
            var added = 0;
            var failed = new List<string>();
            var existingPaths = Files.Select(item => item.SourcePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            for (var candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = candidates[candidateIndex];
                HeaderStatusText.Text = $"Inspecting {candidateIndex + 1} of {candidates.Length}";
                var inspectionFraction = (candidateIndex + 1d) / candidates.Length;
                OverallProgressBar.Value = inspectionFraction * 100;
                ProgressPercentText.Text = $"{inspectionFraction:P0}";
                CurrentActionText.Text = $"Inspecting {Path.GetFileName(path)}";
                if (!existingPaths.Add(path))
                {
                    continue;
                }

                try
                {
                    var metadata = await Task.Run(() => Mp3MetadataService.Inspect(path), cancellationToken);
                    Files.Add(new AudioJobItem
                    {
                        SourcePath = Path.GetFullPath(path),
                        FileName = Path.GetFileName(path) ?? "audio.mp3",
                        SizeBytes = metadata.SizeBytes,
                        Duration = metadata.Duration,
                        LastWriteTimeUtc = metadata.LastWriteTimeUtc,
                        SourceSha256 = metadata.Sha256,
                        Detail = "Ready"
                    });
                    added++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed.Add($"{Path.GetFileName(path)} — {InputValidation.SingleLineForLog(ex.Message)}");
                }
            }

            if (added > 0)
            {
                AppendLog($"Added {added} MP3 file(s) to the queue.");
            }

            if (failed.Count > 0 && !_isClosing)
            {
                AppendLog($"Could not inspect {failed.Count} file(s). See the warning dialog.");
                MessageBox.Show(this,
                    "Some files could not be opened as MP3 audio:\n\n" + string.Join("\n", failed.Take(8)),
                    "Audio import warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (OperationCanceledException) when (_isClosing)
        {
            // The window is closing. Do not show a late dialog.
        }
        catch (Exception ex)
        {
            if (!_isClosing)
            {
                MessageBox.Show(this, ex.Message, "Could not scan the selected paths",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            _isImporting = false;
            Mouse.OverrideCursor = null;
            if (!_isClosing)
            {
                OverallProgressBar.IsIndeterminate = false;
                OverallProgressBar.Value = 0;
                ProgressPercentText.Text = "0%";
                CurrentActionText.Text = "Waiting";
                HeaderStatusText.Text = "Ready for audio";
                UpdateInteractiveState();
            }
            TryCompleteDeferredClose();
        }
    }

    private static IEnumerable<string> ExpandMp3Paths(
        IEnumerable<string> inputPaths,
        string? excludedCacheRoot,
        CancellationToken cancellationToken)
    {
        foreach (var rawPath in inputPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                continue;
            }

            if (File.Exists(rawPath))
            {
                if (string.Equals(Path.GetExtension(rawPath), ".mp3", StringComparison.OrdinalIgnoreCase))
                {
                    var fullPath = Path.GetFullPath(rawPath);
                    if (!IsUnderDirectory(fullPath, excludedCacheRoot))
                    {
                        yield return fullPath;
                    }
                }
                continue;
            }

            if (!Directory.Exists(rawPath))
            {
                continue;
            }

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false,
                MatchCasing = MatchCasing.CaseInsensitive,
                AttributesToSkip = FileAttributes.ReparsePoint
            };

            foreach (var file in Directory.EnumerateFiles(rawPath, "*.mp3", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fullPath = Path.GetFullPath(file);
                if (!IsUnderDirectory(fullPath, excludedCacheRoot))
                {
                    yield return fullPath;
                }
            }
        }
    }

    private static bool IsUnderDirectory(string path, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        return SafeFileSystem.IsPathWithinDirectory(path, directory);
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning || _isImporting || _isTestingKey || _isClosing)
        {
            return;
        }

        try
        {
            var candidate = Environment.ExpandEnvironmentVariables(OutputPathTextBox.Text.Trim());
            var initialDirectory = Directory.Exists(candidate)
                ? Path.GetFullPath(candidate)
                : OutputDirectoryService.ResolveDefault();

            var dialog = new OpenFolderDialog
            {
                Title = "Choose transcript output folder",
                Multiselect = false,
                InitialDirectory = initialDirectory
            };

            if (dialog.ShowDialog(this) == true)
            {
                OutputPathTextBox.Text = dialog.FolderName;
            }
        }
        catch (Exception ex)
        {
            if (!_isClosing)
            {
                AppendLog("Could not open the output-folder picker: " + InputValidation.SingleLineForLog(ex.Message));
                MessageBox.Show(this,
                    "Cosmic Transcriber could not open the folder chooser with the current output path.\n\n" + ex.Message,
                    "Cannot choose output folder", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ReviewUncertain_Click(object sender, RoutedEventArgs e)
    {
        if (!IsUiIdle) return;
        try { new RecoveryWindow(ResolveOutputPath()) { Owner = this }.ShowDialog(); }
        catch (Exception error) {
            StartupDiagnostics.WriteException("Checkpoint", error);
            MessageBox.Show(this, "Recovery review could not open safely. Preserve the cache and inspect local diagnostic coverage.",
                "Recovery review unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ClearCheckpoints_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning || _isImporting || _isTestingKey || _isClosing)
        {
            return;
        }

        try
        {
            var outputPath = ResolveOutputPath();
            if (!CheckpointCacheService.CacheExists(outputPath))
            {
                MessageBox.Show(this, "There are no saved checkpoints in the selected output folder.",
                    "Checkpoint cache", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var queuedInsideCache = Files
                .Where(item => CheckpointCacheService.IsPathInsideCache(item.SourcePath, outputPath))
                .Select(item => item.FileName)
                .Take(8)
                .ToArray();
            if (queuedInsideCache.Length > 0)
            {
                MessageBox.Show(this,
                    "Checkpoint clearing was blocked because queued source audio is inside Cosmic Transcriber's internal cache folder. " +
                    "Move the source files elsewhere, remove and re-add them, then retry.\n\n" +
                    string.Join("\n", queuedInsideCache),
                    "Source audio inside checkpoint cache",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(this,
                "Delete all partial transcript checkpoints for this output folder? Existing exported TXT and Markdown files will not be changed.",
                "Clear checkpoints",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            _isImporting = true;
            UpdateInteractiveState();
            HeaderStatusText.Text = "Clearing checkpoints";
            var deleted = await CheckpointCacheService.ClearAsync(outputPath, _lifetimeCancellation.Token);
            AppendLog($"Checkpoint cache cleared ({deleted} item(s) removed).");
        }
        catch (OperationCanceledException) when (_isClosing)
        {
        }
        catch (IOException ex)
        {
            if (!_isClosing)
            {
                MessageBox.Show(this,
                    "Checkpoints are currently in use by another Cosmic Transcriber instance, or could not be removed.\n\n" + ex.Message,
                    "Could not clear checkpoints", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            if (!_isClosing)
            {
                MessageBox.Show(this, ex.Message, "Could not clear checkpoints",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            _isImporting = false;
            if (!_isClosing)
            {
                HeaderStatusText.Text = "Ready for audio";
                UpdateInteractiveState();
            }
            TryCompleteDeferredClose();
        }
    }

    private void OpenOutput_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = ResolveOutputPath();
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Cannot open output folder",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ComboBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ComboBox comboBox && !comboBox.IsDropDownOpen)
        {
            // Prevent a closed model/retry selector from changing silently, but preserve the
            // user's original intent by forwarding the wheel gesture to the surrounding page.
            e.Handled = true;
            if (comboBox.Parent is UIElement parent)
            {
                parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = Mouse.MouseWheelEvent
                });
            }
        }
    }

    private void ModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_xamlInitialized || PricePerMinuteTextBox is null)
        {
            return;
        }

        PricePerMinuteTextBox.Text = TranscriptionModelCatalog
            .DefaultPricePerMinute(CurrentModel)
            .ToString("0.####", CultureInfo.InvariantCulture);
        UpdateModelSpecificControls();
        UpdateQueueSummary();
        ScheduleSettingsAutoSave();
    }

    private void UpdateModelSpecificControls()
    {
        if (!_xamlInitialized
            || KeywordsTextBox is null
            || KeywordSupportText is null
            || PromptTextBox is null
            || PreviousContextCheckBox is null
            || PromptResetButton is null
            || ChunkDurationSlider is null
            || ChunkDurationText is null
            || ModelCapabilityText is null)
        {
            return;
        }

        var model = CurrentModel;
        var supportsKeywords = TranscriptionModelCatalog.SupportsKeywordHints(model);
        var supportsPrompt = TranscriptionModelCatalog.SupportsPrompt(model);
        var supportsPreviousContext = TranscriptionModelCatalog.SupportsPreviousContext(model);
        var diarizesSpeakers = TranscriptionModelCatalog.SupportsSpeakerDiarization(model);

        // Preserve entered guidance when changing models, but disable fields that the selected
        // endpoint cannot accept. Switching back restores the content.
        KeywordsTextBox.IsEnabled = IsUiIdle && !diarizesSpeakers;
        PromptTextBox.IsEnabled = IsUiIdle && supportsPrompt;
        PromptResetButton.IsEnabled = IsUiIdle && supportsPrompt;
        PreviousContextCheckBox.IsEnabled = IsUiIdle && supportsPreviousContext;
        ChunkDurationSlider.IsEnabled = IsUiIdle;
        ChunkDurationText.Text = $"{(int)ChunkDurationSlider.Value} min";

        if (diarizesSpeakers)
        {
            KeywordSupportText.Text =
                "Speaker-labelled mode uses diarized_json. Prompt, keyword hints, and previous-chunk prompt context are not sent.";
            ModelCapabilityText.Text =
                "Speaker-labelled mode: OpenAI returns Speaker A/B turns with start/end timestamps. " +
                "Long recordings are split into bounded uploads; labels are scoped to each Part because separate API requests cannot guarantee cross-part identity.";
        }
        else
        {
            KeywordSupportText.Text = supportsKeywords
                ? "Structured keyword hints are sent as keywords[] with gpt-transcribe."
                : "This legacy model does not accept structured keyword hints. Clear the field or select gpt-transcribe before starting.";
            ModelCapabilityText.Text = string.Equals(model, TranscriptionModelCatalog.DefaultFileModel, StringComparison.OrdinalIgnoreCase)
                ? "Recommended default for accurate file transcription, multiple language hints, keywords, and recording context."
                : "General file transcription without speaker labels. Choose gpt-4o-transcribe-diarize when you need to distinguish speakers.";
        }
    }

    private void PricePerMinuteTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_xamlInitialized)
        {
            return;
        }

        UpdateQueueSummary();
        ScheduleSettingsAutoSave();
    }

    private void ChunkSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ChunkSizeText is not null)
        {
            ChunkSizeText.Text = $"{(int)e.NewValue} MB";
        }

        ScheduleSettingsAutoSave();
    }

    private void ChunkDurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ChunkDurationText is not null)
        {
            ChunkDurationText.Text = $"{(int)e.NewValue} min";
        }

        ScheduleSettingsAutoSave();
    }

    private void PromptReset_Click(object sender, RoutedEventArgs e)
    {
        PromptTextBox.Text = AppSettings.DefaultPrompt;
    }

    private void LoadInitialApiKey()
    {
        var result = _apiKeyStore.LoadPreferred();
        SetApiKey(result.Key, result.Source);
        RememberKeyCheckBox.IsChecked = _settings.RememberKeySecurely || _apiKeyStore.HasProtectedKey;
    }

    private void SetApiKey(string key, string source)
    {
        _synchronizingKey = true;
        try
        {
            ApiPasswordBox.Password = key;
            ApiKeyRevealTextBox.Clear();
            ApiKeyRevealTextBox.Visibility = Visibility.Collapsed;
            ApiPasswordBox.Visibility = Visibility.Visible;
            RevealKeyButton.Content = "Show";
        }
        finally
        {
            _synchronizingKey = false;
        }

        _apiKeySource = source;
        ApiKeySourceText.Text = source;
    }

    private string GetApiKey()
        => ApiKeyRevealTextBox.Visibility == Visibility.Visible
            ? ApiKeyRevealTextBox.Text.Trim()
            : ApiPasswordBox.Password.Trim();

    private void ApiPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_xamlInitialized || _synchronizingKey)
        {
            return;
        }

        _apiKeySource = "Edited for this session";
        ApiKeySourceText.Text = _apiKeySource;
    }

    private void ApiKeyRevealTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_xamlInitialized || _synchronizingKey || ApiKeyRevealTextBox.Visibility != Visibility.Visible)
        {
            return;
        }

        _apiKeySource = "Edited for this session";
        ApiKeySourceText.Text = _apiKeySource;
    }

    private void RevealKey_Click(object sender, RoutedEventArgs e)
    {
        var reveal = ApiKeyRevealTextBox.Visibility != Visibility.Visible;
        _synchronizingKey = true;
        try
        {
            if (reveal)
            {
                ApiKeyRevealTextBox.Text = ApiPasswordBox.Password;
                ApiPasswordBox.Clear();
                ApiPasswordBox.Visibility = Visibility.Collapsed;
                ApiKeyRevealTextBox.Visibility = Visibility.Visible;
                ApiKeyRevealTextBox.Focus();
                ApiKeyRevealTextBox.CaretIndex = ApiKeyRevealTextBox.Text.Length;
                RevealKeyButton.Content = "Hide";
            }
            else
            {
                ApiPasswordBox.Password = ApiKeyRevealTextBox.Text;
                ApiKeyRevealTextBox.Clear();
                ApiKeyRevealTextBox.Visibility = Visibility.Collapsed;
                ApiPasswordBox.Visibility = Visibility.Visible;
                ApiPasswordBox.Focus();
                RevealKeyButton.Content = "Show";
            }
        }
        finally
        {
            _synchronizingKey = false;
        }
    }

    private void UseEnvironmentKey_Click(object sender, RoutedEventArgs e)
    {
        var result = _apiKeyStore.LoadEnvironment();
        if (string.IsNullOrWhiteSpace(result.Key))
        {
            MessageBox.Show(this,
                "OPENAI_API_KEY was not found or is malformed. Your current session key was left unchanged.",
                "Environment key not available",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        SetApiKey(result.Key, result.Source);
    }

    private void ForgetSavedKey_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var sessionKey = GetApiKey();
            _apiKeyStore.ForgetProtected();
            RememberKeyCheckBox.IsChecked = false;
            _apiKeySource = string.IsNullOrWhiteSpace(sessionKey) ? "Not configured" : "Session only; saved copy removed";
            ApiKeySourceText.Text = _apiKeySource;
            AppendLog("Securely stored local API key was removed. The current in-memory key and environment variable were not changed.");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException("Authentication", ex);
            MessageBox.Show(this, ex.Message, "Could not forget saved key",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void TestKey_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning || _isImporting || _isTestingKey || _isClosing)
        {
            return;
        }

        string key;
        try
        {
            key = InputValidation.NormalizeApiKey(GetApiKey());
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, "Invalid API key",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var model = CurrentModel;
        _isTestingKey = true;
        UpdateInteractiveState();
        TestKeyButton.Content = "Testing…";
        HeaderStatusText.Text = "Testing OpenAI";
        CurrentActionText.Text = "Testing API key and model access…";
        OverallProgressBar.IsIndeterminate = true;

        try
        {
            await _openAiClient.TestConnectionAsync(
                key,
                model,
                TimeSpan.FromSeconds(45),
                _lifetimeCancellation.Token);
            var secureSaveWarning = string.Empty;
            if (RememberKeyCheckBox.IsChecked == true)
            {
                try
                {
                    _apiKeyStore.SaveProtected(key);
                    _apiKeySource = "Secure local vault";
                    ApiKeySourceText.Text = _apiKeySource;
                }
                catch (Exception ex)
                {
                    secureSaveWarning = "\n\nThe connection succeeded, but the key could not be saved securely: " + ex.Message;
                }
            }

            AppendLog($"OpenAI connection test passed for {model}. No transcription was billed.");
            if (!_isClosing)
            {
                MessageBox.Show(this,
                    "The API key and selected transcription model are available." + secureSaveWarning,
                    secureSaveWarning.Length == 0 ? "Connection successful" : "Connection successful with warning",
                    MessageBoxButton.OK,
                    secureSaveWarning.Length == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
        }
        catch (OperationCanceledException) when (_isClosing)
        {
        }
        catch (OperationCanceledException)
        {
            const string message = "The OpenAI connection test was canceled unexpectedly. Check the diagnostic log for details.";
            StartupDiagnostics.Write("OPENAI CONNECTION TEST", message);
            AppendLog(message);
            if (!_isClosing)
            {
                MessageBox.Show(this, message, "Connection test canceled",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException("OPENAI CONNECTION TEST", ex);
            AppendLog("OpenAI connection test failed: " + InputValidation.SingleLineForLog(ex.Message));
            if (!_isClosing)
            {
                MessageBox.Show(this, ex.Message, "Connection test failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            _isTestingKey = false;
            if (!_isClosing)
            {
                UpdateInteractiveState();
                TestKeyButton.Content = "Test connection";
                OverallProgressBar.IsIndeterminate = false;
                OverallProgressBar.Value = 0;
                ProgressPercentText.Text = "0%";
                CurrentActionText.Text = "Waiting";
                HeaderStatusText.Text = "Ready for audio";
            }
            TryCompleteDeferredClose();
        }
    }

    private void ApiKeyShortcut_Click(object sender, RoutedEventArgs e)
    {
        ApiKeySettingsCard.BringIntoView();
        var keyControl = ApiKeyRevealTextBox.Visibility == Visibility.Visible
            ? (Control)ApiKeyRevealTextBox
            : ApiPasswordBox;
        keyControl.Focus();
        Keyboard.Focus(keyControl);
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        var app = CurrentApp;
        var nextTheme = app.CurrentTheme == "Cosmic Night" ? "Cosmic Paper" : "Cosmic Night";
        app.ApplyTheme(nextTheme);
        ThemeToggleButton.Content = nextTheme == "Cosmic Night" ? "☾  Cosmic Night" : "☀  Cosmic Paper";
        _settings.Theme = nextTheme;
        ScheduleSettingsAutoSave();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var aboutWindow = new AboutWindow
        {
            Owner = this
        };
        aboutWindow.ShowDialog();
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning || _isImporting || _isTestingKey || _isClosing)
        {
            return;
        }

        if (!ValidateRun(out var outputDirectory, out var apiKey, out var pricePerMinute))
        {
            return;
        }

        var selectedModel = CurrentModel;
        var usesSpeakerDiarization =
            TranscriptionModelCatalog.SupportsSpeakerDiarization(selectedModel);
        var request = new TranscriptionRequest
        {
            ApiKey = apiKey,
            Model = selectedModel,
            Prompt = TranscriptionModelCatalog.SupportsPrompt(selectedModel)
                ? PromptTextBox.Text.Trim()
                : string.Empty,
            Language = InputValidation.NormalizeLanguageList(LanguageTextBox.Text),
            Keywords = TranscriptionModelCatalog.SupportsKeywordHints(selectedModel)
                ? InputValidation.NormalizeKeywordList(KeywordsTextBox.Text)
                : null,
            ChunkMegabytes = (int)ChunkSizeSlider.Value,
            MaximumChunkMinutes = (int)ChunkDurationSlider.Value,
            RetryCount = CurrentRetryCount,
            UsePreviousContext = TranscriptionModelCatalog.SupportsPreviousContext(selectedModel)
                && PreviousContextCheckBox.IsChecked == true
        };

        var queuedFiles = Files.ToArray();
        var exportText = ExportTextCheckBox.IsChecked == true;
        var exportMarkdown = ExportMarkdownCheckBox.IsChecked == true;
        var includeTimestamps = IncludeTimestampsCheckBox.IsChecked == true;
        var mergeFiles = MergeFilesCheckBox.IsChecked == true;
        var continueOnError = ContinueOnErrorCheckBox.IsChecked == true;
        var combinedTitle = CombinedTitleTextBox.Text;

        if (RememberKeyCheckBox.IsChecked == true)
        {
            try
            {
                _apiKeyStore.SaveProtected(apiKey);
                _apiKeySource = "Secure local vault";
                ApiKeySourceText.Text = _apiKeySource;
            }
            catch (Exception ex)
            {
                StartupDiagnostics.WriteException("Authentication", ex);
                MessageBox.Show(this,
                    "Transcription can continue, but the key could not be saved securely:\n\n" + ex.Message,
                    "Secure key storage warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        _settingsAutoSaveTimer.Stop();
        CaptureSettingsForPersistence(pricePerMinute);
        SaveSettingsQuietly(captureControls: false);
        _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        SetRunningState(true);
        OverallProgressBar.IsIndeterminate = false;
        OverallProgressBar.Value = 0;
        ProgressPercentText.Text = "0%";
        CurrentActionText.Text = "Starting transcription…";
        FooterTitleText.Text = "Preparing transcription run";
        FooterDetailText.Text = $"{queuedFiles.Length} file(s) queued";
        var cancellationToken = _runCancellation.Token;
        List<FileTranscriptionResult>? mergedResults = mergeFiles ? [] : null;
        long mergedTranscriptCharacters = 0;
        var writtenOutputs = new List<string>();
        var successCount = 0;
        var failureCount = 0;
        var skippedCount = 0;
        var stopwatch = Stopwatch.StartNew();
        var completionEvidence = new RunCompletionEvidence();

        foreach (var item in queuedFiles)
        {
            item.Status = AudioJobStatus.Ready;
            item.Progress = 0;
            item.Detail = "Queued";
        }

        AppendLog($"Starting {queuedFiles.Length} file(s) with {request.Model}" +
                  (usesSpeakerDiarization ? " in speaker-labelled mode" : string.Empty) +
                  ". Estimated API cost: " +
                  $"US${queuedFiles.Sum(x => x.Duration.TotalMinutes) * pricePerMinute:0.000}.");

        try
        {
            for (var fileIndex = 0; fileIndex < queuedFiles.Length; fileIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = queuedFiles[fileIndex];
                item.Status = AudioJobStatus.Preparing;
                item.Detail = "Preparing audio";
                HeaderStatusText.Text = $"File {fileIndex + 1} of {queuedFiles.Length}";
                FooterTitleText.Text = item.FileName;

                var currentFileIndex = fileIndex;
                var transcriptionStageReached = false;
                var acceptProgress = true;
                var progress = new Progress<CoordinatorProgress>(update =>
                {
                    if (_isClosing || !acceptProgress)
                    {
                        return;
                    }

                    if (string.Equals(update.Stage, "Transcribing", StringComparison.Ordinal))
                    {
                        transcriptionStageReached = true;
                    }
                    else if (transcriptionStageReached)
                    {
                        // Ignore a delayed preparation callback after transcription has begun.
                        return;
                    }

                    item.Status = transcriptionStageReached
                        ? AudioJobStatus.Transcribing
                        : AudioJobStatus.Preparing;
                    item.Progress = update.Fraction * 100;
                    item.Detail = update.TotalChunks > 0
                        ? $"Chunk {update.CurrentChunk}/{update.TotalChunks}"
                        : update.Stage;

                    var overallFraction = (currentFileIndex + update.Fraction) / queuedFiles.Length;
                    OverallProgressBar.Value = overallFraction * 100;
                    ProgressPercentText.Text = $"{overallFraction:P0}";
                    CurrentActionText.Text = update.Message;
                    FooterDetailText.Text = update.Message;
                });

                var coordinatorStarted = false;
                var coordinatorCompleted = false;
                try
                {
                    EnsureSourceUnchanged(item);
                    FileTranscriptionResult result;
                    try
                    {
                        coordinatorStarted = true;
                        result = await _coordinator.ProcessFileAsync(
                            item,
                            outputDirectory,
                            request,
                            progress,
                            AppendLog,
                            cancellationToken);
                        coordinatorCompleted = true;
                        completionEvidence.Accepted();
                    }
                    finally
                    {
                        // Progress<T> posts through the UI synchronization context. Ignore any
                        // delayed callbacks once the coordinator has returned or failed.
                        acceptProgress = false;
                    }

                    if (mergeFiles)
                    {
                        var resultCharacters = CountTranscriptCharacters(result);
                        if (resultCharacters > MaximumMergedTranscriptCharacters - mergedTranscriptCharacters)
                        {
                            throw new TranscriptExportException(
                                $"Combined export exceeded the safe {MaximumMergedTranscriptCharacters:N0}-character in-memory limit. " +
                                "Export fewer files together; completed transcript checkpoints were retained.");
                        }

                        mergedTranscriptCharacters += resultCharacters;
                        mergedResults!.Add(result);
                    }
                    else
                    {
                        var outputs = await Task.Run(() => _outputWriter.WriteIndividual(
                            result,
                            outputDirectory,
                            exportText,
                            exportMarkdown,
                            includeTimestamps), cancellationToken);
                        writtenOutputs.AddRange(outputs);
                        foreach (var path in outputs)
                        {
                            AppendLog("Created " + path);
                        }
                    }

                    successCount++;
                    item.Status = AudioJobStatus.Completed;
                    item.Progress = 100;
                    item.Detail = mergeFiles ? "Transcribed; awaiting merge" : "Exported";
                }
                catch (OperationCanceledException)
                {
                    item.Status = AudioJobStatus.Cancelled;
                    item.Detail = "Cancelled; checkpoints and unresolved intents retained";
                    throw;
                }
                catch (Exception ex)
                {
                    StartupDiagnostics.WriteException("TRANSCRIPTION FILE FAILURE", ex);
                    completionEvidence.Failed(ex, coordinatorStarted, coordinatorCompleted);
                    failureCount++;
                    item.Status = AudioJobStatus.Failed;
                    item.Detail = InputValidation.SingleLineForLog(ex.Message, 500);
                    AppendLog($"FAILED {item.FileName}: {ex.Message}");
                    var systemicFailure = IsSystemicRunFailure(ex);
                    if (!continueOnError || systemicFailure)
                    {
                        var skipReason = systemicFailure
                            ? "Not processed after a systemic or unexpected failure"
                            : "Not processed after an earlier failure";
                        foreach (var skipped in queuedFiles.Skip(fileIndex + 1))
                        {
                            skipped.Status = AudioJobStatus.Skipped;
                            skipped.Detail = skipReason;
                            skippedCount++;
                        }

                        if (systemicFailure)
                        {
                            AppendLog("Stopping the queue because the failure is systemic rather than file-specific.");
                        }
                        break;
                    }
                }
            }

            if (mergeFiles && mergedResults is { Count: > 0 })
            {
                var runIsComplete = failureCount == 0
                    && skippedCount == 0
                    && mergedResults.Count == queuedFiles.Length;
                if (runIsComplete)
                {
                    var outputs = await Task.Run(() => _outputWriter.WriteCombined(
                        mergedResults,
                        combinedTitle,
                        outputDirectory,
                        exportText,
                        exportMarkdown,
                        includeTimestamps), cancellationToken);
                    writtenOutputs.AddRange(outputs);
                    foreach (var path in outputs)
                    {
                        AppendLog("Created " + path);
                    }

                    foreach (var completedItem in queuedFiles.Where(item => item.Status == AudioJobStatus.Completed))
                    {
                        completedItem.Detail = "Merged and exported";
                    }
                }
                else
                {
                    foreach (var completedItem in queuedFiles.Where(item => item.Status == AudioJobStatus.Completed))
                    {
                        completedItem.Detail = "Transcribed; merge deferred until every file succeeds";
                    }

                    AppendLog("Combined export was not created because the queue was incomplete. " +
                              "Accepted checkpoints were retained. Review failed items and unresolved attempts before another request.");
                }
            }

            stopwatch.Stop();
            var elapsedText = Formatters.Timestamp(stopwatch.Elapsed);
            var completedWithIssues = failureCount > 0 || skippedCount > 0;
            OverallProgressBar.Value = completedWithIssues ? OverallProgressBar.Value : 100;
            ProgressPercentText.Text = completedWithIssues
                ? $"{OverallProgressBar.Value:0}%"
                : "100%";
            HeaderStatusText.Text = completedWithIssues ? "Completed with issues" : "Transcription complete";
            FooterTitleText.Text = completedWithIssues ? "Run completed with issues" : "Transcription complete";
            FooterDetailText.Text = $"{successCount} succeeded, {failureCount} failed, {skippedCount} skipped • {elapsedText}";
            CurrentActionText.Text = writtenOutputs.Count > 0
                ? $"Created {writtenOutputs.Count} transcript file(s)"
                : "No transcript was exported";

            AppendLog($"Run finished: {successCount} succeeded, {failureCount} failed, {skippedCount} skipped, elapsed {elapsedText}.");
            if (!_isClosing)
            {
                try
                {
                    var completionWindow = new CompletionWindow(
                        outputDirectory,
                        successCount,
                        failureCount,
                        skippedCount,
                        writtenOutputs.Count,
                        stopwatch.Elapsed,
                        completionEvidence.Value)
                    {
                        Owner = this
                    };
                    completionWindow.ShowDialog();
                }
                catch (Exception completionEx)
                {
                StartupDiagnostics.WriteException("Export", completionEx);
                    AppendLog("Could not show the completion window: " +
                              InputValidation.SingleLineForLog(completionEx.Message));
                    MessageBox.Show(this,
                        $"Transcription run finished.\n\nSucceeded: {successCount}\nFailed: {failureCount}\n" +
                        $"Skipped: {skippedCount}\nOutput files: {writtenOutputs.Count}\n\n{outputDirectory}\n\n" +
                        RunPresentation.CompletionGuidance(writtenOutputs.Count, completedWithIssues, completionEvidence.Value).Message,
                        completedWithIssues ? "Completed with issues" : "Transcription complete",
                        MessageBoxButton.OK,
                        completedWithIssues ? MessageBoxImage.Warning : MessageBoxImage.Information);
                }
            }
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            foreach (var item in queuedFiles.Where(item => item.Status is AudioJobStatus.Ready or AudioJobStatus.Preparing or AudioJobStatus.Transcribing))
            {
                item.Status = AudioJobStatus.Cancelled;
                item.Detail = "Cancelled; checkpoints and unresolved intents retained";
            }

            if (!_isClosing)
            {
                HeaderStatusText.Text = "Cancelled";
                FooterTitleText.Text = "Run cancelled";
                FooterDetailText.Text = "Completed checkpoints were retained. Unresolved paid attempts may block restart and require review.";
                CurrentActionText.Text = "Cancelled; paid outcomes may need review";
                AppendLog("Run cancelled. Accepted checkpoints remain available; unresolved paid intents are retained and may block restart.");
            }
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException("TRANSCRIPTION RUN FAILURE", ex);
            completionEvidence.Failed(ex, coordinatorStarted: true, coordinatorCompleted: false);
            stopwatch.Stop();
            foreach (var item in queuedFiles.Where(item => item.Status is AudioJobStatus.Preparing or AudioJobStatus.Transcribing))
            {
                item.Status = AudioJobStatus.Failed;
                item.Detail = InputValidation.SingleLineForLog(ex.Message, 500);
            }

            foreach (var item in queuedFiles.Where(item => item.Status == AudioJobStatus.Ready))
            {
                item.Status = AudioJobStatus.Skipped;
                item.Detail = "Not processed after an unexpected run failure";
            }

            foreach (var item in queuedFiles.Where(item => item.Status == AudioJobStatus.Completed && mergeFiles))
            {
                item.Detail = "Transcribed; final export failed";
            }

            AppendLog("UNEXPECTED RUN FAILURE: " + ex.Message);
            if (!_isClosing)
            {
                HeaderStatusText.Text = "Run failed";
                FooterTitleText.Text = "Unexpected run failure";
                FooterDetailText.Text = RunPresentation.CompletionGuidance(writtenOutputs.Count, true, completionEvidence.Value).Message;
                CurrentActionText.Text = InputValidation.SingleLineForLog(ex.Message, 500);
                MessageBox.Show(this,
                    "The run stopped because of an unexpected error.\n\n" + FooterDetailText.Text + "\n\n" + ex.Message,
                    "Transcription run failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            _runCancellation?.Dispose();
            _runCancellation = null;
            SetRunningState(false);
            if (!_isClosing)
            {
                SaveSettingsQuietly();
            }
            TryCompleteDeferredClose();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_runCancellation is null)
        {
            return;
        }

        _runCancellation.Cancel();
        UpdateInteractiveState();
        CurrentActionText.Text = "Cancelling after the current network operation…";
        AppendLog("Cancellation requested.");
    }

    private static bool SourceHasChanged(AudioJobItem item)
    {
        try
        {
            var info = new FileInfo(item.SourcePath);
            return !info.Exists
                || info.Length != item.SizeBytes
                || info.LastWriteTimeUtc != item.LastWriteTimeUtc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Treat an unreadable path as changed so validation can fail safely instead of
            // allowing an async-void UI handler to escape with an unhandled exception.
            return true;
        }
    }

    private static void EnsureSourceUnchanged(AudioJobItem item)
    {
        if (SourceHasChanged(item))
        {
            throw new IOException(
                "The source MP3 changed or disappeared after it was queued. Remove it, add it again, and retry so duration and checkpoints remain trustworthy.");
        }
    }

    private static bool IsSystemicRunFailure(Exception exception)
    {
        if (exception is PendingTranscriptionException or HttpRequestException or OpenAiProtocolException or CheckpointStorageException
            or AudioPreparationStorageException or TranscriptExportException)
        {
            return true;
        }

        if (exception is OpenAiApiException apiException)
        {
            return !IsFileSpecificApiFailure(apiException);
        }

        // Continue only for recognised source-file failures. Unknown exceptions can indicate
        // a broken invariant, resource exhaustion, or a repeated application defect; treating
        // them as file-specific would risk cascading the same failure through the whole queue.
        return exception is not (IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or TranscriptSizeLimitException);
    }

    private static bool IsFileSpecificApiFailure(OpenAiApiException exception)
    {
        if (exception.StatusCode == HttpStatusCode.UnsupportedMediaType)
        {
            return true;
        }

        var numeric = (int)exception.StatusCode;
        if (exception.StatusCode != HttpStatusCode.BadRequest && numeric != 422)
        {
            return false;
        }

        // 400/422 can also indicate an invalid model, schema, account setting, or other
        // systemic request defect. Continue only when OpenAI's structured parameter
        // identifies the uploaded audio/file field itself.
        var parameter = exception.ErrorParam ?? string.Empty;
        return parameter.Equals("file", StringComparison.OrdinalIgnoreCase)
            || parameter.Equals("audio", StringComparison.OrdinalIgnoreCase)
            || parameter.StartsWith("file[", StringComparison.OrdinalIgnoreCase)
            || parameter.StartsWith("audio[", StringComparison.OrdinalIgnoreCase);
    }

    private bool ValidateRun(out string outputDirectory, out string apiKey, out double pricePerMinute)
    {
        outputDirectory = string.Empty;
        apiKey = string.Empty;
        pricePerMinute = 0;

        if (Files.Count == 0)
        {
            MessageBox.Show(this, "Add at least one MP3 file.", "No audio selected",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (MergeFilesCheckBox.IsChecked == true && Files.Count > MaximumMergedFiles)
        {
            MessageBox.Show(this,
                $"Combined export is limited to {MaximumMergedFiles:N0} queued files to keep memory use bounded. " +
                "Disable merging or split the queue into smaller runs.",
                "Combined export limit", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (ExportTextCheckBox.IsChecked != true && ExportMarkdownCheckBox.IsChecked != true)
        {
            MessageBox.Show(this, "Select TXT, Markdown, or both.", "No output format selected",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (!TranscriptionModelCatalog.SupportsFileUploads(CurrentModel))
        {
            MessageBox.Show(this,
                "The selected model cannot process queued MP3/files. Select one of the supported file-transcription models.",
                "Model cannot process queued files",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        try
        {
            apiKey = InputValidation.NormalizeApiKey(GetApiKey());
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, "Invalid API key",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var missingFiles = Files.Where(item => !File.Exists(item.SourcePath)).Select(item => item.FileName).Take(8).ToArray();
        if (missingFiles.Length > 0)
        {
            MessageBox.Show(this,
                "These source files can no longer be found:\n\n" + string.Join("\n", missingFiles),
                "Missing source audio", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        var changedFiles = Files.Where(SourceHasChanged).Select(item => item.FileName).Take(8).ToArray();
        if (changedFiles.Length > 0)
        {
            MessageBox.Show(this,
                "These source files changed after they were added. Remove and add them again so duration, cost, and checkpoint identity are recalculated:\n\n" +
                string.Join("\n", changedFiles),
                "Source audio changed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        try
        {
            if (TranscriptionModelCatalog.UsesPluralLanguageHints(CurrentModel))
            {
                _ = InputValidation.NormalizeLanguages(LanguageTextBox.Text);
            }
            else
            {
                _ = InputValidation.NormalizeLanguage(LanguageTextBox.Text);
            }
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, "Invalid language code",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        IReadOnlyList<string> keywordHints = [];
        if (!TranscriptionModelCatalog.SupportsSpeakerDiarization(CurrentModel))
        {
            try
            {
                keywordHints = InputValidation.NormalizeKeywords(KeywordsTextBox.Text);
                if (keywordHints.Count > 0 && !TranscriptionModelCatalog.SupportsKeywordHints(CurrentModel))
                {
                    throw new ArgumentException(
                        "Structured keyword hints are supported only by gpt-transcribe. Clear the keyword field or select gpt-transcribe.");
                }
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(this, ex.Message, "Invalid keyword hint",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
        }

        if (TranscriptionModelCatalog.SupportsPrompt(CurrentModel)
            && PromptTextBox.Text.Length > InputValidation.MaximumBasePromptCharacters)
        {
            MessageBox.Show(this, "The recording-context prompt is too long.", "Prompt limit",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        string validatedOutputDirectory;
        try
        {
            validatedOutputDirectory = ResolveOutputPath();
            if (!OutputDirectoryService.CanWrite(validatedOutputDirectory))
            {
                throw new IOException("The selected output folder is not writable.");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Invalid output folder",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        outputDirectory = validatedOutputDirectory;
        var sourcesInsideCache = Files
            .Where(item => CheckpointCacheService.IsPathInsideCache(item.SourcePath, validatedOutputDirectory))
            .Select(item => item.FileName)
            .Take(8)
            .ToArray();
        if (sourcesInsideCache.Length > 0)
        {
            MessageBox.Show(this,
                "One or more source MP3 files are inside Cosmic Transcriber's internal checkpoint cache for the selected output folder. " +
                "Move those files elsewhere, remove and re-add them, then retry.\n\n" +
                string.Join("\n", sourcesInsideCache),
                "Unsafe source location",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (!double.TryParse(PricePerMinuteTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out pricePerMinute)
            || !double.IsFinite(pricePerMinute)
            || pricePerMinute < 0)
        {
            MessageBox.Show(this, "Enter a valid non-negative price per minute, using a dot as the decimal separator.",
                "Invalid price estimate", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var estimatedCost = Files.Sum(item => item.Duration.TotalMinutes) * pricePerMinute;
        if (!double.IsFinite(estimatedCost))
        {
            MessageBox.Show(this, "The estimated cost is too large to represent. Check the price field.",
                "Invalid price estimate", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (TranscriptionModelCatalog.SupportsPrompt(CurrentModel)
            && string.IsNullOrWhiteSpace(PromptTextBox.Text)
            && keywordHints.Count == 0)
        {
            var result = MessageBox.Show(this,
                "Both the recording-context prompt and keyword hints are empty. Continue without transcription guidance?",
                "Empty transcription guidance", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return false;
            }
        }

        return true;
    }

    private static string ResolveInitialOutputPath()
    {
        try
        {
            return OutputDirectoryService.ResolveDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            StartupDiagnostics.WriteException("OUTPUT PATH FALLBACK", ex);
            // Keep the window launchable even when no default location can be probed.
            // Run validation and the Browse button will still guide the user to a writable folder.
            return Path.Combine(AppContext.BaseDirectory, "output");
        }
    }

    private string ResolveOutputPath()
    {
        var raw = OutputPathTextBox.Text.Trim();
        if (raw.Length > InputValidation.MaximumOutputPathCharacters)
        {
            throw new PathTooLongException("The output path is too long.");
        }

        var value = Environment.ExpandEnvironmentVariables(raw);
        if (string.IsNullOrWhiteSpace(value))
        {
            value = OutputDirectoryService.ResolveDefault();
            OutputPathTextBox.Text = value;
        }

        if (value.Length > InputValidation.MaximumOutputPathCharacters)
        {
            throw new PathTooLongException("The expanded output path is too long.");
        }

        var fullPath = Path.GetFullPath(value);
        if (fullPath.Length > InputValidation.MaximumOutputPathCharacters)
        {
            throw new PathTooLongException("The absolute output path is too long.");
        }

        return fullPath;
    }

    private string CurrentModel
        => (ModelComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString()
            ?? TranscriptionModelCatalog.DefaultFileModel;

    private int CurrentRetryCount
        => int.TryParse((RetryComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString(), out var retries)
            ? Math.Clamp(retries, 0, 5)
            : 0;

    private void UpdateQueueSummary()
    {
        if (!_xamlInitialized
            || QueueSummaryText is null
            || EstimateText is null
            || CostBadgeText is null)
        {
            return;
        }

        if (Files.Count == 0)
        {
            QueueSummaryText.Text = "No MP3 files selected";
            EstimateText.Text = "Add MP3s to calculate duration and cost";
            CostBadgeText.Text = "US$0.00 est.";
            return;
        }

        long totalTicks = 0;
        long totalBytes = 0;
        foreach (var item in Files)
        {
            var ticks = Math.Max(0, item.Duration.Ticks);
            totalTicks = ticks > long.MaxValue - totalTicks
                ? long.MaxValue
                : totalTicks + ticks;

            var bytes = Math.Max(0, item.SizeBytes);
            totalBytes = bytes > long.MaxValue - totalBytes
                ? long.MaxValue
                : totalBytes + bytes;
        }

        var totalDuration = new TimeSpan(totalTicks);
        QueueSummaryText.Text = $"{Files.Count} file(s)  •  {Formatters.Duration(totalDuration)}  •  {Formatters.FileSize(totalBytes)}";

        EstimateText.Text = $"{totalDuration.TotalMinutes:0.0} audio minutes • {CurrentModel}";
        CostBadgeText.Text = RunPresentation.EstimatedCost(PricePerMinuteTextBox?.Text, totalDuration);
    }

    private static long CountTranscriptCharacters(FileTranscriptionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        long totalCharacters = 0;
        foreach (var chunk in result.Chunks)
        {
            totalCharacters = chunk.Text.Length > long.MaxValue - totalCharacters
                ? long.MaxValue
                : totalCharacters + chunk.Text.Length;
        }

        return totalCharacters;
    }

    private void SetRunningState(bool running)
    {
        _isRunning = running;
        UpdateInteractiveState();
        HeaderStatusText.Text = running ? "Transcription running" : HeaderStatusText.Text;
        if (!running)
        {
            OverallProgressBar.IsIndeterminate = false;
        }
    }

    private void UpdateInteractiveState()
    {
        var idle = IsUiIdle;
        StartButton.IsEnabled = idle;
        ReviewUncertainButton.IsEnabled = idle;
        CancelButton.IsEnabled = _isRunning
            && _runCancellation is { IsCancellationRequested: false };
        TestKeyButton.IsEnabled = idle;
        QueueActionPanel.IsEnabled = idle;
        DragDropWarningBanner.IsEnabled = idle;
        FilesListView.IsHitTestVisible = idle;
        FilesListView.IsTabStop = idle;
        OutputSettingsCard.IsEnabled = idle;
        TranscriptionSettingsCard.IsEnabled = idle;
        UpdateModelSpecificControls();
        ApiKeySettingsCard.IsEnabled = idle;
        ApiKeyShortcutButton.IsEnabled = idle;
    }

    private void FitWindowToWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        var availableWidth = Math.Max(320, workArea.Width - 24);
        var availableHeight = Math.Max(320, workArea.Height - 24);
        MinWidth = Math.Min(MinWidth, availableWidth);
        MinHeight = Math.Min(MinHeight, availableHeight);
        Width = Math.Min(Width, availableWidth);
        Height = Math.Min(Height, availableHeight);
        StartupDiagnostics.Write("MAINWINDOW", $"Work area={workArea}; requested window={Width:0}x{Height:0}; minimum={MinWidth:0}x{MinHeight:0}.");
    }

    private void AppendLog(string message)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => AppendLog(message));
            return;
        }

        // Persist once, after marshalling to the UI thread. Writing before BeginInvoke
        // caused background-originated activity messages to appear twice on disk.
        StartupDiagnostics.Write("ACTIVITY", message);
        var line = $"[{DateTime.Now:HH:mm:ss}] {InputValidation.SingleLineForLog(message, 4_000)}";
        LogTextBox.AppendText((LogTextBox.Text.Length == 0 ? string.Empty : Environment.NewLine) + line);

        if (LogTextBox.Text.Length > MaximumLogCharacters)
        {
            var excess = LogTextBox.Text.Length - MaximumLogCharacters;
            var nextLine = LogTextBox.Text.IndexOf(Environment.NewLine, excess, StringComparison.Ordinal);
            LogTextBox.Text = nextLine >= 0
                ? LogTextBox.Text[(nextLine + Environment.NewLine.Length)..]
                : LogTextBox.Text[^MaximumLogCharacters..];
        }

        LogTextBox.ScrollToEnd();
    }

    private void ApplySettingsToControls()
    {
        CurrentApp.ApplyTheme(_settings.Theme);
        ThemeToggleButton.Content = _settings.Theme == "Cosmic Paper" ? "☀  Cosmic Paper" : "☾  Cosmic Night";

        OutputPathTextBox.Text = string.IsNullOrWhiteSpace(_settings.OutputDirectory)
            ? ResolveInitialOutputPath()
            : _settings.OutputDirectory;
        SelectModel(_settings.Model);
        LanguageTextBox.Text = _settings.Language;
        KeywordsTextBox.Text = _settings.Keywords;
        PromptTextBox.Text = string.IsNullOrWhiteSpace(_settings.Prompt) ? AppSettings.DefaultPrompt : _settings.Prompt;
        ChunkSizeSlider.Value = Math.Clamp(_settings.ChunkMegabytes, 5, 24);
        ChunkDurationSlider.Value = Math.Clamp(_settings.MaximumChunkMinutes, 3, 5);
        SelectRetryCount(_settings.RetryCount);
        PreviousContextCheckBox.IsChecked = _settings.UsePreviousContext;
        IncludeTimestampsCheckBox.IsChecked = _settings.IncludeTimestamps;
        ExportTextCheckBox.IsChecked = _settings.ExportText;
        ExportMarkdownCheckBox.IsChecked = _settings.ExportMarkdown;
        MergeFilesCheckBox.IsChecked = _settings.MergeFiles;
        ContinueOnErrorCheckBox.IsChecked = _settings.ContinueOnError;
        CombinedTitleTextBox.Text = string.IsNullOrWhiteSpace(_settings.CombinedTitle)
            ? "Combined Transcript"
            : _settings.CombinedTitle;
        PricePerMinuteTextBox.Text = _settings.CustomPricePerMinute.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private void SelectModel(string model)
    {
        foreach (var item in ModelComboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.IsEnabled
                && string.Equals(item.Content?.ToString(), model, StringComparison.OrdinalIgnoreCase))
            {
                ModelComboBox.SelectedItem = item;
                return;
            }
        }

        ModelComboBox.SelectedIndex = 0;
    }

    private void SelectRetryCount(int retries)
    {
        var target = Math.Clamp(retries, 0, 5).ToString(CultureInfo.InvariantCulture);
        foreach (var item in RetryComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), target, StringComparison.Ordinal))
            {
                RetryComboBox.SelectedItem = item;
                return;
            }
        }

        RetryComboBox.SelectedIndex = 0;
    }

    private void RegisterSettingsAutoSaveHandlers()
    {
        OutputPathTextBox.TextChanged += SettingsTextChanged;
        LanguageTextBox.TextChanged += SettingsTextChanged;
        KeywordsTextBox.TextChanged += SettingsTextChanged;
        PromptTextBox.TextChanged += SettingsTextChanged;
        CombinedTitleTextBox.TextChanged += SettingsTextChanged;
        RetryComboBox.SelectionChanged += SettingsSelectionChanged;

        foreach (var checkBox in new[]
                 {
                     ExportTextCheckBox,
                     ExportMarkdownCheckBox,
                     IncludeTimestampsCheckBox,
                     MergeFilesCheckBox,
                     PreviousContextCheckBox,
                     ContinueOnErrorCheckBox,
                     RememberKeyCheckBox
                 })
        {
            checkBox.Checked += SettingsCheckBoxChanged;
            checkBox.Unchecked += SettingsCheckBoxChanged;
        }
    }

    private void SettingsTextChanged(object sender, TextChangedEventArgs e)
        => ScheduleSettingsAutoSave();

    private void SettingsSelectionChanged(object sender, SelectionChangedEventArgs e)
        => ScheduleSettingsAutoSave();

    private void SettingsCheckBoxChanged(object sender, RoutedEventArgs e)
        => ScheduleSettingsAutoSave();

    private void ScheduleSettingsAutoSave()
    {
        if (!_constructionCompleted || _isClosing || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        _settingsAutoSaveTimer.Stop();
        _settingsAutoSaveTimer.Start();
        SetSettingsSaveStatus("Unsaved changes · saving shortly", "AccentTextBrush");
    }

    private void SettingsAutoSaveTimer_Tick(object? sender, EventArgs e)
    {
        _settingsAutoSaveTimer.Stop();
        SaveSettingsQuietly(isAutoSave: true);
    }

    private IReadOnlyList<string> CaptureSettingsForPersistence(double? knownPrice = null)
    {
        var invalidFields = new List<string>();

        _settings.OutputDirectory = InputValidation.TruncateWithoutSplittingSurrogatePair(
            OutputPathTextBox.Text.Trim(), InputValidation.MaximumOutputPathCharacters);
        _settings.Model = CurrentModel;

        try
        {
            _settings.Language = InputValidation.NormalizeLanguageList(LanguageTextBox.Text);
        }
        catch (ArgumentException)
        {
            invalidFields.Add("language hints");
        }

        try
        {
            _settings.Keywords = InputValidation.NormalizeKeywordList(KeywordsTextBox.Text);
        }
        catch (ArgumentException)
        {
            invalidFields.Add("keyword hints");
        }

        _settings.Prompt = InputValidation.TruncateWithoutSplittingSurrogatePair(
            PromptTextBox.Text, InputValidation.MaximumBasePromptCharacters);
        _settings.ChunkMegabytes = (int)ChunkSizeSlider.Value;
        _settings.MaximumChunkMinutes = (int)ChunkDurationSlider.Value;
        _settings.RetryCount = CurrentRetryCount;
        _settings.UsePreviousContext = PreviousContextCheckBox.IsChecked == true;
        _settings.IncludeTimestamps = IncludeTimestampsCheckBox.IsChecked == true;
        _settings.ExportText = ExportTextCheckBox.IsChecked == true;
        _settings.ExportMarkdown = ExportMarkdownCheckBox.IsChecked == true;
        _settings.MergeFiles = MergeFilesCheckBox.IsChecked == true;
        _settings.ContinueOnError = ContinueOnErrorCheckBox.IsChecked == true;
        _settings.CombinedTitle = InputValidation.TruncateWithoutSplittingSurrogatePair(
            CombinedTitleTextBox.Text, InputValidation.MaximumCombinedTitleCharacters);
        _settings.Theme = CurrentApp.CurrentTheme;
        _settings.RememberKeySecurely = RememberKeyCheckBox.IsChecked == true;

        if (knownPrice is not null && double.IsFinite(knownPrice.Value) && knownPrice.Value >= 0)
        {
            _settings.CustomPricePerMinute = knownPrice.Value;
        }
        else if (knownPrice is null)
        {
            if (double.TryParse(PricePerMinuteTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var price)
                && double.IsFinite(price)
                && price >= 0)
            {
                _settings.CustomPricePerMinute = price;
            }
            else
            {
                invalidFields.Add("price estimate");
            }
        }

        return invalidFields;
    }

    private bool SaveSettingsQuietly(bool captureControls = true, bool isAutoSave = false)
    {
        IReadOnlyList<string> invalidFields = Array.Empty<string>();
        try
        {
            if (captureControls)
            {
                invalidFields = CaptureSettingsForPersistence();
            }

            _settingsService.Save(_settings, writeDiagnostic: !isAutoSave);
            _settingsSaveFailureLogged = false;

            if (invalidFields.Count == 0)
            {
                var status = isAutoSave
                    ? $"Saved automatically · {DateTime.Now:HH:mm:ss}"
                    : "Settings saved";
                SetSettingsSaveStatus(status, "SuccessBrush");
            }
            else
            {
                SetSettingsSaveStatus(
                    "Saved valid settings; fix " + string.Join(", ", invalidFields) + " to save those fields",
                    "WarningBrush");
            }

            return true;
        }
        catch (Exception ex)
        {
            var message = "Could not save settings: " + InputValidation.SingleLineForLog(ex.Message);
            StartupDiagnostics.WriteException("SETTINGS SAVE", ex);
            SetSettingsSaveStatus("Autosave failed · settings folder is not writable", "ErrorBrush");
            if (!_settingsSaveFailureLogged)
            {
                AppendLog(message);
                _settingsSaveFailureLogged = true;
            }
            return false;
        }
    }

    private void SetSettingsSaveStatus(string message, string brushResourceKey)
    {
        if (SettingsAutoSaveStatusText is null)
        {
            return;
        }

        SettingsAutoSaveStatusText.Text = message;
        SettingsAutoSaveStatusText.SetResourceReference(TextBlock.ForegroundProperty, brushResourceKey);
    }


    private void OpenDiagnosticLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AppendLog(StartupDiagnostics.HealthSummary);
            var directory = StartupDiagnostics.LogDirectory;
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new IOException("No writable diagnostic-log directory is available.");
            }

            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppendLog("Could not open diagnostic logs: " + InputValidation.SingleLineForLog(ex.Message));
            MessageBox.Show(this, ex.Message, "Cannot open diagnostic logs",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        DiagnosticWindowState("loaded");

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        _dragDropUnavailableBecauseElevated = WindowsSecurity.IsProcessElevated();
        _dragDropCompatibilityFlagDetected = WindowsSecurity.HasRunAsAdminCompatibilityFlag(
            out _dragDropCompatibilityFlagSource);
        if (_dragDropUnavailableBecauseElevated || _dragDropCompatibilityFlagDetected)
        {
            DragDropWarningBanner.Visibility = Visibility.Visible;
            if (_dragDropUnavailableBecauseElevated)
            {
                DragDropWarningTitleText.Text = "Cosmic Transcriber is running as administrator";
                DragDropWarningDetailText.Text =
                    "Windows blocks files dragged from normal File Explorer into an elevated app. Choose Restart normally to reopen at the correct privilege level. Paste remains available.";
            }
            else
            {
                DragDropWarningTitleText.Text = "Run as administrator compatibility setting detected";
                DragDropWarningDetailText.Text =
                    $"Windows may elevate this app because of {_dragDropCompatibilityFlagSource}. Choose Restart normally to clear the current-user setting when possible, or use Paste.";
            }

            SetDropZoneFeedback(false);
            AppendLog("Drag-and-drop warning: " + DragDropWarningDetailText.Text);
        }

        UpdateInteractiveState();
        Activate();
    }

    private void DiagnosticFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is FrameworkElement element)
            StartupDiagnostics.WriteFocus(string.IsNullOrEmpty(element.Name) ? "unnamed" : element.Name, element.GetType().Name);
    }

    private void DiagnosticWindowState(string stage)
    {
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            StartupDiagnostics.WriteWindow(stage, IsVisible, IsActive, WindowState.ToString(), ActualWidth, ActualHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY);
        }
        catch { /* Diagnostics must not alter window behavior. */ }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab)
            StartupDiagnostics.WriteLifecycle(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? "shift_tab_navigation" : "tab_navigation");
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O)
        {
            AddFiles_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.O)
        {
            AddFolder_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control
            && e.Key == Key.V
            && Keyboard.FocusedElement is not TextBox
            && Keyboard.FocusedElement is not PasswordBox)
        {
            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    PastePaths_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                }
            }
            catch
            {
                // Let the focused control or Windows handle Ctrl+V if the clipboard is temporarily busy.
            }
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Enter && !_isRunning)
        {
            Start_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _isRunning)
        {
            Cancel_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_constructionCompleted)
        {
            _isClosing = true;
            _lifetimeCancellation.Cancel();
            _runCancellation?.Cancel();
            StartupDiagnostics.Write("MAINWINDOW", "Closing a partially constructed window; control-dependent cleanup was skipped.");
            return;
        }

        if (_allowFinalClose)
        {
            PersistBeforeFinalClose();
            return;
        }

        var workIsActive = _isRunning || _isImporting || _isTestingKey;
        if (!_isClosing && workIsActive)
        {
            var result = MessageBox.Show(this,
                "Background work is active. Close the application and cancel it? The window will remain open until safety cleanup and any completed-response checkpoint save have finished.",
                "Work in progress", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        if (workIsActive)
        {
            // Keep the Window alive until every async finally path runs. Exiting immediately
            // could terminate while a successful paid response is being checkpointed.
            e.Cancel = true;
            _isClosing = true;
            _closeRequestedAfterUnwind = true;
            _lifetimeCancellation.Cancel();
            _runCancellation?.Cancel();
            StartupDiagnostics.Write("MAINWINDOW", "Close deferred until active work finishes unwinding.");
            return;
        }

        _isClosing = true;
        _allowFinalClose = true;
        PersistBeforeFinalClose();
    }

    private void TryCompleteDeferredClose()
    {
        if (!_closeRequestedAfterUnwind || _isRunning || _isImporting || _isTestingKey)
        {
            return;
        }

        _closeRequestedAfterUnwind = false;
        _allowFinalClose = true;
        StartupDiagnostics.Write("MAINWINDOW", "Background work finished unwinding; completing deferred close.");
        if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
        {
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
    }

    private void PersistBeforeFinalClose()
    {
        _settingsAutoSaveTimer.Stop();

        if (RememberKeyCheckBox.IsChecked == true)
        {
            var key = GetApiKey();
            if (!string.IsNullOrWhiteSpace(key))
            {
                try
                {
                    _apiKeyStore.SaveProtected(key);
                }
                catch
                {
                    // Closing must not be blocked by a key persistence failure.
                }
            }
        }

        SaveSettingsQuietly();
        // Do not dispose HttpClient while async cancellation is unwinding. Process exit
        // releases it safely, and disposing here can race a streamed response body.
    }
}


