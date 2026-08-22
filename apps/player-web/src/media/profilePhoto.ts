export const profilePhotoLimits = { maximumInputBytes: 15 * 1024 * 1024, maximumOutputBytes: 5 * 1024 * 1024, maximumDimension: 1200 };

export class ProfilePhotoError extends Error {
  readonly kind: 'unsupported' | 'too-large' | 'processing';
  constructor(kind: 'unsupported' | 'too-large' | 'processing') { super(kind); this.kind = kind; }
}

export async function prepareProfilePhoto(file: File): Promise<Blob> {
  if (!file.type.startsWith('image/')) throw new ProfilePhotoError('unsupported');
  if (file.size > profilePhotoLimits.maximumInputBytes) throw new ProfilePhotoError('too-large');
  const url = URL.createObjectURL(file);
  try {
    const image = await loadImage(url);
    const scale = Math.min(1, profilePhotoLimits.maximumDimension / Math.max(image.naturalWidth, image.naturalHeight));
    const canvas = document.createElement('canvas');
    canvas.width = Math.max(1, Math.round(image.naturalWidth * scale));
    canvas.height = Math.max(1, Math.round(image.naturalHeight * scale));
    const context = canvas.getContext('2d');
    if (!context) throw new ProfilePhotoError('processing');
    context.drawImage(image, 0, 0, canvas.width, canvas.height);
    for (let quality = 0.86; quality >= 0.45; quality -= 0.08) {
      const blob = await canvasToBlob(canvas, quality);
      if (blob.size <= profilePhotoLimits.maximumOutputBytes) return blob;
    }
    throw new ProfilePhotoError('too-large');
  } finally { URL.revokeObjectURL(url); }
}

function loadImage(url: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => resolve(image);
    image.onerror = () => reject(new ProfilePhotoError('processing'));
    image.src = url;
  });
}

function canvasToBlob(canvas: HTMLCanvasElement, quality: number): Promise<Blob> {
  return new Promise((resolve, reject) => canvas.toBlob((blob) => blob ? resolve(blob) : reject(new ProfilePhotoError('processing')), 'image/jpeg', quality));
}
