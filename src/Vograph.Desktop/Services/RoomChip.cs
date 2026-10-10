using Avalonia.Data.Converters;

namespace Vograph.Desktop.Services;

/// <summary>R2-13: чип аудитории показываем, только если аудитория известна. Для «Доп. подготовки» без аудитории
/// раньше висел пустой серый чип «—» (2.86:1).</summary>
public static class RoomChip
{
    public static bool HasRoom(string? room) => !string.IsNullOrWhiteSpace(room) && room.Trim() != "—";

    public static readonly IValueConverter Visible = new FuncValueConverter<string?, bool>(HasRoom);
}
