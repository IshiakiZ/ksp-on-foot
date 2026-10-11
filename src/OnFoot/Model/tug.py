"""
On Foot's crew car: an electric airport baggage tow tractor, made in Blender by this script alone (nothing downloaded, no
model file kept by hand: run it again to make everything again).

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup \\
        --python src/OnFoot/Model/tug.py -- src/OnFoot/PluginData [--quick] [--render FOLDER] [--seat X,Y,Z] [--deck H]

(always --background: Blender's window is never opened.) It writes into the folder given:

    tug.mesh                 the meshes, as On Foot reads them (see write_mesh): "body", "interior" and "steering" (the wheel
                             the driver holds, turned in the game; one atlas of paint for the three), "tyre" (one tyre and
                             its wheel, drawn four times), "deck" (the tread plate)
    tug_colour.png           the paint of body and interior: colour, wear, dirt and shading (ambient occlusion) in red, green
                             and blue; how glossy each place is, in alpha
    tyre_colour.png, tyre_normal.png    the tyres and wheels (the tread's relief as a tangent-space normal map)
    tread_colour.png, tread_normal.png  the deck's aluminium tread plate: a tile 0.3 m square

--quick bakes with few samples, its pictures half as wide (for trying a change); --render draws pictures of the finished model
from several sides, with kerbal-sized stand-ins, into FOLDER; --seat and --deck put the driver's seat and the load deck where
the car in the game has them (metres, in the frame described below; the defaults are what it measured).

The model's frame: x to the right, y forward, z up, in metres; the ground at z = 0, and the origin midway between the axles.
(On Foot turns it into Unity's: x right, y up, z forward.) Real sizes: about 3.0 m long, 1.45 m wide, 2.0 m to the roof of
the cab, wheels 0.55 m across on a 1.7 m wheelbase. No maker's or company's name or mark anywhere; the fleet number is made up.
"""
import bmesh
import bpy
import math
import os
import struct
import sys

import numpy as np
from mathutils import Matrix, Vector

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(ARGS[0] if ARGS and not ARGS[0].startswith("--") else os.path.join(os.path.dirname(__file__), "..", "PluginData"))
QUICK = "--quick" in ARGS
RENDER = ARGS[ARGS.index("--render") + 1] if "--render" in ARGS else None


def arg_vector(name, default):
    if name not in ARGS:
        return default
    return Vector(float(v) for v in ARGS[ARGS.index(name) + 1].split(","))


# ---------------------------------------------------------------- sizes (metres)
W = 0.725                          # half the width
FRONT, BACK = 1.5, -1.47           # the body's ends (the bumper stands out behind)
BOTTOM = 0.24                      # the body's underside
TYRE_R, TYRE_W, TRACK, AXLE_F, AXLE_B = 0.275, 0.2, 0.6, 0.85, -0.85
ARCH_R = 0.36                      # the wheel arches' radius (round the tyre's middle)
# (what the car in the game measures, 2026-10-09: its driver's seat pivot, where the kerbal sits, at 1.116 m up and 0.337 m
# forward of the middle; the crate's underside 0.842 m up; the driver's feet 0.727 m up; the driver reaches 0.1 m behind the
# seat pivot with the backpack, 0.65 m in front of it with the feet. Since 0.2.1 the stock seat stands BOOST higher in the
# craft, and the kerbal sits on a child's booster seat on the tug's own grown-up seat: so the seat pivot is 1.266 m up.)
CAB_FLOOR = 0.72
ROOF = 2.05                        # (the roof's middle: room above a boosted kerbal's helmet)
BOOST = 0.15                       # how far the booster seat lifts the driver
SEAT = arg_vector("--seat", Vector((0.0, 0.337, 1.116 + BOOST)))     # the driver's seat pivot
CUSHION = SEAT.z - 0.11 - BOOST    # the top of the tug's own seat cushion, where the booster stands
WHEEL = (0.31, 0.17)                # the steering wheel's middle: forward of and above the seat pivot (where the hands are)
DECK = float(ARGS[ARGS.index("--deck") + 1]) if "--deck" in ARGS else 0.842
CAGE = SEAT.y - 0.48               # the bars behind the seat (clear of the driver's backpack)
DASH = SEAT.y + 0.72               # the cowl in front of the cab
NUMBER = "214"                     # the fleet number (made up)
ATLAS = 1024 if QUICK else 2048
TYRE_TEX = 256 if QUICK else 512
SAMPLES = 16 if QUICK else 128

PARTS = {"body": [], "interior": [], "steering": [], "tyre": [], "deck": []}     # what goes into each exported mesh
PIVOTS = {}                        # a mesh drawn turning: where it turns about (centre, axis), in the model's frame
CUTTERS = []


# ---------------------------------------------------------------- materials
MATERIALS = {}


def node(tree, kind, **props):
    n = tree.nodes.new(kind)
    for k, v in props.items():
        setattr(n, k, v)
    return n


def material(name, colour, gloss, rough=0.5, build=None):
    """A material for baking: its base colour (with any pattern `build` adds) and how glossy it is (kept for the gloss bake).
    (Never metallic: a metal's diffuse colour bakes black, and the game's part shaders have no metalness anyway.)"""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    t = m.node_tree
    bsdf = t.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*colour, 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    if build is not None:
        build(t, bsdf)
    m["gloss"] = gloss
    MATERIALS[name] = m
    return m


def grime(t, bsdf, base, dirty, wear=None, scale=1.6, amount=0.18):
    """Paint with a little blotchy dirt over it, more of it low down (thrown up from the road), and (if `wear`) lighter or
    darker at worn edges."""
    coords = node(t, "ShaderNodeTexCoord")
    noise = node(t, "ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = scale
    noise.inputs["Detail"].default_value = 4.0
    t.links.new(coords.outputs["Object"], noise.inputs["Vector"])
    ramp = node(t, "ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.5
    ramp.color_ramp.elements[1].position = 0.8
    t.links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
    mix = node(t, "ShaderNodeMix", data_type="RGBA")
    mix.inputs[6].default_value = (*base, 1.0)
    mix.inputs[7].default_value = (*dirty, 1.0)
    scaled = node(t, "ShaderNodeMath", operation="MULTIPLY")
    scaled.inputs[1].default_value = amount
    t.links.new(ramp.outputs["Color"], scaled.inputs[0])
    xyz = node(t, "ShaderNodeSeparateXYZ")
    t.links.new(coords.outputs["Object"], xyz.inputs[0])
    low = node(t, "ShaderNodeMapRange")
    low.inputs["From Min"].default_value = 0.55
    low.inputs["From Max"].default_value = 0.18
    low.inputs["To Max"].default_value = 0.5
    t.links.new(xyz.outputs["Z"], low.inputs["Value"])
    lower = node(t, "ShaderNodeMath", operation="MULTIPLY")
    t.links.new(low.outputs["Result"], lower.inputs[0])
    t.links.new(noise.outputs["Fac"], lower.inputs[1])
    dirt = node(t, "ShaderNodeMath", operation="MAXIMUM")
    t.links.new(scaled.outputs[0], dirt.inputs[0])
    t.links.new(lower.outputs[0], dirt.inputs[1])
    t.links.new(dirt.outputs[0], mix.inputs[0])
    out = mix.outputs[2]
    if wear is not None:
        geo = node(t, "ShaderNodeNewGeometry")
        edge = node(t, "ShaderNodeMapRange")
        edge.inputs["From Min"].default_value = 0.52
        edge.inputs["From Max"].default_value = 0.6
        t.links.new(geo.outputs["Pointiness"], edge.inputs["Value"])
        speck = node(t, "ShaderNodeTexNoise")
        speck.inputs["Scale"].default_value = 40.0
        t.links.new(coords.outputs["Object"], speck.inputs["Vector"])
        both = node(t, "ShaderNodeMath", operation="MULTIPLY")
        t.links.new(edge.outputs["Result"], both.inputs[0])
        t.links.new(speck.outputs["Fac"], both.inputs[1])
        worn = node(t, "ShaderNodeMix", data_type="RGBA")
        t.links.new(both.outputs[0], worn.inputs[0])
        t.links.new(out, worn.inputs[6])
        worn.inputs[7].default_value = (*wear, 1.0)
        out = worn.outputs[2]
    t.links.new(out, bsdf.inputs["Base Color"])


def fabric(t, bsdf):
    """A woven seat fabric: a fine mottle of two blues, lighter where it has worn (from the cover's own coordinates)."""
    coords = node(t, "ShaderNodeTexCoord")
    fine = node(t, "ShaderNodeTexNoise")
    fine.inputs["Scale"].default_value = 90.0
    fine.inputs["Detail"].default_value = 2.0
    t.links.new(coords.outputs["Object"], fine.inputs["Vector"])
    weave = node(t, "ShaderNodeMix", data_type="RGBA")
    weave.inputs[6].default_value = (0.1, 0.26, 0.56, 1.0)
    weave.inputs[7].default_value = (0.15, 0.33, 0.64, 1.0)
    t.links.new(fine.outputs["Fac"], weave.inputs[0])
    coarse = node(t, "ShaderNodeTexNoise")
    coarse.inputs["Scale"].default_value = 6.0
    t.links.new(coords.outputs["Object"], coarse.inputs["Vector"])
    ramp = node(t, "ShaderNodeMapRange")
    ramp.inputs["From Min"].default_value = 0.55
    ramp.inputs["From Max"].default_value = 0.75
    ramp.inputs["To Max"].default_value = 0.35
    t.links.new(coarse.outputs["Fac"], ramp.inputs["Value"])
    worn = node(t, "ShaderNodeMix", data_type="RGBA")
    t.links.new(ramp.outputs["Result"], worn.inputs[0])
    t.links.new(weave.outputs[2], worn.inputs[6])
    worn.inputs[7].default_value = (0.24, 0.36, 0.58, 1.0)
    t.links.new(worn.outputs[2], bsdf.inputs["Base Color"])


def chevrons(t, bsdf):
    """Yellow and black stripes at 45 degrees, running down and outwards on each rear corner (from the body's own coordinates)."""
    coords = node(t, "ShaderNodeTexCoord")
    xyz = node(t, "ShaderNodeSeparateXYZ")
    t.links.new(coords.outputs["Object"], xyz.inputs[0])
    ax = node(t, "ShaderNodeMath", operation="ABSOLUTE")
    t.links.new(xyz.outputs["X"], ax.inputs[0])
    s = node(t, "ShaderNodeMath", operation="ADD")
    t.links.new(xyz.outputs["Z"], s.inputs[0])
    t.links.new(ax.outputs[0], s.inputs[1])
    k = node(t, "ShaderNodeMath", operation="MULTIPLY")
    k.inputs[1].default_value = 1.0 / 0.11
    t.links.new(s.outputs[0], k.inputs[0])
    f = node(t, "ShaderNodeMath", operation="FRACT")
    t.links.new(k.outputs[0], f.inputs[0])
    g = node(t, "ShaderNodeMath", operation="GREATER_THAN")
    g.inputs[1].default_value = 0.5
    t.links.new(f.outputs[0], g.inputs[0])
    mix = node(t, "ShaderNodeMix", data_type="RGBA")
    mix.inputs[6].default_value = (0.86, 0.6, 0.02, 1.0)
    mix.inputs[7].default_value = (0.025, 0.025, 0.025, 1.0)
    t.links.new(g.outputs[0], mix.inputs[0])
    t.links.new(mix.outputs[2], bsdf.inputs["Base Color"])


def tyre_tread(t, bsdf):
    """Tread blocks across the tyre's running face (from its own "tread" coordinates: metres round, metres across)."""
    uv = node(t, "ShaderNodeUVMap", uv_map="tread")
    brick = node(t, "ShaderNodeTexBrick")
    brick.inputs["Scale"].default_value = 1.0
    brick.inputs["Brick Width"].default_value = 0.055
    brick.inputs["Row Height"].default_value = 0.05
    brick.inputs["Mortar Size"].default_value = 0.008
    brick.inputs["Mortar Smooth"].default_value = 0.3
    brick.offset = 0.5
    t.links.new(uv.outputs["UV"], brick.inputs["Vector"])
    # (only on the running face: its stretch of the profile, in the second coordinate)
    sep = node(t, "ShaderNodeSeparateXYZ")
    t.links.new(uv.outputs["UV"], sep.inputs[0])
    lo = node(t, "ShaderNodeMath", operation="GREATER_THAN")
    lo.inputs[1].default_value = TREAD_V[0]
    hi = node(t, "ShaderNodeMath", operation="LESS_THAN")
    hi.inputs[1].default_value = TREAD_V[1]
    t.links.new(sep.outputs["Y"], lo.inputs[0])
    t.links.new(sep.outputs["Y"], hi.inputs[0])
    band = node(t, "ShaderNodeMath", operation="MULTIPLY")
    t.links.new(lo.outputs[0], band.inputs[0])
    t.links.new(hi.outputs[0], band.inputs[1])
    groove = node(t, "ShaderNodeMath", operation="MULTIPLY")
    t.links.new(brick.outputs["Fac"], groove.inputs[0])
    t.links.new(band.outputs[0], groove.inputs[1])
    inv = node(t, "ShaderNodeMath", operation="SUBTRACT")
    inv.inputs[0].default_value = 1.0
    t.links.new(groove.outputs[0], inv.inputs[1])
    bump = node(t, "ShaderNodeBump")
    bump.inputs["Strength"].default_value = 1.0
    bump.inputs["Distance"].default_value = 0.006
    t.links.new(inv.outputs[0], bump.inputs["Height"])
    t.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    dark = node(t, "ShaderNodeMix", data_type="RGBA")
    dark.inputs[6].default_value = (0.05, 0.05, 0.05, 1.0)
    dark.inputs[7].default_value = (0.022, 0.022, 0.022, 1.0)
    t.links.new(groove.outputs[0], dark.inputs[0])
    t.links.new(dark.outputs[2], bsdf.inputs["Base Color"])


TREAD_V = (0.0, 1.0)      # (set once the tyre's profile is known)


def number_image():
    """The fleet number, black on the body's white, drawn as strokes into a picture 4 : 1 (no font needed)."""
    strokes = {
        '0': [(0.15, 0.25, 0.15, 1.35), (0.15, 1.35, 0.5, 1.55), (0.5, 1.55, 0.85, 1.35), (0.85, 1.35, 0.85, 0.25), (0.85, 0.25, 0.5, 0.05), (0.5, 0.05, 0.15, 0.25)],
        '1': [(0.25, 1.25, 0.55, 1.55), (0.55, 1.55, 0.55, 0.05), (0.25, 0.05, 0.85, 0.05)],
        '2': [(0.15, 1.3, 0.45, 1.55), (0.45, 1.55, 0.75, 1.45), (0.75, 1.45, 0.85, 1.15), (0.85, 1.15, 0.15, 0.05), (0.15, 0.05, 0.85, 0.05)],
        '3': [(0.15, 1.45, 0.85, 1.55), (0.85, 1.55, 0.45, 0.9), (0.45, 0.9, 0.85, 0.65), (0.85, 0.65, 0.75, 0.15), (0.75, 0.15, 0.15, 0.1)],
        '4': [(0.7, 0.05, 0.7, 1.55), (0.7, 1.55, 0.1, 0.5), (0.1, 0.5, 0.9, 0.5)],
        '5': [(0.85, 1.55, 0.2, 1.55), (0.2, 1.55, 0.15, 0.9), (0.15, 0.9, 0.7, 0.9), (0.7, 0.9, 0.85, 0.5), (0.85, 0.5, 0.65, 0.08), (0.65, 0.08, 0.15, 0.12)],
        '6': [(0.8, 1.5, 0.3, 1.2), (0.3, 1.2, 0.15, 0.5), (0.15, 0.5, 0.45, 0.05), (0.45, 0.05, 0.85, 0.4), (0.85, 0.4, 0.55, 0.85), (0.55, 0.85, 0.15, 0.55)],
        '7': [(0.15, 1.55, 0.85, 1.55), (0.85, 1.55, 0.4, 0.05)],
        '8': [(0.5, 0.85, 0.2, 1.2), (0.2, 1.2, 0.5, 1.55), (0.5, 1.55, 0.8, 1.2), (0.8, 1.2, 0.5, 0.85), (0.5, 0.85, 0.15, 0.45), (0.15, 0.45, 0.5, 0.05), (0.5, 0.05, 0.85, 0.45), (0.85, 0.45, 0.5, 0.85)],
        '9': [(0.85, 1.05, 0.45, 0.75), (0.45, 0.75, 0.15, 1.15), (0.15, 1.15, 0.5, 1.55), (0.5, 1.55, 0.85, 1.1), (0.85, 1.1, 0.7, 0.4), (0.7, 0.4, 0.2, 0.08)],
    }
    w, h = 512, 128
    fig_h = h * 0.72
    fig_w = fig_h / 1.6
    gap, pen = fig_w * 0.28, fig_w * 0.15
    total = len(NUMBER) * fig_w + (len(NUMBER) - 1) * gap
    x0, y0 = (w - total) / 2, (h - fig_h) / 2
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32) + 0.5
    near = np.full((h, w), 1e9, np.float32)
    for i, ch in enumerate(NUMBER):
        ox = x0 + i * (fig_w + gap)
        for (ax, ay, bx, by) in strokes[ch]:
            a = np.array([ox + ax * fig_w, y0 + ay / 1.6 * fig_h])
            b = np.array([ox + bx * fig_w, y0 + by / 1.6 * fig_h])
            ab = b - a
            k = np.clip(((xx - a[0]) * ab[0] + (yy - a[1]) * ab[1]) / max(ab @ ab, 1e-6), 0, 1)
            d = np.hypot(xx - a[0] - ab[0] * k, yy - a[1] - ab[1] * k)
            near = np.minimum(near, d)
    ink = np.clip(pen / 2 - near + 0.5, 0, 1)
    white = np.array([0.82, 0.83, 0.80], np.float32)
    rgb = white[None, None, :] * (1 - ink[..., None]) + 0.02 * ink[..., None]
    img = bpy.data.images.new("fleet number", w, h, alpha=False)
    img.colorspace_settings.name = "sRGB"
    px = np.concatenate([rgb, np.ones((h, w, 1))], axis=2).astype(np.float32)
    img.pixels.foreach_set(px.ravel())               # (Blender's rows run from the bottom up, as the strokes' y does)
    img.pack()
    return img


def number_material(t, bsdf):
    uv = node(t, "ShaderNodeUVMap", uv_map="decal")
    tex = node(t, "ShaderNodeTexImage")
    tex.image = NUMBER_IMAGE
    t.links.new(uv.outputs["UV"], tex.inputs["Vector"])
    t.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])


def make_materials():
    material("white", (0.82, 0.83, 0.80), 0.55, build=lambda t, b: grime(t, b, (0.82, 0.83, 0.80), (0.66, 0.65, 0.6), wear=(0.5, 0.5, 0.49)))
    material("charcoal", (0.1, 0.104, 0.11), 0.32, build=lambda t, b: grime(t, b, (0.1, 0.104, 0.11), (0.07, 0.07, 0.07), wear=(0.22, 0.22, 0.22), amount=0.35))
    material("black", (0.03, 0.03, 0.03), 0.22, build=lambda t, b: grime(t, b, (0.032, 0.032, 0.032), (0.06, 0.055, 0.05), wear=(0.16, 0.16, 0.16)))
    material("wheelwell", (0.035, 0.034, 0.033), 0.08, build=lambda t, b: grime(t, b, (0.04, 0.04, 0.04), (0.09, 0.08, 0.06), amount=0.6))
    material("steel", (0.42, 0.43, 0.44), 0.7, rough=0.35)
    material("glass", (0.05, 0.06, 0.07), 0.95, rough=0.05)
    material("headlamp", (0.82, 0.83, 0.85), 0.9, rough=0.1)
    material("taillamp", (0.5, 0.03, 0.02), 0.85, rough=0.15)
    material("reflector", (0.45, 0.03, 0.02), 0.8, rough=0.2)
    material("beacon", (0.92, 0.48, 0.04), 0.85, rough=0.15)
    material("amber", (0.92, 0.48, 0.04), 0.8, rough=0.2)
    material("chevron", (0.86, 0.6, 0.02), 0.4, build=chevrons)
    material("number", (0.82, 0.83, 0.8), 0.55, build=number_material)
    material("seat", (0.09, 0.09, 0.095), 0.35, build=lambda t, b: grime(t, b, (0.09, 0.09, 0.095), (0.12, 0.115, 0.11), scale=12.0, amount=0.3))
    material("dash", (0.06, 0.062, 0.065), 0.3)
    # (the child's booster seat: a moulded plastic shell, a padded fabric cover, red belt guides)
    material("booster", (0.38, 0.39, 0.41), 0.5, build=lambda t, b: grime(t, b, (0.38, 0.39, 0.41), (0.3, 0.3, 0.29), wear=(0.5, 0.5, 0.5), scale=14.0, amount=0.15))
    material("fabric", (0.12, 0.3, 0.62), 0.08, build=fabric)
    material("clip", (0.6, 0.05, 0.035), 0.6, rough=0.3)
    material("deck", (0.72, 0.73, 0.74), 0.6, rough=0.4)
    material("rubber", (0.045, 0.045, 0.045), 0.12, rough=0.85, build=tyre_tread)
    material("hub", (0.2, 0.205, 0.21), 0.5, build=lambda t, b: grime(t, b, (0.2, 0.205, 0.21), (0.12, 0.11, 0.1), wear=(0.4, 0.4, 0.4)))


# ---------------------------------------------------------------- shapes
def finish(name, bm, mat, into, bevel=0.0, segments=3, angle=30.0, decal=None):
    """Make an object of a bmesh, with one material, an optional rounding of its edges, and the uv layers every piece has."""
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    me.materials.append(MATERIALS[mat])
    for layer in ("decal",):
        if layer not in me.uv_layers:
            me.uv_layers.new(name=layer)
    if decal is not None:
        uv = me.uv_layers["decal"]
        for poly in me.polygons:
            for li in poly.loop_indices:
                uv.data[li].uv = decal(me.vertices[me.loops[li].vertex_index].co)
    if bevel > 0:
        b = obj.modifiers.new("Bevel", "BEVEL")
        b.width = bevel
        b.segments = segments
        b.limit_method = "ANGLE"
        b.angle_limit = math.radians(angle)
        b.use_clamp_overlap = True
    if into is not None:
        PARTS[into].append(obj)
    return obj


def box(name, lo, hi, mat="white", bevel=0.0, into="body", segments=3, decal=None):
    lo, hi = Vector(lo), Vector(hi)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    size, mid = hi - lo, (hi + lo) * 0.5
    for v in bm.verts:
        v.co = Vector((v.co.x * size.x, v.co.y * size.y, v.co.z * size.z)) + mid
    return finish(name, bm, mat, into, bevel=min(bevel, 0.49 * min(size)), segments=segments, decal=decal)


def cylinder(name, a, b, r, mat="black", segments=16, into="body", caps=True):
    a, b = Vector(a), Vector(b)
    axis = b - a
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=caps, cap_tris=False, segments=segments, radius1=r, radius2=r, depth=axis.length)
    rot = axis.normalized().to_track_quat("Z", "Y").to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=Matrix.Translation((a + b) * 0.5) @ rot, verts=bm.verts)
    obj = finish(name, bm, mat, into)
    for p in obj.data.polygons:
        p.use_smooth = True
    return obj


def dome(name, base, r, height, mat, into="body", around=32, rows=6):
    """A shallow dome facing forward (+y) from a flat round base of radius r at `base`, `height` deep at its middle."""
    bm = bmesh.new()
    rings = []
    for j in range(rows + 1):
        t = j / rows                                   # (0 at the rim, 1 at the top)
        rr = r * math.cos(t * math.pi / 2)
        y = height * math.sin(t * math.pi / 2)
        if j == rows:
            rings.append([bm.verts.new(base + Vector((0, y, 0)))])
            break
        rings.append([bm.verts.new(base + Vector((rr * math.cos(2 * math.pi * k / around), y, rr * math.sin(2 * math.pi * k / around)))) for k in range(around)])
    for j in range(rows - 1):
        for k in range(around):
            bm.faces.new((rings[j][k], rings[j][(k + 1) % around], rings[j + 1][(k + 1) % around], rings[j + 1][k]))
    tip = rings[rows][0]
    for k in range(around):
        bm.faces.new((rings[rows - 1][k], rings[rows - 1][(k + 1) % around], tip))
    bm.faces.new(list(reversed(rings[0])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    obj = finish(name, bm, mat, into)
    for p in obj.data.polygons:
        p.use_smooth = True
    return obj


def sphere(name, c, r, mat="black", into="body", segs=12):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=max(4, segs // 2), radius=r)
    bmesh.ops.translate(bm, vec=Vector(c), verts=bm.verts)
    obj = finish(name, bm, mat, into)
    for p in obj.data.polygons:
        p.use_smooth = True
    return obj


def tube(name, points, r, mat="black", into="body", segments=12):
    """A bent tube through the points: a straight tube between each two, a ball at each bend."""
    for i in range(len(points) - 1):
        cylinder(f"{name}.{i}", points[i], points[i + 1], r, mat, segments, into, caps=(i == 0 or i == len(points) - 2))
    for i in range(1, len(points) - 1):
        sphere(f"{name}.j{i}", points[i], r, mat, into, segs=segments)


def ring(name, c, axis, R, r, mat="black", into="body", around=40, sides=10):
    axis = Vector(axis).normalized()
    x = axis.cross(Vector((0, 0, 1)) if abs(axis.z) < 0.9 else Vector((1, 0, 0))).normalized()
    y = axis.cross(x)
    bm = bmesh.new()
    rows = []
    for i in range(around):
        a = 2 * math.pi * i / around
        out = x * math.cos(a) + y * math.sin(a)
        rows.append([bm.verts.new(Vector(c) + out * R + (out * math.cos(2 * math.pi * j / sides) + axis * math.sin(2 * math.pi * j / sides)) * r) for j in range(sides)])
    for i in range(around):
        for j in range(sides):
            bm.faces.new((rows[i][j], rows[(i + 1) % around][j], rows[(i + 1) % around][(j + 1) % sides], rows[i][(j + 1) % sides]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    obj = finish(name, bm, mat, into)
    for p in obj.data.polygons:
        p.use_smooth = True
    return obj


def panel(name, centre, normal, w, h, mat="number", into="body"):
    """A flat rectangle w by h, facing `normal` (x, y or z, with a sign), its decal coordinates covering it once."""
    c = Vector(centre)
    n = Vector(normal)
    if abs(n.z) > 0.5:
        u, v = Vector((1, 0, 0)), Vector((0, 1, 0)) * (1 if n.z > 0 else -1)
    elif abs(n.x) > 0.5:
        u, v = Vector((0, 1, 0)) * (1 if n.x > 0 else -1), Vector((0, 0, 1))
    else:
        u, v = Vector((1, 0, 0)) * (1 if n.y < 0 else -1), Vector((0, 0, 1))
    bm = bmesh.new()
    corners = [c - u * w / 2 - v * h / 2, c + u * w / 2 - v * h / 2, c + u * w / 2 + v * h / 2, c - u * w / 2 + v * h / 2]
    f = bm.faces.new([bm.verts.new(p) for p in corners])
    bm.normal_update()
    if f.normal.dot(n) < 0:
        f.normal_flip()
    origin = corners[0]

    def decal(p):
        d = Vector(p) - origin
        return (d.dot(u) / w, d.dot(v) / h)
    return finish(name, bm, mat, into, decal=decal)


def arch_trim(name, c, R, a0, a1, steps=40, radial=0.022, across=0.018, mat="black"):
    """A rubber trim bent round part of a circle in the y-z plane (centre c, radius R, from angle a0 to a1 in degrees from +y
    towards +z), its section an ellipse `radial` by `across`."""
    bm = bmesh.new()
    rings = []
    sides = 10
    for i in range(steps + 1):
        a = math.radians(a0 + (a1 - a0) * i / steps)
        out = Vector((0, math.cos(a), math.sin(a)))
        rings.append([bm.verts.new(c + out * (R + radial * math.cos(2 * math.pi * j / sides)) + Vector((across * math.sin(2 * math.pi * j / sides), 0, 0)))
                      for j in range(sides)])
    for i in range(steps):
        for j in range(sides):
            bm.faces.new((rings[i][j], rings[i + 1][j], rings[i + 1][(j + 1) % sides], rings[i][(j + 1) % sides]))
    bm.faces.new(rings[0])
    bm.faces.new(list(reversed(rings[-1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return finish(name, bm, mat, "body")


def cut_arch(target, y, side):
    """A wheel arch: the round hole for a wheel, cut from the body's side, its inside dark."""
    a = Vector((side * (W - 0.27), y, TYRE_R))
    b = Vector((side * (W + 0.3), y, TYRE_R))
    c = cylinder(f"arch cutter {y:+.2f} {side:+d}", a, b, ARCH_R, "wheelwell", segments=48, into=None)
    CUTTERS.append(c)
    m = target.modifiers.new(f"Arch {y:+.2f} {side:+d}", "BOOLEAN")
    m.operation = "DIFFERENCE"
    m.solver = "EXACT"
    m.object = c
    m.material_mode = "TRANSFER"
    target.modifiers.move(len(target.modifiers) - 1, 0)       # (cut before the edges are rounded)


# ---------------------------------------------------------------- the tug
def arc(cy, cz, r, a0, a1, n=6):
    """Points along a circle in the y-z plane (angles in degrees from +y towards +z), the first one left out."""
    return [(cy + r * math.cos(math.radians(a0 + (a1 - a0) * k / n)), cz + r * math.sin(math.radians(a0 + (a1 - a0) * k / n))) for k in range(1, n + 1)]


def prism(name, outline, x0, x1, mat="white", bevel=0.0, into="body", segments=3, angle=30.0):
    """A solid with a side view `outline` (points (y, z), in order round it) the same all the way from x0 to x1."""
    bm = bmesh.new()
    left = [bm.verts.new((x0, y, z)) for (y, z) in outline]
    right = [bm.verts.new((x1, y, z)) for (y, z) in outline]
    bm.faces.new(left)
    bm.faces.new(list(reversed(right)))
    n = len(outline)
    for i in range(n):
        bm.faces.new((left[i], left[(i + 1) % n], right[(i + 1) % n], right[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return finish(name, bm, mat, into, bevel=bevel, segments=segments, angle=angle)


def build_body():
    # the white body, seen from the side: the low rounded nose, the bonnet rising to the cowl in front of the cab, down into
    # the cab, up again behind it to the deck, down the back; the four arches cut out of it and every edge rounded
    side = [(BACK, BOTTOM), (FRONT - 0.2, BOTTOM)]
    side += arc(FRONT - 0.2, BOTTOM + 0.2, 0.2, -90, 0)
    side += [(FRONT, 0.6)]
    side += arc(FRONT - 0.13, 0.6, 0.13, 0, 90)
    side += [(DASH + 0.14, 0.77), (DASH + 0.08, CAB_FLOOR + 0.28), (DASH - 0.06, CAB_FLOOR + 0.3), (DASH - 0.06, CAB_FLOOR),
             (CAGE, CAB_FLOOR), (CAGE, DECK - 0.012), (BACK, DECK - 0.012)]
    # (its top under the deck kept just below the deck: two faces in one place would shade each other black in the bake)
    body = prism("body shell", side, -W, W, "white", bevel=0.04)
    for y in (AXLE_F, AXLE_B):
        for s in (-1, 1):
            cut_arch(body, y, s)
            # a black rubber trim round the arch's edge, from where it leaves the body's underside over the top and back
            ends = math.degrees(math.asin((BOTTOM + 0.01 - TYRE_R) / (ARCH_R + 0.012)))
            arch_trim(f"arch trim {y:+.2f} {s:+d}", Vector((s * (W + 0.004), y, TYRE_R)), ARCH_R + 0.012, ends, 180 - ends)
    # under it, between the wheels, the dark underbody
    box("underbody", (-0.42, AXLE_B + 0.3, 0.14), (0.42, AXLE_F - 0.3, BOTTOM + 0.01), "wheelwell", bevel=0.02)
    # the deck's lips, the roof
    for s in (-1, 1):
        box(f"deck lip {s:+d}", (min(s * W, s * (W - 0.035)), BACK, DECK - 0.03), (max(s * W, s * (W - 0.035)), CAGE, DECK + 0.05), "white", bevel=0.01)
    box("deck lip back", (-W, BACK, DECK - 0.03), (W, BACK + 0.035, DECK + 0.05), "white", bevel=0.01)
    box("roof", (-W - 0.01, CAGE - 0.1, ROOF - 0.03), (W + 0.01, DASH + 0.15, ROOF + 0.03), "white", bevel=0.025)
    # the dark band low along each side between the wheels
    for s in (-1, 1):
        x0, x1 = (W - 0.004, W + 0.012) if s > 0 else (-W - 0.012, -W + 0.004)
        box(f"side band {s:+d}", (x0, AXLE_B + ARCH_R + 0.03, BOTTOM + 0.025), (x1, AXLE_F - ARCH_R - 0.03, 0.45), "charcoal", bevel=0.006)
    # the deck: aluminium tread plate (its own mesh: the game draws a tiled tread on it)
    box("deck", (-W + 0.035, BACK + 0.035, DECK - 0.025), (W - 0.035, CAGE - 0.005, DECK), "deck", bevel=0.006, into="deck")
    # the back: the bumper and counterweight (round the corners a little), chevrons on its corners, the tow jaw and its pin,
    # lamps and reflectors
    rear = -1.66
    box("bumper", (-W - 0.015, rear, 0.26), (W + 0.015, BACK + 0.1, 0.75), "black", bevel=0.04)
    for s in (-1, 1):
        x0, x1 = sorted((s * 0.42, s * 0.7))
        box(f"chevrons {s:+d}", (x0, rear - 0.012, 0.31), (x1, rear + 0.01, 0.7), "chevron", bevel=0.004)
        box(f"tail lamp {s:+d}", (s * 0.6 - 0.07, BACK - 0.016, 0.8), (s * 0.6 + 0.07, BACK + 0.002, 0.88), "taillamp", bevel=0.01)
        box(f"reflector {s:+d}", (s * 0.4 - 0.05, BACK - 0.012, 0.81), (s * 0.4 + 0.05, BACK + 0.002, 0.87), "reflector", bevel=0.006)
        xs = sorted((s * (W + 0.002), s * (W + 0.012)))
        box(f"side reflector back {s:+d}", (xs[0], -1.4, 0.82), (xs[1], -1.32, 0.86), "reflector", bevel=0.003)
        box(f"side reflector front {s:+d}", (xs[0], FRONT - 0.42, 0.5), (xs[1], FRONT - 0.34, 0.54), "amber", bevel=0.003)
    box("jaw top", (-0.085, rear - 0.12, 0.5), (0.085, rear + 0.02, 0.525), "black", bevel=0.006)
    box("jaw bottom", (-0.085, rear - 0.12, 0.385), (0.085, rear + 0.02, 0.41), "black", bevel=0.006)
    box("jaw back", (-0.085, rear - 0.035, 0.385), (0.085, rear + 0.02, 0.525), "black", bevel=0.006)
    cylinder("pin", (0, rear - 0.08, 0.37), (0, rear - 0.08, 0.55), 0.016, "steel", 12)
    sphere("pin head", (0, rear - 0.08, 0.555), 0.024, "steel", segs=10)
    # the front: headlamps in black bezels, indicators outside them, a grille of bars between, a rubber bumper low down
    for s in (-1, 1):
        cylinder(f"bezel {s:+d}", (s * 0.46, FRONT - 0.02, 0.52), (s * 0.46, FRONT + 0.01, 0.52), 0.074, "black", 28)
        # (the lamp a shallow glass dome in a chrome-ish ring, so that it catches the light as a lamp does)
        ring(f"headlamp ring {s:+d}", (s * 0.46, FRONT + 0.012, 0.52), (0, 1, 0), 0.062, 0.007, "steel", around=32, sides=8)
        dome(f"headlamp {s:+d}", Vector((s * 0.46, FRONT + 0.008, 0.52)), 0.058, 0.02, "headlamp")
        box(f"indicator {s:+d}", (s * 0.615 - 0.04, FRONT - 0.03, 0.495), (s * 0.615 + 0.04, FRONT + 0.008, 0.545), "amber", bevel=0.008)
    box("grille recess", (-0.3, FRONT - 0.012, 0.45), (0.3, FRONT + 0.002, 0.59), "black", bevel=0.01)
    for k in range(4):
        z = 0.462 + k * 0.034
        box(f"grille bar {k}", (-0.29, FRONT - 0.004, z), (0.29, FRONT + 0.012, z + 0.014), "charcoal", bevel=0.005)
    box("front bumper", (-W + 0.03, FRONT - 0.12, 0.3), (W - 0.03, FRONT + 0.03, 0.4), "black", bevel=0.03)
    # the cab: posts, half doors, mirrors
    door_top = CAB_FLOOR + 0.62
    for s in (-1, 1):
        px = s * (W - 0.035)
        cylinder(f"post back {s:+d}", (px, CAGE + 0.02, CAB_FLOOR - 0.02), (px, CAGE + 0.02, ROOF - 0.03), 0.026, "black", 16)
        cylinder(f"post front {s:+d}", (px, DASH + 0.04, CAB_FLOOR + 0.27), (px, DASH + 0.04, ROOF - 0.03), 0.026, "black", 16)
        dx = s * (W - 0.03)
        box(f"door panel {s:+d}", (dx - 0.012, CAGE + 0.12, CAB_FLOOR + 0.02), (dx + 0.012, DASH - 0.12, CAB_FLOOR + 0.42), "charcoal", bevel=0.008)
        tube(f"door frame {s:+d}", [(dx, CAGE + 0.12, CAB_FLOOR + 0.4), (dx, CAGE + 0.12, door_top), (dx, DASH - 0.12, door_top), (dx, DASH - 0.12, CAB_FLOOR + 0.4)], 0.016, "charcoal")
        cylinder(f"hinge {s:+d}", (dx, DASH - 0.1, CAB_FLOOR + 0.08), (dx, DASH - 0.1, CAB_FLOOR + 0.3), 0.02, "black", 10)
        box(f"latch {s:+d}", (dx - s * 0.02 - 0.012, CAGE + 0.15, CAB_FLOOR + 0.3), (dx - s * 0.02 + 0.012, CAGE + 0.25, CAB_FLOOR + 0.33), "black", bevel=0.005)
        stalk = [(px, DASH + 0.04, CAB_FLOOR + 0.86), (s * (W + 0.2), DASH + 0.12, CAB_FLOOR + 0.82), (s * (W + 0.25), DASH + 0.12, CAB_FLOOR + 0.78)]
        tube(f"mirror stalk {s:+d}", stalk, 0.012, "black")
        m = Vector((s * (W + 0.25), DASH + 0.12, CAB_FLOOR + 0.62))
        box(f"mirror {s:+d}", m - Vector((0.024, 0.11, 0.16)), m + Vector((0.024, 0.11, 0.16)), "black", bevel=0.016)
        box(f"mirror glass {s:+d}", m + Vector((-0.019, -0.116, -0.14)), m + Vector((0.019, -0.11, 0.14)), "glass", bevel=0.003)
    # the cab floor, the cage of white bars behind the seat
    box("cab floor", (-W + 0.03, CAGE, CAB_FLOOR - 0.025), (W - 0.03, DASH - 0.06, CAB_FLOOR + 0.004), "charcoal", bevel=0.008)
    low, high = DECK + 0.04, ROOF - 0.26
    cylinder("cage low", (-(W - 0.06), CAGE, low), ((W - 0.06), CAGE, low), 0.016, "white", 12)
    cylinder("cage high", (-(W - 0.06), CAGE, high), ((W - 0.06), CAGE, high), 0.016, "white", 12)
    for k in range(9):
        x = -0.56 + 1.12 * k / 8
        cylinder(f"cage bar {k}", (x, CAGE, low), (x, CAGE, high), 0.011, "white", 8)
    # the roof's beacon and camera
    bc = Vector((0.32, CAGE + 0.22, ROOF + 0.03))
    cylinder("beacon base", bc, bc + Vector((0, 0, 0.025)), 0.06, "black", 20)
    cylinder("beacon lens", bc + Vector((0, 0, 0.025)), bc + Vector((0, 0, 0.1)), 0.048, "beacon", 20)
    sphere("beacon top", bc + Vector((0, 0, 0.1)), 0.048, "beacon", segs=20)
    box("camera", (-0.39, CAGE - 0.07, ROOF + 0.03), (-0.31, CAGE + 0.04, ROOF + 0.09), "charcoal", bevel=0.012)
    cylinder("camera lens", (-0.35, CAGE - 0.075, ROOF + 0.06), (-0.35, CAGE - 0.065, ROOF + 0.06), 0.017, "glass", 12)
    # the panels' seams: where the bonnet lifts, and the side panels' joints behind the cab; louvred vents in the sides of the
    # back (where an electric tug's batteries breathe)
    hood_y = DASH + 0.21
    hood_z = 0.77 + (0.73 - 0.77) * (hood_y - (DASH + 0.14)) / ((FRONT - 0.13) - (DASH + 0.14))
    box("bonnet seam", (-W + 0.02, hood_y - 0.003, hood_z - 0.004), (W - 0.02, hood_y + 0.003, hood_z + 0.002), "charcoal")
    for s in (-1, 1):
        xs = sorted((s * (W - 0.002), s * (W + 0.002)))
        box(f"side seam {s:+d}", (xs[0], CAGE + 0.02 - 0.003, 0.47), (xs[1], CAGE + 0.02 + 0.003, CAB_FLOOR - 0.03), "charcoal")
        box(f"side seam back {s:+d}", (xs[0], -1.2 - 0.003, 0.47), (xs[1], -1.2 + 0.003, DECK - 0.04), "charcoal")
        vx = sorted((s * (W - 0.003), s * (W + 0.006)))
        box(f"vent {s:+d}", (vx[0], -0.56, 0.66), (vx[1], -0.24, 0.78), "black", bevel=0.004)
        for k in range(4):
            z = 0.675 + k * 0.027
            sx = sorted((s * W, s * (W + 0.014)))
            box(f"vent slat {s:+d} {k}", (sx[0], -0.55, z), (sx[1], -0.25, z + 0.012), "charcoal", bevel=0.003)
    # the fleet number: on the roof, and on each side over the back wheels
    panel("number roof", (0, 0.5 * (CAGE + DASH), ROOF + 0.0315), (0, 0, 1), 0.56, 0.14)
    for s in (-1, 1):
        panel(f"number side {s:+d}", (s * (W + 0.0015), AXLE_B, 0.5 * (0.64 + DECK)), (s, 0, 0), 0.4, 0.1)


def booster(st, z0):
    """A child's backless booster seat standing on the tug's seat cushion (its top at z0): a moulded plastic shell with arms,
    a padded fabric cover on its seat and arms, red belt guides at the front of the arms, a cup holder at one side. The
    driver sits on its pad, BOOST above the cushion."""
    x0, x1 = st.x - 0.215, st.x + 0.215
    b0, b1 = st.y - 0.255, st.y + 0.12
    pad = st.z - 0.11               # (the pad's top, where the driver sits)
    well = pad - 0.045              # (the shell's seat well, under the pad)
    # the shell's base, its front edge rolled up a little under the knees
    side = [(b0, z0), (b1 - 0.03, z0), (b1, z0 + 0.03), (b1, well + 0.02), (b1 - 0.04, well + 0.035), (b0 + 0.02, well), (b0, well)]
    prism("booster base", side, x0 + 0.01, x1 - 0.01, "booster", bevel=0.03, into="interior")
    # the arms: high at the back, falling towards the front; fabric on top; a red belt guide on the inside of each at its front
    top = pad + 0.11
    for s in (-1, 1):
        a0, a1 = sorted((st.x + s * 0.152, st.x + s * 0.215))
        arm = [(b0, z0 + 0.01), (b0, top), (b0 + 0.2, top - 0.005), (b1 - 0.06, top - 0.045), (b1 - 0.01, pad + 0.03), (b1 - 0.01, z0 + 0.01)]
        prism(f"booster arm {s:+d}", arm, a0, a1, "booster", bevel=0.026, into="interior")
        p0, p1 = sorted((st.x + s * 0.158, st.x + s * 0.209))
        box(f"booster arm pad {s:+d}", (p0, b0 + 0.015, top - 0.004), (p1, b0 + 0.19, top + 0.014), "fabric", bevel=0.007, into="interior")
        g0, g1 = sorted((st.x + s * 0.132, st.x + s * 0.166))
        box(f"belt guide {s:+d}", (g0, b1 - 0.115, top - 0.05), (g1, b1 - 0.055, top - 0.012), "clip", bevel=0.008, into="interior")
    # the padded seat between the arms
    box("booster pad", (st.x - 0.152, b0 + 0.012, well - 0.005), (st.x + 0.152, b1 - 0.012, pad), "fabric", bevel=0.02, into="interior")
    # a cup holder at the right side, its well dark
    cx, cy = st.x + 0.245, b0 + 0.09
    cylinder("cup holder", (cx, cy, z0 + 0.005), (cx, cy, pad + 0.03), 0.038, "booster", 20, into="interior")
    cylinder("cup holder well", (cx, cy, pad + 0.026), (cx, cy, pad + 0.034), 0.03, "black", 20, into="interior")


def build_interior():
    """The tug's own seat (a grown-up's) with a child's booster seat on it, the steering wheel on its raked column, the small
    dashboard and the pedals: placed (in the game) round the driver."""
    # (the cab and its seat are a grown-up's, as the tug is a real one's size; a kerbal is small, so it sits on a booster as a
    # child would, BOOST higher: its head under the roof and its hands at the wheel where a person's would be, its feet hanging
    # clear of the pedals)
    st = SEAT
    top = CUSHION
    box("cushion", (st.x - 0.28, st.y - 0.24, top - 0.09), (st.x + 0.28, st.y + 0.18, top), "seat", bevel=0.04, into="interior")
    rest = [(st.y - 0.27, top - 0.02), (st.y - 0.37, top - 0.02), (st.y - 0.44, top + 0.56), (st.y - 0.34, top + 0.56)]
    prism("backrest", rest, st.x - 0.28, st.x + 0.28, "seat", bevel=0.04, into="interior")
    box("seat base", (st.x - 0.18, st.y - 0.2, CAB_FLOOR - 0.02), (st.x + 0.18, st.y + 0.1, top - 0.09), "black", bevel=0.02, into="interior")
    booster(st, top)
    # the dashboard: a small console out of the cowl towards the driver, its top sloping towards them, two gauges on it
    f = CAB_FLOOR
    rear = st.y + 0.45
    console = [(DASH - 0.06, f + 0.08), (rear, f + 0.24), (rear, f + 0.38), (rear + 0.1, f + 0.48), (DASH + 0.02, f + 0.48), (DASH + 0.02, f + 0.26)]
    prism("console", console, st.x - 0.24, st.x + 0.24, "dash", bevel=0.02, into="interior")
    n = Vector((0, -1, 1)).normalized()
    for k, gx in enumerate((-0.13, 0.13)):
        c = Vector((st.x + gx, rear + 0.05, f + 0.43)) + n * 0.002
        cylinder(f"gauge rim {k}", c - n * 0.004, c + n * 0.008, 0.036, "black", 20, into="interior")
        cylinder(f"gauge {k}", c + n * 0.006, c + n * 0.01, 0.029, "glass", 20, into="interior")
    # the steering wheel where the boosted driver's hands are, on a column out of the console's sloping top
    wheel = Vector((st.x, st.y + WHEEL[0], st.z + WHEEL[1]))
    foot = Vector((st.x, rear + 0.05, f + 0.43)) + n * 0.01
    axis = (wheel - foot).normalized()
    cylinder("column", foot - axis * 0.05, wheel, 0.022, "black", 12, into="interior")
    cylinder("column shroud", foot - axis * 0.03, foot + axis * 0.1, 0.038, "dash", 16, into="interior")
    # (the wheel itself a mesh of its own, turned in the game as the car steers: about its column, from its middle)
    PIVOTS["steering"] = (wheel.copy(), axis.copy())
    ring("steering wheel", wheel, axis, 0.16, 0.016, "black", into="steering")
    side = axis.cross(Vector((1, 0, 0))).normalized()
    for k, d in enumerate((Vector((1, 0, 0)), Vector((-1, 0, 0)), side)):
        cylinder(f"spoke {k}", wheel, wheel + d * 0.15, 0.009, "black", 8, into="steering")
    cylinder("hub boss", wheel - axis * 0.025, wheel + axis * 0.02, 0.038, "black", 16, into="steering")
    for k, dx in enumerate((-0.09, 0.09)):
        box(f"pedal {k}", (st.x + dx - 0.035, st.y + 0.25, f + 0.03), (st.x + dx + 0.035, st.y + 0.35, f + 0.07), "black", bevel=0.008, into="interior")


def tyre_profile():
    """The tyre's cross-section (across, out from the axle) and the distance along it to each point; and where on it the tread
    runs (the running face: within about a centimetre of the full radius), which the rubber's material needs."""
    global TREAD_V
    R, h, inner = TYRE_R, TYRE_W / 2, 0.165
    prof = [(-h + 0.012, inner), (-h, inner + 0.02), (-h, R - 0.05)]
    prof += [(-h + 0.04 - 0.04 * math.cos(0.5 * math.pi * k / 6), R - 0.04 + 0.04 * math.sin(0.5 * math.pi * k / 6)) for k in range(1, 7)]
    prof += [(h - 0.04 + 0.04 * math.sin(0.5 * math.pi * k / 6), R - 0.04 + 0.04 * math.cos(0.5 * math.pi * k / 6)) for k in range(0, 6)]
    prof += [(h, R - 0.05), (h, inner + 0.02), (h - 0.012, inner)]
    lengths = [0.0]
    for i in range(1, len(prof)):
        lengths.append(lengths[-1] + math.dist(prof[i - 1], prof[i]))
    on = [lengths[i] for i, p in enumerate(prof) if p[1] > R - 0.012]
    TREAD_V = (min(on) - 0.002, max(on) + 0.002)
    return prof, lengths


def build_tyre():
    """One tyre and its wheel round the x axis, the wheel's face outwards (+x): the game turns it round for the left side."""
    R = TYRE_R
    prof, lengths = tyre_profile()
    around = 72 if not QUICK else 48
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("tread")
    rows = []
    for k in range(around + 1):
        a = 2 * math.pi * k / around
        rows.append([bm.verts.new((x, r * math.cos(a), r * math.sin(a))) if k < around else None for (x, r) in prof])
    rows[around] = rows[0]
    for k in range(around):
        for i in range(len(prof) - 1):
            f = bm.faces.new((rows[k][i], rows[k + 1][i], rows[k + 1][i + 1], rows[k][i + 1]))
            for loop, (kk, ii) in zip(f.loops, ((k, i), (k + 1, i), (k + 1, i + 1), (k, i + 1))):
                loop[uv].uv = (kk / around * 2 * math.pi * R, lengths[ii])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    t = finish("tyre", bm, "rubber", "tyre")
    for p in t.data.polygons:
        p.use_smooth = True
    # the wheel: a dished steel wheel, its hub and five nuts on the outer face
    cylinder("rim", (-0.085, 0, 0), (0.07, 0, 0), 0.168, "hub", 40, into="tyre")
    cylinder("dish", (0.05, 0, 0), (0.075, 0, 0), 0.15, "hub", 40, into="tyre")
    cylinder("boss", (0.07, 0, 0), (0.095, 0, 0), 0.07, "hub", 24, into="tyre")
    for k in range(5):
        a = 2 * math.pi * k / 5
        p = Vector((0.075, 0.1 * math.cos(a), 0.1 * math.sin(a)))
        cylinder(f"nut {k}", p, p + Vector((0.024, 0, 0)), 0.013, "hub", 6, into="tyre")


# ---------------------------------------------------------------- putting together, unwrapping, baking
def apply_modifiers(obj):
    deps = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(deps)
    me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=deps)
    old = obj.data
    obj.modifiers.clear()
    obj.data = me
    bpy.data.meshes.remove(old)


def join(name, objs):
    for o in objs:
        apply_modifiers(o)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    obj.name = name
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.to_mesh(me)
    bm.free()
    me.shade_smooth()
    me.set_sharp_from_angle(angle=math.radians(40))
    return obj


def unwrap(objs, margin):
    for o in objs:
        if "atlas" not in o.data.uv_layers:
            o.data.uv_layers.new(name="atlas")
        o.data.uv_layers.active = o.data.uv_layers["atlas"]
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=margin, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")


TILE = 0.3          # the tread plate's tile (metres)


def deck_uv(obj):
    """The deck's own coordinates: across and along, one unit to a tile of the tread plate."""
    me = obj.data
    if "tile" not in me.uv_layers:
        me.uv_layers.new(name="tile")
    uv = me.uv_layers["tile"]
    for li, loop in enumerate(me.loops):
        co = me.vertices[loop.vertex_index].co
        uv.data[li].uv = (co.x / TILE, co.y / TILE)


def image(name, size, colour=True):
    img = bpy.data.images.new(name, size, size, alpha=True)
    img.colorspace_settings.name = "sRGB" if colour else "Non-Color"
    return img


def target(objs, img):
    """Make `img` what every material of these objects is baked into."""
    for o in objs:
        for m in o.data.materials:
            t = m.node_tree
            n = t.nodes.get("bake target") or t.nodes.new("ShaderNodeTexImage")
            n.name = "bake target"
            n.image = img
            t.nodes.active = n


def bake(objs, kind, img, samples=4, clear=True, **kw):
    """Bake into `img` (through each object's "atlas" coordinates), with this many samples a texel."""
    target(objs, img)
    for o in objs:
        layer = o.data.uv_layers["atlas"]
        o.data.uv_layers.active = layer
        layer.active_render = True
    bpy.context.scene.cycles.samples = samples
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.bake(type=kind, margin=12, use_clear=clear, **kw)     # (wide margins: thin bars keep their own colour in the smaller sizes)
    print("baked", kind, "into", img.name)


def pixels(img):
    a = np.empty(img.size[0] * img.size[1] * 4, np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(img.size[1], img.size[0], 4)


def save(arr, name, colour=True):
    h, w = arr.shape[:2]
    img = bpy.data.images.new(name, w, h, alpha=True)
    img.colorspace_settings.name = "sRGB" if colour else "Non-Color"
    img.pixels.foreach_set(np.ascontiguousarray(arr, np.float32).ravel())
    img.filepath_raw = os.path.join(OUT, name)
    img.file_format = "PNG"
    img.save()
    print("wrote", img.filepath_raw)


def gloss_swap(objs):
    """For the gloss bake: each material's base colour becomes how glossy it is (for good: the colours are baked already)."""
    for m in {m for o in objs for m in o.data.materials}:
        sock = m.node_tree.nodes.get("Principled BSDF").inputs["Base Color"]
        for l in list(sock.links):
            m.node_tree.links.remove(l)
        g = float(m["gloss"])
        sock.default_value = (g, g, g, 1.0)


def compose(colour, ao, gloss, name, dirt=(0.3, 0.28, 0.25)):
    """The paint as the game shows it: its colour darkened in the folds by the ambient occlusion, a little dirt gathered there,
    and its gloss (less where it is dirty) in alpha."""
    c, a, g = pixels(colour), pixels(ao)[..., :1], pixels(gloss)[..., :1]
    # (the occlusion a little blurred, to take the speckle of its sampling out: the islands' margins take the overspill)
    k = np.pad(a, ((1, 1), (1, 1), (0, 0)), mode="edge")
    a = sum(k[1 + dy:k.shape[0] - 1 + dy, 1 + dx:k.shape[1] - 1 + dx] for dy in (-1, 0, 1) for dx in (-1, 0, 1)) / 9.0
    shade =0.28 + 0.72 * np.power(np.clip(a, 0, 1), 1.15)
    rgb = c[..., :3] * shade
    rgb = rgb * (1 - 0.22 * (1 - a)) + np.array(dirt, np.float32) * 0.22 * (1 - a) * shade
    alpha = np.clip(g * (0.55 + 0.45 * a), 0, 1)
    save(np.concatenate([np.clip(rgb, 0, 1), alpha], axis=2), name)


def tread_plate():
    """The deck's tread plate, a tile 0.3 m square: raised bosses in rows that lean in turn, as on real diamond plate."""
    S, cells = (256, 12) if QUICK else (512, 12)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32) + 0.5
    cell = S / cells
    height = np.zeros((S, S), np.float32)
    for oy in (-1, 0, 1):
        for ox in (-1, 0, 1):
            cx = np.floor(xx / cell) + ox
            cy = np.floor(yy / cell) + oy
            dx = xx - (cx + 0.5) * cell
            dy = yy - (cy + 0.5) * cell
            sa = np.where(((cx + cy) % 2) == 0, 0.7071, -0.7071)
            along = (dx * 0.7071 + dy * sa) / (0.46 * cell)
            across = (-dx * sa + dy * 0.7071) / (0.11 * cell)
            r2 = along ** 2 + across ** 2
            height = np.maximum(height, np.where(r2 < 1, np.sqrt(np.clip(1 - r2, 0, 1)), 0))
    height = np.minimum(height * 1.6, 1.0)
    gy, gx = np.gradient(height)
    n = np.stack([-gx * 2.2, -gy * 2.2, np.ones_like(height)], axis=2)
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    save(np.concatenate([n * 0.5 + 0.5, np.ones((S, S, 1), np.float32)], axis=2), "tread_normal.png", colour=False)
    rng = np.random.default_rng(3)
    streak = np.repeat(rng.random((S, 1), np.float32), S, axis=1)
    streak = 0.5 * streak + 0.5 * np.roll(streak, 1, axis=0)
    base = 0.62 + 0.06 * (streak - 0.5) + 0.05 * height
    rgb = np.stack([base, base, base * 1.01], axis=2)
    save(np.concatenate([np.clip(rgb, 0, 1), np.full((S, S, 1), 0.75, np.float32)], axis=2), "tread_colour.png")


# ---------------------------------------------------------------- the mesh file
def quaternion(M):
    """The rotation of a 3x3 matrix (its columns the turned axes) as a quaternion x, y, z, w (as Unity takes one)."""
    t = M[0, 0] + M[1, 1] + M[2, 2]
    if t > 0:
        s = math.sqrt(t + 1.0) * 2
        return ((M[2, 1] - M[1, 2]) / s, (M[0, 2] - M[2, 0]) / s, (M[1, 0] - M[0, 1]) / s, 0.25 * s)
    if M[0, 0] > M[1, 1] and M[0, 0] > M[2, 2]:
        s = math.sqrt(1.0 + M[0, 0] - M[1, 1] - M[2, 2]) * 2
        return (0.25 * s, (M[0, 1] + M[1, 0]) / s, (M[0, 2] + M[2, 0]) / s, (M[2, 1] - M[1, 2]) / s)
    if M[1, 1] > M[2, 2]:
        s = math.sqrt(1.0 + M[1, 1] - M[0, 0] - M[2, 2]) * 2
        return ((M[0, 1] + M[1, 0]) / s, 0.25 * s, (M[1, 2] + M[2, 1]) / s, (M[0, 2] - M[2, 0]) / s)
    s = math.sqrt(1.0 + M[2, 2] - M[0, 0] - M[1, 1]) * 2
    return ((M[0, 2] + M[2, 0]) / s, (M[1, 2] + M[2, 1]) / s, 0.25 * s, (M[1, 0] - M[0, 1]) / s)


EXPORT ={"glass": "glass", "headlamp": "headlamp", "taillamp": "taillamp", "beacon": "beacon", "deck": "deck", "rubber": "tyre", "hub": "tyre"}


def write_mesh(objs_by_name, path, shift=None):
    """
    The meshes as On Foot reads them (little-endian):
        "ONFOOTM2", u32 how many meshes; for each: name (u16 length, utf-8), its pivot (position, 3 f32, and rotation, a
        quaternion x y z w, 4 f32: where its own frame is in its parent's), u32 vertices, then for each vertex position (3 f32),
        normal (3 f32), tangent (4 f32), uv (2 f32); u32 how many submeshes; for each: material (u16 length, utf-8), u32 indices,
        the indices (u32).
    In Unity's axes (x right, y up, z forward: Blender's y and z swapped, which mirrors, so every triangle is turned round and the
    tangents' bitangent sign changes) and metres. `shift`: a point taken off a mesh's positions (the interior's own origin is
    the seat; the steering wheel's frame is in the interior's). A mesh in PIVOTS is given in a frame of its own, its z along
    the axis it turns about and its origin on it.
    """
    def unity(v):
        return np.array((v[0], v[2], v[1]), np.float64)

    pivot_pos, pivot_rot, frames = {}, {}, {}
    for name in objs_by_name:
        pivot_pos[name], pivot_rot[name] = (0.0, 0.0, 0.0), (0.0, 0.0, 0.0, 1.0)
        if name in PIVOTS:
            centre, axis = PIVOTS[name]
            off = (shift or {}).get(name, Vector((0, 0, 0)))
            c, f = unity(centre - off), unity(axis)
            f /= np.linalg.norm(f)
            r = np.cross((0.0, 1.0, 0.0), f)
            r /= np.linalg.norm(r)
            u = np.cross(f, r)
            M = np.stack([r, u, f], axis=1)          # (its own axes, as columns, in its parent's)
            frames[name] = (c, M)
            pivot_pos[name], pivot_rot[name] = tuple(c), quaternion(M)

    out = bytearray(b"ONFOOTM2")
    out += struct.pack("<I", len(objs_by_name))
    for name, obj in objs_by_name.items():
        me = obj.data
        uvname = "tile" if name == "deck" else "atlas"
        me.calc_tangents(uvmap=uvname)
        me.calc_loop_triangles()
        uv = me.uv_layers[uvname]
        normals = me.corner_normals
        off = (shift or {}).get(name, Vector((0, 0, 0)))
        frame = frames.get(name)
        index, verts = {}, []
        subs = {}
        for tri in me.loop_triangles:
            mat = me.materials[tri.material_index].name if me.materials else "paint"
            key_mat = EXPORT.get(mat, "paint")
            ids = []
            for li in tri.loops:
                loop = me.loops[li]
                P, N, T = unity(me.vertices[loop.vertex_index].co - off), unity(normals[li].vector), unity(loop.tangent)
                if frame is not None:
                    c, M = frame
                    P, N, T = M.T @ (P - c), M.T @ N, M.T @ T
                u = uv.data[li].uv
                key = (round(P[0], 5), round(P[1], 5), round(P[2], 5), round(N[0], 3), round(N[1], 3), round(N[2], 3), round(u.x, 5), round(u.y, 5))
                if key not in index:
                    index[key] = len(verts)
                    verts.append((*P, *N, *T, -loop.bitangent_sign, u.x, u.y))
                ids.append(index[key])
            subs.setdefault(key_mat, []).extend((ids[0], ids[2], ids[1]))
        nb = name.encode()
        out += struct.pack("<H", len(nb)) + nb
        out += struct.pack("<7f", *pivot_pos[name], *pivot_rot[name])
        out += struct.pack("<I", len(verts))
        for v in verts:
            out += struct.pack("<12f", *v)
        out += struct.pack("<I", len(subs))
        for mat, ids in subs.items():
            mb = mat.encode()
            out += struct.pack("<H", len(mb)) + mb + struct.pack("<I", len(ids)) + struct.pack(f"<{len(ids)}I", *ids)
        print(f"mesh {name}: {len(verts)} vertices, {sum(len(i) for i in subs.values()) // 3} triangles, submeshes {list(subs)}")
    with open(path, "wb") as f:
        f.write(out)
    print("wrote", path, len(out), "bytes")


# ---------------------------------------------------------------- pictures of the result, for checking
def render(folder):
    os.makedirs(folder, exist_ok=True)
    scene = bpy.context.scene
    # (the baked pictures on the meshes, as the game will draw them, near enough)
    def shown(name, files, uvname, tile=1.0):
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        t = m.node_tree
        bsdf = t.nodes.get("Principled BSDF")
        uv = node(t, "ShaderNodeUVMap", uv_map=uvname)
        tex = node(t, "ShaderNodeTexImage")
        tex.image = bpy.data.images.load(os.path.join(OUT, files[0]))
        if tile != 1.0:
            mp = node(t, "ShaderNodeMapping")
            mp.inputs["Scale"].default_value = (tile, tile, 1)
            t.links.new(uv.outputs["UV"], mp.inputs["Vector"])
            t.links.new(mp.outputs["Vector"], tex.inputs["Vector"])
        else:
            t.links.new(uv.outputs["UV"], tex.inputs["Vector"])
        t.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        inv = node(t, "ShaderNodeMath", operation="SUBTRACT")
        inv.inputs[0].default_value = 1.0
        t.links.new(tex.outputs["Alpha"], inv.inputs[1])
        rough = node(t, "ShaderNodeMapRange")
        rough.inputs["To Min"].default_value = 0.15
        rough.inputs["To Max"].default_value = 0.9
        t.links.new(inv.outputs[0], rough.inputs["Value"])
        t.links.new(rough.outputs["Result"], bsdf.inputs["Roughness"])
        if len(files) > 1:
            nt = node(t, "ShaderNodeTexImage")
            nt.image = bpy.data.images.load(os.path.join(OUT, files[1]))
            nt.image.colorspace_settings.name = "Non-Color"
            t.links.new(tex.inputs["Vector"].links[0].from_socket, nt.inputs["Vector"])
            nm = node(t, "ShaderNodeNormalMap", uv_map=uvname)
            t.links.new(nt.outputs["Color"], nm.inputs["Color"])
            t.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
        return m

    paint = shown("shown paint", ["tug_colour.png"], "atlas")
    deckm = shown("shown deck", ["tread_colour.png", "tread_normal.png"], "tile")
    tyrem = shown("shown tyre", ["tyre_colour.png", "tyre_normal.png"], "atlas")
    for name, mat in (("body", paint), ("interior", paint), ("steering", paint), ("deck", deckm), ("tyre", tyrem)):
        o = bpy.data.objects[name]
        for i in range(len(o.data.materials)):
            o.data.materials[i] = mat
    tyre = bpy.data.objects["tyre"]
    for i, (x, y) in enumerate(((-TRACK, AXLE_F), (TRACK, AXLE_F), (-TRACK, AXLE_B), (TRACK, AXLE_B))):
        o = tyre if i == 1 else bpy.data.objects.new(f"tyre {i}", tyre.data)
        if o is not tyre:
            scene.collection.objects.link(o)
        o.location = (x, y, TYRE_R)
        o.rotation_euler = (0, 0, math.pi if x < 0 else 0)
    # ground, a kerbal-sized figure, sun and sky
    bpy.ops.mesh.primitive_plane_add(size=60, location=(0, 0, 0))
    ground = bpy.context.active_object
    gm = bpy.data.materials.new("ground")
    gm.use_nodes = True
    gm.node_tree.nodes.get("Principled BSDF").inputs["Base Color"].default_value = (0.32, 0.32, 0.31, 1)
    ground.data.materials.append(gm)
    # (kerbal-sized stand-ins: one standing beside the car, about 1.2 m tall; one seated as the game seats its driver)
    km = bpy.data.materials.new("kerbal")
    km.use_nodes = True
    km.node_tree.nodes.get("Principled BSDF").inputs["Base Color"].default_value = (0.55, 0.62, 0.25, 1)
    st, top, f = SEAT, SEAT.z - 0.11, CAB_FLOOR
    for name, a, b, r in (("stand body", (1.3, 0.6, 0.0), (1.3, 0.6, 0.75), 0.22), ("sit body", (0, st.y - 0.08, top), (0, st.y - 0.14, top + 0.42), 0.17),
                          ("sit thigh l", (-0.08, st.y - 0.05, top + 0.06), (-0.08, st.y + 0.28, top + 0.09), 0.07), ("sit thigh r", (0.08, st.y - 0.05, top + 0.06), (0.08, st.y + 0.28, top + 0.09), 0.07),
                          ("sit shin l", (-0.08, st.y + 0.28, top + 0.08), (-0.08, st.y + 0.31, f + 0.04), 0.06), ("sit shin r", (0.08, st.y + 0.28, top + 0.08), (0.08, st.y + 0.31, f + 0.04), 0.06)):
        o = cylinder(name, a, b, r, "white", 16, into=None)
        o.data.materials[0] = km
    for name, c, r in (("stand head", (1.3, 0.6, 0.97), 0.25), ("sit head", (0, st.y - 0.12, top + 0.62), 0.22)):
        o = sphere(name, c, r, "white", into=None, segs=20)
        o.data.materials[0] = km
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 4.0
    sun.data.angle = math.radians(2)
    sun.rotation_euler = (math.radians(50), 0, math.radians(35))
    scene.collection.objects.link(sun)
    world = bpy.data.worlds.new("sky")
    world.use_nodes = True
    world.node_tree.nodes.get("Background").inputs[0].default_value = (0.55, 0.65, 0.8, 1)
    world.node_tree.nodes.get("Background").inputs[1].default_value = 0.8
    scene.world = world
    cam = bpy.data.objects.new("camera", bpy.data.cameras.new("camera"))
    cam.data.lens = 40
    scene.collection.objects.link(cam)
    scene.camera = cam
    scene.render.resolution_x, scene.render.resolution_y = 960, 540
    scene.cycles.samples = 24 if QUICK else 64
    views = {"front_left": (-4.2, 4.6, 1.9), "back_right": (4.0, -4.8, 2.2), "side": (6.5, 0.0, 1.2), "cab": (-1.9, 2.2, 2.4)}
    for name, at in views.items():
        cam.location = at
        look = Vector((0, 0.1, 0.9)) - Vector(at)
        cam.rotation_euler = look.to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(folder, f"tug_{name}.png")
        bpy.ops.render.render(write_still=True)
        print("rendered", scene.render.filepath)
    # the seat close up, with nobody in it: the booster on the grown-up's seat
    for o in scene.objects:
        if o.name.startswith("sit "):
            o.hide_render = True
    cam.location = (-0.75, SEAT.y + 0.95, SEAT.z + 0.35)
    look = Vector((0, SEAT.y - 0.05, CUSHION + 0.1)) - cam.location
    cam.rotation_euler = look.to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(folder, "tug_booster.png")
    bpy.ops.render.render(write_still=True)
    print("rendered", scene.render.filepath)


# ---------------------------------------------------------------- the whole thing
def main():
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = SAMPLES
    global NUMBER_IMAGE
    NUMBER_IMAGE = number_image()
    tyre_profile()                    # (first: where the tread runs, which the rubber's material needs)
    make_materials()
    build_tyre()
    build_body()
    build_interior()
    body = join("body", PARTS["body"])
    for c in CUTTERS:
        bpy.data.objects.remove(c, do_unlink=True)
    interior = join("interior", PARTS["interior"])
    steering = join("steering", PARTS["steering"])
    tyre = join("tyre", PARTS["tyre"])
    deck = join("deck", PARTS["deck"])
    deck_uv(deck)
    unwrap([body, interior, steering], 0.006)
    unwrap([tyre], 0.01)
    # (the four tyres stand where they will, for the shading of the arches and the deck; the deck is in place too)
    shadow = []
    for i, (x, y) in enumerate(((-TRACK, AXLE_F), (TRACK, AXLE_F), (-TRACK, AXLE_B), (TRACK, AXLE_B))):
        o = bpy.data.objects.new(f"tyre shadow {i}", tyre.data)
        bpy.context.scene.collection.objects.link(o)
        o.location = (x, y, TYRE_R)
        o.rotation_euler = (0, 0, math.pi if x < 0 else 0)
        shadow.append(o)
    world = bpy.data.worlds.new("bake")
    scene.world = world
    world.light_settings.distance = 0.6
    paint = [body, interior, steering]
    tyre.hide_render = True           # (the tyre itself stands at the origin, in the body's way: its copies stand in for it)
    col, ao, gl = image("colour", ATLAS), image("ao", ATLAS, False), image("gloss", ATLAS, False)
    bake(paint, "DIFFUSE", col, pass_filter={"COLOR"})
    bake([body], "AO", ao, samples=SAMPLES)
    # (the cab's inside shaded only by its own nooks: within the roof and the walls all of it would be dark, and the game's
    # light under the roof is dim enough already)
    world.light_settings.distance = 0.22
    bake([interior, steering], "AO", ao, samples=SAMPLES, clear=False)
    world.light_settings.distance = 0.6
    gloss_swap(paint)
    bake(paint, "DIFFUSE", gl, pass_filter={"COLOR"})
    for o in shadow:
        bpy.data.objects.remove(o, do_unlink=True)
    # the tyre by itself (its shading from its own wheel only: it turns, so nothing else may shade it)
    tyre.hide_render = False
    for o in (body, interior, deck):
        o.hide_render = True
    tcol, tao, tgl, tnor = image("tyre colour", TYRE_TEX), image("tyre ao", TYRE_TEX, False), image("tyre gloss", TYRE_TEX, False), image("tyre normal", TYRE_TEX, False)
    bake([tyre], "DIFFUSE", tcol, pass_filter={"COLOR"})
    bake([tyre], "AO", tao, samples=SAMPLES)
    bake([tyre], "NORMAL", tnor, normal_space="TANGENT")
    gloss_swap([tyre])
    bake([tyre], "DIFFUSE", tgl, pass_filter={"COLOR"})
    for o in (body, interior, deck):
        o.hide_render = False
    compose(col, ao, gl, "tug_colour.png")
    compose(tcol, tao, tgl, "tyre_colour.png", dirt=(0.18, 0.16, 0.13))
    save(pixels(tnor), "tyre_normal.png", colour=False)
    tread_plate()
    write_mesh({"body": body, "interior": interior, "steering": steering, "tyre": tyre, "deck": deck}, os.path.join(OUT, "tug.mesh"), shift={"interior": SEAT, "steering": SEAT})
    tris = sum(len(o.data.loop_triangles) for o in (body, interior, tyre, deck))
    print(f"triangles: body {len(body.data.loop_triangles)}, interior {len(interior.data.loop_triangles)}, tyre {len(tyre.data.loop_triangles)} (x4), deck {len(deck.data.loop_triangles)}; textures {ATLAS}, {TYRE_TEX}")
    if RENDER:
        render(RENDER)


main()
