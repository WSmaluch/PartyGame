import { useEffect, useState } from 'react';
import { createDraft, getContentPackages, type ContentPackage } from '../api/contentApi';
import { adminContentApi } from '../api/adminContentApi';
import { Link } from 'react-router-dom';

export function ContentPackages() {
  const [packages, setPackages] = useState<ContentPackage[]>([]);
  const [error, setError] = useState<string>();
  const [loading, setLoading] = useState(true);

  const load = async () => {
    setLoading(true);
    try { setPackages(await getContentPackages()); setError(undefined); }
    catch (cause) { setError(cause instanceof Error ? cause.message : 'Nie udało się pobrać pakietów.'); }
    finally { setLoading(false); }
  };
  useEffect(() => {
    let cancelled = false;
    void getContentPackages()
      .then(items => { if (!cancelled) { setPackages(items); setError(undefined); } })
      .catch((cause: unknown) => { if (!cancelled) setError(cause instanceof Error ? cause.message : 'Nie udało się pobrać pakietów.'); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, []); // initial list is intentionally loaded once

  const draft = async (item: ContentPackage) => {
    try { await createDraft(item.id); await load(); }
    catch (cause) { setError(cause instanceof Error ? cause.message : 'Nie udało się utworzyć Draftu.'); }
  };
  const mutate = async (item: ContentPackage, action: 'publish' | 'archive') => {
    try { if (action === 'publish') await adminContentApi.publish(item.id, item.concurrencyToken); else await adminContentApi.archive(item.id, item.concurrencyToken); await load(); }
    catch (cause) { setError(cause instanceof Error ? cause.message : 'Operacja pakietu nie powiodła się.'); }
  };

  return <section className="content-packages" aria-labelledby="content-packages-title">
    <div className="section-title"><h2 id="content-packages-title">Pakiety treści</h2><span>Etap 6A</span></div>
    <p>Pokój zawsze przypina konkretną opublikowaną wersję. Edycję rozpocznij przez utworzenie Draftu.</p>
    {error && <div className="error-banner" role="status">{error} <button type="button" onClick={() => void load()}>Spróbuj ponownie</button></div>}
    {loading ? <p>Wczytywanie pakietów…</p> : <div className="package-list">
      {packages.length === 0 && <p>Brak pakietów treści.</p>}
      {packages.map(item => <article key={item.id}>
        <div><strong>{item.namePl || item.key}</strong><span>v{item.version} · {item.status}</span></div>
        <small>v{item.version} · {item.categoryCount} kategorii · {item.questionCount} pytań · aktualizacja: {item.updatedAtUtc ?? '—'}</small>
        <small>Opublikowano: {item.publishedAtUtc ?? '—'} · Zarchiwizowano: {item.archivedAtUtc ?? '—'} · Typy: {Object.entries(item.questionCountByType ?? {}).map(([type, count]) => `${type}: ${count}`).join(', ') || '—'}</small>
        <Link to={`/admin/content/packages/${item.id}`}>{item.status === 'Draft' ? 'Edytuj' : 'Otwórz'}</Link>{item.status === 'Draft' && <button type="button" onClick={() => void mutate(item, 'publish')}>Opublikuj</button>}{item.status === 'Published' && <><button type="button" onClick={() => void draft(item)}>Utwórz wersję roboczą</button><button type="button" onClick={() => void mutate(item, 'archive')}>Archiwizuj</button></>}{item.status === 'Archived' && <button type="button" onClick={() => void draft(item)}>Utwórz wersję roboczą</button>}
      </article>)}
    </div>}
  </section>;
}
