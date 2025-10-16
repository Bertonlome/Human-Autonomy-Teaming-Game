using Godot;
using System;
using System.Collections.Generic;

public partial class AnomalyMiniMap : Node3D
{
    // === Exports ===
    [Export] public MultiMeshInstance3D Bars;        // assign in inspector
    [Export] public MeshInstance3D RobotMarker;      // assign in inspector
    [Export] public Camera3D Cam;                    // assign in inspector
    [Export] public DirectionalLight3D Light;        // assign in inspector (optional)
    [Export] public int GridW = 32;                  // Display resolution (reduce for robot window mode)
    [Export] public int GridH = 32;                  // Display resolution (reduce for robot window mode)
    [Export] public float CellSize = 0.15f;          // spacing in minimap world units (wider bars)
    [Export] public float HeightScale = 0.01f;       // height multiplier for better visibility
    [Export] public float MaxValue = 500f;
    [Export] public float Gamma = 0.6f;
    [Export] public bool UsePerspective = true;      // false = ortho

    private MultiMesh _mm;

    // Cached “view window” in map tile coords
    private Rect2I _window;            // which map area is shown
    private Vector2I _mapSize;         // full map width/height (tiles)
    private Vector2I _robotCell;       // robot tile for marker
    private float[,] _grid;            // GridW×GridH downsample
    private bool _initialized = false;
    private Mode _currentMode = Mode.FullMap; // Track current mode
    
    // Change detection
    private float[,] _lastGrid;        // Previous grid state for comparison
    private int _refreshCount = 0;     // Track number of refreshes

    public override void _Ready()
    {
        // Don't initialize here if we're going to set custom grid size
        // The Initialize method will call SetupMultiMesh
        // But if no one calls InitFullMap, we should still initialize with defaults
    }
    
    public void SetupMultiMesh()
    {
        // Allow re-initialization to recreate MultiMesh with new grid size
        if (_initialized)
        {
            // Clean up old MultiMesh
            if (_mm != null)
            {
                _mm = null;
            }
        }
        
        //GD.Print($"Setting up MultiMesh with {GridW}x{GridH} = {GridW * GridH} instances");
        
        // Setup MultiMesh with a box/cube - bars are thicker now (95% of cell size)
        _mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            InstanceCount = GridW * GridH,
            Mesh = new BoxMesh { Size = new Vector3(CellSize * 0.95f, 1f, CellSize * 0.95f) } // Y=1; we'll scale
        };
        
        // Create a material with proper shading for 3D depth
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
            VertexColorUseAsAlbedo = true, // Use the colors we set
            Metallic = 0.0f,
            Roughness = 0.7f
        };
        
        Bars.Multimesh = _mm;
        Bars.MaterialOverride = material;
        
        // Add a light if not provided in the scene
        if (Light == null)
        {
            Light = new DirectionalLight3D();
            AddChild(Light);
            Light.GlobalPosition = new Vector3(5, 10, 5);
            Light.LookAt(Vector3.Zero, Vector3.Up);
            Light.LightEnergy = 1.0f;
        }

        ConfigureCamera();
        _grid = new float[GridW, GridH];
        // pre-place instances (XZ positions) once
        PreplaceInstances();
        
        _initialized = true;
    }

    private void ConfigureCamera()
    {
        // === Camera Tweaking Guide ===
        // Position: Cam.GlobalPosition = new Vector3(X, Y, Z)
        //   - X: left(-) to right(+) - adjust horizontal viewing angle
        //   - Y: height - higher values = bird's eye view, lower = side view
        //   - Z: distance from center - larger = further back
        // Orientation: Cam.LookAt(target, up_vector)
        //   - target: point to look at (usually center of histogram)
        //   - up_vector: usually Vector3.Up for standard orientation
        // Zoom (Perspective): Cam.Fov (field of view)
        //   - Lower FOV (e.g., 20-30) = zoomed in, narrow view
        //   - Higher FOV (e.g., 50-70) = zoomed out, wide angle
        // Zoom (Orthographic): Cam.Size
        //   - Smaller = zoomed in, larger = zoomed out
        
        if (UsePerspective)
        {
            Cam.Projection = Camera3D.ProjectionType.Perspective;
            // Place camera at an angle to better see height variations
            var extentX = GridW * CellSize * 0.5f;
            var extentZ = GridH * CellSize * 0.5f;
            var maxExtent = MathF.Max(extentX, extentZ);
            GlobalPosition = Vector3.Zero;
            
            // Pull camera back further to see the whole scene
            // Position at a good angle with more distance
            Cam.GlobalPosition = new Vector3(maxExtent * 2.5f, maxExtent * 3.0f, maxExtent * 4.0f);
            Cam.LookAt(new Vector3(-0.25f, 0.85f, 0), Vector3.Up); // Look at slightly elevated center
            Cam.Fov = 35.0f; // Wider field of view
        }
        else
        {
            Cam.Projection = Camera3D.ProjectionType.Orthogonal;
            var half = MathF.Max(GridW, GridH) * CellSize * 0.55f;
            Cam.Size = half * 2.0f;
            Cam.GlobalPosition = new Vector3(0, 10f, 0);
            Cam.LookAt(Vector3.Zero, Vector3.Back); // top-down
        }
    }

    private void PreplaceInstances()
    {
        int idx = 0;
        for (int gy = 0; gy < GridH; gy++)
        for (int gx = 0; gx < GridW; gx++, idx++)
        {
            float x = (gx - GridW * 0.5f + 0.5f) * CellSize;
            float z = (gy - GridH * 0.5f + 0.5f) * CellSize;
            // basis with unit height; we'll overwrite Y scale each update
            var basis = new Basis(
                new Vector3(CellSize * 0.95f, 0, 0),
                new Vector3(0, 1f, 0),
                new Vector3(0, 0, CellSize * 0.95f)
            );
            var xform = new Transform3D(basis, new Vector3(x, 0.5f, z)); // temp Y=0.5
            _mm.SetInstanceTransform(idx, xform);
            _mm.SetInstanceColor(idx, new Color(0, 0, 0, 0)); // init invisible
        }
    }

    // === Public API ===
    public void InitFullMap(Vector2I mapSize)
    {
        _mapSize = mapSize;
        _window = new Rect2I(Vector2I.Zero, mapSize);
        
        // Initialize the MultiMesh now that grid size is set
        SetupMultiMesh();
        
        // Initialize change detection grid
        _lastGrid = new float[GridW, GridH];
        
        UpdateRobotMarker(); // place off-grid initially
    }

    public void SetRobotCell(Vector2I robotCell)
    {
        _robotCell = robotCell;
        UpdateRobotMarker();
    }

    public enum Mode { FullMap, RobotWindow }

    public void SetMode(Mode mode, Vector2I windowSizeTiles)
    {
        _currentMode = mode; // Track current mode
        
        if (mode == Mode.FullMap)
        {
            _window = new Rect2I(Vector2I.Zero, _mapSize);
            //GD.Print($"SetMode: FullMap - window covers entire map: {_window}");
        }
        else
        {
            // Center window on robot without clamping - allow negative coordinates
            var origin = new Vector2I(
                _robotCell.X - windowSizeTiles.X / 2,
                _robotCell.Y - windowSizeTiles.Y / 2
            );
            _window = new Rect2I(origin, windowSizeTiles);
            
            //GD.Print($"SetMode: RobotWindow - robot at {_robotCell}, window size {windowSizeTiles}, calculated origin {origin}, final window: {_window}");
        }
        
        //GD.Print($"3D Histogram Mode: {mode} | Window: {_window.Size.X}x{_window.Size.Y} tiles ({_window.Size.X * _window.Size.Y} map tiles) → {GridW}x{GridH} bars ({GridW * GridH} bars)");
    }

    /// <summary>
    /// Update bars from an anomaly sampler. Provide either a dictionary lookup or a function.
    /// </summary>
    public void Refresh(Func<Vector2I, float> sampleAnomaly)
    {
        _refreshCount++;
        //GD.Print($"Refresh #{_refreshCount} called - Window: pos={_window.Position}, size={_window.Size}, robot={_robotCell}, mode={_currentMode}");
        
        // Downsample the current window to GridW×GridH
        DownsampleWindow(sampleAnomaly, _window, _grid);
        
        // Check if the grid values have actually changed
        bool hasChanged = HasGridChanged();
        //GD.Print($"Grid comparison: HAS {(hasChanged ? "" : "NOT ")}CHANGED since last refresh");
        
        // Copy current grid to last grid for next comparison
        CopyGridToLast();
        
        // Push to MultiMesh
        ApplyGridToBars(_grid);
        // Move robot marker within the current window
        UpdateRobotMarker();
    }

    // === Core helpers ===
    private void DownsampleWindow(Func<Vector2I, float> sample, Rect2I win, float[,] outGrid)
    {
        float sx = (float)win.Size.X / GridW;
        float sy = (float)win.Size.Y / GridH;
        
        int sampledCells = 0;
        float minSampled = float.MaxValue;
        float maxSampled = float.MinValue;

        for (int gy = 0; gy < GridH; gy++)
        {
            for (int gx = 0; gx < GridW; gx++)
            {
                int x0 = win.Position.X + (int)(gx * sx);
                int x1 = win.Position.X + Math.Min((int)((gx + 1) * sx), win.Size.X);
                int y0 = win.Position.Y + (int)(gy * sy);
                int y1 = win.Position.Y + Math.Min((int)((gy + 1) * sy), win.Size.Y);

                if (x1 <= x0) x1 = x0 + 1;
                if (y1 <= y0) y1 = y0 + 1;

                // Max-pooling keeps peaks visible; switch to average if you prefer
                float m = 0f;
                for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    float v = sample(new Vector2I(x, y));
                    if (v > m) m = v;
                    sampledCells++;
                }
                outGrid[gx, gy] = m;
                
                if (m < minSampled) minSampled = m;
                if (m > maxSampled) maxSampled = m;
            }
        }
        
        //GD.Print($"Downsampled {sampledCells} cells, value range: {minSampled:F2} to {maxSampled:F2}");
    }

    private void ApplyGridToBars(float[,] grid)
    {
        int idx = 0;
        int visibleBars = 0;
        float maxHeight = 0f;
        float minHeight = float.MaxValue;
        
        for (int gy = 0; gy < GridH; gy++)
        for (int gx = 0; gx < GridW; gx++, idx++)
        {
            float v = grid[gx, gy];
            float n = Mathf.Pow(Mathf.Clamp(v / MaxValue, 0f, 1f), Gamma);
            
            // Height based on the actual anomaly value (v), scaled for visibility
            // Using raw value gives better height variation
            float h = MathF.Max(v * HeightScale, 0.01f);

            // Track statistics
            if (v > 0.01f) visibleBars++;
            if (h > maxHeight) maxHeight = h;
            if (h < minHeight) minHeight = h;

            // Get current transform and rebuild with correct height
            var xf = _mm.GetInstanceTransform(idx);
            
            // Create new basis with correct X, Z dimensions and Y height
            var basis = new Basis(
                new Vector3(CellSize * 0.95f, 0, 0),
                new Vector3(0, h, 0),
                new Vector3(0, 0, CellSize * 0.95f)
            );
            
            var origin = xf.Origin;
            origin.Y = h * 0.5f;

            _mm.SetInstanceTransform(idx, new Transform3D(basis, origin));
            _mm.SetInstanceColor(idx, HeatColor(n));
        }
        
        //GD.Print($"Bars painted: {visibleBars}/{GridW * GridH} | Height range: {minHeight:F2} to {maxHeight:F2} units");
    }

    private void UpdateRobotMarker()
    {
        if (RobotMarker == null) return;
        if (_window.Size == Vector2I.Zero) { RobotMarker.Visible = false; return; }

        // In RobotWindow mode, robot is ALWAYS centered at (0, 0)
        if (_currentMode == Mode.RobotWindow)
        {
            RobotMarker.Visible = true;
            RobotMarker.GlobalTransform = new Transform3D(Basis.Identity, new Vector3(0, 0.02f, 0));
            RobotMarker.Scale = new Vector3(CellSize * 0.5f, CellSize * 0.5f, CellSize * 0.5f);
            return;
        }

        // FullMap mode: calculate position based on robot location in map
        // If robot is outside current window, hide
        if (_robotCell.X < _window.Position.X || _robotCell.Y < _window.Position.Y ||
            _robotCell.X >= _window.End.X     || _robotCell.Y >= _window.End.Y)
        {
            RobotMarker.Visible = false;
            return;
        }

        // Map robot to local grid position
        float gx = ((float)(_robotCell.X - _window.Position.X) / _window.Size.X) * GridW - 0.5f;
        float gy = ((float)(_robotCell.Y - _window.Position.Y) / _window.Size.Y) * GridH - 0.5f;

        float x = (gx - GridW * 0.5f + 0.5f) * CellSize;
        float z = (gy - GridH * 0.5f + 0.5f) * CellSize;

        RobotMarker.Visible = true;
        RobotMarker.GlobalTransform = new Transform3D(Basis.Identity, new Vector3(x, 0.02f, z));
        RobotMarker.Scale = new Vector3(CellSize * 0.5f, CellSize * 0.5f, CellSize * 0.5f);
    }

    private static Color HeatColor(float t)
    {
        t = Mathf.Clamp(t, 0f, 1f);
        if (t < 0.33f) { float k = t / 0.33f; return new Color(0, 0.2f + 0.8f*k, 1, 1); }
        if (t < 0.66f) { float k = (t - 0.33f) / 0.33f; return new Color(k, 1, 1 - 0.5f*k, 1); }
        { float k = (t - 0.66f) / 0.34f; return new Color(1, 1 - 0.7f*k, 0.5f - 0.5f*k, 1); }
    }
    
    // === Change Detection ===
    private bool HasGridChanged()
    {
        if (_lastGrid == null) return true; // First run always counts as changed
        
        const float epsilon = 0.001f; // Threshold for considering values different
        
        for (int y = 0; y < GridH; y++)
        {
            for (int x = 0; x < GridW; x++)
            {
                if (Mathf.Abs(_grid[x, y] - _lastGrid[x, y]) > epsilon)
                {
                    return true; // Found a difference
                }
            }
        }
        
        return false; // No significant changes
    }
    
    private void CopyGridToLast()
    {
        if (_lastGrid == null)
        {
            _lastGrid = new float[GridW, GridH];
        }
        
        for (int y = 0; y < GridH; y++)
        {
            for (int x = 0; x < GridW; x++)
            {
                _lastGrid[x, y] = _grid[x, y];
            }
        }
    }
}
