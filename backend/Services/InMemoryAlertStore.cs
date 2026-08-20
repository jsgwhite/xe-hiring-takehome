using System.Collections.Concurrent;
using RateAlerts.Api.Models;

namespace RateAlerts.Api.Services;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IAlertStore"/> backed by a
/// <see cref="ConcurrentDictionary{TKey, TValue}"/>.
/// </summary>
public sealed class InMemoryAlertStore : IAlertStore
{
    private readonly ConcurrentDictionary<Guid, Alert> _alerts = new();

    public IReadOnlyCollection<Alert> GetAll()
        => _alerts.Values.OrderBy(a => a.CreatedAt).ToList();

    public Alert Add(Alert alert)
    {
        _alerts[alert.Id] = alert;
        return alert;
    }

    public bool Remove(Guid id)
        => _alerts.TryRemove(id, out _);
}
