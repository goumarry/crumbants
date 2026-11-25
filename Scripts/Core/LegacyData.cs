// ─────────────────────────────────────────────────────────────
// Le jeu s'appelait « La Fève » : Godot rangeait ses données dans
// app_userdata/La Fève. Au premier lancement sous le nom Crumbants,
// on y recopie la sauvegarde et les réglages (copie : l'original reste).
// ─────────────────────────────────────────────────────────────
using System;
using System.IO;
using Godot;

namespace Crumbants;

public static class LegacyData
{
    static readonly string[] OldNames = { "La Fève" };
    static readonly string[] Files = { "save_v1.json", "settings.cfg" };

    public static void Migrate()
    {
        try
        {
            string dir = OS.GetUserDataDir();
            string parent = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(parent)) return;
            foreach (var name in OldNames)
            {
                string old = Path.Combine(parent, name);
                if (!Directory.Exists(old) || Path.GetFullPath(old) == Path.GetFullPath(dir)) continue;
                foreach (var f in Files)
                {
                    string src = Path.Combine(old, f), dst = Path.Combine(dir, f);
                    if (!File.Exists(src) || File.Exists(dst)) continue;
                    Directory.CreateDirectory(dir);
                    File.Copy(src, dst);
                    GD.Print($"[migration] {f} recopié depuis « {name} »");
                }
            }
        }
        catch (Exception e) { GD.PushWarning($"[migration] {e.Message}"); }
    }
}
