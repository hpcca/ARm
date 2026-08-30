# Route A evaluation-only AprilTag reference protocol

## Scope and isolation

The AprilTag path is an evaluation instrument, not a Route A input. Its output must
never be read by YOLO, plane/depth fusion, temporal matching, replacement spawning,
or lost-track logic. Route A remains the markerless `YOLO + AR Plane/Depth` method.

The reference stream is therefore written separately:

- `frames.csv`: Route A observations and rendered-output pose.
- `reference_poses.csv`: asynchronous AprilTag reference observations.
- `reference_config.json`: immutable tag, rigid-transform, validity-gate, camera
  coordinate-transform, and calibration provenance for the run. Corrected builds
  write `route_a_apriltag_reference_v2`.

Join the streams offline by the nearest `monotonic_timestamp_ms`. For the static
pilot, reject a joined pair when the absolute timestamp separation exceeds 150 ms.
This implementation is not yet accepted for dynamic ground truth.

## Selected acquisition implementation

Use `jp.keijiro.apriltag` 1.0.3 with `tagStandard41h12`. The upstream package
supports Unity 2021.3+ and Android arm64. Route A uses Unity 2022.3.62f3.

Important limitation: the 1.0.3 pose API receives one field-of-view value and
internally assumes `fx=fy` and a principal point at the image centre. The recorder
uses an aspect-preserving CPU image, derives vertical FOV from AR Foundation
intrinsics, records the full intrinsics, and rejects samples outside configured
intrinsics gates. Passing the gates does not prove absolute accuracy; the physical
validation below is still mandatory.

The GT Player Settings use **Target Architectures = ARM64 only**, because the
selected native plugin provides Android arm64 binaries. The display is locked to
upright Portrait so the CPU-image rotation, screen coordinates, and pose frame do
not change between trials.

The recorder converts the provider pose from its mirrored CPU-image frame into the
Unity AR Camera frame before any `^C T_T` or `^C T_O` value is logged. For the
configured `Clockwise90` camera frame this is a fixed +90 degree rotation about
camera Z. The same camera-frame provider is used to invert screen/crop/rotation
coordinates before sampling the raw environment-depth CPU image. Both transform
IDs and the screen orientation are written to the manifests. These corrections
remain subject to the physical axis/depth test on the target phone.

## Transform notation and frames

`^A T_B` maps coordinates expressed in frame B into frame A.

- C: physical RGB camera frame used by the AprilTag pose estimate.
- W: Unity/AR Foundation world frame.
- T: AprilTag frame at the tag centre.
- O: physical object frame.
- M: instantiated virtual-model Transform frame.

For the selected provider's returned pose, define T operationally as the tag-centre
frame with +X toward the printed image's right, +Y toward its top, and +Z into the
tag board. Mark the print's top and right before cutting it. This convention must
still pass the physical sign test before the reference is accepted.

For the cup pilot, define O as:

- origin: centre of the cup's bottom contact area;
- +Y: upward along the cup axis;
- +Z: from the cup axis toward the handle;
- +X: the remaining Unity-convention horizontal axis.

Configure the measured rigid transform `^T T_O` in the recorder as
`tagFromObjectPositionMeters` and `tagFromObjectEulerDegrees`. The reference is:

`^C T_O,gt = ^C T_T * ^T T_O`

and the world representation used for convenient logging is:

`^W T_O,gt = ^W T_C * ^C T_O,gt`.

For Route A, position error should primarily use the algorithmic anchor
`fused_position_*`, with O chosen to match the cup-bottom contact centre. The
rendered model's `output_position_*` and `output_rotation_*` are logged separately.
This separation prevents prefab pivot/local-axis error from being mislabelled as
Route A position error. Before yaw analysis, determine one fixed `^M T_O` (or its
inverse) for the cup prefab; do not rotate the model post hoc per trial.
The recorder stores `^M T_O` as metadata only. Its translation is expressed in
unscaled prefab-local units, so offline rendered-pose reconstruction must include
the logged output scale:

`^W T_O,rendered = ^W T_M(output TRS) * ^M T_O`.

## Printed tag and rigid fixture

Initial pilot specification:

- family and ID: `tagStandard41h12`, ID 0;
- measured detection-edge length: 80.0 mm;
- white quiet margin: at least 20 mm on every side;
- substrate: flat, matte card bonded to rigid foam board/acrylic/aluminium;
- print mode: 100% / actual size, with all fit-to-page scaling disabled.

The AprilTag size is the distance between detection corners (the black/white border
transition), not the paper or white-margin outside dimension. Measure both horizontal
and vertical detection-edge lengths with a caliper at three locations. Record the
mean and maximum deviation; do not proceed if the tag is visibly warped or the two
axes differ by more than 0.2 mm.

Mount the tag face-up beside the cup on one rigid turntable plate. Use a cup locating
ring and a handle stop so the cup cannot translate or twist relative to the tag. Mark
the fixture's 0/90/180/270 degree detents. A face-up tag remains visible at all four
yaws when the camera views the plate obliquely from above.

## Calibration procedure

1. Mark the tag centre and its printed axes on the fixture.
2. Establish O with a fitted circle/centering jig at the cup bottom and a handle stop.
3. Measure the tag-centre-to-O translation in the tag axes. Use a rigid ruler, square,
   or caliper; record the method and uncertainty.
4. Determine the axis mapping for `^T T_O` using a visible triad, not Euler-angle
   guessing. Enter the resulting transform once and assign a non-`UNSET`
   `calibrationId`.
5. Establish a separate model-to-object transform: inspect the prefab pivot, identify
   the model handle direction, and record the fixed translation/yaw offset needed to
   map M to O. Enter it in the recorder with a non-`UNSET` model-alignment ID.
   Never tune this offset from the GT error results.
6. Run the axis sanity test: move/rotate the fixture in one controlled direction at a
   time and confirm the logged tag/object axes change with the expected sign.
7. Validate at a measured camera-to-tag distance and at the four yaw detents. The
   AprilTag reference remains provisional until repeatability and ruler/turntable
   checks pass.

Recommended initial operational-reference gates at the 1.0 m pilot distance are:

- intrinsics gate passes for every accepted sample;
- successful reference availability at least 80% while the tag is deliberately visible;
- static position standard deviation no greater than 8 mm per configuration;
- static yaw circular standard deviation no greater than 2 degrees;
- median measured-distance absolute error no greater than 20 mm;
- median detent yaw absolute error no greater than 3 degrees.

These are instrument-acceptance gates, not Route A success thresholds. If they fail,
label AprilTag results provisional and do not report Route A translation/yaw accuracy.
The next escalation is an intrinsics-aware package fork or a calibrated multi-tag
board, not Route A algorithm tuning.

## Unity setup

1. Let Unity resolve `jp.keijiro.apriltag` 1.0.3 from `Packages/manifest.json`.
2. On the existing `AR 80s Retro System` object, add
   `AprilTagGroundTruthRecorder`.
3. Assign the experiment config/logger, the AR Camera's `ARCameraManager`, the AR
   Camera component, and the shared `ARCameraFrameProvider`. Also assign that frame
   provider to `ARDepthFrameProvider`.
4. Enter the measured tag size, target ID, `^T T_O`, calibration ID, `^M T_O`,
   model-alignment ID, and uncertainties.
5. Keep the default 0.25 s sampling interval and decimation 2 for the first pilot.
6. Confirm camera rotation is `Clockwise90`, Portrait is locked, and Android Target
   Architectures is ARM64 only; then rebuild the APK.

AprilTag CPU-image conversion and native detection add runtime load. Use these
instrumented runs for controlled pose accuracy. Keep the existing marker-free runs
as the primary latency evidence, or run a paired instrumentation-on/off overhead
check before interpreting GT-run latency.

Do not run a scene-setup menu while `SampleScene.unity` has unrelated uncommitted
work; adding the component is an intentional scene edit and should be reviewed on
its own.

## Minimum single-cup pilot

First rebuild and repeat the short translation-axis/depth validation. Do not rotate
the phone or manually compensate its view: hold it upright in Portrait. Only after
that run shows correct signs and an approximately 1.0 m tag/depth distance should
the controlled pilot begin.

Use one cup, one distance (1.0 m), normal lighting, no occlusion, and a stationary
camera after framing. For yaw 0, 90, 180, and 270 degrees:

1. place the turntable on the detent;
2. fully restart the app/AR session;
3. collect at least 10 s after both Route A and the tag reference stabilize;
4. stop and save the independent run;
5. repeat three times.

This produces 12 independent runs. Runs, not CSV rows, are the statistical repeat
units. First inspect axes, units, matrix direction, timestamp pairing, and reference
validity. Do not expand to three objects/distances until those checks pass.

Validate each pulled run with:

```text
python Tools/Experiments/validate_route_a_csv.py frames.csv --session-json session.json
python Tools/Experiments/validate_apriltag_reference.py reference_poses.csv --config-json reference_config.json
```

For the first paper result, report 3D/X/Y/Z/depth and controlled yaw error only.
Pitch/roll remain `Not measured` unless the fixture explicitly controls and validates
them. Static accuracy and later dynamic reacquisition/stability remain separate tests.
