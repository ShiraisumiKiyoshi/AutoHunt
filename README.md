# AutoHunt

FFXIV 国服 Dalamud 自动狩猎插件。作者：白泉澈

## 功能

- **狩猎车跟随**：设定车头后，自动识别车头在任意聊天频道（说/喊/小队/通讯蛋/部队等）发送的地图坐标，自动插旗、上坐骑并飞行前往
- **狩猎怪精确识别**：基于游戏数据表 NotoriousMonster 按 NameId 匹配，默认锁定 A/S 级狩猎怪（可在设置中开启 B 级）
- **混合寻路**：远距离通过地图插旗 + vnavmesh flyflag 绕障飞行；近距离（<60m）自动切换 IPC 精确悬停，支持高度偏移
- **自动战斗**：到达后自动索敌、进入战斗、施放技能（依赖 ReAction / BossMod 等插件时可配合）

## 依赖插件

| 插件 | 用途 | 必需 |
|---|---|---|
| [vnavmesh](https://github.com/awgil/ffxiv_navmesh) | 寻路与飞行导航 | 是 |
| Lifestream | 跨地图传送 | 推荐 |
| Teleporter | 传送支持 | 推荐 |

## 安装

### 方式一：自定义插件仓库（推荐）

1. 游戏内输入 `/xllang` 确认语言为中文，打开卫月设置（`/xlsettings`）→ **实验功能** 标签页
2. 在 **自定义插件仓库** 一栏填入以下 URL 并勾选启用：

```
https://raw.githubusercontent.com/ShiraisumiKiyoshi/AutoHunt/main/repo/pluginmaster.json
```

3. 保存后打开插件安装器（`/xlplugins`），在"未安装"或第三方仓库分类中找到 **AutoHunt** 安装即可
4. 若 raw.githubusercontent.com 无法直连，可改用镜像地址：

```
https://cdn.jsdelivr.net/gh/ShiraisumiKiyoshi/AutoHunt@main/repo/pluginmaster.json
```

（镜像有约 12 小时缓存，更新会延迟）

### 方式二：开发者模式加载

将 `dist/` 目录下所有文件复制到任意文件夹，或直接用 Dalamud 的 `/xldev` 窗口 → Load Dev Plugin 加载本项目的 `AutoHunt.dll`。

详细说明见 [dist/安装说明.md](dist/安装说明.md)。

## 从源码构建

```
dotnet build -c Release
```

需要 Dalamud CN（国服）开发环境。产物输出至 `bin/Release/`。

## 使用说明

1. 游戏内输入 `/autohunt` 或 `/ah` 打开主窗口
2. 在设置页填写车头角色名（也可以将自己设为车头，自行发坐标测试）
3. 车头发送坐标消息（需包含地图链接或坐标数字），插件自动跟随
4. 建议开启 Debug 开关排查触发问题

## 免责声明

本项目仅供学习交流。使用自动化插件可能违反《最终幻想14》用户协议，由此产生的账号风险由使用者自行承担。

## 许可

仅供个人学习与研究使用。
