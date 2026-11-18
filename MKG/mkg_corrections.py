"""Corrections the author decided, applied to the MKG after it is built (build_mkg.py calls apply(g)).

Usage: python mkg_corrections.py        applies them to the existing mkg.ttl and mkg.jsonld, without a rebuild
"""
import os
from rdflib import Graph, Namespace, URIRef
from rdflib.namespace import RDF

ROOT = os.path.dirname(os.path.abspath(__file__))
MKG = Namespace('https://mpai.community/kg/vocab#')
KG = 'https://mpai.community/kg/'

# schema address of a node: the address the MKG derived is wrong (2026-11-02, decided by the author)
SCHEMA = {
    'datatype/CUI-SRS-V2.0': 'https://schemas.mpai.community/CUI1/V2.1/data/SecondaryRiskStatements.json',
}
# discontinued: the node and everything that is only about it
# (PAF-AVC and PAF-VSC V1.6 no longer exist: their ports and every reference go too)
DISCONTINUED = ['datatype/MAS/AIMDescription', 'aim/PAF-AVC-V1.6', 'aim/PAF-VSC-V1.6',
                'aim/PAF-GPS-V1.6', 'aim/PAF-PGI-V1.6', 'datatype/PAF-AVT-V1.6']   # PAF V1.6: discontinued by the author, 2026-11-02


def apply(g):
    done = []
    for k, url in SCHEMA.items():
        n = URIRef(KG + k)
        if (n, None, None) in g:
            g.set((n, MKG.schema, URIRef(url)))
            done.append('schema of ' + k)
    for k in DISCONTINUED:
        n = URIRef(KG + k)
        if (n, None, None) in g:
            for s, p, o in list(g.triples((None, None, n))):
                g.remove((s, p, o))
            g.remove((n, None, None))
            for sub in {x for x in g.subjects() if isinstance(x, URIRef) and str(x).startswith(str(n) + '/')}:    # its ports and the like
                g.remove((sub, None, None))
            done.append('removed ' + k)
    return done


if __name__ == '__main__':
    g = Graph()
    g.parse(os.path.join(ROOT, 'mkg.ttl'), format='turtle')
    print(apply(g))
    g.serialize(os.path.join(ROOT, 'mkg.ttl'), format='turtle')
    g.serialize(os.path.join(ROOT, 'mkg.jsonld'), format='json-ld', indent=1)
    print('triples:', len(g))
