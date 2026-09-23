using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Zapara.Client.Domain;

namespace Vograph.Desktop.Features.Groups;

public sealed class HoldBox : Border
{
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
            if (!actions.Contains(only)) return;
            MessengerHold.Perform(only, () => row.Apply("reply"), () => row.Apply("reaction"), () => row.Apply("edit"), () => row.Apply("delete"));
            return;
        }
        var menu = new ContextMenu();
        foreach (var action in actions)
        {
            var item = new MenuItem { Header = action switch { "reply" => "Ответить", "reaction" => "Реакция", "edit" => "Изменить", "delete" => "Удалить", _ => action } };
            var name = action;
            item.Click += (_, _) => MessengerHold.Perform(name, () => row.Apply("reply"), () => row.Apply("reaction"), () => row.Apply("edit"), () => row.Apply("delete"));
            menu.Items.Add(item);
        }
        if (actions.Count > 0) menu.Open(this);
    }
}
