import type { GroupSpace, GroupTopic, GroupTopicPage } from "./types";
export type TopicAuthority = {
    space: GroupSpace;
    archived: GroupTopic[];
    legacy: boolean;
    all: GroupTopic[];
};
export function createTopicAuthority(loadSpace: () => Promise<GroupSpace>, loadArchive: () => Promise<GroupTopicPage>, loadLegacy: () => Promise<GroupSpace>, publish: (value: TopicAuthority) => void, onError: (error: unknown) => void, onDenied?: (error: unknown) => void) {
    let epoch = 0, disposed = false, published = 0;
    let current: TopicAuthority | null = null;
    async function refresh() { const ticket = ++epoch; try {
        let space: GroupSpace, archived: GroupTopic[] = [], legacy = false;
        try {
            space = await loadSpace();
        }
        catch (error) {
            if (!(error instanceof Error) || !["404", "405", "501"].includes(error.message))
                throw error;
            space = await loadLegacy();
            legacy = true;
        }
        if (disposed || ticket !== epoch)
            return;
        if (!legacy)
            archived = (await loadArchive()).topics;
        if (disposed || ticket !== epoch)
            return;
        const all = [...space.topics, ...archived];
        current = { space, archived, legacy, all };
        published = ticket;
        publish(current);
    }
    catch (error) {
        if (!disposed && ticket === epoch && error instanceof Error && ["401", "403", "404"].includes(error.message) && ticket >= published)
            onDenied?.(error);
        if (!disposed && ticket === epoch)
            onError(error);
    } }
    return { refresh, current: () => current, ticket: () => epoch, valid: (ticket: number) => !disposed && ticket === epoch, invalidate() { epoch++; }, dispose() { disposed = true; epoch++; } };
}
export async function selectedTopicAuthority(active: () => Promise<GroupTopicPage>, archived: () => Promise<GroupTopicPage>, topicId: string | null) { const page = await active(); let archive: GroupTopicPage; try {
    archive = await archived();
}
catch (error) {
    if (error instanceof Error && error.message === "404" && page.topics.every(topic => !topic.permissions))
        archive = { topics: [], canManageChannels: false };
    else
        throw error;
} const all = [...page.topics, ...archive.topics]; return { page, all, selected: all.find(topic => topic.topicId === topicId) ?? null }; }
