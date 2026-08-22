export function playerJoinUrl(roomCode: string, publicAppUrl: string, origin: string): string {
  const configuredUrl = new URL(publicAppUrl, origin);
  const playerUrl = new URL('/play/', configuredUrl.origin);
  playerUrl.search = new URLSearchParams({ room: roomCode }).toString();
  return playerUrl.toString();
}
