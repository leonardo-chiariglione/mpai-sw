"""N-36 (night 2026-11-18): the files of the CUI-CPP App for the MAS App: Apps/CUI/CPP/Files/companies/<name>.json (copies of Test/Data/CUI/Companies)
and index.json (the plain name of each), and the App's manifest and icon."""
import json
import shutil
from pathlib import Path

ROOT = Path(r"D:\AI")
SRC = ROOT / "Test" / "Data" / "CUI" / "Companies"
APP = ROOT / "Apps" / "CUI" / "CPP"
OUT = APP / "Files" / "companies"
NAMES = [
    ("healthy-small", "A small healthy company"),
    ("blue-chip", "A large blue-chip company"),
    ("average", "An average company"),
    ("family-owned", "A family-owned company"),
    ("leveraged", "A company with heavy debts"),
    ("loss-making", "A loss-making company"),
    ("distressed", "A company in distress"),
    ("startup", "A young start-up"),
    ("climate-exposed", "A company exposed to climate risks"),
    ("cyber-attacked", "A company hit by a cyber attack"),
    ("all-risks", "A company with every risk"),
]
OUT.mkdir(parents=True, exist_ok=True)
index = []
for file, name in NAMES:
    shutil.copyfile(SRC / f"{file}.json", OUT / f"{file}.json")
    index.append({"name": name, "file": f"Apps/CPP/Files/companies/{file}.json"})
(OUT / "index.json").write_text(json.dumps({"companies": index}, indent=1), encoding="utf-8")
(APP / "Files" / "README.md").write_text(
    "# Files of the CUI-CPP App\n\nOffered by the MAS Service with the App (GET /Apps/CPP/Files/...): `companies/index.json` lists the ready-made companies "
    "by a plain name, each a company file (the format of the App's README) copied from `Test/Data/CUI/Companies` (synthetic companies made by "
    "`Test/Mpai.Cui.Tests/SyntheticCompanies.cs`). Made by `Test/Tools/SrsLj/make_cui_app_files.py`.\n", encoding="utf-8")
(APP / "app.json").write_text(json.dumps({
    "Name": "Company Performance Prediction",
    "Description": "How likely a company is to default or to stop operating within 12, 24 or 36 months, and why: which figures weigh most. A demonstration model trained on Polish companies, not for real decisions.",
    "Icon": "icon.svg",
    "Version": "2.1",
    "Standard": "MPAI-CUI V2.1",
    "UseCase": "CPP",
    "Module": "1CUI-CPP-V2.1-I01",
    "Keywords": ["company", "default", "bankruptcy", "risk", "finance", "prediction"],
    "Categories": ["Finance"],
    "Languages": ["en"],
    "Asks": ["a company (ready-made, or a JSON file of your own)", "the prediction horizon", "what-if changes of five figures"],
    "Keeps": "Nothing."
}, indent=4), encoding="utf-8")
(APP / "icon.svg").write_text(
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64"><rect width="64" height="64" rx="12" fill="#1d3557"/>'
    '<rect x="12" y="34" width="8" height="18" fill="#a8dadc"/><rect x="26" y="26" width="8" height="26" fill="#a8dadc"/>'
    '<rect x="40" y="18" width="8" height="34" fill="#e63946"/><path d="M10 20 L26 14 L38 22 L54 10" stroke="#f1faee" stroke-width="3" fill="none"/></svg>\n',
    encoding="utf-8")
print("ok", len(index))
