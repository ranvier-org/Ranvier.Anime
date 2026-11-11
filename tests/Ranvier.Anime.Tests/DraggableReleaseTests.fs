namespace Ranvier.Anime.Tests



open System
open Ranvier
open Ranvier.Anime
open Ranvier.Dom

type ReleaseManualFrames() =
    let callbacks=System.Collections.Generic.Dictionary<int,float -> unit>()
    let mutable next=0
    let mutable now=0.
    member _.Scheduler=
        {new FrameScheduler with
            member _.Now()=now
            member _.Frame callback =
                next<-next+1
                let token=next
                callbacks[token]<-callback
                {new IDisposable with member _.Dispose()=callbacks.Remove token |> ignore}
            member _.Timeout(_,_)={new IDisposable with member _.Dispose()=()}
            member _.Microtask _={new IDisposable with member _.Dispose()=()}
         interface IDisposable with member _.Dispose()=callbacks.Clear()}
    member _.AdvanceBy(milliseconds:float)=
        now<-now+milliseconds
        let work=callbacks.Values |> Seq.toArray
        callbacks.Clear()
        for callback in work do callback now
    member _.Pending=callbacks.Count

open Expecto

module DraggableReleaseTests =
    open System
    open System.Text.Json
    
    
    let compareFixture (fixture:JsonElement) =
        let frames=ReleaseManualFrames()
        let initialCoordinates=fixture.GetProperty("initialCoordinates").EnumerateArray() |> Seq.map _.GetDouble() |> Seq.toArray
        let mutable x=initialCoordinates[0]
        let mutable y=initialCoordinates[1]
        let bounds=fixture.GetProperty("bounds").EnumerateArray() |> Seq.map _.GetDouble() |> Seq.toArray
        let optional (name:string) = match fixture.TryGetProperty name with true,value -> Some value | _ -> None
        let snap=optional "snap" |> Option.map(fun value -> DragSnap.Points(value.EnumerateArray() |> Seq.map _.GetDouble() |> Seq.toList)) |> Option.defaultValue DragSnap.None
        let disabledY=optional "disableY" |> Option.map _.GetBoolean() |> Option.defaultValue false
        let callback=optional "callback" |> Option.map _.GetString() |> Option.defaultValue ""
        let customSpring=optional "spring" |> Option.map(fun _ -> Spring(mass=1.,stiffness=80.,damping=20.))
        let modifier name=if name="double" then (fun value -> value*2.) else (fun value -> value+1.)
        let globalModifier=optional "globalModifier" |> Option.map(fun value -> modifier(value.GetString())) |> Option.defaultValue id
        let modifierX=optional "modifierX" |> Option.map(fun value -> modifier(value.GetString())) |> Option.defaultValue globalModifier
        let modifierY=optional "modifierY" |> Option.map(fun value -> modifier(value.GetString())) |> Option.defaultValue globalModifier
        let alias=optional "alias" |> Option.map _.GetBoolean() |> Option.defaultValue false
        let settings ()={DraggableSettings.Default with MinVelocity=(optional "minVelocity" |> Option.map _.GetDouble() |> Option.defaultValue 0.);MaxVelocity=(optional "maxVelocity" |> Option.map _.GetDouble() |> Option.defaultValue 50.);Contained=true;Bounds={Top=bounds[0];Right=bounds[1];Bottom=bounds[2];Left=bounds[3]};YEnabled=not disabledY;SnapX=snap;SnapY=snap}
        let events=ResizeArray<JsonElement>()
        let mutable hasReset=false
        let record name (self:Draggable) =
            events.Add(JsonSerializer.SerializeToElement {|event=name;state={|x=self.X;y=self.Y;velocity=self.Velocity;grabbed=self.Grabbed;dragged=self.Dragged;released=self.Released|}|})
            if name="Release" && callback="releaseStop" then self.Stop()
            if name="Settle" && callback="settleReset" && not hasReset then hasReset<-true;self.Reset()
        let horizontal=NumericProperty((fun()->x),(fun value->x<-value))
        let vertical=if alias then horizontal else NumericProperty((fun()->y),(fun value->y<-value))
        use controller=new Draggable(frames.Scheduler,horizontal,vertical,settings=settings,?releaseSpring=customSpring,modifierX=modifierX,modifierY=modifierY,
                        onGrab=record "Grab",onDrag=record "Drag",onRelease=record "Release",onUpdate=record "Update",onSettle=record "Settle",onSnap=record "Snap")
        controller.Refresh()
        let snapshot ()=JsonSerializer.SerializeToElement {|x=controller.X;y=controller.Y;destX=controller.DestX;destY=controller.DestY;deltaX=controller.DeltaX;deltaY=controller.DeltaY;velocity=controller.Velocity;angle=controller.Angle;grabbed=controller.Grabbed;dragged=controller.Dragged;released=controller.Released|}
        let rec equal (expected:JsonElement) (actual:JsonElement) =
            if expected.ValueKind<>actual.ValueKind then false
            else match expected.ValueKind with
                 | JsonValueKind.Number -> abs(expected.GetDouble()-actual.GetDouble())<=0.00011
                 | JsonValueKind.Object -> expected.EnumerateObject() |> Seq.filter(fun property -> property.Name<>"manual") |> Seq.forall(fun property -> match actual.TryGetProperty property.Name with true,value -> equal property.Value value | _ -> false)
                 | JsonValueKind.Array -> expected.GetArrayLength()=actual.GetArrayLength() && Seq.forall2 equal (expected.EnumerateArray()) (actual.EnumerateArray())
                 | _ -> expected.ToString()=actual.ToString()
        let failures=ResizeArray<string>()
        for index,sample in fixture.GetProperty("samples").EnumerateArray() |> Seq.indexed do
            events.Clear()
            let step=sample.GetProperty "step"
            match step.GetProperty("op").GetString() with
            | "down" -> controller.Grab(0.,0.)
            | "move" -> controller.Move(step.GetProperty("x").GetDouble(),step.GetProperty("y").GetDouble())
            | "up" -> controller.Release()
            | "disable" -> controller.Disable()
            | "enable" -> controller.Enable()
            | _ -> frames.AdvanceBy(step.GetProperty("after").GetDouble())
            let actual=snapshot()
            let actualEvents=JsonSerializer.SerializeToElement(events.ToArray())
            if not(equal (sample.GetProperty "state") actual) || not(equal (sample.GetProperty "events") actualEvents) then
                failures.Add(sprintf "%s step%d %s expected=%s %s actual=%s %s" (fixture.GetProperty("name").GetString()) index (step.ToString()) (sample.GetProperty("state").ToString()) (sample.GetProperty("events").ToString()) (actual.ToString()) (actualEvents.ToString()))
        failures.ToArray()
    
    let disableVelocityTraceTests fixturePath =
        use document=JsonDocument.Parse(System.IO.File.ReadAllText fixturePath)
        let fixtures=document.RootElement.GetProperty("fixtures").EnumerateArray() |> Seq.map _.Clone() |> Seq.toArray
        testList "Draggable full source release traces" [
            for fixture in fixtures do
                testCase (fixture.GetProperty("name").GetString()) <| fun _ ->
                    let failures=compareFixture fixture
                    Expect.equal failures [||] (String.concat "\n" failures)
        ]
