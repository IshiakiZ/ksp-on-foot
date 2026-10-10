# Which systems it works on

* **Mac:** made and tried here (macOS, the game on OpenGL).
* **Windows and Linux:** not tried; there was no such computer to try them on. Nothing in the mod is the
  Mac's own: it has no shader and no native library of its own, and what it uses of Unity (the character
  controller, the pointer's capture, cameras drawing into pictures) is the same everywhere.

  One thing to watch for there: the assembly building's walls are found by drawing them with Unity's own
  depth shader (`Hidden/Internal-DepthNormalsTexture`). It is one of the shaders Unity puts in every game
  for its own use, so it should be in the Windows game too. If it is not, the mod notices, says so in the
  log ("walls set at the shell's sides"), and puts the walls at the sides of the building's outline
  instead, which still keeps a walker in and the doorway open, but less exactly.
* The keys: Option-W on a Mac is Alt-W elsewhere (the game's own modifier key, whatever it is set to).
