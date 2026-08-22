import { afterEach, describe, expect, it, vi } from 'vitest';
import { uploadDrawingAnswer, uploadPhotoAnswer } from './playerApi';

const session = { roomCode: 'AB12', playerId: 'player-1', reconnectToken: 'reconnect-token', nickname: 'Wojtek' };

describe('media answer API', () => {
  afterEach(() => vi.unstubAllGlobals());

  it.each([
    ['photo', uploadPhotoAnswer, 'photo-answers', 'photo', 'photo.jpg'],
    ['drawing', uploadDrawingAnswer, 'drawing-answers', 'drawing', 'drawing.png'],
  ] as const)('uses the existing %s multipart endpoint without a token in the URL', async (_kind, upload, endpoint, field, filename) => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ playerPrivateGameState: {}, roomSnapshot: {} }), { status: 200, headers: { 'Content-Type': 'application/json' } })); vi.stubGlobal('fetch', fetchMock);
    await upload(session, 'question-1', new Blob(['media'], { type: 'image/png' }), 'submission-1');
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe('http://test-api.local/api/rooms/AB12/questions/question-1/' + endpoint); expect(url).not.toContain(session.reconnectToken);
    const form = init.body as FormData; expect(form.get('playerId')).toBe(session.playerId); expect(form.get('reconnectToken')).toBe(session.reconnectToken); expect(form.get('clientSubmissionId')).toBe('submission-1');
    const media = form.get(field) as File; expect(media.name).toBe(filename);
  });
});
