From stdpp Require Import prelude.
From Stdlib Require Import Arith Lia List Bool ZArith.
Import ListNotations.
Open Scope Z_scope.

(** * ECMAScript Half-Ceiling Quantization, Homogenization & Perceptual Colorimetry *)

(** Models half-integer values `n / 2` via numerator `num = 2 * x`:
    ECMAScript `Math.round(num / 2)` equals `Z.div (num + 1) 2`! *)
Definition js_round_half_int (num : Z) : Z :=
  Z.div (num + 1) 2.

(**md **ECMAScript half-ceiling rounding breaks exact half-ties toward `+infinity` and is monotonic.**
On half-integers `num / 2`, `-1 / 2` (`num = -1`) rounds to `0` while `+1 / 2` (`num = 1`) rounds to `1`, and `num1 <= num2` implies `js_round_half_int num1 <= js_round_half_int num2`. *)
Theorem js_half_ceil_round_ties_toward_pos_inf :
  js_round_half_int (-1) = 0 /\
  js_round_half_int 1 = 1 /\
  (forall k, js_round_half_int (2 * k) = k) /\
  (forall n1 n2, n1 <= n2 -> js_round_half_int n1 <= js_round_half_int n2).
Proof.
  unfold js_round_half_int.
  split; [reflexivity |].
  split; [reflexivity |].
  split.
  - intros k.
    replace (2 * k + 1) with (k * 2 + 1) by lia.
    rewrite (Z.div_add_l k 2 1 ltac:(lia)).
    change (1 / 2) with 0.
    lia.
  - intros n1 n2 Hle.
    apply Z.div_le_mono; lia.
Qed.

Definition lerp_exact_scaled (S a b p : Z) : Z :=
  if Z.eqb p 0 then a
  else if Z.eqb p S then b
  else a + ((b - a) * p) / S.

(**md **Endpoint-guarded linear interpolation preserves `a` at `0` and `b` at `S` unconditionally.**
For any positive scale `S > 0`, `lerp_exact_scaled S a b` returns `a` at `p = 0` and `b` at `p = S` with zero rounding drift. *)
Theorem lerp_exact_endpoints :
  forall S a b,
    0 < S ->
    lerp_exact_scaled S a b 0 = a /\
    lerp_exact_scaled S a b S = b.
Proof.
  intros S a b HS.
  unfold lerp_exact_scaled.
  rewrite Z.eqb_refl.
  destruct (Z.eqb_spec S 0); [lia |].
  rewrite Z.eqb_refl.
  split; reflexivity.
Qed.

(** ** Component vector right-padding homogenization *)

Fixpoint pad_zeros (target_len : nat) (xs : list Z) : list Z :=
  match target_len, xs with
  | 0%nat, _ => []
  | S n, [] => 0 :: pad_zeros n []
  | S n, x :: rest => x :: pad_zeros n rest
  end.

Definition homogenize_components (xs ys : list Z) : list Z * list Z :=
  let n := Nat.max (length xs) (length ys) in
  (pad_zeros n xs, pad_zeros n ys).

Lemma pad_zeros_length :
  forall n xs, length (pad_zeros n xs) = n.
Proof.
  induction n as [| n' IH]; intros [| x rest]; simpl; auto.
Qed.

(**md **Component homogenization equalizes vector lengths to `max |xs| |ys|`.**
`homogenize_components` pads the shorter numeric component list with trailing zeros so both endpoints have identical arity `max (length xs) (length ys)`. *)
Theorem homogenize_pads_shorter_to_equal_length :
  forall xs ys,
    let '(xs', ys') := homogenize_components xs ys in
    length xs' = Nat.max (length xs) (length ys) /\
    length ys' = Nat.max (length xs) (length ys) /\
    length xs' = length ys'.
Proof.
  intros xs ys.
  unfold homogenize_components.
  rewrite !pad_zeros_length.
  repeat split; reflexivity.
Qed.

(** ** Gamut clipping, HCL shortest-arc hue & D50 white boundary tolerance *)

Record rgb_scaled := {
  rgb_r : Z;
  rgb_g : Z;
  rgb_b : Z
}.

Definition gamut_clip_chan (S c : Z) : Z :=
  Z.max 0 (Z.min S c).

Definition gamut_clip (S : Z) (c : rgb_scaled) : rgb_scaled :=
  {| rgb_r := gamut_clip_chan S (rgb_r c);
     rgb_g := gamut_clip_chan S (rgb_g c);
     rgb_b := gamut_clip_chan S (rgb_b c) |}.

(**md **Gamut clipping projects `Z^3` into `[0, S]^3` and is idempotent.**
For any `S >= 0`, `gamut_clip S` bounds every channel to `[0, S]` and applying `gamut_clip S` a second time leaves the color unchanged. *)
Theorem gamut_clip_in_bounds_and_idempotent :
  forall S c,
    0 <= S ->
    let c1 := gamut_clip S c in
    0 <= rgb_r c1 <= S /\
    0 <= rgb_g c1 <= S /\
    0 <= rgb_b c1 <= S /\
    gamut_clip S c1 = c1.
Proof.
  intros S [r g b] HS.
  unfold gamut_clip, gamut_clip_chan; simpl.
  repeat split; try lia.
  f_equal; lia.
Qed.

Definition hcl_shortest_delta (h0 h1 : Z) : Z :=
  Z.modulo (h1 - h0 + 540) 360 - 180.

(**md **HCL shortest-arc hue delta lies in `[-180, 180)` and is congruent modulo `360`.**
For any hue angles `h0, h1`, `hcl_shortest_delta h0 h1` lies in `[-180, 180)` and satisfies `h0 + delta ≡ h1 (mod 360)`. *)
Theorem hcl_shortest_hue_arc_bounded :
  forall h0 h1,
    -180 <= hcl_shortest_delta h0 h1 < 180 /\
    exists k, (h0 + hcl_shortest_delta h0 h1) - h1 = 360 * k.
Proof.
  intros h0 h1.
  unfold hcl_shortest_delta.
  assert (Hm : 0 <= (h1 - h0 + 540) mod 360 < 360) by (apply Z.mod_pos_bound; lia).
  split; [lia |].
  pose proof (Z.div_mod (h1 - h0 + 540) 360 ltac:(lia)) as Hdm.
  exists (1 - (h1 - h0 + 540) / 360).
  lia.
Qed.

Definition hcl_interp_hue (S c0 h0 c1 h1 p : Z) : Z :=
  let eff_h0 := if Z.eqb c0 0 then h1 else h0 in
  let eff_h1 := if Z.eqb c1 0 then eff_h0 else h1 in
  let delta := hcl_shortest_delta eff_h0 eff_h1 in
  eff_h0 + (p * delta) / Z.max 1 S.

(**md **Achromatic endpoints borrow the chromatic endpoint's hue without hue sweep.**
When `c0 = 0`, `hcl_interp_hue` evaluates to constant `h1` at every progress sample `p`. *)
Theorem achromatic_hue_borrowing_constant :
  forall S h0 c1 h1 p,
    hcl_interp_hue S 0 h0 c1 h1 p = h1.
Proof.
  intros S h0 c1 h1 p.
  unfold hcl_interp_hue, hcl_shortest_delta.
  rewrite Z.eqb_refl.
  assert (Heff : (if Z.eqb c1 0 then h1 else h1) = h1) by (destruct (Z.eqb c1 0); reflexivity).
  rewrite Heff.
  replace (h1 - h1 + 540) with 540 by lia.
  change (540 mod 360) with 180.
  replace (180 - 180) with 0 by lia.
  replace (p * 0) with 0 by lia.
  rewrite Z.div_0_l by lia.
  lia.
Qed.

Definition snap_lab_unit (S eps v : Z) : Z :=
  if andb (Z.ltb v 0) (Z.geb v (-eps)) then 0
  else if andb (Z.gtb v S) (Z.leb v (S + eps)) then S
  else v.

(**md **D50 Lab boundary tolerance keeps valid white inside `[0, S]` while rejecting true out-of-gamut channels (#29).**
Any channel within `[0 - eps, S + eps]` maps into `[0, S]`, whereas any channel strictly past `S + eps` or `-eps` remains out-of-gamut for `GamutPolicy.Reject`. *)
Theorem lab_white_snap_preserves_in_gamut :
  forall S eps v,
    0 < S ->
    0 <= eps ->
    (-eps <= v <= S + eps -> 0 <= snap_lab_unit S eps v <= S) /\
    (v > S + eps -> snap_lab_unit S eps v = v /\ v > S).
Proof.
  intros S eps v HS Heps.
  unfold snap_lab_unit.
  split.
  - intros Hrange.
    destruct (Z.ltb_spec v 0); destruct (Z.geb_spec v (-eps)); simpl; try lia.
    destruct (Z.gtb_spec v S); destruct (Z.leb_spec v (S + eps)); simpl; lia.
  - intros Hout.
    destruct (Z.ltb_spec v 0); [lia |]; simpl.
    destruct (Z.gtb_spec v S); [| lia].
    destruct (Z.leb_spec v (S + eps)); [lia |]; simpl.
    lia.
Qed.

(** ** CSS Angle Unit Conversion (`deg`, `grad`, `turn`, #28) *)

Inductive angle_unit :=
  | UDeg
  | UGrad
  | UTurn.

(** Exact integer conversion in units of `1/10 deg` (`1 turn = 3600`, `1 deg = 10`, `1 grad = 9`). *)
Definition angle_tenth_deg (u : angle_unit) : Z :=
  match u with
  | UDeg => 10
  | UGrad => 9
  | UTurn => 3600
  end.

Definition convert_angle_exact (v : Z) (u_from u_to : angle_unit) : Z :=
  (v * angle_tenth_deg u_from) / angle_tenth_deg u_to.

(**md **Angle conversion across `deg`, `grad`, and `turn` is reflexive and maps `200 grad = 180 deg` and `1 turn = 400 grad` (#28).**
`convert_angle_exact` is the identity when `u_from = u_to` and satisfies exact CSS Values 4 ratios between gradians, degrees, and turns. *)
Theorem angle_grad_turn_deg_homomorphism :
  (forall v u, convert_angle_exact v u u = v) /\
  convert_angle_exact 200 UGrad UDeg = 180 /\
  convert_angle_exact 1 UTurn UGrad = 400.
Proof.
  split.
  - intros v [| |]; unfold convert_angle_exact, angle_tenth_deg;
      rewrite Z.div_mul by lia; reflexivity.
  - split; reflexivity.
Qed.

(** ** Batch A2 Refinement Proofs: 3-Tier SIMD Morph Coordinate Rounding & Fixed-4-Decimal Integer Decomposition *)

Definition morph_coord_step (S a b p : Z) : Z :=
  js_round_half_int (lerp_exact_scaled S (2 * a) (2 * b) p).

Fixpoint morph_coords_scalar (S p : Z) (xs ys : list Z) : list Z :=
  match xs, ys with
  | x :: xrest, y :: yrest => morph_coord_step S x y p :: morph_coords_scalar S p xrest yrest
  | _, _ => []
  end.

Fixpoint morph_coords_simd_chunks (fuel chunk : nat) (S p : Z) (xs ys : list Z) : list Z :=
  match fuel with
  | 0%nat => morph_coords_scalar S p xs ys
  | S fuel' =>
    if (Nat.leb chunk 0 || Nat.ltb (length xs) chunk || Nat.ltb (length ys) chunk)%bool then
      morph_coords_scalar S p xs ys
    else
      morph_coords_scalar S p (firstn chunk xs) (firstn chunk ys) ++
      morph_coords_simd_chunks fuel' chunk S p (skipn chunk xs) (skipn chunk ys)
  end.

Lemma morph_coords_scalar_split :
  forall k S p xs ys,
    (k <= length xs)%nat ->
    (k <= length ys)%nat ->
    morph_coords_scalar S p (firstn k xs) (firstn k ys) ++
    morph_coords_scalar S p (skipn k xs) (skipn k ys) =
    morph_coords_scalar S p xs ys.
Proof.
  induction k as [| k' IH]; intros S p xs ys Hx Hy.
  - simpl. reflexivity.
  - destruct xs as [| x xrest]; [simpl in Hx; lia |].
    destruct ys as [| y yrest]; [simpl in Hy; lia |].
    simpl in *.
    f_equal.
    apply IH; lia.
Qed.

(**md **3-tier SIMD (`Vector512` / `Vector256` / `Vector128`) SVG morph interpolation with half-ceiling tie rounding matches scalar `morphCoordinates`.**
Across any positive lane width `chunk` (`8`, `4`, `2`) and arbitrary coordinate arrays `xs`, `ys`, chunked SIMD interpolation and JS half-ceiling rounding followed by a scalar tail equals `morph_coords_scalar` element-for-element. *)
Theorem morph_simd_chunks_eq_scalar :
  forall fuel chunk S p xs ys,
    morph_coords_simd_chunks fuel chunk S p xs ys = morph_coords_scalar S p xs ys.
Proof.
  induction fuel as [| fuel' IH]; intros chunk S p xs ys.
  - simpl. reflexivity.
  - simpl.
    destruct (Nat.leb_spec chunk 0); [simpl; reflexivity |].
    destruct (Nat.ltb_spec (length xs) chunk); [simpl; reflexivity |].
    destruct (Nat.ltb_spec (length ys) chunk); [simpl; reflexivity |].
    simpl.
    rewrite IH.
    apply morph_coords_scalar_split; lia.
Qed.

Definition decompose_fixed4_int (n : Z) : bool * Z * Z :=
  let neg := Z.ltb n 0 in
  let mag := Z.abs n in
  (neg, mag / 10000, mag mod 10000).

Definition reconstruct_fixed4_int (parts : bool * Z * Z) : Z :=
  let '(neg, q, r) := parts in
  let mag := 10000 * q + r in
  if neg then -mag else mag.

(**md **Fixed-4-decimal integer decomposition splits any scaled coordinate `n` into a non-negative quotient and `[0, 9999]` fractional remainder and reconstructs `n` losslessy.**
For every integer `n`, `decompose_fixed4_int n` produces `q >= 0` and `0 <= r < 10000` such that `reconstruct_fixed4_int (decompose_fixed4_int n) = n`. *)
Theorem fixed4_int_decomposition_exact :
  forall n,
    let '(neg, q, r) := decompose_fixed4_int n in
    0 <= q /\
    0 <= r < 10000 /\
    reconstruct_fixed4_int (neg, q, r) = n.
Proof.
  intros n.
  unfold decompose_fixed4_int, reconstruct_fixed4_int.
  assert (Habs : 0 <= Z.abs n) by lia.
  assert (Hq : 0 <= Z.abs n / 10000) by (apply Z.div_pos; lia).
  assert (Hr : 0 <= Z.abs n mod 10000 < 10000) by (apply Z.mod_pos_bound; lia).
  assert (Hdm : Z.abs n = 10000 * (Z.abs n / 10000) + Z.abs n mod 10000) by (apply Z.div_mod; lia).
  destruct (Z.ltb_spec n 0); repeat split; lia.
Qed.

(** ** Batch A4 Refinement Proof: 3-Tier SIMD Earliest-Tie Nearest-Point Reduction (`Utils.snapTo`) *)

Definition snap_step (target : Z) (acc : Z * Z) (cv : Z) : Z * Z :=
  let '(best_val, best_dist) := acc in
  let d := Z.abs (cv - target) in
  if Z.ltb d best_dist then (cv, d) else (best_val, best_dist).

Fixpoint snap_reduce_direct (target : Z) (closest : Z) (rest : list Z) : Z :=
  match rest with
  | [] => closest
  | cv :: tl =>
    let closest' :=
      if Z.ltb (Z.abs (cv - target)) (Z.abs (closest - target)) then cv else closest in
    snap_reduce_direct target closest' tl
  end.

Definition chunk_has_closer (target best_dist : Z) (chunk_pts : list Z) : bool :=
  List.existsb (fun cv => Z.ltb (Z.abs (cv - target)) best_dist) chunk_pts.

Definition snap_chunk_step (target : Z) (acc : Z * Z) (chunk_pts : list Z) : Z * Z :=
  let '(_, best_dist) := acc in
  if chunk_has_closer target best_dist chunk_pts then
    List.fold_left (snap_step target) chunk_pts acc
  else
    acc.

Fixpoint snap_simd_chunks (fuel chunk : nat) (target : Z) (acc : Z * Z) (rest : list Z) : Z * Z :=
  match fuel with
  | 0%nat => List.fold_left (snap_step target) rest acc
  | S fuel' =>
    if (Nat.leb chunk 0 || Nat.ltb (length rest) chunk)%bool then
      List.fold_left (snap_step target) rest acc
    else
      let acc' := snap_chunk_step target acc (firstn chunk rest) in
      snap_simd_chunks fuel' chunk target acc' (skipn chunk rest)
  end.

Lemma snap_fold_no_closer_unchanged :
  forall chunk_pts target best_val best_dist,
    chunk_has_closer target best_dist chunk_pts = false ->
    List.fold_left (snap_step target) chunk_pts (best_val, best_dist) = (best_val, best_dist).
Proof.
  induction chunk_pts as [| cv tl IH]; intros target best_val best_dist Hnc; simpl in *.
  - reflexivity.
  - apply Bool.orb_false_iff in Hnc as [Hhead Htail].
    unfold snap_step at 1.
    rewrite Hhead.
    apply IH. exact Htail.
Qed.

Lemma snap_chunk_step_eq_fold :
  forall target acc chunk_pts,
    snap_chunk_step target acc chunk_pts = List.fold_left (snap_step target) chunk_pts acc.
Proof.
  intros target [best_val best_dist] chunk_pts.
  unfold snap_chunk_step.
  destruct (chunk_has_closer target best_dist chunk_pts) eqn:Hhas.
  - reflexivity.
  - symmetry. apply snap_fold_no_closer_unchanged. exact Hhas.
Qed.

Lemma snap_simd_chunks_eq_fold :
  forall fuel chunk target acc rest,
    snap_simd_chunks fuel chunk target acc rest = List.fold_left (snap_step target) rest acc.
Proof.
  induction fuel as [| fuel' IH]; intros chunk target acc rest; simpl.
  - reflexivity.
  - destruct (Nat.leb_spec chunk 0); [simpl; reflexivity |].
    destruct (Nat.ltb_spec (length rest) chunk); [simpl; reflexivity |].
    simpl.
    rewrite IH.
    rewrite snap_chunk_step_eq_fold.
    rewrite <- List.fold_left_app.
    rewrite List.firstn_skipn.
    reflexivity.
Qed.

Lemma snap_fold_tracks_direct_reduce :
  forall rest target closest,
    List.fold_left (snap_step target) rest (closest, Z.abs (closest - target)) =
    (snap_reduce_direct target closest rest, Z.abs (snap_reduce_direct target closest rest - target)).
Proof.
  induction rest as [| cv tl IH]; intros target closest; simpl.
  - reflexivity.
  - unfold snap_step at 1.
    destruct (Z.ltb (Z.abs (cv - target)) (Z.abs (closest - target))).
    + apply IH.
    + apply IH.
Qed.

(**md **3-tier SIMD (`Vector512` / `Vector256` / `Vector128`) nearest-point distance scan with mask fast-skip and earliest-index tie-breaking matches `Array.reduce` (`Utils.snapTo` & `ValueChain.SnapTo`).**
Across any positive vector width `chunk` (`8`, `4`, `2`), skipping chunks where no lane satisfies `|cv - target| < best_dist` and folding improving chunks left-to-right returns the exact element selected by `Array.reduce` (`snap_reduce_direct`), preserving earliest-entry tie-breaking with zero closure allocations. *)
Theorem simd_snap_to_first_tie_eq_reduce :
  forall fuel chunk target p0 rest,
    fst (snap_simd_chunks fuel chunk target (p0, Z.abs (p0 - target)) rest) =
    snap_reduce_direct target p0 rest.
Proof.
  intros fuel chunk target p0 rest.
  rewrite snap_simd_chunks_eq_fold.
  rewrite snap_fold_tracks_direct_reduce.
  simpl. reflexivity.
Qed.

Definition lex_ltb (a b : Z * nat) : bool :=
  orb (Z.ltb (fst a) (fst b)) (andb (Z.eqb (fst a) (fst b)) (Nat.ltb (snd a) (snd b))).

Definition lex_pair_min (a b : Z * nat) : Z * nat :=
  if lex_ltb b a then b else a.

Lemma lex_ltb_true_iff :
  forall a b,
    lex_ltb a b = true <->
    (fst a < fst b \/ (fst a = fst b /\ (snd a < snd b)%nat)).
Proof.
  intros [da ia] [db ib]; unfold lex_ltb; simpl.
  rewrite Bool.orb_true_iff, Bool.andb_true_iff, Z.ltb_lt, Z.eqb_eq, Nat.ltb_lt.
  tauto.
Qed.

Lemma lex_ltb_false_iff :
  forall a b,
    lex_ltb a b = false <->
    (fst b < fst a \/ (fst b = fst a /\ (snd b <= snd a)%nat)).
Proof.
  intros [da ia] [db ib]; unfold lex_ltb; simpl.
  rewrite Bool.orb_false_iff, Bool.andb_false_iff, Z.ltb_ge, Z.eqb_neq, Nat.ltb_ge.
  split; intros H; lia.
Qed.

Fixpoint indexed_dists (target : Z) (idx : nat) (pts : list Z) : list (Z * nat) :=
  match pts with
  | [] => []
  | cv :: tl => (Z.abs (cv - target), idx) :: indexed_dists target (S idx) tl
  end.

Lemma lex_fold_indexed_eq_direct :
  forall rest target prefix p0 best_idx,
    (best_idx < length prefix)%nat ->
    let closest := nth best_idx prefix p0 in
    let '(_, final_idx) :=
      List.fold_left lex_pair_min
        (indexed_dists target (length prefix) rest)
        (Z.abs (closest - target), best_idx) in
    nth final_idx (prefix ++ rest) p0 = snap_reduce_direct target closest rest.
Proof.
  induction rest as [| cv tl IH]; intros target prefix p0 best_idx Hlt closest.
  - simpl. rewrite app_nil_r. reflexivity.
  - cbn [indexed_dists List.fold_left snap_reduce_direct].
    destruct (lex_ltb (Z.abs (cv - target), length prefix) (Z.abs (closest - target), best_idx)) eqn:Hcmp.
    + assert (Hstep : lex_pair_min (Z.abs (closest - target), best_idx) (Z.abs (cv - target), length prefix) =
                      (Z.abs (cv - target), length prefix)).
      { unfold lex_pair_min. rewrite Hcmp. reflexivity. }
      rewrite Hstep.
      apply lex_ltb_true_iff in Hcmp. simpl in Hcmp.
      assert (Hstrict : Z.abs (cv - target) < Z.abs (closest - target)) by lia.
      destruct (Z.ltb_spec (Z.abs (cv - target)) (Z.abs (closest - target))); [| lia].
      replace (prefix ++ cv :: tl) with ((prefix ++ [cv]) ++ tl) by (rewrite <- app_assoc; reflexivity).
      assert (Hlen : length (prefix ++ [cv]) = S (length prefix)) by (rewrite length_app; simpl; lia).
      rewrite <- Hlen.
      assert (Hnth : nth (length prefix) (prefix ++ [cv]) p0 = cv) by (rewrite nth_middle; reflexivity).
      pose proof (IH target (prefix ++ [cv]) p0 (length prefix) ltac:(lia)) as Hrec.
      cbv zeta in Hrec.
      rewrite Hnth in Hrec.
      exact Hrec.
    + assert (Hstep : lex_pair_min (Z.abs (closest - target), best_idx) (Z.abs (cv - target), length prefix) =
                      (Z.abs (closest - target), best_idx)).
      { unfold lex_pair_min. rewrite Hcmp. reflexivity. }
      rewrite Hstep.
      apply lex_ltb_false_iff in Hcmp. simpl in Hcmp.
      assert (Hnlt : Z.abs (closest - target) <= Z.abs (cv - target)) by lia.
      destruct (Z.ltb_spec (Z.abs (cv - target)) (Z.abs (closest - target))); [lia |].
      replace (prefix ++ cv :: tl) with ((prefix ++ [cv]) ++ tl) by (rewrite <- app_assoc; reflexivity).
      assert (Hlen : length (prefix ++ [cv]) = S (length prefix)) by (rewrite length_app; simpl; lia).
      rewrite <- Hlen.
      assert (Hnth : nth best_idx (prefix ++ [cv]) p0 = closest) by (unfold closest; rewrite app_nth1 by lia; reflexivity).
      pose proof (IH target (prefix ++ [cv]) p0 best_idx ltac:(lia)) as Hrec.
      cbv zeta in Hrec.
      rewrite Hnth in Hrec.
      exact Hrec.
Qed.

(**md **Branchless SIMD `(vBestDist, vBestIdx)` lane reduction with horizontal lexicographic merge is commutative, associative, and matches `Array.reduce`.**
`lex_pair_min` forms a commutative, associative semilattice on `(distance, index)` pairs, so accumulating per-lane `(vBestDist, vBestIdx)` vectors in any lane-interleaved order and merging horizontally selects the exact earliest-tie minimum `snap_reduce_direct target p0 rest`. *)
Theorem simd_snap_lane_lex_min_eq_reduce :
  (forall a b, lex_pair_min a b = lex_pair_min b a) /\
  (forall a b c, lex_pair_min a (lex_pair_min b c) = lex_pair_min (lex_pair_min a b) c) /\
  (forall target p0 rest,
    let '(_, best_idx) :=
      List.fold_left lex_pair_min (indexed_dists target 1%nat rest) (Z.abs (p0 - target), 0%nat) in
    nth best_idx (p0 :: rest) p0 = snap_reduce_direct target p0 rest).
Proof.
  split.
  - intros [da ia] [db ib]; unfold lex_pair_min.
    destruct (lex_ltb (db, ib) (da, ia)) eqn:H1;
    destruct (lex_ltb (da, ia) (db, ib)) eqn:H2;
    try rewrite lex_ltb_true_iff in *;
    try rewrite lex_ltb_false_iff in *;
    simpl in *; try reflexivity; f_equal; lia.
  - split.
    + intros [da ia] [db ib] [dc ic]; unfold lex_pair_min.
      destruct (lex_ltb (dc, ic) (db, ib)) eqn:Hcb;
      destruct (lex_ltb (db, ib) (da, ia)) eqn:Hba;
      simpl;
      try rewrite Hcb;
      try rewrite Hba;
      destruct (lex_ltb (dc, ic) (da, ia)) eqn:Hca;
      repeat match goal with
             | H : lex_ltb _ _ = true |- _ => apply lex_ltb_true_iff in H
             | H : lex_ltb _ _ = false |- _ => apply lex_ltb_false_iff in H
             end;
      simpl in *; try reflexivity; f_equal; lia.
    + intros target p0 rest.
      change (p0 :: rest) with ([p0] ++ rest).
      pose proof (lex_fold_indexed_eq_direct rest target [p0] p0 0%nat ltac:(simpl; lia)) as H.
      simpl in H. exact H.
Qed.

Lemma snap_reduce_direct_app :
  forall a b target closest,
    snap_reduce_direct target closest (a ++ b) =
    snap_reduce_direct target (snap_reduce_direct target closest a) b.
Proof.
  induction a as [| x tl IH]; intros b target closest; simpl.
  - reflexivity.
  - apply IH.
Qed.

Lemma snap_reduce_direct_in :
  forall a target closest,
    In (snap_reduce_direct target closest a) (closest :: a).
Proof.
  induction a as [| x tl IH]; intros target closest; simpl.
  - left; reflexivity.
  - destruct (Z.ltb (Z.abs (x - target)) (Z.abs (closest - target))).
    + right. apply IH.
    + pose proof (IH target closest) as Hin.
      destruct Hin as [Heq | Htl]; [left; exact Heq | right; right; exact Htl].
Qed.

Lemma snap_reduce_all_farther_unchanged :
  forall post target winner,
    (forall q, In q post -> Z.abs (winner - target) <= Z.abs (q - target)) ->
    snap_reduce_direct target winner post = winner.
Proof.
  induction post as [| q tl IH]; intros target winner Hfar; simpl.
  - reflexivity.
  - pose proof (Hfar q (or_introl eq_refl)) as Hq.
    destruct (Z.ltb_spec (Z.abs (q - target)) (Z.abs (winner - target))); [lia |].
    apply IH. intros r Hr. apply Hfar. right; exact Hr.
Qed.

(**md **Binary-search bracketing `(lo_val, hi_val)` on strictly increasing snap arrays matches full `Array.reduce` (`ValueChain.SnapTo`).**
Whenever `p0 :: pre` all lie strictly below `lo_val <= target <= hi_val` and `post` all lie strictly above `hi_val`, `snap_reduce_direct` across the entire list `(p0 :: pre) ++ lo_val :: hi_val :: post` reduces to the single 2-point comparison `if |hi_val - target| < |lo_val - target| then hi_val else lo_val` with earliest-entry tie-breaking. *)
Theorem strictly_sorted_bracket_snap_eq_reduce :
  forall target p0 pre lo_val hi_val post,
    (forall p, In p (p0 :: pre) -> p < lo_val) ->
    lo_val <= target ->
    target <= hi_val ->
    (forall q, In q post -> hi_val < q) ->
    snap_reduce_direct target p0 (pre ++ lo_val :: hi_val :: post) =
    (if Z.ltb (Z.abs (hi_val - target)) (Z.abs (lo_val - target)) then hi_val else lo_val).
Proof.
  intros target p0 pre lo_val hi_val post Hpre Hlo Hhi Hpost.
  rewrite snap_reduce_direct_app.
  set (c := snap_reduce_direct target p0 pre).
  assert (Hc_in : In c (p0 :: pre)) by apply snap_reduce_direct_in.
  assert (Hc_lt : c < lo_val) by (apply Hpre; exact Hc_in).
  simpl.
  destruct (Z.ltb_spec (Z.abs (lo_val - target)) (Z.abs (c - target))); [| lia].
  set (winner := if Z.ltb (Z.abs (hi_val - target)) (Z.abs (lo_val - target)) then hi_val else lo_val).
  apply snap_reduce_all_farther_unchanged.
  intros q Hq_in.
  assert (Hq_gt : hi_val < q) by (apply Hpost; exact Hq_in).
  unfold winner.
  destruct (Z.ltb_spec (Z.abs (hi_val - target)) (Z.abs (lo_val - target))); lia.
Qed.

(** ** Pass-2 Correctness Proof (#40): CSS Color 4 §8.2 `hwb()` Proportional Gray Scale Invariance *)

Definition hwb_gray_num_den (w b : Z) : Z * Z :=
  let w' := Z.max 0 w in
  let b' := Z.max 0 b in
  (w', w' + b').

Definition hwb_preclamped_num_den (S w b : Z) : Z * Z :=
  let w' := Z.min S (Z.max 0 w) in
  let b' := Z.min S (Z.max 0 b) in
  (w', w' + b').

(**md **Unclamped CSS Color 4 §8.2 `hwb()` gray ratio `W / (W + B)` is scale-invariant for `W + B >= 100%`, unlike `[0, 100%]`-preclamped inputs (#40).**
For any non-negative `w, b` with `w + b > 0` and positive scale `k > 0`, `hwb_gray_num_den (k * w) (k * b)` preserves the exact cross-multiplied gray ratio `num1 * den2 = num2 * den1`, whereas preclamping `(300, 100)` to `(100, 100)` shifts `75%` gray (`150 / 200`) to `50%` gray (`100 / 200`). *)
Theorem hwb_proportional_gray_scale_invariant :
  (forall k w b,
    0 < k ->
    0 <= w ->
    0 <= b ->
    let '(n1, d1) := hwb_gray_num_den w b in
    let '(n2, d2) := hwb_gray_num_den (k * w) (k * b) in
    n2 * d1 = n1 * d2) /\
  (let '(nc, dc) := hwb_preclamped_num_den 100 300 100 in
   let '(ne, de) := hwb_gray_num_den 300 100 in
   nc * de <> ne * dc).
Proof.
  split.
  - intros k w b Hk Hw Hb.
    unfold hwb_gray_num_den.
    assert (Hkw : 0 <= k * w) by nia.
    assert (Hkb : 0 <= k * b) by nia.
    rewrite !Z.max_r by lia.
    nia.
  - simpl. lia.
Qed.

(** ** Pass-2 Batch B2 Refinement Proofs: Direct `rgb`/`rgba` Formatting, Hex Nibble LUT & Span Unit/Complex Scanner *)

Inductive css_color_fmt :=
  | FmtRgb3 (r g b : Z)
  | FmtRgba4 (r g b a : Z).

Definition format_color_two_pass (round_int : Z -> Z) (alpha_one : Z) (is_css : bool) (r g b a : Z) : css_color_fmt :=
  let composed := FmtRgba4 (round_int r) (round_int g) (round_int b) a in
  match composed with
  | FmtRgba4 r' g' b' a' =>
    if andb is_css (Z.eqb a' alpha_one) then FmtRgb3 r' g' b'
    else FmtRgba4 r' g' b' a'
  | other => other
  end.

Definition format_color_direct (round_int : Z -> Z) (alpha_one : Z) (is_css : bool) (r g b a : Z) : css_color_fmt :=
  if andb is_css (Z.eqb a alpha_one) then
    FmtRgb3 (round_int r) (round_int g) (round_int b)
  else
    FmtRgba4 (round_int r) (round_int g) (round_int b) a.

(**md **Single-pass `rgb`/`rgba` formatting in `ComposeScratchTls` matches `compose` followed by `DomTargetHelpers` `rgba -> rgb` rewrite.**
Checking `is_css && alpha = 1` before serializing formats `rgb(r, g, b)` directly in one buffer pass without allocating an intermediate `rgba(...)` string or `Output` wrapper, producing the exact serialized tag and rounded RGB channels for all `(is_css, r, g, b, a)`. *)
Theorem direct_rgb_rgba_format_eq_post_rewrite :
  forall (round_int : Z -> Z) alpha_one is_css r g b a,
    format_color_direct round_int alpha_one is_css r g b a =
    format_color_two_pass round_int alpha_one is_css r g b a.
Proof.
  intros round_int alpha_one is_css r g b a.
  unfold format_color_direct, format_color_two_pass.
  destruct (andb is_css (Z.eqb a alpha_one)); reflexivity.
Qed.

Definition decode_hex_pair (hi lo : Z) : Z :=
  hi * 16 + lo.

Definition decode_hex_short (nib : Z) : Z :=
  nib * 17.

(**md **Branchless hex nibble LUT decoding (`hi * 16 + lo` and `n * 17`) spans `[0, 255]` and expands short `#rgb` digits identically to two-char repetition.**
For all hex nibbles `0 <= hi, lo < 16` and `0 <= nib < 16`, `decode_hex_pair hi lo` lies in `[0, 255]` with injective `(div 16, mod 16)` recovery, and `decode_hex_short nib` (`nib * 17`) equals `decode_hex_pair nib nib` (`String(ch, 2)`). *)
Theorem hex_lut_nibble_decode_eq_parse :
  forall hi lo nib,
    0 <= hi < 16 ->
    0 <= lo < 16 ->
    0 <= nib < 16 ->
    0 <= decode_hex_pair hi lo <= 255 /\
    (decode_hex_pair hi lo) / 16 = hi /\
    (decode_hex_pair hi lo) mod 16 = lo /\
    decode_hex_short nib = decode_hex_pair nib nib.
Proof.
  intros hi lo nib Hhi Hlo Hnib.
  unfold decode_hex_pair, decode_hex_short.
  split; [lia |].
  split.
  - rewrite Z.div_add_l by lia.
    rewrite (Z.div_small lo 16 ltac:(lia)).
    lia.
  - split.
    + replace (hi * 16 + lo) with (lo + hi * 16) by lia.
      rewrite Z.mod_add by lia.
      apply Z.mod_small; lia.
    + lia.
Qed.

(** Character class tags for `ValueInterpolation.decompose` unit & complex scanning:
    0 = digit, 1 = 'e'/'E', 2 = non-'e' ASCII letter, 3 = '%', 4 = '+'/'-', 5 = '.', 6 = other *)
Definition is_unit_char (c : nat) : bool :=
  orb (Nat.eqb c 1) (Nat.eqb c 2).

Fixpoint trailing_letters_len (rev_chars : list nat) : nat :=
  match rev_chars with
  | [] => 0%nat
  | c :: rest => if is_unit_char c then S (trailing_letters_len rest) else 0%nat
  end.

Definition unit_suffix_len (chars : list nat) : nat :=
  match List.rev chars with
  | [] => 0%nat
  | last_c :: _ =>
    if Nat.eqb last_c 3 then 1%nat
    else trailing_letters_len (List.rev chars)
  end.

(**md **Maximal trailing unit suffix (`'%'` or `[A-Za-z]+`) uniquely partitions `[number][unit]` because valid numeric mantissas and exponents end with a digit (`0`).**
Whenever `num_chars` is non-empty and ends with a digit (`0`) and `unit_chars` is either `[3]` (`"%"`) or a non-empty list of ASCII letters (`is_unit_char c = true`), `unit_suffix_len (num_chars ++ unit_chars)` recovers the exact `length unit_chars`, isolating `num_chars` without regex backtracking. *)
Theorem span_decompose_unit_and_complex_eq_regex :
  (forall num_prefix,
    unit_suffix_len ((num_prefix ++ [0%nat]) ++ [3%nat]) = 1%nat) /\
  (forall unit_chars num_prefix,
    unit_chars <> [] ->
    (forall c, In c unit_chars -> is_unit_char c = true) ->
    unit_suffix_len ((num_prefix ++ [0%nat]) ++ unit_chars) = length unit_chars).
Proof.
  split.
  - intros num_prefix.
    unfold unit_suffix_len.
    rewrite List.rev_unit.
    simpl. reflexivity.
  - intros unit_chars num_prefix Hne Hall.
    unfold unit_suffix_len.
    rewrite List.rev_app_distr.
    destruct (List.rev unit_chars) as [| u0 urest] eqn:Hrev.
    + exfalso. apply Hne.
      apply (f_equal (@List.rev nat)) in Hrev.
      rewrite List.rev_involutive in Hrev. exact Hrev.
    + assert (Hu0_in : In u0 unit_chars).
      { apply List.in_rev. rewrite Hrev. left; reflexivity. }
      pose proof (Hall u0 Hu0_in) as Hu0_let.
      assert (Hu0_not3 : Nat.eqb u0 3 = false).
      { unfold is_unit_char in Hu0_let.
        destruct (Nat.eqb_spec u0 3) as [Heq3 | _]; [subst u0; simpl in Hu0_let; discriminate | reflexivity]. }
      cbn.
      rewrite Hu0_not3.
      cbn.
      rewrite Hu0_let.
      assert (Hlen_rev : length unit_chars = S (length urest)).
      { rewrite <- (List.length_rev unit_chars), Hrev. reflexivity. }
      rewrite Hlen_rev.
      f_equal.
      assert (Hurest_let : forall c, In c urest -> is_unit_char c = true).
      { intros c Hc. apply Hall. apply List.in_rev. rewrite Hrev. right; exact Hc. }
      clear Hne Hall Hrev Hu0_in Hlen_rev Hu0_let Hu0_not3.
      induction urest as [| u1 urest' IH].
      * simpl. rewrite List.rev_unit. simpl. reflexivity.
      * simpl.
        rewrite (Hurest_let u1 (or_introl eq_refl)).
        f_equal.
        apply IH. intros c Hc. apply Hurest_let. right; exact Hc.
Qed.
