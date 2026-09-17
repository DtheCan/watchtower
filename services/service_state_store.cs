using System.Collections.Concurrent;

namespace watchtower.services;

public class ServiceStateStore
{
    public class ServiceState
    {
        public string Name { get; set; } = "";
        public bool HttpOk { get; set; }
        public int? HttpCode { get; set; }
        public bool SshOk { get; set; }
        public bool SshRunning { get; set; }
        public DateTime LastCheckUtc { get; set; }
        public bool InMaintenance { get; set; }
        public bool LastResultHealthy => HttpOk || (SshOk && SshRunning);
    }

    private readonly ConcurrentDictionary<string, ServiceState> _states = new();

    public void Update(ServiceState state) => _states[state.Name] = state;
    public ServiceState? Get(string name) => _states.GetValueOrDefault(name);
    public IReadOnlyCollection<ServiceState> All() => _states.Values.ToList();
}