# 安装包

## Windows MSI

安装完成后，默认目录位于：

```text
%ProgramFiles%\TSLite Server
```

在安装目录中运行：

```powershell
start-tslite-server.cmd
```

## Linux DEB / RPM

安装目录默认位于：

```text
/opt/tslite-server
```

安装命令示例：

Debian / Ubuntu：

```bash
sudo dpkg -i tslite-server-0.1.0-linux-x64.deb
```

RHEL / CentOS / Fedora：

```bash
sudo rpm -i tslite-server-0.1.0-linux-x64.rpm
```

安装完成后可直接运行：

```bash
tslite-server
```

CLI 也会同时安装为：

```bash
tslite
```
