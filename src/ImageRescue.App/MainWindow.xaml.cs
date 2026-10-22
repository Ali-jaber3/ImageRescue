using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ImageRescue.App.Presentation;
using ImageRescue.App.Recovery;
using Microsoft.Win32;

namespace ImageRescue.App;

public partial class MainWindow : Window
{
    private readonly Localizer _localizer = new();
    private readonly ObservableCollection<ImageRow> _rows = [];
    private readonly Dictionary<long, ImageRow> _byOffset = [];
    private readonly DispatcherTimer _settingsTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _rowTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private bool _bulkRows;
    private Preferences _preferences;
    private bool _ready, _busy, _descending, _closeAfterWork;
    private int _generation;
    private string _state = "Ready", _theme = "dark", _destination = "";
    private string? _image, _output;
    private StorageTarget? _device;
    private SourceSelection? _sessionSource;
    private CancellationTokenSource? _cancellation;
    private PauseController? _pause;
    private ScanUpdate? _lastProgress;
    private sealed record SourceSelection(StorageTarget? Device, string? Image, string Destination);

    public MainWindow()
    {
        InitializeComponent();
        _preferences = PreferenceStore.Load();
        _theme = _preferences.Theme;
        _destination = _preferences.Destination;
        DestinationPathText.Text = _destination;
        LanguageSelector.ItemsSource = Localizer.Languages;
        LanguageSelector.SelectedItem = Localizer.Languages.First(l => l.Code == _preferences.Language);
        ModeSelector.SelectedIndex = 1;
        PolicySelector.SelectedIndex = 1;
        SortSelector.SelectedIndex = 0;
        ResultsGrid.ItemsSource = _rows;
        _settingsTimer.Tick += (_, _) => { _settingsTimer.Stop(); PersistPreferences(); };
        _rowTimer.Tick += (_, _) => { _rowTimer.Stop(); UpdateActions(); };
        _ready = true;
        ApplyLanguage(); ApplyTheme(); ApplySort(); UpdateActions();
        Loaded += async (_, _) => await RefreshDevices();
    }

    private string L(string key) => _localizer[key];

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        ApplyLanguage(); QueuePreferences();
    }

    private void ApplyLanguage()
    {
        string code = (LanguageSelector.SelectedItem as LanguageChoice)?.Code ?? "en";
        _localizer.Apply(code, Resources);
        RootLayout.FlowDirection = code == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(code);
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
        string[] headers = ["", "Name", "Type", "Size", "Pixels", "Camera", "DateTaken", "State", "RecoveryDetails"];
        for (int i = 1; i < headers.Length; i++) ResultsGrid.Columns[i].Header = L(headers[i]);
        foreach (var row in _rows) row.LanguageChanged();
        DirectionButton.Content = L(_descending ? "Descending" : "Ascending");
        if (_ready) ApplyTheme();
        UpdateModeHint(); RenderStatus(); RenderMetrics(); UpdateActions();
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        _theme = _theme == "dark" ? "light" : "dark";
        ApplyTheme(); QueuePreferences();
    }

    private void ApplyTheme()
    {
        string[] colors = _theme switch
        {
            "light" => ["#F1F5F9", "#FFFFFF", "#F7F9FC", "#E9F0F6", "#D9E3EE", "#182B43", "#53677F", "#087F8C", "#FFFFFF"],
            "soft" => ["#DFE6ED", "#F1F4F8", "#E6ECF3", "#D7E2EC", "#BFCDDC", "#20344D", "#4D637D", "#176B7D", "#FFFFFF"],
            _ => ["#1B1B1B", "#242424", "#202020", "#303030", "#444444", "#F4F4F5", "#B4B4BB", "#B6A4FF", "#211B33"]
        };
        string[] keys = ["Page", "Card", "Field", "Surface", "Line", "Text", "Muted", "Accent", "AccentText"];
        for (int i = 0; i < keys.Length; i++)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
            brush.Freeze(); Resources[keys[i]] = brush;
        }
        ThemeIcon.Data = Geometry.Parse(_theme == "dark"
            ? "M12,3 V1 M12,23 V21 M3,12 H1 M23,12 H21 M4.2,4.2 L5.6,5.6 M18.4,18.4 L19.8,19.8 M4.2,19.8 L5.6,18.4 M18.4,5.6 L19.8,4.2 M17,12 A5,5 0 1 1 7,12 A5,5 0 1 1 17,12"
            : "M20,15 A9,9 0 1 1 9,3 A7,7 0 0 0 20,15");
        ThemeToggleButton.ToolTip = L(_theme == "dark" ? "Light" : "Dark");
        ThemeToggleButton.SetValue(System.Windows.Automation.AutomationProperties.NameProperty,
            L(_theme == "dark" ? "Light" : "Dark"));
        SetTitleBarTheme();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e) => SetTitleBarTheme();
    private void SetTitleBarTheme()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        int dark = _theme == "dark" ? 1 : 0;
        try { DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)); }
        catch (DllNotFoundException) { }
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);

    private void QueuePreferences() { _settingsTimer.Stop(); _settingsTimer.Start(); }
    private void PersistPreferences()
    {
        _preferences = new(_localizer.Code, _theme, _destination);
        SettingsWarning.Text = PreferenceStore.Save(_preferences) ? "" : L("SettingsIssue");
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshDevices();
    private async Task RefreshDevices()
    {
        if (_busy) return;
        SetBusy(true); SetState("Loading");
        try
        {
            var devices = await Task.Run(WindowsStorage.ListTargets);
            DeviceSelector.ItemsSource = devices;
            _device = null;
            if (_image is null) SourcePathText.Text = "";
            SetState(devices.Count == 0 ? "NoDevices" : "Ready");
        }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); if (_closeAfterWork) Close(); }
    }

    private void Device_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || DeviceSelector.SelectedItem is not StorageTarget target) return;
        _device = target; _image = null; SourcePathText.Text = target.ToString(); SetState("Ready");
    }
    private void BrowseImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = L("OpenImg"), Filter = "IMG (*.img)|*.img", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        DeviceSelector.SelectedIndex = -1; _device = null;
        _image = dialog.FileName; SourcePathText.Text = _image; SetState("Ready");
    }
    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = L("ChooseFolder") };
        if (dialog.ShowDialog(this) != true) return;
        _destination = dialog.FolderName; DestinationPathText.Text = _destination; QueuePreferences();
    }
    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_destination)) return;
        try { Clipboard.SetText(_destination); } catch (ExternalException ex) { ShowError(ex); }
    }
    private void Mode_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) UpdateModeHint(); }
    private void UpdateModeHint() => ModeHintText.Text = L(ModeSelector.SelectedIndex == 0 ? "QuickHint" : "DeepHint");

    private void SelectTypes_Click(object sender, RoutedEventArgs e)
    {
        bool value = sender is Button { Tag: "all" };
        foreach (var box in new[] { JpegCheck, PngCheck, OtherImagesCheck, PdfCheck, OfficeCheck, VideoCheck, AudioCheck, ArchivesCheck, RawCheck })
            box.IsChecked = value;
    }

    private async void StartScan_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_device is null && (_image is null || !File.Exists(_image))) { Warn("NeedSource"); return; }
        if (!Directory.Exists(_destination)) { Warn("NeedDestination"); return; }
        var options = new ScanOptions(JpegCheck.IsChecked == true, PngCheck.IsChecked == true, PolicySelector.SelectedIndex == 0,
            OtherImagesCheck.IsChecked == true, PdfCheck.IsChecked == true, OfficeCheck.IsChecked == true,
            VideoCheck.IsChecked == true, AudioCheck.IsChecked == true, ArchivesCheck.IsChecked == true, RawCheck.IsChecked == true);
        if (!options.Any) { Warn("NeedType"); return; }
        string prefix = PrefixBox.Text.Trim();
        try { ResultFiles.ValidateBaseName(prefix); } catch (ArgumentException) { Warn("BadName"); return; }
        _sessionSource = new(_device, _image, _destination);
        var sourceSelection = _sessionSource;
        string output = Path.Combine(_destination, $"ImageRescue_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");
        _generation++;
        int generation = _generation;
        foreach (var row in _rows) row.PropertyChanged -= Row_Changed;
        _rows.Clear(); _byOffset.Clear(); _lastProgress = null;
        _output = null; ResultsPathText.Text = ""; ScanProgress.Value = 0;
        BeginCancellableWork(); SetState("Scanning");
        var token = _cancellation!.Token; var pause = _pause;
        var mode = ModeSelector.SelectedIndex == 0 ? ScanMode.Quick : ScanMode.Deep;
        var progress = new Progress<ScanUpdate>(update =>
        {
            if (generation != _generation) return;
            _lastProgress = update; ScanProgress.Value = update.Percent;
            if (Directory.Exists(output)) { _output = output; ResultsPathText.Text = output; }
            RenderMetrics();
        });
        var found = new Progress<RecoveredImage>(image =>
        {
            if (generation == _generation) Upsert(image);
        });
        try
        {
            var report = await Task.Run(() =>
            {
                using var source = OpenSource(sourceSelection, _destination);
                return Carver.Scan(source, sourceSelection.Device?.Root ?? sourceSelection.Image!, output,
                    progress, found, token, mode, pause, prefix, options);
            });
            _output = output; ResultsPathText.Text = output; SyncReport(report);
            SetState(report.Outcome switch { "Cancelled" => "Cancelled", "ResultLimitReached" => "Limit", _ => "Completed" });
        }
        catch (Exception ex)
        {
            if (Directory.Exists(output)) { _output = output; ResultsPathText.Text = output; TrySyncReport(); }
            ShowError(ex);
        }
        finally { EndCancellableWork(); }
    }

    private static Stream OpenSource(SourceSelection selection, string destination)
    {
        if (selection.Device is not null) return WindowsStorage.OpenSource(selection.Device, destination);
        WindowsStorage.ValidateImageDestination(selection.Image!, destination);
        return new FileStream(selection.Image!, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private void BeginCancellableWork()
    {
        _cancellation = new();
        var acknowledgement = new Progress<bool>(_ =>
        {
            if (_pause?.IsPauseRequested == true && _state == "Pausing") SetState("Paused");
        });
        _pause = new() { Paused = () => ((IProgress<bool>)acknowledgement).Report(true) };
        SetBusy(true);
    }
    private void EndCancellableWork()
    {
        _cancellation?.Dispose(); _cancellation = null;
        _pause?.Dispose(); _pause = null;
        SetBusy(false); RenderMetrics();
        if (_closeAfterWork) Close();
    }
    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (_pause is null || _cancellation is null) return;
        if (_pause.IsPauseRequested) { _pause.Resume(); SetState(_exporting ? "Exporting" : "Scanning"); }
        else { _pause.Pause(); SetState("Pausing"); }
        UpdateActions(); RenderMetrics();
    }
    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _cancellation?.Cancel(); SetState("Stopping"); UpdateActions();
    }

    private void Upsert(RecoveredImage image)
    {
        if (_byOffset.TryGetValue(image.Offset, out var row)) row.Update(image);
        else
        {
            row = new ImageRow(image, L); row.PropertyChanged += Row_Changed;
            _byOffset[image.Offset] = row; _rows.Add(row);
        }
        QueueRows();
    }
    private void SyncReport(ScanReport report)
    {
        _bulkRows = true;
        try
        {
            using (CollectionViewSource.GetDefaultView(_rows).DeferRefresh())
                foreach (var image in report.Files) Upsert(image);
        }
        finally { _bulkRows = false; }
        CollectionViewSource.GetDefaultView(_rows).Refresh();
        UpdateActions();
    }
    private void TrySyncReport()
    {
        try { if (_output is not null) SyncReport(ResultFiles.LoadReport(_output)); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { }
    }
    private void Row_Changed(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(ImageRow.IsSelected) or "") QueueRows(); }
    private void QueueRows()
    {
        if (!_bulkRows && !_rowTimer.IsEnabled) _rowTimer.Start();
    }
    private void Results_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_ready) UpdateActions(); }
    private void SelectAll_Click(object sender, RoutedEventArgs e) => SetSelection(true);
    private void ClearSelection_Click(object sender, RoutedEventArgs e) => SetSelection(false);
    private void SetSelection(bool value)
    {
        _bulkRows = true;
        try { foreach (var row in _rows) row.IsSelected = value; }
        finally { _bulkRows = false; }
        UpdateActions();
    }
    private List<RecoveredImage> CheckedImages() => CollectionViewSource.GetDefaultView(_rows)
        .Cast<ImageRow>().Where(r => r.IsSelected).Select(r => r.Image).ToList();
    private bool _exporting;
    private async void SaveSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _output is null || _sessionSource is null) return;
        var selected = CheckedImages().Where(i => !i.IsSaved).ToList();
        if (selected.Count == 0) { Warn("NeedSelection"); return; }
        string output = _output; var sourceSelection = _sessionSource;
        bool allowChanged = SaveChangedCheck.IsChecked == true;
        BeginCancellableWork(); _exporting = true; SetState("Exporting");
        var token = _cancellation!.Token; var pause = _pause;
        var saved = new Progress<RecoveredImage>(Upsert);
        try
        {
            var report = await Task.Run(() =>
            {
                using var source = OpenSource(sourceSelection, output);
                return ResultFiles.SaveSelected(source, output, selected, token, saved, pause, allowChanged);
            });
            SyncReport(report); SetState("Exported");
        }
        catch (OperationCanceledException) { TrySyncReport(); SetState("Cancelled"); }
        catch (Exception ex) { TrySyncReport(); ShowError(ex); }
        finally { _exporting = false; EndCancellableWork(); }
    }

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _output is null || ResultsGrid.SelectedItem is not ImageRow row) return;
        string? name = Prompt("Rename", "RenamePrompt", Path.GetFileNameWithoutExtension(row.FileName));
        if (name is null) return;
        SetBusy(true);
        try { Upsert(await Task.Run(() => ResultFiles.Rename(_output, row.Image, name))); }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); if (_closeAfterWork) Close(); }
    }
    private async void BatchRename_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _output is null) return;
        var selected = CheckedImages();
        if (selected.Count == 0) { Warn("NeedSelection"); return; }
        string? prefix = Prompt("BatchRename", "BatchPrompt", "Trip");
        if (prefix is null) return;
        SetBusy(true);
        try
        {
            var renamed = await Task.Run(() => ResultFiles.RenameBatch(_output, selected, prefix));
            foreach (var item in renamed) Upsert(item);
            CollectionViewSource.GetDefaultView(_rows).Refresh();
        }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); if (_closeAfterWork) Close(); }
    }
    private string? Prompt(string titleKey, string promptKey, string initial)
    {
        var dialog = new Window
        {
            Owner = this, Title = L(titleKey), Width = 490, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (Brush)Resources["Card"], Foreground = (Brush)Resources["Text"],
            FlowDirection = RootLayout.FlowDirection, Icon = Icon
        };
        var layout = new StackPanel { Margin = new Thickness(22) };
        layout.Children.Add(new TextBlock { Text = L(promptKey), TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Resources["Text"], Margin = new Thickness(0, 0, 0, 12) });
        var input = new TextBox { Text = initial, MaxLength = 80, FlowDirection = FlowDirection.LeftToRight,
            Padding = new Thickness(10), Background = (Brush)Resources["Field"], Foreground = (Brush)Resources["Text"] };
        layout.Children.Add(input);
        layout.Children.Add(new TextBlock { Text = L("RenameHint"), TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Resources["Muted"], Margin = new Thickness(0, 12, 0, 12) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var save = new Button { Content = L("Save"), IsDefault = true, Padding = new Thickness(18, 8, 18, 8) };
        var cancel = new Button { Content = L("Cancel"), IsCancel = true, Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(8, 0, 0, 0) };
        save.Click += (_, _) =>
        {
            try { ResultFiles.ValidateBaseName(input.Text.Trim()); dialog.DialogResult = true; }
            catch (ArgumentException) { MessageBox.Show(dialog, L("BadName"), L(titleKey)); }
        };
        buttons.Children.Add(save); buttons.Children.Add(cancel); layout.Children.Add(buttons);
        dialog.Content = layout; dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }

    private void Sort_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) ApplySort(); }
    private void Direction_Click(object sender, RoutedEventArgs e)
    { _descending = !_descending; ApplySort(); DirectionButton.Content = L(_descending ? "Descending" : "Ascending"); }
    private void ApplySort()
    {
        string property = SortSelector.SelectedIndex switch { 1 => nameof(ImageRow.Bytes), 2 => nameof(ImageRow.FileType), _ => nameof(ImageRow.FileName) };
        var view = CollectionViewSource.GetDefaultView(_rows);
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new(property, _descending ? ListSortDirection.Descending : ListSortDirection.Ascending));
        }
    }
    private void OpenOutput_Click(object sender, RoutedEventArgs e)
    {
        if (_output is null || !Directory.Exists(_output)) return;
        try { Process.Start(new ProcessStartInfo(_output) { UseShellExecute = true }); }
        catch (Exception ex) { ShowError(ex); }
    }
    private void SetBusy(bool busy) { _busy = busy; UpdateActions(); }
    private void UpdateActions()
    {
        if (!_ready) return;
        SetupPanel.IsEnabled = !_busy;
        StartButton.IsEnabled = !_busy;
        PauseButton.IsEnabled = _busy && _pause is not null && _state != "Stopping";
        PauseButton.Content = L(_pause?.IsPauseRequested == true ? "Resume" : "Pause");
        StopButton.IsEnabled = _busy && _cancellation is not null && _state != "Stopping";
        OpenOutputButton.IsEnabled = !_busy && _output is not null && Directory.Exists(_output);
        RenameButton.IsEnabled = !_busy && _output is not null && ResultsGrid.SelectedItem is ImageRow;
        BatchButton.IsEnabled = !_busy && _output is not null && _rows.Any(r => r.IsSelected);
        SaveSelectedButton.IsEnabled = !_busy && _output is not null && _sessionSource is not null && _rows.Any(r => r.IsSelected && !r.Image.IsSaved);
        RenderCounts();
    }
    private void SetState(string key) { _state = key; RenderStatus(); }
    private void RenderStatus() => StatusText.Text = L(_state);
    private void RenderCounts()
    {
        FoundText.Text = _rows.Count.ToString("N0");
        SavedText.Text = _rows.Count(r => r.Image.IsSaved).ToString("N0");
        SelectedCountText.Text = L("Selected") + ": " + _rows.Count(r => r.IsSelected).ToString("N0");
        EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void RenderMetrics()
    {
        bool paused = _state is "Paused" or "Pausing";
        SpeedText.Text = _lastProgress is null || _exporting ? "—" : (paused ? 0 : _lastProgress.MegabytesPerSecond).ToString("F1") + " MB/s";
        double? seconds = paused || _exporting || _state is "Cancelled" or "Failed" or "Limit"
            ? null : _lastProgress?.RemainingSeconds;
        EtaText.Text = seconds is null || !double.IsFinite(seconds.Value) ? "—"
            : TimeSpan.FromSeconds(Math.Clamp(seconds.Value, 0, 864000000)).ToString(seconds >= 86400 ? @"d\.hh\:mm\:ss" : @"hh\:mm\:ss");
        RenderCounts();
    }
    private void Results_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var gridScroll = FindChild<ScrollViewer>(ResultsGrid);
        if (gridScroll is not null && (e.Delta > 0 && gridScroll.VerticalOffset > 0 || e.Delta < 0 && gridScroll.VerticalOffset < gridScroll.ScrollableHeight)) return;
        PageScroll.ScrollToVerticalOffset(PageScroll.VerticalOffset - e.Delta); e.Handled = true;
    }
    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } nested) return nested;
        }
        return null;
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_ready) return;
        if (_busy) { e.Cancel = true; _closeAfterWork = true; _cancellation?.Cancel(); return; }
        _settingsTimer.Stop(); _rowTimer.Stop(); PersistPreferences();
    }
    private void Warn(string key) => MessageBox.Show(this, L(key), "ImageRescue", MessageBoxButton.OK, MessageBoxImage.Warning);
    private void ShowError(Exception ex)
    {
        SetState("Failed");
        MessageBox.Show(this, L("ErrorHelp") + "\n\n" + ex.GetBaseException().Message,
            "ImageRescue", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
