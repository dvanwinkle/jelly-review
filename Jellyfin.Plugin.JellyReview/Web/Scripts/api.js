export const Api = (() => {
    const base = '/JellyReview';

    async function request(method, path, body) {
        const opts = {
            method,
            headers: { 'Content-Type': 'application/json' },
            credentials: 'same-origin',
        };
        if (body !== undefined) {
            opts.body = JSON.stringify(body);
        }

        const resp = await fetch(base + path, opts);
        if (!resp.ok) {
            const text = await resp.text().catch(() => resp.statusText);
            throw new Error(`${resp.status}: ${text}`);
        }

        if (resp.status === 204) {
            return null;
        }

        return resp.json();
    }

    return {
        getMedia: (params) => {
            const q = new URLSearchParams(params || {}).toString();
            return request('GET', '/Media' + (q ? '?' + q : ''));
        },
        getCounts: (params) => {
            const q = new URLSearchParams(params || {}).toString();
            return request('GET', '/Media/counts' + (q ? '?' + q : ''));
        },
        sync: (full) => request('POST', `/Media/sync?full=${!!full}`),
        approve: (id, body) => request('POST', `/Reviews/${id}/approve`, body),
        deny: (id, body) => request('POST', `/Reviews/${id}/deny`, body),
        defer: (id, body) => request('POST', `/Reviews/${id}/defer`, body),
        reopen: (id, body) => request('POST', `/Reviews/${id}/reopen`, body),
        bulk: (body) => request('POST', '/Reviews/bulk', body),
        history: (id) => request('GET', `/Reviews/${id}/history`),
        getViewerProfiles: () => request('GET', '/Reviews/viewer-profiles'),
        createViewerProfile: (body) => request('POST', '/Reviews/viewer-profiles', body),
        getRules: () => request('GET', '/Rules'),
        createRule: (body) => request('POST', '/Rules', body),
        updateRule: (id, body) => request('PATCH', `/Rules/${id}`, body),
        deleteRule: (id) => request('DELETE', `/Rules/${id}`),
        getChannels: () => request('GET', '/Notifications/channels'),
        createChannel: (body) => request('POST', '/Notifications/channels', body),
        deleteChannel: (id) => request('DELETE', `/Notifications/channels/${id}`),
        testChannel: (id) => request('POST', `/Notifications/channels/${id}/test`),
        getSettings: () => request('GET', '/Settings'),
        updateTags: (body) => request('PATCH', '/Settings/tags', body),
        updateIntegrations: (body) => request('PATCH', '/Settings/integrations', body),
        getStatus: () => request('GET', '/System/status'),
    };
})();
