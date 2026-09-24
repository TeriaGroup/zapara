using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Zapara.Client.Domain;

namespace Vograph.Desktop.Features.Groups;

public sealed class HoldBox : Border
{
    internal static readonly (string Code, string Label)[] ReactionChoices =
    [
        ("like", "👍 Нравится"), ("heart", "❤️ Сердце"), ("laugh", "😂 Смех"),
        ("wow", "😮 Удивление"), ("sad", "😢 Грусть")
    ];
    private DispatcherTimer? timer;

    public HoldBox()
    {
        PointerPressed += (_, eventArgs) =>
        {
            if (eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
                timer.Tick += (_, _) =>
                {
                    timer?.Stop();
                    Open();
                };
                timer.Start();
            }
        };
        PointerReleased += (_, _) => timer?.Stop();
    }

    private void Open() => Choose(null);

    public void Choose(string? only)
    {
        if (DataContext is not GroupMessageRow row) return;
        var actions = MessengerHold.Actions(row.Kind, row.Mine, row.Deleted, true);
        if (only is not null)
        {
            if (only.StartsWith("reaction:", StringComparison.Ordinal))
            {
                var emoji = only["reaction:".Length..];
                if (actions.Contains("reaction") && ReactionChoices.Any(choice => choice.Code == emoji))
                    row.Apply(only);
                return;
            }
            if (!actions.Contains(only)) return;
            if (only == "reaction") { OpenReactions(row); return; }
            MessengerHold.Perform(only, () => row.Apply("reply"), () => { }, () => row.Apply("edit"), () => row.Apply("delete"));
            return;
        }
        var menu = new ContextMenu();
        foreach (var action in actions)
        {
            var item = new MenuItem { Header = action switch { "reply" => "Ответить", "reaction" => "Реакция", "edit" => "Изменить", "delete" => "Удалить", _ => action } };
            if (action == "reaction")
            {
                foreach (var choice in ReactionChoices)
                {
                    var emoji = choice.Code;
                    var reaction = new MenuItem { Header = choice.Label };
                    reaction.Click += (_, _) => row.Apply("reaction:" + emoji);
                    item.Items.Add(reaction);
                }
                menu.Items.Add(item);
                continue;
            }
            var name = action;
            item.Click += (_, _) => MessengerHold.Perform(name, () => row.Apply("reply"), () => { }, () => row.Apply("edit"), () => row.Apply("delete"));
            menu.Items.Add(item);
        }
        if (actions.Count > 0) menu.Open(this);
    }

    private void OpenReactions(GroupMessageRow row)
    {
        var menu = new ContextMenu();
        foreach (var choice in ReactionChoices)
        {
            var emoji = choice.Code;
            var item = new MenuItem { Header = choice.Label };
            item.Click += (_, _) => row.Apply("reaction:" + emoji);
            menu.Items.Add(item);
        }
        menu.Open(this);
    }
}
