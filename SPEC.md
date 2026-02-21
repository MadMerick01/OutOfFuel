# Out of Fuel — Spec

## Core Loop
- Player flies Sydney → Brisbane.
- Aircraft has fuel onboard, but a fuel line leak forces landings.

## Timer
- Interval: 15:00 (900 seconds) of airborne time.
- Warning state: last 90 seconds before cut.

## Fuel Cut
- At 0:00, trigger fuel starvation:
  - Default: ramp fuel down over 30 seconds to near-zero.

## Refuel
- Refuel is allowed when:
  - On ground AND
  - Groundspeed < 2 knots continuously for 5 seconds
- Refuel action:
  - Set fuel to 40% total usable fuel (configurable)
  - Reset the 15:00 timer
