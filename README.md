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

- Windows
- .NET SDK 9.0+
- VTube Studio running locally
- VTube Studio Plugin API enabled

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

At first run:

1. App connects to `ws://127.0.0.1:8001`
2. VTube Studio shows permission prompt
3. Allow access
4. Token is cached at:

```text
%LocalAppData%\TamKungZ_\Music2DBridge\vts-token.txt
```

## Publish (Single File EXE)

```bash
dotnet publish src/Music2DBridge.App/Music2DBridge.App.csproj -c Release
```

Output (default):

```text
src/Music2DBridge.App/bin/Release/net9.0/win-x64/publish/
```

## Current Parameter Mapping

The app injects these parameter IDs:

- `ParamInstEnergy`
- `ParamInstPitch`
- `ParamInstNoteClass`
- `ParamInstInKey`
- `ParamInstChordRoot`
- `ParamInstChordType`
- `ParamInstKeyRoot`
- `ParamInstKeyMode`

Ensure your Live2D model / VTube Studio setup uses matching parameter IDs.

## Fixed Key Filter (Optional)

You can lock detection to a key/scale. Notes outside the configured key are ignored by note/chord/key history.

- CLI argument: `--fixed-key=<key>`
- Environment variable: `M2D_FIXED_KEY=<key>`

Examples:

- `--fixed-key=Cmaj`
- `--fixed-key=Amin`
- `M2D_FIXED_KEY=F# minor`

Accepted key formats include major/minor suffixes (`maj`, `major`, `min`, `minor`) and sharps/flats (`C#`, `Bb`, etc.).

## License

This project uses a custom source-available commercial license.
See `LICENSE` for full terms.

Commercial licensing contact:

- dev@tamkungz.me
- kittiwut.pimpromma@gmail.com
