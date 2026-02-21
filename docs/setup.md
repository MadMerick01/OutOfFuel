# Setup

## Prerequisites

- .NET SDK 8.0+
- Microsoft Flight Simulator 2024 with SimConnect support
- SimConnect managed DLL at:
  `backend/OutOfFuel.Agent/OutOfFuel.Agent/lib/SimConnect/Microsoft.FlightSimulator.SimConnect.dll`

## 1) Run backend agent

From repository root:

```bash
cd backend/OutOfFuel.Agent/OutOfFuel.Agent
dotnet run -- --debug
```

Expected startup URL:

- `http://localhost:8080`

Available endpoints:

- `GET http://localhost:8080/health`
- `GET http://localhost:8080/state`
- `POST http://localhost:8080/refuel`

## 2) Run overlay in a browser

The overlay is static HTML/CSS/JS and polls `http://localhost:8080/state`.

### Option A: Open local file directly

- Open `overlay/index.html` in your browser.

### Option B: Serve overlay locally (recommended)

From repository root:

```bash
cd overlay
python3 -m http.server 3000
```

Then open:

- `http://localhost:3000`

## 3) Run overlay in OBS Browser Source

1. Start backend agent first (`dotnet run -- --debug`).
2. Add a **Browser Source** in OBS.
3. Use one of the following:
   - **Local file mode**: enable **Local file** and select `<repo>/overlay/index.html`
   - **URL mode**: set URL to `http://localhost:3000` (if using `python3 -m http.server 3000`)

Recommended OBS Browser Source dimensions:

- **Width**: `520`
- **Height**: `260`

Recommended OBS toggles:

- **Shutdown source when not visible**: Off
- **Refresh browser when scene becomes active**: Off

## Config file behavior

The agent reads and writes:

- `backend/OutOfFuel.Agent/OutOfFuel.Agent/config.json`

If the file is missing, it is created with defaults. On load, values are validated and the current config is written back to disk.
