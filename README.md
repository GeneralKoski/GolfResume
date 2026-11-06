# GolfResume

A [BepInEx](https://github.com/BepInEx/BepInEx) mod for **Golf With Your Friends** that lets you pick a game back up after someone disconnects.

The game has no way to rejoin a match in progress: when a player drops, everyone has to start over. GolfResume records every player's score hole by hole, and when you start a new game on the same course the host can restore those scores and jump straight to the hole where you stopped.

> Unofficial fan mod, not affiliated with or endorsed by Team17 or Blacklight Interactive.

## Features

- **Automatic recording**: every player's strokes are saved hole by hole while you play, one JSON file per game.
- **One-key restore** (host, default `F8`): restores the scores of the last game on the current course and moves everyone to the first hole still to be played. The *Set Hole* game option is not needed.
- **Catch-up for players who are behind**: if someone joined late or a new player is in the lobby, they play the holes they are missing on their own while everyone else waits "in the hole", then the whole group continues together.
- **Safe matching**: a save is used only if every player in it is present again; otherwise nothing is touched and the mod tells you who is missing.
- The scoreboard is refreshed right away, without waiting for the end of the hole.

## Status

Tested on Windows with the Steam version of the game (build 303, June 2025), solo and with a second local player. **Online play with other people has not been tested yet**: feedback is welcome.

## Requirements

- Golf With Your Friends (Steam, Windows)
- [BepInEx 6 bleeding edge](https://builds.bepinex.dev/projects/bepinex_be), **Unity IL2CPP, win-x64** build (tested with `6.0.0-be.793`)

Only the **host** needs the mod (to be confirmed online, see Status).

## Installation

1. Download the BepInEx `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.*.zip` build and extract it into the game folder, next to `Golf With Your Friends.exe`.
   (Steam: right-click the game > Manage > Browse local files.)
2. Start the game once and wait for the main menu: the first launch takes a few minutes while BepInEx generates its files. Then close the game.
3. Copy `GolfResume.dll` into `BepInEx\plugins`.
4. Start the game. A BepInEx console window opens alongside it; the log should show `GolfResume loaded`.

To uninstall, delete `GolfResume.dll` (or the whole BepInEx installation: `BepInEx\`, `dotnet\`, `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `changelog.txt`).

## Usage

1. Play as usual. Scores are saved automatically.
2. If someone drops, create a new lobby with the same players on the **same course** and start the game.
3. As host, at the very start of the game, **before anyone shoots**, press `F8`.
   - If the game is still loading, the mod asks you to try again in a few seconds.
   - If someone has already shot, the restore is refused: the game keeps overwriting the current hole's score with the shot count, so restored scores would be lost.
4. The mod restores the scores and moves everyone to the first hole still to be played.

### Catch-up

If some players have fewer holes than others (for example a fifth player joined), the mod moves everyone to the first hole that anyone is missing. Players who already have that hole are kept "in the hole" and **must not shoot**; the others play it. This goes on until everyone is level, then the group plays together again.

Online, a waiting player can still shoot from their own PC (the mod on the host only stops the game from waiting for them). If they do, their saved score for that hole is overwritten, so tell them to wait.

## Configuration

`BepInEx\config\generalkoski.golfresume.cfg`:

| Setting | Default | Description |
|---|---|---|
| `RestoreKey` | `F8` | Key the host presses to restore |

## Saved games

Saved in `BepInEx\GolfResume\`, one file per game, named `yyyyMMdd-HHmmss_<course>.json`. Each file holds the course, the players (Steam ID and name), the score of every hole and the history of each hole as it was completed. You can delete old files at any time.

## Building from source

Requires the .NET SDK (6 or later) and an existing BepInEx installation in the game, started at least once so that `BepInEx\interop` exists.

```sh
cd src
dotnet build -c Release
```

By default the project references BepInEx in the standard Steam path. Point it elsewhere with:

```sh
dotnet build -c Release -p:BepInExDir="/path/to/Golf With Your Friends/BepInEx"
```

The output is `src/bin/Release/net6.0/GolfResume.dll`.

## How it works

- Scores live in each player's `User` object and are synced as the Photon player property `score`. The mod reads them once per second and writes the save file whenever something changes.
- Restoring uses the game's own `User.SetHoleScore` and pushes the change to the other players through Photon.
- The hole jump sets the same hole request the in-game *Set Hole* menu uses, and the game's own flow moves every player there.
- During catch-up, waiting players are flagged as in the hole on the host (Harmony hooks on `User.InHole`, property updates and the game's "all players in hole" checks), so the game moves on as soon as the players who are behind finish.

## License

[MIT](LICENSE)
