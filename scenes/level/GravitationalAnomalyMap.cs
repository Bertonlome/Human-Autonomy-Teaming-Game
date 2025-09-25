using Game.Manager;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class GravitationalAnomalyMap : Node
{
    private const float NoiseScale = 0.1f; // Adjust for smoothness
    private const float MinAnomaly = 0f;
    private const float MaxAnomaly = 255f;
    private List<Vector2I> allTilesBaseLayer;
    private bool fullMapDisplayed = false;
    private bool traceDisplayed = false;

    // Track which tiles have already been painted in the trace
    private HashSet<Vector2I> paintedTraceTiles = new();

    [Export]
    private GridManager gridManager;
    [Export]
    private FastNoiseLite noise;
    [Export]
    private TileMapLayer baseTerrainTilemapLayer;

    private Dictionary<Vector2I, float> anomalyMap;
    private Dictionary<Vector2I, ColorRect> traceRects = new();

    public override void _Ready()
    {
        // Initialize and generate anomalies
        allTilesBaseLayer = baseTerrainTilemapLayer.GetUsedCells().ToList();

        // Sort the list by x, then by y
        List<Vector2I> sortedList = allTilesBaseLayer
            .OrderBy(v => v.X)
            .ThenBy(v => v.Y)
            .ToList();

        // Get bounds of the tilemap
        (int xMin, int yMin, int xRange, int yRange) = AnalyzeVector2IList(sortedList);

        // Generate anomaly map and pre-create ColorRects
        anomalyMap = GenerateGravitationalAnomalyMapPerlinNoise(xMin, yMin, xRange, yRange);
        AddMonolithToAnomalyMap(gridManager.monolithPosition, anomalyMap);

        // Pre-create ColorRects for all tiles
        Vector2 size = new Vector2(64, 64);
        GD.Print("number of tiles in anomaly map: " + anomalyMap.Count);
        foreach (var kv in anomalyMap)
        {
            Vector2I cell = kv.Key;
            float scaledAnomaly = kv.Value;
            ColorRect colorRect = new ColorRect();
            colorRect.SetSize(size);
            colorRect.GlobalPosition = new Vector2(cell.X * 64, cell.Y * 64);
            Color squareColor = Color.Color8(255, 255, 255, (byte)scaledAnomaly);
            colorRect.Color = squareColor;
            colorRect.Visible = false;
            AddChild(colorRect);
            traceRects[cell] = colorRect;
        }
    }

    private (int, int, int, int) AnalyzeVector2IList(List<Vector2I> vectorList)
    {
        if (vectorList.Count == 0)
        {
            GD.PrintErr("The list is empty.");
            return (0, 0, 0, 0);
        }

        // Extract min and max
        int xMin = vectorList[0].X;
        int yMin = vectorList[0].Y;
        int xMax = vectorList[^1].X;
        int yMax = vectorList[^1].Y;

        // Calculate ranges
        int xRange = xMax - xMin + 1;
        int yRange = yMax - yMin + 1;

        return (xMin, yMin, xRange, yRange);
    }

    private Dictionary<Vector2I, float> GenerateGravitationalAnomalyMapPerlinNoise(int xMin, int yMin, int width, int height)
    {
        var noiseScale = 0.6f;
        Dictionary<Vector2I, float> map = new Dictionary<Vector2I, float>();
        noise.Seed = (int)GD.Randi();

        for (int y = yMin; y < yMin + height; y++)
        {
            for (int x = xMin; x < xMin + width; x++)
            {
                // Use noise scale for clouds
                float rawNoise = noise.GetNoise2D(x * noiseScale, y * noiseScale);

                // Wide range for pronounced clouds
                float scaledAnomaly = Mathf.Lerp(MinAnomaly - 500, MaxAnomaly + 500, (rawNoise + 1f) / 2f);

                // Clamp for display
                scaledAnomaly = Mathf.Clamp(scaledAnomaly, MinAnomaly, 100f);

                map[new Vector2I(x, y)] = scaledAnomaly;
            }
        }

        return map;
    }

    private Dictionary<Vector2I, float> GenerateGravitationalAnomalyMap(int xMin, int yMin, int width, int height)
    {
        Dictionary<Vector2I, float> map = new Dictionary<Vector2I, float>();
        Vector2I monolithPos = gridManager.monolithPosition;
        int maxDistance = 50; // or calculate based on map size
        float maxValue = 255f;
        float minValue = 0f;
        var rng = new RandomNumberGenerator();

        for (int y = yMin; y < yMin + height; y++)
        {
            for (int x = xMin; x < xMin + width; x++)
            {
                Vector2I cell = new Vector2I(x, y);
                int distance = Mathf.Abs(monolithPos.X - x) + Mathf.Abs(monolithPos.Y - y); // Manhattan distance
                float normalizedDistance = Mathf.Clamp((float)distance / maxDistance, 0f, 1f);

                // Main gradient
                float anomalyValue = Mathf.Lerp(maxValue, minValue, normalizedDistance);

                // Optional: add subtle noise
                anomalyValue += rng.RandfRange(-20f, 20f);

                anomalyValue = Mathf.Clamp(anomalyValue, minValue, maxValue);

                map[cell] = anomalyValue;
            }
        }
        return map;
    }

    public void DisplayAnomalyMap()
    {
        if (fullMapDisplayed == true)
        {
            foreach(Node child in GetChildren())
            {
                child.QueueFree();
            }
            fullMapDisplayed = false;
        }
        else
            {
            Vector2 size = new Vector2(64, 64);

            foreach (KeyValuePair<Vector2I, float> entry in anomalyMap)
            {
                Vector2I cell = entry.Key;
                float scaledAnomaly = entry.Value;
                ColorRect colorRect = new ColorRect();
                colorRect.SetSize(size);

                // Position it in world space
                colorRect.GlobalPosition = new Vector2(cell.X * 64, cell.Y * 64);

                // Set its color with opacity based on the anomaly value
                Color squareColor = Color.Color8(255, 255, 255, (byte)scaledAnomaly);
                colorRect.Color = squareColor;

                // Add it to the scene tree
                AddChild(colorRect);
            }
            fullMapDisplayed = true;
        }
    }


    public void DisplayTrace(HashSet<Vector2I> tileDiscovered)
    {
        //var stopwatch = new System.Diagnostics.Stopwatch();
        //stopwatch.Start();

        // Only update tiles that are newly discovered (not already painted)
        int newTiles = 0;
        foreach (var tile in tileDiscovered)
        {
            if (paintedTraceTiles.Contains(tile))
                continue;
            if (!traceRects.ContainsKey(tile))
                continue;
            float scaledAnomaly = anomalyMap[tile];
            var rect = traceRects[tile];
            rect.Color = Color.Color8(255, 255, 255, (byte)scaledAnomaly);
            rect.Visible = true;
            paintedTraceTiles.Add(tile);
            newTiles++;
        }
    traceDisplayed = true;
    //stopwatch.Stop();
    //GD.Print($"Trace rendering time: {stopwatch.Elapsed.TotalMilliseconds:F3} ms, new tiles: {newTiles}");
    }

    public void HideTrace()
    {
        foreach (var rect in traceRects.Values)
        {
            rect.Visible = false;
        }
        paintedTraceTiles.Clear();
        traceDisplayed = false;
    }

    public float GetAnomalyAt(int x, int y)
    {
        Vector2I cell = new Vector2I(x, y);

        if (anomalyMap.TryGetValue(cell, out float anomaly))
        {
            return anomaly;
        }

        //GD.PrintErr($"No anomaly data found for tile at ({x}, {y})");
        return 0f;
    }

    public void AddMonolithToAnomalyMap(Vector2I monolithPosition, Dictionary<Vector2I,float> anomalyMap)
    {
        int maxDistance = 70; // Maximum distance to affect tiles
        float maxValue = 255f; // Highest value near the monolith
        float minValue = 0f;   // Lowest value farthest from the monolith
        var randomAnomaly = new RandomNumberGenerator();

        foreach (var cell in anomalyMap.Keys.ToList())
        {
            int manhattanDistance = Mathf.Abs(monolithPosition.X - cell.X) + Mathf.Abs(monolithPosition.Y - cell.Y);

            // Add a ring
            if (manhattanDistance == 9 || manhattanDistance == 10 || manhattanDistance == 11)
            {
                anomalyMap[cell] += randomAnomaly.RandfRange(0, 30);
                anomalyMap[cell] = Mathf.Clamp(anomalyMap[cell], minValue, maxValue);
            }
            // Smooth gradient within maxDistance, combine with noise
            else if (manhattanDistance <= maxDistance)
            {
                float normalizedDistance = Mathf.Clamp((float)manhattanDistance / maxDistance, 0f, 1f);
                float gradientValue = Mathf.Lerp(maxValue, minValue, normalizedDistance);

                anomalyMap[cell] += gradientValue;
                anomalyMap[cell] = Mathf.Clamp(anomalyMap[cell], minValue, maxValue);
            }
            // else: leave the anomaly value as is (background noise)
        }
    }
}
