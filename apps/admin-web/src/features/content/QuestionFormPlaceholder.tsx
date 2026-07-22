import { Link, useLocation, useParams } from 'react-router-dom';

export function QuestionFormPlaceholder({ title }: { title: string }) {
  const { packageVersionId, questionId } = useParams(); const { search } = useLocation();
  return <section><h2>{title}</h2><p>{questionId ? `Pytanie ${questionId} zostanie otwarte w edytorze 6A.2B.` : 'Pełny formularz tworzenia pytania zostanie dostarczony w 6A.2B.'}</p><Link to={`/admin/content/packages/${packageVersionId}/questions${search}`}>Wróć do listy pytań</Link></section>;
}
