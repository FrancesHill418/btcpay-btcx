#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$repo_root/src/BTCPayServer.Plugins.BTCX/BTCPayServer.Plugins.BTCX.csproj"
packer="$repo_root/submodules/btcpayserver/BTCPayServer.PluginPacker/BTCPayServer.PluginPacker.csproj"
publish_dir="$repo_root/artifacts/plugin-publish"
package_root="$repo_root/artifacts/plugin-package"
plugin_name="BTCPayServer.Plugins.BTCX"

"$repo_root/scripts/restore-locked-dotnet.sh" "$project"
dotnet publish "$project" -c Release --no-restore -o "$publish_dir"
dotnet run --no-restore --project "$packer" -c Release -- "$publish_dir" "$plugin_name" "$package_root"

package="$package_root/$plugin_name/0.1.0/$plugin_name.btcpay"
metadata="$package_root/$plugin_name/0.1.0/$plugin_name.btcpay.json"
test -s "$package"
test -s "$metadata"
sha256sum "$package" "$metadata"
printf 'Installable BTCPay plugin package: %s\n' "$package"
