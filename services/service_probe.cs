using System.Diagnostics;
using System.Net;
using Renci.SshNet;

namespace watchtower.services;

/// <summary>
/// Универсальный "пробник" и "перезапускатель" сервиса.
/// Работает через SSH (для удалённых хостов) или локально (Linux).
/// Пытается определить способ управления сервисом автоматически:
/// systemd -> OpenRC -> SysV init -> process -> port-only.
/// </summary>
public class ServiceProbe(LogingService logger)
{
    private readonly LogingService _logger = logger;

    // -------- Публичный API --------

    /// <summary>
    /// Возвращает (hostReachable, serviceRunning).
    /// </summary>
    public async Task<(bool hostReachable, bool serviceRunning)> CheckAsync(ServiceConfig service)
    {
        var useSsh = IsRemote(service);

        if (useSsh)
            return await CheckViaSshAsync(service);
        else
            return await CheckLocalAsync(service);
    }

    /// <summary>
    /// Пытается перезапустить сервис. Возвращает true при успехе.
    /// </summary>
    public async Task<bool> RestartAsync(ServiceConfig service)
    {
        var useSsh = IsRemote(service);

        var command = BuildRestartCommand(service.Name);

        try
        {
            if (useSsh)
            {
                using var client = new SshClient(service.Host, service.SshUser, service.SshPassword);
                client.Connect();
                if (!client.IsConnected) return false;

                var result = client.RunCommand(command);
                client.Disconnect();

                _logger.Info($"Restart via SSH for {service.Name}: exit={result.ExitStatus}, out={result.Result?.Trim()}");
                return result.ExitStatus == 0;
            }
            else
            {
                if (!OperatingSystem.IsLinux())
                {
                    _logger.Warning($"Local restart unsupported on this OS for {service.Name}");
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

                _logger.Info($"Restart local for {service.Name}: exit={proc.ExitCode}, out={stdout.Trim()}, err={stderr.Trim()}");
                return proc.ExitCode == 0;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Restart failed for {service.Name}: {ex.Message}");
            return false;
        }
    }

    // -------- Внутренняя логика --------

    private static bool IsRemote(ServiceConfig s) =>
        !string.IsNullOrEmpty(s.Host) && IPAddress.TryParse(s.Host, out var address) && !IPAddress.IsLoopback(address); 

    /// <summary>
    /// Строит bash-команду, которая печатает RUNNING или STOPPED.
    /// Пробует несколько способов последовательно.
    /// </summary>
    private static string BuildCheckCommand(string name, int port)
    {
        return $@"
(
  # 1. systemd — САМЫЙ НАДЁЖНЫЙ
  #    Если юнит есть и не активен — сервис точно лежит.
  #    Проверяем именно наличие юнита, а не общий systemctl.
  if command -v systemctl >/dev/null 2>&1; then
    if systemctl list-unit-files 2>/dev/null | grep -qE '^{name}\.service\s'; then
      if systemctl is-active --quiet '{name}' 2>/dev/null; then
        echo RUNNING; exit 0
      else
        echo STOPPED; exit 0
      fi
    fi
  fi

  # 2. OpenRC
  if command -v rc-service >/dev/null 2>&1; then
    if rc-service '{name}' status 2>/dev/null | grep -qi 'started'; then
      echo RUNNING; exit 0
    fi
  fi

  # 3. SysV init
  if [ -x /etc/init.d/'{name}' ]; then
    if /etc/init.d/'{name}' status 2>/dev/null | grep -Eqi 'running|started'; then
      echo RUNNING; exit 0
    fi
  fi

  # 4. процесс — ищем ТОЧНОЕ имя процесса, а не подстроку
  #    pgrep -x требует точного совпадения имени (comm, до 15 символов)
  if command -v pgrep >/dev/null 2>&1; then
    if pgrep -x '{name}' >/dev/null 2>&1; then
      echo RUNNING; exit 0
    fi
  fi

  # 5. порт слушается
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

    private static string BuildRestartCommand(string name)
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
                _logger.Error($"SSH connect failed: {service.Host}");
                return (false, false);
            }

            var cmd = BuildCheckCommand(service.Name, service.Port);
            var result = client.RunCommand(cmd);
            client.Disconnect();

            var isRunning = result.Result?.Trim().EndsWith("RUNNING") == true;
            return (true, isRunning);
        }
        catch (Exception ex)
        {
            _logger.Error($"SSH probe error {service.Name}@{service.Host}: {ex.Message}");
            return (false, false);
        }
    }

    private async Task<(bool, bool)> CheckLocalAsync(ServiceConfig service)
    {
        if (!OperatingSystem.IsLinux())
        {
            _logger.Warning($"Local check unsupported on this OS for {service.Name}");
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

            return (true, output.Trim().EndsWith("RUNNING"));
        }
        catch (Exception ex)
        {
            _logger.Error($"Local probe error {service.Name}: {ex.Message}");
            return (true, false);
        }
    }
}