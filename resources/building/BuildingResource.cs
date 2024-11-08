using Godot;

namespace Game.Resources.Building;

[GlobalClass]
public partial class BuildingResource : Resource
{
	[Export]
	public string DisplayName { get; private set; }
	[Export]
	public string Description { get; private set; }
	[Export]
	public bool IsBase { get; private set; }
	[Export]
	public bool IsDeletable { get; private set; } = true;
	[Export]
	public bool IsAerial{get; private set; }
	[Export]
	public Vector2I Dimensions { get; private set; } = Vector2I.One;
	[Export]
	public float StuckChancePerMove {get; private set; } = 0f;
	[Export]
	public float moveInterval {get; private set; } = 1.0f; // Time between moves in seconds
	[Export]
	public int Battery {get; private set;} = 100;
	[Export]
	public int ResourceCost { get; private set; }
	[Export]
	public int BuildableRadius { get; private set; }
	[Export]
	public int ResourceRadius { get; private set; }
	[Export]
	public int VisionRadius {get; private set;}
	[Export]
	public int DangerRadius { get; private set; }
	[Export]
	public int AttackRadius { get; private set; }
	[Export]
	public PackedScene BuildingScene { get; private set; }
	[Export]
	public PackedScene SpriteScene { get; private set; }

	public bool IsAttackBuilding()
	{
		return AttackRadius > 0;
	}

	public bool IsDangerBuilding()
	{
		return DangerRadius > 0;
	}
}
