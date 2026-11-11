namespace Ranvier.Anime.Tests

open System
open Ranvier.Dom
open Ranvier.View.WebCore

/// <summary>Public View45 host input routes for the real DOM dragging probes.</summary>
module DraggableHostProbe =
    let private action = function
        | "mousemove" -> 0
        | "mousedown" -> 1
        | "mouseup" -> 2
        | name -> invalidArg "name" ("Unknown mouse phase: "+name)
    /// <summary>Injects mouse phases through native hit-testing using the matched host's public Mouse API.</summary>
    let mouse (host:WebCoreHost) scheduler document computed geometry resize media advance =
        let inject name (x:float) (y:float) =
            let phase=action name
            host.Mouse(phase,int(Math.Round x),int(Math.Round y),(if phase=0 then -1 else 0),(if phase=0 then 0 else 1),0) |> ignore
        DraggableNativeProbe.run scheduler document computed geometry resize media inject advance
    /// <summary>Injects one active touch contact per phase through public Pointer input; use a dedicated probe host.</summary>
    let touch (host:WebCoreHost) scheduler document computed geometry resize media advance =
        host.SetMaxTouchPoints 1 |> ignore
        let inject name x y = host.Pointer [|PointerSample.Create(2,1,action name,x,y)|] |> ignore
        DraggableNativeProbe.runTouch scheduler document computed geometry resize media inject advance

