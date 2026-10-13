using Jellyfin.Plugin.TrueMusicLibrary.Resolution;
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

    public string Name => Messages.AnalyzeName;

    public string Key => TaskKey;

    public string Description => Messages.AnalyzeDescription;

    public string Category => Messages.Category;

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        using var gate = await TaskGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await _analysis.AnalyzeAsync(progress, cancellationToken).ConfigureAwait(false);
    }
}
