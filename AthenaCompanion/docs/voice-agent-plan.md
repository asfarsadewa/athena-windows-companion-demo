# Athena Voice Agent Plan

Status: first local implementation in progress. Athena has separate pause modes for voice and text, local key setup, microphone streaming, response playback, screen inspection, and screen-based image generation. Further tuning is still expected after live testing.

## Product Rules

- Athena ships with no OpenAI API key.
- First launch or first voice use asks the user for their own OpenAI API key.
- Store the key in Windows Credential Manager, not in plain text files.
- Never log, echo, commit, or include the key in crash reports.
- Local development may fall back to the `OPENAI_API_KEY` environment variable.
- Voice is optional. If no key is configured, Athena still walks and poses normally.
- Screen capture is optional and tool-triggered. Athena should only capture the primary screen after the user explicitly asks about the screen or asks for an image based on the screen.
- Text chat is optional and separate from voice. Text mode must not start microphone capture.

## Interaction Modes

Athena has four high-level interaction modes:

- Walking:
  - Athena walks normally above the taskbar.
  - The `Chat` bubble is visible.
  - No microphone capture.
  - No text chat window.
- Voice pause:
  - Triggered by left-clicking Athena or choosing the tray pause item.
  - Athena pauses in pose animation.
  - Realtime voice session starts if an API key is available.
  - Microphone capture is active only while this mode is active.
- Text pause:
  - Triggered by clicking the `Chat` bubble or tray text chat item.
  - Athena pauses in pose animation.
  - A compact text chat window opens.
  - Uses `gpt-5.5` through the Responses API.
  - Does not start microphone capture.
- Music pause:
  - Triggered by the tray music item or the `open_music_player` tool.
  - Athena pauses in pose animation.
  - Any active voice session and microphone capture stop before playback.
  - A compact local player opens for `%USERPROFILE%\Music\Athena Companion`.
  - MP3 and M4A files are played through mandatory mono AM/SW radio processing.

The code keeps interaction mode separate from animation mode so a dedicated text-pause sprite clip can be added later without changing the app state model.

## Voice Activation Model

Athena only listens while she is paused.

- Walking mode:
  - No microphone capture.
  - No Realtime session.
  - No background listening.
- Pause mode:
  - Left-click or tray pause toggles Athena into pause pose.
  - If voice is enabled and a valid key exists, connect the Realtime session.
  - Use the selected Realtime voice from user settings.
  - Start microphone capture only after the session is ready.
  - Play Athena's spoken responses through the default audio output.
- Resume walking:
  - Stop microphone capture immediately.
  - Cancel or close any active response.
  - Close the Realtime session.
- Screen tools:
  - Available only inside pause-mode voice sessions.
  - `inspect_screen` captures the primary display and asks a vision-capable text model for a concise answer.
  - `create_screen_image` captures the primary display, prepares an image brief, generates a PNG, and opens it in a local lightbox.
  - Generated images are saved to the user's `Pictures\Athena Companion` folder.

This keeps the privacy boundary simple: pause means "Athena may listen"; walking means "Athena is not listening." Screen capture requires a separate explicit screen or image-generation request.

In text pause mode, "pause" means Athena is available for typed chat, not microphone listening.

## OpenAI API Direction

Initial implementation should use the OpenAI Realtime API over WebSocket from the WPF app.

- Model: `gpt-realtime-2`
- Endpoint shape: `wss://api.openai.com/v1/realtime?model=gpt-realtime-2`
- Reasoning effort: `low`
- Default voice: `alloy`
- Built-in voices exposed in the tray menu: `marin`, `cedar`, `coral`, `shimmer`, `verse`, `sage`, `alloy`, `ash`, `ballad`, `echo`
- Tool routing:
  - Realtime function tools: `inspect_screen`, `create_screen_image`
  - Screen understanding: `gpt-5.5` through the Responses API with a screenshot input
  - Screen image generation: `gpt-image-2` through the Image API
- Text mode:
  - Model: `gpt-5.5`
  - Endpoint: Responses API
  - Conversation state: `previous_response_id`
  - Tool calling: same local `inspect_screen`, `create_screen_image`, and `open_music_player` tools
- Music mode:
  - Tool: `open_music_player`
  - Directory: user Music folder under `Athena Companion`, with AppData fallback
  - Voice handoff: visual-only; no spoken follow-up over music
- Auth source:
  1. Windows Credential Manager
  2. `OPENAI_API_KEY` environment variable for local development
  3. Setup dialog if neither exists

For a distributed app where users bring their own API key, direct WebSocket auth is acceptable if the key stays local and is stored securely. If Athena later becomes a hosted service or uses the developer's key, add a small backend that issues ephemeral Realtime credentials instead.

## Language And Persona Prompt

Use a short, structured Realtime prompt. Keep it direct because realtime models follow bullet-point instructions more reliably than long prose.

```text
# Role
- You are Athena, a graceful Windows desktop companion.
- You are warm, playful, elegant, and concise.
- You are never explicit or crude.

# Conversation
- You are only active while the user has paused your walking mode.
- Keep most replies to 1-3 short sentences.
- If the user sounds unclear, noisy, or cut off, ask them to repeat.

# Language
- Match the user's language by default.
- If the user speaks Bahasa Indonesia, respond in natural Bahasa Indonesia.
- If the user speaks Chinese, respond in natural Chinese using the script and tone the user used.
- If the user mixes Bahasa Indonesia and English, mirror that mix naturally.
- If the user mixes Chinese with English or Bahasa Indonesia, mirror that mix naturally.
- If the input language is unclear, ask a brief clarification.

# Reasoning
- For direct answers, simple chat, and short confirmations, respond quickly.
- For multi-step requests, screen inspection, image generation, or tool choice, reason before acting.
- Do not reason through unclear audio; ask a brief clarification instead.

# Preambles
- Use a short spoken preamble only before work that may take a moment, such as inspecting the screen or creating an image.
- Skip preambles for direct answers, unclear audio, and lightweight music commands.

# Boundaries
- Do not claim to access files, apps, or system controls unless a tool exists.
- Use screen tools only when the user explicitly asks about what is visible on screen or asks you to create an image from the screen.
- When a screen image is generated, tell the user it opened in a lightbox and keep the spoken response brief.
- Use the music tool when the user asks to play, browse, or open local music. Music mode stops voice immediately, so do not plan a spoken follow-up over music.
- For medical, legal, financial, or safety-sensitive topics, give cautious general guidance and recommend a qualified professional where appropriate.
```

## Proposed Code Structure

```text
AthenaCompanion/
  Voice/
    AthenaVoiceController.cs
    AthenaRealtimeSession.cs
    AthenaAudioInput.cs
    AthenaAudioOutput.cs
    AthenaVoicePrompt.cs
  TextChat/
    AthenaTextChatSession.cs
    AthenaTextPrompt.cs
  Security/
    IOpenAiKeyStore.cs
    WindowsCredentialOpenAiKeyStore.cs
    EnvironmentOpenAiKeyProvider.cs
  Settings/
    ApiKeySetupWindow.xaml
    ApiKeySetupWindow.xaml.cs
  Tools/
    AthenaToolExecutor.cs
    OpenAiToolClient.cs
    ScreenCaptureService.cs
  UI/
    ImageLightboxWindow.xaml
    ImageLightboxWindow.xaml.cs
```

Responsibilities:

- `AthenaVoiceController`: bridges app state to voice state; starts voice on pause and stops voice on resume.
- `AthenaRealtimeSession`: owns WebSocket connection, Realtime session setup, event send/receive, reconnect policy, and response cancellation.
- `AthenaAudioInput`: captures microphone audio and streams it to the session.
- `AthenaAudioOutput`: buffers and plays model audio responses.
- `WindowsCredentialOpenAiKeyStore`: reads, writes, and deletes the user's API key from Windows Credential Manager.
- `ApiKeySetupWindow`: collects and validates the user's key without persisting it until validation succeeds.
- `AthenaSettings`: stores non-secret user preferences such as selected voice under AppData.
- `AthenaToolExecutor`: executes local screen tools requested by voice or text mode.
- `OpenAiToolClient`: calls Responses for screen inspection and the Image API for generated screen images.
- `ScreenCaptureService`: captures the primary display as PNG bytes.
- `ImageLightboxWindow`: displays generated images and offers copy/open-folder actions.
- `AthenaTextChatSession`: owns the text-mode Responses loop, function-call execution, and previous response state.
- `TextChatWindow`: hosts the compact always-on-top typed chat surface.

## Implementation Phases

1. **Credential and settings foundation** - done
   - Add key lookup order: Credential Manager, then `OPENAI_API_KEY`, then setup dialog.
   - Add tray menu item: `OpenAI API Key...`.
   - Add validation result states: missing, valid, invalid, network error.

2. **Pause-state integration** - done
   - Convert pause/resume into explicit events: `PauseEntered`, `PauseExited`.
   - Start voice only on `PauseEntered`.
   - Stop voice immediately on `PauseExited`.
   - Keep visual pose animation running while voice is active.

3. **Realtime session foundation** - done
   - Connect over WebSocket.
   - Send a session update with Athena's prompt.
   - Log only event types and non-sensitive diagnostics.

4. **Microphone capture** - done
   - Add an audio library such as NAudio.
   - Capture mono PCM audio from the default microphone.
   - Stream audio chunks only while paused.
   - Stop capture on resume, exit, or click-through toggle if needed.

5. **Audio playback** - done
   - Decode response audio deltas.
   - Play through default output.
   - Cancel playback if the user resumes walking.
   - Later: add a speaking animation overlay or pose variant.

6. **Screen tool routing** - done
   - Add Realtime function declarations for screen inspection and screen image creation.
   - Execute local screen capture only when a function call arrives.
   - Send function results back to Realtime and ask Athena to speak the result.
   - Open generated images in a WPF lightbox.

7. **Text chat mode** - done
   - Add a clickable `Chat` bubble in walking mode.
   - Add a separate text pause interaction mode.
   - Use the Responses API with `gpt-5.5`.
   - Reuse the same local screen and image tools.
   - Resume walking when the text chat window closes.

8. **Conversation behavior** - next
   - Tune turn detection and interruption handling.
   - Add Bahasa Indonesia and mixed-language test phrases.
   - Add short persona samples for greeting, unclear audio, and goodbye.

9. **Distribution hardening** - later
   - Add explicit setup copy explaining that users pay OpenAI directly for their own key usage.
   - Add remove-key flow.
   - Add "voice disabled" fallback when no key, microphone permission, or screen-capture permission exists.
   - Add a privacy note: Athena only captures microphone audio while paused, and only captures screen pixels after explicit screen-related requests.

## Test Plan

- Build succeeds with no key configured.
- First voice use prompts for a key.
- Invalid key does not start microphone capture.
- Walking mode never opens the microphone.
- Pause mode starts voice and keeps Athena in pose animation.
- Clicking Athena again resumes walking and stops voice.
- Right-click menu still opens while not click-through.
- Click-through mode prevents left-click pause toggling.
- Bahasa Indonesia input receives Bahasa Indonesia output.
- Mixed Indonesian-English input can be mirrored naturally.
- Chinese input receives natural Chinese output.
- Mixed Chinese-English or Chinese-Indonesian input can be mirrored naturally.
- Network failure shows a non-blocking status and does not crash the app.
- "What's on my screen?" triggers `inspect_screen` and receives a spoken answer.
- "Generate an infographic of my screen" triggers `create_screen_image`, opens the lightbox, and saves a PNG under `Pictures\Athena Companion`.
- Clicking the `Chat` bubble opens text mode without starting microphone capture.
- Text mode can answer normal typed chat through `gpt-5.5`.
- Text mode can use `inspect_screen` and `create_screen_image` when the typed request explicitly asks for screen/image work.

## Later Ideas

- Add a small speech bubble for transcribed text.
- Add separate listening and speaking pose frames.
- Add wake phrase only after explicit user opt-in.
- Add a local session transcript viewer with an off switch.
- Add a hosted token service only if Athena ever needs developer-managed billing or shared service limits.
