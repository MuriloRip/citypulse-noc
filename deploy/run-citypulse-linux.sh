#!/bin/sh
set -eu

app_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
data_dir="${XDG_DATA_HOME:-"$HOME/.local/share"}/citypulse"
mkdir -p "$data_dir"

export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://127.0.0.1:4173}"
export ConnectionStrings__CityPulse="Data Source=$data_dir/citypulse.db"

exec "$app_dir/CityPulse.Api" --urls "$ASPNETCORE_URLS"
