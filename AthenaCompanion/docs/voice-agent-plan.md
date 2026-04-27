# Athena Voice Agent Plan

Status: first local implementation in progress. Athena has pause-only voice mode, local key setup, microphone streaming, and response playback. Further tuning is still expected after live testing.

## Product Rules

- Athena ships with no OpenAI API key.
- First launch or first voice use asks the user for their own OpenAI API key.
- Store the key in Windows Credential Manager, not in plain text files.
- Never log, echo, commit, or include the key in crash reports.
- Local development may fall back to the `OPENAI_API_KEY` environment variable.
- Voice is optional. If no key is configured, Athena still walks and poses normally.

## Voice Activation Model

Athena only listens while she is paused.

- Walking mode:
  - No microphone capture.
  - No Realtime session.
  - No background listening.
- Pause mode:
  - Left-click or tray pause toggles Athena into pause pose.
  - If voice is enabled and a valid key exists, connect the Realtime session.
  - Start microphone capture only after the session is ready.
  - Play Athena's spoken responses through the default audio output.
- Resume walking:
  - Stop microphone capture immediately.
  - Cancel or close any active response.
  - Close the Realtime session.

This keeps the privacy boundary simple: pause means "Athena may listen"; walking means "Athena is not listening."

## OpenAI API Direction

Initial implementation should use the OpenAI Realtime API over WebSocket from the WPF app.

- Model: `gpt-realtime-1.5`
- Endpoint shape: `wss://api.openai.com/v1/realtime?model=gpt-realtime-1.5`
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
- If the user mixes Bahasa Indonesia and English, mirror that mix naturally.
- If the input language is unclear, ask a brief clarification.

# Boundaries
- Do not claim to access files, apps, or system controls unless a tool exists.
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
  Security/
    IOpenAiKeyStore.cs
    WindowsCredentialOpenAiKeyStore.cs
    EnvironmentOpenAiKeyProvider.cs
  Settings/
    ApiKeySetupWindow.xaml
    ApiKeySetupWindow.xaml.cs
```

Responsibilities:

- `AthenaVoiceController`: bridges app state to voice state; starts voice on pause and stops voice on resume.
- `AthenaRealtimeSession`: owns WebSocket connection, Realtime session setup, event send/receive, reconnect policy, and response cancellation.
- `AthenaAudioInput`: captures microphone audio and streams it to the session.
- `AthenaAudioOutput`: buffers and plays model audio responses.
- `WindowsCredentialOpenAiKeyStore`: reads, writes, and deletes the user's API key from Windows Credential Manager.
- `ApiKeySetupWindow`: collects and validates the user's key without persisting it until validation succeeds.

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

6. **Conversation behavior** - next
   - Tune turn detection and interruption handling.
   - Add Bahasa Indonesia and mixed-language test phrases.
   - Add short persona samples for greeting, unclear audio, and goodbye.

7. **Distribution hardening** - later
   - Add explicit setup copy explaining that users pay OpenAI directly for their own key usage.
   - Add remove-key flow.
   - Add "voice disabled" fallback when no key or microphone permission exists.
   - Add a privacy note: Athena only captures microphone audio while paused.

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
- Network failure shows a non-blocking status and does not crash the app.

## Later Ideas

- Add a small speech bubble for transcribed text.
- Add separate listening and speaking pose frames.
- Add wake phrase only after explicit user opt-in.
- Add a local session transcript viewer with an off switch.
- Add a hosted token service only if Athena ever needs developer-managed billing or shared service limits.
