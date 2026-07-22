# Przykład uploadu

```bash
curl -F playerId=... -F reconnectToken=... -F clientSubmissionId=... \
  -F photo=@fixture.jpg\;type=image/jpeg \
  http://localhost:5050/api/rooms/ABCD/questions/.../photo-answers
```
