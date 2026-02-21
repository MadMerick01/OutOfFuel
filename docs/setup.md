# Setup

## Repository layout

- `backend/OutOfFuel.Agent/` - .NET 8 console agent solution and project.
- `overlay/` - static HTML/CSS/JS overlay shell.
- `docs/` - project notes and setup guidance.

## Prerequisites

- .NET SDK 8.0+
- Microsoft Flight Simulator with SimConnect
- SimConnect managed DLL at:
  `backend/OutOfFuel.Agent/OutOfFuel.Agent/lib/SimConnect/Microsoft.FlightSimulator.SimConnect.dll`

## Leak mechanic (total fuel only)

The B mechanic is implemented using a single total fuel value from SimConnect (`FUEL TOTAL QUANTITY`, unit: **gallons**):

- `timeToCutSec` counts down only while airborne (`onGround=false`), and pauses on ground.
- A leak cycle starts on initial fuel read and after each successful refuel.
- Cycle values:
  - `startFuelTotal`: total fuel at cycle start.
  - `minFuelTotal = max(startFuelTotal * (minFuelPercentOfStart/100), minFuelAbsolute)`
  - `drainPerSecond = max((startFuelTotal - minFuelTotal) / intervalSec, 0)`
- While airborne and connected, fuel drains continuously with delta-time and is clamped to not go below `minFuelTotal`.
- STARVING latches when countdown reaches 0 or fuel hits `minFuelTotal + starveEpsilon`, and stays latched until successful refuel.

## Config

`backend/OutOfFuel.Agent/OutOfFuel.Agent/config.json` is created/updated automatically with defaults:

```json
{
  "intervalSec": 900,
  "warningSec": 90,
  "refuelPercent": 40,
  "refuelStopSpeedKts": 2,
  "refuelStopHoldSec": 5,
  "tickHz": 10,
  "minFuelPercentOfStart": 1.0,
  "minFuelAbsolute": 0.25,
  "starveEpsilon": 0.05
}
```

## Build backend

From repository root:

```bash
cd backend/OutOfFuel.Agent
dotnet build OutOfFuel.Agent.sln
```

## Run backend

```bash
cd backend/OutOfFuel.Agent/OutOfFuel.Agent
dotnet run -- --debug
```

Endpoints:

- `GET /health`
- `GET /state`
- `POST /refuel`

## Open overlay

Open `overlay/index.html` in a browser for local UI iteration.

## OBS Browser Source setup

### Option 1: Local file

1. In OBS, add a **Browser Source**.
2. Check **Local file**.
3. Select `overlay/index.html` from this repository.
4. Set a fixed canvas size (recommended below).

### Option 2: Local URL

1. Start the agent (`dotnet run -- --debug`) so the API is available on `http://localhost:8080`.
2. Serve the `overlay/` folder with any local static server.
3. In OBS Browser Source, uncheck **Local file** and set a URL (for example `http://localhost:3000`).

### Recommended Browser Source settings

- **Width**: `520`
- **Height**: `260`
- **Shutdown source when not visible**: **Off**

Turning off shutdown prevents the source from unloading and reconnecting repeatedly during scene switches.
