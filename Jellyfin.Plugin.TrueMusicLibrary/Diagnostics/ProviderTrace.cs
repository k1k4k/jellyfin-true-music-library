using System.Collections.Concurrent;

namespace Jellyfin.Plugin.TrueMusicLibrary.Diagnostics;

/// <summary>Une trace d'appel d'un fournisseur (diagnostic).</summary>
public sealed record ProviderTraceEntry(DateTime Time, long Sequence, string Source, string Event, Guid ItemId, string Item, string Detail);

/// <summary>
/// Journal en mémoire des appels des fournisseurs, lu via <c>GET /TrueMusicLibrary/Trace</c> pour vérifier qu'ils passent
/// et ce qu'ils voient. Borné : les entrées les plus anciennes sont jetées.
/// </summary>
public class ProviderTrace
{
    private const int Capacity = 5000;

    private readonly ConcurrentQueue<ProviderTraceEntry> _entries = new();
    private long _sequence;

    public void Add(string source, string evt, Guid itemId, string? item, string detail)
    {
        _entries.Enqueue(new ProviderTraceEntry(DateTime.UtcNow, Interlocked.Increment(ref _sequence), source, evt, itemId, item ?? string.Empty, detail));
        while (_entries.Count > Capacity && _entries.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<ProviderTraceEntry> Snapshot() => _entries.ToArray();

    public void Clear() => _entries.Clear();
}
