using Avalonia.Data.Converters;

namespace Vograph.Desktop.Features.Preferences;

/// <summary>
/// R2-02: что видит гость в «Помощь → Поддержка». Сервер принимает обращения только от вошедших, поэтому гостю
/// форму не показываем: если вход работает — предлагаем войти, если нет — честно говорим об этом и, если задан,
/// даём другой канал связи.
/// </summary>
public static class SupportGate
{
    /// <summary>Другой канал поддержки для гостей (страница или mailto:). Продуктовое решение: адреса пока нет,
    /// поэтому null — кнопка «Написать в поддержку» скрыта. Выдумывать адрес нельзя.</summary>
    public const string? FallbackUrl = null;

    public static bool HasFallback => !string.IsNullOrWhiteSpace(FallbackUrl);

    public static string GuestText(bool signInWorks, bool hasFallback = false) => signInWorks
        ? "Чтобы написать в поддержку и получить ответ, войдите в аккаунт."
        : "Обращения в поддержку отправляются через аккаунт, а вход сейчас временно недоступен. "
          + (hasFallback ? "Напишите нам по ссылке ниже." : "Расписание, карты и домашка работают и без входа; форма обращения появится здесь, когда вход заработает.");

    /// <summary>Для XAML: текст по AccountPanel.SignInWorks — без подписки окна настроек на общую панель аккаунта.</summary>
    public static readonly IValueConverter GuestTextConverter = new FuncValueConverter<bool, string>(works => GuestText(works, HasFallback));
}
