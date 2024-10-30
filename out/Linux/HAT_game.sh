#!/bin/sh
echo -ne '\033c\033]0;HAT_Game\a'
base_path="$(dirname "$(realpath "$0")")"
"$base_path/HAT_game.x86_64" "$@"
