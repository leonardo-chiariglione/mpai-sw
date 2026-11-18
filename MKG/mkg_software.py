"""The links of the MKG from the AIMs, the L3s and the Modules to the software that implements or runs them (2026-11-12).

An L3 (AIMs/AMDs/1*.json) names its software: BinaryName (the assembly) and ImplementationURI. Its software component is found, in order:
    1. the project (.csproj) whose assembly is the BinaryName;
    2. else the project of the code that registers the L3 by name (a Provider: "1XXX-YYY-Vx.y-Inn" => new ...);
    3. else, for a composite L3, none of its own: the Controller executes it from its Topology, and each AIM it contains has its own;
    4. else: no software yet.
The project then implements the L3 and the AIM (L2) the L3 is an instance of.
A Module is the L3 a workflow (Apps/**/*.orch) runs; the App is that workflow, with the projects of its folder.
A link to GitHub is made only for a path that is in git (with the case git has); other paths are kept, marked "not on GitHub".
"""
import glob, json, os, re, subprocess
from rdflib import URIRef, Literal
from rdflib.namespace import RDF


class Git:
    def __init__(self, root, github):
        self.root, self.github = root, github
        files = subprocess.run(['git', '-C', root, 'ls-files'], capture_output=True, text=True).stdout.splitlines()
        self.files = {f.lower(): f for f in files}
        self.dirs = {}
        for f in files:
            d = os.path.dirname(f)
            while d and d.lower() not in self.dirs:
                self.dirs[d.lower()] = d
                d = os.path.dirname(d)

    def actual(self, path):
        """The path as git has it, or None where it is not in git."""
        p = path.replace('\\', '/').strip('/')
        return self.files.get(p.lower()) or self.dirs.get(p.lower())

    def url(self, path):
        a = self.actual(path)
        if a is None:
            return None
        return URIRef(self.github + a) if a.lower() in self.files else URIRef(self.github.replace('/blob/', '/tree/') + a)


def projects(root, skip):
    """Every .csproj under root: relative path -> (assembly name, folder)."""
    out = {}
    for dp, dn, fn in os.walk(root):
        dn[:] = [d for d in dn if d not in skip and not d.startswith('.')]
        for f in fn:
            if f.endswith('.csproj'):
                full = os.path.join(dp, f)
                t = open(full, encoding='utf-8-sig', errors='ignore').read()
                m = re.search(r'<AssemblyName>([^<]+)</AssemblyName>', t)
                relp = os.path.relpath(full, root).replace(os.sep, '/')
                out[relp] = (m.group(1).strip() if m else f[:-len('.csproj')], os.path.dirname(relp))
    return out


def link(g, MKG, KIND_NS, node, stats, root, git, skip):
    soft = KIND_NS['Software']
    ident = lambda relp: relp[:-len('.csproj')].replace('/', '~')
    projs = projects(root, skip)
    by_assembly = {}
    for relp, (asm, _) in projs.items():
        by_assembly.setdefault(asm, relp)

    def owner(path):
        best = None
        for relp, (_, d) in projs.items():
            if (path == d or path.startswith(d + '/')) and (best is None or len(d) > len(projs[best][1])):
                best = relp
        return best

    # the code that registers L3s by name: a Provider that lists the names it builds, or a "name" => new ... (not the tests, not a User Agent
    # that only names the Module it starts); and the code that names a workflow file
    reg, mentions = {}, {}
    texts = {}
    for f in glob.glob(os.path.join(root, '**', '*.cs'), recursive=True) + glob.glob(os.path.join(root, '**', '*.csproj'), recursive=True):
        r = os.path.relpath(f, root).replace(os.sep, '/')
        if any(('/' + x + '/') in ('/' + r) for x in ('bin', 'obj', 'Test', 'Night', 'legacy', 'deploy', '.tmp')):
            continue
        texts[r] = open(f, encoding='utf-8-sig', errors='ignore').read()
    for r, t in texts.items():
        if not r.endswith('.cs'):
            continue
        provider = 'Provider' in os.path.basename(r)
        for name in set(re.findall(r'"(1[A-Z0-9]+-[A-Z0-9]+-V\d+\.\d+-I\d+)"', t)):
            if provider or re.search(r'"%s"\s*=>\s*new' % re.escape(name), t):
                reg.setdefault(name, []).append(r)

    def project_node(relp):
        n = soft[ident(relp)]
        return n if (n, RDF.type, MKG.Software) in g else None

    for f in sorted(glob.glob(os.path.join(root, 'AIMs', 'AMDs', '1*.json'))):
        name = os.path.basename(f)[:-len('.json')]
        l3 = KIND_NS['L3'][name]
        if (l3, RDF.type, MKG.L3) not in g:
            continue
        try:
            d = json.load(open(f, encoding='utf-8-sig'))
        except Exception:
            continue
        impls = d.get('Implementations') or []
        impl = impls[0] if isinstance(impls, list) and impls else (impls if isinstance(impls, dict) else {})
        binary, uri = impl.get('BinaryName'), impl.get('ImplementationURI')
        if not binary:
            s = json.dumps(d)
            m1, m2 = re.search(r'"BinaryName": "([^"]*)"', s), re.search(r'"ImplementationURI": "([^"]*)"', s)
            binary, uri = (m1.group(1) if m1 else None), (m2.group(1) if m2 else None)
        if binary:
            g.add((l3, MKG.binaryName, Literal(binary)))
        if uri:
            m = re.search(r'github\.com/[^/]+/[^/]+/(?:tree|blob)/[^/]+/(.+)$', uri)
            if m and git.actual(m.group(1)) is None:
                g.add((l3, MKG.implementationURINotInGit, Literal(uri)))
            else:
                g.add((l3, MKG.implementationURI, URIRef(uri) if uri.startswith('http') else Literal(uri)))
        l2 = g.value(l3, MKG.instanceOf)
        relp, how = None, None
        if binary and binary in by_assembly:
            relp, how = by_assembly[binary], 'its BinaryName is the assembly of the project'
        elif name in reg:
            files = reg[name]
            owners = [o for o in (owner(x) for x in files) if o]
            if owners:
                relp = max(set(owners), key=owners.count)
                how = 'the project registers the L3 by name (%s)' % next(x for x in files if owner(x) == relp)
        subs = d.get('SubAIMs') or []
        if relp and project_node(relp) is not None:
            sw = project_node(relp)
            g.add((sw, MKG.implements, l3))
            g.add((l3, MKG.softwareLink, Literal(how)))
            if l2 is not None:
                g.add((sw, MKG.implements, l2))
            if binary and binary not in by_assembly:
                g.add((l3, MKG.binaryNotAProject, Literal(True)))
            stats['l3_software'] = stats.get('l3_software', 0) + 1
        elif subs:
            g.add((l3, MKG.composite, Literal(True)))
            g.add((l3, MKG.softwareLink, Literal('composite: the Controller executes it from its Topology; each AIM it contains has its own software')))
            stats['l3_composite'] = stats.get('l3_composite', 0) + 1
        else:
            g.add((l3, MKG.noSoftware, Literal(True)))
            g.add((l3, MKG.softwareLink, Literal('no software yet' + (' (BinaryName %s: no such project)' % binary if binary else ''))))
            stats['l3_no_software'] = stats.get('l3_no_software', 0) + 1
        for s in subs:
            sname = ((s.get('Identifier') or {}).get('AIMInstID') or (s.get('Identifier') or {}).get('AIMCode') or s.get('AIMInstID') or s.get('AIMCode')) if isinstance(s, dict) else s
            if isinstance(sname, str) and (KIND_NS['L3'][sname], RDF.type, MKG.L3) in g:
                g.add((l3, MKG.containsAIM, KIND_NS['L3'][sname]))

    # the Modules: what the workflows of the Apps run
    for f in sorted(glob.glob(os.path.join(root, 'Apps', '**', '*.orch'), recursive=True)):
        r = os.path.relpath(f, root).replace(os.sep, '/')
        app = node('App', r.replace('/', '~')[:-len('.orch')], os.path.basename(r)[:-len('.orch')])
        g.add((app, MKG.title, Literal(os.path.dirname(r))))
        g.add((app, MKG.path, Literal(r)))
        u = git.url(r)
        if u is not None:
            g.add((app, MKG.codePath, Literal(git.actual(r))))
            g.add((app, MKG.code, u))
        else:
            g.add((app, MKG.codePath, Literal(r)))
            g.add((app, MKG.notOnGitHub, Literal(True)))
        t = open(f, encoding='utf-8-sig', errors='ignore').read()
        for name in sorted(set(re.findall(r'\b(1[A-Z0-9]+-[A-Z0-9]+-V\d+\.\d+-I\d+)\b', t))):
            l3 = KIND_NS['L3'][name]
            if (l3, RDF.type, MKG.L3) in g:
                g.add((app, MKG.runsModule, l3))
                g.add((l3, MKG.isModule, Literal(True)))
        # the software of the App: the projects whose code names this workflow file (the User Agent that runs it); else the projects of its folder
        base = os.path.basename(r)
        users = {owner(x) for x, t in texts.items() if base in t} - {None}
        if not users:
            folder = os.path.dirname(r)
            users = {relp for relp, (_, d) in projs.items() if d == folder or d.startswith(folder + '/')}
        for relp in sorted(users):
            sw = project_node(relp)
            if sw is not None:
                g.add((app, MKG.usesSoftware, sw))
        stats['apps'] = stats.get('apps', 0) + 1
