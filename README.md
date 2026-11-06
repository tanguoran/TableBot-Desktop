# TableBot Desktop / Whale Desk

独立的 Unity 3D 桌面与鲸鱼娘呈现、感知和动作执行仓库。在完整 TableBot 工作区中，本仓库作为 `desktop/` 子模块挂载；Unity 项目路径仍为 `desktop/ds_desktop/`。

## 范围

- `ds_desktop/Assets/`：场景、C# 脚本、角色、动画与美术资产。
- `ds_desktop/Packages/`：包清单、锁文件及嵌入式 UniVRM/UniGLTF 包。
- `ds_desktop/ProjectSettings/`：Unity 项目设置。
- 模型提供方、API 密钥、Python Agent、MCP 服务、知识库与数据库由上层 TableBot 仓库管理，不嵌入本仓库。

## 获取与运行

仓库地址：<https://github.com/tanguoran/TableBot-Desktop>。这是私有仓库，需要使用有访问权限的 GitHub 账号。

需要 Unity **6000.3.27f1** 和 Git LFS。独立获取桌面项目：

```powershell
git -c core.longpaths=true clone https://github.com/tanguoran/TableBot-Desktop.git
cd TableBot-Desktop
```

也可从主仓库 <https://github.com/tanguoran/TableBot> 通过子模块获取。在桌面仓库目录执行：

```powershell
git config core.longpaths true
git lfs install --local
git lfs pull
```

用 Unity Hub 打开 `ds_desktop/`，打开 `Assets/Scenes/WhaleDesk.unity`，点击 Play。场景、文件映射和手动角色控制可以独立运行；模型聊天与自主规划需要启动 TableBot 的 Python 服务。

Unity 缓存、临时文件、日志、预览截图和生成的 IDE 工程不纳入版本控制。二进制源资产使用 Git LFS；场景、预制体、动画控制器和 `.meta` 文件保留在普通 Git 中。切勿将 LFS 指针文件当作完整资产交给 Unity。

## 与 Agent 的边界

- `http://127.0.0.1:18765`：Python 桥接服务；Unity 通过 `POST /unity/exchange` 交换实际场景状态与动作结果，并接收命令。
- `http://127.0.0.1:18766`：Python 聊天服务；Unity 使用 `POST /message` 与 `GET /state`。
- Unity 负责真实的场景状态和动作执行结果；Python 负责模型调用、规划、工具调度和对话。两侧通过接口连接，无需相互导入源码。
- 没有模型服务时，桌面仍可运行，模型功能显示离线。

场景与导航细节见 [Whale Desk 说明](ds_desktop/Assets/WhaleDesk/README.md)。完整工作区中的联调、文件映射和工具说明由上层仓库的 `docs/` 维护。

## 提交顺序

先在本仓库提交桌面改动，再回上层仓库提交 `desktop` 的新提交指针。知识库是上层仓库的另一个并列子模块，与本仓库没有嵌套关系。
