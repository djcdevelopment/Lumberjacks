using Godot;
using System.Collections.Generic;
using CommunitySurvival.Entities;

namespace CommunitySurvival.Core;

/// <summary>
/// 3D world. Spawns typed entity scenes, generates terrain from RegionProfile.
/// </summary>
public partial class World : Node3D
{
    private GameState _state;
    private PackedScene _playerScene;
    private PackedScene _treeScene;
    private MeshInstance3D _ground;
    private readonly Dictionary<string, Node3D> _entities = new();

    private static readonly HashSet<string> TreeTypes = new()
        { "tree", "natural_resource", "oak_tree", "pine_tree", "birch_tree" };

    public override void _Ready()
    {
        _state = GetNode<GameState>("/root/GameState");
        _playerScene = GD.Load<PackedScene>("res://scenes/entities/Player.tscn");
        _treeScene = GD.Load<PackedScene>("res://scenes/entities/Tree.tscn");
        _ground = GetNode<MeshInstance3D>("Ground");

        _state.EntityAdded += OnAdded;
        _state.EntityChanged += OnChanged;
        _state.EntityDataChanged += OnDataChanged;
        _state.EntityRemoved += OnRemoved;
        _state.TerrainReady += OnTerrainReady;

        GD.Print("World: ready");
        _state.ReplayEntities();
    }

    public override void _ExitTree()
    {
        _state.EntityAdded -= OnAdded;
        _state.EntityChanged -= OnChanged;
        _state.EntityDataChanged -= OnDataChanged;
        _state.EntityRemoved -= OnRemoved;
        _state.TerrainReady -= OnTerrainReady;
    }

    private void OnTerrainReady()
    {
        if (!_state.HasTerrain) return;
        // TODO: re-enable terrain mesh generation after visual debugging
        GD.Print($"World: terrain data available ({_state.GridWidth}x{_state.GridHeight}) — generation disabled for debugging");
    }

    private void OnAdded(string id, string type, Vector3 pos, float heading,
        Godot.Collections.Dictionary meta)
    {
        if (_entities.ContainsKey(id)) return;

        if (type == "player")
            SpawnPlayer(id, pos, heading, meta);
        else if (TreeTypes.Contains(type))
            SpawnTree(id, pos, heading, meta);
        else if (type == "structure")
            SpawnPlaceholder(id, pos, new Vector3(1f, 1f, 1f), new Color(0.55f, 0.35f, 0.15f));
        else
            SpawnPlaceholder(id, pos, new Vector3(1f, 1f, 1f), new Color(0.5f, 0.5f, 0.5f));
    }

    private void SpawnTree(string id, Vector3 pos, float heading, Godot.Collections.Dictionary meta)
    {
        var instance = _treeScene.Instantiate<Node3D>();
        _entities[id] = instance;
        AddChild(instance);

        meta["entity_id"] = id;
        if (instance is TreeEntity tree)
        {
            tree.Initialize(pos, heading, meta);
        }
        else
        {
            GD.PrintErr($"World: Tree {id[..8]} is {instance.GetType().Name}, NOT TreeEntity!");
            instance.Position = pos;
        }
    }

    private void SpawnPlayer(string id, Vector3 pos, float heading,
        Godot.Collections.Dictionary meta)
    {
        var instance = _playerScene.Instantiate<Node3D>();
        _entities[id] = instance;
        AddChild(instance);

        // Initialize after AddChild so node is in scene tree
        var re = instance as RemoteEntity;
        re?.Initialize(pos, heading);

        var nameStr = meta.ContainsKey("name") ? (string)meta["name"]
            : id[..System.Math.Min(8, id.Length)];
        if (instance.HasNode("Nametag"))
            instance.GetNode<Label3D>("Nametag").Text = nameStr;

        bool isLocal = id == _state.MyPlayerId;

        if (instance.HasNode("Body"))
        {
            instance.GetNode<MeshInstance3D>("Body").MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = isLocal ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.3f, 0.5f, 0.9f)
            };
        }

        if (isLocal)
        {
            if (instance.HasNode("CameraPivot/Camera3D"))
                instance.GetNode<Camera3D>("CameraPivot/Camera3D").Current = true;
            if (instance.HasNode("Nametag"))
                instance.GetNode<Label3D>("Nametag").Visible = false;
            instance.AddChild(new Player.PlayerController());
            GD.Print("World: local player spawned");
        }
        else
        {
            if (instance.HasNode("CameraPivot"))
                instance.GetNode<Node3D>("CameraPivot").Visible = false;
        }
    }

    private void SpawnPlaceholder(string id, Vector3 pos, Vector3 size, Color color)
    {
        var node = new MeshInstance3D();
        node.Mesh = new BoxMesh { Size = size };
        node.MaterialOverride = new StandardMaterial3D { AlbedoColor = color };
        AddChild(node);
        node.Position = pos + new Vector3(0, size.Y / 2f, 0);
        _entities[id] = node;
    }

    private void OnChanged(string id, Vector3 pos, Vector3 vel, float heading, long tick)
    {
        if (!_entities.TryGetValue(id, out var node)) return;
        if (node is RemoteEntity re)
            re.UpdateFromServer(pos, vel, heading, tick);
        else
            node.GlobalPosition = pos;
    }

    private void OnDataChanged(string id, string type, Godot.Collections.Dictionary meta, long tick)
    {
        if (_entities.TryGetValue(id, out var node) && node is TreeEntity tree)
            tree.UpdateFromServer(meta);
    }

    private void OnRemoved(string id)
    {
        if (_entities.Remove(id, out var node)) node.QueueFree();
    }
}
