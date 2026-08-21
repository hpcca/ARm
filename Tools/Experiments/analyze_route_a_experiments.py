#!/usr/bin/env python3
"""Create condition-level Route A summaries from one or more validated CSV files."""

from __future__ import annotations

import argparse
import csv
import math
import statistics
from collections import defaultdict
from pathlib import Path


GROUP_FIELDS = [
    "ablation_mode", "condition_id", "distance_condition", "view_condition",
    "occlusion_percent", "lighting_condition",
]

LATENCY_FIELDS = [
    "capture_latency_ms", "yolo_latency_ms", "output_readback_latency_ms",
    "depth_latency_ms", "raycast_fusion_latency_ms", "tracking_latency_ms",
    "total_latency_ms",
]


def finite_float(value: str) -> float | None:
    try:
        number = float(value)
    except (TypeError, ValueError):
        return None
    return number if math.isfinite(number) else None


def percentile(values: list[float], percentile_value: float) -> float | None:
    if not values:
        return None
    ordered = sorted(values)
    index = (len(ordered) - 1) * percentile_value
    lower = math.floor(index)
    upper = math.ceil(index)
    if lower == upper:
        return ordered[lower]
    return ordered[lower] * (upper - index) + ordered[upper] * (index - lower)


def sample_std(values: list[float]) -> float | None:
    return statistics.stdev(values) if len(values) >= 2 else None


def unwrap_degrees(values: list[float]) -> list[float]:
    if not values:
        return []
    unwrapped = [values[0]]
    previous_raw = values[0]
    for value in values[1:]:
        delta = (value - previous_raw + 180.0) % 360.0 - 180.0
        unwrapped.append(unwrapped[-1] + delta)
        previous_raw = value
    return unwrapped


def fmt(value: float | None) -> str:
    return "" if value is None else format(value, ".9g")


def load_rows(paths: list[Path]) -> list[dict[str, str]]:
    rows: list[dict[str, str]] = []
    for path in paths:
        with path.open("r", encoding="utf-8-sig", newline="") as handle:
            rows.extend(csv.DictReader(handle))
    return rows


def summarize_group(rows: list[dict[str, str]]) -> dict[str, str]:
    output: dict[str, str] = {field: rows[0].get(field, "") for field in GROUP_FIELDS}
    output["row_count"] = str(len(rows))
    output["successful_row_count"] = str(sum(row.get("success") == "true" for row in rows))
    output["successful_row_rate"] = fmt(
        sum(row.get("success") == "true" for row in rows) / len(rows)
    )
    output["depth_availability_rate"] = fmt(
        sum(row.get("depth_available") == "true" for row in rows) / len(rows)
    )
    attempted_depth_rows = [
        row for row in rows if row.get("depth_sampling_attempted") == "true"
    ]
    valid_sample_total = sum(
        finite_float(row.get("depth_valid_sample_count", "")) or 0
        for row in attempted_depth_rows
    )
    sample_capacity_total = sum(
        finite_float(row.get("depth_sample_capacity", "")) or 0
        for row in attempted_depth_rows
    )
    output["depth_valid_sample_rate"] = fmt(
        valid_sample_total / sample_capacity_total if sample_capacity_total > 0 else None
    )
    output["fade_fallback_activation_rate"] = fmt(
        sum(row.get("fade_fallback_active") == "true" for row in rows) / len(rows)
    )

    expected_object_count = max(
        int(finite_float(row.get("expected_object_count", "")) or 1)
        for row in rows
    )
    active_tracks_by_frame: dict[tuple[str, str, str], set[str]] = defaultdict(set)
    for row in rows:
        if row.get("track_id", "") and row.get("track_state", "") in {
            "Locked", "TrackingMove", "Lost"
        }:
            active_tracks_by_frame[
                (row.get("session_id", ""), row.get("trial_id", ""), row.get("frame_id", ""))
            ].add(row["track_id"])
    duplicate_frames = sum(
        len(track_ids) > expected_object_count
        for track_ids in active_tracks_by_frame.values()
    )
    output["duplicate_replacement_frame_rate"] = fmt(
        duplicate_frames / len(active_tracks_by_frame)
        if active_tracks_by_frame else None
    )

    for field in LATENCY_FIELDS:
        values = [value for row in rows if (value := finite_float(row.get(field, ""))) is not None]
        output[f"{field}_median"] = fmt(statistics.median(values) if values else None)
        output[f"{field}_p95"] = fmt(percentile(values, 0.95))

    unique_frames: dict[tuple[str, str, str], int] = {}
    for row in rows:
        timestamp = finite_float(row.get("timestamp_ms", ""))
        if timestamp is not None:
            unique_frames[(row.get("session_id", ""), row.get("trial_id", ""), row.get("frame_id", ""))] = int(timestamp)
    timestamps_by_trial: dict[tuple[str, str], list[int]] = defaultdict(list)
    for (session_id, trial_id, _), timestamp in unique_frames.items():
        timestamps_by_trial[(session_id, trial_id)].append(timestamp)
    fps_values: list[float] = []
    for timestamps in timestamps_by_trial.values():
        timestamps.sort()
        duration_seconds = (timestamps[-1] - timestamps[0]) / 1000 if len(timestamps) >= 2 else 0
        if duration_seconds > 0:
            fps_values.append((len(timestamps) - 1) / duration_seconds)
    output["effective_fps_mean"] = fmt(statistics.mean(fps_values) if fps_values else None)

    positions_by_track: dict[tuple[str, str, str], list[tuple[int, tuple[float, float, float]]]] = defaultdict(list)
    yaws_by_track: dict[tuple[str, str, str], list[float]] = defaultdict(list)
    for row in rows:
        if row.get("success") != "true" or not row.get("track_id", ""):
            continue
        coordinates = tuple(
            finite_float(row.get(field, ""))
            for field in ("fused_position_x", "fused_position_y", "fused_position_z")
        )
        key = (row.get("session_id", ""), row.get("trial_id", ""), row["track_id"])
        if all(value is not None for value in coordinates):
            positions_by_track[key].append((int(row["timestamp_ms"]), coordinates))
        yaw = finite_float(row.get("output_yaw_deg", ""))
        if yaw is not None:
            yaws_by_track[key].append(yaw)

    axis_std_values: list[float] = []
    maximum_jumps: list[float] = []
    for samples in positions_by_track.values():
        samples.sort(key=lambda item: item[0])
        for axis in range(3):
            standard_deviation = sample_std([position[axis] for _, position in samples])
            if standard_deviation is not None:
                axis_std_values.append(standard_deviation)
        for (_, first), (_, second) in zip(samples, samples[1:]):
            maximum_jumps.append(math.dist(first, second))
    yaw_std_values = []
    for values in yaws_by_track.values():
        standard_deviation = sample_std(unwrap_degrees(values))
        if standard_deviation is not None:
            yaw_std_values.append(standard_deviation)

    states_by_track: dict[tuple[str, str, str], list[tuple[int, str]]] = defaultdict(list)
    for row in rows:
        if row.get("track_id", "") and row.get("track_state", ""):
            key = (row.get("session_id", ""), row.get("trial_id", ""), row["track_id"])
            states_by_track[key].append((int(row["timestamp_ms"]), row["track_state"]))
    track_loss_events = 0
    reacquisition_times_seconds: list[float] = []
    for samples in states_by_track.values():
        samples.sort(key=lambda item: item[0])
        previous_state = ""
        lost_timestamp: int | None = None
        for timestamp, state in samples:
            if state == "Lost" and previous_state != "Lost":
                track_loss_events += 1
                lost_timestamp = timestamp
            elif state != "Lost" and previous_state == "Lost" and lost_timestamp is not None:
                reacquisition_times_seconds.append((timestamp - lost_timestamp) / 1000.0)
                lost_timestamp = None
            previous_state = state
    output["position_axis_std_m_mean"] = fmt(
        statistics.mean(axis_std_values) if axis_std_values else None
    )
    output["maximum_frame_jump_m"] = fmt(max(maximum_jumps) if maximum_jumps else None)
    output["yaw_jitter_std_deg_mean"] = fmt(
        statistics.mean(yaw_std_values) if yaw_std_values else None
    )
    output["track_loss_event_count"] = str(track_loss_events)
    output["reacquisition_time_seconds_median"] = fmt(
        statistics.median(reacquisition_times_seconds)
        if reacquisition_times_seconds else None
    )
    output["translation_error_cm"] = "Not measured"
    output["full_6dof_rotation_error"] = "N/A"
    return output


def run_self_test() -> int:
    rows: list[dict[str, str]] = []
    for frame_id, timestamp_ms, depth_available, state in (
        (1, 1000, "true", "Locked"),
        (2, 1250, "false", "Lost"),
        (3, 1500, "true", "Locked"),
    ):
        row = {
            "session_id": "self_test", "trial_id": "trial_001",
            "frame_id": str(frame_id), "timestamp_ms": str(timestamp_ms),
            "expected_object_count": "1", "ablation_mode": "A3",
            "condition_id": "baseline", "distance_condition": "1.0m",
            "view_condition": "frontal", "occlusion_percent": "0",
            "lighting_condition": "normal", "success": "true",
            "depth_sampling_attempted": "true", "depth_available": depth_available,
            "depth_valid_sample_count": "25" if depth_available == "true" else "0",
            "depth_sample_capacity": "25", "fade_fallback_active": "false",
            "track_id": "1", "track_state": state,
            "fused_position_x": str((frame_id - 1) * 0.01),
            "fused_position_y": "0", "fused_position_z": "1",
            "output_yaw_deg": str((358 + frame_id) % 360),
        }
        for field in LATENCY_FIELDS:
            row[field] = "10"
        rows.append(row)
    summary = summarize_group(rows)
    checks = {
        "row_count": "3",
        "depth_valid_sample_rate": "0.666666667",
        "effective_fps_mean": "4",
        "track_loss_event_count": "1",
        "reacquisition_time_seconds_median": "0.25",
        "translation_error_cm": "Not measured",
        "full_6dof_rotation_error": "N/A",
    }
    failures = [
        f"{key}: expected {expected!r}, got {summary.get(key)!r}"
        for key, expected in checks.items()
        if summary.get(key) != expected
    ]
    if failures:
        print("SELF-TEST FAILED")
        for failure in failures:
            print(f"- {failure}")
        return 1
    print("SELF-TEST PASSED")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("csv", type=Path, nargs="*")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        return run_self_test()
    if not args.csv or args.output is None:
        parser.error("csv and --output are required unless --self-test is used")
    rows = load_rows(args.csv)
    if not rows:
        raise SystemExit("No data rows found")
    groups: dict[tuple[str, ...], list[dict[str, str]]] = defaultdict(list)
    for row in rows:
        groups[tuple(row.get(field, "") for field in GROUP_FIELDS)].append(row)
    summaries = [summarize_group(group_rows) for group_rows in groups.values()]
    args.output.parent.mkdir(parents=True, exist_ok=True)
    fieldnames = list(summaries[0].keys())
    with args.output.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(summaries)
    print(f"Wrote {len(summaries)} condition summaries to {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
