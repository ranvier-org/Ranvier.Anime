module Ranvier.Anime.Tests.AdapterTests

open System
open System.IO
open System.Text.Json
open Expecto
open Ranvier.Dom
open Ranvier.Anime

type private TestWidget() =
    member val Foo = 0. with get, set
    member val Bar = 0. with get, set
    member val Locked = false with get, set
    member val LinearR = 0. with get, set
    member val LinearG = 0. with get, set
    member val LinearB = 0. with get, set
    member val Hex = "#000000" with get, set

type private DynamicProbe(group: string, kind: string, locked: bool) =
    let extras = Collections.Generic.Dictionary<string, float>(StringComparer.Ordinal)
    member _.Group = group
    member _.Kind = kind
    member _.Locked = locked
    member val Alpha = 0. with get, set
    member val Gated = 0. with get, set
    member _.Item
        with get(k: string) = match extras.TryGetValue k with true, v -> v | _ -> 0.
        and set (k: string) (v: float) = extras[k] <- v

let private fixture () =
    let root = Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT"
    let dir = if String.IsNullOrWhiteSpace root then Path.Combine(AppContext.BaseDirectory, "reference") else root
    JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "text-reference.json")))

let tests =
    testList "adapters registry, custom object/color adapters and deferred stubs" [
        testCase "AdapterRegistry two-pass precedence and gating match upstream registry.js" <| fun _ ->
            use doc = fixture()
            let reg = AdapterRegistry()
            let a1 = reg.Register(fun t -> match t with :? DynamicProbe as p -> p.Group = "g1" | _ -> false)
            let ta1 = a1.RegisterTypeAdapter<DynamicProbe>(fun p -> p.Kind = "widget")
            ta1.RegisterScalar<DynamicProbe>("alpha", (fun p -> p.Alpha), (fun p v _ -> p.Alpha <- v))
            ta1.RegisterScalar<DynamicProbe>("gated", (fun p -> p.Gated), (fun p v _ -> p.Gated <- v), gate = (fun p -> not p.Locked))

            let ta2 = a1.RegisterTypeAdapter<DynamicProbe>(fun p -> p.Kind = "widget")
            ta2.RegisterScalar<DynamicProbe>("unmappedByFirst", (fun _ -> 0.), (fun _ _ _ -> ()))

            a1.RegisterPropertyResolver(fun t name ->
                match t with
                | :? DynamicProbe as p when name.StartsWith("dyn_", StringComparison.Ordinal) ->
                    Some
                        { Get = (fun _ -> AdaptedValue.Scalar p[name])
                          Set = (fun _ sOpt info -> p[name] <- defaultArg sOpt info.Number)
                          Gate = None }
                | _ -> None)

            let probes = (doc.RootElement.GetProperty "adapterProbes").EnumerateArray() |> Seq.toArray
            let actual =
                [| (reg.Resolve(box (DynamicProbe("g1", "widget", false)), "alpha")).IsSome
                   (reg.Resolve(box (DynamicProbe("g1", "widget", false)), "gated")).IsSome
                   (reg.Resolve(box (DynamicProbe("g1", "widget", true)), "gated")).IsSome
                   (reg.Resolve(box (DynamicProbe("g1", "widget", false)), "unmappedByFirst")).IsSome
                   (reg.Resolve(box (DynamicProbe("g1", "widget", false)), "dyn_test")).IsSome
                   (reg.Resolve(box (DynamicProbe("other", "widget", false)), "dyn_test")).IsSome |]
            for i in 0 .. probes.Length - 1 do
                let desc = probes[i].GetProperty("desc").GetString()
                let expected = probes[i].GetProperty("found").GetBoolean()
                Expect.equal actual[i] expected desc

        testCase "ComposedAnimation and Revert drive scalar and sRGB-to-linear color target adapters" <| fun _ ->
            use host = new ManualFrameScheduler()
            let reg = AdapterRegistry()
            let adapter = reg.Register()
            let mutable lastInfo : AdaptedTweenInfo option = None
            let widgetTa = adapter.RegisterTypeAdapter<TestWidget>()
            widgetTa.RegisterScalar<TestWidget>(
                "foo",
                (fun w -> w.Foo),
                (fun w v info -> lastInfo <- Some info; w.Foo <- v))
            widgetTa.RegisterColor<TestWidget>(
                "color",
                (fun w -> w.Hex),
                (fun w (r, g, b, _) _ ->
                    w.LinearR <- Adapters.srgbToLinear (r / 255.)
                    w.LinearG <- Adapters.srgbToLinear (g / 255.)
                    w.LinearB <- Adapters.srgbToLinear (b / 255.)))

            let w = TestWidget(Foo = 5., Hex = "#ff0000")
            use anim =
                Composed.animate
                    host
                    (box w)
                    [| PropertySpec.Scalar("foo", 45.)
                       PropertySpec.Text("color", "#0000ff") |]
                    (Some { ComposedAnimationOptions.Default with
                              Duration = 100.; Ease = id; Autoplay = false; Registry = Some reg })
            anim.Seek 50. |> ignore
            Expect.floatClose Accuracy.high w.Foo 25. "scalar foo midpoint"
            Expect.isSome lastInfo "AdaptedTweenInfo passed to setter"
            Expect.isTrue (Object.ReferenceEquals(lastInfo.Value.Target, box w)) "AdaptedTweenInfo.Target"
            let expectedMidChannel = Adapters.srgbToLinear (Math.Round(Math.Sqrt(255. * 255. * 0.5)) / 255.)
            Expect.isLessThan (abs (w.LinearR - expectedMidChannel)) 0.01 "sRGB sqrt-lerp red midpoint"
            Expect.isLessThan (abs (w.LinearB - expectedMidChannel)) 0.01 "sRGB sqrt-lerp blue midpoint"
            anim.Seek 100. |> ignore
            Expect.floatClose Accuracy.veryHigh w.Foo 45. "scalar foo endpoint"
            Expect.floatClose Accuracy.high w.LinearR 0. "red at 0"
            Expect.floatClose Accuracy.high w.LinearB 1. "blue at 1"
            anim.Revert() |> ignore
            Expect.floatClose Accuracy.veryHigh w.Foo 5. "Revert restored scalar foo to 5"
            Expect.floatClose Accuracy.high w.LinearR 1. "Revert restored color red to 1"
            Expect.floatClose Accuracy.high w.LinearB 0. "Revert restored color blue to 0"

        testCase "Deferred ThreeJsAdapter stubs raise documented NotSupportedException" <| fun _ ->
            Expect.throwsT<NotSupportedException> (fun () -> ThreeJsAdapter.register () |> ignore) "ThreeJsAdapter.register stub"
            Expect.throwsT<NotSupportedException> (fun () -> ThreeJsAdapter.getInstances (obj()) |> ignore) "ThreeJsAdapter.getInstances stub"
            Expect.throwsT<NotSupportedException> (fun () -> ThreeJsAdapter.commitChanges (obj())) "ThreeJsAdapter.commitChanges stub"

        ptestCase "DEFERRED RED: JavaScript Three.js InstancedMesh getInstances and commitChanges matrix/color sync" <| fun _ ->
            // Requires JavaScript three.js InstancedMesh/BatchedMesh runtime types outside the native .NET WebCore scope.
            let _adapter = ThreeJsAdapter.register ()
            let instances = ThreeJsAdapter.getInstances (obj())
            ThreeJsAdapter.commitChanges (obj())
            Expect.isNonEmpty instances "InstancedMesh proxy array"

        ptestCase "DEFERRED RED: Opaque cross-scope pending CSS variable shorthand snapshot with conflicting priority" <| fun _ ->
            // Requires native pending-substitution handle exposure in Ranvier.Dom CSSStyleDeclaration.
            use _snap = DeferredCapabilities.captureOpaquePendingCssSnapshot Unchecked.defaultof<CSSStyleDeclaration> "margin"
            ()
    ]
