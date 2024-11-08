using System;
using Game.Autoload;
using Game.Component;
using Game.Manager;
using Game.Resources.Level;
using Game.Ui;
using Game.UI;
using Godot;

namespace Game;

public partial class BaseLevel : Node
{
	private readonly StringName ESCAPE_ACTION = "escape";
	[Export]
	private PackedScene levelCompleteScreenScene;
	[Export]
	private PackedScene selectedRobotUIScene;
	[Export]
	private LevelDefinitionResource levelDefinitionResource;
	[Export]
	private PackedScene escapeMenuScene;

	private GridManager gridManager;
	private GoldMine goldMine;
	private GameCamera gameCamera;
	private Node2D baseBuilding;
	private TileMapLayer baseTerrainTilemapLayer;
	private GameUI gameUI;
	private BuildingManager buildingManager;
	private bool isComplete;
	SelectedRobotUI selectedRobotUI;

	public override void _Ready()
	{
		gridManager = GetNode<GridManager>("GridManager");
		goldMine = GetNode<GoldMine>("%GoldMine");
		gameCamera = GetNode<GameCamera>("GameCamera");
		baseTerrainTilemapLayer = GetNode<TileMapLayer>("%BaseTerrainTileMapLayer");
		baseBuilding = GetNode<Node2D>("%Base");
		gameUI = GetNode<GameUI>("GameUI");
		buildingManager = GetNode<BuildingManager>("BuildingManager");

		buildingManager.SetStartingResourceCount(levelDefinitionResource.StartingResourceCount);

		gameCamera.SetBoundingRect(baseTerrainTilemapLayer.GetUsedRect());
		gameCamera.Zoom = new Vector2((float)0.2, (float)0.2);

		gridManager.SetGoldMinePosition(gridManager.ConvertWorldPositionToTilePosition(goldMine.GlobalPosition));

		gridManager.GridStateUpdated += OnGridStateUpdated;

		GameEvents.Instance.Connect(GameEvents.SignalName.RobotSelected, Callable.From<BuildingComponent>(OnRobotSelected));
	}


    public override void _UnhandledInput(InputEvent evt)
    {
        if(evt.IsActionPressed(ESCAPE_ACTION))
		{
			var escapeMenu = escapeMenuScene.Instantiate<EscapeMenu>();
			AddChild(escapeMenu);
			GetViewport().SetInputAsHandled();
		}
    }

    private void ShowLevelComplete()
	{
		isComplete = true;
		SaveManager.SavelevelCompletion(levelDefinitionResource);
		var levelCompleteScreen = levelCompleteScreenScene.Instantiate<LevelCompleteScreen>();
		AddChild(levelCompleteScreen);
		goldMine.SetActive();
		gameUI.HideUI();
	}

	private void OnGridStateUpdated()
	{
		if(isComplete) return;
		var goldMineTilePosition = gridManager.ConvertWorldPositionToTilePosition(goldMine.GlobalPosition);
		if (gridManager.IsTilePositionInAnyBuildingRadius(goldMineTilePosition))
		{
			ShowLevelComplete();
		}
	}

	private void OnRobotSelected(BuildingComponent buildingComponent)
	{
		selectedRobotUI = selectedRobotUIScene.Instantiate<SelectedRobotUI>();
		AddChild(selectedRobotUI);
		selectedRobotUI.selectedBuildingComponent = buildingComponent;
	}
}
