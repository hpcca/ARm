# Route A IEEE VR draft material

## Method

Route A replaces detected physical objects with category-matched virtual assets in
mobile augmented reality. For each inference cycle, an RGB CPU image acquired from
AR Foundation is converted to a 640×640 tensor and processed by a YOLOv8n model in
Unity Sentis. Predictions are filtered to eight COCO categories and subjected to
class-wise non-maximum suppression. The selected bounding box is mapped from model
image space to the displayed screen coordinates. A ray cast from a category-defined
bounding-box anchor intersects an AR plane to provide the placement height and a
baseline 3D position.

When Environment Depth is enabled, Route A samples a 5×5 neighborhood around the
box centre. Samples outside the configured depth range or below the available depth
confidence threshold are rejected, and the median valid depth is back-projected to
world space. The final horizontal position interpolates between plane and depth X/Z;
large plane–depth disagreement reduces the depth weight. The plane Y coordinate is
retained. If depth acquisition or validation fails, the unmodified plane position is
used.

Scale is estimated from the projected box dimensions and category-specific
calibration parameters. Rotation is a placement rotation derived from the AR plane,
optionally camera facing, followed by a category-level rotation offset. It is not a
full object-pose estimate. Spatial nearest-neighbour association supports multiple
instances of the same category. Tracks pass through Searching, Acquiring, Locked,
TrackingMove and Lost states; confirmation, movement dead-zone and smoothing reduce
short-term detection noise while a reacquisition radius preserves identity.

In the complete configuration, ARCore Environment Depth supplies render occlusion.
If renderable depth remains unavailable after a startup grace period, the virtual
renderer fades toward a minimum opacity based on time since the last confident
detection and the last observation confidence. This is a visual fallback rather than
a substitute depth estimate.

## Implementation

The prototype uses Unity 2022.3.62f3, Universal Render Pipeline 14.0.12, Sentis
2.1.3, AR Foundation 5.2.0 and ARCore XR Plugin 5.2.0. YOLO inference uses a
configured Sentis backend, a confidence threshold and IoU threshold stored in the
session manifest, at most 12 detections and a configured inference interval. Local
depth uses Environment Depth Fastest, optional ARCore temporal smoothing, a 5×5
sampling grid, confidence filtering and median reduction. Algorithm components keep
explicit Inspector references. A passive logger receives diagnostics only after the
replacement pipeline has completed, serializes CSV outside the timed interval and
batches disk writes on a background task.

Four ablations reuse the same code path: A0 uses YOLO and plane ray casting; A1 adds
local depth fusion; A2 adds the temporal multi-instance state machine; and A3 adds
environment-depth render occlusion and the fade fallback. Disabling temporal logic
causes current detections to update instances directly and removes unmatched
instances at the end of the inference cycle, without confirmation, persistence or
smoothing.

## Evaluation protocol

The evaluation varies distance (0.5–2.0 m where supported), view direction,
occlusion (0–75%), lighting and controlled object/camera motion. Single opaque rigid
objects are tested before same-category multi-instance scenes. Each canonical
ablation receives at least 10 trials per condition, with 20 recommended for final
reporting. The preregistered pilot success criterion requires correct class,
no rendered tracks beyond the expected physical-object count, lock within 3 s,
at least 2 s of stable Locked/TrackingMove output and no fused-position jump above
0.15 m during that interval.

We report capture, inference (including output readback), depth, raycast/fusion,
tracking and end-to-end latency; effective inference FPS; depth availability and
valid-sample rate; trial success and duplicate rate; position/yaw stability; track
loss and reacquisition; and fallback activation. Translation error is reported only
with independent ground truth such as calibrated optical tracking or an
evaluation-only AprilTag. Full 6DoF rotation error is not applicable to Route A.

## Limitations

Route A does not reconstruct object geometry, perform point-cloud PCA, or recover a
physical object's complete 6DoF pose. Placement yaw and scale depend on category
calibration and 2D detections. Plane Y can be biased by plane estimation, while local
depth can be missing or noisy on transparent, reflective, thin or distant objects.
Spatial nearest-neighbour association can exchange identities when same-class
objects cross closely. Environment-depth support, accuracy and thermal performance
are device dependent. The fade fallback communicates uncertainty visually but does
not correct geometry. Without independent ground truth, temporal consistency must
not be presented as translation accuracy.

## Results table placeholder

| Field | Route A |
|---|---|
| Route / Method Name | Route A: YOLO + Plane + Local Depth Fusion + Temporal Replacement |
| Owner | Project Route A team |
| Input | AR RGB CPU image, AR planes, optional Environment Depth/confidence |
| Output | Category, bbox, fused 3D placement, category scale/placement rotation, track state |
| Implementation Status | Experiment instrumentation implemented; Android pilot pending |
| Test Objects / Scenes | Not measured |
| Translation Error | Not measured (no independent ground truth yet) |
| Rotation Error | N/A for full object 6DoF |
| Processing Latency | Not measured |
| FPS | Not measured |
| Success Rate | Not measured |
| Robustness | Not measured |
| Test Number | 0 formal trials |
| Strengths | Local depth/plane fallback, same-class multi-instance state, reproducible ablations |
| Limitations | Category-level orientation/scale; device-dependent depth; no full 6DoF |
| Current Measurable Results | Logger/contract/validator ready; compile and scene checks passed |
| Missing Experiments | Android pilot, full ablation matrix, robustness, optional independent ground truth |

No numerical performance or accuracy claim should replace a `Not measured` entry
until it is produced by a validated session.
