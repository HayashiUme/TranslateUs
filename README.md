# TranslateUs — Real-time AI Chat Translation for Among Us

Type in your language, they read in theirs. Seamless AI-powered chat translation.

## Features

- **Auto-translate on send** — Type anything, it sends in the lobby's language
- **Auto-translate on receive** — Incoming messages instantly translated to your language
- **Batch translation** — Opening chat translates all pending messages in a single API call
- **Right-click toggle** — Right-click any chat bubble to flip between original and translated
- **Bring your own key** — Works with Zhipu, OpenAI, DeepSeek, or any OpenAI-compatible API
- **Configurable** — Full control over model, API endpoint, and extra prompt instructions

## Quick Start

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

4. Done. Messages translate automatically.

## Configuration

| Key | Default | Description |
|-----|---------|-------------|
| `ApiKey` | `0` | Your API key |
| `ApiUrl` | Zhipu GLM endpoint | Any OpenAI-compatible endpoint |
| `Model` | `glm-4-flash` | Model name (gpt-4o, claude-3-haiku, deepseek-chat, etc.) |
| `ExtraPrompt` | *(empty)* | Extra instructions for the AI (e.g. "Keep player names untranslated") |

## Requirements

- [BepInEx](https://github.com/BepInEx/BepInEx) for Among Us (IL2CPP)
- An API key from [Zhipu AI](https://open.bigmodel.cn/) (free tier available), OpenAI, or any compatible provider

## How It Works

TranslateUs hooks into Among Us' chat pipeline at three points:

- **Sending** — Intercepts `RpcSendChat`, translates to the lobby language, then sends
- **Receiving** — Catches `AddChat`, translates incoming messages asynchronously
- **Opening chat** — Scans untranslated messages and batch-translates them in one request

Every message is tracked with a `MessageGroup` storing both the original and translated text, which powers the right-click toggle.

## License

MIT
