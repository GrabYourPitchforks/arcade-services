// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

/// <summary>
/// This class is able to initialize an individual repository within the VMR for the first time.
/// It pulls in the new sources adhering to cloaking rules, accommodating for patched files, resolving submodules.
/// It can also initialize all other repositories recursively based on the dependencies stored in Version.Details.xml.
/// </summary>
public class VmrInitializer : VmrManagerBase, IVmrInitializer
{
    // Message shown when initializing an individual repo for the first time
    private const string InitializationCommitMessage =
        $$"""
        [{name}] Initial pull of the individual repository ({newShaShort})

        Original commit: {remote}/commit/{newSha}

        {{Constants.AUTOMATION_COMMIT_TAG}}
        """;

    // Message used when finalizing the initialization with a merge commit
    private const string MergeCommitMessage =
        $$"""
        Recursive initialization for {name} / {newShaShort}

        {{Constants.AUTOMATION_COMMIT_TAG}}
        """;

    private readonly IVmrInfo _vmrInfo;
    private readonly IBasicBarClient _barClient;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IVmrPatchHandler _patchHandler;
    private readonly IRepositoryCloneManager _cloneManager;
    private readonly IWorkBranchFactory _workBranchFactory;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrUpdater> _logger;

    public VmrInitializer(
        IVmrDependencyTracker dependencyTracker,
        IVmrPatchHandler patchHandler,
        IVersionDetailsParser versionDetailsParser,
        IRepositoryCloneManager cloneManager,
        IThirdPartyNoticesGenerator thirdPartyNoticesGenerator,
        ICodeownersGenerator codeownersGenerator,
        ICredScanSuppressionsGenerator credScanSuppressionsGenerator,
        ILocalGitClient localGitClient,
        ILocalGitRepoFactory localGitRepoFactory,
        IDependencyFileManager dependencyFileManager,
        IWorkBranchFactory workBranchFactory,
        IBasicBarClient barClient,
        IFileSystem fileSystem,
        ILogger<VmrUpdater> logger,
        ISourceManifest sourceManifest,
        IVmrInfo vmrInfo)
        : base(vmrInfo, sourceManifest, dependencyTracker, patchHandler, versionDetailsParser, thirdPartyNoticesGenerator, codeownersGenerator, credScanSuppressionsGenerator, localGitClient, localGitRepoFactory, dependencyFileManager, barClient, fileSystem, logger)
    {
        _vmrInfo = vmrInfo;
        _barClient = barClient;
        _dependencyTracker = dependencyTracker;
        _patchHandler = patchHandler;
        _cloneManager = cloneManager;
        _workBranchFactory = workBranchFactory;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task InitializeRepository(
        string mappingName,
        string? targetRevision,
        string? targetVersion,
        bool initializeDependencies,
        LocalPath sourceMappingsPath,
        CodeFlowParameters codeFlowParameters,
        bool lookUpBuilds,
        CancellationToken cancellationToken)
    {
        await _dependencyTracker.RefreshMetadata(sourceMappingsPath);
        var mapping = _dependencyTracker.GetMapping(mappingName);

        string? officialBuildId = null;
        int? barId = null;

        if (lookUpBuilds)
        {
            var build = (await _barClient.GetBuildsAsync(mapping.DefaultRemote, targetRevision))
                .FirstOrDefault();

            officialBuildId = build?.AzureDevOpsBuildNumber;
            barId = build?.Id;
        }

        if (_dependencyTracker.GetDependencyVersion(mapping) is not null)
        {
            throw new EmptySyncException($"Repository {mapping.Name} already exists");
        }

        var workBranchName = $"init/{mapping.Name}";
        if (targetRevision != null)
        {
            workBranchName += $"/{targetRevision}";
        }

        IWorkBranch workBranch = await _workBranchFactory.CreateWorkBranchAsync(GetLocalVmr(), workBranchName);

        var rootUpdate = new VmrDependencyUpdate(
            mapping,
            mapping.DefaultRemote,
            targetRevision ?? mapping.DefaultRef,
            targetVersion,
            null,
            officialBuildId,
            barId);

        try
        {
            IEnumerable<VmrDependencyUpdate> updates = initializeDependencies
                ? await GetAllDependenciesAsync(rootUpdate, codeFlowParameters.AdditionalRemotes, lookUpBuilds, cancellationToken)
                : [rootUpdate];

            foreach (var update in updates)
            {
                var sourcesPath = _vmrInfo.GetRepoSourcesPath(update.Mapping);
                if (_fileSystem.DirectoryExists(sourcesPath) && _fileSystem.GetFiles(sourcesPath).Length > 1)
                {
                    if (_dependencyTracker.GetDependencyVersion(update.Mapping) == null)
                    {
                        throw new InvalidOperationException(
                            $"Sources for {update.Mapping.Name} already exists but repository is not initialized properly. " +
                             "Please investigate!");
                    }

                    // Repository has already been initialized
                    continue;
                }

                await InitializeRepository(
                    update,
                    codeFlowParameters,
                    cancellationToken);
            }
        }
        catch (Exception)
        {
            _logger.LogWarning(
                InterruptedSyncExceptionMessage,
                workBranch.OriginalBranch.StartsWith("sync") || workBranch.OriginalBranch.StartsWith("init") ?
                "the original" : workBranch.OriginalBranch);
            throw;
        }

        string newSha = _dependencyTracker.GetDependencyVersion(mapping)?.Sha
            ?? throw new Exception($"Repository {mapping.Name} was supposed to be but has not been initialized! " +
                                    "Please make sure the sources folder is empty!");

        var commitMessage = PrepareCommitMessage(MergeCommitMessage, mapping.Name, mapping.DefaultRemote, oldSha: null, newSha);
        await workBranch.MergeBackAsync(commitMessage);

        _logger.LogInformation("Recursive initialization for {repo} / {sha} finished", mapping.Name, newSha);
    }

    private async Task InitializeRepository(
        VmrDependencyUpdate update,
        CodeFlowParameters codeFlowParameters,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing {name} at {revision}..", update.Mapping.Name, update.TargetRevision);

        var remotes = codeFlowParameters.AdditionalRemotes
            .Where(r => r.Mapping == update.Mapping.Name)
            .Select(r => r.RemoteUri)
            .Prepend(update.RemoteUri)
            .ToArray();

        ILocalGitRepo clone = await _cloneManager.PrepareCloneAsync(
            update.Mapping,
            remotes,
            new[] { update.TargetRevision },
            update.TargetRevision,
            resetToRemote: false,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        update = update with
        {
            TargetRevision = await clone.GetShaForRefAsync(update.TargetRevision)
        };

        string commitMessage = PrepareCommitMessage(InitializationCommitMessage, update.Mapping.Name, update.RemoteUri, newSha: update.TargetRevision);

        await UpdateRepoToRevisionAsync(
            update,
            clone,
            Constants.EmptyGitObject,
            commitMessage,
            restoreVmrPatches: false,
            codeFlowParameters,
            cancellationToken);

        // We apply the VMR patches for the first time
        var repoVmrPatches = _patchHandler.GetVmrPatches()
            .Where(patch => patch.ApplicationPath!.Path.StartsWith(VmrInfo.GetRelativeRepoSourcesPath(update.Mapping)));
        await ReapplyVmrPatchesAsync([.. repoVmrPatches], cancellationToken);

        _logger.LogInformation("Initialization of {name} finished", update.Mapping.Name);
    }

    // VMR initialization does not need to restore patches,
    // the repository is new and does not have those applied
    protected override Task<IReadOnlyCollection<VmrIngestionPatch>> StripVmrPatchesAsync(IReadOnlyCollection<VmrIngestionPatch> patches, IReadOnlyCollection<AdditionalRemote> additionalRemotes, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyCollection<VmrIngestionPatch>>([]);
}
