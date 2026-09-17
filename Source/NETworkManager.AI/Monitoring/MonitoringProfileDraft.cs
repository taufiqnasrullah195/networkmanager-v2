namespace NETworkManager.AI.Monitoring;

/// <summary>
///     Editable draft of a <see cref="MonitoringProfile"/> used by the WPF editor. Tracks dirty state and validates
///     without touching the persisted configuration; the UI commits via the profile service on Save.
/// </summary>
public sealed class MonitoringProfileDraft
{
    public MonitoringProfileDraft(MonitoringProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Original = profile;
        Current = profile;
    }

    /// <summary>The persisted value the draft started from.</summary>
    public MonitoringProfile Original { get; }

    /// <summary>The working copy being edited.</summary>
    public MonitoringProfile Current { get; private set; }

    /// <summary>True when the working copy differs from the persisted value (records compare by value).</summary>
    public bool IsDirty => Current != Original;

    /// <summary>Applies an edit (the caller produces an updated record via <c>with</c>).</summary>
    public void Update(MonitoringProfile updated)
    {
        ArgumentNullException.ThrowIfNull(updated);
        Current = updated;
    }

    /// <summary>Discards unsaved edits (Cancel).</summary>
    public void Discard() => Current = Original;

    /// <summary>Validates the working copy.</summary>
    public IReadOnlyList<string> Validate() => Current.Validate();
}