# Contextual scalar units

Continue the approved native-port design inline with SageFs, TDD and Partas.Build.

## Contract

Reuse real HTMLElement/Node/style/offsetWidth APIs. Source conversion uses two shallow-clone width measurements at100 source/target units, integer offsetWidth and100 fallback for zero measurements. Common source angles deg/rad/turn use the exact ratio without layout. Keep measurements out of the frame loop and remove temporary nodes on success/failure.

Native adaptation: do not reuse Anime's global currentNumber/from/to cache across elements or hosts. Capture a conversion once during construction from the actual element context; subsequent frames use cached normalized endpoints. Two elements with different font sizes must not share a stale ratio. Reject cross-dimension angle/length inputs rather than manufacturing nonfinite endpoints.

Extend the internal CSS endpoint preparation with a conversion callback at source's post-operator/post-homogenization phase. Add element-context declaration factories using existing typed declarations, options, Timer and StyleDeclaration.apply. Retain uint default/precise-float duration paths, callbacks, priority, owner guards and source compound-template behavior. No public CSS value/unit contract is added.

## Work

- [x] Capture pinned-source angle/measurement/fallback/parent ordering plus global-cache collision. Recording widths are not browser layout proof.
- [x] Prove conversion arithmetic and protocol with failing placeholders; verify cleanup, original target preservation and construction-only measurements.
- [x] Connect element-context declaration playback and compare source-normalized endpoint/render behavior, including relative operators.
- [x] Verify actual native em/rem/percent/absolute units and separate element contexts, then full normal/Traced suites, XML, scoped review and fresh public JIT/NativeAOT replay.

Existing metadata confirms Node.ownerDocument/parentNode/nodeType/cloneNode/appendChild/removeChild/isSameNode, HTMLElement.offsetWidth and Document.body. CSSNumericValue has no conversion members in this public assembly, so no guessed Typed-OM conversion is used. Modern color normalization remains a separate open task.

Verified16 source conversions, source cache replay and six actual-source animation schedules. Identity conversion failed25 arithmetic/probe-eligibility checks; the port matches them. Twenty-nine added tests cover source order, parent/body fallback, cleanup/fault preservation, independent contexts, relative operators before normalization, captured endpoints and invalid-authoring-before-probe. The DOM DispatchProxy tests cannot execute from SageFs's collectible session (non-collectible assembly reference error); compiled suites provide their gate. The original recording style proxy incorrectly returned CSSStyleDeclaration; actual metadata requires CSSStyleProperties, now corrected. Native root access also requires an HTMLElement cast before its inline-style mixin. No upstream defect alleged.

Fresh scoped review found no Critical/Important/Minor issues. Full normal Release and Traced Debug suites1275/1275, XML0 and evidence regressions GREEN. Two source generations match SHA256d7477cdd6a6369390ebdaa50a25147368c0f0afb010e96763a3db922e6272157.

Strict AOT initially failed IL3054 at the private higher-order factory's Literal→Literal→Literal→curried-renderer type. The actual ILC edge from declarationUsing reached FSharpFunc.InvokeFast for that nested type. Selecting an optional flat unit converter and directly calling the preparation functions removes the expansion; public APIs and source results stay unchanged. Both strict publications/PEs pass without IL diagnostics or unconditional-throw messages.

Actual public45/P7 CPU JIT/PE checks pass em16/32 contexts, containing-block percentages, rem root20, source cm integer-baseline conversion37.8px, angle225deg, priority and complete clone removal. Immutable public45-p7-units snapshots preserve both current library/consumer sources; both receipts/all18 files reverified. Current Release provenance binds340 authored/190 ILC inputs. Both solutions remain current. This closes the HTML scalar-unit checkpoint, not modern colors, SVG-specific contexts, refresh semantics or the full port.
