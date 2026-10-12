# Nuke Aftermath: developer notes

How the mod works, what it builds on in the stock game, and how it was tested. The player guide is the
[README](../README.md).

## What the stock game already does (code evidence, decompiled 0.34.1 client, build `cb745d6c44f1`)

- Detonation reaches every client. The server calls `Missile.Detonate`, which sends the `[ClientRpc] RpcDetonate`. Its body `Missile.UserCode_RpcDetonate_897349600` runs on every observer and on the host, and calls `Missile.Warhead.Detonate`, which spawns the effect prefab locally.
- `Shockwave.Start` (all clients) sets `blastRadius = (yieldKg)^(1/3) * 13`. It puts down a dust/shockwave ground decal that always fades and is destroyed about 10 s after the front passes. Above 10 t TNT it calls `BlastManager.AddBlast(pos, blastRadius / 2)`.
- Trees are GPU-only (`TreeRenderer`, `Graphics.RenderMeshIndirect`, no GameObjects or colliders). `DetailRenderer.LateUpdate` drains the blasts. It paints a coarse map-wide blast texture (`_Global_BlastMap`, about 160 m per texel) and dispatches the `clearShader` that removes trees within `0.5 * blastRadius`. The tree shader also tints trees toward `_BlastColor` from that blast map.
- Gameplay damage is server-only (`Shockwave.InfluencedObject.HasShockwaveReached` checks `Server.Active`). Trees carry no gameplay state, so changing tree visuals cannot desync anything. Buildings are server-authoritative; this mod does not touch them.
- None of this is replicated. A player who joins after a blast sees no blast map and a full forest.

## What this mod adds

1. **Scorch**: one `DecalProjector` from the game's own `GameAssets.scorchMarkDecal` prefab at the ground point under the blast, radius `0.45 * blastRadius`, draw distance 30 km. It is not handed to `EffectManager`, which keeps only 20 effects, so it lasts until the map unloads. Like the stock shockwave decal, it is skipped for an airburst higher than half the blast radius.
2. **Charred trunks**: six frames after the blast, one `AsyncGPUReadback` of each `TreeRenderer.treePositionBuffer`. Positions that changed near ground zero, compared with `PositionData`, are exactly the trees the stock clear removed. For each one it draws 7 procedural charcoal trunks. Inside 55 % of the clear radius they lie flat, pointing away from ground zero; further out they stand as leaning snags. The trunks are merged into one mesh per 512 m cell, with no colliders. Placement is seeded per tree, so every client with the mod draws the same trunks.
3. Session reset on `DetailRenderer.Start` (new map or mission). All objects are children of `Datum.origin`, so they follow the floating origin.

Stock maps and TerrainLab maps (Ironjaw Sound) use the same path: TerrainLabNOPatch keeps a cloned stock `TreeRenderer` with the stock `clearShader`, and only swaps `PositionData`, mesh and look.

## Client/server requirements

- Each player who wants the visuals installs it. Players without it see stock visuals, with no desync risk. A dedicated server needs nothing (`DetailRenderer` is off when headless, and the plugin returns early).
- Late joiners would need the host to remember and send past detonations. That needs an optional host-side companion plus a client message, and every client would have to install it. Not built.

## Test log

### 2026-10-12, first in-game test (Linux, Proton, Nuclear Option 0.34.1, build `cb745d6c44f1`)

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

## Performance estimate (not measured)

- A 10 kt blast: blast radius about 2,800 m, tree clear radius about 1,400 m, about 6 km². At Ironjaw density (about 1,000 clumps per km²) that is about 6,000 clumps, 42,000 trunks and 420k triangles in about 40 cell meshes, built once on the main thread. `MaxCharredClumps` caps the total at 40,000 clumps.
- One 7 MB GPU readback per blast per tree renderer.
- One large decal. URP decal cost scales with screen coverage.

## Open questions

- Answered on 2026-10-12: the `clearShader` edits positions in place (`0 changed elsewhere` on the first blast), so the diff finds the right trees. Several blasts in one session are still untested.
- The look of the stock scorch material stretched to kilometres. On 2026-10-12 it read as a broad, soft dark burn on a stock map; an original decal texture could add more detail.
- Answered on 2026-10-12: URP Lit is in the build. No fallback was logged and the trunks rendered.
- Which URP decal technique is active. The decal covered the valley and ridges of the test site; very steep relief is untested.

## Explosion visuals (next step, not started)

The mushroom cloud, shockwave and audio are prefabs on each missile's serialized `Warhead` fields, all local. Better explosion visuals need original particle assets: a lingering stem and cloud, a ground dust ring, ground fires and smoke over the scorch. Those need a Unity batch-mode bundle. The Unity lock (`terrainlab-unity.lock`) is shared with the maps work, so this waits until after the V9/Escalation release.

## Build

```
dotnet build -c Release -p:GameManagedDir="<game folder>/NuclearOption_Data/Managed"
```

The game's DLLs are compile references only and are not copied or redistributed. BepInEx and HarmonyX come from the official NuGet feed as compile references only; HarmonyX ships with BepInEx 5 at runtime. The dedicated server's `NuclearOptionServer_Data/Managed` folder also works for a type check (same build hash).
