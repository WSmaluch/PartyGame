import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom';
import {
  AdminContentApiError,
  adminContentApi,
  type Category,
  type ContentPackage,
  type Question,
  type QuestionListQuery,
  type QuestionType,
} from '../../api/adminContentApi';

const questionTypes: QuestionType[] = [
  'PlayerSelection',
  'TextAnswer',
  'PhotoAnswer',
  'DrawingAnswer',
];
const sortOptions = [
  ['sortOrderAsc', 'Kolejność rosnąco'],
  ['sortOrderDesc', 'Kolejność malejąco'],
  ['updatedDesc', 'Ostatnio zmienione'],
  ['updatedAsc', 'Najdawniej zmienione'],
  ['keyAsc', 'Key A–Z'],
  ['keyDesc', 'Key Z–A'],
  ['typeAsc', 'Typ A–Z'],
] as const;
const typeLabel = (type: QuestionType) =>
  ({
    PlayerSelection: 'Wybór gracza',
    TextAnswer: 'Odpowiedź tekstowa',
    PhotoAnswer: 'Odpowiedź zdjęciem',
    DrawingAnswer: 'Rysowanie',
  })[type];
const message = (cause: unknown) =>
  cause instanceof Error ? cause.message : 'Operacja nie powiodła się.';

export function QuestionListPage() {
  const { packageVersionId = '' } = useParams();
  const location = useLocation();
  const navigate = useNavigate();
  const [items, setItems] = useState<Question[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [packageInfo, setPackageInfo] = useState<ContentPackage>();
  const [packageToken, setPackageToken] = useState('');
  const loadSequence = useRef(0);
  const [totalItems, setTotalItems] = useState(0);
  const [totalPages, setTotalPages] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  const [success, setSuccess] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [reload, setReload] = useState(0);
  const [deleting, setDeleting] = useState<Question>();
  const dialogRef = useRef<HTMLDivElement>(null);
  const paramsKey = location.search.startsWith('?')
    ? location.search.slice(1)
    : location.search;
  const query = useMemo<QuestionListQuery>(() => {
    const current = new URLSearchParams(paramsKey);
    return {
      search: current.get('search') ?? undefined,
      categoryId: current.get('categoryId') ?? undefined,
      questionType:
        (current.get('questionType') as QuestionType | null) ?? undefined,
      isEnabled: current.has('isEnabled')
        ? current.get('isEnabled') === 'true'
        : undefined,
      missingTranslation:
        current.get('missingTranslation') === 'true' || undefined,
      validationErrors: current.get('validationErrors') === 'true' || undefined,
      sort: current.get('sort') ?? 'sortOrderAsc',
      page: Number(current.get('page') ?? '1'),
      pageSize: Number(current.get('pageSize') ?? '25'),
    };
  }, [paramsKey]);
  const [searchDraft, setSearchDraft] = useState(query.search ?? '');
  const readOnly =
    packageInfo?.status !== undefined && packageInfo.status !== 'Draft';
  const load = useCallback(async () => {
    const sequence = ++loadSequence.current;
    setLoading(true);
    try {
      const [page, pkg, cats] = await Promise.all([
        adminContentApi.listQuestions(packageVersionId, query),
        adminContentApi.getPackage(packageVersionId),
        adminContentApi.listCategories(packageVersionId),
      ]);
      if (sequence !== loadSequence.current) return;
      setItems(page.items);
      setTotalItems(page.totalItems);
      setTotalPages(page.totalPages);
      setPackageToken(page.packageConcurrencyToken);
      setPackageInfo(pkg);
      setCategories(cats.items);
      setError(undefined);
    } catch (cause) {
      if (sequence === loadSequence.current) setError(message(cause));
    } finally {
      if (sequence === loadSequence.current) setLoading(false);
    }
  }, [packageVersionId, query]);
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- pobranie danych jest celową synchronizacją z API po zmianie URL.
    void load();
  }, [load, reload]);
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- historia przeglądarki ma odtworzyć wpisane wyszukiwanie.
    setSearchDraft(query.search ?? '');
  }, [query.search]);
  useEffect(() => {
    if (deleting) dialogRef.current?.focus();
  }, [deleting]);
  const updateParams = (next: URLSearchParams) => {
    const nextKey = next.toString();
    if (nextKey !== paramsKey)
      navigate({ search: nextKey ? `?${nextKey}` : '' });
  };
  const change = (name: string, value?: string, resetPage = true) => {
    const next = new URLSearchParams(paramsKey);
    if (!value) next.delete(name);
    else next.set(name, value);
    if (resetPage) next.set('page', '1');
    updateParams(next);
  };
  const clear = () =>
    updateParams(
      new URLSearchParams({
        page: '1',
        pageSize: String(query.pageSize ?? 25),
        sort: 'sortOrderAsc',
      }),
    );
  const fail = (cause: unknown) => {
    setError(
      cause instanceof AdminContentApiError && cause.status === 409
        ? 'To pytanie zostało zmienione w innej sesji. Odśwież listę przed ponowną próbą.'
        : message(cause),
    );
  };
  const toggle = async (question: Question) => {
    setBusy(true);
    setError(undefined);
    try {
      const result = await adminContentApi.updateQuestion(
        packageVersionId,
        question.id,
        {
          isActive: !question.isActive,
          concurrencyToken: question.concurrencyToken,
          packageConcurrencyToken: packageToken,
        },
      );
      setItems((current) =>
        current.map((item) =>
          item.id === question.id ? result.question : item,
        ),
      );
      setPackageToken(result.packageConcurrencyToken);
      setSuccess(
        result.question.isActive
          ? 'Pytanie zostało włączone.'
          : 'Pytanie zostało wyłączone.',
      );
    } catch (cause) {
      fail(cause);
    } finally {
      setBusy(false);
    }
  };
  const duplicate = async (question: Question) => {
    setBusy(true);
    setError(undefined);
    try {
      await adminContentApi.duplicateQuestion(
        packageVersionId,
        question.id,
        question.concurrencyToken,
        packageToken,
      );
      setSuccess('Pytanie zostało zduplikowane.');
      setReload((value) => value + 1);
    } catch (cause) {
      fail(cause);
    } finally {
      setBusy(false);
    }
  };
  const remove = async () => {
    if (!deleting) return;
    setBusy(true);
    setError(undefined);
    try {
      await adminContentApi.deleteQuestion(
        packageVersionId,
        deleting.id,
        deleting.concurrencyToken,
        packageToken,
      );
      setDeleting(undefined);
      if (items.length === 1 && query.page! > 1)
        change('page', String(query.page! - 1));
      else {
        setItems((current) =>
          current.filter((item) => item.id !== deleting.id),
        );
        setTotalItems((value) => value - 1);
      }
      setSuccess('Pytanie zostało usunięte.');
    } catch (cause) {
      fail(cause);
    } finally {
      setBusy(false);
    }
  };
  const canReorder =
    !readOnly &&
    Boolean(query.categoryId) &&
    !query.search &&
    !query.questionType &&
    query.isEnabled === undefined &&
    !query.missingTranslation &&
    !query.validationErrors &&
    query.page === 1 &&
    totalItems <= (query.pageSize ?? 25) &&
    query.sort === 'sortOrderAsc';
  const move = async (index: number, delta: number) => {
    const target = index + delta;
    if (!canReorder || target < 0 || target >= items.length) return;
    const previous = items;
    const next = [...items];
    [next[index], next[target]] = [next[target], next[index]];
    setItems(next);
    setBusy(true);
    setError(undefined);
    try {
      const result = await adminContentApi.reorderQuestions(
        packageVersionId,
        packageToken,
        next.map((item, position) => ({ id: item.id, sortOrder: position })),
      );
      setItems(result.items);
      setPackageToken(result.packageConcurrencyToken);
    } catch (cause) {
      setItems(previous);
      fail(cause);
    } finally {
      setBusy(false);
    }
  };
  return (
    <section className="question-list">
      <h2>Pytania</h2>
      {loading && <p aria-live="polite">Wczytywanie pytań…</p>}
      {readOnly && (
        <p className="notice">
          Ta wersja pakietu jest tylko do odczytu. Utwórz nową wersję roboczą,
          aby edytować pytania.
        </p>
      )}
      {error && (
        <div role="alert" className="error-banner">
          {error}{' '}
          <button type="button" onClick={() => setReload((value) => value + 1)}>
            Odśwież listę
          </button>
        </div>
      )}
      {success && <p role="status">{success}</p>}
      <form
        onSubmit={(event) => {
          event.preventDefault();
          change('search', searchDraft);
        }}
        aria-label="Filtry pytań"
      >
        <label>
          Wyszukaj
          <input
            value={searchDraft}
            onChange={(event) => setSearchDraft(event.target.value)}
          />
        </label>
        <button type="submit">Szukaj</button>
        <label>
          Kategoria
          <select
            value={query.categoryId ?? ''}
            onChange={(event) => change('categoryId', event.target.value)}
          >
            <option value="">Wszystkie</option>
            {categories.map((category) => (
              <option key={category.id} value={category.id}>
                {category.namePl}
              </option>
            ))}
          </select>
        </label>
        <label>
          Typ
          <select
            value={query.questionType ?? ''}
            onChange={(event) => change('questionType', event.target.value)}
          >
            <option value="">Wszystkie</option>
            {questionTypes.map((type) => (
              <option key={type} value={type}>
                {typeLabel(type)}
              </option>
            ))}
          </select>
        </label>
        <label>
          Status
          <select
            value={query.isEnabled === undefined ? '' : String(query.isEnabled)}
            onChange={(event) => change('isEnabled', event.target.value)}
          >
            <option value="">Wszystkie</option>
            <option value="true">Aktywne</option>
            <option value="false">Wyłączone</option>
          </select>
        </label>
        <label>
          <input
            type="checkbox"
            checked={Boolean(query.missingTranslation)}
            onChange={(event) =>
              change(
                'missingTranslation',
                event.target.checked ? 'true' : undefined,
              )
            }
          />{' '}
          Brak tłumaczenia
        </label>
        <label>
          <input
            type="checkbox"
            checked={Boolean(query.validationErrors)}
            onChange={(event) =>
              change(
                'validationErrors',
                event.target.checked ? 'true' : undefined,
              )
            }
          />{' '}
          Błędy walidacji
        </label>
        <label>
          Sortowanie
          <select
            value={query.sort}
            onChange={(event) => change('sort', event.target.value)}
          >
            {sortOptions.map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </label>
        <label>
          Wyników na stronę
          <select
            value={String(query.pageSize)}
            onChange={(event) => change('pageSize', event.target.value)}
          >
            {[10, 25, 50, 100].map((size) => (
              <option key={size} value={size}>
                {size}
              </option>
            ))}
          </select>
        </label>
        <button type="button" onClick={clear}>
          Wyczyść filtry
        </button>
      </form>
      {!readOnly && <Link to={`new?${paramsKey}`}>Dodaj pytanie</Link>}
      {items.length === 0 ? (
        <p>
          {totalItems === 0 &&
          !query.search &&
          !query.categoryId &&
          !query.questionType &&
          query.isEnabled === undefined &&
          !query.missingTranslation &&
          !query.validationErrors
            ? 'Ten pakiet nie zawiera jeszcze pytań.'
            : 'Brak pytań spełniających wybrane kryteria.'}
        </p>
      ) : (
        <>
          <table>
            <thead>
              <tr>
                <th>Typ</th>
                <th>Tekst PL</th>
                <th>Tekst EN</th>
                <th>Kategoria</th>
                <th>Key</th>
                <th>MinimumPlayers</th>
                <th>Status</th>
                <th>SortOrder</th>
                <th>Data modyfikacji</th>
                <th>Akcje</th>
              </tr>
            </thead>
            <tbody>
              {items.map((question, index) => (
                <tr key={question.id}>
                  <td>{typeLabel(question.type)}</td>
                  <td title={question.textPl}>{question.textPl}</td>
                  <td title={question.textEn}>{question.textEn}</td>
                  <td>{question.categoryNamePl}</td>
                  <td>{question.key}</td>
                  <td>{question.minimumPlayers}</td>
                  <td>{question.isActive ? 'Aktywne' : 'Wyłączone'}</td>
                  <td>{question.sortOrder}</td>
                  <td>
                    {new Date(question.updatedAtUtc).toLocaleString('pl-PL')}
                  </td>
                  <td>
                    <Link to={`${question.id}?${paramsKey}`}>
                      {readOnly ? 'Otwórz' : 'Edytuj'}
                    </Link>{' '}
                    <Link to={`${question.id}?${paramsKey}`}>Podgląd</Link>
                    {!readOnly && (
                      <>
                        <button
                          disabled={busy}
                          onClick={() => void toggle(question)}
                        >
                          {question.isActive ? 'Wyłącz' : 'Włącz'}
                        </button>
                        <button
                          disabled={busy}
                          onClick={() => void duplicate(question)}
                        >
                          Duplikuj
                        </button>
                        <button
                          disabled={busy || !canReorder || index === 0}
                          onClick={() => void move(index, -1)}
                        >
                          Przenieś wyżej
                        </button>
                        <button
                          disabled={
                            busy || !canReorder || index === items.length - 1
                          }
                          onClick={() => void move(index, 1)}
                        >
                          Przenieś niżej
                        </button>
                        <button
                          disabled={busy}
                          onClick={() => setDeleting(question)}
                        >
                          Usuń
                        </button>
                      </>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <nav aria-label="Paginacja pytań">
            <button
              disabled={query.page === 1}
              onClick={() => change('page', String(query.page! - 1), false)}
            >
              Poprzednia
            </button>
            <span>
              Strona {query.page} z {totalPages}
            </span>
            <span>{totalItems} wyników</span>
            <button
              disabled={totalPages === 0 || query.page! >= totalPages}
              onClick={() => change('page', String(query.page! + 1), false)}
            >
              Następna
            </button>
          </nav>
        </>
      )}
      {deleting && (
        <div
          ref={dialogRef}
          role="dialog"
          aria-modal="true"
          aria-labelledby="delete-question-title"
          tabIndex={-1}
        >
          <h3 id="delete-question-title">Usunąć to pytanie?</h3>
          <p>
            {typeLabel(deleting.type)} · {deleting.textPl.slice(0, 120)} ·{' '}
            {deleting.categoryNamePl} · {deleting.key}
          </p>
          <button disabled={busy} onClick={() => void remove()}>
            Usuń
          </button>
          <button disabled={busy} onClick={() => setDeleting(undefined)}>
            Anuluj
          </button>
        </div>
      )}
    </section>
  );
}
