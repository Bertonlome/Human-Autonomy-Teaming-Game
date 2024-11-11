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
	
	private Dictionary<Vector2I, Node2D> tileToDiscoveredElements = new();
	private Dictionary<Vector2I, DiscoveredElements> tiletoDarkenedElements = new();
	private Dictionary<Vector2I, string> tiletoTypeElementsString = new();

	private HashSet<Vector2I> discoveredTiles = new();
	private HashSet<Vector2I> displayedElementTiles = new();
	DiscoveredElements discoveredElements;


	public override void _Ready()
	{
		gridManager.DiscoveredTileUpdated += OnDiscoveredTileUpdated;
		discoveredElements = DiscoveredElementsScene.Instantiate<DiscoveredElements>();
		AddChild(discoveredElements);
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
