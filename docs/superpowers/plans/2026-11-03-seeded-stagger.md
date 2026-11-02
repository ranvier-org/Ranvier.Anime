# Random stagger origins and jitter

Implement the approved stagger inventory's random-origin and jitter behavior inline with the executing-plans skill. The existing numeric distribution remains the implementation boundary; no CSS or ownership contract changes.

## Contract

- Add `StaggerOrigin.Random`, `StaggerJitter.Magnitude/Range`, and optional finite floating-point `Seed` and `Jitter` fields to `StaggerOptions`; defaults preserve current behavior.
- Port the exact pinned generator's state transitions, inclusive random rounding and Fisher-Yates order. Use System.Random.Shared for unseeded draws. Seeded behavior must match Anime.js v4.5.0, including zero, negative and fractional seeds.
- Generate jitter samples before shuffling distances. Jitter magnitude interpolates using the transformed, possibly shuffled distance divided by the original maximum. Apply it after spacing/start and before the modifier. Index mapping selects both distance and jitter sample.
- Add actual upstream fixtures for seeded random origins, constant/range jitter, reversal/easing/grids, mapping, start/modifier ordering and degenerate counts. Nonfinite source results are encoded explicitly in fixtures.

## Verification

1. Check public signature impact; unknown semantic-tool results do not prove absence of consumers.
2. Observe ignored random/jitter options failing fixtures, then prove exact arithmetic and distributions in SageFs before persisting.
3. Run the full Release and Traced Debug suites and XML audit through warm Partas.Build; get one fresh final review and fix concrete findings with regressions.
4. Record scope and evidence, commit. Automatic geometry, typed units and timeline positioning remain required stagger work.

Verified: ignored options failed fixtures before implementation; pure generator/shuffle/jitter proofs passed in SageFs. Full suites145/145 Release and145/145 Traced Debug; XML audit0. Fresh review found no material issues and independently matched180000 draws across18 edge seeds. The solution project set is unchanged.
