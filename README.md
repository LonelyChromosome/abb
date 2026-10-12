# Henshin Grid — Windows personal game launcher

A local-first game library with an OLED-black (#000000) cyber/holographic UI and optional animated desktop wallpaper.

## What it does
- Discover existing Steam/Epic games from local install manifest files at first startup.
- Watch installation manifest directories and **only append newly installed games** to the library. Also performs one quick reconciliation scan when opened in case a game was installed while the launcher was closed.
- Add `.exe`, `.lnk`, `.url` games manually; search, favorite, launch from their original locations.
- Choose a local JPG/PNG/WebP/GIF or MP4/WMV/AVI/M4V wallpaper. Video uses Windows built-in codecs; GIF is static first frame in this version.
- True-black OLED canvas and black dimming layer. Adjust dim from 0% to 100%; select preset accent colors or pick a custom RGB color; toggle the hologram HUD.
- Experimental optional desktop wallpaper behind Windows icons (primary display, Explorer WorkerW). Disable at any time; unsupported Windows shell versions may decline to attach. Video playback is paused when a fullscreen foreground window is detected.

## Privacy and anti-cheat boundaries
Reads Steam/Epic **manifest files** and user-selected game shortcuts only. Does not inject a DLL, modify game memory or files, change network routing, install a driver/service, or require admin rights. It asks Windows / Steam / Epic to open a game only after user click. Does not attempt to bypass game anti-cheat.

## Build (Windows 10/11, .NET 8 SDK)
```powershell
dotnet restore .\HenshinGridLauncher.csproj
dotnet run --project .\HenshinGridLauncher.csproj
```

To publish a self-contained x64 executable:
```powershell
dotnet publish .\HenshinGridLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish
```

Open `publish\HenshinGrid.exe`. This is the launcher, **not a game installer**.

## Limits / status
- Source draft for Windows. The working environment cannot run or compile WPF, so **a Windows build must validate it** before a binary can be declared working.
- Detects Steam/Epic and manually selected games initially. Xbox, GOG, Battle.net and custom install folders need separate detection adapters.
- Epic/Steam games keep their original launcher requirements. Windows desktop video mode relies on an undocumented Explorer WorkerW implementation; it is best-effort and not equivalent to all Wallpaper Engine features.
- Some video codecs are unsupported by WPF MediaElement; use H.264 MP4 for the best compatibility.
- Library configuration is stored at `%LOCALAPPDATA%\HenshinGrid\library.json`.

## Design contract
OLED uses literal `#000000` for both the window background and black wallpaper shade. Colored neon may appear only in HUD lines/cards. Selecting a background never converts underlying true black to navy or gray. Dim=100% gives a pure black wallpaper area (UI/HUD can remain visible). Turning HUD off removes decorative animation and grid.