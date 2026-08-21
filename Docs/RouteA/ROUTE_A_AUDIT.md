# Route A code and scene audit

Audit date: 2026-08-21

## Scope and provenance

- Isolated worktree: `F:\sem4\AR\chore\ARm-route-a-evaluation`
- Experiment branch: `feature/route-a-evaluation`
- Required base: `36bc1672c59520a935ea6806bdadd7ecb31c9b3f`
- Existing Route A fade baseline integrated as its direct child:
  `70ffa4591ce75ea3a37a9880f54c56ca2b22f719`
- No code or asset was taken from `dev-ar`.
- No PCA is implemented. The method does not estimate full object 6DoF.

The original `F:\sem4\AR\chore\ARm` worktree was treated as read-only. Its
FoundationPose/BundleSDF content, running processes, scans, results, packages and
project settings were not changed.

## Technical baseline

- Unity 2022.3.62f3, URP 14.0.12
- Sentis 2.1.3
- AR Foundation 5.2.0 and ARCore XR Plugin 5.2.0
- Eight COCO classes: bottle, cup, chair, couch, plant, table, tv and phone
- Maximum 12 detections per inference cycle
- No Route A asmdef; scripts retain the existing default-assembly layout
- Existing `RetroPrefabLibrary` ScriptableObject and explicit Inspector references are preserved

## Data and event flow

The production path is:

`ARCameraFrameProvider` → `YoloObjectDetector.DetectionsReady` →
`RetroDetectionPipeline` → `RetroReplacementManager.ApplyDetections`

`YoloDetectionOverlay` separately observes `DetectionsReady` and never calls the
replacement manager. `MockDetectionInput` and `CameraFrameSmokeTest` are disabled
in `SampleScene`. No second production call to `ApplyDetections` was found.

The experiment logger observes only `RetroDetectionPipeline.ExperimentRecordReady`.
It does not invoke the detector, solver or replacement manager and therefore cannot
change algorithm outputs.

## Position, orientation and scale

- The detection bbox is converted from model image coordinates to normalized screen coordinates.
- The category rule chooses a bbox anchor for the AR plane raycast.
- When enabled, a 5×5 local Environment Depth sample is confidence-filtered and reduced by median depth.
- Fused X/Z are a weighted plane/depth combination; Y remains the AR plane height.
- Missing or invalid depth leaves the original plane pose unchanged.
- Rotation is the AR plane placement rotation plus a category calibration offset.
- Scale is estimated from bbox size and a category calibration rule.

All category rotation offsets are finite. The prefab quaternion `(0, 1, 0, 0)`
found in the TV asset is a valid 180-degree rotation, not an invalid quaternion.

## Temporal multi-instance behavior

The production state machine remains `Searching`, `Acquiring`, `Locked`,
`TrackingMove`, `Lost`. Same-class detections are matched spatially and
`LastMatchedFrame` prevents one track from receiving two detections in one Unity
frame. Confirmation, movement dead-zone, movement confirmation, smoothing, lost
delay and reacquisition radius remain intact in A2/A3.

For the A0/A1 ablation, the temporal state machine and filters are bypassed. A
current detection directly creates or updates an instance, and unmatched instances
are removed at the end of that inference cycle. Nearest-instance reuse is retained
only to avoid destroy/recreate churn inside a current representation; it does not
filter the measured pose or preserve an unmatched result.

## Occlusion and fade

`AROcclusionManager` is on the same AR Camera GameObject as `Camera` and
`ARCameraBackground`. A3 requests Environment Depth occlusion. When renderable
environment depth is unavailable after the startup grace, renderer opacity follows
the existing fallback configuration:

- depth startup grace: 2.0 s
- depth availability grace: 0.5 s
- fade delay: 0.35 s
- fade duration: 0.35 s
- minimum opacity: 0.2

A0–A2 request `NoOcclusion`; A0 also disables Environment Depth acquisition, while
A1/A2 keep Environment Depth available for position fusion.

## Validation status

- Unity runtime and Editor scripts compiled with zero C# errors.
- `SampleScene` contains no Missing Script.
- Every experiment dependency is explicitly assigned.
- Exactly one `AROcclusionManager` is present and it is attached to the AR Camera.
- CSV header/record column count, quoting and invariant-number formatting are checked in the Editor validator.
- A0–A3 feature mappings are checked in the Editor validator.
- Python CSV validator self-test passes.

Android ARCore behavior, device-specific depth support and measured performance are
not yet validated; those require the physical-device pilot.
