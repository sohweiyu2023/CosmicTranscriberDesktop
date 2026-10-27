#!/usr/bin/env python3
"""Independent MP3 frame-copy smoke test for Cosmic Transcriber.

Requires ffmpeg and ffprobe on PATH. This does not execute the WPF application;
it independently mirrors the frame-copy boundary rules and validates every
result through a real decoder.
"""

from __future__ import annotations

import argparse
import json
import subprocess
import tempfile
from datetime import date
from dataclasses import dataclass
from pathlib import Path

BITRATES = {
    (3, 1): [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0],
    (2, 1): [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0],
    (0, 1): [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0],
}
SAMPLE_RATES = {
    3: [44_100, 48_000, 32_000],
    2: [22_050, 24_000, 16_000],
    0: [11_025, 12_000, 8_000],
}


@dataclass(frozen=True)
class Frame:
    data: bytes
    sample_rate: int
    samples: int
    version: int
    mono: bool
    has_crc: bool
    metadata: bool
    main_data_begin: int | None

    @property
    def duration(self) -> float:
        return self.samples / self.sample_rate


def run(command: list[str]) -> subprocess.CompletedProcess[bytes]:
    return subprocess.run(command, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)


def skip_id3v2(data: bytes) -> int:
    if len(data) < 10 or data[:3] != b"ID3":
        return 0
    major_version = data[3]
    revision = data[4]
    if major_version not in (2, 3, 4) or revision == 0xFF:
        raise ValueError("Unsupported or invalid ID3v2 version")
    flags = data[5]
    undefined_mask = {2: 0x3F, 3: 0x1F, 4: 0x0F}[major_version]
    if flags & undefined_mask or any(value >= 0x80 for value in data[6:10]):
        raise ValueError("Invalid ID3v2 flags or synchsafe size")
    payload = (data[6] << 21) | (data[7] << 14) | (data[8] << 7) | data[9]
    footer = 10 if major_version == 4 and flags & 0x10 else 0
    end = 10 + payload + footer
    if end > len(data):
        raise ValueError("Truncated ID3v2 tag")
    return end


def parse_header(header: bytes) -> tuple[int, bool, bool, int, int, int] | None:
    if len(header) < 4:
        return None
    bits = int.from_bytes(header, "big")
    if ((bits >> 21) & 0x7FF) != 0x7FF:
        return None

    version = (bits >> 19) & 0x03
    layer = (bits >> 17) & 0x03
    protection_bit = (bits >> 16) & 0x01
    bitrate_index = (bits >> 12) & 0x0F
    sample_rate_index = (bits >> 10) & 0x03
    padding = (bits >> 9) & 0x01
    channel_mode = (bits >> 6) & 0x03

    if version == 1 or layer != 1 or bitrate_index in (0, 15) or sample_rate_index == 3:
        return None

    bitrate = BITRATES[(version, 1)][bitrate_index]
    sample_rate = SAMPLE_RATES[version][sample_rate_index]
    samples = 1152 if version == 3 else 576
    length = ((144_000 if version == 3 else 72_000) * bitrate) // sample_rate + padding
    if bitrate == 0 or length < 4:
        return None

    return version, protection_bit == 0, channel_mode == 3, sample_rate, samples, length


def find_vbr_metadata_tag(
    data: bytes, version: int, mono: bool, has_crc: bool
) -> tuple[int, bytes] | None:
    side_info = 17 if version == 3 and mono else 32 if version == 3 else 9 if mono else 17
    crc_bytes = 2 if has_crc else 0
    for offset in (4 + side_info, 4 + side_info + crc_bytes):
        if data[offset : offset + 4] in (b"Xing", b"Info"):
            return offset, data[offset : offset + 4]
    for offset in (4 + 32, 4 + 32 + crc_bytes):
        if data[offset : offset + 4] == b"VBRI":
            return offset, b"VBRI"
    return None


def is_vbr_metadata_frame(data: bytes, version: int, mono: bool, has_crc: bool) -> bool:
    return find_vbr_metadata_tag(data, version, mono, has_crc) is not None


def rewrite_vbr_metadata_for_chunk(
    metadata: Frame,
    frame_count: int,
    total_bytes: int,
    frame_offsets: list[int],
) -> bytes:
    if frame_count <= 0 or len(frame_offsets) != frame_count or total_bytes <= 0:
        raise ValueError("Invalid chunk totals for VBR metadata rewrite")
    found = find_vbr_metadata_tag(metadata.data, metadata.version, metadata.mono, metadata.has_crc)
    if found is None:
        raise ValueError("Expected Xing/Info/VBRI metadata tag was not found")

    offset, tag = found
    data = bytearray(metadata.data)
    if tag == b"VBRI":
        if offset + 18 > len(data):
            raise ValueError("Truncated VBRI header")
        data[offset + 10 : offset + 14] = total_bytes.to_bytes(4, "big")
        data[offset + 14 : offset + 18] = frame_count.to_bytes(4, "big")
        return bytes(data)

    if offset + 8 > len(data):
        raise ValueError("Truncated Xing/Info flags")
    flags = int.from_bytes(data[offset + 4 : offset + 8], "big")
    cursor = offset + 8
    if flags & 0x0001:
        if cursor + 4 > len(data):
            raise ValueError("Truncated Xing/Info frame count")
        data[cursor : cursor + 4] = frame_count.to_bytes(4, "big")
        cursor += 4
    if flags & 0x0002:
        if cursor + 4 > len(data):
            raise ValueError("Truncated Xing/Info byte count")
        data[cursor : cursor + 4] = total_bytes.to_bytes(4, "big")
        cursor += 4
    if flags & 0x0004:
        if cursor + 100 > len(data):
            raise ValueError("Truncated Xing/Info TOC")
        for percentile in range(100):
            frame_index = min(frame_count - 1, percentile * frame_count // 100)
            frame_offset = frame_offsets[frame_index]
            if frame_offset < 0 or frame_offset >= total_bytes:
                raise ValueError("Invalid MPEG frame offset")
            data[cursor + percentile] = min(255, frame_offset * 256 // total_bytes)
    return bytes(data)


def get_main_data_begin(data: bytes, version: int, has_crc: bool) -> int | None:
    offset = 4 + (2 if has_crc else 0)
    if version == 3:
        if offset + 2 > len(data):
            return None
        return ((data[offset] << 1) | (data[offset + 1] >> 7)) & 0x1FF
    if offset >= len(data):
        return None
    return data[offset]


def parse_frames(path: Path) -> list[Frame]:
    data = path.read_bytes()
    position = skip_id3v2(data)
    frames: list[Frame] = []

    while position + 4 <= len(data):
        parsed = parse_header(data[position : position + 4])
        if parsed is None:
            next_sync = data.find(b"\xFF", position + 1)
            if next_sync < 0:
                break
            position = next_sync
            continue

        version, has_crc, mono, sample_rate, samples, length = parsed
        if position + length > len(data):
            break
        raw = data[position : position + length]
        metadata = is_vbr_metadata_frame(raw, version, mono, has_crc)
        frames.append(
            Frame(
                raw,
                sample_rate,
                samples,
                version,
                mono,
                has_crc,
                metadata,
                get_main_data_begin(raw, version, has_crc),
            )
        )
        position += length

    return frames


def split_frames(
    frames: list[Frame],
    target_seconds: float,
    target_preroll_seconds: float,
) -> list[tuple[list[Frame], float, float]]:
    chunks: list[tuple[list[Frame], float, float]] = []
    recent: list[Frame] = []
    recent_duration = 0.0
    current: list[Frame] = []
    logical_duration = 0.0
    overlap_duration = 0.0
    first_frame = True

    for frame in frames:
        if first_frame and frame.metadata:
            first_frame = False
            # Preserve the carrier bytes for Layer III reservoir continuity, but do not
            # count the non-speech metadata frame in logical or overlap duration.
            current.append(frame)
            continue

        first_frame = False
        if logical_duration > 0 and logical_duration + frame.duration > target_seconds:
            chunks.append((current, logical_duration, overlap_duration))
            current = list(recent)
            logical_duration = 0.0
            overlap_duration = sum(item.duration for item in recent)
            recent = []
            recent_duration = 0.0

        current.append(frame)
        logical_duration += frame.duration
        recent.append(frame)
        recent_duration += frame.duration
        while len(recent) > 1 and recent_duration - recent[0].duration >= target_preroll_seconds:
            recent_duration -= recent.pop(0).duration

    if logical_duration > 0:
        chunks.append((current, logical_duration, overlap_duration))
    return chunks


def decode_pcm(path: Path, sample_rate: int = 44_100, channels: int = 1) -> bytes:
    return run(
        [
            "ffmpeg",
            "-v",
            "error",
            "-i",
            str(path),
            "-f",
            "s16le",
            "-acodec",
            "pcm_s16le",
            "-ar",
            str(sample_rate),
            "-ac",
            str(channels),
            "-",
        ]
    ).stdout


def probe_seconds(path: Path) -> float:
    result = run(
        [
            "ffprobe",
            "-v",
            "error",
            "-show_entries",
            "format=duration",
            "-of",
            "default=nw=1:nk=1",
            str(path),
        ]
    )
    return float(result.stdout.decode("ascii").strip())


def build_report(duration_seconds: int) -> dict[str, object]:
    target_seconds = 300.0
    target_preroll_seconds = 1.25
    sample_rate = 44_100
    bytes_per_pcm_second = sample_rate * 2
    results: list[dict[str, object]] = []

    with tempfile.TemporaryDirectory(prefix="cosmic-media-smoke-") as temporary:
        directory = Path(temporary)
        for profile, encoder_options in (
            ("CBR", ["-b:a", "32k", "-write_xing", "1"]),
            ("VBR", ["-q:a", "5", "-write_xing", "1"]),
        ):
            source = directory / f"{profile.lower()}.mp3"
            run(
                [
                    "ffmpeg",
                    "-y",
                    "-v",
                    "error",
                    "-f",
                    "lavfi",
                    "-i",
                    f"anoisesrc=color=pink:sample_rate={sample_rate}:duration={duration_seconds}:seed=424242",
                    "-ac",
                    "1",
                    "-c:a",
                    "libmp3lame",
                    *encoder_options,
                    str(source),
                ]
            )

            frames = parse_frames(source)
            if not frames:
                raise RuntimeError(f"No MPEG Layer III frames parsed from {source}")
            chunks = split_frames(frames, target_seconds, target_preroll_seconds)
            source_pcm = decode_pcm(source)
            audio_frames = [frame for frame in frames if not frame.metadata]
            rows: list[dict[str, object]] = []

            for index, (chunk_frames, logical_seconds, overlap_seconds) in enumerate(chunks, 1):
                chunk_path = directory / f"{profile.lower()}-{index}.mp3"
                payloads = [frame.data for frame in chunk_frames]
                frame_offsets: list[int] = []
                current_offset = 0
                for payload in payloads:
                    frame_offsets.append(current_offset)
                    current_offset += len(payload)
                metadata_rewritten = bool(index == 1 and chunk_frames and chunk_frames[0].metadata)
                if metadata_rewritten:
                    payloads[0] = rewrite_vbr_metadata_for_chunk(
                        chunk_frames[0], len(chunk_frames), current_offset, frame_offsets
                    )
                chunk_path.write_bytes(b"".join(payloads))
                decoded_pcm = decode_pcm(chunk_path)
                decoded_seconds = len(decoded_pcm) / bytes_per_pcm_second
                expected_seconds = logical_seconds + overlap_seconds
                rows.append(
                    {
                        "index": index,
                        "logical_seconds": round(logical_seconds, 6),
                        "speech_overlap_seconds": round(overlap_seconds, 6),
                        "bytes": chunk_path.stat().st_size,
                        "container_estimated_seconds": round(probe_seconds(chunk_path), 6),
                        "container_vs_expected_delta_seconds": round(
                            probe_seconds(chunk_path) - expected_seconds, 6
                        ),
                        "decoded_pcm_seconds": round(decoded_seconds, 6),
                        "decoded_vs_logical_plus_overlap_delta_seconds": round(
                            decoded_seconds - expected_seconds, 6
                        ),
                        "pcm_matches_source_prefix": (
                            source_pcm[: len(decoded_pcm)] == decoded_pcm if index == 1 else None
                        ),
                        "decoder_exit_code": 0,
                    }
                )

            results.append(
                {
                    "profile": profile,
                    "source_frames": len(frames),
                    "source_decoded_pcm_seconds": round(len(source_pcm) / bytes_per_pcm_second, 6),
                    "first_frame_is_xing_info_vbri": frames[0].metadata,
                    "metadata_carrier_retained_in_chunk_1": bool(
                        chunks and chunks[0][0] and chunks[0][0][0].metadata
                    ),
                    "metadata_header_rewritten_for_chunk_1": bool(
                        chunks and chunks[0][0] and chunks[0][0][0].metadata
                    ),
                    "metadata_carrier_frame_seconds_excluded_from_timestamps": round(
                        frames[0].duration if frames[0].metadata else 0.0, 6
                    ),
                    "first_audio_frame_main_data_begin_bytes": (
                        audio_frames[0].main_data_begin if audio_frames else None
                    ),
                    "second_audio_frame_main_data_begin_bytes": (
                        audio_frames[1].main_data_begin if len(audio_frames) > 1 else None
                    ),
                    "chunks": rows,
                }
            )

    return {
        "test_date": date.today().isoformat(),
        "method": (
            f"Fresh independent rerun: generated deterministic {duration_seconds}-second CBR and VBR MP3 sources; "
            "parsed MPEG Layer III frames; frame-copied nominal five-minute chunks with a contiguous "
            "1.25-second speech-overlap preroll. The initial Xing/Info/VBRI frame was retained in chunk "
            "1 as a decoder reservoir carrier, rewritten with chunk-local frame/byte/TOC totals, and "
            "excluded from logical timestamps. Every chunk was "
            "decoded to mono 44.1 kHz PCM with FFmpeg; chunk 1 PCM was compared byte-for-byte with the "
            "original decoded source prefix. FFprobe container estimates were recorded separately."
        ),
        "target_chunk_seconds": target_seconds,
        "target_preroll_seconds": target_preroll_seconds,
        "results": results,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--output",
        type=Path,
        default=Path(__file__).resolve().parents[1] / "docs" / "MEDIA-SMOKE-REPORT.json",
    )
    parser.add_argument("--duration", type=int, default=680)
    args = parser.parse_args()
    if args.duration < 610:
        parser.error("--duration must be at least 610 seconds to exercise three chunks")

    report = build_report(args.duration)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
