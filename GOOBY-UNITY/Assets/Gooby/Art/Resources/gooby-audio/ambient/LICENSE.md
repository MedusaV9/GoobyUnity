# Lizenz — Klangbetten (assets/audio/sfx/ambient/)

Alle sechs Orts-Ambience-Loops in diesem Ordner sind **komplette
Eigen-Synthese** (numpy-Signalpfad, keine fremden Samples) und stehen
unter **CC0 1.0** (Public Domain, wie die soft/- und foley/-Familien):

| Datei            | Inhalt                                    | Quelle                              |
|------------------|-------------------------------------------|-------------------------------------|
| bett_kamin.ogg   | Kaminknistern + Glut-Grummeln              | tools/audio/gen_klangbetten.py      |
| bett_uhr.ogg     | Uhr-Ticken (1-Hz-Pendel) + Raumton         | tools/audio/gen_klangbetten.py      |
| bett_voegel.ogg  | Vogelzwitschern + Blätter-Brise            | tools/audio/gen_klangbetten.py      |
| bett_wind.ogg    | weicher Wind mit Böen                      | tools/audio/gen_klangbetten.py      |
| bett_stadt.ogg   | fernes Stadt-Grummeln + Verkehrs-Schwaden  | tools/audio/gen_klangbetten.py      |
| bett_laden.ogg   | Laden-Raumton (Lüftung + Waren-Rascheln)   | tools/audio/gen_klangbetten.py      |

Der Generator ist deterministisch (feste Seeds, byte-stabil) — Dateien nie
von Hand schneiden, sondern das Skript anpassen und neu laufen lassen.
Danach IMMER das Mess-Fixture erneuern: `python3 tools/audio/ef2_manifest.py`.
