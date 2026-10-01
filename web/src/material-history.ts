import { createGroupPoller, mergeGroupMessages, type GroupMessageCursor } from "./groupChat.ts";
import type { ChatMessage } from "./types";
export type MaterialHistory = {
    messages: ChatMessage[] | null;
    hasOlder: boolean;
    loadingOlder: boolean;
    loading?: boolean;
    error?: boolean;
};
export function createMaterialHistory(load: (cursor?: GroupMessageCursor) => Promise<{
    messages: ChatMessage[];
    hasMore: boolean;
}>, publish: (state: MaterialHistory) => void, onError: (error: unknown) => void) {
    let state: MaterialHistory = { messages: null, hasOlder: false, loadingOlder: false };
    let disposed = false, epoch = 0;
    let older: Promise<void> | null = null;
    const put = (next: MaterialHistory) => { state = next; if (!disposed)
        publish(next); };
    const fail = (error: unknown) => { if (disposed)
        return; if (error instanceof Error && ["401", "403", "404"].includes(error.message)) {
        epoch++;
        poller.changed();
        put({ messages: [], hasOlder: false, loadingOlder: false });
    } put({...state,loading:false,error:true}); onError(error); };
    const poller = createGroupPoller(after => load(after ? { after } : undefined), () => state.messages ?? [], (updates, first) => put({ ...state, loading:false,error:false,messages: mergeGroupMessages(state.messages ?? [], updates.messages), hasOlder: first || !state.messages?.length ? updates.hasOlder : state.hasOlder }), fail);
    async function earlier() {
        if (disposed || !state.hasOlder || !state.messages?.length)
            return;
        if (older)
            return older;
        const before = state.messages[0].messageId;
        const ticket = epoch;
        put({ ...state, loadingOlder: true });
        const task = load({ before }).then(page => { if (disposed || ticket !== epoch)
            return; put({ ...state, messages: mergeGroupMessages(state.messages ?? [], page.messages), hasOlder: page.hasMore, loadingOlder: false }); }).catch(error => { if (!disposed && ticket === epoch)
            fail(error); }).finally(() => { if (older === task)
            older = null; if (!disposed && ticket === epoch)
            put({ ...state, loadingOlder: false }); });
        older = task;
        return task;
    }
    return { poll: async () => { if(disposed)return; put({...state,loading:true}); await poller.poll(); if(!disposed)put({...state,loading:false}); }, earlier, append(message: ChatMessage) { if (disposed)
            return; poller.changed(); put({ ...state, messages: mergeGroupMessages(state.messages ?? [], [message]) }); }, dispose() { disposed = true; epoch++; poller.dispose(); } };
}
