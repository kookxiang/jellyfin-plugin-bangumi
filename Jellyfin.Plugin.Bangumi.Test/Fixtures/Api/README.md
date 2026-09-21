# 离线 Bangumi API 数据

所有 .NET 测试通过 `ServiceLocator` 注入 `MockedBangumiApi`，在 HTTP 层返回本目录的内嵌响应。真实的 API 反序列化、缓存、分页、搜索和元数据映射逻辑仍会执行。初始数据来自现有测试使用的公开 Bangumi API 响应。

测试不会录制或下载数据，也不会在缺少 fixture 时回退联网。未匹配请求会立即抛出异常，并在 assembly cleanup 再次检查，防止 provider 捕获异常后掩盖遗漏。测试使用独立的临时应用数据目录，不依赖本机存档或 OAuth 数据。

## 添加或修改数据

每个 JSON 文件包含：

- `Request`：`HTTP方法 + 空格 + PathAndQuery + "\n" + 请求正文`，GET 正文为空；保留查询参数顺序和 POST JSON 的实际序列化形式。
- `Status`：HTTP 状态码，包括需要测试的错误响应。
- `Body`：响应正文字符串。
- `Location`：重定向目标，无重定向时为 `null`。

文件名为 `Request` 的 UTF-8 SHA-256 小写十六进制值加 `.json`。缺少数据的测试错误会给出完整请求和目标文件名。为新场景编写固定响应并检查断言；不需要访问真实服务。修改响应内容不会改变文件名。JSON 文件通过测试项目的 `EmbeddedResource` 自动包含，因此不依赖测试运行目录。

更新线上快照时只使用公开数据，检查差异后提交；不要保存认证信息。数据变化不应自动改变测试预期。

## 运行

```sh
dotnet test Jellyfin.Plugin.Bangumi.Test
```

依赖恢复和前端构建仍按项目原有构建流程执行；完成构建后可以完全离线运行：

```sh
dotnet test Jellyfin.Plugin.Bangumi.Test --no-build --no-restore
```
