using System.Collections.Generic;
using System.Linq;
using Game.UI;
using Godot;

namespace Game.Manager;

public partial class DiscoveredElementsManager : Node
{

	[Export]
	private GridManager gridManager;
	[Export]
	private PackedScene DiscoveredElementsScene;
	[Export]
	private PackedScene treeScene;
	[Export]
	private PackedScene plantScene;
	[Export]
	private PackedScene plantBigScene;
	[Export]
	private PackedScene mediumBushScene;
	[Export]
	private PackedScene bigBushScene;
	[Export]
	private PackedScene smallBushScene;
	[Export]
	private PackedScene smallRockScene;
	[Export]
	private PackedScene mediumRockScene;
	[Export]
	private PackedScene bigRockScene;
	[Export]
	private PackedScene redOreScene;
	[Export]
	private PackedScene blueOreScene;
	[Export]
	private PackedScene greenOreScene;
	[Export]
	private PackedScene mudScene;
	[Export]
	private TileMapLayer cloudLayer;
	private Dictionary<Vector2I, Node2D> tileToDiscoveredElements = new();
	private Dictionary<Vector2I, DiscoveredElements> tiletoDarkenedElements = new();
	private Dictionary<Vector2I, string> tiletoTypeElementsString = new();

	private HashSet<Vector2I> discoveredTiles = new();
	private HashSet<Vector2I> displayedElementTiles = new();
	private HashSet<Vector2I> clearedCloudTiles = new(); // Track tiles where fog has been cleared
	DiscoveredElements discoveredElements;


	public override void _Ready()
	{
		gridManager.DiscoveredTileUpdated += OnDiscoveredTileUpdated;
		gridManager.GridStateUpdated += OnGridStateUpdated;
		discoveredElements = DiscoveredElementsScene.Instantiate<DiscoveredElements>();
		AddChild(discoveredElements);
		cloudLayer = GetNode<TileMapLayer>("%CloudLayer");
	}
	
	/// <summary>
	/// Clear fog of war (cloud tiles) in the vision radius when grid state updates
	/// </summary>
	private void OnGridStateUpdated()
	{
		// Get all buildings with vision
		var allBuildings = Game.Component.BuildingComponent.GetValidBuildingComponents(this);
		
		HashSet<Vector2I> tilesToClear = new HashSet<Vector2I>();
		
		foreach (var building in allBuildings)
		{
			int visionRadius = building.BuildingResource.VisionRadius;
			if (visionRadius <= 0) continue;
			
			// Get all tiles in vision radius
			var tileArea = building.GetTileArea();
			var tilesInVision = gridManager.GetTilesInRadius(tileArea, visionRadius);
			
			// Collect tiles to clear
			foreach (var tile in tilesInVision)
			{
				if (!clearedCloudTiles.Contains(tile))
				{
					tilesToClear.Add(tile);
				}
			}
		}
		
		// Clear all tiles at once
		if (tilesToClear.Count > 0)
		{
			// Convert HashSet to Godot array for batch operation
			var tilesToClearArray = new Godot.Collections.Array<Vector2I>();
			foreach (var tile in tilesToClear)
			{
				tilesToClearArray.Add(tile);
				clearedCloudTiles.Add(tile);
			}
			
			// Use SetCellsTerrainConnect with terrain ID -1 to properly clear tiles
			// while maintaining terrain patterns on adjacent tiles
			// Args: cells array, terrain set (0), terrain ID (-1 for empty/clear)
			cloudLayer.SetCellsTerrainConnect(tilesToClearArray, 0, -1);
		}
	}

	private void UpdateIndicators(Vector2I tile, string type)
	{
		if (displayedElementTiles.Contains(tile)) return;

		var elementNode2D = discoveredElements.GetNode<Node2D>("%ElementNode2D");

		Node2D elementHolder = new Node2D();
		elementNode2D.AddChild(elementHolder);
		elementHolder.GlobalPosition = tile * 64;
		tileToDiscoveredElements[tile] = elementHolder;

		Node2D elementScene;

		switch(type)
		{
			case "plant":
				elementScene = plantScene.Instantiate<Sprite2D>();
				elementHolder.AddChild(elementScene);
				break;
			case "plant_big":
				elementScene = plantBigScene.Instantiate<Sprite2D>();
				elementHolder.AddChild(elementScene);
				break;
			case "medium_bush":
				elementScene = mediumBushScene.Instantiate<Sprite2D>();
				elementHolder.AddChild(elementScene);
				break;
			case "big_bush":
				elementScene = bigBushScene.Instantiate<Sprite2D>();
				elementHolder.AddChild(elementScene);
				break;
			case "small_bush":
				elementScene = smallBushScene.Instantiate<Sprite2D>();
				elementHolder.AddChild(elementScene);
				break;
			case "small_rock":
				elementScene = smallRockScene.Instantiate<Sprite2D>();
				elementHolder.AddChild(elementScene);
				break;
			case "medium_rock":
				elementScene = mediumRockScene.Instantiate<Sprite2D>();
				elementHolder.AddChild(elementScene);
				break;
			case "big_rock":
				elementScene = bigRockScene.Instantiate<Sprite2D>();
				elementHolder.AddChild(elementScene);
				break;
			case "tree":
				elementScene = treeScene.Instantiate<AnimatedSprite2D>();
				elementHolder.AddChild(elementScene);
				break;
			case "red_ore":
				elementScene = redOreScene.Instantiate<Sprite2D>();
				elementHolder.AddChild(elementScene);
				break;
			case "blue_ore":
				elementScene = blueOreScene.Instantiate<Sprite2D>();
				elementHolder.AddChild(elementScene);
				break;
			case "green_ore":
				elementScene = greenOreScene.Instantiate<Sprite2D>();
				elementHolder.AddChild(elementScene);
				break;
			case "mud":
				elementScene = mudScene.Instantiate<Sprite2D>();
				elementHolder.AddChild(elementScene);
				break;
		}

		displayedElementTiles.Add(tile);



		/*foreach (var removeTile in toDarkenTiles)
		{
			tileToDiscoveredElements.TryGetValue(removeTile, out var element);
			if (IsInstanceValid(element))
			{
				element.Darken();
			}
			tileToDiscoveredElements.Remove(removeTile);
			tiletoDarkenedElements[removeTile] = element;
		} */
	}

	private void HandleDiscoveredTile(Vector2I tile, string type)
	{
		var currentDiscoveredTiles = gridManager.GetDiscoveredResourceTiles();
		var newlyDiscoveredTiles = currentDiscoveredTiles.Except(discoveredTiles);
		var toDarkenTiles = discoveredTiles.Except(currentDiscoveredTiles);
		discoveredTiles = currentDiscoveredTiles;
		UpdateIndicators(tile, type);
	}

	private void OnDiscoveredTileUpdated(Vector2I tile, string type)
	{
		Callable.From(() => HandleDiscoveredTile(tile, type)).CallDeferred();
	}

}
