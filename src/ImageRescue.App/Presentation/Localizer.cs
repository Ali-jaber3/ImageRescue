using System.IO;
using System.Text.Json;
using System.Windows;

namespace ImageRescue.App.Presentation;

public sealed record LanguageChoice(string Code, string Name)
{
    public override string ToString() => Name;
}

public sealed class Localizer
{
    public static readonly LanguageChoice[] Languages =
    [
        new("ar", "العربية"), new("en", "English"), new("zh-Hans", "简体中文"),
        new("ru", "Русский"), new("es", "Español"), new("fr", "Français"),
        new("de", "Deutsch"), new("pt", "Português"), new("hi", "हिन्दी"), new("ja", "日本語")
    ];
    private Dictionary<string, string> _strings = [];
    public string Code { get; private set; } = "en";
    public string this[string key] => _strings.GetValueOrDefault(key, key);
    public void Apply(string code, ResourceDictionary resources)
    {
        if (!Languages.Any(l => l.Code == code)) code = "en";
        using var stream = typeof(Localizer).Assembly.GetManifestResourceStream(
            "ImageRescue.App.Assets.Languages." + code + ".json")
            ?? throw new IOException("Missing language resource: " + code);
        _strings = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
        Code = code;
        foreach (var item in _strings) resources["L." + item.Key] = item.Value;
    }
}
