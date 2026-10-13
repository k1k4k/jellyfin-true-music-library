using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.TrueMusicLibrary.Analysis;

/// <summary>
/// Tâche planifiée « Analyser la médiathèque musicale » : calcule les artistes principaux de chaque album et le
/// rapport, sans rien modifier dans Jellyfin. Aucun déclencheur par défaut : lancée à la main, depuis la page du
/// plugin ou les tâches planifiées.
/// </summary>
public sealed class AnalyzeTask : IScheduledTask
{
    public const string TaskKey = "TrueMusicLibraryAnalyze";

    private readonly AnalysisService _analysis;

    public AnalyzeTask(AnalysisService analysis)
    {
        _analysis = analysis;
    }

    public string Name => "Analyser la médiathèque musicale";

    public string Key => TaskKey;

    public string Description => "True Music Library : calcule les artistes principaux de chaque album et le rapport, sans rien modifier.";

    public string Category => "True Music Library";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        using var gate = await TaskGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await _analysis.AnalyzeAsync(progress, cancellationToken).ConfigureAwait(false);
    }
}
