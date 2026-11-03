# Value interpolation and typed declaration playback

Continue the approved full native-port design inline with TDD and SageFs.

## Contract

Port pinned Anime.js value decomposition, endpoint homogenization and composition as internal calculation data. Cover numeric and matching-unit values, relative operators, RGBA/hex/HSL source colors, compound numeric strings, unequal component counts, target string templates, precision and per-component modifiers. RGB interpolation uses source square/square-root blending, alpha linear; preserve source conversion/rounding behavior rather than substituting a color-space approximation.

Public CSS authoring reuses Ranvier.Css.Declaration/Value and native CSS services. Do not add a public CSS value/color model. Add declaration interpolation/playback once the typed boundary and contextual normalization are verified against actual Ranvier APIs. Context-dependent units and modern/named colors must use WebCore services, or remain explicitly unqualified until integrated. Never silently approximate unit conversion or claim a parser implements the browser.

## Work

- [x] Capture clean pinned-source animation outputs and decomposition data for colors/units/compound values/relative operators, including precision, overshoot, component modifiers and generated cases.
- [x] Reproduce placeholder failures in SageFs; port and prove the internal interpolation kernel, including source edge cases and reentrant modifier guards.
- [ ] Integrate typed declaration interpolation and shared playback through the established numeric clock; preserve priority, ownership and callback cancellation. Verify contextual color/unit resolution with the coordinator before selecting a native API.
- [ ] Run complete normal/Traced suites, XML audit, source regeneration and one scoped review; qualify actual native adapter changes with fresh public-cohort JIT/NativeAOT evidence. Maintain both solutions and full parity inventory.

Reconnaissance: RuntimeApi exposes renderValue/tryNumericValue, not color channels. Declaration.name/value/isImportant are available through DeclarationModule. Native ComputedStyleService.Get, StyleValueService.Parse/ParseAll and StyleMapService.Computed exist; there is no Window type in the inspected DOM assembly. FCS external project resolution remains unknown, so real loaded assembly inspection and SageFs type checks supply bounded API evidence. The coordinator's source-only answer below confirms the boundary.

Coordinator source inspection confirmed public P7 has no structured color/channel accessor in its manifest or handwritten C ABI. Computed-style resolution does not promise sRGB rgba serialization for modern/wide-gamut colors; canvas pixels would quantize and lose hidden alpha-zero RGB. No public CSS representation is introduced here. Typed declaration playback and contextual normalization remain open.

Kernel checkpoint:136 decompositions,136 composition cases ×11 progress values,14 number-format fixtures and10 guard/validation tests. Constant placeholders failed2598 checks in the initial oracle. Expanded fixtures reproduced radix/ASCII/color-prefixed-compound boundaries. One review found two Important coercion gaps;14 failing tests reproduced relative scalar radix/empty conversion and NaN/case-sensitive Infinity-like text classification, now fixed. Unit-before-operator precedence is retained: +=0xff is source number0/unitxff, while +=0x20 is scalar32. No Critical/Minor findings or re-review loop.

Full normal Release and Traced Debug suites1095/1095, XML audit0. Final source regeneration is byte-identical, SHA25686311dd1d6b5172f916dd16be5418b740e566611c4aad2c7a3b893f4bc5c797a. Both solutions remain current; no project added. This completes the internal calculation checkpoint, not the remaining public/native adapter or full plan.
