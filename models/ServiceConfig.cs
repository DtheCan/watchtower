namespace w2.models;

public class ServiceConfig
{
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 0;
    public SshConfig Ssh { get; set; } = new(); 
    public override string ToString() => $"{Name} ({Host}:{Port})";
}
