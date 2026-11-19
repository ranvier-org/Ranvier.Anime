
type bool =
| True
| False

(** val xorb : bool -> bool -> bool **)

let xorb b1 b2 =
  match b1 with
  | True -> (match b2 with
             | True -> False
             | False -> True)
  | False -> b2

(** val negb : bool -> bool **)

let negb = function
| True -> False
| False -> True

type nat =
| O
| S of nat

type 'a option =
| Some of 'a
| None

type ('a, 'b) prod =
| Pair of 'a * 'b

(** val snd : ('a1, 'a2) prod -> 'a2 **)

let snd = function
| Pair (_, y) -> y

type 'a list =
| Nil
| Cons of 'a * 'a list

(** val length : 'a1 list -> nat **)

let rec length = function
| Nil -> O
| Cons (_, l') -> S (length l')

(** val app : 'a1 list -> 'a1 list -> 'a1 list **)

let rec app l m =
  match l with
  | Nil -> m
  | Cons (a, l1) -> Cons (a, (app l1 m))

type comparison =
| Eq
| Lt
| Gt

(** val compOpp : comparison -> comparison **)

let compOpp = function
| Eq -> Eq
| Lt -> Gt
| Gt -> Lt

(** val sub : nat -> nat -> nat **)

let rec sub n m =
  match n with
  | O -> n
  | S k -> (match m with
            | O -> n
            | S l -> sub k l)

module Nat =
 struct
  (** val eqb : nat -> nat -> bool **)

  let rec eqb n m =
    match n with
    | O -> (match m with
            | O -> True
            | S _ -> False)
    | S n' -> (match m with
               | O -> False
               | S m' -> eqb n' m')

  (** val leb : nat -> nat -> bool **)

  let rec leb n m =
    match n with
    | O -> True
    | S n' -> (match m with
               | O -> False
               | S m' -> leb n' m')

  (** val max : nat -> nat -> nat **)

  let rec max n m =
    match n with
    | O -> m
    | S n' -> (match m with
               | O -> n
               | S m' -> S (max n' m'))

  (** val min : nat -> nat -> nat **)

  let rec min n m =
    match n with
    | O -> O
    | S n' -> (match m with
               | O -> O
               | S m' -> S (min n' m'))

  (** val even : nat -> bool **)

  let rec even = function
  | O -> True
  | S n0 -> (match n0 with
             | O -> False
             | S n' -> even n')

  (** val odd : nat -> bool **)

  let odd n =
    negb (even n)
 end

(** val map : ('a1 -> 'a2) -> 'a1 list -> 'a2 list **)

let rec map f = function
| Nil -> Nil
| Cons (a, l0) -> Cons ((f a), (map f l0))

(** val seq : nat -> nat -> nat list **)

let rec seq start = function
| O -> Nil
| S len0 -> Cons (start, (seq (S start) len0))

(** val filter : ('a1 -> bool) -> 'a1 list -> 'a1 list **)

let rec filter f = function
| Nil -> Nil
| Cons (x, l0) ->
  (match f x with
   | True -> Cons (x, (filter f l0))
   | False -> filter f l0)

type positive =
| XI of positive
| XO of positive
| XH

type z =
| Z0
| Zpos of positive
| Zneg of positive

module Pos =
 struct
  (** val succ : positive -> positive **)

  let rec succ = function
  | XI p -> XO (succ p)
  | XO p -> XI p
  | XH -> XO XH

  (** val add : positive -> positive -> positive **)

  let rec add x y =
    match x with
    | XI p ->
      (match y with
       | XI q -> XO (add_carry p q)
       | XO q -> XI (add p q)
       | XH -> XO (succ p))
    | XO p ->
      (match y with
       | XI q -> XI (add p q)
       | XO q -> XO (add p q)
       | XH -> XI p)
    | XH -> (match y with
             | XI q -> XO (succ q)
             | XO q -> XI q
             | XH -> XO XH)

  (** val add_carry : positive -> positive -> positive **)

  and add_carry x y =
    match x with
    | XI p ->
      (match y with
       | XI q -> XI (add_carry p q)
       | XO q -> XO (add_carry p q)
       | XH -> XI (succ p))
    | XO p ->
      (match y with
       | XI q -> XO (add_carry p q)
       | XO q -> XI (add p q)
       | XH -> XO (succ p))
    | XH ->
      (match y with
       | XI q -> XI (succ q)
       | XO q -> XO (succ q)
       | XH -> XI XH)

  (** val pred_double : positive -> positive **)

  let rec pred_double = function
  | XI p -> XI (XO p)
  | XO p -> XI (pred_double p)
  | XH -> XH

  (** val mul : positive -> positive -> positive **)

  let rec mul x y =
    match x with
    | XI p -> add y (XO (mul p y))
    | XO p -> XO (mul p y)
    | XH -> y

  (** val compare_cont : comparison -> positive -> positive -> comparison **)

  let rec compare_cont r x y =
    match x with
    | XI p ->
      (match y with
       | XI q -> compare_cont r p q
       | XO q -> compare_cont Gt p q
       | XH -> Gt)
    | XO p ->
      (match y with
       | XI q -> compare_cont Lt p q
       | XO q -> compare_cont r p q
       | XH -> Gt)
    | XH -> (match y with
             | XH -> r
             | _ -> Lt)

  (** val compare : positive -> positive -> comparison **)

  let compare =
    compare_cont Eq

  (** val eqb : positive -> positive -> bool **)

  let rec eqb p q =
    match p with
    | XI p0 -> (match q with
                | XI q0 -> eqb p0 q0
                | _ -> False)
    | XO p0 -> (match q with
                | XO q0 -> eqb p0 q0
                | _ -> False)
    | XH -> (match q with
             | XH -> True
             | _ -> False)

  (** val of_succ_nat : nat -> positive **)

  let rec of_succ_nat = function
  | O -> XH
  | S x -> succ (of_succ_nat x)
 end

module Z =
 struct
  (** val double : z -> z **)

  let double = function
  | Z0 -> Z0
  | Zpos p -> Zpos (XO p)
  | Zneg p -> Zneg (XO p)

  (** val succ_double : z -> z **)

  let succ_double = function
  | Z0 -> Zpos XH
  | Zpos p -> Zpos (XI p)
  | Zneg p -> Zneg (Pos.pred_double p)

  (** val pred_double : z -> z **)

  let pred_double = function
  | Z0 -> Zneg XH
  | Zpos p -> Zpos (Pos.pred_double p)
  | Zneg p -> Zneg (XI p)

  (** val pos_sub : positive -> positive -> z **)

  let rec pos_sub x y =
    match x with
    | XI p ->
      (match y with
       | XI q -> double (pos_sub p q)
       | XO q -> succ_double (pos_sub p q)
       | XH -> Zpos (XO p))
    | XO p ->
      (match y with
       | XI q -> pred_double (pos_sub p q)
       | XO q -> double (pos_sub p q)
       | XH -> Zpos (Pos.pred_double p))
    | XH ->
      (match y with
       | XI q -> Zneg (XO q)
       | XO q -> Zneg (Pos.pred_double q)
       | XH -> Z0)

  (** val add : z -> z -> z **)

  let add x y =
    match x with
    | Z0 -> y
    | Zpos x' ->
      (match y with
       | Z0 -> x
       | Zpos y' -> Zpos (Pos.add x' y')
       | Zneg y' -> pos_sub x' y')
    | Zneg x' ->
      (match y with
       | Z0 -> x
       | Zpos y' -> pos_sub y' x'
       | Zneg y' -> Zneg (Pos.add x' y'))

  (** val opp : z -> z **)

  let opp = function
  | Z0 -> Z0
  | Zpos x0 -> Zneg x0
  | Zneg x0 -> Zpos x0

  (** val sub : z -> z -> z **)

  let sub m n =
    add m (opp n)

  (** val mul : z -> z -> z **)

  let mul x y =
    match x with
    | Z0 -> Z0
    | Zpos x' ->
      (match y with
       | Z0 -> Z0
       | Zpos y' -> Zpos (Pos.mul x' y')
       | Zneg y' -> Zneg (Pos.mul x' y'))
    | Zneg x' ->
      (match y with
       | Z0 -> Z0
       | Zpos y' -> Zneg (Pos.mul x' y')
       | Zneg y' -> Zpos (Pos.mul x' y'))

  (** val compare : z -> z -> comparison **)

  let compare x y =
    match x with
    | Z0 -> (match y with
             | Z0 -> Eq
             | Zpos _ -> Lt
             | Zneg _ -> Gt)
    | Zpos x' -> (match y with
                  | Zpos y' -> Pos.compare x' y'
                  | _ -> Gt)
    | Zneg x' ->
      (match y with
       | Zneg y' -> compOpp (Pos.compare x' y')
       | _ -> Lt)

  (** val leb : z -> z -> bool **)

  let leb x y =
    match compare x y with
    | Gt -> False
    | _ -> True

  (** val ltb : z -> z -> bool **)

  let ltb x y =
    match compare x y with
    | Lt -> True
    | _ -> False

  (** val eqb : z -> z -> bool **)

  let eqb x y =
    match x with
    | Z0 -> (match y with
             | Z0 -> True
             | _ -> False)
    | Zpos p -> (match y with
                 | Zpos q -> Pos.eqb p q
                 | _ -> False)
    | Zneg p -> (match y with
                 | Zneg q -> Pos.eqb p q
                 | _ -> False)

  (** val max : z -> z -> z **)

  let max n m =
    match compare n m with
    | Lt -> m
    | _ -> n

  (** val min : z -> z -> z **)

  let min n m =
    match compare n m with
    | Gt -> m
    | _ -> n

  (** val of_nat : nat -> z **)

  let of_nat = function
  | O -> Z0
  | S n0 -> Zpos (Pos.of_succ_nat n0)

  (** val pos_div_eucl : positive -> z -> (z, z) prod **)

  let rec pos_div_eucl a b =
    match a with
    | XI a' ->
      let Pair (q, r) = pos_div_eucl a' b in
      let r' = add (mul (Zpos (XO XH)) r) (Zpos XH) in
      (match ltb r' b with
       | True -> Pair ((mul (Zpos (XO XH)) q), r')
       | False -> Pair ((add (mul (Zpos (XO XH)) q) (Zpos XH)), (sub r' b)))
    | XO a' ->
      let Pair (q, r) = pos_div_eucl a' b in
      let r' = mul (Zpos (XO XH)) r in
      (match ltb r' b with
       | True -> Pair ((mul (Zpos (XO XH)) q), r')
       | False -> Pair ((add (mul (Zpos (XO XH)) q) (Zpos XH)), (sub r' b)))
    | XH ->
      (match leb (Zpos (XO XH)) b with
       | True -> Pair (Z0, (Zpos XH))
       | False -> Pair ((Zpos XH), Z0))

  (** val div_eucl : z -> z -> (z, z) prod **)

  let div_eucl a b =
    match a with
    | Z0 -> Pair (Z0, Z0)
    | Zpos a' ->
      (match b with
       | Z0 -> Pair (Z0, a)
       | Zpos _ -> pos_div_eucl a' b
       | Zneg b' ->
         let Pair (q, r) = pos_div_eucl a' (Zpos b') in
         (match r with
          | Z0 -> Pair ((opp q), Z0)
          | _ -> Pair ((opp (add q (Zpos XH))), (add b r))))
    | Zneg a' ->
      (match b with
       | Z0 -> Pair (Z0, a)
       | Zpos _ ->
         let Pair (q, r) = pos_div_eucl a' b in
         (match r with
          | Z0 -> Pair ((opp q), Z0)
          | _ -> Pair ((opp (add q (Zpos XH))), (sub b r)))
       | Zneg b' ->
         let Pair (q, r) = pos_div_eucl a' (Zpos b') in Pair (q, (opp r)))

  (** val div : z -> z -> z **)

  let div a b =
    let Pair (q, _) = div_eucl a b in q

  (** val modulo : z -> z -> z **)

  let modulo a b =
    let Pair (_, r) = div_eucl a b in r

  (** val gtb : z -> z -> bool **)

  let gtb x y =
    match compare x y with
    | Gt -> True
    | _ -> False

  (** val abs : z -> z **)

  let abs = function
  | Zneg p -> Zpos p
  | x -> x
 end

(** val clamp_z : z -> z -> z -> z **)

let clamp_z lo hi x =
  Z.max lo (Z.min hi x)

type timing_cfg = { tc_iter_dur : z; tc_iters : z; tc_delay : z;
                    tc_loop_delay : z; tc_alternate : bool; tc_reversed : 
                    bool }

(** val total_duration : timing_cfg -> z **)

let total_duration cfg =
  match Z.leb cfg.tc_iters Z0 with
  | True -> Z0
  | False ->
    let d = Z.max Z0 cfg.tc_iter_dur in
    let ld = Z.max Z0 cfg.tc_loop_delay in
    Z.add (Z.mul cfg.tc_iters d) (Z.mul (Z.sub cfg.tc_iters (Zpos XH)) ld)

(** val sample_bounded_time : timing_cfg -> z -> z **)

let sample_bounded_time cfg raw_t =
  clamp_z Z0 (total_duration cfg) (Z.sub raw_t cfg.tc_delay)

(** val sample_direction : bool -> bool -> nat -> bool **)

let sample_direction reversed alternate iter_idx =
  xorb reversed
    (match alternate with
     | True -> Nat.odd iter_idx
     | False -> False)

(** val advance_clock_anchor : z -> z -> z -> z **)

let advance_clock_anchor last_tick now frame_dur =
  let elapsed = Z.sub now last_tick in
  (match Z.ltb elapsed frame_dur with
   | True -> last_tick
   | False -> Z.sub now (Z.modulo elapsed frame_dur))

(** val insert_priority :
    z -> nat -> (z, nat) prod list -> (z, nat) prod list **)

let rec insert_priority p id q = match q with
| Nil -> Cons ((Pair (p, id)), Nil)
| Cons (p0, rest) ->
  let Pair (qp, qid) = p0 in
  (match Z.leb qp p with
   | True -> Cons ((Pair (qp, qid)), (insert_priority p id rest))
   | False -> Cons ((Pair (p, id)), q))

(** val grid3_decode : z -> z -> z -> ((z, z) prod, z) prod **)

let grid3_decode cols rows idx =
  let x = Z.modulo idx cols in
  let y = Z.modulo (Z.div idx cols) rows in
  let z0 = Z.div idx (Z.mul cols rows) in Pair ((Pair (x, y)), z0)

(** val flat_step_scalar : z -> z -> z -> z -> z **)

let flat_step_scalar origin step start_off i =
  Z.add start_off (Z.mul step (Z.abs (Z.sub origin i)))

(** val flat_step_simd_chunks :
    nat -> nat -> z -> nat -> z -> z -> z -> z list **)

let rec flat_step_simd_chunks fuel chunk base rem origin step start_off =
  match fuel with
  | O ->
    (match rem with
     | O -> Nil
     | S _ ->
       map (fun k ->
         flat_step_scalar origin step start_off (Z.add base (Z.of_nat k)))
         (seq O rem))
  | S f ->
    (match rem with
     | O -> Nil
     | S _ ->
       (match Nat.leb chunk rem with
        | True ->
          let lane_vals =
            map (fun k ->
              Z.add start_off
                (Z.mul step (Z.abs (Z.sub origin (Z.add base (Z.of_nat k))))))
              (seq O chunk)
          in
          app lane_vals
            (flat_step_simd_chunks f chunk (Z.add base (Z.of_nat chunk))
              (sub rem chunk) origin step start_off)
        | False ->
          map (fun k ->
            flat_step_scalar origin step start_off (Z.add base (Z.of_nat k)))
            (seq O rem)))

(** val js_round_half_int : z -> z **)

let js_round_half_int num =
  Z.div (Z.add num (Zpos XH)) (Zpos (XO XH))

(** val pad_zeros : nat -> z list -> z list **)

let rec pad_zeros target_len xs =
  match target_len with
  | O -> Nil
  | S n ->
    (match xs with
     | Nil -> Cons (Z0, (pad_zeros n Nil))
     | Cons (x, rest) -> Cons (x, (pad_zeros n rest)))

(** val homogenize_components : z list -> z list -> (z list, z list) prod **)

let homogenize_components xs ys =
  let n = Nat.max (length xs) (length ys) in
  Pair ((pad_zeros n xs), (pad_zeros n ys))

(** val hcl_shortest_delta : z -> z -> z **)

let hcl_shortest_delta h0 h1 =
  Z.sub
    (Z.modulo
      (Z.add (Z.sub h1 h0) (Zpos (XO (XO (XI (XI (XI (XO (XO (XO (XO
        XH)))))))))))
      (Zpos (XO (XO (XO (XI (XO (XI (XI (XO XH))))))))))
    (Zpos (XO (XO (XI (XO (XI (XI (XO XH))))))))

(** val max_child_end : ((nat, z) prod, z) prod list -> z **)

let rec max_child_end = function
| Nil -> Z0
| Cons (p, rest) ->
  let Pair (p0, dur) = p in
  let Pair (_, off) = p0 in Z.max (Z.add off dur) (max_child_end rest)

(** val timeline_remove_entry :
    nat -> ((nat, z) prod, z) prod list -> (z, ((nat, z) prod, z) prod list)
    prod **)

let timeline_remove_entry target_id entries =
  let remaining =
    filter (fun pat ->
      let Pair (y, _) = pat in
      let Pair (id, _) = y in negb (Nat.eqb id target_id)) entries
  in
  Pair ((max_child_end remaining), remaining)

type tween_timing = { tt_host_epoch : z; tt_clock_delay : z; tt_start : 
                      z; tt_dur : z; tt_abs_start : z }

(** val stretch_top_level_tween :
    z -> z -> tween_timing -> tween_timing option **)

let stretch_top_level_tween new_dur old_dur tw =
  match match Z.ltb new_dur Z0 with
        | True -> True
        | False -> Z.leb old_dur Z0 with
  | True -> None
  | False ->
    let delay' = Z.div (Z.mul tw.tt_clock_delay new_dur) old_dur in
    let start' = Z.div (Z.mul tw.tt_start new_dur) old_dur in
    let dur' = Z.max (Zpos XH) (Z.div (Z.mul tw.tt_dur new_dur) old_dur) in
    Some { tt_host_epoch = tw.tt_host_epoch; tt_clock_delay = delay';
    tt_start = start'; tt_dur = dur'; tt_abs_start =
    (Z.add (Z.add tw.tt_host_epoch delay') start') }

(** val assoc_set : nat -> z -> (nat, z) prod list -> (nat, z) prod list **)

let rec assoc_set k v = function
| Nil -> Cons ((Pair (k, v)), Nil)
| Cons (p, rest) ->
  let Pair (k', v') = p in
  (match Nat.eqb k k' with
   | True -> Cons ((Pair (k, v)), rest)
   | False -> Cons ((Pair (k', v')), (assoc_set k v rest)))

(** val assoc_remove : nat -> (nat, z) prod list -> (nat, z) prod list **)

let rec assoc_remove k = function
| Nil -> Nil
| Cons (p, rest) ->
  let Pair (k', v') = p in
  (match Nat.eqb k k' with
   | True -> assoc_remove k rest
   | False -> Cons ((Pair (k', v')), (assoc_remove k rest)))

(** val apply_originals :
    (nat, z option) prod list -> (nat, z) prod list -> (nat, z) prod list **)

let rec apply_originals origs cache =
  match origs with
  | Nil -> cache
  | Cons (p, rest) ->
    let Pair (k, o) = p in
    (match o with
     | Some v -> apply_originals rest (assoc_set k v cache)
     | None -> apply_originals rest (assoc_remove k cache))

(** val revert_transform_components :
    (nat, z) prod list option -> (nat, z) prod list -> (nat, z option) prod
    list -> (nat, z) prod list **)

let revert_transform_components live_inline _ origs =
  let base_cache = match live_inline with
                   | Some parsed -> parsed
                   | None -> Nil
  in
  apply_originals origs base_cache

(** val resist_exact : z -> z -> z -> z -> z **)

let resist_exact lo hi factor v =
  match Z.ltb v lo with
  | True -> Z.add lo (Z.mul (Z.sub v lo) factor)
  | False ->
    (match Z.gtb v hi with
     | True -> Z.add hi (Z.mul (Z.sub v hi) factor)
     | False -> v)

(** val grab_inverse_exact : z -> z -> z -> z -> z **)

let grab_inverse_exact lo hi factor r =
  match Z.ltb r lo with
  | True -> Z.add lo (Z.div (Z.sub r lo) factor)
  | False ->
    (match Z.gtb r hi with
     | True -> Z.add hi (Z.div (Z.sub r hi) factor)
     | False -> r)

(** val edge_scroll_safe : z -> z -> z -> z -> z -> z -> z -> z **)

let edge_scroll_safe min_b max_b pad_lo pad_hi thresh speed v =
  match Z.leb thresh Z0 with
  | True -> Z0
  | False ->
    let neg =
      Z.mul (Z.min Z0 (Z.max (Z.opp thresh) (Z.add (Z.sub v min_b) pad_lo)))
        speed
    in
    let pos =
      Z.mul (Z.max Z0 (Z.min thresh (Z.sub (Z.sub v max_b) pad_hi))) speed
    in
    (match negb (Z.eqb neg Z0) with
     | True -> neg
     | False -> pos)

type scroll_state = { ss_in_view : bool; ss_has_entered : bool;
                      ss_force_enter : bool; ss_enter_count : nat }

(** val scroll_step : z -> z -> z -> scroll_state -> scroll_state **)

let scroll_step start_p finish_p pos st =
  let inside =
    match Z.ltb start_p pos with
    | True -> Z.ltb pos finish_p
    | False -> False
  in
  let forced =
    match negb st.ss_has_entered with
    | True ->
      (match Z.eqb pos start_p with
       | True -> True
       | False -> Z.eqb pos finish_p)
    | False -> False
  in
  (match match match inside with
               | True -> negb st.ss_in_view
               | False -> False with
         | True -> True
         | False ->
           (match forced with
            | True ->
              (match negb st.ss_force_enter with
               | True -> negb st.ss_has_entered
               | False -> False)
            | False -> False) with
   | True ->
     let in_view' = match inside with
                    | True -> True
                    | False -> st.ss_in_view in
     (match match negb st.ss_force_enter with
            | True -> True
            | False -> negb st.ss_has_entered with
      | True ->
        { ss_in_view = in_view'; ss_has_entered = True; ss_force_enter =
          (match forced with
           | True -> True
           | False -> st.ss_force_enter);
          ss_enter_count = (S st.ss_enter_count) }
      | False ->
        { ss_in_view = in_view'; ss_has_entered = st.ss_has_entered;
          ss_force_enter =
          (match inside with
           | True -> False
           | False -> st.ss_force_enter);
          ss_enter_count = st.ss_enter_count })
   | False ->
     (match match negb inside with
            | True -> st.ss_in_view
            | False -> False with
      | True ->
        { ss_in_view = False; ss_has_entered = st.ss_has_entered;
          ss_force_enter = st.ss_force_enter; ss_enter_count =
          st.ss_enter_count }
      | False -> st))

(** val run_animate_on_updates :
    z option -> z list -> (z option, z option list) prod **)

let rec run_animate_on_updates init_style = function
| Nil -> Pair (init_style, Nil)
| Cons (v, rest) ->
  let Pair (final_style, later_snaps) = run_animate_on_updates (Some v) rest
  in
  Pair (final_style, (Cons (init_style, later_snaps)))

(** val revert_snapshots_reverse : z option -> z option list -> z option **)

let rec revert_snapshots_reverse cur = function
| Nil -> cur
| Cons (snap, _) -> snap

type motion_slot = { ms_closed : bool; ms_running : nat option;
                     ms_cancelled : nat list }

(** val slot_play : nat -> motion_slot -> motion_slot **)

let slot_play next_id s =
  match s.ms_closed with
  | True ->
    { ms_closed = True; ms_running = None; ms_cancelled = (Cons (next_id,
      s.ms_cancelled)) }
  | False ->
    let cancelled' =
      match s.ms_running with
      | Some prev -> Cons (prev, s.ms_cancelled)
      | None -> s.ms_cancelled
    in
    { ms_closed = False; ms_running = (Some next_id); ms_cancelled =
    cancelled' }

(** val slot_close : motion_slot -> motion_slot **)

let slot_close s =
  let cancelled' =
    match s.ms_running with
    | Some prev -> Cons (prev, s.ms_cancelled)
    | None -> s.ms_cancelled
  in
  { ms_closed = True; ms_running = None; ms_cancelled = cancelled' }

(** val insert_prio_seq :
    (z, nat) prod list -> (z, nat) prod list -> (z, nat) prod list **)

let rec insert_prio_seq acc = function
| Nil -> acc
| Cons (p0, rest) ->
  let Pair (p, id) = p0 in insert_prio_seq (insert_priority p id acc) rest

(** val observe_timing_engine :
    z -> z -> z -> z -> bool -> bool -> z -> nat -> z -> z -> z -> (z, nat)
    prod list -> ((((z, z) prod, bool) prod, z) prod, nat list) prod **)

let observe_timing_engine iter_dur iters delay loop_delay alternate reversed raw_t iter_idx last_tick now frame_dur prios =
  let cfg = { tc_iter_dur = iter_dur; tc_iters = iters; tc_delay = delay;
    tc_loop_delay = loop_delay; tc_alternate = alternate; tc_reversed =
    reversed }
  in
  let anchor' =
    match Z.leb frame_dur Z0 with
    | True -> last_tick
    | False -> advance_clock_anchor last_tick now frame_dur
  in
  let ordered_ids = map snd (insert_prio_seq Nil prios) in
  Pair ((Pair ((Pair ((Pair ((total_duration cfg),
  (sample_bounded_time cfg raw_t))),
  (sample_direction reversed alternate iter_idx))), anchor')), ordered_ids)

(** val seq_z : z -> nat -> z list **)

let rec seq_z start = function
| O -> Nil
| S c -> Cons (start, (seq_z (Z.add start (Zpos XH)) c))

(** val observe_stagger_grid :
    z -> z -> z -> z -> z -> nat -> (z list, ((z, z) prod, z) prod list) prod **)

let observe_stagger_grid cols rows origin_idx step start_off count =
  let offsets =
    flat_step_simd_chunks count (S (S (S (S O)))) Z0 count origin_idx step
      start_off
  in
  let safe_cols = Z.max (Zpos XH) cols in
  let safe_rows = Z.max (Zpos XH) rows in
  let coords =
    map (grid3_decode safe_cols safe_rows)
      (seq_z Z0 (Nat.min (S (S (S (S (S (S O)))))) count))
  in
  Pair (offsets, coords)

(** val observe_round_and_color :
    z list -> z list -> z list -> bool -> z -> z -> z -> (((z list, (z list,
    z list) prod) prod, z) prod, z) prod **)

let observe_round_and_color half_nums xs ys c0_zero h0 h1 grad_hund =
  let rounded = map js_round_half_int half_nums in
  let homog = homogenize_components xs ys in
  let hue_mid =
    match c0_zero with
    | True ->
      Z.mul
        (Z.modulo
          (Z.add
            (Z.modulo h1 (Zpos (XO (XO (XO (XI (XO (XI (XI (XO XH))))))))))
            (Zpos (XO (XO (XO (XI (XO (XI (XI (XO XH))))))))))
          (Zpos (XO (XO (XO (XI (XO (XI (XI (XO XH))))))))))
        (Zpos (XO XH))
    | False ->
      let h0n =
        Z.modulo
          (Z.add
            (Z.modulo h0 (Zpos (XO (XO (XO (XI (XO (XI (XI (XO XH))))))))))
            (Zpos (XO (XO (XO (XI (XO (XI (XI (XO XH))))))))))
          (Zpos (XO (XO (XO (XI (XO (XI (XI (XO XH)))))))))
      in
      let h1n =
        Z.modulo
          (Z.add
            (Z.modulo h1 (Zpos (XO (XO (XO (XI (XO (XI (XI (XO XH))))))))))
            (Zpos (XO (XO (XO (XI (XO (XI (XI (XO XH))))))))))
          (Zpos (XO (XO (XO (XI (XO (XI (XI (XO XH)))))))))
      in
      let d = hcl_shortest_delta h0n h1n in
      Z.modulo
        (Z.add
          (Z.modulo (Z.add (Z.mul h0n (Zpos (XO XH))) d) (Zpos (XO (XO (XO
            (XO (XI (XO (XI (XI (XO XH)))))))))))
          (Zpos (XO (XO (XO (XO (XI (XO (XI (XI (XO XH)))))))))))
        (Zpos (XO (XO (XO (XO (XI (XO (XI (XI (XO XH))))))))))
  in
  let deg_x10 =
    Z.div (Z.mul grad_hund (Zpos (XI (XO (XO XH))))) (Zpos (XO (XI (XO XH))))
  in
  Pair ((Pair ((Pair (rounded, homog)), hue_mid)), deg_x10)

(** val apply_blend_targets : z -> (bool, z) prod list -> z **)

let rec apply_blend_targets init_val = function
| Nil -> init_val
| Cons (p, rest) ->
  let Pair (is_muted, tgt) = p in
  let next_val = match is_muted with
                 | True -> init_val
                 | False -> tgt in
  apply_blend_targets next_val rest

(** val index_child_durs : nat -> z list -> ((nat, z) prod, z) prod list **)

let rec index_child_durs idx = function
| Nil -> Nil
| Cons (d, rest) ->
  Cons ((Pair ((Pair (idx, Z0)), (Z.max (Zpos XH) d))),
    (index_child_durs (S idx) rest))

(** val observe_composition_timeline :
    z -> (bool, z) prod list -> z -> z -> z -> z -> z -> z list -> nat ->
    ((z, z option) prod, z) prod **)

let observe_composition_timeline init_val blend_targets host_epoch clock_delay tw_start old_dur new_dur child_durs remove_idx =
  let settled_blend = apply_blend_targets init_val blend_targets in
  let tw0 = { tt_host_epoch = host_epoch; tt_clock_delay = clock_delay;
    tt_start = tw_start; tt_dur = old_dur; tt_abs_start =
    (Z.add (Z.add host_epoch clock_delay) tw_start) }
  in
  let stretched_abs =
    match stretch_top_level_tween new_dur old_dur tw0 with
    | Some tw1 -> Some tw1.tt_abs_start
    | None -> None
  in
  let Pair (rem_iter_dur, _) =
    timeline_remove_entry remove_idx (index_child_durs O child_durs)
  in
  Pair ((Pair (settled_blend, stretched_abs)), rem_iter_dur)

(** val run_scroll_positions :
    z -> z -> z list -> scroll_state -> scroll_state **)

let rec run_scroll_positions start_p finish_p positions st =
  match positions with
  | Nil -> st
  | Cons (p, rest) ->
    run_scroll_positions start_p finish_p rest
      (scroll_step start_p finish_p p st)

(** val observe_drag_scroll :
    z -> z -> z -> z -> z -> z -> z -> z list -> (((z, z) prod, z) prod,
    (nat, bool) prod) prod **)

let observe_drag_scroll lo hi factor v thresh start_p finish_p positions =
  let safe_lo = Z.min lo hi in
  let safe_hi = Z.max lo hi in
  let safe_fac = Z.max (Zpos XH) factor in
  let r = resist_exact safe_lo safe_hi safe_fac v in
  let inv = grab_inverse_exact safe_lo safe_hi safe_fac r in
  let edge_zero = edge_scroll_safe safe_lo safe_hi Z0 Z0 thresh (Zpos XH) v in
  let s_init = { ss_in_view = False; ss_has_entered = False; ss_force_enter =
    False; ss_enter_count = O }
  in
  let s_fin = run_scroll_positions start_p finish_p positions s_init in
  Pair ((Pair ((Pair (r, inv)), edge_zero)), (Pair (s_fin.ss_enter_count,
  s_fin.ss_in_view)))

(** val run_slot_ops : nat option list -> motion_slot -> motion_slot **)

let rec run_slot_ops ops s =
  match ops with
  | Nil -> s
  | Cons (o, rest) ->
    (match o with
     | Some next_id -> run_slot_ops rest (slot_play next_id s)
     | None -> run_slot_ops rest (slot_close s))

(** val observe_transform_and_motion :
    bool -> z option -> z list -> nat option list -> ((nat, z option) prod,
    (bool, nat option) prod) prod **)

let observe_transform_and_motion clear_inline init_style updates slot_ops =
  let live_inline =
    match clear_inline with
    | True -> None
    | False ->
      Some (Cons ((Pair ((S O), (Zpos (XI (XO (XI (XI (XO XH)))))))), Nil))
  in
  let reverted_cache =
    revert_transform_components live_inline (Cons ((Pair ((S O), (Zpos (XI
      (XO (XI (XI (XO XH)))))))), Nil)) (Cons ((Pair (O, None)), Nil))
  in
  let Pair (cur_style, snaps) = run_animate_on_updates init_style updates in
  let restored_style = revert_snapshots_reverse cur_style snaps in
  let s_fin =
    run_slot_ops slot_ops { ms_closed = False; ms_running = None;
      ms_cancelled = Nil }
  in
  Pair ((Pair ((length reverted_cache), restored_style)), (Pair
  (s_fin.ms_closed, s_fin.ms_running)))
