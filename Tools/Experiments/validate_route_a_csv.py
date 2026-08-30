#!/usr/bin/env python3
"""Validate Route A experiment CSV/session JSON without third-party packages."""

from __future__ import annotations

import argparse
import csv
import json
import math
import re
import tempfile
from collections import defaultdict
from pathlib import Path


REQUIRED_COLUMNS = [
    "session_id", "trial_id", "frame_id", "timestamp_ms", "cycle_monotonic_ms", "object_id",
    "expected_object_count",
    "class_label", "scene_id", "condition_id", "distance_condition",
    "view_condition", "occlusion_percent", "lighting_condition",
    "ablation_mode", "yolo_confidence", "bbox_x", "bbox_y", "bbox_width",
    "bbox_height", "depth_fusion_enabled", "temporal_tracking_enabled",
    "occlusion_fade_enabled", "depth_sampling_attempted", "depth_available",
    "depth_valid_sample_count", "depth_sample_capacity", "depth_median_m",
    "depth_confidence_available", "depth_confidence",
    "plane_raycast_success",
    "plane_position_x", "plane_position_y", "plane_position_z", "depth_world_x",
    "depth_world_y", "depth_world_z", "fused_position_x", "fused_position_y",
    "fused_position_z", "camera_world_position_x", "camera_world_position_y",
    "camera_world_position_z", "camera_world_rotation_x", "camera_world_rotation_y",
    "camera_world_rotation_z", "camera_world_rotation_w", "output_position_x",
    "output_position_y", "output_position_z", "output_rotation_x", "output_rotation_y",
    "output_rotation_z", "output_rotation_w", "output_yaw_deg", "output_scale_x",
    "output_scale_y", "output_scale_z", "track_id", "track_state", "capture_latency_ms",
    "yolo_latency_ms", "output_readback_latency_ms", "depth_latency_ms",
    "raycast_fusion_latency_ms", "tracking_latency_ms", "total_latency_ms",
    "depth_occlusion_usable", "fade_fallback_active", "output_opacity",
    "success", "failure_reason", "result_source",
]

REQUIRED_SESSION_KEYS = {
    "session_id", "expected_object_count", "build_commit_sha", "unity_version", "ar_foundation_version",
    "arcore_version", "sentis_version", "device_model", "operating_system",
    "graphics_device_name", "inference_backend", "yolo_model_identifier",
    "input_width", "input_height", "confidence_threshold", "iou_threshold",
    "max_detections", "inference_interval_seconds", "depth_mode",
    "depth_temporal_smoothing", "depth_sample_grid_size", "depth_min_m",
    "depth_max_m", "depth_confidence_threshold", "depth_horizontal_fusion_weight",
    "reacquire_match_radius_m", "duplicate_radius_m", "movement_dead_zone_m",
    "movement_confirmation_frames", "movement_smooth_time_seconds",
    "depth_startup_grace_seconds", "fade_delay_seconds", "fade_duration_seconds",
    "fade_minimum_opacity", "replacement_rules", "scene_description",
}

BOOLEAN_COLUMNS = {
    "depth_fusion_enabled", "temporal_tracking_enabled", "occlusion_fade_enabled",
    "depth_sampling_attempted", "depth_available", "depth_confidence_available",
    "plane_raycast_success", "depth_occlusion_usable",
    "fade_fallback_active", "success",
}

NUMERIC_COLUMNS = {
    "frame_id", "timestamp_ms", "cycle_monotonic_ms", "expected_object_count", "occlusion_percent", "yolo_confidence", "bbox_x",
    "bbox_y", "bbox_width", "bbox_height", "depth_valid_sample_count",
    "depth_sample_capacity",
    "depth_median_m", "depth_confidence", "plane_position_x", "plane_position_y",
    "plane_position_z", "depth_world_x", "depth_world_y", "depth_world_z",
    "fused_position_x", "fused_position_y", "fused_position_z",
    "camera_world_position_x", "camera_world_position_y", "camera_world_position_z",
    "camera_world_rotation_x", "camera_world_rotation_y", "camera_world_rotation_z",
    "camera_world_rotation_w", "output_position_x", "output_position_y",
    "output_position_z", "output_rotation_x", "output_rotation_y", "output_rotation_z",
    "output_rotation_w", "output_yaw_deg",
    "output_scale_x", "output_scale_y", "output_scale_z", "track_id",
    "capture_latency_ms", "yolo_latency_ms", "output_readback_latency_ms",
    "depth_latency_ms", "raycast_fusion_latency_ms", "tracking_latency_ms",
    "total_latency_ms", "output_opacity",
}

LATENCY_COLUMNS = {
    "capture_latency_ms", "yolo_latency_ms", "output_readback_latency_ms",
    "depth_latency_ms", "raycast_fusion_latency_ms", "tracking_latency_ms",
    "total_latency_ms",
}

EXPECTED_ABLATIONS = {
    "A0": (False, False, False),
    "A1": (True, False, False),
    "A2": (True, True, False),
    "A3": (True, True, True),
}

RESULT_SOURCES = {"Measured", "Derived", "Configured", "Not measured", "Invalid"}


def parse_bool(value: str) -> bool:
    normalized = value.strip().lower()
    if normalized == "true":
        return True
    if normalized == "false":
        return False
    raise ValueError(f"expected true/false, got {value!r}")


def validate(csv_path: Path, session_path: Path | None) -> tuple[list[str], dict[str, int]]:
    errors: list[str] = []
    counts: dict[str, int] = defaultdict(int)
    seen_track_rows: set[tuple[str, ...]] = set()
    frame_timestamps: dict[tuple[str, str, int], int] = {}

    with csv_path.open("r", encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        actual_columns = reader.fieldnames or []
        missing = [name for name in REQUIRED_COLUMNS if name not in actual_columns]
        unexpected = [name for name in actual_columns if name not in REQUIRED_COLUMNS]
        if missing:
            errors.append(f"missing columns: {', '.join(missing)}")
        if unexpected:
            errors.append(f"unexpected columns: {', '.join(unexpected)}")

        for line_number, row in enumerate(reader, start=2):
            counts["rows"] += 1
            prefix = f"line {line_number}"

            for column in BOOLEAN_COLUMNS:
                try:
                    parse_bool(row.get(column, ""))
                except ValueError as exc:
                    errors.append(f"{prefix}: {column}: {exc}")

            for column in NUMERIC_COLUMNS:
                value = row.get(column, "").strip()
                if not value:
                    continue
                try:
                    number = float(value)
                    if not math.isfinite(number):
                        raise ValueError("not finite")
                except ValueError:
                    errors.append(f"{prefix}: {column} is not a finite number: {value!r}")

            mode = row.get("ablation_mode", "")
            if mode in EXPECTED_ABLATIONS:
                try:
                    actual_flags = (
                        parse_bool(row["depth_fusion_enabled"]),
                        parse_bool(row["temporal_tracking_enabled"]),
                        parse_bool(row["occlusion_fade_enabled"]),
                    )
                    if actual_flags != EXPECTED_ABLATIONS[mode]:
                        errors.append(
                            f"{prefix}: {mode} flags {actual_flags} do not match "
                            f"{EXPECTED_ABLATIONS[mode]}"
                        )
                except (KeyError, ValueError):
                    pass

            if row.get("result_source", "") not in RESULT_SOURCES:
                errors.append(
                    f"{prefix}: invalid result_source {row.get('result_source', '')!r}"
                )

            try:
                confidence = float(row["yolo_confidence"]) if row["yolo_confidence"] else None
                bbox_values = [
                    float(row[field]) if row[field] else None
                    for field in ("bbox_x", "bbox_y", "bbox_width", "bbox_height")
                ]
                opacity = float(row["output_opacity"]) if row["output_opacity"] else None
                depth_confidence = (
                    float(row["depth_confidence"]) if row["depth_confidence"] else None
                )
                if confidence is not None and not 0 <= confidence <= 1:
                    errors.append(f"{prefix}: yolo_confidence is outside [0, 1]")
                if any(value is not None and not 0 <= value <= 1 for value in bbox_values):
                    errors.append(f"{prefix}: normalized bbox value is outside [0, 1]")
                if opacity is not None and not 0 <= opacity <= 1:
                    errors.append(f"{prefix}: output_opacity is outside [0, 1]")
                if depth_confidence is not None and not 0 <= depth_confidence <= 255:
                    errors.append(f"{prefix}: depth_confidence is outside [0, 255]")
                occlusion_percent = float(row["occlusion_percent"])
                if not 0 <= occlusion_percent <= 100:
                    errors.append(f"{prefix}: occlusion_percent is outside [0, 100]")
            except (KeyError, ValueError):
                pass

            try:
                frame_id = int(row["frame_id"])
                timestamp_ms = int(row["timestamp_ms"])
                frame_key = (row["session_id"], row["trial_id"], frame_id)
                previous = frame_timestamps.setdefault(frame_key, timestamp_ms)
                if previous != timestamp_ms:
                    errors.append(f"{prefix}: one frame_id has multiple timestamps")
            except (KeyError, ValueError):
                pass

            track_id = row.get("track_id", "").strip()
            if track_id:
                duplicate_key = (
                    row.get("session_id", ""), row.get("trial_id", ""),
                    row.get("frame_id", ""), track_id,
                )
                if duplicate_key in seen_track_rows:
                    errors.append(
                        f"{prefix}: duplicate processing row for session/trial/frame/track "
                        f"{duplicate_key}"
                    )
                seen_track_rows.add(duplicate_key)

            try:
                total = float(row["total_latency_ms"])
                latency_values = {
                    field: float(row[field])
                    for field in LATENCY_COLUMNS
                    if row.get(field, "")
                }
                if any(value < 0 for value in latency_values.values()):
                    errors.append(f"{prefix}: latency values must be non-negative")
                staged = sum(
                    latency_values.get(field, 0.0)
                    for field in (
                        "capture_latency_ms", "yolo_latency_ms", "depth_latency_ms",
                        "raycast_fusion_latency_ms", "tracking_latency_ms",
                    )
                )
                if total + 0.25 < staged:
                    errors.append(
                        f"{prefix}: total latency {total} ms is below recorded sequential stages {staged} ms"
                    )
                readback = latency_values.get("output_readback_latency_ms", 0.0)
                yolo = latency_values.get("yolo_latency_ms", 0.0)
                if readback > yolo + 0.25:
                    errors.append(f"{prefix}: output readback exceeds full YOLO latency")
            except (KeyError, ValueError):
                pass

            if (
                row.get("depth_fusion_enabled", "").lower() == "false"
                and row.get("depth_sampling_attempted", "").lower() == "true"
            ):
                errors.append(f"{prefix}: depth sampling occurred while fusion was disabled")
            if (
                row.get("occlusion_fade_enabled", "").lower() == "false"
                and row.get("fade_fallback_active", "").lower() == "true"
            ):
                errors.append(f"{prefix}: fade fallback activated while the feature was disabled")
            if (
                row.get("temporal_tracking_enabled", "").lower() == "false"
                and row.get("failure_reason", "").strip() == "below_tracking_confidence"
            ):
                errors.append(
                    f"{prefix}: below_tracking_confidence is invalid when temporal tracking is disabled"
                )

            success = row.get("success", "").lower() == "true"
            if success:
                counts["successful_rows"] += 1
                if row.get("failure_reason", "").strip():
                    errors.append(f"{prefix}: successful row has a failure_reason")
            elif not row.get("failure_reason", "").strip():
                errors.append(f"{prefix}: failed row has no failure_reason")

            if row.get("depth_available", "").lower() == "true":
                counts["depth_available_rows"] += 1
                if not row.get("depth_median_m", "").strip():
                    errors.append(f"{prefix}: depth_available row has no depth_median_m")
                confidence_available = row.get("depth_confidence_available", "").lower() == "true"
                has_confidence_value = bool(row.get("depth_confidence", "").strip())
                if confidence_available and not has_confidence_value:
                    errors.append(
                        f"{prefix}: confidence image is available but depth_confidence is empty"
                    )
                if not confidence_available and has_confidence_value:
                    errors.append(
                        f"{prefix}: depth_confidence must be empty when no confidence image exists"
                    )

            try:
                valid_samples = int(row.get("depth_valid_sample_count", "0") or 0)
                sample_capacity = int(row.get("depth_sample_capacity", "0") or 0)
                attempted = row.get("depth_sampling_attempted", "").lower() == "true"
                available = row.get("depth_available", "").lower() == "true"
                if valid_samples < 0 or sample_capacity < 0 or valid_samples > sample_capacity:
                    errors.append(
                        f"{prefix}: invalid depth sample count {valid_samples}/{sample_capacity}"
                    )
                if available and valid_samples == 0:
                    errors.append(f"{prefix}: depth_available row has zero valid samples")
                if not attempted and (available or valid_samples > 0):
                    errors.append(f"{prefix}: depth values exist although sampling was not attempted")
            except ValueError:
                pass

    if counts["rows"] == 0:
        errors.append("CSV contains no data rows")

    ordered_frames = sorted(frame_timestamps.items(), key=lambda item: item[0])
    last_by_trial: dict[tuple[str, str], tuple[int, int]] = {}
    for (session_id, trial_id, frame_id), timestamp_ms in ordered_frames:
        trial_key = (session_id, trial_id)
        if trial_key in last_by_trial:
            last_frame, last_timestamp = last_by_trial[trial_key]
            if frame_id > last_frame and timestamp_ms < last_timestamp:
                errors.append(
                    f"timestamps decrease in {session_id}/{trial_id}: "
                    f"frame {last_frame} -> {frame_id}"
                )
        last_by_trial[trial_key] = (frame_id, timestamp_ms)

    if session_path is not None:
        if not session_path.is_file():
            errors.append(f"session JSON not found: {session_path}")
        else:
            with session_path.open("r", encoding="utf-8-sig") as handle:
                manifest = json.load(handle)
            missing_keys = sorted(REQUIRED_SESSION_KEYS - manifest.keys())
            if missing_keys:
                errors.append(f"session JSON missing keys: {', '.join(missing_keys)}")
            if manifest.get("build_commit_sha") in (None, "", "UNSET"):
                errors.append("session JSON build_commit_sha is not configured")
            elif not re.fullmatch(r"[0-9a-fA-F]{7,40}", str(manifest["build_commit_sha"])):
                errors.append("session JSON build_commit_sha is not a Git hexadecimal SHA")

    return errors, dict(counts)


def run_self_test() -> int:
    row = {column: "" for column in REQUIRED_COLUMNS}
    row.update({
        "session_id": "session_test", "trial_id": "trial_001", "frame_id": "1",
        "timestamp_ms": "1000", "cycle_monotonic_ms": "1000.5",
        "object_id": "cup_001", "expected_object_count": "1",
        "class_label": "cup",
        "scene_id": "scene_001", "condition_id": "baseline",
        "distance_condition": "1.0m", "view_condition": "frontal",
        "occlusion_percent": "0", "lighting_condition": "normal",
        "ablation_mode": "A3", "yolo_confidence": "0.9", "bbox_x": "0.1",
        "bbox_y": "0.2", "bbox_width": "0.3", "bbox_height": "0.4",
        "depth_fusion_enabled": "true", "temporal_tracking_enabled": "true",
        "occlusion_fade_enabled": "true", "depth_sampling_attempted": "true",
        "depth_available": "true", "depth_valid_sample_count": "25",
        "depth_sample_capacity": "25", "depth_median_m": "1.0",
        "depth_confidence_available": "true", "depth_confidence": "255",
        "plane_raycast_success": "true",
        "plane_position_x": "0", "plane_position_y": "0", "plane_position_z": "1",
        "depth_world_x": "0", "depth_world_y": "0.1", "depth_world_z": "1",
        "fused_position_x": "0", "fused_position_y": "0", "fused_position_z": "1",
        "camera_world_position_x": "0", "camera_world_position_y": "1",
        "camera_world_position_z": "0", "camera_world_rotation_x": "0",
        "camera_world_rotation_y": "0", "camera_world_rotation_z": "0",
        "camera_world_rotation_w": "1", "output_position_x": "0",
        "output_position_y": "0", "output_position_z": "1",
        "output_rotation_x": "0", "output_rotation_y": "1",
        "output_rotation_z": "0", "output_rotation_w": "0",
        "output_yaw_deg": "180", "output_scale_x": "1", "output_scale_y": "1",
        "output_scale_z": "1", "track_id": "1", "track_state": "Locked",
        "capture_latency_ms": "2", "yolo_latency_ms": "10",
        "output_readback_latency_ms": "4", "depth_latency_ms": "1",
        "raycast_fusion_latency_ms": "2", "tracking_latency_ms": "1",
        "total_latency_ms": "17", "depth_occlusion_usable": "true",
        "fade_fallback_active": "false", "output_opacity": "1", "success": "true",
        "failure_reason": "", "result_source": "Measured",
    })
    with tempfile.TemporaryDirectory() as directory:
        csv_path = Path(directory) / "frames.csv"
        with csv_path.open("w", encoding="utf-8", newline="") as handle:
            writer = csv.DictWriter(handle, fieldnames=REQUIRED_COLUMNS)
            writer.writeheader()
            writer.writerow(row)
        valid_errors, counts = validate(csv_path, None)
        row.update({
            "ablation_mode": "A1",
            "temporal_tracking_enabled": "false",
            "occlusion_fade_enabled": "false",
            "success": "false",
            "failure_reason": "below_tracking_confidence",
        })
        with csv_path.open("w", encoding="utf-8", newline="") as handle:
            writer = csv.DictWriter(handle, fieldnames=REQUIRED_COLUMNS)
            writer.writeheader()
            writer.writerow(row)
        invalid_errors, _ = validate(csv_path, None)
    if valid_errors or counts.get("rows") != 1:
        print("SELF-TEST FAILED")
        for error in valid_errors:
            print(f"- {error}")
        return 1
    if not any(
        "below_tracking_confidence is invalid when temporal tracking is disabled" in error
        for error in invalid_errors
    ):
        print("SELF-TEST FAILED")
        print("- validator accepted a tracking-only failure reason for A1")
        return 1
    print("SELF-TEST PASSED")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("csv", type=Path, nargs="?")
    parser.add_argument("--session-json", type=Path)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        return run_self_test()
    if args.csv is None:
        parser.error("csv is required unless --self-test is used")
    session_path = args.session_json
    if session_path is None:
        inferred = args.csv.with_name("session.json")
        session_path = inferred if inferred.exists() else None
    errors, counts = validate(args.csv, session_path)
    print(json.dumps({"valid": not errors, "counts": counts, "errors": errors}, indent=2))
    return 0 if not errors else 1


if __name__ == "__main__":
    raise SystemExit(main())
