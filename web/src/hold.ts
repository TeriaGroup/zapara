export type HoldKind = "text" | "image" | "video" | "file" | "voice" | "circle" | "sticker" | "card";

export function holdActions(kind: string, mine: boolean, deleted: boolean, held: boolean): string[] {
  if (!held || deleted) return [];
  const actions = ["reply", "reaction"];
  if (mine && kind === "text") actions.push("edit");
  if (mine) actions.push("delete");
  return actions;
}

export function runHold(action: string, ops: { reply(): void; reaction(): void; edit(): void; delete(): void }): void {
  if (!holdActions("text", true, false, true).includes(action) && action !== "reply" && action !== "reaction" && action !== "edit" && action !== "delete") return;
  if (action === "reply") ops.reply();
  else if (action === "reaction") ops.reaction();
  else if (action === "edit") ops.edit();
  else if (action === "delete") ops.delete();
}
