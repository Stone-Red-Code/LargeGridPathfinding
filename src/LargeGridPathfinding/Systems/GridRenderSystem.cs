using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using MonoGame.Extended;
using MonoGame.Extended.ECS;
using MonoGame.Extended.ECS.Systems;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

using LargeGridPathfinding.Services;

namespace LargeGridPathfinding.Systems;

public class GridRenderSystem : IDrawSystem
{
    private const int SectorShift = 7;
    private const int SectorSize = 1 << SectorShift;

    private readonly SpriteBatch spriteBatch;
    private readonly GridService gridService;
    private readonly OrthographicCamera camera;
    private readonly ConcurrentDictionary<Vector2, Color> temporaryIndicators;
    private readonly Func<bool> getShowZones;
    private readonly Func<bool> getShowGrid;
    private readonly Func<Point?> getFillSelectionStart;
    private readonly Func<Point?> getFillSelectionCurrent;

    private static readonly Color[] ColorLookup =
    [
        Color.Blue, Color.Cyan, Color.Magenta, Color.Yellow, Color.Orange,
        Color.DarkMagenta, Color.DarkCyan, Color.Tan, Color.RosyBrown,
        Color.DarkKhaki, Color.DarkSalmon, Color.DarkSlateGray,
        Color.DarkTurquoise, Color.DarkGoldenrod, Color.Aqua, Color.Aquamarine,
        Color.Bisque, Color.DarkSlateBlue, Color.BlueViolet, Color.Brown,
        Color.BurlyWood, Color.CadetBlue, Color.Chartreuse, Color.Chocolate,
        Color.Coral, Color.CornflowerBlue, Color.Crimson, Color.DarkBlue
    ];

    public GridRenderSystem(
        SpriteBatch spriteBatch,
        GridService gridService,
        OrthographicCamera camera,
        ConcurrentDictionary<Vector2, Color> temporaryIndicators,
        Func<bool> getShowZones,
        Func<bool> getShowGrid,
        Func<Point?> getFillSelectionStart,
        Func<Point?> getFillSelectionCurrent)
    {
        this.spriteBatch = spriteBatch;
        this.gridService = gridService;
        this.camera = camera;
        this.temporaryIndicators = temporaryIndicators;
        this.getShowZones = getShowZones;
        this.getShowGrid = getShowGrid;
        this.getFillSelectionStart = getFillSelectionStart;
        this.getFillSelectionCurrent = getFillSelectionCurrent;
    }

    public void Initialize(World world) { }

    public void Dispose() { }

    public void Draw(GameTime gameTime)
    {
        Matrix transformMatrix = camera.GetViewMatrix();

        int yStart = Math.Max(0, (int)(camera.BoundingRectangle.Top / 10) - 2);
        int xStart = Math.Max(0, (int)(camera.BoundingRectangle.Left / 10) - 2);
        int yEnd = Math.Min(gridService.Height, (int)(camera.BoundingRectangle.Bottom / 10) + 2);
        int xEnd = Math.Min(gridService.Width, (int)(camera.BoundingRectangle.Right / 10) + 2);

        int[,] grid = gridService.Grid;
        Rectangle cellRect = new(0, 0, 10, 10);

        spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: SamplerState.PointClamp, transformMatrix: transformMatrix);

        spriteBatch.FillRectangle(new Rectangle(0, 0, gridService.Width * 10, gridService.Height * 10), Color.White, layerDepth: 0.4f);

        if (getShowZones())
        {
            Dictionary<int, Rectangle> rectangles = new(gridService.Filler.PlacedRectangles);

            foreach ((int label, Rectangle rectangle) in rectangles)
            {
                Color color = ColorLookup[label % 28];
                spriteBatch.FillRectangle(new Rectangle(rectangle.X * 10, rectangle.Y * 10, rectangle.Width * 10, rectangle.Height * 10), Color.Lerp(color, Color.Gray, 0.55f), layerDepth: 0.2f);
            }
        }

        for (int y = yStart; y < yEnd; y++)
        {
            for (int x = xStart; x < xEnd; x++)
            {
                cellRect.X = x * 10;
                cellRect.Y = y * 10;

                int cellValue = grid[y, x];
                int cellWeight = gridService.WeightGrid[y, x];

                if (cellWeight > 1 && cellValue >= 0)
                {
                    float blendFactor = Math.Clamp((cellWeight - 1) / 8f, 0f, 1f);
                    Color weightColor = Color.Lerp(Color.LightYellow, Color.DarkOrange, blendFactor);

                    if (getShowZones())
                    {
                        spriteBatch.DrawRectangle(cellRect, weightColor * 0.75f, layerDepth: 0.25f);
                    }
                    else
                    {
                        spriteBatch.FillRectangle(cellRect, weightColor * 0.75f, layerDepth: 0.25f);
                    }
                }

                if (cellValue < 0)
                {
                    spriteBatch.FillRectangle(cellRect, Color.Red, layerDepth: 0.3f);
                }

                if (temporaryIndicators.TryGetValue(new Vector2(x, y), out Color indicatorColor))
                {
                    spriteBatch.DrawCircle(cellRect.Center.ToVector2(), 5, 10, indicatorColor, 2f, layerDepth: 0.2f);
                }

                if (getShowGrid())
                {
                    spriteBatch.DrawRectangle(cellRect, Color.Black, layerDepth: 0.2f);
                }
            }
        }

        Point? fillSelectionStart = getFillSelectionStart();
        if (fillSelectionStart.HasValue)
        {
            Point selectionEnd = getFillSelectionCurrent() ?? fillSelectionStart.Value;
            int left = Math.Min(fillSelectionStart.Value.X, selectionEnd.X);
            int top = Math.Min(fillSelectionStart.Value.Y, selectionEnd.Y);
            int right = Math.Max(fillSelectionStart.Value.X, selectionEnd.X);
            int bottom = Math.Max(fillSelectionStart.Value.Y, selectionEnd.Y);

            Rectangle selectionRectangle = new(left * 10, top * 10, (right - left + 1) * 10, (bottom - top + 1) * 10);
            spriteBatch.FillRectangle(selectionRectangle, Color.LightBlue * 0.2f, layerDepth: 0.24f);
            spriteBatch.DrawRectangle(selectionRectangle, Color.Blue, 2f, layerDepth: 0.19f);
        }

        spriteBatch.End();
    }
}
