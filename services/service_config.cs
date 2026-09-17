namespace watchtower.services;

public class ServiceConfig
{
    public string NodeName { get; set; } = "";

    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; }

    public HttpCheckConfig? HttpCheck { get; set; }
    public SshConfig? Ssh { get; set; }

    public bool HttpEnabled => HttpCheck != null && !string.IsNullOrWhiteSpace(HttpCheck.Url);
    public bool SshEnabled  => Ssh != null && !string.IsNullOrWhiteSpace(Ssh.User);

    public string Key => $"{NodeName}:{Name}";
        public string DisplayName =>
        string.IsNullOrEmpty(NodeName) ? Name : $"{NodeName} / {Name}";

    public string LogName => $"{DisplayName} ({Host}:{Port})";

    public override string ToString() => LogName;
}

public class HttpCheckConfig
{
    public string Url { get; set; } = "";
    public string Method { get; set; } = "GET";
    public int ExpectedStatusCode { get; set; } = 200;
    public int TimeoutSeconds { get; set; } = 5;
    public Dictionary<string, string> Headers { get; set; } = new();
}

public class SshConfig
{
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string? RestartCommand { get; set; }
}