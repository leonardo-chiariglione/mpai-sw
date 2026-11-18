# Company Performance Prediction - the App (MPAI-CUI CPP V2.1)

A **User Agent** that is a console program (`Ua/CuiUa`) and a **Controller** that is a program of its own (`Controller/CuiController`). The User Agent runs the workflow `CUI-CPP.orch`
with the interpreter of the User Agent; it supplies only devices - the data of a company, read from a file, and a report on the screen - and knows no AIM. The Controller starts the
Module (`1CUI-CPP-V2.1-I01`) and its AIMs as the L3 says.

    CuiController [--listen http://127.0.0.1:5061] [--model <FinancialDefault-Polish5y.json>]
    CuiUa --company <company.json> [--horizon <months>] [--controller <url>] [--wait <seconds>] [--json <out.json>] [--verbose]

`Cui.bat Samples\distressed.json` runs both. The company file is one JSON object: `PredictionHorizon` (months), `FinancialDescriptors` (CUI-FND), `GovernanceDescriptors` (CUI-GVS) and,
optionally, `ClimateRiskDescriptors` (CUI-LRD), `CyberRiskDescriptors` (CUI-CRD), `SecondaryRiskStatements` (CUI-SRS). `Samples/` holds a healthy and a distressed company (the ones the tests use).

What the Module gives is printed with the explanation each descriptor carries: the probability of default and the importance of each financial descriptor to it; what the organisation does to it;
the probability of discontinuity and what each risk contributes; the Secondary Discontinuity Probability.

**The model.** The Prediction AIMs use a built-in reference model, or a trained one that is Access data: `AIMs/CUI/V2.1/Models/FinancialDefault-Polish5y.json`, trained on the Polish Companies
Bankruptcy dataset (CC BY 4.0) by `AIMs/CUI/Tools/Mpai.Cui.Train`. The L3 of the AIMs gives its address (on GitHub, valid once the branch is merged) and its SHA-256; `--model` gives the Controller a
file on this machine to obtain it from instead. A demonstration model, trained on companies of Poland: **not for real companies**.

The data cross the Controller API as the JSON instance of their schema (`Mpai.Aif.PortData`: a codec for each CUI Data Type and for `number`; what crosses is checked against the published schema).

## In the MAS App (2026-11-18)

The same workflow, `CUI-CPP.orch`, runs in the MAS App (browser and desktop clients). It binds two devices of the client: **Company** - the person chooses one of eleven ready-made companies (`Files/companies`, named in plain words in `index.json`) or a company file of their own, and the horizon (12, 24 or 36 months); after a result, five figures of the same company can be moved from -50 % to +100 % (revenues CEA1, operating result CEBDIFFVAL, total debts SPPDTOT, equity SPPATOT, interest and other financial expenses CEC3) and given again - and **Report** - the probability of default, of discontinuity and, with secondary risks, the Secondary Discontinuity Probability, each with what weighs most, beside the company chosen before. The Service builds the Module with `Mpai.Cui.CuiProvider`; the App is listed in the Service's `Apps` (id `CPP`).

**How to demonstrate it.**
1. Choose "A small healthy company", then "A company in distress": the two reports side by side - default probability and the figures that weigh most.
2. On the company in distress, move one figure - twice the total debts (+100 %), Apply: the default probability rises; halve them: it falls.
3. Choose "A company exposed to climate risks" or "A company hit by a cyber attack": the discontinuity probability and its parts (financial, governance, climate, cyber) change while the default may not.
Say each time: a demonstration model trained on Polish companies, not for real companies.
