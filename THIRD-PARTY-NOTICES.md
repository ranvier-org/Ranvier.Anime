# Anime.js

Upstream: https://github.com/juliangarnier/anime, v4.5.0, commit `2c9cf8ea00329f6768c7d7902252ed977d75ce42`.

The behavioral reference fixtures are generated from Anime.js. Preserve this notice with derived source, tests and distributions.

The playback timing geometry in `src/Ranvier.Anime/PlaybackTiming.fs`, callback/control transitions in `src/Ranvier.Anime/PlaybackState.fs`, clock anchoring/throttling in `src/Ranvier.Anime/Timer.fs` and numeric render eligibility in `src/Ranvier.Anime/NumericAnimation.fs` are derived from Anime.js's animation, timer, clock and core render logic at this commit.

Timeline iteration/child traversal in `src/Ranvier.Anime/Timeline.fs` and scalar sibling eligibility/composition in `src/Ranvier.Anime/PropertyTrack.fs` also follow the pinned timeline and core render logic.

Endpoint decomposition, homogenization and scalar/color/compound interpolation in `src/Ranvier.Anime/ValueInterpolation.fs` derive from the pinned core values, colors, helpers, render and animation code.

Cached getters and retarget endpoint updates in `src/Ranvier.Anime/AnimatableValue.fs` follow the pinned animatable code, with guarded atomic updates and snapshot component getters.

Retarget playback and cache-only reset integration in `src/Ranvier.Anime/AnimatableTrack.fs` also follow the pinned animatable, animation and timer/render behavior.

The independent callback animation in `src/Ranvier.Anime/AnimatableGroup.fs` and ordered root traversal in `src/Ranvier.Anime/AnimatableFrames.fs` follow the pinned animatable, engine, clock and linked-list helpers.

Contextual conversion in `src/Ranvier.Anime/CssUnits.fs` follows the pinned core units code's angle ratios and clone measurement procedure, with scoped cleanup and per-element construction measurements.

The easing formulas retain Anime.js's credit to Robert Penner, Copyright (c) 2001 Robert Penner, from https://robertpenner.com/easing/.
The cubic Bezier solver retains Anime.js's credit to https://github.com/gre/bezier-easing, Copyright (c) 2014 Gaëtan Renaudeau.
These credits are preserved in the ported source as well as this notice.

The spring solver retains Anime.js's credit to WebKit's https://webkit.org/demos/spring/spring.js, Copyright (c) 2016 WebKit - Apple Inc.

The MIT License

Copyright (c) 2025 Julian Garnier

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
