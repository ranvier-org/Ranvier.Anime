namespace Ranvier.Anime.Tests

open Ranvier.Anime
open Expecto
module DraggableVelocityRangeTests =
    let velocityRangeTests = testList "draggable source velocity range edges" [
        testCase "ordered ranges retain Math.Clamp results" <| fun _ ->
            for value in [-5.;0.;0.5;1.;3.] do
                Expect.equal (DragPhysics.clampAxis 0. 1. value) (System.Math.Clamp(value,0.,1.)) "ordinary range"
        testCase "inverted limits retain lower-first source comparisons" <| fun _ ->
            Expect.equal ([-1.;0.;1.;2.;3.] |> List.map(DragPhysics.clampAxis 2. 1.)) [2.;2.;2.;1.;1.] "source min before max"
        testCase "NaN passes through both ordered and inverted clamp branches" <| fun _ ->
            Expect.isTrue (System.Double.IsNaN(DragPhysics.clampAxis 0. 1. System.Double.NaN)) "ordered"
            Expect.isTrue (System.Double.IsNaN(DragPhysics.clampAxis 2. 1. System.Double.NaN)) "inverted"
    ]
