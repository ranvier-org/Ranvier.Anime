/// <summary>Author a paused composed timeline using children, labels and ordinary F# control flow.</summary>
module Ranvier.Anime.ComposedTimelineDsl

open Ranvier.Dom

/// <summary>A child or label placed when its expression is reached.</summary>
type Cue = private {
    Position: TimelinePosition option
    Place: ComposedTimeline -> TimelinePosition option -> unit
}

let private add targets options properties =
    { Position = None
      Place = fun timeline position ->
          timeline.Add(targets, properties, options = options, ?position = position) |> ignore }

/// <summary>Adds one child animation over all the supplied targets. Target order is preserved.</summary>
let group (targets: 'Target array) options properties = add (Array.map box targets) options properties

/// <summary>Adds a child animation over one target.</summary>
let child (target: obj) options properties = add [| target |] options properties

/// <summary>Records a label at the current end of the timeline.</summary>
let label name =
    { Position = None
      Place = fun timeline position -> timeline.Label(name, ?position = position) |> ignore }

/// <summary>Places the cue at an absolute, relative or named position.</summary>
let at position (cue: Cue) = { cue with Position = Some position }

/// <summary>Applies cues in source order to a fresh composed timeline.</summary>
type ComposedTimelineBuilder internal (scheduler: FrameScheduler) =
    member _.Yield(cue: Cue) = fun timeline -> cue.Place timeline cue.Position
    member _.Zero() = fun (_: ComposedTimeline) -> ()
    member _.Delay(body: unit -> ComposedTimeline -> unit) = fun timeline -> body() timeline
    member _.Combine(first: ComposedTimeline -> unit, second: ComposedTimeline -> unit) =
        fun (timeline: ComposedTimeline) ->
            first timeline
            second timeline
    member _.For(items: seq<'T>, body: 'T -> ComposedTimeline -> unit) =
        fun (timeline: ComposedTimeline) ->
            for item in items do body item timeline
    member _.Run(body: ComposedTimeline -> unit) =
        let timeline = Composed.createTimeline scheduler None (Some false)
        try
            body timeline
            timeline
        with constructionError ->
            try timeline.Revert(false) |> ignore
            with restorationError ->
                raise (System.AggregateException(constructionError, restorationError))
            reraise()

/// <summary>Builds a paused timeline on the supplied scheduler and returns its playback handle.</summary>
let composedTimeline scheduler = ComposedTimelineBuilder scheduler
