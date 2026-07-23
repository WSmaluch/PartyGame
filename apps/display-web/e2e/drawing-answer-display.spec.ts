import { test, expect } from '@playwright/test';
import * as signalR from '@microsoft/signalr';
import crypto from 'crypto';
import { deflateSync } from 'node:zlib';

const crcTable = Uint32Array.from({ length: 256 }, (_, index) => {
  let value = index;
  for (let bit = 0; bit < 8; bit++)
    value = value & 1 ? 0xedb88320 ^ (value >>> 1) : value >>> 1;
  return value >>> 0;
});
const crc32 = (value: Buffer) => {
  let crc = 0xffffffff;
  for (const byte of value) crc = crcTable[(crc ^ byte) & 0xff] ^ (crc >>> 8);
  return (crc ^ 0xffffffff) >>> 0;
};
const drawingPng = () => {
  const width = 320;
  const height = 320;
  const pixels = Buffer.alloc((width * 4 + 1) * height, 255);
  for (let y = 0; y < height; y++) {
    const row = y * (width * 4 + 1);
    pixels[row] = 0;
    for (let x = 156; x < 164; x++) {
      const offset = row + 1 + x * 4;
      pixels[offset] = 20;
      pixels[offset + 1] = 80;
      pixels[offset + 2] = 220;
    }
  }
  const chunk = (type: string, data: Buffer) => {
    const result = Buffer.alloc(data.length + 12);
    result.writeUInt32BE(data.length, 0);
    result.write(type, 4);
    data.copy(result, 8);
    result.writeUInt32BE(
      crc32(Buffer.concat([Buffer.from(type), data])),
      data.length + 8,
    );
    return result;
  };
  const header = Buffer.alloc(13);
  header.writeUInt32BE(width, 0);
  header.writeUInt32BE(height, 4);
  header[8] = 8;
  header[9] = 6;
  return Buffer.concat([
    Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
    chunk('IHDR', header),
    chunk('IDAT', deflateSync(pixels)),
    chunk('IEND', Buffer.alloc(0)),
  ]);
};

test.describe('Display DrawingAnswer E2E', () => {
  type TestPlayer = { id: string; token: string; name: string };

  let roomCode = '';
  const players: TestPlayer[] = [];
  let apiUrl = process.env.VITE_URL?.replace(/:[0-9]+$/, ':5050');
  if (process.env.PLAYWRIGHT_API_URL) apiUrl = process.env.PLAYWRIGHT_API_URL;
  let currentQuestionId = '';
  const connections: signalR.HubConnection[] = [];

  test.beforeAll(async ({ request }) => {
    const packagesResponse = await request.get(
      `${apiUrl}/api/admin/content-packages`,
    );
    expect(packagesResponse.ok()).toBeTruthy();
    const packages: Array<{
      id: string;
      status: string;
      questionCountByType: Record<string, number>;
    }> = await packagesResponse.json();
    const publishedDrawingPackage = packages.find(
      (item) =>
        item.status === 'Published' &&
        item.questionCountByType.DrawingAnswer > 0,
    );
    expect(publishedDrawingPackage).toBeTruthy();

    // 1. Create room from an exact published package version.
    const roomRes = await request.post(`${apiUrl}/api/rooms`, {
      data: {
        nickname: 'Host',
        contentPackageVersionId: publishedDrawingPackage!.id,
        enabledQuestionTypes: ['DrawingAnswer'],
        settings: {
          roundCount: 1,
          questionsPerRound: 4,
          drawingSeconds: 120,
          votingSeconds: 60,
        },
      },
    });
    expect(roomRes.ok(), await roomRes.text()).toBeTruthy();
    const roomData = await roomRes.json();
    roomCode = roomData.roomCode;
    players.push({
      id: roomData.playerId,
      token: roomData.reconnectToken,
      name: 'Host',
    });

    // 2. Join 2 other players
    for (const name of ['Wojtek', 'Kasia']) {
      const pRes = await request.post(
        `${apiUrl}/api/rooms/${roomCode}/players`,
        { data: { nickname: name } },
      );
      const pData = await pRes.json();
      players.push({ id: pData.playerId, token: pData.reconnectToken, name });
    }

    for (const player of players) {
      const profile = await request.post(
        `${apiUrl}/api/rooms/${roomCode}/players/${player.id}/profile-photo`,
        {
          headers: { 'X-Player-Token': player.token },
          multipart: {
            file: {
              name: 'profile.png',
              mimeType: 'image/png',
              buffer: drawingPng(),
            },
          },
        },
      );
      expect(profile.ok()).toBeTruthy();
    }

    // 3. Connect players; readiness is set only after the real Display UI attaches.
    for (const p of players) {
      const conn = new signalR.HubConnectionBuilder()
        .withUrl(`${apiUrl}/hubs/game`)
        .build();
      await conn.start();
      await conn.invoke('AttachPlayer', roomCode, p.id, p.token);
      connections.push(conn);
    }
  });

  test.afterAll(async () => {
    for (const c of connections) await c.stop();
  });

  test('runs a published DrawingAnswer game through collecting, reveal, voting and results', async ({
    page,
    request,
  }) => {
    await page.goto('/display');
    await page.getByLabel('Kod pokoju').fill(roomCode);
    await page.getByRole('button', { name: 'Połącz ekran' }).click();
    await expect(page.getByText(roomCode)).toBeVisible();
    for (let index = 0; index < players.length; index++) {
      await connections[index].invoke(
        'SetReady',
        roomCode,
        players[index].id,
        players[index].token,
        true,
      );
    }

    // Wait for the game to start and enter CollectingDrawingAnswers
    await expect(page.getByTestId('drawing-collecting')).toBeVisible({
      timeout: 15000,
    });

    // Check DOM for Collecting (should not show any drawings)
    await expect(page.getByTestId('drawing-answer-image')).toHaveCount(0);
    await expect(page.getByTestId('drawing-author')).toHaveCount(0);

    const roomSnapshot = await (
      await request.get(`${apiUrl}/api/rooms/${roomCode}`)
    ).json();
    currentQuestionId =
      roomSnapshot.game.drawingAnswerResults.questionInstanceId;

    // Helper to upload drawing
    const uploadDrawing = async (
      player: TestPlayer,
    ): Promise<{ drawingAnswerId: string }> => {
      const pngBuffer = drawingPng();
      const formData = new FormData();
      formData.append('playerId', player.id);
      formData.append('reconnectToken', player.token);
      formData.append('clientSubmissionId', crypto.randomUUID());
      formData.append(
        'drawing',
        new Blob([pngBuffer], { type: 'image/png' }),
        'drawing.png',
      );

      const uploadRes = await fetch(
        `${apiUrl}/api/rooms/${roomCode}/questions/${currentQuestionId}/drawing-answers`,
        {
          method: 'POST',
          body: formData,
        },
      );
      expect(uploadRes.ok).toBeTruthy();
      return await uploadRes.json();
    };

    // Submitting drawing for Player 1, 2, 3
    const p1Answer = await uploadDrawing(players[0]);
    await uploadDrawing(players[1]);
    await uploadDrawing(players[2]);

    // Wait for Reveal
    await expect(page.getByTestId('revealing-drawing-answers')).toBeVisible({
      timeout: 15000,
    });

    // Reveal check: should see 3 drawings, but no authors
    await expect(page.getByTestId('drawing-answer-image')).toHaveCount(3);
    await expect(page.getByTestId('drawing-author')).toHaveCount(0);

    // Refresh during reveal
    await page.reload();
    await expect(page.getByTestId('revealing-drawing-answers')).toBeVisible();
    await expect(page.getByTestId('drawing-answer-image')).toHaveCount(3);
    await expect(page.getByTestId('drawing-author')).toHaveCount(0);

    // Wait for Voting
    await expect(
      page.getByTestId('collecting-drawing-answer-votes'),
    ).toBeVisible({ timeout: 15000 });
    // Voting check: still anonymous
    await expect(page.getByTestId('drawing-author')).toHaveCount(0);

    // Reconnect during voting.
    await page.reload();
    await expect(
      page.getByTestId('collecting-drawing-answer-votes'),
    ).toBeVisible();

    // Submit votes
    for (let i = 0; i < players.length; i++) {
      // Everyone votes for P1
      await connections[i].invoke(
        'SubmitDrawingAnswerVote',
        roomCode,
        players[i].id,
        players[i].token,
        currentQuestionId,
        p1Answer.drawingAnswerId,
      );
    }

    // Wait for Results
    await expect(
      page.getByTestId('showing-drawing-answer-results'),
    ).toBeVisible({ timeout: 15000 });
    // Should see authors and points now
    await expect(page.getByTestId('drawing-author')).toHaveCount(3);
    await expect(page.getByTestId('drawing-votes')).toHaveCount(3);

  });
});
