# Value interpolation and typed declaration playback

Continue the approved full native-port design inline with TDD and SageFs.

## Contract

Port pinned Anime.js value decomposition, endpoint homogenization and composition as internal calculation data. Cover numeric and matching-unit values, relative operators, RGBA/hex/HSL source colors, compound numeric strings, unequal component counts, target string templates, precision and per-component modifiers. RGB interpolation uses source square/square-root blending, alpha linear; preserve source conversion/rounding behavior rather than substituting a color-space approximation.

Public CSS authoring reuses Ranvier.Css.Declaration/Value and native CSS services. Do not add a public CSS value/color model. Add declaration interpolation/playback once the typed boundary and contextual normalization are verified against actual Ranvier APIs. Context-dependent units and modern/named colors must use WebCore services, or remain explicitly unqualified until integrated. Never silently approximate unit conversion or claim a parser implements the browser.

## Work

- [x] Capture clean pinned-source animation outputs and decomposition data for colors/units/compound values/relative operators, including precision, overshoot, component modifiers and generated cases.
- [x] Reproduce placeholder failures in SageFs; port and prove the internal interpolation kernel, including source edge cases and reentrant modifier guards.
- [x] Integrate explicit source-literal declaration interpolation and shared Timer playback; preserve priority, ownership and callback cancellation.
- [x] Add HTML scalar-unit normalization through verified native clone/measurement APIs; see contextual-units plan.
- [ ] Add contextual color normalization and SVG-specific unit contexts. Keep supported endpoints explicit.
- [x] Run complete normal/Traced suites, XML audit, source regeneration and one scoped review for the implemented adapter; qualify it with fresh public-cohort JIT/NativeAOT evidence. Maintain both solutions and full parity inventory.

Reconnaissance: RuntimeApi exposes renderValue/tryNumericValue, not color channels. Declaration.name/value/isImportant are available through DeclarationModule. Native ComputedStyleService.Get, StyleValueService.Parse/ParseAll and StyleMapService.Computed exist; there is no Window type in the inspected DOM assembly. FCS external project resolution remains unknown, so real loaded assembly inspection and SageFs type checks supply bounded API evidence. The coordinator's source-only answer below confirms the boundary.

Coordinator source inspection confirmed public P7 has no structured color/channel accessor in its manifest or handwritten C ABI. Computed-style resolution does not promise sRGB rgba serialization for modern/wide-gamut colors; canvas pixels would quantize and lose hidden alpha-zero RGB. No public CSS representation is introduced here. Typed declaration playback and contextual normalization remain open.

Kernel checkpoint:136 decompositions,136 composition cases ×11 progress values,14 number-format fixtures and10 guard/validation tests. Constant placeholders failed2598 checks in the initial oracle. Expanded fixtures reproduced radix/ASCII/color-prefixed-compound boundaries. One review found two Important coercion gaps;14 failing tests reproduced relative scalar radix/empty conversion and NaN/case-sensitive Infinity-like text classification, now fixed. Unit-before-operator precedence is retained: +=0xff is source number0/unitxff, while +=0x20 is scalar32. No Critical/Minor findings or re-review loop.

Full normal Release and Traced Debug suites1095/1095, XML audit0. Final source regeneration is byte-identical, SHA25686311dd1d6b5172f916dd16be5418b740e566611c4aad2c7a3b893f4bc5c797a. Both solutions remain current; no project added. This completes the internal calculation checkpoint, not the remaining public/native adapter or full plan.

Declaration adapter checkpoint: DeclarationAnimationOptions + CssAnimation.declaration/declarationWith accept uint milliseconds; declarationPrecise/With retain float durations. All authoring uses existing Declaration values and StyleDeclaration.apply; the result is the existing Timer and Timer.own supplies Ranvier ownership. Shared guards now span inherited borrowed setters, user modifiers and later lifecycle callbacks. A pure source-style CSS dispatch oracle exposed39 numeric-rounding mismatches before prepareCss separated CSS precision from raw object numbers. The final oracle includes137 CSS and137 object schedules at11 progress values,138 decompositions and14 number-format cases.

Fresh scoped review: no Critical/Important findings, one Minor doc clarification fixed (scalar endpoint units must match; compound templates follow source behavior). Added a source mixed-unit compound fixture. Strict NativeAOT initially reached our generated Literal/Data/Output diagnostics; actual ILC graph edges led to PrintFormatToString. Authored invariant diagnostics and direct debugger displays remove those roots. Both strict publications and PEs pass without IL diagnostics or suppression. Fresh native checks cover typed width/layout, legacy source color blending, compound transformed geometry and active owner disposal in a component modifier; host.Errors0.

Final full suites1246/1246 normal/Traced, XML0, evidence regressions GREEN. Oracle regenerates identically at48e638c551463ed2aaaf5d4e337d3e70736a753747f816d82873a44e98b6330d. New immutable public45-p7-declarations archives bind both current library/consumer sources; both receipts/all18 files reverified. Current Debug publication validates339 authored and190 ILC inputs. Contextual/modern colors, scalar unit conversion and the full port remain open.
