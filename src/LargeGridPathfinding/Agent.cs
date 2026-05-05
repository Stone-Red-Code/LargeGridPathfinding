using Microsoft.Xna.Framework;

using System;
using System.Collections.Generic;

namespace LargeGridPathfinding;

/// <summary>
/// Represents a moving entity that follows a precomputed grid path.
/// </summary>
internal class Agent(Vector2 position)
{
    /// <summary>
    /// Gets or sets the current world-grid position as floating point coordinates.
    /// </summary>
    public Vector2 Position { get; set; } = position;

    /// <summary>
    /// Gets the integer grid cell for the current position.
    /// </summary>
    public Point GridPosition => new Point((int)Math.Round(Position.X), (int)Math.Round(Position.Y));

    /// <summary>
    /// Gets or sets the next waypoint currently being approached.
    /// </summary>
    public Vector2 NextPosition { get; set; }

    /// <summary>
    /// Gets or sets the current target destination.
    /// </summary>
    public Point? Destination { get; set; }

    /// <summary>
    /// Gets or sets the active path.
    /// </summary>
    public List<Vector2>? Path { get; set; }
}