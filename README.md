# ComputerSetupWizard

A Windows app that sets up a dev machine: it installs tools with winget, signs you in to GitHub, and sets up the SpeakingInBits-Bot so commits and pull requests in SpeakingInBits repos come from the bot while every other repo keeps your own identity.

It's self-contained (WinUI 3 on .NET 10), so it runs on a fresh machine with nothing else installed, and it keeps itself up to date from the GitHub releases.

## Use it

Download `ComputerSetupWizard-win-x64-Setup.exe` (or `-win-arm64-Setup.exe` for an Arm PC) from the [latest release](https://github.com/SpeakingInBits/ComputerSetupWizard/releases/latest) and run it. It installs for your Windows account (no administrator needed), adds Computer Setup Wizard to the Start menu and desktop, and opens the wizard. Uninstall it from **Settings** > **Apps**. The steps are:

1. **Tools**: installs with winget: Git, the GitHub CLI, Claude Code, the .NET 10 SDK, Visual Studio Community 2026 (with the ASP.NET and web development, and .NET MAUI workloads), SQL Server 2025 Developer, Blender and Godot (.NET), plus optional Node.js LTS, PowerShell 7 and VS Code. If Visual Studio 2026 is already installed without those workloads, the wizard adds them. If you installed a tool some other way and winget doesn't list it, **Mark as installed** remembers that for this PC.
2. **GitHub sign-in**: `gh auth login` and your personal global Git identity.
3. **Bot key**: gets this machine's private key for the bot into `C:\Dev`, or another folder you choose for that machine.
4. **Bot identity**: adds one setting to your global Git config, so every repo with a SpeakingInBits remote, including clones you make later, commits as the bot and pushes with bot tokens. It can also clean up clones that older versions of the wizard set up one by one.
5. **Claude Code**: installs the mods from [SpeakingInBits/claude-mods](https://github.com/SpeakingInBits/claude-mods) for your user, so they run in every session, and adds the bot instructions to `~/.claude/CLAUDE.md`. The mod list is read from the marketplace, so new mods show up here without a new wizard. Without the `claude` command line, the mods are turned on in `~/.claude/settings.json` and Claude Code downloads them at the next session start.
6. **Back up and restore**: saves what winget can't install again to a folder the new PC can reach (OneDrive by default, or a USB drive), and puts it back there:
   - **Claude Code**: `CLAUDE.md`, `settings.json`, keybindings, agents, commands, skills, output styles, hooks, rules and each project's memory from `~/.claude`. Not your sign-in, chat history or plugin files.
   - **Cricut Design Space**: `~/.cricut-design-space\LocalData`, with projects saved to this computer, material settings, fonts and images.
   - **Printers and drivers**: Windows' Printer Migration tool (`PrintBrm`). Restoring asks for administrator approval and keeps printers the PC already has.
   - **Apps with secrets**: a list you keep of apps with passwords, API keys or `.env` files to move with your password manager. The wizard saves the list, never the secrets.

   Install the apps on the new PC first, then restore. The wizard's own settings (tools marked as installed, the backup folder and the list) are in `%APPDATA%\ComputerSetupWizard\settings.json`.

The Overview page shows what's done, and every step can be run again.

## Updates

Each time the wizard opens, it checks the latest release. A newer version downloads in the background, and a banner offers **Restart now**; otherwise it installs the next time the wizard opens. If the check fails, for example when offline, it tries again next time. Commands (below) never update, because git and Claude read their output.

The wizard is installed in `%LOCALAPPDATA%\ComputerSetupWizard\current`, which stays the same through updates, because git runs it from there to get bot tokens.

### Moving from the plain exe

Versions released as a plain exe copied themselves to `%LOCALAPPDATA%\Programs\ComputerSetupWizard` and don't update. Install with `Setup.exe` and open the wizard: the Overview shows the bot identity and CLAUDE.md as not set up or outdated. Run the **Bot identity** step (including **Find and clean up**, because those versions set up each clone and pointed it at the old copy) and the **Claude Code** step, then delete the old folder.

## Command line

The same exe has commands for scripts, git and Claude:

```powershell
$wizard = "$env:LOCALAPPDATA\ComputerSetupWizard\current\ComputerSetupWizard.exe"
$env:GH_TOKEN = & $wizard token             # bot token for gh (valid ~1 hour)
& $wizard info                              # app, installation and permissions, no token
```

## How the bot setup works

See [Setting up the SpeakingInBits bot on a new machine](docs/bot-setup.md) for the details, and for doing it by hand.

## Build

Needs the .NET 10 SDK (`winget install Microsoft.DotNet.SDK.10`).

```powershell
dotnet test tests/ComputerSetupWizard.Core.Tests
dotnet run --project src/ComputerSetupWizard
dotnet publish src/ComputerSetupWizard -c Release -r win-x64 -p:PublishSingleFile=true -p:DebugType=none -o artifacts/win-x64
```

A `dotnet run` or `dotnet publish` build isn't installed, so it never updates, and git runs it from wherever it is.

`build.yml` packages the published exe with [Velopack](https://velopack.io) (`vpk pack`, from `dotnet tool install -g vpk`, the same version as the Velopack package in `ComputerSetupWizard.csproj`). Each architecture is its own channel, so an installed wizard only updates to its own package. To try an update before releasing it, pack two versions the same way but without `--noPortable`, extract the older one's `Portable.zip`, and open its `current\ComputerSetupWizard.exe` with `CSW_UPDATE_SOURCE` set to the newer one's output folder.

## Release

1. On GitHub, open **Actions** > **Release** > **Run workflow**.
2. Keep the branch on `main`, choose what changed since the last release (bug fixes, new features or breaking changes) or type an exact version such as `v1.2.0`, and click **Run workflow**.

The Release workflow picks the next version from the latest `vX.Y.Z` tag and runs the same build as pull requests (`build.yml`), which tests and packages both architectures, stamped with that version. Then it creates the tag and a release with generated notes, with `ComputerSetupWizard-win-x64-Setup.exe`, `ComputerSetupWizard-win-arm64-Setup.exe` and the update files attached. It stops before building if the version isn't in `v1.2.0` form, is already used, or the branch isn't `main`.

Installed copies update from the release marked **Latest**, so don't create releases or `v*` tags by hand: they won't get the packages.
