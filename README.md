# TranslateUs — Real-time Chat Translation for Among Us

[English](README.md) · [简体中文](README.zh-CN.md)

---

Type in your language, they read in theirs. Configure a cloud AI provider, or run a local AI model on your own machine or phone — no internet, no API key, total privacy.

## Features

- **Local AI** — Run a GGUF model (e.g. Qwen2.5-1.5B) fully offline via llama.cpp. Works on Windows **and Android**. Zero network, total privacy.
- **Prompt cache warmup** — After joining a lobby, the system prompt and terminology guide are decoded once and cached. Messages remain untranslated during the one-time warmup (~15–30s).
- **Prompt-injection protection** — Chat text can't hijack the model ("forget your rules", "who are you", …); it only translates.
- **Cloud AI** — Bring your own key (OpenAI, Zhipu, DeepSeek, any OpenAI-compatible endpoint). Windows only.
- **Auto-translate on send** — Type in your language, it sends in the lobby's language.
- **Auto-translate on receive** — Incoming messages instantly translated to your language.
- **Batch translation** — Opening chat translates all pending messages in one request.
- **Right-click toggle** — Right-click any chat bubble to flip between original and translated.
- **Pause button** — In-chat toggle to pause/resume translation anytime.
- **Privacy first** — If no backend is available, messages remain untranslated; message text is not sent to another service.
- **Job Object (Windows)** — llama-server auto-terminates when the game exits (no orphan processes).

## Platform support

| Backend | Windows | Android |
|---------|:-------:|:-------:|
| Local AI (llama.cpp) | ✅ | ✅ |
| Cloud AI | ✅ | ❌ |

## Quick Start

### Cloud AI (Windows, better slang quality)

1. Drop `TranslateUs.dll` into `BepInEx/plugins/`
2. Launch Among Us once to generate the config
3. Edit `BepInEx/config/ume.transalte.us.cfg`:

```ini
[AI]
Backend = CloudAI

[Cloud AI]
ApiKey = your-api-key
ApiUrl = https://open.bigmodel.cn/api/paas/v4/chat/completions
Model = glm-4-flash
```

### Local AI (privacy-first, no internet — Windows & Android)

1. Drop `TranslateUs.dll` into `BepInEx/plugins/`
2. Edit `BepInEx/config/ume.transalte.us.cfg`:

```ini
[AI]
Backend = LocalAI
```

3. Launch. The llama.cpp runtime downloads automatically, then the model (~1 GB) downloads once with a progress bar on the splash screen.

> **Android note:** On Android the native runtime (`libllama.so` + GGML CPU backend) is downloaded and loaded in-process — no `llama-server` subprocess, so it works straight from internal storage.

## Configuration

### [AI]

| Key | Default | Description |
|-----|---------|-------------|
| `Backend` | `Auto` | `Auto` / `CloudAI` / `LocalAI`. No translation if no backend is configured. |

### [Cloud AI]

| Key | Default | Description |
|-----|---------|-------------|
| `ApiKey` | *(empty)* | Your API key |
| `ApiUrl` | Zhipu GLM endpoint | Any OpenAI-compatible endpoint |
| `Model` | `glm-4-flash` | Model name |
| `ExtraPrompt` | *(empty)* | Extra instructions appended to the translation prompt |

### [LocalAI]

| Key | Default | Description |
|-----|---------|-------------|
| `LocalModelPath` | *(empty)* | Path to a `.gguf` file. Leave empty to auto-download Qwen 2.5 1.5B. |
| `LocalModelUrl` | HuggingFace URL | Download URL for the GGUF model |
| `NativeLibUrl` | GitHub releases | Base URL for llama.cpp runtime downloads |
| `InferenceThreads` | `2` | CPU threads for inference (keep low to avoid frame drops) |
| `ContextSize` | `4096` | KV-cache context size in tokens |
| `MaxInferenceTokens` | `128` | Max tokens per translation |
| `InferenceTemperature` | `0.1` | Temperature (0.0–2.0). Lower = more deterministic |

### [Translation]

| Key | Default | Description |
|-----|---------|-------------|
| `RoomLanguage` | `Auto` | Override the lobby's spoken language. `Auto` uses the host's Keywords. |
| `TranslateOwnBubbles` | `false` | If `true`, your own bubbles show translated text instead of your original. |

## Requirements

- [BepInEx](https://github.com/BepInEx/BepInEx) for Among Us (IL2CPP)
- **Windows**: 64-bit. ~1 GB free disk space for the local model.
- **Android**: LocalAI only. CloudAI is unavailable. LocalAI needs ~1 GB free space for the model plus the llama.cpp runtime.
- (Optional) An API key from [Zhipu AI](https://open.bigmodel.cn/) (free tier available), OpenAI, or any compatible provider — Windows only.

# Privacy Policy — TranslateUs

**TranslateUs** is a BepInEx plugin for Among Us that provides real-time in-game chat translation. This document explains what data is processed, where it goes, and how user privacy is protected.

*Last updated: 2026-07-22*

---

## 1. Data We DO NOT Collect

TranslateUs does **not** collect, store, log, or transmit any of the following:

- Player identities or usernames
- Account credentials or API keys (your API key is stored **only** in your local `BepInEx/config/ume.transalte.us.cfg` file)
- Chat history or message logs
- Game session data
- Device identifiers or hardware information
- Any analytics or telemetry

The plugin runs entirely within your local game client. There is **no "TranslateUs server"** — we have no backend infrastructure whatsoever.

---

## 2. Terminology Guide — 100% Offline

TranslateUs includes a **pre-baked terminology guide** (Among Us slang, role names, map locations, and community expressions for 16 languages) embedded as static XML files within the plugin.

- **Stored locally** — bundled inside the plugin DLL as embedded resources
- **Never updated at runtime** — no network requests are made for terminology data
- **No AI involved** — the glossary is hand-curated offline, not generated by any AI service

**No chat messages or game data are ever sent to any server for terminology lookup or glossary building.**

---

## 3. Translation Services — Data in Transit

Chat messages can be translated with a local model or, on Windows, with a cloud AI provider you configure.

### 3.1 AI Translation Mode

If you **choose** to configure an API key (OpenAI, Zhipu AI, DeepSeek, or any OpenAI-compatible provider), TranslateUs will send chat messages to **your configured API endpoint**.

| Aspect | Detail |
|--------|--------|
| **What is sent** | Only the chat message text and target language. The system prompt includes the pre-baked terminology guide (stored offline — see Section 2). |
| **Where it goes** | The API URL **you** configured in `BepInEx/config/ume.transalte.us.cfg` |
| **Governing policy** | The privacy policy of the AI provider **you** chose (e.g., [OpenAI](https://openai.com/policies/privacy-policy), [Zhipu AI](https://open.bigmodel.cn/privacy)) |
| **Your control** | You choose the provider, the model, and whether to enable this mode at all. Without a configured backend, messages remain untranslated. |
| **Android restriction** | Cloud AI is unavailable on Android; fully offline Local AI is supported. |

### 3.2 Local AI

When Local AI is enabled, messages are processed on-device with a GGUF model and are not sent to a translation service. The model and runtime are downloaded on first use.

### 3.3 Pause Control

Use the in-game pause button to stop translation at any time. When paused, **no text is sent to any translation service** — messages are shown in their original form only.

---

## 4. Data Flow Summary

| Scenario | Data Sent | Destination | When |
|----------|-----------|-------------|------|
| Terminology lookup | **Nothing** (offline XML) | Nowhere | Always |
| Translation (AI mode) | Chat message text | Your configured API endpoint | Per message, if not paused |
| Translation (paused) | **Nothing** | Nowhere | While paused |
| Translation (Local AI) | Chat message text | This device only | When using a local model |
| No backend configured | **Nothing** | Nowhere | Messages remain untranslated |

---

## 5. Your Choices

| You can... | How |
|------------|-----|
| Use your own AI provider | Set `ApiKey` and `ApiUrl` in config — you control which provider processes your data |
| Pause translation entirely | Click the in-game pause button |
| Inspect the source code | All translation logic is in `Translator.cs`, `SlangCache.cs`, and `TerminologyLoader.cs` |

---

## 6. Changes From Previous Versions

Starting from v1.2.2-pre:

- **Removed**: Runtime AI-powered terminology/glossary generation. The plugin no longer makes any AI API calls for building glossaries.
- **Replaced with**: Pre-baked static XML terminology files (16 languages) bundled within the plugin — zero network dependency, zero runtime AI calls for glossary.
- **Note**: Android supports Local AI only; Cloud AI is unavailable.

---

## 7. Contact

If you have questions about this privacy policy or the plugin's data handling, please open an issue on the project's repository.
