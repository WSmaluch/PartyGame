import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { adminContentApi, type ContentPackage } from '../../api/adminContentApi';

export function PackageOverviewPage() {
  const { packageVersionId } = useParams();
  const [item, setItem] = useState<ContentPackage>(); const [error, setError] = useState<string>();
  useEffect(() => { if (!packageVersionId) return; void adminContentApi.getPackage(packageVersionId).then(setItem).catch((e: unknown) => setError(e instanceof Error ? e.message : 'Nie udało się pobrać pakietu.')); }, [packageVersionId]);
  if (error) return <div className="error-banner" role="alert">{error}</div>;
  if (!item) return <p>Wczytywanie pakietu…</p>;
  const readOnly = item.status !== 'Draft';
  return <section><header><div><p className="eyebrow">{item.status} · v{item.version}</p><h2>{item.namePl}</h2></div><Link to="/admin/content">Wróć do pakietów</Link></header>
    <p>{item.descriptionPl || 'Brak opisu.'}</p><dl className="package-details"><dt>Rodzina</dt><dd>{item.logicalPackageId}</dd><dt>Kategorie</dt><dd>{item.categoryCount}</dd><dt>Pytania</dt><dd>{item.questionCount}</dd></dl>
    <div className="content-actions"><Link to="categories">Kategorie</Link><Link to="questions">Pytania</Link>{readOnly && <span>Ta wersja jest tylko do odczytu.</span>}</div>
  </section>;
}
