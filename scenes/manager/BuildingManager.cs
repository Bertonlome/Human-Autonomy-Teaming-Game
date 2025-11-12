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
	private readonly StringName ACTION_PAINT_PATH = "b_stroke";
	private readonly StringName ACTION_ANNOTATE_PATH = "n_stroke";
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
	public bool IsBasePlaced = false;
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
	[Export]
	private PackedScene tileGhostScene;
	[Export]
	private PackedScene paintedTileScene;
	private List<PaintedTile> paintedTiles = new();

	private enum State
	{
		Normal,
		PlacingBuilding,
		RobotSelected,
		PaintingPath,
		AnnotatingPath,
		PlacingBridge
	}

	private int currentWoodCount;
	private int currentlyUsedWoodCount;
	private BuildingResource toPlaceBuildingResource;
	public Rect2I hoveredGridArea = new(Vector2I.Zero, Vector2I.One);
	private BuildingGhost buildingGhost;
	private TileGhost tileGhost;
	private Vector2I buildingGhostDimensions;
	private Vector2I tileGhostDimensions;
	private State currentState;
	private int startingWoodCount;
	private int currentMaterialCount;
	public int mineralAnalyzedCount;
	private int currentlyUsedMaterialCount;
	private int startingMaterialCount;
	public static BuildingComponent selectedBuildingComponent { get; private set; } = null;
	private static Random random = new Random();
	private bool isPaintingWithMouse = false;
	private bool isErasingWithMouse = false;
	private Vector2I lastPaintedTile = new Vector2I(int.MinValue, int.MinValue);
	private Vector2I lastErasedTile = new Vector2I(int.MinValue, int.MinValue);

	public int AvailableWoodCount => startingWoodCount + currentWoodCount - currentlyUsedWoodCount;
	public int AvailableMaterialCount => startingMaterialCount + currentMaterialCount - currentlyUsedMaterialCount;
	public bool IsInPaintingMode => currentState == State.PaintingPath || currentState == State.AnnotatingPath;

	public override void _Ready()
	{
		ClearAllRobots();
		gridManager.ResourceTilesUpdated += OnResourceTilesUpdated;
		gameUI.BuildingResourceSelected += OnBuildingResourceSelected;
		GameEvents.Instance.Connect(GameEvents.SignalName.PlaceBridgeButtonPressed, Callable.From<BuildingComponent, BuildingResource>(OnPlaceBridgeButtonPressed));
		GameEvents.Instance.Connect(GameEvents.SignalName.PlaceAntennaButtonPressed, Callable.From<BuildingComponent, BuildingResource>(OnPlaceAntennaButtonPressed));
		GameEvents.Instance.Connect(GameEvents.SignalName.BuildingStuck, Callable.From<BuildingComponent>(OnBuildingStuck));


		Callable.From(() => EmitSignal(SignalName.AvailableResourceCountChanged, AvailableWoodCount)).CallDeferred();
		IsBasePlaced = false;
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
				if (evt.IsActionPressed(ACTION_PAINT_PATH))
				{
					if (selectedBuildingComponent != null)
					{
						ChangeState(State.PaintingPath);
						selectedBuildingComponent.paintedTiles.Clear();
						GetViewport().SetInputAsHandled();
					}
				}
				break;
			case State.PlacingBuilding:
				if (evt.IsActionPressed(ACTION_CANCEL) || evt.IsActionPressed(ACTION_RIGHT_CLICK))
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
				if (evt.IsActionPressed(ACTION_CANCEL) || evt.IsActionPressed(ACTION_RIGHT_CLICK))
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
			case State.PaintingPath:
				if (evt.IsActionPressed(ACTION_CANCEL))
				{
					ChangeState(State.Normal);
					DestroyAllPaintedTiles();
					selectedBuildingComponent.paintedTiles.Clear();
					isPaintingWithMouse = false;
					isErasingWithMouse = false;
					GetViewport().SetInputAsHandled();
				}
				else if (evt is InputEventMouseButton mouseButton)
				{
					if (mouseButton.ButtonIndex == MouseButton.Right)
					{
						if (mouseButton.Pressed)
						{
							// Start erasing
							isErasingWithMouse = true;
							Vector2I clickedGridCell = gridManager.GetMouseGridCellPosition();
							lastErasedTile = clickedGridCell;
							PaintedTile tileAtCursor = GetPaintedTileAtPosition(clickedGridCell);
							
							if (tileAtCursor != null)
							{
								ErasePaintedTileAtPosition(clickedGridCell);
							}
							GetViewport().SetInputAsHandled();
						}
						else
						{
							// Mouse button released
							isErasingWithMouse = false;
							lastErasedTile = new Vector2I(int.MinValue, int.MinValue);
						}
					}
					else if (mouseButton.ButtonIndex == MouseButton.Left)
					{
						if (mouseButton.Pressed)
						{
							if (selectedBuildingComponent != null)
							{
								if (selectedBuildingComponent != null //Switch to another robot
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
									ChangeState(State.PaintingPath);
									GetViewport().SetInputAsHandled();
								}
								else
								{
									// Start painting
									isPaintingWithMouse = true;
									Vector2I targetGridCell = gridManager.GetMouseGridCellPosition();
									lastPaintedTile = targetGridCell;
									PaintTileAtHoveredCellPosition(targetGridCell);
									GetViewport().SetInputAsHandled();
								}
							}
						}
						else
						{
							// Mouse button released
							isPaintingWithMouse = false;
							lastPaintedTile = new Vector2I(int.MinValue, int.MinValue);
						}
					}
				}
				else if (evt.IsActionPressed(ACTION_ANNOTATE_PATH))
				{
					ChangeState(State.AnnotatingPath);
				}
				break;
			case State.AnnotatingPath:
				if (evt.IsActionPressed(ACTION_CANCEL) || evt.IsActionPressed(ACTION_PAINT_PATH))
				{
					ChangeState(State.PaintingPath);
					GetViewport().SetInputAsHandled();
				}
				else if (evt.IsActionPressed(ACTION_LEFT_CLICK))
                {
					Vector2I clickedGridCell = gridManager.GetMouseGridCellPosition();
					PaintedTile clickedTile = GetPaintedTileAtPosition(clickedGridCell);
					
					if (clickedTile != null)
					{
						clickedTile.DisplayLabelEdit();
						GetViewport().SetInputAsHandled();
					}
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
			case State.PaintingPath:
				mouseGridPosition = gridManager.GetMouseGridCellPositionWithDimensionOffset(tileGhostDimensions);
				tileGhost.GlobalPosition = mouseGridPosition * 64;
				
				// Continuously paint while left mouse button is held down
				if (isPaintingWithMouse && selectedBuildingComponent != null)
				{
					Vector2I currentTile = gridManager.GetMouseGridCellPosition();
					// Only paint if we've moved to a new tile
					if (currentTile != lastPaintedTile)
					{
						lastPaintedTile = currentTile;
						PaintTileAtHoveredCellPosition(currentTile);
					}
				}
				
				// Continuously erase while right mouse button is held down
				if (isErasingWithMouse)
				{
					Vector2I currentTile = gridManager.GetMouseGridCellPosition();
					// Only erase if we've moved to a new tile
					if (currentTile != lastErasedTile)
					{
						lastErasedTile = currentTile;
						PaintedTile tileAtCursor = GetPaintedTileAtPosition(currentTile);
						if (tileAtCursor != null)
						{
							ErasePaintedTileAtPosition(currentTile);
						}
					}
				}
				break;
			case State.AnnotatingPath:
				mouseGridPosition = gridManager.GetMouseGridCellPosition();
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
		IsBasePlaced = true;
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

	private void PaintTileAtHoveredCellPosition(Vector2I targetGridCell)
	{
		if (selectedBuildingComponent == null) return;

		var paintedTile = paintedTileScene.Instantiate<PaintedTile>();
		paintedTile.GlobalPosition = targetGridCell * 64;
		ySortRoot.AddChild(paintedTile);

		// Set properties for centralized access
		paintedTile.AssociatedRobot = selectedBuildingComponent;
		paintedTile.GridPosition = targetGridCell;

		if (selectedBuildingComponent.BuildingResource.IsAerial) paintedTile.SetColor(Colors.Cyan);
		else paintedTile.SetColor(Colors.Yellow);
		paintedTile.SetNumberLabel(selectedBuildingComponent.GetNextPaintedTileNumber());

		selectedBuildingComponent.AddPaintedTile(paintedTile);
		paintedTiles.Add(paintedTile);
	}

	public List<PaintedTile> GetAllPaintedTiles()
	{
		return paintedTiles;
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

		if (gridManager.IsTileMud(destinationPosition))
		{
			//Higher chance to get stuck on mud
			double mudChance = random.NextDouble();
			if (mudChance <= robot.BuildingResource.StuckChancePerMove * 100)
			{
				MoveInDirectionAutomated(robot, GetRandomDirection());
				FloatingTextManager.ShowMessageAtBuildingPosition("Robot is stuck in the mud :-(", robot);
				robot.SetToStuck();
			}
		}
		else
		{
			double chance = random.NextDouble();
			if (chance <= robot.BuildingResource.StuckChancePerMove)
			{
				MoveInDirectionAutomated(robot, GetRandomDirection());
				FloatingTextManager.ShowMessageAtBuildingPosition("Robot is stuck in the mud :-(", robot);
				robot.SetToStuck();
			}
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


	public bool MoveInDirectionAutomated(BuildingComponent robot, StringName direction)
	{
		if (robot.IsStuck)
		{
			robot.currentExplorMode = BuildingComponent.ExplorMode.None;
			return false;
		}
		if (robot.Battery <= 0)
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("Robot out of battery", robot);
			robot.currentExplorMode = BuildingComponent.ExplorMode.None;
			return false;
		}


		Vector2I directionVector;
		if (direction == MOVE_UP) directionVector = new Vector2I(0, -1);
		else if (direction == MOVE_DOWN) directionVector = new Vector2I(0, 1);
		else if (direction == MOVE_LEFT) directionVector = new Vector2I(-1, 0);
		else if (direction == MOVE_RIGHT) directionVector = new Vector2I(1, 0);
		else return false;

		Node2D buildingNode = (Node2D)robot.GetParent();
		var originPos = robot.GetGridCellPosition();
		var originArea = robot.GetAreaOccupied(originPos);
		//originArea.Position = new Vector2I(originArea.Position.X / 64, originArea.Position.Y / 64);
		Vector2I destinationPosition = new Vector2I((int)((buildingNode.Position.X + (directionVector.X * 64)) / 64), (int)((buildingNode.Position.Y + (directionVector.Y * 64)) / 64));
		Rect2I destinationArea = robot.GetAreaOccupiedAfterMovingFromPos(destinationPosition);


		if (!gridManager.CanMoveBuilding(robot, destinationArea))
		{
			robot.CanMove = false;
			FloatingTextManager.ShowMessageAtBuildingPosition("Robot out of antenna coverage", robot);
			return false;
		}

		if (!IsMoveableAtArea(robot, originArea, destinationArea))
		{
			//FloatingTextManager.ShowMessageAtBuildingPosition("Can't move there!", buildingComponent);
			return false;
		}

		if (gridManager.IsTileMud(destinationPosition))
		{
			//Higher chance to get stuck on mud
			double mudChance = random.NextDouble();
			if (mudChance <= robot.BuildingResource.StuckChancePerMove * 100)
			{
				MoveInDirectionAutomated(robot, GetRandomDirection());
				FloatingTextManager.ShowMessageAtBuildingPosition("Robot is stuck in the mud :-(", robot);
				robot.SetToStuck();
				return false;
			}
		}
		else
		{
			double chance = random.NextDouble();
			if (chance <= robot.BuildingResource.StuckChancePerMove)
			{
				MoveInDirectionAutomated(robot, GetRandomDirection());
				FloatingTextManager.ShowMessageAtBuildingPosition("Robot is stuck in the mud :-(", robot);
				robot.SetToStuck();
				return false;
			}
		}

		if (robot.currentExplorMode != BuildingComponent.ExplorMode.ReturnToBase)
		{
			robot.UpdateMoveHistory(originPos, direction);
		}

		robot.FreeOccupiedCellPosition();
		gridManager.UpdateBuildingComponentGridState(robot);

		buildingNode.Position += directionVector * 64;
		robot.Moved((Vector2I)originPos, destinationPosition);
		robot.TryDropResourcesAtBase();

		//EmitSignal(SignalName.AvailableResourceCountChanged, AvailableResourceCount);

		var test = gridManager.CanMoveBuilding(robot);
		if (!test)
		{
			//FloatingTextManager.ShowMessageAtBuildingPosition("Robot out of antenna coverage", buildingComponent);
			if (direction == MOVE_DOWN) MoveInDirectionAutomated(robot, MOVE_UP);
			else if (direction == MOVE_LEFT) MoveInDirectionAutomated(robot, MOVE_RIGHT);
			else if (direction == MOVE_RIGHT) MoveInDirectionAutomated(robot, MOVE_LEFT);
			else if (direction == MOVE_UP) MoveInDirectionAutomated(robot, MOVE_DOWN);
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

	private void DestroyAllPaintedTiles()
	{
		foreach (var paintedTile in paintedTiles)
		{
			paintedTile.QueueFree();
		}
		paintedTiles.Clear();
	}
	
	/// <summary>
	/// Public method to clear all painted tiles (for API use)
	/// </summary>
	public void ClearAllPaintedTiles()
	{
		DestroyAllPaintedTiles();
		if (selectedBuildingComponent != null)
		{
			selectedBuildingComponent.paintedTiles.Clear();
		}
	}
	
	/// <summary>
	/// Create a painted tile at a specific grid position (for API use)
	/// </summary>
	public void CreatePaintedTileAt(Vector2I gridPosition, string annotation = "")
	{
		if (selectedBuildingComponent == null)
		{
			GD.PrintErr("No robot selected to paint path for");
			return;
		}
		
		var paintedTile = paintedTileScene.Instantiate<PaintedTile>();
		paintedTile.GlobalPosition = gridPosition * 64;
		ySortRoot.AddChild(paintedTile);
		
		// Set properties
		paintedTile.AssociatedRobot = selectedBuildingComponent;
		paintedTile.GridPosition = gridPosition;
		
		if (selectedBuildingComponent.BuildingResource.IsAerial) 
			paintedTile.SetColor(Colors.Cyan);
		else 
			paintedTile.SetColor(Colors.Yellow);
			
		paintedTile.SetNumberLabel(paintedTiles.Count + 1);
		
		// Set annotation if provided
		if (!string.IsNullOrEmpty(annotation))
		{
			paintedTile.SetAnnotation(annotation);
			paintedTile.DisplayLabelEdit();
		}
		
		selectedBuildingComponent.AddPaintedTile(paintedTile);
		paintedTiles.Add(paintedTile);
	}

	private void RenumberPaintedTiles()
	{
		// Renumber all painted tiles sequentially
		for (int i = 0; i < paintedTiles.Count; i++)
		{
			paintedTiles[i].SetNumberLabel(i + 1);
		}
		
		// Also update the robot's tile list to match
		if (selectedBuildingComponent != null)
		{
			selectedBuildingComponent.paintedTiles.Clear();
			selectedBuildingComponent.paintedTiles.AddRange(paintedTiles);
		}
	}

	private void ErasePaintedTileAtPosition(Vector2I gridPosition)
	{
		PaintedTile tileToRemove = GetPaintedTileAtPosition(gridPosition);
		if (tileToRemove != null)
		{
			// Remove from both lists
			paintedTiles.Remove(tileToRemove);
			if (selectedBuildingComponent != null)
			{
				selectedBuildingComponent.paintedTiles.Remove(tileToRemove);
			}
			
			// Destroy the tile
			tileToRemove.QueueFree();
			
			// Renumber remaining tiles
			RenumberPaintedTiles();
		}
	}

	private PaintedTile GetPaintedTileAtPosition(Vector2I gridPosition)
	{
		// Check if any painted tile is at this grid position
		foreach (var paintedTile in paintedTiles)
		{
			Vector2I tileGridPos = new Vector2I((int)(paintedTile.GlobalPosition.X / 64), (int)(paintedTile.GlobalPosition.Y / 64));
			if (tileGridPos == gridPosition)
			{
				return paintedTile;
			}
		}
		return null;
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

	private void ClearTileGhost()
	{
		gridManager.ClearHighlightedTiles();

		if (IsInstanceValid(tileGhost))
		{
			tileGhost.QueueFree();
		}
		tileGhost = null;
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
			case State.PaintingPath:
				gridManager.ClearHighlightedTiles();
				ClearTileGhost();
				ClearBuildingGhost();
				break;
		}

		currentState = toState;

		switch (currentState)
		{
			case State.Normal:
				gameUI.HideSpecialFunctions();
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
			case State.PaintingPath:
				gameUI.DisplaySpecialFunctions();
				if (selectedBuildingComponent == null) return;
				tileGhost = tileGhostScene.Instantiate<TileGhost>();
				ySortRoot.AddChild(tileGhost);
				tileGhostDimensions = new Vector2I(1, 1);
				if (selectedBuildingComponent.BuildingResource.IsAerial) tileGhost.SetColor(Colors.Cyan);
				else tileGhost.SetColor(Colors.Yellow);
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
