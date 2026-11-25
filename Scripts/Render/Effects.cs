// ─────────────────────────────────────────────────────────────
// Effets : éclats de miettes, étincelles, textes flottants,
// surlignage du bloc visé, éboulis qui tombent, fève visible.
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using Godot;
using Crumbants.Sim;
using static Crumbants.Cfg;

namespace Crumbants.Render;

public partial class Effects : Node3D
{
    readonly List<CpuParticles3D> _bursts = new(), _sparks = new();
    int _nb, _ns;
    MeshInstance3D _aim;
    ShaderMaterial _aimMat;
    MultiMeshInstance3D _rubble;
    GameState _game;
    Node3D _feve;
    MeshInstance3D _feveBeam;
    public Font Font;
    Sprite3D _hint;

    public void Setup(GameState game, Font font)
    {
        _game = game;
        Font = font;
        var crumbMesh = new BoxMesh { Size = Vector3.One * 0.09f };
        crumbMesh.Material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.85f };
        var sparkMesh = new QuadMesh { Size = Vector2.One * 0.12f };
        sparkMesh.Material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        };
        for (int i = 0; i < 10; i++)
        {
            var b = new CpuParticles3D
            {
                Emitting = false, OneShot = true, Amount = 14, Lifetime = 0.9f, Explosiveness = 0.95f,
                Mesh = crumbMesh, Direction = Vector3.Up, Spread = 70, InitialVelocityMin = 1.2f, InitialVelocityMax = 3.2f,
                Gravity = new Vector3(0, -14, 0), AngularVelocityMin = -400, AngularVelocityMax = 400,
                ScaleAmountMin = 0.6f, ScaleAmountMax = 1.5f, LocalCoords = false,
            };
            AddChild(b); _bursts.Add(b);
            var s = new CpuParticles3D
            {
                Emitting = false, OneShot = true, Amount = 30, Lifetime = 1.1f, Explosiveness = 0.9f,
                Mesh = sparkMesh, Direction = Vector3.Up, Spread = 180, InitialVelocityMin = 1.5f, InitialVelocityMax = 4,
                Gravity = new Vector3(0, -3, 0), ScaleAmountMin = 0.5f, ScaleAmountMax = 1.6f, LocalCoords = false,
            };
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 1)); curve.AddPoint(new Vector2(1, 0));
            s.ScaleAmountCurve = curve;
            AddChild(s); _sparks.Add(s);
        }
        _aimMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/aim.gdshader") };
        _aim = new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One * 1.04f }, MaterialOverride = _aimMat, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_aim);
        var rubbleMesh = new BoxMesh { Size = Vector3.One * 0.96f };
        rubbleMesh.Material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.9f };
        _rubble = new MultiMeshInstance3D
        {
            Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = rubbleMesh, InstanceCount = 1500, VisibleInstanceCount = 0 },
        };
        AddChild(_rubble);
        BuildFeve();
        // halo de la fève signalée : visible à travers le gâteau
        var img = Image.CreateEmpty(128, 128, false, Image.Format.Rgba8);
        for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
        {
            float d = new Vector2(x - 63.5f, y - 63.5f).Length() / 64f;
            float a = Mathf.Clamp(1 - d, 0, 1); a = a * a * (0.6f + 0.4f * a);
            img.SetPixel(x, y, new Color(1, 0.85f, 0.4f, a));
        }
        _hint = new Sprite3D
        {
            Texture = ImageTexture.CreateFromImage(img), PixelSize = 0.03f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true, Shaded = false, Visible = false, Modulate = new Color(1, 1, 1, 0.9f), RenderPriority = 5,
        };
        AddChild(_hint);
    }

    // Fève de porcelaine : haricot aplati, liseré bleu peint, petite couronne dorée
    public static Node3D FeveModel()
    {
        var root = new Node3D();
        var porcelain = new StandardMaterial3D
        {
            AlbedoColor = new Color("#fff6e2"), Roughness = 0.1f, ClearcoatEnabled = true, Clearcoat = 1, ClearcoatRoughness = 0.04f,
            RimEnabled = true, Rim = 0.4f, RimTint = 0.3f, EmissionEnabled = true, Emission = new Color("#ffe6a0"), EmissionEnergyMultiplier = 0.15f,
        };
        var blue = new StandardMaterial3D { AlbedoColor = new Color("#2b5fae"), Roughness = 0.25f, ClearcoatEnabled = true, Clearcoat = 1 };
        var gold = new StandardMaterial3D { AlbedoColor = new Color("#f2c64a"), Metallic = 0.9f, Roughness = 0.2f, EmissionEnabled = true, Emission = new Color("#ffb020"), EmissionEnergyMultiplier = 0.4f };
        root.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.34f, Height = 0.68f, RadialSegments = 48, Rings = 24 }, MaterialOverride = porcelain, Scale = new Vector3(1.35f, 0.72f, 1f), Position = new Vector3(0, 0.25f, 0) });
        root.AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.325f, OuterRadius = 0.36f, Rings = 48, RingSegments = 10 }, MaterialOverride = blue, Scale = new Vector3(1.35f, 1, 1), Position = new Vector3(0, 0.25f, 0) });
        // couronne : bandeau + 5 pointes perlées
        var crown = new Node3D { Position = new Vector3(0, 0.49f, 0) };
        root.AddChild(crown);
        crown.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.12f, Height = 0.06f, RadialSegments = 24 }, MaterialOverride = gold });
        for (int i = 0; i < 5; i++)
        {
            float a = i * MathF.Tau / 5;
            var tip = new Vector3(MathF.Cos(a) * 0.11f, 0.1f, MathF.Sin(a) * 0.11f);
            crown.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.0f, BottomRadius = 0.045f, Height = 0.12f, RadialSegments = 8 }, MaterialOverride = gold, Position = tip - new Vector3(0, 0.03f, 0) });
            crown.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.022f, Height = 0.044f }, MaterialOverride = gold, Position = tip + new Vector3(0, 0.035f, 0) });
        }
        return root;
    }

    void BuildFeve()
    {
        _feve = new Node3D { Visible = false, Name = "Feve" };
        _feve.AddChild(FeveModel());
        _feve.AddChild(new OmniLight3D { LightColor = new Color(1, 0.82f, 0.4f), LightEnergy = 1.5f, OmniRange = 4, Position = new Vector3(0, 0.6f, 0) });
        var beam = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/beam.gdshader") };
        beam.SetShaderParameter("height", 30f);
        beam.SetShaderParameter("intensity", 0.9f);
        _feveBeam = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.9f, BottomRadius = 0.35f, Height = 30, CapTop = false, CapBottom = false },
            MaterialOverride = beam, Position = new Vector3(0, 15, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _feve.AddChild(_feveBeam);
        AddChild(_feve);
        // la fève enfouie dans chaque gâteau : invisible tant que la pâte l'entoure,
        // elle apparaît (et luit doucement) quand on creuse tout près
        foreach (var K in _game.Cakes)
        {
            var n = FeveModel();
            n.Name = $"Buried{K.Index}";
            var glow = new OmniLight3D { LightColor = new Color(1, 0.82f, 0.4f), LightEnergy = 0.8f, OmniRange = 2.5f, Position = new Vector3(0, 0.4f, 0), Visible = false };
            n.AddChild(glow);
            AddChild(n);
            _buried.Add((K, n, glow));
        }
    }
    readonly List<(CakeSim K, Node3D n, OmniLight3D glow)> _buried = new();

    public static Color LayerColor(CakeSim K, float y)
    {
        var t = K.Theme;
        if (y > Layers - 1.2f) return t.Glaze;
        if (y < 1.5f || y > 10.3f) return t.Crust;
        return MathF.Abs(y - 6) < 0.6f ? t.Accent : t.Fill;
    }

    public void Burst(Vector3 p, Color c, int n = 14, float speed = 1)
    {
        var b = _bursts[_nb++ % _bursts.Count];
        b.GlobalPosition = p;
        b.Amount = Math.Max(1, n);
        b.Color = c;
        b.InitialVelocityMin = 1.2f * speed; b.InitialVelocityMax = 3.2f * speed;
        b.Restart();
    }

    public void Sparkle(Vector3 p, Color c, int n = 30, float speed = 1)
    {
        var s = _sparks[_ns++ % _sparks.Count];
        s.GlobalPosition = p;
        s.Amount = Math.Max(1, n);
        s.Color = c;
        s.InitialVelocityMin = 1.5f * speed; s.InitialVelocityMax = 4 * speed;
        s.Restart();
    }

    public void FloatText(string text, Vector3 p, Color c, bool big = false)
    {
        var l = new Label3D
        {
            Text = text, Font = Font, FontSize = big ? 110 : 76, PixelSize = 0.0035f, Modulate = c,
            OutlineSize = big ? 26 : 18, OutlineModulate = new Color(0.25f, 0.13f, 0.05f, 0.9f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, FixedSize = false,
            Position = p, RenderPriority = 10, OutlineRenderPriority = 9,
        };
        AddChild(l);
        var tw = l.CreateTween();
        tw.SetParallel();
        tw.TweenProperty(l, "position", p + new Vector3(0, 1.6f, 0), 1.3f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(l, "modulate:a", 0f, 0.5f).SetDelay(0.85f);
        tw.TweenProperty(l, "outline_modulate:a", 0f, 0.5f).SetDelay(0.85f);
        tw.Chain().TweenCallback(Callable.From(l.QueueFree));
    }

    public void SetAim(AimHitView? a)
    {
        if (a is not { } h) { _aim.Visible = false; return; }
        _aim.Visible = true;
        _aim.GlobalPosition = new Vector3(h.X + 0.5f, h.Y + 0.5f, h.Z + 0.5f);
        _aimMat.SetShaderParameter("color", h.InRange ? new Color(1, 0.95f, 0.8f) : new Color(1, 0.25f, 0.2f));
    }

    public void UpdateFrame(float dt, Vector3 camPos)
    {
        // éboulis en chute
        var mm = _rubble.Multimesh;
        int n = 0;
        foreach (var K in _game.Cakes)
        foreach (var f in K.Falling)
        {
            if (n >= mm.InstanceCount) break;
            var p = new Vector3(f.I - K.IX + 0.5f, f.Y + 0.5f, f.K - K.IZ + 0.5f);
            mm.SetInstanceTransform(n, new Transform3D(Basis.Identity, p));
            mm.SetInstanceColor(n, f.Crust != 0 ? K.Theme.Glaze : LayerColor(K, f.Y0));
            n++;
        }
        mm.VisibleInstanceCount = n;
        // halo de la fève signalée par une ouvrière
        var FK = _game.FeveCake();
        bool hinted = FK != null && FK.FeveHint && FK.Feve >= 0 && _game.FeveObj == null;
        _hint.Visible = hinted;
        if (hinted)
        {
            _hint.GlobalPosition = FK.WPos(FK.Feve) + new Vector3(0, 0.5f, 0);
            float pulse = 0.8f + 0.2f * MathF.Sin(Time.GetTicksMsec() / 300f);
            _hint.Scale = Vector3.One * pulse;
        }
        // la fève enfouie : couchée dans son bloc, un peu de biais
        foreach (var (K, bn, glow) in _buried)
        {
            bool inside = K.Feve >= 0 && K.Unlocked;
            bn.Visible = inside;
            if (!inside) continue;
            bn.Position = K.WPos(K.Feve) + new Vector3(0.05f, 0.18f, -0.08f);
            bn.Rotation = new Vector3(0.35f, K.Feve * 0.37f, 0.2f);
            int x = K.Feve % Area % CW - K.IX, z = K.Feve % Area / CW - K.IZ, y = K.Feve / Area;
            bool open = false;
            foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
                if (!K.SolidAt(x + dx, y + dy, z + dz)) open = true;
            glow.Visible = open;
        }
        // la fève
        var o = _game.FeveObj;
        _feve.Visible = o != null;
        if (o != null)
        {
            _feve.GlobalPosition = o.Pos + (o.State == FeveState.Carried ? new Vector3(0, 0.1f, 0) : Vector3.Zero);
            _feve.RotateY(dt * 1.6f);
            _feve.Scale = Vector3.One * (o.State == FeveState.Carried ? 0.7f : 1f);
            _feveBeam.Visible = o.State == FeveState.Ground;
        }
    }
}

public struct AimHitView
{
    public int X, Y, Z;
    public bool InRange;
}
