# Roadmap: DalamudMCP API 14 -> 15 迁移

## Overview

从 Dalamud API Level 14 升级到 15 的范围极有限兼容性迁移。研究确认代码库无需修改 API 15 的三个破坏性变更（IChatGui、IClientState、ImRaii 均未使用）。迁移工作集中在三个配置变更（SDK 版本、manifest API Level、packages.lock.json），然后通过结构化验证清单在运行时确认迁移成功。

## Phases

- [ ] **Phase 1: 构建环境前提确认** - 验证 DALAMUD_HOME 指向 API 15 运行时，确认 SDK 版本
- [ ] **Phase 2: SDK 版本升级** - `.csproj` 中 `Dalamud.NET.Sdk` 从 `14.0.2` 升级到 `15.0.0`
- [ ] **Phase 3: Manifest 与锁文件更新** - 更新 `DalamudApiLevel` 并重新生成 `packages.lock.json`
- [ ] **Phase 4: 编译验证** - 通过 `./build/build.ps1` 确认项目在 API 15 下成功编译
- [ ] **Phase 5: 运行时加载与操作验证** - 插件在 API 15 运行时中加载，20+ 操作功能正确
- [ ] **Phase 6: IPC 桥接与 CLI 模式验证** - 命名管道 IPC、stdio MCP、HTTP MCP、直接 CLI 均正常工作
- [ ] **Phase 7: 打包验证** - 发布 zip 中 manifest 准确，`DalamudApiLevel` 为 15

## Phase Details

### Phase 1: 构建环境前提确认
**Goal**: 构建环境已确认指向正确的 API 15 运行时，SDK 版本已核实
**Depends on**: Nothing (first phase)
**Requirements**: ENV-01
**Success Criteria** (what must be TRUE):
  1. `DALAMUD_HOME` 环境变量指向包含 API 15 `Dalamud.dll` 引用程序集的 `Hooks/dev` 目录
  2. 目标目录中可验证存在 `Dalamud.dll` 且版本对应 API 15
  3. `Dalamud.NET.Sdk/15.0.0` 和 `DalamudPackager/15.0.0` 已通过 NuGet 源确认可用
  4. SDK 版本不确定性已解决（若 `15.0.0` 不可用，已确认实际可用版本并记录）
**Plans**: TBD

### Phase 2: SDK 版本升级
**Goal**: `DalamudMCP.Plugin` 项目已引用 API 15 SDK
**Depends on**: Phase 1
**Requirements**: CFG-01
**Success Criteria** (what must be TRUE):
  1. `src/DalamudMCP.Plugin/DalamudMCP.Plugin.csproj` 中 `Dalamud.NET.Sdk` 版本已从 `14.0.2` 改为 `15.0.0`
  2. `dotnet restore` 成功解析新 SDK 版本，无版本冲突错误
  3. 非 Plugin 项目的 NuGet 依赖版本保持不变（MemoryPack、DI 等）
**Plans**: TBD

### Phase 3: Manifest 与锁文件更新
**Goal**: 插件声明和依赖锁文件已更新至 API 15
**Depends on**: Phase 2
**Requirements**: CFG-02, CFG-03
**Success Criteria** (what must be TRUE):
  1. `src/DalamudMCP.Plugin/DalamudMCP.json` 中 `DalamudApiLevel` 已从 `14` 改为 `15`
  2. 三个 `packages.lock.json` 文件（`DalamudMCP.Plugin`、`DalamudMCP.Plugin.Tests`、`DalamudMCP.Plugin.Operations.Tests`）均已重新生成
  3. 重新生成后锁文件中 `DalamudPackager` 版本为 `15.0.0`
  4. 锁文件中非 Dalamud 依赖（MemoryPack、ModelContextProtocol 等）版本与升级前一致
**Plans**: TBD

### Phase 4: 编译验证
**Goal**: 项目在 API 15 引用下成功编译
**Depends on**: Phase 3
**Requirements**: ENV-02
**Success Criteria** (what must be TRUE):
  1. `./build/build.ps1` 执行成功，返回退出码 0
  2. Plugin 项目 DLL（`DalamudMCP.Plugin.dll`）正确生成在输出目录中
  3. 其余六个源项目均正常编译，不受 SDK 升级影响
  4. 八个测试项目均正常编译
**Plans**: TBD

### Phase 5: 运行时加载与操作验证
**Goal**: 插件在 API 15 运行时中正常加载，所有操作功能正确
**Depends on**: Phase 4
**Requirements**: VAL-01, VAL-02
**Success Criteria** (what must be TRUE):
  1. Dalamud 插件列表显示 DalamudMCP 已加载且 `DalamudApiLevel: 15`，控制台无错误输出
  2. 无 `MissingMethodException`、`TypeLoadException` 或其他 API 不兼容异常
  3. 所有 20+ 游戏操作均可成功执行，包括观察读取和行动写入
  4. unsafe 操作（AddonInput、InteractWithTarget 等）在 Patch 7.5 布局下仍正确工作
**Plans**: TBD

### Phase 6: IPC 桥接与 CLI 模式验证
**Goal**: CLI 通过命名管道与插件正常通信，三种模式可用
**Depends on**: Phase 5
**Requirements**: VAL-03, VAL-04
**Success Criteria** (what must be TRUE):
  1. CLI 命名管道连接成功，可发送操作请求并接收二进制响应
  2. stdio MCP 服务模式下 AI 客户端可通过标准输入/输出与 CLI 通信
  3. Streamable HTTP MCP 服务模式下 AI 客户端可通过 HTTP 端点与 CLI 通信
  4. 直接 CLI 模式下参数化命令执行成功，输出格式正确
**Plans**: TBD

### Phase 7: 打包验证
**Goal**: 发布包的 manifest 配置已正确为 API 15
**Depends on**: Phase 4
**Requirements**: VAL-05
**Success Criteria** (what must be TRUE):
  1. `dotnet build -c Release` 生成的 `latest.zip` 中 `DalamudMCP.json` 的 `DalamudApiLevel` 为 `15`
  2. 仓库源文件中的 manifest 在打包过程中未被 API 15 运行时覆盖为不正确的值
  3. zip 包中无多余文件或错误版本的文件
**Plans**: TBD

## Progress

**Execution Order:**
Phases execute in numeric order: 1 -> 2 -> 3 -> 4 -> 5 -> 6 -> 7

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. 构建环境前提确认 | 0/0 | Not started | - |
| 2. SDK 版本升级 | 0/0 | Not started | - |
| 3. Manifest 与锁文件更新 | 0/0 | Not started | - |
| 4. 编译验证 | 0/0 | Not started | - |
| 5. 运行时加载与操作验证 | 0/0 | Not started | - |
| 6. IPC 桥接与 CLI 模式验证 | 0/0 | Not started | - |
| 7. 打包验证 | 0/0 | Not started | - |
