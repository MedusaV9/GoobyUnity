# GOOBY Unity

Öffentlicher CI-Spiegel von **GOOBY 6** (Unity `6000.6.0f1`), damit GitHub Actions das Spiel auf gehosteten Runnern ohne Minuten-Kontingent bauen kann.

Quelle: privates Repo `MedusaV9/GoobyUnitySpiel`, Branch `cursor/gooby-full-game-290a`, Commit `faec4a9610546abc39d5e906184775161186f592` (12. Oktober 2026). Die Historie der privaten Repo ist hier nicht enthalten.

## Was hier liegt

| Pfad | Inhalt |
|---|---|
| `GOOBY-UNITY/` | Das Unity-Projekt (Boot-Szene, Spiel, Tests) |
| `.github/workflows/gooby-unity.yml` | Tests, Linux-Player, unsignierte iOS-`.ipa` |
| `tools/ci/verify_unity_ipa.py` | Prüfung der unsignierten `.ipa` |

Nicht enthalten sind die anderen Projekte aus der privaten Repo: Godot-Fassung, alte Web-Version, Mehrspieler-Server, MonkeyBar, Nexus, Reshape, Trailer und Design-Docs.

## Build

Jeder Push startet den Workflow **GOOBY Unity**. Artefakte:

- `GOOBY-unity-unsigned-ipa` — unsigniert, Installation per AltStore oder Sideloadly
- `GOOBY-unity-linux` — Linux-Standalone
- `GOOBY-unity-xcode-project` — Xcode-Export (3 Tage)
- `core-test-results` und `unity-test-results`

Die Editor-Jobs brauchen diese Repository-Secrets (nicht im Git):

- `UNITY_EMAIL`
- `UNITY_PASSWORD`
- `UNITY_LICENSE` (Inhalt der Unity-Personal-`.ulf`; GameCI liest daraus die Seriennummer)

`core-tests` läuft ohne Lizenz.
