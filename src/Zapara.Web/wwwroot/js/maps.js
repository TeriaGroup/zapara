function observe(promise) {
    promise.catch(error => {
        const text = String(error && (error.message || error) || '');
        if (/disposed|disconnect|circuit/i.test(text)) return;
        throw error;
    });
}
const views = new WeakMap();
const clamp = (v, min, max) => Math.max(min, Math.min(max, v));

export function attach(element, receiver) {
    dispose(element);
    const plane = element.querySelector('.map-plane');
    const image = element.querySelector('.map-image');
    const abort = new AbortController();
    const options = { signal: abort.signal };
    const state = { element, plane, image, receiver, abort, scale: 1, fit: 1, x: 0, y: 0, points: new Map(), moved: false, frame: 0, lastSent: 1 };
    views.set(element, state);
    const draw = () => {
        state.frame = 0;
        const scale = state.fit * state.scale;
        const width = image.naturalWidth * scale, height = image.naturalHeight * scale;
        state.x = clamp(state.x, Math.min(0, element.clientWidth - width) - 80, Math.max(0, element.clientWidth - width) + 80);
        state.y = clamp(state.y, Math.min(0, element.clientHeight - height) - 80, Math.max(0, element.clientHeight - height) + 80);
        plane.style.transform = `translate(${state.x}px,${state.y}px) scale(${scale})`;
        plane.style.setProperty('--map-scale', String(Math.max(.01, scale)));
    };
    state.draw = () => { if (!state.frame) state.frame = requestAnimationFrame(draw); };
    state.notify = () => {
        if (Math.abs(state.lastSent - state.scale) < .001) return;
        state.lastSent = state.scale;
        observe(receiver.invokeMethodAsync('ZoomChanged', state.scale));
    };
    state.reset = () => {
        if (!image.naturalWidth || !element.clientWidth || !element.clientHeight) return;
        plane.style.width = image.naturalWidth + 'px'; plane.style.height = image.naturalHeight + 'px';
        state.fit = Math.min(element.clientWidth / image.naturalWidth, element.clientHeight / image.naturalHeight) * .96;
        state.scale = 1;
        state.x = (element.clientWidth - image.naturalWidth * state.fit) / 2;
        state.y = (element.clientHeight - image.naturalHeight * state.fit) / 2;
        state.draw(); state.notify();
    };
    state.zoom = (factor, x = element.clientWidth / 2, y = element.clientHeight / 2) => {
        const next = clamp(state.scale * factor, .4, 4), ratio = next / state.scale;
        state.x = x - (x - state.x) * ratio; state.y = y - (y - state.y) * ratio;
        state.scale = next; state.draw();
    };
    const local = event => { const rect = element.getBoundingClientRect(); return { x: event.clientX - rect.left, y: event.clientY - rect.top }; };
    const pair = points => { const p = [...points.values()]; return { x: (p[0].x + p[1].x) / 2, y: (p[0].y + p[1].y) / 2, distance: Math.hypot(p[0].x - p[1].x, p[0].y - p[1].y) }; };
    element.addEventListener('pointerdown', event => {
        if (event.button !== 0 && event.pointerType === 'mouse') return;
        state.points.set(event.pointerId, local(event)); state.moved = false;
        // Keep clicks on room buttons intact; capture only after a drag actually starts.
        state.start = local(event);
    }, options);
    element.addEventListener('pointermove', event => {
        if (!state.points.has(event.pointerId)) return;
        const before = state.points.size > 1 ? pair(state.points) : null;
        const previous = state.points.get(event.pointerId), current = local(event);
        state.points.set(event.pointerId, current);
        if (!state.moved && Math.hypot(current.x - state.start.x, current.y - state.start.y) < 4 && !before) return;
        state.moved = true;
        element.setPointerCapture(event.pointerId);
        if (before) {
            const after = pair(state.points);
            if (before.distance > 1) state.zoom(after.distance / before.distance, before.x, before.y);
            state.x += after.x - before.x; state.y += after.y - before.y;
        } else { state.x += current.x - previous.x; state.y += current.y - previous.y; }
        state.draw();
    }, options);
    const end = event => { state.points.delete(event.pointerId); if (element.hasPointerCapture(event.pointerId)) element.releasePointerCapture(event.pointerId); state.notify(); };
    element.addEventListener('pointerup', end, options);
    element.addEventListener('pointercancel', end, options);
    window.addEventListener('pointerup', end, options);
    window.addEventListener('pointercancel', end, options);
    element.addEventListener('click', event => { if (state.moved) { event.preventDefault(); event.stopImmediatePropagation(); state.moved = false; } }, { ...options, capture: true });
    element.addEventListener('wheel', event => {
        event.preventDefault(); const p = local(event); state.zoom(Math.exp(-event.deltaY * .0015), p.x, p.y); state.notify();
    }, { ...options, passive: false });
    element.addEventListener('keydown', event => {
        if (event.target !== element) return;
        if (event.key === '+' || event.key === '=') state.zoom(1.25);
        else if (event.key === '-') state.zoom(.8);
        else if (event.key === '0' || event.key === 'Home') state.reset();
        else if (event.key === 'ArrowLeft') state.x += 30;
        else if (event.key === 'ArrowRight') state.x -= 30;
        else if (event.key === 'ArrowUp') state.y += 30;
        else if (event.key === 'ArrowDown') state.y -= 30;
        else return;
        event.preventDefault(); state.draw(); state.notify();
    }, options);
    image.addEventListener('load', state.reset, options);
    state.observer = new ResizeObserver(state.reset); state.observer.observe(element);
    state.reset();
}

export function zoom(element, factor) { const s = views.get(element); if (s) { s.zoom(factor); s.notify(); } }
export function reset(element) { views.get(element)?.reset(); }
export function retry(element) { const image = element.querySelector('.map-image'); const src = image.getAttribute('src'); image.removeAttribute('src'); requestAnimationFrame(() => image.setAttribute('src', src)); }

export async function fullscreen(viewer, element) {
    const state = views.get(element); if (!state) return;
    if (document.fullscreenElement) { await document.exitFullscreen(); return; }
    if (viewer.classList.contains('map-fullscreen')) { state.closeFullscreen?.(); return; }
    if (viewer.requestFullscreen && document.fullscreenEnabled) {
        try { await viewer.requestFullscreen(); return; } catch { /* iOS and embedded browsers use the local overlay below. */ }
    }
    const previous = document.body.style.overflow;
    viewer.classList.add('map-fullscreen'); document.body.style.overflow = 'hidden';
    const close = event => { if (!event || event.key === 'Escape') state.closeFullscreen?.(); };
    state.closeFullscreen = () => { viewer.classList.remove('map-fullscreen'); document.body.style.overflow = previous; document.removeEventListener('keydown', close); state.closeFullscreen = null; state.reset(); };
    document.addEventListener('keydown', close); state.reset();
}

export async function leaveFullscreen(element) {
    if (document.fullscreenElement?.contains(element)) await document.exitFullscreen();
    views.get(element)?.closeFullscreen?.();
}

export function dispose(element) {
    const state = views.get(element); if (!state) return;
    state.closeFullscreen?.(); state.abort.abort(); state.observer.disconnect(); cancelAnimationFrame(state.frame); views.delete(element);
}
