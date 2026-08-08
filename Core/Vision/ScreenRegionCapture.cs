using System;

namespace BrawlhallaOverlay;

/// <summary>
/// Capture GDI (System.Drawing.Graphics.CopyFromScreen) d'un petit rectangle d'écran en
/// pixels physiques. Volontairement pas Windows.Graphics.Capture/DXGI : pour une ROI de
/// quelques centaines de pixels à ~16 Hz (voir HudDamageSource.PollIntervalMs — la mention
/// "8-10 Hz" datait d'avant la Version 24, audit 2026-08-07 §F10), BitBlt classique est
/// largement suffisant et évite toute l'interop WinRT (device D3D11, DispatcherQueueController)
/// que demanderait WGC pour un gain de perf ici inutile — voir docs/plan_improve_combo.md §4.1.
/// Même limite que le reste de l'overlay : ne capture pas un jeu en plein écran exclusif.
/// </summary>
public static class ScreenRegionCapture
{
    /// <summary>Capture le rectangle donné (coordonnées physiques d'écran) et retourne les
    /// pixels au format BGR 24 bits, ligne par ligne, sans padding de stride (compacté).
    /// Retourne null si la capture échoue (zone hors écran, permissions, etc.) — jamais
    /// d'exception qui remonterait jusqu'à l'appelant (HudDamageSource doit rester best-effort).</summary>
    public static byte[]? Capture(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) return null;

        try
        {
            using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = System.Drawing.Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height), System.Drawing.CopyPixelOperation.SourceCopy);
            }

            var rect = new System.Drawing.Rectangle(0, 0, width, height);
            var data = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            try
            {
                var stride = data.Stride;
                var rowBytes = width * 3;
                var result = new byte[rowBytes * height];
                for (var row = 0; row < height; row++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + row * stride, result, row * rowBytes, rowBytes);
                }
                return result;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }
        catch
        {
            return null;
        }
    }
}
