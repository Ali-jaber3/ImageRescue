using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using ImageRescue.App.Recovery;

namespace ImageRescue.App;

public partial class MainWindow : Window
{
    private bool _ready, _busy, _closeAfterScan;
    private bool _arabic = true;
    private string _state = "ready";
    private string? _imagePath, _output;
    private StorageTarget? _device;
    private CancellationTokenSource? _cancellation;
    private readonly ObservableCollection<RecoveredImage> _files = [];

    public MainWindow()
    {
        InitializeComponent();
        ResultsGrid.ItemsSource = _files;
        LanguageSelector.SelectedIndex = 1;
        ThemeSelector.SelectedIndex = 0;
        _ready = true;
        ApplyTheme();
        ApplyLanguage();
        Loaded += async (_, _) => await RefreshDevices();
    }

    private string T(string english, string arabic) => _arabic ? arabic : english;

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready) ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        _arabic = LanguageSelector.SelectedIndex == 1;
        RootLayout.FlowDirection = _arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        LanguageLabel.Text = T("Language", "اللغة");
        ThemeLabel.Text = T("Appearance", "المظهر");
        DarkThemeItem.Content = T("Dark", "داكن");
        LightThemeItem.Content = T("Light", "فاتح");
        GrayThemeItem.Content = T("Soft gray", "رمادي متوسط");
        SubtitleText.Text = T("Photo recovery • USB / SD / IMG", "استعادة الصور • USB / SD / IMG");
        SetupTitle.Text = T("Recovery setup", "إعداد الاسترجاع");
        SourceLabel.Text = T("Select a USB / SD volume, or open an IMG file", "اختر قسم الفلاش أو بطاقة SD، أو افتح ملف IMG");
        DestinationLabel.Text = T("Destination folder on another physical disk", "مجلد الحفظ على قرص فعلي آخر");
        RefreshButton.Content = T("Refresh devices", "تحديث الأجهزة");
        BrowseButton.Content = T("Open IMG instead", "اختيار ملف IMG بدلًا من الفلاش");
        OpenOutputButton.Content = T("Open results folder", "فتح مجلد النتائج");
        FolderButton.Content = T("Choose folder", "اختيار المجلد");
        ScanButton.Content = T("Scan and recover", "فحص واسترجاع");
        CancelButton.Content = T("Cancel", "إلغاء");
        SupportText.Text = T(
            "Read-only scan of the selected volume, not the whole physical device. Finds existing and deleted contiguous JPEG/PNG images. Use another disk for output. Administrator access is required for USB/SD.",
            "فحص القسم المختار للقراءة فقط، وليس كامل الجهاز الفعلي. يبحث عن صور JPG وPNG المتصلة، الموجودة والمحذوفة. الحفظ على قرص آخر. فحص USB وSD يحتاج تشغيل البرنامج كمسؤول.");
        ResultsTitle.Text = T("Recovered images", "الصور المسترجعة");
        FileColumn.Header = T("File", "الملف");
        TypeColumn.Header = T("Type", "النوع");
        SizeColumn.Header = T("Size", "الحجم");
        OffsetColumn.Header = T("Byte offset", "الموقع بالبايت");
        EmptyHint.Text = T(
            "Images are saved as they are found. Structural checks do not guarantee complete pixels. Limit: 64 MiB / 40 megapixels per image. A session report is saved automatically.",
            "تُحفظ الصور عند العثور عليها. فحص البنية لا يضمن سلامة جميع البكسلات. الحد للصورة: 64 MiB و40 ميجابكسل. يُحفظ تقرير الجلسة تلقائيًا.");
        UpdateStatus();
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready) ApplyTheme();
    }

    private void ApplyTheme()
    {
        string[] colors = ThemeSelector.SelectedIndex switch
        {
            1 => ["#F2F5FA", "#FFFFFF", "#F8FAFC", "#EDF2F8", "#D7E0EC", "#17243B", "#596B84", "#0369A1", "#FFFFFF"],
            2 => ["#D9DEE7", "#EDF0F5", "#E2E7EF", "#D5DDE8", "#B4C0D0", "#1E293B", "#4B5C73", "#075985", "#FFFFFF"],
            _ => ["#0B1120", "#141E30", "#0C1526", "#1D2B41", "#304158", "#F1F5F9", "#94A3B8", "#22D3EE", "#082F49"]
        };
        string[] keys = ["Page", "Card", "Field", "Surface", "Line", "Text", "Muted", "Accent", "AccentText"];
        for (int i = 0; i < keys.Length; i++)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
            brush.Freeze();
            Resources[keys[i]] = brush;
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshDevices();

    private async Task RefreshDevices()
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var targets = await Task.Run(WindowsStorage.ListTargets);
            DeviceSelector.ItemsSource = targets;
            _device = null;
            if (_imagePath is null) SourcePathBox.Clear();
            _state = targets.Count == 0 ? "noDevices" : "ready";
        }
        catch (Exception ex) { ShowError(ex); }
        finally
        {
            SetBusy(false);
            UpdateStatus();
            if (_closeAfterScan) Close();
        }
    }

    private void Device_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (DeviceSelector.SelectedItem is StorageTarget device)
        {
            _device = device;
            _imagePath = null;
            SourcePathBox.Text = device.ToString();
            _state = "source";
            UpdateStatus();
        }
    }

    private void BrowseImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = T("Open disk image", "فتح ملف صورة القرص"),
            Filter = "Raw disk image (*.img)|*.img", CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        DeviceSelector.SelectedIndex = -1;
        _device = null;
        _imagePath = dialog.FileName;
        SourcePathBox.Text = _imagePath;
        _state = "source";
        UpdateStatus();
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = T("Choose destination", "اختيار مجلد الحفظ") };
        if (dialog.ShowDialog(this) != true) return;
        DestinationPathBox.Text = dialog.FolderName;
        _state = "destination";
        UpdateStatus();
    }

    private async void StartScan_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_device is null && (_imagePath is null || !File.Exists(_imagePath)))
        {
            Warn(T("Select a USB/SD volume or an IMG file first.", "اختر الفلاش أو بطاقة SD أو ملف IMG أولًا."));
            return;
        }
        if (!Directory.Exists(DestinationPathBox.Text))
        {
            Warn(T("Choose an existing destination folder.", "اختر مجلدًا موجودًا لحفظ النتائج."));
            return;
        }
        if (_device is not null && !WindowsStorage.IsAdministrator)
        {
            Warn(T("Close the app and launch its EXE using Run as administrator.",
                "أغلق البرنامج وشغّل ملف EXE باستخدام تشغيل كمسؤول."));
            return;
        }

        var device = _device;
        string? image = _imagePath;
        string destination = DestinationPathBox.Text;
        _files.Clear();
        ScanProgress.Value = 0;
        _output = null;
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        _state = "scanning";
        SetBusy(true);
        UpdateStatus();
        // Progress<T> marshals callbacks onto the WPF UI thread.
        var progress = new Progress<ScanUpdate>(p =>
        {
            ScanProgress.Value = Math.Clamp(p.Percent, 0, 100);
            if (_busy) UpdateStatus();
        });
        var found = new Progress<RecoveredImage>(item => _files.Add(item));
        try
        {
            string output = Path.Combine(destination,
                $"ImageRescue_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}");
            var report = await Task.Run(() =>
            {
                using Stream source = device is not null
                    ? WindowsStorage.OpenSource(device, destination)
                    : OpenImage(image!, destination);
                _output = output;
                return Carver.Scan(source, device?.Root ?? image!, output, progress, found, token);
            });
            _state = report.Outcome switch
            {
                "Cancelled" => "cancelled",
                "ResultLimitReached" => "limit",
                _ => "completed"
            };
        }
        catch (Exception ex)
        {
            _state = "failed";
            ShowError(ex);
        }
        finally
        {
            SetBusy(false);
            UpdateStatus();
            _cancellation.Dispose();
            _cancellation = null;
            if (_closeAfterScan) Close();
        }
    }

    private static Stream OpenImage(string image, string destination)
    {
        WindowsStorage.ValidateImageDestination(image, destination);
        return new FileStream(image, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cancellation?.Cancel();
        CancelButton.IsEnabled = false;
        _state = "cancelling";
        UpdateStatus();
    }

    private void OpenOutput_Click(object sender, RoutedEventArgs e)
    {
        if (_output is null || !Directory.Exists(_output)) return;
        try { Process.Start(new ProcessStartInfo(_output) { UseShellExecute = true }); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        DeviceSelector.IsEnabled = RefreshButton.IsEnabled = BrowseButton.IsEnabled =
            FolderButton.IsEnabled = ScanButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy && _cancellation is not null;
        OpenOutputButton.IsEnabled = !busy && _output is not null && Directory.Exists(_output);
    }

    private void UpdateStatus()
    {
        string message = _state switch
        {
            "source" => T("Source selected.", "تم اختيار المصدر."),
            "destination" => T("Destination selected.", "تم اختيار مجلد الحفظ."),
            "noDevices" => T("No supported USB/SD volumes found. Connect one and refresh; if needed, run as administrator.", "لم يُعثر على قسم USB أو SD مدعوم. وصّل الفلاش واضغط تحديث الأجهزة؛ شغّل كمسؤول عند الحاجة."),
            "scanning" => T($"Scanning {ScanProgress.Value:F1}% — {_files.Count} images saved", $"جارٍ الفحص {ScanProgress.Value:F1}% — تم حفظ {_files.Count} صورة"),
            "cancelling" => T("Cancelling… completed images will be kept.", "جارٍ الإلغاء… ستبقى الصور المكتملة محفوظة."),
            "completed" => T($"Completed — {_files.Count} images. Open the results folder.", $"اكتمل الفحص — {_files.Count} صورة. افتح مجلد النتائج."),
            "cancelled" => T($"Cancelled — {_files.Count} completed images kept.", $"أُلغي الفحص — حُفظت {_files.Count} صورة مكتملة."),
            "limit" => T("Stopped at the 10,000-image limit. See the session report.", "توقف عند حد 10,000 صورة. راجع تقرير الجلسة."),
            "failed" => T("Scan stopped with an error. Any completed images were kept.", "توقف الفحص بسبب خطأ. الصور المكتملة تبقى محفوظة."),
            _ => T("Ready — select a source and a destination on another disk.", "جاهز — اختر المصدر ومجلد حفظ على قرص آخر.")
        };
        StatusText.Text = message;
    }

    // Let the results grid scroll first, then pass wheel input to the page at its edges.
    private void Results_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var gridScroll = FindVisualChild<ScrollViewer>(ResultsGrid);
        if (gridScroll is not null &&
            (e.Delta > 0 && gridScroll.VerticalOffset > 0 ||
             e.Delta < 0 && gridScroll.VerticalOffset < gridScroll.ScrollableHeight)) return;
        var page = (ScrollViewer)Content;
        page.ScrollToVerticalOffset(page.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private static TChild? FindVisualChild<TChild>(DependencyObject parent) where TChild : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TChild result) return result;
            if (FindVisualChild<TChild>(child) is { } nested) return nested;
        }
        return null;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_busy) return;
        e.Cancel = true;
        _closeAfterScan = true;
        _cancellation?.Cancel();
    }

    private void Warn(string text) => MessageBox.Show(this, text,
        T("ImageRescue", "تنبيه"), MessageBoxButton.OK, MessageBoxImage.Warning);

    private void ShowError(Exception ex)
    {
        string message = T("The scan could not continue. Check administrator access, device connection and destination disk.",
            "تعذر إكمال العملية. تحقق من صلاحية المسؤول واتصال الفلاش وأن مجلد الحفظ على قرص آخر.");
        MessageBox.Show(this, message + "\n\n" + ex.GetBaseException().Message,
            "ImageRescue", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
