"""Create an experimental head-shell depth from two locally generated md2data files.

The input data files must be generated from the same game build and have identical
topology.  This changes only mesh vertex positions and the matching locker-icon
geometry; it never reads or writes the game installation.
"""
import argparse
import struct


def u16(data, offset):
    return struct.unpack_from("<H", data, offset)[0]


def u32(data, offset):
    return struct.unpack_from("<I", data, offset)[0]


def skip_string(data, offset):
    return offset + 2 + u16(data, offset)


def package_info(data, offset, has_offset):
    offset += 12
    offset = skip_string(data, offset)
    size = u32(data, offset)
    blob_offset = offset + 4
    offset = blob_offset + size
    if has_offset:
        offset += 4
    imports = u32(data, offset)
    offset += 4
    return blob_offset, size, offset + 8 * imports


def skip_package(data, offset):
    return package_info(data, offset, True)[2]


def mesh_blobs(data):
    if data[:4] != b"MD2S":
        raise ValueError("not an md2data file")
    offset = 4 + 4 + 16 + 32
    hero_count = u32(data, offset)
    offset += 4
    for _ in range(hero_count):
        offset = skip_string(data, offset)
        offset += 1
        for _ in range(3):
            offset = skip_package(data, offset)
    result = []
    for _ in range(2):
        blob_offset, size, offset = package_info(data, offset, False)
        result.append((blob_offset, size))
    return result, offset


def position_ranges(data, mesh_offsets, after_meshes):
    ranges = []
    for blob_offset, size in mesh_offsets:
        package = data[blob_offset:blob_offset + size]
        index_count = u32(package, 5269)
        shift = 2 * index_count - 2 * 2310
        count = u32(package, 9905 + shift)
        start = blob_offset + 9909 + shift
        end = start + count * 12
        if end > blob_offset + size:
            raise ValueError("mesh position buffer is outside its package")
        ranges.append((start, end))

    offset = after_meshes
    offset += 4 + u32(data, offset) * 4
    offset += 4 + u32(data, offset) * 4
    for _ in range(2):
        count = u32(data, offset)
        start = offset + 4
        end = start + count * 12
        ranges.append((start, end))
        offset = end + count * 8
        triangles = u32(data, offset)
        offset += 4 + triangles * 6 + triangles * 12
    return ranges


def blend_positions(low, high, output, fraction):
    low_meshes, low_after = mesh_blobs(low)
    high_meshes, high_after = mesh_blobs(high)
    if low_meshes != high_meshes or low_after != high_after:
        raise ValueError("data layout differs; regenerate both files from the same game build")
    changed = 0
    for start, end in position_ranges(low, low_meshes, low_after):
        for offset in range(start, end, 4):
            a = struct.unpack_from("<f", low, offset)[0]
            b = struct.unpack_from("<f", high, offset)[0]
            value = a + (b - a) * fraction
            struct.pack_into("<f", output, offset, value)
            if abs(a - b) > 0.000001:
                changed += 1
    return changed


parser = argparse.ArgumentParser()
parser.add_argument("low", help="locally generated data with the shallower head shell")
parser.add_argument("high", help="locally generated data with the deeper head shell")
parser.add_argument("output")
parser.add_argument("--fraction", type=float, required=True, help="0 = low, 1 = high")
args = parser.parse_args()
if not 0.0 <= args.fraction <= 1.0:
    parser.error("--fraction must be between 0 and 1")

with open(args.low, "rb") as f:
    low = f.read()
with open(args.high, "rb") as f:
    high = f.read()
if len(low) != len(high):
    raise ValueError("data files have different sizes")
output = bytearray(high)
changed = blend_positions(low, high, output, args.fraction)
with open(args.output, "wb") as f:
    f.write(output)
print("wrote", args.output, "with", changed, "interpolated coordinates")
