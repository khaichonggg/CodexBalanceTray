# CodexBalanceTray

CodexBalanceTray is a small Windows notification-area companion for Codex. It
uses the official local `codex app-server` to show remaining quota windows,
reset times, and credits when the account provides them.

## Behavior

- Hover the tray icon to see quota and credits without clicking.
- The icon is a flat white circle with a progress outline. The colored arc
  shrinks with the lowest remaining quota, while a light-gray track shows the
  used portion:
  - green above 60% remaining;
  - orange from 31% through 60%;
  - red at 30% or below;
  - gray when disconnected or stale.
- Right-click to refresh, choose the current Codex account, connect an isolated
  ChatGPT account, toggle startup, view About, or exit.
- The app refreshes every five minutes and reacts to app-server updates.

## Privacy

The app does not parse or copy Codex token files. The current-account mode lets
the official Codex process use its normal `CODEX_HOME`. Independent login uses
`%LOCALAPPDATA%\CodexBalanceTray\codex-home`. Logs contain event names and
exception types only; account identifiers, email addresses, tokens, OAuth URLs,
and raw server responses are not logged.

## Build

```powershell
$dotnet = 'E:\Codex\downloads\dotnet-sdk-10\dotnet.exe'
& $dotnet restore .\CodexBalanceTray.sln
& $dotnet test .\CodexBalanceTray.sln -c Release
& $dotnet publish .\src\CodexBalanceTray\CodexBalanceTray.csproj `
  -c Release -r win-x64 --self-contained true `
  -o E:\Codex\outputs\CodexBalanceTray
```

The published `CodexBalanceTray.exe` is portable and self-contained. Keep it at
its final path so the per-user startup registry entry stays valid.
