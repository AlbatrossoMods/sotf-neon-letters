using System.Text.Json;
using SOTFNeonLetters;
using Xunit;

public sealed class BlueprintMaterialTransactionTests
{
    [Fact]
    public void MaterialFactoryFailureLeavesEveryRendererUnchangedAndReleasesPreparedClones()
    {
        var firstSource = new TransactionMaterial("first", cloneDepth: 0);
        var secondSource = new TransactionMaterial("second", cloneDepth: 0);
        var thirdSource = new TransactionMaterial("third", cloneDepth: 0);
        var firstRenderer = new TransactionRenderer("first-renderer", firstSource, secondSource);
        var secondRenderer = new TransactionRenderer("second-renderer", thirdSource);
        var factory = new TransactionMaterialFactory
        {
            FailAtCreation = 3
        };
        var transaction = CreateMaterialTransaction(
            factory,
            new RuntimeMaterialCatalogEntry(
                "prefab",
                new IRuntimeRendererHandle[] { firstRenderer, secondRenderer }));

        Assert.Throws<InvalidOperationException>(() => transaction.Execute());

        Assert.Equal(
            (
                FirstUnchanged: true,
                SecondUnchanged: true,
                AssignmentCount: 0,
                Released: "clone-2,clone-1"),
            (
                FirstUnchanged: firstRenderer.Materials.SequenceEqual(
                    new IRuntimeMaterialHandle[] { firstSource, secondSource }),
                SecondUnchanged: secondRenderer.Materials.SequenceEqual(
                    new IRuntimeMaterialHandle[] { thirdSource }),
                AssignmentCount: firstRenderer.AssignmentCount + secondRenderer.AssignmentCount,
                Released: string.Join(",", factory.Released.Select(material => material.Id))));
    }

    [Fact]
    public void AssignmentFailureRestoresChangedRenderersAndReleasesEveryOwnedClone()
    {
        var operations = new List<string>();
        var firstSource = new TransactionMaterial("first", cloneDepth: 0);
        var secondSource = new TransactionMaterial("second", cloneDepth: 0);
        var firstRenderer = new TransactionRenderer("first-renderer", operations, firstSource);
        var secondRenderer = new TransactionRenderer("second-renderer", operations, secondSource)
        {
            FailAfterRuntimeAssignment = true
        };
        var factory = new TransactionMaterialFactory();
        var transaction = CreateMaterialTransaction(
            factory,
            new RuntimeMaterialCatalogEntry(
                "prefab",
                new IRuntimeRendererHandle[] { firstRenderer, secondRenderer }));

        Assert.Throws<InvalidOperationException>(() => transaction.Execute());

        Assert.Equal(
            (
                FirstRestored: true,
                SecondRestored: true,
                Operations:
                    "assign:first-renderer,assign:second-renderer," +
                    "restore:second-renderer,restore:first-renderer",
                Released: "clone-2,clone-1"),
            (
                FirstRestored: firstRenderer.Materials.SequenceEqual(
                    new IRuntimeMaterialHandle[] { firstSource }),
                SecondRestored: secondRenderer.Materials.SequenceEqual(
                    new IRuntimeMaterialHandle[] { secondSource }),
                Operations: string.Join(",", operations),
                Released: string.Join(",", factory.Released.Select(material => material.Id))));
    }

    [Fact]
    public void RetryAfterAssignmentFailureCreatesOneFinalCloneLayer()
    {
        var firstSource = new TransactionMaterial("first", cloneDepth: 0);
        var secondSource = new TransactionMaterial("second", cloneDepth: 0);
        var firstRenderer = new TransactionRenderer("first-renderer", firstSource);
        var secondRenderer = new TransactionRenderer("second-renderer", secondSource)
        {
            FailAfterRuntimeAssignment = true
        };
        var factory = new TransactionMaterialFactory();
        var transaction = CreateMaterialTransaction(
            factory,
            new RuntimeMaterialCatalogEntry(
                "prefab",
                new IRuntimeRendererHandle[] { firstRenderer, secondRenderer }));
        Assert.Throws<InvalidOperationException>(() => transaction.Execute());
        secondRenderer.FailAfterRuntimeAssignment = false;

        transaction.Execute();

        Assert.Equal(
            (Depths: "1,1", Created: 4, Released: 2),
            (
                Depths: string.Join(
                    ",",
                    new[]
                    {
                    ((TransactionMaterial)firstRenderer.Materials[0]).CloneDepth,
                    ((TransactionMaterial)secondRenderer.Materials[0]).CloneDepth
                    }),
                Created: factory.Created.Count,
                Released: factory.Released.Count));
    }

    [Fact]
    public void RetainedMaterialTransactionIsIdempotentAndKeepsSdkOwnedClones()
    {
        var source = new TransactionMaterial("source", cloneDepth: 0);
        var renderer = new TransactionRenderer("renderer", source);
        var factory = new TransactionMaterialFactory();
        var transaction = CreateMaterialTransaction(
            factory,
            new RuntimeMaterialCatalogEntry(
                "prefab",
                new IRuntimeRendererHandle[] { renderer }));

        RuntimeMaterialReplacementLease firstLease = transaction.Execute();
        RuntimeMaterialReplacementLease secondLease = transaction.Execute();
        firstLease.Retain();
        firstLease.Rollback();

        Assert.Equal(
            (
                SameLease: true,
                Created: 1,
                Released: 0,
                CloneDepth: 1,
                AssignmentCount: 1),
            (
                SameLease: ReferenceEquals(firstLease, secondLease),
                Created: factory.Created.Count,
                Released: factory.Released.Count,
                CloneDepth: ((TransactionMaterial)renderer.Materials[0]).CloneDepth,
                AssignmentCount: renderer.AssignmentCount));
    }

    [Fact]
    public void CatalogTransactionResolvesShaderFactoryOnceForAllPrefabs()
    {
        int resolverCount = 0;
        var factory = new TransactionMaterialFactory();
        var firstRenderer = new TransactionRenderer(
            "first-renderer",
            new TransactionMaterial("first", cloneDepth: 0));
        var secondRenderer = new TransactionRenderer(
            "second-renderer",
            new TransactionMaterial("second", cloneDepth: 0));
        var transaction = new RuntimeMaterialCatalogTransaction(
            () =>
            {
                resolverCount++;
                return factory;
            },
            new[]
            {
                new RuntimeMaterialCatalogEntry(
                    "first-prefab",
                    new IRuntimeRendererHandle[] { firstRenderer }),
                new RuntimeMaterialCatalogEntry(
                    "second-prefab",
                    new IRuntimeRendererHandle[] { secondRenderer })
            });

        transaction.Execute();
        transaction.Execute();

        Assert.Equal(
            (ResolverCount: 1, Created: 2),
            (ResolverCount: resolverCount, Created: factory.Created.Count));
    }

    [Fact]
    public void RolledBackCatalogRetryReusesTheShaderAndClonesTheOriginalOnce()
    {
        int resolverCount = 0;
        var source = new TransactionMaterial("source", cloneDepth: 0);
        var renderer = new TransactionRenderer("renderer", source);
        var factory = new TransactionMaterialFactory();
        var transaction = new RuntimeMaterialCatalogTransaction(
            () =>
            {
                resolverCount++;
                return factory;
            },
            new[]
            {
                new RuntimeMaterialCatalogEntry(
                    "prefab",
                    new IRuntimeRendererHandle[] { renderer })
            });

        RuntimeMaterialReplacementLease firstLease = transaction.Execute();
        firstLease.Rollback();
        firstLease.Rollback();
        RuntimeMaterialReplacementLease retryLease = transaction.Execute();

        Assert.Equal(
            (
                ResolverCount: 1,
                Created: 2,
                Released: 1,
                NewLease: true,
                CloneDepth: 1),
            (
                ResolverCount: resolverCount,
                Created: factory.Created.Count,
                Released: factory.Released.Count,
                NewLease: !ReferenceEquals(firstLease, retryLease),
                CloneDepth:
                    ((TransactionMaterial)renderer.Materials[0]).CloneDepth));
    }

    [Fact]
    public void CatalogVisualValidationFailureRestoresAllAssignments()
    {
        var source = new TransactionMaterial("source", cloneDepth: 0);
        var renderer = new TransactionRenderer("renderer", source);
        var factory = new TransactionMaterialFactory();
        var transaction = CreateMaterialTransaction(
            factory,
            new RuntimeMaterialCatalogEntry(
                "prefab",
                new IRuntimeRendererHandle[] { renderer },
                () => throw new InvalidOperationException(
                    "visible material validation failed")));

        Assert.Throws<InvalidOperationException>(() => transaction.Execute());

        Assert.Equal(
            (OriginalRestored: true, Released: 1),
            (
                OriginalRestored: ReferenceEquals(
                    source,
                    renderer.Materials[0]),
                Released: factory.Released.Count));
    }

    [Theory]
    [InlineData(RuntimeAssignmentCorruption.MissingSlot)]
    [InlineData(RuntimeAssignmentCorruption.WrongShader)]
    [InlineData(RuntimeAssignmentCorruption.WrongName)]
    [InlineData(RuntimeAssignmentCorruption.WrongRenderQueue)]
    public void InvalidRendererRetentionRestoresOriginalMaterials(
        RuntimeAssignmentCorruption corruption)
    {
        var source = new TransactionMaterial("source", cloneDepth: 0);
        var renderer = new TransactionRenderer("renderer", source)
        {
            AssignmentCorruption = corruption
        };
        var factory = new TransactionMaterialFactory();
        var transaction = CreateMaterialTransaction(
            factory,
            new RuntimeMaterialCatalogEntry(
                "prefab",
                new IRuntimeRendererHandle[] { renderer }));

        Assert.Throws<InvalidOperationException>(() => transaction.Execute());

        Assert.Equal(
            (OriginalRestored: true, Released: 1),
            (
                OriginalRestored: ReferenceEquals(
                    source,
                    renderer.Materials[0]),
                Released: factory.Released.Count));
    }

    [Fact]
    public void MaterialRollbackContinuesRestoringAndReleasingAfterCleanupFailure()
    {
        var operations = new List<string>();
        var firstSource = new TransactionMaterial("first", cloneDepth: 0);
        var secondSource = new TransactionMaterial("second", cloneDepth: 0);
        var firstRenderer = new TransactionRenderer(
            "first-renderer",
            operations,
            firstSource)
        {
            FailAfterOriginalRestoration = true
        };
        var secondRenderer = new TransactionRenderer(
            "second-renderer",
            operations,
            secondSource)
        {
            FailAfterOriginalRestoration = true
        };
        var factory = new TransactionMaterialFactory
        {
            FailAfterRelease = true
        };
        RuntimeMaterialReplacementLease lease = CreateMaterialTransaction(
                factory,
                new RuntimeMaterialCatalogEntry(
                    "prefab",
                    new IRuntimeRendererHandle[] { firstRenderer, secondRenderer }))
            .Execute();

        AggregateException exception =
            Assert.Throws<AggregateException>(() => lease.Rollback());

        Assert.Equal(
            (
                CleanupFailures: 4,
                Operations:
                    "assign:first-renderer,assign:second-renderer," +
                    "restore:second-renderer,restore:first-renderer",
                FirstRestored: true,
                SecondRestored: true,
                Released: 2),
            (
                CleanupFailures: exception.InnerExceptions.Count,
                Operations: string.Join(",", operations),
                FirstRestored: ReferenceEquals(
                    firstSource,
                    firstRenderer.Materials[0]),
                SecondRestored: ReferenceEquals(
                    secondSource,
                    secondRenderer.Materials[0]),
                Released: factory.Released.Count));
    }

    [Fact]
    public void MaterialAssignmentFailurePreservesAllCleanupFailuresWhileCompletingRollback()
    {
        var operations = new List<string>();
        var assignmentFailure = new InvalidOperationException();
        var firstRestorationFailure = new ArgumentException();
        var secondRestorationFailure = new InvalidOperationException();
        var releaseFailure = new NotSupportedException();
        var firstSource = new TransactionMaterial("first", cloneDepth: 0);
        var secondSource = new TransactionMaterial("second", cloneDepth: 0);
        var firstRenderer = new TransactionRenderer(
            "first-renderer",
            operations,
            firstSource)
        {
            OriginalRestorationException = firstRestorationFailure
        };
        var secondRenderer = new TransactionRenderer(
            "second-renderer",
            operations,
            secondSource)
        {
            RuntimeAssignmentException = assignmentFailure,
            OriginalRestorationException = secondRestorationFailure
        };
        var factory = new TransactionMaterialFactory
        {
            ReleaseException = releaseFailure
        };
        var transaction = CreateMaterialTransaction(
            factory,
            new RuntimeMaterialCatalogEntry(
                "prefab",
                new IRuntimeRendererHandle[] { firstRenderer, secondRenderer }));

        AggregateException exception =
            Assert.Throws<AggregateException>(() => transaction.Execute());
        Exception[] reportedFailures =
            exception.Flatten().InnerExceptions.ToArray();

        Assert.Equal(
            (
                OriginalFailureIsFirst: true,
                OriginalFailures: 1,
                FirstRestorationFailures: 1,
                SecondRestorationFailures: 1,
                ReleaseFailures: 2,
                RestoreOrder: "restore:second-renderer,restore:first-renderer",
                FirstRestored: true,
                SecondRestored: true,
                Created: 2,
                Releases: 2,
                DistinctReleases: 2),
            (
                OriginalFailureIsFirst:
                    ReferenceEquals(
                        assignmentFailure,
                        exception.InnerExceptions[0]),
                OriginalFailures:
                    reportedFailures.Count(
                        failure => ReferenceEquals(failure, assignmentFailure)),
                FirstRestorationFailures:
                    reportedFailures.Count(
                        failure => ReferenceEquals(
                            failure,
                            firstRestorationFailure)),
                SecondRestorationFailures:
                    reportedFailures.Count(
                        failure => ReferenceEquals(
                            failure,
                            secondRestorationFailure)),
                ReleaseFailures:
                    reportedFailures.Count(
                        failure => ReferenceEquals(failure, releaseFailure)),
                RestoreOrder:
                    string.Join(
                        ",",
                        operations.Where(
                            operation => operation.StartsWith(
                                "restore:",
                                StringComparison.Ordinal))),
                FirstRestored:
                    ReferenceEquals(firstSource, firstRenderer.Materials[0]),
                SecondRestored:
                    ReferenceEquals(secondSource, secondRenderer.Materials[0]),
                Created: factory.Created.Count,
                Releases: factory.Released.Count,
                DistinctReleases: factory.Released.Distinct().Count()));
    }

    [Fact]
    public void InvalidPreparedClonePreventsEveryRendererAssignment()
    {
        var firstSource = new TransactionMaterial("first", cloneDepth: 0);
        var secondSource = new TransactionMaterial("second", cloneDepth: 0);
        var firstRenderer = new TransactionRenderer("first-renderer", firstSource);
        var secondRenderer = new TransactionRenderer("second-renderer", secondSource);
        var factory = new TransactionMaterialFactory
        {
            InvalidShaderAtCreation = 2
        };
        var transaction = CreateMaterialTransaction(
            factory,
            new RuntimeMaterialCatalogEntry(
                "prefab",
                new IRuntimeRendererHandle[] { firstRenderer, secondRenderer }));

        Assert.Throws<InvalidOperationException>(() => transaction.Execute());

        Assert.Equal(
            (
                FirstUnchanged: true,
                SecondUnchanged: true,
                Assignments: 0,
                Released: 2),
            (
                FirstUnchanged: firstRenderer.Materials.SequenceEqual(
                    new IRuntimeMaterialHandle[] { firstSource }),
                SecondUnchanged: secondRenderer.Materials.SequenceEqual(
                    new IRuntimeMaterialHandle[] { secondSource }),
                Assignments: firstRenderer.AssignmentCount + secondRenderer.AssignmentCount,
                Released: factory.Released.Count));
    }

    [Theory]
    [InlineData(CatalogPreflightCorruption.EmptyRenderers)]
    [InlineData(CatalogPreflightCorruption.EmptyMaterials)]
    [InlineData(CatalogPreflightCorruption.NullMaterial)]
    public void InvalidCatalogShapeIsRejectedBeforeAnyCloneIsCreated(
        CatalogPreflightCorruption corruption)
    {
        var factory = new TransactionMaterialFactory();
        var validRenderer = new TransactionRenderer(
            "valid-renderer",
            new TransactionMaterial("valid", cloneDepth: 0));
        RuntimeMaterialCatalogEntry invalidEntry =
            CreateInvalidCatalogEntry(corruption);
        var transaction = CreateMaterialTransaction(
            factory,
            new RuntimeMaterialCatalogEntry(
                "valid-prefab",
                new IRuntimeRendererHandle[] { validRenderer }),
            invalidEntry);

        Assert.Throws<InvalidOperationException>(() => transaction.Execute());

        Assert.Equal(
            (Created: 0, Released: 0, Assignments: 0),
            (
                Created: factory.Created.Count,
                Released: factory.Released.Count,
                Assignments: validRenderer.AssignmentCount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void CallbackFailureAtAnyMutationRestoresEveryObservableField(int failureIndex)
    {
        var state = CallbackState.CreateInitial();
        CallbackStateSnapshot initial = state.Snapshot();

        Assert.Throws<InvalidOperationException>(
            () => NeonLetterCallbackTransaction.Execute(
                transaction =>
                {
                    ApplyCallbackMutation(
                        transaction,
                        failureIndex,
                        0,
                        () => state.Placement = "wall",
                        () => state.Placement = initial.Placement);
                    ApplyCallbackMutation(
                        transaction,
                        failureIndex,
                        1,
                        () => state.IngredientCount = 2,
                        () => state.IngredientCount = initial.IngredientCount);
                    ApplyCallbackMutation(
                        transaction,
                        failureIndex,
                        2,
                        () => state.ColliderSize = 20,
                        () => state.ColliderSize = initial.ColliderSize);
                    ApplyCallbackMutation(
                        transaction,
                        failureIndex,
                        3,
                        () => state.RecipeImage = "new-image",
                        () => state.RecipeImage = initial.RecipeImage);
                    ApplyCallbackMutation(
                        transaction,
                        failureIndex,
                        4,
                        () => state.CoordinatorRecipes.Add("new-recipe"),
                        () => RestoreList(state.CoordinatorRecipes, initial.CoordinatorRecipes));
                    ApplyCallbackMutation(
                        transaction,
                        failureIndex,
                        5,
                        () => state.Pages.Add("new-page"),
                        () => RestoreList(state.Pages, initial.Pages));
                }));

        Assert.Equal(initial, state.Snapshot());
    }

    [Fact]
    public void CallbackTransactionKeepsSuccessfulValues()
    {
        var state = CallbackState.CreateInitial();

        NeonLetterCallbackTransaction.Execute(
            transaction =>
            {
                transaction.Apply(
                    () => state.Placement = "wall",
                    () => state.Placement = "ground");
                transaction.Apply(
                    () => state.ColliderSize = 20,
                    () => state.ColliderSize = 10);
                transaction.Apply(
                    () => state.RecipeImage = "new-image",
                    () => state.RecipeImage = "old-image");
                transaction.Apply(
                    () => state.CoordinatorRecipes.Add("new-recipe"),
                    () => state.CoordinatorRecipes.Remove("new-recipe"));
                transaction.Apply(
                    () => state.Pages.Add("new-page"),
                    () => state.Pages.Remove("new-page"));
            });

        Assert.Equal(
            new CallbackStateSnapshot(
                "wall",
                1,
                20,
                "new-image",
                new[] { "base-recipe", "new-recipe" },
                new[] { "base-page", "new-page" }),
            state.Snapshot());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FailureBeforeFinalProviderCommitRestoresStateAndKeepsProvider(
        int failureIndex)
    {
        var operations = new List<string>();
        var provider = new DeferredProviderRemoval(operations);
        var values = new List<string> { "initial" };

        Assert.Throws<InvalidOperationException>(
            () => NeonLetterCallbackTransaction.Execute(
                transaction =>
                {
                    ApplyCallbackMutation(
                        transaction,
                        failureIndex,
                        0,
                        () => provider.MarkPending(),
                        () => operations.Add("rollback-placement"));
                    ApplyCallbackMutation(
                        transaction,
                        failureIndex,
                        1,
                        () => values.Add("placement"),
                        () => values.Remove("placement"));
                    ApplyCallbackMutation(
                        transaction,
                        failureIndex,
                        2,
                        () => values.Add("page"),
                        () => values.Remove("page"));
                    ApplyCallbackMutation(
                        transaction,
                        failureIndex,
                        3,
                        () => values.Add("localization"),
                        () => values.Remove("localization"));
                },
                provider.Commit,
                provider.Cancel));

        Assert.Equal(
            (
                ProviderPresent: true,
                Pending: false,
                Values: "initial",
                DestroyCount: 0),
            (
                ProviderPresent: provider.IsPresent,
                Pending: provider.IsPending,
                Values: string.Join(",", values),
                DestroyCount: provider.DestroyCount));
    }

    [Fact]
    public void SuccessfulProviderCommitDestroysExactlyOnceAsTheFinalOperation()
    {
        var operations = new List<string>();
        var provider = new DeferredProviderRemoval(operations);

        NeonLetterCallbackTransaction.Execute(
            transaction =>
            {
                transaction.Apply(
                    provider.MarkPending,
                    () => operations.Add("rollback-placement"));
                transaction.Apply(
                    () => operations.Add("page"),
                    () => operations.Add("rollback-page"));
            },
            provider.Commit,
            provider.Cancel);

        Assert.Equal(
            (
                ProviderPresent: false,
                DestroyCount: 1,
                LastOperation: "destroy-provider"),
            (
                ProviderPresent: provider.IsPresent,
                DestroyCount: provider.DestroyCount,
                LastOperation: operations[^1]));
    }

    [Fact]
    public void ProviderCommitFailureBeforeDestroyRollsBackAndKeepsProvider()
    {
        var operations = new List<string>();
        var provider = new DeferredProviderRemoval(operations)
        {
            FailBeforeDestroy = true
        };
        var pages = new List<string> { "base-page" };

        Assert.Throws<InvalidOperationException>(
            () => NeonLetterCallbackTransaction.Execute(
                transaction =>
                {
                    transaction.Apply(
                        provider.MarkPending,
                        () => operations.Add("rollback-placement"));
                    transaction.Apply(
                        () => pages.Add("page-ab"),
                        () => pages.Remove("page-ab"));
                },
                provider.Commit,
                provider.Cancel));

        Assert.Equal(
            (
                ProviderPresent: true,
                Pending: false,
                Pages: "base-page",
                DestroyCount: 0),
            (
                ProviderPresent: provider.IsPresent,
                Pending: provider.IsPending,
                Pages: string.Join(",", pages),
                DestroyCount: provider.DestroyCount));
    }

    [Fact]
    public void CancelFailureStillRunsRollbackAndAggregatesCleanupErrors()
    {
        var operations = new List<string>();
        var provider = new DeferredProviderRemoval(operations)
        {
            FailAfterCancel = true
        };
        string state = "initial";

        AggregateException exception = Assert.Throws<AggregateException>(
            () => NeonLetterCallbackTransaction.Execute(
                transaction =>
                {
                    transaction.Apply(
                        () =>
                        {
                            provider.MarkPending();
                            state = "changed";
                        },
                        () => state = "initial");
                    transaction.Apply(
                        () => throw new InvalidOperationException(
                            "callback failure"),
                        () => throw new InvalidOperationException(
                            "rollback failure"));
                },
                provider.Commit,
                provider.Cancel));

        Assert.Equal(
            (
                ReportedFailures: 3,
                ProviderPresent: true,
                Pending: false,
                State: "initial"),
            (
                ReportedFailures: exception.InnerExceptions.Count,
                ProviderPresent: provider.IsPresent,
                Pending: provider.IsPending,
                State: state));
    }

    [Fact]
    public void FinalCommitFailurePreservesCancelAndReverseRollbackFailures()
    {
        var originalFailure = new InvalidOperationException();
        var cancelFailure = new NotSupportedException();
        var firstRollbackFailure = new ArgumentException();
        var secondRollbackFailure = new InvalidOperationException();
        var rollbackOrder = new List<string>();
        var state = new List<string> { "base" };
        bool cancelAttempted = false;

        AggregateException exception = Assert.Throws<AggregateException>(
            () => NeonLetterCallbackTransaction.Execute(
                transaction =>
                {
                    transaction.Apply(
                        () => state.Add("first"),
                        () =>
                        {
                            state.Remove("first");
                            rollbackOrder.Add("first");
                            throw firstRollbackFailure;
                        });
                    transaction.Apply(
                        () => state.Add("second"),
                        () =>
                        {
                            state.Remove("second");
                            rollbackOrder.Add("second");
                            throw secondRollbackFailure;
                        });
                },
                () => throw originalFailure,
                () =>
                {
                    cancelAttempted = true;
                    throw cancelFailure;
                }));
        Exception[] reportedFailures =
            exception.Flatten().InnerExceptions.ToArray();

        Assert.Equal(
            (
                OriginalFailureIsFirst: true,
                OriginalFailures: 1,
                CancelFailures: 1,
                FirstRollbackFailures: 1,
                SecondRollbackFailures: 1,
                CancelAttempted: true,
                RollbackOrder: "second,first",
                State: "base"),
            (
                OriginalFailureIsFirst:
                    ReferenceEquals(
                        originalFailure,
                        exception.InnerExceptions[0]),
                OriginalFailures:
                    reportedFailures.Count(
                        failure => ReferenceEquals(failure, originalFailure)),
                CancelFailures:
                    reportedFailures.Count(
                        failure => ReferenceEquals(failure, cancelFailure)),
                FirstRollbackFailures:
                    reportedFailures.Count(
                        failure => ReferenceEquals(
                            failure,
                            firstRollbackFailure)),
                SecondRollbackFailures:
                    reportedFailures.Count(
                        failure => ReferenceEquals(
                            failure,
                            secondRollbackFailure)),
                CancelAttempted: cancelAttempted,
                RollbackOrder: string.Join(",", rollbackOrder),
                State: string.Join(",", state)));
    }

    [Fact]
    public void FailedDeferredProviderCallbackCanRetryWithoutDuplicatePages()
    {
        var operations = new List<string>();
        var provider = new DeferredProviderRemoval(operations);
        var pages = new List<string> { "base-page" };

        Assert.Throws<InvalidOperationException>(
            () => NeonLetterCallbackTransaction.Execute(
                transaction =>
                {
                    transaction.Apply(
                        provider.MarkPending,
                        () => operations.Add("rollback-placement"));
                    transaction.Apply(
                        () =>
                        {
                            pages.Add("page-ab");
                            throw new InvalidOperationException("page failure");
                        },
                        () => pages.Remove("page-ab"));
                },
                provider.Commit,
                provider.Cancel));

        NeonLetterCallbackTransaction.Execute(
            transaction =>
            {
                transaction.Apply(
                    provider.MarkPending,
                    () => operations.Add("rollback-placement"));
                transaction.Apply(
                    () => pages.Add("page-ab"),
                    () => pages.Remove("page-ab"));
            },
            provider.Commit,
            provider.Cancel);

        Assert.Equal(
            (
                Pages: "base-page,page-ab",
                ProviderPresent: false,
                DestroyCount: 1,
                LastOperation: "destroy-provider"),
            (
                Pages: string.Join(",", pages),
                ProviderPresent: provider.IsPresent,
                DestroyCount: provider.DestroyCount,
                LastOperation: operations[^1]));
    }

    [Fact]
    public void CallbackRollbackRunsInReverseOrder()
    {
        var operations = new List<string>();

        Assert.Throws<InvalidOperationException>(
            () => NeonLetterCallbackTransaction.Execute(
                transaction =>
                {
                    transaction.Apply(
                        () => operations.Add("apply-first"),
                        () => operations.Add("rollback-first"));
                    transaction.Apply(
                        () =>
                        {
                            operations.Add("apply-second");
                            throw new InvalidOperationException("failure");
                        },
                        () => operations.Add("rollback-second"));
                }));

        Assert.Equal(
            new[]
            {
                "apply-first",
                "apply-second",
                "rollback-second",
                "rollback-first"
            },
            operations);
    }

    [Fact]
    public void CallbackRollbackContinuesAndAggregatesEveryRestoreFailure()
    {
        var operations = new List<string>();

        AggregateException exception = Assert.Throws<AggregateException>(
            () => NeonLetterCallbackTransaction.Execute(
                transaction =>
                {
                    transaction.Apply(
                        () => operations.Add("apply-first"),
                        () =>
                        {
                            operations.Add("rollback-first");
                            throw new InvalidOperationException(
                                "rollback-first failed");
                        });
                    transaction.Apply(
                        () => operations.Add("apply-second"),
                        () =>
                        {
                            operations.Add("rollback-second");
                            throw new InvalidOperationException(
                                "rollback-second failed");
                        });
                    transaction.Apply(
                        () => throw new InvalidOperationException(
                            "callback failed"),
                        () => operations.Add("rollback-third"));
                }));

        Assert.Equal(
            (
                CleanupFailures: 2,
                Operations:
                    "apply-first,apply-second,rollback-third," +
                    "rollback-second,rollback-first"),
            (
                CleanupFailures:
                    ((AggregateException)exception.InnerExceptions[1])
                    .InnerExceptions.Count,
                Operations: string.Join(",", operations)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void BookPageFailureRestoresPagesAndLocalizationEntries(int failureIndex)
    {
        var target = new TransactionalBookPageTarget
        {
            FailAfterMutation = failureIndex
        };
        BookPageTargetSnapshot initial = target.Snapshot();

        Assert.Throws<InvalidOperationException>(
            () => RegisterBookPage(target));

        Assert.Equal(initial, target.Snapshot());
    }

    [Fact]
    public void BookPageMutationFailurePreservesRestoreFailureAfterRestoringState()
    {
        var mutationFailure = new InvalidOperationException();
        var restorationFailure = new InvalidOperationException();
        var target = new TransactionalBookPageTarget
        {
            FailAfterMutation = 0,
            MutationException = mutationFailure,
            RestoreException = restorationFailure
        };
        BookPageTargetSnapshot initial = target.Snapshot();

        AggregateException exception =
            Assert.Throws<AggregateException>(() => RegisterBookPage(target));
        Exception[] reportedFailures =
            exception.Flatten().InnerExceptions.ToArray();

        Assert.Equal(
            (
                OriginalFailureIsFirst: true,
                MutationFailures: 1,
                RestorationFailures: 1,
                StateRestored: true),
            (
                OriginalFailureIsFirst:
                    ReferenceEquals(
                        mutationFailure,
                        exception.InnerExceptions[0]),
                MutationFailures:
                    reportedFailures.Count(
                        failure => ReferenceEquals(failure, mutationFailure)),
                RestorationFailures:
                    reportedFailures.Count(
                        failure => ReferenceEquals(
                            failure,
                            restorationFailure)),
                StateRestored:
                    initial.Pages.SequenceEqual(target.Pages) &&
                    initial.Localizations.SequenceEqual(
                        target.Localizations.OrderBy(pair => pair.Key))));
    }

    [Fact]
    public void BookPageRetryAfterFailureAddsOnePageWithoutDuplicateRegistration()
    {
        var target = new TransactionalBookPageTarget
        {
            FailAfterMutation = 3
        };
        Assert.Throws<InvalidOperationException>(() => RegisterBookPage(target));
        target.FailAfterMutation = null;

        RegisterBookPage(target);
        RegisterBookPage(target);

        Assert.Equal(
            (Pages: "base-page,page-ab", Localizations: 4),
            (
                Pages: string.Join(",", target.Pages),
                Localizations: target.Localizations.Count));
    }

    [Fact]
    public void InvalidBookPageCountIsRolledBackBeforeLocalizationMutation()
    {
        var target = new TransactionalBookPageTarget
        {
            ExtraPageOnCreate = true
        };
        BookPageTargetSnapshot initial = target.Snapshot();

        Assert.Throws<InvalidOperationException>(() => RegisterBookPage(target));

        Assert.Equal(initial, target.Snapshot());
    }

    [Fact]
    public void CoordinatorSnapshotAllowsFailedPageCallbackToRetryWithoutDuplicates()
    {
        NeonLetterSmallDefinition firstDefinition = NeonLetterSmallCatalog.All[0];
        NeonLetterSmallDefinition secondDefinition = NeonLetterSmallCatalog.All[1];
        var coordinator = new AlphabetBookPageCoordinator<string>();
        coordinator.Add(firstDefinition, "first-recipe");
        var pages = new List<int>();

        Assert.Throws<InvalidOperationException>(
            () => NeonLetterCallbackTransaction.Execute(
                transaction =>
                {
                    AlphabetBookPageCoordinatorSnapshot<string> snapshot =
                        coordinator.CaptureSnapshot();
                    transaction.Apply(
                        () => coordinator.Add(secondDefinition, "second-recipe"),
                        () => coordinator.Restore(snapshot));
                    transaction.Apply(
                        () =>
                        {
                            pages.Add(0);
                            throw new InvalidOperationException("page failure");
                        },
                        () => pages.RemoveAt(pages.Count - 1));
                }));

        ReadyAlphabetBookPage<string>? retry =
            coordinator.Add(secondDefinition, "second-recipe");
        if (retry != null)
        {
            pages.Add(retry.PageIndex);
            coordinator.MarkCompleted(retry.PageIndex);
        }
        ReadyAlphabetBookPage<string>? duplicate =
            coordinator.Add(secondDefinition, "second-recipe");

        Assert.Equal(
            (Pages: "0", DuplicateReady: false),
            (Pages: string.Join(",", pages), DuplicateReady: duplicate != null));
    }

    [Fact]
    public void CoordinatorRollbackRestoresPreviouslyCompletedPages()
    {
        var coordinator = new AlphabetBookPageCoordinator<string>();
        NeonLetterSmallDefinition first = NeonLetterSmallCatalog.All[0];
        NeonLetterSmallDefinition second = NeonLetterSmallCatalog.All[1];
        NeonLetterSmallDefinition third = NeonLetterSmallCatalog.All[2];
        NeonLetterSmallDefinition fourth = NeonLetterSmallCatalog.All[3];
        coordinator.Add(first, "first-recipe");
        ReadyAlphabetBookPage<string>? firstPage =
            coordinator.Add(second, "second-recipe");
        coordinator.MarkCompleted(firstPage!.PageIndex);
        AlphabetBookPageCoordinatorSnapshot<string> snapshot =
            coordinator.CaptureSnapshot();
        coordinator.Add(third, "third-recipe");
        ReadyAlphabetBookPage<string>? secondPage =
            coordinator.Add(fourth, "fourth-recipe");
        coordinator.MarkCompleted(secondPage!.PageIndex);

        coordinator.Restore(snapshot);
        coordinator.Add(third, "third-recipe");
        ReadyAlphabetBookPage<string>? restoredSecondPage =
            coordinator.Add(fourth, "fourth-recipe");

        Assert.Equal(1, restoredSecondPage?.PageIndex);
    }

    [Fact]
    public void CoordinatorPlanComputesEveryReadyPageWithoutMutatingLiveState()
    {
        var coordinator = new AlphabetBookPageCoordinator<string>();
        NeonLetterSmallDefinition first = NeonLetterSmallCatalog.All[0];
        NeonLetterSmallDefinition second = NeonLetterSmallCatalog.All[1];
        NeonLetterSmallDefinition third = NeonLetterSmallCatalog.All[2];
        NeonLetterSmallDefinition fourth = NeonLetterSmallCatalog.All[3];
        coordinator.Add(first, "first-recipe");
        coordinator.Add(third, "third-recipe");
        coordinator.Add(fourth, "fourth-recipe");

        AlphabetBookPageCoordinatorPlan<string> plan =
            coordinator.PrepareAdd(second, "second-recipe");

        Assert.Equal(
            (
                PlannedPages: "0,1",
                LiveStateChanged: false),
            (
                PlannedPages: string.Join(
                    ",",
                    plan.ReadyPages.Select(page => page.PageIndex)),
                LiveStateChanged: coordinator.GetNextReadyPage() != null));
    }

    [Fact]
    public void ManifestRequiresLoader086()
    {
        string manifestPath = FindRepositoryFile("manifest.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        JsonElement root = document.RootElement;

        Assert.Equal(
            "0.8.6",
            root.GetProperty("loaderVersion").GetString());
    }

    private static RuntimeMaterialCatalogTransaction CreateMaterialTransaction(
        TransactionMaterialFactory factory,
        params RuntimeMaterialCatalogEntry[] entries)
    {
        return new RuntimeMaterialCatalogTransaction(() => factory, entries);
    }

    private static RuntimeMaterialCatalogEntry CreateInvalidCatalogEntry(
        CatalogPreflightCorruption corruption)
    {
        return corruption switch
        {
            CatalogPreflightCorruption.EmptyRenderers =>
                new RuntimeMaterialCatalogEntry(
                    "invalid-prefab",
                    Array.Empty<IRuntimeRendererHandle>()),
            CatalogPreflightCorruption.EmptyMaterials =>
                new RuntimeMaterialCatalogEntry(
                    "invalid-prefab",
                    new IRuntimeRendererHandle[]
                    {
                        new TransactionRenderer("invalid-renderer")
                    }),
            CatalogPreflightCorruption.NullMaterial =>
                new RuntimeMaterialCatalogEntry(
                    "invalid-prefab",
                    new IRuntimeRendererHandle[]
                    {
                        new TransactionRenderer(
                            "invalid-renderer",
                            new IRuntimeMaterialHandle[] { null! })
                    }),
            _ => throw new ArgumentOutOfRangeException(nameof(corruption))
        };
    }

    private static void ApplyCallbackMutation(
        NeonLetterCallbackTransaction transaction,
        int failureIndex,
        int mutationIndex,
        Action mutation,
        Action rollback)
    {
        transaction.Apply(
            () =>
            {
                mutation();
                if (failureIndex == mutationIndex)
                {
                    throw new InvalidOperationException($"failure-{mutationIndex}");
                }
            },
            rollback);
    }

    private static void RestoreList<T>(List<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        target.AddRange(source);
    }

    private static void RegisterBookPage(TransactionalBookPageTarget target)
    {
        BookPageRegistrar.Register(
            "BLUEPRINT_PAGE_SOTF_NEON_LETTERS",
            "Neon Symbols",
            "recipe-a",
            "Neon Letter A (Small)",
            "recipe-b",
            "Neon Letter B (Small)",
            "page-ab",
            target);
    }

    private static string FindRepositoryFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find repository file '{relativePath}'.");
    }
}

internal sealed class TransactionMaterialFactory :
    IRuntimeMaterialFactory,
    IRuntimeMaterialOwner
{
    private int _creationCount;

    public string ShaderName => "HDRP/Lit";
    public bool IsShaderSupported => true;
    public int? FailAtCreation { get; init; }
    public int? InvalidShaderAtCreation { get; init; }
    public bool FailAfterRelease { get; init; }
    public Exception? ReleaseException { get; init; }
    public List<TransactionMaterial> Created { get; } = new();
    public List<TransactionMaterial> Released { get; } = new();

    public IRuntimeMaterialHandle Create()
    {
        _creationCount++;
        if (_creationCount == FailAtCreation)
        {
            throw new InvalidOperationException("factory failure");
        }

        var material = new TransactionMaterial($"clone-{_creationCount}", cloneDepth: 0)
        {
            ShaderName = _creationCount == InvalidShaderAtCreation
                ? "invalid"
                : ShaderName
        };
        Created.Add(material);
        return material;
    }

    public void Release(IRuntimeMaterialHandle material)
    {
        Released.Add((TransactionMaterial)material);
        if (ReleaseException != null)
        {
            throw ReleaseException;
        }

        if (FailAfterRelease)
        {
            throw new InvalidOperationException("material release failure");
        }
    }
}

internal sealed class TransactionRenderer : IRuntimeRendererHandle
{
    private readonly List<string>? _operations;

    public TransactionRenderer(
        string name,
        params IRuntimeMaterialHandle[] materials)
        : this(name, operations: null, materials)
    {
    }

    public TransactionRenderer(
        string name,
        List<string>? operations,
        params IRuntimeMaterialHandle[] materials)
    {
        Name = name;
        _operations = operations;
        Materials = materials;
    }

    public string Name { get; }
    public IReadOnlyList<IRuntimeMaterialHandle> Materials { get; private set; }
    public bool FailAfterRuntimeAssignment { get; set; }
    public bool FailAfterOriginalRestoration { get; set; }
    public Exception? RuntimeAssignmentException { get; set; }
    public Exception? OriginalRestorationException { get; set; }
    public RuntimeAssignmentCorruption AssignmentCorruption { get; set; }
    public int AssignmentCount { get; private set; }

    public void SetMaterials(IReadOnlyList<IRuntimeMaterialHandle> materials)
    {
        bool isRuntimeAssignment = materials.Any(
            material => material.Name.EndsWith("_Runtime", StringComparison.Ordinal));
        _operations?.Add(
            $"{(isRuntimeAssignment ? "assign" : "restore")}:{Name}");
        Materials = materials;
        if (!isRuntimeAssignment)
        {
            if (OriginalRestorationException != null)
            {
                throw OriginalRestorationException;
            }

            if (FailAfterOriginalRestoration)
            {
                throw new InvalidOperationException(
                    "original restoration failure");
            }

            return;
        }

        AssignmentCount++;
        if (RuntimeAssignmentException != null)
        {
            throw RuntimeAssignmentException;
        }

        if (FailAfterRuntimeAssignment)
        {
            throw new InvalidOperationException("assignment failure");
        }

        if (AssignmentCorruption == RuntimeAssignmentCorruption.None)
        {
            return;
        }

        if (AssignmentCorruption ==
            RuntimeAssignmentCorruption.MissingSlot)
        {
            Materials = Array.Empty<IRuntimeMaterialHandle>();
            return;
        }

        var expected = (TransactionMaterial)materials[0];
        var retained = new TransactionMaterial(
            "retained-copy",
            expected.CloneDepth)
        {
            Name = expected.Name,
            ShaderName = expected.ShaderName,
            RenderQueue = expected.RenderQueue
        };
        switch (AssignmentCorruption)
        {
            case RuntimeAssignmentCorruption.WrongShader:
                retained.ShaderName = "invalid";
                break;
            case RuntimeAssignmentCorruption.WrongName:
                retained.Name = "invalid";
                break;
            case RuntimeAssignmentCorruption.WrongRenderQueue:
                retained.RenderQueue++;
                break;
        }

        Materials = new IRuntimeMaterialHandle[] { retained };
    }
}

public enum RuntimeAssignmentCorruption
{
    None,
    MissingSlot,
    WrongShader,
    WrongName,
    WrongRenderQueue
}

public enum CatalogPreflightCorruption
{
    EmptyRenderers,
    EmptyMaterials,
    NullMaterial
}

internal sealed class TransactionMaterial : IRuntimeMaterialHandle
{
    public TransactionMaterial(string id, int cloneDepth)
    {
        Id = id;
        Name = id;
        CloneDepth = cloneDepth;
        ShaderKeywords = Array.Empty<string>();
    }

    public string Id { get; }
    public int CloneDepth { get; private set; }
    public string Name { get; set; }
    public string ShaderName { get; set; } = "bundle";
    public int RenderQueue { get; set; } = 2450;
    public object ShaderKeywords { get; set; }

    public void CopyPropertiesFrom(IRuntimeMaterialHandle source)
    {
        CloneDepth = ((TransactionMaterial)source).CloneDepth + 1;
    }
}

internal sealed class CallbackState
{
    public string Placement { get; set; } = string.Empty;
    public int IngredientCount { get; set; }
    public int ColliderSize { get; set; }
    public string RecipeImage { get; set; } = string.Empty;
    public List<string> CoordinatorRecipes { get; } = new();
    public List<string> Pages { get; } = new();

    public static CallbackState CreateInitial()
    {
        var state = new CallbackState
        {
            Placement = "ground",
            IngredientCount = 1,
            ColliderSize = 10,
            RecipeImage = "old-image"
        };
        state.CoordinatorRecipes.Add("base-recipe");
        state.Pages.Add("base-page");
        return state;
    }

    public CallbackStateSnapshot Snapshot()
    {
        return new CallbackStateSnapshot(
            Placement,
            IngredientCount,
            ColliderSize,
            RecipeImage,
            CoordinatorRecipes.ToArray(),
            Pages.ToArray());
    }
}

internal sealed record CallbackStateSnapshot(
    string Placement,
    int IngredientCount,
    int ColliderSize,
    string RecipeImage,
    IReadOnlyList<string> CoordinatorRecipes,
    IReadOnlyList<string> Pages)
{
    public bool Equals(CallbackStateSnapshot? other)
    {
        return other != null &&
               Placement == other.Placement &&
               IngredientCount == other.IngredientCount &&
               ColliderSize == other.ColliderSize &&
               RecipeImage == other.RecipeImage &&
               CoordinatorRecipes.SequenceEqual(other.CoordinatorRecipes) &&
               Pages.SequenceEqual(other.Pages);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            Placement,
            IngredientCount,
            ColliderSize,
            RecipeImage,
            CoordinatorRecipes.Count,
            Pages.Count);
    }
}

internal sealed class TransactionalBookPageTarget :
    ITransactionalBookPageRegistrationTarget<string, string>
{
    private int _mutationIndex;

    public List<string> Pages { get; } = new() { "base-page" };
    public Dictionary<string, string> Localizations { get; } =
        new() { ["base-key"] = "base-value" };
    public int? FailAfterMutation { get; set; }
    public Exception? MutationException { get; set; }
    public Exception? RestoreException { get; set; }
    public bool ExtraPageOnCreate { get; set; }
    public int PageCount => Pages.Count;

    public void AddLocalization(string key, string value)
    {
        Localizations[key] = value;
        FailIfRequested();
    }

    public string GetRecipeLocalizationId(string recipe)
    {
        return $"localized-{recipe}";
    }

    public void CreatePage(
        string topRecipe,
        string? bottomRecipe,
        string background,
        string titleLocalizationKey)
    {
        if (ExtraPageOnCreate)
        {
            Pages.Add($"{background}-duplicate");
        }

        Pages.Add(background);
        FailIfRequested();
    }

    public bool LastPageMatches(
        string topRecipe,
        string? bottomRecipe,
        string background,
        string titleLocalizationKey)
    {
        return Pages[^1] == background;
    }

    public object CaptureRegistrationState(
        string titleLocalizationKey,
        string topRecipe,
        string? bottomRecipe)
    {
        _mutationIndex = 0;
        return Snapshot();
    }

    public void RestoreRegistrationState(object snapshot)
    {
        BookPageTargetSnapshot state = (BookPageTargetSnapshot)snapshot;
        Pages.Clear();
        Pages.AddRange(state.Pages);
        Localizations.Clear();
        foreach ((string key, string value) in state.Localizations)
        {
            Localizations.Add(key, value);
        }

        if (RestoreException != null)
        {
            throw RestoreException;
        }
    }

    public BookPageTargetSnapshot Snapshot()
    {
        return new BookPageTargetSnapshot(
            Pages.ToArray(),
            Localizations.OrderBy(pair => pair.Key).ToArray());
    }

    private void FailIfRequested()
    {
        int mutationIndex = _mutationIndex++;
        if (mutationIndex == FailAfterMutation)
        {
            if (MutationException != null)
            {
                throw MutationException;
            }

            throw new InvalidOperationException($"book mutation failure {mutationIndex}");
        }
    }
}

internal sealed record BookPageTargetSnapshot(
    IReadOnlyList<string> Pages,
    IReadOnlyList<KeyValuePair<string, string>> Localizations)
{
    public bool Equals(BookPageTargetSnapshot? other)
    {
        return other != null &&
               Pages.SequenceEqual(other.Pages) &&
               Localizations.SequenceEqual(other.Localizations);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Pages.Count, Localizations.Count);
    }
}

internal sealed class DeferredProviderRemoval
{
    private readonly List<string> _operations;

    public DeferredProviderRemoval(List<string> operations)
    {
        _operations = operations;
    }

    public bool IsPresent { get; private set; } = true;
    public bool IsPending { get; private set; }
    public int DestroyCount { get; private set; }
    public bool FailBeforeDestroy { get; init; }
    public bool FailAfterCancel { get; init; }

    public void MarkPending()
    {
        IsPending = true;
        _operations.Add("provider-removal-pending");
    }

    public void Cancel()
    {
        IsPending = false;
        if (FailAfterCancel)
        {
            throw new InvalidOperationException(
                "provider cancellation failed");
        }
    }

    public void Commit()
    {
        if (!IsPending)
        {
            throw new InvalidOperationException(
                "Provider removal was not prepared.");
        }

        if (FailBeforeDestroy)
        {
            throw new InvalidOperationException(
                "provider destruction failed before removal");
        }

        IsPresent = false;
        IsPending = false;
        DestroyCount++;
        _operations.Add("destroy-provider");
    }
}
