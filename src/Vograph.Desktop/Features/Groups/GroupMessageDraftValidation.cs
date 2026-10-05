using System.Buffers;
using System.Text;

namespace Vograph.Desktop.Features.Groups;

public readonly record struct GroupMessageDraftState(int Count, string Error)
{
    public bool IsValid => Error.Length == 0;
    public string Label => IsValid ? $"{Count} из 2000 символов" : $"{Count} из 2000 символов · {Error}";
}

public static class GroupMessageDraftValidation
{
    public static GroupMessageDraftState Check(string draft, string context, bool editing = false)
    {
        var text = ((editing || context.Length == 0 ? "" : context + "\n\n") + draft.Trim())
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var remaining = text.AsSpan();
        var count = 0;
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out var rune, out var consumed) != OperationStatus.Done)
                return new(count, "Текст содержит повреждённый символ.");
            if (rune.Value == 0 || Rune.IsControl(rune) && rune.Value is not (9 or 10))
                return new(count, "Уберите недопустимый управляющий символ.");
            count++;
            remaining = remaining[consumed..];
        }
        if (count > 2000) return new(count, "Сообщение вместе с контекстом длиннее 2000 символов.");
        if (string.IsNullOrWhiteSpace(text)) return new(count, "Введите сообщение.");
        return new(count, "");
    }
}
