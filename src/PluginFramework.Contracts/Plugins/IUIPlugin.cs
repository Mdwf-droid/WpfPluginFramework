using System.Windows;
using PluginFramework.Contracts.Base;
using PluginFramework.Contracts.Versioning;

namespace PluginFramework.Contracts.Plugins;

[InterfaceVersion(1, 0, 0)]
public interface IUIPlugin : IPluginBase
{
    /// <summary>Crée la vue WPF du plugin</summary>
    UIElement CreateView();

    /// <summary>Optionnel : ViewModel associé à la vue</summary>
    object? CreateViewModel() => null;

    /// <summary>Titre affiché dans l'onglet/fenêtre hôte</summary>
    string DisplayTitle => Name;

    /// <summary>Icône optionnelle (chemin ressource ou geometry)</summary>
    string? IconPath => null;
}
