import { describe, it, expect, vi, beforeEach } from 'vitest';
import { Api } from '../Jellyfin.Plugin.JellyReview/Web/Scripts/api.js';

describe('Api profile-aware requests', () => {
    beforeEach(() => {
        global.fetch = vi.fn(async () => ({
            ok: true,
            status: 200,
            json: async () => ({}),
        }));
    });

    it('sends profile filters for counts', async () => {
        await Api.getCounts({ viewerProfileId: 'profile-1' });

        expect(global.fetch).toHaveBeenCalledWith(
            '/JellyReview/Media/counts?viewerProfileId=profile-1',
            expect.objectContaining({ method: 'GET' }),
        );
    });

    it('sends all-profiles review actions', async () => {
        await Api.approve('media-1', { allProfiles: true });

        expect(global.fetch).toHaveBeenCalledWith(
            '/JellyReview/Reviews/media-1/approve',
            expect.objectContaining({
                method: 'POST',
                body: JSON.stringify({ allProfiles: true }),
            }),
        );
    });

    it('sends many-to-many profile assignments for rules', async () => {
        await Api.createRule({
            name: 'Older kid rule',
            action: 'auto_approve',
            priority: 10,
            conditionsJson: '{"official_rating_in":["PG"]}',
            enabled: true,
            viewerProfileIds: ['profile-1', 'profile-2'],
        });

        const [, opts] = global.fetch.mock.calls[0];
        expect(JSON.parse(opts.body).viewerProfileIds).toEqual(['profile-1', 'profile-2']);
    });
});
