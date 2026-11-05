# Sofle Status for Windows

Replace the Sofle's OLED with a **live layer-number tray icon** and an optional
**always-visible panel above the taskbar**. The panel shows the layer name, left
and right batteries, USB power, output transport, Bluetooth profile, and
Caps/Num/Scroll Lock. It never takes keyboard focus and does not appear in Alt-Tab.

This implementation targets your PandaKB Sofle RGB MX, **nRF52840 / Bluetooth /
ZMK v0.3**, using the companion firmware in `../sofle-rgb-kb-config`. It is not
compatible with PandaKB's separate QMK/Vial/2.4 GHz models.

## Why this approach

| Option | Visible without clicking | Assessment |
| --- | --- | --- |
| Native Windows tray icon | Layer number and low-battery color | Recommended: small, supported Windows interface. Promote the icon out of overflow once. |
| Non-activating desktop panel | Layer name and full status | Recommended alongside the icon. Draggable, topmost, and optional. It sits above, **not inside**, the taskbar. |
| Custom taskbar toolbar / shell modification | Potentially full text | Avoid. Classic deskbands are not a supported Windows 11 taskbar extension path; shell injection and taskbar replacement add fragility. |
| ZMK Studio | Configuration UI | Useful for remapping; its stock RPC is not a live OLED/status mirror. |
| Infer layers from laptop key events | Sometimes | Incorrect for hold-tap, transparent keys, conditional layers, toggles, Studio edits and profile switching. Not used. |

The essential part is **firmware telemetry**. Standard Bluetooth HID keyboard
reports contain keycodes, not the active ZMK layer. A laptop-only application
cannot reliably recover that internal state. Standard battery reporting also
does not provide the full OLED display.

The native .NET/WinForms app uses Windows' Bluetooth APIs directly: no Electron,
browser, Python installation, server, administrator access, keyboard hook,
cloud service, or additional Bluetooth pairing/connection slot.

## Install and use

Requires **Windows 10 2004 or newer, or Windows 11**, and a working Bluetooth LE
adapter. The self-contained release does not require a separate .NET runtime.

Local prebuilt packages are in `artifacts/`:

- `SofleStatus-win-x64.zip`: most Intel/AMD Windows laptops.
- `SofleStatus-win-arm64.zip`: Windows ARM laptops.
- `SofleStatus-Firmware.zip`: both halves, with and without OLEDs, built against
  your existing configuration.

1. Build and flash the firmware addition described below. Keep the OLED attached
   until you verify the replacement.
2. Extract the entire `SofleStatus-win-x64.zip` (or `win-arm64` for a Windows ARM
   laptop) to a permanent folder. Do not move it after enabling startup.
3. Run `SofleStatus.exe`. On first launch choose the already-paired **left** half,
   usually named `Sofle_RGB`. Pair through Windows Bluetooth settings first if
   it is not listed.
4. In Windows taskbar settings, enable Sofle Status under **Other system tray
   icons** (Windows 11), or **Select which icons appear on the taskbar**
   (Windows 10). Windows controls whether new tray icons start in overflow;
   applications cannot reliably force promotion.
5. The tray number is the **actual highest active ZMK layer index**, including
   hold-tap and the LOWER+RAISE conditional ADJUST layer. The panel defaults to
   the bottom-right working area, just above the taskbar.

Right-click the tray or panel for:

- **Choose keyboard**: select a different paired device.
- **Show status panel**: toggle full always-visible status.
- **Start with Windows**: opt-in, current-user startup; no administrator access.
- **Reset panel position**: recover a panel after changing monitors.
- **Edit layer names / Reload settings**: change names after editing your keymap.
- **Connection details / Open log**: actionable Bluetooth errors.
- **Exit**.

Drag the panel with the left mouse button; its position is saved. It is topmost,
including over fullscreen content, so hide it for games/presentations if needed.
Your existing keymap's names are preconfigured:
`0 BASE`, `1 LOWER`, `2 RAISE`, `3 ADJUST`, `4 GAMING`.
Names are local labels, not fetched from Studio; update them if you rename or
reorder layers. Unknown indices display `Layer N`.

Settings and a size-limited diagnostic log live in
`%LOCALAPPDATA%\SofleStatus`. Battery `?` means not sampled yet, **not zero**.
`R offline` reflects the peripheral Bluetooth connection, not an old battery
sample. The right battery may remain unchanged for a long time because ZMK's
battery service notifies on percentage changes. Sample age is available on
hover. `+USB` means USB power is present, **not proof the battery is charging**.
Profiles display 1-5 to match your keymap's printed BT1-BT5 labels (ZMK stores
them as 0-4). A red tray background means a connected half is at or below 20%.

Disconnection clears the displayed layer rather than leaving stale information.
The app retries every five seconds and performs an uncached health read every
30 seconds. Layer updates arrive through notifications, not that timer.
It cannot report what another laptop is doing after you switch away from this
laptop's Bluetooth profile.

For a hardware-free Windows UI preview:

```powershell
.\SofleStatus.exe --demo
```

Demo status is prominently marked **DEMO** and never connects to hardware.
Only one instance may run per Windows session.

## Firmware integration

The local companion repository has been updated, without changing your keymap:

- `zephyr/module.yml` registers a local Zephyr feature module.
- `host-status/` implements the encrypted, read-only GATT status service.
- Its Kconfig enables the feature only on the BLE central/left half, and enables
  peripheral battery fetching and host lock indicators.
- The existing v0.3 reusable GitHub build automatically loads a module at the
  repository root. Its push filters now include the new module.
- OLED contrast and built-in screen selection are conditional shield defaults,
  preserving the original display while supporting OLED-disabled variants.

For immediate installation, use the prebuilt files in `SofleStatus-Firmware.zip`.
For future builds, commit/push those changes to **your firmware repository**, or run its existing
**Build ZMK firmware** workflow after making the changes available there. This
task does not push, flash, reset pairing, or change your Studio keymap for you.
Download the generated `sofle_left-nice_nano_v2-zmk.uf2` and corresponding right
half image. Double-press reset on each half and copy the correctly named UF2 to
that half's bootloader drive. **Do not flash `settings_reset`** for normal
installation: it clears pairing.

Both original OLED-enabled builds remain available so you can first compare
the panel against the physical OLED. The added service changes the GATT table.
If Windows reports **status service missing** after flashing, remove the
keyboard in Windows Bluetooth settings and re-pair it on this laptop's chosen
profile. Only clear that profile on the keyboard if necessary; do not clear all
profiles unnecessarily.

To disable the firmware feature later, set `CONFIG_ZMK_HOST_STATUS=n` in the
left-half configuration.

### Remove the OLED only after comparison

Use `sofle_left-no-oled-nice_nano_v2-zmk.uf2` and
`sofle_right-no-oled-nice_nano_v2-zmk.uf2` before disconnecting the display. See
the companion firmware README for configuration. Verify momentary
LOWER/RAISE, ADJUST, GAMING, batteries, sleep/wake and profile switching first.

**Power off both halves and disconnect USB before removing OLED modules.**
Pull socketed modules evenly; desoldered modules require suitable soldering
tools. Do not short exposed header pins. Keeping the firmware addition does not
require leaving an OLED connected.

## Build the Windows app

Install the **.NET 10 SDK**, then run from Windows PowerShell:

```powershell
.\scripts\Publish-Windows.ps1
# Windows ARM laptop:
.\scripts\Publish-Windows.ps1 -Runtime win-arm64
```

This runs the portable protocol tests, publishes a self-contained application,
and creates the ZIP in `artifacts/`. Keep every extracted file together.
The GitHub Actions workflow builds both Windows architectures.

Cross-builds are also possible from macOS/Linux with the .NET 10 SDK:

```sh
dotnet test tests/Sofle.Status.Tests/Sofle.Status.Tests.csproj -c Release
dotnet publish src/Sofle.Status.Windows/Sofle.Status.Windows.csproj \
  -c Release -r win-x64 --self-contained true -o artifacts/win-x64
```

The Windows GUI and Bluetooth APIs **cannot run on macOS/Linux**. Tests cover
packet shape, endianness, malformed/version-mismatched data, unknown versus zero
batteries, disconnected versus old samples, and status text. Cross-compilation
does not verify live Bluetooth or Windows UI behavior.

To compile the actual companion firmware using ZMK's build container:

```sh
mkdir -p /tmp/sofle-status-build
docker run --rm \
  -v "$PWD/../sofle-rgb-kb-config:/firmware:ro" \
  -v "$PWD/scripts:/scripts:ro" \
  -v /tmp/sofle-status-build:/work \
  -w /work zmkfirmware/zmk-build-arm:stable \
  sh /scripts/build-firmware.sh /firmware /work
```

This checks both halves, with and without OLEDs, against the repository's ZMK v0.3 pin.
Build output stays outside the firmware source tree.

## Status protocol v1

- Service UUID: `8c6b7a10-5f2d-4a7c-9b31-6e8d0f2c4100`
- Characteristic UUID: `8c6b7a10-5f2d-4a7c-9b31-6e8d0f2c4101`

Encrypted **READ + NOTIFY**, exactly **20 bytes**, little-endian. This fits the
default BLE ATT payload without MTU negotiation. Only the active paired host
may read it, and notifications are addressed only to that host, not broadcast
to split peripherals or other paired profiles. No keycodes or typed text are
included; the service has no keyboard-control write command.

| Offset | Field |
| --- | --- |
| 0 | Version, 1 |
| 1 | Highest active layer index |
| 2, 3 | Central/left and peripheral/right battery percentage; 255 = unknown |
| 4 | Active Bluetooth profile, zero-based |
| 5 | Output: 0 none, 1 USB, 2 Bluetooth |
| 6 | Flags: bit 0 active host connected, bit 1 USB powered, bit 2 split peripheral connected, bit 3 lock indicators supported |
| 7 | HID lock indicators: bit 0 Num, bit 1 Caps, bit 2 Scroll |
| 8-11 | Active layer bitmask (stable layer IDs; index and ID can differ after Studio reordering) |
| 12-15 | Update sequence number; wraps at uint32 maximum |
| 16-19 | Age in seconds of right battery sample; `0xffffffff` = unknown |

Firmware publishes after actual state events with 10 ms coalescing. It does not
poll keys or send an always-running firmware heartbeat. Right-half battery
fetching adds standard BAS subscription traffic; app health reads add two small
reads per minute. Battery-life impact must be measured on your hardware.
ZMK v0.3 has only USB/Bluetooth endpoint types; protocol reserves output 0 for
future firmware.

## Sources used for the design

- [PandaKB Sofle RGB MX build guide](https://pandakb.com/guides/sofle-rgb-mx-build-guide/):
  nRF52840 wireless versus RP2040 wired controllers.
- [ZMK modules](https://zmk.dev/docs/development/module-creation) and
  [v0.3 reusable build workflow](https://github.com/zmkfirmware/zmk/blob/v0.3/.github/workflows/build-user-config.yml):
  local module loading without replacing PandaKB's shield.
- [ZMK v0.3 keymap API](https://github.com/zmkfirmware/zmk/blob/v0.3/app/include/zmk/keymap.h):
  actual active layer and stable layer IDs.
- [Windows notification area](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area):
  supported tray interface and user-controlled icon visibility.
- [Windows Bluetooth GATT](https://learn.microsoft.com/en-us/windows/uwp/devices-sensors/gatt-client):
  native discovery, encrypted access and notifications.
