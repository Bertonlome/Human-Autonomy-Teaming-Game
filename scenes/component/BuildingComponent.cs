using System.Collections.Generic;
using System.Linq;
using Game.Autoload;
using Game.Manager;
using Game.Resources.Building;
using Godot;

namespace Game.Component;

public partial class BuildingComponent : Node2D
{
	[Signal]
	public delegate void DisabledEventHandler();
	[Signal]
	public delegate void EnabledEventHandler();
	[Signal]
	public delegate void NewAnomalyReadingEventHandler(int value);
	[Signal]
	public delegate void BatteryChangeEventHandler(int value);
	


	[Export(PropertyHint.File, "*.tres")]
	private string buildingResourcePath;
	[Export]
	private BuildingAnimatorComponent buildingAnimatorComponent;

	public BuildingResource BuildingResource { get; private set; }
	public BuildingManager buildingManager;
	public GridManager gridManager;
	public bool IsDestroying { get; private set; }
	public bool IsDisabled { get; private set; }
	public bool IsRandomMode {get; set;} = false;
	public bool IsStuck {get; private set;} = false; 
	public bool IsRecharging {get; set;} = false;
	public int Battery {get; set;} = 100;

	private HashSet<Vector2I> occupiedTiles = new();

	private readonly StringName MOVE_UP = "move_up";
	private readonly StringName MOVE_DOWN = "move_down";
	private readonly StringName MOVE_LEFT = "move_left";
	private readonly StringName MOVE_RIGHT = "move_right";
	private string previousDir = "";

	// Timer variables
    private float timerMove = 0.0f; // Tracks time since last move
	private float timerRecharge = 0.0f; // Tracks time since last move
	public const float RECHARGE_INTERVAL = 3.0f;


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

		// Get the root node of the current scene
		Node rootNode = GetTree().Root;

		// Attempt to get the BaseLevel node
		BaseLevel level = rootNode.GetFirstNodeOfType<BaseLevel>();

		if (level != null)
		{
			// Attempt to get the BuildingManager node from BaseLevel
			buildingManager = level.GetFirstNodeOfType<BuildingManager>();

			if (buildingManager != null)
			{
				GD.Print($"First child of the root: {buildingManager.Name}");
			}
			else
			{
				GD.PushError("BuildingManager node not found.");
			}
		}
		else
		{
			GD.PushError("BaseLevel node not found.");
		}
		Battery = this.BuildingResource.BatteryMax;
		gridManager = level.GetFirstNodeOfType<GridManager>();
	}

    public override void _Process(double delta)
    {
        if (IsRandomMode == true && !IsStuck)
        {
            // Update the timer
            timerMove += (float)delta;

            // Check if enough time has passed to move
            if (timerMove >= this.BuildingResource.moveInterval)
            {
				var randDir = buildingManager.GetRandomDirection(previousDir);
        		//GD.Print($"Robot Position: {GetGridCellPosition()}, Action Taken: {randDir}");
                buildingManager.MoveBuildingInDirectionAutomated(this, randDir);
				previousDir = randDir;
                timerMove = 0.0f; // Reset the timer
            }
        }
		if(IsRecharging)
		{
			timerRecharge += (float)delta;

			if(timerRecharge >= RECHARGE_INTERVAL && Battery <= this.BuildingResource.BatteryMax - 5)
			{
				Battery += 10;
				EmitSignal(SignalName.BatteryChange, Battery);
				timerRecharge = 0.0f;
			}
		}
	}

	public void EnableRandomMode()
	{
		IsRandomMode = true;
	}

	public void StopRandomMode()
	{
		IsRandomMode = false;
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

	public HashSet<Vector2I> GetTileAndAdjacent()
	{
		var tileAreaAndAdjacent = new HashSet<Vector2I>(occupiedTiles); 
		foreach (var tile in occupiedTiles)
		{
			tileAreaAndAdjacent.Add(new Vector2I(tile.X + 1, tile.Y));
			tileAreaAndAdjacent.Add(new Vector2I(tile.X-1, tile.Y));
			tileAreaAndAdjacent.Add(new Vector2I(tile.X, tile.Y -1));
			tileAreaAndAdjacent.Add(new Vector2I(tile.X, tile.Y + 1));
		} 
		return tileAreaAndAdjacent.ToHashSet();
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
		buildingAnimatorComponent.Rotate(-1.05f);
		GameEvents.EmitBuildingStuck(this);
	}

	public void SetToUnstuck()
	{
		IsStuck = false;
		buildingAnimatorComponent.Rotate(1.05f);
		GameEvents.EmitBuildingUnStuck(this);
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
		if (Battery >= 0) Battery -= 1;
		EmitSignal(SignalName.BatteryChange, Battery);
		GD.Print("Battery left in robot : " + Battery);
		var anomalyValue = gridManager.ComputeAnomalyValue(destinationPos);
		EmitSignal(SignalName.NewAnomalyReading, anomalyValue);
	}

	public int GetAnomalyReadingAtCurrentPos()
	{
		return gridManager.ComputeAnomalyValue(GetGridCellPosition());
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
