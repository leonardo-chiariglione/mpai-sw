"""The CAV Proving Ground: an M-Location of the MMM where a Connected Autonomous Vehicle tests what it can do (2026-11-27).

  python AllApps/MMM/World/make_proving_ground.py

Writes AllApps/MMM/Viewer/world/proving-ground.glb and adds the Proving Ground to AllApps/MMM/Viewer/world/world.json ("environments", "gates"), leaving the rest of the world as it is.

It is not a copy of a place of the Universe: it is a place made for testing. Its roads are given exactly (world.json "roads": centre lines, lanes, lane widths), because a test needs
exact lane widths, curves and signals; the look is boxes and prisms, as the rest of the world. What happens there is set by Item Properties a User with the Right changes at once:
the time of day, the weather, the grip of the road (Properties of the Proving Ground's M-Location), and the traffic lights (Items, each with a Signal).

  - the Town: a grid of two-lane streets (3.5 m lanes), blocks with buildings that hide what comes round a corner, six junctions with traffic lights, zebra crossings and stop lines;
  - the Roundabout, east of the Town, with four arms;
  - the Test Straight, east of the Roundabout: three lanes, 250 m, for braking and lane changes;
  - the Motorway, along the north: two carriageways of two lanes (3.75 m) and a hard shoulder, a median with a barrier, an on-ramp from the Roundabout with an acceleration lane;
  - the Country Road, south of the Roundabout: winding, 6.5 m, between trees, to the Car Park with its marked bays;
  - the Entrance Plaza, at the west end, with the Gate to the Metaverse Square.

Axes: x east, y north, z up, metres (AllApps/MMM/AXES.md). The glTF's origin is the centre of the Proving Ground; world.json says where it is in the world (AT).
"""
import json, math, os, random

from meshes import Mesh

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', 'Viewer', 'world')
AT = (1000.0, 0.0)                         # the centre of the Proving Ground in the world: far from the Square; it is reached through the Gate, not by walking
EXT = (-320.0, -220.0, 320.0, 220.0)      # its extent, relative to its centre
Z_ROAD, Z_MARK, Z_WALK = 0.02, 0.026, 0.12

m = Mesh()
solids = []                                # rectangles that stop a walker, relative to the centre
roads = []                                 # the exact description of the roads, relative to the centre (moved to the world's frame at the end)
random.seed(7)


def ccw(pts):
    a = sum(x0 * y1 - x1 * y0 for (x0, y0), (x1, y1) in zip(pts, pts[1:] + pts[:1]))
    return pts if a > 0 else list(reversed(pts))


def quad(mat, a, b, w, z0, z1):
    """A rectangle from a to b, w wide, as a prism."""
    (ax, ay), (bx, by) = a, b
    ln = math.hypot(bx - ax, by - ay)
    if ln < 1e-6: return
    nx, ny = -(by - ay) / ln * w / 2, (bx - ax) / ln * w / 2
    m.prism(mat, ccw([(ax + nx, ay + ny), (bx + nx, by + ny), (bx - nx, by - ny), (ax - nx, ay - ny)]), z0, z1)


def disc(mat, cx, cy, r, z0, z1, sides=32):
    m.prism(mat, [(cx + r * math.cos(2 * math.pi * k / sides), cy + r * math.sin(2 * math.pi * k / sides)) for k in range(sides)], z0, z1)


def strip(mat, pts, w, z0, z1, joints=True):
    """A band w wide along a polyline: a rectangle for each segment and, where it turns, a disc that fills the gap."""
    for a, b in zip(pts, pts[1:]): quad(mat, a, b, w, z0, z1)
    if joints:
        for p in pts[1:-1]: disc(mat, p[0], p[1], w / 2, z0, z1, 12)


def offset(pts, d):
    """The polyline moved d to its left (d < 0: to its right)."""
    out = []
    for i, (x, y) in enumerate(pts):
        (ax, ay), (bx, by) = pts[max(0, i - 1)], pts[min(len(pts) - 1, i + 1)]
        ln = math.hypot(bx - ax, by - ay) or 1.0
        out.append((x - (by - ay) / ln * d, y + (bx - ax) / ln * d))
    return out


def along(pts, step):
    """Points every step metres along a polyline, with the direction there."""
    out, carry = [], 0.0
    for (ax, ay), (bx, by) in zip(pts, pts[1:]):
        ln = math.hypot(bx - ax, by - ay)
        s = carry
        while s < ln:
            out.append((ax + (bx - ax) * s / ln, ay + (by - ay) * s / ln, (bx - ax) / ln, (by - ay) / ln)); s += step
        carry = s - ln
    return out


def dashes(pts, w, dash, gap, mat='marking'):
    for x, y, dx, dy in along(pts, dash + gap):
        quad(mat, (x, y), (x + dx * dash, y + dy * dash), w, Z_ROAD, Z_MARK)


def solid_line(pts, w, mat='marking'):
    strip(mat, pts, w, Z_ROAD, Z_MARK, joints=False)


def road(id, name, kind, line, lanes, lane_w, two_way=True, **more):
    roads.append(dict(id=id, name=name, kind=kind, centreline=[list(p) for p in line], lanes=lanes, laneWidth=lane_w, twoWay=two_way, **more))


# ------------------------------------------------------------------ the ground
m.box('grass', EXT[0], EXT[1], -0.1, EXT[2], EXT[3], 0.0)

# ------------------------------------------------------------------ the Town: streets every 70 m east-west and 80 m north-south; lanes of 3.5 m; sidewalks of 2.5 m
XS, YS, HALF, WALK = [-250, -180, -110, -40], [-160, -80, 0, 80, 160], 3.5, 2.5
SIGNALISED = [(x, y) for x in (-180, -110) for y in (-80, 0, 80)]
for x in XS: m.box('asphalt', x - HALF, YS[0] - HALF, -0.05, x + HALF, YS[-1] + HALF, Z_ROAD)
for y in YS: m.box('asphalt', XS[0] - HALF, y - HALF, -0.05, XS[-1] + HALF, y + HALF, Z_ROAD)
m.box('asphalt', -275, -HALF, -0.05, XS[0], HALF, Z_ROAD)              # the street from the Entrance Plaza
m.box('asphalt', XS[-1], -HALF, -0.05, 23, HALF, Z_ROAD)               # the street to the Roundabout
for x in XS: road(f'Town-Street-{x}', f'Town street x={x}', 'urban', [(x, YS[0]), (x, YS[-1])], 2, 3.5)
for y in YS: road(f'Town-Street-y{y}', f'Town street y={y}', 'urban', [(XS[0], y), (XS[-1], y)], 2, 3.5)
road('Town-Plaza-Street', 'Street from the Entrance Plaza', 'urban', [(-275, 0), (XS[0], 0)], 2, 3.5)
road('Town-Roundabout-Street', 'Street to the Roundabout', 'urban', [(XS[-1], 0), (23, 0)], 2, 3.5)

# The blocks: a sidewalk round each (raised: the kerb), and on it buildings - or a park - that hide what comes round the corner.
facades = ['facade1', 'facade2', 'facade3', 'facade4']
for i in range(len(XS) - 1):
    for j in range(len(YS) - 1):
        x0, y0, x1, y1 = XS[i] + HALF, YS[j] + HALF, XS[i + 1] - HALF, YS[j + 1] - HALF
        m.box('sidewalk', x0, y0, 0, x1, y1, Z_WALK)
        lx0, ly0, lx1, ly1 = x0 + WALK, y0 + WALK, x1 - WALK, y1 - WALK
        if (i, j) == (1, 2):                                            # a park: grass and trees, nothing hides a child running out
            m.box('grass', lx0, ly0, Z_WALK, lx1, ly1, Z_WALK + 0.02)
            for k in range(10):
                tx, ty = lx0 + 6 + (lx1 - lx0 - 12) * random.random(), ly0 + 6 + (ly1 - ly0 - 12) * random.random()
                m.prism('trunk', [(tx + 0.25 * math.cos(a * math.pi / 3), ty + 0.25 * math.sin(a * math.pi / 3)) for a in range(6)], Z_WALK, 3.0)
                m.prism('leaves', [(tx + 2.2 * math.cos(a * math.pi / 4), ty + 2.2 * math.sin(a * math.pi / 4)) for a in range(8)], 3.0, 6.5)
                solids.append([tx - 0.3, ty - 0.3, tx + 0.3, ty + 0.3])
            continue
        w2, h2 = (lx1 - lx0 - 4) / 2, (ly1 - ly0 - 4) / 2
        for a in range(2):
            for b in range(2):
                bx0, by0 = lx0 + a * (w2 + 4), ly0 + b * (h2 + 4)
                h = random.choice([6, 9, 12, 15, 18, 24])
                m.box(random.choice(facades), bx0, by0, Z_WALK, bx0 + w2, by0 + h2, h)
                m.box('roof', bx0 - 0.2, by0 - 0.2, h, bx0 + w2 + 0.2, by0 + h2 + 0.2, h + 0.3)
                solids.append([bx0, by0, bx0 + w2, by0 + h2])
# the sidewalks round the outside of the Town, open where a street leaves it
def outer_walk(x0, y0, x1, y1): m.box('sidewalk', x0, y0, 0, x1, y1, Z_WALK)
W0, W1, S0, S1 = XS[0] - HALF, XS[-1] + HALF, YS[0] - HALF, YS[-1] + HALF
outer_walk(W0 - WALK, S0 - WALK, W0, -HALF); outer_walk(W0 - WALK, HALF, W0, S1 + WALK)
outer_walk(W1, S0 - WALK, W1 + WALK, -HALF); outer_walk(W1, HALF, W1 + WALK, S1 + WALK)
outer_walk(W0, S0 - WALK, W1, S0); outer_walk(W0, S1, W1, S1 + WALK)

# The markings of the Town: a dashed centre line and solid edge lines along each street, between the junctions; at a junction with traffic lights a zebra crossing and a stop line.
def town_markings(a, b, horizontal, fixed):
    """The markings of the street from a to b (coordinates along it), at fixed (its other coordinate)."""
    P = (lambda s, t: (s, fixed + t)) if horizontal else (lambda s, t: (fixed + t, s))
    sig = lambda s: (s, fixed) in SIGNALISED if horizontal else (fixed, s) in SIGNALISED
    s0 = a + HALF + (8.5 if sig(a) else 0.5); s1 = b - HALF - (8.5 if sig(b) else 0.5)
    if s1 - s0 < 2: return
    dashes([P(s0, 0), P(s1, 0)], 0.12, 3, 6)
    for t in (-HALF + 0.25, HALF - 0.25): solid_line([P(a + HALF + 0.5, t), P(b - HALF - 0.5, t)], 0.12)
    for end, sign in ((a, 1), (b, -1)):
        if not sig(end): continue
        for k in range(7):                                             # the zebra: stripes 0.5 m wide, 3 m long, across the street
            t = -HALF + 0.5 + k * 1.0
            quad('marking', P(end + sign * (HALF + 1.0), t), P(end + sign * (HALF + 4.0), t), 0.5, Z_ROAD, Z_MARK)
        lane = -1 if sign == 1 else 1                                  # the stop line, across the lane that comes to the junction (traffic keeps to the right)
        lane = lane if horizontal else -lane
        quad('marking', P(end + sign * (HALF + 5.0), 0), P(end + sign * (HALF + 5.0), lane * HALF), 0.4, Z_ROAD, Z_MARK)
for x in XS:
    for a, b in zip(YS, YS[1:]): town_markings(a, b, False, x)
for y in YS:
    for a, b in zip(XS, XS[1:]): town_markings(a, b, True, y)
dashes([(-275, 0), (W0, 0)], 0.12, 3, 6); dashes([(W1, 0), (22, 0)], 0.12, 3, 6)

# ------------------------------------------------------------------ the traffic lights: one Item for the north-south way and one for the east-west way of each junction
signals = []
for n, (jx, jy) in enumerate(SIGNALISED, 1):
    c = HALF + 1.4
    signals.append(dict(id=f'Signal-J{n}-NS', name=f'Junction {n}, north-south', junction=[jx, jy], heads=[
        dict(x=jx + c, y=jy - c, yaw=180), dict(x=jx - c, y=jy + c, yaw=0)]))          # for who comes from the south (looking north at it), and from the north
    signals.append(dict(id=f'Signal-J{n}-EW', name=f'Junction {n}, east-west', junction=[jx, jy], heads=[
        dict(x=jx - c, y=jy - c, yaw=90), dict(x=jx + c, y=jy + c, yaw=270)]))         # for who comes from the west, and from the east
    for h in signals[-2]['heads'] + signals[-1]['heads']: solids.append([h['x'] - 0.15, h['y'] - 0.15, h['x'] + 0.15, h['y'] + 0.15])

# ------------------------------------------------------------------ the Roundabout
RB, R_IN, R_OUT = (40.0, 0.0), 10.0, 18.0
disc('asphalt', RB[0], RB[1], R_OUT, -0.05, Z_ROAD, 48)
disc('island', RB[0], RB[1], R_IN, 0, 0.3, 48)
ring = lambda r, n=64: [(RB[0] + r * math.cos(2 * math.pi * k / n), RB[1] + r * math.sin(2 * math.pi * k / n)) for k in range(n + 1)]
solid_line(ring(R_IN + 0.3), 0.15); dashes(ring(R_OUT - 0.3), 0.15, 2, 2)
road('Roundabout', 'Roundabout', 'roundabout', ring((R_IN + R_OUT) / 2, 32), 1, R_OUT - R_IN, two_way=False, centre=list(RB), innerRadius=R_IN, outerRadius=R_OUT, direction='counter-clockwise')
for (dx, dy) in ((-1, 0), (0, 1), (0, -1), (1, 0)):                  # the give-way lines where the arms come in
    p = (RB[0] + dx * (R_OUT + 0.4), RB[1] + dy * (R_OUT + 0.4))
    dashes([(p[0] - dy * HALF, p[1] + dx * HALF), (p[0] + dy * HALF, p[1] - dx * HALF)], 0.3, 0.6, 0.4)

# ------------------------------------------------------------------ the Test Straight: three lanes of 3.75 m, east of the Roundabout
TS = 3 * 3.75
m.box('asphalt', RB[0] + R_OUT - 1, -TS / 2, -0.05, 305, TS / 2, Z_ROAD)
for t in (-TS / 2 + 0.25, TS / 2 - 0.25): solid_line([(RB[0] + R_OUT + 1, t), (305, t)], 0.15)
for t in (-TS / 2 + 3.75, -TS / 2 + 7.5): dashes([(RB[0] + R_OUT + 3, t), (305, t)], 0.15, 6, 12)
for x in range(100, 301, 50): quad('marking', (x, TS / 2 + 0.3), (x, TS / 2 + 1.3), 0.3, 0, Z_MARK + 0.05)   # a marker every 50 m, beside the road
road('Test-Straight', 'Test Straight', 'test', [(RB[0] + R_OUT, 0), (305, 0)], 3, 3.75, two_way=False, direction='east')

# ------------------------------------------------------------------ the Motorway, along the north: eastbound south of the median, westbound north of it
MY = 190.0
m.box('asphalt', -310, MY - 11.5, -0.05, 310, MY - 1.5, Z_ROAD); m.box('asphalt', -310, MY + 1.5, -0.05, 310, MY + 11.5, Z_ROAD)
m.box('island', -310, MY - 1.5, 0, 310, MY + 1.5, 0.05)
m.box('barrier', -310, MY - 0.15, 0, 310, MY + 0.15, 0.8); solids.append([-310, MY - 0.15, 310, MY + 0.15])
for t in (MY - 9.0, MY - 1.65, MY + 1.65, MY + 9.0): solid_line([(-310, t), (310, t)], 0.2)
for t in (MY - 5.25, MY + 5.25): dashes([(-310, t), (310, t)], 0.15, 6, 12)
road('Motorway-East', 'Motorway, eastbound', 'motorway', [(-310, MY - 5.25), (310, MY - 5.25)], 2, 3.75, two_way=False, direction='east', hardShoulder=2.5)
road('Motorway-West', 'Motorway, westbound', 'motorway', [(310, MY + 5.25), (-310, MY + 5.25)], 2, 3.75, two_way=False, direction='west', hardShoulder=2.5)

# The on-ramp: north from the Roundabout, one way, then a curve to the east and an acceleration lane that joins the eastbound carriageway.
RAMP_W = 5.0
arc_c, arc_r = (RB[0] + 36.75, 140.0), 36.75
ramp = [(RB[0], R_OUT - 1), (RB[0], 140.0)] + [(arc_c[0] + arc_r * math.cos(a), arc_c[1] + arc_r * math.sin(a)) for a in [math.pi - k * math.pi / 2 / 16 for k in range(1, 17)]]
ramp += [(240.0, 176.75), (300.0, MY - 7.25)]
strip('asphalt', ramp, RAMP_W, -0.05, Z_ROAD)
for d in (RAMP_W / 2 - 0.25, -RAMP_W / 2 + 0.25): solid_line(offset(ramp[:-1], d), 0.15)
road('On-Ramp', 'Motorway on-ramp', 'ramp', ramp, 1, RAMP_W, two_way=False, direction='north, then east')

# ------------------------------------------------------------------ the Country Road: south of the Roundabout, winding between trees, to the Car Park
cr = [(RB[0], -R_OUT + 1), (RB[0], -60.0)] + [(RB[0] + 200 * s, -60 - 80 * s - 22 * math.sin(2.5 * math.pi * s)) for s in [k / 60 for k in range(1, 61)]] + [(250.0, -170.0)]
CR_W = 6.5
strip('asphalt', cr, CR_W, -0.05, Z_ROAD)
for d in (CR_W / 2 - 0.2, -CR_W / 2 + 0.2): solid_line(offset(cr, d), 0.12)
dashes(cr[1:], 0.12, 3, 9)
road('Country-Road', 'Country Road', 'rural', cr, 2, 3.25)
for k, (x, y, dx, dy) in enumerate(along(cr[2:-1], 22)):              # the trees: on both sides, 8 m from the centre
    side = 1 if k % 2 else -1
    tx, ty = x - dy * 8 * side, y + dx * 8 * side
    m.prism('trunk', [(tx + 0.25 * math.cos(a * math.pi / 3), ty + 0.25 * math.sin(a * math.pi / 3)) for a in range(6)], 0, 3.0)
    m.prism('leaves', [(tx + 2.4 * math.cos(a * math.pi / 4), ty + 2.4 * math.sin(a * math.pi / 4)) for a in range(8)], 3.0, 7.0)
    solids.append([tx - 0.3, ty - 0.3, tx + 0.3, ty + 0.3])

# The Car Park: 70 x 40 m, bays of 2.5 x 5 m in four rows.
CP = (215.0, -210.0, 290.0, -170.0)
m.box('asphalt', CP[0], CP[1], -0.05, CP[2], CP[3], Z_ROAD)
for row_y, facing in ((CP[3] - 5, -1), (CP[1] + 5, 1), (-185.5, 1), (-194.5, -1)):
    for k in range(27):
        x = CP[0] + 2 + k * 2.5
        quad('marking', (x, row_y), (x, row_y - facing * 5), 0.12, Z_ROAD, Z_MARK)
road('Car-Park', 'Car Park', 'parking', [(CP[0], CP[1]), (CP[2], CP[1]), (CP[2], CP[3]), (CP[0], CP[3])], 0, 2.5, two_way=True, bay=[2.5, 5.0])

# ------------------------------------------------------------------ the Entrance Plaza, with the Gate to the Metaverse Square
PLAZA = (-318.0, -15.0, -275.0, 15.0)
m.box('plaza', PLAZA[0], PLAZA[1], 0, PLAZA[2], PLAZA[3], 0.05)

m.glb(os.path.join(OUT, 'proving-ground.glb'))

# ------------------------------------------------------------------ world.json: the Proving Ground, in the world's frame
def W(p): return [round(p[0] + AT[0], 3), round(p[1] + AT[1], 3)]
for r in roads:
    r['centreline'] = [W(p) for p in r['centreline']]
    if 'centre' in r: r['centre'] = W(r['centre'])
for s in signals:
    s['junction'] = W(s['junction'])
    for h in s['heads']: h['x'], h['y'] = W((h['x'], h['y']))
env = dict(id='ProvingGround', name='CAV Proving Ground', model='world/proving-ground.glb', x=AT[0], y=AT[1],
           bounds=[AT[0] + EXT[0], AT[1] + EXT[1], AT[0] + EXT[2], AT[1] + EXT[3]],
           spawn=dict(x=AT[0] - 290, y=AT[1], radius=3.0, yaw=270),
           conditions=dict(TimeOfDay=12, Weather='Clear', Friction=0.9),
           solids=[[round(v, 3) for v in (r[0] + AT[0], r[1] + AT[1], r[2] + AT[0], r[3] + AT[1])] for r in solids],
           roads=roads, signals=signals)
# The Gate: one Item, with a side on the Square and a side on the Entrance Plaza. Walked through, it takes a Persona to the other side.
gate = dict(id='Gate-ProvingGround', name='CAV Proving Ground', leads='ProvingGround', at='MVS', width=5.0, sides=[
    dict(at='MVS', x=0.0, y=-13.0, axis='y', outside=1, arrive=dict(at='ProvingGround', x=AT[0] - 296, y=AT[1], yaw=270)),
    dict(at='ProvingGround', x=AT[0] - 302, y=AT[1], axis='x', outside=1, arrive=dict(at='MVS', x=0.0, y=-10.0, yaw=0))])

path = os.path.join(OUT, 'world.json')
raw = open(path, 'rb').read()
crlf = b'\r\n' in raw
world = json.loads(raw.decode('utf-8'))
world['environments'] = [env]
world['gates'] = [gate]
text = json.dumps(world, indent=1)
open(path, 'wb').write((text.replace('\n', '\r\n') if crlf else text).encode('utf-8'))
print('wrote proving-ground.glb and world.json:', len(roads), 'roads,', len(signals), 'traffic lights,', len(solids), 'solids')
