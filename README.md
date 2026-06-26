# XIV Raid Tools

Browser-based HUDs for FFXIV mechanics. No install, no login. Open the page and go.

**Live site:** https://RoarkGit.github.io/XIV-Raid-Tools/

---

## Tools

### [Kefka Says](kefka-says/) (UMAD)
Real/Fake resolver for the Kefka Says mechanic in *Dancing Mad (Ultimate)*.

Tracks GCOs, Floor AOEs, Thunder, and Blizzard.

Supports real-time sync via a shared room code so the whole static can see shared callouts:

1. One person clicks **Create** and shares the link (or the 4-letter room code)
2. Others open the link or paste the code and click **Join**
3. Mechanic-wide fields (real/fake assignments) sync instantly across all connected tabs/machines
4. Player-specific fields (your spread/stack and Acceleration Bomb) stay local

Sync is handled by a lightweight WebSocket relay server hosted on Railway. No state is stored server-side; the server only relays messages between clients in the same room.

---

## Development

**Run the sync server locally:**
```bash
npm install
npm start
# Server starts on ws://localhost:3000
```

When running locally, the frontend auto-connects to `localhost:3000`. On GitHub Pages it connects to the Railway deployment.

**Edit a tool:**
Each tool is a single self-contained HTML file at `<tool-name>/index.html`. No build step required. Open directly in a browser or serve with any static file server.

---

## Deployment

- **Frontend:** GitHub Pages (auto-deploys from `main` branch)
- **Sync server:** Railway (deploys from this repo, runs `npm start`)

To update the Railway server URL after deploying, set `WS_URL` in the relevant tool's HTML file.
