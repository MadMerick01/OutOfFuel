const API_BASE = "http://localhost:8080";
const POLL_MS = 200;

const countdownEl = document.getElementById("countdown");
const stateEl = document.getElementById("state");
const fuelEl = document.getElementById("fuel");
const leakEl = document.getElementById("leak");
const drainEl = document.getElementById("drain");
const refuelBtn = document.getElementById("refuel");
const statusEl = document.getElementById("status");
const holdWrapEl = document.getElementById("holdWrap");
const holdBarEl = document.getElementById("holdBar");

const stateClassByName = {
  SAFE: "state-safe",
  WARNING: "state-warning",
  STARVING: "state-starving"
};

let isOffline = false;

function formatTime(seconds) {
  const safe = Number.isFinite(seconds) ? Math.max(0, Math.floor(seconds)) : 0;
  const mm = String(Math.floor(safe / 60)).padStart(2, "0");
  const ss = String(safe % 60).padStart(2, "0");
  return `${mm}:${ss}`;
}

function toFinite(value, fallback = 0) {
  const n = Number(value);
  return Number.isFinite(n) ? n : fallback;
}

function showOffline() {
  if (isOffline) {
    return;
  }

  isOffline = true;
  countdownEl.textContent = "--:--";
  stateEl.textContent = "OFFLINE";
  stateEl.className = "state state-starving";
  fuelEl.textContent = "Fuel Total: --";
  leakEl.textContent = "LEAK PAUSED";
  drainEl.textContent = "Drain -- /sec";
  refuelBtn.hidden = true;
  holdWrapEl.hidden = true;
  statusEl.hidden = false;
  statusEl.textContent = "Agent not running";
}

function showOnline(data) {
  isOffline = false;
  statusEl.hidden = true;

  const state = String(data?.state ?? "SAFE").toUpperCase();
  const stateClass = stateClassByName[state] ?? "state-safe";

  countdownEl.textContent = formatTime(data?.timeToCutSec);
  stateEl.textContent = state;
  stateEl.className = `state ${stateClass}`;

  const fuelPercent = Number(data?.fuelPercent);
  if (Number.isFinite(fuelPercent)) {
    fuelEl.textContent = `Fuel ${fuelPercent.toFixed(1)}%`;
  } else {
    const fuelTotal = toFinite(data?.fuelTotal, 0);
    fuelEl.textContent = `Fuel Total: ${fuelTotal.toFixed(2)}`;
  }

  const leakActive = Boolean(data?.leakActive);
  leakEl.textContent = leakActive ? "LEAK ACTIVE" : "LEAK PAUSED";

  const drainPerSecond = toFinite(data?.drainPerSecond, 0);
  drainEl.textContent = `Drain ${drainPerSecond.toFixed(3)} /sec`;

  const holdProgress = Math.max(0, Math.min(100, Math.round(toFinite(data?.stopHoldProgress, 0))));
  holdWrapEl.hidden = false;
  holdBarEl.style.width = `${holdProgress}%`;

  refuelBtn.hidden = !Boolean(data?.refuelAllowed);
}

async function pollState() {
  try {
    const response = await fetch(`${API_BASE}/state`, {
      cache: "no-store"
    });

    if (!response.ok) {
      throw new Error(`HTTP ${response.status}`);
    }

    const data = await response.json();
    showOnline(data);
  } catch {
    showOffline();
  }
}

refuelBtn.addEventListener("click", async () => {
  try {
    await fetch(`${API_BASE}/refuel`, {
      method: "POST"
    });
    await pollState();
  } catch {
    showOffline();
  }
});

pollState();
setInterval(pollState, POLL_MS);
