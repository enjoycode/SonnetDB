---
layout: default
title: 安装包
description: Windows MSI 与 Linux DEB/RPM 安装包的默认目录、安装命令和启动方式。
permalink: /releases/installers/
---

## Windows MSI

默认安装目录通常为：

```text
%ProgramFiles%\TSLite Server
```

安装后可直接运行：

```powershell
start-tslite-server.cmd
```

## Linux DEB / RPM

默认安装目录通常为：

```text
/opt/tslite-server
```

安装示例：

```bash
sudo dpkg -i tslite-server-<version>-linux-x64.deb
sudo rpm -i tslite-server-<version>-linux-x64.rpm
```

安装完成后，一般可以直接运行：

```bash
tslite-server
```
