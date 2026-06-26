# 东北往事无障碍 MUD 正式版

这是《东北往事》无障碍插件的第一个正式版，包含可直接安装的发布包和插件源码。

仓库不包含游戏本体、游戏 DLL、游戏资源或游戏 DLL 反编译源码。

## 目录

- `BepInEx/plugins/DongbeiAccessibility.dll`: 当前构建好的正式版插件。
- `decompiled/plugin`: 插件源码项目。
- `release/东北往事无障碍mud正式版.zip`: 当前完整发布包，用户下载这个即可安装。

## 构建

```powershell
dotnet build .\decompiled\plugin\DongbeiAccessibility.csproj -c Release
```

构建后的 DLL 位于：

`decompiled/plugin/bin/Release/netstandard2.1/DongbeiAccessibility.dll`

构建源码时需要传入本机游戏 Managed 目录：

```powershell
dotnet build .\decompiled\plugin\DongbeiAccessibility.csproj -c Release -p:GameManagedDir="D:\...\EastNorthStory_Data\Managed"
```
