using System;
using System.Collections.Generic;
using System.Linq;
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

	[Signal]
	public delegate void AvailableResourceCountChangedEventHandler(int availableResourceCount);
	[Signal]
	public delegate void BasePlacedEventHandler();

	[Export]
	private GridManager gridManager;
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
		RobotSelected
	}

	private int currentResourceCount;
	private int currentlyUsedResourceCount;
	private BuildingResource toPlaceBuildingResource;
	private Rect2I hoveredGridArea = new(Vector2I.Zero, Vector2I.One);
	private BuildingGhost buildingGhost;
	private Vector2 buildingGhostDimensions;
	private State currentState;
	private int startingResourceCount;
	public static BuildingComponent selectedBuildingComponent {get; private set;} = null;
	private static Random random = new Random();

	private int AvailableResourceCount => startingResourceCount + currentResourceCount - currentlyUsedResourceCount;

	public override void _Ready()
	{
		gridManager.ResourceTilesUpdated += OnResourceTilesUpdated;
		gameUI.BuildingResourceSelected += OnBuildingResourceSelected;
		GameEvents.Instance.Connect(GameEvents.SignalName.BuildingStuck, Callable.From<BuildingComponent>(OnBuildingStuck));
		

		Callable.From(() => EmitSignal(SignalName.AvailableResourceCountChanged, AvailableResourceCount)).CallDeferred();
	}

	public override void _UnhandledInput(InputEvent evt)
	{
		switch (currentState)
		{
			case State.Normal:
				if (evt.IsActionPressed(ACTION_RIGHT_CLICK))
				{
					//DestroyBuildingAtHoveredCellPosition();
					if(selectedBuildingComponent!= null)
					{
						UnHighlightSelectedBuilding(selectedBuildingComponent);
						selectedBuildingComponent = null;
					}
					gridManager.ClearHighlightedTiles();
					GetViewport().SetInputAsHandled();
				}
				if(evt.IsActionPressed(ACTION_LEFT_CLICK))
				{
					if(selectedBuildingComponent == null) 
					{
					selectedBuildingComponent = SelectBuildingAtHoveredCellPosition();
					if(selectedBuildingComponent == null) return;
					HighlightSelectedBuilding(selectedBuildingComponent);
					GetViewport().SetInputAsHandled();
					}
					else if (SelectBuildingAtHoveredCellPosition() == selectedBuildingComponent) //Clicked on the same robot
					{
						UnHighlightSelectedBuilding(selectedBuildingComponent);
						selectedBuildingComponent = null;
						GetViewport().SetInputAsHandled();
					}
					else if(selectedBuildingComponent != null //Switch to another robot
							&& SelectBuildingAtHoveredCellPosition() != selectedBuildingComponent
							&& SelectBuildingAtHoveredCellPosition() != null
							&& !selectedBuildingComponent.IsDestroying)
					{
						UnHighlightSelectedBuilding(selectedBuildingComponent);
						selectedBuildingComponent = null;
						selectedBuildingComponent = SelectBuildingAtHoveredCellPosition();
						HighlightSelectedBuilding(selectedBuildingComponent);
						GetViewport().SetInputAsHandled();
					}
				}
				if (evt.IsActionPressed(MOVE_UP))
				{
					if (selectedBuildingComponent != null)
					{
						MoveBuildingInDirection(MOVE_UP);
						GetViewport().SetInputAsHandled();
					}
				}
				if (evt.IsActionPressed(MOVE_DOWN))
				{
					if (selectedBuildingComponent != null)
					{
						MoveBuildingInDirection(MOVE_DOWN);
						GetViewport().SetInputAsHandled();
					}
				}
				if (evt.IsActionPressed(MOVE_LEFT))
				{
					if (selectedBuildingComponent != null)
					{
						MoveBuildingInDirection(MOVE_LEFT);
						GetViewport().SetInputAsHandled();
					}
				}
				if (evt.IsActionPressed(MOVE_RIGHT))
				{
					if (selectedBuildingComponent != null)
					{
						MoveBuildingInDirection(MOVE_RIGHT);
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
			default:
				break;
		}
	}

	public override void _Process(double delta)
	{
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
		startingResourceCount = count;
	}

	private void UpdateGridDisplay()
	{
		gridManager.ClearHighlightedTiles();

		if (toPlaceBuildingResource.IsAttackBuilding())
		{
			gridManager.HighlightDangerOccupiedTiles();
			gridManager.HighlightBuildableTiles(true);
		}
		else
		{
			gridManager.HighlightBuildableTiles();
			gridManager.HighlightDangerOccupiedTiles();
		}

		if(toPlaceBuildingResource.IsBase)
		{
			if(IsBasePlaceableAtArea(hoveredGridArea))
			{
				gridManager.HighlightExpandedBuildableTiles(hoveredGridArea, toPlaceBuildingResource.BuildableRadius);
				buildingGhost.SetValid();
			}
			else
			{
				buildingGhost.SetInvalid();
			}
		}
		else if (!toPlaceBuildingResource.IsAerial)
		{
			if(!gridManager.IsInBaseProximity(hoveredGridArea.Position))
			{
				buildingGhost.SetInvalid();
			}
			else
			{
				buildingGhost.SetValid();
			}
		}
		else if (IsBuildingResourcePlaceableAtArea(hoveredGridArea))
		{
			if (toPlaceBuildingResource.IsAttackBuilding())
			{
				gridManager.HighlightAttackTiles(hoveredGridArea, toPlaceBuildingResource.AttackRadius);
			}
			else
			{
				gridManager.HighlightExpandedBuildableTiles(hoveredGridArea, toPlaceBuildingResource.BuildableRadius);
			}
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
		if(!CanAffordBuilding())
		{
			FloatingTextManager.ShowMessageAtMousePosition("Can't afford!");
			return;
		}
		if(buildingResource.IsBase)
		{
			if(!IsBasePlaceableAtArea(hoveredGridArea))
			{
				FloatingTextManager.ShowMessageAtMousePosition("Invalid placement!");
				return;
			}
			CallDeferred("EmitSignalBasePlaced");
		}
		else if(!IsBuildingResourcePlaceableAtArea(hoveredGridArea))
		{
			FloatingTextManager.ShowMessageAtMousePosition("Invalid placement!");
			return;
		}
		else if(!buildingResource.IsAerial)
		{
			if(!gridManager.IsInBaseProximity(hoveredGridArea.Position))
			{
				FloatingTextManager.ShowMessageAtMousePosition("Too far from base!");
				return;
			}
		}
		
		var building = toPlaceBuildingResource.BuildingScene.Instantiate<Node2D>();
		ySortRoot.AddChild(building);

		building.GlobalPosition = hoveredGridArea.Position * 64;
		building.GetFirstNodeOfType<BuildingAnimatorComponent>()?.PlayInAnimation();

		currentlyUsedResourceCount += toPlaceBuildingResource.ResourceCost;

		ChangeState(State.Normal);
		EmitSignal(SignalName.AvailableResourceCountChanged, AvailableResourceCount);
	}

	public void MoveBuildingInDirection(StringName direction)
	{
		if(selectedBuildingComponent.IsStuck) return;

		if (selectedBuildingComponent.Battery <= 0)
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("Robot out of battery", selectedBuildingComponent);
			return;
		}


		Vector2I directionVector;
		if(direction == MOVE_UP) directionVector = new Vector2I(0, -1);
		else if(direction == MOVE_DOWN) directionVector = new Vector2I(0,1);
		else if(direction == MOVE_LEFT) directionVector = new Vector2I(-1, 0);
		else if(direction == MOVE_RIGHT) directionVector = new Vector2I(1, 0);
		else return;

		Node2D buildingNode = (Node2D)selectedBuildingComponent.GetParent();
		var originPos = buildingNode.Position;
		var originArea = selectedBuildingComponent.GetAreaOccupied((Vector2I)originPos);
		originArea.Position = new Vector2I(originArea.Position.X / 64, originArea.Position.Y / 64);
		Vector2I destinationPosition = new Vector2I((int)((buildingNode.Position.X + (directionVector.X * 64))/64), (int)((buildingNode.Position.Y + (directionVector.Y * 64))/64));
		Rect2I destinationArea = selectedBuildingComponent.GetAreaOccupiedAfterMovingFromPos(destinationPosition);

		if(!gridManager.CanMoveBuilding(selectedBuildingComponent, destinationArea))
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("Robot out of antenna coverage", selectedBuildingComponent);
			return;
		}

		if(!IsMoveableAtArea(selectedBuildingComponent, originArea, destinationArea))
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("Can't move there!", selectedBuildingComponent);
			return;
		}

		double chance = random.NextDouble();
		if(chance <= selectedBuildingComponent.BuildingResource.StuckChancePerMove)
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("The robot got stuck while attempting to move", selectedBuildingComponent);
			selectedBuildingComponent.SetToStuck();
			return;
		}

		selectedBuildingComponent.FreeOccupiedCellPosition();
		gridManager.UpdateBuildingComponentGridState(selectedBuildingComponent);

		buildingNode.Position +=  directionVector * 64;
		selectedBuildingComponent.Moved((Vector2I)originPos, destinationPosition);

		EmitSignal(SignalName.AvailableResourceCountChanged, AvailableResourceCount);

		var test = gridManager.CanMoveBuilding(selectedBuildingComponent);
		if (!test)
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("Robot out of antenna coverage", selectedBuildingComponent);
			if(direction == MOVE_DOWN) MoveBuildingInDirection(MOVE_UP);
			else if (direction == MOVE_LEFT) MoveBuildingInDirection(MOVE_RIGHT);
			else if (direction == MOVE_RIGHT) MoveBuildingInDirection(MOVE_LEFT);
			else if (direction == MOVE_UP) MoveBuildingInDirection(MOVE_DOWN);
		}
	}

	
	public void MoveBuildingInDirectionAutomated(BuildingComponent buildingComponent, StringName direction)
	{
		if(buildingComponent.IsStuck) 
		{
			buildingComponent.IsRandomMode = false;
			return;
		}
		if (selectedBuildingComponent.Battery <= 0)
		{
			FloatingTextManager.ShowMessageAtBuildingPosition("Robot out of battery", selectedBuildingComponent);
			buildingComponent.IsRandomMode = false;
			return;
		}


		Vector2I directionVector;
		if(direction == MOVE_UP) directionVector = new Vector2I(0, -1);
		else if(direction == MOVE_DOWN) directionVector = new Vector2I(0,1);
		else if(direction == MOVE_LEFT) directionVector = new Vector2I(-1, 0);
		else if(direction == MOVE_RIGHT) directionVector = new Vector2I(1, 0);
		else return;

        Node2D buildingNode = (Node2D)buildingComponent.GetParent();
		var originPos = buildingNode.Position;
		var originArea = buildingComponent.GetAreaOccupied((Vector2I)originPos);
		originArea.Position = new Vector2I(originArea.Position.X / 64, originArea.Position.Y / 64);
		Vector2I destinationPosition = new Vector2I((int)((buildingNode.Position.X + (directionVector.X * 64))/64), (int)((buildingNode.Position.Y + (directionVector.Y * 64))/64));
        Rect2I destinationArea = buildingComponent.GetAreaOccupiedAfterMovingFromPos(destinationPosition);

		if(!gridManager.CanMoveBuilding(buildingComponent, destinationArea))
		{
            //FloatingTextManager.ShowMessageAtBuildingPosition("Robot out of antenna coverage", buildingComponent);
			return;
		}

		if(!IsMoveableAtArea(buildingComponent, originArea, destinationArea))
		{
            //FloatingTextManager.ShowMessageAtBuildingPosition("Can't move there!", buildingComponent);
			return;
		}

		double chance = random.NextDouble();
		if(chance <= buildingComponent.BuildingResource.StuckChancePerMove)
		{
            MoveBuildingInDirectionAutomated(buildingComponent, GetRandomDirection());
            //FloatingTextManager.ShowMessageAtBuildingPosition("The robot got stuck while attempting to move", buildingComponent);
            buildingComponent.SetToStuck();
			return;
		}

        buildingComponent.FreeOccupiedCellPosition();
        gridManager.UpdateBuildingComponentGridState(buildingComponent);

		buildingNode.Position +=  directionVector * 64;
        buildingComponent.Moved((Vector2I)originPos, destinationPosition);

		EmitSignal(SignalName.AvailableResourceCountChanged, AvailableResourceCount);

		var test = gridManager.CanMoveBuilding(buildingComponent);
		if (!test)
		{
            //FloatingTextManager.ShowMessageAtBuildingPosition("Robot out of antenna coverage", buildingComponent);
			if(direction == MOVE_DOWN) MoveBuildingInDirectionAutomated(buildingComponent, MOVE_UP);
			else if (direction == MOVE_LEFT) MoveBuildingInDirectionAutomated(buildingComponent, MOVE_RIGHT);
			else if (direction == MOVE_RIGHT) MoveBuildingInDirectionAutomated(buildingComponent, MOVE_LEFT);
			else if (direction == MOVE_UP) MoveBuildingInDirectionAutomated(buildingComponent, MOVE_DOWN);
		}
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
		currentlyUsedResourceCount -= buildingComponent.BuildingResource.ResourceCost;
		buildingComponent.Destroy();
        BuildingManager.selectedBuildingComponent = null;
		EmitSignal(SignalName.AvailableResourceCountChanged, AvailableResourceCount);
	}

	private BuildingComponent SelectBuildingAtHoveredCellPosition()
	{
		var rootCell = hoveredGridArea.Position;
		var buildingComponent = BuildingComponent.GetValidBuildingComponents(this)
			.FirstOrDefault((buildingComponent) =>
			{
				return !buildingComponent.BuildingResource.IsBase && buildingComponent.IsTileInBuildingArea(rootCell);
			});
		if (buildingComponent == null) return null;

		GameEvents.EmitRobotSelected(buildingComponent);
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
		string[] directions = {MOVE_DOWN, MOVE_LEFT, MOVE_RIGHT, MOVE_UP};

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

	private bool CanAffordBuilding()
	{
		return AvailableResourceCount >= toPlaceBuildingResource.ResourceCost;
	}

	private bool IsBuildingResourcePlaceableAtArea(Rect2I tileArea)
	{
		var isAttackTiles = toPlaceBuildingResource.IsAttackBuilding();
		var allTilesBuildable = gridManager.IsTileAreaBuildable(tileArea, isAttackTiles);
		return allTilesBuildable && CanAffordBuilding();
	}

	private bool IsBasePlaceableAtArea(Rect2I tileArea)
	{
		var isBase = true;
		var allTilesBuildable = gridManager.IsTileAreaBuildable(tileArea, false, isBase);
		return allTilesBuildable;
	}

	private bool IsBuildingComponentPlaceableAtArea(Rect2I tileArea)
	{
		var isAttackTiles = selectedBuildingComponent.BuildingResource.IsAttackBuilding();
		var allTilesBuildable = gridManager.IsTileAreaBuildable(tileArea, isAttackTiles);
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
		}
	}

	private void OnResourceTilesUpdated(int resourceCount)
	{
		currentResourceCount = resourceCount;
		EmitSignal(SignalName.AvailableResourceCountChanged, AvailableResourceCount);
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

}
