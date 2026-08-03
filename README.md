# TranslateUs — Real-time Chat Translation for Among Us

Type in your language, they read in theirs. Works out of the box with free Google Translate, or bring your own AI key for better slang translation. Now also supports running a local AI model on your own machine — no internet, no API key, total privacy.

## Features

- **Local AI (NEW)** — Run a GGUF model on your own machine via llama.cpp. Zero network, total privacy.
- **Cloud AI** — Bring your own key (OpenAI, Zhipu, DeepSeek, any OpenAI-compatible endpoint).
- **Google Translate** — Zero-config free fallback. Works without any API key.
- **Auto-translate on send** — Type in your language, it sends in the lobby's language.
- **Auto-translate on receive** — Incoming messages instantly translated to your language.
- **Batch translation** — Opening chat translates all pending messages in one request.
- **Right-click toggle** — Right-click any chat bubble to flip between original and translated.
- **Pause button** — In-chat toggle to pause/resume translation anytime.
- **Smart fallback** — AI fails → Google Translate. LocalAI warming up → Google Translate. Never miss a message.
- **Job Object** — llama-server auto-terminates when the game exits (no orphan processes).
- **Android** — Google Translate and CloudAI work. **LocalAI is not available on Android yet** (see note below).

## Quick Start

### Google Translate (zero config)

1. Drop `TranslateUs.dll` into `BepInEx/plugins/`
2. Done. Messages translate automatically.

### Cloud AI (better slang quality)

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

### Local AI (Windows only, privacy-first, no internet)

1. Drop `TranslateUs.dll` into `BepInEx/plugins/`
2. Edit `BepInEx/config/ume.transalte.us.cfg`:

```ini
[AI]
Backend = LocalAI
```

4. Launch. The model (~1 GB) downloads once with a progress bar on the splash screen.

## Configuration

### [AI]

| Key | Default | Description |
|-----|---------|-------------|
| `Backend` | `Auto` | `Auto` / `Google` / `CloudAI` / `LocalAI` |

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
- **Android**: Google Translate and CloudAI backends only. LocalAI is currently unavailable (see below).

> [!WARNING]
> **Android + LocalAI:** Android's linker security blocks native library loading from external storage. Since Among Us is often installed on SD card, LocalAI cannot load `libllama.so`. A proper fix requires an .aar-based preloader. Google Translate and CloudAI backends are unaffected.
- (Optional) An API key from [Zhipu AI](https://open.bigmodel.cn/) (free tier available), OpenAI, or any compatible provider.
