"""Wait for one stable character-save change and copy it to a test snapshot.

Read-only: this never opens the game, changes equipment, or writes to the
original save. It exits after copying the first stable changed save.
"""
import argparse
import hashlib
import shutil
import time
from pathlib import Path


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


parser = argparse.ArgumentParser()
parser.add_argument("save", type=Path)
parser.add_argument("output", type=Path)
parser.add_argument("--poll-seconds", type=float, default=1.0)
parser.add_argument("--stable-seconds", type=float, default=3.0)
args = parser.parse_args()

baseline = digest(args.save)
candidate = None
stable_since = None
print("watching", args.save, "baseline", baseline, flush=True)
while True:
    current = digest(args.save)
    if current == baseline:
        candidate = None
        stable_since = None
    elif current != candidate:
        candidate = current
        stable_since = time.monotonic()
        print("change detected", current, flush=True)
    elif time.monotonic() - stable_since >= args.stable_seconds:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(args.save, args.output)
        if digest(args.output) == candidate:
            print("captured", args.output, candidate, flush=True)
            break
        print("save changed while copying; waiting again", flush=True)
        candidate = None
        stable_since = None
    time.sleep(args.poll_seconds)
