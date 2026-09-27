namespace w2.models;

public class ServiceConfig
{
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string SshUser { get; set; } = string.Empty;
    public string SshPassword { get; set; } = string.Empty;

    public override string ToString() => $"{Name} ({Host}:{Port})";
}
