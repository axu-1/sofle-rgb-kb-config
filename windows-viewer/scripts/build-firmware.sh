#!/bin/sh
set -eu
firmware="${1:-/firmware}"
workspace="${2:-/work}"
mkdir -p "$workspace"
cd "$workspace"
mkdir -p "$workspace/config"
cp -R "$firmware/config/." "$workspace/config/"
if [ ! -d .west ]; then
    west init -l config
fi
west update --narrow --fetch-opt=--depth=1 --fetch-opt=--filter=blob:none
west zephyr-export
for mode in oled no-oled; do
    for side in left right; do
        set -- -p auto -s zmk/app -d "build-$side-$mode" -b nice_nano_v2
        if [ "$side" = left ]; then
            set -- "$@" -S studio-rpc-usb-uart
        fi
        set -- "$@" -- "-DZMK_CONFIG=$workspace/config" "-DZMK_EXTRA_MODULES=$firmware" "-DSHIELD=sofle_$side"
        if [ "$mode" = no-oled ]; then
            set -- "$@" -DCONFIG_ZMK_DISPLAY=n -DCONFIG_ZMK_DISPLAY_BLANK_ON_IDLE=n
        fi
        west build "$@"
    done
done
