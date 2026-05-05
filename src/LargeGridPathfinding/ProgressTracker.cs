using System;
using System.Collections.Generic;

namespace LargeGridPathfinding;

/// <summary>
/// Tracks named progress entries for long-running operations.
/// </summary>
internal class ProgressTracker
{
    private readonly List<ProgressData> progresses = [];

    /// <summary>
    /// Adds a determinate progress entry.
    /// </summary>
    public ProgressData AddProgress(string name, out IProgress<float> progress)
    {
        return AddProgress(name, false, false, out progress);
    }

    /// <summary>
    /// Adds an optionally indeterminate progress entry.
    /// </summary>
    public ProgressData AddProgress(string name, bool indeterminate, out IProgress<float> progress)
    {
        return AddProgress(name, indeterminate, false, out progress);
    }

    /// <summary>
    /// Adds a progress entry with full control over removal behavior.
    /// </summary>
    public ProgressData AddProgress(string name, bool indeterminate, bool disableAutoRemove, out IProgress<float> progress)
    {
        CustomProgress<float> newProgress = new CustomProgress<float>();
        ProgressData progressData = new ProgressData(name, indeterminate, disableAutoRemove);

        newProgress.ProgressChanged += (_, progress) => Progress_ProgressChanged(progressData, progress);

        lock (progresses)
        {
            progresses.Add(progressData);
        }

        progress = newProgress;
        return progressData;
    }

    /// <summary>
    /// Removes a previously added progress entry.
    /// </summary>
    public void RemoveProgress(ProgressData progressData)
    {
        lock (progresses)
        {
            _ = progresses.Remove(progressData);
        }
    }

    /// <summary>
    /// Returns active progress entries.
    /// </summary>
    public IReadOnlyList<ProgressData> GetProgresses()
    {
        return progresses;
    }

    private void Progress_ProgressChanged(ProgressData progressData, float progress)
    {
        progressData.Progress = progress;

        // Auto-removal keeps the HUD uncluttered once tasks finish.
        if (progress >= 1 && !progressData.DoNotRemove)
        {
            RemoveProgress(progressData);
        }
    }

    /// <summary>
    /// Immutable metadata with mutable progress value for one tracked task.
    /// </summary>
    public class ProgressData(string name, bool indeterminate, bool doNotRemove)
    {
        public string Name { get; } = name;
        public float Progress { get; set; } = 0;
        public bool Indeterminate { get; } = indeterminate;
        public bool DoNotRemove { get; } = doNotRemove;
    }

    /// <summary>
    /// Minimal IProgress implementation exposing ProgressChanged events.
    /// </summary>
    public class CustomProgress<T>() : IProgress<T>
    {
        public EventHandler<T>? ProgressChanged { get; set; }

        public void Report(T value)
        {
            ProgressChanged?.Invoke(this, value);
        }
    }
}