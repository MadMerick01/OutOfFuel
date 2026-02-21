# Setup

## Repository layout

- `backend/OutOfFuel.Agent/` - .NET 8 console agent solution and project.
- `overlay/` - static HTML/CSS/JS overlay shell.
- `docs/` - project notes and setup guidance.

## Prerequisites

- .NET SDK 8.0+
- MSFS 2024 environment for future SimConnect integration

## Build backend

From repository root:

```bash
cd backend/OutOfFuel.Agent
dotnet build OutOfFuel.Agent.sln
```

## Run backend

```bash
cd backend/OutOfFuel.Agent/OutOfFuel.Agent
dotnet run
```

## Open overlay

Open `overlay/index.html` in a browser for local UI iteration.

## OBS Browser Source setup

### Option 1: Local file

1. In OBS, add a **Browser Source**.
2. Check **Local file**.
3. Select `overlay/index.html` from this repository.
4. Set a fixed canvas size (recommended below).

### Option 2: Local URL

1. Start the agent (`dotnet run`) so the API is available on `http://localhost:8080`.
2. Serve the `overlay/` folder with any local static server.
3. In OBS Browser Source, uncheck **Local file** and set a URL (for example `http://localhost:3000`).

### Recommended Browser Source settings

- **Width**: `500`
- **Height**: `220`
- **Shutdown source when not visible**: **Off**

Turning off shutdown prevents the source from unloading and reconnecting repeatedly during scene switches.
