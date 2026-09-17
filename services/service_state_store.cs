using System.Collections.Concurrent;

namespace watchtower.services;

public class ServiceStateStore
{
    public class ServiceState
    {
        public string Key { get; set; } = "";        
        public string NodeName { get; set; } = "";   
        public string Name { get; set; } = "";      
        public string Host { get; set; } = "";       
        public int Port { get; set; }               

        public bool HttpOk { get; set; }
        public int? HttpCode { get; set; }
        public bool SshOk { get; set; }
        public bool SshRunning { get; set; }
        public DateTime LastCheckUtc { get; set; }
        public bool InMaintenance { get; set; }

        public bool LastResultHealthy => HttpOk || (SshOk && SshRunning);
        public string DisplayName =>
            string.IsNullOrEmpty(NodeName) ? Name : $"{NodeName} / {Name}";
    }

    private readonly ConcurrentDictionary<string, ServiceState> _states = new();

    public void Update(ServiceState state) => _states[state.Key] = state;
    public ServiceState? Get(string key) => _states.GetValueOrDefault(key);
    public IReadOnlyCollection<ServiceState> All() => _states.Values.ToList();
}