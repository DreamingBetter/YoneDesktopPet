# Contributing

欢迎提交 issue 和 pull request。

## 开发流程

1. Fork 仓库。
2. 创建功能分支。
3. 修改代码并确认可以构建：

```powershell
dotnet build -c Release
```

4. 提交 pull request，说明变更内容和验证方式。

## 约定

- 不提交 `bin/`、`obj/`、`publish*/` 等构建产物。
- 不提交未确认授权的角色图片、游戏素材、音频或其他第三方资源。
- 新增交互时优先保持桌宠窗口透明、轻量、可直接双击发布 EXE 运行。
