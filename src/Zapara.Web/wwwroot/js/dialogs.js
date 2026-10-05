const dialogs = new Map();
let pageOverflow, pageFocus;
const selector = 'button:not([disabled]),a[href],input:not([disabled]),select:not([disabled]),textarea:not([disabled]),[tabindex="0"]';
export function open(element) {
    if (dialogs.has(element)) return;
    if (!dialogs.size) { pageOverflow = document.body.style.overflow; pageFocus = document.activeElement; }
    const previous = document.activeElement;
    const handler = event => {
        if (event.key !== 'Tab') return;
        const items = [...element.querySelectorAll(selector)].filter(x => x.getClientRects().length);
        if (!items.length) { event.preventDefault(); element.focus(); return; }
        const first = items[0], last = items[items.length - 1];
        if (event.shiftKey && (document.activeElement === first || document.activeElement === element)) { event.preventDefault(); last.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    };
    element.addEventListener('keydown', handler);
    dialogs.set(element, { previous, handler });
    document.body.style.overflow = 'hidden';
    (element.querySelector('[autofocus]') || element.querySelector(selector) || element).focus({ preventScroll: true });
}
export function close(element) {
    const state = dialogs.get(element);
    if (!state) return;
    element.removeEventListener('keydown', state.handler);
    dialogs.delete(element);
    const top = [...dialogs.keys()].at(-1);
    if (top) {
        document.body.style.overflow = 'hidden';
        if (!top.contains(document.activeElement)) {
            const target = state.previous?.isConnected && top.contains(state.previous) ? state.previous : top.querySelector(selector) || top;
            target.focus({ preventScroll: true });
        }
    } else {
        document.body.style.overflow = pageOverflow ?? '';
        if (pageFocus?.isConnected) pageFocus.focus({ preventScroll: true });
        pageOverflow = pageFocus = undefined;
    }
}
