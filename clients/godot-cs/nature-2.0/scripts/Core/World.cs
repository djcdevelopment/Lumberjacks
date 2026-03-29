using Godot;
using System.Collections.Generic;
using CommunitySurvival.Entities;

namespace CommunitySurvival.Core;

/// <summary>
/// 3D world. Spawns typed entity scenes from GameState signals.
/// </summary>
public partial class World : Node3D
{
    private GameState _state;
    private PackedScene _playerScene;
    private readonly Dictionary<string, Node3D> _entities = new();

    private static readonly HashSet<string> TreeTypes = new()
        { "tree", "natural_resource", "oak_tree", "pine_tree", "birch_tree" };

    public override void _Ready()
    {
        _state = GetNode<GameState>("/root/GameState");
        _playerScene = GD.Load<PackedScene>("res://scenes/entities/Player.tscn");

        _state.EntityAdded += OnAdded;
        _state.EntityChanged += OnChanged;
        _state.EntityDataChanged += OnDataChanged;
        _state.EntityRemoved += OnRemoved;

        GD.Print("World: ready");
        _state.ReplayEntities();
    }

    public override void _ExitTree()
    {
        _state.EntityAdded -= OnAdded;
        _state.EntityChanged -= OnChanged;
        _state.EntityDataChanged -= OnDataChanged;
        _state.EntityRemoved -= OnRemoved;
    }

    private void OnAdded(string id, string type, Vector3 pos, float heading,
        Godot.Collections.Dictionary meta)
    {
        if (_entities.ContainsKey(id)) return;

        if (type == "player")
        {
            SpawnPlayer(id, pos, heading, meta);
        }
        else if (TreeTypes.Contains(type))
        {
            // Slice 6: will use Tree.tscn. For now, tall green box.
            SpawnPlaceholder(id, pos, new Vector3(0.6f, 4f, 0.6f), new Color(0.13f, 0.4f, 0.13f));
        }
        else if (type == "structure")
        {
            SpawnPlaceholder(id, pos, new Vector3(1f, 1f, 1f), new Color(0.55f, 0.35f, 0.15f));
        }
        else
        {
            SpawnPlaceholder(id, pos, new Vector3(1f, 1f, 1f), new Color(0.5f, 0.5f, 0.5f));
        }
    }

    private void SpawnPlayer(string id, Vector3 pos, float heading,
        Godot.Collections.Dictionary meta)
    {
        var instance = _playerScene.Instantiate<Node3D>();
        AddChild(instance);
        _entities[id] = instance;

        var re = instance as RemoteEntity;
        re?.Initialize(pos, heading);

        // Nametag
        var nameStr = meta.ContainsKey("name") ? (string)meta["name"]
            : id[..System.Math.Min(8, id.Length)];
        if (instance.HasNode("Nametag"))
            instance.GetNode<Label3D>("Nametag").Text = nameStr;

        bool isLocal = id == _state.MyPlayerId;

        // Body color
        if (instance.HasNode("Body"))
        {
            var body = instance.GetNode<MeshInstance3D>("Body");
            body.MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = isLocal ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.3f, 0.5f, 0.9f)
            };
        }

        if (isLocal)
        {
            // Enable camera
            if (instance.HasNode("CameraPivot/Camera3D"))
                instance.GetNode<Camera3D>("CameraPivot/Camera3D").Current = true;

            // Hide own nametag
            if (instance.HasNode("Nametag"))
                instance.GetNode<Label3D>("Nametag").Visible = false;

            // Add controller
            var ctrl = new Player.PlayerController();
            instance.AddChild(ctrl);

            GD.Print("World: local player spawned");
        }
        else
        {
            // Disable remote camera
            if (instance.HasNode("CameraPivot"))
                instance.GetNode<Node3D>("CameraPivot").Visible = false;
        }
    }

    private void SpawnPlaceholder(string id, Vector3 pos, Vector3 size, Color color)
    {
        var node = new MeshInstance3D();
        var box = new BoxMesh { Size = size };
        node.Mesh = box;
        node.MaterialOverride = new StandardMaterial3D { AlbedoColor = color };
        node.GlobalPosition = pos + new Vector3(0, size.Y / 2f, 0);
        AddChild(node);
        _entities[id] = node;
    }

    private void OnChanged(string id, Vector3 pos, Vector3 vel, float heading, long tick)
    {
        if (!_entities.TryGetValue(id, out var node)) return;

        if (node is RemoteEntity re)
            re.UpdateFromServer(pos, vel, heading, tick);
        else
            node.GlobalPosition = pos; // Snap for non-interpolated entities
    }

    private void OnDataChanged(string id, string type, Godot.Collections.Dictionary meta, long tick)
    {
        // Slice 6: TreeEntity.UpdateFromServer(meta) will go here
    }

    private void OnRemoved(string id)
    {
        if (_entities.Remove(id, out var node)) node.QueueFree();
    }
}
