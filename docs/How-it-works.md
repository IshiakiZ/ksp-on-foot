# How it works

## Setting off

In the assembly building or the hangar, Option-W (Alt-W elsewhere), or the button on On Foot's page in the
Keystone window, sets off on foot. From the space centre, Option-W with the pointer on the assembly
building or the hangar opens it as a click does and sets off just outside its door, looking in.

What happens, in order:

1. **The game's camera is taken over.** The editor's camera is a rig of three cameras (one draws the
   building, one the craft and the crew, one the markers) that the editor's own script, `VABCamera` or
   `SPHCamera`, swings round the craft. That script is switched off; where the rig was, how near each
   camera draws, its angle of view, and the sideways shift that leaves room for the parts list
   (`CameraOffCenter`) are kept to be put back.
2. **The editor's keys and clicks are held off** (one control lock, as the game's own dialogs do), the
   pointer is captured for looking about, and the game's interface is put away as F2 does it.
3. **The place is made solid** (next section).
4. **A body is put on the floor** under where the camera was, facing the way the camera faced (coming in
   from the space centre: a few steps outside the doorway, looking in; the doorway is the widest spread
   of lines from the middle of the floor, at waist height, that get out of the room made solid, and its
   plane is where the lines just past its edges meet the walls): Unity's own character controller, a capsule
   the size of a kerbal of the ground crew (1 m tall, half a metre across), whose eyes are 0.8 m up.
5. The view comes down from the game's camera to the eyes in 0.7 s.

Each frame the keys and the mouse move the body (the game's own EVA keys: W A S D, Shift to run, Space to
jump, so a player who has changed them keeps their own), the controller slides it along whatever it meets,
climbs steps up to 0.3 m and slopes up to 50 degrees, and lets it fall where there is nothing under it.
The head rises and falls a little with each step and gives a little on landing (`Head movement`).

Stopping (the same key, or Esc) eases the view back up to where the game's camera was, then puts
everything back: the rig where it was, the editor's script running again, each camera's reach, angle and
shift, the pointer, the lock, the interface. Everything made for the walk is destroyed. The scene is then
as it was before; the development build checks that nothing of the mod's is left in it and that every
setting of the cameras is as before. If the scene changes during a walk (a launch, the other building),
everything is put back at once before it goes.

## Making the building solid

Nothing in the editor's building is solid: it was never meant to be stood in. For the walk:

* **The building's meshes**: each that can be read is given a collider of exactly its shape, as a child
  of it. Pieces the game already made solid (the space centre outside, its ground, the pad) are left as
  they are. Glows, glass and pools of light are skipped. The planet's own local scenery, which the scene
  also holds far off, is left out.
* **The assembly building's walls** cannot be read (see [the other buildings](The-other-buildings.md)), so
  they are found by looking at them: from the middle of the room, a camera with no perspective looks at
  each side in turn, seeing only the shell, through a band from a quarter of a metre above the floor to
  over a kerbal's head, drawn with Unity's own depth shader (`Hidden/Internal-DepthNormalsTexture`, the
  one Unity uses for a camera's depth). Each column of that picture says how far the wall is that way, or
  that there is nothing: the doorway. A slab is laid where the wall is, none where the doorway is.
* **A floor**: a slab under the room, its top a hair under the floor that is drawn. (The floor itself has
  no collider in the game.)
* **The craft**: in the editor its parts' colliders are "triggers", which things pass through (that is
  what lets a part be dragged through others). Each gets a solid copy for the walk, on a layer of its own
  (19) that nothing in the editor looks at.
* **The ground crew**: each gets an upright capsule that goes about with them, and their trucks and carts
  a box round what is drawn of them, which goes about with them too (the trucks drive about the floor).
  They do not know anyone is there: one who walks or drives into you pushes you aside.

All of it is made in a few tens of milliseconds when a walk begins, and destroyed when it ends.

## The crew car

The car is a craft of stock parts kept as a craft file in `GameData/OnFoot/PluginData/`. From the space
centre, the button on On Foot's page puts it on the runway with a kerbal in its seat, as the hangar's
launch does (`FlightDriver.StartWithNewLaunch`). The game's own choice of crew never fills an outside seat
(it leaves those for a kerbal to climb into), so the first free pilot, or anyone free, is put in it; with
nobody free the car goes out empty, driven by its probe core. The game is saved
first, a career pays for it as for any craft rolled out, and in a career or science game its parts must
have been researched. If anything is on the runway already it says so and does nothing (it does not clear
the runway itself). It is then a craft like any other: driven with the rover keys, recovered from the
runway.
