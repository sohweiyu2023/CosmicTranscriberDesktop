#!/usr/bin/env python3
"""Generate the synthetic media fixtures used by the offline regression suites.

Requires ffmpeg on PATH. Every file is a generated tone, test pattern or deliberately
broken synthetic file: no recordings, speech or user data. Output goes to a fresh
directory outside the repository (the source-export audit rejects media files).

    python scripts/generate_media_fixtures.py <fixtures-dir>

Produces the 31 container/codec cases plus the error cases MediaInputRegression reads,
and synthetic-short.mp3 / synthetic-long.mp3 for DesktopRegression and
PendingRecoveryRegression.
"""
from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

TONE = ["-f", "lavfi", "-i", "sine=frequency=440:duration=2.5:sample_rate=44100"]
VIDEO = ["-f", "lavfi", "-i", "testsrc=size=160x120:rate=10:duration=2.5"]

# name -> (inputs, output options). The container is chosen by -f where the name lies.
CASES: dict[str, tuple[list[str], list[str]]] = {
    "tone.wav": (TONE, ["-c:a", "pcm_s16le"]),
    "tone.flac": (TONE, ["-c:a", "flac"]),
    "tone.m4a": (TONE, ["-c:a", "aac", "-b:a", "96k"]),
    "lossless.m4a": (TONE, ["-c:a", "alac", "-f", "ipod"]),
    "tone.aac": (TONE, ["-c:a", "aac", "-f", "adts"]),
    "tone.ogg": (TONE, ["-c:a", "libvorbis"]),
    "tone.opus": (TONE, ["-c:a", "libopus", "-ar", "48000"]),
    "tone.webm": (TONE, ["-c:a", "libopus", "-ar", "48000"]),
    "tone.mp4": (TONE, ["-c:a", "aac"]),
    "tone.mkv": (TONE, ["-c:a", "flac"]),
    "tone.avi": (TONE, ["-c:a", "pcm_s16le"]),
    "tone.wma": (TONE, ["-c:a", "wmav2", "-f", "asf"]),
    "tone.wmv": (TONE, ["-c:a", "wmav2", "-f", "asf"]),
    "tone.aiff": (TONE, ["-c:a", "pcm_s16be"]),
    "tone.caf": (TONE, ["-c:a", "pcm_s16le"]),
    "tone.ac3": (TONE, ["-c:a", "ac3"]),
    "tone.mpg": (TONE, ["-c:a", "mp2", "-f", "mpeg"]),
    "tone.ts": (TONE, ["-c:a", "aac", "-f", "mpegts"]),
    "tone.m4b": (TONE, ["-c:a", "aac", "-f", "ipod"]),
    "tone.wv": (TONE, ["-c:a", "wavpack"]),
    "tone.tta": (TONE, ["-c:a", "tta"]),
    "tone.w64": (TONE, ["-c:a", "pcm_s16le", "-f", "w64"]),
    "tone.mp3": (TONE, ["-c:a", "libmp3lame", "-b:a", "128k"]),
    "unusual.extension": (TONE, ["-c:a", "flac", "-f", "flac"]),
    "misnamed.mp3": (TONE, ["-c:a", "pcm_s16le", "-f", "wav"]),
    "tone.mov": (TONE, ["-c:a", "pcm_s16le"]),
    "tone.3gp": (TONE, ["-c:a", "aac", "-ar", "16000", "-ac", "1"]),
    "tone.au": (TONE, ["-c:a", "pcm_mulaw", "-ar", "8000"]),
    "video-audio.mp4": (VIDEO + TONE, ["-c:v", "mpeg4", "-c:a", "aac", "-shortest"]),
    "video-audio.avi": (VIDEO + TONE, ["-c:v", "mpeg4", "-c:a", "pcm_s16le", "-shortest"]),
    "video-audio.mkv": (VIDEO + TONE, ["-c:v", "mpeg4", "-c:a", "flac", "-shortest"]),
    "video-only.mp4": (VIDEO, ["-c:v", "mpeg4", "-an"]),
    "space & $(echo x); 'quote' é 字.m4a": (TONE, ["-c:a", "aac"]),
    "long.m4a": (["-f", "lavfi", "-i", "sine=frequency=440:duration=650:sample_rate=44100"], ["-c:a", "aac", "-b:a", "64k"]),
}
MP3S = {
    "synthetic-short.mp3": "sine=frequency=440:duration=20:sample_rate=44100",
    "synthetic-long.mp3": "sine=frequency=440:duration=650:sample_rate=44100",
}


def ffmpeg(arguments: list[str]) -> None:
    command = ["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", *arguments]
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(f"ffmpeg failed ({result.returncode}): {' '.join(arguments[-1:])}\n{result.stderr.strip()}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    media = args.output.resolve() / "media"
    mp3 = args.output.resolve() / "mp3"
    for directory in (media, mp3):
        directory.mkdir(parents=True, exist_ok=False)
    for name, (inputs, options) in CASES.items():
        ffmpeg([*inputs, *options, "-map_metadata", "-1", str(media / name)])
    # Broken and hostile inputs: a truncated MP4 box stream and a playlist dressed as M4A.
    whole = (media / "tone.m4a").read_bytes()
    (media / "corrupt.m4a").write_bytes(whole[:64] + bytes(range(256)) * 8)
    (media / "playlist.m4a").write_text("#EXTM3U\n#EXTINF:1,remote\nhttp://example.invalid/remote.mp3\n", encoding="utf-8")
    for name, source in MP3S.items():
        ffmpeg(["-f", "lavfi", "-i", source, "-c:a", "libmp3lame", "-q:a", "5", str(mp3 / name)])
    print(f"Generated {len(CASES) + 2} media fixtures in {media} and {len(MP3S)} MP3 fixtures in {mp3}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
