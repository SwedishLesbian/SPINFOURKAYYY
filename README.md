<p align="center">
  <img src="docs/assets/SpinFOURKAYYY-icon.png" alt="Spin's FOURKAYYY" width="220">
</p>

<h1 align="center">SpinFOURKAYYY</h1>

<p align="center">
  <strong>A simpler, more readable EverQuest Legends experience on high-resolution displays.</strong>
</p>

<p align="center">
  <a href="https://github.com/itsspin/SPINFOURKAYYY/releases/latest"><strong>Download the latest release</strong></a>
  &nbsp;&middot;&nbsp;
  <a href="https://itsspin.github.io/spintexture/"><strong>Improve textures with SpinTexture</strong></a>
  &nbsp;&middot;&nbsp;
  <a href="https://github.com/itsspin/spinips"><strong>Try SpinUI</strong></a>
  &nbsp;&middot;&nbsp;
  <a href="https://github.com/itsspin/spinips#spins-loremaster"><strong>Try Spin's Loremaster</strong></a>
</p>

SpinFOURKAYYY makes the complete EverQuest Legends interface easier to read on 4K, ultrawide, and other high-resolution monitors. Choose the size that feels right, launch EverQuest normally, and the app prepares your personal UI layout for you.

It works with the default UI, custom interfaces, and character-specific layouts. Your macros, hotbuttons, keybinds, spell sets, chat settings, and other character data are left alone.

## What it does

- Offers every UI size from **100% to 200%** in simple 1% steps.
- Remembers your Legends folder, monitor, size, quality, and overlay choices.
- Automatically fits your existing layout to the size you choose.
- Remembers layout changes separately for each size.
- Creates desktop shortcuts that play straight away with your saved choices.
- Restores your native layout when EverQuest exits.
- Keeps supported DPS meters and companion overlays above the scaled game.
- Includes readable presets for a quick, good-looking setup.
- Uses safe backups and recovery if the game, app, or Windows closes unexpectedly.
- Checks completed GitHub Releases and can verify, install, and reopen updates for you.
- Runs alongside EverQuest without injecting into or modifying the game.

## Quick start

1. Download the latest ZIP from [GitHub Releases](https://github.com/itsspin/SPINFOURKAYYY/releases/latest).
2. Extract the entire ZIP into its own folder.
3. Open `SpinFOURKAYYY.exe` and choose your monitor and UI size.
4. Click **Start EverQuest for me**, then patch and sign in through the normal launcher.
5. Keep SpinFOURKAYYY open while playing and exit EverQuest normally when finished.

Once your settings feel right, use **Make Shortcut** to put a one-click EverQuest shortcut on your desktop.

Your first launch at a new size may take a moment while the app prepares a matching copy of your current layout. After that, you can play normally and move windows or edit hotbars as usual. Those layout changes are saved for that size when the game closes.

The next time you open SpinFOURKAYYY, your last setup is ready to use again.

## Automatic updates

SpinFOURKAYYY checks the latest completed GitHub Release when it opens. When a newer stable version is available, the app clearly offers to download and safely verify it before anything closes.

When you start from a desktop shortcut, SpinFOURKAYYY still shows that an update is available but never interrupts the launch to ask about it. Use **Install update** whenever it suits you.

Choose **Install update** after EverQuest is closed. SpinFOURKAYYY keeps your saved settings in place, replaces only files owned by the app, reopens the new version automatically, and confirms the installed version. You can also use **Check updates** in the top-right corner at any time.

## Play with SpinTexture

If you use [SpinTexture](https://itsspin.github.io/spintexture/), install and verify your enhanced texture pack there first. Then click **Play Enhanced EQ** in SpinFOURKAYYY and choose `SpinTexture.exe` the first time. The app remembers that location, asks SpinTexture to verify the installed pack, starts EverQuest without LaunchPad replacing it, and applies your normal SpinFOURKAYYY layout and scaling choices.

Use **Start EverQuest for me** whenever the game needs an update. After updating, reinstall or verify the enhanced pack in SpinTexture before using **Play Enhanced EQ** again. The enhanced flow never reads or reuses account credentials; sign-in behavior may differ from the normal launcher.

## Desktop shortcuts

Choose **Make Shortcut** in the top-right corner to put a one-click shortcut on your desktop.

- **Make EverQuest Shortcut** starts EverQuest through the normal launcher.
- **Make Enhanced EverQuest Shortcut** starts the enhanced texture pack through SpinTexture.

Either shortcut opens SpinFOURKAYYY and begins your last used setup immediately, with the same folder, display, size, quality, UI and overlay choices you had saved. The shortcut shows the EverQuest Legends icon from your own installed client, so no game artwork is copied onto your computer. If no Legends folder is selected yet, the SpinFOURKAYYY icon is used instead.

If SpinFOURKAYYY cannot use your saved settings, it opens normally and tells you what needs attention instead of starting the game. The enhanced shortcut needs SpinTexture chosen once through **Play Enhanced EQ** before it will work. SpinFOURKAYYY still runs one at a time, so close it before using a shortcut.

You can also start SpinFOURKAYYY with `--play` or `--play-enhanced` yourself; the shortcuts simply save you from typing them.

## Choosing a size

- **100% · Native pixels** keeps the original game image and UI size.
- **110–125%** is a great starting range for a clearer, gently enlarged interface.
- **150% · Balanced** gives a noticeably larger and easier-to-read UI.
- **200% · Comfort** provides the largest interface for maximum readability.

On smaller displays, SpinFOURKAYYY may use the nearest safe percentage so EverQuest's sign-in and character-select controls remain fully usable.

**Readable UI** is the recommended quality mode. If small text looks soft, leave anti-aliasing off and try a slightly larger size instead of adding more sharpening.

Choose your settings before launching. To change size or quality later, exit EverQuest, select the new options, and start a fresh managed session.

## Companion overlays

Leave **Keep companion overlays visible** enabled for Loremaster, EQBuddy, EQ Legends Companion, and similar companion HUDs. SpinFOURKAYYY keeps recognized overlays above the scaled game without resizing their pixels, so their text remains native and sharp.

Overlay controls stay clickable when you point at them, while hidden overlay areas are kept from blocking clicks in EverQuest.

Supported overlays can open before or after scaling starts. Run the overlay, EverQuest, and SpinFOURKAYYY at the same Windows privilege level.

If EQ Legends Companion is set to hide overlays when the game loses focus, turn that option off while using fullscreen scaling.
SpinFOURKAYYY also leaves Companion's own lock and click-through controls in charge, so lock a panel when you are not editing it.

## More from Spin

- **[SpinTexture](https://itsspin.github.io/spintexture/)** improves EverQuest Legends world textures while preserving the game's classic look.
- **[SpinUI](https://github.com/itsspin/spinips)** is a complete EverQuest Legends interface overhaul with matching layouts for different resolutions.
- **[Spin's Loremaster](https://github.com/itsspin/spinips#spins-loremaster)** is a live encounter, progression, loot, travel, and adventure companion included with the SpinUI project.

All three are optional. SpinFOURKAYYY still works with the default interface, custom UIs, and your existing textures. Select **Current/default/custom UI** if you do not use SpinUI.

## Helpful notes

- EverQuest must be closed before starting a new managed session.
- Close SpinFOURKAYYY before opening a desktop shortcut; only one copy runs at a time.
- Extract the release completely; do not run it from inside the ZIP.
- Keep `SpinFOURKAYYY.exe` and the included `Engine` folder together.
- Administrator access is not required or recommended.
- Close any separately installed copy of Magpie before launching, or tick **Close other Magpie for me** and SpinFOURKAYYY will ask it to quit for you.
- If a session is interrupted, reopen SpinFOURKAYYY and let it finish recovery.

## The scaling engine

SpinFOURKAYYY brings its own copy of the Magpie scaling engine and keeps it in its own folder, separate from any Magpie you may have installed yourself. You never need to install, update, or start it.

Because each SpinFOURKAYYY version keeps its own copy, an engine left running by a previous version is closed and tidied away automatically the next time you open the app. Your settings and EverQuest files are not touched.

The engine only reads your size and quality choices when it starts, so if it is still running when you change them, SpinFOURKAYYY restarts it for you. You no longer need to close it yourself between sessions.

If you *have* installed Magpie separately and it is running, SpinFOURKAYYY cannot start its own copy, because Magpie allows only one at a time. It will offer to close yours, asking it to quit normally just as its tray icon would. Tick **Close other Magpie for me** in the top-right corner to have it do that without asking each time. It is off until you turn it on, and it never affects anything other than Magpie.

## Compatibility

- Windows 10 version 1903 or newer, or Windows 11
- AMD, NVIDIA, and compatible Intel graphics
- EverQuest Legends in windowed mode

The Windows release is self-contained. You do not need to install .NET or Magpie separately.

## Building from source

Developers need Windows x64, the .NET 9 SDK, PowerShell, Git, and internet access for the first build.

```powershell
.\build.ps1
```

For a release package with a specific semantic version, use `./build.ps1 -Version 1.0.6`. The GitHub **Release** workflow also accepts either `1.0.6` or `v1.0.6` and applies that version automatically; editing the project file first is not required.

## License and trademarks

SpinFOURKAYYY is MIT-licensed. Its bundled Magpie engine is distributed under GPL-3.0; details are available in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

EverQuest and related assets remain the property of their respective owners. SpinFOURKAYYY does not include or modify EverQuest game assets.
