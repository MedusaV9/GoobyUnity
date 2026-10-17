# gooby-audio — Herkunft und Lizenz

Laufzeit-Zugriff: `Sfx.Play("<id>")` lädt `Resources/gooby-audio/sfx/<id>.ogg` (Runtime/App/Sfx.cs).

| Dateien | Herkunft | Lizenz |
|---|---|---|
| `sfx/game_*.ogg` (14: hit, pop, miss, combo, count, countdown, go, win, lose, star, record, coin, whoosh, perfect) | 1:1 kopiert aus `GOOBY-GODOT/assets/audio/sfx/game/` — die Minispiel-Feel-Karte der Godot-Version (`scripts/minigames/feel/feel_sfx.gd`, POLISH-A): weiche numpy-Synth-Plucks, im Projekt selbst erzeugt; EF-2-Nachbearbeitung (Peak ≤ −1 dBFS, `game_hit` 30-ms-Fade-out) über `tools/audio/ef2_gen_sfx.py` | CC0 1.0, projekteigen |
| `sfx/soft_*.ogg` (17: tap, chip, back, confirm, error, open, close, toggle, tick, buy, coins, levelup, sticker, toast, win, collect, care_erfolg) | 1:1 kopiert aus `GOOBY-GODOT/assets/audio/sfx/soft/` — die weiche UI-Familie (FIX-4, `scripts/audio/sfx_map.gd`): gestimmte Sinus-/Glocken-Plucks per Projekt-Skript synthetisiert (`soft_collect`/`soft_win`/`soft_care_erfolg` aus `tools/audio/ef2_gen_sfx.py`) | CC0 1.0, projekteigen |

| `sfx/amb_regen.ogg` (Regen-Loop des Wetters, `Runtime/World/WeatherFx.cs`) | 1:1 kopiert aus `GOOBY-GODOT/assets/ranch/audio/sfx/ambience_regen.ogg` (dort dokumentiert in `License-audio.md`): https://freesound.org/people/rasunter255/sounds/416227/ „gentle rainfall“ von rasunter255, Ausschnitt 10–50 s | CC0 1.0 |
| `sfx/amb_donner.ogg` (Donner nach einem Blitz) | im Projekt synthetisiert: `tools/audio/gen_wetter_sfx.py` (Knall aus hochpass-gefiltertem Rauschen, Grollen aus tiefpass-gefiltertem braunem Rauschen in vier Wellen, fester Seed — reproduzierbar) | CC0 1.0, projekteigen |

| `sfx/build_hammer.ogg` (Klopfen an der Werkbank, `CraftScreen`) | 1:1 kopiert aus `GOOBY-GODOT/assets/audio/sfx/impactPlank_medium_001.ogg` — Godots `build_hammer` (`scripts/audio/sfx_map.gd`); Kenney „Impact Sounds“ 1.1 (www.kenney.nl), siehe `GOOBY-GODOT/assets/audio/sfx/LICENSE-kenney-cc0.txt` | CC0 1.0 |

| `sfx/ranch_*.ogg` (6: wiehern_a/b, schnauben_a/b, striegeln, heu — der Ranch-Hof, `RanchHofScreen`) | 1:1 kopiert aus `GOOBY-GODOT/assets/ranch/audio/sfx/` (dort dokumentiert in `License-audio.md`): `pferd_wiehern_a` Lydmakeren (freesound 510903), `pferd_wiehern_b` Salsero_classic (freesound 826753), `pferd_schnauben_a` bruno.auzet (freesound 538697), `pferd_schnauben_b` black_trillium (freesound 397236), `buerste_striegeln` ldezem (freesound 223358, Ausschnitt 6–18 s), `heu_rascheln` soundandmelodies (freesound 776155, Ausschnitt 1–7 s) | CC0 1.0 |

| `sfx/ranch_huf_*.ogg` (gras, sand, holz, stein — Hufschlag beim Reiten, `Hufschlag`), `sfx/ranch_fanfare*.ogg`, `sfx/ranch_menge_jubel.ogg`, `sfx/ranch_sattel.ogg` | 1:1 kopiert aus `GOOBY-GODOT/assets/ranch/audio/sfx/` (`huf_*`, `turnier_fanfare*`, `menge_jubel`, `sattel_aufsteigen`; Quellen in `License-audio.md` dort): `huf_gras`/`huf_sand` CC0; `huf_holz`/`huf_stein` congusbongus mit Samples von moodyfingers und Twigy233 (**CC-BY 4.0**); Fanfaren fvcalderan bzw. OGA „Hyper-Ultra-Fanfare“ (CC0); `menge_jubel` Gregor Quendel (**CC-BY 4.0**); `sattel_aufsteigen` craigsmith (CC0) | CC0 1.0 / CC-BY — Pflicht-Credits stehen in `settings.credits_liste` |
| `ambient/ranch_*.ogg` (wind, regen, gewitter, voegel, grillen, bach, menge — die Klangebenen der Ranch, `RanchKlang`) | 1:1 kopiert aus `GOOBY-GODOT/assets/ranch/audio/sfx/ambience_*.ogg` und `menge_gemurmel.ogg`: Wind Luke.RUSTLTD, Regen rasunter255, Vögel isaiah658, Grillen Wolfgang_ (alle CC0); Gewitter DoKashiteru (**CC-BY 3.0**), Bach kurt (**CC-BY 3.0**), Gemurmel Gregor Quendel (**CC-BY 4.0**) | CC0 1.0 / CC-BY — Pflicht-Credits stehen in `settings.credits_liste` |

Fremd-Samples in diesem Ordner: `amb_regen.ogg`, `build_hammer.ogg`, alle `ranch_*.ogg` und `ambient/ranch_*.ogg` (siehe Zeilen oben). Die übrigen
Kenney-Klänge („Interface Sounds“, „Impact Sounds“) liegen nur im Godot-Wurzelordner `sfx/` und wurden **nicht** übernommen.

Lautstärke-Trims (dB) stammen aus `feel_sfx.gd` bzw. `sfx_map.gd` und stehen in `Sfx.Trims`.

CC0 1.0 Universal: https://creativecommons.org/publicdomain/zero/1.0/ — frei für private, schulische und
kommerzielle Nutzung, keine Namensnennung nötig.
