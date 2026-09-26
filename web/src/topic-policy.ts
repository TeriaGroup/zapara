import type { AccessRule, GroupDesk, GroupTopic } from "./types";
export const groupOnlyPowers = new Set(["joins", "exclude", "roles", "grants"]);
export const topicRules = (rules: AccessRule[]) => rules.filter(rule => !groupOnlyPowers.has(rule.power));
export const globalPower = (desk: Pick<GroupDesk, "headman" | "mine">, power: string) => desk.headman || desk.mine.includes(power);
export function topicAction(topic: GroupTopic | null | undefined, power: string, desk: Pick<GroupDesk, "headman" | "mine"> | null, legacy = false) {
    if (!topic?.topicId || topic.supported === false || !["chat", "ballots", "forms", "materials", "homework", "schedule"].includes(topic.kind))
        return false;
    if (topic.permissions)
        return topic.permissions.includes("read") && topic.permissions.includes(power);
    return legacy && ["chat", "ballots"].includes(topic.kind) && !!desk && ["channels", "pin"].includes(power) && globalPower(desk, "channels");
}
export const canReadAudit = (desk: Pick<GroupDesk, "headman" | "mine">) => ["channels", "access", "roles"].some(power => globalPower(desk, power));
export type AccessPreset = {
    mode: "all" | "selected" | "headman" | "custom";
    roles: string[];
};
export function detectAccessPreset(rules: AccessRule[], power: "read" | "post"): AccessPreset {
    const rows = rules.filter(rule => rule.power === power && rule.state !== "inherit"), everyone = rows.find(rule => rule.roleId === null), roles = rows.filter(rule => rule.roleId !== null);
    if (power === "post" && !rows.length || power === "read" && everyone?.state === "allow" && !roles.length)
        return { mode: "all", roles: [] };
    if (everyone?.state === "deny" && roles.every(rule => rule.state === "allow")) {
        const allowed = roles.map(rule => rule.roleId!).sort();
        return { mode: power === "post" && !allowed.length ? "headman" : "selected", roles: allowed };
    }
    return { mode: "custom", roles: [] };
}
export function applyAccessPreset(rules: AccessRule[], power: "read" | "post", selection: AccessPreset): AccessRule[] {
    if (selection.mode === "custom")
        return rules;
    const retained = topicRules(rules).filter(rule => rule.power !== power);
    if (selection.mode === "all")
        return power === "read" ? [...retained, { roleId: null, power, state: "allow" }] : retained;
    const selected = selection.mode === "selected" ? [...new Set(selection.roles)].sort().map(roleId => ({ roleId, power, state: "allow" as const })) : [];
    return [...retained, { roleId: null, power, state: "deny" }, ...selected];
}
export function preservedPinMetadata(topic: GroupTopic, pinned: boolean, legacy: boolean) {
    const base = { description: topic.description, accent: topic.accent, pinned, writePolicy: topic.writePolicy };
    return legacy ? base : { ...base, template: topic.template ?? null, categoryId: topic.categoryId ?? null, position: topic.position ?? 0, subject: topic.subject ?? null, expectedRevision: topic.revision ?? 0 };
}
