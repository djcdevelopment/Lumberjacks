using Godot;
using System;

namespace CommunitySurvival.Lab;

/// <summary>
/// Isolation lab. Tiny world (20x20 units), one tree, one player orb.
/// Full telemetry. No server — everything is local.
///
/// Controls:
///   WASD = move on XZ plane
///   Space = up, Ctrl = down
///   Right-click + mouse = orbit camera
///   Scroll = zoom
///   F2 = toggle wireframe overlay
///
/// HUD shows exact position, terrain height, and entity info.
/// </summary>
public partial class LabWorld : Node3D
{
    // World params
    private const float WorldSize = 20f;     // 20x20 units total
    private const int GridRes = 80;          // 80x80 grid = 0.25 units per cell — smoother terrain
    private const float MaxAltitude = 4f;    // Gentle hills, max 4 units high
    private const float PlayerRadius = 0.3f;
    private const float MoveSpeed = 5f;

    // Nodes
    private MeshInstance3D _ground;
    private MeshInstance3D _player;
    private Node3D _tree;
    private Camera3D _camera;
    private Node3D _cameraPivot;
    private Label _hud;
    private double[] _altGrid;

    // Camera
    private float _camYaw, _camPitch = -30f, _camDist = 12f;
    private bool _orbiting;
    private bool _lmbHeld, _rmbHeld;

    // Player state
    private Vector3 _playerPos;
    private float _playerFacing; // radians, 0 = -Z (Godot forward)
    private Node3D _axe;

    public override void _Ready()
    {
        // Generate altitude grid
        _altGrid = GenerateHills();

        // Build terrain mesh
        _ground = new MeshInstance3D();
        _ground.Mesh = BuildTerrainMesh();
        AddChild(_ground);

        // Grid lines for reference
        BuildGridLines();

        // Player orb
        _player = new MeshInstance3D();
        var sphere = new SphereMesh { Radius = PlayerRadius, Height = PlayerRadius * 2 };
        _player.Mesh = sphere;
        _player.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.1f, 0.9f, 0.2f),
            EmissionEnabled = true,
            Emission = new Color(0.1f, 0.5f, 0.1f),
            EmissionEnergyMultiplier = 0.3f,
        };
        // Axe handle + blade as orientation indicator
        _axe = new Node3D();
        var handle = new MeshInstance3D();
        handle.Mesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.02f, Height = 0.5f };
        handle.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.28f, 0.12f) };
        handle.RotationDegrees = new Vector3(0, 0, 90); // Horizontal
        _axe.AddChild(handle);
        var blade = new MeshInstance3D();
        blade.Mesh = new BoxMesh { Size = new Vector3(0.03f, 0.15f, 0.2f) };
        blade.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.7f, 0.7f, 0.7f), Metallic = 0.8f };
        blade.Position = new Vector3(0.25f, 0, 0);
        _axe.AddChild(blade);
        _axe.Position = new Vector3(0, 0, -PlayerRadius - 0.1f); // In front of orb
        _player.AddChild(_axe);

        AddChild(_player);
        _playerPos = new Vector3(0, GetTerrainY(0, 0) + PlayerRadius, 0);
        _player.Position = _playerPos;

        // One tree at (3, terrain, -2)
        _tree = BuildTree(new Vector3(3f, 0, -2f));
        AddChild(_tree);

        // Sun
        var sun = new DirectionalLight3D();
        sun.Transform = new Transform3D(Basis.Identity, Vector3.Zero);
        sun.RotationDegrees = new Vector3(-45, 30, 0);
        sun.LightEnergy = 1.3f;
        sun.ShadowEnabled = true;
        sun.LightColor = new Color(1, 0.95f, 0.9f);
        AddChild(sun);

        // Fill light
        var fill = new DirectionalLight3D();
        fill.RotationDegrees = new Vector3(-30, -150, 0);
        fill.LightEnergy = 0.3f;
        fill.ShadowEnabled = false;
        fill.LightColor = new Color(0.7f, 0.8f, 1f);
        AddChild(fill);

        // Environment
        var env = new Godot.Environment();
        env.BackgroundMode = Godot.Environment.BGMode.Color;
        env.BackgroundColor = new Color(0.55f, 0.7f, 0.9f);
        env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        env.AmbientLightColor = new Color(0.4f, 0.45f, 0.5f);
        env.AmbientLightEnergy = 0.5f;
        var we = new WorldEnvironment { Environment = env };
        AddChild(we);

        // Camera pivot at player
        _cameraPivot = new Node3D();
        AddChild(_cameraPivot);
        _camera = new Camera3D();
        _camera.Fov = 50;
        _camera.Current = true;
        _cameraPivot.AddChild(_camera);
        UpdateCamera();

        // HUD
        var canvas = new CanvasLayer();
        AddChild(canvas);
        _hud = new Label();
        _hud.Position = new Vector2(10, 10);
        _hud.AddThemeFontSizeOverride("font_size", 14);
        _hud.AddThemeColorOverride("font_color", Colors.White);
        _hud.AddThemeColorOverride("font_shadow_color", Colors.Black);
        _hud.AddThemeConstantOverride("shadow_offset_x", 1);
        _hud.AddThemeConstantOverride("shadow_offset_y", 1);
        canvas.AddChild(_hud);

        GD.Print("Lab: ready. 20x20 world, 1 tree, WASD+Space/Ctrl to move, right-click to orbit");
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left) _lmbHeld = mb.Pressed;
            if (mb.ButtonIndex == MouseButton.Right) _rmbHeld = mb.Pressed;

            // Capture mouse when either RMB or both buttons held
            _orbiting = _rmbHeld;
            Input.MouseMode = (_rmbHeld || (_lmbHeld && _rmbHeld))
                ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;

            if (mb.ButtonIndex == MouseButton.WheelUp)
                _camDist = Mathf.Max(2f, _camDist - 1f);
            else if (mb.ButtonIndex == MouseButton.WheelDown)
                _camDist = Mathf.Min(30f, _camDist + 1f);
            UpdateCamera();
        }
        else if (ev is InputEventMouseMotion mm && (_rmbHeld || (_lmbHeld && _rmbHeld)))
        {
            _camYaw -= mm.Relative.X * 0.3f;
            _camPitch = Mathf.Clamp(_camPitch - mm.Relative.Y * 0.3f, -85f, 85f);
            UpdateCamera();
        }
    }

    public override void _Process(double delta)
    {
        // Movement — camera-relative on XZ plane
        // Camera forward on XZ = direction camera is looking, ignoring pitch
        float yawRad = Mathf.DegToRad(_camYaw);
        var camForward = new Vector3(-Mathf.Sin(yawRad), 0, -Mathf.Cos(yawRad));
        var camRight = new Vector3(camForward.Z, 0, -camForward.X);

        var move = Vector3.Zero;
        bool bothMouse = _lmbHeld && _rmbHeld;

        if (bothMouse)
        {
            // Both mouse buttons = auto-run forward in camera direction
            move += camForward;
        }
        else
        {
            if (Input.IsActionPressed("move_forward")) move += camForward;
            if (Input.IsActionPressed("move_back")) move -= camForward;
            if (Input.IsActionPressed("move_right")) move += camRight;
            if (Input.IsActionPressed("move_left")) move -= camRight;
            if (Input.IsKeyPressed(Key.Q)) move -= camRight;
            if (Input.IsKeyPressed(Key.E)) move += camRight;
        }

        if (move.LengthSquared() > 0.01f)
        {
            move = move.Normalized() * MoveSpeed * (float)delta;
            _playerPos += move;
            _playerPos.X = Mathf.Clamp(_playerPos.X, -WorldSize / 2, WorldSize / 2);
            _playerPos.Z = Mathf.Clamp(_playerPos.Z, -WorldSize / 2, WorldSize / 2);
        }

        if (Input.IsKeyPressed(Key.Space)) _playerPos.Y += MoveSpeed * (float)delta;
        if (Input.IsKeyPressed(Key.Ctrl)) _playerPos.Y -= MoveSpeed * (float)delta;

        // Snap Y to terrain + radius (feet on ground)
        float terrainY = GetTerrainY(_playerPos.X, _playerPos.Z);
        float groundY = terrainY + PlayerRadius;

        // If not manually flying (Y input), snap to ground
        if (!Input.IsKeyPressed(Key.Space) && !Input.IsKeyPressed(Key.Ctrl))
            _playerPos.Y = groundY;

        _player.Position = _playerPos;

        // Smoothly rotate orb to face movement direction
        if (move.X != 0 || move.Z != 0)
        {
            float targetFacing = Mathf.Atan2(move.X, move.Z);
            _playerFacing = Mathf.LerpAngle(_playerFacing, targetFacing, (float)delta * 12f);
        }
        _player.Rotation = new Vector3(0, _playerFacing, 0);

        _cameraPivot.Position = _playerPos;

        // Distance to tree
        float treeDist = (_playerPos - _tree.Position).Length();

        // HUD
        _hud.Text = $"Player: ({_playerPos.X:F2}, {_playerPos.Y:F2}, {_playerPos.Z:F2})\n" +
                     $"Terrain Y: {terrainY:F2}  Ground Y: {groundY:F2}\n" +
                     $"Tree dist: {treeDist:F2}\n" +
                     $"Tree pos: ({_tree.Position.X:F2}, {_tree.Position.Y:F2}, {_tree.Position.Z:F2})\n" +
                     $"Facing: {Mathf.RadToDeg(_playerFacing):F0}°\n" +
                     $"Camera: yaw={_camYaw:F0} pitch={_camPitch:F0} dist={_camDist:F1}\n" +
                     $"[WASD] move  [Q/E] strafe  [Space/Ctrl] up/down  [RMB] orbit  [LMB+RMB] auto-run\n" +
                     (bothMouse ? ">>> AUTO-RUN <<<" : "");
    }

    private void UpdateCamera()
    {
        _cameraPivot.RotationDegrees = new Vector3(_camPitch, _camYaw, 0);
        _camera.Position = new Vector3(0, 0, _camDist);
    }

    private float GetTerrainY(float x, float z)
    {
        // Map world coords to grid
        float gx = (x + WorldSize / 2) / WorldSize * GridRes;
        float gz = (z + WorldSize / 2) / WorldSize * GridRes;
        int ix = Mathf.Clamp((int)gx, 0, GridRes - 2);
        int iz = Mathf.Clamp((int)gz, 0, GridRes - 2);
        float fx = gx - ix, fz = gz - iz;

        float y00 = (float)_altGrid[iz * (GridRes + 1) + ix];
        float y10 = (float)_altGrid[iz * (GridRes + 1) + ix + 1];
        float y01 = (float)_altGrid[(iz + 1) * (GridRes + 1) + ix];
        float y11 = (float)_altGrid[(iz + 1) * (GridRes + 1) + ix + 1];

        return Mathf.Lerp(Mathf.Lerp(y00, y10, fx), Mathf.Lerp(y01, y11, fx), fz);
    }

    private double[] GenerateHills()
    {
        // (GridRes+1) x (GridRes+1) vertices
        int size = GridRes + 1;
        var grid = new double[size * size];
        var rng = new Random(42);
        for (int z = 0; z < size; z++)
        {
            for (int x = 0; x < size; x++)
            {
                // Gentle sine hills
                double nx = (double)x / GridRes;
                double nz = (double)z / GridRes;
                double h = Math.Sin(nx * Math.PI * 2) * Math.Cos(nz * Math.PI * 1.5) * MaxAltitude * 0.5
                         + MaxAltitude * 0.3
                         + rng.NextDouble() * 0.3;
                grid[z * size + x] = h;
            }
        }
        return grid;
    }

    private ArrayMesh BuildTerrainMesh()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float half = WorldSize / 2;
        float cellSize = WorldSize / GridRes;
        int verts = GridRes + 1;

        for (int z = 0; z < GridRes; z++)
        {
            for (int x = 0; x < GridRes; x++)
            {
                float x0 = -half + x * cellSize;
                float x1 = -half + (x + 1) * cellSize;
                float z0 = -half + z * cellSize;
                float z1 = -half + (z + 1) * cellSize;

                float y00 = (float)_altGrid[z * verts + x];
                float y10 = (float)_altGrid[z * verts + x + 1];
                float y01 = (float)_altGrid[(z + 1) * verts + x];
                float y11 = (float)_altGrid[(z + 1) * verts + x + 1];

                var v00 = new Vector3(x0, y00, z0);
                var v10 = new Vector3(x1, y10, z0);
                var v01 = new Vector3(x0, y01, z1);
                var v11 = new Vector3(x1, y11, z1);

                // Color by height
                var c00 = HeightColor(y00); var c10 = HeightColor(y10);
                var c01 = HeightColor(y01); var c11 = HeightColor(y11);

                st.SetColor(c00); st.AddVertex(v00);
                st.SetColor(c01); st.AddVertex(v01);
                st.SetColor(c10); st.AddVertex(v10);
                st.SetColor(c10); st.AddVertex(v10);
                st.SetColor(c01); st.AddVertex(v01);
                st.SetColor(c11); st.AddVertex(v11);
            }
        }
        st.GenerateNormals();
        var mesh = st.Commit();
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.85f });
        return mesh;
    }

    private static Color HeightColor(float y)
    {
        float n = Mathf.Clamp(y / 4f, 0f, 1f);
        return new Color(
            Mathf.Lerp(0.18f, 0.35f, n),
            Mathf.Lerp(0.35f, 0.5f, n),
            Mathf.Lerp(0.12f, 0.18f, n));
    }

    private Node3D BuildTree(Vector3 worldPos)
    {
        float terrainY = GetTerrainY(worldPos.X, worldPos.Z);
        var root = new Node3D();
        root.Position = new Vector3(worldPos.X, terrainY, worldPos.Z);

        // Trunk
        var trunk = new MeshInstance3D();
        var trunkMesh = new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.12f, Height = 1.5f };
        trunk.Mesh = trunkMesh;
        trunk.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.3f, 0.15f) };
        trunk.Position = new Vector3(0, 0.75f, 0);
        root.AddChild(trunk);

        // Canopy
        var canopy = new MeshInstance3D();
        var canopyMesh = new SphereMesh { Radius = 0.6f, Height = 1.2f };
        canopy.Mesh = canopyMesh;
        canopy.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.55f, 0.15f) };
        canopy.Position = new Vector3(0, 1.7f, 0);
        root.AddChild(canopy);

        // Second canopy
        var canopy2 = new MeshInstance3D();
        canopy2.Mesh = canopyMesh;
        canopy2.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.15f, 0.5f, 0.12f) };
        canopy2.Position = new Vector3(0.25f, 1.5f, 0.15f);
        canopy2.Scale = Vector3.One * 0.7f;
        root.AddChild(canopy2);

        return root;
    }

    private void BuildGridLines()
    {
        // Axis markers at origin
        AddAxisMarker(Vector3.Right, new Color(1, 0, 0), "+X");
        AddAxisMarker(Vector3.Up, new Color(0, 1, 0), "+Y");
        AddAxisMarker(new Vector3(0, 0, -1), new Color(0, 0, 1), "-Z (Godot fwd)");
    }

    private void AddAxisMarker(Vector3 dir, Color color, string label)
    {
        var line = new MeshInstance3D();
        var cyl = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.02f, Height = 2f };
        line.Mesh = cyl;
        line.MaterialOverride = new StandardMaterial3D { AlbedoColor = color };
        // Rotate cylinder to align with direction
        line.Position = dir * 1f;
        if (dir == Vector3.Right) line.RotationDegrees = new Vector3(0, 0, 90);
        else if (dir == Vector3.Up) line.RotationDegrees = Vector3.Zero;
        else line.RotationDegrees = new Vector3(90, 0, 0);
        AddChild(line);

        var lbl = new Label3D();
        lbl.Text = label;
        lbl.Position = dir * 2.2f + Vector3.Up * 0.3f;
        lbl.FontSize = 24;
        lbl.Modulate = color;
        AddChild(lbl);
    }
}
