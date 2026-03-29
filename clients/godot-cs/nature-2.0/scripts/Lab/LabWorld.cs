using Godot;
using System;

namespace CommunitySurvival.Lab;

/// <summary>
/// Isolation lab. Tiny world, one tree cluster, player orb.
/// Full telemetry + real-time atmosphere tuning.
///
/// Movement:
///   WASD = camera-relative move, Q/E = strafe
///   Space/Ctrl = up/down, RMB = orbit, LMB+RMB = auto-run, Scroll = zoom
///
/// Atmosphere (hold Tab to show tuning panel, keys while held):
///   1/2 = fog density up/down
///   3/4 = god ray energy up/down
///   5/6 = sun angle up/down
///   7/8 = ambient light up/down
///   9/0 = tree scale up/down
///   -/= = time of day shift
/// </summary>
public partial class LabWorld : Node3D
{
    private const float WorldSize = 40f;
    private const int GridRes = 100;
    private const float MaxAltitude = 5f;
    private const float PlayerRadius = 0.3f;
    private const float MoveSpeed = 5f;

    // Nodes
    private MeshInstance3D _ground;
    private MeshInstance3D _player;
    private Node3D _axe;
    private Camera3D _camera;
    private Node3D _cameraPivot;
    private Label _hud;
    private double[] _altGrid;

    // Trees
    private Node3D _treeCluster;
    private float _treeScale = 1.0f;

    // Lights
    private DirectionalLight3D _sun;
    private float _sunAngle = -40f;
    private float _sunRotation = 30f; // horizontal angle

    // Environment
    private Godot.Environment _env;
    private float _fogDensity = 0.02f;
    private float _ambientEnergy = 0.4f;
    private float _godRayEnergy = 0.0f; // starts off, user dials up

    // Smoke
    private GpuParticles3D _smoke;

    // Camera
    private float _camYaw, _camPitch = -20f, _camDist = 10f;
    private bool _lmbHeld, _rmbHeld;

    // Player
    private Vector3 _playerPos;
    private float _playerFacing;

    // Tuning
    private bool _tuning;

    public override void _Ready()
    {
        _altGrid = GenerateHills();

        // Terrain
        _ground = new MeshInstance3D { Mesh = BuildTerrainMesh() };
        AddChild(_ground);

        // Player orb + axe
        BuildPlayer();

        // Tree cluster — a small grove of varied trees
        _treeCluster = new Node3D();
        AddChild(_treeCluster);
        SpawnTree(new Vector3(5f, 0, -3f), 1.0f, 120, false);
        SpawnTree(new Vector3(7f, 0, -1f), 0.7f, 60, false);
        SpawnTree(new Vector3(4f, 0, -6f), 1.3f, 180, true); // fire-scarred old growth
        SpawnTree(new Vector3(8f, 0, -5f), 0.5f, 30, false); // young tree
        SpawnTree(new Vector3(3f, 0, 0f), 1.1f, 140, false);
        SpawnTree(new Vector3(-2f, 0, -4f), 1.5f, 200, true); // ancient
        SpawnTree(new Vector3(6f, 0, -8f), 0.9f, 90, false);

        // Stump (felled tree remains)
        SpawnStump(new Vector3(5.5f, 0, -1f));

        // Woodpile
        SpawnWoodpile(new Vector3(2f, 0, 1f));

        // Campfire with smoke
        SpawnCampfire(new Vector3(1f, 0, 2f));

        // Sun (warm, low angle for god rays through canopy)
        _sun = new DirectionalLight3D();
        _sun.LightEnergy = 1.4f;
        _sun.ShadowEnabled = true;
        _sun.LightColor = new Color(1f, 0.92f, 0.8f);
        _sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
        UpdateSunAngle();
        AddChild(_sun);

        // Fill light (cool)
        var fill = new DirectionalLight3D();
        fill.RotationDegrees = new Vector3(-30, -150, 0);
        fill.LightEnergy = 0.2f;
        fill.ShadowEnabled = false;
        fill.LightColor = new Color(0.6f, 0.7f, 0.9f);
        AddChild(fill);

        // Environment
        SetupEnvironment();

        // Camera
        _cameraPivot = new Node3D();
        AddChild(_cameraPivot);
        _camera = new Camera3D { Fov = 55, Current = true };
        _cameraPivot.AddChild(_camera);
        UpdateCamera();

        // HUD
        var canvas = new CanvasLayer();
        AddChild(canvas);
        _hud = new Label { Position = new Vector2(10, 10) };
        _hud.AddThemeFontSizeOverride("font_size", 13);
        _hud.AddThemeColorOverride("font_color", Colors.White);
        _hud.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.7f));
        _hud.AddThemeConstantOverride("shadow_offset_x", 1);
        _hud.AddThemeConstantOverride("shadow_offset_y", 1);
        canvas.AddChild(_hud);

        _playerPos = new Vector3(0, GetTerrainY(0, 0) + PlayerRadius, 0);
        _player.Position = _playerPos;

        GD.Print("Lab: ready. Tab=tuning panel, RMB=orbit, WASD=move");
    }

    private void SetupEnvironment()
    {
        var sky = new ProceduralSkyMaterial();
        sky.SkyTopColor = new Color(0.3f, 0.5f, 0.8f);
        sky.SkyHorizonColor = new Color(0.6f, 0.7f, 0.85f);
        sky.GroundBottomColor = new Color(0.15f, 0.2f, 0.1f);
        sky.GroundHorizonColor = new Color(0.45f, 0.5f, 0.4f);
        sky.SunAngleMax = 30f;
        sky.SunCurve = 0.1f;

        var skyRes = new Sky { SkyMaterial = sky };

        _env = new Godot.Environment();
        _env.BackgroundMode = Godot.Environment.BGMode.Sky;
        _env.Sky = skyRes;

        // Ambient
        _env.AmbientLightSource = Godot.Environment.AmbientSource.Sky;
        _env.AmbientLightEnergy = _ambientEnergy;

        // Tonemap
        _env.TonemapMode = Godot.Environment.ToneMapper.Aces;
        _env.TonemapWhite = 6f;

        // Volumetric fog — the key atmosphere feature
        _env.VolumetricFogEnabled = true;
        _env.VolumetricFogDensity = _fogDensity;
        _env.VolumetricFogAlbedo = new Color(0.85f, 0.88f, 0.9f);
        _env.VolumetricFogEmission = new Color(0.1f, 0.1f, 0.12f);
        _env.VolumetricFogEmissionEnergy = 0.5f;
        // VolumetricFogGi not available in Godot 4.6 C# API — skip
        _env.VolumetricFogLength = 60f;
        _env.VolumetricFogDetailSpread = 0.7f;

        // Glow for soft bloom
        _env.GlowEnabled = true;
        _env.GlowIntensity = 0.3f;
        _env.GlowBloom = 0.1f;

        // SSAO for ground contact shadows
        _env.SsaoEnabled = true;
        _env.SsaoRadius = 2f;
        _env.SsaoIntensity = 1.5f;

        var we = new WorldEnvironment { Environment = _env };
        AddChild(we);
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left) _lmbHeld = mb.Pressed;
            if (mb.ButtonIndex == MouseButton.Right) _rmbHeld = mb.Pressed;
            Input.MouseMode = _rmbHeld ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
            if (mb.ButtonIndex == MouseButton.WheelUp) _camDist = Mathf.Max(2f, _camDist - 1f);
            else if (mb.ButtonIndex == MouseButton.WheelDown) _camDist = Mathf.Min(40f, _camDist + 1f);
            UpdateCamera();
        }
        else if (ev is InputEventMouseMotion mm && _rmbHeld)
        {
            _camYaw -= mm.Relative.X * 0.3f;
            _camPitch = Mathf.Clamp(_camPitch - mm.Relative.Y * 0.3f, -85f, 85f);
            UpdateCamera();
        }

        if (ev is InputEventKey k && k.Pressed && k.Keycode == Key.Tab)
            _tuning = !_tuning;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // Atmosphere tuning (hold Tab + number keys)
        if (_tuning) ProcessTuning(dt);

        // Movement
        float yawRad = Mathf.DegToRad(_camYaw);
        var camFwd = new Vector3(-Mathf.Sin(yawRad), 0, -Mathf.Cos(yawRad));
        var camRight = new Vector3(camFwd.Z, 0, -camFwd.X);

        var move = Vector3.Zero;
        bool bothMouse = _lmbHeld && _rmbHeld;
        if (bothMouse) { move += camFwd; }
        else
        {
            if (Input.IsActionPressed("move_forward")) move += camFwd;
            if (Input.IsActionPressed("move_back")) move -= camFwd;
            if (Input.IsActionPressed("move_right")) move += camRight;
            if (Input.IsActionPressed("move_left")) move -= camRight;
            if (Input.IsKeyPressed(Key.Q)) move -= camRight;
            if (Input.IsKeyPressed(Key.E)) move += camRight;
        }
        if (move.LengthSquared() > 0.01f)
        {
            move = move.Normalized() * MoveSpeed * dt;
            _playerPos += move;
            _playerPos.X = Mathf.Clamp(_playerPos.X, -WorldSize / 2, WorldSize / 2);
            _playerPos.Z = Mathf.Clamp(_playerPos.Z, -WorldSize / 2, WorldSize / 2);
        }
        if (Input.IsKeyPressed(Key.Space)) _playerPos.Y += MoveSpeed * dt;
        if (Input.IsKeyPressed(Key.Ctrl)) _playerPos.Y -= MoveSpeed * dt;

        float terrainY = GetTerrainY(_playerPos.X, _playerPos.Z);
        if (!Input.IsKeyPressed(Key.Space) && !Input.IsKeyPressed(Key.Ctrl))
            _playerPos.Y = terrainY + PlayerRadius;

        _player.Position = _playerPos;
        if (move.X != 0 || move.Z != 0)
            _playerFacing = Mathf.LerpAngle(_playerFacing, Mathf.Atan2(move.X, move.Z), dt * 12f);
        _player.Rotation = new Vector3(0, _playerFacing, 0);
        _cameraPivot.Position = _playerPos;

        // Nearest tree distance
        float nearDist = 999f;
        foreach (Node child in _treeCluster.GetChildren())
        {
            if (child is Node3D n)
            {
                float d = (_playerPos - n.GlobalPosition).Length();
                if (d < nearDist) nearDist = d;
            }
        }

        // HUD
        string tuneText = _tuning ?
            $"=== TUNING (Tab to close) ===\n" +
            $"[1/2] Fog density: {_fogDensity:F3}\n" +
            $"[3/4] God ray (sun energy): {_sun.LightEnergy:F1}\n" +
            $"[5/6] Sun pitch: {_sunAngle:F0}°\n" +
            $"[7/8] Ambient energy: {_ambientEnergy:F2}\n" +
            $"[9/0] Tree scale: {_treeScale:F1}x\n" +
            $"[-/=] Sun rotation: {_sunRotation:F0}°\n"
            : "[Tab] Tuning";

        _hud.Text =
            $"Player: ({_playerPos.X:F1}, {_playerPos.Y:F1}, {_playerPos.Z:F1})\n" +
            $"Terrain: {terrainY:F2}  Nearest tree: {nearDist:F1}\n" +
            $"Facing: {Mathf.RadToDeg(_playerFacing):F0}°  Cam: yaw={_camYaw:F0} pitch={_camPitch:F0}\n" +
            (bothMouse ? ">>> AUTO-RUN <<<\n" : "") +
            $"\n{tuneText}";
    }

    private void ProcessTuning(float dt)
    {
        float rate = dt * 2f;
        bool changed = false;

        if (Input.IsKeyPressed(Key.Key1)) { _fogDensity = Mathf.Min(0.2f, _fogDensity + rate * 0.01f); changed = true; }
        if (Input.IsKeyPressed(Key.Key2)) { _fogDensity = Mathf.Max(0f, _fogDensity - rate * 0.01f); changed = true; }
        if (Input.IsKeyPressed(Key.Key3)) { _sun.LightEnergy = Mathf.Min(5f, _sun.LightEnergy + rate); changed = true; }
        if (Input.IsKeyPressed(Key.Key4)) { _sun.LightEnergy = Mathf.Max(0f, _sun.LightEnergy - rate); changed = true; }
        if (Input.IsKeyPressed(Key.Key5)) { _sunAngle = Mathf.Clamp(_sunAngle - rate * 20f, -89f, -5f); UpdateSunAngle(); }
        if (Input.IsKeyPressed(Key.Key6)) { _sunAngle = Mathf.Clamp(_sunAngle + rate * 20f, -89f, -5f); UpdateSunAngle(); }
        if (Input.IsKeyPressed(Key.Key7)) { _ambientEnergy = Mathf.Min(2f, _ambientEnergy + rate * 0.2f); changed = true; }
        if (Input.IsKeyPressed(Key.Key8)) { _ambientEnergy = Mathf.Max(0f, _ambientEnergy - rate * 0.2f); changed = true; }
        if (Input.IsKeyPressed(Key.Key9)) { _treeScale = Mathf.Min(5f, _treeScale + rate * 0.5f); UpdateTreeScale(); }
        if (Input.IsKeyPressed(Key.Key0)) { _treeScale = Mathf.Max(0.3f, _treeScale - rate * 0.5f); UpdateTreeScale(); }
        if (Input.IsKeyPressed(Key.Minus)) { _sunRotation -= rate * 30f; UpdateSunAngle(); }
        if (Input.IsKeyPressed(Key.Equal)) { _sunRotation += rate * 30f; UpdateSunAngle(); }

        if (changed)
        {
            _env.VolumetricFogDensity = _fogDensity;
            _env.AmbientLightEnergy = _ambientEnergy;
        }
    }

    private void UpdateSunAngle()
    {
        _sun.RotationDegrees = new Vector3(_sunAngle, _sunRotation, 0);
    }

    private void UpdateTreeScale()
    {
        foreach (Node child in _treeCluster.GetChildren())
            if (child is Node3D n) n.Scale = Vector3.One * _treeScale;
    }

    private void UpdateCamera()
    {
        _cameraPivot.RotationDegrees = new Vector3(_camPitch, _camYaw, 0);
        _camera.Position = new Vector3(0, 0, _camDist);
    }

    // ——— Terrain ———

    private float GetTerrainY(float x, float z)
    {
        float gx = (x + WorldSize / 2) / WorldSize * GridRes;
        float gz = (z + WorldSize / 2) / WorldSize * GridRes;
        int ix = Mathf.Clamp((int)gx, 0, GridRes - 2);
        int iz = Mathf.Clamp((int)gz, 0, GridRes - 2);
        float fx = gx - ix, fz = gz - iz;
        int w = GridRes + 1;
        float y00 = (float)_altGrid[iz * w + ix];
        float y10 = (float)_altGrid[iz * w + ix + 1];
        float y01 = (float)_altGrid[(iz + 1) * w + ix];
        float y11 = (float)_altGrid[(iz + 1) * w + ix + 1];
        return Mathf.Lerp(Mathf.Lerp(y00, y10, fx), Mathf.Lerp(y01, y11, fx), fz);
    }

    private double[] GenerateHills()
    {
        int w = GridRes + 1;
        var grid = new double[w * w];
        var rng = new Random(42);
        for (int z = 0; z < w; z++)
            for (int x = 0; x < w; x++)
            {
                double nx = (double)x / GridRes, nz = (double)z / GridRes;
                double h = Math.Sin(nx * Math.PI * 1.5) * Math.Cos(nz * Math.PI) * MaxAltitude * 0.5
                         + Math.Sin(nx * Math.PI * 3 + 1) * Math.Sin(nz * Math.PI * 2.5) * MaxAltitude * 0.2
                         + MaxAltitude * 0.25 + rng.NextDouble() * 0.15;
                grid[z * w + x] = Math.Max(0, h);
            }
        return grid;
    }

    private ArrayMesh BuildTerrainMesh()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float half = WorldSize / 2, cell = WorldSize / GridRes;
        int w = GridRes + 1;

        for (int z = 0; z < GridRes; z++)
            for (int x = 0; x < GridRes; x++)
            {
                float x0 = -half + x * cell, x1 = x0 + cell;
                float z0 = -half + z * cell, z1 = z0 + cell;
                float y00 = (float)_altGrid[z * w + x], y10 = (float)_altGrid[z * w + x + 1];
                float y01 = (float)_altGrid[(z + 1) * w + x], y11 = (float)_altGrid[(z + 1) * w + x + 1];

                var v00 = new Vector3(x0, y00, z0); var v10 = new Vector3(x1, y10, z0);
                var v01 = new Vector3(x0, y01, z1); var v11 = new Vector3(x1, y11, z1);
                var c00 = HColor(y00); var c10 = HColor(y10); var c01 = HColor(y01); var c11 = HColor(y11);

                st.SetColor(c00); st.AddVertex(v00);
                st.SetColor(c01); st.AddVertex(v01);
                st.SetColor(c10); st.AddVertex(v10);
                st.SetColor(c10); st.AddVertex(v10);
                st.SetColor(c01); st.AddVertex(v01);
                st.SetColor(c11); st.AddVertex(v11);
            }

        st.GenerateNormals();
        var mesh = st.Commit();
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.9f });
        return mesh;
    }

    private static Color HColor(float y)
    {
        float n = Mathf.Clamp(y / 5f, 0f, 1f);
        // Forest floor green → mossy → earthy
        return new Color(
            Mathf.Lerp(0.12f, 0.28f, n),
            Mathf.Lerp(0.28f, 0.38f, n),
            Mathf.Lerp(0.08f, 0.12f, n));
    }

    // ——— Entity builders ———

    private void BuildPlayer()
    {
        _player = new MeshInstance3D();
        var sphere = new SphereMesh { Radius = PlayerRadius, Height = PlayerRadius * 2 };
        _player.Mesh = sphere;
        _player.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.15f, 0.85f, 0.25f),
            EmissionEnabled = true,
            Emission = new Color(0.05f, 0.3f, 0.05f),
            EmissionEnergyMultiplier = 0.2f,
        };

        _axe = new Node3D();
        var handle = new MeshInstance3D();
        handle.Mesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.02f, Height = 0.5f };
        handle.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.28f, 0.12f) };
        handle.RotationDegrees = new Vector3(0, 0, 90);
        _axe.AddChild(handle);
        var blade = new MeshInstance3D();
        blade.Mesh = new BoxMesh { Size = new Vector3(0.03f, 0.15f, 0.2f) };
        blade.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.7f, 0.7f, 0.7f), Metallic = 0.8f };
        blade.Position = new Vector3(0.25f, 0, 0);
        _axe.AddChild(blade);
        _axe.Position = new Vector3(0, 0, -PlayerRadius - 0.1f);
        _player.AddChild(_axe);

        AddChild(_player);
    }

    private void SpawnTree(Vector3 pos, float scale, int age, bool fireScars)
    {
        float ty = GetTerrainY(pos.X, pos.Z);
        var root = new Node3D();
        root.Position = new Vector3(pos.X, ty, pos.Z);
        root.Scale = Vector3.One * scale;

        float trunkH = Mathf.Lerp(2f, 5f, age / 200f);
        float trunkR = Mathf.Lerp(0.08f, 0.2f, age / 200f);
        float canopyR = Mathf.Lerp(0.5f, 1.5f, age / 200f);

        // Trunk
        var trunk = new MeshInstance3D();
        trunk.Mesh = new CylinderMesh { TopRadius = trunkR * 0.7f, BottomRadius = trunkR, Height = trunkH };
        var trunkColor = fireScars ? new Color(0.25f, 0.16f, 0.08f) : new Color(0.4f, 0.26f, 0.13f);
        trunk.MaterialOverride = new StandardMaterial3D { AlbedoColor = trunkColor };
        trunk.Position = new Vector3(0, trunkH / 2, 0);
        root.AddChild(trunk);

        // Canopy spheres (3 overlapping for volume)
        var canopyColor = fireScars
            ? new Color(0.2f, 0.4f, 0.12f)
            : new Color(0.15f + age * 0.0003f, 0.5f + age * 0.0005f, 0.12f);
        var rng = new Random(age * 1000 + (int)(pos.X * 100));

        for (int i = 0; i < 3; i++)
        {
            var c = new MeshInstance3D();
            float r = canopyR * Mathf.Lerp(0.6f, 1.0f, (float)rng.NextDouble());
            c.Mesh = new SphereMesh { Radius = r, Height = r * 2 };
            var cColor = canopyColor;
            cColor.G += (float)(rng.NextDouble() - 0.5) * 0.08f;
            c.MaterialOverride = new StandardMaterial3D { AlbedoColor = cColor };
            c.Position = new Vector3(
                (float)(rng.NextDouble() - 0.5) * canopyR * 0.6f,
                trunkH + r * 0.5f + (float)rng.NextDouble() * canopyR * 0.3f,
                (float)(rng.NextDouble() - 0.5) * canopyR * 0.6f);
            root.AddChild(c);
        }

        _treeCluster.AddChild(root);
    }

    private void SpawnStump(Vector3 pos)
    {
        float ty = GetTerrainY(pos.X, pos.Z);
        var stump = new MeshInstance3D();
        stump.Mesh = new CylinderMesh { TopRadius = 0.15f, BottomRadius = 0.18f, Height = 0.3f };
        stump.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.22f, 0.1f) };
        stump.Position = new Vector3(pos.X, ty + 0.15f, pos.Z);
        AddChild(stump);
    }

    private void SpawnWoodpile(Vector3 pos)
    {
        float ty = GetTerrainY(pos.X, pos.Z);
        var pile = new Node3D();
        pile.Position = new Vector3(pos.X, ty, pos.Z);
        var rng = new Random(99);
        for (int i = 0; i < 8; i++)
        {
            var log = new MeshInstance3D();
            log.Mesh = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.07f, Height = 0.8f };
            log.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.26f, 0.12f) };
            log.RotationDegrees = new Vector3(0, 0, 90);
            log.Position = new Vector3(
                (float)(rng.NextDouble() - 0.5) * 0.3f,
                0.07f + (i / 4) * 0.14f,
                (float)(rng.NextDouble() - 0.5) * 0.2f);
            pile.AddChild(log);
        }
        AddChild(pile);
    }

    private void SpawnCampfire(Vector3 pos)
    {
        float ty = GetTerrainY(pos.X, pos.Z);

        // Stone ring
        for (int i = 0; i < 8; i++)
        {
            var stone = new MeshInstance3D();
            stone.Mesh = new SphereMesh { Radius = 0.08f, Height = 0.12f };
            stone.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.38f, 0.35f) };
            float angle = i * Mathf.Tau / 8;
            stone.Position = new Vector3(pos.X + Mathf.Cos(angle) * 0.3f, ty + 0.04f, pos.Z + Mathf.Sin(angle) * 0.3f);
            AddChild(stone);
        }

        // Smoke particles
        _smoke = new GpuParticles3D();
        _smoke.Amount = 30;
        _smoke.Lifetime = 3f;
        _smoke.Position = new Vector3(pos.X, ty + 0.2f, pos.Z);

        var mat = new ParticleProcessMaterial();
        mat.Direction = new Vector3(0, 1, 0);
        mat.Spread = 15f;
        mat.InitialVelocityMin = 0.3f;
        mat.InitialVelocityMax = 0.8f;
        mat.Gravity = new Vector3(0, 0.1f, 0); // Slight updraft
        mat.DampingMin = 0.3f;
        mat.DampingMax = 0.7f;
        mat.ScaleMin = 0.1f;
        mat.ScaleMax = 0.4f;
        mat.Color = new Color(0.6f, 0.6f, 0.6f, 0.3f);
        _smoke.ProcessMaterial = mat;

        // Smoke mesh (simple quad)
        var smokeMesh = new QuadMesh { Size = new Vector2(1f, 1f) };
        var smokeMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.7f, 0.7f, 0.7f, 0.15f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        smokeMesh.Material = smokeMat;
        _smoke.DrawPass1 = smokeMesh;

        _smoke.Emitting = true;
        AddChild(_smoke);
    }
}
