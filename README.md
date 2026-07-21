# Telegram Panel

[English](README.md) | [中文](README.zh-CN.md)

A multi-account Telegram management panel built with **WTelegramClient**, a **.NET 8**
backend, and a **Vue 3** management UI.

<p align="center">
  <a href="https://t.me/zhanzhangck"><img src="https://img.shields.io/badge/Telegram-站长仓库-blue?logo=telegram" alt="Telegram 站长仓库"></a>
  <a href="https://t.me/vpsbbq"><img src="https://img.shields.io/badge/Telegram-NexHub_AI社区-blue?logo=telegram" alt="Telegram NexHub AI社区"></a>
</p>

## Current capabilities

- **Account onboarding:** Telethon, TData, and StringSession import; Telethon and TData
  export; phone-code, QR-code, and 2FA login.
- **Account-bound proxies:** HTTP, SOCKS5, MTProxy,
  [Resin](https://github.com/Resinat/Resin) sticky routes, and managed Cloudflare WARP,
  with per-account and batch binding plus egress checks.
- **Safe first connection:** import and login select and freeze the route before the first
  Telegram request instead of connecting directly and changing IP afterward.
- **Account maintenance:** status checks, invalid-account cleanup, device logout, 2FA, and
  recovery-email management.
- **Channels, groups, and bots:** creation, sync, categories, invitations, admin changes,
  public conversion, leave/disband operations, and link export.
- **Automation:** immediate and scheduled tasks, pause/edit/rerun controls, data dictionaries,
  templates, and optional OpenAI-compatible verification assistance.
- **Modules and APIs:** installable `.tpm` or `.zip` extensions for tasks, APIs, and management
  pages, with legacy Razor page compatibility.

## Account routes are managed by the host

Existing-account Telegram operations use the route assigned to that account. Modules should
call host account services with an `accountId`; they should not duplicate proxy credentials or
construct a separate `WTelegram.Client`. The host client pool applies the account's current
HTTP, SOCKS5, MTProxy, WARP, Resin, global-proxy, or explicit-direct route.

Module-owned `HttpClient` and third-party API traffic do not automatically inherit an account
route. Configure those connections separately only when the module itself needs it.

See [Proxy management and account egress](docs/guides/proxy-management.md) and the
[module development guide](docs/developer/modules.md).

## Install with Docker

Requirements: Docker Engine, or Docker Desktop with WSL2 on Windows.

```bash
git clone https://github.com/moeacgx/Telegram-Panel
cd Telegram-Panel
cp .env.example .env
docker compose pull
docker compose up -d
```

Open <http://localhost:5000> and sign in with the initial credentials:

- Username: `tgpanel`
- Password: `tgpanel123`

Change the initial password after the first sign-in. Persistent data is stored in
`./docker-data`; back up that directory before updates or migrations.

### Enable managed WARP

Managed WARP requires Linux containers and explicit Docker Socket access:

```bash
docker compose -f docker-compose.yml -f docker-compose.warp.yml up -d
```

Set the automatic-creation default to `http` or `socks5` in `.env`:

```dotenv
TP_WARP_PROXY_PROTOCOL=http
```

Docker Socket access is close to host `root`; enable this overlay only on a trusted host.

## Download and update

- Docker image: `ghcr.io/moeacgx/telegram-panel:latest`
- Windows installer and Linux packages:
  [latest GitHub Release](https://github.com/moeacgx/Telegram-Panel/releases/latest)
- In-app Docker update: **version badge → Version info → Check for updates → Update and restart**

Check the [update guide](docs/getting-started/update.md) before changing an existing deployment.

## Develop locally

Install the .NET 8 SDK and use the repository-pinned pnpm version:

```bash
corepack enable
pnpm --dir frontend install --frozen-lockfile
pnpm --dir frontend run build
dotnet run --project src/TelegramPanel.Web
```

Open <http://localhost:5000>.

## Documentation

- [Installation](docs/getting-started/installation.md)
- [Account import](docs/guides/account-import.md)
- [Proxy management and account egress](docs/guides/proxy-management.md)
- [Configuration and persistent data](docs/reference/configuration.md)
- [Module development](docs/developer/modules.md)
- [API reference](docs/reference/api.md)
- [Documentation site](https://moeacgx.github.io/Telegram-Panel/)

## Screenshots

More screenshots: `screenshot/`

| | | |
|---|---|---|
| <img src="screenshot/Dashboard.png" width="300" /> | <img src="screenshot/account.png" width="300" /> | <img src="screenshot/Import account.png" width="300" /> |

## Star History

[![Star History Chart](https://api.star-history.com/svg?repos=moeacgx/Telegram-Panel&type=Date)](https://star-history.com/#moeacgx/Telegram-Panel&Date)
