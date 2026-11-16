"""Builds ConsoleHints.package and copies it into Mods/Packages.

    python build.py              compile, package, deploy
    python build.py --no-deploy  compile and package only (output goes in build/)
    python build.py --package-only  skip compiling, package build/ConsoleHints.dll as it is
                                    (the Visual Studio project calls this after a build)

Needs the game's DLLs to compile against, see README. Close the game before deploying,
it keeps its packages locked while running.
"""
import glob
import os
import struct
import subprocess
import sys

ROOT = os.path.dirname(os.path.abspath(__file__))
ASSEMBLY = "ConsoleHints"
TUNING_CLASS = "ConsoleHints.Instantiator"

# Where the game DLLs are. Defaults to lib/, override with SIMS3_LIBS.
GAME_LIBS = os.environ.get("SIMS3_LIBS", os.path.join(ROOT, "lib"))
REFERENCES = ["mscorlib.dll", "System.dll", "SimIFace.dll", "ScriptCore.dll",
              "Sims3GameplaySystems.dll", "UI.dll"]

CSC = r"C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
SIMS3_DOCS = os.path.expanduser(r"~\Documents\Electronic Arts\The Sims 3")

S3SA = 0x073FAA07
TUNING_XML = 0x0333406C
TRIGGER_MAP = 0xF0FF5598


def fnv64(text):
    # Sims 3 flavour of FNV: lowercase the input, multiply then xor
    h = 0xCBF29CE484222325
    for byte in text.lower().encode("ascii"):
        h = (h * 0x00000100000001B3) & 0xFFFFFFFFFFFFFFFF
        h ^= byte
    return h


def build_s3sa(dll):
    # Script assembly wrapper, same layout s3pi writes. A zeroed md5 table means
    # the assembly is stored unencrypted.
    blocks = (len(dll) + 511) // 512
    out = bytearray()
    out += struct.pack("<B", 2)
    out += struct.pack("<I", 0)
    out += struct.pack("<I", 0x2BC4F79F)
    out += b"\x00" * 64
    out += struct.pack("<H", blocks)
    out += b"\x00" * (blocks * 8)
    out += dll + b"\x00" * (blocks * 512 - len(dll))
    return bytes(out)


def build_dbpf(resources):
    # resources: list of (type, group, instance, data). Nothing is compressed.
    header_size = 96
    body = bytearray()
    index = bytearray(struct.pack("<I", 0))
    for type_id, group, instance, data in resources:
        index += struct.pack("<IIIIIIIHH", type_id, group, instance >> 32,
                             instance & 0xFFFFFFFF, header_size + len(body),
                             len(data) | 0x80000000, len(data), 0, 1)
        body += data

    header = bytearray(header_size)
    header[0:4] = b"DBPF"
    struct.pack_into("<I", header, 0x04, 2)
    struct.pack_into("<I", header, 0x24, len(resources))
    struct.pack_into("<I", header, 0x2C, len(index))
    struct.pack_into("<I", header, 0x3C, 3)
    struct.pack_into("<I", header, 0x40, header_size + len(body))
    return bytes(header) + bytes(body) + bytes(index)


def game_running():
    result = subprocess.run(["tasklist", "/FI", "IMAGENAME eq TS3W.exe"],
                            capture_output=True, text=True)
    return "TS3W.exe" in result.stdout


def compile_dll(dll_path):
    missing = [r for r in REFERENCES if not os.path.exists(os.path.join(GAME_LIBS, r))]
    if missing:
        sys.exit("Missing game DLLs in %s: %s\nSee the README for where to get them."
                 % (GAME_LIBS, ", ".join(missing)))

    args = [CSC, "/nologo", "/target:library", "/out:" + dll_path, "/nostdlib",
            "/noconfig", "/langversion:4", "/optimize+"]
    args += ["/reference:" + os.path.join(GAME_LIBS, ref) for ref in REFERENCES]
    args += sorted(glob.glob(os.path.join(ROOT, "src", "*.cs")))

    result = subprocess.run(args, capture_output=True, text=True)
    if result.stdout.strip():
        print(result.stdout.strip())
    if result.returncode != 0:
        print(result.stderr)
        sys.exit("Compile failed.")


def main():
    deploy = "--no-deploy" not in sys.argv
    out_dir = os.path.join(ROOT, "build")
    os.makedirs(out_dir, exist_ok=True)
    dll_path = os.path.join(out_dir, ASSEMBLY + ".dll")
    package_path = os.path.join(out_dir, ASSEMBLY + ".package")

    if "--package-only" not in sys.argv:
        compile_dll(dll_path)
    elif not os.path.exists(dll_path):
        sys.exit("Nothing to package, %s doesn't exist yet." % dll_path)

    with open(dll_path, "rb") as f:
        dll = f.read()
    with open(os.path.join(ROOT, "src", "Tuning.xml"), "rb") as f:
        tuning = f.read()
    with open(os.path.join(ROOT, "src", ASSEMBLY + ".triggers"), "rb") as f:
        triggers = f.read()

    package = build_dbpf([
        (S3SA, 0, fnv64(ASSEMBLY), build_s3sa(dll)),
        (TUNING_XML, 0, fnv64(TUNING_CLASS), tuning),
        (TRIGGER_MAP, 0, fnv64(ASSEMBLY), triggers),
    ])
    with open(package_path, "wb") as f:
        f.write(package)
    print("Built %s (%d bytes)" % (package_path, len(package)))

    if not deploy:
        return
    if game_running():
        sys.exit("The Sims 3 is running. Close it and run build.py again to deploy.")

    target = os.path.join(SIMS3_DOCS, "Mods", "Packages", ASSEMBLY + ".package")
    with open(target, "wb") as f:
        f.write(package)
    print("Deployed to " + target)

    # The game caches compiled scripts, clear it so the new assembly gets picked up
    for cache in (os.path.join(SIMS3_DOCS, "scriptCache.package"),
                  os.path.join(SIMS3_DOCS, "ScriptCache", "scriptCache.package")):
        if os.path.exists(cache):
            os.remove(cache)
            print("Deleted " + cache)


if __name__ == "__main__":
    main()
