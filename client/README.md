# Crawlers client

The browser client for [Crawlers](../README.md). React 19 + TypeScript, Pixi.js v8 for
the dungeon canvas, `@microsoft/signalr` for the realtime link, bundled by Vite.

The client is a renderer, not a simulation. It sends intents over SignalR and draws
the snapshots the server broadcasts back. See the "Architecture rules" section of the
[root README](../README.md) for exactly where that boundary sits.

## Running it

```sh
npm install
npm run dev
```

Then open `http://localhost:5173`. The dev server needs the game server reachable on
`http://localhost:5238`; start it with `docker compose up --build` from the repo root,
or `dotnet run --project server/src/Crawlers.Server --launch-profile http`.

`vite.config.ts` binds on `0.0.0.0` and proxies `/game`, `/lobby`, and `/api` to that
server, so the Vite port is the only one that has to be reachable from another device.

## Scripts

| Command | What it does |
|---|---|
| `npm run dev` | Vite dev server with HMR on port 5173 |
| `npm run build` | `tsc -b` typecheck, then a production bundle into `dist/` |
| `npm run lint` | ESLint over the whole package |
| `npm run preview` | Serve the built `dist/` locally |

## Layout

```
src/
├── api/       SignalR wiring and TypeScript mirrors of the server DTOs
├── game/      Pixi renderer, asset loader, tile palette
├── ui/        HUD, combat log, inventory, lobby, mobile controls, overlays
├── dev/       SpriteProbe, an atlas inspector reachable at ?probe=sprites
├── identity.ts  localStorage player UUID + username
├── App.tsx    asset preload, identity, lobby phase machine
└── Game.tsx   /game connection, key handling, snapshot to render
```

Sprite coordinates live in `public/assets/dungeon/assets.json`, never hardcoded in TS.
