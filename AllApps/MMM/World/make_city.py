"""The city round the Metaverse Square: a grid of regular streets, as in New York, with buildings on both sides, and the taxi rank (2026-11-28, M3329).

  python AllApps/MMM/World/make_city.py

Run after make_world.py and make_proving_ground.py: writes AllApps/MMM/Viewer/world/city.glb and taxi.glb, and adds "city" to AllApps/MMM/Viewer/world/world.json (widening its
"bounds" and its "ground" to the city), leaving the rest of the world as it is.

The Square, the MPAI building, the Library and the Cafe stay as they are: they are one block of the grid, the campus. Round it, avenues (north-south) and streets (east-west) every
80 m, two lanes of 3.5 m and sidewalks of 3 m; the blocks have buildings of different heights on all their sides. Rank Street goes south from the south edge of the Square to the
first street: on it, by the Square, the taxi rank - a lay-by with three bays, where the taxis wait facing south. The roads are described exactly in world.json ("city.roads"),
as those of the Proving Ground are: what the Offline Map of a taxi is made from. Later: roads on hills, with curves, beyond the grid.

Axes: x east, y north, z up, metres (AllApps/MMM/AXES.md).
"""
import json, math, os, random

from meshes import Mesh

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', 'Viewer', 'world')
XS = [-146.0, -66.0, 66.0, 146.0, 226.0]      # the avenues (north-south), their centre lines
YS = [-146.0, -66.0, 66.0, 146.0]             # the streets (east-west)
HALF, WALK = 3.5, 3.0                          # a lane each way; the sidewalk of a block
CAMPUS = (-66.0, -66.0, 66.0, 66.0)           # the block of the Square and its buildings, between the avenues and the streets round it
RANK_X, SQUARE_EDGE = -9.0, -15.0              # Rank Street: its centre line, from the south edge of the Square
LAYBY = (-15.5, -38.0, RANK_X - HALF, -18.0)   # the taxi rank: a lay-by on the west side of Rank Street
Z_ROAD, Z_MARK, Z_WALK = 0.02, 0.026, 0.12

m = Mesh()
solids, roads = [], []
random.seed(11)


def quad(mat, a, b, w, z0, z1):
    (ax, ay), (bx, by) = a, b
    ln = math.hypot(bx - ax, by - ay)
    if ln < 1e-6: return
    nx, ny = -(by - ay) / ln * w / 2, (bx - ax) / ln * w / 2
    pts = [(ax + nx, ay + ny), (bx + nx, by + ny), (bx - nx, by - ny), (ax - nx, ay - ny)]
    if sum(x0 * y1 - x1 * y0 for (x0, y0), (x1, y1) in zip(pts, pts[1:] + pts[:1])) < 0: pts.reverse()
    m.prism(mat, pts, z0, z1)


def dashes(a, b, w=0.12, dash=3.0, gap=6.0):
    (ax, ay), (bx, by) = a, b
    ln = math.hypot(bx - ax, by - ay); dx, dy = (bx - ax) / ln, (by - ay) / ln; s = 0.0
    while s + dash <= ln:
        quad('marking', (ax + dx * s, ay + dy * s), (ax + dx * (s + dash), ay + dy * (s + dash)), w, Z_ROAD, Z_MARK); s += dash + gap


def road(id, name, line, lanes=2, lane_w=3.5, **more):
    roads.append(dict(id=id, name=name, kind='urban', centreline=[list(p) for p in line], lanes=lanes, laneWidth=lane_w, twoWay=True, **more))


# ------------------------------------------------------------------ the carriageways
X0, X1, Y0, Y1 = XS[0], XS[-1], YS[0], YS[-1]
for x in XS:
    m.box('asphalt', x - HALF, Y0 - HALF, -0.05, x + HALF, Y1 + HALF, Z_ROAD)
    road(f'Avenue-{int(x)}', f'Avenue x={int(x)}', [(x, Y0), (x, Y1)])
for y in YS:
    m.box('asphalt', X0 - HALF, y - HALF, -0.05, X1 + HALF, y + HALF, Z_ROAD)
    road(f'Street-{int(y)}', f'Street y={int(y)}', [(X0, y), (X1, y)])
m.box('asphalt', RANK_X - HALF, YS[1] + HALF, -0.05, RANK_X + HALF, SQUARE_EDGE, Z_ROAD)
road('Rank-Street', 'Rank Street', [(RANK_X, SQUARE_EDGE), (RANK_X, YS[1])])

# The Proving Ground is part of the grid (M3329): Street y=-66 goes on, past the last avenue, to the west edge of the Proving Ground, where its own street begins.
world_path = os.path.join(OUT, 'world.json')
_pg = next((e for e in json.loads(open(world_path, 'rb').read().decode('utf-8')).get('environments', []) if e['id'] == 'ProvingGround'), None)
if _pg:
    pg_edge, pg_y = _pg['bounds'][0], _pg['y']
    assert pg_y == YS[1] and pg_edge > X1 + HALF, 'make_proving_ground.py places the Proving Ground east of the city, on Street y=-66'
    m.box('asphalt', X1 + HALF, pg_y - HALF, -0.05, pg_edge + 2, pg_y + HALF, Z_ROAD)
    road('Proving-Ground-Street', 'Street to the CAV Proving Ground', [(X1, pg_y), (pg_edge, pg_y)], leads='ProvingGround')

# The markings: a dashed centre line between the junctions, a zebra crossing on each arm of a junction.
for x in XS:
    for a, b in zip(YS, YS[1:]): dashes((x, a + HALF + 6), (x, b - HALF - 6))
for y in YS:
    for a, b in zip(XS, XS[1:]): dashes((a + HALF + 6, y), (b - HALF - 6, y))
dashes((RANK_X, SQUARE_EDGE - 3), (RANK_X, YS[1] + HALF + 6))
def zebra(cx, cy, horizontal):
    for k in range(7):
        t = -HALF + 0.5 + k * 1.0
        if horizontal: quad('marking', (cx - 1.5, cy + t), (cx + 1.5, cy + t), 0.5, Z_ROAD, Z_MARK)
        else: quad('marking', (cx + t, cy - 1.5), (cx + t, cy + 1.5), 0.5, Z_ROAD, Z_MARK)
for x in XS:
    for y in YS:
        for d in (-1, 1):
            if X0 <= x + d * (HALF + 3) <= X1: zebra(x + d * (HALF + 3), y, True)
            if Y0 <= y + d * (HALF + 3) <= Y1: zebra(x, y + d * (HALF + 3), False)

# ------------------------------------------------------------------ the blocks: sidewalks, and buildings on all their sides
facades = ['facade1', 'facade2', 'facade3', 'facade4']
def block(x0, y0, x1, y1):
    m.box('sidewalk', x0, y0, 0, x1, y1, Z_WALK)
    lx0, ly0, lx1, ly1 = x0 + WALK, y0 + WALK, x1 - WALK, y1 - WALK
    # a ring of buildings along the four sides, a courtyard in the middle; a gap every few buildings
    depth = 18.0
    def row(ax0, ay0, ax1, ay1, along_x):
        length = (ax1 - ax0) if along_x else (ay1 - ay0); s = 0.0
        while s < length - 4:
            w = min(random.choice([12, 14, 16, 20, 24]), length - s)
            if w < 6: break
            h = random.choice([12, 15, 18, 24, 30, 36, 45])
            if along_x: bx0, by0, bx1, by1 = ax0 + s, ay0, ax0 + s + w, ay1
            else: bx0, by0, bx1, by1 = ax0, ay0 + s, ax1, ay0 + s + w
            m.box(random.choice(facades), bx0, by0, Z_WALK, bx1, by1, h)
            m.box('roof', bx0 - 0.2, by0 - 0.2, h, bx1 + 0.2, by1 + 0.2, h + 0.3)
            solids.append([round(bx0, 2), round(by0, 2), round(bx1, 2), round(by1, 2)])
            s += w + random.choice([0, 0, 2, 3])
    row(lx0, ly0, lx1, ly0 + depth, True)                       # south side
    row(lx0, ly1 - depth, lx1, ly1, True)                       # north side
    row(lx0, ly0 + depth + 2, lx0 + depth, ly1 - depth - 2, False)   # west side
    row(lx1 - depth, ly0 + depth + 2, lx1, ly1 - depth - 2, False)   # east side
for i in range(len(XS) - 1):
    for j in range(len(YS) - 1):
        x0, y0, x1, y1 = XS[i] + HALF, YS[j] + HALF, XS[i + 1] - HALF, YS[j + 1] - HALF
        if (XS[i], YS[j], XS[i + 1], YS[j + 1]) == CAMPUS: continue
        block(x0, y0, x1, y1)
# the campus: only a sidewalk round it, open where Rank Street leaves it
cx0, cy0, cx1, cy1 = CAMPUS[0] + HALF, CAMPUS[1] + HALF, CAMPUS[2] - HALF, CAMPUS[3] - HALF
m.box('sidewalk', cx0, cy1 - WALK, 0, cx1, cy1, Z_WALK)
m.box('sidewalk', cx0, cy0, 0, cx0 + WALK, cy1 - WALK, Z_WALK)
m.box('sidewalk', cx1 - WALK, cy0, 0, cx1, cy1 - WALK, Z_WALK)
m.box('sidewalk', cx0 + WALK, cy0, 0, RANK_X - HALF, cy0 + WALK, Z_WALK)
m.box('sidewalk', RANK_X + HALF, cy0, 0, cx1 - WALK, cy0 + WALK, Z_WALK)

# ------------------------------------------------------------------ the taxi rank: a lay-by with three bays, a sign, a shelter
m.box('asphalt', LAYBY[0], LAYBY[1], -0.05, LAYBY[2], LAYBY[3], Z_ROAD)
bays = []
for k in range(3):
    y_top = LAYBY[3] - k * 6.5
    quad('yellow', (LAYBY[0] + 0.1, y_top), (LAYBY[2], y_top), 0.12, Z_ROAD, Z_MARK)
    bays.append(dict(id=f'Taxi-Bay-{k + 1}', x=round((LAYBY[0] + LAYBY[2]) / 2, 2), y=round(y_top - 3.25, 2), yaw=180))
quad('yellow', (LAYBY[0] + 0.1, LAYBY[3] - 19.5), (LAYBY[2], LAYBY[3] - 19.5), 0.12, Z_ROAD, Z_MARK)
for k in range(3): quad('yellow', (LAYBY[0] + 0.6, LAYBY[3] - 2 - k * 6.5), (LAYBY[0] + 0.6, LAYBY[3] - 4.5 - k * 6.5), 0.15, Z_ROAD, Z_MARK)
m.box('sidewalk', LAYBY[0] - 2.5, LAYBY[1], 0, LAYBY[0], SQUARE_EDGE, Z_WALK)      # where the passengers wait
m.box('gate', LAYBY[0] - 2.2, LAYBY[3] - 1, Z_WALK, LAYBY[0] - 2.0, LAYBY[3] - 0.8, 3.2)                # the shelter: two posts and a roof
m.box('gate', LAYBY[0] - 2.2, LAYBY[3] - 6, Z_WALK, LAYBY[0] - 2.0, LAYBY[3] - 5.8, 3.2)
m.box('roof', LAYBY[0] - 2.5, LAYBY[3] - 6.5, 3.2, LAYBY[0] - 0.3, LAYBY[3] - 0.3, 3.35)
m.box('gate', LAYBY[0] - 1.3, SQUARE_EDGE - 1.6, Z_WALK, LAYBY[0] - 1.1, SQUARE_EDGE - 1.4, 3.6)          # the sign: a pole and a yellow board
m.box('yellow', LAYBY[0] - 1.9, SQUARE_EDGE - 1.65, 2.9, LAYBY[0] - 0.5, SQUARE_EDGE - 1.35, 3.6)
solids += [[LAYBY[0] - 2.2, LAYBY[3] - 1, LAYBY[0] - 2.0, LAYBY[3] - 0.8], [LAYBY[0] - 2.2, LAYBY[3] - 6, LAYBY[0] - 2.0, LAYBY[3] - 5.8],
           [LAYBY[0] - 1.3, SQUARE_EDGE - 1.6, LAYBY[0] - 1.1, SQUARE_EDGE - 1.4]]
m.glb(os.path.join(OUT, 'city.glb'))

# ------------------------------------------------------------------ the taxi: the vehicle of the Proving Ground, yellow, with a sign on its roof (taxi.glb). It faces south in its own frame.
car = Mesh()
car.box('yellow', -0.9, -2.25, 0.3, 0.9, 2.25, 0.95)
car.box('cabin', -0.82, -0.7, 0.95, 0.82, 1.5, 1.45)
car.box('roof', -0.3, 0.2, 1.45, 0.3, 0.6, 1.7)
for x in (-0.95, 0.68):
    for y in (-1.45, 1.35): car.box('tyre', x, y - 0.33, 0, x + 0.27, y + 0.33, 0.65)
for x in (-0.75, 0.45): car.box('headlight', x, -2.27, 0.62, x + 0.3, -2.2, 0.8); car.box('taillight', x, 2.2, 0.65, x + 0.3, 2.27, 0.82)
car.glb(os.path.join(OUT, 'taxi.glb'))

# ------------------------------------------------------------------ world.json
path = os.path.join(OUT, 'world.json')
raw = open(path, 'rb').read(); crlf = b'\r\n' in raw
world = json.loads(raw.decode('utf-8'))
extent = [X0 - HALF - WALK, Y0 - HALF - WALK, X1 + HALF + WALK, Y1 + HALF + WALK]
world['city'] = dict(model='world/city.glb', extent=extent, solids=solids, roads=roads,
                     taxiRank=dict(id='Taxi-Rank', name='Taxi rank', at='MVS', x=round(LAYBY[0] - 1.2, 2), y=SQUARE_EDGE - 1.5, layby=list(LAYBY), bays=bays, taxiModel='world/taxi.glb'))
world['bounds'] = extent
world['ground']['size'] = 2 * max(abs(v) for v in extent) + 40
text = json.dumps(world, indent=1)
open(path, 'wb').write((text.replace('\n', '\r\n') if crlf else text).encode('utf-8'))
print('wrote city.glb, taxi.glb and world.json:', len(roads), 'roads,', len(solids), 'solids,', len(bays), 'bays; extent', extent)
