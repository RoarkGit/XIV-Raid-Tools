# XIV Raid Tools, Dalamud plugin

An in-game ImGui companion to the `kefka-says` webapp. Currently ships one
tool, Kefka Says, an in-game mirror of the Kefka Says / UMAD room tracker
for the custom ultimate "Dancing Mad", synced live with the webapp over the
same WebSocket relay (`server/index.js`).

## What it does

- Connects to the same `server/index.js` WebSocket relay the webapp uses,
  speaking the identical wire protocol (`{type:'create'}`, `{type:'join',room}`,
  `{type:'state', state:{...}}`) with the same synced fields as
  `kefka-says/app.js`.
- Renders an ImGui window styled to match the webapp, including its actual
  background/title bar colors: mechanic-wide fields plus derived status
  cards (spread/stack, accel, gaze, floor AOE shapes), using real in-game
  debuff icons where the game has one.
- Ports the Grand Cross Omega mutual-exclusion rules and the
  `enforceOrder` button-disabling logic 1:1 from `app.js`.
- Ports Reset, including clearing personal (unsynced) debuff selections on
  every other connected client when one player hits Reset.
- "Two-cast Thunder & Blizzard" (off by default): some pulls only cast
  Thunder/Blizzard once per phase, others twice. Off, a single Real/Fake
  call each, the original behavior. On, each is cast twice per phase and
  the real call is whichever way the two casts combine (both the same ->
  Real, different -> Fake), with 1st/2nd rows to call each cast separately.
  A synced, room-wide setting (like `enforceOrder`), not a per-client
  preference, and lives in the Kefka Says window itself rather than
  `/xrt config` since it's specific to that mechanic.
- "Results only" mode (`/xrt config`): hides the button/input column
  entirely, showing just the derived status column - for players who send
  state via in-game macros instead of clicking buttons themselves.
- Two-stage macro queuing: a bare mechanic command (`gco`, `tsunami`,
  `inferno`, `thunder`, `blizzard`) and a bare `real`/`fake` command can be
  fired in either order, from separate macros, and still resolve to the
  same call the instant the second one lands - see the Commands section
  below. Lets a macro-driven player get by on one macro per mechanic plus
  two shared result macros, instead of a real/fake pair for each.
- Deliberately does not read any game state (party list, duty, casts). It's
  a synced display, same scope as the webapp, just in an ImGui window.

## Installing (custom plugin repository)

This repo hosts its own [`pluginmaster.json`](../pluginmaster.json), the
standard way small/personal plugins are distributed outside the official
Dalamud plugin listing:

1. In-game, open the Plugin Installer, go to Settings → Experimental →
   Custom Plugin Repositories.
2. Add: `https://raw.githubusercontent.com/RoarkGit/XIV-Raid-Tools/main/pluginmaster.json`
3. Save, then find "XIV Raid Tools" under All Plugins and install it.

## Building

Requires XIVLauncher's Dalamud dev environment:

```
export DALAMUD_HOME="$HOME/.xlcore/dalamud/Hooks/dev"   # adjust for your OS/launcher
cd XIVRaidToolsPlugin   # this directory, if not already here
dotnet build
```

The build produces `bin/Debug/XIVRaidToolsPlugin.json` alongside the DLL
(via DalamudPackager), which is what the in-game Dev Plugin Location points
at for local testing.

## Releasing

Pushing a version tag triggers `.github/workflows/release-plugin.yml`,
which builds in Release configuration (producing a `latest.zip` via
DalamudPackager), creates a GitHub Release with that zip attached, and
commits an updated `pluginmaster.json` (new `AssemblyVersion`, new download
links) back to `main`. No manual csproj editing needed, the tag itself is
the version:

```
git tag v0.2.0.0
git push origin v0.2.0.0
```

The tag (minus the leading `v`) becomes `AssemblyVersion`, which is what
the installer compares to decide an update is available, so every release
needs a new, higher tag. The workflow needs Actions to have "Read and
write permissions" under this repo's Settings → Actions → General →
Workflow permissions, since it commits `pluginmaster.json` back to `main`.

## Commands

- `/xrt kefka` (or bare `/xrt`) opens the Kefka Says tracker.
- `/xrt config` opens plugin settings: show/hide hover tooltips, disable
  synced buttons (a safety toggle against misclicks changing party state),
  and "Results only" mode (hide the button column entirely). "Two-cast
  Thunder & Blizzard" lives in the Kefka Says window itself instead - see
  "What it does" above.

Session actions (create/join/leave a room) are buttons in the window
itself, not command-line subcommands. Firing a mechanic call, however, is
available from the command line too, for a macro or hotkey:

- `/xrt kefka gco real` / `/xrt kefka gco fake`: toggles the Cast row,
  same as clicking it. Targets whichever Grand Cross Omega target's Cast
  is not yet called (target 1 first, then target 2), since a command has
  no clean way to say "target 1" or "target 2" without hardcoding pull
  order.
- `/xrt kefka gco water|lightning|bomb`: sets that target's debuff.
  Targeting works the same way, but keyed off whichever target's debuff
  is not yet assigned; once one target's debuff is set, the other is
  already derived by the pairing rule, so only one call is ever needed
  per pull. Note that these fields are never synced to the room (personal,
  like clicking the button yourself), so this only affects your own window.
- `/xrt kefka gco1 [real|fake|water|lightning|bomb]` / `/xrt kefka gco2
  [...]`: same as `gco`, but targets that exact Grand Cross Omega target
  directly instead of inferring which one from order of occurrence, for
  when a macro needs to be explicit about which target it means.
- `/xrt kefka tsunami real|fake` / `/xrt kefka inferno real|fake`: toggles
  a Floor AOE's Cast row. The first call of either name claims Floor AOE
  #1 for that shape (Floor AOE #2 is always the other shape); a later call
  naming the other shape targets Floor AOE #2 instead.
- `/xrt kefka thunder real|fake` / `/xrt kefka blizzard real|fake`: with
  "Two-cast Thunder & Blizzard" off (the default), sets that element's Cast
  row directly, no target inference needed. With it on, behaves like `gco`
  instead - targets whichever cast (1st, then 2nd) is not yet called.
- `/xrt kefka thunder1|thunder2 real|fake` / `/xrt kefka blizzard1|blizzard2
  real|fake`: same as `gco1`/`gco2` - targets that exact cast directly
  regardless of the toggle above, for when a macro needs to be explicit.
- `/xrt kefka real` / `/xrt kefka fake`: two-stage macro queuing, to cut
  down on macro bloat for someone who'd rather have one macro per mechanic
  (`gco`, `inferno`, `tsunami`, `thunder`, `blizzard`) plus two shared
  macros for the result, instead of a real/fake pair for each. Pressing
  `real`/`fake` alone queues that result for whichever bare mechanic
  command (no `real|fake` argument of its own) is pressed next; pressing a
  bare mechanic command first queues the mechanic instead and waits for the
  result. Either order resolves the pairing the instant the second press
  lands - `gco` then `fake`, or `fake` then `gco`, both call `gco fake`.
  Only affects a mechanic call left bare: an explicit `gco real` (or
  `tsunami fake`, `thunder1 real`, etc.) still fires immediately as always,
  completely unaffected by anything queued. At most one call can be queued
  at a time; the window shows what's queued and what it's waiting on, and
  Reset clears it, same as the personal debuff fields above.
- `/xrt kefka reset`: same as clicking Reset.

An unknown tool, an unrecognized subcommand, or a `gco`/`thunder`/`blizzard`
call with nothing left to target (both instances already resolved) prints a
visible error in the game chat rather than silently doing nothing, and does
not open the window.

## Pull history

Reset saves a snapshot of the mechanic state it is about to clear,
skipping the save if there was nothing set (so repeated accidental clicks
do not fill it with blank entries). The "History" button next to Reset
opens a popout window docked to the right of the main window (not a
dropdown, so it stays open while you interact with the main window) with a
scrollable list of saved pulls, each with a one-line summary and a Restore
button, plus a Clear button to wipe the whole list. History is local to
each client (like the personal debuff fields), not synced to the room.

## Session/WS behavior

- **Reconnect with backoff**: `SessionClient` tracks a `_desiredRoom` (the
  room to stay connected to) separately from the live socket. An unexpected
  drop (server restart, network blip) retries `join` against that room on a
  1s/2s/5s/10s/20s backoff (`SessionStatus.Reconnecting`), rather than
  silently going dark like the webapp does (it just sits disconnected until
  you reload the tab). A manual "Leave"/"Give up" clears `_desiredRoom` and
  cancels any in-flight backoff.
- `SessionClient.ApplyRemote` uses `ContainsKey` rather than a null check to
  tell "field not sent" apart from "field explicitly cleared", matching
  `app.js`'s `if (k in state)` (a plain null check can't tell those apart,
  which used to mean clearing a field, such as via Reset, never reached
  peers).
