"""Validate paired probe runs and summarize medians, IQRs and paired deltas."""
import argparse
import json
import statistics
from pathlib import Path


def distribution(values):
    values = [value for value in values if value is not None]
    if not values:
        return None
    values.sort()
    def percentile(p):
        rank = (len(values) - 1) * p
        i = int(rank)
        return values[i] + (values[min(i + 1, len(values) - 1)] - values[i]) * (rank - i)
    return {"n": len(values), "median": statistics.median(values),
            "q1": percentile(.25), "q3": percentile(.75), "min": min(values), "max": max(values)}


def summarize(root):
    manifest = json.loads((root / "manifest.json").read_text(encoding="utf-8"))
    runs = {variant: [] for variant in ("reference", "optimized")}
    for round_index in range(1, manifest["rounds"] + 1):
        for variant in runs:
            report = json.loads((root / f"round-{round_index}-{variant}.json").read_text(encoding="utf-8"))
            assert report["complete"] and report["label"] == variant
            assert report["measurementSeconds"] == manifest["seconds"]
            assert report["warmupSeconds"] == manifest["warmup"]
            expected = len(manifest["scenarios"]) if "scenarios" in manifest else 6 if manifest["nativeDrag"] else 5
            assert len(report["results"]) == expected
            if "scenarios" in manifest:
                assert {row["scenario"] for row in report["results"]} == set(manifest["scenarios"])
            for result in report["results"]:
                assert result["valid"] and result["seconds"] >= manifest["seconds"] - .1
                assert result["observedMotionUpdates"] <= result["motionInputs"]
                if result["scenario"] in ("moving-quota", "native-drag"):
                    assert result["latencyMedianMs"] is not None and result["latencyMedianMs"] >= 0
                count = result["observedMotionUpdates"]
                result["observableUpdatesPerSecond"] = count / result["seconds"]
                result["cpuMillisecondsPerObservedUpdate"] = result["cpuCorePercent"] * result["seconds"] * 10 / count if count else None
                result["coalescedInputPercent"] = 100 * (1 - count / result["motionInputs"]) if result["motionInputs"] else None
            runs[variant].append({row["scenario"]: row for row in report["results"]})
    scenarios = list(runs["reference"][0])
    summary = {"method": manifest["reference"], "rounds": manifest["rounds"],
               "seconds": manifest["seconds"], "warmup": manifest["warmup"],
               "nativeInput": manifest["nativeDrag"], "scenarios": {}}
    metrics = ["cpuCorePercent", "hostCpuPercent", "workingSetMiB", "privateMiB", "managedKiB",
               "gen0Collections", "selectionReads", "stateReads", "publications", "frames", "backgroundReads",
               "motionInputs", "observedMotionUpdates", "draggingUpdates", "latencyMedianMs", "latencyP95Ms",
               "updateIntervalMedianMs", "updateIntervalP95Ms"]
    metrics += ["observableUpdatesPerSecond", "cpuMillisecondsPerObservedUpdate", "coalescedInputPercent"]
    for scenario in scenarios:
        row = {}
        for variant in runs:
            row[variant] = {metric: distribution([trial[scenario][metric] for trial in runs[variant]]) for metric in metrics}
        decreases = []
        for before, after in zip(runs["reference"], runs["optimized"]):
            b, a = before[scenario]["cpuCorePercent"], after[scenario]["cpuCorePercent"]
            decreases.append({"cpuCorePercentagePointDecrease": b - a,
                              "relativeCpuDecreasePercent": 100 * (b - a) / b if b else None,
                              "hostCpuPercentagePointChange": after[scenario]["hostCpuPercent"] - before[scenario]["hostCpuPercent"]})
        row["paired"] = decreases
        row["pairedCpuDecrease"] = distribution([item["cpuCorePercentagePointDecrease"] for item in decreases])
        row["pairedRelativeCpuDecreasePercent"] = distribution([item["relativeCpuDecreasePercent"] for item in decreases])
        row["lowerCpuPairs"] = sum(item["cpuCorePercentagePointDecrease"] > 0 for item in decreases)
        summary["scenarios"][scenario] = row
    return summary


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    result = summarize(args.directory)
    output = args.output or args.directory / "summary.json"
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    for scenario, row in result["scenarios"].items():
        before = row["reference"]["cpuCorePercent"]
        after = row["optimized"]["cpuCorePercent"]
        print(f"{scenario}: CPU median {before['median']:.3f}% -> {after['median']:.3f}%; "
              f"lower in {row['lowerCpuPairs']}/{result['rounds']} pairs")
    print(output.resolve())
