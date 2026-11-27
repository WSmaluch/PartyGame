import { afterEach, describe, expect, it, vi } from 'vitest';
import { createRoom, fetchPackages, joinRoom, uploadDrawingAnswer, uploadPhotoAnswer } from './playerApi';

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

describe('room creation API', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('uses the public create-room and package endpoints without an operator credential', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ roomCode: 'AB12' }), { status: 201, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify([]), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    vi.stubGlobal('fetch', fetchMock);
    await createRoom({ nickname: 'Host', settings: { roundCount: 1, questionsPerRound: 4, playerSelectionSeconds: 5, textAnswerSeconds: 5, votingSeconds: 5, photoSeconds: 10, drawingSeconds: 30, resultPresentationSeconds: 3, finalRoundEnabled: false, finalDrawingPasses: 1 }, selectedPackageKeys: ['starter'], enabledQuestionTypes: ['PlayerSelection'] });
    await fetchPackages();
    const [createUrl, createInit] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(createUrl).toBe('http://test-api.local/api/rooms'); expect(createInit.method).toBe('POST'); expect(createInit.headers).not.toHaveProperty('Authorization'); expect(JSON.parse(createInit.body as string)).toMatchObject({ nickname: 'Host', selectedPackageKeys: ['starter'] });
    expect(fetchMock.mock.calls[1][0]).toBe('http://test-api.local/api/content/packages');
  });
});

describe('join conflicts', () => {
  afterEach(() => vi.unstubAllGlobals());

  it.each([
    ['This nickname is already in use in the room.', 'nickname-taken'],
    ['The room already contains the maximum number of players.', 'room-full'],
    ['Players cannot join after the room has started.', 'started'],
  ])('maps 409 "%s" to %s', async (title, kind) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ status: 409, title }), { status: 409, headers: { 'Content-Type': 'application/problem+json' } })));
    await expect(joinRoom('AB12', 'Wojtek')).rejects.toMatchObject({ kind });
  });

  it('falls back to started when the 409 body is unreadable', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('not json', { status: 409 })));
    await expect(joinRoom('AB12', 'Wojtek')).rejects.toMatchObject({ kind: 'started' });
  });
});
