# Telegram Panel

[English](README.md) | [中文](README.zh-CN.md)

A **WTelegramClient**-based multi-account Telegram management panel built with **.NET 8** and **Blazor Server**.

## Introduction

Telegram Panel is a unified web panel for managing and operating multiple Telegram accounts, focusing on account lifecycle management, batch operations, channel/group management, automated tasks, and module extension capabilities.

## Features

- 📥 **Multi-account Import / Login**: Supports Telethon / TData archive import/export, phone verification login, 2FA password handling
- 👥 **Batch Operations**: Batch join/subscribe/leave groups, auto-send messages in private groups, batch invite members/bots, batch set admins, export links, etc.
- 📱 **One-click Device Logout**: Keep current panel session, clean up other online devices
- 🧹 **Dead Account Detection & Cleanup**: Batch process banned, restricted, frozen, inactive, and session-expired accounts
- 🔐 **2FA Management**: Support single/batch password change, bind/rebind recovery email
- 👤 **Account Visibility Enhancement**: View joined channels/groups with estimated registration time
- 🧩 **Module Extension**: Task / API / UI installable extension modules (see docs/developer/modules.md)

## Quick Start

### Docker Deployment (Recommended)

Requirements: Docker (Windows: Docker Desktop + WSL2; Linux: Docker Engine)

```````bash
git clone https://github.com/moeacgx/Telegram-Panel
cd Telegram-Panel
cp .env.example .env
docker compose pull
docker compose up -d
```````

Access: http://localhost:5000

Default credentials: admin / admin123

### Local Development

```````bash
dotnet run --project src/TelegramPanel.Web
```````

Access: http://localhost:5000

## Screenshots

See screenshot/ folder.

## Star History

https://star-history.com/#moeacgx/Telegram-Panel&Date