"""Boxes and prisms grouped by material, written as glTF (.glb): what the buildings of the MMM's world are made of (make_world.py, make_proving_ground.py).

Axes: space - x east, y north, z up, metres (AllApps/MMM/AXES.md). glTF is Y-up, so a point (x, y, z) of space is written as (x, z, -y).
"""
import json, math, struct

COL = dict(wall=(0.93, 0.92, 0.88, 1), floor=(0.62, 0.45, 0.28, 1), ceiling=(0.97, 0.97, 0.95, 1), table=(0.33, 0.2, 0.12, 1),
           chair=(0.15, 0.25, 0.45, 1), glass=(0.7, 0.85, 1.0, 0.25), door=(0.45, 0.3, 0.18, 1), desk=(0.55, 0.38, 0.22, 1),
           screen=(0.05, 0.06, 0.08, 1), trim=(0.3, 0.3, 0.32, 1), carpet=(0.35, 0.18, 0.2, 1), brick=(0.7, 0.42, 0.32, 1), roof=(0.3, 0.3, 0.34, 1),
           shelf=(0.4, 0.28, 0.16, 1), counter=(0.8, 0.8, 0.82, 1))


class Mesh:
    """Boxes, grouped by material, written as one glTF mesh with one primitive per material."""
    def __init__(self): self.mats = {}; self.prims = {}

    def box(self, mat, x0, y0, z0, x1, y1, z1):
        if x1 - x0 <= 1e-6 or y1 - y0 <= 1e-6 or z1 - z0 <= 1e-6: return
        p = self.prims.setdefault(mat, ([], [], []))
        pos, nor, idx = p
        X0, X1, Y0, Y1, Z0, Z1 = x0, x1, z0, z1, -y1, -y0           # to glTF: (x, z, -y)
        faces = [((0, 0, 1), [(X0, Y0, Z1), (X1, Y0, Z1), (X1, Y1, Z1), (X0, Y1, Z1)]), ((0, 0, -1), [(X1, Y0, Z0), (X0, Y0, Z0), (X0, Y1, Z0), (X1, Y1, Z0)]),
                 ((1, 0, 0), [(X1, Y0, Z1), (X1, Y0, Z0), (X1, Y1, Z0), (X1, Y1, Z1)]), ((-1, 0, 0), [(X0, Y0, Z0), (X0, Y0, Z1), (X0, Y1, Z1), (X0, Y1, Z0)]),
                 ((0, 1, 0), [(X0, Y1, Z1), (X1, Y1, Z1), (X1, Y1, Z0), (X0, Y1, Z0)]), ((0, -1, 0), [(X0, Y0, Z0), (X1, Y0, Z0), (X1, Y0, Z1), (X0, Y0, Z1)])]
        for n, vs in faces:
            b = len(pos)
            pos.extend(vs); nor.extend([n] * 4); idx.extend([b, b + 1, b + 2, b, b + 2, b + 3])

    def prism(self, mat, pts, z0, z1):
        """A vertical prism on a convex polygon pts = [(x, y), ...] (counter-clockwise seen from above), from z0 to z1: a round table, a chair turned any way."""
        if z1 - z0 <= 1e-6 or len(pts) < 3: return
        pos, nor, idx = self.prims.setdefault(mat, ([], [], []))
        tr = lambda x, y, z: (x, z, -y)                                  # to glTF: (x, z, -y)
        def face(n, vs):
            b = len(pos); pos.extend(tr(*v) for v in vs); nor.extend([tr(*n)] * len(vs))
            idx.extend(i for k in range(1, len(vs) - 1) for i in (b, b + k, b + k + 1))
        face((0, 0, 1), [(x, y, z1) for x, y in pts]); face((0, 0, -1), [(x, y, z0) for x, y in reversed(pts)])
        for k, (a, b) in enumerate(zip(pts, pts[1:] + pts[:1])):
            dx, dy = b[0] - a[0], b[1] - a[1]; ln = math.hypot(dx, dy) or 1.0
            face((dy / ln, -dx / ln, 0), [(a[0], a[1], z0), (b[0], b[1], z0), (b[0], b[1], z1), (a[0], a[1], z1)])

    def glb(self, path):
        bin_ = bytearray(); views, accs, prims, mats = [], [], [], []
        def add(data, fmt, target, comp, typ, count, mn=None, mx=None):
            while len(bin_) % 4: bin_.append(0)
            views.append({'buffer': 0, 'byteOffset': len(bin_), 'byteLength': len(data), 'target': target}); bin_.extend(data)
            a = {'bufferView': len(views) - 1, 'componentType': comp, 'count': count, 'type': typ}
            if mn is not None: a['min'], a['max'] = mn, mx
            accs.append(a); return len(accs) - 1
        for mat, (pos, nor, idx) in self.prims.items():
            c = COL[mat]
            m = {'name': mat, 'pbrMetallicRoughness': {'baseColorFactor': list(c), 'metallicFactor': 0.0, 'roughnessFactor': 0.85}}
            if c[3] < 1: m['alphaMode'] = 'BLEND'; m['doubleSided'] = True
            mats.append(m)
            p = add(b''.join(struct.pack('<3f', *v) for v in pos), 'f', 34962, 5126, 'VEC3', len(pos), [min(v[i] for v in pos) for i in range(3)], [max(v[i] for v in pos) for i in range(3)])
            n = add(b''.join(struct.pack('<3f', *v) for v in nor), 'f', 34962, 5126, 'VEC3', len(nor))
            i = add(b''.join(struct.pack('<I', v) for v in idx), 'I', 34963, 5125, 'SCALAR', len(idx), [min(idx)], [max(idx)])
            prims.append({'attributes': {'POSITION': p, 'NORMAL': n}, 'indices': i, 'material': len(mats) - 1})
        doc = {'asset': {'version': '2.0', 'generator': 'AllApps/MMM/World/make_world.py'}, 'scene': 0, 'scenes': [{'nodes': [0]}], 'nodes': [{'mesh': 0}], 'meshes': [{'primitives': prims}],
               'materials': mats, 'buffers': [{'byteLength': len(bin_)}], 'bufferViews': views, 'accessors': accs}
        js = json.dumps(doc, separators=(',', ':')).encode()
        while len(js) % 4: js += b' '
        while len(bin_) % 4: bin_.append(0)
        data = struct.pack('<III', 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(bin_)) + struct.pack('<II', len(js), 0x4E4F534A) + js + struct.pack('<II', len(bin_), 0x004E4942) + bytes(bin_)
        open(path, 'wb').write(data)


# THE CAV PROVING GROUND (make_proving_ground.py, 2026-11-27): the road, its markings, the land round it.
COL.update(asphalt=(0.17, 0.17, 0.18, 1), marking=(0.95, 0.95, 0.93, 1), yellow=(0.95, 0.78, 0.15, 1), kerb=(0.62, 0.62, 0.6, 1), sidewalk=(0.72, 0.71, 0.68, 1),
           grass=(0.36, 0.55, 0.3, 1), island=(0.3, 0.5, 0.27, 1), barrier=(0.75, 0.76, 0.78, 1), trunk=(0.35, 0.24, 0.14, 1), leaves=(0.2, 0.42, 0.18, 1),
           facade1=(0.78, 0.74, 0.66, 1), facade2=(0.62, 0.66, 0.72, 1), facade3=(0.7, 0.55, 0.45, 1), facade4=(0.85, 0.84, 0.8, 1), gate=(0.25, 0.32, 0.45, 1),
           portal=(0.45, 0.75, 1.0, 0.35), plaza=(0.66, 0.64, 0.6, 1))
