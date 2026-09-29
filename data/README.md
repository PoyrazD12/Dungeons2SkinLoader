# data/

`md2data.bin` goes here. The app embeds it at build time. It holds what the app needs from the game:

- the hero texture packages (with all original pixels zeroed out),
- the player model with the outer-layer shells and the blink-eye quads already added,
- the texture remap table and the locker-icon pose/camera,
- small preview renders of the original heroes,
- the information needed to write mod files the game accepts.

It is generated from your own copy of the game by `tools/export_data.py`, and is **not** included in this repository.
