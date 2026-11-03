module Ranvier.Anime.Tests.Program

open Expecto

[<EntryPoint>]
let main args = runTestsWithCLIArgs [] args (testList "native Anime" [RunnerTests.tests; ReactiveTests.tests; CssTests.tests; EaseTests.tests; CurveTests.tests; ValueInterpolationTests.tests; DeclarationTests.tests; SpringTests.tests; StaggerTests.tests; TimelineTests.tests; PlaybackTimingTests.tests; PlaybackStateTests.tests; TimerTests.tests; AnimationPlaybackTests.tests; TimelineClockTests.tests; TimelinePlaybackTests.tests])
