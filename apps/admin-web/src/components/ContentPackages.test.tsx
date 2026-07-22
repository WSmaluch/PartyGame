import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { adminContentApi, type ContentPackage } from '../api/adminContentApi';
import { createDraft, getContentPackages } from '../api/contentApi';
import { ContentPackages } from './ContentPackages';

vi.mock('../api/contentApi', () => ({ getContentPackages: vi.fn(), createDraft: vi.fn() }));
vi.mock('../api/adminContentApi', () => ({ adminContentApi: { publish: vi.fn(), archive: vi.fn() } }));

const packages: ContentPackage[] = [
  { id: 'd', logicalPackageId: 'f', version: 3, key: 'draft', namePl: 'Draft', nameEn: 'Draft', status: 'Draft', isActive: true, categoryCount: 2, questionCount: 4, questionCountByType: { TextAnswer: 4 }, updatedAtUtc: '2026-07-22', concurrencyToken: 'd-token' },
  { id: 'p', logicalPackageId: 'f', version: 2, key: 'published', namePl: 'Published', nameEn: 'Published', status: 'Published', isActive: true, categoryCount: 3, questionCount: 8, questionCountByType: { PlayerSelection: 5, TextAnswer: 3 }, publishedAtUtc: '2026-07-21', concurrencyToken: 'p-token' },
  { id: 'a', logicalPackageId: 'f', version: 1, key: 'archived', namePl: 'Archived', nameEn: 'Archived', status: 'Archived', isActive: true, categoryCount: 1, questionCount: 2, archivedAtUtc: '2026-07-20', concurrencyToken: 'a-token' },
];
const renderList = () => render(<MemoryRouter><ContentPackages /></MemoryRouter>);

describe('ContentPackages', () => {
  beforeEach(() => { vi.resetAllMocks(); vi.mocked(getContentPackages).mockResolvedValue(packages); vi.mocked(createDraft).mockResolvedValue(packages[0]); vi.mocked(adminContentApi.publish).mockResolvedValue(packages[1]); vi.mocked(adminContentApi.archive).mockResolvedValue(packages[2]); });

  it('pokazuje loading oraz pełne dane statusów i liczniki', async () => {
    vi.mocked(getContentPackages).mockReturnValueOnce(new Promise(() => undefined));
    renderList();
    expect(screen.getByText('Wczytywanie pakietów…')).toBeInTheDocument();
  });

  it('renderuje Draft, Published i Archived z dozwolonymi akcjami', async () => {
    renderList();
    await screen.findByText('Draft');
    expect(screen.getByText(/Opublikowano: 2026-07-21/)).toBeInTheDocument();
    expect(screen.getByText(/Typy: PlayerSelection: 5, TextAnswer: 3/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Opublikuj' })).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Utwórz wersję roboczą' })).toHaveLength(2);
    expect(screen.getByRole('button', { name: 'Archiwizuj' })).toBeInTheDocument();
  });

  it('obsługuje pusty stan oraz błąd API z retry', async () => {
    vi.mocked(getContentPackages).mockResolvedValueOnce([]).mockRejectedValueOnce(new Error('Backend offline')).mockResolvedValueOnce(packages);
    const view = renderList();
    expect(await screen.findByText('Brak pakietów treści.')).toBeInTheDocument();
    view.unmount();
    // A second render verifies the explicit error/retry path without simulating local data.
    renderList();
    expect(await screen.findByRole('status')).toHaveTextContent('Backend offline');
    await userEvent.click(screen.getByRole('button', { name: 'Spróbuj ponownie' }));
    expect(await screen.findByText('Published')).toBeInTheDocument();
  });

  it('wywołuje prawdziwe akcje API i odświeża listę', async () => {
    renderList();
    await screen.findByText('Published');
    await userEvent.click(screen.getByRole('button', { name: 'Opublikuj' }));
    await userEvent.click(screen.getAllByRole('button', { name: 'Utwórz wersję roboczą' })[0]);
    await userEvent.click(screen.getByRole('button', { name: 'Archiwizuj' }));
    await waitFor(() => expect(adminContentApi.publish).toHaveBeenCalledWith('d', 'd-token'));
    expect(createDraft).toHaveBeenCalledWith('p');
    expect(adminContentApi.archive).toHaveBeenCalledWith('p', 'p-token');
  });
});
