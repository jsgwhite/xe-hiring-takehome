using RateAlerts.Api.Models;

namespace RateAlerts.Api.Services;

/// <summary>
/// In-memory storage for user alerts. No persistence, no authentication, and backed by a single
/// instance shared across all requests—suitable only for demo and test environments.
/// </summary>
public interface IAlertStore
{
    /// <summary>
    /// Returns a snapshot of all stored alerts, ordered by creation time ascending.
    /// </summary>
    IReadOnlyCollection<Alert> GetAll();

    /// <summary>
    /// Stores an alert and returns it.
    /// </summary>
    Alert Add(Alert alert);

    /// <summary>
    /// Removes an alert by id. Returns true if an alert with that id was stored and removed;
    /// false if the id was not present.
    /// </summary>
    bool Remove(Guid id);
}
