namespace Jellyfin.Plugin.TrueMusicLibrary.Resolution;

/// <summary>
/// Textes montrés à l'administrateur (remarques du rapport, tâches planifiées, erreurs), en français ou en anglais selon la
/// langue de l'interface du serveur. Le plugin branche <see cref="IsFrench"/> sur la configuration de Jellyfin.
/// </summary>
public static class Messages
{
    /// <summary>Vrai si les textes doivent être en français (sinon anglais).</summary>
    public static Func<bool> IsFrench { get; set; } = () => false;

    private static string T(string fr, string en) => IsFrench() ? fr : en;

    // Remarques du rapport
    public static string MusicBrainzUnavailable => T("MusicBrainz indisponible pour cette analyse.", "MusicBrainz unavailable for this analysis.");

    public static string ProviderUnavailable(string provider) => T($"Fournisseur « {provider} » indisponible pour cette analyse : ignoré.", $"Provider \"{provider}\" unavailable for this analysis: skipped.");

    public static string AnalysisError(string error) => T("Erreur pendant l'analyse : " + error, "Error during the analysis: " + error);

    public static string SuspiciousMbid(string name, string mbName, string mbid)
        => T($"« {name} » est rattaché à l'artiste MusicBrainz « {mbName} » ({mbid}) : identifiant ignoré.", $"\"{name}\" is linked to the MusicBrainz artist \"{mbName}\" ({mbid}): identifier ignored.");

    public static string AlbumLocked => T("Album verrouillé dans Jellyfin : rien n'est modifié.", "Album locked in Jellyfin: nothing is changed.");

    public static string Override(string note) => T("Choix manuel : " + note, "Manual choice: " + note);

    public static string ReleaseRejected(string source, string? title, int? matched, int tracks)
        => T(
            $"{source} : release « {title} » écartée ({matched}/{tracks} pistes reconnues, artiste sans rapport avec le dossier ni les tags)",
            $"{source}: release \"{title}\" rejected ({matched}/{tracks} tracks matched, artist unrelated to the folder and the tags)");

    public static string NoSource => T("Aucune source exploitable : artistes actuels gardés.", "No usable source: current artists kept.");

    public static string FolderDisagrees(string folder) => T("Dossier : " + folder, "Folder: " + folder);

    public static string TagsDisagree(string tags) => T("Tags : " + tags, "Tags: " + tags);

    /// <summary>Nom par défaut des compilations sans dossier artiste.</summary>
    public static string VariousArtists => T("Artistes variés", "Various Artists");

    // Tâches planifiées
    public static string Category => "Jellyfin True Music Library";

    public static string AnalyzeName => T("Analyser la médiathèque musicale", "Analyze the music library");

    public static string AnalyzeDescription => T(
        "True Music Library : calcule les artistes principaux de chaque album et le rapport, sans rien modifier.",
        "True Music Library: works out the main artists of every album and the report, without changing anything.");

    public static string RepairName => T("Réparer la médiathèque musicale", "Repair the music library");

    public static string RepairDescription => T(
        "True Music Library : applique les artistes principaux proposés par l'analyse. Un instantané est enregistré avant toute écriture : « Restaurer » revient en arrière.",
        "True Music Library: applies the main artists proposed by the analysis. A snapshot is saved before anything is written: \"Restore\" undoes it.");

    public static string RestoreName => T("Restaurer la médiathèque musicale", "Restore the music library");

    public static string RestoreDescription => T(
        "True Music Library : remet les artistes tels qu'ils étaient avant la première réparation, et arrête de les corriger pendant les scans.",
        "True Music Library: puts the artists back the way they were before the first repair, and stops fixing them during scans.");

    // Erreurs des tâches
    public static string OtherOperationRunning => T("Une autre opération True Music Library est déjà en cours.", "Another True Music Library operation is already running.");

    public static string ScanRunningRepair => T("Un scan de bibliothèque est en cours : relancez la réparation quand il sera terminé.", "A library scan is running: start the repair again once it has finished.");

    public static string ScanRunningRestore => T("Un scan de bibliothèque est en cours : relancez la restauration quand il sera terminé.", "A library scan is running: start the restore again once it has finished.");

    public static string NothingToRestore => T("Aucune réparation à restaurer.", "No repair to restore.");
}
