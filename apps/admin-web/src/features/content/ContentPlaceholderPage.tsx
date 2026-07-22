import { Link, useParams } from 'react-router-dom';
export function ContentPlaceholderPage({ title }: { title: string }) { const { packageVersionId } = useParams(); return <section><h2>{title}</h2><p>Widok korzysta z wersji pakietu {packageVersionId}. Wróć do <Link to="..">podsumowania</Link>.</p></section>; }
