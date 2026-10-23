# MatPhone: notes for working in this repository

A small PBX with a web phone ("a simple Starface"): ASP.NET Core 10 Razor Pages, EF Core 10 on PostgreSQL 17, one container (web **6050**, SIP **5060** UDP/TCP,
RTP **6100–6199**/udp) plus a database. The telephony is pure C# on **SIPSorcery** (a B2BUA in the web process). The interface speaks **English and German**;
code, identifiers, URLs, comments and docs (except the README) are English. The infrastructure (setup, sign-in, TOTP, sessions, backup to a NAS, list controls,
localization, PWA) came from `../MatBill`; the look is MatDesign (`../MatDesign`, MatUi copied into `src/MatPhone/wwwroot/ui` by `scripts/sync-matui.ps1`,
version in `scripts/matui-synced.txt`). Before changing UI read MatDesign's `CLAUDE.md` and `docs/ADOPTING.md`.

## Commands

```powershell
./scripts/redeploy.ps1            # always: rebuild the image and redeploy the dev stack (web 6050, pgAdmin 6051, Postgres 16050)
./scripts/redeploy.ps1 -Fresh     # the same with empty volumes (setup page again)
dotnet build MatPhone.slnx        # build locally BEFORE a redeploy: a compile error otherwise shows up as "docker compose failed"
$env:MATPHONE_TEST_DB="Host=localhost;Port=16050;Username=postgres;Password=matphone"; dotnet test MatPhone.slnx
cd tests/e2e; npm install; node call.js http://localhost:6050    # two headless Edge browsers call each other and the mailbox (fake microphone)
node tools/app-icons.js                                          # wwwroot/ui/mp-icons.svg from ../MatDesign/tools/iconlib/lucide.json
NODE_PATH=tests/e2e/node_modules node tools/icons.js             # PNG icons of the manifest from the SVGs
dotnet ef migrations add <Name> -p src/MatPhone -s src/MatPhone -o Data/Migrations
```

Test changes by **rebuilding the container and redeploying the stack**; wait for `http://localhost:6050/health`. After a fresh stack: set the server address under
*Telephony settings* (the host's LAN IP), otherwise calls connect without audio. The integration tests start the app in-process: its engine cannot bind 5060
while the dev stack runs (the pages still work). The NAS tests need Samba (`MATPHONE_TEST_SMB`, the CI starts one).

## Layout

- `src/MatPhone.Core` (no web types): `Telephony/` (NumberRules: E.164, national form, provider formats; DialPattern `0900.`/`11X`/`[1-5]`; TimeWindow: day bits,
  bit 0 = Sunday, over midnight; RuleMatcher), `Audio/` (G711 tables after ITU G.191, Wav 8 kHz mono), `Security/` (PasswordHasher, Totp).
- `src/MatPhone/Business`: **static** classes, the only place with queries and rules. Every public method takes an `Actor` and checks it
  (`RequireAdmin`, `RequireSelfOrAdmin`, `RequireSignedIn` → `AccessDeniedException` → 403 by `AccessDeniedFilter`). Saving returns `SaveResult` (errors per field).
  Pages and the UI never query the database themselves. A change that the engine must see calls `TelephonySignals.Notify(...)`.
- `src/MatPhone/Telephony`: `PbxEngine` (hosted service: transport, dispatch, restart on address/port change, WebSocket messages of the web phone),
  `PbxSnapshot` (an immutable copy of users, devices, numbers, groups, mailboxes, lines, rules; reloaded on a signal), `Router` (what a dialled number means),
  `CallManager` + `ActiveCall` (ringing strategies, forwarding, transfer, pickup, CDR), `Legs.cs` (SIP inbound/outbound legs), `WebLeg` (WebRTC, the server always offers),
  `Media.cs` (MediaPort: G.711 transcoding, RFC 4733 DTMF; MediaBridge), `MailboxApp` (record, listen, feature codes; tones only), `Registrar` (digest auth, fail2ban),
  `LineManager` (trunk registrations), `Subscriptions` (BLF dialog + MWI), `PresenceTracker`, `TelephonyLogWriter` (queued log).
- `src/MatPhone/Pages`: `Phone` (the web phone, `wwwroot/js/phone.js`), `Index`, `Calls`, `Voicemail`, `Directory`, `Account/Telephony`, `Admin/*` (CRUD: list + edit page each).
- `tests/MatPhone.Tests` (Core unit tests, Integration against PostgreSQL), `tests/e2e/call.js` (Playwright, two browsers).

## Rules of the telephony

- Numbers inside the installation: extensions are digits (≤ 8), external numbers are **E.164 with a plus**; a line gets them in its `NumberFormat`. Dialling out needs
  the outside-line prefix (`0` by default); emergency numbers (110, 112, 116117) always go out.
- An incoming call: the rules (first match by position) → else the target of the called number. A group rings its logged-in members by strategy
  (parallel / linear / round robin with `LastCallDate`) within `MaxWaitSeconds`, then its fallback. A user rings all devices and browsers; DND and forwarding apply.
- The web phone: WebSocket `/ws/phone` (origin checked), messages `{ t: ... }` (hello, dial, answer, reject, hangup, candidate, dtmf, hold, transfer, pickup, dnd, forward,
  group, ring, ping). The server offers, the browser answers; the public address is added as host candidate. **The receive loop must never wait for the browser**:
  `dial` runs beside it (the answer comes over the same socket), and a leg being set up is found in `_settingUp`. Audio to a browser is lost until WebRTC is
  connected: apps that play something first call `MediaPort.WaitReadyAsync`.
- Files of the mailboxes go through `FileStoreService` (local or NAS); point at a new file with the navigation property (`AudioFile = file`), not `file.Id`:
  the row is saved with the caller's `SaveChanges`.
- Configuration (company name, telephony, mail, storage, security, backup, logs) is JSON in `/data/config/app.json` (`ConfigStore`); logic in the database.
- Database: tables PascalCase, `Id` bigint, audit columns filled by `MpDbContext`, `UpdateState` soft delete (0 created, 1 changed, 2 deleted; `ISoftDeletable`),
  enums as text, `Token` uuid for secrets. Unique numbers and SIP users are filtered on not deleted.

## UI conventions

- MatUi only, flat look; app classes `mp-*` in `app.css`; the accent is yellow (`[data-app="matphone"]`: `#ffd200`, text on it dark). No inline styles or scripts
  (CSP `default-src 'self'`, `media-src 'self' blob:`, microphone only for the own origin). Icons from the sprite (`Ui.Icon`; own icons in `ui/mp-icons.svg`; there is no `history`, use `clock`).
- Lists: toolbar above (search, filter, sort), table, pager directly below, actions below on the left. CRUD = list + separate edit page with Save, Back, (space) Delete.
  Several sections → tabs (with a hidden `Tab` field so a post returns to the tab). Forms are `Width = "narrow"`, the phone page `wide`.
- Dialogs are MatUi (`data-confirm`, `MatUi.toast`); the phone page asks before leaving during a call.
- Mobile: check 390 px; PWA: `sw.js` never caches `/api`, `/ws`, `/voicemail`, `/greeting`, `/Account`.
- The version link bottom left opens `/About` (anonymous).

## Languages

- **No interface text in code.** Every text is a key in `src/MatPhone/Resources/Texts/<Area>.json`: `{ "Area.Key": { "en": "…", "de": "…" } }`; the part before the first
  dot names the file; German uses "du". Razor `@L["Key"]`, page models `L["Key", arg]`, elsewhere `Text.T("Key", args)`, `phone.js`/`app.js` `t('Js.Key')` or `data-text-*`.
- German on purpose (and allowed by `TextsTests`): sample data (`Business/Demo.cs`), the notes of the built-in provider profiles (`Business/Lines.cs`),
  the voicemail mail templates (`AppConfig`, `MailService`). The telephony log is technical English.

## Gotchas

- Shell strings with backticks, `$`, apostrophes or `\n` get mangled by the Bash tool: write C# and JSON with the editor tools or a node script file.
- Git Bash converts arguments that start with `/` into Windows paths: `MSYS_NO_PATHCONV=1` for node scripts that take URL paths.
- A `<partial model="...">` attribute cannot hold a C# object initialiser with quotes; Razor local functions with tag helpers must be `async Task`.
- `LogSeverity`, not `LogLevel` (clash with Microsoft.Extensions.Logging).
- The dev compose has an explicit subnet (`10.253.65.0/24`). Docker Desktop maps the RTP range; the server address must be the host's IP, not the container's.

## Git

Branches: `main` (release), `dev` (development). Commit and push **under the user's name** only: no `Co-Authored-By` trailer, no "Generated with" line, no other names in branches.
Versions: release `<major>.<minor>.<build>-<date>`, dev `nightly-<build>-<date>`, local `local-<date>` (`Directory.Build.props`, set by the GitHub action).
