"""Summarise binary changes between two Minecraft Dungeons character saves.

This is a read-only research helper. It never decrypts, edits, uploads, or
attempts to interpret save data; it only identifies changed byte ranges after
the player changes one known cosmetic in-game.
"""
import argparse
import hashlib
from pathlib import Path


def digest(data):
    return hashlib.sha256(data).hexdigest()


def ranges(before, after):
    limit = min(len(before), len(after))
    changed = [i for i in range(limit) if before[i] != after[i]]
    if len(before) != len(after):
        changed.extend(range(limit, max(len(before), len(after))))
    if not changed:
        return []
    result = []
    start = previous = changed[0]
    for offset in changed[1:]:
        if offset == previous + 1:
            previous = offset
            continue
        result.append((start, previous + 1))
        start = previous = offset
    result.append((start, previous + 1))
    return result


parser = argparse.ArgumentParser()
parser.add_argument("before", type=Path)
parser.add_argument("after", type=Path)
parser.add_argument("--max-runs", type=int, default=30)
args = parser.parse_args()

before = args.before.read_bytes()
after = args.after.read_bytes()
diffs = ranges(before, after)
print("before:", len(before), "bytes sha256", digest(before))
print("after: ", len(after), "bytes sha256", digest(after))
print("changed bytes:", sum(end - start for start, end in diffs), "across", len(diffs), "ranges")
for start, end in diffs[:args.max_runs]:
    print("0x{0:04X}-0x{1:04X} ({2} bytes)".format(start, end - 1, end - start))
if len(diffs) > args.max_runs:
    print("... {0} more ranges".format(len(diffs) - args.max_runs))
