# tools/

Python scripts that read the game's files and produce `data/md2data.bin` for the app. They are only needed by developers; players just use the released `.exe`.

Requirements: Python 3.10+, `numpy`, `pillow`, `cryptography`, `scipy` (for `fit_pose.py`).

Configuration (environment variables):

| Variable | Meaning |
|---|---|
| `MD2_GAME_DIR` | Minecraft Dungeons II install folder (default: the standard Steam path) |
| `MD2_OODLE_DLL` | Path to an Oodle `oo2core_7_win64.dll` or newer (the game's archives use Oodle Leviathan) |
| `MD2_AES_KEY` | The game's archive key as hex (or put it in `tools/aes.key`) |

None of these are provided here.

| Script | What it does |
|---|---|
| `iostore.py` | Reads the game's UE5 IoStore containers (`.utoc/.ucas`) |
| `pakindex.py` | Lists files in the game's legacy `.pak` |
| `build_skin_mod.py` | Reference Python implementation of the skin converter and mod-container writer |
| `mesh_layers.py` | Adds the outer-layer shells and the blink-eye quads to the player model |
| `remap.py` | Maps Minecraft skin texels onto the game model through 3D |
| `fit_pose.py` | Fits the locker-icon pose/camera to the game's official icons, writing `icon_pose.json` |
| `icon_pose.py`, `icon_render.py`, `preview_mesh.py` | Posing and rendering helpers |
| `bc7.py` | Minimal BC7 decoder |
| `export_data.py` | Writes `../data/md2data.bin` |

Typical run:

```
python export_data.py
..\build.bat
```
