using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using MonoGame.Extended;
using MonoGame.Extended.ECS;
using MonoGame.Extended.ECS.Systems;

using System;
using System.Collections.Generic;

using LargeGridPathfinding.Components;

namespace LargeGridPathfinding.Systems;

public class PathRenderSystem : EntityDrawSystem
{
    private const int SectorSize = 1 << 7;

    private readonly SpriteBatch spriteBatch;
    private readonly OrthographicCamera camera;
    private readonly Func<bool> getShowPaths;
    private readonly Func<int> getSectorsX;
    private readonly Func<int> getSectorsY;
    private readonly Func<List<AgentComponent>[,]> getAgentSectors;
    private ComponentMapper<AgentComponent> agentMapper = null!;

    public PathRenderSystem(
        SpriteBatch spriteBatch,
        OrthographicCamera camera,
        Func<bool> getShowPaths,
        Func<int> getSectorsX,
        Func<int> getSectorsY,
        Func<List<AgentComponent>[,]> getAgentSectors)
        : base(Aspect.All(typeof(AgentComponent)))
    {
        this.spriteBatch = spriteBatch;
        this.camera = camera;
        this.getShowPaths = getShowPaths;
        this.getSectorsX = getSectorsX;
        this.getSectorsY = getSectorsY;
        this.getAgentSectors = getAgentSectors;
    }

    public override void Initialize(IComponentMapperService mapperService)
    {
        agentMapper = mapperService.GetMapper<AgentComponent>();
    }

    public override void Draw(GameTime gameTime)
    {
        if (!getShowPaths())
        {
            return;
        }

        Matrix transformMatrix = camera.GetViewMatrix();
        spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: SamplerState.PointClamp, transformMatrix: transformMatrix);

        float viewLeft = camera.BoundingRectangle.Left - 10;
        float viewRight = camera.BoundingRectangle.Right + 10;
        float viewTop = camera.BoundingRectangle.Top - 10;
        float viewBottom = camera.BoundingRectangle.Bottom + 10;

        int sectorsX = getSectorsX();
        int sectorsY = getSectorsY();
        var agentSectors = getAgentSectors();

        int pathMinSX = Math.Max(0, (int)(viewLeft / 10f / SectorSize) - 1);
        int pathMaxSX = Math.Min(sectorsX - 1, (int)(viewRight / 10f / SectorSize) + 1);
        int pathMinSY = Math.Max(0, (int)(viewTop / 10f / SectorSize) - 1);
        int pathMaxSY = Math.Min(sectorsY - 1, (int)(viewBottom / 10f / SectorSize) + 1);

        for (int sx = pathMinSX; sx <= pathMaxSX; sx++)
        {
            for (int sy = pathMinSY; sy <= pathMaxSY; sy++)
            {
                List<AgentComponent>? sector = agentSectors[sx, sy];
                if (sector is null)
                {
                    continue;
                }

                foreach (AgentComponent agent in sector)
                {
                    List<Vector2>? path = agent.Path;
                    if (path is null || path.Count < 2)
                    {
                        continue;
                    }

                    for (int i = 0; i < path.Count - 1; i++)
                    {
                        Vector2 start = (path[i] * 10) + new Vector2(5, 5);
                        Vector2 end = (path[i + 1] * 10) + new Vector2(5, 5);

                        bool startInView = start.X >= viewLeft && start.X <= viewRight
                                        && start.Y >= viewTop && start.Y <= viewBottom;
                        bool endInView = end.X >= viewLeft && end.X <= viewRight
                                      && end.Y >= viewTop && end.Y <= viewBottom;

                        bool segmentVisible = !((start.X < viewLeft && end.X < viewLeft) ||
                                                (start.X > viewRight && end.X > viewRight) ||
                                                (start.Y < viewTop && end.Y < viewTop) ||
                                                (start.Y > viewBottom && end.Y > viewBottom));

                        if (segmentVisible)
                        {
                            spriteBatch.DrawLine(start, end, Color.Gray, 2f, layerDepth: 0.1f);
                        }

                        if (startInView)
                        {
                            spriteBatch.DrawCircle(start, 5, 10, i == 0 ? Color.Green : Color.Gray, 2f, layerDepth: 0.1f);
                        }

                        if (i == path.Count - 2 && endInView)
                        {
                            spriteBatch.DrawCircle(end, 5, 10, Color.Red, 2f, layerDepth: 0.1f);
                        }
                    }
                }
            }
        }

        spriteBatch.End();
    }
}
