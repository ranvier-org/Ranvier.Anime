(* OCaml driver for Ranvier.Anime differential conformance.
   Reads one S-expression program per line from stdin, runs the extracted Rocq pure model
   from [Engine], and prints [<index>\tok\t<canonical_observation>] per line. *)

open Engine

type sexp = Atom of string | Node of sexp Stdlib.List.t

let parse_sexp (text : string) : sexp =
  let n = Stdlib.String.length text in
  let i = ref 0 in
  let skip () =
    while !i < n && (text.[!i] = ' ' || text.[!i] = '\t' || text.[!i] = '\r') do
      incr i
    done
  in
  let rec one () =
    skip ();
    if text.[!i] = '(' then begin
      incr i;
      let items = ref [] in
      skip ();
      while text.[!i] <> ')' do
        items := one () :: !items;
        skip ()
      done;
      incr i;
      Node (Stdlib.List.rev !items)
    end else begin
      let start = !i in
      while !i < n && text.[!i] <> ' ' && text.[!i] <> '\t' && text.[!i] <> '\r'
            && text.[!i] <> ')' && text.[!i] <> '(' do
        incr i
      done;
      Atom (Stdlib.String.sub text start (!i - start))
    end
  in
  one ()

let rec nat_of_int (n : int) : nat = if n <= 0 then O else S (nat_of_int (n - 1))

let int_of_nat (n : nat) : int =
  let rec go acc = function O -> acc | S m -> go (acc + 1) m in
  go 0 n

let rec positive_of_int (n : int) : positive =
  if n = 1 then XH
  else if n land 1 = 1 then XI (positive_of_int (n lsr 1))
  else XO (positive_of_int (n lsr 1))

let rec int_of_positive (p : positive) : int =
  match p with
  | XH -> 1
  | XO q -> 2 * int_of_positive q
  | XI q -> (2 * int_of_positive q) + 1

let z_of_int (n : int) : z =
  if n = 0 then Z0 else if n > 0 then Zpos (positive_of_int n) else Zneg (positive_of_int (-n))

let int_of_z (v : z) : int =
  match v with
  | Z0 -> 0
  | Zpos p -> int_of_positive p
  | Zneg p -> -int_of_positive p

let bool_of_atom = function
  | Atom "1" | Atom "true" -> True
  | Atom "0" | Atom "false" -> False
  | _ -> failwith "expected bool"

let str_of_bool = function True -> "true" | False -> "false"

let atom_int = function
  | Atom a -> int_of_string a
  | Node _ -> failwith "expected int atom"

let rec coq_list_of_ocaml = function
  | [] -> Nil
  | x :: xs -> Cons (x, coq_list_of_ocaml xs)

let rec ocaml_list_of_coq = function
  | Nil -> []
  | Cons (x, xs) -> x :: ocaml_list_of_coq xs

let z_list_of_sexp = function
  | Node xs -> coq_list_of_ocaml (Stdlib.List.map (fun x -> z_of_int (atom_int x)) xs)
  | Atom _ -> failwith "expected z list"

let str_z_list xs =
  "[" ^ Stdlib.String.concat ";" (Stdlib.List.map (fun z -> string_of_int (int_of_z z)) (ocaml_list_of_coq xs)) ^ "]"

let str_nat_list xs =
  "[" ^ Stdlib.String.concat ";" (Stdlib.List.map (fun v -> string_of_int (int_of_nat v)) (ocaml_list_of_coq xs)) ^ "]"

let str_opt_z = function
  | None -> "none"
  | Some z -> "some:" ^ string_of_int (int_of_z z)

let str_opt_nat = function
  | None -> "none"
  | Some v -> "some:" ^ string_of_int (int_of_nat v)

let eval_program (s : sexp) : string =
  match s with
  | Node [Atom "timing"; iter_dur; iters; delay; loop_delay; alt; rev; raw_t; iter_idx; last_tick; now; frame_dur; Node prios] ->
    let coq_prios =
      coq_list_of_ocaml
        (Stdlib.List.map
          (function
            | Node [p; id] -> Pair (z_of_int (atom_int p), nat_of_int (atom_int id))
            | _ -> failwith "bad prio")
          prios)
    in
    let Pair (Pair (Pair (Pair (td, bd), dir), anc), ids) =
      observe_timing_engine
        (z_of_int (atom_int iter_dur))
        (z_of_int (atom_int iters))
        (z_of_int (atom_int delay))
        (z_of_int (atom_int loop_delay))
        (bool_of_atom alt)
        (bool_of_atom rev)
        (z_of_int (atom_int raw_t))
        (nat_of_int (atom_int iter_idx))
        (z_of_int (atom_int last_tick))
        (z_of_int (atom_int now))
        (z_of_int (atom_int frame_dur))
        coq_prios
    in
    Stdlib.Printf.sprintf "td=%d|bd=%d|dir=%s|anc=%d|ids=%s"
      (int_of_z td) (int_of_z bd) (str_of_bool dir) (int_of_z anc) (str_nat_list ids)

  | Node [Atom "stagger"; cols; rows; origin_idx; step; start_off; count] ->
    let Pair (offs, coords) =
      observe_stagger_grid
        (z_of_int (atom_int cols))
        (z_of_int (atom_int rows))
        (z_of_int (atom_int origin_idx))
        (z_of_int (atom_int step))
        (z_of_int (atom_int start_off))
        (nat_of_int (atom_int count))
    in
    let c_strs =
      Stdlib.List.map
        (fun (Pair (Pair (x, y), z)) ->
          Stdlib.Printf.sprintf "(%d,%d,%d)" (int_of_z x) (int_of_z y) (int_of_z z))
        (ocaml_list_of_coq coords)
    in
    Stdlib.Printf.sprintf "offs=%s|coords=[%s]" (str_z_list offs) (Stdlib.String.concat ";" c_strs)

  | Node [Atom "color"; half_nums; xs; ys; c0_zero; h0; h1; grad_hund] ->
    let Pair (Pair (Pair (rnd, Pair (hx, hy)), hm), deg) =
      observe_round_and_color
        (z_list_of_sexp half_nums)
        (z_list_of_sexp xs)
        (z_list_of_sexp ys)
        (bool_of_atom c0_zero)
        (z_of_int (atom_int h0))
        (z_of_int (atom_int h1))
        (z_of_int (atom_int grad_hund))
    in
    Stdlib.Printf.sprintf "rnd=%s|hx=%s|hy=%s|hm=%d|deg=%d"
      (str_z_list rnd) (str_z_list hx) (str_z_list hy) (int_of_z hm) (int_of_z deg)

  | Node [Atom "comp"; init_val; Node bts; host_epoch; clock_delay; tw_start; old_dur; new_dur; child_durs; remove_idx] ->
    let coq_bts =
      coq_list_of_ocaml
        (Stdlib.List.map
          (function
            | Node [m; t] -> Pair (bool_of_atom m, z_of_int (atom_int t))
            | _ -> failwith "bad bt")
          bts)
    in
    let Pair (Pair (bl, st), rem) =
      observe_composition_timeline
        (z_of_int (atom_int init_val))
        coq_bts
        (z_of_int (atom_int host_epoch))
        (z_of_int (atom_int clock_delay))
        (z_of_int (atom_int tw_start))
        (z_of_int (atom_int old_dur))
        (z_of_int (atom_int new_dur))
        (z_list_of_sexp child_durs)
        (nat_of_int (atom_int remove_idx))
    in
    Stdlib.Printf.sprintf "bl=%d|st=%s|rem=%d" (int_of_z bl) (str_opt_z st) (int_of_z rem)

  | Node [Atom "dragscroll"; lo; hi; fac; v; thresh; start_p; finish_p; positions] ->
    let Pair (Pair (Pair (r, inv), edge), Pair (ec, iv)) =
      observe_drag_scroll
        (z_of_int (atom_int lo))
        (z_of_int (atom_int hi))
        (z_of_int (atom_int fac))
        (z_of_int (atom_int v))
        (z_of_int (atom_int thresh))
        (z_of_int (atom_int start_p))
        (z_of_int (atom_int finish_p))
        (z_list_of_sexp positions)
    in
    Stdlib.Printf.sprintf "r=%d|inv=%d|edge=%d|ec=%d|iv=%s"
      (int_of_z r) (int_of_z inv) (int_of_z edge) (int_of_nat ec) (str_of_bool iv)

  | Node [Atom "motion"; clear_in; init_st; updates; Node ops] ->
    let coq_init =
      match init_st with
      | Atom "none" -> None
      | Node [Atom "some"; x] -> Some (z_of_int (atom_int x))
      | _ -> failwith "bad init_st"
    in
    let coq_ops =
      coq_list_of_ocaml
        (Stdlib.List.map
          (function
            | Atom "close" -> None
            | Node [Atom "play"; id] -> Some (nat_of_int (atom_int id))
            | _ -> failwith "bad slot op")
          ops)
    in
    let Pair (Pair (cnt, rest), Pair (cl, act)) =
      observe_transform_and_motion
        (bool_of_atom clear_in)
        coq_init
        (z_list_of_sexp updates)
        coq_ops
    in
    Stdlib.Printf.sprintf "cnt=%d|rest=%s|cl=%s|act=%s"
      (int_of_nat cnt) (str_opt_z rest) (str_of_bool cl) (str_opt_nat act)

  | _ -> failwith "unrecognized program"

let () =
  let idx = ref 0 in
  try
    while true do
      let line = input_line stdin in
      if Stdlib.String.length line > 0 then begin
        let obs = eval_program (parse_sexp line) in
        Stdlib.Printf.printf "%d\tok\t%s\n" !idx obs;
        incr idx
      end
    done
  with End_of_file -> ()
