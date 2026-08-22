import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import App from './App';
import { loadRuntimeConfig } from './api/apiConfig';
import { preferredLocale, translations } from './translations';
import './styles.css';

const root = createRoot(document.getElementById('root')!);
void bootstrap();

async function bootstrap(): Promise<void> {
  try {
    await loadRuntimeConfig();
    root.render(<StrictMode><App /></StrictMode>);
  } catch {
    root.render(<main className="page-shell"><p className="form-error" role="alert">{translations[preferredLocale()].configurationError}</p></main>);
  }
}
