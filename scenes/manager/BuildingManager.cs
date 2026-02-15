using System;
using System.Collections.Generic;
using System.Diagnostics;
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
	private readonly StringName ACTION_PLUS_CLICK = "plus";
	private readonly StringName ACTION_MINUS_CLICK = "minus";
	private readonly StringName ACTION_ENTER_CLICK = "enter";
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
	
	// Global exclusion zones for A* pathfinding (LLM-specified tiles to avoid)
	public static HashSet<Vector2I> GlobalExclusionZones { get; private set; } = new();
	
	/// <summary>
	/// Set exclusion zones that A* pathfinding should avoid
	/// </summary>
	public static void SetExclusionZones(HashSet<Vector2I> exclusionZones)
	{
		GlobalExclusionZones = exclusionZones ?? new HashSet<Vector2I>();
		GD.Print($"BuildingManager: Set {GlobalExclusionZones.Count} exclusion zones for pathfinding");
	}
	
	/// <summary>
	/// Clear all exclusion zones
	/// </summary>
	public static void ClearExclusionZones()
	{
		GlobalExclusionZones.Clear();
		GD.Print("BuildingManager: Cleared all exclusion zones");
	}

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
	[Export]
	private PackedScene rakeScene;
	private List<PaintedTile> paintedTiles = new();
	// Track avoided tile positions for each robot (for path recalculation when erasing tiles)
	private Dictionary<BuildingComponent, HashSet<Vector2I>> robotAvoidedPositions = new();
	// Track required waypoint positions for each robot (for path recalculation when pushing tiles)
	private Dictionary<BuildingComponent, List<Vector2I>> robotRequiredWaypoints = new();

	private enum State
	{
		Normal,
		PlacingBuilding,
		RobotSelected,
		PaintingPath,
		AnnotatingPath,
		PlacingBridge,
		DragRake,
		PressRake,
		DroppedRake,
	}

	private int currentWoodCount;
	private int currentlyUsedWoodCount;
	private BuildingResource toPlaceBuildingResource;
	public Rect2I hoveredGridArea = new(Vector2I.Zero, Vector2I.One);
	private BuildingGhost buildingGhost;
	private TileGhost tileGhost;
	private Rake selectedRake;
	private List<Rake> placedRakes = new();
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
		gameUI.TouchedRakePanel += OnTouchedRakePanel;
		gameUI.SendPathToRobotButtonPressed += OnSendPathToRobotButtonPressed;
		GameEvents.Instance.Connect(GameEvents.SignalName.PlaceBridgeButtonPressed, Callable.From<BuildingComponent, BuildingResource>(OnPlaceBridgeButtonPressed));
		GameEvents.Instance.Connect(GameEvents.SignalName.PlaceAntennaButtonPressed, Callable.From<BuildingComponent, BuildingResource>(OnPlaceAntennaButtonPressed));
		GameEvents.Instance.Connect(GameEvents.SignalName.BuildingStuck, Callable.From<BuildingComponent>(OnBuildingStuck));

		currentState = State.Normal;

		Callable.From(() => EmitSignal(SignalName.AvailableResourceCountChanged, AvailableWoodCount)).CallDeferred();
		
		// Schedule the check after 1 second to ensure scene tree is ready
		CallDeferred(nameof(CheckAndEmitBasePlaced));
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
						// Clear avoided positions and waypoints when starting a new path
						if (robotAvoidedPositions.ContainsKey(selectedBuildingComponent))
						{
							robotAvoidedPositions[selectedBuildingComponent].Clear();
						}
						if (robotRequiredWaypoints.ContainsKey(selectedBuildingComponent))
						{
							robotRequiredWaypoints[selectedBuildingComponent].Clear();
						}
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
							// First check if there's a rake at this position
							Vector2I clickedGridCell = gridManager.GetMouseGridCellPosition();
							Rake rakeAtPosition = GetRakeAtPosition(clickedGridCell);
							
							if (rakeAtPosition != null)
							{
								// Found a rake - pick it up
								placedRakes.Remove(rakeAtPosition);
								selectedRake = rakeAtPosition;
								selectedRake.PickUp();
								ChangeState(State.DragRake);
								GetViewport().SetInputAsHandled();
							}
							else if (selectedBuildingComponent != null)
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
			case State.DragRake:
				if(evt.IsActionPressed(ACTION_CANCEL))
				{
					ChangeState(State.PaintingPath);
					if (selectedRake != null)
					{
						selectedRake.QueueFree(); // Remove the rake entirely
						selectedRake = null;
					}
					GetViewport().SetInputAsHandled();
				}
				else if (evt.IsActionPressed(ACTION_LEFT_CLICK))
				{
					ChangeState(State.PressRake);
					selectedRake.Press();
					GetViewport().SetInputAsHandled();
				}
				else if (evt.IsActionPressed(ACTION_RIGHT_CLICK))
				{
					if (selectedRake != null)
					{
						placedRakes.Add(selectedRake); // Leave rake on map in PickedUp state
						selectedRake = null;
					}
					ChangeState(State.PaintingPath);
					GetViewport().SetInputAsHandled();
				}
				else if (evt.IsActionPressed(ACTION_PLUS_CLICK))
				{
					selectedRake.SetSize(selectedRake.RakeDimension + 1);
					//selectedBuildingComponent.AdjustRakeSize(1);
					GetViewport().SetInputAsHandled();
				}
				else if (evt.IsActionPressed(ACTION_MINUS_CLICK))
				{
					selectedRake.SetSize(selectedRake.RakeDimension - 1);
					//selectedBuildingComponent.AdjustRakeSize(-1);
					GetViewport().SetInputAsHandled();
				}
				else if (evt.IsActionPressed(ACTION_ENTER_CLICK))
				{
					// Rotate rake
					selectedRake.ToggleOrientation();
					GetViewport().SetInputAsHandled();
				}
				break;
			case State.PressRake:
				if(evt.IsActionPressed(ACTION_CANCEL))
				{
					ChangeState(State.PaintingPath);
					if (selectedRake != null)
					{
						selectedRake.QueueFree(); // Remove the rake entirely
						selectedRake = null;
					}
					GetViewport().SetInputAsHandled();
				}
				else if (evt.IsActionPressed(ACTION_LEFT_CLICK))
				{
					ChangeState(State.DragRake);
					selectedRake.PickUp();
					GetViewport().SetInputAsHandled();
				}
				else if (evt.IsActionPressed(ACTION_RIGHT_CLICK))
				{
					ChangeState(State.DroppedRake);
					if (selectedRake != null)
					{
						placedRakes.Add(selectedRake); // Leave rake on map in Pressed state
						selectedRake = null;
					}
					GetViewport().SetInputAsHandled();
				}
				break;
				case State.DroppedRake:
				if(evt.IsActionPressed(ACTION_CANCEL))
				{
					ChangeState(State.PaintingPath);
				}
				else if (evt.IsActionPressed(ACTION_LEFT_CLICK))
				{
					Vector2I clickedGridCell = gridManager.GetMouseGridCellPosition();
					Rake rakeAtPosition = GetRakeAtPosition(clickedGridCell);
					if (rakeAtPosition != null)
					{
						// Found a rake - pick it up
						placedRakes.Remove(rakeAtPosition);
						selectedRake = rakeAtPosition;
					}
					ChangeState(State.DragRake);
					selectedRake.PickUp();
					GetViewport().SetInputAsHandled();
				}
				else if (evt.IsActionPressed(ACTION_RIGHT_CLICK))
				{
					//delete rake underneath
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
			case State.DragRake:
				mouseGridPosition = gridManager.GetMouseGridCellPosition();
				if (selectedRake != null)
				{
					selectedRake.SetCenteredPosition(mouseGridPosition * 64);
					hoveredGridArea.Position = mouseGridPosition;
					UpdateGridDisplay();
				}
				else
				{
					// No rake selected, go back to painting
					ChangeState(State.PaintingPath);
				}
				break;
			case State.PressRake:
				mouseGridPosition = gridManager.GetMouseGridCellPosition();
				if (selectedRake != null)
				{
					selectedRake.SetCenteredPosition(mouseGridPosition * 64);
					hoveredGridArea.Position = mouseGridPosition;
					UpdateGridDisplay();
				}
				else
				{
					// No rake selected, go back to painting
					ChangeState(State.PaintingPath);
				}
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
		// Only update grid display if we're placing a building (not in DragRake or other states)
		if (toPlaceBuildingResource == null || buildingGhost == null)
		{
			return;
		}
		
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
	
	private void CheckAndEmitBasePlaced()
	{
		var baseBuilding = BuildingComponent.GetBaseBuilding(this);
		if (baseBuilding.Any())
		{
			IsBasePlaced = true;
			EmitSignal(SignalName.BasePlaced);
		}
		else
		{
			IsBasePlaced = false;
		}
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
		bool success = false;
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
			success = gridManager.TryPlaceBridgeTile(robotPosition, hoveredGridArea, "vertical");
		}
		else if (hoveredGridArea.Position.Y == robotPosition.Position.Y)
		{
			success = gridManager.TryPlaceBridgeTile(robotPosition, hoveredGridArea, "horizontal");
		}
		if (!success)
		{
			FloatingTextManager.ShowMessageAtMousePosition("Invalid placement!");
			return;
		}
		selectedBuildingComponent.RemoveResource("wood");
		ChangeState(State.Normal);
	}

	private void PaintTileAtHoveredCellPosition(Vector2I targetGridCell)
	{
		if (selectedBuildingComponent == null) return;

		// Check if there are already painted tiles for this robot
		var currentRobotTiles = selectedBuildingComponent.paintedTiles;
		
		if (currentRobotTiles.Count > 0)
		{
			// Get the last painted tile position
			Vector2I lastTilePos = currentRobotTiles[currentRobotTiles.Count - 1].GridPosition;
			
			// Check if the new tile is adjacent (Manhattan distance = 1)
			int manhattanDistance = Math.Abs(targetGridCell.X - lastTilePos.X) + Math.Abs(targetGridCell.Y - lastTilePos.Y);
			
			if (manhattanDistance > 1)
			{
				// Not adjacent - auto-complete the path using A*
				var pathPositions = FindPathBetweenTiles(selectedBuildingComponent, lastTilePos, targetGridCell);
				
				if (pathPositions == null || pathPositions.Count == 0)
				{
					FloatingTextManager.ShowMessageAtMousePosition("Cannot reach that tile!");
					return;
				}
				
				// Paint all intermediate tiles (connecting tiles)
				// The LAST tile in the path is the target (checkpoint)
				for (int i = 0; i < pathPositions.Count; i++)
				{
					var pos = pathPositions[i];
					
					// Skip if already painted at this position
					if (GetPaintedTileAtPosition(pos) != null)
					{
						continue;
					}
					
					// Last tile in path is the target where user clicked = checkpoint
					bool isCheckpoint = (i == pathPositions.Count - 1);
					CreateAndAddPaintedTile(pos, isCheckpoint);
				}
				
				return; // Exit since we've already painted the target tile in the path
			}
		}
		else
		{
			// First tile - check if robot can reach it from current position
			Vector2I robotPos = selectedBuildingComponent.GetGridCellPosition();
			int distanceToRobot = Math.Abs(targetGridCell.X - robotPos.X) + Math.Abs(targetGridCell.Y - robotPos.Y);
			
			if (distanceToRobot > 1)
			{
				// Auto-complete path from robot to target
				var pathPositions = FindPathBetweenTiles(selectedBuildingComponent, robotPos, targetGridCell);
				
				if (pathPositions == null || pathPositions.Count == 0)
				{
					FloatingTextManager.ShowMessageAtMousePosition("Cannot reach that tile from robot!");
					return;
				}
				
				// Paint all tiles in the path (excluding robot's current position)
				// The LAST tile is the target where user clicked = checkpoint
				for (int i = 0; i < pathPositions.Count; i++)
				{
					var pos = pathPositions[i];
					
					if (pos != robotPos) // Don't paint where robot is standing
					{
						// Last tile in path is the target where user clicked = checkpoint
						bool isCheckpoint = (i == pathPositions.Count - 1);
						CreateAndAddPaintedTile(pos, isCheckpoint);
					}
				}
				
				return;
			}
		}

		// Paint the single tile (either adjacent or first tile next to robot)
		// Mark it as a checkpoint since it was directly clicked by the user
		CreateAndAddPaintedTile(targetGridCell, isCheckpoint: true);
	}

	/// <summary>
	/// Creates and adds a painted tile at the specified position
	/// </summary>
	/// <param name="isCheckpoint">True if this tile was directly clicked by user (checkpoint), false if auto-generated connecting tile</param>
	private void CreateAndAddPaintedTile(Vector2I gridPosition, bool isCheckpoint = false)
	{
		if (selectedBuildingComponent == null) return;

		var paintedTile = paintedTileScene.Instantiate<PaintedTile>();
		paintedTile.GlobalPosition = gridPosition * 64;
		ySortRoot.AddChild(paintedTile);

		// Set properties for centralized access
		paintedTile.AssociatedRobot = selectedBuildingComponent;
		paintedTile.GridPosition = gridPosition;
		paintedTile.IsCheckpoint = isCheckpoint;

		GD.Print($"[CreateAndAddPaintedTile] Creating tile at ({gridPosition.X},{gridPosition.Y}), isCheckpoint={isCheckpoint}");

		if (selectedBuildingComponent.BuildingResource.IsAerial) paintedTile.SetColor(Colors.Cyan);
		else paintedTile.SetColor(Colors.Yellow);
		paintedTile.SetNumberLabel(selectedBuildingComponent.GetNextPaintedTileNumber());

		selectedBuildingComponent.AddPaintedTile(paintedTile);
		paintedTiles.Add(paintedTile);
	}

	/// <summary>
	/// Finds a path between two tiles using A* pathfinding.
	/// Returns list of positions including the target position.
	/// </summary>
	/// <param name="excludedPositions">Optional set of positions to exclude from the path (e.g., erased tiles)</param>
	private List<Vector2I> FindPathBetweenTiles(BuildingComponent robot, Vector2I startPos, Vector2I targetPos, HashSet<Vector2I> excludedPositions = null)
	{
		// First try without bridges
		var path = FindPathBetweenTilesInternal(robot, startPos, targetPos, excludedPositions, false, null);
		
		// If no path found and robot is ground-based, try with bridge crossing
		if (path == null && !robot.BuildingResource.IsAerial)
		{
			// Check if start and target are at the same elevation level (required for bridging)
			var (startElevation, startIsElevated) = gridManager.GetElevationLayerForTile(startPos);
			var (targetElevation, targetIsElevated) = gridManager.GetElevationLayerForTile(targetPos);
			
			if (startIsElevated == targetIsElevated)
			{
				// Try pathfinding with bridge crossing allowed
				path = FindPathBetweenTilesInternal(robot, startPos, targetPos, excludedPositions, true, startIsElevated);
			}
		}
		
		return path;
	}
	
	private List<Vector2I> FindPathBetweenTilesInternal(BuildingComponent robot, Vector2I startPos, Vector2I targetPos, HashSet<Vector2I> excludedPositions, bool allowBridges, bool? bridgeElevationIsElevated)
	{
		var open = new List<PathNode>();
		var closed = new HashSet<Vector2I>();
		
		open.Add(new PathNode(startPos, null, 0, Heuristic(startPos, targetPos)));
		
		int maxIterations = 1000;
		int iteration = 0;
		
		while (open.Count > 0 && iteration < maxIterations)
		{
			iteration++;
			
			// Get node with lowest F cost
			open.Sort((a, b) => a.F.CompareTo(b.F));
			var current = open[0];
			open.RemoveAt(0);
			closed.Add(current.Position);
			
			// Check if we reached the target
			if (current.Position == targetPos)
			{
				// Reconstruct path
				var path = new List<Vector2I>();
				var node = current;
				while (node != null)
				{
					path.Add(node.Position);
					node = node.Parent;
				}
				path.Reverse();
				return path;
			}
			
			// Explore neighbors
			var neighbors = new[]
			{
				new Vector2I(current.Position.X, current.Position.Y - 1), // Up
				new Vector2I(current.Position.X, current.Position.Y + 1), // Down
				new Vector2I(current.Position.X - 1, current.Position.Y), // Left
				new Vector2I(current.Position.X + 1, current.Position.Y)  // Right
			};
			
			foreach (var neighborPos in neighbors)
			{
				if (closed.Contains(neighborPos))
				{
					continue;
				}
				
				// Skip excluded positions
				if (excludedPositions != null && excludedPositions.Contains(neighborPos))
				{
					continue;
				}
				
				// Check if this move is valid (with optional bridge consideration)
				Rect2I originArea = new Rect2I(current.Position, Vector2I.One);
				Rect2I destinationArea = new Rect2I(neighborPos, Vector2I.One);
				
				if (!gridManager.IsBuildingMovable(robot, originArea, destinationArea, allowBridges, bridgeElevationIsElevated))
				{
					continue;
				}
				
				int gCost = current.G + 1;
				int hCost = Heuristic(neighborPos, targetPos);
				
				// Check if this neighbor is already in open list
				var existingNode = open.FirstOrDefault(n => n.Position == neighborPos);
				if (existingNode != null)
				{
					// If we found a better path, update it
					if (gCost < existingNode.G)
					{
						existingNode.G = gCost;
						existingNode.Parent = current;
					}
				}
				else
				{
					// Add new node to open list
					open.Add(new PathNode(neighborPos, current, gCost, hCost));
				}
			}
		}
		
		// No path found
		return null;
	}

	public List<PaintedTile> GetAllPaintedTiles()
	{
		// Validate reachability for each painted tile using its associated robot
		foreach (var paintedTile in paintedTiles)
		{
			if (paintedTile.AssociatedRobot != null)
			{
				Rect2I robotArea = paintedTile.AssociatedRobot.GetTileArea();
				
				// Create destination area from painted tile position
				Rect2I destinationArea = new Rect2I(paintedTile.GridPosition, Vector2I.One);
				
				// Check if the associated robot can move to this tile
				bool isReachable = gridManager.IsBuildingMovable(paintedTile.AssociatedRobot, robotArea, destinationArea);
				
				// Set the reachability status on the painted tile
				paintedTile.IsReachable = isReachable;
			}
			else
			{
				// No associated robot, mark as not reachable
				paintedTile.IsReachable = false;
			}
		}
		return paintedTiles;
	}

	/// <summary>
	/// Gets all placed rakes (excluding the currently selected rake)
	/// </summary>
	public List<Rake> GetAllPlacedRakes()
	{
		return new List<Rake>(placedRakes);
	}

	/// <summary>
	/// Get contextual tiles for a list of painted tiles. Creates a minimum bounding rectangle
	/// that contains all painted tiles and checks reachability for each tile in that area.
	/// This provides the LLM with surrounding context for better path understanding.
	/// </summary>
	public List<ContextTile> GetContextualTilesForPaintedTiles(List<PaintedTile> paintedTilesList)
	{
		List<ContextTile> contextTiles = new();
		
		if (paintedTilesList == null || paintedTilesList.Count == 0)
		{
			return contextTiles;
		}
		
		// Get the robot from the first painted tile (assuming all tiles belong to same robot)
		BuildingComponent robot = paintedTilesList[0].AssociatedRobot;
		if (robot == null)
		{
			GD.PrintErr("Cannot create context tiles: painted tiles have no associated robot");
			return contextTiles;
		}
		
		Rect2I robotArea = robot.GetTileArea();
		
		// Create a dictionary for quick lookup of painted tiles by position
		Dictionary<Vector2I, PaintedTile> paintedTilePositions = new();
		foreach (var paintedTile in paintedTilesList)
		{
			paintedTilePositions[paintedTile.GridPosition] = paintedTile;
		}
		
		// Use a HashSet to avoid duplicate context tiles when squares overlap
		HashSet<Vector2I> contextTilePositions = new HashSet<Vector2I>();
		
		// Generate 4x4 square around each checkpoint tile (2 tiles in each direction)
		const int CONTEXT_RADIUS = 2; // 2 tiles in each direction = 5x5 square (center + 2 each side)
		
		foreach (var paintedTile in paintedTilesList)
		{
			Vector2I center = paintedTile.GridPosition;
			
			// Add all tiles in the square around this checkpoint
			for (int x = center.X - CONTEXT_RADIUS; x <= center.X + CONTEXT_RADIUS; x++)
			{
				for (int y = center.Y - CONTEXT_RADIUS; y <= center.Y + CONTEXT_RADIUS; y++)
				{
					contextTilePositions.Add(new Vector2I(x, y));
				}
			}
		}
		
		// Calculate bounding box for logging
		int minX = int.MaxValue, minY = int.MaxValue;
		int maxX = int.MinValue, maxY = int.MinValue;
		
		// Create ContextTile objects for each unique position
		foreach (var gridPos in contextTilePositions)
		{
			minX = Math.Min(minX, gridPos.X);
			minY = Math.Min(minY, gridPos.Y);
			maxX = Math.Max(maxX, gridPos.X);
			maxY = Math.Max(maxY, gridPos.Y);
			
			ContextTile contextTile = new ContextTile(gridPos, robot);
			
			// Check if this position has a painted tile
			if (paintedTilePositions.TryGetValue(gridPos, out PaintedTile existingPaintedTile))
			{
				contextTile.IsPaintedTile = true;
				contextTile.PaintedTileReference = existingPaintedTile;
				contextTile.IsReachable = existingPaintedTile.IsReachable;
			}
			else
			{
				// Not a painted tile, check if it's reachable
				Rect2I destinationArea = new Rect2I(gridPos, Vector2I.One);
				contextTile.IsReachable = gridManager.IsBuildingMovable(robot, robotArea, destinationArea);
			}
			
			contextTiles.Add(contextTile);
		}
		
		GD.Print($"Created {contextTiles.Count} context tiles ({paintedTilesList.Count} checkpoints) using 5x5 squares, bounding box ({minX},{minY}) to ({maxX},{maxY})");
		
		return contextTiles;
	}

	/// <summary>
	/// Manhattan distance heuristic for A* pathfinding.
	/// </summary>
	private int Heuristic(Vector2I from, Vector2I to)
	{
		return Math.Abs(to.X - from.X) + Math.Abs(to.Y - from.Y);
	}

	/// <summary>
	/// Simple node class for A* pathfinding.
	/// </summary>
	private class PathNode
	{
		public Vector2I Position;
		public PathNode Parent;
		public int G; // Cost from start
		public int H; // Heuristic cost to target
		public int F => G + H; // Total cost

		public PathNode(Vector2I position, PathNode parent, int g, int h)
		{
			Position = position;
			Parent = parent;
			G = g;
			H = h;
		}
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
				FloatingTextManager.ShowMessageAtBuildingPosition("Robot is stuck", robot);
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
				FloatingTextManager.ShowMessageAtBuildingPosition("Robot is stuck", robot);
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
	/// Clear painted tiles for a specific robot
	/// </summary>
	public void ClearPaintedTilesForRobot(BuildingComponent robot)
	{
		if (robot == null) return;
		
		// Remove and free painted tiles associated with this robot
		var tilesToRemove = paintedTiles.Where(tile => tile.AssociatedRobot == robot).ToList();
		foreach (var tile in tilesToRemove)
		{
			tile.QueueFree();
			paintedTiles.Remove(tile);
		}
		
		// Clear the robot's own list
		robot.paintedTiles.Clear();
	}
	
	/// <summary>
	/// Public method to clear all painted tiles (for API use)
	/// If a specific robot is provided, only clears that robot's tiles
	/// </summary>
	public void ClearAllPaintedTiles(BuildingComponent robot = null)
	{
		if (robot != null)
		{
			ClearPaintedTilesForRobot(robot);
		}
		else
		{
			DestroyAllPaintedTiles();
			// Clear all robots' painted tile lists
			var allRobots = BuildingComponent.GetValidBuildingComponents(gridManager)
				.Where(b => b.BuildingResource.BuildableRadius > 0);
			foreach (var r in allRobots)
			{
				r.paintedTiles.Clear();
			}
		}
	}
	
	/// <summary>
	/// Create a painted tile at a specific grid position (for API use)
	/// </summary>
	/// <param name="isCheckpoint">True if this represents a waypoint, false if connecting tile</param>
	public void CreatePaintedTileAt(Vector2I gridPosition, string annotation = "", bool isCheckpoint = false)
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
		paintedTile.IsCheckpoint = isCheckpoint; // Mark if this is a waypoint vs connecting tile
		
		// Check annotation for special coloring
		if (!string.IsNullOrEmpty(annotation))
		{
			if (annotation.Equals("LIFT", StringComparison.OrdinalIgnoreCase) || 
			    annotation.Equals("DROP", StringComparison.OrdinalIgnoreCase))
			{
				// Bright red for LIFT/DROP waypoints - these are always checkpoints
				paintedTile.IsCheckpoint = true;
				paintedTile.SetColor(Colors.Red);
			}
			else if (annotation.Equals("LIFTING", StringComparison.OrdinalIgnoreCase))
			{
				// Softer red/pink for intermediate carrying tiles
				paintedTile.SetColor(new Color(1.0f, 0.5f, 0.5f)); // Light red/pink
			}
			else
			{
				// Other annotations make it a checkpoint
				paintedTile.IsCheckpoint = true;
				// Default color for other annotations
				if (selectedBuildingComponent.BuildingResource.IsAerial) 
					paintedTile.SetColor(Colors.Cyan);
				else 
					paintedTile.SetColor(Colors.Yellow);
			}
		}
		else
		{
			// No annotation - use default robot colors
			if (selectedBuildingComponent.BuildingResource.IsAerial) 
			paintedTile.SetColor(Colors.Cyan);
		else 
			paintedTile.SetColor(Colors.Yellow);
	}
		
	paintedTile.SetNumberLabel(paintedTiles.Count + 1);
	
	// Set annotation if provided (but don't display "LIFTING" - it clutters the screen)
	if (!string.IsNullOrEmpty(annotation))
	{
		paintedTile.SetAnnotation(annotation);
		
		// Only display label for LIFT/DROP and other important annotations, not for intermediate "LIFTING" tiles
		if (!annotation.Equals("LIFTING", StringComparison.OrdinalIgnoreCase))
		{
			paintedTile.DisplayLabelEdit();
		}
	}
	
	selectedBuildingComponent.AddPaintedTile(paintedTile);
	paintedTiles.Add(paintedTile);
}	private void RenumberPaintedTiles()
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
		if (tileToRemove == null) return;
		
		var associatedRobot = tileToRemove.AssociatedRobot;
		if (associatedRobot == null || selectedBuildingComponent == null) 
		{
			// No robot associated, just remove the tile
			paintedTiles.Remove(tileToRemove);
			tileToRemove.QueueFree();
			RenumberPaintedTiles();
			return;
		}
		
		// Get all tiles for this robot
		var robotTiles = associatedRobot.paintedTiles;
		int tileIndex = robotTiles.IndexOf(tileToRemove);
		
		// Check if this is the first tile (start of path)
		if (tileIndex == 0)
		{
			// Erasing first tile - need to recalculate from robot position to second tile
			if (robotTiles.Count > 1)
			{
				// Get robot's current position
				Vector2I robotPos = associatedRobot.GetGridCellPosition();
				// The new start should be the second tile (what was after the one we're deleting)
				Vector2I newStartPos = robotTiles[1].GridPosition;
				Vector2I endPos = robotTiles[robotTiles.Count - 1].GridPosition;
				
				// Get or create the avoided positions set for this robot
				if (!robotAvoidedPositions.ContainsKey(associatedRobot))
				{
					robotAvoidedPositions[associatedRobot] = new HashSet<Vector2I>();
				}
				
				var avoidedPositions = robotAvoidedPositions[associatedRobot];
				
				// Add the erased position to avoided positions
				avoidedPositions.Add(gridPosition);
				
				// Find new path from robot to the second tile, then continue to end
				var pathToSecondTile = FindPathBetweenTiles(associatedRobot, robotPos, newStartPos, avoidedPositions);
				
				if (pathToSecondTile != null && pathToSecondTile.Count > 0)
				{
					// Clear all current tiles
					foreach (var tile in robotTiles.ToList())
					{
						paintedTiles.Remove(tile);
						tile.QueueFree();
					}
					robotTiles.Clear();
					
					// Create tiles from robot to second tile
					foreach (var pos in pathToSecondTile)
					{
						if (pos != robotPos) // Don't paint where robot is standing
						{
							CreateAndAddPaintedTile(pos);
						}
					}
					
					// Now continue from second tile to end
					var pathToEnd = FindPathBetweenTiles(associatedRobot, newStartPos, endPos, avoidedPositions);
					
					if (pathToEnd != null && pathToEnd.Count > 1)
					{
						// Skip first position (already added) and add the rest
						for (int i = 1; i < pathToEnd.Count; i++)
						{
							CreateAndAddPaintedTile(pathToEnd[i]);
						}
					}
					
					GD.Print($"Recalculated path after erasing first tile at {gridPosition}: path now connects robot to remaining waypoints");
					return;
				}
				else
				{
					FloatingTextManager.ShowMessageAtMousePosition("Cannot find path from robot!");
					return;
				}
			}
			else
			{
				// Only one tile in path, just remove it
				paintedTiles.Remove(tileToRemove);
				robotTiles.Remove(tileToRemove);
				tileToRemove.QueueFree();
				return;
			}
		}
		// Check if this is a middle tile (not first or last)
		else if (tileIndex > 0 && tileIndex < robotTiles.Count - 1)
		{
			// Get the start and end of the path
			Vector2I startPos = robotTiles[0].GridPosition;
			Vector2I endPos = robotTiles[robotTiles.Count - 1].GridPosition;
			
			// Get or create the avoided positions set for this robot
			if (!robotAvoidedPositions.ContainsKey(associatedRobot))
			{
				robotAvoidedPositions[associatedRobot] = new HashSet<Vector2I>();
			}
			
			var avoidedPositions = robotAvoidedPositions[associatedRobot];
			
			// Add the current tile being erased to avoided positions
			avoidedPositions.Add(gridPosition);
			
			// Collect all current tile positions
			var currentPathPositions = new HashSet<Vector2I>();
			foreach (var tile in robotTiles)
			{
				currentPathPositions.Add(tile.GridPosition);
			}
			
			// Find new path excluding ALL avoided positions (accumulated across multiple erasures)
			var newPath = FindPathBetweenTiles(associatedRobot, startPos, endPos, avoidedPositions);
			
			if (newPath != null && newPath.Count > 0)
			{
				// Clear all current tiles
				foreach (var tile in robotTiles.ToList())
				{
					paintedTiles.Remove(tile);
					tile.QueueFree();
				}
				robotTiles.Clear();
				
				// Create new tiles along the recalculated path
				foreach (var pos in newPath)
				{
					CreateAndAddPaintedTile(pos);
				}
				
				GD.Print($"Recalculated path after erasing tile at {gridPosition}: {newPath.Count} tiles, avoiding {avoidedPositions.Count} positions total");
				return;
			}
			else
			{
				FloatingTextManager.ShowMessageAtMousePosition("Cannot find alternative path!");
				// Don't erase if no alternative path exists
				return;
			}
		}
		
		// If it's the first or last tile, or if path recalculation didn't apply, just remove it
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
	
	/// <summary>
	/// Finds a placed rake at the given grid position
	/// </summary>
	private Rake GetRakeAtPosition(Vector2I gridPosition)
	{
		foreach (var rake in placedRakes)
		{
			// Get all grid positions covered by this rake
			var rakePositions = rake.GetRakeGridPositionsFromPosition(rake.GlobalPosition);
			if (rakePositions.Contains(gridPosition))
			{
				return rake;
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
			case State.DragRake:
				// Clear constraints when exiting rake mode
				if (toState == State.PaintingPath && selectedBuildingComponent != null)
				{
					if (robotAvoidedPositions.ContainsKey(selectedBuildingComponent))
					{
						robotAvoidedPositions[selectedBuildingComponent].Clear();
					}
					if (robotRequiredWaypoints.ContainsKey(selectedBuildingComponent))
					{
						robotRequiredWaypoints[selectedBuildingComponent].Clear();
					}
				}
				ClearTileGhost();
				break;
			case State.PressRake:
				// Clear constraints when exiting rake mode
				if (toState == State.PaintingPath && selectedBuildingComponent != null)
				{
					if (robotAvoidedPositions.ContainsKey(selectedBuildingComponent))
					{
						robotAvoidedPositions[selectedBuildingComponent].Clear();
					}
					if (robotRequiredWaypoints.ContainsKey(selectedBuildingComponent))
					{
						robotRequiredWaypoints[selectedBuildingComponent].Clear();
					}
				}
				ClearTileGhost();
				break;
		}

		currentState = toState;

		switch (currentState)
		{
			case State.Normal:
				DeleteAllRakes();
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
			case State.DragRake:
				//
				break;
			case State.PressRake:
				//
				break;
		}
	}

	private void OnResourceTilesUpdated(int resourceCount, string resourceType)
	{
		//currentResourceCount = resourceCount;
		//EmitSignal(SignalName.AvailableResourceCountChanged, AvailableResourceCount);
	}

	private void OnSendPathToRobotButtonPressed()
	{
		if (selectedBuildingComponent == null) return;
		ChangeState(State.Normal);
	}

	private void OnTouchedRakePanel()
	{
		if (currentState == State.PaintingPath)
		{
			ChangeState(State.DragRake);
			hoveredGridArea.Size = new Vector2I(1, 1);
			var rake = rakeScene.Instantiate<Rake>();
			selectedRake = rake;
			selectedRake.SetBuildingManager(this);
			selectedRake.CallDeferred("PickUp");
			ySortRoot.AddChild(rake);
		}
		else if (currentState == State.DragRake || currentState == State.PressRake)
		{
			selectedRake.QueueFree();
			selectedRake = null;
			ChangeState(State.PaintingPath);
		}
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
	
	/// <summary>
	/// Fills a gap at the specified position to maintain path continuity
	/// Called when a tile is pushed, leaving a gap in the path
	/// </summary>
	public void FillGapAtPosition(Vector2I gapPosition, BuildingComponent robot)
	{
		// Check if there's already a tile at this position
		if (GetPaintedTileAtPosition(gapPosition) != null)
		{
			return; // Gap already filled
		}
		
		// Create a new painted tile to fill the gap
		var paintedTile = paintedTileScene.Instantiate<PaintedTile>();
		paintedTile.GlobalPosition = gapPosition * 64;
		ySortRoot.AddChild(paintedTile);
		
		// Set properties for centralized access
		paintedTile.AssociatedRobot = robot;
		paintedTile.GridPosition = gapPosition;
		
		if (robot.BuildingResource.IsAerial) 
			paintedTile.SetColor(Colors.Cyan);
		else 
			paintedTile.SetColor(Colors.Yellow);
			
		paintedTile.SetNumberLabel(robot.GetNextPaintedTileNumber());
		
		// Add to robot's painted tiles list
		robot.paintedTiles.Add(paintedTile);
		paintedTiles.Add(paintedTile);
		
		GD.Print($"Filled gap at position {gapPosition}");
	}
	
	/// <summary>
	/// Recalculates the path after a tile has been pushed by the rake
	/// The path MUST pass through ALL pushed tile positions (waypoints) in order
	/// </summary>
	public void RecalculatePathAfterPush(BuildingComponent robot, Vector2I oldPosition, Vector2I requiredPosition, Vector2I startPos, Vector2I endPos)
	{
		// Get or create the required waypoints list for this robot
		if (!robotRequiredWaypoints.ContainsKey(robot))
		{
			robotRequiredWaypoints[robot] = new List<Vector2I>();
		}
		
		var waypoints = robotRequiredWaypoints[robot];
		
		// Remove the old position from waypoints if it exists there
		// (it might be there if this tile was previously pushed)
		if (waypoints.Contains(oldPosition))
		{
			// Replace it with the new position at the same index to maintain order
			int oldIndex = waypoints.IndexOf(oldPosition);
			waypoints[oldIndex] = requiredPosition;
		}
		else
		{
			// Add the new position to waypoints if not already present
			if (!waypoints.Contains(requiredPosition))
			{
				waypoints.Add(requiredPosition);
			}
		}
		
		// Clear all current tiles for this robot
		var robotTiles = robot.paintedTiles;
		foreach (var tile in robotTiles.ToList())
		{
			paintedTiles.Remove(tile);
			tile.QueueFree();
		}
		robotTiles.Clear();
		
		// Build the full path by connecting start → waypoints (in order) → end
		var fullPath = new List<Vector2I>();
		Vector2I currentPos = startPos;
		
		// Get avoided positions for this robot (from erase operations only)
		HashSet<Vector2I> avoidedPositions = null;
		if (robotAvoidedPositions.ContainsKey(robot))
		{
			avoidedPositions = robotAvoidedPositions[robot];
		}
		
		// Add path from start to first waypoint, then between all waypoints
		foreach (var waypoint in waypoints)
		{
			var segmentPath = FindPathBetweenTiles(robot, currentPos, waypoint, avoidedPositions);
			
			if (segmentPath == null || segmentPath.Count == 0)
			{
				GD.PrintErr($"Cannot find path from {currentPos} to waypoint at {waypoint}");
				return;
			}
			
			// Add segment, avoiding duplicates at connection points
			if (fullPath.Count == 0)
			{
				fullPath.AddRange(segmentPath);
			}
			else
			{
				for (int i = 1; i < segmentPath.Count; i++) // Skip first (duplicate)
				{
					fullPath.Add(segmentPath[i]);
				}
			}
			
			currentPos = waypoint;
		}
		
		// Finally, add path from last waypoint (or start if no waypoints) to end
		var finalSegment = FindPathBetweenTiles(robot, currentPos, endPos, avoidedPositions);
		
		if (finalSegment == null || finalSegment.Count == 0)
		{
			GD.PrintErr($"Cannot find path from {currentPos} to end at {endPos}");
			return;
		}
		
		// Add final segment
		if (fullPath.Count > 0 && fullPath[fullPath.Count - 1] == finalSegment[0])
		{
			for (int i = 1; i < finalSegment.Count; i++)
			{
				fullPath.Add(finalSegment[i]);
			}
		}
		else
		{
			fullPath.AddRange(finalSegment);
		}
		
		// Create tiles along the new path
		foreach (var pos in fullPath)
		{
			var paintedTile = paintedTileScene.Instantiate<PaintedTile>();
			paintedTile.GlobalPosition = pos * 64;
			ySortRoot.AddChild(paintedTile);
			
			paintedTile.AssociatedRobot = robot;
			paintedTile.GridPosition = pos;
			
			if (robot.BuildingResource.IsAerial) 
				paintedTile.SetColor(Colors.Cyan);
			else 
				paintedTile.SetColor(Colors.Yellow);
				
			paintedTile.SetNumberLabel(robot.GetNextPaintedTileNumber());
			
			robot.paintedTiles.Add(paintedTile);
			paintedTiles.Add(paintedTile);
		}
		
		int avoidedCount = avoidedPositions != null ? avoidedPositions.Count : 0;
		GD.Print($"Recalculated path after push: {fullPath.Count} tiles, passing through {waypoints.Count} waypoints, avoiding {avoidedCount} positions");
	}
	
	/// <summary>
	/// Recalculates the path after a tile has been erased/cleared by the rake
	/// Similar to the erase logic but called from Rake
	/// </summary>
	public void RecalculatePathAfterErase(BuildingComponent robot, Vector2I erasedPosition, Vector2I startPos, Vector2I endPos)
	{
		// Check if startPos is the robot's current position (not a tile)
		Vector2I robotPos = robot.GetGridCellPosition();
		bool startIsRobotPosition = (startPos == robotPos);
		
		// Get or create the avoided positions set for this robot
		if (!robotAvoidedPositions.ContainsKey(robot))
		{
			robotAvoidedPositions[robot] = new HashSet<Vector2I>();
		}
		
		var avoidedPositions = robotAvoidedPositions[robot];
		
		// Add the erased position to avoided positions
		avoidedPositions.Add(erasedPosition);
		
		// Clear all current tiles for this robot
		var robotTiles = robot.paintedTiles;
		foreach (var tile in robotTiles.ToList())
		{
			paintedTiles.Remove(tile);
			tile.QueueFree();
		}
		robotTiles.Clear();
		
		// Get waypoints for this robot if any exist
		HashSet<Vector2I> waypointsSet = null;
		if (robotRequiredWaypoints.ContainsKey(robot))
		{
			waypointsSet = new HashSet<Vector2I>(robotRequiredWaypoints[robot]);
		}
		
		// If we have waypoints, we need to route through them
		if (waypointsSet != null && waypointsSet.Count > 0)
		{
			// Build the full path by connecting start → waypoints (in order) → end
			var fullPath = new List<Vector2I>();
			Vector2I currentPos = startPos;
			
			// Add path from start to first waypoint, then between all waypoints
			foreach (var waypoint in robotRequiredWaypoints[robot])
			{
				var segmentPath = FindPathBetweenTiles(robot, currentPos, waypoint, avoidedPositions);
				
				if (segmentPath == null || segmentPath.Count == 0)
				{
					GD.PrintErr($"Cannot find path from {currentPos} to waypoint at {waypoint} while avoiding erased position");
					return;
				}
				
				// Add segment, avoiding duplicates at connection points
				if (fullPath.Count == 0)
				{
					fullPath.AddRange(segmentPath);
				}
				else
				{
					for (int i = 1; i < segmentPath.Count; i++) // Skip first (duplicate)
					{
						fullPath.Add(segmentPath[i]);
					}
				}
				
				currentPos = waypoint;
			}
			
			// Finally, add path from last waypoint to end
			var finalSegment = FindPathBetweenTiles(robot, currentPos, endPos, avoidedPositions);
			
			if (finalSegment == null || finalSegment.Count == 0)
			{
				GD.PrintErr($"Cannot find path from {currentPos} to end at {endPos} while avoiding erased position");
				return;
			}
			
			// Add final segment
			if (fullPath.Count > 0 && fullPath[fullPath.Count - 1] == finalSegment[0])
			{
				for (int i = 1; i < finalSegment.Count; i++)
				{
					fullPath.Add(finalSegment[i]);
				}
			}
			else
			{
				fullPath.AddRange(finalSegment);
			}
			
			// Create tiles along the new path (skip robot position if that's where we're starting)
			foreach (var pos in fullPath)
			{
				if (startIsRobotPosition && pos == robotPos)
				{
					continue; // Don't paint where robot is standing
				}
				
				var paintedTile = paintedTileScene.Instantiate<PaintedTile>();
				paintedTile.GlobalPosition = pos * 64;
				ySortRoot.AddChild(paintedTile);
				
				paintedTile.AssociatedRobot = robot;
				paintedTile.GridPosition = pos;
				
				if (robot.BuildingResource.IsAerial) 
					paintedTile.SetColor(Colors.Cyan);
				else 
					paintedTile.SetColor(Colors.Yellow);
					
				paintedTile.SetNumberLabel(robot.GetNextPaintedTileNumber());
				
				robot.paintedTiles.Add(paintedTile);
				paintedTiles.Add(paintedTile);
			}
			
			GD.Print($"Recalculated path after rake erase: {fullPath.Count} tiles, passing through {waypointsSet.Count} waypoints, avoiding {avoidedPositions.Count} positions");
		}
		else
		{
			// No waypoints, simple direct path avoiding erased positions
			var newPath = FindPathBetweenTiles(robot, startPos, endPos, avoidedPositions);
			
			if (newPath == null || newPath.Count == 0)
			{
				GD.PrintErr($"Cannot find path from {startPos} to {endPos} while avoiding erased position at {erasedPosition}");
				return;
			}
			
			// Create new tiles along the recalculated path (skip robot position if that's where we're starting)
			foreach (var pos in newPath)
			{
				if (startIsRobotPosition && pos == robotPos)
				{
					continue; // Don't paint where robot is standing
				}
				
				var paintedTile = paintedTileScene.Instantiate<PaintedTile>();
				paintedTile.GlobalPosition = pos * 64;
				ySortRoot.AddChild(paintedTile);
				
				paintedTile.AssociatedRobot = robot;
				paintedTile.GridPosition = pos;
				
				if (robot.BuildingResource.IsAerial) 
					paintedTile.SetColor(Colors.Cyan);
				else 
					paintedTile.SetColor(Colors.Yellow);
					
				paintedTile.SetNumberLabel(robot.GetNextPaintedTileNumber());
				
				robot.paintedTiles.Add(paintedTile);
				paintedTiles.Add(paintedTile);
			}
			
			GD.Print($"Recalculated path after rake erase: {newPath.Count} tiles, avoiding {avoidedPositions.Count} positions");
		}
	}
	
	/// <summary>
	/// Deletes a rake from the game (removes it from placedRakes and destroys it)
	/// </summary>
	public void DeleteRake(Rake rake)
	{
		if (rake == null)
		{
			GD.PrintErr("Cannot delete null rake");
			return;
		}
		
		// Remove from placed rakes list if it's there
		if (placedRakes.Contains(rake))
		{
			placedRakes.Remove(rake);
		}
		
		// If this is the currently selected rake, clear the selection
		if (selectedRake == rake)
		{
			selectedRake = null;
		}
		
		// Destroy the rake
		rake.QueueFree();
		
		GD.Print($"Deleted rake at position {rake.GridPosition}");
	}
	
	/// <summary>
	/// Deletes a rake at the specified grid position
	/// </summary>
	public void DeleteRakeAt(Vector2I gridPosition)
	{
		Rake rakeAtPosition = GetRakeAtPosition(gridPosition);
		if (rakeAtPosition != null)
		{
			DeleteRake(rakeAtPosition);
		}
	}
	
	/// <summary>
	/// Deletes all rakes from the game
	/// </summary>
	public void DeleteAllRakes()
	{
		// Create a copy of the list to avoid modification during iteration
		var rakesToDelete = new List<Rake>(placedRakes);
		
		foreach (var rake in rakesToDelete)
		{
			rake.QueueFree();
		}
		
		placedRakes.Clear();
		
		// Clear selected rake if any
		if (selectedRake != null)
		{
			selectedRake = null;
		}
		
		GD.Print($"Deleted all rakes ({rakesToDelete.Count} total)");
	}
}
