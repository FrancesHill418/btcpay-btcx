#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
image_prefix="${BTCX_IMAGE_PREFIX:?Set the approved image registry/repository prefix; no registry is assumed}"
release_tag="${BTCX_RELEASE_TAG:-$(git -C "$repo_root" describe --tags --exact-match HEAD)}"
[[ "$release_tag" =~ ^v[0-9]+\.[0-9]+\.[0-9]+-rc[0-9]+$ ]] || {
  echo "Refusing to publish from a non-RC tag: $release_tag" >&2
  exit 2
}
[[ "$(git -C "$repo_root" rev-parse HEAD)" == "$(git -C "$repo_root" rev-parse "$release_tag")" ]] || {
  echo "HEAD must equal the selected release tag." >&2
  exit 2
}
[[ "$(git -C "$repo_root" status --porcelain)" == "" ]] || {
  echo "Refusing to publish from a dirty checkout." >&2
  exit 2
}
command -v docker >/dev/null
docker buildx version >/dev/null

builder="btcx-release-$$"
buildkit_image='moby/buildkit:buildx-stable-1@sha256:28a898719c18a33f4e8000685287fa36fd0dd9560c6440227d3a732d79bb41d8'
cleanup() {
  docker buildx rm "$builder" >/dev/null 2>&1 || true
}
trap cleanup EXIT
docker buildx create --name "$builder" --driver docker-container --driver-opt "image=$buildkit_image" --use >/dev/null
docker buildx inspect "$builder" --bootstrap >/dev/null

out="$repo_root/artifacts/production-publish"
mkdir -p "$out"
lock_file="$out/image-lock.txt"
{
  printf 'release_tag=%s\n' "$release_tag"
  printf 'source_commit=%s\n' "$(git -C "$repo_root" rev-parse "$release_tag")"
  printf 'build_time_utc=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  printf 'platform=linux/amd64\n'
  printf 'buildkit=%s\n' "$buildkit_image"
  printf 'sbom_generator=docker/scout-sbom-indexer@sha256:4b67f29eb0d1244ab0f62de867ac5dafd7262fcd7ebbdefa6ec8aacd6b15252d\n'
  printf 'bitcoin_pocx_dockerfile_sha256=%s\n' "$(sha256sum "$repo_root/integrations/production/bitcoin-pocx/Dockerfile" | cut -d' ' -f1)"
  printf 'electrs_dockerfile_sha256=%s\n' "$(sha256sum "$repo_root/integrations/production/electrs/Dockerfile" | cut -d' ' -f1)"
  printf 'btcpay_dockerfile_sha256=%s\n' "$(sha256sum "$repo_root/integrations/production/btcpay/Dockerfile" | cut -d' ' -f1)"
  printf 'rest_patch_blockpart_sha256=%s\n' "$(sha256sum "$repo_root/patches/electrs-pocx-rest/bitcoin-pocx-v30-blockpart-compat.patch" | cut -d' ' -f1)"
  printf 'rest_patch_net_processing_sha256=%s\n' "$(sha256sum "$repo_root/patches/electrs-pocx-rest/bitcoin-pocx-v30-net-processing-compat.patch" | cut -d' ' -f1)"
} > "$lock_file"
export DOCKER_BUILD_CHECKS_ANNOTATIONS=false

build_and_push() {
  local name="$1" dockerfile="$2" reference="$image_prefix/$1:$release_tag" metadata="$out/$1-metadata.json"
  docker buildx build --builder "$builder" \
    --platform linux/amd64 \
    --pull \
    --build-arg SOURCE_DATE_EPOCH="$(git -C "$repo_root" show -s --format=%ct "$release_tag")" \
    --build-arg BTCX_BUILD_COMMIT="$(git -C "$repo_root" rev-parse "$release_tag")" \
    --provenance=mode=max,version=v1 \
    --attest "type=sbom,generator=docker/scout-sbom-indexer@sha256:4b67f29eb0d1244ab0f62de867ac5dafd7262fcd7ebbdefa6ec8aacd6b15252d" \
    --metadata-file "$metadata" \
    --tag "$reference" \
    --push \
    --file "$repo_root/$dockerfile" \
    "$repo_root"

  python3 - "$metadata" "$reference" <<'PY'
import json, sys
path, reference = sys.argv[1:]
with open(path, encoding="utf-8") as file:
    metadata = json.load(file)
digest = metadata.get("containerimage.digest", "")
if not digest.startswith("sha256:") or len(digest) != 71:
    raise SystemExit(f"BuildKit did not return a registry manifest digest for {reference}")
print(f"{reference}@{digest}")
with open(sys.argv[1] + ".reference", "w", encoding="utf-8") as file:
    file.write(f"{reference}@{digest}\n")
PY
  cat "$metadata.reference" >> "$lock_file"
}

build_and_push bitcoin-pocx integrations/production/bitcoin-pocx/Dockerfile
build_and_push electrs-btcx integrations/production/electrs/Dockerfile
build_and_push btcpayserver integrations/production/btcpay/Dockerfile

sha256sum "$lock_file" > "$lock_file.sha256"
echo "Published digests and BuildKit SBOM/provenance metadata are in $out. Record and verify them before deployment."
