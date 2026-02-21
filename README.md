# OutOfFuel (MSFS 2024 Tool + Overlay)

OutOfFuel is a companion mod concept for Microsoft Flight Simulator 2024 with two parts:

1. **Agent backend** (`backend/OutOfFuel.Agent`) - a .NET 8 console process that will eventually ingest simulator telemetry and expose it locally.
2. **Web overlay** (`overlay/`) - a lightweight static UI designed to render fuel/range/status information in an always-on-top style panel.

## Current status

This repository currently provides project scaffolding only:

- .NET solution + console project structure in place.
- Domain folders prepared (`src/Models`, `src/Http`, `src/Sim`, `src/Config`).
- SimConnect intentionally **not** implemented yet.
- Static overlay shell created for future live data binding.

## How the overlay works (target design)

The backend agent will host a local endpoint (HTTP/WebSocket), and the overlay will connect to it from static web assets to render near-real-time flight data. This keeps simulator integration in C# while UI iteration stays simple and fast in HTML/CSS/JS.

See `docs/setup.md` for bootstrap commands.
