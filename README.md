# TranslateUs — Real-time Chat Translation for Among Us

[English](README.md) · [简体中文](README.zh-CN.md)

---

Type in your language, they read in theirs. Works out of the box with free Google Translate, or bring your own AI key for better slang translation. You can also run a local AI model on your own machine or phone — no internet, no API key, total privacy.

## Features

- **Local AI** — Run a GGUF model (e.g. Qwen2.5-1.5B) fully offline via llama.cpp. Works on Windows **and Android**. Zero network, total privacy.
- **Prompt cache warmup** — After joining a lobby, the system prompt and terminology guide are decoded once and cached, so every following translation is instant (~1s) instead of ~30s. During the one-time warmup (~15–30s), messages fall back to Google Translate with a one-time chat notice.
- **Prompt-injection protection** — Chat text can't hijack the model ("forget your rules", "who are you", …); it only translates.
- **Cloud AI** — Bring your own key (OpenAI, Zhipu, DeepSeek, any OpenAI-compatible endpoint). Windows only.
- **Google Translate** — Zero-config free fallback. Works without any API key.
- **Auto-translate on send** — Type in your language, it sends in the lobby's language.
- **Auto-translate on receive** — Incoming messages instantly translated to your language.
- **Batch translation** — Opening chat translates all pending messages in one request.
- **Right-click toggle** — Right-click any chat bubble to flip between original and translated.
- **Pause button** — In-chat toggle to pause/resume translation anytime.
- **Smart fallback** — AI fails → Google Translate. LocalAI warming up → Google Translate. Never miss a message.
- **Job Object (Windows)** — llama-server auto-terminates when the game exits (no orphan processes).

## Platform support

| Backend | Windows | Android |
|---------|:-------:|:-------:|
| Google Translate | ✅ | ✅ |
| Local AI (llama.cpp) | ✅ | ✅ |
| Cloud AI | ✅ | ❌ (disabled for privacy) |

## Quick Start

### Google Translate (zero config)

1. Drop `TranslateUs.dll` into `BepInEx/plugins/`
2. Done. Messages translate automatically.

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
| `Backend` | `Auto` | `Auto` / `Google` / `CloudAI` / `LocalAI` (`CloudAI` is Windows-only) |

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
- **Android**: Google Translate and LocalAI backends. CloudAI is disabled (falls back to Google). LocalAI needs ~1 GB free space for the model plus the llama.cpp runtime.
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

Translation of chat messages requires sending the **text to be translated** to a translation service. TranslateUs supports two modes, both under your control.

### 3.1 Google Translate Mode (Default — No API Key Required)

When no API key is configured, TranslateUs uses the free Google Translate API.

| Aspect | Detail |
|--------|--------|
| **What is sent** | Only the chat message text to be translated. No user identity, no metadata. |
| **Where it goes** | `https://translate.googleapis.com` — Google's public translation endpoint |
| **Governing policy** | [Google Privacy Policy](https://policies.google.com/privacy) |
| **Data retention** | Governed by Google. TranslateUs does not cache or retain any translated content. |

### 3.2 AI Translation Mode (Optional — Requires Your API Key)

If you **choose** to configure an API key (OpenAI, Zhipu AI, DeepSeek, or any OpenAI-compatible provider), TranslateUs will send chat messages to **your configured API endpoint**.

| Aspect | Detail |
|--------|--------|
| **What is sent** | Only the chat message text and target language. The system prompt includes the pre-baked terminology guide (stored offline — see Section 2). |
| **Where it goes** | The API URL **you** configured in `BepInEx/config/ume.transalte.us.cfg` |
| **Governing policy** | The privacy policy of the AI provider **you** chose (e.g., [OpenAI](https://openai.com/policies/privacy-policy), [Zhipu AI](https://open.bigmodel.cn/privacy)) |
| **Your control** | You choose the provider, the model, and whether to enable this mode at all. Delete the API key at any time to revert to Google Translate. |
| **Android restriction** | AI translation is **automatically disabled** on Android. Only Google Translate is available on Android devices. |

### 3.3 Pause Control

Use the in-game pause button to stop translation at any time. When paused, **no text is sent to any translation service** — messages are shown in their original form only.

---

## 4. Data Flow Summary

| Scenario | Data Sent | Destination | When |
|----------|-----------|-------------|------|
| Terminology lookup | **Nothing** (offline XML) | Nowhere | Always |
| Translation (Google mode) | Chat message text | `translate.googleapis.com` | Per message, if not paused |
| Translation (AI mode) | Chat message text | Your configured API endpoint | Per message, if not paused |
| Translation (paused) | **Nothing** | Nowhere | While paused |
| Translation (Android) | Chat message text | `translate.googleapis.com` only | AI mode disabled on Android |

---

## 5. Your Choices

| You can... | How |
|------------|-----|
| Use Google Translate exclusively | Leave `ApiKey` empty in config — zero third-party AI involvement |
| Use your own AI provider | Set `ApiKey` and `ApiUrl` in config — you control which provider processes your data |
| Pause translation entirely | Click the in-game pause button |
| Inspect the source code | All translation logic is in `Translator.cs`, `SlangCache.cs`, and `TerminologyLoader.cs` |

---

## 6. Changes From Previous Versions

Starting from v1.2.2-pre:

- **Removed**: Runtime AI-powered terminology/glossary generation. The plugin no longer makes any AI API calls for building glossaries.
- **Replaced with**: Pre-baked static XML terminology files (16 languages) bundled within the plugin — zero network dependency, zero runtime AI calls for glossary.
- **Added**: Automatic AI translation disable on Android — only Google Translate is available on Android devices.

---

## 7. Contact

If you have questions about this privacy policy or the plugin's data handling, please open an issue on the project's repository.
