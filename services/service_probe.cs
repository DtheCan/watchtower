using System.Diagnostics;
using Renci.SshNet;

namespace watchtower.services;

public class HttpProbe
{
  private readonly LogingService _logger;
  private readonly HttpClient _client;

  public HttpProbe(LogingService logger, IHttpClientFactory factory)
  {
    _logger = logger;
    _client = factory.CreateClient("probe");
  }

  public async Task<(bool reachable, bool healthy, int? statusCode, long elapsedMs)> CheckAsync(
      ServiceConfig svc, CancellationToken ct)
  {
    var cfg = svc.HttpCheck!;
    var sw = Stopwatch.StartNew();

    try
    {
      using var req = new HttpRequestMessage(new HttpMethod(cfg.Method), cfg.Url);
      foreach (var h in cfg.Headers)
        req.Headers.TryAddWithoutValidation(h.Key, h.Value);

      using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      cts.CancelAfter(TimeSpan.FromSeconds(cfg.TimeoutSeconds));

      using var resp = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
      sw.Stop();

      int code = (int)resp.StatusCode;
      bool ok = code == cfg.ExpectedStatusCode;

      _logger.Info("http_probe",
          $"[{svc.Name}] {cfg.Method} {cfg.Url} → {code} за {sw.ElapsedMilliseconds}ms (ожидалось {cfg.ExpectedStatusCode})");

      return (true, ok, code, sw.ElapsedMilliseconds);
    }
    catch (TaskCanceledException)
    {
      sw.Stop();
      _logger.Warning("http_probe",
          $"[{svc.Name}] {cfg.Method} {cfg.Url} → TIMEOUT ({cfg.TimeoutSeconds}s)");
      return (false, false, null, sw.ElapsedMilliseconds);
    }
    catch (HttpRequestException ex)
    {
      sw.Stop();
      _logger.Warning("http_probe",
          $"[{svc.Name}] {cfg.Method} {cfg.Url} → ОШИБКА: {ex.Message}");
      return (false, false, null, sw.ElapsedMilliseconds);
    }
    catch (Exception ex)
    {
      sw.Stop();
      _logger.Error("http_probe",
          $"[{svc.Name}] {cfg.Method} {cfg.Url} → ИСКЛЮЧЕНИЕ: {ex.Message}");
      return (false, false, null, sw.ElapsedMilliseconds);
    }
  }
}

public class ServiceProbe
{
  private readonly LogingService _logger;

  public ServiceProbe(LogingService logger)
  {
    _logger = logger;
  }

  public async Task<(bool hostReachable, bool serviceRunning)> CheckAsync(ServiceConfig service)
  {
    bool useSsh = IsRemote(service);
    return useSsh
        ? await CheckViaSshAsync(service)
        : await CheckLocalAsync(service);
  }

  public async Task<bool> RestartAsync(ServiceConfig service)
  {
    bool useSsh = IsRemote(service);
    string command = !string.IsNullOrWhiteSpace(service.Ssh?.RestartCommand)
    ? service.Ssh!.RestartCommand!
    : BuildRestartCommand(service.Name);

    try
    {
      if (useSsh)
      {
        using var client = new SshClient(service.Host, service.Ssh!.User, service.Ssh!.Password);
        client.Connect();
        if (!client.IsConnected) return false;

        var result = client.RunCommand(command);
        client.Disconnect();

        _logger.Info("ssh_probe",
            $"[{service.Name}] RESTART via SSH: exit={result.ExitStatus}");
        return result.ExitStatus == 0;
      }
      else
      {
        if (!OperatingSystem.IsLinux())
        {
          _logger.Warning("ssh_probe", $"[{service.Name}] Локальный рестарт не поддерживается на этой ОС");
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
            $"[{service.Name}] RESTART локально: exit={proc.ExitCode}");
        return proc.ExitCode == 0;
      }
    }
    catch (Exception ex)
    {
      _logger.Error("ssh_probe", $"[{service.Name}] RESTART failed: {ex.Message}");
      return false;
    }
  }

  private static bool IsRemote(ServiceConfig s) =>
      !string.IsNullOrEmpty(s.Host) &&
      s.Host != "localhost" &&
      s.Host != "127.0.0.1";

  private string BuildCheckCommand(string name, int port)
  { /* без изменений */
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
  { /* без изменений */
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
      using var client = new SshClient(service.Host, service.Ssh!.User, service.Ssh!.Password);
      client.Connect();

      if (!client.IsConnected)
      {
        _logger.Error("ssh_probe", $"[{service.Name}] SSH connect failed");
        return (false, false);
      }

      var cmd = BuildCheckCommand(service.Name, service.Port);
      var result = client.RunCommand(cmd);
      client.Disconnect();

      bool isRunning = result.Result?.Trim().EndsWith("RUNNING") == true;
      _logger.Info("ssh_probe", $"[{service.Name}] SSH check → {(isRunning ? "RUNNING" : "STOPPED")}");
      return (true, isRunning);
    }
    catch (Exception ex)
    {
      _logger.Error("ssh_probe", $"[{service.Name}] SSH probe error: {ex.Message}");
      return (false, false);
    }
  }

  private async Task<(bool, bool)> CheckLocalAsync(ServiceConfig service)
  {
    if (!OperatingSystem.IsLinux())
    {
      _logger.Warning("ssh_probe", $"[{service.Name}] Локальная проверка не поддерживается");
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
      _logger.Info("ssh_probe", $"[{service.Name}] Локальная проверка → {(isRunning ? "RUNNING" : "STOPPED")}");
      return (true, isRunning);
    }
    catch (Exception ex)
    {
      _logger.Error("ssh_probe", $"[{service.Name}] Local probe error: {ex.Message}");
      return (true, false);
    }
  }
}