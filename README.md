# Bullwhip-Effekt: Simulation

Stand: 2026-11-03. Simulation zur Bachelorarbeit über den Bullwhip-Effekt in einer mehrstufigen Lieferkette.
Planung und Begründungen: `bullwhip-effekt/PLAN.md`, Entscheidungen: `bullwhip-effekt/DECISIONS.md`, Quellen: `bullwhip-effekt/ZITATE.md` (Bachelorarbeit-Ordner). Arbeitsregeln für Entwickler und Agenten: `CLAUDE.md`.

## Aufbau

- `src/Bullwhip.Core`: Modell ohne I/O.
  - `Chain/`: mehrstufige Kette (`SerialChain`), Konfiguration (`ChainConfig`), Deckungsregeln (`CoverRules`, `PhaseTarget`), Schutzfenster, Kaplan-Lieferzeit.
  - `Forecast/`: gleitender Durchschnitt, Stufenprognose, Fehlerfenster, EWMA-Absolutfehler.
  - `Demand/`: AR(1)-Nachfrage.
  - `Analytic/`: geschlossene Formeln mit Quellen (Chen et al. 2000; Chen, Ryan & Simchi-Levi 2000).
  - `Validation/`: Prüfungen gegen die Literatur (`ChenSingleStageCheck` für V1, `GateA` für die übrigen Gate-A-Prüfungen).
  - `Rng/`, `Stats/`: SplitMix64 mit Box–Muller, Normalverteilung (Abramowitz–Stegun 7.1.26, Bisektion für Quantile).
- `tests/Bullwhip.Tests`: xUnit-Tests. `StructureRulesTests` prüft Methodenlänge und Verschachtelung über den ganzen Code.

## Status

- `dotnet build --no-incremental`: 0 Warnungen, 0 Fehler.
- `dotnet test`: 46 von 46 Tests bestehen.
- Gate A: alle Prüfungen sind umgesetzt und bestehen. Die Abnahme durch den Autor steht aus (`DECISIONS.md`, F8).
- Hash-Baselines: Vereinfachungen und Umbauten müssen die Ergebnis-Hashes unverändert lassen (siehe `CLAUDE.md`).
- Nicht umgesetzt: Kostenkalibrierung K (M3), CLI, Konfiguration aus JSON, CSV-Ausgabe.

## Modellannahmen (Auswahl)

- Basis-Vorlaufzeit: Mittel 10 Tage (1,43 Wochen), CV 0,30. Quelle: APQC-Benchmark (USA, Median, n = 3.077). Sensitivitäten 7 Tage, 4 Tage und 3,6 Wochen (`DECISIONS.md`, C24).
- Nicht-ganzzahlige Vorlaufzeit: bei R = 1 im Kaplan-Modus. Bei R > 1 gilt die Deckungsregel B (CycleAverage) (`DECISIONS.md`, C21). Die Regel für vorgelagerte Stufen ist eine Modellannahme ohne Quelle (F6).
- Start: leerer Start mit Start-Überbestellung (600 bei R = 4, Phase 0). Im Warm-up ohne Wirkung auf die Messung; für Experimente mit geklemmten Bestellungen Vorbelegung (`DECISIONS.md`, F5).
- Eingabeprüfung: L < 1 und zu kurze Stufen-Arrays werfen `ArgumentException` (C22).

## Bauen und testen

```
dotnet build --no-incremental
dotnet test
```

## Bekannte Lücken

- Keine CLI und keine Konfiguration aus JSON; die Ausgabe läuft über die Testausgabe.
- V10 (Chen et al., exponentielle Glättung) ist gegen das Original geprüft, aber noch nicht im Testcode (Meilenstein M7).
- Ryans Konstante (Dissertation 1997) ist nicht beschafft; unsere Ableitung ersetzt sie vorläufig (F7).
- Kaplan- und stochastische Lieferzeit sind optional; Standard ist aus.
