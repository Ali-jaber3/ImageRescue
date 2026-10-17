using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace ImageRescue.App;

public partial class MainWindow : Window
{
    private bool _ready;
    private bool _arabic = true;
    private string _statusKey = "ready";

    public MainWindow()
    {
        InitializeComponent();

        // Initialize only after every named control exists.
        LanguageSelector.SelectedIndex = 1;
        ThemeSelector.SelectedIndex = 0;

        _ready = true;

        ApplyTheme();
        ApplyLanguage();
    }

    private string Translate(string english, string arabic)
    {
        return _arabic ? arabic : english;
    }

    private void Language_Changed(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_ready)
            return;

        ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        _arabic = LanguageSelector.SelectedIndex == 1;

        RootLayout.FlowDirection = _arabic
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

        LanguageLabel.Text = Translate("Language", "اللغة");
        ThemeLabel.Text = Translate("Appearance", "المظهر");

        DarkThemeItem.Content = Translate("Dark", "داكن");
        LightThemeItem.Content = Translate("Light", "فاتح");
        GrayThemeItem.Content = Translate("Soft gray", "رمادي متوسط");

        SubtitleText.Text = Translate(
            "A simple workspace for recovering your photos.",
            "مساحة بسيطة لاستعادة صورك.");

        SetupTitle.Text = Translate(
            "Recovery setup",
            "إعداد الاسترجاع");

        SourceLabel.Text = Translate(
            "Source disk image (.img)",
            "ملف صورة القرص (.img)");

        DestinationLabel.Text = Translate(
            "Recovery destination",
            "مجلد حفظ الصور المسترجعة");

        BrowseButton.Content = Translate(
            "Browse IMG",
            "اختيار ملف IMG");

        FolderButton.Content = Translate(
            "Choose folder",
            "اختيار المجلد");

        ScanButton.Content = Translate(
            "Start scan",
            "بدء الفحص");

        SupportText.Text = Translate(
            "JPEG / PNG • Raw disk images",
            "JPG / PNG • ملفات صورة القرص");

        ResultsTitle.Text = Translate(
            "Recovery results",
            "نتائج الاسترجاع");

        FileColumn.Header = Translate("File", "الملف");
        TypeColumn.Header = Translate("Type", "النوع");
        SizeColumn.Header = Translate("Size", "الحجم");
        StatusColumn.Header = Translate("Status", "الحالة");

        EmptyTitle.Text = Translate(
            "No results yet",
            "لا توجد نتائج بعد");

        EmptyHint.Text = Translate(
            "Your recovered images will appear here.",
            "ستظهر الصور المسترجعة هنا.");

        UpdateStatus();
    }

    private void Theme_Changed(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_ready)
            return;

        ApplyTheme();
    }

    private void ApplyTheme()
    {
        string[] colors = ThemeSelector.SelectedIndex switch
        {
            // Light theme
            1 => new[]
            {
                "#F2F5FA", // Page
                "#FFFFFF", // Card
                "#F8FAFC", // Field
                "#EDF2F8", // Surface
                "#D7E0EC", // Line
                "#17243B", // Text
                "#596B84", // Muted
                "#0369A1", // Accent
                "#FFFFFF"  // AccentText
            },

            // Medium gray theme
            2 => new[]
            {
                "#D9DEE7",
                "#EDF0F5",
                "#E2E7EF",
                "#D5DDE8",
                "#B4C0D0",
                "#1E293B",
                "#4B5C73",
                "#075985",
                "#FFFFFF"
            },

            // Dark theme
            _ => new[]
            {
                "#0B1120",
                "#141E30",
                "#0C1526",
                "#1D2B41",
                "#304158",
                "#F1F5F9",
                "#94A3B8",
                "#22D3EE",
                "#082F49"
            }
        };

        string[] keys =
        {
            "Page",
            "Card",
            "Field",
            "Surface",
            "Line",
            "Text",
            "Muted",
            "Accent",
            "AccentText"
        };

        for (int i = 0; i < keys.Length; i++)
        {
            var color = (Color)ColorConverter.ConvertFromString(colors[i]);
            var brush = new SolidColorBrush(color);
            brush.Freeze();

            Resources[keys[i]] = brush;
        }
    }

    private void SetStatus(string key)
    {
        _statusKey = key;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        StatusText.Text = _statusKey switch
        {
            "source" => Translate(
                "Source image selected.",
                "تم اختيار ملف صورة القرص."),

            "destination" => Translate(
                "Recovery destination selected.",
                "تم اختيار مجلد حفظ الصور."),

            "validated" => Translate(
                "Paths validated. The recovery engine is not implemented yet.",
                "الاختيارات صحيحة. محرك استخراج الصور لم يُضف بعد."),

            _ => Translate(
                "Ready — select an IMG file and a recovery destination.",
                "جاهز — اختر ملف IMG ومجلد حفظ الصور.")
        };
    }

    private void BrowseImage_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Translate(
                "Select a raw disk image",
                "اختيار ملف صورة القرص"),

            Filter = Translate(
                "Raw disk images (*.img)|*.img",
                "ملفات صورة القرص (*.img)|*.img"),

            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            SourcePathBox.Text = dialog.FileName;
            SetStatus("source");
        }
    }

    private void ChooseFolder_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = Translate(
                "Choose a recovery destination",
                "اختيار مجلد حفظ الصور"),

            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            DestinationPathBox.Text = dialog.FolderName;
            SetStatus("destination");
        }
    }

    private void StartScan_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!File.Exists(SourcePathBox.Text))
        {
            ShowWarning(
                Translate(
                    "Please select an existing IMG file.",
                    "اختر ملف IMG موجودًا على جهازك."),
                Translate("Source required", "ملف المصدر مطلوب"));

            return;
        }

        if (!Directory.Exists(DestinationPathBox.Text))
        {
            ShowWarning(
                Translate(
                    "Please select an existing destination folder.",
                    "اختر مجلدًا موجودًا لحفظ الصور."),
                Translate("Destination required", "مجلد الحفظ مطلوب"));

            return;
        }

        SetStatus("validated");
    }

    private void ShowWarning(string message, string title)
    {
        var options = _arabic
            ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign
            : MessageBoxOptions.None;

        MessageBox.Show(
            this,
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning,
            MessageBoxResult.OK,
            options);
    }
}