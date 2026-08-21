# Route A experiment data contract

## Output location

The logger writes to:

`Application.persistentDataPath/AR80sRetroExperiments/<session_id>/`

Each run creates a new directory. An existing session directory is never
overwritten; a `_run_<UTC timestamp>` suffix is added on collision.

- `frames.csv`: buffered per-inference/per-track observations
- `session.json`: immutable configuration and device metadata for the run

CSV serialization uses UTF-8 without BOM, `InvariantCulture`, newline-safe quoting
and doubled embedded quotes. Disk appends run on a background task and start only
after the measured pipeline has emitted its record. Logging is off by default.

## Observation semantics

One successful detection produces one row. A failed detector cycle produces a
failure row. In A2/A3, an existing track not matched in the current inference cycle
also produces a `Derived` row with `failure_reason=track_unmatched_this_cycle`; this
makes Lost and reacquisition transitions observable without changing the state
machine.

Core groups:

- Identity and controlled conditions: session/trial/object/scene/condition IDs,
  expected object count, distance, view, occlusion percentage, lighting and ablation.
- Detector: class, confidence and normalized bbox.
- Depth: whether sampling was attempted, availability, valid count, capacity,
  median metres, confidence-image availability and confidence.
- Geometry: plane success/position, depth world point and fused position.
- Output: category placement yaw, scale, track ID/state and renderer opacity.
- Timing: capture, full YOLO, output readback, depth, raycast/fusion, tracking and total.
- Fallback: render-depth usability and fade activation.
- Outcome: technical per-row success and a machine-readable failure reason.

An empty numeric field means the value was not observed for that row; zero is not
used as a substitute for missing data. When ARCore supplies no confidence image,
`depth_confidence_available=false` and `depth_confidence` is empty.

## Timing boundaries

- `capture_latency_ms`: acquire CPU image, convert, rotate and upload the inference texture.
- `yolo_latency_ms`: texture-to-tensor conversion, `Schedule`, blocking output availability/readback and parsing/NMS.
- `output_readback_latency_ms`: the readback subset of YOLO latency; do not add it to YOLO again.
- `depth_latency_ms`: acquire depth/confidence CPU images and sample/filter/sort the local grid.
- `raycast_fusion_latency_ms`: AR plane raycast plus numeric fusion/placement rotation, excluding depth sampling.
- `tracking_latency_ms`: spatial match and track/instance update only.
- `total_latency_ms`: capture start through completion of all replacement processing, before CSV serialization or disk I/O.

All timers use `System.Diagnostics.Stopwatch.GetTimestamp()`.

## Source labels

- `Measured`: directly observed by the running pipeline.
- `Derived`: state snapshot or statistic computed from measured records.
- `Configured`: Inspector/session setting, never presented as a measurement.
- `Not measured`: valid metric with no available measurement, such as translation error without ground truth.
- `Invalid`: excluded after validation or protocol violation.

## Session manifest

`session.json` stores the commit field supplied before build, Unity/package versions,
phone/OS/GPU information (but no device-unique identifier), model/backend/input and
detector thresholds, Depth settings, fusion weights, tracking/fade thresholds,
all per-category placement/scale rules, effective A0–A3 flags and scene notes.

`build_commit_sha=UNSET` is invalid for formal collection. The validator deliberately
rejects it.
