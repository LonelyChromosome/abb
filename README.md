# Henshin Grid

Windows desktop game launcher — cyber holographic / Kamen Rider-inspired visual design.

## Design contract
- **OLED black** is literal `#000000`, never a gray/navy replacement.
- Game launcher handles **library and launching only**: no anti-cheat hooks, process injection, network modification or FPS boosters.
- First-run discovery from Steam/Epic install manifests; new install manifests trigger add-only refresh.
- Select a **custom image or video wallpaper**, adjust 0–100% black dim overlay, choose accent color and toggle hologram HUD.
- Optional animated wallpaper behind Windows desktop icons, explicitly opt-in. WorkerW mode is experimental and primary-monitor-only.
- Customize background independently of the game library; settings stored locally.

## Implementation
C# / WPF for Windows 10/11; target .NET 8. A Windows build is required to validate the code and produce an executable.

## Status
Initial repository setup. Source draft exists separately and needs integration/build validation; no Windows binary has been verified yet.
