# DeathIntercept / 死亡拦截

[English](#english) | [汉语](#汉语)

---

## English

### What is this?

**DeathIntercept** is a mod for *Slay the Spire 2* that lets you choose whether to retry a combat or give up when your character dies. Instead of immediately ending your run and deleting the auto-save, the mod pauses the death flow and presents a dialog:

| Button | Action |
|--------|--------|
| **Retry** (Enter) | Reload the combat directly from the pre-combat save — the fight restarts from round 1. |
| **Give Up** (Esc) | Proceed with the normal game over flow — saves your score, badges, and run history. |

### How it works

1. Intercepts `CreatureCmd.Kill` *before* the death music and game-over logic run.
2. Preserves the run save by blocking `SaveManager.DeleteCurrentRun`.
3. On retry, reloads the combat in-place using the same flow as the main menu "Continue" button — no main menu transition, no extra clicks.

### Installation

1. Drop `DeathIntercept.dll` and `DeathIntercept.json` into `Slay the Spire 2/mods/DeathIntercept/`.
2. Launch the game. The mod auto-activates.

### Building from source

```bash
dotnet build DeathIntercept.csproj
```

Requires .NET 9.0 SDK and references to `sts2.dll`, `GodotSharp.dll`, and `0Harmony.dll` from the game's `data_sts2_windows_x86_64/` directory (auto-discovered via `Sts2PathDiscovery.props`).

### Credits

The combat reload logic is adapted from **[STS2-QuickReload](https://github.com/mmmmie/STS2-QuickReload)**, specifically the `QuickReloadRunner.RestartSinglePlayer()` flow — thank you for the reference implementation!

---

## 汉语

### 这是什么？

**DeathIntercept**是一个《杀戮尖塔2》的 Mod，让你在角色阵亡时可以选择重打或放弃。不再直接结束对局并删除自动存档，而是暂停死亡流程，弹出对话框：

| 按钮 | 行为 |
|------|------|
| **重打**（Enter） | 从战斗前存档直接重载战斗——从第一回合重新开始。 |
| **放弃**（Esc） | 走正常游戏结束流程——保存分数、徽章和对局历史。 |

### 原理

1. 在战败 BGM 和游戏结束逻辑运行**之前**拦截 `CreatureCmd.Kill`。
2. 通过拦截 `SaveManager.DeleteCurrentRun` 保护战斗前存档不被删除。
3. 选择重打时，复用主菜单「继续」按钮的流程原地重载战斗——无需返回主菜单，无需额外点击。

### 安装

1. 将 `DeathIntercept.dll` 和 `DeathIntercept.json` 放入 `Slay the Spire 2/mods/DeathIntercept/`。
2. 启动游戏，Mod 自动激活。

### 从源码构建

```bash
dotnet build DeathIntercept.csproj
```

需要 .NET 9.0 SDK，依赖游戏 `data_sts2_windows_x86_64/` 目录下的 `sts2.dll`、`GodotSharp.dll`、`0Harmony.dll`（通过 `Sts2PathDiscovery.props` 自动发现）。

### 鸣谢

战斗重载逻辑参考了 **[STS2-QuickReload](https://github.com/mmmmie/STS2-QuickReload)**，尤其是 `QuickReloadRunner.RestartSinglePlayer()` 的实现——感谢参考代码！

