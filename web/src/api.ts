import { topicRules } from "./topic-policy.ts";
import { canonicalUtc } from "./utc.ts";
import { getGroupMedia, postGroupMedia, type GroupMediaDownload, type GroupMediaKind } from "./group-media.ts";
import { groupMessageQuery, type GroupMessageCursor } from "./groupChat.ts";
import { avatarPath, type AvatarKind } from "./avatar.ts";
import type { BallotBoard, ChatMessage, Community, Conversation, GroupDesk, GroupHomeworkCopy, GroupHome, GroupTopicMetadata, GroupTopicPage, GroupsPayload, HomeworkAudience, MapsManifest, Session, SocialHome, SocialMessage, SocialPage, Teacher, TeacherLesson, TimetablePayload } from "./types";

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

async function read<T>(url: string, signal?: AbortSignal, extraHeaders?: Record<string, string>): Promise<T> {
  const response = await fetch(url, { signal, credentials: "same-origin", headers: { ...authHeaders(false, url.startsWith("/web-api/communities")), ...extraHeaders } });
  if (!response.ok) throw new Error(String(response.status));
  return response.json() as Promise<T>;
}

export function loadGroups(signal?: AbortSignal) {
  return read<GroupsPayload>("/api/v1/groups", signal);
}

export function loadTimetable(groupId: string, signal?: AbortSignal) {
  return read<TimetablePayload>("/api/v1/groups/" + encodeURIComponent(groupId) + "/timetable", signal);
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
let signedInUser = "";
let authEpoch = 0;
let sessionSerial = 0;
export const authGeneration = () => authEpoch;
function authChanging() { authEpoch++; sessionSerial++; }

function authHeaders(json = false, groupSpace = false): Record<string, string> {
  const headers: Record<string, string> = { Accept: "application/json" };
  if (groupSpace) { headers["X-Zapara-Group-Space"] = "1"; headers["X-Zapara-Homework"] = "1"; }
  if (csrf) headers["X-Zapara-CSRF"] = csrf;
  if (familyId) headers["X-Zapara-Family"] = familyId;
  if (json) headers["Content-Type"] = "application/json";
  return headers;
}

function remember(value: { csrfToken?: string; familyId?: string | null; user?: { userId: string } | null }) {
  if ("user" in value) signedInUser = value.user?.userId || "";
  if ("csrfToken" in value) csrf = value.csrfToken || "";
  if ("familyId" in value) familyId = value.familyId || "";
}

export async function session(): Promise<Session> {
  const generation = authEpoch;
  const request = ++sessionSerial;
  const value = await read<Session>("/web-api/session");
  if (generation !== authEpoch || request !== sessionSerial) throw new Error("stale-session");
  remember(value);
  return value;
}

async function send<T>(method: string, url: string, body?: unknown, empty = false): Promise<T> {
  const headers = authHeaders(!empty && body !== undefined, url.startsWith("/web-api/communities"));
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
  authChanging();
  return send<Session>("POST", "/web-api/auth/login", { username, password, deviceName: "Браузер «Расписание военмех»" });
}

export function register(username: string, password: string, displayName: string) {
  authChanging();
  return send<Session>("POST", "/web-api/auth/register", { username, password, displayName });
}

export function logout() {
  authChanging();
  return send<void>("POST", "/web-api/auth/logout", undefined, true);
}

export async function avatarImage(kind: AvatarKind, id: string, etag: string | null = null): Promise<{ url: string | null; etag: string | null; notModified?: boolean; missing?: boolean }> {
  const response = await fetch(avatarPath(kind, id), {
    credentials: "same-origin", cache: "no-store",
    headers: { ...authHeaders(false, kind === "group"), Accept: "image/webp", ...(etag ? { "If-None-Match": etag } : {}) },
  });
  if (response.status === 304) return { url: null, etag, notModified: true };
  // 404 — аватара нет или он недоступен; кэш запоминает это и не повторяет запрос при каждом обновлении (#34).
  if (response.status === 404) return { url: null, etag: null, missing: true };
  if (response.status === 401 || response.status === 403) return { url: null, etag: null };
  if (!response.ok || !response.headers.get("Content-Type")?.toLowerCase().startsWith("image/webp")) throw new Error(String(response.status));
  const limit = 512 * 1024;
  if (Number(response.headers.get("Content-Length")) > limit) {
    void response.body?.cancel().catch(() => undefined);
    throw new Error("invalid-avatar-image");
  }
  const reader = response.body?.getReader();
  if (!reader) throw new Error("invalid-avatar-image");
  const chunks: ArrayBuffer[] = [];
  let size = 0;
  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    size += value.byteLength;
    if (size > limit) { void reader.cancel().catch(() => undefined); throw new Error("invalid-avatar-image"); }
    const copy = new Uint8Array(value.byteLength);
    copy.set(value);
    chunks.push(copy.buffer);
  }
  if (size === 0) throw new Error("invalid-avatar-image");
  const image = new Blob(chunks, { type: "image/webp" });
  return { url: URL.createObjectURL(image), etag: response.headers.get("ETag") };
}

export type AvatarMutationScope = { userId: string; familyId: string };

function avatarMutationHeaders(expected: AvatarMutationScope, group: boolean): Record<string, string> {
  if (!expected.userId || !expected.familyId || signedInUser !== expected.userId || familyId !== expected.familyId) throw new Error("avatar-scope-changed");
  return { ...authHeaders(false, group), "X-Zapara-Family": expected.familyId };
}

export async function saveAvatar(kind: AvatarKind, id: string, file: File, expected: AvatarMutationScope): Promise<{ revision: string }> {
  if (kind === "user" && id !== expected.userId) throw new Error("avatar-scope-changed");
  const form = new FormData();
  form.append("file", file, file.name);
  const path = kind === "user" ? "/web-api/social/avatars/me" : avatarPath("group", id);
  const response = await fetch(path, { method: "PUT", credentials: "same-origin", headers: avatarMutationHeaders(expected, kind === "group"), body: form });
  if (!response.ok) throw new Error(String(response.status));
  return response.json() as Promise<{ revision: string }>;
}

export async function deleteAvatar(kind: AvatarKind, id: string, expected: AvatarMutationScope): Promise<void> {
  if (kind === "user" && id !== expected.userId) throw new Error("avatar-scope-changed");
  const path = kind === "user" ? "/web-api/social/avatars/me" : avatarPath("group", id);
  const response = await fetch(path, { method: "DELETE", credentials: "same-origin", headers: avatarMutationHeaders(expected, kind === "group") });
  if (!response.ok) throw new Error(String(response.status));
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
  authChanging();
  const started = await send<{ authorizeUrl: string }>("POST", "/web-api/auth/external/" + provider + "/start", { purpose: "login" });
  window.location.assign(providerAddress(provider, started.authorizeUrl));
}

export function groupHomework(id: string, topicId?: string) {
  return read<GroupHomeworkCopy[]>(`/web-api/communities/${id}/homework/copies` + (topicId ? `?topicId=${encodeURIComponent(topicId)}` : ""));
}

export type HomeworkPublication = { title: string; body: string; deadlineAt?: string | null; topicId?: string | null; audience?: HomeworkAudience; operationId?: string };
export function shareHomework(id: string, title: string, body: string, deadlineAt: string | null = null, topicId: string | null = null, audience?: HomeworkAudience, operationId?: string) {
  return send<{ homeworkId: string }>("POST", `/web-api/communities/${id}/homework/share`, { title, body, expectedRevision: 0, ...(deadlineAt ? { deadlineAt: canonicalUtc(deadlineAt) } : {}), ...(topicId ? { topicId } : {}), ...(audience?.kind === "selected" ? { audience } : {}), ...(operationId ? { operationId } : {}) });
}

export function editHomework(id: string, homeworkId: string, update: HomeworkPublication, expectedRevision: number) {
  return send<{ homeworkId: string; revision: number }>("PUT", `/web-api/communities/${id}/homework/${homeworkId}`, { ...update, expectedRevision });
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

export function ballots(id: string, topicId?: string) {
  return read<BallotBoard>(`/web-api/communities/${id}/ballots` + (topicId ? `?topic=${encodeURIComponent(topicId)}` : ""));
}

export function openHeadmanBallot(id: string, question: string, options: string[], days: number, topicId?: string) {
  return send<BallotBoard>("POST", `/web-api/communities/${id}/ballots/headman`, { question, options, days, ...(topicId ? { topicId } : {}) });
}

export function proposeBallot(id: string, question: string, options: string[], days: number, topicId?: string) {
  return send<BallotBoard>("POST", `/web-api/communities/${id}/ballots/collective`, { question, options, days, ...(topicId ? { topicId } : {}) });
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
  return read<{ messages: ChatMessage[]; hasMore: boolean }>(`/web-api/communities/conversations/${id}/messages` + groupMessageQuery(topic, cursor), undefined, { "X-Zapara-Read-Cursor": "1" });
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

export async function sendGroupMedia(id: string, kind: GroupMediaKind, name: string, file: Blob, replyTo?: string, durationMs?: number, topicId?: string): Promise<ChatMessage> {
  const response = await postGroupMedia(id, kind, name, file, replyTo, fetch, authHeaders(false, true), durationMs, topicId);
  if (!response.ok) throw new Error(String(response.status));
  return response.json() as Promise<ChatMessage>;
}

export function groupMedia(download: GroupMediaDownload): Promise<Blob> {
  return getGroupMedia(download, fetch, authHeaders(false, true));
}

export function topics(id: string) {
  return read<GroupTopicPage>(`/web-api/communities/${id}/topics?typed=1`);
}

export function createTopic(id: string, title: string, icon: string, kind: string, metadata?: GroupTopicMetadata & { initialAccessRules?: AccessRule[] | null; template?: string; categoryId?: string | null; position?: number; subject?: string | null; expectedRevision?: number }) {
  const {initialAccessRules,...settings}=metadata ?? {};
  const initial=topicRules(initialAccessRules ?? []).filter(rule=>rule.state!=="inherit");
  return send<GroupTopicPage>("POST", `/web-api/communities/${id}/topics?typed=1`, { title, icon, kind, ...settings, ...(initial.length?{initialAccessRules:initial}:{}) });
}

export function renameTopic(id: string, topicId: string, title: string, icon: string, kind: string, metadata?: GroupTopicMetadata & { template?: string | null; categoryId?: string | null; position?: number; subject?: string | null; expectedRevision?: number }) {
  const settings={...metadata};
  delete (settings as {initialAccessRules?: unknown}).initialAccessRules;
  return send<GroupTopicPage>("POST", `/web-api/communities/${id}/topics/${topicId}?typed=1`, { title, icon, kind, ...settings });
}

export function deleteTopic(id: string, topicId: string) {
  return send<GroupTopicPage>("POST", `/web-api/communities/${id}/topics/${topicId}/delete?typed=1`, undefined, true);
}

export function uploadHomeworkFile(body: FormData) {
  return fetch("/web-api/files", { method: "POST", credentials: "same-origin", headers: authHeaders(), body });
}

export function markRead(id: string, throughMessageId?: string) {
  return throughMessageId
    ? send("POST", `/web-api/communities/conversations/${id}/read`, { throughMessageId })
    : send("POST", `/web-api/communities/conversations/${id}/read`, undefined, true);
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
import type { AccessRule, GroupAuditEvent, GroupForm, GroupSpace, TopicAccess, FormQuestion, FormAnswer, FormResponse } from "./types";
const spacePath = (id: string) => `/web-api/communities/${id}/space`;
export const groupSpace = (id: string) => read<GroupSpace>(spacePath(id));
export const archivedTopics = (id: string) => read<GroupTopicPage>(`${spacePath(id)}/archive`);
export const saveCategory = (id: string, categoryId: string | null, title: string, position: number, expectedRevision = 0) => send<GroupSpace>("POST", `${spacePath(id)}/categories`, { categoryId, title, position, expectedRevision });
export const deleteCategory = (id: string, categoryId: string) => send<GroupSpace>("POST", `${spacePath(id)}/categories/${categoryId}/delete`, {});
export const archiveTopic = (id: string, topicId: string, archived: boolean, expectedRevision: number) => send<GroupSpace>("POST", `${spacePath(id)}/topics/${topicId}/archive`, { archived, expectedRevision });
export const topicAccess = (id: string, topicId: string) => read<TopicAccess>(`${spacePath(id)}/topics/${topicId}/access`);
export const saveTopicAccess = (id: string, topicId: string, rules: AccessRule[], expectedRevision: number) => send<GroupSpace>("POST", `${spacePath(id)}/topics/${topicId}/access`, { rules:topicRules(rules), expectedRevision });
export const previewPermissions = (id: string, target: { userId: string | null; roleId: string | null }) => send<{ topics: import("./types").GroupTopic[] }>("POST", `${spacePath(id)}/preview`, target);
export const groupAudit = (id: string) => read<{ events: GroupAuditEvent[] }>(`${spacePath(id)}/audit`);
export const saveRoleSettings = (id: string, roleId: string, name: string, icon: string, position: number, expectedRevision: number) => send<GroupDesk>("POST", `${spacePath(id)}/roles/${roleId}`, { name, icon, position, expectedRevision });
export const roleImpact = (id: string, roleId: string) => read<{ roleId: string; assignments: number; accessRules: number }>(`${spacePath(id)}/roles/${roleId}/impact`);
export const groupForms = (id: string, topicId: string) => read<{ forms: GroupForm[] }>(`${spacePath(id)}/topics/${topicId}/forms`);
export const createGroupForm = (id: string, topicId: string, request: { title: string; description: string; deadlineAt: string | null; anonymous: boolean; questions: FormQuestion[] }) => send<{ forms: GroupForm[] }>("POST", `${spacePath(id)}/topics/${topicId}/forms`, request);
export const submitGroupForm = (id: string, formId: string, answers: FormAnswer[]) => send<GroupForm>("POST", `${spacePath(id)}/forms/${formId}/response`, { answers });
export const groupFormResponses = (id: string, formId: string, after?: string) => read<{ formId: string; responses: FormResponse[]; nextCursor: string | null; totalResponses: number }>(`${spacePath(id)}/forms/${formId}/responses` + (after ? `?after=${encodeURIComponent(after)}` : ""));
export type AccountDevice = { familyId: string; deviceId: string; deviceName: string; platform: string; lastSeenAt: string; expiresAt: string; isCurrent: boolean };
export const accountMe = () => read<{ user: import("./types").SessionUser; familyId: string; authenticationMethods: string[] }>("/web-api/account/me");
export const updateDisplayName = (displayName: string) => send<import("./types").SessionUser>("PATCH", "/web-api/account/me", { displayName });
export const changeAccountPassword = (currentPassword: string, newPassword: string) => send<void>("POST", "/web-api/account/password/change", { currentPassword, newPassword });
export const accountDevices = (cursor?: string) => read<{ devices: AccountDevice[]; nextCursor: string | null }>("/web-api/account/devices" + (cursor ? `?cursor=${encodeURIComponent(cursor)}` : ""));
export const revokeDevice = (familyId: string) => send<void>("DELETE", `/web-api/account/devices/${familyId}`, undefined, true);
export const revokeAllDevices = () => send<void>("POST", "/web-api/account/sessions/revoke-all", undefined, true);
export const requestPasswordReset = (username: string) => send<void>("POST", "/web-api/auth/password-reset/request", { username });
export const confirmPasswordReset = (token: string, newPassword: string) => send<void>("POST", "/web-api/auth/password-reset/confirm", { token, newPassword });
export const syncMetadata = () => read<{ syncEpoch: string; currentSequence: number; minAfterSequence: number }>("/web-api/sync/metadata");
import type { SyncRecord, SyncHomeworkValue, SyncSettingsValue } from "./private-sync";
export const beginSyncSnapshot = () => send<{ manifestId: string; syncEpoch: string; highWater: number }>("POST", "/web-api/sync/resync", undefined, true);
export const syncSnapshotPage = (manifestId: string, afterOrdinal: number) => read<{ nextAfterOrdinal: number; hasMore: boolean; items: { ordinal: number; record: SyncRecord }[] }>(`/web-api/sync/resync/${manifestId}?afterOrdinal=${afterOrdinal}&limit=200`);
export async function mutatePrivate(epoch: string, opId: string, type: string, id: string, revision: number, value: SyncHomeworkValue | SyncSettingsValue | { done: boolean; doneAtUtc: string | null } | null, expectedUserId?: string, action: "upsert" | "delete" = "upsert") {
  if (expectedUserId && signedInUser !== expectedUserId) throw new Error("stale-owner");
  const response = await fetch("/web-api/sync/mutations", { method: "POST", credentials: "same-origin", headers: authHeaders(true), body: JSON.stringify({ syncEpoch: epoch, opId, entityType: type, entityId: id, expectedRevision: revision, action, value }) });
  if (response.status !== 409 && !response.ok) throw new Error(String(response.status));
  return response.json() as Promise<{ status: number; code: string; serverRecord: SyncRecord | null }>;
}
export const privateChanges = (epoch: string, afterSequence: number) => read<{ metadata: { syncEpoch: string; currentSequence: number }; nextAfterSequence: number; hasMore: boolean; changes: { record: SyncRecord }[] }>(`/web-api/sync/changes?epoch=${encodeURIComponent(epoch)}&afterSequence=${afterSequence}&limit=200`);
export const pushCapabilities = () => read<{ available: boolean; publicKey: string | null; reason: string | null }>("/web-api/notifications/capabilities");
export const pushSubscriptions = () => read<{ subscriptionId: string; enabled: boolean; times: (string | null)[] }[]>("/web-api/notifications/subscriptions");
export const savePushSubscription = (request: { endpoint: string; keys: { p256dh: string; auth: string }; enabled: boolean; timeZone: string }) => send<{ subscriptionId: string; enabled: boolean }>("POST", "/web-api/notifications/subscriptions", request);
export const deletePushSubscription = (id: string) => send<void>("DELETE", `/web-api/notifications/subscriptions/${id}`, undefined, true);
export const testPushSubscription = (subscriptionId: string) => send<{ status: string }>("POST", "/web-api/notifications/test", { subscriptionId });
export type AccessPreview = { topicId: string; revision: number; affectedCount: number; beforeReaders: string[]; afterReaders: string[]; participants: { userId: string; beforePermissions: string[]; afterPermissions: string[]; sources: Record<string,string> }[] };
export const previewTopicAccess = (id: string, topicId: string, rules: AccessRule[], expectedRevision: number) => send<AccessPreview>("POST", `${spacePath(id)}/topics/${topicId}/access-preview`, { rules:topicRules(rules), expectedRevision });
