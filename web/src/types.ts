export type Period = { start: string; weekCount: number; title: string; timeZone: string };
export type Group = { id: string; name: string; lessonCount: number };
export type Lesson = {
  dayOfWeek: number; parity: number; index: number;
  timeStart: string; timeEnd: string; subjectRaw: string; subjectNormalized: string;
  typeRaw: string | null; teacherRaw: string | null; classroomRaw: string | null;
  roomRaw: string | null; buildingRaw: string | null;
};
export type SnapshotMeta = { snapshotId: string; fetchedAt: string; publishedAt: string; stale: boolean; sourceKind: string };
export type GroupsPayload = { period: Period; meta: SnapshotMeta; groups: Group[] };
export type TimetablePayload = GroupsPayload & { group: Group; lessons: Lesson[] };

export type HomeworkFile = { id: string; kind: "photo" | "document"; name: string; mime: string };
export type HomeworkItem = { id: string; subject: string; text: string; done: boolean; created: string; files?: HomeworkFile[] };
export type GroupHomeworkCopy = { homeworkId: string; title: string; body: string; revision: number; completed: boolean; completionRevision: number };
export type FriendItem = { id: string; groupName: string; members: string; enabled: boolean; color: string };

export type SessionUser = { userId: string; username: string; displayName: string | null };
export type Session = {
  authenticated: boolean;
  user: SessionUser | null;
  familyId?: string | null;
  csrfToken: string;
  capabilities: { password?: boolean; vk?: boolean; yandex?: boolean; registration: boolean; recovery?: boolean };
};

export type Community = { communityId: string; name: string; description: string; revision: number; role: string | null };
export type Classmate = { userId: string; username: string; displayName: string | null; role: string; self: boolean };
export type GroupRole = { roleId: string; name: string };
export type GroupGrant = { roleId: string; userId: string };
export type GroupApplicant = { requestId: string; userId: string; username: string; displayName: string | null };
export type GroupPower = { roleId: string; power: string };
export type GroupDesk = { headman: boolean; roles: GroupRole[]; grants: GroupGrant[]; applicants: GroupApplicant[]; powers: GroupPower[]; mine: string[] };
export type BallotOption = { optionId: string; label: string; votes: number; chosen: boolean };
export type Ballot = {
  ballotId: string; question: string; origin: string; status: string; deadlineAt: string;
  supporters: number; supportersNeeded: number; supported: boolean; options: BallotOption[];
  effect: string; outcome: string;
};
export type BallotBoard = { headman: boolean; canOpen: boolean; canClose: boolean; members: number; supportersNeeded: number; ballots: Ballot[] };
export type Conversation = {
  conversationId: string; kind: string; communityId: string; title: string;
  peerUserId: string | null; lastBody: string | null; lastAt: string | null; unread: number;
};
export type GroupHome = {
  communityId: string; name: string; groupName: string | null;
  groupChat: Conversation; classmates: Classmate[]; directs: Conversation[];
};
export type ChannelAccent = "default" | "blue" | "green" | "purple" | "orange" | "red";
export type ChannelWritePolicy = "all" | "managers";
export type GroupTopicMetadata = {
  description: string; accent: ChannelAccent; pinned: boolean; writePolicy: ChannelWritePolicy;
};
export type GroupTopic = GroupTopicMetadata & {
  topicId: string | null; title: string; icon: string; kind: "chat" | "ballots";
  lastBody: string | null; lastAuthor: string | null; lastAt: string | null;
  unread: number; canDelete: boolean; activeBallots: number; canPost: boolean;
};
export type GroupTopicPage = { topics: GroupTopic[]; canManageChannels: boolean };
export type ChatMessage = {
  messageId: string; conversationId: string; senderId: string; senderName: string; body: string; createdAt: string;
  kind?: string; deleted?: boolean; replyTo?: string | null;
  reactions?: { emoji: string; count: number; mine: boolean }[];
};

export type MapPlan = { id: string; building: string; floor: number; url: string };
export type MapsManifest = { version: string; maps: MapPlan[] };

export type SocialInvite = { friendshipId: string; username: string; displayName: string | null; createdAt: string };
export type SocialFriend = {
  userId: string; username: string; displayName: string | null; conversationId: string;
  lastBody: string | null; lastAt: string | null; unread: number;
};
export type SocialHome = { code: string; friends: SocialFriend[]; incoming: SocialInvite[]; outgoing: SocialInvite[] };
export type SocialReaction = { emoji: string; count: number; mine: boolean };
export type SocialMessage = {
  messageId: string; senderId: string; senderName: string; kind: string; body: string | null;
  attachmentId: string | null; fileName: string | null; contentType: string | null; bytes: number | null; createdAt: string;
  replyTo: string | null; replyBody: string | null; editedAt: string | null; deleted: boolean; read: boolean;
  durationMs: number | null; reactions: SocialReaction[];
};
export type SocialPage = { messages: SocialMessage[]; hasMore: boolean };

export type Teacher = { id: string; name: string; kafedra: string; shortName: string };
export type TeacherGroup = { idGroup?: string; number?: string };
export type TeacherLesson = {
  dayOfWeek: number; timeStart: string; timeEnd?: string; disciplineRaw?: string;
  classroomRaw?: string; roomRaw?: string; buildingRaw?: string; typeRaw?: string;
  parity: number; subjectRaw?: string; groups?: TeacherGroup[];
};
