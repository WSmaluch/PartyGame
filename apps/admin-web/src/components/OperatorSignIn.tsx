import { useState } from 'react';
import type { FormEvent } from 'react';
import { adminContentApi, AdminContentApiError } from '../api/adminContentApi';
import { setOperatorToken } from '../api/operatorSession';

export function OperatorSignIn() {
  const [token, setToken] = useState('');
  const [error, setError] = useState<string>();
  const [pending, setPending] = useState(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setPending(true);
    setError(undefined);
    setOperatorToken(token);
    try {
      await adminContentApi.listPackages();
    } catch (requestError) {
      setOperatorToken('');
      setError(requestError instanceof AdminContentApiError && requestError.status === 401
        ? 'Token operatora jest nieprawidłowy lub wygasł.'
        : 'Nie można teraz zweryfikować dostępu operatora.');
    } finally {
      setPending(false);
    }
  }

  return (
    <main className="admin-shell" aria-labelledby="operator-sign-in-title">
      <section className="status-panel">
        <h1 id="operator-sign-in-title">PartyGame Admin</h1>
        <p>Podaj token operatora, aby otworzyć panel administracyjny.</p>
        <form onSubmit={(event) => void submit(event)}>
          <label htmlFor="operator-token">Token operatora</label>
          <input
            id="operator-token"
            type="password"
            autoComplete="current-password"
            value={token}
            onChange={(event) => setToken(event.target.value)}
            required
          />
          <button type="submit" disabled={pending}>{pending ? 'Sprawdzanie…' : 'Otwórz panel'}</button>
        </form>
        {error && <p role="alert" className="error-banner">{error}</p>}
      </section>
    </main>
  );
}
