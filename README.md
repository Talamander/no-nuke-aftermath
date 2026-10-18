<p align="center">
  <img src="docs/media/cover.jpg" alt="A huge nuclear fireball over green hills, with a ring of vapour and a wall of dust racing out along the ground, and the title Nuke Aftermath" width="100%">
</p>

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/media/logo-dark.png">
    <img src="docs/media/logo-light.png" alt="Nuke Aftermath" width="420">
  </picture>
</p>

<p align="center">
  <b>Nuclear strikes in Nuclear Option get a far bigger blast, and they leave burnt ground, fallen forest and fires behind.</b>
</p>

<p align="center">
  <img src="docs/media/compare.gif" alt="A wipe between the stock game and Nuke Aftermath, three minutes after the same strike, from the same camera: green hills on one side, black burnt ground with fires and smoke columns on the other" width="960">
</p>

Nuke Aftermath is a free mod for [Nuclear Option](https://store.steampowered.com/app/2168680/). It is
fan-made and not affiliated with Shockfront Studios.

**Status: test build 0.2.1.** Made for Nuclear Option 0.34.2. Tested on Linux only, in an offline
Escalation mission on a stock map, with a 1.5 kt bomb (see [Before and after](#before-and-after)). Not
tested yet on Windows, in VR, in multiplayer, at night or with the bigger 20 kt and 250 kt warheads.
Please report what you see.

## What it does

**The blast**

- **A bigger, longer explosion.** The game's own fireball, mushroom cloud, stem and base cloud are
  made about 1.6 times bigger and last about 1.6 times longer.
- **A real flash.** A brighter, longer flash, a short white-out of the screen when you look at it, and
  a light pulse that lights up the ground and the clouds around the blast.
- **A dust ring.** A wall of dust races out along the ground from ground zero, then hangs and thins out.
- **A condensation ring.** A thin white ring of vapour spreads out from the fireball in the first seconds.
- **The camera shakes** when the shock wave reaches you in the cockpit. It is off in VR.

**What is left behind**

- **A large burn.** About 2 km across for a 1.5 kt bomb, with a black centre, a charred ring with
  streaks pointing away from the blast, grey ash and a ragged, soft edge.
- **A flattened forest.** The blast clears trees further out than in the stock game. Where it
  cleared them, charred trunks lie on the ground pointing away from ground zero. Near the edge, burnt
  trunks still stand, many of them snapped off.
- **Fires and smoke for minutes.** Lines of fire burn across the cleared area for up to 6 minutes, each
  with its own column of dark smoke drifting with the wind. A tall smoke column rises from ground zero,
  embers drift over the burnt ground, and what is left of the mushroom cloud drifts away slowly.
- **It stays.** The burn and the trunks stay until you leave the map.

**Nothing to set up.** There are no keys. It reacts to every nuclear warhead by itself.
**Only for you.** It changes what your game draws, nothing else. Other players do not need it, and it
cannot change damage, units or the outcome of a match.

## Before and after

The same 1.5 kt strike in the same offline Escalation mission on a stock map, filmed twice from the
same cameras at the same times after the blast: once in the stock game and once with Nuke Aftermath.
The bomb went off about 150 m above a forest. Both sides of every pair have the same colour grade with
the same numbers; only the labels are added. The sky differs a little between the two runs because the
weather clouds are not the same each time.

### The blast, 2.5 seconds in

| Stock game | Nuke Aftermath |
|---|---|
| <img src="docs/media/compare-blast-wide-stock.jpg" alt="Stock game: a small fireball over a translucent shockwave disc"> | <img src="docs/media/compare-blast-wide-mod.jpg" alt="Nuke Aftermath: a much bigger fireball with a dust wall and a ring of vapour"> |
| From about 1.9 km away. | A bigger fireball, the dust ring and the condensation ring. |
| <img src="docs/media/compare-blast-near-stock.jpg" alt="Stock game: the fireball seen from the ground"> | <img src="docs/media/compare-blast-near-mod.jpg" alt="Nuke Aftermath: the fireball seen from the ground, the ground lit orange by the flash"> |
| From the ground, about 500 m away. | The flash lights the ground orange. |

<p align="center">
  <img src="docs/media/compare-blast.gif" alt="A wipe between the stock game and Nuke Aftermath 2.5 seconds after the strike" width="720">
</p>

### Thirty seconds after

| Stock game | Nuke Aftermath |
|---|---|
| <img src="docs/media/compare-30s-wide-stock.jpg" alt="Stock game: a small dark cloud at the base of the stem over green forest"> | <img src="docs/media/compare-30s-wide-mod.jpg" alt="Nuke Aftermath: fires and dark smoke over a large black burn"> |
| <img src="docs/media/compare-30s-forest-stock.jpg" alt="Stock game: green forest beyond the blast, with some smoke"> | <img src="docs/media/compare-30s-forest-mod.jpg" alt="Nuke Aftermath: the same forest flattened and burnt, with smoke"> |
| A forest about 850 m from ground zero. In the stock game the blast does not reach it. | The same forest, blown flat and burnt. |

### Three minutes after

| Stock game | Nuke Aftermath |
|---|---|
| <img src="docs/media/compare-3min-near-stock.jpg" alt="Stock game: green hills with a small dark patch"> | <img src="docs/media/compare-3min-near-mod.jpg" alt="Nuke Aftermath: black burnt ground with lines of fire and columns of smoke"> |
| From the ground. The stock blast has left a small dark patch. | Burnt ground, fires and smoke. |
| <img src="docs/media/compare-3min-wide-stock.jpg" alt="Stock game: wide aerial view with a small cleared patch in the forest"> | <img src="docs/media/compare-3min-wide-mod.jpg" alt="Nuke Aftermath: wide aerial view with smoke columns rising from a large burnt area"> |
| From about 1.9 km away. | Easy to see from the air. |
| <img src="docs/media/compare-3min-top-stock.jpg" alt="Stock game: top-down view of the forest with a small cleared patch"> | <img src="docs/media/compare-3min-top-mod.jpg" alt="Nuke Aftermath: top-down view with smoke drifting over the burnt area"> |
| <img src="docs/media/compare-3min-forest-stock.jpg" alt="Stock game: the forest beyond the blast, untouched"> | <img src="docs/media/compare-3min-forest-mod.jpg" alt="Nuke Aftermath: the flattened forest with trunks pointing away from the blast"> |
| <img src="docs/media/compare-3min-cloud-stock.jpg" alt="Stock game: an empty sky, the mushroom cloud is gone"> | <img src="docs/media/compare-3min-cloud-mod.jpg" alt="Nuke Aftermath: the remains of the mushroom cloud above smoke columns"> |
| From 3.4 km away. After three minutes the stock cloud is gone. | The cloud still hangs over the fires. |

## Install

You need Nuclear Option from Steam.

1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) (the x64 version for Windows)
   into your Nuclear Option folder, if it is not there yet. BepInEx is the mod loader most Nuclear
   Option mods use. On Linux, also set this Steam launch option for the game:
   `WINEDLLOVERRIDES="winhttp=n,b" %command%`
2. Download `NoNukeAftermath-<version>.zip` from
   [Releases](https://github.com/Talamander/no-nuke-aftermath/releases).
3. Unzip it into your Nuclear Option folder (the folder with `NuclearOption.exe`). You should now have
   `BepInEx/plugins/NoNukeAftermath/NoNukeAftermath.dll`.
4. Start the game and play a mission with a nuclear weapon.

To check that it loaded, open `BepInEx/LogOutput.log` in your Nuclear Option folder. It should have the
line `Nuke Aftermath 0.2.1 loaded.`

If you already use other BepInEx mods, they keep working. Nuke Aftermath only adds its own folder.

To find your Nuclear Option folder: in Steam, right-click Nuclear Option, then **Manage**, then
**Browse local files**.

## Uninstall

Close the game first.

- The mod: delete `BepInEx/plugins/NoNukeAftermath` in your game folder.
- Its settings: delete `BepInEx/config/talamander.no.nukeaftermath.cfg`.
- The mod loader: if no other mod uses BepInEx, also delete `BepInEx`, `winhttp.dll`,
  `doorstop_config.ini`, `.doorstop_version` and `changelog.txt` from the game folder.

## Questions and limits

**Do other players need it?**
No. Each player who wants to see the aftermath installs it. Players without it see the stock game.
The host and dedicated servers need nothing.

**Does it work in multiplayer?**
It should, because it only changes what your own game draws, and it reacts to the same explosion
every player sees. It has not been tested in multiplayer yet.

**Can it get me out of sync or change the match?**
No. Trees in Nuclear Option have no effect on the game, and the mod does not touch buildings, units,
damage or the shock wave. The trees it clears further out are only cleared in your own game. It only
adds things to look at.

**What about performance?**
It was measured on one PC (Linux, RTX 5090, 1080p, no frame cap), with the same mission, camera and
1.5 kt nuke with and without the mod. In normal play the mod made no difference you could measure:
average frame time stayed between 3.6 and 4.1 ms (about 250 fps) before, during and after the blast,
the same as the stock game within the run-to-run noise. The one cost is the moment of the blast: the
frame the bomb goes off took about 52 to 58 ms with the mod, against about 30 to 33 ms in the stock
game, so there is one slightly bigger hitch. Slower GPUs, very large blasts and standing inside the
smoke will cost more; that has not been measured. If it is too heavy, lower `ParticleBudget` or
`MaxActiveBlasts` in the settings, or turn off the effects you do not need.

**I joined a match after a nuke went off. Why is there no burn?**
The mod only knows about explosions it saw. The stock game is the same: a player who joins late also
sees a full forest.

**Can I change it?**
Yes, in `BepInEx/config/talamander.no.nukeaftermath.cfg` (it appears after the first start). Every
effect has its own on/off switch: the bigger cloud, the flash, the screen flash, the dust ring, the
condensation ring, the lingering cloud, the camera shake, the burn, the extra tree clearing, the
trunks, the fires, the smoke column and the embers. You can also change the cloud size and how long it
lasts, the flash brightness, the shake strength, the burn size, how many trunks and fires there are,
and how long the fires burn. Under `Performance`, `ParticleBudget` scales every particle effect down
(0.5 = half) and `MaxActiveBlasts` limits how many blasts keep their particle effects at the same time.

**Something does not work.**
Open an issue on [GitHub](https://github.com/Talamander/no-nuke-aftermath/issues). Say what you did and
what went wrong, and attach `BepInEx/LogOutput.log` from your Nuclear Option folder. Attach it right
after the problem, before you start the game again, because the game rewrites it on every start.

## More mods

- [Nuclear Replay](https://github.com/Talamander/nuclear-replay): record your matches and watch them
  back from any angle.
- [Missile Cam](https://github.com/Talamander/no-missile-cam): follow your own missiles in flight with the
  camera, and look at your selected target.

## Credits and license

Made by Talamander. Nuke Aftermath is a fan-made mod. It is not made, supported or endorsed by
Shockfront Studios, the makers of Nuclear Option.

Nuke Aftermath is released under the [MIT license](LICENSE). At runtime it reuses the game's own
scorch decal and smoke, fire and steam materials; nothing from the game is copied into the mod. It
needs BepInEx, which keeps its own license.

## For developers

How it works, the test log and how to build it are in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).
