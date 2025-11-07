using Game.Manager;
using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

public partial class GravitationalAnomalyMap : Node
{
    private const float MinAnomaly = 0f;
    private const float MaxAnomaly = 500f;   // unify max (you clamp to 500 later)
    private const int   TilePx = 64;

    [Export] private GridManager gridManager;
    [Export] private FastNoiseLite noise;
    [Export] private TileMapLayer baseTerrainTilemapLayer;

    // Visual tuning
    [Export] public float Gamma = 0.6f;                 // <1 boosts lows
    [Export] public bool UseAdditiveBlend = true;       // makes peaks pop
    [Export] public float OverlayAlpha = 1.0f;          // master alpha

    private Dictionary<Vector2I, float> anomalyMap;
    private List<Vector2I> allTilesBaseLayer;

    // --- New: MultiMesh overlays ---
    private MultiMeshInstance2D _fullHeatMMI;  // whole-map heat layer
    private MultiMeshInstance2D _traceMMI;     // “discovered” layer
    private MultiMesh _fullHeatMM;
    private MultiMesh _traceMM;

    // Fast index for per-instance updates
    private Dictionary<Vector2I,int> _cellToIndex = new();

    // State
    private bool fullMapDisplayed = false;
    private bool traceDisplayed = false;
    private HashSet<Vector2I> paintedTraceTiles = new();
    public Vector2I MapSize => new Vector2I(baseTerrainTilemapLayer.GetUsedRect().Size.X, baseTerrainTilemapLayer.GetUsedRect().Size.Y);
    public Rect2I MapBounds => baseTerrainTilemapLayer.GetUsedRect(); // Full bounds including position

    public override void _Ready()
    {
        // 1) Tiles & bounds
        allTilesBaseLayer = baseTerrainTilemapLayer.GetUsedCells().ToList();
        var sorted = allTilesBaseLayer.OrderBy(v => v.X).ThenBy(v => v.Y).ToList();
        var (xMin, yMin, xRange, yRange) = AnalyzeVector2IList(sorted);

        // 2) Generate anomalies
        anomalyMap = GenerateGravitationalAnomalyMapPerlinNoise(xMin, yMin, xRange, yRange);
        AddMonolithToAnomalyMap(gridManager.monolithPosition, anomalyMap);

        // 3) Build overlays (but don’t add to tree yet)
        BuildFullHeatOverlay(sorted);
        BuildTraceOverlay(sorted);
    }

    // ---------- Building overlays ----------

    private void BuildFullHeatOverlay(List<Vector2I> cells)
    {
        _fullHeatMMI = new MultiMeshInstance2D();
        _fullHeatMM = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseColors = true,  // Change this line
            InstanceCount = cells.Count,
            Mesh = MakeQuadMesh(new Godot.Vector2(TilePx, TilePx))
        };

        var mat = new CanvasItemMaterial
        {
            BlendMode = UseAdditiveBlend ? CanvasItemMaterial.BlendModeEnum.Add
                                         : CanvasItemMaterial.BlendModeEnum.Mix,
            LightMode = CanvasItemMaterial.LightModeEnum.Unshaded
        };

        _fullHeatMMI.Multimesh = _fullHeatMM;
        _fullHeatMMI.Material = mat;
        _fullHeatMMI.ZIndex = 100;

        _cellToIndex.Clear();
        int i = 0;
        foreach (var cell in cells)
        {
            _cellToIndex[cell] = i;

            // Place a quad at tile center
            var pos = new Godot.Vector2(cell.X * TilePx + TilePx * 0.5f,
                                  cell.Y * TilePx + TilePx * 0.5f);
            _fullHeatMM.SetInstanceTransform2D(i, new Transform2D(0, pos));

            // Color from anomaly using gamma + heat ramp
            float raw = anomalyMap.TryGetValue(cell, out var v) ? v : 0f;
            float n = Mathf.Pow(Mathf.Clamp(raw / MaxAnomaly, 0f, 1f), Gamma);
            var c = HeatColor(n);
            c.A *= OverlayAlpha;
            _fullHeatMM.SetInstanceColor(i, c);

            i++;
        }
    }

    private void BuildTraceOverlay(List<Vector2I> cells)
    {
        _traceMMI = new MultiMeshInstance2D();
        _traceMM = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseColors = true,  // Change this line
            InstanceCount = cells.Count,
            Mesh = MakeQuadMesh(new Godot.Vector2(TilePx, TilePx))
        };

        var mat = new CanvasItemMaterial
        {
            BlendMode = UseAdditiveBlend ? CanvasItemMaterial.BlendModeEnum.Add
                                         : CanvasItemMaterial.BlendModeEnum.Mix,
            LightMode = CanvasItemMaterial.LightModeEnum.Unshaded
        };

        _traceMMI.Multimesh = _traceMM;
        _traceMMI.Material = mat;
        _traceMMI.ZIndex = 110; // draw above full heat if both shown

        int i = 0;
        foreach (var cell in cells)
        {
            var pos = new Godot.Vector2(cell.X * TilePx + TilePx * 0.5f,
                                  cell.Y * TilePx + TilePx * 0.5f);
            _traceMM.SetInstanceTransform2D(i, new Transform2D(0, pos));

            // Start hidden (alpha 0). We’ll reveal as the player “discovers”.
            _traceMM.SetInstanceColor(i, new Color(0,0,0,0));
            i++;
        }
    }

    private static Mesh MakeQuadMesh(Godot.Vector2 size)
    {
        var m = new QuadMesh { Size = size };  // Remove Offset property
        return m;
    }

    // ---------- Your existing generators (kept, minor tidy) ----------

    private (int, int, int, int) AnalyzeVector2IList(List<Vector2I> list)
    {
        if (list.Count == 0) return (0,0,0,0);
        int xMin = list[0].X;
        int yMin = list[0].Y;
        int xMax = list[^1].X;
        int yMax = list[^1].Y;
        return (xMin, yMin, xMax - xMin + 1, yMax - yMin + 1);
    }

    private Dictionary<Vector2I, float> GenerateGravitationalAnomalyMapPerlinNoise(
        int xMin, int yMin, int width, int height)
    {
        var map = new Dictionary<Vector2I, float>(width * height);
        float noiseScale = 0.6f;
        noise.Seed = (int)GD.Randi();

        for (int y = yMin; y < yMin + height; y++)
        for (int x = xMin; x < xMin + width; x++)
        {
            float rawNoise = noise.GetNoise2D(x * noiseScale, y * noiseScale); // [-1,1]
            float scaled = Mathf.Lerp(MinAnomaly - 200, MaxAnomaly -200, (rawNoise + 1f) * 0.5f);
            map[new Vector2I(x, y)] = Mathf.Clamp(scaled, MinAnomaly, MaxAnomaly);
        }
        return map;
    }

    public void AddMonolithToAnomalyMap(Vector2I monolithPosition, Dictionary<Vector2I,float> map)
    {
        int maxDistance = 150;
        float maxValue = MaxAnomaly;
        float minValue = MinAnomaly;
        var rng = new RandomNumberGenerator();

        foreach (var cell in map.Keys.ToList())
        {
            int d = Mathf.Abs(monolithPosition.X - cell.X) + Mathf.Abs(monolithPosition.Y - cell.Y);

            if (d == 7 || d == 8 || d == 9)
            {
                map[cell] = Mathf.Clamp(map[cell] + rng.RandfRange(0, 30), minValue, maxValue);
            }
            else if (d <= maxDistance)
            {
                float t = Mathf.Clamp((float)d / maxDistance, 0f, 1f);
                
                // Use power curve: smaller exponent = steeper near monolith, gentler far away
                float curve = Mathf.Pow(t, 0.5f);  // Try values between 2.0-3.0
                
                float grad = Mathf.Lerp(maxValue, minValue, curve);
                map[cell] = Mathf.Clamp(map[cell] + grad, minValue, maxValue);
            }
        }
    }

    // ---------- Public API (same names) ----------

    public void DisplayAnomalyMap()
    {
        if (fullMapDisplayed)
        {
            if (_fullHeatMMI.IsInsideTree())
            {
                RemoveChild(_fullHeatMMI);
                _fullHeatMMI.QueueFree();
            }
            fullMapDisplayed = false;
        }
        else
        {
            // Rebuild the instance if it was disposed
            if (!IsInstanceValid(_fullHeatMMI))
            {
                BuildFullHeatOverlay(allTilesBaseLayer.OrderBy(v => v.X).ThenBy(v => v.Y).ToList());
            }
            AddChild(_fullHeatMMI);
            fullMapDisplayed = true;
        }
    }

    // Now receives only newly discovered tiles (delta), not all tiles
    public void DisplayTrace(HashSet<Vector2I> newlyDiscoveredTiles)
    {
        //var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        if (!traceDisplayed)
        {
            // Rebuild if disposed
            if (!IsInstanceValid(_traceMMI))
            {
                BuildTraceOverlay(allTilesBaseLayer.OrderBy(v => v.X).ThenBy(v => v.Y).ToList());
            }
            AddChild(_traceMMI);
            traceDisplayed = true;
        }

        // OPTIMIZED: Only process new tiles (no iteration over previously discovered tiles)
        int revealed = 0;
        foreach (var cell in newlyDiscoveredTiles)
        {
            if (!_cellToIndex.TryGetValue(cell, out int idx)) continue;

            float raw = anomalyMap.TryGetValue(cell, out var v) ? v : 0f;
            float n = Mathf.Pow(Mathf.Clamp(raw / MaxAnomaly, 0f, 1f), Gamma);
            var c = HeatColor(n);
            c.A *= OverlayAlpha;

            _traceMM.SetInstanceColor(idx, c);
            paintedTraceTiles.Add(cell); // Track that we've painted this
            revealed++;
        }
        
        //stopwatch.Stop();
        
        // Performance metrics
        //GD.Print($"DisplayTrace: Processed {newlyDiscoveredTiles.Count} input tiles, revealed {revealed} new tiles, total painted: {paintedTraceTiles.Count}, Time: {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
    }

    public void HideTrace()
    {
        if (_traceMMI.IsInsideTree())
        {
            RemoveChild(_traceMMI);
            _traceMMI.QueueFree();
        }
        traceDisplayed = false;
        paintedTraceTiles.Clear();

        // Rebuild empty trace (so we can show it again later without realloc)
        BuildTraceOverlay(allTilesBaseLayer.OrderBy(v=>v.X).ThenBy(v=>v.Y).ToList());
    }

    public float GetAnomalyAt(int x, int y)
    {
        var cell = new Vector2I(x, y);
        return anomalyMap.TryGetValue(cell, out float a) ? a : 0f;
    }

    // ---------- Color mapping ----------

    private Color HeatColor(float t)
    {
        // 0..1 → blue→cyan→yellow→red (high contrast)
        t = Mathf.Clamp(t, 0f, 1f);
        if (t < 0.33f)
        {
            float k = t / 0.33f;
            return new Color(0f, 0.2f + 0.8f * k, 1f, 1f);
        }
        if (t < 0.66f)
        {
            float k = (t - 0.33f) / 0.33f;
            return new Color(k, 1f, 1f - 0.5f * k, 1f);
        }
        {
            float k = (t - 0.66f) / 0.34f;
            return new Color(1f, 1f - 0.7f * k, 0.5f - 0.5f * k, 1f);
        }
    }
}
