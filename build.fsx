#load "build.defs.fsx"

open Partas.Build

exit (Command.invoke (Args.script()) BuildDefs.root).ExitCode
