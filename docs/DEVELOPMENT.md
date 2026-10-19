# Nuke Aftermath: developer notes

How the mod works, what it builds on in the stock game, and how it was tested. The player guide is the
[README](../README.md).

## What the stock game already does (code evidence, decompiled 0.34.1 client, build `cb745d6c44f1`)

- Detonation reaches every client. The server calls `Missile.Detonate`, which sends the `[ClientRpc] RpcDetonate`. Its body `Missile.UserCode_RpcDetonate_897349600` runs on every observer and on the host, and calls `Missile.Warhead.Detonate`, which spawns the effect prefab locally.
- `Shockwave.Start` (all clients) sets `blastRadius = (yieldKg)^(1/3) * 13`. It puts down a dust/shockwave ground decal that always fades and is destroyed about 10 s after the front passes. Above 10 t TNT it calls `BlastManager.AddBlast(pos, blastRadius / 2)`.
- Trees are GPU-only (`TreeRenderer`, `Graphics.RenderMeshIndirect`, no GameObjects or colliders). `DetailRenderer.LateUpdate` drains the blasts. It paints a coarse map-wide blast texture (`_Global_BlastMap`, about 160 m per texel) and dispatches the `clearShader` that removes trees within `0.5 * blastRadius`. The tree shader also tints trees toward `_BlastColor` from that blast map.
- Gameplay damage is server-only (`Shockwave.InfluencedObject.HasShockwaveReached` checks `Server.Active`). Trees carry no gameplay state, so changing tree visuals cannot desync anything. Buildings are server-authoritative; this mod does not touch them.
- None of this is replicated. A player who joins after a blast sees no blast map and a full forest.

## What this mod adds (0.2.0)

Everything hangs off one Harmony postfix on `Missile.UserCode_RpcDetonate_897349600` (armed, yield at or
above `MinYieldKg`), so it runs on every client that has the mod and sees the detonation. By then
`Warhead.Detonate` has already instantiated the stock effect prefab (`explosion_1kt` for 1.5 kt) under
`Datum.origin`, but its `MushroomCloud.Start` has not run yet, so a cloud with `blastScale == 0` is the
fresh one. All objects are children of `Datum.origin` and follow the floating origin. Particle systems
use custom simulation space on `Datum.origin`, so emit positions are in global (map) coordinates.

### Stock blast tweaks (`StockBlastTweaks.cs`)

On the fresh `MushroomCloud`, before its `Start`, through Traverse:

- `yield` times `CloudScale`^3, which the cloud uses for its own scale.
- Cloud: `horizontalSizeOverTime` times the scale, rise rate times its square root, particle rate times 1.6.
- Fireball: size and emission curves replaced: it grows to `CloudScale` times the stock end size and
  keeps emitting until 1.4 s.
- Base cloud radius, stem size and rates scaled up; lens flare values and times doubled.
- `TimedDeletion.deleteDelay` (read in `Start`) times `CloudLifetimeScale`: 300 s to 480 s.
- `LightAnimation`: 1.8 times the intensity, curve times stretched 2.2 times.

A postfix on `MushroomCloud.Start` stretches the timelines of the cloud and stem by `CloudLifetimeScale`
(their `lifetime` fields and the systems' `main.duration`). The base cloud keeps its stock lifetime:
stretched, it lay on the terrain as a flat grey sheet for minutes. The base cloud and stem renderers get
per-instance materials with a 150 m soft-particle fade for the same reason.

### Runtime-built effects (`BlastFx.cs`, `GroundFires.cs`, `Fx.cs`)

No asset bundle. Every effect is a `ParticleSystem` built in code with a clone of a stock particle
material found by name among loaded materials (`smoke_background`, `smoke_midground_turbulent`, `steam`,
`fire_small1`, `sparks`). The clones are made once per session (`Fx.Prewarm`). Smoke clones get a 60 m
soft-particle fade and a 20 to 180 m camera fade. The systems are built one per frame after the blast
frame. Particles are emitted by script (`ParticleSystem.Emit(EmitParams)`). Note: with a
size-over-lifetime curve that ends at 1, `EmitParams.startSize` is the final size; tuning against that
fixed an early "popcorn" look of small separate puffs.

- **Flash**: a point light 0.25 R above the burst, range 9 R, peak `2.5e7 * FlashBrightness * (R/1487)^2`,
  a sharp rise then a fast and a slow decay, white to orange over 4 s, gone after 14 s. Screen flash:
  `OnGUI` full-screen white quad, strength from distance and view direction, under half a second; off in VR.
- **Dust ring (base surge)**: 1,000 tan particles from 0.08 to 0.2 R at ground level, 130 to 210 m/s
  outward with drag, 170 to 300 m, 20 to 32 s life; plus a billowing skirt for 7 s.
- **Condensation ring**: 1,400 translucent steam particles at 0.45 s, 120 to 340 m/s outward with
  heavy drag, 1.8 to 3.4 s life.
- **Lingering cloud**: from 22 s, large soft puffs at the cap drift with the wind (`Fx.Wind`) and fade
  over `LingeringCloudMinutes`; from 30 s, the same for the upper stem.
- **Camera shake**: `CameraStateManager.ShakeCamera` when `340 m/s * t` reaches the camera, scaled by
  distance, for 3 s. It only acts in the cockpit view. Off in VR (XR device active, or a VR plugin loaded).
- **Fires** (after the tree readback): up to `MaxFires` plus a third more on open ground, spread over
  angular sectors of the cleared clumps. Each fire is a ragged line about 90 m long across the blast
  direction: small dimmed `fire_small1` flames, one dense plume of dark smoke drifting with the wind,
  burning 55 to 100 % of `FireMinutes`. A smoke column rises from ground zero from 20 s. Embers drift
  over the cleared area for 3 minutes.

### Aftermath (`BurnDecal.cs`, `TrunkMeshBuilder.cs`, `Plugin.cs`)

1. **Burn**: the game's `GameAssets.scorchMarkDecal` prefab (a URP `DecalProjector`, "crater" shader
   graph), with its `crater_b` texture swapped for a 1024x1024 procedural burn generated at session
   start (about 0.7 s): black core, charred ring with radial streaks, ash patches, singed edge, ragged
   feathered alpha. `crater_n` is swapped for a flat normal map. Radius `BurnRadiusFactor * R` (1,041 m
   for 1.5 kt), random spin, draw distance 30 km. Not handed to `EffectManager`, so it lasts until the
   map unloads.
2. **Extra tree clearing**: one more `BlastManager.AddBlast(global, TreeClearFactor * R)`, visual
   only: it clears trees to 1,116 m instead of 744 m. The shock wave and damage are untouched.
3. **Charred trunks**: 10 frames after the blast, one `AsyncGPUReadback` of each
   `TreeRenderer.treePositionBuffer`. Positions that changed near ground zero, compared with
   `PositionData`, are the cleared trees. Each cleared clump gets `TrunksPerClump` (16) procedural
   trunks dropped onto the ground with a raycast. Inside `FlattenFraction` of the clear radius they lie
   flat pointing away from ground zero, some broken in two; further out more and more stand as leaning
   snags, many snapped, with branch stubs. A four-tone bark atlas (charcoal, black-brown, grey ash,
   scorched bark) on URP Lit. One mesh per 512 m cell, built one cell per frame, no colliders, seeded per
   tree so every client draws the same trunks.
4. Session reset on `DetailRenderer.Start` (new map or mission).
5. **Runways and taxiways (0.2.2, `PavedAreas.cs`)**: a player reported that after a strike the runways
   and taxiways were black, so the pavement edge could not be seen and aircraft taxied into the grass.
   On the blast frame the mod collects every static runway (`Airbase.runways`: `Start`, `End`,
   `GetWidth()`) and taxiway (`Airbase.GetTaxiNetwork().roads`, polylines without a width, drawn
   30 m wide) of every registered airbase (`FactionRegistry.airbaseLookup`) within reach of the
   blast. Runways with a `Rigidbody` (carriers) are skipped. A worker thread builds a copy of the burn
   texture with the alpha on those strips cut to `PavedBurn` (default 0.3) and a 12 m soft edge, in the
   decal's own texture axes (projector right = u, up = v); it is swapped onto the burn material a few
   frames later (about 0.1 s, under the fireball) with a 3 ms upload on the main thread. Burns with no
   pavement inside keep the shared texture. Fire lines are not placed within 70 m of a strip and
   trunks not within 26 m.

### Plugin lifetime fix

BepInEx 5 adds plugins to its manager object. With `HideManagerGameObject = false` (the default), the
game's scene loads destroyed that object and the plugin with it, so 0.1.0 only worked next to a plugin
that hid the manager (Nuclear Replay does). 0.2.0 sets `HideAndDontSave` on its own GameObject in `Awake`
unless it is already `DontSave`.

## Client/server requirements

- Each player who wants the visuals installs it. Players without it see stock visuals, with no desync risk. A dedicated server needs nothing (`DetailRenderer` is off when headless, and the plugin returns early).
- Late joiners would need the host to remember and send past detonations. That needs an optional host-side companion plus a client message, and every client would have to install it. Not built.

## Test log

### 2026-10-19, 0.2.2 runway test (Linux, Proton, Nuclear Option 0.34.2, same build as the main install)

Isolated copy, offline Escalation host on Heartland, only Nuke Aftermath and a test-only plugin loaded.
The test plugin picked the longest static runway (`airbase_boscali_north` Runway 11, 2,416 m by 60 m),
dropped the 1.5 kt GPO-N 400 m beside its middle and took stills at +45 s and +100 s from a top camera
(950 m up), a near camera and a pilot's eye on the runway. Three runs: `PavedBurn` 1 (the 0.2.1 look),
0.2 with a 25 m edge, and 0.3 with a 12 m edge (shipped default).

```
[Info   :Nuke Aftermath] Paved strips near the blast: 1 runways, 12 taxiways.
[Info   :Nuke Aftermath] Burn: radius 1041 m, relief 27 m, depth 729 m.
[Info   :Nuke Aftermath] Blast frame work: 15.5 ms.
[Info   :Nuke Aftermath] Burn: 1 runways and 12 taxiways kept at 30 % burn (ready after 90 ms, upload 4.8 ms on the main thread).
```

- `PavedBurn` 1 reproduces the report: from the runway the pavement is as black as the grass and its
  edge cannot be seen.
- 0.3: the runway and taxiways keep their grey with light soot, the grass right up to their edges stays
  black, so the edge is sharp. The mask lines up with the pavement in all views (texture axes confirmed).
- A first version built the mask on the blast frame and cost 56 ms there; the worker thread brought the
  blast frame back to 15 to 19 ms (0.2.1: 14.5 ms in the same run).
- The burn is not drawn from cameras about 1.3 km or more from the burn centre in this test (no burn at
  all in the first set of far shots, with or without the change). Not investigated; likely the URP
  decal feature's own draw distance.

### 2026-10-12, 0.2.0 live A/B test (Linux, Proton, Nuclear Option 0.34.1, build `cb745d6c44f1`)

Method change from 0.1.0: the replay viewer needs Steam running, so 0.2.0 was tested in a live offline
mission instead. A separate, headless copy of the game hosted the built-in Escalation mission (stock
Heartland map) offline. A test-only BepInEx plugin (never shipped) picked the densest forest as ground
zero, Global(14988.2, 155.6, 17453.9), dropped a 1.5 kt GPO-N 150 m above it, paused and resumed the
game, and locked the camera to fixed poses relative to ground zero. The same script ran twice: once
without the mod (stock) and once with it, so both runs show the same moment from the same cameras:
before the blast, +2.5 s, +30 s and +3 min, from wide (about 1.9 km), near (about 0.5 km, inside the
burn), top, forest-edge and cloud (about 3.4 km) cameras. Stills used one colour grade with the same
numbers on both sides; the video is ungraded on both sides.

Standalone proof: only two plugins were loaded (the test plugin and Nuke Aftermath; no Nuclear Replay),
with `HideManagerGameObject = false` in `BepInEx.cfg`. Log lines from the final build
(`BepInEx/LogOutput.log`; the test plugin's lines are marked "dev harness"):

```
[Info   :Nuke Aftermath] Nuke Aftermath 0.2.0 loaded.
[Info   :Nuke Aftermath] Burn texture generated in 664 ms.
[Info   :Nuke Aftermath] Effect materials ready: 5 of 5 stock materials found.
[Info   :Nuke Aftermath] Nuke Aftermath 0.2.0: new map session, plugin alive.
[Info   :Nuke Aftermath dev harness (test only)] NA live: mission running after 2.74 s; Nuke Aftermath instance ALIVE, manager object 'BepInEx_Manager' hideFlags HideAndDontSave
[Info   :Nuke Aftermath] Nuclear detonation 1500.0 t at Global(14988.2,302.1,17453.9); blast radius 1487 m, tree clear radius 1116 m (stock 744 m). Blast 1 this session.
[Info   :Nuke Aftermath] Stock cloud: size x1.60, lifetime x1.60, deletion 300 s -> 480 s.
[Info   :Nuke Aftermath] Burn: radius 1041 m.
[Info   :Nuke Aftermath] Blast frame work: 11.9 ms.
[Info   :Nuke Aftermath] Renderer 0: 549864 trees, 622 newly cleared near ground zero, 0 changed elsewhere.
[Info   :Nuke Aftermath] Ground fires: 18 fires for 6.0 min, smoke column, embers.
[Info   :Nuke Aftermath] Charred trees: 622 clumps, 16 trunks each.
[Info   :Nuke Aftermath dev harness (test only)] NA live: phase nuked; Nuke Aftermath instance ALIVE, manager object 'BepInEx_Manager' hideFlags HideAndDontSave; scene GameWorld
```

No warnings or exceptions from the plugin.

Findings:

- At +3 minutes the stock game shows no mushroom cloud and no smoke column at all: the stock cloud has
  faded by then. The ground already looks the same as before, apart from the cleared trees. With the mod
  the cloud is still there (fading), a smoke column rises from ground zero, and the fire lines burn with
  dark smoke over a black burn with flattened trunks.
- Problems found and fixed during the test: small separate smoke puffs ("popcorn", a size-semantics
  mistake), a row of light puffs left by the late dust ring, flat grey sheets on the ground (the stock
  base cloud with a stretched lifetime, and a hard-edged stem material), and flame emission that grew
  darker with every blast (a shared cached material).
- Look questions left for review: from the near camera the condensation ring reads as a band of puffs;
  at 3 minutes the fire smoke is heavy and dark and hides much of the burn and trunks from the top
  camera.

#### Frame time

Measured with the test plugin's per-frame log (`Time.unscaledDeltaTime`), frame cap off, 1080p, RTX
5090, from the wide camera and from the near camera inside the burn. Mean / 95th percentile in ms; time
from the detonation frame. "Blast frame" is the detonation frame and the frame after it.

| Run | Before | 0-10 s | 10-60 s | 60-200 s | Blast frame |
| --- | --- | --- | --- | --- | --- |
| Stock, wide | 3.72 / 5.14 | 3.78 / 5.16 | 3.97 / 5.84 | 4.57 / 7.42 | 29.9, 13.3 |
| Mod, wide | 3.68 / 5.07 | 3.85 / 5.15 | 3.75 / 5.04 | 3.98 / 5.54 | 52.2, 18.7 |
| Stock, near | 3.65 / 4.96 | 3.75 / 5.06 | 3.71 / 5.05 | 4.03 / 5.78 | 32.8, 11.7 |
| Mod, near | 3.64 / 5.00 | 3.83 / 5.14 | 3.91 / 5.59 | 4.10 / 6.02 | 58.0, 19.0 |

- Away from the blast frame the mod is within run-to-run noise of the stock game (the stock wide run
  was slower than the mod run after 60 s).
- The blast frame costs about 20 to 25 ms more than stock. The plugin's own work on that frame is 12 ms
  (`Blast frame work`: the stock-cloud changes, the burn decal, the extra tree clearing, the flash light and the first particle systems; not broken down further).
  The remaining systems are built one per frame afterwards, and the effect materials are cloned once at
  session start. An earlier build without that was 59 ms.
- The PC was shared with other jobs. Three bursts of 30 to 70 ms frames in the mod runs (wide 53-58 s,
  near 69-88 s and 158-167 s) matched, to the second, a Blender render and a decompile job started by
  other programs; those windows are left out of the table. The stock runs had no such bursts. A run
  on an idle PC would make this cleaner.
- Not measured: slower GPUs, larger yields, several blasts, standing inside the fire smoke, VR.

### 2026-10-12, first in-game test of 0.1.0 (Linux, Proton, Nuclear Option 0.34.1, build `cb745d6c44f1`)

Method: a real recorded match (Confrontation, with a 1.5 kt GPO-N strike over a forested valley) was
played back twice in a separate, headless copy of the game with Nuclear Replay 0.5.0. Run 1 had no
Nuke Aftermath; run 2 had the 0.1.0 release build (`NoNukeAftermath-0.1.0.zip`, sha256 `48c4de30...`).
A test-only BepInEx plugin locked the replay's free camera to fixed world positions, so both runs were
filmed from exactly the same four cameras at the same replay times: before the blast, about 2.5 s
after, about 30 s after and about 3 minutes after. Both runs used one colour grade with the same numbers.

The replay runs the detonation through the same `Missile.UserCode_RpcDetonate_897349600` path as a live
match, on the local host.

Log lines from run 2 (`BepInEx/LogOutput.log`):

```
[Info   :Nuke Aftermath] Nuke Aftermath 0.1.0 loaded.
[Info   :Nuke Aftermath] Nuclear detonation 1500.0 t at Global(19241.9,242.8,9859.6); blast radius 1487 m, tree clear radius 744 m.
[Info   :Nuke Aftermath] Renderer 0: 549864 trees, 58 newly cleared near ground zero, 0 changed elsewhere (earlier blasts or compaction).
```

Scene objects after the blast: one `NukeAftermath_Scorch` decal (active, at the ground point under the
burst, 150 m below it) and six `NukeAftermath_TrunkCell` meshes. No warnings or exceptions from the
plugin.

Findings:

- The clear shader edits tree positions in place: `0 changed elsewhere` on the first blast, so the trunk
  placement method works.
- The stock game already leaves a small dark scorch at ground zero, and it was still there after 3
  minutes. The mod's scorch is much larger (radius `0.45 * blastRadius`, about 670 m here) and covers
  the whole cleared area.
- The trunks show up as thin charcoal logs lying on the scorched ground, plus a few standing snags
  towards the edge of the cleared area.
- The scorch was still in place after 3 minutes. Longer persistence (until the map unloads) is by
  design but was not watched.

Not tested yet: Windows, a live mission, a multiplayer client, TerrainLab maps, several blasts in one
session, and frame time. The headless test ran at its 60 fps cap when the shots were taken, but
frame time around the blast was not measured.

## Performance notes

- See the 0.2.0 frame-time table above. Particle counts scale with `ParticleBudget`; `MaxActiveBlasts`
  stops the particle effects of the oldest blasts when there are too many. The burn and trunks stay.
- Trunks for a 10 kt blast: blast radius about 2,800 m, tree clear radius about 2,100 m with the extra
  clearing. At dense forest that can be tens of thousands of trunks, built one 512 m cell per frame;
  `MaxTrunksPerBlast` caps it. Not measured.
- One 7 MB GPU readback per blast per tree renderer.
- One large decal. URP decal cost scales with screen coverage.

## Open questions

- Several blasts in one session: untested with 0.2.0 (the fires, trunks and burn are per blast;
  `MaxActiveBlasts` caps the particles).
- 20 kt and 250 kt yields: the effects scale with the blast radius, but only 1.5 kt was watched.
- Night: the flash light and the emissive flames were only seen in daylight.
- VR: the screen flash and camera shake switch off when an XR device is active or a VR plugin is
  loaded. That logic has not been run in VR.
- Multiplayer clients and Windows: untested. The code path (`RpcDetonate` on every client) is the same
  as the host's.
- TerrainLab and other custom maps: untested; the burn decal on very steep relief is untested.
- Persistence: the burn and trunks stay until the map unloads by design; the fires end after
  `FireMinutes`. Nothing was watched past about 3.5 minutes.
- The stock scorch material stretched to kilometres reads as a soft dark burn; an original decal
  texture could add more detail.

## Build

```
dotnet build -c Release -p:GameManagedDir="<game folder>/NuclearOption_Data/Managed"
```

The game's DLLs are compile references only and are not copied or redistributed. BepInEx and HarmonyX come from the official NuGet feed as compile references only; HarmonyX ships with BepInEx 5 at runtime. The dedicated server's `NuclearOptionServer_Data/Managed` folder also works for a type check (same build hash).
