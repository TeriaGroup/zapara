namespace Zapara.Client.Domain;

public static class MessengerHold
{
    public static IReadOnlyList<string> Actions(string kind, bool mine, bool deleted, bool held)
    {
        if (!held || deleted) return [];
        var actions = new List<string> { "reply", "reaction" };
        if (mine && kind == "text") actions.Add("edit");
        if (mine) actions.Add("delete");
        return actions;
    }

    public static void Perform(string action, Action reply, Action reaction, Action edit, Action delete)
    {
        if (action == "reply") reply();
        else if (action == "reaction") reaction();
        else if (action == "edit") edit();
        else if (action == "delete") delete();
    }
}
