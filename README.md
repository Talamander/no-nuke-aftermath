# Nuke Aftermath (Nuclear Option, BepInEx 5)

Client-only, visual-only nuclear aftermath: a persistent scorch at ground zero and charred, blown-down trunks where the blast removed trees. Status: **code written and type-checked only. Not installed, not run in the game, not tested.**

Videogame visual effects only.

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

## Performance estimate (not measured)

- A 10 kt blast: blast radius about 2,800 m, tree clear radius about 1,400 m, about 6 km². At Ironjaw density (about 1,000 clumps per km²) that is about 6,000 clumps, 42,000 trunks and 420k triangles in about 40 cell meshes, built once on the main thread. `MaxCharredClumps` caps the total at 40,000 clumps.
- One 7 MB GPU readback per blast per tree renderer.
- One large decal. URP decal cost scales with screen coverage.

## Unknowns that need one controlled game test

- Whether the `clearShader` edits positions in place, which the diff relies on, or compacts the buffer. The log line `changed elsewhere` shows it: a large number means compaction, and the trunk placement would be wrong.
- The look of the stock scorch material stretched to kilometres. A dedicated original decal texture may be needed.
- Whether URP Lit without instancing is in the build. Likely, because stock materials use it; the plugin logs a fallback otherwise.
- Which URP decal technique is active, and how the decal reads on steep relief.

## Explosion visuals (next step, not started)

The mushroom cloud, shockwave and audio are prefabs on each missile's serialized `Warhead` fields, all local. Better explosion visuals need original particle assets: a lingering stem and cloud, a ground dust ring, ground fires and smoke over the scorch. Those need a Unity batch-mode bundle. The Unity lock (`terrainlab-unity.lock`) is shared with the maps work, so this waits until after the V9/Escalation release.

## Build

```
dotnet build -c Release                     # against the client install (needs /mnt/games)
dotnet build -c Release -p:GameManagedDir=$HOME/Games/nuclear-option-server/NuclearOptionServer_Data/Managed
```

The second form only type-checks against the dedicated server DLLs (same build hash). BepInEx and HarmonyX come from the official NuGet feed as compile references only.

Do not install into the Steam game, `/mnt/games/no-maptest` or `/mnt/games/no-friendtest` without coordinating first.
