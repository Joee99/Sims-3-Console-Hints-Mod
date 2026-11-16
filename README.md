# Sims 3 Console Hints Mod

I can never remember what the cheats are called, so this adds suggestions to the cheat
console. Open it with Ctrl+Shift+C, start typing, and the matching commands show up
underneath with a description of what they do.

No more googling to find out what commands are in-game and their exact syntax.

## Install

Put `ConsoleHints.package` into `Documents\Electronic Arts\The Sims 3\Mods\Packages`

Made and tested on 1.67 (Steam)

## Using it

- **Type** and the list filters as you go. Commands starting with what you typed come first.
- **Click** a command to put it in the text bar.
- **Tab** or **down arrow** picks the top one. Keep pressing to work down the list.
- **Mouse wheel** scrolls when there are more matches than fit.
- Type a command followed by a space and it shows you how to use that command.

Cheats that need `testingcheatsenabled true` are marked if you haven't turned it on yet.
Commands added by your other mods show up too.

## Building it yourself

You'll need Windows and the game's own DLLs, which I can't share here. Pull these
out of `gameplay.package`, `scripts.package` and `simcore.package` (in `Game\Bin`) with S3PE
and put them in a `lib` folder next to `build.py`:

    mscorlib.dll  System.dll  SimIFace.dll  ScriptCore.dll  Sims3GameplaySystems.dll  UI.dll

Then either:

- open `ConsoleHints.sln` in Visual Studio and build. The DLL and the package end up in
  `build\`. (The package step uses Python. Without it you still get the DLL and can import
  it with S3PE the usual way.)
- or close the game and run `python build.py`, which builds the package, copies it into
  your Mods folder and clears the script cache.

## Known issues

- A few hidden cheats aren't listed anywhere by the game, so I've had to add those by hand.
  If one's missing, open an issue and I'll add it.
