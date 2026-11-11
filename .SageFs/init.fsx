#load "build.defs.fsx"

open System

let build (args: string list) = BuildDefs.root.Invoke(args, output = Console.Out)
