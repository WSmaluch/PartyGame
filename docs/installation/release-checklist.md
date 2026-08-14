# Release and physical-device checklist

Automated RC acceptance covers package integrity, clean installation, upgrade, restore, security, diagnostics, support bundle and Mixed Client E2E. Before final production approval, manually record the following on the intended LAN:

- iPhone opens the server URL, joins a room, reconnects after Wi-Fi toggle and after app relaunch.
- A second computer or tablet opens Admin and Display over LAN.
- TV/Display remains attached through a game, reconnect and host restart.
- Play profile photo, drawing, vote and final ranking; verify all media survives restart.
- Confirm operator token is never visible in Admin UI, logs or exported evidence.

If physical devices were unavailable, record **Manual physical-device acceptance: pending**. This blocks final `v1.0.0`, not automated RC publication.

## RC5 deterministic physical-device QA

Use a normal published content package rather than changing the game planner or editing SQLite. In Admin, create a package named `RC5 physical QA`, add one active category and exactly four active questions (minimum three players): one `PlayerSelection`, one `TextAnswer`, one `PhotoAnswer`, and one `DrawingAnswer`. The Text question must be `Co {player} na pewno zapomni spakować?`. Publish the package.

On the LAN server, start the installed release with `scripts/start-lan.sh --deploy-root <install-root> --runtime-root <runtime-root> --host <private-lan-ip> --port <port>`. Open Admin at `http://<private-lan-ip>:<port>/admin/` and create/publish the package there. On the iPhone, set the same server address, choose **Host game**, select only `RC5 physical QA`, select all four entries in **Question types**, set one round and four questions per round, then create the room. Join two additional players, attach Display, and mark all players ready.

The production `GamePlanner` may shuffle the four questions, but because the selected package contains exactly one question of each enabled type and the game requires four questions, every run contains exactly one Player Selection, Text Answer, Photo Answer, and Drawing Answer. This guarantees the media checks without a test endpoint, a random seed, or manual database changes. Record the observed order and verify all of the following: the rendered Text prompt contains a real player nickname and no token, both eligible answers reach voting, Photo and Drawing render on Display, a Wi-Fi/relaunch reconnect restores the authoritative stage, and every phone reaches Completed with the final ranking.

### Final Round acceptance

With **Enable final round** selected, normal round `N` must be followed by the extra Final Round (`N+1`) before Completed. Each player receives a localized built-in selfie prompt and a makeover role. The starter catalog is intentionally centralized in `GameStateMachine` until authorable final prompts are added: scary face / rock star / mysterious hero / dramatic face and bandit / space pirate / disco royalty / superhero.

Verify that every player submits one selfie; on each edit pass, every available artifact is assigned to exactly one *other* player and no player receives their own artifact. The editor must draw on the supplied image and submit the accumulated result. At presentation, Display must show the completed image captioned `<PlayerName> as <TargetRole>`; then complete final voting and confirm that only the final results lead into Game Summary and Completed. Repeat a reconnect during selfie collection, an edit pass, and voting.
