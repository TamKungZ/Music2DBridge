# Music2DBridge VTS

Music2DBridge is an independent third-party C# bridge for VTube Studio.
It captures microphone audio, estimates musical information (note/chord/key), and injects mapped values into VTube Studio parameters through the WebSocket API.

## Important

- This project is **not** affiliated with, endorsed by, sponsored by, or officially associated with VTube Studio or DenchiSoft.
- Music2DBridge is an **external app/plugin bridge**. It is not a Unity in-process plugin.
- Do **not** install it into `VTube Studio_Data/Plugins/x86_64` as a primary setup method.

## Project Structure

```text
src/
  Music2DBridge.Core/         # Audio analysis and musical state logic
  Music2DBridge.VTubeStudio/  # VTube Studio WebSocket client/auth/inject
  Music2DBridge.App/          # Executable host app (mic capture + bridge loop)
LICENSE
```

## Requirements

- Windows / macOS / Linux
- .NET SDK 9.0+
- VTube Studio running locally
- VTube Studio Plugin API enabled
- OpenAL runtime for microphone capture (for example `libopenal1` on Linux)

In VTube Studio:

1. Open settings.
2. Enable Plugin API access.
3. Keep API port at default `8001` (or update app code if changed).

## Build

From repo root:

```bash
dotnet build Music2DBridge.sln
```

## Run (Development)

```bash
dotnet run --project src/Music2DBridge.App/Music2DBridge.App.csproj
```

Default launch mode is desktop UI (Avalonia).

To run in terminal/CLI mode from cmd/PowerShell:

```bash
dotnet run --project src/Music2DBridge.App/Music2DBridge.App.csproj -- --cli
```

At first run:

1. App connects to `ws://127.0.0.1:8001`
2. VTube Studio shows permission prompt
3. Allow access
4. Token is cached at:

```text
%LocalAppData%\TamKungZ_\Music2DBridge\vts-token.txt
```

## Publish (Single File)

Windows:

```bash
dotnet publish src/Music2DBridge.App/Music2DBridge.App.csproj -c Release -r win-x64
```

macOS (Intel):

```bash
dotnet publish src/Music2DBridge.App/Music2DBridge.App.csproj -c Release -r osx-x64
```

macOS (Apple Silicon):

```bash
dotnet publish src/Music2DBridge.App/Music2DBridge.App.csproj -c Release -r osx-arm64
```

Output (default):

```text
src/Music2DBridge.App/bin/Release/net9.0/win-x64/publish/
```

## Current Parameter Mapping

The app always injects these parameter IDs:

- `ParamInstEnergy`
- `ParamInstPitch`
- `ParamInstInKey`
- `ParamInstChordRoot`
- `ParamInstChordType`
- `ParamInstKeyRoot`
- `ParamInstKeyMode`

Ensure your Live2D model / VTube Studio setup uses matching parameter IDs.

### Note Output Modes

Use `--note-mode=<mode>` or `M2D_NOTE_MODE=<mode>` to choose note output behavior.

- `class` (default): injects `ParamInstNoteClass` directly as note class value `0..11`
- `per-note`: injects 12 note parameters in range `0..1` for deep rig/chord alignment

In `per-note` mode, the app injects these 12 parameters:

- `ParamInstNoteC`
- `ParamInstNoteCs`
- `ParamInstNoteD`
- `ParamInstNoteDs`
- `ParamInstNoteE`
- `ParamInstNoteF`
- `ParamInstNoteFs`
- `ParamInstNoteG`
- `ParamInstNoteGs`
- `ParamInstNoteA`
- `ParamInstNoteAs`
- `ParamInstNoteB`

Example:

- `--note-mode=per-note`

This uses fixed parameter names `ParamInstNoteC` ... `ParamInstNoteB`.

## Fixed Key Filter (Optional)

You can lock detection to a key/scale. Notes outside the configured key are ignored by note/chord/key history.

- CLI argument: `--fixed-key=<key>`
- Environment variable: `M2D_FIXED_KEY=<key>`

Examples:

- `--fixed-key=Cmaj`
- `--fixed-key=Amin`
- `M2D_FIXED_KEY=F# minor`

Accepted key formats include major/minor suffixes (`maj`, `major`, `min`, `minor`) and sharps/flats (`C#`, `Bb`, etc.).

## Audio Input Device

- GUI: choose `Input` from the top control bar.
- CLI argument: `--input-device=<device-id>`
- Environment variable: `M2D_INPUT_DEVICE=<device-id>`
- Default behavior: `System Default` (uses the computer default capture device)

On Windows, capture uses WASAPI shared mode first, so one microphone/interface can be used by multiple applications at the same time.
This also works well when you are running guitar software in an ASIO host and need Music2DBridge to share the same input path.

## License

This project uses a custom source-available commercial license.
See `LICENSE` for full terms.

Commercial licensing contact:

- dev@tamkungz.me
- kittiwut.pimpromma@gmail.com
