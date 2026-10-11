# Building

## The crew car's tug

The baggage tug is made by `src/OnFoot/Model/tug.py` in Blender (made with 5.2 LTS), run without its window:

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup \
        --python src/OnFoot/Model/tug.py -- src/OnFoot/PluginData

(elsewhere, the `blender` program with the same arguments). It needs nothing but Blender: no add-on, nothing
downloaded. It takes about a minute and a half on eight cores and writes `tug.mesh` and five PNG pictures into
the folder given, which the build copies into `GameData/OnFoot/PluginData/`. Players need none of this: the
files ship with the mod.

* `--quick` bakes with fewer samples at half the size (for trying a change; about half a minute).
* `--render FOLDER` also draws pictures of the finished model from four sides, with kerbal-sized stand-ins,
  and of the booster seat with nobody on it, into `FOLDER`.
* `--seat X,Y,Z` and `--deck H` put the driver's seat pivot and the load deck where the car in the game has
  them (metres: right, forward, up from the ground midway between the axles; the defaults are what the car
  has, `0,0.337,1.266` with its seat raised for the booster, and `0.842`). How far the booster lifts the
  driver (`BOOST`, 0.15 m) and where the steering wheel is from the seat pivot (`WHEEL`) are at the top of
  the script; the craft's seat must be raised by the same `BOOST`.

The model is about 35,900 triangles as drawn for one car (the body 19,600, the cab's inside with its seat,
booster and steering wheel 4,300, the four tyres 3,000 each, the deck 200), in 13 draws. Its pictures: the paint 2048 pixels square (with its gloss in
alpha), the tyres' paint and normal map 512, the deck's tread plate 512 (tiled every 0.3 m). In the game the
pictures are squeezed (DXT5) with their smaller sizes, about 7 MB of video memory in all.

## The mod

`./build.sh` builds the mod and installs it into the game; `./build.sh check` only compiles; `./build.sh dist`
builds into `GameData/` here without touching the game. It needs the .NET SDK, a copy of the game (`KSP_DIR=/path`
if it is not where Steam puts it), and `Keystone.dll` from [Keystone](https://github.com/IshiakiZ/ksp-keystone):
the copy installed in the game, a built copy of that repository beside this one, or `KEYSTONE=/path/to/Keystone.dll`.

This repository is made from a workspace that holds several mods side by side; changes arrive here from there.
