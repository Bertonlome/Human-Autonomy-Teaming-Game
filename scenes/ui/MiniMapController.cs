using Game.Component;
using Godot;
using System;
using System.Collections.Generic;
public partial class MiniMapController : Node
{
    [Export] public PackedScene MiniMapScene;      // res://ui/AnomalyMiniMap.tscn
    [Export] public SubViewportContainer Container; // UI slot bottom-right

    private AnomalyMiniMap _mini;
    private GravitationalAnomalyMap _anomalyMap;   // reference to your map
    private Vector2I _mapSize;
    private BuildingComponent _robot;
    private bool _isRobotWindowMode = false;
    private Vector2I _windowSize = new Vector2I(64, 64);
    
    public override void _Ready()
    {
        // Navigate: Container -> SubViewport -> AnomalyMiniMap
        var viewport = Container.GetChild<SubViewport>(0);
        _mini = viewport.GetChild<AnomalyMiniMap>(0);
    }

    public void Initialize(BuildingComponent robot, GravitationalAnomalyMap anomalyMap, Vector2I mapSize)
    {
        _robot = robot;
        _anomalyMap = anomalyMap;
        _mapSize = mapSize;
        
        // Configure grid resolution based on sensor radius FIRST
        int sensorRadius = robot.BuildingResource.AnomalySensorRadius;
        int windowTiles = sensorRadius * 2; // diameter
        
        // Set grid resolution to match sensor range (1:1 ratio for clearer visualization)
        _mini.GridW = windowTiles;
        _mini.GridH = windowTiles;
        
        GD.Print($"MiniMap configured for sensor radius {sensorRadius}: {windowTiles}x{windowTiles} tiles = {windowTiles * windowTiles} bars");
        
        // NOW initialize the MultiMesh with the correct grid size
        _mini.InitFullMap(mapSize);
    }

    public void SetRobotCell(Vector2I cell)
    {
        _mini.SetRobotCell(cell);
        
        // If in robot window mode, recenter the window around the new position
        if (_isRobotWindowMode)
        {
            _mini.SetMode(AnomalyMiniMap.Mode.RobotWindow, _windowSize);
        }
        
        Refresh();
    }

    public void SetMode(bool fullMap, Vector2I? windowSize = null) // true=full, false=window
    {
        _isRobotWindowMode = !fullMap;
        var windowTiles = windowSize ?? new Vector2I(64, 64); // default window size
        _windowSize = windowTiles;
        
        _mini.SetMode(fullMap ? AnomalyMiniMap.Mode.FullMap
                              : AnomalyMiniMap.Mode.RobotWindow,
                      windowTiles);
        // Removed Refresh() - caller should refresh after setting mode and position
    }

    public void Refresh()
    {
        _mini.Refresh((Vector2I c) => _anomalyMap.GetAnomalyAt(c.X, c.Y));
    }
}
