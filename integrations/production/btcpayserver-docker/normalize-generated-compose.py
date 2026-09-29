#!/usr/bin/env python3
"""Remove YAML serializer trailing spaces without changing generated semantics."""

from pathlib import Path
import sys


if len(sys.argv) != 2:
    raise SystemExit(f"usage: {sys.argv[0]} Generated/docker-compose.generated.yml")

path = Path(sys.argv[1])
text = path.read_text(encoding="utf-8")
path.write_text("\n".join(line.rstrip() for line in text.splitlines()) + "\n", encoding="utf-8")
