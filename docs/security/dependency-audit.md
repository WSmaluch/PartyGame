# Dependency audit — stage 8.4

`npm audit` was run separately for Display and Admin. Both lockfiles reported three high advisories: transitive `brace-expansion` and `react-router`/`react-router-dom`. The installed application is a browser-only Vite SPA and does not use React Server Components, the affected React Router server feature. The available automated remediation requires the breaking React Router 8 line, so no forced upgrade was applied. `brace-expansion` is reached through build tooling, not the released browser runtime.

Mitigation and accepted residual risk: production artifacts contain no server-side React Router execution; package upgrades remain a tracked maintenance item; `npm audit` is repeated for releases. No critical advisory was reported.

NuGet vulnerability and outdated-package reports are generated per project because this repository has no `server/PartyGame.sln`. Only security fixes with a compatible minimal update may be applied; routine outdated packages are not upgraded blindly.
