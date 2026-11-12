# dwm_lut v5.1.0 — Windows 11 25H2 / 26H2 Support
### A fork of [lauralex/dwm_lut](https://github.com/lauralex/dwm_lut) (original by [ledoge](https://github.com/ledoge/dwm_lut))

## [Download v5.1.0 for Windows 11 25H2 / 26H2](https://github.com/DeDuplicate/dwm_lut/releases/latest)

> **v5.1.0** fixes **Windows 11 26H2** (and 25H2 with recent cumulative updates, `dwmcore.dll` 10.0.26100.9818+), where v5.0.0 either did nothing or crashed DWM. Tested on OS build 26300.9974.
>
> **v5.0.0** added support for **Windows 11 25H2 (build 26200+)** where the DWM architecture changed significantly. If you are on an older Windows version, use the [upstream release](https://github.com/lauralex/dwm_lut/releases/latest/download/Release.zip) instead.

### What changed in v5.1.0
- The `COverlayContext::Present` signature scan no longer depends on stack size / register allocation, which changed in the new `dwmcore.dll`.
- Monitor position is no longer a hardcoded struct offset: it is decoded from DWM's own `COverlayContext::GetMonitorTransform`, so per-monitor LUTs keep working when the layout shifts.
- `COverlayContext::OverlaysEnabled` is no longer hooked on 25H2+. New DWM builds rely on that function preserving `rcx`, and the hook crashed DWM (screen flashing, LUT inactive). Setting DWM's overlay test mode already has the same effect.

### After a Windows update
If LUTs stop applying after an update, run `python check_aob.py` from the repo root. It checks that every byte pattern in `lutdwm/dllmain.cpp` still matches exactly once in your `C:\Windows\System32\dwmcore.dll` and tells you which one broke.

# About
This tool applies 3D LUTs to the Windows desktop by hooking into DWM. It works in both SDR and HDR modes, and uses tetrahedral interpolation on the LUT data. In SDR, blue-noise dithering is applied to the output to reduce banding.

Right now it should work on any 20H2 or 21H1 build of Windows 10, and also the current build of Windows 11, and I'll try to update it whenever a new version breaks it.

# Usage
Use DisplayCAL or similar to generate .cube LUT files of any size, run `DwmLutGUI.exe`, assign them to monitors and then click Apply. Note that LUTs cannot be applied to monitors that are in "Duplicate" mode.


For ColourSpace users with HT license level, 65^3 eeColor LUT .txt files are also supported.

HDR LUTs must use BT.2020 + SMPTE ST 2084 values as input and output.

Minimizing the GUI will make it disappear from the taskbar, and you can use the context menu of the tray icon to quickly apply or disable all LUTs. For automation, you can start the exe with any (sensible) combination of `-apply`,  `-disable`, `-minimize` and `-exit` as arguments.

Note: DirectFlip and MPO get force disabled on monitors with an active LUT. These features are designed to improve performance for some windowed applications by allowing them to bypass DWM (and therefore also the LUT). This ensures that LUTs get applied properly to all applications (except exclusive fullscreen ones).

# Compiling
Install [vcpkg](https://vcpkg.io/en/getting-started.html) for C++ dependency management:

- Create and switch to your desired install folder (e.g. _%LOCALAPPDATA%\vcpkg_)
- `git clone https://github.com/Microsoft/vcpkg.git .`
- `.\bootstrap-vcpkg.bat`
- `vcpkg integrate install`

Just open the projects in Visual Studio and compile a x64 Release build.
