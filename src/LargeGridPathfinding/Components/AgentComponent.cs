using Microsoft.Xna.Framework;

using System.Collections.Generic;

namespace LargeGridPathfinding.Components;

public class AgentComponent
{
    public Vector2 Position { get; set; }
    public Vector2 NextPosition { get; set; }
    public Point? Destination { get; set; }
    public List<Vector2>? Path { get; set; }
    public int PathIndex { get; set; }
    public Point GridPosition => new((int)Position.X, (int)Position.Y);
}
