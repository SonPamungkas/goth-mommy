# GOTH MOMMY Radar Overhaul
<img width="2496" height="2560" alt="imgonline-com-ua-twotoone-VqqXmlZtO2_(1) 1" src="https://github.com/user-attachments/assets/bbe42dcd-bae3-45e0-aa0d-65a7591a0ba9" />

## Description
GOTH (Ground Over The Horizon) drastically buffs radar detection systems, eliminating their inability to detect high-altitude threats at long ranges. 

At the core of GOTH is the **Multi Orbital Mapping & Monitoring Yield (MOMMY)** sub-system. MOMMY dynamically adjusts the internal math of the vanilla radar engine, ensuring that both airborne and surface-based air defense systems remain highly lethal over the horizon without causing exponential math errors or tanking framerates. Because sometimes, SAM sites just need their MOMMY to help them see.

## How the Formula Works
The radar relies on a "Signal Strength" equation that mimics real-world physics (the 4th-root decay law). Normally, radar systems have a hardcoded `maxRange` and a `minSignal` threshold that prevents them from detecting distant targets regardless of altitude.

GOTH intercepts the `CanSeeRadarReturn` function and applies a series of dynamic multipliers **before** the engine calculates the signal score:

1. **Target Altitude Multiplier (`detectionMult`)**
   - The formula reads the target's absolute altitude (in meters) and Radar Cross Section (RCS).
   - `detectionMult = 1.0 + (targetAlt * 0.001 * RCS)`
   - *Example:* An aircraft flying at 5,000 meters with an RCS of 0.5 will generate a multiplier of `3.5x`. This simulates the radar's line-of-sight clearing terrain curvature.

2. **RCS Lock Multiplier (`lockMult`)**
   - Large targets are exponentially easier to track.
   - `lockMult = 1.0 + RCS`
   - *Example:* A massive bomber with an RCS of 2.0 gets an additional `3.0x` lock bonus.

3. **Maximum Range Extension**
   - The radar's raw `maxRange` is multiplied by the detection multipliers and scaled logarithmically based on the emitter's altitude.
   - `maxRange = origMaxRange * detectionMult * lockMult * (1.0 + log10(emitter_altitude + 1))`
   - High-altitude radar systems like AWACS or mountaintop SAM sites will see massive range extensions, while low-altitude units are properly grounded in reality.

4. **MinSignal Reduction**
   - The vanilla game hardcodes the `minSignal` threshold (usually ~0.5), meaning distant weak returns are ignored.
   - GOTH aggressively reduces this threshold by dividing it by the RCS lock multiplier:
   - `minSignal = origMinSignal / lockMult`
   - This ensures that large-RCS targets drop the detection threshold, guaranteeing the radar picks them up far beyond vanilla limits.

## Ground Radar Emulation
MOMMY automatically scans every single radar-emitting unit and ship on startup, forcefully injecting configurable visual optics (Visual Range, Magnification, Max Speed) directly into their TargetDetectors. This makes surface radar units inherently capable of tracking low-flying or non-radar units at immense distances without relying on aircraft EOTS.

![1000182621](https://github.com/user-attachments/assets/64856022-cb48-407c-b1ff-9ee2c355078a)

## Features
- **Atmospheric Refraction & Over-The-Horizon Radar:**
  - Tropospheric 4/3 effective Earth radius geometry extending radar sight beyond geometric horizons.
  - RCS diffraction slope calculations allowing large targets to be detected beyond line-of-sight based on radar cross-section.
  - Broad spatial envelope search expansion up to 250km.
  - Zero-allocation, non-mutating signal strength evaluation.

- **Universal Optics Injection:**
  - Upgraded visual detection optics (visual range up to 50km, 8x magnification, 1 m/s target speed threshold) directly injected into surface radar and warship units on spawn.

- **Over-The-Horizon Naval Bombardment:**
  - Long-range engagement without direct line-of-sight requirements for heavy naval cannons and railguns.
  - Unified 2000 m/s exit velocity for ship-launched guided shells and bombardment cannons.
  - Dynamic 45-degree high-angle elevation capping for guided shells engaging at long range for maximum ballistic reach.
  - Caliber filtering ensuring heavy land artillery (SPGs) receives long-range capabilities while light mortars remain standard.
  - Surface priority gating to keep heavy bombardment turrets locked on warships and land installations rather than wasting rounds on incoming missiles.
  - Extended bullet self-destruct timers for long-range flight.

- **Smart SAM Defense:**
  - **3-Slot Seeker Discrimination:** Enforces independent Fox 1 (SARH), Fox 2 (IR), and Fox 3 (ARH) seeker slots per target. Prevents redundant missile dumping of the same seeker type while allowing mixed-seeker salvos.
  - **Force Change Target:** Never blocks the launcher trigger. When a target is already engaged or quota is met, the turret automatically forces a target change to another valid target.
  - **Self & Buddy Point Defense Trigger:** Emergency salvo multilock only activates if incoming missiles threaten the platform or an allied surface unit within a configurable radius (separate thresholds for ships vs. ground vehicles/buildings). Aerial and peacetime engagements remain 100% vanilla.
  - **Adjustable Salvo Interval & Threat Re-engagement Cooldown:** Interceptor replacement rate and per-threat re-engagement cooldown are both configurable, giving direct control over cumulative ammunition expenditure.
  - **Independent SARH (Fox 1) Cadence:** SARH turrets are handled entirely independently, with their own rate of fire.
  - **Independent CIWS Range & Reaction-Time Buff:** CIWS point-defense guns get their own unconditional engagement-range and lock/assessment-cadence buff.

- **ARH (Active Radar Homing) Buff:**
  - Softens the ground-clutter signal penalty on ARH seekers that causes lock loss against low-altitude targets.
  - Extends the terminal homing range at which an ARH missile switches from datalink guidance to active radar terminal homing.
