# On Foot

Walk the floor of the assembly building and the hangar in first person, at a kerbal's height: among the
ground crew, under the rocket on its clamps, out through the great door onto the crawlerway. And a car for
the ground crew to drive about the space centre: an airport baggage tug.

For Kerbal Space Program 1.12.x. Needs [Keystone](https://github.com/IshiakiZ/ksp-keystone), a separate download ([its page on SpaceDock](https://spacedock.info/mod/4665/Keystone)) without which this mod does not load, and
nothing else.

## Install and use

Install [Keystone](https://github.com/IshiakiZ/ksp-keystone) first: it is a download of its own, and its `Keystone` folder goes into your KSP
`GameData`. Then copy the `OnFoot` folder from this mod's download into `GameData` beside it.

* In the **assembly building** or the **hangar**, press **Option-W** on a Mac (**Alt-W** elsewhere) to set
  off on foot from where the camera was. Walk with **W A S D** (the game's own EVA keys), look with the
  mouse, run with **Shift**, jump with **Space**. **Option-W** (Alt-W) or **Esc** stops, and the game's
  camera comes back to where it was. Nothing of the building, the craft or the game's controls is
  changed by a walk.
* From the **space centre**, point at the assembly building or the hangar and press **Option-W** (Alt-W):
  it opens as a click opens it, and you are on foot outside its door, looking in.
* The **crew car**: from the space centre, open the Keystone window (the keystone button on the toolbar,
  or Option-K / Alt-K), turn to **On Foot** and press **Drive the crew car**. It is put on the runway with
  a kerbal in its seat and a storage crate on the back, and driven with the rover keys like any rover.
  It looks like an electric baggage tug, real size (a model of the mod's own, drawn over the stock rover
  parts the game drives), so its kerbal driver sits on a child's booster seat. It drives itself up to
  30 km/h; **Top speed** on the same page changes that, and **Looks like a baggage tug** shows the stock
  parts instead.
* The same page has the walking and running speeds, the eye height, the mouse, the field of view, and
  how much the head moves with each step.

The other buildings of the space centre have no inside to walk in: their menus show a kerbal in front of a
painted backdrop, or a picture ([what was found](docs/The-other-buildings.md)).

## Where it works

| System | |
| --- | --- |
| Mac | Made and tried here (KSP 1.12.5, OpenGL). |
| Windows | Tried on Windows 11 (KSP 1.12.5, Direct3D 11): walking in the assembly building, set off by the page's button and by Alt-W, moved with W and stopped with Alt-W and Escape; the crew car taken out from the page, drawn as the baggage tug on the runway and driven. Walking into the hangar from the space centre (Alt-W with the pointer on it) and about inside it. Then running (Shift with W: 6.8 m in a second and a half, against 2.4 walking) and jumping (Space: half a metre up, 0.63 s off the floor), pressed on the keyboard. |
| Linux | **Never run.** It has no shaders and nothing else of one system's own (the tug is drawn with the game's own part shaders). |

More: [how it works](docs/How-it-works.md), [limits](docs/Limits.md),
[what was tested](docs/What-was-tested.md),
[building it yourself](docs/Building.md).

## Licence

[CC BY 4.0](LICENSE): use it, change it and pass it on as you like, as long as you credit Ishiaki and link back here.
