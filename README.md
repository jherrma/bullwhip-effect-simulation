# Bullwhip-Effekt: Simulation

Stand: 2026-11-20. Simulation zur Bachelorarbeit über den Bullwhip-Effekt in einer mehrstufigen Lieferkette (vier Stufen, periodische Überprüfung, Order-up-to-Regel).
Planung und Begründungen: `bullwhip-effekt/PLAN.md`, Entscheidungen: `bullwhip-effekt/DECISIONS.md`, Quellen: `bullwhip-effekt/ZITATE.md` (Bachelorarbeit-Ordner). Arbeitsregeln für Entwickler und Agenten: `CLAUDE.md`.

## Aufbau

- `src/Bullwhip.Core`: Modell ohne I/O.
  - `Chain/`: mehrstufige Kette (`SerialChain`), Konfiguration (`ChainConfig`), Deckungsregeln (`CoverRules`, `PhaseTarget`), Schutzfenster, Kaplan-Lieferzeit (`KaplanLeadTime`, `BaseLeadTime`).
  - `Forecast/`: gleitender Durchschnitt, Stufenprognose, Fehlerfenster, EWMA-Absolutfehler.
  - `Demand/`: AR(1)-Nachfrage und Nachfragemuster.
  - `Analytic/`: geschlossene Formeln mit Quellen (Chen et al. 2000; Chen, Ryan & Simchi-Levi 2000).
  - `Validation/`: Prüfungen gegen die Literatur (`ChenSingleStageCheck`, `GateA`).
  - `Rng/`, `Stats/`: SplitMix64 mit Box–Muller, Normalverteilung.
- `experiments/KSweep`: Studienmodi (`base-kpi`, `countermeasure`, `k-base`, `ratio-scan`, `r-grid-window`, `gate-b`, …; Auswahl in `Program.cs`). Ausgabe nach Datei oder Standardausgabe.
- `tests/Bullwhip.Tests`: xUnit-Tests. `StructureRulesTests` prüft Methodenlänge (höchstens 50 Zeilen) und Verschachtelung (höchstens 4) über `src`, `tests` und `experiments`.

## Basisfall

- Vorlaufzeit: Transitzeit T ∈ {1, 2, 3, 4} Wochen (Median-Transit 10 Tage = 1,43 Wochen), Modell-L = T + 1 ∈ {2, 3, 4, 5}; E[L] = 2,43, CV(L) = 0,30. Vierpunktige Verteilung, damit die Kaplan-Hazardrate nicht fällt (DECISIONS C82).
- Kaplan-Modell (kein Überholen), Deckungsregel B (CycleAverage) bei R > 1, Bestellintervall R = 4, Prognosefenster p = 8.
- Kalibrierung: Fehlmengen-zu-Lager-Verhältnis 26, β = 1 für die vorgelagerten Stufen, K-Faktor 2,6379 (K₁ = 121,75 €); bestätigt in DECISIONS C84.
- Start: leerer Start mit Start-Überbestellung; Messung nach dem Warm-up.

## Status

- `dotnet build --no-incremental`: 0 Warnungen, 0 Fehler.
- `dotnet test -c Release`: 243 von 243 Tests bestehen.
- Gegenmaßnahmen (Lieferzeit-Dosis 02, Informationsweitergabe 01, Bestellintervalle 03, Prognosefenster 04b) sind umgesetzt und getestet.
- Ergebnisse der Läufe: `bullwhip-effekt/simulation-outcome/` im Bachelorarbeit-Ordner.
- Hash-Baselines (`FingerprintTests`): Änderungen nur mit Begründung in `DECISIONS.md` und neuem Fingerabdruck.

## Bauen und testen

```
dotnet build -c Release --no-incremental
dotnet test -c Release
dotnet run -c Release --no-build --project experiments/KSweep -- <modus> [argumente] <ausgabe>
```

## Bekannte Lücken

- Keine CLI und keine Konfiguration aus JSON. Die Studienmodi in `experiments/KSweep` haben keine Unit-Tests; ihre Ausgaben werden über die Läufe im Bachelorarbeit-Ordner verglichen.
- Gleichverteilte Bestellphasen sind die einzige geprüfte Phasenstruktur; gestaffelte Phasen sind nicht als Lauf dokumentiert.
- Ryans Konstante (Dissertation 1997) ist nicht beschafft; die eigene Ableitung ersetzt sie vorläufig (F7).
- Die Regel für vorgelagerte Stufen bei nicht-ganzzahliger Vorlaufzeit ist eine Modellannahme ohne Quelle (F6).
