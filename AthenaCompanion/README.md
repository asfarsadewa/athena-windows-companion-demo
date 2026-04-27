# Athena Companion

Transparent WPF desktop companion window for Windows. Athena walks just above the primary taskbar, pauses for pose animation, and exposes a tray menu for pause, click-through, and exit.

## Run

```powershell
dotnet run --project .\AthenaCompanion.csproj
```

## Sprite Atlas

The runtime looks for this generated atlas:

```text
Assets\Sprites\athena-atlas.png
```

Expected atlas format:

- 2560x1024 PNG
- 10 columns by 4 rows
- 256x256 cells
- frames 1-30: right-facing walk cycle
- frames 31-39: idle/pose loop
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
  --columns 10 `
  --rows 4 `
  --walk-frames 30 `
  --pose-frames 9
```
