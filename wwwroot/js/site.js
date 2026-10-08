(() => {
    const shell = document.querySelector('[data-load-set]');
    if (!shell) return;

    const set = shell.dataset.loadSet;
    const session = shell.dataset.loadSession;
    if (!session) return;
    const pageVersion = Number(shell.dataset.loadVersion || '0');
    const basePath = shell.dataset.loadBasePath || '/';
    const apiPath = path => `${basePath.replace(/\/$/, '')}/${path}`;
    const status = document.querySelector('[data-load-status]');
    const message = document.querySelector('[data-load-message]');
    let wasRunning = false;

    async function heartbeat() {
        console.log('[Afleverlijst] heartbeat request', { set });
        try {
            const response = await fetch(`${apiPath('api/load/heartbeat')}?_set=${encodeURIComponent(set)}&session=${encodeURIComponent(session)}`, { method: 'POST', cache: 'no-store' });
            if (!response.ok) {
                console.error('[Afleverlijst] heartbeat HTTP error', response.status, response.statusText);
                return;
            }
            const data = await response.json();
            console.log('[Afleverlijst] heartbeat reply', data);
            if (Number(data.version ?? 0) !== pageVersion) {
                console.log('[Afleverlijst] version changed; page reload', { pageVersion, serverVersion: data.version });
                window.location.reload();
                return;
            }
            const running = data.state === 'initial_loading' || data.state === 'syncing' || data.state === 'processing';
            const initialLoadRunning = running && (data.state === 'initial_loading' || (data.state === 'processing' && (data.lastMode || '').toLowerCase().startsWith('initial')));
            status?.classList.toggle('is-loading', initialLoadRunning);
            if (message) message.textContent = running ? `${data.lastMode} wordt uitgevoerd…` : `Bronstatus: ${data.state}`;
            if (wasRunning && !running) window.location.reload();
            wasRunning = running;
        } catch (error) {
            console.error('[Afleverlijst] heartbeat failed', error);
        }
    }

    heartbeat();
    const timer = window.setInterval(heartbeat, 10000);
    window.addEventListener('pagehide', () => {
        window.clearInterval(timer);
        navigator.sendBeacon(`${apiPath('api/load/close')}?_set=${encodeURIComponent(set)}&session=${encodeURIComponent(session)}`);
    });
})();

(() => {
    const toggle = document.querySelector('[data-filter-toggle]');
    const panel = document.getElementById('delivery-filter-panel');
    const close = document.querySelector('[data-filter-close]');
    if (!toggle || !panel) return;

    const setOpen = open => {
        panel.hidden = !open;
        toggle.setAttribute('aria-expanded', String(open));
        if (open) panel.querySelector('select, input')?.focus();
    };

    toggle.addEventListener('click', () => setOpen(panel.hidden));
    close?.addEventListener('click', () => setOpen(false));
    document.addEventListener('keydown', event => {
        if (event.key === 'Escape' && !panel.hidden) setOpen(false);
    });
})();
