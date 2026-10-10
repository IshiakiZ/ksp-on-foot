# The space centre's other buildings

Asked for: "walk ... into the hangar, the ship builder and every building that has an inside". So the first
thing done was to find out which of the space centre's buildings have an inside at all. Looked at in the
game itself (KSP 1.12.5 with both expansions, a sandbox game, every building at its top level) and in its
code.

| Building | What a click on it opens | Is there a room to walk in? |
| --- | --- | --- |
| **Vehicle Assembly Building** | the editor scene, with the building's whole interior in 3D | **yes**: the hall, the ground crew, the gantries; and through its open door, the crawlerway and the space centre outside |
| **Spaceplane Hangar** | the editor scene, the hangar's interior in 3D | **yes**: the hangar floor, the crew; through its open door, the runway |
| Mission Control | a menu (contracts), with Gene Kerman at the side | no: a header picture, and Gene drawn by a small camera of his own in front of a flat painted backdrop 2.4 by 1.6 m, set up at the world's origin, into a picture in the menu |
| Research and Development | the tech tree; the archives with Wernher von Kerman | no: the tree is laid over pictures; Wernher is the same kind of portrait (a backdrop 2.2 by 1.6 m); the archives' planets are a quad |
| Administration | a menu (strategies), with Mortimer Smith and the department heads | no: its background is a picture (`defaultBackGround`, 2048 by 1024), the heads small portraits |
| Astronaut Complex | a menu (the crew roster) | no: the lounge seen behind the lists is a picture (`AC_lev1`, 2048 by 1280); nothing 3D is drawn for it |
| Tracking Station | its own scene: the map of the solar system | no: it is the planetarium, with no room |
| Launch pad, runway | flight scenes | (outside: Through The Eyes of a Kerbal already walks there, in first person, as a kerbal on EVA) |

(Looked at by opening the Astronaut Complex, and by reading what each building's menu is made of without opening it: the
other three are closed in a sandbox game. None of the four holds a mesh or a camera of a room.)

So the two editors are the only buildings with an inside, and they are what On Foot walks in. The editor
scene turns out to hold more than the building: the space centre's own models and ground, with their own
colliders, stand round it (the assembly building's scene has the hangar, the tracking station, the
crawlerway and the launch pad; the hangar's has the runway and the assembly building's ground). What is
seen through the open doors is real, and can be walked out to.

What else was found about the scenes:

* In the editor, nothing in the building is solid: of its 1,000-odd meshes only some 40 have colliders
  (the space centre's ground and buildings outside, put there for the game's own use), and the floor
  has none. Most of the meshes can be read, so a collider of the same shape can be made for each. The
  assembly building's shell (its walls) cannot: the game combined it with other meshes when it was built
  ("static batching"), and keeps no readable copy.
* The craft's parts have colliders in the editor, but as "triggers": things pass through them (that is
  what lets a part be dragged through others).
* The ground crew are 0.9 to 1 m tall and have no colliders. They walk set rounds (`GroundCrew`,
  `SpaceCenterCrew` and kinds of them: scientists, mechanics with hammers...).
* The editor's camera is a rig of three cameras (the building, the craft and crew, the markers) that
  `VABCamera` or `SPHCamera` swings round the craft; the picture is shifted sideways by `CameraOffCenter`
  to leave room for the parts list.
* In the space centre's own scene, "up" at the space centre lies along Unity's x axis, not its y (the
  scene is the planet as it is), so Unity's character controller, which only stands along y, could not
  be used there; in the editor scene up is y.

Other mods, checked again on 2026-10-09: nothing lets you walk inside the space centre's buildings on a
flat screen. KerbalVR lets you roam the assembly building, in VR only (and its last builds are for older
versions of the game). Through The Eyes of a Kerbal is first person outside the ship (kept up to 1.12.5),
FreeIva is walking inside ships, WalkAbout puts kerbals outside a building's door, and "WASD Editor Camera"
(KSP 1.3) flew the editor's camera about without walking.
