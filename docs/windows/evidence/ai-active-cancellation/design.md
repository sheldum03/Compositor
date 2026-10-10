# Active inference cancellation experiment

Existing W-032/033 scope requires active cancellation and recovery; no production framework or model choice is made here. Keep the existing three-argument probe unchanged. An opt-in --active-cancel fourth argument creates a dedicated profiled CPU session with the same verified model and tensor, starts Run on a worker, waits for entry and 10 ms without completion, then requests RunOptions termination from the controller. The worker must report a termination error and join before options, session or borrowed tensors are released. Unset termination and recover an exact output in the same session.

The independent reviewer must observe CPU kernel work within the canceled model_run, a second successful model_run, an exact recovered tensor, and no cancellation output being published. Scheduling that ends inference before cancellation, cancellation before any kernel work, unexpected exceptions, or absent profile evidence fail the experiment. Cancellation duration is observed, not assigned an invented product threshold. Five actual runs establish repeatability. A preterminated-only old report and corrupted profile are negative controls.

Alternatives: keep pretermination only (does not cover active work); kill the process (does not prove cooperative cancellation or same-session recovery); cooperative runtime cancellation with profile evidence (chosen, matches required ownership behavior).

The OS/UI document transaction, stale revision rejection, model license, Windows execution and physical interaction remain independent release gates. This is an authorized bounded experiment under the existing plan; no new production design or scope decision is introduced.
