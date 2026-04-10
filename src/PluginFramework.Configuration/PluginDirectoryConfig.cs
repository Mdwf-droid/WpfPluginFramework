namespace PluginFramework.Configuration;

public class PluginDirectoryConfig
{
    /// <summary>Chemin absolu ou relatif du dossier de plugins</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Explorer les sous-dossiers ?</summary>
    public bool Recursive { get; set; } = true;

    /// <summary>Si false et que le dossier n'existe pas, une erreur est loguée</summary>
    public bool Optional { get; set; } = true;

    /// <summary>Priorité de chargement (plus bas = chargé en premier)</summary>
    public int Priority { get; set; } = 100;

    /// <summary>Pattern de recherche spécifique à ce dossier (override du global)</summary>
    public string? SearchPatternOverride { get; set; }
}
