import { useEffect, useState } from 'react';
import { createRoom, fetchPackages, PlayerApiError } from './api/playerApi';
import { localizedText, type ContentPackage, type CreateRoomRequest, type RoomSettings } from './api/types';
import type { Locale, TranslationKey } from './translations';

const questionTypes = [
  { key: 'PlayerSelection', label: 'questionTypePlayerSelection' },
  { key: 'TextAnswer', label: 'questionTypeTextAnswer' },
  { key: 'PhotoAnswer', label: 'questionTypePhotoAnswer' },
  { key: 'DrawingAnswer', label: 'questionTypeDrawingAnswer' },
] as const;

const defaultSettings: RoomSettings = {
  roundCount: 4, questionsPerRound: 5, playerSelectionSeconds: 20, textAnswerSeconds: 40,
  votingSeconds: 20, photoSeconds: 45, drawingSeconds: 90, resultPresentationSeconds: 8,
  finalRoundEnabled: true, finalDrawingPasses: 3,
};

type Props = {
  locale: Locale;
  t: (key: TranslationKey) => string;
  error?: TranslationKey;
  onBack: () => void;
  onCreated: (response: Awaited<ReturnType<typeof createRoom>>, nickname: string) => void;
  onError: (error: TranslationKey) => void;
};

export function HostSetup({ locale, t, error, onBack, onCreated, onError }: Props) {
  const [nickname, setNickname] = useState('');
  const [settings, setSettings] = useState<RoomSettings>(defaultSettings);
  const [packages, setPackages] = useState<ContentPackage[]>([]);
  const [selectedPackageKeys, setSelectedPackageKeys] = useState<Set<string>>(new Set());
  const [enabledQuestionTypes, setEnabledQuestionTypes] = useState<Set<string>>(new Set(['PlayerSelection']));
  const [loadingPackages, setLoadingPackages] = useState(true);
  const [creating, setCreating] = useState(false);

  useEffect(() => {
    let active = true;
    void fetchPackages().then((items) => { if (active) setPackages(items); }).catch(() => { if (active) onError('network'); }).finally(() => { if (active) setLoadingPackages(false); });
    return () => { active = false; };
  }, [onError]);

  const changeNumber = (key: keyof RoomSettings, value: number) => setSettings((current) => ({ ...current, [key]: value }));
  const toggle = (setValue: React.Dispatch<React.SetStateAction<Set<string>>>, key: string) => setValue((current) => {
    const next = new Set(current); if (next.has(key)) next.delete(key); else next.add(key); return next;
  });
  async function submit(event: React.FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault();
    const cleanNickname = nickname.trim();
    if (!cleanNickname) { onError('nicknameRequired'); return; }
    if (cleanNickname.length < 2 || cleanNickname.length > 20) { onError('nicknameLength'); return; }
    if (!enabledQuestionTypes.size) { onError('questionTypeRequired'); return; }
    const request: CreateRoomRequest = {
      nickname: cleanNickname, settings,
      selectedPackageKeys: selectedPackageKeys.size ? [...selectedPackageKeys].sort() : undefined,
      enabledQuestionTypes: [...enabledQuestionTypes].sort(),
    };
    setCreating(true);
    try { onCreated(await createRoom(request), cleanNickname); }
    catch (error) { onError(error instanceof PlayerApiError ? error.kind === 'validation' ? 'invalid' : error.kind === 'network' ? 'network' : 'server' : 'server'); }
    finally { setCreating(false); }
  }

  return <main className="page-shell"><section className="card host-setup-card" aria-labelledby="host-title">
    <button type="button" className="inline-button host-back" onClick={onBack}>{t('back')}</button>
    <p className="eyebrow">{t('hostGame')}</p><h1 id="host-title">{t('hostSetupTitle')}</h1>
    <form onSubmit={submit} noValidate>
      <label htmlFor="hostNickname">{t('nickname')}</label><input id="hostNickname" value={nickname} onChange={(event) => setNickname(event.target.value)} maxLength={20} autoFocus autoCapitalize="words" autoComplete="nickname" />
      <fieldset className="host-fieldset"><legend>{t('packages')}</legend>
        {loadingPackages ? <p>{t('loadingPackages')}</p> : packages.length === 0 ? <p>{t('noPackages')}</p> : packages.map((pkg) => <label className="checkbox-row" key={pkg.key}><input type="checkbox" checked={selectedPackageKeys.has(pkg.key)} onChange={() => toggle(setSelectedPackageKeys, pkg.key)} /><span>{localizedText(pkg.name, locale)}</span></label>)}
      </fieldset>
      <fieldset className="host-fieldset"><legend>{t('gameSettings')}</legend>
        <NumberSetting label={t('roundCount')} value={settings.roundCount} min={1} max={10} onChange={(value) => changeNumber('roundCount', value)} />
        <NumberSetting label={t('questionsPerRound')} value={settings.questionsPerRound} min={4} max={6} onChange={(value) => changeNumber('questionsPerRound', value)} />
        <NumberSetting label={t('playerSelectionSeconds')} value={settings.playerSelectionSeconds} min={5} max={120} step={5} onChange={(value) => changeNumber('playerSelectionSeconds', value)} />
        <NumberSetting label={t('textAnswerSeconds')} value={settings.textAnswerSeconds} min={5} max={180} step={5} onChange={(value) => changeNumber('textAnswerSeconds', value)} />
        <NumberSetting label={t('votingSeconds')} value={settings.votingSeconds} min={5} max={120} step={5} onChange={(value) => changeNumber('votingSeconds', value)} />
        <NumberSetting label={t('photoSeconds')} value={settings.photoSeconds} min={10} max={180} step={5} onChange={(value) => changeNumber('photoSeconds', value)} />
        <NumberSetting label={t('drawingSeconds')} value={settings.drawingSeconds} min={30} max={300} step={10} onChange={(value) => changeNumber('drawingSeconds', value)} />
        <NumberSetting label={t('resultPresentationSeconds')} value={settings.resultPresentationSeconds} min={3} max={30} onChange={(value) => changeNumber('resultPresentationSeconds', value)} />
        <label className="checkbox-row"><input type="checkbox" checked={settings.finalRoundEnabled} onChange={(event) => setSettings((current) => ({ ...current, finalRoundEnabled: event.target.checked }))} /><span>{t('finalRoundEnabled')}</span></label>
        {settings.finalRoundEnabled && <NumberSetting label={t('finalDrawingPasses')} value={settings.finalDrawingPasses} min={1} max={9} onChange={(value) => changeNumber('finalDrawingPasses', value)} />}
      </fieldset>
      <fieldset className="host-fieldset"><legend>{t('questionTypes')}</legend>{questionTypes.map((questionType) => <label className="checkbox-row" key={questionType.key}><input type="checkbox" checked={enabledQuestionTypes.has(questionType.key)} onChange={() => toggle(setEnabledQuestionTypes, questionType.key)} /><span>{t(questionType.label)}</span></label>)}</fieldset>
      {error && <p className="form-error" role="alert">{t(error)}</p>}<button type="submit" disabled={creating || loadingPackages}>{creating ? t('creatingRoom') : t('createRoom')}</button>
    </form>
  </section></main>;
}

function NumberSetting({ label, value, min, max, step = 1, onChange }: { label: string; value: number; min: number; max: number; step?: number; onChange: (value: number) => void }) {
  return <label className="number-setting"><span>{label}</span><input type="number" value={value} min={min} max={max} step={step} onChange={(event) => { const next = event.currentTarget.valueAsNumber; if (!Number.isNaN(next)) onChange(Math.min(max, Math.max(min, next))); }} /></label>;
}
