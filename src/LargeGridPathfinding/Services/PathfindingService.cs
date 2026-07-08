using Microsoft.Xna.Framework;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using LargeGridPathfinding.Components;

namespace LargeGridPathfinding.Services;

public class PathfindingService
{
    private readonly Pathfinder pathfinder;
    private readonly GridService gridService;
    private readonly List<AgentComponent> agents;
    private readonly ConcurrentQueue<(AgentComponent agent, List<Vector2>? path)> pendingPathResults = new();
    private readonly ProgressTracker progressTracker;
    private CancellationTokenSource? cts;
    private Task? workerTask;

    public ConcurrentQueue<(AgentComponent agent, List<Vector2>? path)> PendingPathResults => pendingPathResults;
    public Pathfinder Pathfinder => pathfinder;

    public PathfindingService(Pathfinder pathfinder, GridService gridService, List<AgentComponent> agents, ProgressTracker progressTracker)
    {
        this.pathfinder = pathfinder;
        this.gridService = gridService;
        this.agents = agents;
        this.progressTracker = progressTracker;
    }

    public void Start()
    {
        cts = new CancellationTokenSource();
        workerTask = Task.Factory.StartNew(() => WorkerLoop(cts.Token), TaskCreationOptions.LongRunning);
    }

    public void Stop()
    {
        cts?.Cancel();
    }

    public List<Vector2>? CalculatePath(Point? start = null, Point? goal = null, int maxNodes = int.MaxValue)
    {
        int width = gridService.Width;
        int height = gridService.Height;
        int[,] g = gridService.Grid;

        if (start is null)
        {
            start = RandomPoint(width, height, g);
        }

        if (goal is null)
        {
            goal = RandomPoint(width, height, g);
        }

        return pathfinder.FindPath(start.Value, goal.Value, maxNodes);
    }

    private static Point RandomPoint(int width, int height, int[,] grid)
    {
        Point p;
        do
        {
            p = new Point(Random.Shared.Next(0, width - 1), Random.Shared.Next(0, height - 1));
        }
        while (grid[p.Y, p.X] <= 0);
        return p;
    }

    private void WorkerLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                HashSet<int> localAffectedZones = gridService.TakeAffectedZones();

                if (localAffectedZones.Count > 0)
                {
                    gridService.GridChanged = false;
                    ProgressTracker.ProgressData progressDataUpdateGraph = progressTracker.AddProgress("Updating graph", true, out _);
                    pathfinder.IncrementalUpdateGraph(localAffectedZones);
                    progressTracker.RemoveProgress(progressDataUpdateGraph);
                }
                else if (gridService.GridChanged)
                {
                    gridService.GridChanged = false;
                    ProgressTracker.ProgressData progressDataBuildGraph = progressTracker.AddProgress("Building graph", true, out _);
                    pathfinder.BuildGraph();
                    progressTracker.RemoveProgress(progressDataBuildGraph);
                }

                List<AgentComponent> agentsRequirePathList = new(agents.Count);

                foreach (AgentComponent agent in agents)
                {
                    if (agent.Path is null)
                    {
                        agentsRequirePathList.Add(agent);
                    }
                }

                if (agentsRequirePathList.Count != 0)
                {
                    int agentCount = agentsRequirePathList.Count;
                    int batchSize = Math.Max(64, agentCount / (Environment.ProcessorCount * 4));

                    ProgressTracker.ProgressData progressDataPaths = progressTracker.AddProgress($"Calculating {agentCount} paths", out IProgress<float> progress);

                    ParallelOptions parallelOptions = new()
                    {
                        MaxDegreeOfParallelism = Environment.ProcessorCount
                    };

                    long lastProgressReport = Environment.TickCount64;
                    int pathsCalculated = 0;

                    _ = Parallel.ForEach(Partitioner.Create(0, agentCount, batchSize), parallelOptions, range =>
                    {
                        for (int i = range.Item1; i < range.Item2; i++)
                        {
                            AgentComponent agent = agentsRequirePathList[i];
                            List<Vector2>? path = CalculatePath(agent.GridPosition, agent.Destination, maxNodes: int.MaxValue) ?? CalculatePath(maxNodes: int.MaxValue);
                            pendingPathResults.Enqueue((agent, path));
                        }

                        int local = Interlocked.Add(ref pathsCalculated, range.Item2 - range.Item1);
                        long now = Environment.TickCount64;
                        if (now - lastProgressReport > 100)
                        {
                            progress.Report((float)local / agentCount);
                            _ = Interlocked.Exchange(ref lastProgressReport, now);
                        }
                    });

                    progress.Report(1.0f);
                    progressTracker.RemoveProgress(progressDataPaths);
                }

                if (agentsRequirePathList.Count < 1000)
                {
                    Task.Delay(100).Wait();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
            }
        }
    }
}
