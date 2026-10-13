using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Jellyfin.Plugin.TrueMusicLibrary.Analysis;
using Jellyfin.Plugin.TrueMusicLibrary.Apply;
using Jellyfin.Plugin.TrueMusicLibrary.Diagnostics;
using Jellyfin.Plugin.TrueMusicLibrary.Persistence;
using Jellyfin.Plugin.TrueMusicLibrary.Resolution;
using Jellyfin.Plugin.TrueMusicLibrary.Store;
using MediaBrowser.Common.Api;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.TrueMusicLibrary.Api;

/// <summary>Corps de <c>PUT /TrueMusicLibrary/Overrides/{albumId}</c>.</summary>
public sealed class OverrideRequest
{
    [Required]
    public List<string> MainArtists { get; set; } = [];

    public string? Note { get; set; }
}

/// <summary>API d'administration du plugin.</summary>
[ApiController]
[Route(Plugin.RoutePrefix)]
[Authorize(Policy = Policies.RequiresElevation)]
public class TrueMusicLibraryController : ControllerBase
{
    private readonly ProviderTrace _trace;
    private readonly DecisionStore _store;
    private readonly PersistenceState _persistence;
    private readonly ITaskManager _taskManager;

    public TrueMusicLibraryController(ProviderTrace trace, DecisionStore store, PersistenceState persistence, ITaskManager taskManager)
    {
        _trace = trace;
        _store = store;
        _persistence = persistence;
        _taskManager = taskManager;
    }

    /// <summary>GET /TrueMusicLibrary/Status : version, cible de compilation, configuration et état de la persistance.</summary>
    [HttpGet("Status")]
    public ActionResult<object> GetStatus()
    {
        var state = _persistence.State;
        return new
        {
            version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
#if JF10
            target = "jf10",
#else
            target = "jf12",
#endif
            configuration = Plugin.Instance?.Configuration,
            persistence = new
            {
                enabled = PersistenceState.IsEnabled,
                active = PersistenceState.IsEnabled && state is not null,
                since = state?.ActivatedAt,
                updatedAt = state?.UpdatedAt,
                albums = state?.Albums.Count ?? 0,
                pendingProviderChanges = _persistence.JournalCount
            }
        };
    }

    /// <summary>GET /TrueMusicLibrary/Analysis : dernier rapport d'analyse (404 s'il n'y en a pas).</summary>
    [HttpGet("Analysis")]
    public ActionResult GetAnalysis()
    {
        var json = _store.ReadAnalysisJson();
        return json is null ? NotFound() : Content(json, "application/json");
    }

    /// <summary>POST /TrueMusicLibrary/Analysis/Run : lance la tâche d'analyse (lecture seule).</summary>
    [HttpPost("Analysis/Run")]
    public ActionResult RunAnalysis()
    {
        _taskManager.QueueScheduledTask<AnalyzeTask>();
        return NoContent();
    }

    /// <summary>GET /TrueMusicLibrary/Analysis/Status : état de la tâche d'analyse.</summary>
    [HttpGet("Analysis/Status")]
    public ActionResult<object> GetAnalysisStatus()
    {
        var status = TaskStatus<AnalyzeTask>();
        return status is null ? NotFound() : status;
    }

    /// <summary>GET /TrueMusicLibrary/Tasks/Status : état des tâches Analyser, Réparer, Restaurer.</summary>
    [HttpGet("Tasks/Status")]
    public ActionResult<object> GetTasksStatus() => new
    {
        analyze = TaskStatus<AnalyzeTask>(),
        repair = TaskStatus<RepairTask>(),
        restore = TaskStatus<RestoreTask>(),
        canRestore = _store.LatestRestorableSnapshot() is not null
    };

    /// <summary>POST /TrueMusicLibrary/Repair/Run : lance la réparation (instantané avant écriture).</summary>
    [HttpPost("Repair/Run")]
    public ActionResult RunRepair()
    {
        _taskManager.QueueScheduledTask<RepairTask>();
        return NoContent();
    }

    /// <summary>POST /TrueMusicLibrary/Restore/Run : remet l'état d'avant la première réparation et coupe la persistance.</summary>
    [HttpPost("Restore/Run")]
    public ActionResult RunRestore()
    {
        _taskManager.QueueScheduledTask<RestoreTask>();
        return NoContent();
    }

    /// <summary>GET /TrueMusicLibrary/Operation/Last : bilan de la dernière réparation, restauration ou mise à jour après scan.</summary>
    [HttpGet("Operation/Last")]
    public ActionResult GetLastOperation()
    {
        var json = _store.ReadLastResultJson();
        return json is null ? NotFound() : Content(json, "application/json");
    }

    /// <summary>GET /TrueMusicLibrary/Snapshots : instantanés de réparation et de persistance (sans le détail des éléments).</summary>
    [HttpGet("Snapshots")]
    public ActionResult<object> GetSnapshots()
        => _store.LoadSnapshots().Select(s => new
        {
            s.Id,
            s.Origin,
            s.CreatedAt,
            s.Restored,
            s.RestoredAt,
            Items = s.Items.Count,
            DeletedArtists = s.DeletedArtists.Count
        }).ToList();

    private object? TaskStatus<T>()
        where T : IScheduledTask
    {
        var worker = _taskManager.ScheduledTasks.FirstOrDefault(w => w.ScheduledTask is T);
        if (worker is null)
        {
            return null;
        }

        var last = worker.LastExecutionResult;
        return new
        {
            state = worker.State.ToString(),
            progress = worker.CurrentProgress,
            lastStatus = last?.Status.ToString(),
            lastEnd = last?.EndTimeUtc,
            lastError = last?.ErrorMessage
        };
    }

    /// <summary>GET /TrueMusicLibrary/Overrides : choix manuels.</summary>
    [HttpGet("Overrides")]
    public ActionResult<Dictionary<string, OverrideEntry>> GetOverrides() => _store.LoadOverrides();

    /// <summary>PUT /TrueMusicLibrary/Overrides/{albumId} : impose les artistes principaux d'un album.</summary>
    [HttpPut("Overrides/{albumId}")]
    public ActionResult SetOverride([FromRoute] string albumId, [FromBody] OverrideRequest request)
    {
        if (!Guid.TryParse(albumId, out var id))
        {
            return BadRequest();
        }

        var names = request.MainArtists.Select(n => n.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count == 0)
        {
            return BadRequest();
        }

        _store.SetOverride(id.ToString("N"), new OverrideEntry { MainArtists = names, Note = request.Note, UpdatedAt = DateTime.UtcNow });
        return NoContent();
    }

    /// <summary>DELETE /TrueMusicLibrary/Overrides/{albumId}.</summary>
    [HttpDelete("Overrides/{albumId}")]
    public ActionResult DeleteOverride([FromRoute] string albumId)
    {
        if (!Guid.TryParse(albumId, out var id))
        {
            return BadRequest();
        }

        _store.SetOverride(id.ToString("N"), null);
        return NoContent();
    }

    /// <summary>GET /TrueMusicLibrary/Trace : trace des appels des fournisseurs (diagnostic, en mémoire).</summary>
    [HttpGet("Trace")]
    public ActionResult<IReadOnlyList<ProviderTraceEntry>> GetTrace() => Ok(_trace.Snapshot());

    /// <summary>DELETE /TrueMusicLibrary/Trace.</summary>
    [HttpDelete("Trace")]
    public ActionResult ClearTrace()
    {
        _trace.Clear();
        return NoContent();
    }
}
