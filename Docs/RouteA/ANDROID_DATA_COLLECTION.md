# Android pilot data collection

## Before building

Use only `F:\sem4\AR\chore\ARm-route-a-evaluation` in Unity 2022.3.62f3.

1. Commit or otherwise freeze the exact code that will be built.
2. Copy `git rev-parse HEAD` into `AR80sRetro System > ARReplacementExperimentConfig > Session > Build Commit Sha`.
3. Confirm the worktree has no unintended code/scene changes after the frozen commit.
4. Set a unique session ID and the current trial ID, object ID, scene ID and condition ID.
5. Set distance/view/occlusion/lighting exactly as physically arranged.
6. Set `expectedObjectCount` and select canonical A0, A1, A2 or A3.
7. Enable logging. Disable the debug overlay for formal timing unless overlay-on is the declared condition.
8. Run `Tools > AR 80s Retro > Experiments > Validate Scene Wiring`.
9. Build a Development APK for the first pilot; do not include raw videos or logs in Git.

The logger uses the app-specific `Application.persistentDataPath`, so it requests no
broad external-storage permission.

## Per trial

1. Close other thermally intensive phone apps and record phone temperature state if available.
2. Place one opaque target at the measured distance and prescribed view/lighting/occlusion.
3. Start the app and allow AR tracking/depth to stabilize for at least two seconds.
4. Begin the defined motion sequence; do not move the object during a stationary condition.
5. Record for the preregistered duration (recommended 10–15 s for stability trials).
6. Pause/background or close the app normally so buffered rows are flushed.
7. Change the trial ID before the next run. Do not reuse a formal session directory.

Complete 3–5 pilot trials and inspect the files before scaling to the full matrix.

## Retrieve logs

On Android, the path normally resolves to:

`/storage/emulated/0/Android/data/<application-id>/files/AR80sRetroExperiments/<session_id>/`

Use Android Studio Device Explorer or ADB for a debuggable pilot build, for example:

```text
adb pull /sdcard/Android/data/<application-id>/files/AR80sRetroExperiments/<session_id> <local-output-folder>
```

If the device restricts direct access, use Device Explorer or `run-as` for the debug
package. Do not add storage permissions just to work around Android scoped storage.

## Validate and summarize

From the repository root:

```text
python Tools/Experiments/validate_route_a_csv.py <session-folder>/frames.csv --session-json <session-folder>/session.json
python Tools/Experiments/analyze_route_a_experiments.py <session-folder>/frames.csv --output <output-folder>/condition_summary.csv
```

The validator must report `valid: true`. A missing commit SHA, malformed rows,
inconsistent A-mode flags, duplicate frame/track processing, invalid Depth samples
or contradictory success/failure fields invalidates the run until explained.

## Pilot acceptance gate

- Session JSON matches Inspector and device reality.
- No CSV corruption or missing required fields.
- Capture+YOLO does not exceed total latency.
- Depth-on rows distinguish unavailable Depth from unavailable confidence.
- A0 has no Depth sampling, temporal filtering, occlusion or fade.
- A1 samples Depth but directly follows detections.
- A2 records stable track IDs and loss/reacquisition.
- A3 records occlusion usability and fallback opacity.
- No unexplained duplicate track beyond `expectedObjectCount`.

Only after this gate should formal collection begin.
