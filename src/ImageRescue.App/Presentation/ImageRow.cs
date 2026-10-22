using System.ComponentModel;
using ImageRescue.App.Recovery;

namespace ImageRescue.App.Presentation;

public sealed class ImageRow(RecoveredImage image, Func<string, string> translate) : INotifyPropertyChanged
{
    private bool _selected;
    public RecoveredImage Image { get; private set; } = image;
    public string FileName => Image.FileName;
    public string FileType => Image.FileType;
    public long Bytes => Image.Bytes;
    public string FileSize => Image.FileSize;
    public string Dimensions => Image.Dimensions;
    public string Camera => Image.Camera ?? "—";
    public string DateTaken => Image.DateTaken ?? "—";
    public string RecoveryDetails => Image.RecoveryNote is null ? "—" : translate(Image.RecoveryNote);
    public string SavedState => translate(Image.SourceChanged ? "SavedChanged" : Image.IsSaved ? "Saved" : "Catalogue");
    public bool IsSelected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; Changed(nameof(IsSelected)); }
    }
    public void Update(RecoveredImage image)
    {
        Image = image;
        Changed("");
    }
    public void LanguageChanged() { Changed(nameof(SavedState)); Changed(nameof(RecoveryDetails)); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
