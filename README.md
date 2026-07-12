# TranslateUs — Real-time Chat Translation for Among Us

Type in your language, they read in theirs. Works out of the box with free Google Translate, or bring your own AI key for better slang translation.

## Features

- **Zero-config mode** — No API key? No problem. Uses free Google Translate automatically.
- **AI-powered mode** — Bring your own key (OpenAI, Zhipu, DeepSeek) for smarter slang/terminology translation
- **Auto-translate on send** — Type anything, it sends in the lobby's language
- **Auto-translate on receive** — Incoming messages instantly translated to your language
- **Batch translation** — Opening chat translates all pending messages in one go
- **Right-click toggle** — Right-click any chat bubble to flip between original and translated
- **Smart fallback** — If AI fails, automatically falls back to Google Translate
- **Configurable** — Full control over model, API endpoint, and extra prompt instructions

## Quick Start

### Zero-config (Google Translate)

1. Drop `TranslateUs.dll` into `BepInEx/plugins/`
2. Done. Messages translate automatically via Google Translate.

### With AI (better quality)

1. Drop `TranslateUs.dll` into `BepInEx/plugins/`
2. Launch Among Us once to generate the config
3. Edit `BepInEx/config/ume.transalte.us.cfg`:

```ini
[AI]
ApiKey = your-api-key
ApiUrl = https://open.bigmodel.cn/api/paas/v4/chat/completions
Model = glm-4-flash
ExtraPrompt = 
```

4. Done. AI handles Among Us slang (vent, sus, etc.) better than Google.

## Configuration

| Key | Default | Description |
|-----|---------|-------------|
| `ApiKey` | `0` | Your API key. Leave as `0` to use free Google Translate. |
| `ApiUrl` | Zhipu GLM endpoint | Any OpenAI-compatible endpoint |
| `Model` | `glm-4-flash` | Model name (gpt-4o, claude-3-haiku, deepseek-chat, etc.) |
| `ExtraPrompt` | *(empty)* | Extra instructions for the AI (e.g. "Keep player names untranslated") |
| `UseGoogleFallback` | `true` | Set to `false` to disable Google Translate entirely (if Google is blocked in your region) |

## Requirements

- [BepInEx](https://github.com/BepInEx/BepInEx) for Among Us (IL2CPP)
- (Optional) An API key from [Zhipu AI](https://open.bigmodel.cn/) (free tier available), OpenAI, or any compatible provider for AI-powered translation
