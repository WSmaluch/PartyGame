import { useEffect, useState } from 'react';
import { toDataURL } from 'qrcode';
import { apiConfig } from '../api/apiConfig';
import { playerJoinUrl } from './playerJoinUrl';

export function PlayerJoinQrCode({ roomCode }: { roomCode: string }) {
  const joinUrl = playerJoinUrl(roomCode, apiConfig.publicAppUrl, window.location.origin);
  const [imageUrl, setImageUrl] = useState<string>();

  useEffect(() => {
    let current = true;
    void toDataURL(joinUrl, {
      errorCorrectionLevel: 'M',
      margin: 1,
      width: 260,
      color: { dark: '#180d3b', light: '#ffffff' },
    }).then((url) => { if (current) setImageUrl(url); }).catch(() => { if (current) setImageUrl(undefined); });
    return () => { current = false; };
  }, [joinUrl]);

  return <aside className="player-join-qr" aria-label="Dołącz do gry przez kod QR">
    {imageUrl ? <img src={imageUrl} alt={`Kod QR do pokoju ${roomCode}`} /> : <div className="player-join-qr__loading" role="status">Przygotowywanie kodu QR…</div>}
    <div><strong>Zeskanuj, aby dołączyć</strong><a href={joinUrl}>Otwórz na tym urządzeniu</a></div>
  </aside>;
}
