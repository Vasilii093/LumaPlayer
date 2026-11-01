"""Fuse the 32 beta multiply/add/LeakyReLU chains in the pinned RIFE 4.6 graph.

The binary weights are untouched. Requires the matching LumaResidual Vulkan
layer: FP16 storage, FP32 arithmetic, pack4, slope 0.2. Never edit the input.
"""
from pathlib import Path
import argparse
import hashlib


def fuse(text):
    lines = text.splitlines()
    assert lines[0].strip() == "7767517"
    source = [line.split() for line in lines[2:] if line.strip()]
    result = []
    count = 0
    i = 0
    while i < len(source):
        a = source[i]
        if (i + 2 < len(source) and a[0] == "BinaryOp"
                and a[1].startswith("Mul_") and a[2:4] == ["2", "1"]
                and ".convblock." in a[5] and a[5].endswith(".beta")):
            b, c = source[i + 1:i + 3]
            assert a[7:] == ["0=2"]
            assert b[0] == "BinaryOp" and b[2:4] == ["2", "1"]
            assert b[4] == a[6] and b[7:] == []
            assert c[0] == "ReLU" and c[2:4] == ["1", "1"]
            assert c[4] == b[6] and c[6:] == ["0=2.000000e-01"]
            # Neither removed intermediate may have another consumer.
            for blob in (a[6], b[6]):
                consumers = sum(blob in row[4:4 + int(row[2])] for row in source)
                assert consumers == 1, (blob, consumers)
            result.append(["luma.Residual", c[1], "3", "1", a[4], a[5], b[5], c[5]])
            count += 1
            i += 3
        else:
            result.append(a)
            i += 1
    assert count == 32, f"Expected pinned RIFE 4.6 graph, found {count} chains"
    blobs = {blob for row in result for blob in row[4:4 + int(row[2]) + int(row[3])]}
    return "7767517\n" + f"{len(result)} {len(blobs)}\n" + "\n".join(" ".join(row) for row in result) + "\n"


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("original", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    if args.original.resolve() == args.output.resolve():
        parser.error("Use a separate output file; preserve the original model")
    output = fuse(args.original.read_text(encoding="utf-8"))
    args.output.write_text(output, encoding="utf-8", newline="\n")
    print("32 chains fused; weights unchanged; param SHA256", hashlib.sha256(output.encode()).hexdigest())
