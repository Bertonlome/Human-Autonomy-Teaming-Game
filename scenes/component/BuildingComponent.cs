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
	[Signal]
	public delegate void ModeChangedEventHandler(string mode);
	[Signal]
	public delegate void robotStuckEventHandler();
	[Signal]
	public delegate void robotUnStuckEventHandler();
	[Signal]
	public delegate void StartChargingEventHandler();
	[Signal]
	public delegate void StopChargingEventHandler();
	


	[Export(PropertyHint.File, "*.tres")]
	private string buildingResourcePath;
	[Export]
	private BuildingAnimatorComponent buildingAnimatorComponent;

	public BuildingResource BuildingResource { get; private set; }
	public BuildingManager buildingManager;
	public GravitationalAnomalyMap gravitationalAnomalyMap;
	public bool IsDestroying { get; private set; }
	public bool IsDisabled { get; private set; }
	public bool IsStuck {get; private set;} = false; 
	public bool IsRecharging {get; set;} = false;
	public int Battery {get; set;} = 100;
	
	private List<(Vector2I,StringName)> moveHistory = new();

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


	public enum ExplorMode
	{
		Random,
		GradientSearch,
		RewindMoves,
		ReturnToBase,
		MoveToPos,
		None
	}

	public ExplorMode currentExplorMode = ExplorMode.None;



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
		gravitationalAnomalyMap = level.GetFirstNodeOfType<GravitationalAnomalyMap>();
	}

    public override void _Process(double delta)
    {
		switch(currentExplorMode)
		{
			case ExplorMode.Random:
			if (!IsStuck)
			{
				// Update the timer
				timerMove += (float)delta;

				// Check if enough time has passed to move
				if (timerMove >= this.BuildingResource.moveInterval)
				{
					var randDir = buildingManager.GetRandomDirection(previousDir);
					//GD.Print($"Robot Position: {GetGridCellPosition()}, Action Taken: {randDir}");
					buildingManager.MoveInDirectionAutomated(this, randDir);
					previousDir = randDir;
					timerMove = 0.0f; // Reset the timer
				}
			}
			break;
			case ExplorMode.GradientSearch:
			break;
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

	private List<StringName> GenerateHistoryMoveList()
	{
		var reversedMoveHistory = moveHistory.AsEnumerable().Reverse();
		List<StringName> reversedDirections = new();
		foreach((Vector2I position, StringName direction) in reversedMoveHistory)
		{
			reversedDirections.Add(GetOppositeDirection(direction));
		}
		return reversedDirections;
	}

	public StringName GetOppositeDirection(StringName directionInput)
	{
		StringName directionOutput = "";
		
		if(directionInput == MOVE_DOWN) directionOutput = MOVE_UP;
		else if(directionInput == MOVE_UP) directionOutput = MOVE_DOWN;
		else if(directionInput == MOVE_LEFT) directionOutput = MOVE_RIGHT;
		else if(directionInput == MOVE_RIGHT) directionOutput = MOVE_LEFT;
		
		return directionOutput;
	}

	public void UpdateMoveHistory(Vector2I position, StringName direction)
	{
		moveHistory.Add((position, direction));
	}

	public void EnableRandomMode()
	{
		currentExplorMode = ExplorMode.Random;
		EmitSignal(SignalName.ModeChanged, currentExplorMode.ToString());
	}

	public void EnableGradientSearchMode()
	{
		currentExplorMode = ExplorMode.GradientSearch;
		EmitSignal(SignalName.ModeChanged, currentExplorMode.ToString());
		GradientSearch();
	}

	public void EnableRewindMovesMode()
	{
		currentExplorMode = ExplorMode.RewindMoves;
		EmitSignal(SignalName.ModeChanged, currentExplorMode.ToString());
		RewindMoves();
	}

	public void EnableReturnToBase()
	{
		currentExplorMode = ExplorMode.ReturnToBase;
		EmitSignal(SignalName.ModeChanged, currentExplorMode.ToString());
		ReturnToBase();
	}

	public void StopAnyAutomatedMovementMode()
	{
		currentExplorMode = ExplorMode.None;
		EmitSignal(SignalName.ModeChanged, currentExplorMode.ToString());
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

	public HashSet<Vector2I> GetTilesWithinDistance(Vector2I startTile, int maxDistanceSensor)
	{
		var reachableTiles = new HashSet<Vector2I> { startTile }; // Start with the initial tile

		for (int distance = 1; distance <= maxDistanceSensor; distance++)
		{
			var currentLevelTiles = new HashSet<Vector2I>();

			foreach (var tile in reachableTiles)
			{
				// Add all tiles reachable in one move from the current tile
				currentLevelTiles.Add(new Vector2I(tile.X + 1, tile.Y));
				currentLevelTiles.Add(new Vector2I(tile.X - 1, tile.Y));
				currentLevelTiles.Add(new Vector2I(tile.X, tile.Y + 1));
				currentLevelTiles.Add(new Vector2I(tile.X, tile.Y - 1));
			}

			// Merge the newly found tiles into the main set
			reachableTiles.UnionWith(currentLevelTiles);
		}

		return reachableTiles;
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
		EmitSignal(SignalName.robotStuck);
	}

	public void SetToUnstuck()
	{
		IsStuck = false;
		buildingAnimatorComponent.Rotate(1.05f);
		GameEvents.EmitBuildingUnStuck(this);
		EmitSignal(SignalName.robotUnStuck);
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
		//GD.Print("Battery left in robot : " + Battery);
		var anomalyValue = gravitationalAnomalyMap.GetAnomalyAt(destinationPos.X, destinationPos.Y);
		EmitSignal(SignalName.NewAnomalyReading, anomalyValue);
	}

	public int GetAnomalyReadingAtCurrentPos()
	{
		return (int)gravitationalAnomalyMap.GetAnomalyAt(GetGridCellPosition().X, GetGridCellPosition().Y);
	}

	public List<Vector2I> GetTileDiscovered()
	{
		List<Vector2I> tileDiscovered = new();
		foreach((var tile, _) in moveHistory)
		{
			tileDiscovered.Add(tile);
			var tilesInRadius = GetTilesWithinDistance(tile, BuildingResource.AnomalySensorRadius);
			foreach (var tileInRadius in tilesInRadius)
			{
				if (!tileDiscovered.Contains(tileInRadius))
				{
					tileDiscovered.Add(tileInRadius);
				}
			}
		}
		return tileDiscovered;
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

	public bool cancelMoveRequested = false;
	public async void MoveAlongPath(List<string> moves)
	{
		// Cancel any previous movement
		cancelMoveRequested = true;
		await ToSignal(GetTree().CreateTimer(0.01f), "timeout"); // Give time for previous to exit

		cancelMoveRequested = false; // Reset for this movement

		if (currentExplorMode != ExplorMode.None)
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("Already moving!", this);
			return;
		}
		if (Battery < moves.Count)
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("Not enough battery to move automatically", this);
			return;
		}
		if (IsStuck)
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("Cannot move while stuck", this);
			return;
		}
		currentExplorMode = ExplorMode.MoveToPos;
		EmitSignal(SignalName.ModeChanged, currentExplorMode.ToString());

		foreach (var direction in moves)
		{
			if (cancelMoveRequested)
			{
				currentExplorMode = ExplorMode.None;
				EmitSignal(SignalName.ModeChanged, currentExplorMode.ToString());
				return; // Stop movement
			}
			buildingManager.MoveInDirectionAutomated(this, direction);
			await ToSignal(GetTree().CreateTimer(BuildingResource.moveInterval), "timeout");
		}
		currentExplorMode = ExplorMode.None;
		EmitSignal(SignalName.ModeChanged, currentExplorMode.ToString());
	}

	public async void RewindMoves()
	{
		if (Battery < moveHistory.Count)
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("Not enough battery to go back to base automatically", this);
		}
		if (!IsStuck && Battery > moveHistory.Count)
		{
			var reversedHistoryMove = GenerateHistoryMoveList();
			foreach (StringName direction in reversedHistoryMove)
			{
				buildingManager.MoveInDirectionAutomated(this, direction);

				await ToSignal(GetTree().CreateTimer(this.BuildingResource.moveInterval), "timeout");
			}
		}
		currentExplorMode = ExplorMode.None;
		moveHistory.Clear();
	}

	public async void ReturnToBase()
	{
		var myBase = GetBaseBuilding(this).FirstOrDefault();
		var movesToReachBase = GetMovesToReachTile(GetGridCellPosition(), myBase.GetGridCellPosition());
		if(Battery < movesToReachBase.Count)
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("Not enough battery to go back to base automatically", this);
		}
		if (!IsStuck && Battery > movesToReachBase.Count)
		{
			foreach(StringName direction in movesToReachBase)
			{
				buildingManager.MoveInDirectionAutomated(this, direction);

				await ToSignal(GetTree().CreateTimer(this.BuildingResource.moveInterval), "timeout");
			}
		}
		currentExplorMode = ExplorMode.None;
		EmitSignal(SignalName.ModeChanged, currentExplorMode.ToString());
	}

	public List<string> GetMovesToReachTile(Vector2I currentPosition, Vector2I targetPosition)
	{
		List<string> moves = new List<string>();

		// Calculate the deltas
		int deltaX = targetPosition.X - currentPosition.X;
		int deltaY = targetPosition.Y - currentPosition.Y;

		// Add horizontal moves
		if (deltaX > 0)
		{
			for (int i = 0; i < deltaX; i++)
			{
				moves.Add(MOVE_RIGHT);
			}
		}
		else if (deltaX < 0)
		{
			for (int i = 0; i < -deltaX; i++)
			{
				moves.Add(MOVE_LEFT);
			}
		}

		// Add vertical moves
		if (deltaY > 0)
		{
			for (int i = 0; i < deltaY; i++)
			{
				moves.Add(MOVE_DOWN);
			}
		}
		else if (deltaY < 0)
		{
			for (int i = 0; i < -deltaY; i++)
			{
				moves.Add(MOVE_UP);
			}
		}

		return moves;
	}
	
	public void SetRecharging(bool recharging)
	{
		IsRecharging = recharging;
		EmitSignal(recharging ? SignalName.StartCharging : SignalName.StopCharging);
	}




public async void GradientSearch()
	{
		if (currentExplorMode == ExplorMode.GradientSearch && !IsStuck)
		{
			bool reachedMaxima = false;
			var currentPosition = GetGridCellPosition();
			var candidateTiles = GetTilesWithinDistance(currentPosition, BuildingResource.AnomalySensorRadius);

			Vector2I highestTile = currentPosition; // Default to current position
			float highestAnomaly = float.MinValue; // Start with a very low value

			while (currentExplorMode == ExplorMode.GradientSearch && !reachedMaxima)
			{
				highestAnomaly = float.MinValue;
				foreach (var tile in candidateTiles)
				{
					// Get anomaly value
					var anomaly = gravitationalAnomalyMap.GetAnomalyAt(tile.X, tile.Y);
					//GD.Print($"Adjacent pos: ({tile.X}, {tile.Y}) anomaly = {anomaly}");

					// Update the highest anomaly tile if a higher value is found
					if (anomaly > highestAnomaly)
					{
						highestAnomaly = anomaly;
						highestTile = tile;
					}

					// Add delay
				}

				// Calculate the relative direction
				string relativeDirection = GetDirectionFromDelta(currentPosition, highestTile);

				//GD.Print($"Tile with the highest anomaly is at {highestTile}, direction: {relativeDirection}");
				if (relativeDirection != "CURRENT")
				{
					buildingManager.MoveInDirectionAutomated(this, relativeDirection);
				}
				else
				{
					reachedMaxima = true;
				}
				await ToSignal(GetTree().CreateTimer(BuildingResource.moveInterval), "timeout");
				currentPosition = GetGridCellPosition();
				candidateTiles = GetTilesWithinDistance(currentPosition, BuildingResource.AnomalySensorRadius);
			}
		}
		currentExplorMode = ExplorMode.None;
		EmitSignal(SignalName.ModeChanged, currentExplorMode.ToString());
	}

private string GetDirectionFromDelta(Vector2I current, Vector2I target)
{
    int dx = target.X - current.X;
    int dy = target.Y - current.Y;

    if (dx > 0) return MOVE_RIGHT;
    if (dx < 0) return MOVE_LEFT;
    if (dy < 0) return MOVE_UP;
    if (dy > 0) return MOVE_DOWN;

    return "CURRENT"; // In case the target is the same as the current position
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
