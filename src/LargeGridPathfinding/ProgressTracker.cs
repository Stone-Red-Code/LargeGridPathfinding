using System;
using System.Collections.Generic;

namespace LargeGridPathfinding;

internal class ProgressTracker
{
    private readonly List<ProgressData> progresses = [];

    public ProgressData AddProgress(string name, out IProgress<float> progress)
    {
        return AddProgress(name, false, false, out progress);
    }

    public ProgressData AddProgress(string name, bool indeterminate, out IProgress<float> progress)
    {
        return AddProgress(name, indeterminate, false, out progress);
    }

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

    public void RemoveProgress(ProgressData progressData)
    {
        lock (progresses)
        {
            if (!progressData.DoNotRemove)
            {
                _ = progresses.Remove(progressData);
            }
        }
    }

    public IReadOnlyList<ProgressData> GetProgresses()
    {
        return progresses;
    }

    private void Progress_ProgressChanged(ProgressData progressData, float progress)
    {
        progressData.Progress = progress;

        if (progress >= 1)
        {
            RemoveProgress(progressData);
        }
    }

    public class ProgressData(string name, bool indeterminate, bool doNotRemove)
    {
        public string Name { get; } = name;
        public float Progress { get; set; } = 0;
        public bool Indeterminate { get; } = indeterminate;
        public bool DoNotRemove { get; } = doNotRemove;
    }

    public class CustomProgress<T>() : IProgress<T>
    {
        public EventHandler<T>? ProgressChanged { get; set; }

        public void Report(T value)
        {
            ProgressChanged?.Invoke(this, value);
        }
    }
}