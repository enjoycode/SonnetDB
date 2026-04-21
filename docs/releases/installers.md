---
layout: default
title: 安装包
description: Windows MSI 与 Linux DEB/RPM 安装包的默认目录、安装命令和启动方式。
permalink: /releases/installers/
---

## Windows MSI

默认安装目录通常为：

```text
%ProgramFiles%\SonnetDB Server
```

安装后可直接运行：

```powershell
start-sonnetdb.cmd
```

## Linux DEB / RPM

默认安装目录通常为：

```text
/opt/sonnetdb
```

安装示例：

```bash
sudo dpkg -i sonnetdb-<version>-linux-x64.deb
sudo rpm -i sonnetdb-<version>-linux-x64.rpm
```

安装完成后，一般可以直接运行：

```bash
sonnetdb
```
