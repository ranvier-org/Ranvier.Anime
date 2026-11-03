module Ranvier.Anime.Tests.Program

open Expecto

[<EntryPoint>]
let main args = runTestsWithCLIArgs [] args (testList "native Anime" [RunnerTests.tests; ReactiveTests.tests; CssTests.tests; EaseTests.tests; CurveTests.tests; ValueInterpolationTests.tests; KeyframeCompilationTests.compilationTests; KeyframePlaybackTests.playbackTests; KeyframePlaybackTests.interruptionTests; KeyframeNumericTests.inputTests; KeyframeNumericTests.contractTests; KeyframeCssTests.cssSourceTests; KeyframeCssTests.cssContractTests; CssSnapshotTests.tests; AnimatableValueTests.tests; AnimatableTrackTests.trackTests; AnimatableTrackTests.groupTests; AnimatableNumberTests.numericFacadeTests; AnimatableCssTests.cssFacadeTests; DeclarationTests.tests; UnitConversionTests.tests; SpringTests.tests; StaggerTests.tests; TimelineTests.tests; PlaybackTimingTests.tests; PlaybackStateTests.tests; TimerTests.tests; AnimationPlaybackTests.tests; TimelineClockTests.tests; TimelinePlaybackTests.tests])
