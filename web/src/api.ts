import { getGroupMedia, postGroupMedia, type GroupMediaDownload } from "./group-media";
import { groupMessageQuery, type GroupMessageCursor } from "./groupChat";
import type { BallotBoard, ChatMessage, Community, Conversation, GroupDesk, GroupHomeworkCopy, GroupHome, GroupTopic, GroupsPayload, MapsManifest, Session, SocialHome, SocialMessage, SocialPage, Teacher, TeacherLesson, TimetablePayload } from "./types";

const cacheKey = "zapara.react.cache.v1";

export type Cache = {
  groups?: GroupsPayload;
  lessons: Record<string, TimetablePayload>;
};

export function readCache(): Cache {
  try {
    const raw = localStorage.getItem(cacheKey);
    if (!raw) return { lessons: {} };
    const parsed = JSON.parse(raw) as Cache;
    return { groups: parsed.groups, lessons: parsed.lessons ?? {} };
  } catch {
    return { lessons: {} };
  }
}

export function writeCache(cache: Cache) {
  localStorage.setItem(cacheKey, JSON.stringify(cache));
}

async function read<T>(url: string): Promise<T> {
  const response = await fetch(url, { credentials: "same-origin", headers: authHeaders() });
  if (!response.ok) throw new Error(String(response.status));
  return response.json() as Promise<T>;
}

export function loadGroups() {
  return read<GroupsPayload>("/api/v1/groups");
}

export function loadTimetable(groupId: string) {
  return read<TimetablePayload>("/api/v1/groups/" + encodeURIComponent(groupId) + "/timetable");
}

export function loadMaps() {
  return read<MapsManifest>("/api/v1/maps/manifest");
}

export function loadTeachers() {
  return read<{ lecturers: Teacher[] }>("/api/v1/teachers");
}

export function loadTeacher(id: string) {
  return read<{ lecturer: Teacher; lessons: TeacherLesson[] }>("/api/v1/teachers/" + encodeURIComponent(id) + "/timetable");
}

let csrf = "";
let familyId = "";

function authHeaders(json = false): Record<string, string> {
  const headers: Record<string, string> = { Accept: "application/json" };
  if (csrf) headers["X-Zapara-CSRF"] = csrf;
  if (familyId) headers["X-Zapara-Family"] = familyId;
  if (json) headers["Content-Type"] = "application/json";
  return headers;
}

function remember(value: { csrfToken?: string; familyId?: string | null }) {
  if (value.csrfToken) csrf = value.csrfToken;
  if (value.familyId) familyId = value.familyId;
}

export async function session(): Promise<Session> {
  const value = await read<Session>("/web-api/session");
  remember(value);
  return value;
}

async function send<T>(method: string, url: string, body?: unknown, empty = false): Promise<T> {
  const headers = authHeaders(!empty && body !== undefined);
  if (!empty) headers["Content-Type"] = "application/json";
  const response = await fetch(url, {
    method,
    credentials: "same-origin",
    headers,
    body: empty ? "" : body === undefined ? undefined : JSON.stringify(body)
  });
  if (!response.ok) throw new Error(String(response.status));
  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}

export type SupportAttachment = { id: string; kind: "photo" | "log"; name: string };
export type SupportLine = { author: string; body: string; at: string; attachments?: SupportAttachment[] };
export type SupportThread = { id: string; subject: string; messages: SupportLine[] };

export function supportList() {
  return read<SupportThread[]>("/web-api/support");
}

export function supportOpen(subject: string, body: string, photos: File[] = [], logs: File[] = []) {
  return supportSend("/web-api/support", { subject, body }, photos, logs);
}

export function supportReply(id: string, body: string, photos: File[] = [], logs: File[] = []) {
  return supportSend("/web-api/support/" + id, { body }, photos, logs);
}

async function supportSend(path: string, fields: Record<string, string>, photos: File[], logs: File[]) {
  if (photos.length === 0 && logs.length === 0) return send<SupportThread>("POST", path, fields);
  const form = new FormData();
  for (const [key, value] of Object.entries(fields)) form.append(key, value);
  for (const file of photos) form.append("photo", file, file.name);
  for (const file of logs) form.append("log", file, file.name);
  const response = await fetch(path, { method: "POST", credentials: "same-origin", headers: authHeaders(), body: form });
  if (!response.ok) {
    let message = "Не удалось отправить сообщение.";
    try {
      const problem = await response.json() as { title?: string };
      if (problem.title) message = problem.title;
    } catch { /* The status line stays the fallback. */ }
    throw new Error(message);
  }
  return response.json() as Promise<SupportThread>;
}

export function login(username: string, password: string) {
  return send<Session>("POST", "/web-api/auth/login", { username, password, deviceName: "Браузер «Расписание военмех»" });
}

export function register(username: string, password: string, displayName: string) {
  return send<Session>("POST", "/web-api/auth/register", { username, password, displayName });
}

export function logout() {
  return send<void>("POST", "/web-api/auth/logout", undefined, true);
}

function providerAddress(provider: "vk" | "yandex", target: string) {
  const url = new URL(target);
  const host = provider === "vk" ? "id.vk.ru" : "oauth.yandex.ru";
  if (url.protocol !== "https:" || url.hostname !== host || (url.port !== "" && url.port !== "443")
    || url.pathname !== "/authorize" || url.username !== "" || url.password !== "" || url.hash !== "") {
    throw new Error("bad-authorize");
  }
  return url.href;
}

export async function startExternal(provider: "vk" | "yandex") {
  const started = await send<{ authorizeUrl: string }>("POST", "/web-api/auth/external/" + provider + "/start", { purpose: "login" });
  window.location.assign(providerAddress(provider, started.authorizeUrl));
}

export function groupHomework(id: string) {
  return read<GroupHomeworkCopy[]>(`/web-api/communities/${id}/homework/copies`);
}

export function shareHomework(id: string, title: string, body: string) {
  return send<{ homeworkId: string }>("POST", `/web-api/communities/${id}/homework/share`, { title, body, expectedRevision: 0 });
}

export function completeHomework(id: string, homeworkId: string, completed: boolean, expectedRevision: number) {
  return send<{ homeworkId: string; completed: boolean; revision: number }>("PUT", `/web-api/communities/${id}/homework/${homeworkId}/completion`, { completed, expectedRevision });
}

export function communities(groupId?: string) {
  const query = groupId ? "?groupId=" + encodeURIComponent(groupId) : "";
  return read<Community[]>("/web-api/communities" + query);
}

export function joinCommunity(id: string) {
  return send("POST", `/web-api/communities/${id}/join-requests`, undefined, true);
}

export function openDirect(communityId: string, userId: string) {
  return send<Conversation>("POST", "/web-api/communities/direct", { communityId, userId });
}

export function groupHome(id: string) {
  return read<GroupHome>(`/web-api/communities/${id}/home`);
}

export function groupDesk(id: string) {
  return read<GroupDesk>(`/web-api/communities/${id}/desk`);
}

export function createGroupRole(id: string, name: string) {
  return send<GroupDesk>("POST", `/web-api/communities/${id}/roles`, { name });
}

export function renameGroupRole(id: string, roleId: string, name: string) {
  return send<GroupDesk>("POST", `/web-api/communities/${id}/roles/${roleId}`, { name });
}

export function deleteGroupRole(id: string, roleId: string) {
  return send<GroupDesk>("POST", `/web-api/communities/${id}/roles/${roleId}/delete`, undefined, true);
}

export function grantGroupRole(id: string, roleId: string, userId: string) {
  return send<GroupDesk>("POST", `/web-api/communities/${id}/roles/${roleId}/grants`, { userId });
}

export function revokeGroupRole(id: string, roleId: string, userId: string) {
  return send<GroupDesk>("POST", `/web-api/communities/${id}/roles/${roleId}/grants/${userId}/delete`, undefined, true);
}

export function removeGroupMember(id: string, userId: string) {
  return send<GroupDesk>("POST", `/web-api/communities/${id}/members/${userId}/remove`, undefined, true);
}

export function acceptJoin(id: string, requestId: string) {
  return send("POST", `/web-api/communities/${id}/join-requests/${requestId}/accept`, undefined, true);
}

export function rejectJoin(id: string, requestId: string) {
  return send("POST", `/web-api/communities/${id}/join-requests/${requestId}/reject`, undefined, true);
}

export function ballots(id: string) {
  return read<BallotBoard>(`/web-api/communities/${id}/ballots`);
}

export function openHeadmanBallot(id: string, question: string, options: string[], days: number) {
  return send<BallotBoard>("POST", `/web-api/communities/${id}/ballots/headman`, { question, options, days });
}

export function proposeBallot(id: string, question: string, options: string[], days: number) {
  return send<BallotBoard>("POST", `/web-api/communities/${id}/ballots/collective`, { question, options, days });
}

export function supportBallot(id: string, ballotId: string) {
  return send<BallotBoard>("POST", `/web-api/communities/${id}/ballots/${ballotId}/support`, undefined, true);
}

export function voteBallot(id: string, ballotId: string, optionId: string) {
  return send<BallotBoard>("POST", `/web-api/communities/${id}/ballots/${ballotId}/votes`, { optionId });
}

export function closeBallot(id: string, ballotId: string) {
  return send<BallotBoard>("POST", `/web-api/communities/${id}/ballots/${ballotId}/close`, undefined, true);
}

export function proposeChange(id: string, change: { kind: string; days: number; roleId: string; userId: string; name: string; power: string; enabled: boolean }) {
  return send<BallotBoard>("POST", `/web-api/communities/${id}/ballots/changes`, change);
}

export function setRolePower(id: string, roleId: string, power: string, enabled: boolean) {
  return send<GroupDesk>("POST", `/web-api/communities/${id}/roles/${roleId}/powers`, { power, enabled });
}

export function messages(id: string, topic?: string, cursor?: GroupMessageCursor) {
  return read<{ messages: ChatMessage[]; hasMore: boolean }>(`/web-api/communities/conversations/${id}/messages` + groupMessageQuery(topic, cursor));
}

export function sendMessage(id: string, body: string, replyTo?: string) {
  return send<ChatMessage>("POST", `/web-api/communities/conversations/${id}/messages`, replyTo ? { body, replyTo } : { body });
}

export function sendTopicMessage(id: string, body: string, topicId: string | null, replyTo?: string) {
  return send<ChatMessage>("POST", `/web-api/communities/conversations/${id}/topic-messages`, replyTo ? { body, topicId, replyTo } : { body, topicId });
}

export function editGroupMessage(id: string, messageId: string, body: string) {
  return send<ChatMessage>("POST", `/web-api/communities/conversations/${id}/messages/${messageId}/edit`, { body });
}

export function deleteGroupMessage(id: string, messageId: string) {
  return send<ChatMessage>("POST", `/web-api/communities/conversations/${id}/messages/${messageId}/delete`, undefined, true);
}

export function reactGroupMessage(id: string, messageId: string, emoji: string) {
  return send<ChatMessage>("POST", `/web-api/communities/conversations/${id}/messages/${messageId}/react`, { emoji });
}

export async function sendGroupMedia(id: string, kind: "image" | "video" | "file", name: string, file: Blob, replyTo?: string): Promise<ChatMessage> {
  const response = await postGroupMedia(id, kind, name, file, replyTo, fetch, authHeaders());
  if (!response.ok) throw new Error(String(response.status));
  return response.json() as Promise<ChatMessage>;
}

export function groupMedia(download: GroupMediaDownload): Promise<Blob> {
  return getGroupMedia(download, fetch, authHeaders());
}

export function topics(id: string) {
  return read<{ topics: GroupTopic[] }>(`/web-api/communities/${id}/topics`);
}

export function createTopic(id: string, title: string, icon: string) {
  return send<{ topics: GroupTopic[] }>("POST", `/web-api/communities/${id}/topics`, { title, icon });
}

export function renameTopic(id: string, topicId: string, title: string, icon: string) {
  return send<{ topics: GroupTopic[] }>("POST", `/web-api/communities/${id}/topics/${topicId}`, { title, icon });
}

export function deleteTopic(id: string, topicId: string) {
  return send<{ topics: GroupTopic[] }>("POST", `/web-api/communities/${id}/topics/${topicId}/delete`, undefined, true);
}

export function markRead(id: string) {
  return send("POST", `/web-api/communities/conversations/${id}/read`, undefined, true);
}

export function socialHome() {
  return read<SocialHome>("/web-api/social/home");
}

export function socialInvite(code: string) {
  return send<SocialHome>("POST", "/web-api/social/invites", { code });
}

export function socialAccept(friendshipId: string) {
  return send<SocialHome>("POST", `/web-api/social/invites/${friendshipId}/accept`, undefined, true);
}

export function socialDecline(friendshipId: string) {
  return send<SocialHome>("POST", `/web-api/social/invites/${friendshipId}/decline`, undefined, true);
}

export function socialMessages(id: string, before?: string) {
  const query = before ? "?before=" + encodeURIComponent(before) : "";
  return read<SocialPage>(`/web-api/social/conversations/${id}/messages` + query);
}

export function socialText(id: string, body: string, replyTo?: string) {
  return send<SocialMessage>("POST", `/web-api/social/conversations/${id}/messages`, replyTo ? { body, replyTo } : { body });
}

export function socialCard(id: string, body: string, replyTo?: string) {
  return send<SocialMessage>("POST", `/web-api/social/conversations/${id}/cards`, replyTo ? { body, replyTo } : { body });
}

export function socialSticker(id: string, sticker: string, replyTo?: string) {
  return send<SocialMessage>("POST", `/web-api/social/conversations/${id}/stickers`, replyTo ? { sticker, replyTo } : { sticker });
}

export function socialEdit(id: string, messageId: string, body: string) {
  return send<SocialMessage>("POST", `/web-api/social/conversations/${id}/messages/${messageId}/edit`, { body });
}

export function socialDelete(id: string, messageId: string) {
  return send<SocialMessage>("POST", `/web-api/social/conversations/${id}/messages/${messageId}/delete`, undefined, true);
}

export function socialReact(id: string, messageId: string, emoji: string) {
  return send<SocialMessage>("POST", `/web-api/social/conversations/${id}/messages/${messageId}/reaction`, { emoji });
}

export async function socialUpload(id: string, file: File, kind: "image" | "file" | "voice" | "circle", extra?: { replyTo?: string; durationMs?: number }): Promise<SocialMessage> {
  const body = new FormData();
  body.append("file", file, file.name);
  if (extra?.replyTo) body.append("replyTo", extra.replyTo);
  if (extra?.durationMs) body.append("durationMs", String(extra.durationMs));
  const path = kind === "image" ? "images" : kind === "voice" ? "voice" : kind === "circle" ? "circles" : "files";
  const response = await fetch(`/web-api/social/conversations/${id}/${path}`, {
    method: "POST",
    credentials: "same-origin",
    headers: authHeaders(),
    body
  });
  if (!response.ok) throw new Error(String(response.status));
  return response.json() as Promise<SocialMessage>;
}

export function socialAttachment(id: string) {
  return "/web-api/social/attachments/" + id;
}
