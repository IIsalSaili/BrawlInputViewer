using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BrawlhallaOverlay;

/// <summary>
/// Icônes vectorielles des actions (game-icons.net, licence CC BY 3.0 — voir l'onglet À propos
/// du panneau de contrôle pour l'attribution complète), utilisées par le bandeau overlay du
/// Parcours (Windows/ParcoursOverlayWindow.xaml.cs).
///
/// Duplique délibérément les dictionnaires déjà présents dans MainWindow.xaml.cs (mode
/// Tutoriel) plutôt que de les en extraire — même convention que le reste du projet pour garder
/// les fenêtres indépendantes (voir par ex. le sélecteur d'arme dupliqué entre
/// ControlPanelWindow et DashboardWindow, CLAUDE.md) : toucher au panneau de combo existant pour
/// un chantier qui ne le concerne pas serait un risque inutile sur du code déjà éprouvé en jeu.
/// </summary>
public static class ActionIcons
{
    private static readonly Dictionary<string, string> BaseNames = new()
    {
        ["Saut"] = "saut",
        ["Att. légère"] = "attaque_legere",
        ["Att. forte"] = "attaque_forte",
        ["Esquive"] = "esquive",
        ["Lancer"] = "lancer",
        ["Gauche"] = "direction",
        ["Droite"] = "direction",
        ["Haut"] = "direction",
        ["Bas"] = "direction",
    };

    // Chemins Geometry (mini-langage WPF, syntaxe compatible avec le "d" SVG d'origine) sur un
    // viewBox natif 512x512 — Path.Stretch="Uniform" fait la mise à l'échelle. Contenu identique
    // à MainWindow.IconGeometryByBaseName.
    private static readonly Dictionary<string, string> GeometryByBaseName = new()
    {
        ["saut"] = "M295.883 20.338c-14.656-.098-30.21 16.152-37.057 29.625-8.19 16.117-14.16 43.37-5.826 58.734l-13.63 6.483c-5.76-3.823-46.376-13.28-63.386-10.748-27.583 6.662-52.99 20.944-78.793 33.84l12.165 26.667c23.13-10.42 42.92-28.464 69.89-30.424 21.533-1.566 34.608 11.535 50.786 18.552-1.066 68.896-16.84 101.175-54.03 160.44-26.528 16.792-61.213 17.727-94.11 22.693l12.62 28.323c40.826-5.42 80.217-10.064 108.947-26.65 58.103-41.767 85.666-62.308 148.543-92.38 30.3 9.43 41.237 39.108 55.03 61.048l24.163-22.63c-12.5-27.36-44.15-61.68-79.193-84.066-22.694 7.043-44.088 17.01-64.133 30.01 6.64-24.67 6.65-44.777-1.678-69.448 18.79 6.873 36.892 10.287 54.28 10.137 27.537-20.4 42.684-46.306 62.66-70.066L384 84.564c-16.46 18.927-25.97 37.853-49.404 56.78-16.322-1.3-32.255-8.444-48.114-16.69l-2.732-7.615c15.41-6.64 30.163-24.084 35.334-38.8 6.553-18.647 1.573-50.056-17.004-56.804a18.37 18.37 0 0 0-6.197-1.098zM18 384v110h142V384H18zm334 0v110h142V384H352z",
        ["attaque_legere"] = "M275.03 20c35.223 49.563 53.59 113.64 55.69 173.47C315.154 143 289.092 88.423 250.81 48.75c40.294 79.527 51.15 172.312 37.938 256.094-12.287-75.777-40.564-159.524-92.375-227.156 29.6 70.937 36.64 149.785 24.813 221.843-8.745-51.804-25.41-107.4-52.594-158.81 13.023 54.315 12.854 107.64 3.437 159.28l21.657 6.813 15 4.718-11.28 10.908c-10.68 10.332-19.868 21.905-27.345 34.343 93.614 35.486 232.952 64.53 298.032 41.376-41.02 56.466-210.332 13.822-309.313-18.687-1.514 3.775-2.918 7.594-4.124 11.467a152.536 152.536 0 0 0-6.062 29.657l176.47 66.375c98.5 31.095 150.5-24.62 158.655-81.72C505.253 254.472 485.016 105.66 426.06 20h-22.187c40.092 65.52 66.67 154.216 60.47 255.344-8.154-79.833-42.8-157.214-98.44-219.5 38.676 85.094 56.566 185.746 34.376 288.625.057-118.816-33.1-225.865-105.092-324.47H275.03zm-110.186 1.594c41.255 29.176 74.328 74.093 97.5 120.656-7.702-46.15-21.3-86.79-44-120.656h-53.5zm176.375 0c28.882 15.143 52.096 36.614 71.28 66.78-7.14-27.79-17.217-49.85-31.438-66.78H341.22zM123.686 304.406a179.344 179.344 0 0 1-4.062 64L18.812 336.344V366l91.938 29.094a178.602 178.602 0 0 1-30.313 48.28l50.094 15.75c-3.038-24.898-1.136-49.885 6.282-73.718 7.446-23.92 20.223-46.108 37.032-65.22l-50.156-15.78z",
        ["attaque_forte"] = "m311.313 25.625-23 10.656-29.532 123.032 60.814-111.968-8.28-21.72zM59.625 50.03c11.448 76.937 48.43 141.423 100.188 195.75a3267.323 3267.323 0 0 0 42.718-29.405c-22.156-27.314-37.85-56.204-43.593-86.28-34.214-26.492-67.613-53.376-99.312-80.064zm390.47.032C419.178 76.1 386.64 102.33 353.31 128.22c-10.333 58.234-58.087 112.074-118.218 158.624-65.433 50.654-146.56 92.934-215.28 121.406l-.002 32.78c93.65-34.132 195.55-81.378 276.875-146.592 79.035-63.378 138.329-143.063 153.41-244.375zm-236.158 9.344-8.5 27.813 40.688 73.06-6.875-85.31-25.313-15.564zm114.688 87.813C223.39 227.47 112.257 302.862 19.812 355.905V388c65.917-27.914 142.58-68.51 203.844-115.938 49.83-38.574 88.822-81.513 104.97-124.843zm-144.563 2.155c7.35 18.89 19.03 37.68 34 56.063 7.03-4.98 14.056-10.03 21.094-15.094-18.444-13.456-36.863-27.12-55.094-40.97zM352.656 269.72c-9.573 9.472-19.58 18.588-29.906 27.405 54.914 37.294 117.228 69.156 171.906 92.156V358.19c-43.86-24.988-92.103-55.13-142-88.47zm-44.906 39.81c-11.65 9.32-23.696 18.253-36.03 26.845 70.326 45.135 149.33 79.775 222.935 106.375v-33.22c-58.858-24.223-127.1-58.727-186.906-100zm-58.625 52.033l-46.188 78.25 7.813 23.593 27.75-11.344 10.625-90.5zm15.844.812L316.343 467l36.47 10.28-3.533-31.967-84.31-82.938z",
        ["esquive"] = "M396.082 17.326c-.166-.025-1.922.108-4.977.108-21.975 0-42.158 18.904-49.437 46.595l75.713 12.61-78.526 13.085c.564 16.248 5.55 30.99 13.062 42.367l54.39 9.603-41.277 7.29.484.607-15.91 2.47c-15.262 2.366-25.866 9.63-34.46 21.165-2.534 3.4-4.848 7.198-6.962 11.328l90.798 13.2-100.976 14.684a197.818 197.818 0 0 0-1.627 6.874c-1.662 7.613-2.953 15.622-3.982 23.854l115.275 14.107-117.81 14.418c-.525 9.083-.84 18.236-1.022 27.31l114.07 16.407-113.304 16.3h40.826l2.144 32.532 82.026 11.38-80.54 11.173 2.512 38.14 75.582 10.897-74.158 10.69 2.938 44.59h96.306l11.875-159.403h43.983c-.228-36.033-1.914-77.32-10.137-111.194-4.462-18.384-10.84-34.42-19.314-46.063-8.472-11.642-18.583-18.958-32.248-21.53l-15.59-2.933 10.124-12.213c10.435-12.587 17.49-30.688 17.49-51.127 0-37.056-22.084-66.04-47.127-69.295l-.106-.013-.108-.016zm-53.535 5.055L16.785 45.968l304.93 22.082c3.073-17.672 10.43-33.57 20.832-45.67zm-22.402 62.114L16.783 106.46l312.28 22.612c-5.686-12.618-8.96-27.047-8.96-42.422 0-.722.027-1.437.042-2.156zm-2.612 60.688L16.783 166.96l269.96 19.546c3.583-8.906 7.975-17.144 13.415-24.445 4.868-6.532 10.676-12.254 17.375-16.878zm-37.79 63.228-262.96 19.04L273.19 246.02c1.18-10.497 2.77-20.808 4.927-30.69.51-2.33 1.05-4.635 1.625-6.918zm-8.327 57.803L16.783 284.65l253.225 18.336c.18-12.057.585-24.438 1.408-36.773zm-1.562 60.605-253.07 18.325 297.22 21.52-1.072-16.267H269.86v-9.343c0-4.62-.01-9.38-.006-14.235zm45.294 57.22L16.783 405.64l301.227 21.81-2.862-43.413zm3.97 60.202L16.782 466.13l305.233 22.102-2.9-43.992z",
        ["lancer"] = "M167 18.813c-20.39-.002-36.813 16.92-36.813 37.312 0 20.39 16.423 36.813 36.813 36.813 12.06 0 22.896-5.747 29.75-14.657l73.094 19.595L305.5 145.75l186.844-.094-161.75-93.5-53.906 23.25L204.344 56c-.07-20.335-16.996-37.19-37.344-37.188zm0 18.656c10.29 0 18.656 8.365 18.656 18.655 0 10.288-8.366 18.156-18.656 18.156s-18.125-7.867-18.125-18.155c0-10.29 7.835-18.658 18.125-18.656zM64.062 69.874c-3.547.035-7.133.54-10.718 1.5C30.4 77.523 16.79 101.088 22.937 124.03c4.89 18.253 20.803 30.59 38.657 31.782l22.78 84.907-27.56 63.874 109.03 188.625.125-217.876-54.876-40.844-22.97-85.72c15.04-9.912 22.795-28.642 17.876-47-5.187-19.357-22.783-32.096-41.938-31.905zm.25 19.22c10.707-.108 20.57 6.99 23.47 17.81 3.435 12.825-4.177 26.003-17 29.44-12.825 3.435-26.002-4.177-29.438-17-3.436-12.825 4.144-26.003 16.968-29.44a24.125 24.125 0 0 1 6-.81zm112.438 44.28c-12.127.323-24.084 5.554-32.625 15.47-16.078 18.662-13.976 46.827 4.688 62.905 14.85 12.794 35.712 14.094 51.718 4.688l69.032 59.5 13.688 70.843 203.594 98.033L359.75 257.969 288.844 255l-69.656-60.063c7.095-17.28 2.774-37.888-12.157-50.75-8.747-7.536-19.58-11.097-30.28-10.812zm.72 19.813a24.78 24.78 0 0 1 16.905 6.03c10.432 8.988 11.612 24.756 2.625 35.188-8.987 10.432-24.724 11.58-35.156 2.594-10.432-8.987-11.612-24.724-2.625-35.156 4.773-5.542 11.47-8.476 18.25-8.656z",
        ["direction"] = "M130.81 21.785v245.95H43.84L256 489.382l212.158-221.644H381.19V21.786H130.81z",
    };

    private static readonly Dictionary<string, double> RotationDegrees = new()
    {
        ["Bas"] = 0,
        ["Gauche"] = 90,
        ["Haut"] = 180,
        ["Droite"] = 270,
    };

    /// <summary>"todo" = pas encore joué (accent doré), "done" = validé (vert), "forbidden" =
    /// action à ne PAS faire pendant une leçon AbsenceTimer (rouge, informatif seulement — ne
    /// bloque rien).</summary>
    public static Brush BrushForVariant(string variant) => variant switch
    {
        "done" => Theme.StateSuccess,
        "forbidden" => Theme.StateFail,
        _ => Theme.AccentGold,
    };

    /// <summary>Construit l'icône d'une action (Path vectoriel si dédiée, sinon un glyphe texte de
    /// repli — seul Taunt n'a pas d'icône dédiée aujourd'hui). Toujours un FrameworkElement neuf :
    /// pas de partage d'instance entre appels, pour pouvoir recolorer chaque occurrence
    /// indépendamment (voir Recolor).</summary>
    public static FrameworkElement BuildIcon(string action, double size, string variant = "todo")
    {
        if (BaseNames.TryGetValue(action, out var baseName) && GeometryByBaseName.TryGetValue(baseName, out var geometryData))
        {
            var shape = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(geometryData),
                Fill = BrushForVariant(variant),
                Stretch = Stretch.Uniform,
                Width = size,
                Height = size,
            };
            if (RotationDegrees.TryGetValue(action, out var rotation))
            {
                shape.RenderTransformOrigin = new Point(0.5, 0.5);
                shape.RenderTransform = new RotateTransform(rotation);
            }
            return shape;
        }

        return new TextBlock
        {
            Text = "💬",
            FontSize = size * 0.6,
            Foreground = BrushForVariant(variant),
            Width = size,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>Recolore une icône déjà construite par BuildIcon, quel que soit son type réel
    /// (Path vectoriel ou TextBlock de repli) — évite à l'appelant de tester le type lui-même.</summary>
    public static void Recolor(FrameworkElement icon, string variant)
    {
        var brush = BrushForVariant(variant);
        if (icon is System.Windows.Shapes.Path path) path.Fill = brush;
        else if (icon is TextBlock text) text.Foreground = brush;
    }
}
