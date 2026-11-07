module Program

open Ranvier.View.WebCore.Sdl

[<EntryPoint>]
let main args =
    if Array.contains "smoke" args then
        Smoke.run ()
    else
        use window = Window.Create ("Ranvier Motion", 1180, 860, ?renderer = Host.renderer)
        use running = Host.mount window.Host "kanban"

        while Host.step window do
            ()

        0
