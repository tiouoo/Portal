# Portal.Bedrock.Linux

Linux x64 implementation of Portal's Bedrock GDK launcher and installer.

## Prerequisites

- Linux x64 and .NET 10.
- A working network connection on the first launch, or an existing Proton build capable of running Minecraft
  Bedrock GDK. The resolver can manage GDK-Proton automatically.
- A Steam installation. The resolver checks `~/.steam/root`, `~/.steam/steam`,
  `~/.local/share/Steam`, and the Flatpak Steam data directory.
- A working Vulkan driver and the 64-bit/32-bit graphics libraries required by the selected Proton build.

Set `PORTAL_PROTON_PATH` to either the Proton `proton` script or its containing directory to force a specific
runtime. If it is not set, the resolver searches Steam's `compatibilitytools.d` directories first, then previously
managed runtimes. Only when neither exists does it query the maintained GitHub Releases endpoints for
`Weather-OS/GDK-Proton` (with `LukasPAH/GDK-Proton-Custom` as a fallback), select the Linux x64 `.tar.gz` asset,
and download it. A failed automatic download reports this variable as the manual recovery path.

Managed releases are installed under `$XDG_DATA_HOME/Portal/Bedrock/proton/<tag>`, or
`~/.local/share/Portal/Bedrock/proton/<tag>`. Archives are retained under
`$XDG_CACHE_HOME/Portal/Bedrock/proton/<tag>`, or `~/.cache/Portal/Bedrock/proton/<tag>`. Cached archives are
SHA256-verified against the GitHub asset digest when supplied. When the API has no digest, the resolver computes
SHA256 after download, saves a `.sha256` sidecar, and verifies it before reuse. Extraction rejects absolute paths,
path traversal, links escaping the install directory, and special tar entries. The installed `proton` script is made
executable. Download progress is forwarded through the launch progress and log callbacks.

Set `PORTAL_BEDROCK_PREFIX` to override the dedicated compat data directory. The default is
`$XDG_DATA_HOME/Portal/Bedrock/proton-prefix`, or `~/.local/share/Portal/Bedrock/proton-prefix`.

## Scope

- Only GDK Release/Preview x64 builds are listed, downloaded, MD5-verified, and installed.
- Launch runs `Minecraft.Windows.exe` through Proton and exposes the actual Proton process. Standard output and
  standard error are forwarded through `IBedrockLaunch.LogReceived`.
- If an instance contains `Installers/GameInputRedist.msi`, Portal installs it once into the dedicated Proton prefix
  before launching the game.
- The launcher does not inject DLLs on Linux and does not claim to support UWP packages.
- Steam remains a prerequisite. Proton is discovered locally or downloaded and managed on first use; the game
  installer itself does not download Proton.
- Xbox authentication and Proton-specific first-run prerequisites remain responsibilities of the selected
  GDK-Proton runtime. Resolver errors name the missing prerequisite and the relevant configuration variable.

## Integration

The host must reference this project on Linux, set `BedrockInstallationService.DefaultInstaller` to a
`BedrockInstaller`, and set `MinecraftLaunchService.DefaultBedrockLauncherFactory` to construct `BedrockLaunch`.
Those host changes are intentionally not included because this project was added without modifying existing files.
