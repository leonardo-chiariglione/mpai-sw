"""MPAI Knowledge Graph (MKG): built from the repository, as RDF (Turtle and JSON-LD).

Kinds (a node is identified by its kind and its acronym, so the same acronym can be a
Standard, an AIM and a Data Type):
    Standard, Part, AIM, DataType, Action, L3,
    Implementer, MPAIStore, PerformanceAssessor, Framework (AIF), UserAgent, Unit.
Relations: specifies, partOf, port (AIM - Data Type, with direction and PortNumber),
    subAIM, flow, uses (Data Type/Action - Data Type), instanceOf (L3 - AIM), produces,
    receives, communicatesWith, usesUnit, code (pointer to the code that implements it).

The standards, their parts and web addresses are those of M3260 (standards.json);
only the latest version of each standard is read.

Usage:  python build_mkg.py [STANDARD-FOLDER ...]       (default: all of them)
"""
import json, re, sys, glob, os
from rdflib import Graph, Namespace, URIRef, Literal
from rdflib.namespace import RDF, XSD

ROOT = os.path.dirname(os.path.abspath(__file__))
DI = os.path.dirname(ROOT)
SCH = os.path.join(DI, 'schemas')
AI_ROOT = os.environ.get('MPAI_AI_ROOT') or os.path.join(os.path.dirname(DI), 'AI')   # a second tree, read for the standards that this one does not have
AI_SCH = os.path.join(AI_ROOT, 'schemas')
SCHEMA_BASE = 'https://schemas.mpai.community/'
GITHUB = 'https://github.com/leonardo-chiariglione/mpai-sw/blob/main/'

MKG = Namespace('https://mpai.community/kg/vocab#')
KIND_NS = {k: Namespace('https://mpai.community/kg/%s/' % k.lower())
           for k in ('Standard', 'Part', 'AIM', 'DataType', 'Action', 'L3', 'Implementer', 'MPAIStore',
                     'PerformanceAssessor', 'Framework', 'UserAgent', 'Unit', 'Software', 'Library', 'App')}
STD, PRT, AIM, DT, ACT, L3 = (KIND_NS[k] for k in ('Standard', 'Part', 'AIM', 'DataType', 'Action', 'L3'))
import mkg_software as _ms
GIT = _ms.Git(DI, GITHUB)        # a GitHub link only for what is in git, with the case git has (2026-11-12)
SOFTWARE_SKIP = {'bin', 'obj', 'deploy', 'legacy', '.git', 'node_modules', '.backups', 'Models', 'Output', 'SharedStorage', 'TestData', 'Datasets', 'Lib', '.tmp'}


def gh_point(g, n, relpath):
    a = GIT.actual(relpath)
    g.add((n, MKG.codePath, Literal(a or relpath)))
    if a is not None:
        g.add((n, MKG.code, GIT.url(a)))
    else:
        g.add((n, MKG.notOnGitHub, Literal(True)))


HDR = re.compile(r'([A-Z0-9]+)-([A-Z0-9]+)-V(\d+\.\d+)')


def load(path):
    s = open(path, encoding='utf-8-sig').read()
    s = re.sub(r'"(?:\\.|[^"\\])*"|/\*.*?\*/|//[^\n]*',
               lambda m: m.group(0) if m.group(0).startswith('"') else '', s, flags=re.S)
    return json.loads(s)


def refs(node, out):
    if isinstance(node, dict):
        for k, v in node.items():
            if k == '$ref' and isinstance(v, str):
                out.add(v)
            else:
                refs(v, out)
    elif isinstance(node, list):
        for x in node:
            refs(x, out)
    return out


def real(name):
    """An AIMInstID (L3) or AIMCode (L2), or None where an L2 gives a placeholder comment (/* ... */): the AIM Type is then AIMType."""
    return None if not isinstance(name, str) or name.lstrip().startswith('/*') else name


def header_of(d):
    h = (d.get('properties', {}).get('Header', {}) or {}).get('const')
    if h:
        return h
    if isinstance(d.get('AIMType'), str):                 # an L2 or L3: the AIM Type it describes
        return d['AIMType']
    if isinstance(d.get('Header'), str):
        return d['Header']
    r = real(((d.get('Identifier', {}) or {}).get('AIMInstID') or (d.get('Identifier', {}) or {}).get('AIMCode')))
    if r:
        return r
    # a plain list of values has no Header: its code is given in the comment ("Data Type XRV-GSA-V1.0: ...")
    m = re.match(r'Data Type ([A-Z]{3}-[A-Z0-9]{3}-V\d+\.\d+)', str(d.get('$comment', '')))
    return m.group(1) if m else None


def as_list(x):
    return x if isinstance(x, list) else [x]


def vkey(v):
    return tuple(int(x) for x in re.findall(r'\d+', v))


def rel(path):
    return os.path.relpath(path, DI).replace(os.sep, '/')


def schema_url(d, f):
    return d.get('$id') or SCHEMA_BASE + os.path.relpath(f, SCH if f.startswith(SCH) else AI_SCH).replace(os.sep, '/')


def build(folders):
    g = Graph()
    g.bind('mkg', MKG)
    for k, n in KIND_NS.items():
        g.bind(k.lower(), n)
    stats = {}

    def count(k, n=1):
        stats[k] = stats.get(k, 0) + n

    def node(kind, ident, label=None):
        n = KIND_NS[kind][ident.replace(' ', '_')]
        if (n, RDF.type, MKG[kind]) not in g:
            g.add((n, RDF.type, MKG[kind]))
            g.add((n, MKG.acronym, Literal(label or ident)))
        return n

    # ---- the standards of M3260, with their parts (numbered in the order of the table)
    table = json.load(open(os.path.join(ROOT, 'standards.json'), encoding='utf-8'))
    for s in table:
        sn = node('Standard', s['acronym'])
        g.add((sn, MKG.webSpec, URIRef(s['url'])))
        g.add((sn, MKG.inM3260, Literal(True)))
        for i, pa in enumerate(s.get('parts', []), 1):
            pn = node('Part', s['acronym'] + str(i))
            g.add((pn, MKG.partOf, sn))
            g.add((pn, MKG.partAcronym, Literal(pa)))
            g.add((pn, MKG.title, Literal('%s part %d - %s' % (s['acronym'], i, pa))))

    # ---- pass 1: the identifiers of the data types and actions, by schema URL (latest version only)
    latest = {}
    roots = {}
    for root in (SCH, AI_SCH):
        if os.path.isdir(root):
            for folder in os.listdir(root):
                if folder in folders and os.path.isdir(os.path.join(root, folder)) and folder not in roots:
                    roots[folder] = root
    def merged(folder, version, sub):
        """The schemas of folder/version/sub: those of D:\\DI, and those of D:\\AI that D:\\DI does not have."""
        seen, out = set(), []
        for root in (SCH, AI_SCH):
            for f in sorted(glob.glob(os.path.join(root, folder, version, sub, '*.json'))):
                if os.path.basename(f) not in seen:
                    seen.add(os.path.basename(f)); out.append(f)
        return out
    versions = {}
    for folder in folders:
        if folder not in roots:
            continue
        vs = sorted((v for v in os.listdir(os.path.join(roots[folder], folder)) if v.startswith('V')), key=vkey)
        if vs:
            latest[folder] = vs[-1]
            versions[folder] = vs[::-1]
    # THE LATEST VERSION, AND WHAT IT DOES NOT HAVE FROM THE EARLIER ONES (the author, 2026-11-18): "for each standard you take the last version. if there is a previous
    # version in D:\AI\schemas, you take from the previous ones the AIMs, the Units, and the Data Types that are not in the last Versions". A new version holds only what
    # changed (MMM-TEC V3.0: 5 data types; the others are those of V2.2). An AIM or a Data Type is the same one in two versions when its code is the same without the
    # version (MMM-ACT-V2.2 and MMM-ACT-V3.0); one without a code, when its file has the same name. The Actions are those of the latest version only.
    def unversioned(ident):
        return re.sub(r'-V\d+(\.\d+)*$', '', ident)
    def aim_name(d):
        return real(((d.get('Identifier', {}) or {}).get('AIMInstID') or (d.get('Identifier', {}) or {}).get('AIMCode'))) or (d.get('AIMType') if isinstance(d.get('AIMType'), str) else None)
    def picked(folder, sub):
        seen, out = set(), []
        for v in versions[folder]:
            here = set()
            for f in merged(folder, v, sub):
                d = load(f)
                k = unversioned((aim_name(d) if sub == 'AIMs' else header_of(d)) or os.path.splitext(os.path.basename(f))[0])
                if k not in seen:
                    here.add(k); out.append(f)
            seen |= here
        return out
    id_by_url = {}
    for folder, v in latest.items():
        for sub in ('data', 'actions'):
            for f in (picked(folder, sub) if sub == 'data' else merged(folder, v, sub)):
                d = load(f)
                id_by_url[schema_url(d, f)] = header_of(d) or folder + '/' + os.path.splitext(os.path.basename(f))[0]

    def datatype(ident):
        n = node('DataType', ident)
        m = HDR.match(ident)
        if m and (n, MKG.ofStandard, None) not in g:
            g.add((n, MKG.ofStandard, node('Standard', m.group(1))))
        return n

    def aim(ident):
        n = node('AIM', ident)
        m = HDR.match(ident)
        if m and (n, MKG.ofStandard, None) not in g:
            g.add((n, MKG.ofStandard, node('Standard', m.group(1))))
        return n

    for folder, version in latest.items():
        sm = re.fullmatch(r'([A-Z]+?)(\d+)?', folder)
        acr, part = sm.group(1), sm.group(2)
        std = node('Standard', acr)
        if (std, MKG.inM3260, None) not in g:
            g.add((std, MKG.notInM3260, Literal(True)))
        owner = node('Part', acr + part) if part else std
        if part:
            g.add((owner, MKG.partOf, std))
        g.add((std, MKG.version, Literal(version)))
        if roots[folder] == AI_SCH:
            g.add((owner, MKG.sourceAI, Literal(True)))
        vdir = os.path.join(roots[folder], folder, version)

        # ---- data types and actions
        for sub, kind in (('data', 'DataType'), ('actions', 'Action')):
            for f in (picked(folder, sub) if sub == 'data' else merged(folder, version, sub)):
                from_ai = f.startswith(AI_SCH)
                d = load(f)
                ident = id_by_url[schema_url(d, f)]
                n = datatype(ident) if kind == 'DataType' else node('Action', ident)
                if kind == 'Action':
                    g.add((n, MKG.ofStandard, std))
                g.add((owner, MKG.specifies, n))
                g.add((n, MKG.title, Literal(d.get('title', ''))))
                g.add((n, MKG.description, Literal(d.get('description', ''))))
                g.add((n, MKG.schema, URIRef(schema_url(d, f))))
                if '/' in ident:
                    g.add((n, MKG.noAcronym, Literal(True)))
                if from_ai:
                    g.add((n, MKG.sourceAI, Literal(True)))
                count(kind.lower() + 's')
                for r in refs(d, set()):
                    t = id_by_url.get(r.split('#')[0])
                    if t and t != ident:
                        g.add((n, MKG.uses, datatype(t)))

        # ---- AIMs (L2)
        for f in picked(folder, 'AIMs'):
            from_ai = f.startswith(AI_SCH)
            d = load(f)
            name = aim_name(d)
            nameless = not name
            if nameless:
                name = acr + '/' + os.path.splitext(os.path.basename(f))[0]
            a = aim(name)
            if nameless:
                g.add((a, MKG.noAcronym, Literal(True)))
                g.add((a, MKG.ofStandard, std))
            if from_ai:
                g.add((a, MKG.sourceAI, Literal(True)))
            count('aims')
            g.add((owner, MKG.specifies, a))
            g.add((a, MKG.title, Literal(d.get('title', ''))))
            g.add((a, MKG.description, Literal(d.get('Description', ''))))
            g.add((a, MKG.schema, URIRef(schema_url(d, f))))
            g.add((a, MKG.apiProfile, Literal(d.get('APIProfile', ''))))
            for doc in d.get('Documentation', []) or []:
                if doc.get('URI'):
                    g.add((a, MKG.webSpec, URIRef(doc['URI'])))
            for p in d.get('ExternalPorts', []) or []:
                for t in as_list(p.get('DataType', [])):
                    if not t:
                        continue
                    port = URIRef('%s/port/%s/%s/%s' % (AIM[name], p['Direction'], t, p.get('PortNumber', 0)))
                    g.add((a, MKG.port, port))
                    g.add((port, RDF.type, MKG.Port))
                    g.add((port, MKG.dataType, datatype(t)))
                    g.add((port, MKG.direction, Literal(p['Direction'])))
                    g.add((port, MKG.label, Literal(p.get('Name', ''))))
                    if p.get('PortNumber'):
                        g.add((port, MKG.portNumber, Literal(p['PortNumber'], datatype=XSD.integer)))
                    g.add((port, MKG.optional, Literal(bool(p.get('IsOptional')))))
                    count('ports')
            for s in d.get('SubAIMs', []) or []:
                sn = real(((s.get('Identifier', {}) or {}).get('AIMInstID') or (s.get('Identifier', {}) or {}).get('AIMCode'))) or (s.get('AIMType') if isinstance(s.get('AIMType'), str) else None)
                if sn:
                    g.add((a, MKG.subAIM, aim(sn)))
                    count('subaims')
            for i, t in enumerate(d.get('Topology', []) or []):
                o, ie = t.get('Output', {}), t.get('Input', {})
                if 'DataType' not in o or 'DataType' not in ie:
                    count('flows_by_name')
                    g.add((a, MKG.notAligned, Literal(True)))
                    continue
                fl = URIRef('%s/flow/%d' % (AIM[name], i + 1))
                g.add((a, MKG.flow, fl))
                g.add((fl, RDF.type, MKG.Flow))
                g.add((fl, MKG.dataType, datatype(o['DataType'])))
                for tag, end in (('from', o), ('to', ie)):
                    g.add((fl, MKG[tag + 'AIM'], aim(end.get('AIMInstID') or end.get('AIMCode')) if (end.get('AIMInstID') or end.get('AIMCode')) else a))
                    g.add((fl, MKG[tag + 'Type'], datatype(end['DataType'])))
                    if end.get('PortNumber'):
                        g.add((fl, MKG[tag + 'PortNumber'], Literal(end['PortNumber'], datatype=XSD.integer)))
                count('flows')

    # ---- the participants of M3260
    store = node('MPAIStore', 'MPAI Store')
    g.add((store, MKG.description, Literal('Receives the AIM Instances (L3s), tests them for conformance, publishes the AIM Descriptions of the accepted AIMs and verifies that an AIM Instance belongs to the MPAI Ecosystem.')))
    assessor = node('PerformanceAssessor', 'Performance Assessors')
    g.add((assessor, MKG.description, Literal('Appointed by MPAI; they publish their assessments.')))
    aif = node('Framework', 'AI Framework')
    g.add((aif, MKG.description, Literal('The AI Framework executes AIMs. An AIM is executed in the AIF and may communicate with a User Agent.')))
    g.add((aif, MKG.specifiedBy, STD['AIF']))
    ua = node('UserAgent', 'User Agent')
    g.add((ua, MKG.description, Literal('A User Agent uses Units to interact with the real world. Acquisition from and delivery to the physical layer are done by Units of the User Agent, not by AIMs.')))
    g.add((aif, MKG.communicatesWith, ua))

    # ---- L3s: an instance of the L2 whose Header they carry, with their Implementer
    for f in sorted(glob.glob(os.path.join(DI, 'AIMs', 'AMDs', '1*.json'))):
        try:
            d = load(f)
        except Exception:
            continue
        l2, name = d.get('AIMType'), real(((d.get('Identifier', {}) or {}).get('AIMInstID') or (d.get('Identifier', {}) or {}).get('AIMCode')))
        if l2 and name and (AIM[l2], RDF.type, MKG.AIM) in g:
            n = node('L3', name)
            g.add((n, MKG.instanceOf, AIM[l2]))
            iid = str((d.get('Identifier', {}) or {}).get('ImplementerID', '')) or '?'
            imp = node('Implementer', iid, 'Implementer ' + iid)
            g.add((imp, MKG.produces, n))
            g.add((store, MKG.receives, n))
            g.add((imp, MKG.holdsIDFrom, store))
            count('l3')

    code_pointers(g, stats, node)
    units_and_software(g, stats, node)
    import mkg_software
    mkg_software.link(g, MKG, KIND_NS, node, stats, DI, GIT, SOFTWARE_SKIP)   # L3s, AIMs and Modules -> software (2026-11-12)
    web_pages(g, stats)
    return g, stats


def slug(title):
    t = re.sub(r'\s+V\d+(\.\d+)*\s*$', '', title or '').strip().lower()
    t = t.replace('&', 'and').replace('/', ' ')
    return re.sub(r'[^a-z0-9]+', '-', t).strip('-')


def web_pages(g, stats):
    """The web page of an AIM or of a Data Type: the link that the /ai-modules and /data-types pages of its standard give
    (weblinks.json, harvested by scrape_web.py), matched on the name.  What the pages list and no schema describes
    becomes a node flagged webOnly (an AIM or a Data Type that only the web page knows)."""
    path = os.path.join(ROOT, 'weblinks.json')
    if not os.path.exists(path):
        return
    links = json.load(open(path, encoding='utf-8'))
    missing = {'AIM': [], 'DataType': []}
    matched = {}
    # the page of a Part: the home page of its version, where the AI Modules and Data Types pages are
    for pn in list(g.subjects(RDF.type, MKG.Part)):
        std = g.value(pn, MKG.partOf)
        pa = g.value(pn, MKG.partAcronym)
        if std is None or pa is None:
            continue
        entry = links.get(str(g.value(std, MKG.acronym)), {}).get(str(pa).lower())
        if entry and entry.get('base'):
            g.add((pn, MKG.webSpec, URIRef(entry['base'])))
    for kind, page in (('AIM', 'ai-modules'), ('DataType', 'data-types')):
        for n in g.subjects(RDF.type, MKG[kind]):
            std = g.value(n, MKG.ofStandard)
            if std is None or g.value(n, MKG.schema) is None:
                continue
            sa = str(g.value(std, MKG.acronym))
            tables = [e[page] for e in links.get(sa, {}).values()]
            title = str(g.value(n, MKG.title) or '')
            stem = os.path.splitext(str(g.value(n, MKG.schema)).rsplit('/', 1)[-1])[0]
            cand = [slug(title), slug(re.sub(r'(?<=[a-z0-9])(?=[A-Z])', ' ', stem))]
            if kind == 'DataType':
                cand.append(slug(re.sub(r'\bObject$', '', title.strip())))
            hit = next(((c, t[c]) for t in tables for c in cand if c in t), None)
            if hit:
                g.add((n, MKG.webPage, URIRef(hit[1]['url'])))
                matched[(sa, page, hit[0])] = True
                stats['web_' + kind] = stats.get('web_' + kind, 0) + 1
            elif any(tables):
                missing[kind].append('%s %s (%s)' % (sa, g.value(n, MKG.acronym), cand[0]))
    only = 0
    for sa, parts in links.items():
        for part, e in parts.items():
            for kind, page in (('AIM', 'ai-modules'), ('DataType', 'data-types')):
                for sl, item in e[page].items():
                    if (sa, page, sl) in matched:
                        continue
                    ident = '%s/%s' % (sa + ('-' + part.upper() if part else ''), sl)
                    n = KIND_NS[kind][ident.replace(' ', '_')]
                    if (n, RDF.type, MKG[kind]) in g:
                        continue
                    g.add((n, RDF.type, MKG[kind]))
                    g.add((n, MKG.acronym, Literal(ident)))
                    g.add((n, MKG.title, Literal(item['name'])))
                    g.add((n, MKG.webPage, URIRef(item['url'])))
                    g.add((n, MKG.webOnly, Literal(True)))
                    g.add((n, MKG.noAcronym, Literal(True)))
                    g.add((n, MKG.ofStandard, KIND_NS['Standard'][sa]))
                    only += 1
    # the Process Actions of MMM-TEC that the web page lists and the repository does not have (a schema is missing)
    mmm = links.get('MMM', {}).get('tec')
    if mmm:
        for code, name in mmm.get('actions', {}).items():
            ident = code + '-' + mmm['version']
            n = KIND_NS['Action'][ident]
            # the same Process Action under another code: match on the name (UM-Actuate = UMActuatePA)
            same = next((a for a in g.subjects(RDF.type, MKG.Action) if g.value(a, MKG.schema) is not None
                         and os.path.splitext(str(g.value(a, MKG.schema)).rsplit('/', 1)[-1])[0].lower() == name.replace('-', '').lower() + 'pa'), None)
            if same is not None and same != n:
                g.add((same, MKG.webPage, URIRef(mmm['base'] + 'process-actions/')))
                g.add((same, MKG.webCode, Literal(code)))
                g.add((same, MKG.codeDiffersFromWeb, Literal(True)))
                stats['action_codes_differ'] = stats.get('action_codes_differ', 0) + 1
                continue
            if (n, RDF.type, MKG.Action) not in g:
                g.add((n, RDF.type, MKG.Action))
                g.add((n, MKG.acronym, Literal(ident)))
                g.add((n, MKG.title, Literal(name + ' (Process Action)')))
                g.add((n, MKG.webPage, URIRef(mmm['base'] + 'process-actions/')))
                g.add((n, MKG.webOnly, Literal(True)))
                g.add((n, MKG.ofStandard, KIND_NS['Standard']['MMM']))
                stats['actions_web_only'] = stats.get('actions_web_only', 0) + 1
                stats['actions_web_only_names'] = stats.get('actions_web_only_names', []) + [ident]
    stats['web_only'] = only
    stats['web_missing'] = {k: len(v) for k, v in missing.items()}
    json.dump(missing, open(os.path.join(ROOT, 'weblinks-unmatched.json'), 'w', encoding='utf-8'), indent=1)


def code_pointers(g, stats, node):
    """Pointers to the code that implements an AIM, a Data Type, a Unit.

    AIMs: the folder AIMs/<Standard>/V<x.y>/<ACR>/ (the *AimProcessor.cs is the entry point), or the Provider
    that registers an L3 by its name and builds its processor ("1MMC-ASR-V2.5-I01" => new AsrAimProcessor(...)).
    Data Types: the C# class whose Header is the Data Type's, or that says it mirrors the schema file.
    Units: the classes of AIF/UserAgent/PhysicalLayer/Units.cs.
    """
    aims_dir = os.path.join(DI, 'AIMs')

    def point(n, path):
        g.remove((n, MKG.codePath, None)); g.remove((n, MKG.code, None)); g.remove((n, MKG.notOnGitHub, None))
        gh_point(g, n, rel(path))

    skip = [os.sep + x + os.sep for x in ('obj', 'bin', 'deploy', 'legacy')]
    cs = [f for f in glob.glob(os.path.join(DI, '**', '*.cs'), recursive=True) if not any(x in f for x in skip)]
    text = {f: open(f, encoding='utf-8-sig', errors='ignore').read() for f in cs}

    # AIMs: folder convention
    for a in list(g.subjects(RDF.type, MKG.AIM)):
        m = HDR.fullmatch(str(g.value(a, MKG.acronym)))
        if not m:
            continue
        std, acr, ver = m.groups()
        for d in sorted(glob.glob(os.path.join(aims_dir, std + '*', 'V' + ver, acr))):
            files = sorted(glob.glob(os.path.join(d, '*.cs')))
            entry = [f for f in files if f.endswith('AimProcessor.cs')] or files
            if entry:
                point(a, entry[0]); stats['code_aims'] = stats.get('code_aims', 0) + 1
                break
    # AIMs: what a Provider registers wins over the folder name
    classes = {}
    for f, t in text.items():
        for c in re.findall(r'\b(?:class|record)\s+([A-Za-z0-9_]+)', t):
            classes.setdefault(c, f)
    registered = {}
    for f, t in text.items():
        if 'Provider' not in os.path.basename(f):
            continue
        for l3name, cls in re.findall(r'"(1[A-Z0-9]+-[A-Z0-9]+-V\d+\.\d+-I\d+)"\s*=>\s*new\s+([A-Za-z0-9_]+)', t):
            n = KIND_NS['L3'][l3name]
            if (n, RDF.type, MKG.L3) in g and cls in classes:
                point(n, classes[cls])
                l2 = g.value(n, MKG.instanceOf)
                registered[rel(classes[cls])] = l2
                if l2 is not None and g.value(l2, MKG.codePath) is None:
                    point(l2, classes[cls]); stats['code_aims'] = stats.get('code_aims', 0) + 1
    for a in list(g.subjects(RDF.type, MKG.AIM)):
        c = g.value(a, MKG.codePath)
        if c is not None and str(c) in registered and registered[str(c)] != a:
            g.remove((a, MKG.codePath, None)); g.remove((a, MKG.code, None)); stats['code_aims'] -= 1

    # Data Types: the class that carries the Header, else the file that says it mirrors the schema
    by_header = {}
    for f, t in text.items():
        for h in re.findall(r'Header\s*\{\s*get;\s*(?:init|set);\s*\}\s*=\s*"([A-Z0-9]+-[A-Z0-9]+-V\d+\.\d+)"', t):
            by_header.setdefault(h, f)
    norm = {f: t.replace('\\', '/') for f, t in text.items()}
    for d in g.subjects(RDF.type, MKG.DataType):
        acr = str(g.value(d, MKG.acronym))
        f = by_header.get(acr)
        if not f:
            sch = g.value(d, MKG.schema)
            if sch is not None:
                tail = str(sch).replace(SCHEMA_BASE, '')
                f = next((ff for ff, t in norm.items() if 'schemas/' + tail in t), None)
        if f:
            point(d, f); stats['code_datatypes'] = stats.get('code_datatypes', 0) + 1

    # User Agent
    ua = KIND_NS['UserAgent']['User_Agent']
    if os.path.isdir(os.path.join(DI, 'AIF', 'UserAgent')):
        gh_point(g, ua, 'AIF/UserAgent')


def units_and_software(g, stats, node):
    """The Units of the User Agent (units.json: UAU) and the software of D:\\DI and D:\\AI (every .csproj)."""
    ua = KIND_NS['UserAgent']['User_Agent']
    uau = node('Standard', 'UAU')
    g.add((uau, MKG.notInM3260, Literal(True)))
    g.add((uau, MKG.title, Literal('Units of the User Agent')))
    spec = json.load(open(os.path.join(ROOT, 'units.json'), encoding='utf-8'))
    for u in spec['units']:
        n = node('Unit', u['acronym'])
        g.add((n, MKG.title, Literal(u['name'])))
        g.add((n, MKG.ofStandard, uau))
        g.add((uau, MKG.specifies, n))
        g.add((ua, MKG.usesUnit, n))
        g.add((n, MKG.unitStatus, Literal(u['status'])))
        for p in u['ports']:
            port = URIRef('%s/port/%s/%s/0' % (KIND_NS['Unit'][u['acronym']], p['direction'], p['dataType']))
            g.add((n, MKG.port, port))
            g.add((port, RDF.type, MKG.Port))
            g.add((port, MKG.dataType, node('DataType', p['dataType'])))
            g.add((port, MKG.direction, Literal(p['direction'])))
            g.add((port, MKG.label, Literal('')))
            g.add((port, MKG.optional, Literal(False)))
        code = [c for c in u.get('code', []) if os.path.exists(os.path.join(DI, c))]
        if code:
            gh_point(g, n, code[0])
            for c in code[1:]:
                g.add((n, MKG.alsoCode, Literal(c)))
        for lib in u.get('libraries', []):
            g.add((n, MKG.usesLibrary, node('Library', lib)))
        stats['units'] = stats.get('units', 0) + 1
    for lib in spec.get('libraries', []):
        ln = node('Library', lib['name'])
        g.add((ln, MKG.description, Literal(lib['description'])))
        g.add((ln, MKG.webSpec, URIRef(lib['url'])))
        for path in lib['paths']:
            g.add((ln, MKG.alsoCode, Literal(path)))
        gh_point(g, ln, lib['paths'][0])
        for sw in lib.get('software', []):
            relp = sw[:-len('.csproj')].replace('/', '~')
            g.add((KIND_NS['Software'][relp], MKG.usesLibrary, ln))
        stats['libraries'] = stats.get('libraries', 0) + 1

    # the software: a project is a .csproj; D:\DI first, D:\AI for what D:\DI does not have
    skip = {'bin', 'obj', 'deploy', 'legacy', '.git', 'node_modules', '.backups', 'Models', 'Output', 'SharedStorage', 'TestData', 'Datasets', 'Lib'}
    projects = {}
    for tag, root in (('DI', DI),):          # the software of this tree only; the second tree is read for its schemas (2026-11-12)
        for dp, dn, fn in os.walk(root):
            dn[:] = [x for x in dn if x not in skip]
            for f in fn:
                if f.endswith('.csproj'):
                    full = os.path.join(dp, f)
                    relp = os.path.relpath(full, root).replace(os.sep, '/')
                    projects.setdefault(relp, (tag, full))
    ident = lambda relp: relp[:-len('.csproj')].replace('/', '~')
    proj_dirs = []
    for relp, (tag, full) in sorted(projects.items()):
        n = node('Software', ident(relp), os.path.basename(full)[:-len('.csproj')])
        text = open(full, encoding='utf-8-sig', errors='ignore').read()
        g.add((n, MKG.title, Literal(relp.rsplit('/', 1)[0] if '/' in relp else relp)))
        g.add((n, MKG.repository, Literal(DI)))
        g.add((n, MKG.path, Literal(relp)))
        m = re.search(r'<OutputType>(\w+)</OutputType>', text)
        g.add((n, MKG.softwareKind, Literal('program' if m and m.group(1).lower() in ('exe', 'winexe') else ('web service/program' if 'Sdk="Microsoft.NET.Sdk.Web"' in text else 'library'))))
        gh_point(g, n, relp)
        for ref in re.findall(r'<ProjectReference\s+Include="([^"]+)"', text):
            target = os.path.normpath(os.path.join(os.path.dirname(full), ref.replace('\\', os.sep)))
            for t2, root in (('DI', DI), ('AI', AI_ROOT)):
                if target.startswith(root):
                    tr = os.path.relpath(target, root).replace(os.sep, '/')
                    if tr in projects:
                        g.add((n, MKG.dependsOn, KIND_NS['Software'][ident(tr)]))
                    break
        proj_dirs.append((os.path.dirname(relp), tag, n))
        stats['software'] = stats.get('software', 0) + 1
    # a project implements the AIMs, L3s, Data Types and Units whose code is in its folder
    for kind in ('AIM', 'L3', 'DataType', 'Unit'):
        for x in g.subjects(RDF.type, MKG[kind]):
            paths = [g.value(x, MKG.codePath)] + (list(g.objects(x, MKG.alsoCode)) if kind == 'Unit' else [])
            for cp in [c for c in paths if c is not None]:
                best = None
                for d, tag, n in proj_dirs:
                    if tag == 'DI' and (str(cp).lower() == d.lower() or str(cp).lower().startswith(d.lower() + '/')) and (best is None or len(d) > best[0]):
                        best = (len(d), n)
                if best:
                    g.add((best[1], MKG.implements, x))


if __name__ == '__main__':
    folders = sys.argv[1:] or sorted({d for r in (SCH, AI_SCH) if os.path.isdir(r) for d in os.listdir(r) if os.path.isdir(os.path.join(r, d)) and re.fullmatch(r'[A-Z]+\d?', d) and d != 'UAG'})
    g, stats = build(folders)
    import mkg_corrections
    stats['corrections'] = mkg_corrections.apply(g)    # what the author decided about single nodes
    out = os.path.join(ROOT, 'mkg' if len(sys.argv) == 1 else 'mkg-' + '-'.join(f.lower() for f in folders))
    g.serialize(out + '.ttl', format='turtle')
    g.serialize(out + '.jsonld', format='json-ld', indent=1)
    print('triples:', len(g))
    print(stats)
    print('written:', out + '.ttl', out + '.jsonld')
