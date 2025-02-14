using Microsoft.Xna.Framework;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace LargeGridPathfinding;

internal class Pathfinder
{
    private readonly ConcurrentDictionary<int, Rectangle> rectanglesSource;
    private readonly bool pathRandomization;
    private readonly bool penalizeStretchedRectangles;
    private Dictionary<int, Rectangle> rectangleMap;
    private Dictionary<int, List<int>> adjacencyList;

    public Pathfinder(ConcurrentDictionary<int, Rectangle> rectangles, bool pathRandomization = false, bool penalizeStretchedRectangles = false)
    {
        rectanglesSource = rectangles;
        this.pathRandomization = pathRandomization;
        this.penalizeStretchedRectangles = penalizeStretchedRectangles;
        rectangleMap = new Dictionary<int, Rectangle>(rectangles);
        adjacencyList = BuildGraph();
    }

    public List<Vector2>? FindPath(ref int start, ref int goal)
    {
        PriorityQueue<(int node, List<int> path, int cost), int> queue = new PriorityQueue<(int node, List<int> path, int cost), int>();
        HashSet<int> visited = [];
        queue.Enqueue((start, new List<int> { start }, 0), 0);

        if (!rectangleMap.ContainsKey(start) || !rectangleMap.ContainsKey(goal))
        {
            Debug.WriteLine($"{(rectangleMap.ContainsKey(start) ? "Goal" : "Start")} rectangle not found.");
            return null;
        }

        while (queue.Count > 0)
        {
            (int node, List<int> path, int cost) = queue.Dequeue();

            if (node == goal)
            {
                // Convert rectangle path to coordinate transitions
                List<Vector2> coordinatePath = [];

                for (int i = 0; i < path.Count - 1; i++)
                {
                    (Vector2 entry, Vector2 exit) = GetTransitionPoints(path[i], path[i + 1], i + 2 < path.Count ? path[i + 2] : null);
                    coordinatePath.Add(entry);
                    coordinatePath.Add(exit);
                }

                return coordinatePath;
            }

            if (visited.Contains(node))
            {
                continue;
            }

            _ = visited.Add(node);

            foreach (int neighbor in adjacencyList.TryGetValue(node, out List<int>? neighbors) ? neighbors : [])
            {
                if (!visited.Contains(neighbor))
                {
                    List<int> newPath = new List<int>(path) { neighbor };

                    int newCost = cost + 1;

                    if (penalizeStretchedRectangles)
                    {
                        newCost += Math.Max(rectangleMap[neighbor].Width, rectangleMap[neighbor].Height) / Math.Min(rectangleMap[neighbor].Width, rectangleMap[neighbor].Height);
                    }

                    if (pathRandomization)
                    {
                        newCost += Random.Shared.Next(-1, 2);
                    }

                    queue.Enqueue((neighbor, newPath, newCost), newCost);
                }
            }
        }

        return null;
    }

    public void RebuildGraph()
    {
        rectangleMap = new Dictionary<int, Rectangle>(rectanglesSource);
        adjacencyList.Clear();
        adjacencyList = BuildGraph();
    }

    private static bool AreRectanglesAdjacent(Rectangle r1, Rectangle r2)
    {
        bool adjacentHorizontally = (r1.X + r1.Width == r2.X || r2.X + r2.Width == r1.X) && r1.Y < r2.Y + r2.Height && r1.Y + r1.Height > r2.Y;
        bool adjacentVertically = (r1.Y + r1.Height == r2.Y || r2.Y + r2.Height == r1.Y) && r1.X < r2.X + r2.Width && r1.X + r1.Width > r2.X;
        return adjacentHorizontally || adjacentVertically;
    }

    private Vector2 GetRectangleClosestEdge(Rectangle rect, Vector2 point)
    {
        if (pathRandomization)
        {
            float randomX = Random.Shared.Next(rect.Left, rect.Right);
            float randomY = Random.Shared.Next(rect.Top, rect.Bottom);
            return new Vector2(randomX, randomY);
        }

        // Clamp the point to the closest edge of the rectangle
        float closestX = Math.Clamp(point.X, rect.Left, rect.Right - 1);
        float closestY = Math.Clamp(point.Y, rect.Top, rect.Bottom - 1);

        // If point is inside the rectangle, push it to the nearest edge
        if (point.X >= rect.Left && point.X < rect.Right &&
            point.Y >= rect.Top && point.Y < rect.Bottom)
        {
            float leftDistance = Math.Abs(point.X - rect.Left);
            float rightDistance = Math.Abs(point.X - (rect.Right - 1));
            float topDistance = Math.Abs(point.Y - rect.Top);
            float bottomDistance = Math.Abs(point.Y - (rect.Bottom - 1));

            float minDistance = Math.Min(Math.Min(leftDistance, rightDistance), Math.Min(topDistance, bottomDistance));

            if (minDistance == leftDistance)
            {
                closestX = rect.Left;
            }
            else if (minDistance == rightDistance)
            {
                closestX = rect.Right - 1;
            }
            else if (minDistance == topDistance)
            {
                closestY = rect.Top;
            }
            else if (minDistance == bottomDistance)
            {
                closestY = rect.Bottom - 1;
            }
        }

        return new Vector2(closestX, closestY);
    }

    // Build a graph where each rectangle is a node and each edge represents an adjacency
    private Dictionary<int, List<int>> BuildGraph()
    {
        Dictionary<int, List<int>> graph = [];

        foreach (KeyValuePair<int, Rectangle> rect1 in rectangleMap)
        {
            if (!graph.ContainsKey(rect1.Key))
            {
                graph[rect1.Key] = [];
            }

            foreach (KeyValuePair<int, Rectangle> rect2 in rectangleMap)
            {
                if (rect1.Key != rect2.Key && AreRectanglesAdjacent(rect1.Value, rect2.Value))
                {
                    graph[rect1.Key].Add(rect2.Key);
                }
            }
        }

        return graph;
    }

    // Find the transition points between two rectangles
    private (Vector2 entry, Vector2 exit) GetTransitionPoints(int from, int to, int? next = null)
    {
        Rectangle r1 = rectangleMap[from];
        Rectangle r2 = rectangleMap[to];

        // Find all possible valid transition points
        List<(Vector2 entry, Vector2 exit)> possibleTransitions = [];

        if (r1.Right == r2.Left)
        {
            for (int y = Math.Max(r1.Top, r2.Top); y < Math.Min(r1.Bottom, r2.Bottom); y++)
            {
                possibleTransitions.Add((new Vector2(r1.Right - 1, y), new Vector2(r2.Left, y)));
            }
        }
        else if (r2.Right == r1.Left)
        {
            for (int y = Math.Max(r1.Top, r2.Top); y < Math.Min(r1.Bottom, r2.Bottom); y++)
            {
                possibleTransitions.Add((new Vector2(r1.Left, y), new Vector2(r2.Right - 1, y)));
            }
        }
        else if (r1.Bottom == r2.Top)
        {
            for (int x = Math.Max(r1.Left, r2.Left); x < Math.Min(r1.Right, r2.Right); x++)
            {
                possibleTransitions.Add((new Vector2(x, r1.Bottom - 1), new Vector2(x, r2.Top)));
            }
        }
        else if (r2.Bottom == r1.Top)
        {
            for (int x = Math.Max(r1.Left, r2.Left); x < Math.Min(r1.Right, r2.Right); x++)
            {
                possibleTransitions.Add((new Vector2(x, r1.Top), new Vector2(x, r2.Bottom - 1)));
            }
        }

        if (possibleTransitions.Count == 0)
        {
            return (new Vector2(-1, -1), new Vector2(-1, -1));
        }

        // If there's a next rectangle, optimize the transition to minimize the distance
        if (next is not null && rectangleMap.TryGetValue(next.Value, out Rectangle r3))
        {
            (Vector2 entry, Vector2 exit) bestTransition = possibleTransitions
                .OrderBy(t => Vector2.DistanceSquared(t.exit, GetRectangleClosestEdge(r3, t.exit)))
                .First();

            return bestTransition;
        }

        // Default: Pick the middle transition point
        return possibleTransitions[possibleTransitions.Count / 2];
    }
}