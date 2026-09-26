using Zapara.Contracts.Communities;
namespace Zapara.Server.Communities;

public static class GroupPermissionRules
{
    public static readonly string[] Powers = ["read", "post", "media", "vote", "formsRespond", "ballots", "forms", "close", "pin", "moderate", "homework", "mentionAll", "joins", "exclude", "channels", "access", "roles", "grants"];
    public static readonly string[] GroupOnlyPowers = ["joins", "exclude", "roles", "grants"];
    public static readonly string[] Templates = ["chat", "announcements", "polls", "forms", "subject", "materials", "homework", "schedule"];
    public static GroupCapabilitiesResponse Capabilities => new(12, 3, 24, Powers, Templates);
    public static bool Supported(string kind, string template) => template switch
    {
        "chat" or "announcements" or "subject" => kind == "chat",
        "polls" => kind == "ballots",
        "forms" or "materials" or "homework" or "schedule" => kind == template,
        _ => false
    };
    public sealed record Resolution(string[] Permissions, IReadOnlyDictionary<string, string> Sources);

    public static string[] Calculate(bool member, bool headman, bool curator, IEnumerable<string> rolePowers,
        IEnumerable<Guid> roleIds, IEnumerable<GroupAccessRule> rules, string kind, string template, bool archived, string writePolicy)
        => Resolve(member, headman, curator, rolePowers, roleIds, rules, kind, template, archived, writePolicy).Permissions;

    // Both normal authorization and access preview use this exact ordered resolver.
    public static Resolution Resolve(bool member, bool headman, bool curator, IEnumerable<string> rolePowers,
        IEnumerable<Guid> roleIds, IEnumerable<GroupAccessRule> rules, string kind, string template, bool archived, string writePolicy)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var sources = Powers.ToDictionary(p => p, _ => member ? "Право не выдано" : "Нет активного членства в этой группе", StringComparer.Ordinal);
        if (!member) return new([], sources);
        if (headman) Set(Powers, true, "Защищённая роль старосты этой группы");
        else
        {
            Set(["read", "post", "media", "vote", "formsRespond"], true, "Базовые права участника");
            foreach (var power in rolePowers.Where(Powers.Contains))
                if (!result.Contains(power)) Set([power], true, "Разрешение дополнительных ролей группы");
            if (writePolicy == "managers" && !result.Contains("channels")) Set(["post"], false, "Публиковать могут управляющие каналом");
            if (template == "announcements") Set(["post"], false, "Шаблон объявлений ограничивает публикацию");
            var selected = roleIds.ToHashSet();
            var overrides = rules.ToArray();
            Apply(overrides.Where(r => r.RoleId is null));
            Apply(overrides.Where(r => r.RoleId is Guid id && selected.Contains(id)));
            // Channel ACL cannot remove the curator's protected group join authority.
            if (curator) Set(["joins"], true, "Защищённое право куратора принимать заявки");
        }
        if (!result.Contains("read")) Set(["post", "media", "vote", "formsRespond", "ballots", "forms", "close", "pin", "moderate", "homework", "mentionAll"], false, "Недоступно без просмотра канала");
        else if (!result.Contains("post")) Set(["media", "ballots", "forms", "homework", "mentionAll"], false, "Недоступно без публикации в канале");
        if (!Supported(kind, template)) Set(Powers.Where(p => p != "read" && !GroupOnlyPowers.Contains(p)), false, "Неподдерживаемый тип канала: только чтение");
        else
        {
            var typeReason = "Тип канала «" + template + "» не поддерживает это действие";
            if (kind == "schedule") Set(["post", "media", "vote", "formsRespond", "ballots", "forms", "homework", "mentionAll"], false, typeReason);
            if (kind != "forms") Set(["forms", "formsRespond"], false, typeReason);
            if (kind != "ballots") Set(["ballots", "vote", "close"], false, typeReason);
            if (kind is not ("chat" or "materials")) Set(["media"], false, typeReason);
            if (kind != "homework") Set(["homework"], false, typeReason);
        }
        if (archived) Set(["post", "media", "vote", "formsRespond", "ballots", "forms", "close", "pin", "moderate", "homework", "mentionAll"], false, "Канал в архиве: изменения и ответы запрещены");
        foreach (var power in GroupOnlyPowers) sources[power] += "; групповое полномочие, правила канала его не меняют";
        return new(Powers.Where(result.Contains).ToArray(), sources);

        void Set(IEnumerable<string> powers, bool allow, string source)
        {
            foreach (var power in powers)
            {
                if (!sources.ContainsKey(power)) continue;
                if (allow) result.Add(power); else result.Remove(power);
                sources[power] = source;
            }
        }
        void Apply(IEnumerable<GroupAccessRule> sequence)
        {
            var items = sequence.Where(r => !GroupOnlyPowers.Contains(r.Power)).ToArray();
            foreach (var state in new[] { "deny", "allow" })
                foreach (var group in items.Where(r => r.State == state).GroupBy(r => r.Power))
                {
                    var ids = group.Where(r => r.RoleId is not null).Select(r => r.RoleId!.Value).Order().ToArray();
                    var source = (state == "allow" ? "Разрешение" : "Запрет") + (ids.Length == 0 ? " для всех участников" : " роли: " + string.Join(", ", ids));
                    if (ids.Length > 0 && state == "allow") source += "; применяется после запретов ролей";
                    Set([group.Key], state == "allow", source);
                }
        }
    }
    public static bool CanDelegate(bool headman, int actorPosition, int targetPosition, IEnumerable<string> actorPowers, IEnumerable<string> targetPowers)
        => headman || actorPosition > targetPosition && targetPowers.All(actorPowers.Contains);
}
