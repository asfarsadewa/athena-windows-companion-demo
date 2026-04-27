# Athena Companion

Transparent WPF desktop companion window for Windows. Athena walks just above the primary taskbar, pauses for pose animation, and exposes a tray menu for pause, text chat, click-through, voice setup, and exit.

Left-click Athena to toggle voice pause mode. Click the small `Chat` bubble to open text pause mode. Right-click Athena to open the tray menu.

## Voice Agent Plan

Voice support is implemented as a first local Realtime pass. Athena ships with no OpenAI API key; users provide their own key during setup, and the app stores it in Windows Credential Manager. Local development can continue to use the `OPENAI_API_KEY` environment variable.

Athena only listens while she is paused. Walking mode stops microphone capture and closes the Realtime session. See [docs/voice-agent-plan.md](docs/voice-agent-plan.md) for the implementation plan and privacy boundary.

Voice behavior:

- left-click Athena to pause and start voice mode
- left-click again to resume walking and stop voice mode
- right-click for the menu, including voice status, voice selection, and API key setup
- first voice use asks for an API key if neither Credential Manager nor `OPENAI_API_KEY` has one
- default voice is `alloy`; selected voice is saved under the user's AppData settings

Pause-only voice tools:

- screen questions such as "what's on my screen?" capture the primary display and ask `gpt-5.5` for a concise spoken answer
- image requests such as "generate an infographic of what I am seeing" capture the primary display, prepare an image brief with `gpt-5.5`, generate a PNG with `gpt-image-2`, and open it in Athena's lightbox
- generated screen images are saved under the user's `Pictures\Athena Companion` folder
- screen capture is only triggered by an explicit voice tool request while Athena is paused

## Text Chat

Text chat is a separate pause mode from voice:

- click the `Chat` bubble while Athena is walking to pause her and open the text chat window
- text mode uses `gpt-5.5` through the Responses API, not the Realtime voice WebSocket
- text mode has the same local tools as voice mode, including screen inspection and `gpt-image-2` image generation
- closing the text chat window resumes walking
- text mode does not start microphone capture

## Run

```powershell
dotnet run --project .\AthenaCompanion.csproj
```

## Release

Build the self-contained Windows installer locally from the repository root:

```powershell
.\scripts\build-release.ps1 -Version 0.1.0
```

The installer is written to:

```text
artifacts\installer\AthenaCompanionSetup-0.1.0.exe
```

GitHub Actions also builds the installer when a `v*` tag is pushed:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

The workflow uploads the installer artifact and attaches it to the GitHub release for the tag.

## App Icon

The generated Athena app icon lives at:

```text
Assets\Icons\athena.ico
```

The `gpt-image-2` source prompt is saved at:

```text
Assets\Icons\athena-icon.prompt.txt
```

## Sprite Atlas

The runtime looks for this generated atlas:

```text
Assets\Sprites\athena-atlas.png
```

Expected atlas format:

- 2048x768 PNG
- 8 columns by 3 rows
- 256x256 cells
- frames 1-15: right-facing walk cycle, curated from every other generated walk frame
- frames 16-24: idle/pose loop
- transparent background after chroma-key cleanup
- metadata: `Assets\Sprites\athena-atlas.json`

The exact `gpt-image-2` generation prompt is saved at:

```text
Assets\Sprites\athena-atlas.prompt.txt
```

Dry-run the image request:

```powershell
python 'C:\Users\asfar\.codex\skills\.system\imagegen\scripts\image_gen.py' generate `
  --prompt-file 'Assets\Sprites\athena-atlas.prompt.txt' `
  --model gpt-image-2 `
  --size 2048x1024 `
  --quality high `
  --out 'output\imagegen\athena-atlas-raw.png' `
  --no-augment `
  --dry-run
```

Generate the raw chroma-key source:

```powershell
python 'C:\Users\asfar\.codex\skills\.system\imagegen\scripts\image_gen.py' generate `
  --prompt-file 'Assets\Sprites\athena-atlas.prompt.txt' `
  --model gpt-image-2 `
  --size 2048x1024 `
  --quality high `
  --out 'output\imagegen\athena-atlas-raw.png' `
  --no-augment
```

Remove the chroma-key background:

```powershell
python 'C:\Users\asfar\.codex\skills\.system\imagegen\scripts\remove_chroma_key.py' `
  --input 'output\imagegen\athena-atlas-raw.png' `
  --out 'Assets\Sprites\athena-atlas.png' `
  --auto-key border `
  --soft-matte `
  --transparent-threshold 12 `
  --opaque-threshold 220 `
  --despill
```

Keep the transparent generated layout, then normalize it into fixed 256x256 cells:

```powershell
Copy-Item 'Assets\Sprites\athena-atlas.png' 'output\imagegen\athena-atlas-transparent-layout.png' -Force

python 'tools\normalize_athena_atlas.py' `
  --input 'output\imagegen\athena-atlas-transparent-layout.png' `
  --out 'Assets\Sprites\athena-atlas.png' `
  --preview 'output\imagegen\athena-atlas-normalized-preview.png' `
  --columns 8 `
  --rows 3 `
  --walk-frames 30 `
  --walk-stride 2 `
  --pose-frames 9
```
