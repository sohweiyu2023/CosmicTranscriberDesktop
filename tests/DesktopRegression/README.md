# Desktop regression smoke

This Windows/.NET 10 console harness compiles and exercises the production desktop Models, Services and Utilities source without opening the UI. It uses a generated 650-second tone, never recordings or credentials. Every API client has its private HTTP client replaced by an in-memory handler before use; unexpected targets fail. No provider requests are sent. The native WPF application build is verified separately.

From the source root, generate a fixture with FFmpeg and pass a fresh output directory:

```powershell
ffmpeg -hide_banner -loglevel error -f lavfi -i 'sine=frequency=440:duration=650' -c:a libmp3lame -q:a 5 synthetic-long.mp3
dotnet run --project tests/DesktopRegression/DesktopRegression.csproj -c Release -- . synthetic-long.mp3 smoke-output
```

The checks cover invalid/zero/valid/overflowing price estimates, zero-output completion wording, named settings controls, long MP3 metadata and three-chunk preparation, empty and invalid files, cancellation, mocked successful transcription and status, checkpoint reuse, request metadata, TXT/Markdown exports, filename collisions, combined output, nonretryable authentication failure, transient retry, malformed JSON, and a valid no-speech response. The test fails if any assertion fails. It does not validate visible WPF layout, hardware input, API model availability or speech recognition accuracy.

For an offline run, first restore against an authorized local NuGet package feed and add `--no-restore`. To avoid .NET first-run certificate setup, set `DOTNET_GENERATE_ASPNET_CERTIFICATE=false` before invoking the SDK. Logs and output remain local. The repository's `static_verify.py` requires a clean source-only copy with regenerated manifests because it rejects build/cache residue by design.
