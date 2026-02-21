const API_BASE = "http://localhost:8080";
const POLL_MS = 200;

const countdownEl = document.getElementById("countdown");
const stateEl = document.getElementById("state");
const fuelEl = document.getElementById("fuel");
const refuelBtn = document.getElementById("refuel");
const statusEl = document.getElementById("status");

const stateClassByName = {
  SAFE: "state-safe",
  WARNING: "state-warning",
  STARVING: "state-starving",
  LANDED: "state-landed"
};

let isOffline = false;

function formatTime(seconds) {
  const safe = Number.isFinite(seconds) ? Math.max(0, Math.floor(seconds)) : 0;
  const mm = String(Math.floor(safe / 60)).padStart(2, "0");
  const ss = String(safe % 60).padStart(2, "0");
  return `${mm}:${ss}`;
}

function showOffline() {
  if (isOffline) {
    return;
  }

  isOffline = true;
  countdownEl.textContent = "--:--";
  stateEl.textContent = "OFFLINE";
  stateEl.className = "state state-starving";
  fuelEl.textContent = "Fuel --%";
  refuelBtn.hidden = true;
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

  const fuel = Number(data?.fuelPercent);
  fuelEl.textContent = `Fuel ${Number.isFinite(fuel) ? fuel.toFixed(1) : "--"}%`;

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
