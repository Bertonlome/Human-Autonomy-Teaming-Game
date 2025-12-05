using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using Game.Autoload;
using Game.Building;
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
	public bool CanMove { get; set; } = true;
	public BuildingManager buildingManager;
	public GridManager gridManager;
	public GravitationalAnomalyMap gravitationalAnomalyMap;
	public bool IsDestroying { get; private set; }
	public bool IsDisabled { get; private set; }
	public bool IsStuck {get; private set;} = false; 
	public bool IsRecharging {get; set;} = false;
	private bool HasMoved = false;

	public bool IsLifting;
	public bool IsLifted;
	public int Battery { get; set; } = 100;
	public List<string> resourceCollected = new();
	public List<PaintedTile> paintedTiles = new();

	private List<(Vector2I, StringName)> moveHistory = new();

	private HashSet<Vector2I> occupiedTiles = new();
	private HashSet<Vector2I> scannedTiles = new HashSet<Vector2I>();
	private HashSet<Vector2I> obstacleTiles = new HashSet<Vector2I>();
	private HashSet<Vector2I> _discoveredTilesCache = new HashSet<Vector2I>(); // Cache for GetTileDiscovered()

	private readonly StringName MOVE_UP = "move_up";
	private readonly StringName MOVE_DOWN = "move_down";
	private readonly StringName MOVE_LEFT = "move_left";
	private readonly StringName MOVE_RIGHT = "move_right";
	private string previousDir = "";
	private int numberOfWoodCarried => resourceCollected.Count(res => res == "wood");

	private BuildingComponent AttachedRobot;

	// Timer variables
	private float timerMove = 0.0f; // Tracks time since last move
	private float timerRecharge = 0.0f; // Tracks time since last move
	public const float RECHARGE_INTERVAL = 3.0f;

	private Sprite2D grappleComponent;
	private Node grappleRoot;
	private bool IsGrappleExtended = false;

	public enum ExplorMode
	{
		Random,
		GradientSearch,
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
			gridManager = level.GetFirstNodeOfType<GridManager>();

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
		if(IsRecharging)
		{
			timerRecharge += (float)delta;

			// Only recharge if wood is available
			if (buildingManager.AvailableWoodCount > 0)
			{
				if (timerRecharge >= RECHARGE_INTERVAL && Battery <= this.BuildingResource.BatteryMax - 5)
				{
					Battery += 30;
					EmitSignal(SignalName.BatteryChange, Battery);
					timerRecharge = 0.0f;

					// Consume wood at specified rate (e.g., 1 per recharge)
					buildingManager.ConsumeWoodForCharging(1);
				}
			}
			else
			{
				// Stop charging if no wood left
				SetRecharging(false);
			}
		}
	}

	public void AttachToRobot(BuildingComponent robot)
	{
		AttachedRobot = robot;
		if (BuildingResource.IsAerial)
		{
			IsLifting = true;
			ExtendGrapple();
			EmitSignal(SignalName.ModeChanged, "Lifting");
		}
		else
		{
			IsLifted = true;
			EmitSignal(SignalName.ModeChanged, "Lifted");
		}
	}

	public void DetachRobot()
	{
		AttachedRobot = null;
		IsLifted = false;
		IsLifting = false;
		RetractGrapple();
		EmitSignal(SignalName.ModeChanged, "Idle");
	}

	private List<StringName> GenerateHistoryMoveList()
	{
		var reversedMoveHistory = moveHistory.AsEnumerable().Reverse();
		List<StringName> reversedDirections = new();
		foreach ((Vector2I position, StringName direction) in reversedMoveHistory)
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
		
		// OPTIMIZATION: Update discovered tiles cache incrementally
		_discoveredTilesCache.Add(position);
		var tilesInRadius = GetTilesWithinDistance(position, BuildingResource.AnomalySensorRadius);
		foreach (var tile in tilesInRadius)
		{
			_discoveredTilesCache.Add(tile);
		}
	}

	public void EnableRandomMode()
	{
		currentExplorMode = ExplorMode.Random;
		EmitSignal(SignalName.ModeChanged, currentExplorMode.ToString());
		RandomExplore();
	}

	public void EnableGradientSearchMode()
	{
		currentExplorMode = ExplorMode.GradientSearch;
		EmitSignal(SignalName.ModeChanged, currentExplorMode.ToString());
		GradientSearch();
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
		if (IsStuck) return; // Already stuck, don't rotate again
		
		IsStuck = true;
		buildingAnimatorComponent.Rotate(-1.05f);
		GameEvents.EmitBuildingStuck(this);
		EmitSignal(SignalName.robotStuck);
	}

	public void SetToUnstuck()
	{
		if (!IsStuck) return; // Not stuck, don't rotate again
		
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
		if (IsLifting)
		{
			if (Battery >= 0) Battery -= 20;
		}
		else if (!IsLifted && Battery >= 0) Battery -= 1;
		EmitSignal(SignalName.BatteryChange, Battery);
		//GD.Print("Battery left in robot : " + Battery);
		var anomalyValue = gravitationalAnomalyMap.GetAnomalyAt(destinationPos.X, destinationPos.Y);
		EmitSignal(SignalName.NewAnomalyReading, anomalyValue);
	}

	public int GetAnomalyReadingAtCurrentPos()
	{
		return (int)gravitationalAnomalyMap.GetAnomalyAt(GetGridCellPosition().X, GetGridCellPosition().Y);
	}

	public void AddPaintedTile(PaintedTile tile)
	{
		paintedTiles.Add(tile);
	}

	public int GetNextPaintedTileNumber()
	{
		return paintedTiles.Count + 1;
	}

	public List<Vector2I> GetTileDiscovered()
	{
		// OPTIMIZED: Return cached discovered tiles instead of rebuilding every time
		// The cache is updated incrementally in UpdateMoveHistory()
		return new List<Vector2I>(_discoveredTilesCache);
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

	public void Move(string direction)
	{
		if (BuildingResource.IsAerial && AttachedRobot is not null && Battery >= 0)
		{
			MoveLifted(direction);
			AttachedRobot.MoveLifted(direction);
		}
		else
		{
			if (AttachedRobot is not null)
			{
				AttachedRobot.DetachRobot();
				DetachRobot();
			}
			buildingManager.MoveInDirection(this, direction);
		}
	}

	public void MoveLifted(string direction)
	{
		buildingManager.LiftInDirection(this, direction);
	}

	public bool cancelMoveRequested = false;
	class NodeAStar
	{
		public Vector2I Position;
		public NodeAStar? Parent;
		public int G; // Cost from start to current node
		public int H; // Heuristic cost from current node to target
		public int F => G + H; // Total cost

		public NodeAStar(Vector2I position, NodeAStar? parent, int g, int h)
		{
			Position = position;
			Parent = parent;
			G = g;
			H = h;
		}
	}

	/// <summary>
	/// Public wrapper for path preview - generates A* path from start to end
	/// </summary>
	public (List<string> moves, HashSet<Vector2I> bridgeTiles) ComputeAStarPath(Vector2I fromPos, Vector2I toPos, bool allowWaterCrossing = false)
	{
		return GetMovesWithAStarAndBridges(fromPos, toPos, allowWaterCrossing);
	}
	
	/// <summary>
	/// Public helper to get next position from a move direction
	/// </summary>
	public Vector2I ComputeNextPosition(Vector2I currentPos, string moveDirection)
	{
		return GetNextPosFromCurrentPos(currentPos, moveDirection);
	}

	private (List<string> path, HashSet<Vector2I> bridgeTiles) GetMovesWithAStarAndBridges(Vector2I currentPos, Vector2I targetPos, bool allowWaterCrossing = false)
	{
		// Determine the elevation level for bridge pathfinding
		bool? bridgeElevationIsElevated = null;
		HashSet<Vector2I> bridgeTiles = new HashSet<Vector2I>();
		
		if (allowWaterCrossing && !BuildingResource.IsAerial)
		{
			// Check if start and target are at the same elevation level
			var (startElevation, startIsElevated) = gridManager.GetElevationLayerForTile(currentPos);
			var (targetElevation, targetIsElevated) = gridManager.GetElevationLayerForTile(targetPos);
			
			// Only allow bridge crossing if both start and target are at the same elevation level
			if (startIsElevated == targetIsElevated)
			{
				bridgeElevationIsElevated = startIsElevated;
			}
			else
			{
				// Cannot bridge between different elevation levels
				return (new List<string> {}, bridgeTiles);
			}
		}
		
		var open = new List<NodeAStar>();
		var closed = new HashSet<Vector2I>();
		open.Add(new NodeAStar(currentPos, null, 0, Heuristic(currentPos, targetPos)));
		var directions = new[] { MOVE_UP, MOVE_DOWN, MOVE_LEFT, MOVE_RIGHT };

		int maxIterations = 1000;
		int iteration = 0;
		while (open.Count > 0 && iteration < maxIterations)
		{
			open.Sort((a, b) => a.F.CompareTo(b.F));
			var current = open[0];
			open.RemoveAt(0);
			closed.Add(current.Position);

			if (current.Position == targetPos)
			{
				// Reached the target, reconstruct the path and identify bridge tiles
				var path = new List<string>();
				var pathPositions = new List<Vector2I>();
				var node = current;
				while (node.Parent != null)
				{
					var direction = GetDirection(node.Parent.Position, node.Position);
					path.Add(direction);
					pathPositions.Add(node.Position);
					node = node.Parent;
				}
				path.Reverse();
				pathPositions.Reverse();
				
				// Identify which tiles need bridges
				if (allowWaterCrossing && bridgeElevationIsElevated.HasValue)
				{
					foreach (var pos in pathPositions)
					{
						var (tileElevation, tileIsElevated) = gridManager.GetElevationLayerForTile(pos);
						(_, bool isWater) = gridManager.GetTileCustomData(pos, GridManager.IS_WATER);
						
						// A tile needs a bridge if:
						// 1. It's a water tile, OR
						// 2. Its elevation doesn't match the path elevation
						if (isWater || tileIsElevated != bridgeElevationIsElevated.Value)
						{
							bridgeTiles.Add(pos);
						}
					}
				}
				
				return (path, bridgeTiles);
			}

			// Explore neighbors
			foreach (var direction in directions)
			{
				var neighborPos = GetNextPosFromCurrentPos(current.Position, direction);

				// Skip if already explored
				if (closed.Contains(neighborPos)) continue;
				
				// Skip if this tile is in the global exclusion zones (LLM-specified avoid list)
				if (BuildingManager.GlobalExclusionZones.Contains(neighborPos))
				{
					continue;
				}

				// Check if robot can move to this neighbor
				var originArea = GetAreaOccupied(current.Position);
				var destinationArea = GetAreaOccupied(neighborPos);
				
				// Pass the bridge elevation context to the validation
				if (!gridManager.IsBuildingMovable(this, originArea, destinationArea, allowWaterCrossing, bridgeElevationIsElevated))
					continue;

				var gCost = current.G + 1; // Assume cost is 1 for each move
				var hCost = Heuristic(neighborPos, targetPos);
				var neighbor = new NodeAStar(neighborPos, current, gCost, hCost);

				// Check if neighbor is already in open list
				var existing = open.Find(n => n.Position == neighbor.Position);
				if (existing == null)
				{
					open.Add(neighbor);
				}
				else if (gCost < existing.G)
				{
					existing.G = gCost;
					existing.Parent = current;
				}
			}
			iteration ++;
		}
		return (new List<string> {}, new HashSet<Vector2I>()); 
	}
	
	private int Heuristic(Vector2I a, Vector2I b)
	{
		return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y); // Manhattan distance
	}

	private Vector2I GetNextPosFromCurrentPos(Vector2I currentPos, String direction)
	{
		return direction switch
		{
			"move_up" => currentPos + new Vector2I(0, -1),
			"move_down" => currentPos + new Vector2I(0, 1),
			"move_left" => currentPos + new Vector2I(-1, 0),
			"move_right" => currentPos + new Vector2I(1, 0),
			_ => currentPos,
		};
	}

	private StringName GetDirection(Vector2I from, Vector2I to)
	{
		var delta = to - from;
		if (delta == new Vector2I(0, -1)) return MOVE_UP;
		if (delta == new Vector2I(0, 1)) return MOVE_DOWN;
		if (delta == new Vector2I(-1, 0)) return MOVE_LEFT;
		if (delta == new Vector2I(1, 0)) return MOVE_RIGHT;
		return "";
	}

	public async void MoveAlongPath(Vector2I targetPosition, bool astar=false)
	{
		if (AttachedRobot is not null) DetachRobot();
		// Cancel any previous movement
		cancelMoveRequested = true;

		await ToSignal(GetTree().CreateTimer(0.1f), "timeout"); // Give time for previous to exit

		cancelMoveRequested = false; // Reset for this movement

		if (currentExplorMode != ExplorMode.None && currentExplorMode != ExplorMode.ReturnToBase && currentExplorMode != ExplorMode.Random)
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("Already moving!", this);
			return;
		}
		if (IsStuck)
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("Cannot move while stuck", this);
			return;
		}
		if (Battery <= 15)
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("Battery depleted!", this);
			currentExplorMode = ExplorMode.None;
			return;
		}
		if (currentExplorMode != ExplorMode.Random)
		{
			currentExplorMode = ExplorMode.MoveToPos;
			EmitSignal(SignalName.ModeChanged, currentExplorMode.ToString());
		}
		if (astar)
		{
			// First, try to find a land-only path
			var (path, _) = GetMovesWithAStarAndBridges(GetGridCellPosition(), targetPosition, false);
			bool requiresBridge = false;
			HashSet<Vector2I> bridgeTilesNeeded = new HashSet<Vector2I>();
			
			// If no land path found and this is a rover (non-aerial), try allowing water crossing
			if (path.Count == 0 && !BuildingResource.IsAerial)
			{
				//Here we need to check if the end point is on the same elevation layer as the start point
				var (robotElevation, robotIsElevated) = gridManager.GetElevationLayerForTile(GetGridCellPosition());
				var (targetElevation, targetIsElevated) = gridManager.GetElevationLayerForTile(targetPosition);
				if (robotIsElevated != targetIsElevated)
				{
					FloatingTextManager.ShowMessageAtBuildingPosition("No path found", this);
					currentExplorMode = ExplorMode.None;
					EmitSignal(SignalName.ModeChanged, currentExplorMode.ToString());
					return;
				}
				(path, bridgeTilesNeeded) = GetMovesWithAStarAndBridges(GetGridCellPosition(), targetPosition, true);
				
				if (path.Count > 0)
				{
					// Found a path that requires crossing water
					requiresBridge = true;
					
					// Check if we have enough wood to build all bridges needed
					int woodNeeded = bridgeTilesNeeded.Count;
					if (numberOfWoodCarried < woodNeeded)
					{
						FloatingTextManager.ShowMessageAtBuildingPosition($"Need {woodNeeded} wood for bridges, but only have {numberOfWoodCarried}!", this);
						currentExplorMode = ExplorMode.None;
						EmitSignal(SignalName.ModeChanged, currentExplorMode.ToString());
						return;
					}
					else
					{
						FloatingTextManager.ShowMessageAtBuildingPosition($"Path requires {woodNeeded} bridge", this);
					}
				}
			}
			
			// Check if target itself is water (for aerial units this is fine)
			(_, bool iswater) = gridManager.GetTileCustomData(targetPosition, GridManager.IS_WATER);
			if (iswater && !BuildingResource.IsAerial)
			{
				if (numberOfWoodCarried > 0)
				{
					FloatingTextManager.ShowMessageAtBuildingPosition("Building a bridge to cross water!", this);
					//buildingManager.BuildBridgeAtPosition(targetPosition, this);
				}
				else if (!requiresBridge) // Only show this if we haven't already shown the bridge message
				{
					FloatingTextManager.ShowMessageAtBuildingPosition("Need wood to build a bridge!", this);
					currentExplorMode = ExplorMode.None;
					EmitSignal(SignalName.ModeChanged, currentExplorMode.ToString());
					return;
				}
			}
			
			// If still no path found, give up
			if (path.Count == 0)
			{
				FloatingTextManager.ShowMessageAtBuildingPosition("No path found!", this);
				GD.Print("No path found for " + GetGridCellPosition() + " to " + targetPosition);
				currentExplorMode = ExplorMode.None;
				EmitSignal(SignalName.ModeChanged, currentExplorMode.ToString());
				return;
			}
			
			// Paint the path
			var paintPosition = GetGridCellPosition();
			foreach (var move in path)
			{
				paintPosition = GetNextPosFromCurrentPos(paintPosition, move);
				buildingManager.CreatePaintedTileAt(paintPosition);
			}
			
			// Move along the path and place bridges as we go
			int bridgesBuilt = 0;
			for (int i = 0; i < path.Count; i++)
			{
				if (cancelMoveRequested) break;
				if (currentExplorMode == ExplorMode.None) break;
				if (IsStuck) break;
				if (Battery <= 0) break;
				
				var currentDirection = path[i];
				var currentPos = GetGridCellPosition();
				var nextPos = GetNextPosFromCurrentPos(currentPos, currentDirection);
				
				// Check if next position needs a bridge
				if (bridgeTilesNeeded.Contains(nextPos) && requiresBridge)
				{
					// Double-check we have wood (safety check)
					if (numberOfWoodCarried <= 0)
					{
						FloatingTextManager.ShowMessageAtBuildingPosition("Ran out of wood for bridges!", this);
						currentExplorMode = ExplorMode.None;
						EmitSignal(SignalName.ModeChanged, currentExplorMode.ToString());
						buildingManager.ClearAllPaintedTiles();
						return;
					}
					
					var robotArea = GetAreaOccupied(currentPos);
					var bridgeArea = GetAreaOccupied(nextPos);
					
					// Determine orientation based on direction
					var delta = nextPos - currentPos;
					string orientation = (delta.X != 0) ? "horizontal" : "vertical";
					
					if (gridManager.TryPlaceBridgeTile(robotArea, bridgeArea, orientation))
					{
						RemoveResource("wood"); // Consume wood for bridge
						bridgesBuilt++;
						GD.Print($"Built bridge {bridgesBuilt}/{bridgeTilesNeeded.Count} at {nextPos}");
						if (orientation == "vertical")
							FloatingTextManager.ShowMessageAtBuildingPosition($"Built bridge {bridgesBuilt}/{bridgeTilesNeeded.Count}", this);
						else
							FloatingTextManager.ShowMessageAtMousePosition($"Built bridge {bridgesBuilt}/{bridgeTilesNeeded.Count}");
						// Wait a moment after placing bridge
						await ToSignal(GetTree().CreateTimer(BuildingResource.moveInterval), "timeout");
					}
				}
				
				// Now move in this direction
				buildingManager.MoveInDirectionAutomated(this, currentDirection);
				await ToSignal(GetTree().CreateTimer(BuildingResource.moveInterval), "timeout");
			}
		}
		/*
		else
		{
			Vector2I currentPos = GetGridCellPosition();
			int maxSteps = 100; // Prevent infinite loops
			int steps = 0;
			int visitedLimit = 5; // How many tiles to remember
			Queue<Vector2I> visited = new Queue<Vector2I>();
			HashSet<Vector2I> visitedSet = new HashSet<Vector2I>();
			var chosenDirection = (string)null;
			bool couldMove = true;
			while (currentPos != targetPosition && !cancelMoveRequested && steps < maxSteps && !IsStuck && Battery > 0 && currentExplorMode != ExplorMode.None)
			{
				if (!couldMove)
				{
					chosenDirection = GetPerpendicularDirection(chosenDirection);
					Vector2I nextPos = currentPos;
					if (chosenDirection == MOVE_UP) nextPos += new Vector2I(0, -1);
					else if (chosenDirection == MOVE_DOWN) nextPos += new Vector2I(0, 1);
					else if (chosenDirection == MOVE_LEFT) nextPos += new Vector2I(-1, 0);
					else if (chosenDirection == MOVE_RIGHT) nextPos += new Vector2I(1, 0);
					var limit = 0;
					while (limit < 20 && visitedSet.Contains(nextPos) || obstacleTiles.Contains(nextPos))
					{
						chosenDirection = buildingManager.GetRandomDirection();
						if (chosenDirection == MOVE_UP) nextPos += new Vector2I(0, -1);
						else if (chosenDirection == MOVE_DOWN) nextPos += new Vector2I(0, 1);
						else if (chosenDirection == MOVE_LEFT) nextPos += new Vector2I(-1, 0);
						else if (chosenDirection == MOVE_RIGHT) nextPos += new Vector2I(1, 0);
						limit++;
					}
				}
				else
				{
					// Plan only the next move
					var nextMoves = GetMovesToReachTile(currentPos, targetPosition);
					// Filter out moves that would revisit a recently visited tile
					Vector2I nextPos = currentPos;
					if (nextMoves[0] == MOVE_UP) nextPos += new Vector2I(0, -1);
					else if (nextMoves[0] == MOVE_DOWN) nextPos += new Vector2I(0, 1);
					else if (nextMoves[0] == MOVE_LEFT) nextPos += new Vector2I(-1, 0);
					else if (nextMoves[0] == MOVE_RIGHT) nextPos += new Vector2I(1, 0);
					if (!visitedSet.Contains(nextPos) || !obstacleTiles.Contains(nextPos))
					{
						chosenDirection = nextMoves[0];
					}
					else
					{
						var limit = 0;
						while (limit < 20 && (visitedSet.Contains(nextPos) || obstacleTiles.Contains(nextPos)))
						{
							chosenDirection = buildingManager.GetRandomDirection(chosenDirection);
							if (chosenDirection == MOVE_UP) nextPos = currentPos + new Vector2I(0, -1);
							else if (chosenDirection == MOVE_DOWN) nextPos = currentPos + new Vector2I(0, 1);
							else if (chosenDirection == MOVE_LEFT) nextPos = currentPos + new Vector2I(-1, 0);
							else if (chosenDirection == MOVE_RIGHT) nextPos = currentPos + new Vector2I(1, 0);
							limit++;
						}
					}
				}
				var attemptPos = currentPos;
				if (chosenDirection == MOVE_UP) attemptPos += new Vector2I(0, -1);
				else if (chosenDirection == MOVE_DOWN) attemptPos += new Vector2I(0, 1);
				else if (chosenDirection == MOVE_LEFT) attemptPos += new Vector2I(-1, 0);
				else if (chosenDirection == MOVE_RIGHT) attemptPos += new Vector2I(1, 0);
				couldMove = buildingManager.MoveInDirectionAutomated(this, chosenDirection);
				obstacleTiles.Add(attemptPos);
				await ToSignal(GetTree().CreateTimer(BuildingResource.moveInterval), "timeout");
				Vector2I newPos = GetGridCellPosition();
				// Mark as visited
				visited.Enqueue(newPos);
				visitedSet.Add(newPos);
				if (visited.Count > visitedLimit)
				{
					var old = visited.Dequeue();
					visitedSet.Remove(old);
				}
				currentPos = newPos;
				steps++;
			}
		}
		*/
			currentExplorMode = ExplorMode.None;
		CanMove = true; // Reset in case it wasn't already
		buildingManager.ClearAllPaintedTiles();
		EmitSignal(SignalName.ModeChanged, currentExplorMode.ToString());
	}

	private string GetPerpendicularDirection(string direction)
	{
		return direction switch
		{
			"move_up" or "move_down" => (new Random().Next(0, 2) == 0) ? "move_left" : "move_right",
			"move_left" or "move_right" => (new Random().Next(0, 2) == 0) ? "move_up" : "move_down",
			_ => direction,
		};
	}

	public void ReturnToBase()
	{
		DetachRobot();
		var myBase = GetBaseBuilding(this).FirstOrDefault();
		var basePosition = myBase.GetGridCellPosition();
		basePosition.X += 2; // Adjust to drop off position
		basePosition.Y += 3;
		MoveAlongPath(basePosition, true);
	}

	public List<string> GetMovesToReachTile(Vector2I currentPosition, Vector2I targetPosition)
	{
		if (currentPosition == targetPosition)
		{
			return new List<string>(); // Already at the target
		}
		bool isWood = gridManager.GetTileCustomData(targetPosition, "is_wood").Item2;
		if (gridManager.IsTileOccupied(targetPosition) || isWood)
		{
			return new List<string>(); // Target is not reachable
		}
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
    // Only allow charging if near base AND wood is available
    bool canCharge = buildingManager.gridManager.IsInBaseProximity(GetGridCellPosition()) && buildingManager.AvailableWoodCount > 0;

    if (recharging && canCharge)
    {
        IsRecharging = true;
        EmitSignal(SignalName.StartCharging);
    }
    else
    {
        IsRecharging = false;
        EmitSignal(SignalName.StopCharging);
    }
}

	public void CollectResource(string resourceType)
	{
		if (IsLifted == true) return;

		if (resourceCollected.Count < BuildingResource.ResourceCapacity)
		{
			resourceCollected.Add(resourceType);
			GameEvents.EmitCarriedResourceCountChanged(resourceCollected.Count);
		}
	}

	public void RemoveResource(string resourceType)
	{
		if (resourceCollected.Contains(resourceType))
		{
			resourceCollected.Remove(resourceType);
			GameEvents.EmitCarriedResourceCountChanged(resourceCollected.Count);
		}
	}

	public void TryDropResourcesAtBase()
	{
		if (buildingManager.gridManager.IsInBaseProximity(GetGridCellPosition()) && resourceCollected.Count > 0)
		{
			buildingManager.DropResourcesAtBase(resourceCollected);
			resourceCollected.Clear();
			GameEvents.EmitCarriedResourceCountChanged(resourceCollected.Count);
			FloatingTextManager.ShowMessageAtBuildingPosition("Resources dropped at base", this);
		}
	}


	public async void RandomExplore()
	{
		// 1. Get all tiles under antenna coverage
		var antennaTiles = buildingManager.gridManager.baseAntennaCoveredTiles; // HashSet<Vector2I>
		if (antennaTiles == null || antennaTiles.Count == 0)
		{
			GD.Print("No antenna coverage tiles found.");
			return;
		}

		// 2. Set of scanned tiles
		int scanRadius = BuildingResource.AnomalySensorRadius;

		// 3. Start from base
		var baseRect = buildingManager.gridManager.baseArea; // Rect2I
		Vector2I start = new Vector2I(baseRect.Position.X + 2, baseRect.Position.Y + 3); // Drop-off position
		Vector2I current = GetGridCellPosition();
		if (!antennaTiles.Contains(current))
			await MoveAlongPathAsync(start);

		// 4. Main exploration loop
		int maxSteps = 1000;
		int steps = 0;
		while (scannedTiles.Count < antennaTiles.Count && steps < maxSteps && Battery > 50)
		{
			// Scan all tiles within radius
			foreach (var tile in GetTilesWithinDistance(current, scanRadius))
			{
				if (antennaTiles.Contains(tile))
					scannedTiles.Add(tile);
			}

			// Find next best tile to move to (greedy: maximizes new coverage)
			Vector2I? bestNext = null;
			int bestNew = 0;
			foreach (var candidate in antennaTiles)
			{
				if (candidate == current) continue;
				// Only consider reachable tiles
				var path = GetMovesToReachTile(current, candidate);
				if (path.Count == 0) continue;
				// How many new tiles would be scanned from there?
				int newTiles = 0;
				foreach (var t in GetTilesWithinDistance(candidate, scanRadius))
					if (antennaTiles.Contains(t) && !scannedTiles.Contains(t)) newTiles++;
				if (newTiles > bestNew)
				{
					bestNew = newTiles;
					bestNext = candidate;
				}
			}
			if (bestNext == null || bestNew == 0)
				break; // No more progress

			await MoveAlongPathAsync(bestNext.Value);
			current = GetGridCellPosition();
			steps++;
		}
		GD.Print($"RandomExplore finished. Scanned {scannedTiles.Count} / {antennaTiles.Count} tiles.");
	}

	// Helper: awaitable MoveAlongPath
	private async System.Threading.Tasks.Task MoveAlongPathAsync(Vector2I targetPosition)
	{
		var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
		MoveAlongPath(targetPosition, true);
		// Wait until robot arrives at target (simple polling)
		while (GetGridCellPosition() != targetPosition || currentExplorMode == ExplorMode.MoveToPos)
			await ToSignal(GetTree().CreateTimer(0.1f), "timeout");
		tcs.TrySetResult(true);
		await tcs.Task;
	}


	public async void GradientSearch()
	{
		DetachRobot();
		// Cancel any previous gradient search
		cancelMoveRequested = true;
		await ToSignal(GetTree().CreateTimer(0.01f), "timeout"); // Give time for previous to exit

		cancelMoveRequested = false; // Reset for this search

		if (currentExplorMode == ExplorMode.GradientSearch && !IsStuck)
		{
			bool reachedMaxima = false;
			var currentPosition = GetGridCellPosition();
			
			// Oscillation detection: track recent positions
			Queue<Vector2I> recentPositions = new Queue<Vector2I>();
			int oscillationWindowSize = 10; // Check last 10 positions
			int oscillationThreshold = 2; // If we revisit a position 2+ times in the window, it's oscillating

			while (currentExplorMode == ExplorMode.GradientSearch && !reachedMaxima && Battery > 0)
			{
				if (cancelMoveRequested || IsStuck || !CanMove)
				{
					currentExplorMode = ExplorMode.None;
					EmitSignal(SignalName.ModeChanged, currentExplorMode.ToString());
					CanMove = true; // Reset so the robot can move next time
					return; // Stop search
				}

				// Check for oscillation (going back and forth between same tiles)
				if (recentPositions.Count >= oscillationWindowSize)
				{
					var positionCounts = new Dictionary<Vector2I, int>();
					foreach (var pos in recentPositions)
					{
						if (positionCounts.ContainsKey(pos))
							positionCounts[pos]++;
						else
							positionCounts[pos] = 1;
					}
					
					// If any position appears multiple times, we're oscillating
					if (positionCounts.Values.Any(count => count >= oscillationThreshold))
					{
						reachedMaxima = true;
						FloatingTextManager.ShowMessageAtBuildingPosition("Oscillating - Reached Maxima", this);
						EmitSignal(SignalName.ModeChanged, "Reached Maxima (Oscillation Detected)");
						break;
					}
				}

				// Get all candidate tiles within sensor radius
				var candidateTiles = GetTilesWithinDistance(currentPosition, BuildingResource.AnomalySensorRadius);
				
				// Sort candidates by anomaly value (highest first)
				var sortedCandidates = candidateTiles
					.Select(tile => new { 
						Tile = tile, 
						Anomaly = gravitationalAnomalyMap.GetAnomalyAt(tile.X, tile.Y) 
					})
					.OrderByDescending(x => x.Anomaly)
					.ToList();

				Vector2I? targetTile = null;
				List<string> path = null;

				// Try to find a path to the highest anomaly tile, if not try next best
				foreach (var candidate in sortedCandidates)
				{
					// Skip current position
					if (candidate.Tile == currentPosition)
						continue;

					// Try to get A* path to this candidate
					var (testPath, _) = GetMovesWithAStarAndBridges(currentPosition, candidate.Tile, false);
					
					if (testPath.Count > 0)
					{
						// Found a valid path!
						targetTile = candidate.Tile;
						path = testPath;
						break;
					}
				}

				// If no path found to any candidate, we've reached a local maxima
				if (targetTile == null || path == null || path.Count == 0)
				{
					reachedMaxima = true;
					FloatingTextManager.ShowMessageAtBuildingPosition("Reached Maxima", this);
					EmitSignal(SignalName.ModeChanged, "Reached Maxima");
					break;
				}

				var nextPosition = currentPosition;
				foreach (var tile in path)
				{
					nextPosition = GetNextPosFromCurrentPos(nextPosition, tile);
					buildingManager.CreatePaintedTileAt(nextPosition);
				}
				// Execute the path move by move
				foreach (var move in path)
				{
					if (cancelMoveRequested || IsStuck || Battery <= 0 || currentExplorMode != ExplorMode.GradientSearch)
					{
						break;
					}

					CanMove = buildingManager.MoveInDirectionAutomated(this, move);
					
					if (!CanMove)
					{
						// Hit an obstacle, break and recalculate
						break;
					}

					await ToSignal(GetTree().CreateTimer(BuildingResource.moveInterval), "timeout");
					currentPosition = GetGridCellPosition();
					
					// Track position for oscillation detection
					recentPositions.Enqueue(currentPosition);
					if (recentPositions.Count > oscillationWindowSize)
					{
						recentPositions.Dequeue(); // Keep only the most recent positions
					}
				}
				buildingManager.ClearAllPaintedTiles();
			}
		}
		currentExplorMode = ExplorMode.None;
		EmitSignal(SignalName.ModeChanged, "Idle");
		CanMove = true; // Reset at the end, in case it wasn't already
	}

	private void ExtendGrapple()
	{
	// Only create the grapple if we don't already have one
	if (grappleComponent == null && !IsGrappleExtended)
	{
		var packed = GD.Load<PackedScene>("res://scenes/building/sprite/GrappleSprite2D.tscn");
		if (packed == null)
		{
			GD.PrintErr("Failed to load GrappleSprite2D scene.");
			return;
		}

		// Instantiate the scene (root may not be Sprite2D)
		var root = packed.Instantiate();
		if (root == null)
		{
			GD.PrintErr("Failed to instantiate GrappleSprite2D (root is null).");
			return;
		}

		// Add the instantiated root to this node
		AddChild(root);
		grappleRoot = root;

		// Try to find a Sprite2D to use as grappleComponent. The scene's root may be a Node2D
		Sprite2D sprite = null;
		if (root is Sprite2D rs)
		{
			sprite = rs;
		}
		else
		{
			sprite = FindFirstSprite2D(root);
		}

		// Position / z-order the instantiated node appropriately
		if (root is Node2D root2d)
		{
			root2d.Position = new Vector2(32, 32);
			// If the scene root has ZIndex, set it; otherwise try to set sprite ZIndex
			try { root2d.ZIndex = 1; } catch { }
		}
		else if (sprite != null)
		{
			sprite.Position = new Vector2(0, 0);
			try { sprite.ZIndex = 1; } catch { }
		}

		if (sprite == null)
		{
			GD.PrintErr("Grapple instantiated but no Sprite2D found in the scene. The root has been added anyway.");
		}
		else
		{
			grappleComponent = sprite;
		}

		IsGrappleExtended = true;
	}
}

	// Recursively search a node tree for the first Sprite2D instance
	private Sprite2D FindFirstSprite2D(Node node)
	{
		if (node == null) return null;
		if (node is Sprite2D s) return s;
		foreach (var childObj in node.GetChildren())
		{
			if (childObj is not Node child) continue;
			var found = FindFirstSprite2D(child);
			if (found != null) return found;
		}
		return null;
	}

	private void RetractGrapple()
	{
		if (!IsGrappleExtended) return;
		// Prefer removing the root node if we have it
		if (grappleRoot != null)
		{
			try { RemoveChild(grappleRoot); } catch { }
			grappleRoot.QueueFree();
			grappleRoot = null;
		}
		else if (grappleComponent != null)
		{
			try { RemoveChild(grappleComponent); } catch { }
			grappleComponent.QueueFree();
			grappleComponent = null;
		}
		IsGrappleExtended = false;
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

	public void SetToIdle()
	{
		EmitSignal(SignalName.ModeChanged, "Idle");
		currentExplorMode = ExplorMode.None;
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
