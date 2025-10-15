using Godot;
using System;
using System.Collections.Generic;
public partial class MiniMapController : Node
{
    [Export] public SubViewportContainer Container;
    [Export] public PackedScene MiniMapScene;

    private AnomalyMiniMap _mini;

    public override void _Ready()
    {
        var inst = MiniMapScene.Instantiate<SubViewport>();
        Container.AddChild(inst);

        _mini = inst.GetChild<AnomalyMiniMap>(0);
    }

    public void Initialize(Dictionary<Vector2I, float> anomalyMap, Vector2I mapSize)
    {
        _mini.InitFullMap(mapSize);
        _mini.Refresh(c => anomalyMap.TryGetValue(c, out var v) ? v : 0f);
    }

    public void SetRobotCell(Vector2I cell) => _mini.SetRobotCell(cell);
}
