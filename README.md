# Bullwhip-Effekt: Simulation (POC)

Stand: 2026-11-03. Umsetzung von M0 und M1 aus `PLAN.md` (Bachelorarbeit, Bullwhip-Effekt; Planungsstand in
`/home/jherrmann/Documents/claude_working_dir/bullwhip-effekt/PLAN.md`).

## Was gebaut ist

- **M0 (Grundgerüst):** Solution `Bullwhip.sln`, Projekte `src/Bullwhip.Core` (Modell, keine I/O) und `tests/Bullwhip.Tests` (xUnit). Deterministische Zufallszahlen (SplitMix64 mit Box–Muller).
- **M1 (kleinster Schnitt):** ein Händler, AR(1)-Nachfrage (Chen et al. 2000, Gl. 1), gleitender Durchschnitt MA(p), Order-up-to mit z = 0, ohne Clipping. Prüfung V1 gegen Chen et al. (2000), Gl. (5), auf dem vollen Raster (L ∈ {1,2,4}, p ∈ {2,4,8,16}, ρ ∈ {0, 0.5, −0.5}; 36 Punkte, je 100 Replikationen × 2000 Perioden nach 200 Warm-up).

## Ergebnis

- `dotnet test`: 7 von 7 Tests bestehen, darunter V1 (36/36 Punkte bestehen; Abweichung höchstens 0,3 %) und V11 (gleicher Seed, gleiches Ergebnis).

## Abweichungen vom Plan

- **MathNet.Numerics nicht verwendet.** Das Paket ist im lokalen NuGet-Cache nicht vorhanden. Stattdessen eingebauter Normalverteilungs-Sampler (Box–Muller) und eigene Normalverteilungsfunktion (Abramowitz–Stegun 7.1.26, Fehler ~1,5·10⁻⁷). Für V1 ausreichend; vor einer Verwendung in Gate B nachprüfen.
- **FluentResults nicht verwendet** (in M1 nicht nötig). Kommt mit der Konfiguration und der CLI (M0 vollständig).
- **CLI fehlt** (`Bullwhip.Cli` mit `validate`/`run`). Die V1-Ausgabe läuft über die Testausgabe.
- **Noch nicht implementiert:** Mehrstufenkette (M2), Kosten und K (M3), Konfiguration aus JSON, CSV-Ausgabe, Kaplan-Lieferzeit (M7).

## Nicht geprüft

- V1 gilt nur für Stufe 1 bei z = 0. Die Verteilung der Lieferzeit und die Zeitabläufe der Lagerbewegungen sind hier nicht enthalten; sie kommen mit V8 und V13–V15.
