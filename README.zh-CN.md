# TranslateUs — Among Us 实时聊天翻译

[English](README.md) · [简体中文](README.zh-CN.md)

---

用你的母语打字,别人看到的是他们的语言。可配置云端 AI,也可以在自己的电脑或手机上运行本地 AI 模型 —— 无需联网、无需 API Key,隐私完全掌握在自己手里。

## 功能特性

- **本地 AI** — 通过 llama.cpp 完全离线运行 GGUF 模型(例如 Qwen2.5-1.5B)。Windows **和 Android** 均可使用。零联网、隐私无忧。
- **提示词缓存预热** — 进入房间后,系统提示词和术语表只解码一次并缓存。首次预热(约 15–30 秒)期间消息暂不翻译,完成后即可使用本地模型。
- **提示注入防护** — 聊天正文无法劫持模型(如"忘记你的规则""你是谁"等),模型只负责翻译。
- **云端 AI** — 自带 Key(OpenAI、智谱、DeepSeek 或任意 OpenAI 兼容接口)。仅限 Windows。
- **发送即翻译** — 用你的语言打字,发送时自动转成房间语言。
- **接收即翻译** — 收到的消息即时翻译成你的语言。
- **批量翻译** — 打开聊天时一次性翻译所有待处理消息。
- **右键切换** — 右键任意气泡,在原文本与译文之间切换。
- **暂停按钮** — 聊天内的开关,随时暂停/恢复翻译。
- **隐私优先** — 不可用或未配置翻译后端时保留原文,不会将消息发送给其他服务。
- **作业对象(Windows)** — 游戏退出时 llama-server 自动终止,不留孤儿进程。

## 平台支持

| 后端 | Windows | Android |
|------|:-------:|:-------:|
| 本地 AI(llama.cpp) | ✅ | ✅ |
| 云端 AI | ✅ | ❌ |

## 快速开始

### 云端 AI(Windows,俚语效果更好)

1. 把 `TranslateUs.dll` 丢进 `BepInEx/plugins/`
2. 启动一次 Among Us 生成配置文件
3. 编辑 `BepInEx/config/ume.transalte.us.cfg`:

```ini
[AI]
Backend = CloudAI

[Cloud AI]
ApiKey = your-api-key
ApiUrl = https://open.bigmodel.cn/api/paas/v4/chat/completions
Model = glm-4-flash
```

### 本地 AI(隐私优先、无需联网 —— Windows 和 Android)

1. 把 `TranslateUs.dll` 丢进 `BepInEx/plugins/`
2. 编辑 `BepInEx/config/ume.transalte.us.cfg`:

```ini
[AI]
Backend = LocalAI
```

3. 启动游戏。llama.cpp 运行库会自动下载,然后模型(约 1 GB)会下载一次,并在启动画面上显示进度条。

> **Android 说明:** 在 Android 上,原生运行库(`libllama.so` + GGML CPU 后端)会下载并进程内加载 —— 不需要 `llama-server` 子进程,因此可直接从内部存储正常运行。

## 配置

### [AI]

| 键 | 默认值 | 说明 |
|-----|---------|-------------|
| `Backend` | `Auto` | `Auto` / `CloudAI` / `LocalAI`。未配置后端时不翻译。 |

### [Cloud AI]

| 键 | 默认值 | 说明 |
|-----|---------|-------------|
| `ApiKey` | *(空)* | 你的 API Key |
| `ApiUrl` | 智谱 GLM 接口 | 任意 OpenAI 兼容接口 |
| `Model` | `glm-4-flash` | 模型名 |
| `ExtraPrompt` | *(空)* | 追加到翻译提示词的额外指令 |

### [LocalAI]

| 键 | 默认值 | 说明 |
|-----|---------|-------------|
| `LocalModelPath` | *(空)* | `.gguf` 模型文件路径。留空则自动下载 Qwen 2.5 1.5B。 |
| `LocalModelUrl` | HuggingFace 链接 | GGUF 模型的下载地址 |
| `NativeLibUrl` | GitHub releases | llama.cpp 运行库下载的基础地址 |
| `InferenceThreads` | `2` | 推理用的 CPU 线程数(建议保持较低以免掉帧) |
| `ContextSize` | `4096` | KV 缓存上下文大小(单位:token) |
| `MaxInferenceTokens` | `128` | 每次翻译生成的最大 token 数 |
| `InferenceTemperature` | `0.1` | 温度(0.0–2.0)。越低越确定 |

### [Translation]

| 键 | 默认值 | 说明 |
|-----|---------|-------------|
| `RoomLanguage` | `Auto` | 覆盖房间语言。`Auto` 使用房主的 Keywords 设置。 |
| `TranslateOwnBubbles` | `false` | 为 `true` 时,自己的气泡显示译文而非原文。 |

## 环境要求

- [BepInEx](https://github.com/BepInEx/BepInEx)(Among Us IL2CPP 版)
- **Windows**:64 位。本地模型需约 1 GB 磁盘空间。
- **Android**:仅支持本地 AI。云端 AI 不可用。本地 AI 需要约 1 GB 空间存放模型,外加 llama.cpp 运行库。
- (可选)来自 [智谱 AI](https://open.bigmodel.cn/)(有免费额度)、OpenAI 或任意兼容服务商的 API Key —— 仅限 Windows。

# 隐私政策 — TranslateUs

**TranslateUs** 是一款针对《Among Us》的 BepInEx 插件，用于提供游戏内聊天消息的实时翻译。本文档说明数据的处理方式、流向以及隐私保护措施。

*最后更新：2026-07-22*

---

## 1. 我们不收集的数据

TranslateUs **不会**收集、存储、记录或传输以下任何信息：

- 玩家身份或用户名
- 账户凭证或 API 密钥（API 密钥**仅**存储在您本地的 `BepInEx/config/ume.transalte.us.cfg` 文件中）
- 聊天历史或消息记录
- 游戏会话数据
- 设备标识符或硬件信息
- 任何分析或遥测数据

本插件完全在您的本地游戏客户端中运行。**TranslateUs 没有任何后端服务器**——我们没有任何服务器基础设施。

---

## 2. 术语表 — 完全离线

TranslateUs 内嵌了一份**预制的术语指南**（涵盖 16 种语言的 Among Us 俚语、角色名称、地图位置和社区表达），以静态 XML 文件的形式打包在插件内部。

- **本地存储** — 作为嵌入资源打包在插件 DLL 中
- **运行时从不联网更新** — 不会为术语数据发起任何网络请求
- **不涉及 AI** — 词汇表为离线人工整理，不由任何 AI 服务生成

**任何聊天消息或游戏数据都不会为了术语查找或词汇表构建而发送到任何服务器。**

---

## 3. 翻译服务 — 传输中的数据

翻译聊天消息可使用本地模型，或在 Windows 上使用您配置的云端 AI。

### 3.1 AI 翻译模式

如果您**选择**配置 API 密钥（OpenAI、智谱 AI、DeepSeek 或任何兼容 OpenAI 接口的服务商），TranslateUs 会将聊天消息发送到**您配置的 API 端点**。

| 项目 | 详情 |
|------|------|
| **发送什么** | 仅发送聊天消息文本和目标语言。系统提示词中包含预烘焙的术语表（离线存储 — 见第 2 节）。 |
| **发往何处** | **您**在 `BepInEx/config/ume.transalte.us.cfg` 中配置的 API 地址 |
| **适用隐私政策** | **您所选** AI 服务商的隐私政策（例如 [OpenAI](https://openai.com/policies/privacy-policy)、[智谱 AI](https://open.bigmodel.cn/privacy)） |
| **您的控制权** | 您自行选择服务商、模型，以及是否启用此模式。未配置后端时消息保持原文。 |
| **Android 限制** | 云端 AI 在 Android 上不可用；可使用完全离线的本地 AI。 |

### 3.2 本地 AI

启用本地 AI 后，消息在设备上使用 GGUF 模型处理，不会发送到翻译服务。模型和运行库首次使用时会下载。

### 3.3 暂停控制

您可以使用游戏内的暂停按钮随时停止翻译。暂停期间，**不会向任何翻译服务发送任何文本**——消息仅以原始形式显示。

---

## 4. 数据流向总览

| 场景 | 发送的数据 | 目的地 | 何时 |
|------|-----------|--------|------|
| 术语查找 | **无**（离线 XML） | 无 | 始终 |
| 翻译（AI 模式） | 聊天消息文本 | 您配置的 API 端点 | 每条消息，未暂停时 |
| 翻译（已暂停） | **无** | 无 | 暂停期间 |
| 翻译（本地 AI） | 聊天消息文本 | 仅本机 | 使用本地模型时 |
| 未配置后端 | **无** | 无 | 消息保持原文 |

---

## 5. 您的选择

| 您可以... | 操作方法 |
|-----------|---------|
| 使用自己的 AI 服务商 | 在配置中设置 `ApiKey` 和 `ApiUrl` — 您掌控数据发往哪家服务商 |
| 完全暂停翻译 | 点击游戏内暂停按钮 |
| 审查源代码 | 所有翻译逻辑在 `Translator.cs`、`SlangCache.cs`、`TerminologyLoader.cs` 中 |

---

## 6. 与之前版本的变更

从 v1.2.2-pre 起：

- **已移除**：运行时的 AI 术语/词汇表生成。插件不再为构建词汇表发起任何 AI API 调用。
- **替换为**：预烘焙的静态 XML 术语文件（16 种语言）打包于插件内 — 零网络依赖，零运行时 AI 调用。
- **说明**：Android 仅支持本地 AI 翻译；云端 AI 不可用。

---

## 7. 联系方式

如对本隐私政策或插件的数据处理有任何疑问，请在项目仓库中提交 Issue。
