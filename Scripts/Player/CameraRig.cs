// ─────────────────────────────────────────────────────────────
// Caméras : 3e personne en orbite (clic droit + glisser, molette),
// qui se rapproche au lieu de traverser le gâteau ou les hautes herbes,
// mode « tunnel » dans les galeries, et vue fourmi (touche V).
// ─────────────────────────────────────────────────────────────
using System;
using Godot;
using Crumbants.Render;
using Crumbants.Sim;
using static Crumbants.Cfg;

namespace Crumbants.Player;

public partial class CameraRig : Node3D
{
    public Camera3D Camera;
    public GameState Game;
    public PlayerController Player;
    public bool FirstPerson;
    public float Sensitivity = 1;
    public float FpYaw, FpPitch;
    public float Yaw = 0.75f, Pitch = 0.55f, Dist = NearDist;
    float _yawS, _pitchS = 0.55f, _distS = NearDist, _colDist = NearDist, _outT, _tunnelK, _shake;
    // pas de zoom libre : trois vues fixes, la touche V passe de l'une à l'autre
    public const float NearDist = 7.5f, WideDist = 24f;
    public enum ViewMode { Near, Wide, Ant }
    public ViewMode View = ViewMode.Near;
    public void CycleView()
    {
        View = View switch { ViewMode.Near => ViewMode.Wide, ViewMode.Wide => ViewMode.Ant, _ => ViewMode.Near };
        if (View == ViewMode.Ant) SetFirstPerson(true);
        else
        {
            if (FirstPerson) SetFirstPerson(false);
            Dist = View == ViewMode.Wide ? WideDist : NearDist;
            // vue large : un peu plus plongeante, pour poser les bâtiments
            if (View == ViewMode.Wide) Pitch = MathF.Max(Pitch, 0.8f);
        }
    }
    Vector3 _target;
    bool _rightHeld;
    public Vector3? Override; public Vector3 OverrideLook;
    OmniLight3D _lamp;
    public MeshInstance3D Outline;

    // l'axe des déplacements suit la caméra
    public float MoveYaw => FirstPerson ? FpYaw : Override != null ? Yaw : _yawS;

    public void Setup(GameState game, PlayerController player)
    {
        Game = game; Player = player;
        Camera = new Camera3D { Fov = 60, Near = 0.05f, Far = 2500, Current = true };
        AddChild(Camera);
        // contour encre (post-traitement plein écran)
        Outline = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = Vector2.One * 2, FlipFaces = false },
            // dessiné en premier parmi les transparents : il ne recouvre ni le verre, ni les textes, ni les effets
            MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/outline.gdshader"), RenderPriority = -128 },
            ExtraCullMargin = 16384, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        Camera.AddChild(Outline);
        _target = game.Player.Pos + new Vector3(0, 0.45f, 0);
        // lampe des galeries : chaude, avec ombres (la fourmi projette son ombre sur les parois)
        _lamp = new OmniLight3D { LightColor = new Color(1f, 0.82f, 0.55f), LightEnergy = 0, OmniRange = 9, ShadowEnabled = true, OmniAttenuation = 1.2f, ShadowBias = 0.05f, LightSize = 0.15f };
        AddChild(_lamp);
        // poussière de sucre en suspension dans les galeries, qui accroche la lumière
        var pm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(3.5f, 1.4f, 3.5f),
            Gravity = new Vector3(0, -0.02f, 0), InitialVelocityMin = 0.02f, InitialVelocityMax = 0.08f, Spread = 180,
            TurbulenceEnabled = true, TurbulenceNoiseStrength = 0.6f, TurbulenceNoiseScale = 2.5f, TurbulenceInfluenceMin = 0.05f, TurbulenceInfluenceMax = 0.12f,
            ScaleMin = 0.5f, ScaleMax = 1.4f,
        };
        var mote = new QuadMesh { Size = new Vector2(0.035f, 0.035f) };
        mote.Material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(1f, 0.92f, 0.7f, 0.75f),
            EmissionEnabled = true, Emission = new Color(1f, 0.85f, 0.55f), EmissionEnergyMultiplier = 1.5f,
            AlbedoTexture = MoteTex(),
        };
        _dust = new GpuParticles3D { Amount = 260, Lifetime = 9, Preprocess = 6, ProcessMaterial = pm, DrawPass1 = mote, Emitting = false, LocalCoords = false,
            VisibilityAabb = new Aabb(new Vector3(-6, -3, -6), new Vector3(12, 6, 12)), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_dust);
        _yawS = Yaw;
        // vue fourmi : ses deux mandibules en bas de l'écran (elles claquent quand on croque)
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, InstanceCount = 1, Mesh = Render.AntSwarm.PartMesh("mandible_L") };
        _fpMand = new MultiMeshInstance3D { Multimesh = mm, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = 4 };
        Camera.AddChild(_fpMand);
        var mmR = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, InstanceCount = 1, Mesh = Render.AntSwarm.PartMesh("mandible_R") };
        _fpMandR = new MultiMeshInstance3D { Multimesh = mmR, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = 4 };
        Camera.AddChild(_fpMandR);
    }

    MultiMeshInstance3D _fpMand, _fpMandR;
    public static bool LampShadows = true; // qualité « rapide » : lampe des galeries sans ombres
    GpuParticles3D _dust;
    float _dbg;

    static Texture2D MoteTex()
    {
        var img = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
        {
            float d = new Vector2(x - 15.5f, y - 15.5f).Length() / 16f;
            float a = Mathf.Clamp(1 - d, 0, 1); a *= a;
            img.SetPixel(x, y, new Color(1, 1, 1, a));
        }
        return ImageTexture.CreateFromImage(img);
    }

    void UpdateFpMandibles(bool on)
    {
        _fpMand.Visible = _fpMandR.Visible = on;
        if (!on) return;
        float open = Render.AntRig.MandOpen(Player.BiteAnim, Player.LoadAnim);
        var col = Cfg.AntColors[Math.Clamp(Game.Player.Color, 0, Cfg.AntColors.Length - 1)].Darkened(0.35f).SrgbToLinear();
        // repère caméra : avant = −Z. Les mandibules (modèle : avant +Z) sont retournées vers l'avant,
        // charnières en bas de l'écran, pointes qui se croisent au centre.
        foreach (int side in new[] { -1, 1 })
        {
            // charnières SOUS le bord bas de l'écran (demi-hauteur visible à 0,14 : ≈ 0,10) : les mandibules
            // sortent du cadre comme si elles tenaient à la tête, seules les pointes entrent dans la vue
            var b = new Basis(Vector3.Up, MathF.PI) * new Basis(Vector3.Forward, side * 0.12f) * new Basis(Vector3.Right, -0.30f) * new Basis(Vector3.Up, side * open * 1.1f);
            var tr = new Transform3D(b.Scaled(Vector3.One * 0.95f), new Vector3(-side * 0.104f, -0.117f, -0.14f));
            if (side < 0) { _fpMand.Multimesh.SetInstanceTransform(0, tr); _fpMand.Multimesh.SetInstanceColor(0, col); }
            else { _fpMandR.Multimesh.SetInstanceTransform(0, tr); _fpMandR.Multimesh.SetInstanceColor(0, col); }
        }
    }

    public void Shake(float s) => _shake = MathF.Max(_shake, s);

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right) _rightHeld = mb.Pressed;
        }
        else if (e is InputEventMouseMotion mm)
        {
            if (FirstPerson && Input.MouseMode == Input.MouseModeEnum.Captured)
            {
                FpYaw -= mm.Relative.X * 0.0035f * Sensitivity;
                FpPitch = Mathf.Clamp(FpPitch - mm.Relative.Y * 0.0035f * Sensitivity, -1.3f, 1.2f);
            }
            else if (_rightHeld)
            {
                Yaw -= mm.Relative.X * 0.006f * Sensitivity;
                Pitch = Mathf.Clamp(Pitch + mm.Relative.Y * 0.005f * Sensitivity, -0.25f, 1.45f);
            }
        }
    }

    public void SetFirstPerson(bool on)
    {
        FirstPerson = on;
        if (on) View = ViewMode.Ant;
        else if (View == ViewMode.Ant) { View = ViewMode.Near; Dist = NearDist; }
        if (on) { FpYaw = _yawS; FpPitch = -0.1f; Camera.Fov = FpFov; Input.MouseMode = Input.MouseModeEnum.Captured; }
        else { Yaw = FpYaw; Camera.Fov = 60; Input.MouseMode = Input.MouseModeEnum.Visible; }
    }

    bool SolidP(Vector3 p) => p.Y < 0.05f || PropPhysics.HitsSphere(p, 0.08f) || Player.InsideCake(p);

    static readonly Vector3[] Probe =
    {
        Vector3.Zero, new(0.25f, 0, 0), new(-0.25f, 0, 0), new(0, 0.25f, 0), new(0, -0.25f, 0), new(0, 0, 0.25f), new(0, 0, -0.25f),
    };

    bool Clear(Vector3 p)
    {
        if (p.Y < 0.2f + Terrain.BaseGroundAt(p.X, p.Z)) return false;
        if (PropPhysics.HitsSphere(p, 0.22f)) return false; // objets du décor et gâteaux, à leur forme affichée exacte
        if (Player.InsideCake(p)) return false;              // … et l'intérieur plein du gâteau (un maillage n'a pas de « dedans »)
        float gh = Terrain.GrassHeightAt(p.X, p.Z);
        if (gh > 2 && gh * 1.25f > p.Y) return false; // les hautes herbes font mur
        return true;
    }

    public void UpdateCamera(float dt)
    {
        if (Input.IsActionPressed("cam_left")) { Yaw += dt * 2.2f; FpYaw += dt * 2.2f; }
        if (Input.IsActionPressed("cam_right")) { Yaw -= dt * 2.2f; FpYaw -= dt * 2.2f; }
        // stick droit de la manette
        float rx = Input.GetJoyAxis(0, JoyAxis.RightX), ry = Input.GetJoyAxis(0, JoyAxis.RightY);
        if (MathF.Abs(rx) > 0.15f) { Yaw -= rx * dt * 2.6f * Sensitivity; FpYaw -= rx * dt * 2.6f * Sensitivity; }
        if (MathF.Abs(ry) > 0.15f) { Pitch = Mathf.Clamp(Pitch + ry * dt * 1.6f * Sensitivity, -0.25f, 1.45f); FpPitch = Mathf.Clamp(FpPitch - ry * dt * 1.6f * Sensitivity, -1.3f, 1.2f); }
        var disp = Player.DisplayPos;
        if (Override is { } ov)
        {
            Camera.GlobalPosition = ov;
            Camera.LookAt(OverrideLook);
            return;
        }
        if (FirstPerson)
        {
            // l'œil est sur la vraie tête du modèle (posée sur la surface) ; jamais dans la matière
            var eye = Player.EyePos;
            var core = disp + Player.Orientation * new Vector3(0, 0.28f, 0.05f);
            for (int i = 0; i < 6 && Player.PointSolid(eye, 0.06f); i++) eye = eye.Lerp(core, 0.4f);
            var d = new Vector3(-MathF.Sin(FpYaw) * MathF.Cos(FpPitch), MathF.Sin(FpPitch), -MathF.Cos(FpYaw) * MathF.Cos(FpPitch));
            Camera.GlobalPosition = eye;
            Camera.LookAt(eye + d);
            ApplyShake(dt);
            UpdateFpMandibles(true);
            _lamp.GlobalPosition = eye - d * 0.25f;
            _lamp.LightEnergy = Player.Underground() ? 1.2f : 0.5f;
            _lamp.Visible = true;
            _lamp.ShadowEnabled = LampShadows;
            _dust.GlobalPosition = eye; _dust.Emitting = Player.Underground();
            return;
        }
        UpdateFpMandibles(false);
        bool inTunnel = Player.Underground();
        _tunnelK += ((inTunnel ? 1 : 0) - _tunnelK) * Mathf.Min(1, dt * 4);
        if (inTunnel && Player.Moving && !_rightHeld && Input.IsActionPressed("move_forward"))
            Yaw = Mathf.LerpAngle(Yaw, Player.Yaw + MathF.PI, Mathf.Min(1, dt * 1.5f));
        float k = Mathf.Min(1, dt * 12);
        _yawS = Mathf.LerpAngle(_yawS, Yaw, k);
        _pitchS += (Pitch - _pitchS) * k;
        _distS += (Dist - _distS) * Mathf.Min(1, dt * 8);
        float pitch = _pitchS + (MathF.Min(_pitchS, 0.1f) - _pitchS) * _tunnelK; // dans les galeries : presque à l'horizontale, le long du couloir
        float wantDist = _distS + (MathF.Min(_distS, 3.6f) - _distS) * _tunnelK;

        // point visé : au-dessus du dos de la fourmi (selon son orientation : sur une paroi, il est devant la paroi)
        var antT = disp + Player.Orientation * new Vector3(0, 0.45f, 0);
        if (SolidP(antT)) antT = disp + Player.Orientation * new Vector3(0, 0.2f, 0);
        _target = _target.Lerp(antT, Mathf.Min(1, dt * 14));
        if (SolidP(_target)) _target = antT;
        var off = new Vector3(MathF.Sin(_yawS) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(_yawS) * MathF.Cos(pitch));
        // distance libre derrière la fourmi (jusqu'au premier obstacle)
        // distance libre derrière la fourmi ; si le point visé touche la paroi (fourmi qui grimpe),
        // on ignore le tout début du rayon tant qu'il est encore contre le mur
        float free = wantDist;
        bool started = false;
        for (float t = 0.25f; t <= wantDist; t += 0.1f)
        {
            bool c = Clear(_target + off * t);
            if (!started) { if (c) started = true; else if (t > 1.2f) { free = 0.4f; break; } continue; }
            if (!c) { free = MathF.Max(0.4f, t - 0.2f); break; }
        }
        // anti-pompage : on rentre vite, on attend un peu, puis on ressort lentement
        if (free < _colDist) { _colDist = MathF.Max(free, _colDist + (free - _colDist) * Mathf.Min(1, dt * 14)); _outT = 0; }
        else if (free > _colDist + 0.35f)
        {
            _outT += dt;
            if (_outT > 0.45f) _colDist += (free - _colDist) * Mathf.Min(1, dt * (_tunnelK > 0.5f ? 1.2f : 2.5f));
        }
        else _outT = 0;
        _colDist = MathF.Min(_colDist, wantDist);
        var camPos = _target + off * _colDist;
        if (PlayerController.Debug && (_dbg += dt) > 0.5f) { _dbg = 0; GD.Print($"[cam] tunnel {inTunnel} want {wantDist:0.0} free {free:0.0} col {_colDist:0.0} target {_target} cam {camPos}"); }
        for (int i = 0; i < 8 && SolidP(camPos); i++) camPos = camPos.Lerp(_target, 0.5f);
        Camera.GlobalPosition = camPos;
        Camera.LookAt(_target);
        ApplyShake(dt);
        // lampe frontale quand la fourmi est sous terre
        // la lampe flotte au-dessus et en arrière de la fourmi : ombres portées vers l'avant sur les parois
        _lamp.GlobalPosition = disp.Lerp(Camera.GlobalPosition, 0.45f) + new Vector3(0, 0.45f, 0);
        _lamp.LightEnergy = Mathf.Lerp(_lamp.LightEnergy, inTunnel ? 1.35f : 0, Mathf.Min(1, dt * 4));
        _lamp.Visible = _lamp.LightEnergy > 0.01f;
        _lamp.ShadowEnabled = LampShadows;
        _dust.GlobalPosition = disp + new Vector3(0, 0.8f, 0);
        _dust.Emitting = inTunnel;
    }

    void ApplyShake(float dt)
    {
        if (_shake <= 0.001f) return;
        float a = _shake * _shake * 0.35f;
        var rnd = new Vector3(GD.Randf() - 0.5f, GD.Randf() - 0.5f, GD.Randf() - 0.5f) * a;
        Camera.GlobalPosition += rnd;
        _shake = MathF.Max(0, _shake - dt * 1.6f);
    }
}
