#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
btcpay_root="$repo_root/submodules/btcpayserver"
lock_root="$repo_root/locks/dotnet/btcpayserver"
projects=(
  BTCPayServer.Abstractions
  BTCPayServer.Client
  BTCPayServer.Common
  BTCPayServer.Data
  BTCPayServer.Rating
  BTCPayServer
  BTCPayServer.Tests
  BTCPayServer.PluginPacker
)
created=()
cleanup() {
  for target in "${created[@]}"; do rm -f "$target"; done
}
trap cleanup EXIT

test "$(git -C "$btcpay_root" rev-parse HEAD)" = 2d5a0d8077bb33af080e949031da33d84b80638d || {
  echo "BTCPay submodule is not the locked v2.4.4 commit." >&2
  exit 2
}

for project in "${projects[@]}"; do
  source="$lock_root/$project.packages.lock.json"
  target="$btcpay_root/$project/packages.lock.json"
  test -s "$source" || { echo "Missing external lock snapshot: $source" >&2; exit 2; }
  if [[ -e "$target" ]]; then
    cmp -s "$source" "$target" || {
      echo "BTCPay checkout contains a different package lock: $target" >&2
      exit 2
    }
  else
    install -m 0644 "$source" "$target"
    created+=("$target")
  fi
done

cd "$repo_root"
dotnet restore "${1:-BTCPayServer.Plugins.BTCX.slnx}" --locked-mode
dotnet restore "$btcpay_root/BTCPayServer.PluginPacker/BTCPayServer.PluginPacker.csproj" --locked-mode
