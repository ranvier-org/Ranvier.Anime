# Stagger distribution implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans inline with red/green tests.

**Goal:** Port numeric step/range staggering with origins, 2D/3D grids, axes, reversal, easing, start offsets, modifiers, total overrides and index mapping.

**Architecture:** Typed options describe animation distribution without duplicating CSS or scheduler contracts. A pure distribution function returns numeric values for target indices. Later work adds automatic DOM/object geometry, seeded/random origins, jitter, typed-unit values and timeline positioning; these remain required for complete staggering parity.

**Tech Stack:** Existing F# library, pinned JavaScript reference generator, warm Partas.Build and normal/Traced tests.

**Spec:** ../specs/2026-11-03-native-anime-design.md

## Constraints and review focus

- Use Math.Clamp and other BCL operations where their semantics match. JavaScript nearest rounding uses ties toward positive infinity, which differs from .NET's default; preserve that explicit behavior.
- Unsigned counts, grid dimensions and origins prevent negative authoring inputs; zero grid dimensions are rejected. Signed numeric values and axis distances remain floating point.
- Source reference is v4.5.0 /2c9cf8ea00329f6768c7d7902252ed977d75ce42; compare actual outputs before asserting edge semantics.
- Existing normalized-easing validation must not reject signed grid-axis values that upstream legitimately passes. Add an explicit reference case before adjusting it.
- Check center origins on even target counts; negative axes; zero range distance; start=0 on ranges; reversal with axes; out-of-range index mapping; and negative rounding ties.

## Task 1: Typed numeric distribution

**Files:** Stagger.fs; StaggerTests.fs; tools/reference.mjs; existing project compile lists.

**Interfaces:** StaggerOrigin (First, Last, Center, Index, Position2D, Position3D), StaggerGrid (Flat, Grid2D, Grid3D), StaggerAxis (X,Y,Z), StaggerValue (Step,Range), StaggerOptions.Default, Stagger.distribute : StaggerValue -> StaggerOptions -> uint -> float array.

- [ ] Expand upstream fixtures for all listed options, including degenerate cases and signed-axis easing. Test actual reference arrays with1e-9 absolute tolerance.
- [ ] Observe a zero/identity placeholder failing, then implement distance construction, source rounding and output transformation. Validate authored finite numbers and size limits before allocation.
- [ ] Test invalid dimensions, empty targets, total override and index mapping. Preserve visible outputs for zero max distance; document any safer callback-domain adaptation explicitly.
- [ ] Prove pure functions in SageFs, persist, run full normal/Traced suites and XML audit, then obtain one fresh review. Fix concrete findings through regressions and commit.

## Task 2: Follow-up staggering work

Automatic geometry, random origin, seeded jitter, typed CSS units and timeline offsets remain incomplete until separately implemented and tested. Do not claim complete Stagger parity from Task1.
