# What was tested

On 2026-10-09, on a Mac (KSP 1.12.5, both expansions, Scatterer, EVE, TUFX and Waterfall installed), in a
sandbox game with every building at its top level, on the development build. The walking was driven by a
script through the game's remote control (forward, run, turn, look, jump), not by hand.

**In the assembly building** (with a Kerbal X):

* Setting off: the view came down from the editor's camera to the floor under it in 0.7 s, at a kerbal's
  height, among the ground crew. Walking 4 s went 6.4 m (1.6 m/s), running 8 s went 36 m (4.5 m/s), a jump
  rose 0.5 m and came down.
* Solid: running at the back wall stopped 3.6 m short of the shell's outline, where the measured wall is;
  running at a side stopped at the gantries and the measured wall 31 m out (twice, at the two places a
  map of what is solid had shown gaps in the line); the launch clamps' girders, which are solid in the
  game already, and a crew member's capsule stopped lines drawn towards the craft. The rest of a Kerbal X
  stands above a kerbal's head on its clamps, and can be walked under.
* Out of the door: running east from the middle went out through the doorway onto the crawlerway (to 54 m
  from the middle, the doorway being at 41), the launch pad ahead. Looking back shows the hall through the
  open door; the building's outer walls are not in the editor's scene (sky shows beside the door frame).
* Making it solid took 50 to 110 ms: 65 of the building's pieces given colliders (33 had their own), the
  shell's walls measured as 150 slabs with 125 m of openings, 618 of the craft's shapes copied, 40 of the
  ground crew.

**In the hangar** (with an Aeris 4A): setting off, walking and running; its floor and walls are solid from
its own meshes (35 pieces given colliders, 44 already had them, none unreadable but 1); 228 of the plane's
shapes copied: 28 of 48 lines drawn towards the plane at a third, a half and two thirds of its height were
stopped by those copies (the cockpit, the fuselage, an engine). Its big door is open to the runway.

**Putting everything back**, checked after every walk with the development build's `Locks`: the editor's
camera script running again, the rig back where it was to the centimetre and degree, each camera's near
plane and angle of view as before, the picture's sideways shift back (0.2), the pointer free and shown,
the interface showing, no control lock of the mod's left, and nothing of the mod's left in the scene.
Pictures taken before and after a walk are the same.

**A launch during a walk** (the editor's own launch, started while walking in the hangar): the flight
began normally; in it the pointer was free, the interface showing, no lock left, nothing of the mod's.

**From the space centre**: walking into the hangar and the assembly building as Option-W over them does
(by the development build's `WalkIntoNamed`, since the remote control cannot point the mouse): the building
opened as a click opens it, and the walk began 4 m outside its doorway, looking in: at the assembly building
just outside the great door, the hall ahead; at the hangar outside its door, where one of the crew's
fuel trucks happened to be driving through the doorway right in front, and drove on. The crew's vehicles
are solid (6 in the assembly building, 5 in the hangar): walking on into the hangar slid along the truck.

**The crew car**: built in the hangar from stock parts (10 parts, 0.59 t, 3,280 funds), sent out from the
space centre: it appeared on the runway, its lamps lit, and it drove 80 m east at 7 m/s on its probe core
and stopped. The first two times its seat was empty (the game's own crew choice never fills an outside
seat); the launcher now seats a driver itself, and the third time Bob Kerman was at the wheel.

**Films** (frame by frame, 30 a second): a walk across the assembly building's floor towards the Kerbal X
(the test's aim held the view low for most of it, then looked up the rocket); walking into the hangar from
outside its door.

**The space centre's other buildings**: the Astronaut Complex opened and closed again; the other three
menus were read without opening them (they are closed in a sandbox game); Gene's and Wernher's portrait
sets were made for a moment and photographed. See [the other buildings](The-other-buildings.md).

Not tried: walking by hand with the keys and the mouse; a career or science game (the car's research and
cost checks); the buildings at their lower levels (other models); climbing the gantries' stairs (not
looked for); Windows and Linux.
