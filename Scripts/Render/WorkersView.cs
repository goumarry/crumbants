// ─────────────────────────────────────────────────────────────
// Affichage des ouvrières (essaim instancié, interpolé entre deux
// pas de simulation) et du personnel (gestionnaire, conductrices,
// cuisinières…), qui partagent le même squelette.
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using Godot;
using Crumbants.Sim;
using static Crumbants.Cfg;

namespace Crumbants.Render;

public partial class WorkersView : Node3D
{
    GameState _game;
    AntSwarm _workers, _staff;
    readonly Dictionary<int, (float gait, float walk, Vector3 last, float pitch)> _anim = new();
    static readonly Color WorkerCol = new("#7a3a1c");
    float _time;
    public readonly List<AntPose> StaffPoses = new();

    public void Setup(GameState game)
    {
        _game = game;
        _workers = AntSwarm.Create(80);
        AddChild(_workers);
        _staff = AntSwarm.Create(256, true, helmets: true);
        AddChild(_staff);
    }

    // radiographie (G) : silhouettes rouges des ouvrières à travers les murs
    ShaderMaterial _xrayMat;
    bool _xray;
    public void SetXray(bool on)
    {
        if (on == _xray) return;
        _xray = on;
        if (on) _xrayMat ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/xray_ant.gdshader"), RenderPriority = 4 };
        _workers.SetOverlay(on ? _xrayMat : null);
    }

    public void UpdateFrame(float dt, float alpha)
    {
        _time += dt;
        var ws = _game.WStats();
        _workers.Begin();
        foreach (var w in _game.Workers)
        {
            var p = w.PrevPos.Lerp(w.Pos, Mathf.Clamp(alpha, 0, 1));
            // au sol, la fourmi suit le relief (rebord des assiettes, dôme du nid)
            if (p.Y < 1.3f) p.Y = MathF.Max(p.Y, Terrain.GroundAt(p.X, p.Z));
            if (!_anim.TryGetValue(w.Id, out var a)) a = (0, 0, p, 0);
            var d = p - a.last;
            var h = new Vector2(d.X, d.Z);
            float moved = h.Length();
            a.gait += moved / (AntRig.Stride * 0.75f) * MathF.Tau;
            a.walk += ((moved / MathF.Max(dt, 1e-4f) > 0.3f ? 1 : 0) - a.walk) * Mathf.Min(1, dt * 8);
            // sur une rampe, le corps suit la pente
            float slope = moved > 1e-4f ? -MathF.Atan2(d.Y, moved) : 0;
            a.pitch += (Mathf.Clamp(slope, -0.6f, 0.6f) - a.pitch) * Mathf.Min(1, dt * 8);
            a.last = p;
            _anim[w.Id] = a;
            _workers.Add(new AntPose
            {
                Pos = p, Yaw = w.Yaw, Pitch = a.pitch, Scale = 0.75f, Gait = a.gait, Walk = a.walk,
                Bite = w.BiteT, Load = ws.load > 0 ? Mathf.Clamp(w.Load / (float)ws.load, 0, 1) : 0,
                LoadColor = Cfg.CrumbColor(Math.Max(0, w.LoadC)),
                Time = _time + w.Id * 1.37f, Stun = w.Stun > 0 ? 1 : 0, Color = WorkerCol,
                // au fond du trou du nid, elle disparaît sous terre
                Hidden = new Vector2(p.X - Nest.X, p.Z - Nest.Y).Length() < 0.45f && p.Y > 0.5f,
            });
        }
        _workers.End();

        _staff.Begin();
        foreach (var sp in StaffPoses) _staff.Add(sp);
        _staff.End();
        StaffPoses.Clear();
    }
}
