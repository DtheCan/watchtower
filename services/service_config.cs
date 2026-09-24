namespace watchtower.services;

public class ServiceConfig
{
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 0;
    public string SshUser { get; set; } = string.Empty;
    public string SshPassword { get; set; } = string.Empty;

    public override string ToString() => $"{Name} ({Host}:{Port})";
}