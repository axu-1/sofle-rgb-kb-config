# PandaKB Sofle RGB MX configuration

This configuration uses the existing PandaKB Sofle shield, nice!nano v2-compatible
nRF52840 controllers, and **ZMK v0.3**. Keep the custom shield: do not replace it
with a generic upstream Sofle definition.

## Laptop status display

The `host-status/` Zephyr module reports actual keyboard status to the native
Windows companion in `../keyboard-layer-viewers`. It is enabled on the central
(left) half only, independent of the OLED. The feature adds:

- An encrypted, read-only Bluetooth GATT characteristic on the existing host
  connection; no extra pairing slot or typed-key data.
- Active layer index and mask, both battery readings, active profile, output,
  USB power, peripheral connection and Caps/Num/Scroll Lock.
- Event-driven notifications and a current-state read for reconnects.

The module enables standard peripheral battery fetching and HID lock indicators.
Disable it with `CONFIG_ZMK_HOST_STATUS=n` if you no longer use the viewer.

The build workflow loads `zephyr/module.yml` automatically. Pushes affecting
the module, build matrix or configuration trigger the existing firmware build.
No firmware version, hardware pins, keymap bindings or Studio settings were
changed.

## Flash and verify first

Use the normal `sofle_left` and `sofle_right` UF2 files from **Build ZMK firmware**.
Flash the correct half through its USB bootloader. Do not use the
`settings_reset` image unless you deliberately want to clear pairing.

The original builds keep both OLEDs enabled. Compare them with the Windows
display before removing hardware:

1. BASE, held LOWER, held RAISE, simultaneous LOWER+RAISE (ADJUST), GAMING and
   returning to BASE should match immediately.
2. Verify left/right batteries after the first measurement; USB power does not
   necessarily mean charging.
3. Power off the right half and verify `R offline`; power it back on.
4. Verify USB output versus Bluetooth output, this laptop's profile, Caps Lock,
   sleep/wake and reconnect after switching profiles.
5. Verify the Windows panel does not steal focus while typing, can be dragged,
   and stays visible without opening a menu.

If the new status service is absent in Windows after flashing, Windows may have
cached the old GATT table. Remove and re-pair the keyboard on this laptop's
profile before resetting any other stored profiles.

## OLED removal

After verification, build OLED-disabled firmware for **both halves**. Do not
merely unplug the display while leaving the OLED UI enabled.

The build matrix also produces:

- `sofle_left-no-oled-nice_nano_v2-zmk.uf2`
- `sofle_right-no-oled-nice_nano_v2-zmk.uf2`

Flash these after your OLED-enabled comparison passes. The left OLED-disabled
variant retains the laptop status service and the Studio USB snippet.

The equivalent CMake/Kconfig overrides are:

```sh
-DCONFIG_ZMK_DISPLAY=n \
-DCONFIG_ZMK_DISPLAY_BLANK_ON_IDLE=n
```

These disable the ZMK display UI without changing RGB, sleep, Studio or keymap
behavior. The device-tree display definition remains available for restoring
the OLED later.

OLED contrast (32) and built-in screen selection now use display-conditional
shield defaults instead of unconditional `.conf` assignments. This preserves
the original OLED appearance while allowing clean OLED-disabled configuration.

**Before removing either OLED, turn both halves off and unplug USB.** Avoid
shorting exposed pins. Restore the original firmware variants to use OLEDs
again.
