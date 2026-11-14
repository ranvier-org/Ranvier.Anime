/// <summary>
/// A three-lane board run by an Elmish loop: cards enter and leave with presence, move between lanes with FLIP on the
/// board, and drop into a lane when dragged.
/// </summary>
module Kanban

open global.Ranvier
open Ranvier.Anime
open Ranvier.Dom
open Ranvier.Elmish
open Ranvier.View
open Ranvier.View.Dsl
open Ranvier.View.Anime

type Card = { Id: int; Title: string; Column: int }

type Model =
    {
        Cards: Card array
        NextId: int
        Seed: int
        /// <summary>Earlier card arrays, latest first, at most 32.</summary>
        History: Card array list
    }

type Msg =
    | Add of column: int
    | Delete of id: int
    | Move of id: int * column: int
    | Shuffle
    | Clear
    | Undo

let lanes = [| "Backlog"; "In progress"; "Shipped" |]

let private titles =
    [|
        "Sketch the board"
        "Wire the reducer"
        "Pick an easing"
        "Measure the bundle"
        "Hydrate the story"
        "Spring the puck"
        "Morph the chart"
        "Route the shell"
        "Review the seam"
        "Trim the layout"
        "Tune the stagger"
        "Ship the demo"
    |]

let private cardOf (id: int) (column: int) =
    {
        Id = id
        Title = titles[(id - 1) % titles.Length]
        Column = column
    }

let init =
    {
        Cards = [| cardOf 1 0; cardOf 2 0; cardOf 3 1; cardOf 4 1; cardOf 5 2 |]
        NextId = 6
        Seed = 7
        History = []
    }

let private commit (model: Model) (cards: Card array) =
    { model with
        Cards = cards
        History = List.truncate 32 (model.Cards :: model.History)
    }

let update (msg: Msg) (model: Model) : Model =
    match msg with
    | Add column ->
        { commit model (Array.append model.Cards [| cardOf model.NextId column |]) with
            NextId = model.NextId + 1
        }
    | Delete id when
        model.Cards
        |> Array.exists (fun card -> card.Id = id)
        ->
        commit
            model
            (model.Cards
             |> Array.filter (fun card -> card.Id <> id))
    | Move (id, column) ->
        match
            model.Cards
            |> Array.tryFind (fun card -> card.Id = id)
        with
        | Some card when
            card.Column <> column
            && column >= 0
            && column < lanes.Length
            ->
            let rest =
                model.Cards
                |> Array.filter (fun other -> other.Id <> id)

            commit model (Array.append rest [| { card with Column = column } |])
        | _ -> model
    | Shuffle when model.Cards.Length > 1 ->
        let cards = Array.copy model.Cards
        let mutable seed = model.Seed

        for index in cards.Length - 1 .. -1 .. 1 do
            seed <- Native.nextSeed seed
            let other = seed % (index + 1)
            let card = cards[index]
            cards[index] <- cards[other]
            cards[other] <- card

        let placed =
            cards
            |> Array.map (fun card ->
                seed <- Native.nextSeed seed

                { card with
                    Column = seed % lanes.Length
                })

        { commit model placed with Seed = seed }
    | Clear when model.Cards.Length > 0 -> commit model [||]
    | Undo ->
        match model.History with
        | previous :: rest ->
            { model with
                Cards = previous
                History = rest
            }
        | [] -> model
    | Delete _
    | Shuffle
    | Clear -> model

/// <summary>A card with its row in its lane, counted in board order.</summary>
type Slot = { Card: Card; Row: int }

let slots (cards: Card array) : Slot array =
    let counts = Array.zeroCreate lanes.Length

    cards
    |> Array.map (fun card ->
        let row = counts[card.Column]
        counts[card.Column] <- row + 1
        { Card = card; Row = row })

let counts (cards: Card array) : int array =
    Array.init lanes.Length (fun lane ->
        cards
        |> Array.filter (fun card -> card.Column = lane)
        |> Array.length)

/// <summary>The board's loop and the memos its view reads.</summary>
[<NoEquality; NoComparison>]
type Board =
    {
        Loop: Mvu<Model, Msg>
        Slots: Memo<Slot array>
        Counts: Memo<int array>
        Total: Memo<int>
        CanUndo: Memo<bool>
        /// <summary>The board element once placed, for the lane width a drop is measured against.</summary>
        Element: obj ref
    }

/// <summary>A board over a new loop starting at <c>start</c>, in the current graph's scope.</summary>
let createBoard (start: Model) : Board =
    let loop = Mvu.create start update

    {
        Loop = loop
        Slots = loop.Select (fun model -> slots model.Cards)
        Counts = loop.Select (fun model -> counts model.Cards)
        Total = loop.Select (fun model -> model.Cards.Length)
        CanUndo = loop.Select (fun model -> not model.History.IsEmpty)
        Element = ref null
    }

/// <summary>A card row's instance: its current slot, and the drag position and state it shares with its draggable.</summary>
[<NoEquality; NoComparison>]
type CardRow =
    {
        Current: unit -> Slot
        Position: Signal<struct (float * float)>
        Dragging: Signal<bool>
        /// <summary>The spring returning the card to its slot; a grab or the row's disposal cancels it.</summary>
        Spring: MotionSlot
        /// <summary>The card's element once placed, whose drop box starts its FLIP into another lane.</summary>
        Element: obj ref
    }

/// <summary>The lane a card dragged by <c>dx</c> pixels lands in, from lane <c>column</c> on lanes <c>width</c> wide.</summary>
let dropLane (column: int) (width: float) (dx: float) =
    if width <= 0. then
        column
    else
        max 0 (min (lanes.Length - 1) (column + int (System.Math.Round (dx / width))))

let private enterCard (_: CardRow) (element: Element) =
    Native.animate element (Native.timing 320. Native.outBack) [| Native.fromTo "opacity" 0. 1.; Native.fromTo "scale" 0.85 1. |]

let private leaveCard (_: CardRow) (element: Element) =
    Native.animate element (Native.timing 240. Native.inQuad) [| Native.toward "opacity" 0.; Native.toward "scale" 0.8 |]

// Returns the card to its slot along a spring, through the position signal the draggable follows.
let private springHome (graph: Graph) (row: CardRow) (offset: struct (float * float)) =
    row.Spring.Play (fun () ->
        Native.springPoint 260. 20. offset (fun point -> graph.Batch (fun () -> row.Position.Value <- point)))

let private cardRow (board: Board) =
    Component.template
        (fun _ (current: unit -> Slot) ->
            let graph = Graph.Current

            let row =
                {
                    Current = current
                    Position = Api.createSignal (struct (0., 0.))
                    Dragging = Api.createSignal false
                    Spring = Motion.slot ()
                    Element = ref null
                }

            Api.createEffectOn (fun () -> row.Dragging.Value) (fun dragging ->
                let struct (dx, dy) = Api.untrack (fun () -> row.Position.Value)

                if not dragging && (dx <> 0. || dy <> 0.) then
                    let slot = Api.untrack current

                    let width =
                        Native.widthOf board.Element.Value
                        / float lanes.Length

                    let lane = dropLane slot.Card.Column width dx

                    if lane = slot.Card.Column then
                        springHome graph row (struct (dx, dy))
                    else
                        Motion.flipFrom (unbox row.Element.Value)
                        row.Position.Value <- struct (0., 0.)
                        board.Loop.Dispatch (Move (slot.Card.Id, lane)))

            row)
        (li (
            li.className (fun (row: CardRow) ->
                if row.Dragging.Value then
                    "card-slot dragging"
                else
                    "card-slot"),
            li.data ("id", fun (row: CardRow) -> Some (string (row.Current ()).Card.Id)),
            li.data ("lane", fun (row: CardRow) -> Some (string (row.Current ()).Card.Column)),
            Motion.presence enterCard leaveCard,
            Motion.drag
                { NativeDraggableOptions.Default with
                    Cursor = false
                    Physics = fun () -> { DraggableSettings.Default with VelocityMultiplier = 0. } } (fun (row: CardRow) ->
                {
                    Position = row.Position
                    Dragging = ValueSome row.Dragging
                    Slot = ValueSome row.Spring
                }),
            li.mounted (fun (row: CardRow) element ->
                row.Element.Value <- box element
                fun () -> row.Element.Value <- null)
        ) {
            div (div.className "card") {
                div (div.className "card-top") {
                    span (span.className "card-id") { fun (row: CardRow) -> "#" + string (row.Current ()).Card.Id }

                    button (
                        button.className "icon delete",
                        button.title "Delete",
                        button.onClick (fun (row: CardRow) _ -> board.Loop.Dispatch (Delete (row.Current ()).Card.Id))
                    ) {
                        "×"
                    }
                }

                p (p.className "card-title") { fun (row: CardRow) -> (row.Current ()).Card.Title }

                div (div.className "card-moves") {
                    button (
                        button.className "icon left",
                        button.title "Move left",
                        button.disabled (fun (row: CardRow) -> (row.Current ()).Card.Column = 0),
                        button.onClick (fun (row: CardRow) _ ->
                            let card = (row.Current ()).Card
                            board.Loop.Dispatch (Move (card.Id, card.Column - 1)))
                    ) {
                        "←"
                    }

                    button (
                        button.className "icon right",
                        button.title "Move right",
                        button.disabled (fun (row: CardRow) -> (row.Current ()).Card.Column = lanes.Length - 1),
                        button.onClick (fun (row: CardRow) _ ->
                            let card = (row.Current ()).Card
                            board.Loop.Dispatch (Move (card.Id, card.Column + 1)))
                    ) {
                        "→"
                    }
                }
            }
        })

/// <summary>The board's component over <c>board</c>; build it once per board.</summary>
let view (board: Board) : Component<unit> =
    let row = cardRow board

    Component.template
        (fun _ () -> board)
        (section (section.className "kanban") {
            header (header.className "toolbar") {
                button (button.className "primary add", button.onClick (fun (b: Board) _ -> b.Loop.Dispatch (Add 0))) { "Add card" }
                button (button.className "shuffle", button.onClick (fun (b: Board) _ -> b.Loop.Dispatch Shuffle)) { "Shuffle" }
                button (button.className "clear", button.onClick (fun (b: Board) _ -> b.Loop.Dispatch Clear)) { "Clear" }

                button (
                    button.className "undo",
                    button.disabled (fun (b: Board) -> not b.CanUndo.Value),
                    button.onClick (fun (b: Board) _ -> b.Loop.Dispatch Undo)
                ) {
                    "Undo"
                }

                span (span.className "meta") { fun (b: Board) -> sprintf "%d cards" b.Total.Value }
            }

            div (div.className "board-frame") {
                div (div.className "lanes") {
                    for lane in 0 .. lanes.Length - 1 do
                        div (div.className "lane") {
                            div (div.className "lane-head") {
                                h2 () { lanes[lane] }
                                span (span.className "count") { fun (b: Board) -> string b.Counts.Value[lane] }

                                button (
                                    button.className "icon add-to",
                                    button.title ("Add to " + lanes[lane]),
                                    button.onClick (fun (b: Board) _ -> b.Loop.Dispatch (Add lane))
                                ) {
                                    "+"
                                }
                            }
                        }
                }

                ul (
                    ul.className "board",
                    Motion.flip { LayoutOptions.Default.WithDuration 420u with Ease = Native.outQuart },
                    ul.mounted (fun (b: Board) element ->
                        b.Element.Value <- box element
                        fun () -> b.Element.Value <- null)
                ) {
                    each (fun (slot: Slot) -> slot.Card.Id) (fun (b: Board) -> b.Slots.Value) row
                }

                show (fun (b: Board) -> b.Total.Value = 0) (p (p.className "empty", Motion.fade) { "The board is clear. Add a card, or undo." })
            }
        })

