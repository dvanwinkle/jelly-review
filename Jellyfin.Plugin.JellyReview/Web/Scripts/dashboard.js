const PAGE_SIZE = 25;

function toCamel(obj) {
    if (Array.isArray(obj)) return obj.map(toCamel);
    if (obj && typeof obj === 'object') {
        const out = {};
        for (const [k, v] of Object.entries(obj)) {
            out[k[0].toLowerCase() + k.slice(1)] = toCamel(v);
        }
        return out;
    }
    return obj;
}

function loadScript(name) {
    return new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = `/JellyReview/Scripts/${name}`;
        script.onload = resolve;
        script.onerror = () => reject(new Error(`Failed to load ${name}`));
        document.head.appendChild(script);
    });
}

function esc(str) {
    if (!str) return '';
    return str
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

function makeApi() {
    const base = '/JellyReview';

    function jellyfinAuthHeader() {
        const c = window.ApiClient;
        if (!c) return '';
        const token = typeof c.accessToken === 'function' ? c.accessToken() : c.accessToken;
        if (!token) return '';
        return `MediaBrowser Token="${token}"`;
    }

    async function request(method, path, body) {
        const opts = {
            method,
            headers: {
                'Content-Type': 'application/json',
                'X-Emby-Authorization': jellyfinAuthHeader(),
            },
        };
        if (body !== undefined) opts.body = JSON.stringify(body);
        const resp = await fetch(base + path, opts);
        if (!resp.ok) {
            const text = await resp.text().catch(() => resp.statusText);
            throw new Error(`${resp.status}: ${text}`);
        }
        if (resp.status === 204) return null;
        return resp.json().then(toCamel);
    }

    return {
        getMedia: (params) => {
            const q = new URLSearchParams(params || {}).toString();
            return request('GET', '/Media' + (q ? '?' + q : ''));
        },
        getCounts: () => request('GET', '/Media/counts'),
        sync: (full) => request('POST', `/Media/sync?full=${!!full}`),
        approve: (id, body) => request('POST', `/Reviews/${id}/approve`, body),
        deny: (id, body) => request('POST', `/Reviews/${id}/deny`, body),
        defer: (id, body) => request('POST', `/Reviews/${id}/defer`, body),
        reopen: (id, body) => request('POST', `/Reviews/${id}/reopen`, body),
        bulk: (body) => request('POST', '/Reviews/bulk', body),
        history: (id) => request('GET', `/Reviews/${id}/history`),
        getJellyfinUsers: () => request('GET', '/Reviews/jellyfin-users'),
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
}

function createController(view) {
    const Api = makeApi();
    let currentSection = 'pending';
    let currentState = 'pending';
    let currentOffset = 0;
    let eventsWired = false;
    let rb = null;

    const $ = (sel) => view.querySelector(sel);
    const $$ = (sel) => Array.from(view.querySelectorAll(sel));

    function showToast(msg, isError = false) {
        const el = $('#jr-toast');
        if (!el) return;
        el.textContent = msg;
        el.className = `jr-toast ${isError ? 'jr-toast-error' : 'jr-toast-success'}`;
        el.style.display = 'block';
        setTimeout(() => { el.style.display = 'none'; }, 3000);
    }

    function showError(msg) { showToast(msg, true); }

    function closeHistoryModal() {
        const modal = $('#jr-history-modal');
        if (modal) modal.style.display = 'none';
    }

    async function loadStatus() {
        try {
            const s = await Api.getStatus();
            const pending = $('#jr-status-pending');
            if (pending) pending.textContent = s.pendingCount;
            const ver = $('#jr-version');
            if (ver) ver.textContent = `v${s.version}`;
        } catch (e) {
            console.warn('JellyReview: failed to load status', e);
        }
    }

    async function loadCounts() {
        try {
            const c = await Api.getCounts();
            $('#jr-count-pending').textContent = c.pending;
            $('#jr-count-approved').textContent = c.approved;
            $('#jr-count-denied').textContent = c.denied;
            $('#jr-count-deferred').textContent = c.deferred;
        } catch { }
    }

    function renderPagination(targetId, total) {
        const el = $(targetId);
        if (!el) return;
        const pages = Math.max(1, Math.ceil(total / PAGE_SIZE));
        const page = Math.floor(currentOffset / PAGE_SIZE) + 1;
        el.innerHTML = `
            <button type="button" data-page-dir="-1" ${page <= 1 ? 'disabled' : ''}>Prev</button>
            <span>Page ${page} of ${pages} (${total} items)</span>
            <button type="button" data-page-dir="1" ${page >= pages ? 'disabled' : ''}>Next</button>
        `;
    }

    function renderMediaItems(target, items, includeCheckboxes) {
        if (!items?.length) {
            target.innerHTML = '<p class="jr-empty">No items found.</p>';
            return;
        }
        target.innerHTML = items.map((item) => {
            const state = item.decision?.state ?? 'unknown';
            const year = item.year ?? '';
            const rating = item.officialRating ?? 'NR';
            const genres = (item.genres || []).slice(0, 2).join(', ');
            return `
                <div class="jr-card" data-id="${esc(item.id)}">
                    ${includeCheckboxes ? `<div style="padding-top:4px"><input class="jr-check" type="checkbox" data-id="${esc(item.id)}"></div>` : ''}
                    <img class="jr-poster" src="/Items/${esc(item.jellyfinItemId)}/Images/Primary?maxHeight=150" onerror="this.src=''" loading="lazy">
                    <div class="jr-card-body">
                        <div class="jr-card-title">${esc(item.title)} <span class="jr-year">${year}</span></div>
                        <div class="jr-card-meta">${esc(rating)} · ${esc(item.mediaType)} ${genres ? `· ${esc(genres)}` : ''}</div>
                        ${item.overview ? `<div class="jr-card-overview">${esc(item.overview.slice(0, 150))}${item.overview.length > 150 ? '…' : ''}</div>` : ''}
                        <div class="jr-card-state jr-state-${state}">${state}</div>
                        <div class="jr-card-actions">
                            ${includeCheckboxes
                                ? `
                                    <button type="button" class="jr-btn jr-btn-approve" data-action="approve" data-id="${esc(item.id)}">Approve</button>
                                    <button type="button" class="jr-btn jr-btn-deny" data-action="deny" data-id="${esc(item.id)}">Deny</button>
                                    <button type="button" class="jr-btn jr-btn-defer" data-action="defer" data-id="${esc(item.id)}">Defer</button>
                                  `
                                : `<button type="button" class="jr-btn jr-btn-secondary" data-action="reopen" data-id="${esc(item.id)}">Reopen</button>`
                            }
                            <button type="button" class="jr-btn jr-btn-secondary" data-history-id="${esc(item.id)}" data-history-title="${esc(item.title)}">History</button>
                        </div>
                    </div>
                </div>
            `;
        }).join('');
    }

    async function loadMediaList(stateOverride = undefined) {
        if (stateOverride !== undefined) currentState = stateOverride;
        const params = { limit: PAGE_SIZE, offset: currentOffset };
        if (currentState) params.state = currentState;
        const search = currentSection === 'history' ? $('#jr-history-search')?.value : $('#jr-search')?.value;
        if (search) params.search = search;

        try {
            const data = await Api.getMedia(params);
            if (currentSection === 'history') {
                renderMediaItems($('#jr-history-items'), data.items, false);
                renderPagination('#jr-history-pagination', data.total);
            } else {
                renderMediaItems($('#jr-media-list'), data.items, true);
                renderPagination('#jr-pagination', data.total);
            }
        } catch (e) {
            showError('Failed to load media: ' + e.message);
        }
    }

    async function showSection(section) {
        currentSection = section;
        $$('.jr-section').forEach((el) => { el.style.display = 'none'; });
        $$('.jr-nav-btn').forEach((el) => el.classList.remove('active'));
        $(`#jr-section-${section}`)?.style.setProperty('display', 'block');
        view.querySelector(`.jr-nav-btn[data-section="${section}"]`)?.classList.add('active');

        switch (section) {
            case 'pending':
                currentOffset = 0; currentState = 'pending';
                await loadMediaList(); break;
            case 'history':
                currentOffset = 0; currentState = $('#jr-history-state')?.value || null;
                await loadMediaList(); break;
            case 'profiles': await loadViewerProfiles(); break;
            case 'rules': await loadRules(); break;
            case 'notifications': await loadChannels(); break;
            case 'settings': await loadSettings(); break;
        }
    }

    async function doAction(itemId, action) {
        try {
            await Api[action](itemId);
            await loadCounts();
            await loadMediaList();
            showToast(`${action.charAt(0).toUpperCase() + action.slice(1)}d successfully`);
        } catch (e) {
            showError(`Failed to ${action}: ${e.message}`);
        }
    }

    async function bulkAction(action) {
        const checked = $$('.jr-check:checked').map((c) => c.dataset.id);
        if (!checked.length) { showError('No items selected'); return; }
        try {
            const r = await Api.bulk({ itemIds: checked, action });
            showToast(`${r.succeeded} item(s) ${action}d`);
            await loadCounts();
            await loadMediaList();
        } catch (e) {
            showError('Bulk action failed: ' + e.message);
        }
    }

    async function showHistory(itemId, title) {
        try {
            const hist = await Api.history(itemId);
            $('#jr-history-title').textContent = title;
            $('#jr-history-body').innerHTML = hist.map((h) => `
                <tr>
                    <td>${h.createdAt.replace('T', ' ').slice(0, 19)}</td>
                    <td>${h.previousState ?? '—'} → ${h.newState}</td>
                    <td>${h.action}</td>
                    <td>${h.actorType} ${h.actorId ?? ''}</td>
                </tr>
            `).join('');
            $('#jr-history-modal').style.display = 'flex';
        } catch (e) {
            showError('Failed to load history: ' + e.message);
        }
    }

    async function loadViewerProfiles() {
        try {
            const [users, profiles] = await Promise.all([
                Api.getJellyfinUsers(),
                Api.getViewerProfiles(),
            ]);

            const profileByUserId = {};
            profiles.forEach((p) => { if (p.jellyfinUserId) profileByUserId[p.jellyfinUserId] = p; });

            $('#jr-profiles-list').innerHTML = users.map((u) => {
                const profile = profileByUserId[u.id];
                return `
                <div class="jr-profile-card" style="display:flex;align-items:center;gap:12px;">
                    <div style="flex:1">
                        <strong>${esc(u.name)}</strong>
                        ${profile ? `<span class="jr-meta" style="margin-left:8px">Profile: ${esc(profile.displayName)}</span>` : ''}
                        ${profile?.ageHint ? `<span class="jr-meta" style="margin-left:8px">Age hint: ${profile.ageHint}</span>` : ''}
                    </div>
                    ${!profile ? `<button type="button" class="jr-btn jr-btn-approve" data-create-profile-id="${esc(u.id)}" data-create-profile-name="${esc(u.name)}">Add Profile</button>` : '<span class="jr-meta">✓ Profile active</span>'}
                </div>`;
            }).join('') || '<p class="jr-empty">No Jellyfin users found.</p>';
        } catch (e) {
            showError('Failed to load profiles: ' + e.message);
        }
    }

    async function createProfileForUser(jellyfinUserId, displayName) {
        try {
            await Api.createViewerProfile({ displayName, jellyfinUserId });
            showToast(`Profile created for ${displayName}`);
            await loadViewerProfiles();
        } catch (e) {
            showError('Failed to create profile: ' + e.message);
        }
    }

    let editingRuleId = null;

    function getConditionsFromBuilder() {
        return rb.getConditionsFromDom($$('.jr-condition-row'));
    }

    function updateConditionPreview() {
        const conditions = getConditionsFromBuilder();
        const preview = $('#jr-rule-preview');
        if (!preview) return;
        const keys = Object.keys(conditions);
        if (keys.length === 0) {
            preview.style.display = 'none';
        } else {
            preview.style.display = 'block';
            preview.textContent = JSON.stringify(conditions);
        }
    }

    function addConditionType() {
        const select = $('#jr-add-condition-type');
        const type = select.value;
        if (!type) return;
        if ($(`[data-condition-type="${type}"]`)) {
            showError('That condition is already added');
            select.value = '';
            return;
        }
        const list = $('#jr-conditions-list');
        list.insertAdjacentHTML('beforeend', rb.renderConditionRow(type, type.startsWith('community_rating') ? null : []));
        select.value = '';

        const opt = select.querySelector(`option[value="${type}"]`);
        if (opt) opt.disabled = true;

        const exclusive = rb.MUTUALLY_EXCLUSIVE[type];
        if (exclusive) {
            const exOpt = select.querySelector(`option[value="${exclusive}"]`);
            if (exOpt) exOpt.disabled = true;
        }

        updateConditionPreview();
    }

    function removeCondition(row) {
        const type = row.dataset.conditionType;
        row.remove();
        const opt = $(`#jr-add-condition-type option[value="${type}"]`);
        if (opt) opt.disabled = false;
        const exclusive = rb.MUTUALLY_EXCLUSIVE[type];
        if (exclusive && !$(`[data-condition-type="${exclusive}"]`)) {
            const exOpt = $(`#jr-add-condition-type option[value="${exclusive}"]`);
            if (exOpt) exOpt.disabled = false;
        }
        updateConditionPreview();
    }

    function resetRuleBuilder() {
        editingRuleId = null;
        $('#jr-rule-name').value = '';
        $('#jr-rule-action').value = 'auto_approve';
        $('#jr-rule-priority').value = '100';
        $('#jr-conditions-list').innerHTML = '';
        $('#jr-rule-preview').style.display = 'none';
        $('#jr-rule-builder-title').textContent = 'Add Rule';
        $('#jr-rule-create').textContent = 'Add Rule';
        $('#jr-rule-cancel').style.display = 'none';
        $$('#jr-add-condition-type option').forEach((o) => { o.disabled = false; });
    }

    function populateBuilderForEdit(rule) {
        editingRuleId = rule.id;
        $('#jr-rule-name').value = rule.name;
        $('#jr-rule-action').value = rule.action;
        $('#jr-rule-priority').value = rule.priority;
        $('#jr-conditions-list').innerHTML = '';
        $$('#jr-add-condition-type option').forEach((o) => { o.disabled = false; });

        let conditions = {};
        try { conditions = JSON.parse(rule.conditionsJson); } catch {}

        for (const [type, value] of Object.entries(conditions)) {
            if (!(type in rb.CONDITION_LABELS)) continue;
            const list = $('#jr-conditions-list');
            list.insertAdjacentHTML('beforeend', rb.renderConditionRow(type, value));
            const opt = $(`#jr-add-condition-type option[value="${type}"]`);
            if (opt) opt.disabled = true;
            const exclusive = rb.MUTUALLY_EXCLUSIVE[type];
            if (exclusive) {
                const exOpt = $(`#jr-add-condition-type option[value="${exclusive}"]`);
                if (exOpt) exOpt.disabled = true;
            }
        }

        $('#jr-rule-builder-title').textContent = 'Edit Rule';
        $('#jr-rule-create').textContent = 'Save Rule';
        $('#jr-rule-cancel').style.display = '';
        updateConditionPreview();

        $('#jr-rule-builder').scrollIntoView({ behavior: 'smooth' });
    }

    async function ensureRuleBuilder() {
        if (!rb) {
            await loadScript('rule-builder.js');
            rb = window.JellyReviewRuleBuilder;
        }
    }

    async function loadRules() {
        try {
            await ensureRuleBuilder();
            const rules = await Api.getRules();
            $('#jr-rules-list').innerHTML = rules.map((r) => `
                <div class="jr-rule-row">
                    <span class="jr-rule-priority">${r.priority}</span>
                    <span class="jr-rule-name">${esc(r.name)}</span>
                    <span class="jr-rule-action jr-action-${r.action}">${r.action}</span>
                    <span class="jr-rule-conditions">${rb.formatConditions(r.conditionsJson)}</span>
                    <button type="button" class="jr-btn jr-btn-secondary" data-rule-edit='${esc(JSON.stringify(r))}'>Edit</button>
                    <button type="button" class="jr-btn jr-btn-secondary" data-rule-toggle="${esc(r.id)}" data-rule-enabled="${!r.enabled}">${r.enabled ? 'Disable' : 'Enable'}</button>
                    <button type="button" class="jr-btn jr-btn-danger" data-rule-delete="${esc(r.id)}">Delete</button>
                </div>
            `).join('') || '<p class="jr-empty">No rules yet.</p>';
        } catch (e) {
            showError('Failed to load rules: ' + e.message);
        }
    }

    async function saveRule() {
        const name = $('#jr-rule-name').value.trim();
        if (!name) { showError('Rule name is required'); return; }

        const conditions = getConditionsFromBuilder();
        if (Object.keys(conditions).length === 0) { showError('Add at least one condition'); return; }

        const conditionsJson = JSON.stringify(conditions);
        const action = $('#jr-rule-action').value;
        const priority = parseInt($('#jr-rule-priority').value, 10) || 100;

        try {
            if (editingRuleId) {
                await Api.updateRule(editingRuleId, { name, action, priority, conditionsJson });
                showToast('Rule updated');
            } else {
                await Api.createRule({ name, action, priority, conditionsJson, enabled: true });
                showToast('Rule created');
            }
            resetRuleBuilder();
            await loadRules();
        } catch (e) {
            showError(`Failed to ${editingRuleId ? 'update' : 'create'} rule: ${e.message}`);
        }
    }

    async function toggleRule(id, enabled) {
        try { await Api.updateRule(id, { enabled }); await loadRules(); }
        catch (e) { showError('Failed to update rule: ' + e.message); }
    }

    async function deleteRule(id) {
        if (!window.confirm('Delete this rule?')) return;
        try { await Api.deleteRule(id); await loadRules(); }
        catch (e) { showError('Failed to delete rule: ' + e.message); }
    }

    async function loadChannels() {
        try {
            const channels = await Api.getChannels();
            $('#jr-channels-list').innerHTML = channels.map((ch) => `
                <div class="jr-channel-row">
                    <span class="jr-channel-name">${esc(ch.name)}</span>
                    <span class="jr-channel-type">${esc(ch.providerType)}</span>
                    <span class="${ch.enabled ? 'jr-badge-on' : 'jr-badge-off'}">${ch.enabled ? 'Enabled' : 'Disabled'}</span>
                    <button type="button" class="jr-btn jr-btn-secondary" data-channel-test="${esc(ch.id)}">Test</button>
                    <button type="button" class="jr-btn jr-btn-danger" data-channel-delete="${esc(ch.id)}">Delete</button>
                </div>
            `).join('') || '<p class="jr-empty">No channels configured.</p>';
        } catch (e) {
            showError('Failed to load channels: ' + e.message);
        }
    }

    async function createChannel() {
        const name = $('#jr-ch-name').value.trim();
        const rawConfig = $('#jr-ch-config').value.trim();
        if (!name || !rawConfig) { showError('Channel name and config are required'); return; }
        let config;
        try { config = JSON.parse(rawConfig); } catch { showError('Config must be valid JSON'); return; }
        try {
            await Api.createChannel({
                name,
                providerType: $('#jr-ch-type').value,
                config,
                notifyOnPending: true,
                notifyOnConflict: true,
            });
            showToast('Channel created');
            await loadChannels();
        } catch (e) {
            showError('Failed to create channel: ' + e.message);
        }
    }

    async function testChannel(id) {
        try {
            const result = await Api.testChannel(id);
            if (result?.success) showToast('Test sent successfully');
            else showError(result?.error || 'Test failed');
        } catch (e) { showError('Failed to test channel: ' + e.message); }
    }

    async function deleteChannel(id) {
        if (!window.confirm('Delete this channel?')) return;
        try { await Api.deleteChannel(id); await loadChannels(); }
        catch (e) { showError('Failed to delete channel: ' + e.message); }
    }

    async function loadSettings() {
        try {
            const s = await Api.getSettings();
            $('#jr-setting-pending-tag').value = s.pendingTag;
            $('#jr-setting-denied-tag').value = s.deniedTag;
            $('#jr-setting-polling').value = s.pollingIntervalSeconds;
            $('#jr-setting-auto-rules').checked = s.autoRulesEnabled;
        } catch (e) { showError('Failed to load settings: ' + e.message); }
    }

    async function saveTags() {
        try {
            await Api.updateTags({
                pendingTag: $('#jr-setting-pending-tag').value.trim(),
                deniedTag: $('#jr-setting-denied-tag').value.trim(),
            });
            showToast('Tags saved');
        } catch (e) { showError('Failed to save tags: ' + e.message); }
    }

    async function saveIntegrations() {
        try {
            await Api.updateIntegrations({
                pollingIntervalSeconds: parseInt($('#jr-setting-polling').value, 10),
                autoRulesEnabled: $('#jr-setting-auto-rules').checked,
            });
            showToast('Settings saved');
        } catch (e) { showError('Failed to save settings: ' + e.message); }
    }

    async function triggerSync(full) {
        const btn = full ? $('#jr-btn-full-sync') : $('#jr-btn-sync');
        if (btn) btn.disabled = true;
        try {
            const r = await Api.sync(full);
            showToast(`Sync complete: ${r.imported} imported, ${r.updated ?? 0} updated, ${r.errors} errors`);
            await loadCounts();
        } catch (e) {
            showError('Sync failed: ' + e.message);
        } finally {
            if (btn) btn.disabled = false;
        }
    }

    function bindEvents() {
        if (eventsWired) return;
        eventsWired = true;

        $('#jr-nav')?.addEventListener('click', (event) => {
            const btn = event.target.closest('.jr-nav-btn');
            if (btn) showSection(btn.dataset.section);
        });

        $$('.jr-count-badge').forEach((badge) => {
            badge.addEventListener('click', () => {
                const state = badge.dataset.state;
                if (state === 'pending') {
                    showSection('pending');
                } else {
                    const histState = $('#jr-history-state');
                    if (histState) histState.value = state;
                    showSection('history');
                }
            });
        });

        $('#jr-search')?.addEventListener('input', () => { currentOffset = 0; loadMediaList(); });
        $('#jr-history-search')?.addEventListener('input', () => { currentOffset = 0; loadMediaList(); });
        $('#jr-history-state')?.addEventListener('change', () => {
            currentOffset = 0;
            currentState = $('#jr-history-state')?.value || null;
            loadMediaList();
        });

        $('#jr-bulk-approve')?.addEventListener('click', () => bulkAction('approve'));
        $('#jr-bulk-deny')?.addEventListener('click', () => bulkAction('deny'));
        $('#jr-bulk-defer')?.addEventListener('click', () => bulkAction('defer'));
        $('#jr-rule-create')?.addEventListener('click', saveRule);
        $('#jr-rule-cancel')?.addEventListener('click', resetRuleBuilder);
        $('#jr-add-condition-type')?.addEventListener('change', addConditionType);
        $('#jr-conditions-list')?.addEventListener('input', (event) => {
            if (event.target.closest('.jr-condition-number')) updateConditionPreview();
        });
        $('#jr-channel-create')?.addEventListener('click', createChannel);
        $('#jr-tags-save')?.addEventListener('click', saveTags);
        $('#jr-integrations-save')?.addEventListener('click', saveIntegrations);
        $('#jr-btn-sync')?.addEventListener('click', () => triggerSync(false));
        $('#jr-btn-full-sync')?.addEventListener('click', () => triggerSync(true));
        $('#jr-history-close')?.addEventListener('click', closeHistoryModal);
        $('#jr-history-modal')?.addEventListener('click', (event) => {
            if (event.target.id === 'jr-history-modal') closeHistoryModal();
        });

        view.addEventListener('click', (event) => {
            const actionBtn = event.target.closest('[data-action]');
            if (actionBtn) { doAction(actionBtn.dataset.id, actionBtn.dataset.action); return; }

            const historyBtn = event.target.closest('[data-history-id]');
            if (historyBtn) { showHistory(historyBtn.dataset.historyId, historyBtn.dataset.historyTitle); return; }

            const pagerBtn = event.target.closest('[data-page-dir]');
            if (pagerBtn) {
                currentOffset = Math.max(0, currentOffset + parseInt(pagerBtn.dataset.pageDir, 10) * PAGE_SIZE);
                loadMediaList();
                return;
            }

            const chip = event.target.closest('.jr-chip');
            if (chip) { chip.classList.toggle('selected'); updateConditionPreview(); return; }

            const removeCondBtn = event.target.closest('.jr-condition-remove');
            if (removeCondBtn) { removeCondition(removeCondBtn.closest('.jr-condition-row')); return; }

            const condNumInput = event.target.closest('.jr-condition-number');
            if (condNumInput) { updateConditionPreview(); return; }

            const editRuleBtn = event.target.closest('[data-rule-edit]');
            if (editRuleBtn) { populateBuilderForEdit(JSON.parse(editRuleBtn.dataset.ruleEdit)); return; }

            const toggleBtn = event.target.closest('[data-rule-toggle]');
            if (toggleBtn) { toggleRule(toggleBtn.dataset.ruleToggle, toggleBtn.dataset.ruleEnabled === 'true'); return; }

            const deleteRuleBtn = event.target.closest('[data-rule-delete]');
            if (deleteRuleBtn) { deleteRule(deleteRuleBtn.dataset.ruleDelete); return; }

            const testChannelBtn = event.target.closest('[data-channel-test]');
            if (testChannelBtn) { testChannel(testChannelBtn.dataset.channelTest); return; }

            const deleteChannelBtn = event.target.closest('[data-channel-delete]');
            if (deleteChannelBtn) { deleteChannel(deleteChannelBtn.dataset.channelDelete); return; }

            const createProfileBtn = event.target.closest('[data-create-profile-id]');
            if (createProfileBtn) {
                createProfileForUser(createProfileBtn.dataset.createProfileId, createProfileBtn.dataset.createProfileName);
            }
        });
    }

    return {
        bindEvents,
        async refresh() {
            currentOffset = 0;
            currentState = 'pending';
            await loadStatus();
            await loadCounts();
            await showSection('pending');
        },
    };
}

export default function (view) {
    const controller = createController(view);

    view.addEventListener('viewshow', async () => {
        controller.bindEvents();
        await controller.refresh();
    });
}
