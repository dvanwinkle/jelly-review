import { describe, it, expect, beforeEach } from 'vitest';
import { JSDOM } from 'jsdom';
import {
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
} from '../Jellyfin.Plugin.JellyReview/Web/Scripts/rule-builder.js';

describe('esc', () => {
    it('returns empty string for falsy input', () => {
        expect(esc('')).toBe('');
        expect(esc(null)).toBe('');
        expect(esc(undefined)).toBe('');
    });

    it('escapes HTML special characters', () => {
        expect(esc('<script>alert("xss")</script>')).toBe(
            '&lt;script&gt;alert(&quot;xss&quot;)&lt;/script&gt;'
        );
    });

    it('escapes ampersands', () => {
        expect(esc('Tom & Jerry')).toBe('Tom &amp; Jerry');
    });

    it('passes through safe strings unchanged', () => {
        expect(esc('hello world')).toBe('hello world');
    });
});

describe('constants', () => {
    it('has all expected ratings', () => {
        expect(RATING_OPTIONS).toContain('G');
        expect(RATING_OPTIONS).toContain('PG-13');
        expect(RATING_OPTIONS).toContain('R');
        expect(RATING_OPTIONS).toContain('TV-MA');
        expect(RATING_OPTIONS).toContain('NC-17');
        expect(RATING_OPTIONS.length).toBe(14);
    });

    it('has movie and series media types', () => {
        expect(MEDIA_TYPE_OPTIONS).toEqual(['movie', 'series']);
    });

    it('has labels for all condition types', () => {
        expect(Object.keys(CONDITION_LABELS)).toEqual([
            'official_rating_in',
            'official_rating_not_in',
            'media_type_in',
            'genre_in',
            'community_rating_gte',
            'community_rating_lte',
        ]);
    });
});

describe('MUTUALLY_EXCLUSIVE', () => {
    it('maps rating_in to rating_not_in and vice versa', () => {
        expect(MUTUALLY_EXCLUSIVE.official_rating_in).toBe('official_rating_not_in');
        expect(MUTUALLY_EXCLUSIVE.official_rating_not_in).toBe('official_rating_in');
    });

    it('does not include non-exclusive conditions', () => {
        expect(MUTUALLY_EXCLUSIVE.media_type_in).toBeUndefined();
        expect(MUTUALLY_EXCLUSIVE.genre_in).toBeUndefined();
        expect(MUTUALLY_EXCLUSIVE.community_rating_gte).toBeUndefined();
        expect(MUTUALLY_EXCLUSIVE.community_rating_lte).toBeUndefined();
    });
});

describe('getExclusionTarget', () => {
    it('returns the opposite for exclusive types', () => {
        expect(getExclusionTarget('official_rating_in')).toBe('official_rating_not_in');
        expect(getExclusionTarget('official_rating_not_in')).toBe('official_rating_in');
    });

    it('returns null for non-exclusive types', () => {
        expect(getExclusionTarget('media_type_in')).toBeNull();
        expect(getExclusionTarget('genre_in')).toBeNull();
        expect(getExclusionTarget('community_rating_gte')).toBeNull();
    });
});

describe('renderConditionRow', () => {
    let dom;

    function parse(html) {
        dom = new JSDOM(`<div>${html}</div>`);
        return dom.window.document.querySelector('.jr-condition-row');
    }

    it('renders rating chips for official_rating_in', () => {
        const row = parse(renderConditionRow('official_rating_in', ['G', 'PG']));
        expect(row.dataset.conditionType).toBe('official_rating_in');

        const chips = row.querySelectorAll('.jr-chip');
        expect(chips.length).toBe(RATING_OPTIONS.length);

        const selected = row.querySelectorAll('.jr-chip.selected');
        expect(selected.length).toBe(2);
        expect(selected[0].dataset.chipValue).toBe('G');
        expect(selected[1].dataset.chipValue).toBe('PG');
    });

    it('renders rating chips for official_rating_not_in', () => {
        const row = parse(renderConditionRow('official_rating_not_in', ['R']));
        expect(row.dataset.conditionType).toBe('official_rating_not_in');

        const selected = row.querySelectorAll('.jr-chip.selected');
        expect(selected.length).toBe(1);
        expect(selected[0].dataset.chipValue).toBe('R');
    });

    it('renders media type chips', () => {
        const row = parse(renderConditionRow('media_type_in', ['movie']));
        const chips = row.querySelectorAll('.jr-chip');
        expect(chips.length).toBe(2);

        const selected = row.querySelectorAll('.jr-chip.selected');
        expect(selected.length).toBe(1);
        expect(selected[0].textContent).toBe('Movie');
    });

    it('renders genre chips', () => {
        const row = parse(renderConditionRow('genre_in', ['Horror', 'Thriller']));
        const chips = row.querySelectorAll('.jr-chip');
        expect(chips.length).toBe(GENRE_OPTIONS.length);

        const selected = row.querySelectorAll('.jr-chip.selected');
        expect(selected.length).toBe(2);
        const values = Array.from(selected).map((c) => c.dataset.chipValue);
        expect(values).toContain('Horror');
        expect(values).toContain('Thriller');
    });

    it('renders number input for community_rating_gte', () => {
        const row = parse(renderConditionRow('community_rating_gte', 7.5));
        const input = row.querySelector('.jr-condition-number');
        expect(input).not.toBeNull();
        expect(input.value).toBe('7.5');
        expect(input.type).toBe('number');
    });

    it('renders number input for community_rating_lte', () => {
        const row = parse(renderConditionRow('community_rating_lte', null));
        const input = row.querySelector('.jr-condition-number');
        expect(input).not.toBeNull();
        expect(input.value).toBe('');
    });

    it('renders with no pre-selected chips when values is empty', () => {
        const row = parse(renderConditionRow('official_rating_in', []));
        const selected = row.querySelectorAll('.jr-chip.selected');
        expect(selected.length).toBe(0);
    });

    it('includes a remove button', () => {
        const row = parse(renderConditionRow('media_type_in', []));
        const removeBtn = row.querySelector('.jr-condition-remove');
        expect(removeBtn).not.toBeNull();
    });

    it('displays the correct label', () => {
        const row = parse(renderConditionRow('genre_in', []));
        const label = row.querySelector('span');
        expect(label.textContent).toBe('Genre includes');
    });
});

describe('getConditionsFromDom', () => {
    let dom;

    function createRows(html) {
        dom = new JSDOM(`<div>${html}</div>`);
        return Array.from(dom.window.document.querySelectorAll('.jr-condition-row'));
    }

    it('extracts selected chip values as arrays', () => {
        const rows = createRows(renderConditionRow('official_rating_in', ['G', 'PG']));
        const result = getConditionsFromDom(rows);
        expect(result).toEqual({ official_rating_in: ['G', 'PG'] });
    });

    it('extracts number values as floats', () => {
        const html = renderConditionRow('community_rating_gte', 7);
        const rows = createRows(html);
        const result = getConditionsFromDom(rows);
        expect(result).toEqual({ community_rating_gte: 7 });
    });

    it('skips chip conditions with no selections', () => {
        const rows = createRows(renderConditionRow('media_type_in', []));
        const result = getConditionsFromDom(rows);
        expect(result).toEqual({});
    });

    it('skips number conditions with empty value', () => {
        const rows = createRows(renderConditionRow('community_rating_lte', null));
        const result = getConditionsFromDom(rows);
        expect(result).toEqual({});
    });

    it('combines multiple condition rows', () => {
        const html =
            renderConditionRow('official_rating_in', ['PG-13']) +
            renderConditionRow('genre_in', ['Horror']) +
            renderConditionRow('community_rating_gte', 6);
        const rows = createRows(html);
        const result = getConditionsFromDom(rows);
        expect(result).toEqual({
            official_rating_in: ['PG-13'],
            genre_in: ['Horror'],
            community_rating_gte: 6,
        });
    });

    it('handles community rating range (gte + lte together)', () => {
        const html =
            renderConditionRow('community_rating_gte', 5) +
            renderConditionRow('community_rating_lte', 8);
        const rows = createRows(html);
        const result = getConditionsFromDom(rows);
        expect(result).toEqual({
            community_rating_gte: 5,
            community_rating_lte: 8,
        });
    });

    it('returns empty object for no rows', () => {
        expect(getConditionsFromDom([])).toEqual({});
    });
});

describe('formatConditions', () => {
    it('formats array conditions with labels', () => {
        const json = '{"official_rating_in":["G","PG"]}';
        expect(formatConditions(json)).toBe('Rating is one of: G, PG');
    });

    it('formats number conditions with labels', () => {
        const json = '{"community_rating_gte":7}';
        expect(formatConditions(json)).toBe('Community rating \u2265: 7');
    });

    it('joins multiple conditions with a separator', () => {
        const json = '{"official_rating_in":["R"],"genre_in":["Horror"]}';
        const result = formatConditions(json);
        expect(result).toBe('Rating is one of: R · Genre includes: Horror');
    });

    it('falls back to raw string for invalid JSON', () => {
        expect(formatConditions('not json')).toBe('not json');
    });

    it('escapes HTML in output', () => {
        const json = '{"official_rating_in":["<script>"]}';
        expect(formatConditions(json)).toContain('&lt;script&gt;');
    });

    it('uses the key name for unknown condition types', () => {
        const json = '{"some_unknown_key":["val"]}';
        expect(formatConditions(json)).toBe('some_unknown_key: val');
    });

    it('handles empty conditions object', () => {
        expect(formatConditions('{}')).toBe('');
    });
});
