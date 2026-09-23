"""Summarize captured perf records, grouping by observed game state."""

import argparse
import collections
import pathlib
import re


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("files", nargs="+", type=pathlib.Path)
    args = parser.parse_args()
    for path in args.files:
        groups = collections.defaultdict(list)
        records = [line for line in path.read_text().splitlines() if "[SlopWorld] perf " in line]
        # Context is sampled at the end of a window. Exclude its first record and
        # any transition record whose work may belong to the previous condition.
        previous = None
        for line in records:
            lanes = {}
            for part in line.split("[SlopWorld] perf ", 1)[1].split(";"):
                parts = part.split()
                if parts:
                    lanes[parts[0]] = dict(re.findall(r"(\w+)=(-?[\d.]+)", part))
            context = lanes.get("context", {})
            key = tuple(context.get(k, "?") for k in ("eco", "terminal", "sessions", "width", "height"))
            if key != previous:
                previous = key
                continue
            groups[key].append(lanes)
        print(f"\nFile: {path} ({len(records)} records; first and transition records excluded)")
        for key, rows in groups.items():
            def total(lane, metric):
                return sum(float(row.get(lane, {}).get(metric, 0)) for row in rows)
            frames = total("root-update", "calls")
            hits = total("sidebar-presentation-hits", "calls")
            misses = total("sidebar-presentation-rebuilds", "calls")
            print(f"eco={key[0]} terminal={key[1]} sessions={key[2]} size={key[3]}x{key[4]} windows={len(rows)}")
            print(f"mean reported FPS={total('context', 'fps') / len(rows):.2f}; gen0 collections={total('context', 'gc0'):.0f}")
            print(f"sidebar presentation hits={hits:.0f}, rebuilds={misses:.0f}; solar samples={total('solar-clock-samples', 'calls'):.0f}")
            memory = [row["memory"] for row in rows if "memory" in row]
            if memory:
                print(f"Memory samples={len(memory)} (MiB: first -> last, then peak):")
                for metric in ("rssBytes", "managedUsed", "managedHeap", "unityAllocated", "unityReserved", "unityUnusedReserved"):
                    values = [float(sample[metric]) / 1048576 for sample in memory
                              if metric in sample and float(sample[metric]) >= 0]
                    if values:
                        print(f"  {metric}: {values[0]:.2f} -> {values[-1]:.2f} (peak {max(values):.2f})")
            print("Mean lane ms per Root.Update call (lanes overlap, so do not sum):")
            if frames:
                for lane in ("root-update", "ws-events", "colonist-bar", "sidebar", "topbar", "terminal-window"):
                    print(f"  {lane}: {total(lane, 'ms') / frames:.3f}")


if __name__ == "__main__":
    main()
