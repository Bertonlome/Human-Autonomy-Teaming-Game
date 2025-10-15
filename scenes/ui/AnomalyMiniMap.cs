using Godot;
using System;
using System.Collections.Generic;

public partial class AnomalyMiniMap : Node3D
{
    // === Exports ===
    [Export] public MultiMeshInstance3D Bars;        // assign in inspector
    [Export] public MeshInstance3D RobotMarker;      // assign in inspector
    [Export] public Camera3D Cam;                    // assign in inspector
    [Export] public int GridW = 64;
    [Export] public int GridH = 64;
    [Export] public float CellSize = 0.1f;           // spacing in minimap world units
    [Export] public float HeightScale = 0.003f;      // meters per anomaly unit
    [Export] public float MaxValue = 500f;
    [Export] public float Gamma = 0.6f;
    [Export] public bool UsePerspective = true;      // false = ortho

    private MultiMesh _mm;

    // Cached “view window” in map tile coords
    private Rect2I _window;            // which map area is shown
    private Vector2I _mapSize;         // full map width/height (tiles)
    private Vector2I _robotCell;       // robot tile for marker
    private float[,] _grid;            // GridW×GridH downsample

    public override void _Ready()
    {
        // Setup MultiMesh with a box/cube
        _mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            InstanceCount = GridW * GridH,
            Mesh = new BoxMesh { Size = new Vector3(CellSize * 0.9f, 1f, CellSize * 0.9f) } // Y=1; we'll scale
        };
        Bars.Multimesh = _mm;

        ConfigureCamera();
        _grid = new float[GridW, GridH];
        // pre-place instances (XZ positions) once
        PreplaceInstances();
    }

    private void ConfigureCamera()
    {
        if (UsePerspective)
        {
            Cam.Projection = Camera3D.ProjectionType.Perspective;
            // Place camera to see whole grid nicely
            var extentX = GridW * CellSize * 0.5f;
            var extentZ = GridH * CellSize * 0.5f;
            GlobalPosition = Vector3.Zero;
            Cam.GlobalPosition = new Vector3(0, MathF.Max(extentX, extentZ) * 1.8f, MathF.Max(extentX, extentZ));
            Cam.LookAt(new Vector3(0, 0, 0), Vector3.Up);
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
                new Vector3(CellSize * 0.9f, 0, 0),
                new Vector3(0, 1f, 0),
                new Vector3(0, 0, CellSize * 0.9f)
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
        if (mode == Mode.FullMap)
            _window = new Rect2I(Vector2I.Zero, _mapSize);
        else
        {
            var origin = new Vector2I(
                Mathf.Clamp(_robotCell.X - windowSizeTiles.X / 2, 0, Math.Max(0, _mapSize.X - windowSizeTiles.X)),
                Mathf.Clamp(_robotCell.Y - windowSizeTiles.Y / 2, 0, Math.Max(0, _mapSize.Y - windowSizeTiles.Y))
            );
            _window = new Rect2I(origin, windowSizeTiles);
        }
    }

    /// <summary>
    /// Update bars from an anomaly sampler. Provide either a dictionary lookup or a function.
    /// </summary>
    public void Refresh(Func<Vector2I, float> sampleAnomaly)
    {
        // Downsample the current window to GridW×GridH
        DownsampleWindow(sampleAnomaly, _window, _grid);
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
                }
                outGrid[gx, gy] = m;
            }
        }
    }

    private void ApplyGridToBars(float[,] grid)
    {
        int idx = 0;
        for (int gy = 0; gy < GridH; gy++)
        for (int gx = 0; gx < GridW; gx++, idx++)
        {
            float v = grid[gx, gy];
            float n = Mathf.Pow(Mathf.Clamp(v / MaxValue, 0f, 1f), Gamma);
            float h = MathF.Max(n * (MaxValue * HeightScale), 0.001f);

            // Read current transform, replace Y-scale and Y-offset
            var xf = _mm.GetInstanceTransform(idx);
            var basis = xf.Basis;
            basis.Y = new Vector3(0, h, 0);
            var origin = xf.Origin;
            origin.Y = h * 0.5f;

            _mm.SetInstanceTransform(idx, new Transform3D(basis, origin));
            _mm.SetInstanceColor(idx, HeatColor(n));
        }
    }

    private void UpdateRobotMarker()
    {
        if (RobotMarker == null) return;
        if (_window.Size == Vector2I.Zero) { RobotMarker.Visible = false; return; }

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
}
