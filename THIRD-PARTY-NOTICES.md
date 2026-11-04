# Anime.js

Upstream: https://github.com/juliangarnier/anime, v4.5.0, commit `2c9cf8ea00329f6768c7d7902252ed977d75ce42`.

The behavioral reference fixtures are generated from Anime.js. Preserve this notice with derived source, tests and distributions.

CSS snapshot expansion data in `src/Ranvier.Anime/CssPropertyMetadata.fs` is generated from WebKit's `Source/WebCore/css/CSSProperties.json` at commit `b95c91b1c0ee43cc86d25ff7af73038f67a88e04`, the WebKit revision recorded by the qualified NativeP7 payload. `tools/css-metadata.mjs` is the port's own generator; `tests/reference/css-metadata.json` records exact input/output hashes. The metadata's associated WebKit generator carries these terms, retained here with the generated data:

Copyright (C) 2022-2023 Apple Inc. All rights reserved.
Copyright (C) 2024-2026 Samuel Weinig <sam@webkit.org>

Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY APPLE INC. AND ITS CONTRIBUTORS ``AS IS'' AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL APPLE INC. OR ITS CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

The playback timing geometry in `src/Ranvier.Anime/PlaybackTiming.fs`, callback/control transitions in `src/Ranvier.Anime/PlaybackState.fs`, clock anchoring/throttling in `src/Ranvier.Anime/Timer.fs` and numeric render eligibility in `src/Ranvier.Anime/NumericAnimation.fs` are derived from Anime.js's animation, timer, clock and core render logic at this commit.

Timeline iteration/child traversal in `src/Ranvier.Anime/Timeline.fs` and scalar sibling eligibility/composition in `src/Ranvier.Anime/PropertyTrack.fs` also follow the pinned timeline and core render logic.

Scope resource groups, methods, media refresh and keepTime reconstruction in `src/Ranvier.Anime/Scope.fs`, `ScopeTimer.fs` and `ScopeAnimation.fs` derive from the pinned scope implementation. The port uses actual Ranvier owners and typed clock snapshots, with terminal native restoration handles.

Endpoint decomposition, homogenization and scalar/color/compound interpolation in `src/Ranvier.Anime/ValueInterpolation.fs` derive from the pinned core values, colors, helpers, render and animation code.

Keyframe array/row/offset normalization, endpoint chaining and construction geometry in `src/Ranvier.Anime/KeyframeCompilation.fs` follow the pinned animation keyframe generation and tween construction logic.

Per-keyframe history, same-parent replace eligibility, ordered dispatch and stretch behavior in `src/Ranvier.Anime/KeyframePlayback.fs` follow the pinned core renderer and animation composition logic.

Typed frame callback evaluation and numeric/CSS keyframe binding in `src/Ranvier.Anime/Keyframe.fs` and `Keyframes.fs` adapt the pinned animation constructor and revert behavior to Ranvier ownership and captured property accessors. Native CSS restoration uses one pre-construction snapshot per property instead of replaying each source frame's inline snapshot.

Cached getters and retarget endpoint updates in `src/Ranvier.Anime/AnimatableValue.fs` follow the pinned animatable code, with guarded atomic updates and snapshot component getters.

Retarget playback and cache-only reset integration in `src/Ranvier.Anime/AnimatableTrack.fs` also follow the pinned animatable, animation and timer/render behavior.

The independent callback animation in `src/Ranvier.Anime/AnimatableGroup.fs` and ordered root traversal in `src/Ranvier.Anime/AnimatableFrames.fs` follow the pinned animatable, engine, clock and linked-list helpers.

The public numeric property handles and ordered rewind/restoration in `src/Ranvier.Anime/Animatable.fs` follow the pinned animatable lifecycle, with explicit Ranvier ownership and terminal native handles.

Typed CSS cached getters and component retargeting in `src/Ranvier.Anime/AnimatableCss.fs` use the same pinned animatable behavior, with per-property inline restoration and Ranvier scalar-unit contexts.

Contextual conversion in `src/Ranvier.Anime/CssUnits.fs` follows the pinned core units code's angle ratios and clone measurement procedure, with scoped cleanup and per-element construction measurements.

SVG geometry motion sampling, drawable attribute and inline stroke-linecap handling, and morph endpoint generation in `src/Ranvier.Anime/Svg.fs` and `src/Ranvier.Anime/SvgProperties.fs` derive from the pinned Anime.js v4.5.0 SVG and morph logic. They use typed Ranvier DOM geometry, the actual Ranvier owner and scope restoration, and canonical CSS snapshots. The behavioral oracle `tools/svg-reference.mjs` generates `tests/reference/svg-reference.json` (SHA-256 `6459c0c0c83d7519656a76e4532d0fc32d17306bd610f4dfa4fbb3bd1cf60a34`). These derived files are covered by the Anime.js MIT License below.

Draggable physics, release springs, scroll and DOM input, CSS axis snapshots, the source transform cache and native composite lifetime in `src/Ranvier.Anime/DragPhysics.fs`, `Draggable.fs`, `DraggableScroll.fs`, `DraggableDom.fs`, `DraggableCss.fs`, `TransformCache.fs`, `DraggableTransformTarget.fs` and `NativeDraggable.fs` derive from the pinned Anime.js v4.5.0 draggable, physics, transform and timer logic. They use typed Ranvier DOM and CSS services, the actual Ranvier owner and scope restoration, and native host input. The behavioral oracles `tools/draggable-reference.mjs`, `tools/transform-reference.mjs`, `tools/release-reference.mjs` and `tools/inline-reference.mjs` generate `tests/reference/draggable-reference.json`, `transform-reference.json`, `release-reference.json` and `inline-reference.json`. The tests and native probes that consume these fixtures are derived from the same oracles. These derived files are covered by the Anime.js MIT License below.

The easing formulas retain Anime.js's credit to Robert Penner, Copyright (c) 2001 Robert Penner, from https://robertpenner.com/easing/.
The cubic Bezier solver retains Anime.js's credit to https://github.com/gre/bezier-easing, Copyright (c) 2014 Gaëtan Renaudeau.
These credits are preserved in the ported source as well as this notice.

The spring solver retains Anime.js's credit to WebKit's https://webkit.org/demos/spring/spring.js, Copyright (c) 2016 WebKit - Apple Inc.

Scroll observation in `src/Ranvier.Anime/Scroll.fs`, `src/Ranvier.Anime/ScrollDebug.fs` and `src/Ranvier.Anime/ScrollDom.fs` derives from the pinned `ScrollObserver` thresholds, geometry, transition, link and debug overlay logic in `src/events/scroll.js`. Its fixtures `tests/reference/scroll-reference.json`, `tests/reference/scroll-debug-reference.json` and `tests/reference/scroll-construction-reference.json` are generated by `tools/scroll-reference.mjs`, `tools/scroll-debug-reference.mjs` and `tools/scroll-construction-reference.mjs` from unmodified upstream modules. `tests/Ranvier.Anime.Tests/ScrollTests.fs`, `tests/Ranvier.Anime.Tests/ScrollDomTests.fs` and `tests/Ranvier.Anime.NativeSmoke/ScrollNativeProbe.fs` verify these ports.

The MIT License

Copyright (c) 2025 Julian Garnier

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
