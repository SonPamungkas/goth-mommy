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

## Performance & Mod Optimization
GOTH is built for maximum frame rates. It uses zero-allocation Memory Pointers (`FieldRefAccess`) to directly manipulate the radar's physics struct without boxing/unboxing overhead, avoiding garbage collection (GC) stutter entirely. It completely eliminates hierarchy scans (`GetComponentInParent`) in the hot path. With the added bonus of compatibility with any modded and future radar-emitting unit.

## Configuration
By default, the mod gives all surface radars a 50km optical range and runs the math silently for maximum performance. If you want to customize the values:
1. Run the game once to generate the config file.
2. Open `BepInEx/config/com.groundoverthehorizon.cfg`.
3. Tweak the global optics values (`VisualRange`, `Magnification`, `MaxSpeed`) to your liking.
4. Set `VerboseLogging = true` to see the real-time math of every radar ping.
5. Check your BepInEx console for detailed `[MOMMY]` logs.
