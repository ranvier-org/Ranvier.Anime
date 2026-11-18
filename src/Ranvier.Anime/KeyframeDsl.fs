/// <summary>Author a native keyframe sequence from sparse rows or normalized offsets.</summary>
module Ranvier.Anime.KeyframeDsl

/// <summary>Collects one kind of key in source order and captures the sequence once.</summary>
type KeyframeBuilder internal () =
    member _.Yield(key) = Seq.singleton key
    member _.Zero() = Seq.empty
    member _.Combine(first, rest) = Seq.append first rest
    member _.Delay(body) = Seq.delay body
    member _.For(items, body) = Seq.collect body items
    member _.Run(keys: seq<KeyframeRow<'Target,'Value>>) = KeyframeSequence.Rows(Seq.toArray keys)
    member _.Run(keys: seq<KeyframeOffset<'Target,'Value>>) = KeyframeSequence.Offsets(Seq.toArray keys)

/// <summary>Yield KeyframeRow values or KeyframeOffset values to produce an ordinary KeyframeSequence.</summary>
let keyframes = KeyframeBuilder()
