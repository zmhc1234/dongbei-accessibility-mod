# 东北往事无障碍 MUD 正式版

这是《东北往事》无障碍插件的第一个正式版仓库。

仓库内容分两类：

- 想直接安装插件：下载 Release 里的 `dongbei-accessibility-mod-v1.0.0.zip`。
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

- 自动朗读剧情字幕、菜单、选项、设置、故事线、结尾页和 QTE 提示。
- 支持上下左右光标、回车、Esc、数字键等键盘操作。
- 设置页面可朗读项目名称和当前状态。
- 故事线支持章节切换、节点浏览、节点跳转失败提示。
- QTE 会优先朗读“空格”，方向提示作为补充。
- 快捷键只在游戏窗口前台时生效，并尽量放行屏幕阅读器组合键。
