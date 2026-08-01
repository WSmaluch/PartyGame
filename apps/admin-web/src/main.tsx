import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import App from './App.tsx';
import { loadRuntimeConfig } from './api/apiConfig.ts';
import './styles/global.css';

const root = createRoot(document.getElementById('root')!);

void bootstrap();

async function bootstrap(): Promise<void> {
  try {
    const runtimeConfig = await loadRuntimeConfig();
    console.info(
      `PartyGame Admin build ${runtimeConfig.buildVersion}; API ${runtimeConfig.apiBaseUrl}`,
    );
    root.render(
      <StrictMode>
        <BrowserRouter>
          <App />
        </BrowserRouter>
      </StrictMode>,
    );
  } catch (error) {
    root.render(
      <main role="alert">
        Nie można uruchomić Admina:{' '}
        {error instanceof Error ? error.message : 'nieznany błąd konfiguracji.'}
      </main>,
    );
  }
}
