using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Game.Autoload;
using Game.Building;
using Game.Component;
using Game.Resources.Building;
using Game.UI;
using Godot;

namespace Game.Manager;

public partial class BuildingManager : Node
{
	private readonly StringName ACTION_LEFT_CLICK = "left_click";
	private readonly StringName ACTION_CANCEL = "cancel";
	private readonly StringName ACTION_RIGHT_CLICK = "right_click";
	private readonly StringName MOVE_UP = "move_up";
	private readonly StringName MOVE_DOWN = "move_down";
	private readonly StringName MOVE_LEFT = "move_left";
	private readonly StringName MOVE_RIGHT = "move_right";
	private readonly StringName WOOD = "wood";

	[Signal]
	public delegate void AvailableResourceCountChangedEventHandler(int availableResourceCount);
	[Signal]
	public delegate void NewMineralAnalyzedEventHandler(int mineralAnalyzedCount);
	[Signal]
	public delegate void AvailableMaterialCountChangedEventHandler(int availableMaterialCount);
	[Signal]
	public delegate void BuildingPlacedEventHandler(BuildingComponent buildingComponent, BuildingResource resource);
	[Signal]
	public delegate void BasePlacedEventHandler();
	[Signal]
	public delegate void ClockIsTickingEventHandler();
	[Signal]
	public delegate void NewRobotSelectedEventHandler(BuildingComponent buildingComponent);
	[Signal]
	public delegate void NoMoreRobotSelectedEventHandler();
	public List<Node2D> AliveRobots { get; private set; } = new();
	private HashSet<string> analyzedMineralTypes = new();
	private double clockTickTimer = 0.0;

	[Export]
	public GridManager gridManager;
	[Export]
	private GameUI gameUI;
	[Export]
	private Node2D ySortRoot;
	[Export]
	private PackedScene buildingGhostScene;

	private enum State
	{
		Normal,
		PlacingBuilding,
		RobotSelected,
		PlacingBridge
	}

	private int currentWoodCount;
	private int currentlyUsedWoodCount;
	private BuildingResource toPlaceBuildingResource;
	public Rect2I hoveredGridArea = new(Vector2I.Zero, Vector2I.One);
	private BuildingGhost buildingGhost;
	private Vector2I buildingGhostDimensions;
	private State currentState;
	private int startingWoodCount;
	private int currentMaterialCount;
	public int mineralAnalyzedCount;
	private int currentlyUsedMaterialCount;
	private int startingMaterialCount;
	public static BuildingComponent selectedBuildingComponent { get; private set; } = null;
	private static Random random = new Random();

	public int AvailableWoodCount => startingWoodCount + currentWoodCount - currentlyUsedWoodCount;
	public int AvailableMaterialCount => startingMaterialCount + currentMaterialCount - currentlyUsedMaterialCount;

	public override void _Ready()
	{
		ClearAllRobots();
		gridManager.ResourceTilesUpdated += OnResourceTilesUpdated;
		gameUI.BuildingResourceSelected += OnBuildingResourceSelected;
		GameEvents.Instance.Connect(GameEvents.SignalName.PlaceBridgeButtonPressed, Callable.From<BuildingComponent, BuildingResource>(OnPlaceBridgeButtonPressed));
		GameEvents.Instance.Connect(GameEvents.SignalName.PlaceAntennaButtonPressed, Callable.From<BuildingComponent, BuildingResource>(OnPlaceAntennaButtonPressed));
		GameEvents.Instance.Connect(GameEvents.SignalName.BuildingStuck, Callable.From<BuildingComponent>(OnBuildingStuck));


		Callable.From(() => EmitSignal(SignalName.AvailableResourceCountChanged, AvailableWoodCount)).CallDeferred();
	}

	public override void _UnhandledInput(InputEvent evt)
	{
		switch (currentState)
		{
			case State.Normal:
				if (evt.IsActionPressed(ACTION_RIGHT_CLICK))
				{
					if (selectedBuildingComponent != null)
					{
						Vector2I targetGridCell = gridManager.GetMouseGridCellPosition();
						selectedBuildingComponent.MoveAlongPath(targetGridCell, true);
						GetViewport().SetInputAsHandled();
					}
					//DestroyBuildingAtHoveredCellPosition();
					//if(selectedBuildingComponent!= null)
					//{
					//UnHighlightSelectedBuilding(selectedBuildingComponent);
					//selectedBuildingComponent = null;
					//}
					//gridManager.ClearHighlightedTiles();
					GetViewport().SetInputAsHandled();
				}
				if (evt.IsActionPressed(ACTION_LEFT_CLICK))
				{
					if (selectedBuildingComponent == null)
					{
						selectedBuildingComponent = SelectBuildingAtHoveredCellPosition();
						if (selectedBuildingComponent == null) return;
						GameEvents.EmitRobotSelected(selectedBuildingComponent);
						EmitSignal(SignalName.NewRobotSelected, selectedBuildingComponent);
						HighlightSelectedBuilding(selectedBuildingComponent);
						GetViewport().SetInputAsHandled();
					}
					else if (SelectBuildingAtHoveredCellPosition() == selectedBuildingComponent) //Clicked on the same robot
					{
						//UnHighlightSelectedBuilding(selectedBuildingComponent);
						//selectedBuildingComponent = null;
						gridManager.HighlightBuildableTiles();
						GetViewport().SetInputAsHandled();
						return;
					}
					else if (selectedBuildingComponent != null //Switch to another robot
							&& SelectBuildingAtHoveredCellPosition() != selectedBuildingComponent
							&& SelectBuildingAtHoveredCellPosition() != null
							&& !selectedBuildingComponent.IsDestroying)
					{
						UnHighlightSelectedBuilding(selectedBuildingComponent);
						selectedBuildingComponent = null;
						EmitSignal(SignalName.NoMoreRobotSelected);
						selectedBuildingComponent = SelectBuildingAtHoveredCellPosition();
						EmitSignal(SignalName.NewRobotSelected, selectedBuildingComponent);
						GameEvents.EmitRobotSelected(selectedBuildingComponent);
						HighlightSelectedBuilding(selectedBuildingComponent);
						GetViewport().SetInputAsHandled();
					}
					else if (SelectBuildingAtHoveredCellPosition() == null) //Clicked on empty space
					{
						UnHighlightSelectedBuilding(selectedBuildingComponent);
						selectedBuildingComponent = null;
						EmitSignal(SignalName.NoMoreRobotSelected);
						GetViewport().SetInputAsHandled();
					}
				}
				if (evt.IsActionPressed(MOVE_UP))
				{
					if (selectedBuildingComponent != null)
					{
						selectedBuildingComponent.Move(MOVE_UP);
						GetViewport().SetInputAsHandled();
					}
				}
				if (evt.IsActionPressed(MOVE_DOWN))
				{
					if (selectedBuildingComponent != null)
					{
						selectedBuildingComponent.Move(MOVE_DOWN);
						GetViewport().SetInputAsHandled();
					}
				}
				if (evt.IsActionPressed(MOVE_LEFT))
				{
					if (selectedBuildingComponent != null)
					{
						selectedBuildingComponent.Move(MOVE_LEFT);
						GetViewport().SetInputAsHandled();
					}
				}
				if (evt.IsActionPressed(MOVE_RIGHT))
				{
					if (selectedBuildingComponent != null)
					{
						selectedBuildingComponent.Move(MOVE_RIGHT);
						GetViewport().SetInputAsHandled();
					}
				}
				break;
			case State.PlacingBuilding:
				if (evt.IsActionPressed(ACTION_CANCEL))
				{
					ChangeState(State.Normal);
					GetViewport().SetInputAsHandled();
				}
				else if (toPlaceBuildingResource != null && evt.IsActionPressed(ACTION_LEFT_CLICK))
				{
					PlaceBuildingAtHoveredCellPosition(toPlaceBuildingResource);
					GetViewport().SetInputAsHandled();
				}
				break;
			case State.PlacingBridge:
				if (evt.IsActionPressed(ACTION_CANCEL))
				{
					ChangeState(State.Normal);
					GetViewport().SetInputAsHandled();
				}
				else if (toPlaceBuildingResource != null && evt.IsActionPressed(ACTION_LEFT_CLICK))
				{
					PlaceBridgeAtHoveredCellPosition(toPlaceBuildingResource);
					GetViewport().SetInputAsHandled();
				}
				break;
			default:
				break;
		}
	}

	public void SelectBuilding(BuildingComponent buildingComponent)
	{
		if (buildingComponent is null) return;
		if (selectedBuildingComponent == buildingComponent)
		{
			// Already selected
			return;
		}

		if (selectedBuildingComponent != null && selectedBuildingComponent != buildingComponent)
		{
			UnHighlightSelectedBuilding(selectedBuildingComponent);
			EmitSignal(SignalName.NoMoreRobotSelected);
		}

		selectedBuildingComponent = buildingComponent;
		EmitSignal(SignalName.NewRobotSelected, selectedBuildingComponent);
		HighlightSelectedBuilding(selectedBuildingComponent);
		GameEvents.EmitRobotSelected(buildingComponent);
	}

	public override void _Process(double delta)
	{
		clockTickTimer += delta;
		if (clockTickTimer >= 1.0)
		{
			clockTickTimer = 0.0;
			EmitSignal(SignalName.ClockIsTicking);
		}
		Vector2I mouseGridPosition = Vector2I.Zero;

		switch (currentState)
		{
			case State.Normal:
				mouseGridPosition = gridManager.GetMouseGridCellPosition();
				break;
			case State.PlacingBuilding:
				mouseGridPosition = gridManager.GetMouseGridCellPositionWithDimensionOffset(buildingGhostDimensions);
				buildingGhost.GlobalPosition = mouseGridPosition * 64;
				break;
			case State.PlacingBridge:
				mouseGridPosition = gridManager.GetMouseGridCellPositionWithDimensionOffset(buildingGhostDimensions);
				buildingGhost.GlobalPosition = mouseGridPosition * 64;
				break;
		}

		var rootCell = hoveredGridArea.Position;
		if (rootCell != mouseGridPosition)
		{
			hoveredGridArea.Position = mouseGridPosition;
			UpdateHoveredGridArea();
		}

	}

	public void SetStartingResourceCount(int count)
	{
		startingWoodCount = count;
		EmitSignal(SignalName.AvailableResourceCountChanged, AvailableWoodCount);
	}

	public void SetStartingMaterialCount(int count)
	{
		startingMaterialCount = count;
		EmitSignal(SignalName.AvailableMaterialCountChanged, AvailableMaterialCount);
	}

	public void DropResourcesAtBase(List<string> resourceList)
	{
		foreach (var resource in resourceList)
		{
			if (resource == "wood")
			{
				currentWoodCount++;
			}
			else if (resource == "red_ore" || resource == "green_ore" || resource == "blue_ore")
			{
				// Only count if this ore type hasn't been analyzed yet
				if (!analyzedMineralTypes.Contains(resource))
				{
					analyzedMineralTypes.Add(resource);
					mineralAnalyzedCount++;
				}
			}
		}
		EmitSignal(SignalName.AvailableResourceCountChanged, AvailableWoodCount);
		EmitSignal(SignalName.NewMineralAnalyzed, mineralAnalyzedCount);
	}

	private void UpdateGridDisplay()
	{
		gridManager.ClearHighlightedTiles();

		if (toPlaceBuildingResource.IsBase)
		{
			if (IsBasePlaceableAtArea(hoveredGridArea))
			{
				gridManager.HighlightExpandedBuildableTiles(hoveredGridArea, toPlaceBuildingResource.BuildableRadius);
				buildingGhost.SetValid();
			}
			else
			{
				buildingGhost.SetInvalid();
			}
		}
		else if (toPlaceBuildingResource.DisplayName == "Antenna")
		{
			if (selectedBuildingComponent == null) return;
			if (selectedBuildingComponent.GetTileAndAdjacent().Contains(hoveredGridArea.Position))
			{
				buildingGhost.SetValid();
			}
			else
			{
				buildingGhost.SetInvalid();
			}
		}
		else if (gridManager.IsInBaseProximity(hoveredGridArea.Position) && IsBuildingResourcePlaceableAtArea(hoveredGridArea))
		{
			gridManager.HighlightExpandedBuildableTiles(hoveredGridArea, toPlaceBuildingResource.BuildableRadius);
			gridManager.HighlightResourceTiles(hoveredGridArea, toPlaceBuildingResource.ResourceRadius);
			buildingGhost.SetValid();
			
		}
		else
		{
			buildingGhost.SetInvalid();
		}
		buildingGhost.DoHoverAnimation();
	}

	private void EmitSignalBasePlaced()
	{
		EmitSignal(SignalName.BasePlaced);
	}

	private void PlaceBuildingAtHoveredCellPosition(BuildingResource buildingResource)
	{
		if (!CanAffordRobot())
		{
			FloatingTextManager.ShowMessageAtMousePosition("Can't afford!");
			return;
		}
		if (buildingResource.IsBase)
		{
			if (!IsBasePlaceableAtArea(hoveredGridArea))
			{
				FloatingTextManager.ShowMessageAtMousePosition("Invalid placement!");
				return;
			}
			gridManager.SetBaseArea(buildingResource.Dimensions, hoveredGridArea.Position);
			CallDeferred("EmitSignalBasePlaced");
		}
		else if (!IsBuildingResourcePlaceableAtArea(hoveredGridArea))
		{
			FloatingTextManager.ShowMessageAtMousePosition("Invalid placement!");
			return;
		}
		else if (buildingResource.DisplayName == "Antenna")
		{
			if (!selectedBuildingComponent.GetTileAndAdjacent().Contains(hoveredGridArea.Position))
			{
				return;
			}
		}
		else if (!buildingResource.IsBase)
		{
			if (!gridManager.IsInBaseProximity(hoveredGridArea.Position))
			{
				FloatingTextManager.ShowMessageAtMousePosition("Too far from base!");
				return;
			}
		}

		var building = toPlaceBuildingResource.BuildingScene.Instantiate<Node2D>();
		ySortRoot.AddChild(building);

		var buildingComponent = building.GetFirstNodeOfType<BuildingComponent>();
		AliveRobots.Add(building);
		EmitSignal(SignalName.BuildingPlaced, buildingComponent, buildingResource);

		building.GlobalPosition = hoveredGridArea.Position * 64;
		building.GetFirstNodeOfType<BuildingAnimatorComponent>()?.PlayInAnimation();

		currentlyUsedMaterialCount += toPlaceBuildingResource.ResourceCost;

		ChangeState(State.Normal);
		EmitSignal(SignalName.AvailableMaterialCountChanged, AvailableMaterialCount);
	}

	private void PlaceBridgeAtHoveredCellPosition(BuildingResource buildingResource)
	{
		if (!CanAffordBridge())
		{
			FloatingTextManager.ShowMessageAtMousePosition("Need wood!");
			return;
		}
		if (!IsBridgePlaceableAtArea(hoveredGridArea))
		{
			FloatingTextManager.ShowMessageAtMousePosition("Invalid placement!");
			return;
		}

		var robotPosition = selectedBuildingComponent.GetTileArea();
		if (hoveredGridArea.Position.X == robotPosition.Position.X)
		{
			gridManager.PlaceBridgeTile(hoveredGridArea, "vertical");
		}
		else if (hoveredGridArea.Position.Y == robotPosition.Position.Y)
		{
			gridManager.PlaceBridgeTile(hoveredGridArea, "horizontal");
		}

		selectedBuildingComponent.RemoveResource("wood");
		ChangeState(State.Normal);
	}

	public void LiftInDirection(BuildingComponent liftedRobot, StringName direction)
	{
		if (liftedRobot.BuildingResource.IsAerial && liftedRobot.Battery <= 0)
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("Robot out of battery", liftedRobot);
			return;
		}

		Vector2I directionVector;
		if (direction == MOVE_UP) directionVector = new Vector2I(0, -1);
		else if (direction == MOVE_DOWN) directionVector = new Vector2I(0, 1);
		else if (direction == MOVE_LEFT) directionVector = new Vector2I(-1, 0);
		else if (direction == MOVE_RIGHT) directionVector = new Vector2I(1, 0);
		else return;

		Node2D buildingNode = (Node2D)liftedRobot.GetParent();
		var originPos = liftedRobot.GetGridCellPosition();
		var originArea = liftedRobot.GetAreaOccupied(originPos);
		//originArea.Position = new Vector2I(originArea.Position.X / 64, originArea.Position.Y / 64);
		Vector2I destinationPosition = new Vector2I((int)((buildingNode.Position.X + (directionVector.X * 64)) / 64), (int)((buildingNode.Position.Y + (directionVector.Y * 64)) / 64));
		Rect2I destinationArea = liftedRobot.GetAreaOccupiedAfterMovingFromPos(destinationPosition);

		if (!gridManager.CanMoveBuilding(liftedRobot, destinationArea))
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("liftedRobot out of antenna coverage", liftedRobot);
			return;
		}

		liftedRobot.UpdateMoveHistory(originPos, direction);

		liftedRobot.FreeOccupiedCellPosition();
		gridManager.UpdateBuildingComponentGridState(liftedRobot);

		buildingNode.Position += directionVector * 64;
		liftedRobot.Moved((Vector2I)originPos, destinationPosition);
		liftedRobot.TryDropResourcesAtBase();
		//EmitSignal(SignalName.AvailableResourceCountChanged, AvailableResourceCount);

		var test = gridManager.CanMoveBuilding(liftedRobot);
		if (!test)
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("liftedRobot out of antenna coverage", liftedRobot);
			if (direction == MOVE_DOWN) MoveInDirection(liftedRobot, MOVE_UP);
			else if (direction == MOVE_LEFT) MoveInDirection(liftedRobot, MOVE_RIGHT);
			else if (direction == MOVE_RIGHT) MoveInDirection(liftedRobot, MOVE_LEFT);
			else if (direction == MOVE_UP) MoveInDirection(liftedRobot, MOVE_DOWN);
		}

	}

	public void MoveInDirection(BuildingComponent robot, StringName direction)
	{
		if (robot.IsStuck) return;

		if (robot.Battery <= 0)
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("Robot out of battery", robot);
			return;
		}


		Vector2I directionVector;
		if (direction == MOVE_UP) directionVector = new Vector2I(0, -1);
		else if (direction == MOVE_DOWN) directionVector = new Vector2I(0, 1);
		else if (direction == MOVE_LEFT) directionVector = new Vector2I(-1, 0);
		else if (direction == MOVE_RIGHT) directionVector = new Vector2I(1, 0);
		else return;

		Node2D buildingNode = (Node2D)robot.GetParent();
		var originPos = robot.GetGridCellPosition();
		var originArea = robot.GetAreaOccupied(originPos);
		//originArea.Position = new Vector2I(originArea.Position.X / 64, originArea.Position.Y / 64);
		Vector2I destinationPosition = new Vector2I((int)((buildingNode.Position.X + (directionVector.X * 64)) / 64), (int)((buildingNode.Position.Y + (directionVector.Y * 64)) / 64));
		Rect2I destinationArea = robot.GetAreaOccupiedAfterMovingFromPos(destinationPosition);

		if (!gridManager.CanMoveBuilding(robot, destinationArea))
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("Robot out of antenna coverage", robot);
			return;
		}

		if (!IsMoveableAtArea(robot, originArea, destinationArea))
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("Can't move there!", robot);
			return;
		}

		double chance = random.NextDouble();
		if (chance <= robot.BuildingResource.StuckChancePerMove)
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("The robot got stuck while attempting to move", robot);
			robot.SetToStuck();
			return;
		}

		robot.UpdateMoveHistory(originPos, direction);

		robot.FreeOccupiedCellPosition();
		gridManager.UpdateBuildingComponentGridState(robot);

		buildingNode.Position += directionVector * 64;
		robot.Moved((Vector2I)originPos, destinationPosition);
		robot.TryDropResourcesAtBase();
		//EmitSignal(SignalName.AvailableResourceCountChanged, AvailableResourceCount);

		var test = gridManager.CanMoveBuilding(robot);
		if (!test)
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("Robot out of antenna coverage", robot);
			if (direction == MOVE_DOWN) MoveInDirection(robot, MOVE_UP);
			else if (direction == MOVE_LEFT) MoveInDirection(robot, MOVE_RIGHT);
			else if (direction == MOVE_RIGHT) MoveInDirection(robot, MOVE_LEFT);
			else if (direction == MOVE_UP) MoveInDirection(robot, MOVE_DOWN);
		}
		robot.SetToIdle();
	}


	public bool MoveInDirectionAutomated(BuildingComponent buildingComponent, StringName direction)
	{
		if (buildingComponent.IsStuck)
		{
			buildingComponent.currentExplorMode = BuildingComponent.ExplorMode.None;
			return false;
		}
		if (buildingComponent.Battery <= 0)
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("Robot out of battery", buildingComponent);
			buildingComponent.currentExplorMode = BuildingComponent.ExplorMode.None;
			return false;
		}


		Vector2I directionVector;
		if (direction == MOVE_UP) directionVector = new Vector2I(0, -1);
		else if (direction == MOVE_DOWN) directionVector = new Vector2I(0, 1);
		else if (direction == MOVE_LEFT) directionVector = new Vector2I(-1, 0);
		else if (direction == MOVE_RIGHT) directionVector = new Vector2I(1, 0);
		else return false;

		Node2D buildingNode = (Node2D)buildingComponent.GetParent();
		var originPos = buildingComponent.GetGridCellPosition();
		var originArea = buildingComponent.GetAreaOccupied(originPos);
		//originArea.Position = new Vector2I(originArea.Position.X / 64, originArea.Position.Y / 64);
		Vector2I destinationPosition = new Vector2I((int)((buildingNode.Position.X + (directionVector.X * 64)) / 64), (int)((buildingNode.Position.Y + (directionVector.Y * 64)) / 64));
		Rect2I destinationArea = buildingComponent.GetAreaOccupiedAfterMovingFromPos(destinationPosition);


		if (!gridManager.CanMoveBuilding(buildingComponent, destinationArea))
		{
			buildingComponent.CanMove = false;
			FloatingTextManager.ShowMessageAtBuildingPosition("Robot out of antenna coverage", buildingComponent);
			return false;
		}

		if (!IsMoveableAtArea(buildingComponent, originArea, destinationArea))
		{
			//FloatingTextManager.ShowMessageAtBuildingPosition("Can't move there!", buildingComponent);
			return false;
		}

		double chance = random.NextDouble();
		if (chance <= buildingComponent.BuildingResource.StuckChancePerMove)
		{
			MoveInDirectionAutomated(buildingComponent, GetRandomDirection());
			//FloatingTextManager.ShowMessageAtBuildingPosition("The robot got stuck while attempting to move", buildingComponent);
			buildingComponent.SetToStuck();
			return false;
		}

		if (buildingComponent.currentExplorMode != BuildingComponent.ExplorMode.ReturnToBase)
		{
			buildingComponent.UpdateMoveHistory(originPos, direction);
		}

		buildingComponent.FreeOccupiedCellPosition();
		gridManager.UpdateBuildingComponentGridState(buildingComponent);

		buildingNode.Position += directionVector * 64;
		buildingComponent.Moved((Vector2I)originPos, destinationPosition);
		buildingComponent.TryDropResourcesAtBase();

		//EmitSignal(SignalName.AvailableResourceCountChanged, AvailableResourceCount);

		var test = gridManager.CanMoveBuilding(buildingComponent);
		if (!test)
		{
			//FloatingTextManager.ShowMessageAtBuildingPosition("Robot out of antenna coverage", buildingComponent);
			if (direction == MOVE_DOWN) MoveInDirectionAutomated(buildingComponent, MOVE_UP);
			else if (direction == MOVE_LEFT) MoveInDirectionAutomated(buildingComponent, MOVE_RIGHT);
			else if (direction == MOVE_RIGHT) MoveInDirectionAutomated(buildingComponent, MOVE_LEFT);
			else if (direction == MOVE_UP) MoveInDirectionAutomated(buildingComponent, MOVE_DOWN);
		}
		return true;
	}



	private void DestroyBuildingAtHoveredCellPosition()
	{
		var rootCell = hoveredGridArea.Position;
		var buildingComponent = BuildingComponent.GetValidBuildingComponents(this)
			.FirstOrDefault((buildingComponent) =>
			{
				return buildingComponent.BuildingResource.IsDeletable && buildingComponent.IsTileInBuildingArea(rootCell);
			});
		if (buildingComponent == null) return;
		/*if (!gridManager.CanDestroyBuilding(buildingComponent)) 
		{
			FloatingTextManager.ShowMessageAtMousePosition("Can't destroy");
			return;
		};
		*/
		if (buildingComponent == selectedBuildingComponent)
		{
			UnHighlightSelectedBuilding(selectedBuildingComponent);
		}
		currentlyUsedWoodCount -= buildingComponent.BuildingResource.ResourceCost;
		buildingComponent.Destroy();
		BuildingManager.selectedBuildingComponent = null;
		//EmitSignal(SignalName.AvailableResourceCountChanged, AvailableResourceCount);
	}

	private BuildingComponent SelectBuildingAtHoveredCellPosition()
	{
		var rootCell = hoveredGridArea.Position;
		var buildingComponent = BuildingComponent.GetValidBuildingComponents(this)
			.FirstOrDefault((buildingComponent) =>
			{
				return !buildingComponent.BuildingResource.IsBase && buildingComponent.BuildingResource.DisplayName != "Antenna" && buildingComponent.IsTileInBuildingArea(rootCell);
			});
		if (buildingComponent == null) return null;

		//GameEvents.EmitRobotSelected(buildingComponent);
		return buildingComponent;
	}


	public void HighlightSelectedBuilding(BuildingComponent buildingComponent)
	{
		if (buildingComponent.IsStuck == true)
		{
			HighlightStuckBuilding(buildingComponent);
			return;
		}
		else
		{
			var highlightZone = buildingComponent.GetNode<ColorRect>("%HighlightZone");
			highlightZone.Visible = true;
			highlightZone.Color = Colors.Green;
			gridManager.HighlightBuildableTiles();
			gridManager.HighlightExpandedBuildableTiles(buildingComponent.GetTileArea(), buildingComponent.BuildingResource.BuildableRadius);
		}
	}

	public void HighlightStuckBuilding(BuildingComponent buildingComponent)
	{
		var highlightZone = buildingComponent.GetNode<ColorRect>("%HighlightZone");
		highlightZone.Visible = false;
		highlightZone.Color = Colors.Red;
		highlightZone.Visible = true;
		gridManager.ClearHighlightedTiles();
	}

	public void UnHighlightSelectedBuilding(BuildingComponent buildingComponent)
	{
		var highlightZone = buildingComponent.GetNode<ColorRect>("%HighlightZone");
		highlightZone.Visible = false;
		gridManager.ClearHighlightedTiles();
		GameEvents.EmitNoMoreRobotSelected(buildingComponent);
	}

	public string GetRandomDirection(string previousDir = "")
	{
		string[] directions = { MOVE_DOWN, MOVE_LEFT, MOVE_RIGHT, MOVE_UP };

		// Define opposite directions
		var oppositeDirections = new Dictionary<string, string>
		{
			{ MOVE_UP, MOVE_DOWN },
			{ MOVE_DOWN, MOVE_UP },
			{ MOVE_LEFT, MOVE_RIGHT },
			{ MOVE_RIGHT, MOVE_LEFT }
		};

		// Filter out the opposite direction if previousDir is provided
		var availableDirections = previousDir != "" && oppositeDirections.ContainsKey(previousDir)
			? directions.Where(dir => dir != oppositeDirections[previousDir]).ToArray()
			: directions;

		// Get a random direction from the remaining available directions
		int index = random.Next(availableDirections.Length);
		return availableDirections[index];
	}

	private void ClearBuildingGhost()
	{
		gridManager.ClearHighlightedTiles();

		if (IsInstanceValid(buildingGhost))
		{
			buildingGhost.QueueFree();
		}
		buildingGhost = null;
	}

	private bool CanAffordRobot()
	{
		return AvailableMaterialCount >= toPlaceBuildingResource.ResourceCost;
	}

	private bool CanAffordBridge()
	{
		return selectedBuildingComponent.resourceCollected.Contains("wood");
	}

	private bool IsBuildingResourcePlaceableAtArea(Rect2I tileArea)
	{
		var allTilesBuildable = gridManager.IsTileAreaBuildable(tileArea);
		return allTilesBuildable && CanAffordRobot();
	}

	private bool IsBasePlaceableAtArea(Rect2I tileArea)
	{
		var isBase = true;
		var allTilesBuildable = gridManager.IsTileAreaBuildable(tileArea, false, isBase);
		return allTilesBuildable;
	}

	private bool IsBridgePlaceableAtArea(Rect2I tileArea)
	{
		var robotTileAndAdjacent = selectedBuildingComponent.GetTileAndAdjacent();
		if (robotTileAndAdjacent.Contains(tileArea.Position))
		{
			return true;
		}
		else
		{
			return false;
		}
	}

	private bool IsBuildingComponentPlaceableAtArea(Rect2I tileArea)
	{
		var allTilesBuildable = gridManager.IsTileAreaBuildable(tileArea);
		return allTilesBuildable;
	}

	private bool IsMoveableAtArea(BuildingComponent buildingComponent, Rect2I originArea, Rect2I destinationArea)
	{
		var allTilesMovable = gridManager.IsBuildingMovable(buildingComponent, originArea, destinationArea);
		return allTilesMovable;
	}

	private void UpdateHoveredGridArea()
	{
		switch (currentState)
		{
			case State.Normal:
				break;
			case State.PlacingBuilding:
				UpdateGridDisplay();
				break;
		}
	}

	private void ChangeState(State toState)
	{
		switch (currentState)
		{
			case State.Normal:
				break;
			case State.PlacingBuilding:
				ClearBuildingGhost();
				toPlaceBuildingResource = null;
				break;
			case State.PlacingBridge:
				ClearBuildingGhost();
				toPlaceBuildingResource = null;
				break;
		}

		currentState = toState;

		switch (currentState)
		{
			case State.Normal:
				break;
			case State.PlacingBuilding:
				buildingGhost = buildingGhostScene.Instantiate<BuildingGhost>();
				ySortRoot.AddChild(buildingGhost);
				break;
			case State.PlacingBridge:
				gridManager.ClearHighlightedTiles();
				gridManager.HighlightBridgePlaceableTiles(selectedBuildingComponent.GetTileArea());
				buildingGhost = buildingGhostScene.Instantiate<BuildingGhost>();
				ySortRoot.AddChild(buildingGhost);
				break;
		}
	}

	private void OnResourceTilesUpdated(int resourceCount, string resourceType)
	{
		//currentResourceCount = resourceCount;
		//EmitSignal(SignalName.AvailableResourceCountChanged, AvailableResourceCount);
	}

	private void OnBuildingResourceSelected(BuildingResource buildingResource)
	{
		ChangeState(State.PlacingBuilding);
		hoveredGridArea.Size = buildingResource.Dimensions;
		var buildingSprite = buildingResource.SpriteScene.Instantiate<AnimatedSprite2D>();
		buildingGhost.AddSpriteNode(buildingSprite);
		buildingGhost.SetDimensions(buildingResource.Dimensions);
		buildingGhostDimensions = buildingResource.Dimensions;
		toPlaceBuildingResource = buildingResource;
		UpdateGridDisplay();
	}

	private void OnBuildingStuck(BuildingComponent buildingComponent)
	{
		if (BuildingManager.selectedBuildingComponent == buildingComponent)
		{
			UnHighlightSelectedBuilding(buildingComponent);
			HighlightStuckBuilding(buildingComponent);
		}
	}

	private void OnPlaceBridgeButtonPressed(BuildingComponent buildingComponent, BuildingResource buildingResource)
	{
		if (buildingComponent == null) return;
		if (buildingComponent.IsStuck)
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("Cannot place bridge while stuck", buildingComponent);
			return;
		}
		if (buildingComponent.Battery <= 0)
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("Robot out of battery", buildingComponent);
			return;
		}
		if (!buildingComponent.resourceCollected.Contains(WOOD))
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("Not enough wood to place bridge", buildingComponent);
			return;
		}
		ChangeState(State.PlacingBridge);
		hoveredGridArea.Size = new Vector2I(1, 1);
		var buildingSprite = buildingResource.SpriteScene.Instantiate<AnimatedSprite2D>();
		buildingGhost.AddSpriteNode(buildingSprite);
		buildingGhost.SetDimensions(buildingResource.Dimensions);
		buildingGhostDimensions = buildingResource.Dimensions;
		toPlaceBuildingResource = buildingResource;
		//UpdateGridDisplay();
	}

	private void OnPlaceAntennaButtonPressed(BuildingComponent buildingComponent, BuildingResource buildingResource)
	{
		if (buildingComponent == null) return;
		if (buildingComponent.IsStuck)
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("Cannot place antenna while stuck", buildingComponent);
			return;
		}
		if (buildingComponent.Battery <= 0)
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("Robot out of battery", buildingComponent);
			return;
		}
		if (AvailableMaterialCount < 1)
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("Not enough material to place antenna", buildingComponent);
			return;
		}
		ChangeState(State.PlacingBuilding);
		hoveredGridArea.Size = new Vector2I(1, 1);
		var buildingSprite = buildingResource.SpriteScene.Instantiate<AnimatedSprite2D>();
		buildingGhost.AddSpriteNode(buildingSprite);
		buildingGhost.SetDimensions(buildingResource.Dimensions);
		buildingGhostDimensions = buildingResource.Dimensions;
		toPlaceBuildingResource = buildingResource;
	}

	public void ConsumeWoodForCharging(int amount)
	{
		if (AvailableWoodCount >= amount)
		{
			currentWoodCount -= amount;
			EmitSignal(SignalName.AvailableResourceCountChanged, AvailableWoodCount);
		}
		else
		{
			FloatingTextManager.ShowMessageAtMousePosition("Not enough resources to charge!");
		}
	}

	private void ClearAllRobots()
	{
		foreach (var robot in AliveRobots)
		{
			robot.QueueFree();
		}
		AliveRobots.Clear();
		selectedBuildingComponent = null;
	}
}
