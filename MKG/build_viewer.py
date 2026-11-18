"""Builds mkg-viewer.html: one self-contained page (data embedded) to explore the MKG.

Usage: python build_viewer.py [mkg.ttl]            the page for this machine: MKG/mkg-viewer.html
       python build_viewer.py [mkg.ttl] --public   the pages for the web (schemas/mkg): the Finder is index.html, the explorer explorer.html, with the user guide beside them;
                                                   no path of this machine, and no repository that is not public
"""
import json, os, re, shutil, sys
from rdflib import Graph, URIRef
from rdflib.namespace import RDF

here = os.path.dirname(os.path.abspath(__file__))
public = '--public' in sys.argv
args = [a for a in sys.argv[1:] if not a.startswith('--')]
ttl = args[0] if args else 'mkg.ttl'
g = Graph()
g.parse(os.path.join(here, ttl), format='turtle')
V = 'https://mpai.community/kg/vocab#'
def p(n): return URIRef(V + n)

PREFIX = {'standard': 'STD:', 'part': 'PART:', 'aim': 'AIM:', 'datatype': 'DT:', 'action': 'ACT:', 'l3': 'L3:',
          'implementer': 'IMP:', 'mpaistore': 'STORE:', 'performanceassessor': 'ASSESS:', 'framework': 'AIF:',
          'useragent': 'UA:', 'unit': 'UNIT:', 'software': 'SW:', 'library': 'LIB:', 'app': 'APP:'}
KINDS = ['Standard', 'Part', 'AIM', 'DataType', 'Action', 'L3', 'Implementer', 'MPAIStore', 'PerformanceAssessor', 'Framework', 'UserAgent', 'Unit', 'Software', 'Library', 'App']

def nid(u):
    s = str(u)
    k = s.split('/kg/')[1].split('/')[0]
    return PREFIX[k] + s.rsplit('/', 1)[-1]
def lit(s, pred):
    v = g.value(s, p(pred))
    return str(v) if v is not None else ''

nodes = {}
for kind in KINDS:
    for s in g.subjects(RDF.type, p(kind)):
        i = nid(s)
        std = g.value(s, p('ofStandard'))
        nodes[i] = {'id': i, 'label': (re.sub(r'\d+$', '', lit(s, 'acronym')) + '-' + lit(s, 'partAcronym')) if kind == 'Part' else (lit(s, 'acronym') or i.split(':', 1)[1]), 'kind': kind,
                    'std': nid(std).split(':', 1)[1] if std is not None else (lit(s, 'acronym') if kind == 'Standard' else ''),
                    'title': lit(s, 'title'), 'desc': lit(s, 'description'), 'api': lit(s, 'apiProfile'),
                    'schema': lit(s, 'schema'), 'web': lit(s, 'webSpec'), 'page': lit(s, 'webPage'), 'code': lit(s, 'code'), 'codePath': lit(s, 'codePath'),
                    'ports': [], 'subs': [], 'flows': [], 'uses': [], 'of': '',
                    'declaredOnly': kind == 'DataType' and not lit(s, 'schema'),
                    'noAcronym': bool(lit(s, 'noAcronym')), 'notAligned': (s, p('notAligned'), None) in g,
                    'webOnly': bool(lit(s, 'webOnly')), 'sourceAI': bool(lit(s, 'sourceAI')), 'notInM3260': bool(lit(s, 'notInM3260')),
                    'webCode': lit(s, 'webCode'), 'codeDiffers': bool(lit(s, 'codeDiffersFromWeb')),
                    'repository': lit(s, 'repository'), 'path': lit(s, 'path'), 'softwareKind': lit(s, 'softwareKind'), 'unitStatus': lit(s, 'unitStatus'),
                    'version': lit(s, 'version'), 'partAcronym': lit(s, 'partAcronym'), 'inM3260': bool(lit(s, 'inM3260')),
                    # the software of an L3 (2026-11-12): its BinaryName, how its project was found (or why there is none), and whether a path is not on GitHub
                    'binary': lit(s, 'binaryName'), 'swLink': lit(s, 'softwareLink'), 'implURI': lit(s, 'implementationURI'),
                    'notOnGitHub': bool(lit(s, 'notOnGitHub')), 'isModule': bool(lit(s, 'isModule'))}

for s in list(g.subjects(RDF.type, p('AIM'))) + list(g.subjects(RDF.type, p('Unit'))):
    a = nodes[nid(s)]
    for port in g.objects(s, p('port')):
        a['ports'].append({'dt': nid(g.value(port, p('dataType'))), 'dir': lit(port, 'direction'), 'n': int(lit(port, 'portNumber') or 0),
                           'opt': lit(port, 'optional') == 'true', 'label': lit(port, 'label')})
    a['ports'].sort(key=lambda x: (x['dir'] != 'Input', x['dt'], x['n']))
    a['subs'] = sorted(nid(x) for x in g.objects(s, p('subAIM')))
    for fl in g.objects(s, p('flow')):
        a['flows'].append({'dt': nid(g.value(fl, p('dataType'))),
                           'from': nid(g.value(fl, p('fromAIM'))) if g.value(fl, p('fromAIM')) is not None else '',
                           'fn': int(lit(fl, 'fromPortNumber') or 0),
                           'to': nid(g.value(fl, p('toAIM'))) if g.value(fl, p('toAIM')) is not None else '',
                           'tn': int(lit(fl, 'toPortNumber') or 0)})
for kind in ('DataType', 'Action'):
    for s in g.subjects(RDF.type, p(kind)):
        nodes[nid(s)]['uses'] = sorted(nid(x) for x in g.objects(s, p('uses')))
for s in g.subjects(RDF.type, p('L3')):
    nodes[nid(s)]['of'] = nid(g.value(s, p('instanceOf')))

# the simple relations between nodes (the Standard, Part, L3, participants), with a label
edges = []
for rel in ('partOf', 'specifies', 'produces', 'receives', 'holdsIDFrom', 'communicatesWith', 'usesUnit', 'specifiedBy', 'dependsOn', 'implements', 'usesLibrary',
            'runsModule', 'usesSoftware', 'containsAIM'):
    for a, b in g.subject_objects(p(rel)):
        if isinstance(b, URIRef) and '/kg/' in str(b) and '/kg/' in str(a):
            edges.append([nid(a), rel, nid(b)])
# a Standard specifies what its Parts specify: keep the Part as the owner (no duplicate)
# the list of standards (M3076) with their names and links, for the Finder; a Part's name where the web gives one
# the Reference Model figure of the web page of an AIM or a Part (scrape_figures.py): the Finder shows it under the map
_figs = json.load(open(os.path.join(here, 'figures.json'), encoding='utf8')) if os.path.exists(os.path.join(here, 'figures.json')) else {}
_figs = {k.rstrip('/'): v for k, v in _figs.items()}
for _n in nodes.values():
    _f = _figs.get((_n.get('page') or '').rstrip('/')) or _figs.get((_n.get('web') or '').rstrip('/'))
    if _f and _n['kind'] in ('AIM', 'Part'): _n['fig'] = _f.get('comp') or _f.get('ref')   # a composite AIM: the figure of its Sub-AIMs, else the AIM's own
info = json.load(open(os.path.join(here, 'standards-info.json'), encoding='utf8'))
for n in nodes.values():
    if n['kind'] == 'Standard' and n['label'] in info['standards']:
        i = info['standards'][n['label']]
        n.update({'listed': True, 'title': i['name'], 'site': i['site'], 'about': i['about']})
finder_names = {'standards': {k: v['name'] for k, v in info['standards'].items()}, 'parts': info['parts']}
import datetime, subprocess
try:
    commit = subprocess.run(['git', '-C', os.path.dirname(here), 'rev-parse', '--short', 'HEAD'], capture_output=True, text=True).stdout.strip()
except Exception:
    commit = ''
meta = {'generated': datetime.date.today().isoformat(), 'commit': commit}
out = os.path.join(here, 'mkg-viewer.html')
if public:
    # WHAT THE WEB MAY SEE. A software project is published when it is in git (the public repository mpai-sw): it then has a link to
    # GitHub; a project that is not in git is not published. The path inside the repository stays, the machine's folder goes (2026-11-12).
    for n in nodes.values():
        if n['kind'] == 'Software':
            n['repository'] = 'mpai-sw' if n['code'] else 'not published'
        if n['kind'] == 'App' and not n['code']:
            n['codePath'] = ''
    # the page is about what MPAI has published: software that is not in a public repository is left out, with what refers to it
    # (to show it, marked "not published", comment out the next three lines)
    for i in [i for i, n in nodes.items() if n['kind'] == 'Software' and n['repository'] == 'not published']:
        del nodes[i]
    edges = [e for e in edges if e[0] in nodes and e[2] in nodes]
    guide = os.path.join(here, 'MKG-User-Guide.docx')
    outdir = os.path.join(os.path.dirname(here), 'schemas', 'mkg')   # the same structure as the web: schemas.mpai.community/mkg
    os.makedirs(outdir, exist_ok=True)
    if os.path.exists(guide):
        shutil.copy2(guide, outdir)
        meta['guide'] = 'MKG-User-Guide.docx'
    out = os.path.join(outdir, 'explorer.html')      # 2026-11-15: the Finder is the home page (index.html); the explorer is beside it
data = {'nodes': list(nodes.values()), 'edges': edges, 'meta': meta}
html = open(os.path.join(here, 'viewer-template.html'), encoding='utf-8').read().replace('/*DATA*/null', json.dumps(data, ensure_ascii=False))
if public:
    leaks = re.findall(r'[A-Za-z]:\\\\[A-Za-z]', html)
    assert not leaks, 'a path of this machine is still in the public page: %s' % leaks[:3]
open(out, 'w', encoding='utf-8').write(html)

# the Finder: three searches (AIM, Data Type, Standard) over the same data, one self-contained page beside the explorer
fdata = dict(data); fdata['meta'] = dict(meta, explorer='explorer.html' if public else 'mkg-viewer.html')
fhtml = open(os.path.join(here, 'finder-template.html'), encoding='utf-8').read().replace('/*DATA*/null', json.dumps(fdata, ensure_ascii=False)).replace('/*NAMES*/null', json.dumps(finder_names, ensure_ascii=False))
if public:
    leaks = re.findall(r'[A-Za-z]:\\\\[A-Za-z]', fhtml)
    assert not leaks, 'a path of this machine is still in the public Finder: %s' % leaks[:3]
fout = os.path.join(os.path.dirname(out), 'index.html' if public else 'mkg-finder.html')
open(fout, 'w', encoding='utf-8').write(fhtml)
print('written', fout)
print('written', out, '- %d nodes, %d relations' % (len(nodes), len(edges)))
