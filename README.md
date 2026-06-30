# 东北往事无障碍 MUD 正式版

这是《东北往事》无障碍插件的正式版仓库。

仓库内容分两类：

- 想直接安装插件：下载 Release 里的 `东北往事无障碍mud正式版.zip`。
- 想查看或编译源码：下载 GitHub 仓库源码。

本仓库和发布包不包含游戏本体、游戏 DLL、游戏资源、游戏反编译源码。这里只包含插件源码、插件运行所需文件和说明文档。

## 目录说明

- `BepInEx/plugins/DongbeiAccessibility.dll`：当前构建好的插件。
- `decompiled/plugin`：插件源码项目。
- `release/东北往事无障碍mud正式版.zip`：可直接安装的插件包，不包含源码。
- `使用说明.txt`：给玩家看的安装和按键说明。
- `changelog.txt`：玩家视角更新日志。

## 构建源码

本插件源码编译时需要你本机游戏的 `EastNorthStory_Data/Managed` 目录作为引用路径。

```powershell
dotnet build .\decompiled\plugin\DongbeiAccessibility.csproj -c Release -p:GameManagedDir="D:\...\EastNorthStory_Data\Managed"
```

构建后的 DLL 位于：

```text
decompiled/plugin/bin/Release/netstandard2.1/DongbeiAccessibility.dll
```

## 主要功能

- 自动朗读剧情字幕、剧情旁白、好感度提示、菜单、选项、设置、故事线、结尾页和 QTE 提示。
- 剧情旁白和好感度增加/减少提示会优先强制朗读，不再只依赖字幕朗读。
- 支持上下左右光标、回车、Esc、数字键等键盘操作。
- 设置页面可朗读项目名称和当前状态。
- 故事线支持章节切换、自动节点浏览、节点跳转失败提示。
- 剧情选项优先于探索交互点检测，避免假交互点抢占真正选项。
- QTE 会优先朗读“空格”，方向提示作为补充。
- F11 可切换自动过 QTE。
- 快捷键只在游戏窗口前台时生效，并尽量放行屏幕阅读器组合键和系统组合键。

## 当前常用按键

- 上下光标：切换选项、设置项、故事线节点。
- 左右光标：调整设置，或在故事线章节页切换章节。
- 回车：确认当前项目。
- Esc：返回或关闭当前页面。
- 数字键 1 到 9：直接选择列表里的对应项目。
- D：开关字幕朗读。
- F3：故事线内跳到当前进度节点。
- F5：重复朗读上一条内容。
- F6：停止朗读。
- F11：切换自动过 QTE。
- 空格：按 QTE 提示完成或跳过当前 QTE。

F2、F4、F10 的旧手动功能已经移除。
