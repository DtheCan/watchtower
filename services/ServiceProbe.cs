using System.Diagnostics;
using System.Net;
using Renci.SshNet;
using w2.enums;
using w2.models;

namespace w2.services;

/// <summary>
/// Универсальный "пробник" и "перезапускатель" сервиса.
/// Работает через SSH (для удалённых хостов) или локально (Linux).
/// Пытается определить способ управления сервисом автоматически:
/// systemd -> OpenRC -> SysV init -> process -> port-only.
/// </summary>
public class ServiceProbe
{
    private readonly LogingService _logger = logger;

    public async Task<(bool hostReachable, bool serviceRunning)> CheckAsync(ServiceConfig service)
    {
        bool useSsh = IsRemote(service);

        if (useSsh)
            return await CheckViaSshAsync(service);
        else
            return await CheckLocalAsync(service);
    }

    public async Task<bool> RestartAsync(ServiceConfig service)
    {
        bool useSsh = IsRemote(service);

        string command = BuildRestartCommand(service.Name);

        try
        {
            if (useSsh)
            {
                using var client = new SshClient(service.Host, service.SshUser, service.SshPassword);
                client.Connect();
                if (!client.IsConnected) return false;

                var result = client.RunCommand(command);
                client.Disconnect();

                _logger.Info("ssh_probe",
                    $"[{service.LogName}] RESTART via SSH: exit={result.ExitStatus}");
                return result.ExitStatus == 0;
            }
            else
            {
                if (!OperatingSystem.IsLinux())
                {
                    _logger.Warning("ssh_probe", $"[{service.LogName}] Локальный рестарт не поддерживается");
                    return false;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "bash",
                    Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return false;

                var stdout = await proc.StandardOutput.ReadToEndAsync();
                var stderr = await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();

                _logger.Info("ssh_probe",
                    $"[{service.LogName}] RESTART локально: exit={proc.ExitCode}");
                return proc.ExitCode == 0;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("ssh_probe", $"[{service.LogName}] RESTART failed: {ex.Message}");
            return false;
        }
    }

    private static bool IsRemote(ServiceConfig s) =>
         !string.IsNullOrEmpty(s.Host) && IPAddress.TryParse(s.Host, out var address) && !IPAddress.IsLoopback(address);

    /// <summary>
    /// Строит bash-команду, которая печатает RUNNING или STOPPED.
    /// Пробует несколько способов последовательно.
    /// </summary>
    private string BuildCheckCommand(string name, int port)
    {
        return $@"
(
  if command -v systemctl >/dev/null 2>&1; then
    if systemctl list-unit-files 2>/dev/null | grep -qE '^{name}\.service\s'; then
      if systemctl is-active --quiet '{name}' 2>/dev/null; then
        echo RUNNING; exit 0
      else
        echo STOPPED; exit 0
      fi
    fi
  fi
  if command -v rc-service >/dev/null 2>&1; then
    if rc-service '{name}' status 2>/dev/null | grep -qi 'started'; then
      echo RUNNING; exit 0
    fi
  fi
  if [ -x /etc/init.d/'{name}' ]; then
    if /etc/init.d/'{name}' status 2>/dev/null | grep -Eqi 'running|started'; then
      echo RUNNING; exit 0
    fi
  fi
  if command -v pgrep >/dev/null 2>&1; then
    if pgrep -x '{name}' >/dev/null 2>&1; then
      echo RUNNING; exit 0
    fi
  fi
  if command -v ss >/dev/null 2>&1; then
    if ss -ltnH 2>/dev/null | awk '{{print $4}}' | grep -qE ':{port}$'; then
      echo RUNNING; exit 0
    fi
  elif command -v netstat >/dev/null 2>&1; then
    if netstat -ltn 2>/dev/null | awk '{{print $4}}' | grep -qE ':{port}$'; then
      echo RUNNING; exit 0
    fi
  fi
  echo STOPPED
)";
    }

    private string BuildRestartCommand(string name)
    {
        return $@"
(
  if command -v systemctl >/dev/null 2>&1; then
    if systemctl list-unit-files 2>/dev/null | grep -qE '^{name}\.service\s'; then
      systemctl restart '{name}' && exit 0
    fi
  fi
  if command -v rc-service >/dev/null 2>&1; then
    rc-service '{name}' restart && exit 0
  fi
  if [ -x /etc/init.d/'{name}' ]; then
    /etc/init.d/'{name}' restart && exit 0
  fi
  exit 1
)";
    }

    private async Task<(bool, bool)> CheckViaSshAsync(ServiceConfig service)
    {
        try
        {
            using var client = new SshClient(service.Host, service.SshUser, service.SshPassword);
            client.Connect();

            if (!client.IsConnected)
            {
                _logger.Error("ssh_probe", $"[{service.LogName}] SSH connect failed");
                return (false, false);
            }

            var cmd = BuildCheckCommand(service.Name, service.Port);
            var result = client.RunCommand(cmd);
            client.Disconnect();

            bool isRunning = result.Result?.Trim().EndsWith("RUNNING") == true;
            return (true, isRunning);
        }
        catch (Exception ex)
        {
            _logger.Error("ssh_probe", $"[{service.LogName}] SSH probe error: {ex.Message}");
            return (false, false);
        }
    }

    private async Task<(bool, bool)> CheckLocalAsync(ServiceConfig service)
    {
        if (!OperatingSystem.IsLinux())
        {
            _logger.Warning("ssh_probe", $"[{service.LogName}] Локальная проверка не поддерживается");
            return (true, false);
        }

        try
        {
            var cmd = BuildCheckCommand(service.Name, service.Port);
            var psi = new ProcessStartInfo
            {
                FileName = "bash",
                Arguments = $"-c \"{cmd.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return (true, false);

            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            bool isRunning = output.Trim().EndsWith("RUNNING");
            _logger.Info("ssh_probe", $"[{service.LogName}] Локальная проверка → {(isRunning ? "RUNNING" : "STOPPED")}");
            return (true, isRunning);
        }
        catch (Exception ex)
        {
            _logger.Error("ssh_probe", $"[{service.LogName}] Local probe error: {ex.Message}");
            return (true, false);
        }
    }

    private static Renci.SshNet.ConnectionInfo GetSshConnection(ServiceConfig service)
    {
        return service.Ssh.SshType switch
        {
            SshConfigType.PasswordAuth => new Renci.SshNet.ConnectionInfo(service.Host, service.Ssh.SshPort.ToString(),
                new PasswordAuthenticationMethod(service.Ssh.SshUser, service.Ssh.SshPassword)),
            SshConfigType.PrivateKeyAuth => new Renci.SshNet.ConnectionInfo(service.Host, service.Ssh.SshPort, service.Ssh.SshUser,
                new PrivateKeyAuthenticationMethod(service.Ssh.SshUser, new PrivateKeyFile(service.Ssh.SshKeyFilePath, service.Ssh.SshPassphrase))),
            _ => throw new ArgumentException()
        };
    }
}