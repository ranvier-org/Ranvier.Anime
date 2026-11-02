module Ranvier.Anime.Tests.Program

open Expecto

[<EntryPoint>]
let main args = runTestsWithCLIArgs [] args RunnerTests.tests
