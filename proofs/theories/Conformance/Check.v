From Stdlib Require Import List ZArith.
From RanvierAnime.Pure Require Import
  TimingEngine CurvesStagger InterpolationColor CompositionTimeline DomScrollDragMotion.
From RanvierAnime Require Import Extract.
From RanvierAnime.Conformance Require Import Expected.
Import ListNotations.
Open Scope nat_scope.

(** Each [check_Sxx] verifies by [vm_compute] that the Rocq pure model matches the
    concrete output [exp_Sxx] recorded by [proofs/conformance/Scenarios.fsx] from
    running the real F# implementation. *)

Example check_S01 :
  observe_timing_engine 100%Z 3%Z 20%Z 10%Z true false 190%Z 1 100%Z 137%Z 16%Z
    [(10%Z, 1); ((-5)%Z, 2); (10%Z, 3); (0%Z, 4)] = exp_S01.
Proof. vm_compute. reflexivity. Qed.

Example check_S02 :
  observe_timing_engine 50%Z 4%Z 0%Z 0%Z true true 175%Z 2 0%Z 50%Z 20%Z
    [(5%Z, 10); (5%Z, 11); (1%Z, 12)] = exp_S02.
Proof. vm_compute. reflexivity. Qed.

Example check_S03 :
  observe_stagger_grid 3%Z 2%Z 4%Z (-10)%Z 50%Z 9 = exp_S03.
Proof. vm_compute. reflexivity. Qed.

Example check_S04 :
  observe_stagger_grid 4%Z 3%Z 0%Z 5%Z (-20)%Z 17 = exp_S04.
Proof. vm_compute. reflexivity. Qed.

Example check_S05 :
  observe_round_and_color
    [(-3)%Z; (-1)%Z; 0%Z; 1%Z; 3%Z; 5%Z]
    [10%Z; 20%Z] [1%Z; 2%Z; 3%Z; 4%Z]
    false 350%Z 10%Z 200%Z = exp_S05.
Proof. vm_compute. reflexivity. Qed.

Example check_S06 :
  observe_round_and_color
    [(-5)%Z; (-2)%Z; 2%Z; 7%Z]
    [5%Z; 6%Z; 7%Z] [9%Z]
    true 140%Z 275%Z 400%Z = exp_S06.
Proof. vm_compute. reflexivity. Qed.

Example check_S07 :
  observe_composition_timeline
    10%Z [(true, 500%Z); (false, 80%Z); (false, 150%Z)]
    10000%Z 20%Z 0%Z 100%Z 200%Z
    [100%Z; 500%Z] 1 = exp_S07.
Proof. vm_compute. reflexivity. Qed.

Example check_S08 :
  observe_composition_timeline
    25%Z [(false, 60%Z); (true, 999%Z)]
    5000%Z 10%Z 30%Z 100%Z (-10)%Z
    [250%Z; 80%Z; 120%Z] 0 = exp_S08.
Proof. vm_compute. reflexivity. Qed.

Example check_S09 :
  observe_drag_scroll
    0%Z 100%Z 2%Z (-30)%Z 0%Z
    200%Z 400%Z [200%Z; 250%Z; 450%Z; 300%Z] = exp_S09.
Proof. vm_compute. reflexivity. Qed.

Example check_S10 :
  observe_transform_and_motion
    true None [20%Z; 80%Z; 40%Z]
    [Some 1; Some 2; None; Some 3] = exp_S10.
Proof. vm_compute. reflexivity. Qed.
