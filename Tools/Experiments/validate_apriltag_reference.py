#!/usr/bin/env python3
"""Validate the evaluation-only AprilTag reference stream and calibration manifest."""

from __future__ import annotations

import argparse
import csv
import json
import math
import tempfile
from pathlib import Path


REQUIRED_COLUMNS = [
    "session_id", "trial_id", "reference_sample_id", "timestamp_ms",
    "monotonic_timestamp_ms", "cpu_image_timestamp_s", "tag_family",
    "target_tag_id", "tag_size_m", "decimation", "image_width", "image_height",
    "intrinsics_available", "intrinsics_width", "intrinsics_height", "fx_px",
    "fy_px", "cx_px", "cy_px", "fx_fy_relative_delta",
    "principal_offset_x_fraction", "principal_offset_y_fraction",
    "intrinsics_gate_passed", "camera_world_position_x", "camera_world_position_y",
    "camera_world_position_z", "camera_world_rotation_x", "camera_world_rotation_y",
    "camera_world_rotation_z", "camera_world_rotation_w", "tag_detected",
    "detected_tag_count", "tag_camera_position_x", "tag_camera_position_y",
    "tag_camera_position_z", "tag_camera_rotation_x", "tag_camera_rotation_y",
    "tag_camera_rotation_z", "tag_camera_rotation_w", "tag_world_position_x",
    "tag_world_position_y", "tag_world_position_z", "tag_world_rotation_x",
    "tag_world_rotation_y", "tag_world_rotation_z", "tag_world_rotation_w",
    "object_gt_camera_position_x", "object_gt_camera_position_y",
    "object_gt_camera_position_z", "object_gt_camera_rotation_x",
    "object_gt_camera_rotation_y", "object_gt_camera_rotation_z",
    "object_gt_camera_rotation_w", "object_gt_world_position_x",
    "object_gt_world_position_y", "object_gt_world_position_z",
    "object_gt_world_rotation_x", "object_gt_world_rotation_y",
    "object_gt_world_rotation_z", "object_gt_world_rotation_w",
    "object_gt_world_yaw_deg", "reference_latency_ms", "success", "failure_reason",
    "result_source",
]

BOOLEAN_COLUMNS = {
    "intrinsics_available", "intrinsics_gate_passed", "tag_detected", "success",
}

NUMERIC_COLUMNS = set(REQUIRED_COLUMNS) - {
    "session_id", "trial_id", "tag_family", "intrinsics_available",
    "intrinsics_gate_passed", "tag_detected", "success", "failure_reason",
    "result_source",
}

QUATERNION_GROUPS = [
    "camera_world_rotation", "tag_camera_rotation", "tag_world_rotation",
    "object_gt_camera_rotation", "object_gt_world_rotation",
]

REQUIRED_CONFIG_KEYS = {
    "schema_version", "reference_role", "provider", "provider_version", "tag_family",
    "tag_id", "tag_size_m", "decimation", "sample_interval_s",
    "tag_from_object_position_x", "tag_from_object_position_y",
    "tag_from_object_position_z", "tag_from_object_rotation_x",
    "tag_from_object_rotation_y", "tag_from_object_rotation_z",
    "tag_from_object_rotation_w", "calibration_id", "tag_frame_definition",
    "model_alignment_id", "model_from_object_position_x",
    "model_from_object_position_y", "model_from_object_position_z",
    "model_from_object_rotation_x", "model_from_object_rotation_y",
    "model_from_object_rotation_z", "model_from_object_rotation_w",
    "model_from_object_translation_units", "rendered_object_transform_formula",
    "max_fx_fy_relative_delta",
    "max_principal_offset_fraction", "transform_notation", "object_frame_definition",
    "pose_estimator_limitation",
}

SUPPORTED_SCHEMA_VERSIONS = {
    "route_a_apriltag_reference_v1",
    "route_a_apriltag_reference_v2",
}

V2_REQUIRED_CONFIG_KEYS = {
    "screen_orientation", "camera_frame_rotation",
    "camera_coordinate_transform_id", "camera_coordinate_rotation_x",
    "camera_coordinate_rotation_y", "camera_coordinate_rotation_z",
    "camera_coordinate_rotation_w", "camera_frame_definition",
    "depth_uv_transform_id",
}


def parse_bool(value: str) -> bool:
    normalized = value.strip().lower()
    if normalized == "true":
        return True
    if normalized == "false":
        return False
    raise ValueError(f"expected true/false, got {value!r}")


def finite_float(value: str) -> float | None:
    if not value.strip():
        return None
    result = float(value)
    if not math.isfinite(result):
        raise ValueError("not finite")
    return result


def validate_quaternion(row: dict[str, str], prefix: str, group: str) -> list[str]:
    errors: list[str] = []
    values = [row.get(f"{group}_{axis}", "").strip() for axis in "xyzw"]
    if not any(values):
        return errors
    if not all(values):
        return [f"{prefix}: {group} quaternion is partially missing"]
    try:
        norm = math.sqrt(sum(float(value) ** 2 for value in values))
    except ValueError:
        return [f"{prefix}: {group} quaternion is not numeric"]
    if not 0.95 <= norm <= 1.05:
        errors.append(f"{prefix}: {group} quaternion norm {norm:.6f} is outside [0.95, 1.05]")
    return errors


def validate(csv_path: Path, config_path: Path) -> tuple[list[str], dict[str, int]]:
    errors: list[str] = []
    counts = {"rows": 0, "successful_rows": 0, "tag_detected_rows": 0}
    previous_sample: dict[tuple[str, str], tuple[int, float]] = {}

    with config_path.open("r", encoding="utf-8-sig") as handle:
        config = json.load(handle)
    missing_config = sorted(REQUIRED_CONFIG_KEYS - config.keys())
    if missing_config:
        errors.append(f"reference config missing keys: {', '.join(missing_config)}")
    schema_version = config.get("schema_version")
    if schema_version not in SUPPORTED_SCHEMA_VERSIONS:
        errors.append("reference config has an unsupported schema_version")
    if schema_version == "route_a_apriltag_reference_v2":
        missing_v2_config = sorted(V2_REQUIRED_CONFIG_KEYS - config.keys())
        if missing_v2_config:
            errors.append(
                "reference config v2 missing keys: " + ", ".join(missing_v2_config)
            )
        if config.get("screen_orientation") != "Portrait":
            errors.append("reference config v2 screen_orientation must be Portrait")
        if config.get("camera_frame_rotation") != "Clockwise90":
            errors.append("reference config v2 camera_frame_rotation must be Clockwise90")
        for key in ("camera_coordinate_transform_id", "depth_uv_transform_id"):
            if config.get(key) in (None, "", "UNSET", "Not configured"):
                errors.append(f"reference config v2 {key} is not configured")
        try:
            camera_rotation_norm = math.sqrt(sum(
                float(config[f"camera_coordinate_rotation_{axis}"]) ** 2
                for axis in "xyzw"
            ))
            if not 0.999 <= camera_rotation_norm <= 1.001:
                errors.append(
                    "reference config v2 camera coordinate rotation is not normalized"
                )
        except (KeyError, TypeError, ValueError):
            errors.append(
                "reference config v2 camera coordinate rotation is not numeric"
            )
    if config.get("reference_role") != "evaluation_only_no_algorithm_feedback":
        errors.append("reference config does not declare evaluation-only isolation")
    if config.get("calibration_id") in (None, "", "UNSET"):
        errors.append("reference config calibration_id is not configured")
    if config.get("model_alignment_id") in (None, "", "UNSET"):
        errors.append("reference config model_alignment_id is not configured")
    if float(config.get("tag_size_m", 0) or 0) <= 0:
        errors.append("reference config tag_size_m must be positive")

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
            parsed_bools: dict[str, bool] = {}
            for column in BOOLEAN_COLUMNS:
                try:
                    parsed_bools[column] = parse_bool(row.get(column, ""))
                except ValueError as exc:
                    errors.append(f"{prefix}: {column}: {exc}")

            for column in NUMERIC_COLUMNS:
                try:
                    finite_float(row.get(column, ""))
                except ValueError:
                    errors.append(f"{prefix}: {column} is not a finite number")

            try:
                sample_id = int(row["reference_sample_id"])
                monotonic_ms = float(row["monotonic_timestamp_ms"])
                key = (row["session_id"], row["trial_id"])
                if key in previous_sample:
                    previous_id, previous_ms = previous_sample[key]
                    if sample_id <= previous_id:
                        errors.append(f"{prefix}: reference_sample_id is not increasing")
                    if monotonic_ms < previous_ms:
                        errors.append(f"{prefix}: monotonic_timestamp_ms moved backwards")
                previous_sample[key] = (sample_id, monotonic_ms)
            except (KeyError, ValueError):
                pass

            for group in QUATERNION_GROUPS:
                errors.extend(validate_quaternion(row, prefix, group))

            tag_detected = parsed_bools.get("tag_detected", False)
            gate_passed = parsed_bools.get("intrinsics_gate_passed", False)
            success = parsed_bools.get("success", False)
            if tag_detected:
                counts["tag_detected_rows"] += 1
            if success:
                counts["successful_rows"] += 1
                if not tag_detected or not gate_passed:
                    errors.append(f"{prefix}: successful reference lacks tag/gate validity")
                if row.get("failure_reason", "").strip():
                    errors.append(f"{prefix}: successful reference has a failure_reason")
                required_pose_fields = [
                    "object_gt_camera_position_x", "object_gt_camera_position_y",
                    "object_gt_camera_position_z", "object_gt_world_yaw_deg",
                ]
                if any(not row.get(field, "").strip() for field in required_pose_fields):
                    errors.append(f"{prefix}: successful reference has missing object GT pose")
            elif not row.get("failure_reason", "").strip():
                errors.append(f"{prefix}: failed reference has no failure_reason")

            source = row.get("result_source", "")
            if source not in {"Measured", "Invalid"}:
                errors.append(f"{prefix}: invalid result_source {source!r}")
            if row.get("failure_reason") == "intrinsics_model_gate_failed" and source != "Invalid":
                errors.append(f"{prefix}: gate failure must use result_source=Invalid")

    return errors, counts


def run_self_test() -> int:
    config = {key: "test" for key in REQUIRED_CONFIG_KEYS | V2_REQUIRED_CONFIG_KEYS}
    config.update({
        "schema_version": "route_a_apriltag_reference_v2",
        "reference_role": "evaluation_only_no_algorithm_feedback",
        "provider": "jp.keijiro.apriltag",
        "provider_version": "1.0.3",
        "tag_family": "tagStandard41h12",
        "tag_id": 0,
        "tag_size_m": 0.08,
        "decimation": 2,
        "sample_interval_s": 0.25,
        "tag_from_object_position_x": 0.1,
        "tag_from_object_position_y": 0.0,
        "tag_from_object_position_z": 0.0,
        "tag_from_object_rotation_x": 0.0,
        "tag_from_object_rotation_y": 0.0,
        "tag_from_object_rotation_z": 0.0,
        "tag_from_object_rotation_w": 1.0,
        "calibration_id": "cup_fixture_v1",
        "tag_frame_definition": "test",
        "model_alignment_id": "cup_prefab_v1",
        "model_from_object_position_x": 0.0,
        "model_from_object_position_y": 0.0,
        "model_from_object_position_z": 0.0,
        "model_from_object_rotation_x": 0.0,
        "model_from_object_rotation_y": 0.0,
        "model_from_object_rotation_z": 0.0,
        "model_from_object_rotation_w": 1.0,
        "model_from_object_translation_units": "unscaled_prefab_local_units_before_output_scale",
        "rendered_object_transform_formula": "test",
        "max_fx_fy_relative_delta": 0.01,
        "max_principal_offset_fraction": 0.01,
        "screen_orientation": "Portrait",
        "camera_frame_rotation": "Clockwise90",
        "camera_coordinate_transform_id":
            "apriltag_cpu_pose_to_unity_camera_v1_Clockwise90",
        "camera_coordinate_rotation_x": 0.0,
        "camera_coordinate_rotation_y": 0.0,
        "camera_coordinate_rotation_z": math.sqrt(0.5),
        "camera_coordinate_rotation_w": math.sqrt(0.5),
        "camera_frame_definition": "test",
        "depth_uv_transform_id":
            "screen_top_left_to_cpu_top_left_v1_Clockwise90_mirror_y",
    })
    row = {column: "" for column in REQUIRED_COLUMNS}
    row.update({
        "session_id": "session_test", "trial_id": "trial_001",
        "reference_sample_id": "1", "timestamp_ms": "1000",
        "monotonic_timestamp_ms": "1000.5", "cpu_image_timestamp_s": "1.0",
        "tag_family": "tagStandard41h12", "target_tag_id": "0",
        "tag_size_m": "0.08", "decimation": "2", "image_width": "960",
        "image_height": "720", "intrinsics_available": "true",
        "intrinsics_width": "1920", "intrinsics_height": "1440", "fx_px": "700",
        "fy_px": "700", "cx_px": "480", "cy_px": "360",
        "fx_fy_relative_delta": "0", "principal_offset_x_fraction": "0",
        "principal_offset_y_fraction": "0", "intrinsics_gate_passed": "true",
        "camera_world_position_x": "0", "camera_world_position_y": "1",
        "camera_world_position_z": "0", "camera_world_rotation_x": "0",
        "camera_world_rotation_y": "0", "camera_world_rotation_z": "0",
        "camera_world_rotation_w": "1", "tag_detected": "true",
        "detected_tag_count": "1", "tag_camera_position_x": "0",
        "tag_camera_position_y": "0", "tag_camera_position_z": "1",
        "tag_camera_rotation_x": "0", "tag_camera_rotation_y": "0",
        "tag_camera_rotation_z": "0", "tag_camera_rotation_w": "1",
        "tag_world_position_x": "0", "tag_world_position_y": "1",
        "tag_world_position_z": "1", "tag_world_rotation_x": "0",
        "tag_world_rotation_y": "0", "tag_world_rotation_z": "0",
        "tag_world_rotation_w": "1", "object_gt_camera_position_x": "0.1",
        "object_gt_camera_position_y": "0", "object_gt_camera_position_z": "1",
        "object_gt_camera_rotation_x": "0", "object_gt_camera_rotation_y": "0",
        "object_gt_camera_rotation_z": "0", "object_gt_camera_rotation_w": "1",
        "object_gt_world_position_x": "0.1", "object_gt_world_position_y": "1",
        "object_gt_world_position_z": "1", "object_gt_world_rotation_x": "0",
        "object_gt_world_rotation_y": "0", "object_gt_world_rotation_z": "0",
        "object_gt_world_rotation_w": "1", "object_gt_world_yaw_deg": "0",
        "reference_latency_ms": "10", "success": "true", "failure_reason": "",
        "result_source": "Measured",
    })

    with tempfile.TemporaryDirectory() as directory:
        directory_path = Path(directory)
        csv_path = directory_path / "reference_poses.csv"
        config_path = directory_path / "reference_config.json"
        config_path.write_text(json.dumps(config), encoding="utf-8")
        with csv_path.open("w", encoding="utf-8", newline="") as handle:
            writer = csv.DictWriter(handle, fieldnames=REQUIRED_COLUMNS)
            writer.writeheader()
            writer.writerow(row)
        valid_errors, counts = validate(csv_path, config_path)

        row["fx_fy_relative_delta"] = "Infinity"
        with csv_path.open("w", encoding="utf-8", newline="") as handle:
            writer = csv.DictWriter(handle, fieldnames=REQUIRED_COLUMNS)
            writer.writeheader()
            writer.writerow(row)
        invalid_errors, _ = validate(csv_path, config_path)

    if valid_errors or counts.get("rows") != 1:
        print("SELF-TEST FAILED")
        for error in valid_errors:
            print(f"- {error}")
        return 1
    if not any("fx_fy_relative_delta is not a finite number" in error
               for error in invalid_errors):
        print("SELF-TEST FAILED")
        print("- validator accepted a non-finite intrinsics value")
        return 1
    print("SELF-TEST PASSED")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("reference_csv", type=Path, nargs="?")
    parser.add_argument("--config-json", type=Path)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        return run_self_test()
    if args.reference_csv is None:
        parser.error("reference_csv is required unless --self-test is used")
    config_path = args.config_json or args.reference_csv.with_name("reference_config.json")
    if not args.reference_csv.is_file():
        parser.error(f"reference CSV not found: {args.reference_csv}")
    if not config_path.is_file():
        parser.error(f"reference config not found: {config_path}")

    errors, counts = validate(args.reference_csv, config_path)
    print(json.dumps(counts, indent=2, sort_keys=True))
    if errors:
        print("VALIDATION FAILED")
        for error in errors:
            print(f"- {error}")
        return 1
    print("VALIDATION PASSED")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
