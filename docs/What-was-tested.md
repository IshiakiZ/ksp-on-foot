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

**The crew car as a baggage tug** (2026-10-09, on the runway of the test save, in the evening and then by
day after a time warp):

* Measured first as stock parts: the seat pivot 1.116 m up and 0.34 m forward of the middle, the crate's
  underside 0.84 m up, the driver's feet 0.73 m up; the RoveMax wheels stood 1.12 m either side of the
  middle (a 2.25 m track, wider than the tug's body). The tug was modelled round those numbers, and the
  wheels were then moved in under the body in the craft file: measured again, their pivots stood at 0.60 m
  either side and 0.85 m fore and aft, under the drawn tyres.
* Drawn: the stock parts hidden (58 renderers; the kerbal and the crate left), the body, the cab's inside,
  the deck and four tyres in their places, the driver in the seat with the steering wheel before him, the
  crate on the deck. The first try showed nothing but the kerbal and the crate: the meshes were read but
  never handed to the pieces (fixed).
* By day from several sides, with the driver in the seat and standing beside it (the driver was got out
  of the seat, put beside the car and sat in it again, as a kerbal on EVA boards it); and the same car
  with the tug turned off, to compare.
* Driven on the game's rover autopilot: 160 m along the runway at 7 to 8 m/s, then round to the north at
  4.7 m/s, the front wheels steered 25 and 16 degrees (the inner one more) and the drawn front tyres with
  them. Off the runway's north edge, down its bank (9 degrees) at 8.5 m/s, pitched 9 degrees nose down and
  leaning 2 degrees, its wheels on the ground throughout; 1.2 km over the grass to a spot the terrain
  said sloped 4 degrees (the ground the car drove on there was nearly level). The tyres rolled, and rose
  and fell with the suspension (a few centimetres).
* Against the stock rover it was (the same parts with the wheels on the body's sides, 2.25 m apart), both
  by hand on the controls, the same tests one after the other: from standing at full throttle both went
  from 1 to 20 m/s in about 4 s (the stock rover reached 27 m/s, the tug 23 m/s and still gaining when the
  test ended); a full-lock turn at full speed turned both at 13 to 14 degrees a second (the game eases
  the steering off at speed: a 117 m circle), the stock rover leaning 0.1 degrees and the tug 0.8, neither
  near tipping; the brakes took both from 25 m/s to a stop or nearly in 3.7 to 3.8 s. Both went off the
  runway's bank at the end of that turn, at 27 to 28 m/s, nose down 7 to 8 degrees, still on their
  wheels (the tug leaning at most 3.8 degrees, the stock rover 5.8). The tug then climbed the bank (9.4
  degrees) at 5.7 to 6 m/s, keeping its speed; the stock rover, sent the same way on the autopilot, only
  wove about at 1.5 m/s on the level grass (an autopilot matter, not the slope), so the climb was not
  compared.
* At night with the lights on: the headlamps glow and light the runway ahead (the lamps' own lights moved
  to them), the tail lamps glow red, the beacon flashes amber.
* The driver got out, was put 3.5 m and 8 m away, walked back on the remote control's walk and boarded
  (the seat's own board call): fine once the walk had ended. Boarding while the remote control's walk was
  still pushing him flung the car off at 20 m/s once and blew it up another time (the walk keeps pushing
  the kerbal after he is in the seat): a matter of the test's tools, not of a player's boarding, but not
  tried by hand. The test save was quickloaded after, as after every test.
* Films: the tug turning from east to north on the runway by day (recorded live); stills of the night
  lamps. (Frame-locked films came out with the car standing still: the recorder holds the remote
  control's driving still.)
* What it costs: the tug's own work each frame (following four wheels, the lamps) took about 48
  microseconds. The game ran at about 26 frames a second in that scene with or without the tug (37.7 ms
  with it, 40.8 ms without: the difference is noise). It draws 34,126 triangles in 13 draws for the car,
  where the stock parts it hides drew 12,466 triangles in 58 draws: more triangles, far fewer draws.

**0.2.1, the booster seat and the top speed** (2026-10-10, the runway of the test save by day):

* With the stock seat raised 0.15 m in the craft, the driver sat 0.15 m higher (the seat part 0.57 to 0.58 m
  above the RoveMate, against 0.42 before), and stayed so after getting out and in again. The seated
  kerbal's bones: the top of its helmet about 1.87 m up, under the roof's underside at 2.02 m; its wrists
  at its sides at the height of its seat pivot, on the booster's arms; its feet 1.17 to 1.2 m up, well clear
  of the pedals. Pictures from the side, the front and into the cab, with the driver on the booster and
  with the booster empty, mid-morning and with the early sun low in front (which lights the booster
  under the roof). The booster was first a black shell with dark blue cover; under the roof, in the game's
  light, it hardly showed, so it is now a light grey shell with a bright blue cover, and the cab's inside
  is shaded only by its own nooks.
* The top speed: at full throttle along the runway the car first held 10.5 m/s, not 8.3. The wheels' motor
  reads its torque curve against the rim speed of the radius written in the part (0.378 m), but the game
  sizes and rolls the wheel 1.25 times larger (0.4725 m); with that allowed for, the car reached 8.5 m/s in
  2 s and held 8.47 to 8.50 m/s (30.5 km/h) for the 16 s that followed, and 8.43 to 8.46 m/s another time.
  Changing the setting by hand on the page was not tried (the cap follows the setting each half second).

**Films** (frame by frame, 30 a second): a walk across the assembly building's floor towards the Kerbal X
(the test's aim held the view low for most of it, then looked up the rocket); walking into the hangar from
outside its door.

**The space centre's other buildings**: the Astronaut Complex opened and closed again; the other three
menus were read without opening them (they are closed in a sandbox game); Gene's and Wernher's portrait
sets were made for a moment and photographed. See [the other buildings](The-other-buildings.md).

**On Windows** (2026-10-10; Windows 11, KSP 1.12.5 on Direct3D 11, the five public mods and the rest of
this family installed): a walk in the assembly building set off from the mod's page and by Alt-W (also
straight after Photo Studio was opened and closed with its keys), moved with W held down (pressed in the
game's window by a program at the keyboard) and stopped with Alt-W and with Escape; the crew car taken out
from the page, drawn as the baggage tug on the runway (its log: "the crew car 'Crew Car' is drawn as a
baggage tug") and driven a few seconds through the remote control. Once, earlier that day, a walk set off with
Alt-W two seconds after Photo Studio closed ended by itself; it did not happen again. Later, with release
builds: Alt-W with the pointer on the hangar at the space centre opened the hangar, and the walk began at its
door ("walked in by the doorway 65 degrees wide, 62.0 m from the middle; standing 4.0 m outside it"), a ground
crew's vehicle just in front; walked about inside with W, S and D.

Not tried: walking by hand with the mouse; a career or science game (the car's research and
cost checks); the buildings at their lower levels (other models); climbing the gantries' stairs (not
looked for); Linux. Of the tug: driving it by hand with the keys (only the remote control's
inputs); a kerbal walking up to it and boarding with the key (only the seat's own board call was made);
a long drive over rough country; a quicksave and quickload with it out.
