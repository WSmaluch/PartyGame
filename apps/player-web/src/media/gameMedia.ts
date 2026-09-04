export class GameMediaError extends Error {
  readonly kind: 'unsupported' | 'too-large' | 'processing' | 'empty';
  constructor(kind: 'unsupported' | 'too-large' | 'processing' | 'empty') { super(kind); this.kind = kind; }
}

const maximumInputBytes = 15 * 1024 * 1024;
const maximumOutputBytes = 5 * 1024 * 1024;

export async function preparePhotoAnswer(file: File): Promise<Blob> {
  if (!file.type.startsWith('image/')) throw new GameMediaError('unsupported');
  if (file.size > maximumInputBytes) throw new GameMediaError('too-large');
  const startedAt = performance.now();
  const source = URL.createObjectURL(file);
  try {
    const image = await loadImage(source);
    const scale = Math.min(1, 2048 / Math.max(image.naturalWidth, image.naturalHeight));
    const canvas = document.createElement('canvas'); canvas.width = Math.max(1, Math.floor(image.naturalWidth * scale)); canvas.height = Math.max(1, Math.floor(image.naturalHeight * scale));
    const context = canvas.getContext('2d'); if (!context) throw new GameMediaError('processing');
    context.fillStyle = '#000'; context.fillRect(0, 0, canvas.width, canvas.height); context.drawImage(image, 0, 0, canvas.width, canvas.height);
    for (let quality = 0.86; quality >= 0.62; quality -= 0.06) {
      const blob = await toBlob(canvas, 'image/jpeg', quality);
      if (blob.size <= maximumOutputBytes) {
        // Deliberately metadata-only: dimensions, bytes and duration help
        // diagnose slow mobile captures without emitting image content.
        console.info('[PartyGame photo-preparation]', JSON.stringify({
          sourceWidth: image.naturalWidth,
          sourceHeight: image.naturalHeight,
          sourceBytes: file.size,
          encodedBytes: blob.size,
          durationMs: Math.round(performance.now() - startedAt),
        }));
        return blob;
      }
    }
    throw new GameMediaError('too-large');
  } catch (error) { if (error instanceof GameMediaError) throw error; throw new GameMediaError('processing'); } finally { URL.revokeObjectURL(source); }
}

export async function drawingPng(canvas: HTMLCanvasElement, hasInk: boolean): Promise<Blob> {
  if (!hasInk) throw new GameMediaError('empty');
  const blob = await toBlob(canvas, 'image/png');
  if (blob.size > maximumOutputBytes) throw new GameMediaError('too-large');
  return blob;
}

function loadImage(source: string): Promise<HTMLImageElement> { return new Promise((resolve, reject) => { const image = new Image(); image.onload = () => resolve(image); image.onerror = reject; image.src = source; }); }
function toBlob(canvas: HTMLCanvasElement, type: string, quality?: number): Promise<Blob> { return new Promise((resolve, reject) => canvas.toBlob((blob) => blob ? resolve(blob) : reject(new GameMediaError('processing')), type, quality)); }
