// Test wrapper: executes rule-builder.js in a simulated window environment
// and re-exports the functions for vitest imports.
import { JSDOM } from 'jsdom';
import { readFileSync } from 'fs';
import { fileURLToPath } from 'url';
import { dirname, resolve } from 'path';

const __dirname = dirname(fileURLToPath(import.meta.url));
const code = readFileSync(resolve(__dirname, '../Jellyfin.Plugin.JellyReview/Web/Scripts/rule-builder.js'), 'utf-8');

const dom = new JSDOM('', { runScripts: 'dangerously' });
const scriptEl = dom.window.document.createElement('script');
scriptEl.textContent = code;
dom.window.document.head.appendChild(scriptEl);

const rb = dom.window.JellyReviewRuleBuilder;

export const {
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
} = rb;
