# Little Witch Co-op

Source for the experimental 0.10.6 Windows/Steam mod for Little Witch in the Woods.
This repository is prepared for inspection and building the plugin, including
review of a Nexus Mods automated quarantine. It is not an antivirus clearance
or a formal security audit. No game files or loader binaries are included.

## Features and status

Steam public room browser, private-code invitations, direct TCP/IP (LAN/Radmin),
remote Ellie visuals, chat, emotes, player names, and host kick/ban controls.
F2 opens settings; Y opens chat; B opens emotes. Windows language is the initial
PT-BR/English default, with a saved manual override in the settings gear.
Up to 99 slots is experimental: roster/chat were simulated at that scale, not
99 actual games. Two-PC Steam public discovery/joining and Radmin worked in
previous builds. The new private-code flow still needs live two-account testing.
Each player keeps their own save and progress; world state is not a shared save.

## Build

See [BUILDING.md](BUILDING.md) for exact dependencies and commands. Build.ps1
requires your legally installed Windows game and BepInEx 5.4.23.5. The public
build excludes development preview buttons. Package-PluginOnly.ps1 produces a
minimal archive without scripts or loader binaries.

## Review map

- src/Plugin.cs: BepInEx entry point, UI, cursor and input blocking.
- src/NetworkRoom.cs, RoomState.cs: direct TCP transport and host relay.
- src/SteamRoom.cs, SteamLobbyBrowser.cs: Steam transport, invitations and discovery.
- src/PrivateRoomCode.cs: random private-code encoding/validation.
- src/ChatOverlay.cs, EmoteWheel.cs, EmoteWav.cs: in-game chat, audio and emotes.
- src/L10n.cs: UI text and read-only Windows display-language lookup.
- packaging/Install.cmd, Uninstall.cmd: optional local installation scripts.

The mod exchanges gameplay snapshots, player names, chat and emote identifiers.
Private codes are sent to the encoded host through Steam to request an invite;
there is no external code registry. Direct IP hosts listen on the selected TCP
port (7777 by default). Config, ban lists and optional WAVs are local files.
The copy button writes a room code to the clipboard. The Windows language call
is GetUserDefaultUILanguage; the mod does not change Windows security settings.

## Assets and permissions

Twemoji graphics are credited in assets/ATTRIBUTION.txt and covered by the
included CC BY 4.0 license. BepInEx is a separate MIT-licensed prerequisite.
Personal WAVs, Minecraftia, saves, machine logs, and proprietary game assemblies
are excluded. No project-wide license grant for the mod's own source has been
selected yet; publication here is for inspection. Do not assume an MIT/GPL grant.

## Nexus review

A source link helps reviewers inspect compiled code; it does not identify which
file triggered a generic warning or guarantee expedited review. Keep the flagged
upload available for moderators. Reviewers should compare the specific artifact
hashes in BUILD-REFERENCE.json and follow BUILDING.md to compile independently.
