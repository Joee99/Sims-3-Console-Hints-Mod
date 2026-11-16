# Sims 3 Console Hints Mod

A small script mod for The Sims 3 that adds suggestions to the cheat console (Ctrl+Shift+C).
Start typing and the matching commands show up underneath with their descriptions, so you
don't have to remember exactly what `disableSnappingToSlotsOnAlt` is called.

- Lists commands as you type. Names starting with what you typed come first, then names
  that contain it.
- Click a suggestion to put it in the input line.
- Tab or the down arrow picks the top match. Press again to move down the list.
- Once you've typed a command and a space, it shows that command's usage text.
- Commands that only work with `testingcheatsenabled true` are marked when it's off.
- Picks up commands added by other mods too, since the list is read from the game each
  time the console opens.

Built and tested on 1.67 (Steam). It's a plain script mod, no core mod or ASI loader needed.

## Install

Drop `ConsoleHints.package` into `Documents\Electronic Arts\The Sims 3\Mods\Packages`.

## Building

You need Windows with the .NET Framework 4 compiler (`csc.exe`, comes with Windows) and
Python 3.

The build compiles against the game's own assemblies, which I can't include here. Extract
these from the game with S3PE (they're the S3SA resources in `gameplay.package`,
`scripts.package` and `simcore.package` under `Game\Bin`) and put them in a `lib` folder next
to `build.py`, or point the `SIMS3_LIBS` environment variable at wherever you keep them:

    mscorlib.dll  System.dll  SimIFace.dll  ScriptCore.dll  Sims3GameplaySystems.dll  UI.dll

Then, with the game closed:

    python build.py

That compiles `src/*.cs`, wraps the DLL, the tuning XML and the key binding for the down arrow into a package, copies it into
your Mods folder and deletes `scriptCache.package` so the game reloads scripts.
`python build.py --no-deploy` just builds into `build/`.

## How it works

Short version: the console is `Sims3.UI.CommandConsoleDialog`, and the game keeps the open
one in a public static. A tiny task polls that field, and when a console appears the mod
subscribes to the input box's `TextChange` event and writes suggestions into the box the
game normally uses for `help` output. More detail in [docs/NOTES.md](docs/NOTES.md).

## Known limitations

- A few hidden commands aren't listed by the game anywhere, so those are hard-coded
  (`help`, `freeRealEstate`, `unlockOutfits`). If you know of others, open an issue.
