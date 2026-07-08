using Microsoft.Xna.Framework;

using MonoGame.Extended;
using MonoGame.Extended.ECS;
using MonoGame.Extended.ECS.Systems;

using System;
using System.Collections.Generic;

using LargeGridPathfinding.Components;
using LargeGridPathfinding.Services;

namespace LargeGridPathfinding.Systems;

public class AgentMovementSystem : EntityUpdateSystem
{
    private const int SectorShift = 7;
    private const int SectorSize = 1 << SectorShift;

    private readonly GridService gridService;
    private readonly OrthographicCamera camera;
    private readonly List<AgentComponent> agentList;
    private ComponentMapper<AgentComponent> agentMapper = null!;
    private int sectorsX;
    private int sectorsY;
    private List<AgentComponent>[,] agentSectors = null!;
    private int frameCount;

    public int SectorsX => sectorsX;
    public int SectorsY => sectorsY;
    public List<AgentComponent>[,] AgentSectors => agentSectors;

    public AgentMovementSystem(GridService gridService, OrthographicCamera camera, List<AgentComponent> agentList)
        : base(Aspect.All(typeof(AgentComponent)))
    {
        this.gridService = gridService;
        this.camera = camera;
        this.agentList = agentList;
    }

    public override void Initialize(IComponentMapperService mapperService)
    {
        agentMapper = mapperService.GetMapper<AgentComponent>();
        RebuildSectors();
    }

    public override void Update(GameTime gameTime)
    {
        int[,] grid = gridService.Grid;
        float dt = gameTime.GetElapsedSeconds();
        float step = 10f * dt;
        float sqrStep = step * step;

        frameCount++;

        if ((frameCount & 127) == 0)
        {
            RebuildSectors();
        }

        RectangleF viewBounds = camera.BoundingRectangle;
        float margin = 5f;
        float cellViewLeft = (viewBounds.Left / 10f) - margin;
        float cellViewRight = (viewBounds.Right / 10f) + margin;
        float cellViewTop = (viewBounds.Top / 10f) - margin;
        float cellViewBottom = (viewBounds.Bottom / 10f) + margin;

        int minSX = Math.Max(0, (int)(cellViewLeft / SectorSize) - 1);
        int maxSX = Math.Min(sectorsX - 1, (int)(cellViewRight / SectorSize) + 1);
        int minSY = Math.Max(0, (int)(cellViewTop / SectorSize) - 1);
        int maxSY = Math.Min(sectorsY - 1, (int)(cellViewBottom / SectorSize) + 1);

        for (int sx = minSX; sx <= maxSX; sx++)
        {
            for (int sy = minSY; sy <= maxSY; sy++)
            {
                List<AgentComponent>? sector = agentSectors[sx, sy];
                if (sector is null)
                {
                    continue;
                }

                foreach (AgentComponent agent in sector)
                {
                    List<Vector2>? path = agent.Path;
                    if (path is null || agent.PathIndex >= path.Count)
                    {
                        continue;
                    }

                    Vector2 currentPos = agent.Position;
                    bool inView = currentPos.X >= cellViewLeft && currentPos.X <= cellViewRight
                               && currentPos.Y >= cellViewTop && currentPos.Y <= cellViewBottom;

                    Vector2 targetPos = agent.NextPosition;
                    float dx = targetPos.X - currentPos.X;
                    float dy = targetPos.Y - currentPos.Y;
                    float sqrDist = (dx * dx) + (dy * dy);

                    if (sqrDist < 0.01f)
                    {
                        if (agent.PathIndex == path.Count - 1)
                        {
                            agent.Path = null;
                            agent.Destination = null;
                        }
                        else
                        {
                            agent.NextPosition = path[++agent.PathIndex];
                        }
                    }
                    else
                    {
                        Vector2 nextPosition;
                        if (sqrDist <= sqrStep)
                        {
                            nextPosition = targetPos;
                        }
                        else
                        {
                            float t = step / MathF.Sqrt(sqrDist);
                            nextPosition = new Vector2(currentPos.X + (dx * t), currentPos.Y + (dy * t));
                        }

                        agent.Position = nextPosition;

                        if (inView)
                        {
                            int newX = (int)(nextPosition.X + 0.5f);
                            int newY = (int)(nextPosition.Y + 0.5f);

                            if (grid[newY, newX] < 0)
                            {
                                agent.Position = currentPos;
                                agent.Path = null;
                            }
                        }
                    }
                }
            }
        }

        int totalAgents = agentList.Count;
        int offScreenBudget = Math.Max(1000, totalAgents / 1000);
        float catchUpStep = step * (totalAgents / (float)offScreenBudget);
        int startIndex = frameCount * offScreenBudget % totalAgents;

        for (int i = 0; i < offScreenBudget; i++)
        {
            AgentComponent a = agentList[(startIndex + i) % totalAgents];

            List<Vector2>? p = a.Path;
            if (p is null || a.PathIndex >= p.Count)
            {
                continue;
            }

            Vector2 pos = a.Position;
            if (pos.X >= cellViewLeft && pos.X <= cellViewRight
             && pos.Y >= cellViewTop && pos.Y <= cellViewBottom)
            {
                continue;
            }

            float remaining = catchUpStep;

            while (remaining > 0.01f)
            {
                Vector2 currentPos = a.Position;
                Vector2 targetPos = a.NextPosition;
                float ddx = targetPos.X - currentPos.X;
                float ddy = targetPos.Y - currentPos.Y;
                float sqrDist = (ddx * ddx) + (ddy * ddy);

                if (sqrDist < 0.01f)
                {
                    if (a.PathIndex == p.Count - 1)
                    {
                        a.Path = null;
                        a.Destination = null;
                        break;
                    }

                    a.NextPosition = p[++a.PathIndex];
                    continue;
                }

                if (sqrDist <= remaining * remaining)
                {
                    a.Position = targetPos;
                    remaining -= MathF.Sqrt(sqrDist);

                    if (a.PathIndex == p.Count - 1)
                    {
                        a.Path = null;
                        a.Destination = null;
                        break;
                    }

                    a.NextPosition = p[++a.PathIndex];
                }
                else
                {
                    float t = remaining / MathF.Sqrt(sqrDist);
                    a.Position = new Vector2(currentPos.X + (ddx * t), currentPos.Y + (ddy * t));
                    break;
                }
            }
        }
    }

    public void RebuildSectors()
    {
        int sx = (gridService.Width + SectorSize - 1) / SectorSize;
        int sy = (gridService.Height + SectorSize - 1) / SectorSize;

        List<AgentComponent>[,] newSectors = new List<AgentComponent>[sx, sy];

        foreach (AgentComponent agent in agentList)
        {
            int x = Math.Clamp((int)(agent.Position.X / SectorSize), 0, sx - 1);
            int y = Math.Clamp((int)(agent.Position.Y / SectorSize), 0, sy - 1);
            (newSectors[x, y] ??= []).Add(agent);
        }

        sectorsX = sx;
        sectorsY = sy;
        agentSectors = newSectors;
    }
}
