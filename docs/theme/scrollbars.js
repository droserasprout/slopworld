/* Owns the book's scrollbar rails, leaving scrolling with the browser.
   DOM rails are needed because native scrollbars can ignore custom cursors. */
(() => {
    const root = document.documentElement;
    const scrollingElement = document.scrollingElement;
    if (!scrollingElement || !window.ResizeObserver || !window.PointerEvent) return;

    const bars = [];
    const owners = new WeakSet();
    let pending = false;
    let serial = 0;
    let dragging = null;

    function schedule() {
        if (pending) return;
        pending = true;
        requestAnimationFrame(() => {
            pending = false;
            discover();
            bars.forEach(update);
        });
    }

    const sizes = new ResizeObserver(schedule);

    function metrics(bar) {
        const viewport = bar.page
            ? (bar.horizontal ? root.clientWidth : root.clientHeight)
            : (bar.horizontal ? bar.owner.clientWidth : bar.owner.clientHeight);
        const total = bar.horizontal ? bar.owner.scrollWidth : bar.owner.scrollHeight;
        const offset = bar.horizontal ? bar.owner.scrollLeft : bar.owner.scrollTop;
        return { viewport, total, offset, maximum: Math.max(0, total - viewport) };
    }

    function scrollTo(bar, value) {
        const { maximum } = metrics(bar);
        const offset = Math.max(0, Math.min(maximum, value));
        if (bar.horizontal) bar.owner.scrollLeft = offset;
        else bar.owner.scrollTop = offset;
        schedule();
    }

    function update(bar) {
        if (!bar.owner.isConnected) {
            bar.rail.remove();
            return;
        }
        const rect = bar.page
            ? { top: 0, left: 0, right: root.clientWidth, bottom: root.clientHeight }
            : bar.owner.getBoundingClientRect();
        const { viewport, total, offset, maximum } = metrics(bar);
        const sidebarHidden = bar.owner.closest('.sidebar') && !root.classList.contains('sidebar-visible');
        const start = Math.max(0, bar.horizontal ? rect.left : rect.top) + 5;
        const end = Math.min(bar.horizontal ? root.clientWidth : root.clientHeight,
            bar.horizontal ? rect.right : rect.bottom) - 5;
        const cross = (bar.horizontal ? rect.bottom : rect.right) - 16;
        const visible = maximum > 1 && end > start && !sidebarHidden &&
            rect.bottom > 0 && rect.right > 0 && rect.top < root.clientHeight &&
            rect.left < root.clientWidth && (bar.page || bar.owner.getClientRects().length > 0);
        bar.rail.hidden = !visible;
        if (!visible) return;

        const length = end - start;
        bar.thumbLength = Math.min(length, Math.max(10, length * viewport / total));
        bar.travel = length - bar.thumbLength;
        const position = maximum ? bar.travel * offset / maximum : 0;
        Object.assign(bar.rail.style, bar.horizontal
            ? { left: `${start}px`, top: `${cross}px`, width: `${length}px` }
            : { top: `${start}px`, left: `${cross}px`, height: `${length}px` });
        Object.assign(bar.thumb.style, bar.horizontal
            ? { width: `${bar.thumbLength}px`, left: `${position}px` }
            : { height: `${bar.thumbLength}px`, top: `${position}px` });
        bar.rail.setAttribute('aria-valuemax', Math.round(maximum));
        bar.rail.setAttribute('aria-valuenow', Math.round(offset));
    }

    function add(owner) {
        if (owners.has(owner)) return;
        owners.add(owner);
        if (!owner.id) owner.id = `game-scroll-owner-${++serial}`;
        const page = owner === scrollingElement;
        for (const horizontal of page ? [false] : [false, true]) {
            const rail = document.createElement('div');
            rail.className = `game-scrollbar ${horizontal ? 'horizontal' : 'vertical'}`;
            rail.hidden = true;
            rail.tabIndex = 0;
            rail.setAttribute('role', 'scrollbar');
            rail.setAttribute('aria-controls', owner.id);
            rail.setAttribute('aria-orientation', horizontal ? 'horizontal' : 'vertical');
            rail.setAttribute('aria-label', page ? 'Page scroll' :
                owner.matches('.sidebar-scrollbox') ? 'Table of contents scroll' : 'Content scroll');
            rail.setAttribute('aria-valuemin', '0');
            const thumb = rail.appendChild(document.createElement('div'));
            thumb.className = 'game-scrollbar-thumb';
            const bar = { owner, page, horizontal, rail, thumb, travel: 0, thumbLength: 0 };
            bars.push(bar);

            rail.addEventListener('pointerdown', event => {
                if (event.button !== 0 || dragging) return;
                event.preventDefault();
                update(bar);
                const coordinate = horizontal ? event.clientX : event.clientY;
                const bounds = rail.getBoundingClientRect();
                if (event.target !== thumb) {
                    const at = coordinate - (horizontal ? bounds.left : bounds.top) - bar.thumbLength / 2;
                    scrollTo(bar, bar.travel > 0 ? at / bar.travel * metrics(bar).maximum : 0);
                }
                dragging = { bar, coordinate, offset: metrics(bar).offset, pointer: event.pointerId };
                rail.setPointerCapture(event.pointerId);
                rail.classList.add('held');
                root.classList.add('game-scrollbar-dragging');
            });
            rail.addEventListener('pointermove', event => {
                if (dragging?.bar !== bar || dragging.pointer !== event.pointerId) return;
                const delta = (horizontal ? event.clientX : event.clientY) - dragging.coordinate;
                scrollTo(bar, dragging.offset + (bar.travel > 0 ? delta / bar.travel * metrics(bar).maximum : 0));
            });
            function release(event) {
                if (dragging?.bar !== bar || dragging.pointer !== event.pointerId) return;
                dragging = null;
                rail.classList.remove('held');
                root.classList.remove('game-scrollbar-dragging');
                if (rail.hasPointerCapture(event.pointerId)) rail.releasePointerCapture(event.pointerId);
            }
            rail.addEventListener('pointerup', release);
            rail.addEventListener('pointercancel', release);
            rail.addEventListener('lostpointercapture', release);
            rail.addEventListener('keydown', event => {
                const { offset, viewport, maximum } = metrics(bar);
                const destinations = {
                    ArrowUp: offset - 40, ArrowLeft: offset - 40,
                    ArrowDown: offset + 40, ArrowRight: offset + 40,
                    PageUp: offset - viewport, PageDown: offset + viewport,
                    Home: 0, End: maximum,
                    ' ': offset + (event.shiftKey ? -viewport : viewport),
                };
                if (!(event.key in destinations)) return;
                event.preventDefault();
                scrollTo(bar, destinations[event.key]);
            });
            // Rails are outside their owners; forward wheel input to the same scroller.
            rail.addEventListener('wheel', event => {
                const { viewport } = metrics(bar);
                const scale = event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? viewport : 1;
                const delta = horizontal ? (event.deltaX || event.deltaY) : event.deltaY;
                const before = metrics(bar).offset;
                scrollTo(bar, before + delta * scale);
                if (metrics(bar).offset !== before) event.preventDefault();
            }, { passive: false });
            document.body.appendChild(rail);
        }
        // Hide native rails only once their interactive replacements exist.
        owner.classList.add('game-scroll-owner');
        sizes.observe(owner);
        if (owner.firstElementChild) sizes.observe(owner.firstElementChild);
    }

    function discover() {
        document.querySelectorAll('.sidebar-scrollbox, .content, pre, .table-wrapper, textarea')
            .forEach(add);
    }

    add(scrollingElement);
    sizes.observe(document.body);
    document.addEventListener('scroll', schedule, { capture: true, passive: true });
    document.addEventListener('load', schedule, true);
    document.addEventListener('transitionend', schedule);
    window.addEventListener('resize', schedule);
    // mdBook populates the sidebar and search results after initial page parsing.
    const changes = new MutationObserver(schedule);
    changes.observe(document.getElementById('mdbook-body-container'), {
        childList: true, subtree: true, attributes: true, characterData: true,
    });
    changes.observe(root, { attributes: true });
    schedule();
})();
