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
