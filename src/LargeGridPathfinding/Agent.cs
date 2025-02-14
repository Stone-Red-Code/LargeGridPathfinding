using Microsoft.Xna.Framework;

using System;
using System.Collections.Generic;

namespace LargeGridPathfinding;

internal class Agent(Vector2 position)
{
    public Vector2 Position { get; set; } = position;

    public Point GridPosition => new Point((int)Math.Round(Position.X), (int)Math.Round(Position.Y));

    public Vector2 NextPosition { get; set; }

    public Point? Destination { get; set; }

    public List<Vector2>? Path { get; set; }
}