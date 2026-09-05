using System.ComponentModel;
using Avalonia.Data;
using Vograph.Core.Services;

namespace Vograph.Desktop.Services;

/// <summary>Binding-friendly wrapper over Core's I18nService. Language switches at runtime without restart.</summary>
public sealed class Loc : INotifyPropertyChanged
{
    private static Loc? _current;

    public static Loc Current => _current ?? throw new InvalidOperationException("Loc.Init was not called");

    public static void Init(I18nService i18n) => _current = new Loc(i18n);

    public I18nService I18n { get; }

    public Loc(I18nService i18n)
    {
        I18n = i18n;
        i18n.LanguageChanged += () =>
        {
            LanguageChanged?.Invoke();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty)); // "everything changed"
        };
    }

    public event Action? LanguageChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Language => I18n.Language;
    public string this[string key] => I18n.T(key);
    public string T(string key, params object[] args) => I18n.T(key, args);
    public void SetLanguage(string lang) => I18n.SetLanguage(lang);

    /// <summary>Russian: 1 пара / 2 пары / 5 пар; English: one / many.</summary>
    public string Plural(int n, string oneKey, string fewKey, string manyKey)
    {
        string key;
        if (Language == "en")
        {
            key = n == 1 ? oneKey : manyKey;
        }
        else
        {
            int mod10 = n % 10, mod100 = n % 100;
            if (mod10 == 1 && mod100 != 11) key = oneKey;
            else if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14) key = fewKey;
            else key = manyKey;
        }
        return T(key, n);
    }
}

/// <summary>One localized string that notifies when the language changes. Target of {loc:T key} bindings.</summary>
public sealed class LocString : INotifyPropertyChanged
{
    private readonly Loc _loc;
    private readonly string _key;

    public LocString(Loc loc, string key)
    {
        _loc = loc;
        _key = key;
        loc.LanguageChanged += () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
    }

    public string Value => _loc.T(_key);
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>XAML: Text="{loc:T today}". Produces a one-way binding to LocString.Value.</summary>
public sealed class TExtension
{
    public TExtension(string key) => Key = key;

    public string Key { get; set; }

    public object ProvideValue(IServiceProvider serviceProvider) =>
        new ReflectionBinding(nameof(LocString.Value))
        {
            Source = new LocString(Loc.Current, Key),
            Mode = BindingMode.OneWay
        };
}

/// <summary>XAML: Text="{loc:TU navTools}" — the same as {loc:T} but upper-cased (section labels).</summary>
public sealed class TUExtension
{
    public TUExtension(string key) => Key = key;

    public string Key { get; set; }

    public object ProvideValue(IServiceProvider serviceProvider) =>
        new ReflectionBinding(nameof(LocString.Value))
        {
            Source = new LocString(Loc.Current, Key),
            Mode = BindingMode.OneWay,
            Converter = Converters.Upper
        };
}
