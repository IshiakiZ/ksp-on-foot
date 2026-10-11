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

### The baggage tug

What is seen of the car is an airport baggage tractor of its own, drawn over the stock parts it is made of:
a RoveMate body, four RoveMax M1 wheels (the front pair steers), an outside command seat, a storage crate
and two lamps. The game still drives those parts, with its own wheels and physics; only what is drawn
changes, and nothing is added to the craft. In flight, every vessel made of those parts whose name has
"Crew Car" in it gets the tug, and loses it again if a part comes off or the page's **Looks like a baggage
tug** is turned off.

* **Where it goes.** The car's own frame is worked out from where its parts are: up from the plane of its
  four wheel pivots, forward from the crate (at the back) to the seat, the ground under the middle of its
  wheels as the origin. The tug's body is fixed there on the car's root part, posed again once the car has
  stood still on the ground for a second (the first pose may be in the air as it is put down). The seat,
  steering column, dashboard and pedals stand round the seat's own pivot, where the game seats the driver.
* **What is hidden.** Every renderer of the car's parts is switched off but the kerbal's and the crate's
  (the crate rides on the tug's deck); every half second anything the game has switched on again is
  switched off. Turning the tug off switches them all back on. The parts' colliders stay as they were.
* **The wheels.** The craft's wheels stand where the tug's tyres are drawn (0.6 m either side of the middle,
  0.85 m ahead of and behind it): they were built on the RoveMate's sides and moved in under the body in
  the craft file (the game lets parts overlap; parts of one vessel do not collide). Each tyre follows one
  wheel: it turns with the wheel's steering (read from where the wheel's axle points), rises and falls
  with its suspension (how far the wheel's pivot has moved on its part), and rolls as the wheel rolls (how
  far the wheel's spokes have turned since the last frame, scaled up for the tyre's smaller size, 0.275 m
  against the wheel's 0.4725 m: the RoveMax's written 0.378 m made 1.25 times larger, as the game draws and rolls it). The steering wheel turns eight times as far as the front wheels.
* **The booster seat.** The tug is a real one's size and its seat a grown-up's; a kerbal is small, so it
  sits on a child's backless booster seat (a moulded grey plastic shell with arms, a padded blue cover, red
  belt guides, a cup holder at one side) on the tug's seat, which lifts it 0.15 m: its helmet comes up under
  the roof, where a grown-up driver's head would be, with the steering wheel just before its chest; its
  hands rest on the booster's arms (the game's seated pose holds them at its sides) and its feet hang clear
  of the pedals. For that the stock seat stands 0.15 m higher in the craft file than where the editor put it
  (the game seats the kerbal at the seat part's own pivot), and the roof is 5 cm higher than at first.
* **The top speed** (30 km/h unless the page says otherwise; a real baggage tug does about 25). Each of the
  car's wheel motors is given a torque curve of its own: the stock one up to 85% of the top speed, falling
  away smoothly past it and giving nothing from 3% over it (the game looks the curve up against how fast
  the wheel's rim is turning). Nothing else is touched: no force of the mod's, no speed held. So the car
  pulls up to its top speed on the level and stays there; downhill it can roll faster, the brakes work as
  ever, and a wheel spinning on loose ground also gives out at the top speed. At 100 km/h the stock curves
  are back (the RoveMax's own limit, about 95 km/h). The stock curves go back too when the car stops
  being a crew car (a wheel lost, say).
* **The lamps.** The headlamps glow with the car's lights, the tail lamps glow dimly with them and brightly
  with the brakes on, and the beacon on the roof flashes while the lights are on. The two lamps' own lights
  (the game's, which light the ground) are moved each frame to the tug's headlamps and pointed ahead and a
  little down; they go back where their parts have them when the tug comes off.
* **How it is drawn.** With the game's own part shaders (`KSP/Specular` for the paint, `KSP/Bumped Specular`
  for the tyres and the deck's tread plate, `KSP/Emissive/Specular` for the lamps), so there is no shader of
  the mod's and nothing that differs from one system to another. It casts and takes shadows as parts do.
* **Where it comes from.** `Model/tug.py`, a script run in Blender with no window (see
  [Building](Building.md)), models it to real sizes (3.1 m long with the tow jaw, 1.45 m wide, 2.1 m to the
  top of the roof, wheels 0.55 m across on a 1.7 m wheelbase), rounds its edges, cuts its wheel arches, unwraps it, and
  bakes its paint, wear and dirt, its shading (ambient occlusion) and how glossy each place is into
  pictures; the tyres' tread and the deck's diamond plate are normal maps. It writes `tug.mesh` (the meshes
  in Unity's axes) and the pictures into `PluginData`, which the mod reads the first time a car is dressed
  (about a quarter of a second). The fleet number, 214, is made up; there is no maker's or company's name or
  mark on it.
