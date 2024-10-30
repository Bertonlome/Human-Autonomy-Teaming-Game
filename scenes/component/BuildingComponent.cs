using System.Collections.Generic;
using System.Linq;
using Game.Autoload;
using Game.Resources.Building;
using Godot;

namespace Game.Component;

public partial class BuildingComponent : Node2D
{
	[Signal]
	public delegate void DisabledEventHandler();
	[Signal]
	public delegate void EnabledEventHandler();
	


	[Export(PropertyHint.File, "*.tres")]
	private string buildingResourcePath;
	[Export]
	private BuildingAnimatorComponent buildingAnimatorComponent;

	public BuildingResource BuildingResource { get; private set; }
	public bool IsDestroying { get; private set; }
	public bool IsDisabled { get; private set; }
	public bool IsStuck {get; private set;} = false; 

	private HashSet<Vector2I> occupiedTiles = new();

	public static IEnumerable<BuildingComponent> GetValidBuildingComponents(Node node)
	{
		return node.GetTree()
			.GetNodesInGroup(nameof(BuildingComponent)).Cast<BuildingComponent>()
			.Where((buildingComponent) => !buildingComponent.IsDestroying);
	}

	public static IEnumerable<BuildingComponent> GetDangerBuildingComponents(Node node)
	{
		return GetValidBuildingComponents(node)
			.Where((buildingComponent) => buildingComponent.BuildingResource.IsDangerBuilding());
	}

	public static IEnumerable<BuildingComponent> GetNonDangerBuildingComponents(Node node)
	{
		return GetValidBuildingComponents(node)
			.Where((buildingComponent) => !buildingComponent.BuildingResource.IsDangerBuilding());
	}

	public static IEnumerable<BuildingComponent> GetBaseBuilding(Node node)
	{
		return node.GetTree()
				.GetNodesInGroup(nameof(BuildingComponent)).Cast<BuildingComponent>()
				.Where((buildingComponent) => buildingComponent.BuildingResource.IsBase);
	}

	public override void _Ready()
	{
		if (buildingResourcePath != null)
		{
			BuildingResource = GD.Load<BuildingResource>(buildingResourcePath);
		}

		if (buildingAnimatorComponent != null)
		{
			buildingAnimatorComponent.DestroyAnimationFinished += OnDestroyAnimationFinished;
		}
		AddToGroup(nameof(BuildingComponent));
		Callable.From(Initialize).CallDeferred();
	}

	public Vector2I GetGridCellPosition()
	{
		var gridPosition = GlobalPosition / 64;
		gridPosition = gridPosition.Floor();
		return new Vector2I((int)gridPosition.X, (int)gridPosition.Y);
	}

	public HashSet<Vector2I> GetOccupiedCellPositions()
	{
		return occupiedTiles.ToHashSet();
	}

	public Rect2I GetTileArea()
	{
		var rootCell = GetGridCellPosition();
		var tileArea = new Rect2I(rootCell, BuildingResource.Dimensions);
		return tileArea;
	}

	public bool IsTileInBuildingArea(Vector2I tilePosition)
	{
		return occupiedTiles.Contains(tilePosition);
	}

	public void Disable()
	{
		if (IsDisabled) return;
		IsDisabled = true;
		EmitSignal(SignalName.Disabled);
		GameEvents.EmitBuildingDisabled(this);
	}

	public void Enable()
	{
		if (!IsDisabled) return;
		IsDisabled = false;
		EmitSignal(SignalName.Enabled);
		GameEvents.EmitBuildingEnabled(this);
	}

	public void Destroy()
	{
		IsDestroying = true;
		GameEvents.EmitBuildingDestroyed(this);
		buildingAnimatorComponent?.PlayDestroyAnimation();

		if (buildingAnimatorComponent == null)
		{
			Owner.QueueFree();
		}
	}

	public void SetToStuck()
	{
		IsStuck = true;
		GameEvents.EmitBuildingStuck(this);
		buildingAnimatorComponent.Rotate(-1.05f);
	}

	public Rect2I GetAreaOccupiedAfterMovingFromPos(Vector2I position)
	{
		Vector2I dimensionVector = new Vector2I(BuildingResource.Dimensions.X, BuildingResource.Dimensions.Y);
		Rect2I area = new Rect2I(position, dimensionVector);
		return area;
	}

	public void Moved(Vector2I originPos, Vector2I destinationPos)
	{
		GameEvents.EmitBuildingMoved(this);
		buildingAnimatorComponent?.PlayMoveAnimation(originPos, destinationPos);
		Initialize();
	}

	public Rect2I GetAreaOccupied(Vector2I position)
	{
		Vector2I dimensionVector = new Vector2I(BuildingResource.Dimensions.X, BuildingResource.Dimensions.Y);
		Rect2I area = new Rect2I(position, dimensionVector);
		return area;
	}

	public Rect2I GetAreaOccupiedFromAbsolutePos(Vector2 AbsolutePos)
	{
		Vector2I base64pos = (Vector2I)AbsolutePos;
		base64pos = base64pos.ToBase64();
        Vector2I dimensionVector = new Vector2I(BuildingResource.Dimensions.X, BuildingResource.Dimensions.Y);
		Rect2I area = new Rect2I(base64pos, dimensionVector);
		return area;
	}



	private void CalculateOccupiedCellPositions()
	{
		var gridPosition = GetGridCellPosition();
		for (int x = gridPosition.X; x < gridPosition.X + BuildingResource.Dimensions.X; x++)
		{
			for (int y = gridPosition.Y; y < gridPosition.Y + BuildingResource.Dimensions.Y; y++)
			{
				occupiedTiles.Add(new Vector2I(x, y));
			}
		}
	}

	public void FreeOccupiedCellPosition()
	{
		var gridPosition = GetGridCellPosition();
		for (int x = gridPosition.X; x < gridPosition.X + BuildingResource.Dimensions.X; x++)
		{
			for (int y = gridPosition.Y; y < gridPosition.Y + BuildingResource.Dimensions.Y; y++)
			{
				occupiedTiles.Remove(new Vector2I(x, y));
			}
		}
	}
	

	private void Initialize()
	{
		CalculateOccupiedCellPositions();
		GameEvents.EmitBuildingPlaced(this);
	}

	private void OnDestroyAnimationFinished()
	{
		Owner.QueueFree();
	}
}
