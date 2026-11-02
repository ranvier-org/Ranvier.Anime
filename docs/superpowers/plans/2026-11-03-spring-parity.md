# Spring parity implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans inline with red/green tests.

**Goal:** Port the pinned Anime.js spring solver, perceived and physical parameter modes, mutation and completion behavior, then drive numeric animations with derived settling duration.

**Architecture:** Spring is a reusable stateful easing object above the existing numeric runner. Calculations use floating point milliseconds/seconds as upstream does. Public convenience for whole-millisecond perceived duration accepts uint and converts to float; the precise constructor remains available.

**Tech Stack:** Existing F# library and tests, warm Partas.Build, normal Release and Traced Debug, pinned unmodified upstream reference generator.

**Spec:** ../specs/2026-11-03-native-anime-design.md

## Global constraints

- Ranvier ownership/scheduling and existing CSS types remain unchanged. Investigate our usage before attributing an issue to Ranvier.
- Use source v4.5.0 at2c9cf8ea00329f6768c7d7902252ed977d75ce42; preserve embedded WebKit/Apple spring-solver attribution.
- Rest calculation uses a0.02-second step, threshold0.0005, ten rest steps and3000 maximum iterations, matching inclusive upstream loop bounds.
- Physical fields clamp to source ranges; perceived mode resets mass/velocity. All authored parameters must be finite as an explicit native validation policy.
- Project operations run through build definitions in warm SageFs. Keep solution current; no new project is needed here.

## Review focus

- Critical and overdamped branches, not just the default underdamped curve.
- JavaScript rounding versus .NET midpoint rounding.
- Perceived duration differs from settling duration; animations use the latter.
- Parameter mutation changes solver coefficients and the correct companion fields.
- Completion can fire again after seeking below the perceived threshold; callback reentrancy must be respected by the runner.

## Task 1: Spring object and reference cases

**Files:** tools/reference.mjs; src/Ranvier.Anime/Spring.fs; tests/Ranvier.Anime.Tests/SpringTests.fs; project compile lists.

**Interfaces:** Spring(?mass:float, ?stiffness:float, ?damping:float, ?velocity:float, ?bounce:float, ?duration:float, ?onComplete:(unit->unit)); mutable Mass, Stiffness, Damping, Velocity, Bounce, Duration; read-only SettlingDuration, SolverDuration, Completed; Solve(seconds:float):float and Ease(normalized:float):float.

- [ ] Generate default, physical, critical, overdamped, perceived positive/negative bounce and clamped-boundary references, recording effective parameters, both durations, and sampled outputs. Capture sequential property mutations separately.
- [ ] Tests compare each fixture's effective parameters and samples; duration tolerance0.001ms, curve tolerance1e-10. Identity/missing-behavior stub must fail before implementation.
- [ ] Port exact solver and inclusive rest loop; match source parameter-mode precedence and setter recomputation. Verify formulas in SageFs before persistence.
- [ ] Test completion threshold crossing, backward reset and second crossing; validate nonfinite parameters before callbacks.

## Task 2: Numeric runner integration and unsigned convenience

**Interfaces:** Animation.animateSpring spring scheduler read write target creates an animation using spring.SettlingDuration and spring.Ease. Spring.perceivedMilliseconds duration:uint bounce:float constructs perceived-mode springs while preserving the precise Spring constructor.

- [ ] Test derived duration overrides a user-assumed default: sample halfway through the spring's actual settling duration and compare source output. Completion callback cancellation must suppress the interrupted property write.
- [ ] Verify unsigned conversion at zero and uint32 maximum follows source clamp; keep fractional perceived durations via the precise constructor.
- [ ] Run full Release/Traced tests, XML audit and one independent review; fix material findings with regressions. Commit and update parity inventory. Full timelines/stagger/native integration still remain required.
