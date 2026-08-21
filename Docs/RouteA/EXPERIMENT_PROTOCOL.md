# Route A evaluation protocol

## Frozen method conditions

| Mode | Plane | Depth position fusion | Temporal state/filtering | Depth occlusion + fade |
|---|---:|---:|---:|---:|
| A0 | On | Off | Off | Off |
| A1 | On | On | Off | Off |
| A2 | On | On | On | Off |
| A3 | On | On | On | On |

The `Custom` mode is for diagnosis only and must not be mixed with canonical A0–A3
paper results. Debug overlay and logging are independent switches.

## Pilot

Run 3–5 pilot trials before formal collection. Start with one opaque, rigid,
non-reflective, stationary object that YOLO detects reliably: an opaque mug, plastic
bottle or chair. Do not begin with transparent glass or a complex multi-object scene.

For every pilot:

1. Confirm `session.json` contains the intended commit, device and A-mode.
2. Confirm CSV validation passes.
3. Confirm one YOLO result produces one processing row for that track/frame.
4. Confirm A0 retains plane-only placement when Depth is unavailable/disabled.
5. Confirm A1 has no temporal confirmation/smoothing.
6. Confirm A2 preserves same-class spatial multi-instance tracks.
7. Confirm A3 uses environment occlusion or the documented fade fallback.

## Formal matrix

- A0, A1, A2 and A3: at least 10 trials per condition; 20 recommended for paper results.
- Distances: 0.5, 1.0, 1.5 and 2.0 m where device support permits.
- Views: frontal, left oblique, right oblique, slightly high and slightly low.
- Occlusion: 0%, 25%, 50% and 75%.
- Lighting: normal, dim and strong/backlit.
- Motion: stationary object with slow camera motion; small object move then stop;
  short detection loss then reappearance.

Complete the single-object matrix before same-class multi-instance trials. Set
`expectedObjectCount` to the number of physical target objects in that trial.

## Frozen success definition

For the pilot, a trial is successful when all of the following hold:

- the target class is correct;
- the number of rendered target tracks never exceeds `expectedObjectCount` after acquisition;
- every expected instance reaches `Locked` within 3.0 seconds of its first valid detection;
- it remains `Locked` or `TrackingMove` for at least 2.0 consecutive seconds;
- no fused-position frame jump exceeds 0.15 m during that stable interval.

Freeze these time/jump thresholds after the pilot and before formal collection.
If independent ground truth is later added, preregister a translation-error threshold
as an additional criterion; do not retroactively tune it on the test set.

The CSV `success` field is a lower-level per-observation processing result, not the
formal trial-success label. Formal trial success is derived over the time window.

## Ground truth

Preferred ground truth is external optical tracking, an evaluation-only AprilTag
with calibrated `objectFromTag`, or a known calibration fixture. The tag/fixture must
not enter Route A inference. Without independent ground truth:

- Translation Error: `Not measured`
- full object 6DoF Rotation Error: `N/A`

ARCore's own plane pose, adjacent predictions, visual judgement and a single ruler
reading are not ground truth.

## Reported metrics

- Latency median/P95 by stage; effective inference FPS.
- Depth availability and valid-sample rates.
- Formal trial success rate with numerator/denominator.
- Duplicate-replacement frame rate relative to `expectedObjectCount`.
- Static X/Y/Z and 3D position dispersion; maximum frame jump.
- Track-loss events and reacquisition time.
- Fade/fallback activation rate.
- Circularly unwrapped output-yaw jitter and 90°/180° jump counts.
- Translation error mean, MAE, RMSE, median, SD and P95 only when valid ground truth exists.

Use `Tools/Experiments/validate_route_a_csv.py` before
`Tools/Experiments/analyze_route_a_experiments.py`. Never substitute configured
Inspector values for measured results.
