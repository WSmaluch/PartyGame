# Dependency audit — stage 8.4

`npm audit` was run separately for Display and Admin. Both lockfiles reported three high advisories: transitive `brace-expansion` and `react-router`/`react-router-dom`. The installed application is a browser-only Vite SPA and does not use React Server Components, the affected React Router server feature. The available automated remediation requires the breaking React Router 8 line, so no forced upgrade was applied. `brace-expansion` is reached through build tooling, not the released browser runtime.

Mitigation and accepted residual risk: production artifacts contain no server-side React Router execution; package upgrades remain a tracked maintenance item; `npm audit` is repeated for releases. No critical advisory was reported.

NuGet vulnerability and outdated-package reports are generated per project because this repository has no `server/PartyGame.sln`. Only security fixes with a compatible minimal update may be applied; routine outdated packages are not upgraded blindly.

## Aktualizacja po RC15

React Router wydał poprawkę GHSA-qwww-vcr4-c8h2 w linii 7.x, więc Admin przeszedł na `react-router-dom` 7.18.7 bez zmiany wersji głównej. Zależności deweloperskie Admina, Display i Web Playera (m.in. `vitest`, `brace-expansion`, `baseline-browser-mapping`) zaktualizowano przez `npm audit fix` bez `--force`. Po tej zmianie `npm audit` nie raportuje żadnych podatności w trzech aplikacjach webowych.

### NuGet po RC15

- `Microsoft.AspNetCore.SignalR.Client` w testach i skryptach podniesiono z 10.0.10 do 10.0.15 (CVE-2026-70159, DoS w SignalR, poprawione w 10.0.13). Ta sama podatność dotyczy współdzielonego frameworka ASP.NET Core, z którego korzysta serwer: host musi mieć runtime .NET 10 w wersji co najmniej 10.0.13.
- **Zamknięte:** serwer nie używa już `SixLabors.ImageSharp` 3.1.x (podatności high/moderate przy dekodowaniu bez poprawki w linii 3.1). Normalizację obrazów przejął SkiaSharp 3.119.4 (MIT); wymiary są sprawdzane z nagłówka przed dekodowaniem pikseli. ImageSharp został tylko w `PartyGame.Tests` i w narzędziach `scripts/` jako dekoder pomocniczy; nie trafia do wydania, ale `dotnet restore` nadal pokazuje dla nich ostrzeżenia NU1902/NU1903.
