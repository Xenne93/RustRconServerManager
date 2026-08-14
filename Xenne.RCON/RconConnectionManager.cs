using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Xenne.RCON
{
     
    
    // RCON Connection Manager manages the RCON clients in a Thread-Safe Dictionary.
    // Needs to be instanced.
    
    public class RconConnectionManager
    {
        private readonly ConcurrentDictionary<int, RconClient> _clients = new();

        public IEnumerable<int> ClientIds => _clients.Keys;

        public bool TryAddClient(int id, RconClient client)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            return _clients.TryAdd(id, client);
        }

        public void AddOrReplaceClient(int id, RconClient client)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));

            // If replacing an existing client, dispose the old one first
            if (_clients.TryGetValue(id, out var oldClient))
            {
                oldClient?.Dispose();
            }

            _clients[id] = client;
        }

        public bool RemoveClient(int id)
        {
            if (_clients.TryRemove(id, out var client))
            {
                // Dispose the removed client to free resources
                client?.Dispose();
                return true;
            }
            return false;
        }

        public bool TryGetClient(int id, out RconClient? client)
        {
            return _clients.TryGetValue(id, out client);
        }

        public RconClient? GetClient(int id)
        {
            _clients.TryGetValue(id, out var client);
            return client;
        }

        public void ClearClients()
        {
            // Dispose all clients before clearing
            foreach (var client in _clients.Values)
            {
                client?.Dispose();
            }

            _clients.Clear();
        }

        public bool IsClientConnected(int id)
        {
            return _clients.TryGetValue(id, out var client) && client.IsConnected;
        }

        public IReadOnlyDictionary<int, RconClient> GetAllClients()
        {
            return new Dictionary<int, RconClient>(_clients);
        }
    }
}