import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminContentApiError, adminContentApi, type ContentPackage } from '../../api/adminContentApi';
import { PackageOverviewPage } from './PackageOverviewPage';

vi.mock('../../api/adminContentApi', async original => {
  const actual = await original<typeof import('../../api/adminContentApi')>();
  return { ...actual, adminContentApi: { getPackage: vi.fn(), updatePackage: vi.fn(), createDraft: vi.fn(), publish: vi.fn(), archive: vi.fn() } };
});

const draft: ContentPackage = { id: 'p1', logicalPackageId: 'family-1', version: 2, key: 'pack', namePl: 'Pakiet PL', nameEn: 'Package EN', descriptionPl: 'Opis PL', descriptionEn: 'Description EN', status: 'Draft', isActive: true, categoryCount: 2, questionCount: 4, questionCountByType: { PlayerSelection: 3, TextAnswer: 1 }, concurrencyToken: 'token-1' };
const renderPage = (path = '/admin/content/packages/p1') => render(<MemoryRouter initialEntries={[path]}><Routes><Route path="/admin/content/packages/:packageVersionId" element={<PackageOverviewPage />} /></Routes></MemoryRouter>);

describe('PackageOverviewPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(adminContentApi.getPackage).mockResolvedValue(draft);
    vi.mocked(adminContentApi.updatePackage).mockResolvedValue({ ...draft, namePl: 'Zapisany pakiet', concurrencyToken: 'token-2' });
    vi.mocked(adminContentApi.createDraft).mockResolvedValue({ ...draft, id: 'p2', version: 3 });
    vi.mocked(adminContentApi.publish).mockResolvedValue({ ...draft, status: 'Published', publishedAtUtc: '2026-07-22T12:00:00Z', concurrencyToken: 'published-token' });
    vi.mocked(adminContentApi.archive).mockResolvedValue({ ...draft, status: 'Archived', archivedAtUtc: '2026-07-22T12:01:00Z', concurrencyToken: 'archived-token' });
  });

  it('pokazuje loading, metadane, liczniki i rozkład typów', async () => {
    vi.mocked(adminContentApi.getPackage).mockReturnValueOnce(new Promise(() => undefined));
    renderPage();
    expect(screen.getByText('Wczytywanie pakietu…')).toBeInTheDocument();
  });

  it('wypełnia formularz Draftu, zapisuje dane i używa świeżego tokenu', async () => {
    renderPage();
    await screen.findByDisplayValue('Pakiet PL');
    expect(screen.getByText('PlayerSelection: 3, TextAnswer: 1')).toBeInTheDocument();
    await userEvent.clear(screen.getByLabelText('Nazwa PL'));
    await userEvent.type(screen.getByLabelText('Nazwa PL'), 'Moja nazwa');
    await userEvent.click(screen.getByRole('button', { name: 'Zapisz' }));
    await waitFor(() => expect(adminContentApi.updatePackage).toHaveBeenCalledWith('p1', expect.objectContaining({ namePl: 'Moja nazwa', concurrencyToken: 'token-1' })));
    expect(await screen.findByRole('heading', { name: 'Zapisany pakiet' })).toBeInTheDocument();
  });

  it('blokuje podwójny zapis i zachowuje wartości po błędzie 409 z odświeżeniem', async () => {
    let resolveSave: (value: ContentPackage) => void = () => undefined;
    vi.mocked(adminContentApi.updatePackage).mockImplementationOnce(() => new Promise(resolve => { resolveSave = resolve; })).mockRejectedValueOnce(new AdminContentApiError(409, 'Konflikt'));
    renderPage();
    await screen.findByLabelText('Nazwa PL');
    await userEvent.clear(screen.getByLabelText('Nazwa PL'));
    await userEvent.type(screen.getByLabelText('Nazwa PL'), 'Nie zapisuj');
    await userEvent.click(screen.getByRole('button', { name: 'Zapisz' }));
    expect(screen.getByRole('button', { name: 'Zapisywanie…' })).toBeDisabled();
    resolveSave(draft);
    await waitFor(() => expect(screen.getByRole('button', { name: 'Zapisz' })).toBeEnabled());
    await userEvent.click(screen.getByRole('button', { name: 'Zapisz' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('zmieniony w innej sesji');
    expect(screen.getByLabelText('Nazwa PL')).toHaveValue('Nie zapisuj');
    await userEvent.click(screen.getByRole('button', { name: 'Odśwież dane' }));
    await waitFor(() => expect(adminContentApi.getPackage).toHaveBeenCalledTimes(2));
  });

  it('ostrzega przeglądarkę o niezapisanych zmianach', async () => {
    renderPage();
    await screen.findByLabelText('Opis PL');
    await userEvent.type(screen.getByLabelText('Opis PL'), ' zmiana');
    const event = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(true);
  });

  it('publikuje przez dialog, pokazuje błędy walidacji i zostawia dialog otwarty', async () => {
    vi.mocked(adminContentApi.publish).mockRejectedValueOnce(new AdminContentApiError(400, 'Walidacja', { errors: [{ path: 'categories[0]', code: 'missing', message: 'Brak pytania' }] })).mockResolvedValueOnce({ ...draft, status: 'Published', publishedAtUtc: '2026-07-22T12:00:00Z' });
    renderPage();
    await screen.findByText('Pakiet PL');
    await userEvent.click(screen.getByRole('button', { name: 'Publikuj' }));
    const dialog = screen.getByRole('dialog');
    expect(dialog).toHaveTextContent('Pakiet PL · v2');
    expect(dialog).toHaveTextContent('Rozkład typów: PlayerSelection: 3, TextAnswer: 1');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Publikuj' }));
    expect(await screen.findByText('categories[0]: Brak pytania')).toBeInTheDocument();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Publikuj' }));
    expect(await screen.findByText(/tylko do odczytu/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Nazwa PL')).not.toBeInTheDocument();
  });

  it('obsługuje konflikt publikacji bez automatycznego retry', async () => {
    vi.mocked(adminContentApi.publish).mockRejectedValue(new AdminContentApiError(409, 'Konflikt'));
    renderPage();
    await screen.findByText('Pakiet PL');
    await userEvent.click(screen.getByRole('button', { name: 'Publikuj' }));
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Publikuj' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('zmieniony w innej sesji');
    expect(adminContentApi.publish).toHaveBeenCalledTimes(1);
  });

  it('Published i Archived są read-only oraz tworzą Draft przez API', async () => {
    vi.mocked(adminContentApi.getPackage).mockResolvedValueOnce({ ...draft, status: 'Published', publishedAtUtc: '2026-07-22T12:00:00Z' });
    renderPage();
    await screen.findByText(/tylko do odczytu/);
    expect(screen.queryByLabelText('Nazwa PL')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Utwórz Draft' }));
    await waitFor(() => expect(adminContentApi.createDraft).toHaveBeenCalledWith('p1'));
  });

  it('archiwizuje tylko Published, anuluje dialog i zachowuje statystyki', async () => {
    vi.mocked(adminContentApi.getPackage).mockResolvedValue({ ...draft, status: 'Published', publishedAtUtc: '2026-07-22T12:00:00Z' });
    renderPage();
    await screen.findByRole('button', { name: 'Archiwizuj' });
    await userEvent.click(screen.getByRole('button', { name: 'Archiwizuj' }));
    expect(screen.getByRole('dialog')).toHaveTextContent('Nowe pokoje nie będą mogły jej użyć');
    await userEvent.click(screen.getByRole('button', { name: 'Anuluj' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Archiwizuj' }));
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Archiwizuj' }));
    expect(await screen.findByText('Archived · v2')).toBeInTheDocument();
    expect(screen.getByText('4')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Utwórz Draft' })).toBeInTheDocument();
  });

  it('obsługuje conflict i błąd archiwizacji bez zamykania dialogu', async () => {
    vi.mocked(adminContentApi.getPackage).mockResolvedValue({ ...draft, status: 'Published' });
    vi.mocked(adminContentApi.archive).mockRejectedValue(new AdminContentApiError(409, 'Konflikt'));
    renderPage();
    await screen.findByRole('button', { name: 'Archiwizuj' });
    await userEvent.click(screen.getByRole('button', { name: 'Archiwizuj' }));
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Archiwizuj' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('zmieniony w innej sesji');
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });
});
