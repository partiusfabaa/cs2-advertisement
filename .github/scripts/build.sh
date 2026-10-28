#!/usr/bin/env bash
set -euo pipefail

props=(-c Release)
if [ -n "${VERSION:-}" ]; then
  props+=("-p:Version=$VERSION")
fi

out="$PWD/out"
publish="$out/publish"
rm -rf "$out" bin obj
mkdir -p "$publish"

dotnet publish Advertisement.csproj "${props[@]}" -o "$publish"

pkg="$out/pkg/addons/counterstrikesharp/plugins/Advertisement"
mkdir -p "$pkg"
cp "$publish"/Advertisement.{dll,pdb,deps.json} "$pkg/"
cp -r lang "$pkg/lang"
(cd "$out/pkg" && zip -qr "$out/Advertisement.zip" addons)

echo "Packed:"
ls -la "$out"/*.zip
