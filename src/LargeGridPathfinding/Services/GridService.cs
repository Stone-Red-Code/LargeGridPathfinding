using Microsoft.Xna.Framework;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace LargeGridPathfinding.Services;

public class GridService
{
    private readonly GridFiller gridFiller;
    private readonly object pendingOperationsLock = new();
    private readonly Dictionary<Point, PendingOperation> pendingOperations = [];
    private readonly HashSet<int> affectedZones = [];
    private readonly object affectedZonesLock = new();
    private bool batchProcessingScheduled;
    private bool gridChanged;

    public GridFiller Filler => gridFiller;
    public int[,] Grid => gridFiller.Grid;
    public int[,] WeightGrid => gridFiller.WeightGrid;
    public int Width => gridFiller.Width;
    public int Height => gridFiller.Height;
    public bool GridChanged
    {
        get => gridChanged;
        set => gridChanged = value;
    }

    public HashSet<int> AffectedZones => affectedZones;

    public GridService(GridFiller gridFiller)
    {
        this.gridFiller = gridFiller;
    }

    public void EnqueueOperations(IEnumerable<Point> points, PendingOperation operation)
    {
        lock (pendingOperationsLock)
        {
            foreach (Point point in points)
            {
                pendingOperations[point] = operation;
            }

            if (batchProcessingScheduled)
            {
                return;
            }

            batchProcessingScheduled = true;
        }
    }

    public bool TryDequeueBatch(out PendingOperationEntry[] batch)
    {
        lock (pendingOperationsLock)
        {
            if (pendingOperations.Count == 0)
            {
                batchProcessingScheduled = false;
                batch = [];
                return false;
            }

            batch = [.. pendingOperations.Select(kvp => new PendingOperationEntry(kvp.Key, kvp.Value))];
            pendingOperations.Clear();
            return true;
        }
    }

    public void ProcessOperationBatch(PendingOperationEntry[] batch)
    {
        IGrouping<int, Point>[] weightGroups = [.. batch
            .Where(op => op.Operation.Kind == PendingOperationKind.SetWeight)
            .GroupBy(op => op.Operation.Weight, op => op.Point)];

        Point[] resetWeightPoints = [.. batch
            .Where(op => op.Operation.Kind == PendingOperationKind.ResetWeight)
            .Select(op => op.Point)];

        Rectangle[] placeObstacleRectangles = [.. batch
            .Where(op => op.Operation.Kind == PendingOperationKind.PlaceObstacle)
            .Select(op => new Rectangle(op.Point.X, op.Point.Y, 1, 1))];

        Rectangle[] removeObstacleRectangles = [.. batch
            .Where(op => op.Operation.Kind == PendingOperationKind.RemoveObstacle)
            .Select(op => new Rectangle(op.Point.X, op.Point.Y, 1, 1))];

        lock (affectedZonesLock)
        {
            foreach (IGrouping<int, Point> weightGroup in weightGroups)
            {
                affectedZones.UnionWith(gridFiller.SetTileWeightsWithAffected(weightGroup, weightGroup.Key));
            }

            if (resetWeightPoints.Length > 0)
            {
                affectedZones.UnionWith(gridFiller.ResetTileWeightsWithAffected(resetWeightPoints));
            }

            if (placeObstacleRectangles.Length > 0)
            {
                affectedZones.UnionWith(gridFiller.PlaceObstaclesWithAffected(placeObstacleRectangles));
            }

            if (removeObstacleRectangles.Length > 0)
            {
                affectedZones.UnionWith(gridFiller.RemoveObstaclesWithAffected(removeObstacleRectangles));
            }
        }

        gridChanged = true;
    }

    public HashSet<int> TakeAffectedZones()
    {
        lock (affectedZonesLock)
        {
            if (affectedZones.Count == 0)
            {
                return [];
            }

            HashSet<int> result = [.. affectedZones];
            affectedZones.Clear();
            return result;
        }
    }

    public readonly record struct PendingOperation(PendingOperationKind Kind, int Weight);

    public readonly record struct PendingOperationEntry(Point Point, PendingOperation Operation);

    public enum PendingOperationKind
    {
        SetWeight,
        ResetWeight,
        PlaceObstacle,
        RemoveObstacle
    }
}
