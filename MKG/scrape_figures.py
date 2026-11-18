"""The Reference Model figure of each AIM web page (and of each Part's page): figures.json {page URL: image URL}.
The figure is the first image after the heading 'Reference Model' (else 'Reference Architecture', else 'Architecture') of the page.
Usage: python scrape_figures.py     (reads weblinks.json, writes figures.json)"""
import json, re, os, sys, urllib.request
from concurrent.futures import ThreadPoolExecutor

ROOT = os.path.dirname(os.path.abspath(__file__))
W = json.load(open(os.path.join(ROOT, 'weblinks.json'), encoding='utf-8'))

urls = set()
def walk(o):
    if isinstance(o, dict):
        for k, v in o.items():
            if k == 'url' and isinstance(v, str) and '/ai-modules/' in v: urls.add(v)
            elif k == 'base' and isinstance(v, str): urls.add(v)
            else: walk(v)
walk(W)

HEAD = re.compile(r'<h([1-6])[^>]*>(.*?)</h\1>', re.S | re.I)
IMG = re.compile(r'<img\b[^>]*?\bsrc="([^"]+)"', re.I)
WANT = ['reference model', 'reference architecture', 'architecture']

def first_img(html, s, e):
    for m in IMG.finditer(html[s:e]):
        src = m.group(1)
        if '/wp-content/uploads/' in src and 'logo' not in src.lower():
            return 'https://mpai.community' + src if src.startswith('/') else src
    return None

def figure(url):
    try:
        html = urllib.request.urlopen(urllib.request.Request(url, headers={'User-Agent': 'mkg-figures'}), timeout=40).read().decode('utf-8', 'replace')
    except Exception as e:
        return url, None
    heads = [(m.start(), m.end(), re.sub(r'<[^>]+>', '', m.group(2)).strip().lower()) for m in HEAD.finditer(html)]
    out = {}
    nxt = lambda i: heads[i + 1][0] if i + 1 < len(heads) else len(html)
    for want in WANT:                                  # the figure of the AIM (a black box with its Data Types)
        for i, (s, e, t) in enumerate(heads):
            if want in t:
                f = first_img(html, e, nxt(i))
                if f: out['ref'] = f; break
        if 'ref' in out: break
    for i, (s, e, t) in enumerate(heads):              # a composite AIM: the Reference Model under 'SubAIMs' (its Sub-AIMs and their connections)
        if 'subaims' in t.replace('-', '').replace(' ', '') and 'reference model' not in t:
            for j in range(i + 1, min(i + 4, len(heads))):
                if 'reference model' in heads[j][2]:
                    f = first_img(html, heads[j][1], nxt(j))
                    if f: out['comp'] = f
                    break
            if 'comp' in out: break
    return url, (out or None)

with ThreadPoolExecutor(10) as ex:
    res = list(ex.map(figure, sorted(urls)))
out = {u: f for u, f in res if f}
json.dump(out, open(os.path.join(ROOT, 'figures.json'), 'w', encoding='utf-8'), indent=1)
print(len(urls), 'pages;', len(out), 'with a figure;', sum(1 for v in out.values() if 'comp' in v), 'with a composite figure')
