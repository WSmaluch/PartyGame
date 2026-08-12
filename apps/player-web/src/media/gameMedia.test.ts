import { describe, expect, it } from 'vitest';
import { drawingPng, GameMediaError, preparePhotoAnswer } from './gameMedia';

describe('game media preparation', () => {
  it('rejects unsupported and oversized photo inputs before image processing', async () => {
    await expect(preparePhotoAnswer(new File(['text'], 'answer.txt', { type: 'text/plain' }))).rejects.toMatchObject({ kind: 'unsupported' } satisfies Partial<GameMediaError>);
    await expect(preparePhotoAnswer(new File([new Uint8Array(15 * 1024 * 1024 + 1)], 'large.jpg', { type: 'image/jpeg' }))).rejects.toMatchObject({ kind: 'too-large' } satisfies Partial<GameMediaError>);
  });

  it('rejects empty drawing export without serializing a canvas', async () => {
    await expect(drawingPng(document.createElement('canvas'), false)).rejects.toMatchObject({ kind: 'empty' } satisfies Partial<GameMediaError>);
  });
});
