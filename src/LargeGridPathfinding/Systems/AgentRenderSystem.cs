using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using MonoGame.Extended;
using MonoGame.Extended.ECS;
using MonoGame.Extended.ECS.Systems;

using System;
using System.Collections.Generic;

using LargeGridPathfinding.Components;

namespace LargeGridPathfinding.Systems;

public class AgentRenderSystem : EntityDrawSystem
{
    private const int SectorSize = 1 << 7;

    private readonly SpriteBatch spriteBatch;
    private readonly OrthographicCamera camera;
    private readonly Func<int> getSectorsX;
    private readonly Func<int> getSectorsY;
    private readonly Func<List<AgentComponent>[,]> getAgentSectors;
    private ComponentMapper<AgentComponent> agentMapper = null!;

    public AgentRenderSystem(
        SpriteBatch spriteBatch,
        OrthographicCamera camera,
        Func<int> getSectorsX,
        Func<int> getSectorsY,
        Func<List<AgentComponent>[,]> getAgentSectors)
        : base(Aspect.All(typeof(AgentComponent)))
    {
        this.spriteBatch = spriteBatch;
        this.camera = camera;
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
        Matrix transformMatrix = camera.GetViewMatrix();

        int yStart = Math.Max(0, (int)(camera.BoundingRectangle.Top / 10) - 2);
        int xStart = Math.Max(0, (int)(camera.BoundingRectangle.Left / 10) - 2);
        int yEnd = Math.Min((int)(camera.BoundingRectangle.Bottom / 10) + 2, 1000000);
        int xEnd = Math.Min((int)(camera.BoundingRectangle.Right / 10) + 2, 1000000);

        int sectorsX = getSectorsX();
        int sectorsY = getSectorsY();
        var agentSectors = getAgentSectors();

        spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: SamplerState.PointClamp, transformMatrix: transformMatrix);

        int drawMinSX = Math.Max(0, (xStart / SectorSize) - 1);
        int drawMaxSX = Math.Min(sectorsX - 1, (xEnd / SectorSize) + 1);
        int drawMinSY = Math.Max(0, (yStart / SectorSize) - 1);
        int drawMaxSY = Math.Min(sectorsY - 1, (yEnd / SectorSize) + 1);

        for (int sx = drawMinSX; sx <= drawMaxSX; sx++)
        {
            for (int sy = drawMinSY; sy <= drawMaxSY; sy++)
            {
                List<AgentComponent>? sector = agentSectors[sx, sy];
                if (sector is null)
                {
                    continue;
                }

                foreach (AgentComponent agent in sector)
                {
                    Vector2 pos = agent.Position;
                    if (pos.X >= xStart && pos.X <= xEnd && pos.Y >= yStart && pos.Y <= yEnd)
                    {
                        spriteBatch.DrawCircle((pos + new Vector2(0.5f, 0.5f)) * 10, 5, 10, Color.Blue, 2f, layerDepth: 0.1f);
                    }
                }
            }
        }

        spriteBatch.End();
    }
}
