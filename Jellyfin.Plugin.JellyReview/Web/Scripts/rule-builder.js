// UMD-style: export for ES module (tests) and attach to window (Jellyfin runtime)
function esc(str) {
    if (!str) return '';
    return str
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

const RATING_OPTIONS = ['G', 'PG', 'PG-13', 'R', 'NC-17', 'TV-Y', 'TV-Y7', 'TV-G', 'TV-PG', 'TV-14', 'TV-MA', 'X', 'NR', 'UR'];
const MEDIA_TYPE_OPTIONS = ['movie', 'series'];
const GENRE_OPTIONS = ['Action', 'Adventure', 'Animation', 'Comedy', 'Crime', 'Documentary', 'Drama', 'Family', 'Fantasy', 'History', 'Horror', 'Music', 'Mystery', 'Romance', 'Science Fiction', 'Thriller', 'War', 'Western'];

const CONDITION_LABELS = {
    official_rating_in: 'Rating is one of',
    official_rating_not_in: 'Rating is NOT one of',
    media_type_in: 'Media type is',
    genre_in: 'Genre includes',
    community_rating_gte: 'Community rating \u2265',
    community_rating_lte: 'Community rating \u2264',
};

const MUTUALLY_EXCLUSIVE = {
    official_rating_in: 'official_rating_not_in',
    official_rating_not_in: 'official_rating_in',
};

function renderConditionRow(type, values) {
    const label = CONDITION_LABELS[type] || type;
    let inputHtml = '';

    if (type === 'official_rating_in' || type === 'official_rating_not_in') {
        inputHtml = `<div class="jr-chip-group">${RATING_OPTIONS.map((r) =>
            `<button type="button" class="jr-chip${(values || []).includes(r) ? ' selected' : ''}" data-chip-value="${esc(r)}">${esc(r)}</button>`
        ).join('')}</div>`;
    } else if (type === 'media_type_in') {
        inputHtml = `<div class="jr-chip-group">${MEDIA_TYPE_OPTIONS.map((t) =>
            `<button type="button" class="jr-chip${(values || []).includes(t) ? ' selected' : ''}" data-chip-value="${esc(t)}">${t === 'movie' ? 'Movie' : 'Series'}</button>`
        ).join('')}</div>`;
    } else if (type === 'genre_in') {
        inputHtml = `<div class="jr-chip-group">${GENRE_OPTIONS.map((g) =>
            `<button type="button" class="jr-chip${(values || []).includes(g) ? ' selected' : ''}" data-chip-value="${esc(g)}">${esc(g)}</button>`
        ).join('')}</div>`;
    } else if (type === 'community_rating_gte' || type === 'community_rating_lte') {
        inputHtml = `<input type="number" class="jr-condition-number" min="0" max="10" step="0.1" value="${values ?? ''}" placeholder="0-10">`;
    }

    return `<div class="jr-condition-row" data-condition-type="${esc(type)}">
            <span style="color:#94a3b8;font-size:12px;font-weight:600;min-width:140px;padding-top:5px">${label}</span>
            ${inputHtml}
            <button type="button" class="jr-condition-remove" title="Remove condition">&times;</button>
        </div>`;
}

function getConditionsFromDom(conditionRows) {
    const conditions = {};
    conditionRows.forEach((row) => {
        const type = row.dataset.conditionType;
        if (type === 'community_rating_gte' || type === 'community_rating_lte') {
            const val = row.querySelector('.jr-condition-number')?.value;
            if (val !== '' && val != null) conditions[type] = parseFloat(val);
        } else {
            const selected = Array.from(row.querySelectorAll('.jr-chip.selected')).map((c) => c.dataset.chipValue);
            if (selected.length) conditions[type] = selected;
        }
    });
    return conditions;
}

function formatConditions(conditionsJson) {
    try {
        const c = JSON.parse(conditionsJson);
        const parts = [];
        for (const [key, val] of Object.entries(c)) {
            const label = CONDITION_LABELS[key] || key;
            if (Array.isArray(val)) {
                parts.push(`${label}: ${val.join(', ')}`);
            } else {
                parts.push(`${label}: ${val}`);
            }
        }
        return esc(parts.join(' · '));
    } catch {
        return esc(conditionsJson);
    }
}

function getExclusionTarget(type) {
    return MUTUALLY_EXCLUSIVE[type] || null;
}

const JellyReviewRuleBuilder = {
    esc,
    RATING_OPTIONS,
    MEDIA_TYPE_OPTIONS,
    GENRE_OPTIONS,
    CONDITION_LABELS,
    MUTUALLY_EXCLUSIVE,
    renderConditionRow,
    getConditionsFromDom,
    formatConditions,
    getExclusionTarget,
};

// Attach to window for Jellyfin runtime (loaded as a plain <script> tag)
if (typeof window !== 'undefined') {
    window.JellyReviewRuleBuilder = JellyReviewRuleBuilder;
}
