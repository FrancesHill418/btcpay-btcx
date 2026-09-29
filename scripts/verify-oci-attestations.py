#!/usr/bin/env python3
"""Fail unless an OCI archive contains SPDX SBOM and SLSA v1 provenance."""

import json
import sys
import tarfile


def blob(archive: tarfile.TarFile, digest: str) -> bytes:
    algorithm, value = digest.split(":", 1)
    if algorithm != "sha256":
        raise ValueError(f"unsupported OCI digest algorithm: {algorithm}")
    member = archive.extractfile(f"blobs/sha256/{value}")
    if member is None:
        raise ValueError(f"missing OCI blob {digest}")
    return member.read()


def main(path: str) -> None:
    found: set[str] = set()
    with tarfile.open(path, "r:") as archive:
        outer = json.load(archive.extractfile("index.json"))
        for outer_descriptor in outer.get("manifests", []):
            inner = json.loads(blob(archive, outer_descriptor["digest"]))
            for descriptor in inner.get("manifests", []):
                annotations = descriptor.get("annotations", {})
                if annotations.get("vnd.docker.reference.type") != "attestation-manifest":
                    continue
                manifest = json.loads(blob(archive, descriptor["digest"]))
                for layer in manifest.get("layers", []):
                    statement = json.loads(blob(archive, layer["digest"]))
                    predicate = statement.get("predicateType")
                    if predicate == "https://spdx.dev/Document":
                        found.add("spdx-sbom")
                    elif predicate == "https://slsa.dev/provenance/v1":
                        found.add("slsa-v1-provenance")

    missing = {"spdx-sbom", "slsa-v1-provenance"} - found
    if missing:
        raise SystemExit(f"{path}: missing OCI attestations: {', '.join(sorted(missing))}")
    print(f"{path}: SPDX SBOM and SLSA v1 provenance present")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("usage: verify-oci-attestations.py IMAGE.oci.tar")
    main(sys.argv[1])
