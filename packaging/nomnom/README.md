# NOMNOM entry

`NoNukeAftermath.json` is the entry for [NOMNOM](https://github.com/KopterBuzz/NOMNOM), the registry that
NOMM (Nuclear Option Mod Manager) installs mods from. It follows NOMNOM's `SCHEMA.md` and passes its
`ValidationSchema.json` and the content checks of `Validate-JsonContent.ps1` (file name equals `id`,
id not taken, https URLs, archive file names, numeric versions). Submitted for 0.2.0 on 2026-10-14.

## Choices

- `id` `NoNukeAftermath`: NOMNOM asks for the plugin's assembly name. It is also the folder a manual
  install uses (`BepInEx/plugins/NoNukeAftermath/`), so NOMM and a manual install share one folder.
- `isClientOrServer` `Client`: only the player who wants the bigger blast installs it. It sends no
  network messages, adds no colliders and changes no damage or gameplay values. The extra tree clearing
  and ground tint exist only on that player's screen.
- `tags`: `mod`, `Visual` and `Flavor`, as other visual mods in NOMNOM use.
- `imageUrl` and `imageHash`: the icon, `docs/media/icon-512.png` (PNG, 512x512, NOMNOM's limit), as a
  raw.githubusercontent.com URL pinned to a commit so the file behind it never changes. `imageHash` is
  the file's SHA-256 as plain lowercase hex with no `sha256:` prefix, the format NOMM's
  `PreviewImageLoader.kt` compares. To change the icon after the merge, use the "Update Mod Image URL"
  issue template (`HOWTO_UPDATE_MODIMAGE.md` in NOMNOM) with a new pinned URL.
- No `dependencies`: NOMM installs BepInEx 5 itself, and the plugin needs nothing else.
- `gameVersion` `0.34.2`: the game build 0.2.0 was tested on, which reports Early Access 0.34.2 in the
  main menu (its Unity player version string still reads 0.34.1).
- Only 0.2.0 is listed. 0.1.0 was a pre-release that stopped working on a default BepInEx install.

## How NOMM installs it

- NOMM downloads the artifact's `downloadUrl`, checks it against `hash`, unpacks the whole archive into
  `BepInEx/disabledPlugins/<id>/` and moves the folder to `BepInEx/plugins/<id>/` when the mod is turned on.
- The archive is unpacked as it is, with no folder flattening. The game-folder zip, which has
  `BepInEx/plugins/NoNukeAftermath/` inside, would nest. That is why each release has two zips, and NOMM
  uses `NoNukeAftermath-<version>-plugins.zip`, which has its files at the root.
- `autoUpdateArtifacts` `"True"`: NOMNOM's hourly job reads the **first** asset of each new GitHub
  release (`Update-ModArtifact.ps1`) and turns the tag (`v0.2.1` becomes `0.2.1`) into the artifact
  version. GitHub lists release assets by name, not upload order, and `-plugins.zip` sorts before `.zip`,
  so the mod-manager zip is always first. Keep the tag numeric, matching `Plugin.Version` and the csproj
  `<Version>`. `scripts/package.py` builds both zips and checks that the two versions match.
- The 0.2.0 `-plugins.zip` was added to the release on 2026-10-14. It holds the exact files of the
  game-folder zip (same DLL, sha256 `599944cf...b1bb`), without the folders.
