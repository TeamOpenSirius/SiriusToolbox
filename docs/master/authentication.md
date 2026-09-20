# Authentication and state

The official-sync window performs Environment, optional registration, authentication,
login, and MasterData-manifest requests. Supplied tokens take precedence over private
`state.json` values. The state file contains credentials and must not be published.
TLS validation remains enabled.

# 认证与状态

官方下载窗口依次请求环境、按需注册、认证、登录和主数据清单。界面输入的令牌优先于
私有 `state.json`；状态文件包含凭据，不应发布。TLS 校验默认开启。
