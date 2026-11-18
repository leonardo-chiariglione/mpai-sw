# Files of the SRS App

Offered by the MAS Service with the App (GET /Apps/SRS/Files/...), fetched by the client when the person picks one.

- `damaged/<clip>.wav`: six clips of LJ Speech 1.1 (public domain, keithito.com) never used to train the networks, at 16 kHz, two sections of each covered by noise; `damaged/<clip>.json`: those sections (from, to in seconds, the words), with which the client fills the table of damaged sections.
- `voice/LJ-reader.wav`: LJ002-0013, a validation clip of the same reader (not trained on, not in the test), given as the Speech Segments for Modelling.

Made by Test/Tools/SrsLj/make_app_files.py from Test/Data/Speech/LJSpeech (see its README).
