"""Writes standards-info.json for the MKG Finder: the list of standards, their names and links.

The LIST and the links (subdomain site, About page) come from M3076 - Simple links to MPAI Standards.xlsx (input2000).
The NAMES come from the pages of mpai.community/standards (read 2026-11-02); a Part's name is the title of its page when it has one.
Usage: python make_standards_info.py
"""
import json, os
import openpyxl

here = os.path.dirname(os.path.abspath(__file__))
XLSX = r'C:\Users\Thor\OneDrive - CEDEO\My Standards\mpai\input2000\M3076 - Simple links to MPAI Standards.xlsx'

NAMES = {
    'AIF': 'AI Framework', 'AIH': 'Artificial Intelligence for Health', 'AIP': 'Artificial Intelligence Passport', 'AIT': 'Artificial Intelligence Tools',
    'CAE': 'Context-based Audio Enhancement', 'CAV': 'Connected Autonomous Vehicles', 'CUI': 'Compression and Understanding of Industrial Data',
    'EEV': 'AI-based End-to-End Video Coding', 'EVC': 'AI-Enhanced Video Coding', 'GME': 'Governance of the MPAI Ecosystem', 'GSA': 'Integrative Genomic/Sensor Analysis',
    'HMC': 'Human and Machine Communication', 'MAS': 'MPAI as a Service', 'MMC': 'Multimodal Conversation', 'MMM': 'MPAI Metaverse Model', 'NNW': 'Neural Network Watermarking',
    'OSD': 'Object and Scene Description', 'PAF': 'Portable Avatar Format', 'PGM': 'Pursuing Goals in the Metaverse', 'PRF': 'AI Module Profiles', 'PTF': 'Process Instance Trust Framework',
    'SPG': 'Server-based Predictive Multiplayer Gaming', 'TFA': 'Data Types, Formats, and Attributes', 'WMG': 'AIW and AIM Implementation Guidelines', 'XRV': 'Extended Reality Venues',
}
# Parts, by the label of the MKG (standard-part acronym); only those whose name is on the web
PARTS = {
    'AIH-HSP': 'Health Secure Platform', 'XRV-LTP': 'Live Theatrical Performance', 'CAE-6DF': 'Six Degrees of Freedom', 'NNW-NNT': 'Neural Network Traceability',
    'NNW-TEC': 'Neural Network Watermarking - Technologies', 'CAV-ARC': 'Architecture', 'SPG-MDL': 'Mitigation of Data Loss effects', 'PGM-AUA': 'Autonomous User Architecture',
    'CAV-TEC': 'Connected Autonomous Vehicle - Technologies', 'MMM-TEC': 'MPAI Metaverse Model Technologies', 'CAE-USC': 'Context-based Audio Enhancement - Use Cases',
    'CUI-CPP': 'Company Performance Prediction', 'CAE-ASM': 'Audio Scene Management',
}
ws = openpyxl.load_workbook(XLSX, data_only=True).active
std = {}
for r in ws.iter_rows(min_row=4, values_only=True):
    if r[1] and r[2] and r[3] and r[1] != 'Standard Acronym':
        std[r[1].strip()] = {'name': NAMES.get(r[1].strip(), ''), 'site': r[2].strip(), 'about': r[3].strip(), 'flag': (r[4] or '').strip()}
missing = [a for a in std if not std[a]['name']]
assert not missing, missing
json.dump({'standards': std, 'parts': PARTS}, open(os.path.join(here, 'standards-info.json'), 'w', encoding='utf8'), indent=1, ensure_ascii=False)
print(len(std), 'standards,', len(PARTS), 'part names')
