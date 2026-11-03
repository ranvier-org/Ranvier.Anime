module Ranvier.Anime.Tests.Program

open Expecto

[<EntryPoint>]
let main args = runTestsWithCLIArgs [] args (testList "native Anime" [RunnerTests.tests; ReactiveTests.tests; CssTests.tests; EaseTests.tests; CurveTests.tests; ValueInterpolationTests.tests; KeyframeCompilationTests.compilationTests; KeyframePlaybackTests.playbackTests; KeyframePlaybackTests.interruptionTests; AnimatableValueTests.tests; AnimatableTrackTests.trackTests; AnimatableTrackTests.groupTests; AnimatableNumberTests.numericFacadeTests; AnimatableCssTests.cssFacadeTests; DeclarationTests.tests; UnitConversionTests.tests; SpringTests.tests; StaggerTests.tests; TimelineTests.tests; PlaybackTimingTests.tests; PlaybackStateTests.tests; TimerTests.tests; AnimationPlaybackTests.tests; TimelineClockTests.tests; TimelinePlaybackTests.tests])
