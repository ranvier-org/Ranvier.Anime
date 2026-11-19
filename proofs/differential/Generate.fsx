#load "Harness.fsx"

open System
open System.IO
open System.Text
open Harness

let args = fsi.CommandLineArgs[1..]
let count = if args.Length >= 1 then int args[0] else 400
let seed = if args.Length >= 2 then int args[1] else 1
let workDir = if args.Length >= 3 then args[2] else "."

let rng = Random(seed)
let nextInt lo hi = rng.Next(lo, hi + 1)
let nextBool () = rng.Next(0, 2) = 1

let fmtIntList (xs: int list) =
    "(" + (xs |> List.map string |> String.concat " ") + ")"

let showIntList (xs: int list) =
    "[" + (xs |> List.map string |> String.concat ";") + "]"

let showOptInt (o: int option) =
    match o with
    | None -> "none"
    | Some v -> $"some:{v}"

let showBool (b: bool) = if b then "true" else "false"

let progSb = StringBuilder()
let fsSb = StringBuilder()

for idx in 0 .. count - 1 do
    match idx % 6 with
    | 0 ->
        let iterDur = nextInt 0 250
        let iters = nextInt 0 5
        let delay = nextInt -30 80
        let loopDelay = nextInt 0 40
        let alt = nextBool ()
        let rev = nextBool ()
        let rawT = nextInt -50 900
        let iterIdx = nextInt 0 5
        let lastTick = nextInt 0 500
        let now = lastTick + nextInt 0 120
        let frameDur = [| 0; 8; 10; 16; 20; 25; 50 |][rng.Next(0, 7)]
        let prioLen = nextInt 0 6
        let prios = [ for i in 1 .. prioLen -> nextInt -10 10, i ]
        let prioSexp =
            "(" + (prios |> List.map (fun (p, id) -> $"({p} {id})") |> String.concat " ") + ")"
        progSb.Append($"(timing {iterDur} {iters} {delay} {loopDelay} {showBool alt} {showBool rev} {rawT} {iterIdx} {lastTick} {now} {frameDur} {prioSexp})\n") |> ignore
        let td, bd, dir, anc, ids =
            runTimingEngine iterDur iters delay loopDelay alt rev rawT iterIdx lastTick now frameDur prios
        fsSb.Append($"{idx}\tok\ttd={td}|bd={bd}|dir={showBool dir}|anc={anc}|ids={showIntList ids}\n") |> ignore

    | 1 ->
        let cols = nextInt 1 6
        let rows = nextInt 1 5
        let countT = nextInt 0 35
        let originIdx = nextInt 0 (max 0 (countT - 1))
        let step = nextInt -25 25
        let startOff = nextInt -50 100
        progSb.Append($"(stagger {cols} {rows} {originIdx} {step} {startOff} {countT})\n") |> ignore
        let offs, coords = runStaggerGrid cols rows originIdx step startOff countT
        let cStr = coords |> List.map (fun (x, y, z) -> $"({x},{y},{z})") |> String.concat ";"
        fsSb.Append($"{idx}\tok\toffs={showIntList offs}|coords=[{cStr}]\n") |> ignore

    | 2 ->
        let halfLen = nextInt 1 8
        let halfNums = [ for _ in 1 .. halfLen -> nextInt -25 25 ]
        let xLen = nextInt 1 5
        let yLen = nextInt 1 5
        let xs = [ for _ in 1 .. xLen -> nextInt -50 50 ]
        let ys = [ for _ in 1 .. yLen -> nextInt -50 50 ]
        let c0Zero = nextBool ()
        let h0 = nextInt 0 359
        let mutable h1 = nextInt 0 359
        // Avoid exact 180-degree antipode where floating Lab roundoff of 1e-14 can flip -180 vs +180
        if abs (((h1 - h0 + 540) % 360) - 180) <= 1 then
            h1 <- (h1 + 17) % 360
        let gradHund = nextInt -400 800
        progSb.Append($"(color {fmtIntList halfNums} {fmtIntList xs} {fmtIntList ys} {showBool c0Zero} {h0} {h1} {gradHund})\n") |> ignore
        let rnd, (hx, hy), hm, deg = runRoundAndColor halfNums xs ys c0Zero h0 h1 gradHund
        fsSb.Append($"{idx}\tok\trnd={showIntList rnd}|hx={showIntList hx}|hy={showIntList hy}|hm={hm}|deg={deg}\n") |> ignore

    | 3 ->
        let initVal = nextInt -50 50
        let btLen = nextInt 0 4
        let bts = [ for _ in 1 .. btLen -> nextBool (), nextInt -200 300 ]
        let btSexp = "(" + (bts |> List.map (fun (m, t) -> $"({showBool m} {t})") |> String.concat " ") + ")"
        let hostEpoch = [| 0; 1000; 5000; 12000 |][rng.Next(0, 4)]
        let oldDur = [| 50; 100; 200 |][rng.Next(0, 3)]
        let mult = [| -1; 0; 1; 2; 3; 4 |][rng.Next(0, 6)]
        let newDur = if mult < 0 then -10 else oldDur * mult
        let clockDelay = nextInt 0 5 * 10
        let twStart = nextInt 0 4 * 10
        let childLen = nextInt 1 5
        let childDurs = [ for _ in 1 .. childLen -> nextInt 1 10 * 50 ]
        let remIdx = nextInt 0 childLen
        progSb.Append($"(comp {initVal} {btSexp} {hostEpoch} {clockDelay} {twStart} {oldDur} {newDur} {fmtIntList childDurs} {remIdx})\n") |> ignore
        let bl, st, rem =
            runCompositionTimeline initVal bts hostEpoch clockDelay twStart oldDur newDur childDurs remIdx
        fsSb.Append($"{idx}\tok\tbl={bl}|st={showOptInt st}|rem={rem}\n") |> ignore

    | 4 ->
        let lo = nextInt -100 50
        let hi = lo + nextInt 20 200
        let fac = nextInt 1 4
        // Use v such that (v - lo) and (v - hi) are exact integers (resist multiplies by fac, inverse divides by fac)
        let v = nextInt (lo - 80) (hi + 80)
        let thresh = [| -10; 0; 20; 50 |][rng.Next(0, 4)]
        let startP = nextInt 50 300
        let finishP = startP + nextInt 50 300
        let posLen = nextInt 1 6
        let positions =
            [ for _ in 1 .. posLen ->
                match rng.Next(0, 4) with
                | 0 -> startP
                | 1 -> finishP
                | 2 -> nextInt (startP + 1) (finishP - 1)
                | _ -> if nextBool () then startP - nextInt 1 50 else finishP + nextInt 1 50 ]
        progSb.Append($"(dragscroll {lo} {hi} {fac} {v} {thresh} {startP} {finishP} {fmtIntList positions})\n") |> ignore
        let r, inv, edge, (ec, iv) = runDragScroll lo hi fac v thresh startP finishP positions
        fsSb.Append($"{idx}\tok\tr={r}|inv={inv}|edge={edge}|ec={ec}|iv={showBool iv}\n") |> ignore

    | _ ->
        let clearIn = nextBool ()
        let initSt = if nextBool () then None else Some(nextInt 0 100)
        let initSexp =
            match initSt with
            | None -> "none"
            | Some x -> $"(some {x})"
        let upLen = nextInt 0 5
        let updates = [ for _ in 1 .. upLen -> nextInt 0 100 ]
        let opLen = nextInt 0 6
        let ops =
            [ for step in 1 .. opLen ->
                if rng.Next(0, 4) = 0 then None else Some step ]
        let opsSexp =
            "("
            + (ops
               |> List.map (function
                   | None -> "close"
                   | Some id -> $"(play {id})")
               |> String.concat " ")
            + ")"
        progSb.Append($"(motion {showBool clearIn} {initSexp} {fmtIntList updates} {opsSexp})\n") |> ignore
        let cnt, rest, (cl, act) = runTransformAndMotion clearIn initSt updates ops
        fsSb.Append($"{idx}\tok\tcnt={cnt}|rest={showOptInt rest}|cl={showBool cl}|act={showOptInt act}\n") |> ignore

File.WriteAllText(Path.Combine(workDir, "programs.txt"), progSb.ToString(), UTF8Encoding(false))
File.WriteAllText(Path.Combine(workDir, "fsharp.txt"), fsSb.ToString(), UTF8Encoding(false))
printfn "Generated and ran %d F# differential programs (seed %d)" count seed
