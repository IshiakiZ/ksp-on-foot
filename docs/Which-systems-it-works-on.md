# Which systems it works on

* **Mac:** made and tried here (macOS, the game on OpenGL).
* **Windows:** tried on Windows 11 (KSP 1.12.5, Direct3D 11) on 2026-10-10: walking in the assembly building and
  into the hangar from the space centre, the crew car, and with the keys pressed on the keyboard, running (Shift
  and W: 6.8 m in a second and a half, against 2.4 m walking) and jumping (Space: the feet half a metre up, 0.63 s
  in the air, twice). The walls were found by the depth shader ("walls measured: 150 slabs, 125 m of doorway").
* **Linux:** not tried; there was no such computer to try it on. Nothing in the mod is the
  Mac's own: it has no shader and no native library of its own, and what it uses of Unity (the character
  controller, the pointer's capture, cameras drawing into pictures) is the same everywhere.

  One thing to watch for there: the assembly building's walls are found by drawing them with Unity's own
  depth shader (`Hidden/Internal-DepthNormalsTexture`). It is one of the shaders Unity puts in every game
  for its own use, so it should be in the Linux game too, as it was in the Windows one. If it is not, the mod notices, says so in the
  log ("walls set at the shell's sides"), and puts the walls at the sides of the building's outline
  instead, which still keeps a walker in and the doorway open, but less exactly.
* The crew car's tug is drawn with the game's own part shaders (`KSP/Specular`, `KSP/Bumped Specular`,
  `KSP/Emissive/Specular`), which every copy of the game has on every system; its meshes are read from a
  file of the mod's own and its pictures from PNG files with Unity's own reader, and squeezed with Unity's
  own `Texture2D.Compress`. Its normal maps are laid out so that either way the game's shaders may read one
  (x from alpha alone, or from red times alpha) comes out right. Nothing of it is the Mac's own; it has been
  seen only on the Mac (OpenGL).
* The keys: Option-W on a Mac is Alt-W elsewhere (the game's own modifier key, whatever it is set to).
