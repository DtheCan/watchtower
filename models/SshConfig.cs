using w2.enums;

namespace w2.models;

public class SshConfig
{
    public SshConfigType SshType = 0;
    public string SshUser { get; set; } = string.Empty;
    public string SshPassword { get; set; } = string.Empty;
    public int SshPort { get; set; } = 22;
    public string SshKeyFilePath { get; set; } = string.Empty;
    public string SshPassphrase { get; set; } = string.Empty;
}
