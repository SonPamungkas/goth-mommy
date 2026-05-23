# GOTH MOMMY Radar Overhaul
<img width="2496" height="2560" alt="imgonline-com-ua-twotoone-VqqXmlZtO2_(1) 1" src="https://github.com/user-attachments/assets/bbe42dcd-bae3-45e0-aa0d-65a7591a0ba9" />

## Description
GOTH (Ground Over The Horizon) is a highly optimized, high-performance Harmony injection mod for Nuclear Option that drastically buffs radar detection systems, eliminating their inability to detect high-altitude threats at long ranges. 

At the core of GOTH is the **Multi Orbital Mapping & Monitoring Yield (MOMMY)** sub-system. MOMMY dynamically adjusts the internal math of the vanilla radar engine, ensuring that both airborne and surface-based air defense systems remain highly lethal over the horizon without causing exponential math errors or tanking framerates. Because sometimes, SAM sites just need their MOMMY to help them see.

## How the Formula Works
The Nuclear Option vanilla radar relies on a "Signal Strength" equation that mimics real-world physics (the 4th-root decay law). Normally, radar systems have a hardcoded `maxRange` and a `minSignal` threshold that prevents them from detecting distant targets regardless of altitude.

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
   - The radar's raw `maxRange` is multiplied by `(detectionMult * lockMult)`.
   - A standard 30km SAM radar can easily reach over 100km if the target is high up and large enough.

4. **Aggressive MinSignal Reduction**
   - The vanilla game hardcodes the `minSignal` threshold (usually ~0.5), meaning distant weak returns are ignored.
   - GOTH aggressively reduces this threshold using a direct subtraction based on the target's RCS, and then divides it by the high-altitude multiplier:
   - `minSignal = Max(0.0001, (origMinSignal - RCS) / detectionMult)`
   - This ensures that high-altitude, large-RCS targets drop the detection threshold to near zero, guaranteeing the radar picks them up far beyond vanilla limits.

## Performance Optimization
GOTH is built for maximum frame rates. It uses zero-allocation Memory Pointers (`FieldRefAccess`) to directly manipulate the radar's physics struct without boxing/unboxing overhead, avoiding garbage collection (GC) stutter entirely. It completely eliminates hierarchy scans (`GetComponentInParent`) in the hot path.

## Configuration
By default, the mod runs silently for maximum performance. If you want to see the real-time math of every single radar ping:
1. Run the game once to generate the config file.
2. Open `BepInEx/config/com.groundoverthehorizon.cfg`.
3. Set `VerboseLogging = true`.
4. Check your BepInEx console for detailed `[RADAR]` logs.
