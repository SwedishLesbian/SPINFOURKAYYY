using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpinFourKay.Core.Backup;
using SpinFourKay.Core.Configuration;
using SpinFourKay.Core.Display;
using SpinFourKay.Core.Layouts;
using SpinFourKay.Core.Magpie;
using SpinFourKay.Core.Orchestration;
using SpinFourKay.Core.Preferences;
using SpinFourKay.Core.Startup;
using SpinFourKay.Core.Updates;
using SpinFourKay.Core.Windows;

namespace SpinFourKay.SelfTest;

internal static class Program
{
    private static readonly byte[] Utf8Preamble = [0xEF, 0xBB, 0xBF];

    private static async Task<int> Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("SpinFOURKAYYY deterministic self-test");
        Console.WriteLine("------------------------------------");

        TestRunner runner = new();

        runner.Add("Resolution / 4K preset matrix", Resolution4KPresetMatrix);
        runner.Add("Resolution / ultrawide fit", ResolutionUltrawideFit);
        runner.Add(
            "Resolution / fine UI-scale quantization and bounds",
            FineUiScaleQuantizationAndBounds);
        runner.Add(
            "Resolution / exact 1-percent steps and filter normalization",
            GenericUiScaleStepAndFilterPolicy);
        runner.Add(
            "Resolution / automatic scale and monitor policy",
            AutomaticAttachScaleAndMonitorPolicy);
        runner.Add("Resolution / fine 1-percent scale matrix", ResolutionFineScaleMatrix);
        runner.Add("Resolution / non-4K safety floor", ResolutionNon4KSafetyFloor);
        runner.Add(
            "Resolution / nearest-neighbor integer fallback",
            ResolutionNearestNeighborIntegerFallback);
        runner.Add("Resolution / invalid input errors", ResolutionInvalidInputErrors);
        runner.Add(
            "SpinUI resolution / validated source catalog",
            SpinUiValidatedSourceCatalog);
        runner.Add(
            "SpinUI resolution / required monitor matrix",
            SpinUiRequiredMonitorMatrix);
        runner.Add(
            "SpinUI resolution / exact source metadata and filters",
            SpinUiExactSourceMetadataAndFilters);
        runner.Add(
            "SpinUI resolution / bounded rejection paths",
            SpinUiBoundedRejectionPaths);
        runner.Add("INI / case, duplicates, comments", IniCaseDuplicatesAndComments);
        runner.Add("INI / mixed newlines and insertion", IniMixedNewlinesAndInsertion);
        runner.Add("INI / BOM and legacy-byte preservation", IniBomAndLegacyBytePreservationAsync);
        runner.Add("INI / invalid input errors", IniInvalidInputErrors);
        runner.Add(
            "UI layout / generic geometry preserves personal data",
            UiLayoutGenericGeometryPreservesPersonalData);
        runner.Add(
            "UI layout / automatic profile capture and native restore",
            UiLayoutAutomaticProfileLifecycleAsync);
        runner.Add(
            "UI layout / rollback and interrupted restore recovery",
            UiLayoutRollbackAndInterruptedRestoreRecoveryAsync);
        runner.Add(
            "UI layout / stale session recovered before relaunch",
            UiLayoutStaleSessionRecoveredBeforeRelaunchAsync);
        runner.Add(
            "Preferences / complete atomic round trip",
            PreferencesAtomicRoundTripAsync);
        runner.Add(
            "Preferences / corruption and invalid values use safe defaults",
            PreferencesCorruptionAndValidationAsync);
        runner.Add(
            "Startup / play switches, conflicts, and unknown arguments",
            StartupSwitchInterpretation);
        runner.Add(
            "Shortcuts / desktop plan contents and icon fallback",
            DesktopShortcutPlanContentsAsync);
        runner.Add(
            "Shortcuts / verified .lnk round trip and refusals",
            WindowsShortcutVerifiedRoundTripAsync);
        runner.Add(
            "Engine runtime / superseded instances are not foreign Magpie",
            SupersededEngineInstancesAreNotForeign);
        runner.Add(
            "Engine runtime / superseded folders pruned, live ones retained",
            SupersededEngineRuntimePruningAsync);
        runner.Add(
            "Updates / GitHub release discovery and verified staging",
            UpdateDiscoveryAndVerifiedStagingAsync);
        runner.Add(
            "Updates / checksum and archive attacks fail closed",
            UpdatePackageFailuresAreFailClosedAsync);
        runner.Add(
            "Updates / transactional install preserves settings and rolls back",
            UpdateTransactionalInstallAndRollbackAsync);
        runner.Add("Configuration / atomic fake-client edit", ConfigurationAtomicFakeClientEditAsync);
        runner.Add("Configuration / native UI-scale guard", ConfigurationNativeUiScaleGuardAsync);
        runner.Add("Configuration / missing file and cancellation", ConfigurationErrorPathsAsync);
        runner.Add(
            "Configuration / player-state protection scope",
            PlayerStateProtectionScopeAsync);
        runner.Add("Backup / checksums, de-duplication, idempotent restore", BackupRoundTripAsync);
        runner.Add("Backup / corrupted payload blocks every live write", BackupCorruptionIsConflictSafeAsync);
        runner.Add("Backup / traversal and malformed input errors", BackupErrorPathsAsync);
        runner.Add("DPI / compatibility-token merge is lossless", DpiTokenMergeIsLossless);
        runner.Add("DPI / validation never reaches registry", DpiValidationAvoidsRegistry);
        runner.Add(
            "DPI / persisted intent and legacy crash recovery",
            DpiPersistedIntentAndLegacyCrashRecovery);
        runner.Add("Magpie / portable JSON schema and exact profile", MagpiePortableJsonSchemaAsync);
        runner.Add("Magpie / profile upsert and every filter", MagpieProfileUpsertAndFiltersAsync);
        runner.Add(
            "Magpie / readable scale threshold and isolated effects",
            MagpieReadableScaleThresholdAsync);
        runner.Add(
            "Magpie / stable versioned runtime survives source replacement",
            MagpieStableRuntimeProvisioningAsync);
        runner.Add(
            "Magpie / config transaction rollback and conflict preservation",
            MagpieConfigTransactionRollbackAsync);
        runner.Add("Magpie / malformed and invalid requests", MagpieErrorPathsAsync);
        runner.Add("Magpie / coordinate mapping and aspect guard", CoordinateMappingGuard);
        runner.Add("Magpie / scaling-window inspection is non-invasive", ScalingWindowInspectionAsync);
        runner.Add(
            "Alt+Tab supervision / background suspension is unbounded",
            AltTabBackgroundSuspensionIsUnbounded);
        runner.Add(
            "Alt+Tab supervision / foreground resume and exact acceptance",
            AltTabForegroundResumeAndExactAcceptance);
        runner.Add(
            "Alt+Tab supervision / engine-owned resume sends no toggle",
            AltTabEngineOwnedResumeSendsNoToggle);
        runner.Add(
            "Alt+Tab supervision / resume timeout stops owned session",
            AltTabResumeTimeoutStopsOwnedSession);
        runner.Add(
            "Alt+Tab supervision / health failures stop immediately",
            AltTabHealthFailuresStopImmediately);
        runner.Add(
            "Alt+Tab supervision / invalid policy inputs fail closed",
            AltTabInvalidPolicyInputs);
        runner.Add("Windows / process discovery validation", ProcessDiscoveryValidationAsync);
        runner.Add("Windows / safe window discovery validation", WindowDiscoveryValidationAsync);
        runner.Add(
            "Windows / managed-launch interaction readiness policy",
            ManagedLaunchInteractionReadinessPolicyAsync);
        runner.Add(
            "Windows / physical-monitor client placement planner",
            PhysicalMonitorClientPlacementPlanner);
        runner.Add(
            "Windows / native-size overlay placement and filtering",
            OverlayPlacementAndFiltering);
        runner.Add(
            "Windows / 4K 125-percent overlay source-shadow mapping",
            Overlay125PercentSourceShadowMapping);
        runner.Add(
            "Windows / overlay promotion, discovery, and exact restoration",
            OverlayCompatibilityLifecycle);
        runner.Add(
            "Windows / EQ Legends Companion overlay lifecycle",
            EqLegendsCompanionOverlayLifecycle);
        runner.Add(
            "Windows / overlay source-shadow input pass-through",
            OverlayCompatibilityInputPassThrough);
        runner.Add(
            "Windows / overlay event recovery defeats topmost races",
            OverlayCompatibilityEventRecovery);
        runner.Add(
            "Windows / native overlay observer lifecycle",
            NativeOverlayObserverLifecycle);
        runner.Add("Journal / save, update, active order, restored", JournalLifecycleAsync);
        runner.Add(
            "Journal / legacy prepared geometry remains exact",
            JournalLegacyPreparedGeometryAsync);
        runner.Add(
            "Journal / prepared UI mode cannot be relabeled",
            JournalPreparedUiModeCannotBeRelabeledAsync);
        runner.Add(
            "Journal / newest recovery session preserves each UI mode",
            JournalNewestRecoverySessionPreservesEachUiModeAsync);
        runner.Add("Journal / duplicate, missing, corrupt, version errors", JournalErrorPathsAsync);
        runner.Add("Preparation / reversible prepared state and pending blocker", PreparationAndPendingBlockerAsync);
        runner.Add(
            "Preparation / managed launch commits config without player-state restore",
            ManagedLaunchPreparationCommitsConfigurationOnlyAsync);
        runner.Add(
            "Preparation / explicit custom mode ignores installed SpinUI assets",
            PreparationExplicitCustomModeIgnoresInstalledSpinUiAssetsAsync);
        runner.Add("Preparation / failure rolls back INI and DPI", PreparationRollbackAsync);
        runner.Add(
            "Preparation / interrupted DPI apply retains recovery intent",
            InterruptedDpiApplyRetainsRecoveryIntentAsync);
        runner.Add("Preparation / restore preserves post-prepare conflict", RestoreConflictRecoveryAsync);
        runner.Add("Preparation / running game blocks restore", RestoreGameStillRunningAsync);
        runner.Add(
            "Preparation / final writer race remains recoverable",
            RestoreFinalWriterRaceRemainsRecoverableAsync);
        runner.Add("Preparation / request and running-game validation", PreparationValidationAsync);
        runner.Add(
            "Preparation / invalid native scale stops before managed writes",
            PreparationInvalidNativeScalePreflightAsync);
        runner.Add("Launch / requests fail before external actions", LaunchRequestValidationAsync);
        runner.Add(
            "Launch / automatic Attach identity rejects replacements before effects",
            AttachExpectedProcessIdentityRejectsBeforeEffectsAsync);
        runner.Add(
            "Launch / prepared checksum and native scale guards",
            LaunchConfigurationIntegrityGuardsAsync);
        runner.Add(
            "Launch / prepared target monitor must match the plan",
            LaunchTargetMonitorMismatchRejectsBeforeEffectsAsync);
        runner.Add("Launch / external Magpie conflict prevents process launch", LaunchExternalMagpieConflictAsync);
        runner.Add(
            "Launch / Attach enforces confirmed client resolution",
            AttachEnforcesExpectedClientResolutionAsync);
        runner.Add(
            "Launch / generic Attach applies requested client size",
            AttachGenericRequestedClientSizeAsync);
        runner.Add(
            "Launch / generic Attach resize failure restores exact geometry",
            AttachGenericResizeFailureRestoresExactGeometryAsync);
        runner.Add(
            "Launch / generic Attach rollback failure is fail closed",
            AttachGenericResizeRollbackFailureIsFailClosedAsync);
        runner.Add(
            "Launch / same-size generic and strict Attach restore full placement",
            AttachSameSizeFailuresRestoreFullPlacementAsync);
        runner.Add(
            "Launch / post-scaler generic resize restores full placement",
            AttachPostScalerFailureRestoresFullPlacementAsync);
        runner.Add(
            "Launch / startup attachment identity and geometry are exact",
            LaunchAttachmentIdentityAndGeometryAreExactAsync);
        runner.Add(
            "Launch / exact startup attachment succeeds without cleanup",
            LaunchExactAttachmentSucceedsWithoutCleanupAsync);
        runner.Add(
            "Launch / overlay compatibility activates after safe attachment",
            LaunchOverlayCompatibilityActivatesAfterSafeAttachmentAsync);
        runner.Add(
            "Live scale / strict and Exact-pixel requests reject before effects",
            LiveScaleStrictAndNearestRejectBeforeEffectsAsync);
        runner.Add(
            "Live scale / successful transaction commits exact geometry",
            LiveScaleSuccessfulCommitUsesExpectedGeometryAsync);
        runner.Add(
            "Live scale / resize and validation failures restore previous geometry",
            LiveScaleFailuresRestorePreviousGeometryAsync);
        runner.Add(
            "Live scale / rollback failure retains both causes",
            LiveScaleRollbackFailureRetainsBothCausesAsync);
        runner.Add(
            "Live scale / source and scaler focus fail closed around mutation",
            LiveScaleFocusReturningFailsClosedAsync);
        runner.Add(
            "Live scale / post-resize output ownership and safety",
            LiveScalePostResizeOutputValidationAsync);
        runner.Add(
            "Live scale / every non-fatal post-placement failure rolls back",
            LiveScaleUnexpectedFailureAfterPlacementRollsBackAsync);
        runner.Add(
            "Live scale / monitor bounds drift rejects before mutation",
            LiveScaleMonitorBoundsDriftRejectsAsync);
        runner.Add(
            "Live scale / unrelated INI changes remain compatible",
            LiveScaleUnrelatedIniChangesAreAllowedAsync);
        runner.Add(
            "Launch / config boundary failures retain safe ownership",
            ConfigBoundaryFailuresRetainOwnershipAsync);
        runner.Add(
            "Cleanup lease / pre-apply newer writer is terminally preserved",
            PreApplyNewerWriterIsTerminallyPreservedAsync);
        runner.Add(
            "Cleanup lease / unowned exact token remains passive",
            UnownedExactTokenCleanupIsPassiveAsync);
        runner.Add(
            "Cleanup lease / idle same-path no-token remains passive",
            NoTokenCleanupIsPassiveAsync);
        runner.Add(
            "Cleanup lease / exact-owned retry shuts down before rollback",
            ExactOwnedCleanupLeaseRetryAsync);
        runner.Add(
            "Cleanup lease / Attach geometry waits for exact cleanup and retries",
            AttachCleanupLeaseDefersGeometryAndRetriesAsync);
        runner.Add(
            "Cleanup lease / exited or replaced Attach process is terminal",
            AttachCleanupLeaseReplacementIsTerminalAsync);
        runner.Add(
            "Cleanup lease / changed rollback permission follows ownership",
            ChangedRollbackPermissionFollowsOwnershipAsync);
        runner.Add(
            "Launch / own engine restarted for a new profile, refused if it stays",
            BundledPreflightRefusesWithoutMutationAsync);
        runner.Add(
            "Cleanup / replacement after owned exit remains untouched",
            ReplacementAfterOwnedExitRemainsUntouchedAsync);
        runner.Add(
            "Cleanup / normal Stop restores exact Attach window geometry",
            StopExactOwnedSessionRestoresAttachGeometryAsync);
        runner.Add(
            "Cleanup / same-source unowned owner PID remains untouched",
            SameSourceUnownedOwnerPidRemainsUntouchedAsync);
        runner.Add(
            "Launch / failed-scaling cleanup is fail closed",
            FailedScalingCleanupIsFailClosedAsync);
        runner.Add(
            "Launch / cleanup state, cancellation, and handle disposal",
            ScalingFailureStateAndDisposalAsync);

        return await runner.RunAsync().ConfigureAwait(false);
    }

    private static void Resolution4KPresetMatrix()
    {
        ResolutionPlanner planner = new();
        PixelSize target = new(3840, 2160);

        ResolutionPlan balanced = planner.CreatePlan(target, ResolutionPresetKind.Balanced);
        Assert.Equal(new PixelSize(2560, 1440), balanced.SourceResolution);
        Assert.Equal(new PixelRect(0, 0, 3840, 2160), balanced.DestinationContent);
        Assert.NearlyEqual(1.5, balanced.ActualUiScale);
        Assert.Equal(ScalingFilter.Nis, balanced.Filter);
        Assert.False(balanced.UsesIntegerScale);

        ResolutionPlan comfort = planner.CreatePlan(target, ResolutionPresetKind.Comfort);
        Assert.Equal(new PixelSize(1920, 1080), comfort.SourceResolution);
        Assert.Equal(new PixelRect(0, 0, 3840, 2160), comfort.DestinationContent);
        Assert.NearlyEqual(2.0, comfort.ActualUiScale);
        Assert.True(comfort.UsesIntegerScale);

        ResolutionPlan crisp = planner.CreatePlan(target, ResolutionPresetKind.PixelCrisp);
        Assert.Equal(new PixelSize(1920, 1080), crisp.SourceResolution);
        Assert.Equal(ScalingFilter.NearestNeighbor, crisp.Filter);
        Assert.True(crisp.UsesIntegerScale);

        Assert.Equal(3, planner.Presets.Count);
        Assert.Equal(3, planner.Presets.Select(item => item.Kind).Distinct().Count());
    }

    private static void ResolutionUltrawideFit()
    {
        ResolutionPlanner planner = new();
        PixelSize target = new(3440, 1440);

        ResolutionPlan balanced = planner.CreatePlan(target, ResolutionPresetKind.Balanced);
        Assert.Equal(0, balanced.SourceResolution.Width % 2);
        Assert.Equal(0, balanced.SourceResolution.Height % 2);
        Assert.True(balanced.DestinationContent.Width <= target.Width);
        Assert.True(balanced.DestinationContent.Height <= target.Height);
        Assert.True(balanced.DestinationContent.X >= 0);
        Assert.True(balanced.DestinationContent.Y >= 0);
        Assert.True(Math.Abs(
            balanced.SourceResolution.AspectRatio - target.AspectRatio) < 0.002);
        Assert.True(balanced.ActualUiScale > 1.49 && balanced.ActualUiScale <= 1.5);

        ResolutionPlan crisp = planner.CreatePlan(target, ResolutionPresetKind.PixelCrisp);
        Assert.Equal(new PixelSize(1720, 720), crisp.SourceResolution);
        Assert.Equal(new PixelRect(0, 0, 3440, 1440), crisp.DestinationContent);
        Assert.True(crisp.UsesIntegerScale);
    }

    private static void ResolutionFineScaleMatrix()
    {
        ResolutionPlanner planner = new();
        PixelSize[] targets =
        [
            new PixelSize(3440, 1440),
            new PixelSize(3840, 2160),
        ];

        foreach (PixelSize target in targets)
        {
            PixelSize? previousSource = null;
            for (
                int hundredths = FineUiScale.MinimumHundredths;
                hundredths <= FineUiScale.MaximumHundredths;
                hundredths++)
            {
                FineUiScale scale = new(hundredths);
                double requestedScale = scale.Factor;
                ResolutionPlan plan = planner.CreateCustomPlan(
                    target,
                    requestedScale,
                    ScalingFilter.Fsr);
                Assert.Equal(target, plan.TargetResolution);
                Assert.Equal(ResolutionPresetKind.Custom, plan.PresetKind);
                Assert.Equal(0, plan.SourceResolution.Width % 2);
                Assert.Equal(0, plan.SourceResolution.Height % 2);
                Assert.True(plan.SourceResolution.Width <= target.Width);
                Assert.True(plan.SourceResolution.Height <= target.Height);
                if (hundredths == 100)
                {
                    Assert.Equal(target, plan.SourceResolution);
                    Assert.Equal(target, plan.DestinationContent.Size);
                }
                Assert.True(plan.DestinationContent.X >= 0);
                Assert.True(plan.DestinationContent.Y >= 0);
                Assert.True(
                    plan.DestinationContent.Width <= target.Width
                    && plan.DestinationContent.Height <= target.Height);
                Assert.True(
                    Math.Abs(plan.ActualUiScale - requestedScale) < 0.004,
                    $"{target} at {requestedScale:0.00}x achieved "
                        + $"{plan.ActualUiScale:0.000000}x.");
                if (previousSource is { } previous)
                {
                    Assert.True(
                        plan.SourceResolution.Width <= previous.Width
                            && plan.SourceResolution.Height <= previous.Height,
                        $"{target} source size increased between "
                            + $"{requestedScale - 0.01:0.00}x and "
                            + $"{requestedScale:0.00}x.");
                }

                previousSource = plan.SourceResolution;
            }
        }

        ResolutionPlan subtle4K = planner.CreateCustomPlan(
            new PixelSize(3840, 2160),
            new FineUiScale(110).Factor,
            ScalingFilter.Fsr);
        Assert.Equal(new PixelSize(3492, 1964), subtle4K.SourceResolution);

        ResolutionPlan gentle4K = planner.CreateCustomPlan(
            new PixelSize(3840, 2160),
            1.25,
            ScalingFilter.Fsr);
        Assert.Equal(new PixelSize(3072, 1728), gentle4K.SourceResolution);
        Assert.Equal(new PixelRect(0, 0, 3840, 2160), gentle4K.DestinationContent);

        ResolutionPlan subtleUltrawide = planner.CreateCustomPlan(
            new PixelSize(3440, 1440),
            new FineUiScale(110).Factor,
            ScalingFilter.Fsr);
        Assert.Equal(new PixelSize(3128, 1310), subtleUltrawide.SourceResolution);

        // The stock character-select window requires an 800x600 canvas. On a
        // 720p display, 125% is therefore safely capped just under 120% while
        // retaining the display aspect ratio instead of clipping Enter World.
        ResolutionPlan laptopGentle = planner.CreateCustomPlan(
            new PixelSize(1280, 720),
            1.25,
            ScalingFilter.Fsr);
        Assert.Equal(new PixelSize(1068, 600), laptopGentle.SourceResolution);
        Assert.True(laptopGentle.ActualUiScale > 1.19);
        Assert.True(laptopGentle.ActualUiScale <= 1.20);
    }

    private static void FineUiScaleQuantizationAndBounds()
    {
        Assert.Equal(100, FineUiScale.MinimumHundredths);
        Assert.Equal(200, FineUiScale.MaximumHundredths);

        for (
            int hundredths = FineUiScale.MinimumHundredths;
            hundredths <= FineUiScale.MaximumHundredths;
            hundredths++)
        {
            double sliderValue = hundredths / 100.0;
            FineUiScale direct = new(hundredths);
            FineUiScale quantized = FineUiScale.FromSlider(sliderValue);
            Assert.Equal(hundredths, direct.Hundredths);
            Assert.Equal(hundredths, quantized.Hundredths);
            Assert.NearlyEqual(sliderValue, direct.Factor);
            Assert.Equal($"{sliderValue:0.00}x", direct.ToString());
        }

        Assert.Equal(100, FineUiScale.FromSlider(1.004).Hundredths);
        Assert.Equal(101, FineUiScale.FromSlider(1.005).Hundredths);
        Assert.Equal(110, FineUiScale.FromSlider(1.104).Hundredths);
        Assert.Equal(111, FineUiScale.FromSlider(1.105).Hundredths);
        Assert.Equal(199, FineUiScale.FromSlider(1.994).Hundredths);
        Assert.Equal(200, FineUiScale.FromSlider(1.995).Hundredths);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new FineUiScale(FineUiScale.MinimumHundredths - 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new FineUiScale(FineUiScale.MaximumHundredths + 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FineUiScale.FromSlider(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FineUiScale.FromSlider(double.NegativeInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FineUiScale.FromSlider(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FineUiScale.FromSlider(0.99));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FineUiScale.FromSlider(2.01));
    }

    private static void GenericUiScaleStepAndFilterPolicy()
    {
        for (
            int hundredths = FineUiScale.MinimumHundredths;
            hundredths <= FineUiScale.MaximumHundredths;
            hundredths++)
        {
            FineUiScale current = new(hundredths);
            FineUiScale decreased = GenericUiScalePolicy.Step(current, -1);
            FineUiScale increased = GenericUiScalePolicy.Step(current, 1);
            Assert.Equal(
                Math.Max(FineUiScale.MinimumHundredths, hundredths - 1),
                decreased.Hundredths);
            Assert.Equal(
                Math.Min(FineUiScale.MaximumHundredths, hundredths + 1),
                increased.Hundredths);
            Assert.Equal(
                hundredths == FineUiScale.MaximumHundredths
                    ? ScalingFilter.NearestNeighbor
                    : ScalingFilter.Nis,
                GenericUiScalePolicy.NormalizeFilter(
                    current,
                    ScalingFilter.NearestNeighbor));
            Assert.Equal(
                hundredths == FineUiScale.MinimumHundredths
                    ? ScalingFilter.Nis
                    : ScalingFilter.Fsr,
                GenericUiScalePolicy.NormalizeFilter(
                    current,
                    ScalingFilter.Fsr));
            Assert.Equal(
                hundredths == FineUiScale.MinimumHundredths
                    ? ScalingFilter.Nis
                    : ScalingFilter.Lanczos,
                GenericUiScalePolicy.NormalizeFilter(
                    current,
                    ScalingFilter.Lanczos));
            Assert.Equal(
                ScalingFilter.Nis,
                GenericUiScalePolicy.NormalizeFilter(
                    current,
                    ScalingFilter.Nis));
        }

        Assert.Equal(
            FineUiScale.MinimumHundredths,
            GenericUiScalePolicy.Step(
                new FineUiScale(FineUiScale.MinimumHundredths),
                int.MinValue).Hundredths);
        Assert.Equal(
            FineUiScale.MaximumHundredths,
            GenericUiScalePolicy.Step(
                new FineUiScale(FineUiScale.MaximumHundredths),
                int.MaxValue).Hundredths);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GenericUiScalePolicy.NormalizeFilter(
                new FineUiScale(150),
                (ScalingFilter)int.MaxValue));
    }

    private static void AutomaticAttachScaleAndMonitorPolicy()
    {
        Assert.Equal(
            110,
            AutomaticAttachPolicy.RecommendScale(
                new PixelSize(1920, 1080)).Hundredths);
        Assert.Equal(
            115,
            AutomaticAttachPolicy.RecommendScale(
                new PixelSize(3440, 1440)).Hundredths);
        Assert.Equal(
            125,
            AutomaticAttachPolicy.RecommendScale(
                new PixelSize(3840, 2160)).Hundredths);

        MonitorDescriptor primary = new(
            new nint(1),
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1040),
            IsPrimary: true);
        MonitorDescriptor secondary = new(
            new nint(2),
            new PixelRect(1920, 0, 1920, 1080),
            new PixelRect(1920, 0, 1920, 1040),
            IsPrimary: false);
        MonitorDescriptor selected =
            AutomaticAttachPolicy.ResolveTargetMonitor(
                new PixelRect(1700, 100, 1000, 800),
                [primary, secondary]);
        Assert.Equal(secondary, selected);

        InvalidOperationException evenSplit =
            Assert.Throws<InvalidOperationException>(
                () => AutomaticAttachPolicy.ResolveTargetMonitor(
                    new PixelRect(1720, 100, 400, 800),
                    [primary, secondary]));
        Assert.Contains("split evenly", evenSplit.Message);

        InvalidOperationException underMajority =
            Assert.Throws<InvalidOperationException>(
                () => AutomaticAttachPolicy.ResolveTargetMonitor(
                    new PixelRect(1370, 100, 1000, 800),
                    [primary, secondary]));
        Assert.Contains("Most of", underMajority.Message);
    }

    private static void ResolutionNon4KSafetyFloor()
    {
        ResolutionPlanner planner = new();
        Assert.Equal(800, ResolutionPlanner.MinimumInteractiveWidth);
        Assert.Equal(600, ResolutionPlanner.MinimumInteractiveHeight);

        ResolutionPlan hd = planner.CreatePlan(
            new PixelSize(1920, 1080),
            ResolutionPresetKind.Comfort);
        Assert.Equal(new PixelSize(1068, 600), hd.SourceResolution);
        Assert.True(hd.ActualUiScale > 1.79 && hd.ActualUiScale <= 1.8);

        ResolutionPlan small = planner.CreatePlan(
            new PixelSize(1280, 720),
            ResolutionPresetKind.PixelCrisp);
        Assert.True(
            small.SourceResolution.Width >= 800,
            "The source width must not fall below the documented safe floor.");
        Assert.True(
            small.SourceResolution.Height >= 600,
            "The source height must not fall below the documented safe floor.");

        ResolutionPlan native = planner.CreateCustomPlan(
            new PixelSize(1600, 900),
            1.0,
            ScalingFilter.Lanczos);
        Assert.Equal(new PixelSize(1600, 900), native.SourceResolution);
        Assert.NearlyEqual(1.0, native.ActualUiScale);
    }

    private static void ResolutionNearestNeighborIntegerFallback()
    {
        ResolutionPlanner planner = new();

        // A display that divides exactly by two keeps true exact pixels.
        ResolutionPlan exact = planner.CreateCustomPlan(
            new PixelSize(3840, 2160),
            2.0,
            ScalingFilter.NearestNeighbor);
        Assert.Equal(ScalingFilter.NearestNeighbor, exact.Filter);
        Assert.Equal(new PixelSize(1920, 1080), exact.SourceResolution);
        Assert.True(exact.UsesIntegerScale);

        // A display with no enlarging integer divisor must not silently
        // collapse to a 1:1 nearest pass at an advertised 200%.
        ResolutionPlan oddWidth = planner.CreateCustomPlan(
            new PixelSize(3441, 1440),
            2.0,
            ScalingFilter.NearestNeighbor);
        Assert.Equal(ScalingFilter.Nis, oddWidth.Filter);
        Assert.True(
            oddWidth.SourceResolution.Width < 3441,
            "The fallback must still render at a reduced source size.");
        Assert.True(
            oddWidth.ActualUiScale > 1.5,
            "The fallback must keep most of the requested enlargement.");

        // A small display whose exact half falls below the safety floor also
        // falls back instead of returning an unscaled nearest pass.
        ResolutionPlan floor = planner.CreatePlan(
            new PixelSize(1280, 720),
            ResolutionPresetKind.PixelCrisp);
        Assert.Equal(ScalingFilter.Nis, floor.Filter);
        Assert.True(
            floor.ActualUiScale > 1.0,
            "The fallback plan must remain an enlargement.");
    }

    private static void ResolutionInvalidInputErrors()
    {
        ResolutionPlanner planner = new();
        PixelSize target = new(3840, 2160);

        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new PixelSize(0, 1080));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new PixelSize(1920, -1));
        Assert.Throws<ArgumentException>(
            () => planner.CreatePlan(target, ResolutionPresetKind.Custom));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => planner.CreatePlan(target, (ResolutionPresetKind)999));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => planner.CreateCustomPlan(target, double.NaN, ScalingFilter.Fsr));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => planner.CreateCustomPlan(target, 0.99, ScalingFilter.Fsr));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => planner.CreateCustomPlan(target, 4.01, ScalingFilter.Fsr));
    }

    private static void SpinUiValidatedSourceCatalog()
    {
        SpinUiResolutionPlanner planner = new();
        PixelSize[] expected =
        [
            new PixelSize(1920, 1080),
            new PixelSize(2048, 1080),
            new PixelSize(2560, 1080),
            new PixelSize(2560, 1440),
            new PixelSize(3440, 1440),
            new PixelSize(3840, 1600),
            new PixelSize(3840, 2160),
        ];

        Assert.Equal(expected.Length, planner.ValidatedSourceResolutions.Count);
        Assert.Equal(
            expected.Length,
            planner.ValidatedSourceResolutions.Distinct().Count());
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.Equal(
                expected[index],
                planner.ValidatedSourceResolutions[index]);
            Assert.True(planner.IsValidatedSource(expected[index]));
        }

        Assert.False(planner.IsValidatedSource(new PixelSize(1600, 900)));
        Assert.NearlyEqual(0.02, SpinUiResolutionPlanner.MaximumRelativeAspectError);
        Assert.NearlyEqual(1.5, SpinUiResolutionPlanner.PreferredUiScale);
    }

    private static void SpinUiRequiredMonitorMatrix()
    {
        SpinUiResolutionPlanner planner = new();

        IReadOnlyList<SpinUiResolutionPlan> ultrawide1440 =
            planner.GetRecommendedPlans(new PixelSize(3440, 1440));
        Assert.Equal(1, ultrawide1440.Count);
        SpinUiResolutionPlan ultrawidePlan = ultrawide1440[0];
        Assert.Equal(new PixelSize(2560, 1080), ultrawidePlan.SourceResolution);
        Assert.Equal(new PixelRect(13, 0, 3413, 1440), ultrawidePlan.DestinationContent);
        Assert.NearlyEqual(1.333203125, ultrawidePlan.ActualUiScale);
        Assert.Equal(13, ultrawidePlan.LeftMarginPixels);
        Assert.Equal(14, ultrawidePlan.RightMarginPixels);
        Assert.True(ultrawidePlan.HasPillarboxing);
        Assert.False(ultrawidePlan.HasLetterboxing);

        IReadOnlyList<SpinUiResolutionPlan> fourK =
            planner.GetRecommendedPlans(new PixelSize(3840, 2160));
        Assert.Equal(2, fourK.Count);
        Assert.Equal(new PixelSize(2560, 1440), fourK[0].SourceResolution);
        Assert.NearlyEqual(1.5, fourK[0].ActualUiScale);
        Assert.False(fourK[0].UsesIntegerScale);
        Assert.Equal(new PixelSize(1920, 1080), fourK[1].SourceResolution);
        Assert.NearlyEqual(2.0, fourK[1].ActualUiScale);
        Assert.True(fourK[1].UsesIntegerScale);

        IReadOnlyList<SpinUiResolutionPlan> ultrawide1600 =
            planner.GetRecommendedPlans(new PixelSize(3840, 1600));
        Assert.Equal(2, ultrawide1600.Count);
        Assert.Equal(
            new PixelSize(2560, 1080),
            ultrawide1600[0].SourceResolution);
        Assert.NearlyEqual(1.48125, ultrawide1600[0].ActualUiScale);
        Assert.Equal(
            new PixelSize(3440, 1440),
            ultrawide1600[1].SourceResolution);
        Assert.NearlyEqual(
            1.111046511627907,
            ultrawide1600[1].ActualUiScale);

        IReadOnlyList<SpinUiResolutionPlan> qhd =
            planner.GetRecommendedPlans(new PixelSize(2560, 1440));
        Assert.Equal(1, qhd.Count);
        Assert.Equal(new PixelSize(1920, 1080), qhd[0].SourceResolution);
        Assert.NearlyEqual(4.0 / 3.0, qhd[0].ActualUiScale);

        foreach (SpinUiResolutionPlan plan in ultrawide1440
            .Concat(fourK)
            .Concat(ultrawide1600)
            .Concat(qhd))
        {
            Assert.True(plan.SourceResolution.Width < plan.TargetResolution.Width);
            Assert.True(plan.SourceResolution.Height < plan.TargetResolution.Height);
            Assert.True(plan.ActualUiScale > 1.0);
            Assert.True(
                plan.RelativeAspectError
                <= SpinUiResolutionPlanner.MaximumRelativeAspectError);
            Assert.True(plan.RequiresCharacterLayoutConfirmation);
        }
    }

    private static void SpinUiExactSourceMetadataAndFilters()
    {
        SpinUiResolutionPlanner planner = new();
        PixelSize fourK = new(3840, 2160);

        SpinUiResolutionPlan comfort = planner.CreateExactSourcePlan(
            fourK,
            new PixelSize(1920, 1080));
        Assert.Equal(ResolutionPresetKind.Custom, comfort.ResolutionPlan.PresetKind);
        Assert.Equal(ScalingFilter.Nis, comfort.Filter);
        Assert.Equal(new PixelRect(0, 0, 3840, 2160), comfort.DestinationContent);
        Assert.NearlyEqual(0, comfort.RelativeAspectError);
        Assert.NearlyEqual(2, comfort.ActualUiScale);
        Assert.Equal(0, comfort.LeftMarginPixels);
        Assert.Equal(0, comfort.RightMarginPixels);
        Assert.Equal(0, comfort.TopMarginPixels);
        Assert.Equal(0, comfort.BottomMarginPixels);

        SpinUiResolutionPlan nearest = planner.CreateExactSourcePlan(
            fourK,
            new PixelSize(1920, 1080),
            ScalingFilter.NearestNeighbor);
        Assert.True(nearest.UsesIntegerScale);
        Assert.Equal(ScalingFilter.NearestNeighbor, nearest.Filter);

        SpinUiResolutionPlan lanczos = planner.CreateExactSourcePlan(
            fourK,
            new PixelSize(2560, 1440),
            ScalingFilter.Lanczos);
        Assert.Equal(ScalingFilter.Lanczos, lanczos.Filter);
        Assert.NearlyEqual(1.5, lanczos.ActualUiScale);

        IReadOnlyList<SpinUiResolutionPlan> nearestRecommendations =
            planner.GetRecommendedPlans(fourK, ScalingFilter.NearestNeighbor);
        Assert.Equal(1, nearestRecommendations.Count);
        Assert.Equal(
            new PixelSize(1920, 1080),
            nearestRecommendations[0].SourceResolution);
    }

    private static void SpinUiBoundedRejectionPaths()
    {
        SpinUiResolutionPlanner planner = new();
        PixelSize fourK = new(3840, 2160);

        Assert.Throws<ArgumentException>(
            () => planner.CreateExactSourcePlan(
                fourK,
                new PixelSize(1600, 900)));
        Assert.Throws<ArgumentException>(
            () => planner.CreateExactSourcePlan(
                fourK,
                fourK));
        Assert.Throws<ArgumentException>(
            () => planner.CreateExactSourcePlan(
                new PixelSize(3840, 1800),
                new PixelSize(3840, 1600)));
        Assert.Throws<ArgumentException>(
            () => planner.CreateExactSourcePlan(
                new PixelSize(3440, 1440),
                new PixelSize(1920, 1080)));
        Assert.Throws<ArgumentException>(
            () => planner.CreateExactSourcePlan(
                fourK,
                new PixelSize(2560, 1440),
                ScalingFilter.NearestNeighbor));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => planner.CreateExactSourcePlan(
                fourK,
                new PixelSize(1920, 1080),
                (ScalingFilter)999));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => planner.GetRecommendedPlans(fourK, (ScalingFilter)999));

        Assert.Empty(
            planner.GetRecommendedPlans(new PixelSize(1920, 1080)));
        Assert.Empty(
            planner.GetRecommendedPlans(new PixelSize(2560, 1080)));
        Assert.Empty(
            planner.GetRecommendedPlans(new PixelSize(1921, 1081)));
        Assert.Throws<ArgumentException>(
            () => planner.CreateExactSourcePlan(
                new PixelSize(1921, 1081),
                new PixelSize(1920, 1080)));

        // The generic planner intentionally remains available for non-SpinUI
        // calculated sizes.
        ResolutionPlan generic = new ResolutionPlanner().CreateCustomPlan(
            new PixelSize(3440, 1440),
            desiredUiScale: 1.5,
            ScalingFilter.Fsr);
        Assert.False(planner.IsValidatedSource(generic.SourceResolution));
    }

    private static void IniCaseDuplicatesAndComments()
    {
        const string input =
            "; untouched header\r\n"
            + "[VideoMode]\r\n"
            + "Width = 800 ; first copy\r\n"
            + "WIDTH=1024 # effective copy\r\n"
            + "Name=Lavastorm\r\n"
            + "[Other]\r\n"
            + "Keep = exactly this ; comment\r\n";

        IniDocument document = IniDocument.Parse(input);
        Assert.Equal("1024", document.GetValue("videomode", "width"));

        document.SetValue("VIDEOMODE", "wIdTh", "1920");
        string serialized = document.Serialize();
        Assert.Contains("Width = 1920 ; first copy", serialized);
        Assert.Contains("WIDTH=1920 # effective copy", serialized);
        Assert.Contains("Keep = exactly this ; comment", serialized);
        Assert.Equal("1920", document.GetValue("VideoMode", "WIDTH"));

        Assert.Equal(2, document.RemoveKey("videoMODE", "WIDTH"));
        Assert.Null(document.GetValue("VideoMode", "Width"));
        Assert.Equal(0, document.RemoveKey("VideoMode", "missing"));
        Assert.DoesNotContain("first copy", document.Serialize());
        Assert.DoesNotContain("effective copy", document.Serialize());
    }

    private static void IniMixedNewlinesAndInsertion()
    {
        const string input = "[One]\r\nA=1\r\n[Two]\nB=2";
        IniDocument document = IniDocument.Parse(input);

        document.SetValue("One", "Added", "yes");
        document.SetValue("Two", "C", "3");
        document.SetValue("Three", "D", "4");

        const string expected =
            "[One]\r\n"
            + "A=1\r\n"
            + "Added=yes\r\n"
            + "[Two]\n"
            + "B=2\r\n"
            + "C=3\r\n"
            + "\r\n"
            + "[Three]\r\n"
            + "D=4";
        Assert.Equal(expected, document.Serialize());

        IniDocument root = IniDocument.Parse("Loose=old");
        root.SetValue(string.Empty, "Loose", "new");
        root.SetValue(string.Empty, "Added", "value");
        Assert.Equal($"Loose=new{Environment.NewLine}Added=value", root.Serialize());
    }

    private static async Task IniBomAndLegacyBytePreservationAsync()
    {
        await using TempDirectory temp = new();
        string utf8Path = Path.Combine(temp.Path, "utf8-bom.ini");
        byte[] utf8Payload = Encoding.UTF8.GetBytes("[VideoMode]\r\nWidth=800\r\n");
        byte[] utf8Bom = [0xEF, 0xBB, 0xBF, .. utf8Payload];
        await File.WriteAllBytesAsync(utf8Path, utf8Bom).ConfigureAwait(false);

        IniDocument utf8 = await IniDocument.LoadAsync(utf8Path).ConfigureAwait(false);
        utf8.SetValue("VideoMode", "Width", "1920");
        await utf8.SaveAtomicAsync(utf8Path).ConfigureAwait(false);

        byte[] savedUtf8 = await File.ReadAllBytesAsync(utf8Path).ConfigureAwait(false);
        Assert.True(savedUtf8.AsSpan().StartsWith(Utf8Preamble));
        Assert.Equal(1, CountPreamble(savedUtf8, Utf8Preamble));
        Assert.Equal(
            "[VideoMode]\r\nWidth=1920\r\n",
            Encoding.UTF8.GetString(savedUtf8, 3, savedUtf8.Length - 3));

        string latin1Path = Path.Combine(temp.Path, "latin1.ini");
        byte[] latin1Bytes =
        [
            .. Encoding.ASCII.GetBytes("[Names]\r\nLabel=caf"),
            0xE9,
            .. Encoding.ASCII.GetBytes("\r\n")
        ];
        await File.WriteAllBytesAsync(latin1Path, latin1Bytes).ConfigureAwait(false);
        IniDocument latin1 = await IniDocument.LoadAsync(latin1Path).ConfigureAwait(false);
        latin1.SetValue("Names", "Added", "safe");
        await latin1.SaveAtomicAsync(latin1Path).ConfigureAwait(false);
        byte[] savedLatin1 = await File.ReadAllBytesAsync(latin1Path).ConfigureAwait(false);
        Assert.True(savedLatin1.Contains((byte)0xE9));
        Assert.Equal("café", latin1.GetValue("Names", "Label"));
    }

    private static void IniInvalidInputErrors()
    {
        IniDocument document = IniDocument.Parse(string.Empty);

        Assert.Throws<ArgumentNullException>(() => IniDocument.Parse(null!));
        Assert.Throws<ArgumentException>(() => document.SetValue("Bad[Section", "Key", "x"));
        Assert.Throws<ArgumentException>(() => document.SetValue("Good", "Bad=Key", "x"));
        Assert.Throws<ArgumentException>(() => document.SetValue("Good", "Key", "line1\nline2"));
        Assert.Throws<ArgumentException>(() => document.GetValue("Good", " "));
    }

    private static void UiLayoutGenericGeometryPreservesPersonalData()
    {
        byte[] native =
        [
            .. Encoding.ASCII.GetBytes(
                "[Main]\r\n"
                    + "UISkin=someone_elses_custom_ui\r\n"
                    + "[MainChat]\r\n"
                    + "XRef=left\r\n"
                    + "YRef=bottom\r\n"
                    + "XPos=16.249998%\r\n"
                    + "YPos=0.555556%\r\n"
                    + "Width=552\r\n"
                    + "Height=212\r\n"
                    + "ChatWindow0_Width=552\r\n"
                    + "Label=caf"),
            0xE9,
            .. Encoding.ASCII.GetBytes("\r\n[Second]\nWidth=706\nHeight=209\n"),
        ];

        UiLayoutTransformResult scaled = UiLayoutTransformer.Transform(
            native,
            new PixelSize(3440, 1440),
            new PixelSize(2866, 1200));
        string text = Encoding.Latin1.GetString(scaled.Content);
        Assert.Equal(2, scaled.ScaledWidthCount);
        Assert.Equal(2, scaled.ScaledHeightCount);
        Assert.Contains("UISkin=someone_elses_custom_ui\r\n", text);
        Assert.Contains("XPos=16.249998%\r\n", text);
        Assert.Contains("YPos=0.555556%\r\n", text);
        Assert.Contains("Width=460\r\n", text);
        Assert.Contains("Height=177\r\n", text);
        Assert.Contains("ChatWindow0_Width=552\r\n", text);
        Assert.Contains("Width=588\n", text);
        Assert.Contains("Height=174\n", text);
        Assert.True(scaled.Content.Contains((byte)0xE9));

        UiLayoutTransformResult roundTrip = UiLayoutTransformer.Transform(
            scaled.Content,
            new PixelSize(2866, 1200),
            new PixelSize(3440, 1440));
        Assert.SequenceEqual(native, roundTrip.Content);
    }

    private static async Task UiLayoutAutomaticProfileLifecycleAsync()
    {
        await using TempDirectory temp = new();
        string eqDirectory = Path.Combine(temp.Path, "Different EverQuest Install");
        string stateRoot = Path.Combine(temp.Path, "controller state");
        Directory.CreateDirectory(eqDirectory);

        string eqClientPath = Path.Combine(eqDirectory, "eqclient.ini");
        const string originalEqClient =
            "; user formatting stays\r\n"
            + "[Defaults]\r\n"
            + "AllowResize=1\r\n"
            + "Maximized=1\r\n"
            + "WindowedModeXOffset=77\r\n"
            + "WindowedModeYOffset=88\r\n"
            + "UIScale=2\r\n"
            + "Unrelated=preserve\r\n"
            + "[VideoMode]\r\n"
            + "Width=3440\r\n"
            + "Height=1440\r\n"
            + "WindowedWidth=1720\r\n"
            + "WindowedHeight=720\r\n"
            + "Fullscreen=1\r\n";
        await File.WriteAllTextAsync(eqClientPath, originalEqClient).ConfigureAwait(false);

        string layoutPath = Path.Combine(eqDirectory, "UI_Old_mischief_LO7.ini");
        byte[] originalLayout = Encoding.Latin1.GetBytes(
            "[Main]\r\n"
                + "UISkin=totally_custom_skin\r\n"
                + "[MainChat]\r\n"
                + "XPos=16.249998%\r\n"
                + "YPos=0.555556%\r\n"
                + "Width=552\r\n"
                + "Height=212\r\n"
                + "UserLabel=café\r\n");
        await File.WriteAllBytesAsync(layoutPath, originalLayout).ConfigureAwait(false);

        string backupLayoutPath = Path.Combine(
            eqDirectory,
            "UI_Old_mischief_backup_LO7.ini");
        byte[] backupLayout = Encoding.ASCII.GetBytes(
            "[MainChat]\r\nWidth=999\r\nHeight=999\r\n");
        await File.WriteAllBytesAsync(backupLayoutPath, backupLayout).ConfigureAwait(false);

        string characterPath = Path.Combine(eqDirectory, "Old_mischief.ini");
        byte[] characterSettings = Encoding.ASCII.GetBytes(
            "[HotButtons]\r\nPage1Button1=/assist group\r\n[Socials]\r\nPage1Name1=Incoming\r\n");
        await File.WriteAllBytesAsync(characterPath, characterSettings).ConfigureAwait(false);

        UiLayoutProfileService service = new();
        UiLayoutPrepareResult prepared = await service.PrepareAsync(
            new UiLayoutPrepareRequest
            {
                EqDirectory = eqDirectory,
                StateRoot = stateRoot,
                NativeResolution = new PixelSize(3440, 1440),
                ScaledResolution = new PixelSize(2866, 1200),
            }).ConfigureAwait(false);

        Assert.Equal(1, prepared.LayoutCount);
        Assert.Equal(1, prepared.GeneratedProfileCount);
        Assert.Equal(0, prepared.ReusedProfileCount);
        Assert.Equal(UiLayoutSessionStatus.Active, prepared.State.Status);
        string scaledLayout = Encoding.Latin1.GetString(
            await File.ReadAllBytesAsync(layoutPath).ConfigureAwait(false));
        Assert.Contains("UISkin=totally_custom_skin\r\n", scaledLayout);
        Assert.Contains("XPos=16.249998%\r\n", scaledLayout);
        Assert.Contains("Width=460\r\n", scaledLayout);
        Assert.Contains("Height=177\r\n", scaledLayout);
        Assert.Contains("UserLabel=café\r\n", scaledLayout);
        Assert.SequenceEqual(
            backupLayout,
            await File.ReadAllBytesAsync(backupLayoutPath).ConfigureAwait(false));
        Assert.SequenceEqual(
            characterSettings,
            await File.ReadAllBytesAsync(characterPath).ConfigureAwait(false));

        IniDocument preparedEqClient = await IniDocument.LoadAsync(eqClientPath)
            .ConfigureAwait(false);
        Assert.Equal("2866", preparedEqClient.GetValue("VideoMode", "Width"));
        Assert.Equal("1200", preparedEqClient.GetValue("VideoMode", "Height"));
        Assert.Equal("0", preparedEqClient.GetValue("VideoMode", "Fullscreen"));
        Assert.Equal("0", preparedEqClient.GetValue("Defaults", "UIScale"));
        Assert.Equal("preserve", preparedEqClient.GetValue("Defaults", "Unrelated"));

        string playerAdjustedScaledLayout = scaledLayout
            .Replace("Width=460", "Width=500", StringComparison.Ordinal)
            + "[ChatManager]\r\nChannelMap=keep-this-change\r\n";
        await File.WriteAllBytesAsync(
            layoutPath,
            Encoding.Latin1.GetBytes(playerAdjustedScaledLayout)).ConfigureAwait(false);

        string newLayoutPath = Path.Combine(eqDirectory, "UI_Bryn_bertoxxulous.ini");
        await File.WriteAllTextAsync(
            newLayoutPath,
            "[Main]\r\nUISkin=another_ui\r\n[Inventory]\r\nXPos=40.000000%\r\nWidth=300\r\nHeight=100\r\n")
            .ConfigureAwait(false);

        preparedEqClient.SetValue("Player", "SavedDuringGame", "yes");
        await preparedEqClient.SaveAtomicAsync(eqClientPath).ConfigureAwait(false);

        UiLayoutCompleteResult completed = await service.CompleteAsync(prepared.State)
            .ConfigureAwait(false);
        Assert.Equal(UiLayoutSessionStatus.Completed, completed.State.Status);
        Assert.Equal(2, completed.CapturedProfileCount);
        Assert.Equal(2, completed.ConvertedBackCount);
        Assert.Equal(1, completed.NewLayoutCount);

        byte[] nativeAfterComplete = await File.ReadAllBytesAsync(layoutPath)
            .ConfigureAwait(false);
        string completedLayout = Encoding.Latin1.GetString(nativeAfterComplete);
        Assert.Contains("Width=600\r\n", completedLayout);
        Assert.Contains("Height=212\r\n", completedLayout);
        Assert.Contains("UISkin=totally_custom_skin\r\n", completedLayout);
        Assert.Contains("ChannelMap=keep-this-change\r\n", completedLayout);
        string completedNewLayout = await File.ReadAllTextAsync(newLayoutPath)
            .ConfigureAwait(false);
        Assert.Contains("UISkin=another_ui\r\n", completedNewLayout);
        Assert.Contains("XPos=40.000000%\r\n", completedNewLayout);
        Assert.Contains("Width=360\r\n", completedNewLayout);
        Assert.Contains("Height=120\r\n", completedNewLayout);

        IniDocument restoredEqClient = await IniDocument.LoadAsync(eqClientPath)
            .ConfigureAwait(false);
        Assert.Equal("3440", restoredEqClient.GetValue("VideoMode", "Width"));
        Assert.Equal("1440", restoredEqClient.GetValue("VideoMode", "Height"));
        Assert.Equal("1720", restoredEqClient.GetValue("VideoMode", "WindowedWidth"));
        Assert.Equal("720", restoredEqClient.GetValue("VideoMode", "WindowedHeight"));
        Assert.Equal("1", restoredEqClient.GetValue("VideoMode", "Fullscreen"));
        Assert.Equal("2", restoredEqClient.GetValue("Defaults", "UIScale"));
        Assert.Equal("yes", restoredEqClient.GetValue("Player", "SavedDuringGame"));
        Assert.Null(await service.LoadActiveAsync(eqDirectory, stateRoot).ConfigureAwait(false));
        Assert.SequenceEqual(
            backupLayout,
            await File.ReadAllBytesAsync(backupLayoutPath).ConfigureAwait(false));
        Assert.SequenceEqual(
            characterSettings,
            await File.ReadAllBytesAsync(characterPath).ConfigureAwait(false));
        Assert.True(completed.State.Entries.All(
            entry => File.Exists(entry.ScaledProfilePath)
                && File.Exists(entry.ScaledProfilePath + ".sha256")));

        UiLayoutPrepareResult reused = await service.PrepareAsync(
            new UiLayoutPrepareRequest
            {
                EqDirectory = eqDirectory,
                StateRoot = stateRoot,
                NativeResolution = new PixelSize(3440, 1440),
                ScaledResolution = new PixelSize(2866, 1200),
            }).ConfigureAwait(false);
        Assert.Equal(0, reused.GeneratedProfileCount);
        Assert.Equal(2, reused.ReusedProfileCount);
        Assert.Contains(
            "Width=500\r\n",
            Encoding.Latin1.GetString(
                await File.ReadAllBytesAsync(layoutPath).ConfigureAwait(false)));
        await service.RollbackAsync(reused.State).ConfigureAwait(false);
        Assert.SequenceEqual(
            nativeAfterComplete,
            await File.ReadAllBytesAsync(layoutPath).ConfigureAwait(false));
        Assert.SequenceEqual(
            characterSettings,
            await File.ReadAllBytesAsync(characterPath).ConfigureAwait(false));
    }

    private static async Task UiLayoutRollbackAndInterruptedRestoreRecoveryAsync()
    {
        await using TempDirectory temp = new();
        string eqDirectory = Path.Combine(temp.Path, "EQ");
        string stateRoot = Path.Combine(temp.Path, "State");
        Directory.CreateDirectory(eqDirectory);
        string eqClientPath = Path.Combine(eqDirectory, "eqclient.ini");
        byte[] originalEqClient = Encoding.ASCII.GetBytes(
            "[Defaults]\r\nUIScale=3\r\n[VideoMode]\r\nWidth=3440\r\nHeight=1440\r\n"
                + "WindowedWidth=1600\r\nWindowedHeight=900\r\nFullscreen=1\r\n");
        await File.WriteAllBytesAsync(eqClientPath, originalEqClient).ConfigureAwait(false);
        string layoutPath = Path.Combine(eqDirectory, "UI_One_firiona.ini");
        byte[] originalLayout = Encoding.ASCII.GetBytes(
            "[Main]\r\nUISkin=default\r\n[Chat]\r\nXPos=10.000000%\r\nWidth=552\r\nHeight=212\r\n");
        await File.WriteAllBytesAsync(layoutPath, originalLayout).ConfigureAwait(false);

        UiLayoutProfileService service = new();
        UiLayoutPrepareRequest request = new()
        {
            EqDirectory = eqDirectory,
            StateRoot = stateRoot,
            NativeResolution = new PixelSize(3440, 1440),
            ScaledResolution = new PixelSize(2866, 1200),
        };
        UiLayoutPrepareResult first = await service.PrepareAsync(request)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(layoutPath, "unexpected scaled edit")
            .ConfigureAwait(false);
        UiLayoutSessionState rolledBack = await service.RollbackAsync(first.State)
            .ConfigureAwait(false);
        Assert.Equal(UiLayoutSessionStatus.RolledBack, rolledBack.Status);
        Assert.SequenceEqual(
            originalLayout,
            await File.ReadAllBytesAsync(layoutPath).ConfigureAwait(false));
        Assert.SequenceEqual(
            originalEqClient,
            await File.ReadAllBytesAsync(eqClientPath).ConfigureAwait(false));

        UiLayoutPrepareResult interrupted = await service.PrepareAsync(request)
            .ConfigureAwait(false);
        JsonObject journal = JsonNode.Parse(
            await File.ReadAllTextAsync(interrupted.State.JournalPath).ConfigureAwait(false))!
            .AsObject();
        journal["status"] = "restoring";
        await File.WriteAllTextAsync(
            interrupted.State.JournalPath,
            journal.ToJsonString(new JsonSerializerOptions { WriteIndented = true }))
            .ConfigureAwait(false);
        await File.WriteAllBytesAsync(layoutPath, originalLayout).ConfigureAwait(false);

        UiLayoutCompleteResult recovered = await service.CompleteAsync(interrupted.State)
            .ConfigureAwait(false);
        Assert.Equal(UiLayoutSessionStatus.Completed, recovered.State.Status);
        Assert.SequenceEqual(
            originalLayout,
            await File.ReadAllBytesAsync(layoutPath).ConfigureAwait(false));
        Assert.SequenceEqual(
            originalEqClient,
            await File.ReadAllBytesAsync(eqClientPath).ConfigureAwait(false));
        Assert.Null(await service.LoadActiveAsync(eqDirectory, stateRoot).ConfigureAwait(false));
    }

    private static async Task UiLayoutStaleSessionRecoveredBeforeRelaunchAsync()
    {
        await using TempDirectory temp = new();
        string eqDirectory = Path.Combine(temp.Path, "User Selected EQ");
        string stateRoot = Path.Combine(temp.Path, "Local App State");
        Directory.CreateDirectory(eqDirectory);
        string eqClientPath = Path.Combine(eqDirectory, "eqclient.ini");
        byte[] originalEqClient = Encoding.ASCII.GetBytes(
            "[Defaults]\r\nUIScale=1\r\n[VideoMode]\r\nWidth=3440\r\n"
                + "Height=1440\r\nWindowedWidth=1720\r\n"
                + "WindowedHeight=720\r\nFullscreen=1\r\n");
        await File.WriteAllBytesAsync(eqClientPath, originalEqClient)
            .ConfigureAwait(false);
        string layoutPath = Path.Combine(eqDirectory, "UI_Spin_qeynos_LO1.ini");
        byte[] originalLayout = Encoding.ASCII.GetBytes(
            "[Main]\r\nUISkin=custom\r\n[Chat]\r\nXPos=10.000000%\r\n"
                + "Width=550\r\nHeight=220\r\n");
        await File.WriteAllBytesAsync(layoutPath, originalLayout)
            .ConfigureAwait(false);

        UiLayoutProfileService service = new();
        UiLayoutPrepareRequest request = new()
        {
            EqDirectory = eqDirectory,
            StateRoot = stateRoot,
            NativeResolution = new PixelSize(3440, 1440),
            ScaledResolution = new PixelSize(2752, 1152),
        };
        UiLayoutPrepareResult interrupted = await service.PrepareAsync(request)
            .ConfigureAwait(false);
        InvalidOperationException blocked =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.PrepareAsync(request)).ConfigureAwait(false);
        Assert.Contains("still active", blocked.Message);

        // This mirrors the application pre-launch recovery after it has
        // positively verified that no eqgame process is running.
        UiLayoutSessionState? discovered = await service.LoadActiveAsync(
            eqDirectory,
            stateRoot).ConfigureAwait(false);
        Assert.Equal(interrupted.State.SessionId, discovered?.SessionId);
        _ = await service.CompleteAsync(discovered!).ConfigureAwait(false);
        Assert.Null(await service.LoadActiveAsync(eqDirectory, stateRoot)
            .ConfigureAwait(false));
        Assert.SequenceEqual(
            originalLayout,
            await File.ReadAllBytesAsync(layoutPath).ConfigureAwait(false));
        Assert.SequenceEqual(
            originalEqClient,
            await File.ReadAllBytesAsync(eqClientPath).ConfigureAwait(false));

        UiLayoutPrepareResult next = await service.PrepareAsync(request)
            .ConfigureAwait(false);
        Assert.Equal(UiLayoutSessionStatus.Active, next.State.Status);
        await service.RollbackAsync(next.State).ConfigureAwait(false);
    }

    private static async Task PreferencesAtomicRoundTripAsync()
    {
        await using TempDirectory temp = new();
        string preferencesPath = Path.Combine(
            temp.Path,
            "LocalAppData",
            "SpinFOURKAYYY",
            "preferences.json");
        using UserPreferencesStore store = new(preferencesPath);
        UserPreferences initial = new()
        {
            LegendsDirectory = Path.Combine(temp.Path, "My Legends Install"),
            SpinTextureExecutablePath = Path.Combine(
                temp.Path,
                "Tools",
                "SpinTexture.exe"),
            TargetDisplayBounds = new PixelRect(-3840, 0, 3840, 2160),
            UiScaleHundredths = 137,
            ScalingFilter = ScalingFilter.Fsr,
            AntiAliasing = AntiAliasingMode.Smaa,
            ClarityPercent = 15,
            MaintainTopmostOverlays = false,
            CloseConflictingMagpieAutomatically = true,
            UiCompatibilityMode = FourKayUiCompatibilityMode.SpinUiStrict,
            SpinUiPresetIndex = 1,
        };

        UserPreferencesLoadResult missing = await store.LoadAsync()
            .ConfigureAwait(false);
        Assert.False(missing.WasLoaded);
        Assert.Null(missing.Warning);
        Assert.Equal(125, missing.Preferences.UiScaleHundredths);

        await store.SaveAsync(initial).ConfigureAwait(false);
        UserPreferencesLoadResult loaded = await store.LoadAsync()
            .ConfigureAwait(false);
        Assert.True(loaded.WasLoaded);
        Assert.Null(loaded.Warning);
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(initial.LegendsDirectory!)),
            loaded.Preferences.LegendsDirectory);
        Assert.Equal(
            Path.GetFullPath(initial.SpinTextureExecutablePath!),
            loaded.Preferences.SpinTextureExecutablePath);
        Assert.Equal(initial.TargetDisplayBounds, loaded.Preferences.TargetDisplayBounds);
        Assert.Equal(137, loaded.Preferences.UiScaleHundredths);
        Assert.Equal(ScalingFilter.Fsr, loaded.Preferences.ScalingFilter);
        Assert.Equal(AntiAliasingMode.Smaa, loaded.Preferences.AntiAliasing);
        Assert.Equal(15, loaded.Preferences.ClarityPercent);
        Assert.False(loaded.Preferences.MaintainTopmostOverlays);
        Assert.True(loaded.Preferences.CloseConflictingMagpieAutomatically);
        // Closing a Magpie the user started must never be the silent default.
        Assert.False(missing.Preferences.CloseConflictingMagpieAutomatically);
        Assert.False(UserPreferences.Default.CloseConflictingMagpieAutomatically);
        Assert.Equal(
            FourKayUiCompatibilityMode.SpinUiStrict,
            loaded.Preferences.UiCompatibilityMode);
        Assert.Equal(1, loaded.Preferences.SpinUiPresetIndex);

        await store.SaveAsync(initial with
        {
            UiScaleHundredths = 110,
            ScalingFilter = ScalingFilter.Lanczos,
        }).ConfigureAwait(false);
        UserPreferencesLoadResult replaced = await store.LoadAsync()
            .ConfigureAwait(false);
        Assert.Equal(110, replaced.Preferences.UiScaleHundredths);
        Assert.Equal(ScalingFilter.Lanczos, replaced.Preferences.ScalingFilter);
        Assert.Empty(Directory.EnumerateFiles(
            Path.GetDirectoryName(preferencesPath)!,
            "*.tmp",
            SearchOption.TopDirectoryOnly));
    }

    private static async Task PreferencesCorruptionAndValidationAsync()
    {
        await using TempDirectory temp = new();
        string preferencesPath = Path.Combine(temp.Path, "preferences.json");
        using UserPreferencesStore store = new(preferencesPath);

        await File.WriteAllTextAsync(preferencesPath, "{not valid json")
            .ConfigureAwait(false);
        UserPreferencesLoadResult corrupt = await store.LoadAsync()
            .ConfigureAwait(false);
        Assert.False(corrupt.WasLoaded);
        Assert.True(!string.IsNullOrWhiteSpace(corrupt.Warning));
        Assert.Equal(125, corrupt.Preferences.UiScaleHundredths);
        Assert.Equal(ScalingFilter.Nis, corrupt.Preferences.ScalingFilter);

        await File.WriteAllTextAsync(
            preferencesPath,
            "{\"formatVersion\":99}").ConfigureAwait(false);
        UserPreferencesLoadResult future = await store.LoadAsync()
            .ConfigureAwait(false);
        Assert.False(future.WasLoaded);
        Assert.Contains("not supported", future.Warning!);

        await store.SaveAsync(new UserPreferences()).ConfigureAwait(false);
        byte[] valid = await File.ReadAllBytesAsync(preferencesPath)
            .ConfigureAwait(false);
        await Assert.ThrowsAnyAsync<ArgumentOutOfRangeException>(
            () => store.SaveAsync(new UserPreferences
            {
                UiScaleHundredths = 99,
            })).ConfigureAwait(false);
        Assert.SequenceEqual(
            valid,
            await File.ReadAllBytesAsync(preferencesPath).ConfigureAwait(false));
        await Assert.ThrowsAnyAsync<InvalidDataException>(
            () => store.SaveAsync(new UserPreferences
            {
                UiCompatibilityMode = FourKayUiCompatibilityMode.UnspecifiedLegacy,
            })).ConfigureAwait(false);
    }

    private static async Task UpdateDiscoveryAndVerifiedStagingAsync()
    {
        await using TempDirectory temp = new();
        const string version = "1.0.6";
        byte[] package = CreateUpdatePackage(
            version,
            new Dictionary<string, byte[]>
            {
                ["SpinFOURKAYYY.exe"] = Encoding.ASCII.GetBytes("new app"),
                ["Engine/Magpie/test.dat"] = Encoding.ASCII.GetBytes("engine"),
                ["README.md"] = Encoding.UTF8.GetBytes("update fixture"),
            });
        string packageHash = Convert.ToHexString(SHA256.HashData(package));
        string packageName = $"SpinFOURKAYYY-{version}-win-x64.zip";
        byte[] sidecar = Encoding.ASCII.GetBytes(
            $"{packageHash.ToLowerInvariant()} *{packageName}\n");
        using RouteHttpMessageHandler handler = CreateUpdateRoutes(
            version,
            package,
            sidecar,
            packageHash);
        using HttpClient httpClient = new(handler);
        using GitHubReleaseUpdateService service = new(
            Path.Combine(temp.Path, "updates"),
            httpClient);

        ReleaseUpdateCheck check = await service.CheckAsync(new Version(1, 0, 5))
            .ConfigureAwait(false);
        Assert.True(check.IsUpdateAvailable);
        Assert.Equal(new Version(1, 0, 6), check.LatestRelease.Version);
        Assert.Equal(packageName, check.LatestRelease.PackageFileName);
        List<int> progressValues = [];
        PreparedReleaseUpdate prepared = await service.PrepareAsync(
            check.LatestRelease,
            new InlineProgress<int>(progressValues.Add)).ConfigureAwait(false);
        Assert.Equal(100, progressValues[^1]);
        Assert.Equal(packageHash, prepared.PackageSha256);
        Assert.True(File.Exists(Path.Combine(
            prepared.StagedAppDirectory,
            "SpinFOURKAYYY.exe")));
        Assert.True(File.Exists(Path.Combine(
            prepared.StagedAppDirectory,
            "Engine",
            "Magpie",
            "test.dat")));
        _ = await ReleasePackageValidator.ValidateStagedAppAsync(
            prepared.StagedAppDirectory,
            new Version(1, 0, 6)).ConfigureAwait(false);

        ReleaseUpdateCheck current = await service.CheckAsync(new Version(1, 0, 6))
            .ConfigureAwait(false);
        Assert.False(current.IsUpdateAvailable);
        Assert.Equal(new Version(1, 2, 3),
            GitHubReleaseUpdateService.NormalizeThreePartVersion(
                new Version(1, 2, 3, 99)));
    }

    private static async Task UpdatePackageFailuresAreFailClosedAsync()
    {
        await using TempDirectory temp = new();
        const string version = "1.0.6";
        byte[] validPackage = CreateUpdatePackage(
            version,
            new Dictionary<string, byte[]>
            {
                ["SpinFOURKAYYY.exe"] = Encoding.ASCII.GetBytes("app"),
            });
        string validHash = Convert.ToHexString(SHA256.HashData(validPackage));
        string packageName = $"SpinFOURKAYYY-{version}-win-x64.zip";
        byte[] wrongSidecar = Encoding.ASCII.GetBytes(
            $"{new string('0', 64)} *{packageName}\n");
        string checksumRoot = Path.Combine(temp.Path, "checksum-updates");
        using (RouteHttpMessageHandler handler = CreateUpdateRoutes(
            version,
            validPackage,
            wrongSidecar,
            validHash))
        using (HttpClient httpClient = new(handler))
        using (GitHubReleaseUpdateService service = new(checksumRoot, httpClient))
        {
            ReleaseUpdateCheck check = await service.CheckAsync(new Version(1, 0, 5))
                .ConfigureAwait(false);
            await Assert.ThrowsAsync<InvalidDataException>(
                () => service.PrepareAsync(check.LatestRelease))
                .ConfigureAwait(false);
        }

        Assert.False(Directory.Exists(Path.Combine(checksumRoot, "v1.0.6")));

        byte[] unsafePackage = CreateUpdatePackage(
            version,
            new Dictionary<string, byte[]>
            {
                ["SpinFOURKAYYY.exe"] = Encoding.ASCII.GetBytes("app"),
            },
            unsafeEntry: $"SpinFOURKAYYY-{version}-win-x64/../escape.txt");
        string unsafeHash = Convert.ToHexString(SHA256.HashData(unsafePackage));
        byte[] unsafeSidecar = Encoding.ASCII.GetBytes(
            $"{unsafeHash.ToLowerInvariant()} *{packageName}\n");
        string unsafeRoot = Path.Combine(temp.Path, "unsafe-updates");
        using (RouteHttpMessageHandler handler = CreateUpdateRoutes(
            version,
            unsafePackage,
            unsafeSidecar,
            unsafeHash))
        using (HttpClient httpClient = new(handler))
        using (GitHubReleaseUpdateService service = new(unsafeRoot, httpClient))
        {
            ReleaseUpdateCheck check = await service.CheckAsync(new Version(1, 0, 5))
                .ConfigureAwait(false);
            InvalidDataException failure =
                await Assert.ThrowsAsync<InvalidDataException>(
                    () => service.PrepareAsync(check.LatestRelease))
                    .ConfigureAwait(false);
            Assert.Contains("unsafe path", failure.Message);
        }

        Assert.False(File.Exists(Path.Combine(temp.Path, "escape.txt")));
        Assert.False(Directory.Exists(Path.Combine(unsafeRoot, "v1.0.6")));
    }

    private static async Task UpdateTransactionalInstallAndRollbackAsync()
    {
        await using TempDirectory temp = new();
        string target = Path.Combine(temp.Path, "Installed App");
        string requestRoot = Path.Combine(temp.Path, "updates", "v1.0.6");
        string staged = Path.Combine(
            requestRoot,
            "staged",
            "SpinFOURKAYYY-1.0.6-win-x64");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(staged);
        byte[] oldExecutable = Encoding.ASCII.GetBytes("old app exact");
        byte[] oldOwned = Encoding.ASCII.GetBytes("remove on update");
        byte[] userFile = Encoding.ASCII.GetBytes("never owned");
        string targetExecutable = Path.Combine(target, "SpinFOURKAYYY.exe");
        await File.WriteAllBytesAsync(targetExecutable, oldExecutable)
            .ConfigureAwait(false);
        await File.WriteAllBytesAsync(Path.Combine(target, "old-owned.txt"), oldOwned)
            .ConfigureAwait(false);
        await File.WriteAllBytesAsync(Path.Combine(target, "my-notes.txt"), userFile)
            .ConfigureAwait(false);
        ReleasePackageManifest oldManifest = CreateReleaseManifest(
            "1.0.5",
            new Dictionary<string, byte[]>
            {
                ["SpinFOURKAYYY.exe"] = oldExecutable,
                ["old-owned.txt"] = oldOwned,
            });
        await WriteManifestAsync(target, oldManifest).ConfigureAwait(false);
        string savedSettings = Path.Combine(temp.Path, "LocalAppData", "preferences.json");
        Directory.CreateDirectory(Path.GetDirectoryName(savedSettings)!);
        byte[] settingsBytes = Encoding.UTF8.GetBytes("{\"uiScaleHundredths\":125}");
        await File.WriteAllBytesAsync(savedSettings, settingsBytes).ConfigureAwait(false);

        byte[] newExecutable = Encoding.ASCII.GetBytes("new app exact");
        byte[] newOwned = Encoding.ASCII.GetBytes("new feature");
        Dictionary<string, byte[]> newContent = new()
        {
            ["SpinFOURKAYYY.exe"] = newExecutable,
            ["new-owned.txt"] = newOwned,
        };
        ReleasePackageManifest newManifest = CreateReleaseManifest(
            "1.0.6",
            newContent);
        await WriteStagedContentAsync(staged, newContent, newManifest)
            .ConfigureAwait(false);
        UpdateApplyRequest request = new()
        {
            ExpectedVersion = "1.0.6",
            PreviousVersion = "1.0.5",
            PreviousProcessId = Environment.ProcessId,
            PreviousExecutablePath = targetExecutable,
            TargetDirectory = target,
            StagedAppDirectory = staged,
        };

        UpdateApplyResult applied =
            await ReleaseUpdateInstaller.ApplyFilesTransactionalAsync(
                request,
                newManifest,
                restartApplication: false).ConfigureAwait(false);
        Assert.Equal("1.0.6", applied.InstalledVersion);
        Assert.SequenceEqual(
            newExecutable,
            await File.ReadAllBytesAsync(targetExecutable).ConfigureAwait(false));
        Assert.SequenceEqual(
            newOwned,
            await File.ReadAllBytesAsync(Path.Combine(target, "new-owned.txt"))
                .ConfigureAwait(false));
        Assert.False(File.Exists(Path.Combine(target, "old-owned.txt")));
        Assert.SequenceEqual(
            userFile,
            await File.ReadAllBytesAsync(Path.Combine(target, "my-notes.txt"))
                .ConfigureAwait(false));
        Assert.SequenceEqual(
            settingsBytes,
            await File.ReadAllBytesAsync(savedSettings).ConfigureAwait(false));
        ReleasePackageManifest installedManifest =
            JsonSerializer.Deserialize<ReleasePackageManifest>(
                await File.ReadAllBytesAsync(
                    Path.Combine(target, "release-manifest.json"))
                    .ConfigureAwait(false))
            ?? throw new TestFailureException(
                "The installed release manifest was empty.");
        Assert.Equal("1.0.6", installedManifest.Version);
        Assert.Equal(2, installedManifest.Files.Count);

        // A post-copy hash failure must restore the exact installed version,
        // including stale owned files, while leaving user/settings files alone.
        string rollbackTarget = Path.Combine(temp.Path, "Rollback App");
        string rollbackStaged = Path.Combine(
            temp.Path,
            "updates",
            "v1.0.7",
            "staged",
            "SpinFOURKAYYY-1.0.7-win-x64");
        Directory.CreateDirectory(rollbackTarget);
        Directory.CreateDirectory(rollbackStaged);
        string rollbackExecutable = Path.Combine(
            rollbackTarget,
            "SpinFOURKAYYY.exe");
        await File.WriteAllBytesAsync(rollbackExecutable, oldExecutable)
            .ConfigureAwait(false);
        await File.WriteAllBytesAsync(
            Path.Combine(rollbackTarget, "old-owned.txt"),
            oldOwned).ConfigureAwait(false);
        await File.WriteAllBytesAsync(
            Path.Combine(rollbackTarget, "my-notes.txt"),
            userFile).ConfigureAwait(false);
        await WriteManifestAsync(rollbackTarget, oldManifest).ConfigureAwait(false);
        await WriteStagedContentAsync(rollbackStaged, newContent, newManifest)
            .ConfigureAwait(false);
        ReleasePackageManifest badManifest = newManifest with
        {
            Version = "1.0.7",
            Files = newManifest.Files.Select(file =>
                string.Equals(file.Path, "new-owned.txt", StringComparison.Ordinal)
                    ? file with { Sha256 = new string('F', 64) }
                    : file).ToArray(),
        };
        await WriteManifestAsync(rollbackStaged, badManifest).ConfigureAwait(false);
        UpdateApplyRequest rollbackRequest = request with
        {
            ExpectedVersion = "1.0.7",
            PreviousExecutablePath = rollbackExecutable,
            TargetDirectory = rollbackTarget,
            StagedAppDirectory = rollbackStaged,
        };
        InvalidOperationException rollbackFailure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => ReleaseUpdateInstaller.ApplyFilesTransactionalAsync(
                    rollbackRequest,
                    badManifest,
                    restartApplication: false)).ConfigureAwait(false);
        Assert.Contains("every replaced app file was restored", rollbackFailure.Message);
        Assert.SequenceEqual(
            oldExecutable,
            await File.ReadAllBytesAsync(rollbackExecutable).ConfigureAwait(false));
        Assert.SequenceEqual(
            oldOwned,
            await File.ReadAllBytesAsync(
                Path.Combine(rollbackTarget, "old-owned.txt")).ConfigureAwait(false));
        Assert.False(File.Exists(Path.Combine(rollbackTarget, "new-owned.txt")));
        Assert.SequenceEqual(
            userFile,
            await File.ReadAllBytesAsync(
                Path.Combine(rollbackTarget, "my-notes.txt")).ConfigureAwait(false));
    }

    private static RouteHttpMessageHandler CreateUpdateRoutes(
        string version,
        byte[] package,
        byte[] sidecar,
        string packageHash)
    {
        string packageName = $"SpinFOURKAYYY-{version}-win-x64.zip";
        string packageUrl =
            $"https://github.com/itsspin/SPINFOURKAYYY/releases/download/v{version}/{packageName}";
        string sidecarUrl = packageUrl + ".sha256";
        string releaseJson = JsonSerializer.Serialize(new
        {
            tag_name = "v" + version,
            html_url =
                $"https://github.com/itsspin/SPINFOURKAYYY/releases/tag/v{version}",
            draft = false,
            prerelease = false,
            assets = new object[]
            {
                new
                {
                    name = packageName,
                    size = package.LongLength,
                    digest = "sha256:" + packageHash.ToLowerInvariant(),
                    browser_download_url = packageUrl,
                },
                new
                {
                    name = packageName + ".sha256",
                    size = sidecar.LongLength,
                    digest = "sha256:" + Convert.ToHexString(
                        SHA256.HashData(sidecar)).ToLowerInvariant(),
                    browser_download_url = sidecarUrl,
                },
            },
        });
        return new RouteHttpMessageHandler(new Dictionary<string, byte[]>
        {
            ["https://api.github.com/repos/itsspin/SPINFOURKAYYY/releases/latest"] =
                Encoding.UTF8.GetBytes(releaseJson),
            [packageUrl] = package,
            [sidecarUrl] = sidecar,
        });
    }

    private static byte[] CreateUpdatePackage(
        string version,
        IReadOnlyDictionary<string, byte[]> files,
        string? unsafeEntry = null)
    {
        string root = $"SpinFOURKAYYY-{version}-win-x64";
        ReleasePackageManifest manifest = CreateReleaseManifest(version, files);
        using MemoryStream output = new();
        using (ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string relative, byte[] content) in files)
            {
                ZipArchiveEntry entry = archive.CreateEntry(
                    root + "/" + relative.Replace('\\', '/'));
                using Stream stream = entry.Open();
                stream.Write(content);
            }

            ZipArchiveEntry manifestEntry = archive.CreateEntry(
                root + "/release-manifest.json");
            using (Stream stream = manifestEntry.Open())
            {
                JsonSerializer.Serialize(stream, manifest);
            }

            if (unsafeEntry is not null)
            {
                ZipArchiveEntry unsafeZipEntry = archive.CreateEntry(unsafeEntry);
                using Stream stream = unsafeZipEntry.Open();
                stream.Write(Encoding.ASCII.GetBytes("unsafe"));
            }
        }

        return output.ToArray();
    }

    private static ReleasePackageManifest CreateReleaseManifest(
        string version,
        IReadOnlyDictionary<string, byte[]> files) =>
        new()
        {
            Product = "SpinFOURKAYYY",
            Version = version,
            Runtime = "win-x64",
            Files = files.Select(pair => new ReleasePackageFile
            {
                Path = pair.Key.Replace('\\', '/'),
                Size = pair.Value.LongLength,
                Sha256 = Convert.ToHexString(SHA256.HashData(pair.Value)),
            }).ToArray(),
        };

    private static async Task WriteStagedContentAsync(
        string stagedDirectory,
        IReadOnlyDictionary<string, byte[]> files,
        ReleasePackageManifest manifest)
    {
        foreach ((string relative, byte[] content) in files)
        {
            string path = Path.Combine(
                stagedDirectory,
                relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, content).ConfigureAwait(false);
        }

        await WriteManifestAsync(stagedDirectory, manifest).ConfigureAwait(false);
    }

    private static Task WriteManifestAsync(
        string directory,
        ReleasePackageManifest manifest)
    {
        Directory.CreateDirectory(directory);
        return File.WriteAllBytesAsync(
            Path.Combine(directory, "release-manifest.json"),
            JsonSerializer.SerializeToUtf8Bytes(manifest));
    }

    private static async Task ConfigurationAtomicFakeClientEditAsync()
    {
        await using TempDirectory temp = new();
        string iniPath = Path.Combine(temp.Path, "eqclient.ini");
        const string original =
            "; preserve me\r\n"
            + "[Defaults]\r\n"
            + "AllowResize=0\r\n"
            + "Unrelated=untouched\r\n"
            + "[VideoMode]\r\n"
            + "Width=800\r\n"
            + "Height=600\r\n"
            + "WindowedWidth=800\r\n"
            + "WindowedHeight=600\r\n"
            + "Fullscreen=1\r\n";
        byte[] originalBytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(original)];
        await File.WriteAllBytesAsync(iniPath, originalBytes).ConfigureAwait(false);

        EqClientConfigurationService service = new();
        await service.ApplyWindowedResolutionAsync(
            iniPath,
            new PixelSize(2560, 1440)).ConfigureAwait(false);

        EqClientVideoSettings settings = await service.ReadAsync(iniPath).ConfigureAwait(false);
        Assert.Equal(new PixelSize(2560, 1440), settings.FullscreenResolution);
        Assert.Equal(new PixelSize(2560, 1440), settings.WindowedResolution);
        Assert.Equal(false, settings.Fullscreen);
        Assert.Equal(false, settings.AllowResize);
        Assert.Equal(false, settings.Maximized);

        byte[] editedBytes = await File.ReadAllBytesAsync(iniPath).ConfigureAwait(false);
        Assert.True(editedBytes.AsSpan().StartsWith(Utf8Preamble));
        string edited = Encoding.UTF8.GetString(editedBytes, 3, editedBytes.Length - 3);
        Assert.Contains("; preserve me\r\n", edited);
        Assert.Contains("Unrelated=untouched\r\n", edited);
        Assert.Contains("Width=2560\r\n", edited);
        Assert.Contains("Maximized=0\r\n", edited);
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    private static async Task ConfigurationNativeUiScaleGuardAsync()
    {
        await using TempDirectory temp = new();
        EqClientConfigurationService service = new();
        PixelSize resolution = new(1920, 1080);

        string validPath = Path.Combine(temp.Path, "valid.ini");
        await File.WriteAllTextAsync(
            validPath,
            "[Defaults]\r\nUIScale=2\r\n[VideoMode]\r\nWidth=800\r\nHeight=600\r\n")
            .ConfigureAwait(false);

        EqClientVideoSettings beforeApply = await service.ReadAsync(validPath)
            .ConfigureAwait(false);
        Assert.Equal(2, beforeApply.NativeUiScaleIndex);
        Assert.Equal(NativeUiScaleStatus.Valid, beforeApply.NativeUiScaleStatus);

        EqClientConfigurationApplyResult validResult =
            await service.ApplyWindowedResolutionAsync(
                validPath,
                resolution,
                chatFontSizeIncrease: 0).ConfigureAwait(false);
        EqClientVideoSettings afterApply = await service.ReadAsync(validPath)
            .ConfigureAwait(false);
        Assert.Equal(0, afterApply.NativeUiScaleIndex);
        Assert.Equal(NativeUiScaleStatus.Valid, afterApply.NativeUiScaleStatus);
        Assert.Empty(validResult.Warnings);

        string alreadyNativePath = Path.Combine(temp.Path, "already-native.ini");
        await File.WriteAllTextAsync(
            alreadyNativePath,
            "[Defaults]\r\nUIScale=0\r\n[VideoMode]\r\nWidth=800\r\nHeight=600\r\n")
            .ConfigureAwait(false);

        EqClientConfigurationApplyResult alreadyNativeResult =
            await service.ApplyWindowedResolutionAsync(
                alreadyNativePath,
                resolution,
                chatFontSizeIncrease: 0).ConfigureAwait(false);
        EqClientVideoSettings alreadyNative = await service.ReadAsync(alreadyNativePath)
            .ConfigureAwait(false);
        Assert.Equal(0, alreadyNative.NativeUiScaleIndex);
        Assert.Equal(NativeUiScaleStatus.Valid, alreadyNative.NativeUiScaleStatus);
        Assert.Empty(alreadyNativeResult.Warnings);

        string missingPath = Path.Combine(temp.Path, "missing.ini");
        await File.WriteAllTextAsync(
            missingPath,
            "[Defaults]\r\nUnrelated=untouched\r\n[VideoMode]\r\nWidth=800\r\nHeight=600\r\n")
            .ConfigureAwait(false);

        EqClientVideoSettings beforeMissing = await service.ReadAsync(missingPath)
            .ConfigureAwait(false);
        Assert.Equal(null, beforeMissing.NativeUiScaleIndex);
        Assert.Equal(NativeUiScaleStatus.Missing, beforeMissing.NativeUiScaleStatus);

        EqClientConfigurationApplyResult missingResult =
            await service.ApplyWindowedResolutionAsync(
                missingPath,
                resolution,
                chatFontSizeIncrease: 0).ConfigureAwait(false);
        EqClientVideoSettings missing = await service.ReadAsync(missingPath)
            .ConfigureAwait(false);
        Assert.Equal(0, missing.NativeUiScaleIndex);
        Assert.Equal(NativeUiScaleStatus.Valid, missing.NativeUiScaleStatus);
        Assert.Contains(
            "UIScale=0",
            await File.ReadAllTextAsync(missingPath).ConfigureAwait(false));
        Assert.Empty(missingResult.Warnings);

        foreach ((string value, string fileName) in new[]
        {
            ("larger", "malformed.ini"),
            ("5", "out-of-range.ini"),
        })
        {
            string invalidPath = Path.Combine(temp.Path, fileName);
            await File.WriteAllTextAsync(
                invalidPath,
                $"[Defaults]\r\nUIScale={value} ; preserve\r\n"
                + "[VideoMode]\r\nWidth=800\r\nHeight=600\r\n")
                .ConfigureAwait(false);

            byte[] original = await File.ReadAllBytesAsync(invalidPath)
                .ConfigureAwait(false);
            EqClientVideoSettings invalid = await service.ReadAsync(invalidPath)
                .ConfigureAwait(false);
            Assert.Equal(null, invalid.NativeUiScaleIndex);
            Assert.Equal(NativeUiScaleStatus.Invalid, invalid.NativeUiScaleStatus);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => service.ApplyWindowedResolutionAsync(
                    invalidPath,
                    resolution,
                    chatFontSizeIncrease: 0)).ConfigureAwait(false);
            Assert.SequenceEqual(
                original,
                await File.ReadAllBytesAsync(invalidPath).ConfigureAwait(false));
        }
    }

    private static async Task ConfigurationErrorPathsAsync()
    {
        await using TempDirectory temp = new();
        EqClientConfigurationService service = new();
        string missing = Path.Combine(temp.Path, "missing.ini");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.ReadAsync(missing)).ConfigureAwait(false);
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.ApplyWindowedResolutionAsync(
                missing,
                new PixelSize(1920, 1080))).ConfigureAwait(false);

        string iniPath = Path.Combine(temp.Path, "eqclient.ini");
        const string original = "[VideoMode]\r\nWidth=800\r\nHeight=600\r\n";
        await File.WriteAllTextAsync(iniPath, original).ConfigureAwait(false);
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync().ConfigureAwait(false);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ApplyWindowedResolutionAsync(
                iniPath,
                new PixelSize(1920, 1080),
                cancellation.Token)).ConfigureAwait(false);
        Assert.Equal(original, await File.ReadAllTextAsync(iniPath).ConfigureAwait(false));
    }

    private static async Task BackupRoundTripAsync()
    {
        await using TempDirectory temp = new();
        string client = Path.Combine(temp.Path, "client");
        string backups = Path.Combine(temp.Path, "backups");
        Directory.CreateDirectory(client);
        string eqClient = Path.Combine(client, "eqclient.ini");
        byte[] original = Encoding.UTF8.GetBytes("[VideoMode]\r\nWidth=1920\r\n");
        await File.WriteAllBytesAsync(eqClient, original).ConfigureAwait(false);

        BackupService service = new();
        BackupManifest manifest = await service.CreateAsync(
            [eqClient, eqClient],
            backups).ConfigureAwait(false);

        Assert.Equal(1, manifest.Entries.Count);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(original)),
            manifest.Entries[0].Sha256);
        Assert.True(File.Exists(manifest.ManifestPath));
        BackupVerificationResult verification = await service.VerifyAsync(manifest)
            .ConfigureAwait(false);
        Assert.True(verification.IsValid);
        Assert.Empty(verification.Errors);

        BackupManifest loaded = await service.LoadManifestAsync(manifest.ManifestPath)
            .ConfigureAwait(false);
        Assert.Equal(manifest.BackupId, loaded.BackupId);
        Assert.Equal(manifest.Entries[0].Sha256, loaded.Entries[0].Sha256);

        await File.WriteAllTextAsync(eqClient, "modified").ConfigureAwait(false);
        await service.RestoreAsync(loaded).ConfigureAwait(false);
        Assert.SequenceEqual(original, await File.ReadAllBytesAsync(eqClient).ConfigureAwait(false));

        // Restore is intentionally safe to repeat and remains byte-identical.
        await service.RestoreAsync(loaded).ConfigureAwait(false);
        Assert.SequenceEqual(original, await File.ReadAllBytesAsync(eqClient).ConfigureAwait(false));
        Assert.Empty(Directory.GetDirectories(backups, "*.pending", SearchOption.TopDirectoryOnly));
    }

    private static async Task BackupCorruptionIsConflictSafeAsync()
    {
        await using TempDirectory temp = new();
        string client = Path.Combine(temp.Path, "client");
        string backups = Path.Combine(temp.Path, "backups");
        Directory.CreateDirectory(client);
        string first = Path.Combine(client, "eqclient.ini");
        string second = Path.Combine(client, "layout.ini");
        await File.WriteAllTextAsync(first, "original first").ConfigureAwait(false);
        await File.WriteAllTextAsync(second, "original second").ConfigureAwait(false);

        BackupService service = new();
        BackupManifest manifest = await service.CreateAsync([first, second], backups)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(first, "live conflict first").ConfigureAwait(false);
        await File.WriteAllTextAsync(second, "live conflict second").ConfigureAwait(false);

        BackupEntry damagedEntry = manifest.Entries[1];
        string damagedPayload = Path.Combine(
            manifest.BackupDirectory,
            damagedEntry.BackupRelativePath.Replace('/', Path.DirectorySeparatorChar));
        await File.WriteAllTextAsync(damagedPayload, "damaged").ConfigureAwait(false);

        BackupVerificationResult verification = await service.VerifyAsync(manifest)
            .ConfigureAwait(false);
        Assert.False(verification.IsValid);
        Assert.True(verification.Errors.Count > 0);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.RestoreAsync(manifest)).ConfigureAwait(false);
        Assert.Equal("live conflict first", await File.ReadAllTextAsync(first).ConfigureAwait(false));
        Assert.Equal("live conflict second", await File.ReadAllTextAsync(second).ConfigureAwait(false));
    }

    private static async Task BackupErrorPathsAsync()
    {
        await using TempDirectory temp = new();
        BackupService service = new();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateAsync([], Path.Combine(temp.Path, "backups")))
            .ConfigureAwait(false);
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.CreateAsync(
                [Path.Combine(temp.Path, "missing.ini")],
                Path.Combine(temp.Path, "backups"))).ConfigureAwait(false);

        string source = Path.Combine(temp.Path, "eqclient.ini");
        await File.WriteAllTextAsync(source, "safe").ConfigureAwait(false);
        BackupManifest manifest = await service.CreateAsync(
            [source],
            Path.Combine(temp.Path, "backups")).ConfigureAwait(false);
        BackupEntry escapedEntry = manifest.Entries[0] with
        {
            BackupRelativePath = "../outside.dat",
        };
        BackupManifest escaped = manifest with { Entries = [escapedEntry] };

        BackupVerificationResult escapedVerification = await service.VerifyAsync(escaped)
            .ConfigureAwait(false);
        Assert.False(escapedVerification.IsValid);
        Assert.True(escapedVerification.Errors.Any(
            error => error.Contains("escaped", StringComparison.OrdinalIgnoreCase)));
    }

    private static void DpiTokenMergeIsLossless()
    {
        MethodInfo mergeApplicationAware = typeof(DpiCompatibilityService).GetMethod(
            "MergeApplicationAware",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new TestFailureException(
                "DPI token merge logic is unavailable for side-effect-free validation.");
        MethodInfo mergeOriginal = typeof(DpiCompatibilityService).GetMethod(
            "MergeOriginalDpiFlags",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new TestFailureException(
                "DPI restore merge logic is unavailable for side-effect-free validation.");

        Assert.Equal(
            "~ HIGHDPIAWARE",
            InvokePrivate<string>(
                mergeApplicationAware,
                new object?[] { null }));
        Assert.Equal(
            "~ # WIN7RTM HIGHDPIAWARE",
            InvokePrivate<string>(
                mergeApplicationAware,
                "~ WIN7RTM DPIUNAWARE GDIDPISCALING #"));
        Assert.Equal(
            "~ DISABLETHEMES HIGHDPIAWARE",
            InvokePrivate<string>(
                mergeApplicationAware,
                "~ highdpiaware DISABLETHEMES HIGHDPIAWARE"));

        DpiCompatibilitySnapshot snapshot = new(
            @"C:\Games\EverQuest\eqgame.exe",
            ValueExisted: true,
            OriginalValue: "~ DPIUNAWARE WIN7RTM",
            Microsoft.Win32.RegistryValueKind.String,
            AppliedValue: "~ WIN7RTM HIGHDPIAWARE");
        Assert.Equal(
            "~ WIN7RTM NEWFLAG DPIUNAWARE",
            InvokePrivate<string>(
                mergeOriginal,
                "~ WIN7RTM HIGHDPIAWARE NEWFLAG",
                snapshot));

        DpiCompatibilitySnapshot noOriginal = snapshot with
        {
            ValueExisted = false,
            OriginalValue = null,
        };
        Assert.Null(InvokePrivate<string?>(
            mergeOriginal,
            "~ HIGHDPIAWARE",
            noOriginal));
    }

    private static void DpiValidationAvoidsRegistry()
    {
        DpiCompatibilityService service = new();
        string missing = Path.Combine(
            Path.GetTempPath(),
            $"SpinFOURKAYYY-missing-{Guid.NewGuid():N}.exe");

        Assert.Throws<ArgumentException>(() => service.Capture(" "));
        Assert.Throws<FileNotFoundException>(() => service.Capture(missing));
        Assert.Throws<FileNotFoundException>(() => service.ApplyApplicationAware(missing));
    }

    private static void DpiPersistedIntentAndLegacyCrashRecovery()
    {
        MethodInfo resolveExpected = typeof(DpiCompatibilityService).GetMethod(
            "ResolveExpectedAppliedValue",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new TestFailureException(
                "The deterministic DPI recovery intent is unavailable.");
        MethodInfo isOwned = typeof(DpiCompatibilityService).GetMethod(
            "IsOwnedAppliedValue",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new TestFailureException(
                "The owned DPI recovery check is unavailable.");
        MethodInfo matchesOriginal = typeof(DpiCompatibilityService).GetMethod(
            "RegistryValueMatches",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new TestFailureException(
                "The exact-original DPI recovery check is unavailable.");

        DpiCompatibilitySnapshot legacyEmpty = new(
            @"C:\Games\EverQuest\eqgame.exe",
            ValueExisted: false,
            OriginalValue: null,
            Microsoft.Win32.RegistryValueKind.String,
            AppliedValue: string.Empty);
        Assert.Equal(
            "~ HIGHDPIAWARE",
            InvokePrivate<string>(resolveExpected, legacyEmpty));
        Assert.True(InvokePrivate<bool>(
            isOwned,
            true,
            "~ HIGHDPIAWARE",
            Microsoft.Win32.RegistryValueKind.String,
            legacyEmpty));
        Assert.False(InvokePrivate<bool>(
            isOwned,
            true,
            "~ DPIUNAWARE",
            Microsoft.Win32.RegistryValueKind.String,
            legacyEmpty));
        Assert.False(InvokePrivate<bool>(
            isOwned,
            true,
            "~ HIGHDPIAWARE",
            Microsoft.Win32.RegistryValueKind.ExpandString,
            legacyEmpty));
        Assert.True(InvokePrivate<bool>(
            matchesOriginal,
            false,
            null,
            false,
            null));

        DpiCompatibilitySnapshot legacyWithOriginal = legacyEmpty with
        {
            ValueExisted = true,
            OriginalValue = "~ WIN7RTM DPIUNAWARE",
        };
        Assert.Equal(
            "~ WIN7RTM HIGHDPIAWARE",
            InvokePrivate<string>(resolveExpected, legacyWithOriginal));
        Assert.True(InvokePrivate<bool>(
            matchesOriginal,
            true,
            "~ WIN7RTM DPIUNAWARE",
            true,
            "~ WIN7RTM DPIUNAWARE"));

        DpiCompatibilitySnapshot persistedIntent = legacyWithOriginal with
        {
            AppliedValue = "~ WIN7RTM HIGHDPIAWARE",
        };
        Assert.Equal(
            persistedIntent.AppliedValue,
            InvokePrivate<string>(resolveExpected, persistedIntent));
        Assert.True(InvokePrivate<bool>(
            isOwned,
            true,
            persistedIntent.AppliedValue,
            Microsoft.Win32.RegistryValueKind.String,
            persistedIntent));

        DpiCompatibilityService service = new();
        Assert.Throws<InvalidDataException>(
            () => service.Restore(
                persistedIntent with
                {
                    AppliedValue = "~ DPIUNAWARE",
                }));
        Assert.Throws<InvalidDataException>(
            () => service.Restore(
                persistedIntent with
                {
                    ValueExisted = false,
                }));
    }

    private static async Task MagpiePortableJsonSchemaAsync()
    {
        await using TempDirectory temp = new();
        MagpieFixture fixture = await MagpieFixture.CreateAsync(temp.Path).ConfigureAwait(false);
        string configDirectory = Path.Combine(fixture.MagpieDirectory, "config");
        Directory.CreateDirectory(configDirectory);
        string configPath = Path.Combine(configDirectory, "config.json");
        const string seed =
            """
            {
              "language": "en",
              "customSetting": "keep",
              "scalingModes": [
                { "name": "User mode", "effects": [] }
              ],
              "profiles": [
                { "scalingMode": 0, "autoScale": 1, "captureMethod": 2 },
                {
                  "name": "Unrelated",
                  "pathRule": "C:\\Other.exe",
                  "classNameRule": "Other",
                  "autoScale": 2,
                  "3DGameMode": true,
                  "sentinel": 7
                }
              ]
            }
            """;
        await File.WriteAllTextAsync(configPath, seed).ConfigureAwait(false);

        MagpiePortableConfigService service = new();
        MagpieProfileRequest request = fixture.CreateRequest(ScalingFilter.Fsr) with
        {
            SourceWindowClass = "EverQuestWndClass",
            RcasSharpness = 0.66,
            AutoScaleFullscreen = true,
            DisableDirectFlip = true,
            CaptureTitleBar = true,
            AdjustCursorSpeed = false,
            GraphicsAdapter = new MagpieGraphicsAdapter(2, 0x10DE, 0x1234),
            MaximumFrameRate = 144,
        };
        MagpiePortableConfigResult result = await service.WriteAsync(request)
            .ConfigureAwait(false);

        Assert.Equal(Path.GetFullPath(configPath), result.ConfigPath);
        Assert.Equal(1, result.ScalingModeIndex);
        Assert.Equal(2, result.ProfileIndex);

        byte[] bytes = await File.ReadAllBytesAsync(configPath).ConfigureAwait(false);
        Assert.False(bytes.AsSpan().StartsWith(Utf8Preamble));
        using JsonDocument json = JsonDocument.Parse(bytes);
        JsonElement root = json.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.Equal("en", root.GetProperty("language").GetString());
        Assert.Equal("keep", root.GetProperty("customSetting").GetString());
        Assert.False(root.GetProperty("alwaysRunAsAdmin").GetBoolean());
        Assert.False(root.GetProperty("autoCheckForUpdates").GetBoolean());

        JsonElement modes = root.GetProperty("scalingModes");
        Assert.Equal(6, modes.GetArrayLength());
        Assert.Equal("User mode", modes[0].GetProperty("name").GetString());
        JsonElement fsr = FindNamed(modes, MagpiePortableConfigService.SmoothModeName);
        JsonElement fsrEffects = fsr.GetProperty("effects");
        Assert.Equal(@"FSR\FSR_EASU", fsrEffects[0].GetProperty("name").GetString());
        Assert.Equal(@"FSR\FSR_RCAS", fsrEffects[1].GetProperty("name").GetString());
        Assert.NearlyEqual(
            0.66,
            fsrEffects[1]
                .GetProperty("parameters")
                .GetProperty("sharpness")
                .GetDouble());
        JsonElement readable = FindNamed(
            modes,
            MagpiePortableConfigService.ReadableModeName);
        JsonElement readableEffects = readable.GetProperty("effects");
        Assert.Equal(1, readableEffects.GetArrayLength());
        Assert.Equal(
            @"NIS\NIS",
            readableEffects[0].GetProperty("name").GetString());
        Assert.Equal(1, readableEffects[0].GetProperty("scalingType").GetInt32());
        Assert.NearlyEqual(
            1.0,
            readableEffects[0].GetProperty("scale").GetProperty("x").GetDouble());
        Assert.NearlyEqual(
            1.0,
            readableEffects[0].GetProperty("scale").GetProperty("y").GetDouble());
        Assert.NearlyEqual(
            0.66,
            readableEffects[0]
                .GetProperty("parameters")
                .GetProperty("sharpness")
                .GetDouble());
        JsonElement nativeClarity = FindNamed(
            modes,
            MagpiePortableConfigService.NativeClarityModeName);
        JsonElement nativeEffects = nativeClarity.GetProperty("effects");
        Assert.Equal(1, nativeEffects.GetArrayLength());
        Assert.Equal("Nearest", nativeEffects[0].GetProperty("name").GetString());
        Assert.Equal(1, nativeEffects[0].GetProperty("scalingType").GetInt32());
        Assert.NearlyEqual(
            1.0,
            nativeEffects[0].GetProperty("scale").GetProperty("x").GetDouble());
        Assert.NearlyEqual(
            1.0,
            nativeEffects[0].GetProperty("scale").GetProperty("y").GetDouble());
        Assert.False(nativeEffects[0].TryGetProperty("parameters", out _));

        JsonElement profiles = root.GetProperty("profiles");
        Assert.Equal(3, profiles.GetArrayLength());
        Assert.False(profiles[0].TryGetProperty("name", out _));
        Assert.Equal(0, profiles[0].GetProperty("autoScale").GetInt32());
        Assert.Equal(0, profiles[0].GetProperty("captureMethod").GetInt32());
        Assert.False(profiles[0].GetProperty("3DGameMode").GetBoolean());
        Assert.Equal(7, profiles[1].GetProperty("sentinel").GetInt32());
        Assert.Equal(0, profiles[1].GetProperty("autoScale").GetInt32());
        Assert.False(profiles[1].GetProperty("3DGameMode").GetBoolean());

        JsonElement profile = profiles[result.ProfileIndex];
        Assert.Equal(request.ProfileName, profile.GetProperty("name").GetString());
        Assert.Equal(
            Path.GetFullPath(fixture.GameExecutable),
            profile.GetProperty("pathRule").GetString());
        Assert.Equal("EverQuestWndClass", profile.GetProperty("classNameRule").GetString());
        Assert.Equal(
            Path.GetFullPath(fixture.LauncherExecutable),
            profile.GetProperty("launcherPath").GetString());
        Assert.Equal(1, profile.GetProperty("autoScale").GetInt32());
        Assert.Equal(result.ScalingModeIndex, profile.GetProperty("scalingMode").GetInt32());
        Assert.False(profile.GetProperty("3DGameMode").GetBoolean());
        Assert.True(profile.GetProperty("captureTitleBar").GetBoolean());
        Assert.False(profile.GetProperty("adjustCursorSpeed").GetBoolean());
        Assert.True(profile.GetProperty("disableDirectFlip").GetBoolean());
        Assert.True(profile.GetProperty("frameRateLimiterEnabled").GetBoolean());
        Assert.NearlyEqual(144, profile.GetProperty("maxFrameRate").GetDouble());
        JsonElement adapter = profile.GetProperty("graphicsCardId");
        Assert.Equal(2, adapter.GetProperty("idx").GetInt32());
        Assert.Equal(0x10DEu, adapter.GetProperty("vendorId").GetUInt32());
        Assert.Equal(0x1234u, adapter.GetProperty("deviceId").GetUInt32());
        Assert.Empty(Directory.GetFiles(configDirectory, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    private static async Task MagpieProfileUpsertAndFiltersAsync()
    {
        await using TempDirectory temp = new();
        MagpieFixture fixture = await MagpieFixture.CreateAsync(temp.Path).ConfigureAwait(false);
        MagpiePortableConfigService service = new();

        MagpiePortableConfigResult fsrResult = await service.WriteAsync(
            fixture.CreateRequest(ScalingFilter.Fsr)).ConfigureAwait(false);
        MagpiePortableConfigResult readableResult = await service.WriteAsync(
            fixture.CreateRequest(ScalingFilter.Nis)).ConfigureAwait(false);
        MagpiePortableConfigResult crispResult = await service.WriteAsync(
            fixture.CreateRequest(ScalingFilter.NearestNeighbor)).ConfigureAwait(false);
        MagpiePortableConfigResult lanczosResult = await service.WriteAsync(
            fixture.CreateRequest(ScalingFilter.Lanczos)).ConfigureAwait(false);
        MagpiePortableConfigResult nativeClarityResult = await service.WriteAsync(
            fixture.CreateRequest(ScalingFilter.Fsr) with
            {
                NativeClarityOnly = true,
            }).ConfigureAwait(false);

        Assert.True(fsrResult.ScalingModeIndex != readableResult.ScalingModeIndex);
        Assert.True(readableResult.ScalingModeIndex != crispResult.ScalingModeIndex);
        Assert.True(crispResult.ScalingModeIndex != lanczosResult.ScalingModeIndex);
        Assert.True(
            nativeClarityResult.ScalingModeIndex
                != fsrResult.ScalingModeIndex);
        Assert.Equal(fsrResult.ProfileIndex, readableResult.ProfileIndex);
        Assert.Equal(readableResult.ProfileIndex, crispResult.ProfileIndex);
        Assert.Equal(crispResult.ProfileIndex, lanczosResult.ProfileIndex);
        Assert.Equal(
            lanczosResult.ProfileIndex,
            nativeClarityResult.ProfileIndex);

        using JsonDocument json = JsonDocument.Parse(
            await File.ReadAllBytesAsync(nativeClarityResult.ConfigPath)
                .ConfigureAwait(false));
        JsonElement modes = json.RootElement.GetProperty("scalingModes");
        JsonElement profiles = json.RootElement.GetProperty("profiles");
        Assert.Equal(5, modes.GetArrayLength());
        Assert.Equal(2, profiles.GetArrayLength());
        Assert.Equal(
            MagpiePortableConfigService.NativeClarityModeName,
            modes[nativeClarityResult.ScalingModeIndex]
                .GetProperty("name")
                .GetString());
        Assert.Equal(
            MagpiePortableConfigService.ReadableModeName,
            modes[readableResult.ScalingModeIndex]
                .GetProperty("name")
                .GetString());
        Assert.Equal(
            nativeClarityResult.ScalingModeIndex,
            profiles[nativeClarityResult.ProfileIndex]
                .GetProperty("scalingMode")
                .GetInt32());
        MagpiePortableConfigResult nativeCompatibilityResult =
            await service.WriteAsync(
                fixture.CreateRequest(ScalingFilter.Lanczos) with
                {
                    NativeClarityOnly = true,
                    AntiAliasing = AntiAliasingMode.Fxaa,
                    RcasSharpness = 1.0,
                }).ConfigureAwait(false);
        Assert.Equal(
            nativeClarityResult.ScalingModeIndex,
            nativeCompatibilityResult.ScalingModeIndex);

        // The strongest supported FSR clarity setting remains one RCAS pass,
        // avoiding compounded texture noise.
        MagpiePortableConfigResult boosted = await service.WriteAsync(
            fixture.CreateRequest(ScalingFilter.Fsr) with
            {
                RcasSharpness = 1.0,
            }).ConfigureAwait(false);
        using JsonDocument boostedJson = JsonDocument.Parse(
            await File.ReadAllBytesAsync(boosted.ConfigPath)
                .ConfigureAwait(false));
        JsonElement boostedModes =
            boostedJson.RootElement.GetProperty("scalingModes");
        JsonElement boostedFsr = FindNamed(
            boostedModes,
            MagpiePortableConfigService.SmoothModeName);
        JsonElement boostedFsrEffects = boostedFsr.GetProperty("effects");
        Assert.Equal(2, boostedFsrEffects.GetArrayLength());
        Assert.Equal(
            @"FSR\FSR_RCAS",
            boostedFsrEffects[1].GetProperty("name").GetString());
        Assert.NearlyEqual(
            1.0,
            boostedFsrEffects[1]
                .GetProperty("parameters")
                .GetProperty("sharpness")
                .GetDouble());
        JsonElement boostedClarity = FindNamed(
            boostedModes,
            MagpiePortableConfigService.NativeClarityModeName);
        JsonElement boostedNativeEffects = boostedClarity.GetProperty("effects");
        Assert.Equal(1, boostedNativeEffects.GetArrayLength());
        Assert.Equal(
            "Nearest",
            boostedNativeEffects[0].GetProperty("name").GetString());
        Assert.False(boostedNativeEffects[0].TryGetProperty("parameters", out _));

        MagpiePortableConfigResult smaa = await service.WriteAsync(
            fixture.CreateRequest(ScalingFilter.Fsr) with
            {
                AntiAliasing = AntiAliasingMode.Smaa,
                RcasSharpness = 0.75,
            }).ConfigureAwait(false);
        using JsonDocument smaaJson = JsonDocument.Parse(
            await File.ReadAllBytesAsync(smaa.ConfigPath).ConfigureAwait(false));
        JsonElement smaaModes = smaaJson.RootElement.GetProperty("scalingModes");
        JsonElement smaaFsrEffects = FindNamed(
            smaaModes,
            MagpiePortableConfigService.SmoothModeName).GetProperty("effects");
        Assert.Equal(3, smaaFsrEffects.GetArrayLength());
        Assert.Equal(
            @"FSR\FSR_EASU",
            smaaFsrEffects[0].GetProperty("name").GetString());
        Assert.Equal(
            @"SMAA\SMAA_High",
            smaaFsrEffects[1].GetProperty("name").GetString());
        Assert.Equal(
            @"FSR\FSR_RCAS",
            smaaFsrEffects[2].GetProperty("name").GetString());
        JsonElement smaaReadableEffects = FindNamed(
            smaaModes,
            MagpiePortableConfigService.ReadableModeName).GetProperty("effects");
        Assert.Equal(1, smaaReadableEffects.GetArrayLength());
        Assert.Equal(
            @"NIS\NIS",
            smaaReadableEffects[0].GetProperty("name").GetString());
        Assert.NearlyEqual(
            0.75,
            smaaReadableEffects[0]
                .GetProperty("parameters")
                .GetProperty("sharpness")
                .GetDouble());
        string[] readableEffectNames = smaaReadableEffects
            .EnumerateArray()
            .Select(effect => effect.GetProperty("name").GetString() ?? string.Empty)
            .ToArray();
        Assert.False(
            readableEffectNames.Any(
                name => name.Contains("RCAS", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("SMAA", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("FXAA", StringComparison.OrdinalIgnoreCase)));
        JsonElement smaaNativeEffects = FindNamed(
            smaaModes,
            MagpiePortableConfigService.NativeClarityModeName).GetProperty("effects");
        Assert.Equal(1, smaaNativeEffects.GetArrayLength());
        Assert.Equal(
            "Nearest",
            smaaNativeEffects[0].GetProperty("name").GetString());
        Assert.False(smaaNativeEffects[0].TryGetProperty("parameters", out _));
        JsonElement smaaCrispEffects = FindNamed(
            smaaModes,
            MagpiePortableConfigService.PixelCrispModeName).GetProperty("effects");
        Assert.Equal(2, smaaCrispEffects.GetArrayLength());
        Assert.Equal("Nearest", smaaCrispEffects[0].GetProperty("name").GetString());
        Assert.Equal(
            @"SMAA\SMAA_High",
            smaaCrispEffects[1].GetProperty("name").GetString());

        MagpiePortableConfigResult nativeSmaaStrong = await service.WriteAsync(
            fixture.CreateRequest(ScalingFilter.Fsr) with
            {
                NativeClarityOnly = true,
                AntiAliasing = AntiAliasingMode.Smaa,
                RcasSharpness = 1.0,
            }).ConfigureAwait(false);
        using JsonDocument nativeSmaaStrongJson = JsonDocument.Parse(
            await File.ReadAllBytesAsync(nativeSmaaStrong.ConfigPath)
                .ConfigureAwait(false));
        JsonElement nativeSmaaStrongEffects = FindNamed(
            nativeSmaaStrongJson.RootElement.GetProperty("scalingModes"),
            MagpiePortableConfigService.NativeClarityModeName)
            .GetProperty("effects");
        Assert.Equal(1, nativeSmaaStrongEffects.GetArrayLength());
        Assert.Equal(
            "Nearest",
            nativeSmaaStrongEffects[0].GetProperty("name").GetString());
        Assert.Equal(
            1,
            nativeSmaaStrongEffects[0].GetProperty("scalingType").GetInt32());
        Assert.NearlyEqual(
            1.0,
            nativeSmaaStrongEffects[0]
                .GetProperty("scale")
                .GetProperty("x")
                .GetDouble());
        Assert.NearlyEqual(
            1.0,
            nativeSmaaStrongEffects[0]
                .GetProperty("scale")
                .GetProperty("y")
                .GetDouble());
        Assert.False(nativeSmaaStrongEffects[0].TryGetProperty("parameters", out _));

        MagpiePortableConfigResult fxaa = await service.WriteAsync(
            fixture.CreateRequest(ScalingFilter.Lanczos) with
            {
                AntiAliasing = AntiAliasingMode.Fxaa,
            }).ConfigureAwait(false);
        using JsonDocument fxaaJson = JsonDocument.Parse(
            await File.ReadAllBytesAsync(fxaa.ConfigPath).ConfigureAwait(false));
        JsonElement fxaaLanczosEffects = FindNamed(
            fxaaJson.RootElement.GetProperty("scalingModes"),
            MagpiePortableConfigService.LanczosModeName).GetProperty("effects");
        Assert.Equal(2, fxaaLanczosEffects.GetArrayLength());
        Assert.Equal("Lanczos", fxaaLanczosEffects[0].GetProperty("name").GetString());
        Assert.Equal(
            @"FXAA\FXAA_High",
            fxaaLanczosEffects[1].GetProperty("name").GetString());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.WriteAsync(
                fixture.CreateRequest(ScalingFilter.Fsr) with
                {
                    AntiAliasing = (AntiAliasingMode)999,
                })).ConfigureAwait(false);
    }

    private static async Task MagpieReadableScaleThresholdAsync()
    {
        await using TempDirectory temp = new();
        MagpieFixture fixture = await MagpieFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        MagpiePortableConfigService service = new();

        async Task<JsonElement> WriteAndReadReadableModeAsync(
            double uiScaleFactor,
            double sharpness)
        {
            MagpiePortableConfigResult result = await service.WriteAsync(
                fixture.CreateRequest(ScalingFilter.Nis) with
                {
                    UiScaleFactor = uiScaleFactor,
                    RcasSharpness = sharpness,
                    AntiAliasing = AntiAliasingMode.Smaa,
                }).ConfigureAwait(false);
            using JsonDocument json = JsonDocument.Parse(
                await File.ReadAllBytesAsync(result.ConfigPath)
                    .ConfigureAwait(false));
            JsonElement modes = json.RootElement.GetProperty("scalingModes");
            Assert.Equal(
                MagpiePortableConfigService.ReadableModeName,
                modes[result.ScalingModeIndex].GetProperty("name").GetString());
            return FindNamed(
                modes,
                MagpiePortableConfigService.ReadableModeName).Clone();
        }

        foreach (double uiScaleFactor in new[] { 1.10, 1.199 })
        {
            JsonElement readableMode = await WriteAndReadReadableModeAsync(
                uiScaleFactor,
                sharpness: 0.42).ConfigureAwait(false);
            JsonElement effects = readableMode.GetProperty("effects");
            Assert.Equal(1, effects.GetArrayLength());
            JsonElement effect = effects[0];
            Assert.Equal("Lanczos", effect.GetProperty("name").GetString());
            Assert.Equal(1, effect.GetProperty("scalingType").GetInt32());
            Assert.NearlyEqual(
                1.0,
                effect.GetProperty("scale").GetProperty("x").GetDouble());
            Assert.NearlyEqual(
                1.0,
                effect.GetProperty("scale").GetProperty("y").GetDouble());
            JsonElement parameters = effect.GetProperty("parameters");
            Assert.NearlyEqual(
                MagpiePortableConfigService.ReadableLanczosAntiRinging,
                parameters.GetProperty("ARStrength").GetDouble());
            Assert.NearlyEqual(
                0.75,
                parameters.GetProperty("ARStrength").GetDouble());
            Assert.False(parameters.TryGetProperty("sharpness", out _));

            string[] effectNames = effects
                .EnumerateArray()
                .Select(item => item.GetProperty("name").GetString() ?? string.Empty)
                .ToArray();
            Assert.False(
                effectNames.Any(
                    name => name.Contains("NIS", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("RCAS", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("SMAA", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("FXAA", StringComparison.OrdinalIgnoreCase)));
        }

        const double RequestedSharpness = 0.27;
        JsonElement thresholdMode = await WriteAndReadReadableModeAsync(
            MagpiePortableConfigService.ReadableNisMinimumScale,
            RequestedSharpness).ConfigureAwait(false);
        JsonElement thresholdEffects = thresholdMode.GetProperty("effects");
        Assert.Equal(1, thresholdEffects.GetArrayLength());
        JsonElement thresholdEffect = thresholdEffects[0];
        Assert.Equal(
            @"NIS\NIS",
            thresholdEffect.GetProperty("name").GetString());
        Assert.NearlyEqual(
            RequestedSharpness,
            thresholdEffect
                .GetProperty("parameters")
                .GetProperty("sharpness")
                .GetDouble());
        Assert.False(
            thresholdEffect
                .GetProperty("parameters")
                .TryGetProperty("ARStrength", out _));
    }

    private static async Task MagpieStableRuntimeProvisioningAsync()
    {
        await using TempDirectory temp = new();
        string source = Path.Combine(temp.Path, "bundled");
        string runtimeRoot = Path.Combine(temp.Path, "runtime");
        Directory.CreateDirectory(source);

        foreach (string relativePath in MagpieRuntimeAssets.RequiredRelativePaths)
        {
            string assetPath = Path.Combine(source, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
            await File.WriteAllTextAsync(assetPath, relativePath)
                .ConfigureAwait(false);
        }

        foreach (string mutableDirectory in new[] { "cache", "config", "logs" })
        {
            string mutablePath = Path.Combine(source, mutableDirectory);
            Directory.CreateDirectory(mutablePath);
            await File.WriteAllTextAsync(
                Path.Combine(mutablePath, "stale.txt"),
                "must not be provisioned").ConfigureAwait(false);
        }

        const string runtimeKey = "app-1.0.4-magpie-0.12.1";
        string prepared = MagpieRuntimeProvisioner.Prepare(
            source,
            runtimeRoot,
            runtimeKey);
        Assert.Equal(Path.Combine(runtimeRoot, runtimeKey), prepared);
        Assert.True(MagpieRuntimeAssets.IsComplete(prepared));
        Assert.False(Directory.Exists(Path.Combine(prepared, "cache")));
        Assert.False(Directory.Exists(Path.Combine(prepared, "config")));
        Assert.False(Directory.Exists(Path.Combine(prepared, "logs")));

        string nisRelative = Path.Combine("effects", "NIS", "NIS.hlsl");
        File.Delete(Path.Combine(source, nisRelative));
        Assert.Equal(
            prepared,
            MagpieRuntimeProvisioner.Prepare(source, runtimeRoot, runtimeKey));
        Assert.True(File.Exists(Path.Combine(prepared, nisRelative)));

        string staleRuntimeConfig = Path.Combine(prepared, "config", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(staleRuntimeConfig)!);
        await File.WriteAllTextAsync(staleRuntimeConfig, "stale runtime config")
            .ConfigureAwait(false);
        File.Delete(Path.Combine(prepared, nisRelative));
        await File.WriteAllTextAsync(Path.Combine(source, nisRelative), nisRelative)
            .ConfigureAwait(false);

        string repaired = MagpieRuntimeProvisioner.Prepare(
            source,
            runtimeRoot,
            runtimeKey);
        Assert.Equal(prepared, repaired);
        Assert.True(MagpieRuntimeAssets.IsComplete(repaired));
        Assert.False(File.Exists(staleRuntimeConfig));
        Assert.Empty(
            Directory.EnumerateDirectories(
                runtimeRoot,
                $".{runtimeKey}.*",
                SearchOption.TopDirectoryOnly));

        InvalidDataException missingAsset = Assert.Throws<InvalidDataException>(
            () => MagpieRuntimeAssets.EnsureComplete(
                Path.Combine(temp.Path, "missing-runtime")));
        Assert.Contains("Missing required files", missingAsset.Message);
        Assert.Throws<ArgumentException>(
            () => MagpieRuntimeProvisioner.Prepare(source, runtimeRoot, ".."));
    }

    private static async Task MagpieConfigTransactionRollbackAsync()
    {
        await using TempDirectory temp = new();
        MagpieFixture fixture = await MagpieFixture.CreateAsync(temp.Path).ConfigureAwait(false);
        string configDirectory = Path.Combine(fixture.MagpieDirectory, "config");
        Directory.CreateDirectory(configDirectory);
        string configPath = Path.Combine(configDirectory, "config.json");
        byte[] original = Encoding.UTF8.GetBytes(
            "{\"language\":\"keep\",\"scalingModes\":[],\"profiles\":[]}");
        await File.WriteAllBytesAsync(configPath, original).ConfigureAwait(false);

        MagpiePortableConfigService service = new();
        MagpiePortableConfigTransaction transaction =
            await service.PrepareTransactionAsync(
                fixture.CreateRequest(ScalingFilter.Fsr)).ConfigureAwait(false);
        await service.ApplyTransactionAsync(transaction).ConfigureAwait(false);
        Assert.False(
            (await File.ReadAllBytesAsync(configPath).ConfigureAwait(false))
                .AsSpan()
                .SequenceEqual(original));

        byte[] postStartSave = Encoding.UTF8.GetBytes(
            "{\"postStartWindowPlacement\":true}");
        await File.WriteAllBytesAsync(configPath, postStartSave).ConfigureAwait(false);
        MagpiePortableConfigRollbackResult recovered =
            await service.RollbackTransactionAsync(
                transaction,
                allowChangedContentAfterEngineExit: true).ConfigureAwait(false);
        Assert.True(recovered.Restored);
        Assert.Contains("preserved", recovered.Issue ?? string.Empty);
        Assert.SequenceEqual(
            original,
            await File.ReadAllBytesAsync(configPath).ConfigureAwait(false));
        string[] recoveryCopies = Directory.GetFiles(
            configDirectory,
            "config.json.spinfourkayyy-recovery-*",
            SearchOption.TopDirectoryOnly);
        Assert.Equal(1, recoveryCopies.Length);
        Assert.SequenceEqual(
            postStartSave,
            await File.ReadAllBytesAsync(recoveryCopies[0]).ConfigureAwait(false));

        MagpiePortableConfigTransaction conflict =
            await service.PrepareTransactionAsync(
                fixture.CreateRequest(ScalingFilter.Lanczos)).ConfigureAwait(false);
        await service.ApplyTransactionAsync(conflict).ConfigureAwait(false);
        byte[] newer = Encoding.UTF8.GetBytes("{\"newerWriter\":true}");
        await File.WriteAllBytesAsync(configPath, newer).ConfigureAwait(false);
        MagpiePortableConfigRollbackResult withheld =
            await service.RollbackTransactionAsync(conflict).ConfigureAwait(false);
        Assert.False(withheld.Restored);
        Assert.SequenceEqual(
            newer,
            await File.ReadAllBytesAsync(configPath).ConfigureAwait(false));

        string absentRoot = Path.Combine(temp.Path, "absent");
        MagpieFixture absentFixture = await MagpieFixture.CreateAsync(absentRoot)
            .ConfigureAwait(false);
        MagpiePortableConfigTransaction absent =
            await service.PrepareTransactionAsync(
                absentFixture.CreateRequest(ScalingFilter.Fsr)).ConfigureAwait(false);
        await service.ApplyTransactionAsync(absent).ConfigureAwait(false);
        Assert.True(File.Exists(absent.ConfigPath));
        MagpiePortableConfigRollbackResult removed =
            await service.RollbackTransactionAsync(absent).ConfigureAwait(false);
        Assert.True(removed.Restored);
        Assert.False(File.Exists(absent.ConfigPath));
    }

    private static async Task MagpieErrorPathsAsync()
    {
        await using TempDirectory temp = new();
        MagpiePortableConfigService service = new();
        MagpieFixture fixture = await MagpieFixture.CreateAsync(temp.Path).ConfigureAwait(false);

        string missingMagpie = Path.Combine(temp.Path, "missing-magpie");
        Directory.CreateDirectory(missingMagpie);
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.WriteAsync(fixture.CreateRequest(ScalingFilter.Fsr) with
            {
                MagpieDirectory = missingMagpie,
            })).ConfigureAwait(false);
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.WriteAsync(fixture.CreateRequest(ScalingFilter.Fsr) with
            {
                SourceExecutablePath = Path.Combine(temp.Path, "missing-game.exe"),
            })).ConfigureAwait(false);
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.WriteAsync(fixture.CreateRequest(ScalingFilter.Fsr) with
            {
                SourceWindowClass = " ",
            })).ConfigureAwait(false);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.WriteAsync(fixture.CreateRequest(ScalingFilter.Fsr) with
            {
                RcasSharpness = 1.01,
            })).ConfigureAwait(false);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.WriteAsync(fixture.CreateRequest(ScalingFilter.Fsr) with
            {
                RcasSharpness = -0.01,
            })).ConfigureAwait(false);
        foreach (double invalidUiScaleFactor in new[] { 0.999, 4.001, double.NaN })
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => service.WriteAsync(
                    fixture.CreateRequest(ScalingFilter.Nis) with
                    {
                        UiScaleFactor = invalidUiScaleFactor,
                    })).ConfigureAwait(false);
        }
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.WriteAsync(fixture.CreateRequest(ScalingFilter.Fsr) with
            {
                MaximumFrameRate = 9,
            })).ConfigureAwait(false);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.WriteAsync(fixture.CreateRequest((ScalingFilter)999)))
            .ConfigureAwait(false);

        string configDirectory = Path.Combine(fixture.MagpieDirectory, "config");
        Directory.CreateDirectory(configDirectory);
        string configPath = Path.Combine(configDirectory, "config.json");
        await File.WriteAllTextAsync(configPath, "[]").ConfigureAwait(false);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.WriteAsync(fixture.CreateRequest(ScalingFilter.Fsr)))
            .ConfigureAwait(false);

        await File.WriteAllTextAsync(
            configPath,
            """{"scalingModes":{},"profiles":[]}""").ConfigureAwait(false);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.WriteAsync(fixture.CreateRequest(ScalingFilter.Fsr)))
            .ConfigureAwait(false);
    }

    private static void CoordinateMappingGuard()
    {
        CoordinateMappingValidator validator = new();
        PixelRect source = new(0, 0, 1920, 1080);
        PixelRect exactDestination = new(0, 0, 3840, 2160);
        CoordinateMappingValidationResult exact = validator.Validate(
            source,
            exactDestination);
        Assert.True(exact.IsSafe);
        Assert.True(exact.MaximumRoundTripError <= 1.0);
        Assert.NearlyEqual(0, exact.RelativeAspectRatioError);
        Assert.Equal(5, exact.Samples.Count);

        CoordinateMappingValidationResult fractional = validator.Validate(
            new PixelRect(100, 50, 2560, 1440),
            new PixelRect(0, 0, 3840, 2160));
        Assert.True(fractional.IsSafe);
        Assert.True(fractional.MaximumRoundTripError <= 1.0);

        PixelPoint destination = CoordinateMappingValidator.SourceToDestination(
            new PixelPoint(100, 50),
            new PixelRect(100, 50, 100, 100),
            new PixelRect(300, 200, 200, 200));
        Assert.Equal(new PixelPoint(300, 200), destination);
        Assert.Equal(
            new PixelPoint(100, 50),
            CoordinateMappingValidator.DestinationToSource(
                destination,
                new PixelRect(100, 50, 100, 100),
                new PixelRect(300, 200, 200, 200)));

        PixelRect endpointSource = new(100, 50, 1920, 1080);
        PixelRect endpointDestination = new(-200, 75, 3840, 2160);
        Assert.Equal(
            new PixelPoint(
                endpointDestination.X + endpointDestination.Width - 1,
                endpointDestination.Y + endpointDestination.Height - 1),
            CoordinateMappingValidator.SourceToDestination(
                new PixelPoint(
                    endpointSource.X + endpointSource.Width - 1,
                    endpointSource.Y + endpointSource.Height - 1),
                endpointSource,
                endpointDestination));
        Assert.Equal(
            new PixelPoint(
                endpointSource.X + endpointSource.Width - 1,
                endpointSource.Y + endpointSource.Height - 1),
            CoordinateMappingValidator.DestinationToSource(
                new PixelPoint(
                    endpointDestination.X + endpointDestination.Width - 1,
                    endpointDestination.Y + endpointDestination.Height - 1),
                endpointSource,
                endpointDestination));

        const int InteriorSourceOffset = 123;
        double expectedDestinationX = endpointDestination.X + Math.Round(
            (double)InteriorSourceOffset
                * (endpointDestination.Width - 1)
                / (endpointSource.Width - 1),
            MidpointRounding.AwayFromZero);
        PixelPoint mappedInterior = CoordinateMappingValidator.SourceToDestination(
            new PixelPoint(endpointSource.X + InteriorSourceOffset, endpointSource.Y),
            endpointSource,
            endpointDestination);
        Assert.NearlyEqual(expectedDestinationX, mappedInterior.X, tolerance: 0);
        Assert.NearlyEqual(endpointDestination.Y, mappedInterior.Y, tolerance: 0);
        Assert.Equal(
            new PixelPoint(
                endpointDestination.X + endpointDestination.Width,
                endpointDestination.Y + endpointDestination.Height),
            CoordinateMappingValidator.SourceToDestination(
                new PixelPoint(
                    endpointSource.X + endpointSource.Width,
                    endpointSource.Y + endpointSource.Height),
                endpointSource,
                endpointDestination));
        Assert.Equal(
            new PixelPoint(endpointDestination.X - 1, endpointDestination.Y - 1),
            CoordinateMappingValidator.SourceToDestination(
                new PixelPoint(endpointSource.X - 1, endpointSource.Y - 1),
                endpointSource,
                endpointDestination));

        CoordinateMappingValidationResult stretched = validator.Validate(
            source,
            new PixelRect(0, 0, 3840, 2000));
        Assert.False(stretched.IsSafe);
        Assert.True(stretched.RelativeAspectRatioError > 0.005);
        Assert.True(stretched.Errors.Any(
            error => error.Contains("aspect ratio", StringComparison.OrdinalIgnoreCase)));

        CoordinateMappingValidationResult empty = validator.Validate(
            new PixelRect(0, 0, 0, 1080),
            exactDestination);
        Assert.False(empty.IsSafe);
        Assert.True(double.IsPositiveInfinity(empty.MaximumRoundTripError));
        Assert.Empty(empty.Samples);
        Assert.False(
            validator.Validate(
                new PixelRect(0, 0, 1, 1080),
                exactDestination).IsSafe);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => validator.Validate(source, exactDestination, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => validator.Validate(source, exactDestination, 1, -0.01));
        Assert.Throws<ArgumentException>(
            () => CoordinateMappingValidator.SourceToDestination(
                new PixelPoint(0, 0),
                new PixelRect(0, 0, 0, 0),
                exactDestination));
    }

    private static async Task ScalingWindowInspectionAsync()
    {
        MagpieScalingWindowInspector inspector = new();
        MagpieScalingWindowInspection inspection = inspector.Inspect(nint.MaxValue);

        Assert.False(inspection.IsSafeForInput);
        Assert.True(inspection.Issues.Count > 0);
        if (inspection.IsActive)
        {
            Assert.True(inspection.ScalingWindowHandle != nint.Zero);
        }
        else
        {
            Assert.Equal(nint.Zero, inspection.ScalingWindowHandle);
            Assert.False(inspection.IsVisible);
        }

        await Assert.ThrowsAsync<ArgumentException>(
            () => inspector.WaitForSafeAttachmentAsync(
                nint.Zero,
                TimeSpan.FromMilliseconds(20))).ConfigureAwait(false);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => inspector.WaitForSafeAttachmentAsync(
                nint.MaxValue,
                TimeSpan.Zero)).ConfigureAwait(false);

        MagpieScalingWindowInspection timed = await inspector.WaitForSafeAttachmentAsync(
            nint.MaxValue,
            TimeSpan.FromMilliseconds(20)).ConfigureAwait(false);
        Assert.False(timed.IsSafeForInput);
        Assert.True(timed.Issues.Count > 0);
    }

    private static void AltTabBackgroundSuspensionIsUnbounded()
    {
        DateTimeOffset startedAt =
            new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);
        TimeSpan grace = TimeSpan.FromSeconds(5);

        ScalingSessionSupervisionDecision suspended =
            ScalingSessionSupervisionPolicy.Evaluate(
                ScalingSessionSupervisionState.Active,
                CreateSupervisionObservation(
                    startedAt,
                    sourceIsForeground: false,
                    ScalingOutputValidation.Inactive),
                grace);
        Assert.Equal(ScalingSupervisionAction.Continue, suspended.Action);
        Assert.Equal(ScalingSupervisionStopReason.None, suspended.StopReason);
        Assert.Equal(ScalingSessionMode.Suspended, suspended.State.Mode);
        Assert.Null(suspended.State.ResumeDeadlineUtc);

        ScalingSessionSupervisionDecision stillSuspended =
            ScalingSessionSupervisionPolicy.Evaluate(
                suspended.State,
                CreateSupervisionObservation(
                    startedAt.AddDays(365),
                    sourceIsForeground: false,
                    ScalingOutputValidation.Pending),
                grace);
        Assert.Equal(ScalingSupervisionAction.Continue, stillSuspended.Action);
        Assert.Equal(ScalingSessionMode.Suspended, stillSuspended.State.Mode);
        Assert.Null(stillSuspended.State.ResumeDeadlineUtc);

        ScalingSessionSupervisionDecision outputStillSafe =
            ScalingSessionSupervisionPolicy.Evaluate(
                stillSuspended.State,
                CreateSupervisionObservation(
                    startedAt.AddDays(365).AddMilliseconds(1),
                    sourceIsForeground: false,
                    ScalingOutputValidation.ExactSafe),
                grace);
        Assert.Equal(ScalingSupervisionAction.Continue, outputStillSafe.Action);
        Assert.Equal(ScalingSessionMode.Suspended, outputStillSafe.State.Mode);
        Assert.Null(outputStillSafe.State.ResumeDeadlineUtc);
    }

    private static void AltTabForegroundResumeAndExactAcceptance()
    {
        DateTimeOffset returnedAt =
            new(2026, 7, 29, 13, 0, 0, TimeSpan.Zero);
        TimeSpan grace = TimeSpan.FromSeconds(5);

        ScalingSessionSupervisionDecision resuming =
            ScalingSessionSupervisionPolicy.Evaluate(
                ScalingSessionSupervisionState.Suspended,
                CreateSupervisionObservation(
                    returnedAt,
                    sourceIsForeground: true,
                    ScalingOutputValidation.Inactive),
                grace);
        Assert.Equal(ScalingSupervisionAction.Continue, resuming.Action);
        Assert.Equal(ScalingSessionMode.Resuming, resuming.State.Mode);
        Assert.Equal(returnedAt.Add(grace), resuming.State.ResumeDeadlineUtc);

        ScalingSessionSupervisionDecision initializing =
            ScalingSessionSupervisionPolicy.Evaluate(
                resuming.State,
                CreateSupervisionObservation(
                    returnedAt.AddSeconds(2),
                    sourceIsForeground: true,
                    ScalingOutputValidation.Pending),
                grace);
        Assert.Equal(ScalingSupervisionAction.Continue, initializing.Action);
        Assert.Equal(resuming.State, initializing.State);

        ScalingSessionSupervisionDecision interrupted =
            ScalingSessionSupervisionPolicy.Evaluate(
                initializing.State,
                CreateSupervisionObservation(
                    returnedAt.AddSeconds(3),
                    sourceIsForeground: false,
                    ScalingOutputValidation.Inactive),
                grace);
        Assert.Equal(ScalingSessionMode.Suspended, interrupted.State.Mode);
        Assert.Null(interrupted.State.ResumeDeadlineUtc);

        DateTimeOffset returnedAgainAt = returnedAt.AddMinutes(30);
        ScalingSessionSupervisionDecision resumingAgain =
            ScalingSessionSupervisionPolicy.Evaluate(
                interrupted.State,
                CreateSupervisionObservation(
                    returnedAgainAt,
                    sourceIsForeground: true,
                    ScalingOutputValidation.Pending),
                grace);
        Assert.Equal(ScalingSessionMode.Resuming, resumingAgain.State.Mode);
        Assert.Equal(
            returnedAgainAt.Add(grace),
            resumingAgain.State.ResumeDeadlineUtc);

        ScalingSessionSupervisionDecision resumed =
            ScalingSessionSupervisionPolicy.Evaluate(
                resumingAgain.State,
                CreateSupervisionObservation(
                    returnedAgainAt.AddSeconds(2),
                    sourceIsForeground: true,
                    ScalingOutputValidation.ExactSafe),
                grace);
        Assert.Equal(ScalingSupervisionAction.Continue, resumed.Action);
        Assert.Equal(ScalingSupervisionStopReason.None, resumed.StopReason);
        Assert.Equal(ScalingSessionSupervisionState.Active, resumed.State);
    }

    private static void AltTabEngineOwnedResumeSendsNoToggle()
    {
        DateTimeOffset returnedAt =
            new(2026, 7, 29, 13, 30, 0, TimeSpan.Zero);
        TimeSpan grace = TimeSpan.FromSeconds(5);

        ScalingSessionSupervisionDecision firstForegroundPoll =
            ScalingSessionSupervisionPolicy.Evaluate(
                ScalingSessionSupervisionState.Suspended,
                CreateSupervisionObservation(
                    returnedAt,
                    sourceIsForeground: true,
                    ScalingOutputValidation.Inactive),
                grace);
        Assert.Equal(ScalingSupervisionAction.Continue, firstForegroundPoll.Action);
        Assert.Equal(
            ScalingSessionMode.Resuming,
            firstForegroundPoll.State.Mode);

        ScalingSessionSupervisionDecision waitingForEngine =
            ScalingSessionSupervisionPolicy.Evaluate(
                firstForegroundPoll.State,
                CreateSupervisionObservation(
                    returnedAt.AddMilliseconds(100),
                    sourceIsForeground: true,
                    ScalingOutputValidation.Inactive),
                grace);
        Assert.Equal(ScalingSupervisionAction.Continue, waitingForEngine.Action);
        Assert.Equal(firstForegroundPoll.State, waitingForEngine.State);

        ScalingSessionSupervisionDecision missedBackgroundPoll =
            ScalingSessionSupervisionPolicy.Evaluate(
                ScalingSessionSupervisionState.Active,
                CreateSupervisionObservation(
                    returnedAt.AddMilliseconds(200),
                    sourceIsForeground: true,
                    ScalingOutputValidation.Inactive),
                grace);
        Assert.Equal(ScalingSupervisionAction.Continue, missedBackgroundPoll.Action);
        Assert.Equal(
            ScalingSessionMode.Resuming,
            missedBackgroundPoll.State.Mode);

        ScalingSessionSupervisionDecision alreadyStarting =
            ScalingSessionSupervisionPolicy.Evaluate(
                ScalingSessionSupervisionState.Suspended,
                CreateSupervisionObservation(
                    returnedAt.AddSeconds(1),
                    sourceIsForeground: true,
                    ScalingOutputValidation.Pending),
                grace);
        Assert.Equal(ScalingSupervisionAction.Continue, alreadyStarting.Action);

        ScalingSessionSupervisionDecision alreadyActive =
            ScalingSessionSupervisionPolicy.Evaluate(
                ScalingSessionSupervisionState.Suspended,
                CreateSupervisionObservation(
                    returnedAt.AddSeconds(2),
                    sourceIsForeground: true,
                    ScalingOutputValidation.ExactSafe),
                grace);
        Assert.Equal(ScalingSupervisionAction.Continue, alreadyActive.Action);
        Assert.Equal(ScalingSessionSupervisionState.Active, alreadyActive.State);
    }

    private static void AltTabResumeTimeoutStopsOwnedSession()
    {
        DateTimeOffset returnedAt =
            new(2026, 7, 29, 14, 0, 0, TimeSpan.Zero);
        TimeSpan grace = TimeSpan.FromSeconds(4);
        ScalingSessionSupervisionState resuming =
            ScalingSessionSupervisionState.ResumingUntil(
                returnedAt.Add(grace));

        ScalingSessionSupervisionDecision beforeDeadline =
            ScalingSessionSupervisionPolicy.Evaluate(
                resuming,
                CreateSupervisionObservation(
                    returnedAt.Add(grace).AddTicks(-1),
                    sourceIsForeground: true,
                    ScalingOutputValidation.Pending),
                grace);
        Assert.Equal(ScalingSupervisionAction.Continue, beforeDeadline.Action);
        Assert.Equal(resuming, beforeDeadline.State);

        ScalingSessionSupervisionDecision expired =
            ScalingSessionSupervisionPolicy.Evaluate(
                resuming,
                CreateSupervisionObservation(
                    returnedAt.Add(grace),
                    sourceIsForeground: true,
                    ScalingOutputValidation.Inactive),
                grace);
        Assert.Equal(
            ScalingSupervisionAction.StopOwnedSession,
            expired.Action);
        Assert.Equal(
            ScalingSupervisionStopReason.ResumeGraceExpired,
            expired.StopReason);
        Assert.Equal(resuming, expired.State);

        ScalingSessionSupervisionDecision exactAtNextObservation =
            ScalingSessionSupervisionPolicy.Evaluate(
                resuming,
                CreateSupervisionObservation(
                    returnedAt.AddMinutes(1),
                    sourceIsForeground: true,
                    ScalingOutputValidation.ExactSafe),
                grace);
        Assert.Equal(
            ScalingSupervisionAction.Continue,
            exactAtNextObservation.Action);
        Assert.Equal(
            ScalingSessionSupervisionState.Active,
            exactAtNextObservation.State);
    }

    private static void AltTabHealthFailuresStopImmediately()
    {
        DateTimeOffset observedAt =
            new(2026, 7, 29, 15, 0, 0, TimeSpan.Zero);
        ScalingSessionSupervisionState suspended =
            ScalingSessionSupervisionState.Suspended;
        var failures = new[]
        {
            (
                Observation: CreateSupervisionObservation(
                    observedAt,
                    sourceIsForeground: false,
                    ScalingOutputValidation.Inactive,
                    infrastructureHealthy: false),
                Reason: ScalingSupervisionStopReason.InfrastructureFailure),
            (
                Observation: CreateSupervisionObservation(
                    observedAt,
                    sourceIsForeground: false,
                    ScalingOutputValidation.Inactive,
                    nativeUiScaleSafe: false),
                Reason: ScalingSupervisionStopReason.NativeUiScaleFailure),
            (
                Observation: CreateSupervisionObservation(
                    observedAt,
                    sourceIsForeground: false,
                    ScalingOutputValidation.Inactive,
                    sourceHealthy: false),
                Reason: ScalingSupervisionStopReason.SourceFailure),
            (
                Observation: CreateSupervisionObservation(
                    observedAt,
                    sourceIsForeground: false,
                    ScalingOutputValidation.Unsafe),
                Reason: ScalingSupervisionStopReason.UnsafeOutput),
        };

        foreach (var failure in failures)
        {
            ScalingSessionSupervisionDecision decision =
                ScalingSessionSupervisionPolicy.Evaluate(
                    suspended,
                    failure.Observation,
                    TimeSpan.FromSeconds(5));
            Assert.Equal(
                ScalingSupervisionAction.StopOwnedSession,
                decision.Action);
            Assert.Equal(failure.Reason, decision.StopReason);
            Assert.Equal(suspended, decision.State);
        }

        ScalingSessionSupervisionDecision foregroundInvariantLoss =
            ScalingSessionSupervisionPolicy.Evaluate(
                ScalingSessionSupervisionState.Active,
                CreateSupervisionObservation(
                    observedAt,
                    sourceIsForeground: true,
                    ScalingOutputValidation.Pending),
                TimeSpan.FromSeconds(5));
        Assert.Equal(
            ScalingSupervisionAction.StopOwnedSession,
            foregroundInvariantLoss.Action);
        Assert.Equal(
            ScalingSupervisionStopReason.UnsafeOutput,
            foregroundInvariantLoss.StopReason);
    }

    private static void AltTabInvalidPolicyInputs()
    {
        DateTimeOffset observedAt =
            new(2026, 7, 29, 16, 0, 0, TimeSpan.Zero);
        ScalingSessionObservation observation =
            CreateSupervisionObservation(
                observedAt,
                sourceIsForeground: true,
                ScalingOutputValidation.Inactive);

        Assert.Throws<ArgumentNullException>(
            () => ScalingSessionSupervisionPolicy.Evaluate(
                null!,
                observation,
                TimeSpan.FromSeconds(5)));
        Assert.Throws<ArgumentNullException>(
            () => ScalingSessionSupervisionPolicy.Evaluate(
                ScalingSessionSupervisionState.Active,
                null!,
                TimeSpan.FromSeconds(5)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ScalingSessionSupervisionPolicy.Evaluate(
                ScalingSessionSupervisionState.Active,
                observation,
                TimeSpan.Zero));
        Assert.Throws<ArgumentException>(
            () => ScalingSessionSupervisionPolicy.Evaluate(
                new ScalingSessionSupervisionState(
                    ScalingSessionMode.Resuming),
                observation,
                TimeSpan.FromSeconds(5)));
        Assert.Throws<ArgumentException>(
            () => ScalingSessionSupervisionPolicy.Evaluate(
                new ScalingSessionSupervisionState(
                    ScalingSessionMode.Active,
                    observedAt.AddSeconds(5)),
                observation,
                TimeSpan.FromSeconds(5)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ScalingSessionSupervisionPolicy.Evaluate(
                new ScalingSessionSupervisionState(
                    (ScalingSessionMode)int.MaxValue),
                observation,
                TimeSpan.FromSeconds(5)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ScalingSessionSupervisionPolicy.Evaluate(
                ScalingSessionSupervisionState.Active,
                observation with
                {
                    OutputValidation =
                        (ScalingOutputValidation)int.MaxValue,
                },
                TimeSpan.FromSeconds(5)));
    }

    private static ScalingSessionObservation CreateSupervisionObservation(
        DateTimeOffset observedAtUtc,
        bool sourceIsForeground,
        ScalingOutputValidation outputValidation,
        bool infrastructureHealthy = true,
        bool nativeUiScaleSafe = true,
        bool sourceHealthy = true) =>
        new(
            observedAtUtc,
            infrastructureHealthy,
            nativeUiScaleSafe,
            sourceHealthy,
            sourceIsForeground,
            outputValidation);

    private static async Task ProcessDiscoveryValidationAsync()
    {
        await using TempDirectory temp = new();
        ProcessDiscoveryService service = new();
        string nonexistent = Path.Combine(temp.Path, $"{Guid.NewGuid():N}.exe");

        Assert.Throws<ArgumentException>(() => service.FindByExecutableName(" "));
        Assert.Throws<ArgumentException>(
            () => service.FindByExecutableName(Path.Combine("folder", "eqgame.exe")));
        Assert.Empty(
            service.FindByExecutableName($"{Guid.NewGuid():N}.exe"));
        Assert.Throws<ArgumentException>(() => service.FindByExecutablePath(" "));
        Assert.Empty(service.FindByExecutablePath(nonexistent));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.WaitForExecutableAsync(nonexistent, TimeSpan.Zero))
            .ConfigureAwait(false);
        await Assert.ThrowsAsync<TimeoutException>(
            () => service.WaitForExecutableAsync(
                nonexistent,
                TimeSpan.FromMilliseconds(20))).ConfigureAwait(false);
    }

    private static void PhysicalMonitorClientPlacementPlanner()
    {
        MonitorDescriptor ultrawide = new(
            new nint(42),
            new PixelRect(0, 0, 3440, 1440),
            new PixelRect(0, 0, 3440, 1400),
            IsPrimary: true);
        WindowFrameExtents frame = new(
            Left: 8,
            Top: 31,
            Right: 8,
            Bottom: 8);

        WindowPlacementPlan native = WindowPlacementPlanner.Create(
            new PixelSize(3440, 1440),
            ultrawide,
            frame);
        Assert.Equal(
            new PixelRect(-8, -31, 3456, 1479),
            native.OuterBounds);
        Assert.Equal(
            ultrawide.Bounds,
            native.ExpectedClientBounds);
        Assert.False(native.UsesWorkArea);
        Assert.True(native.FrameExtendsBeyondMonitor);

        WindowPlacementPlan oneOhFive = WindowPlacementPlanner.Create(
            new PixelSize(3276, 1372),
            ultrawide,
            frame);
        Assert.Equal(
            new PixelRect(74, 14, 3292, 1411),
            oneOhFive.OuterBounds);
        Assert.Equal(
            new PixelRect(82, 45, 3276, 1372),
            oneOhFive.ExpectedClientBounds);
        Assert.False(oneOhFive.UsesWorkArea);
        Assert.False(oneOhFive.FrameExtendsBeyondMonitor);

        WindowPlacementPlan gentle = WindowPlacementPlanner.Create(
            new PixelSize(2752, 1152),
            ultrawide,
            frame);
        Assert.Equal(
            new PixelRect(336, 104, 2768, 1191),
            gentle.OuterBounds);
        Assert.Equal(
            new PixelRect(344, 135, 2752, 1152),
            gentle.ExpectedClientBounds);
        Assert.True(gentle.UsesWorkArea);
        Assert.False(gentle.FrameExtendsBeyondMonitor);

        Assert.True(
            WindowPlacementPlanner.CanPreserveCurrentClientArea(
                new PixelRect(0, 31, 2752, 1152),
                new PixelSize(2752, 1152),
                ultrawide));
        Assert.False(
            WindowPlacementPlanner.CanPreserveCurrentClientArea(
                new PixelRect(0, 31, 2750, 1152),
                new PixelSize(2752, 1152),
                ultrawide));
        Assert.False(
            WindowPlacementPlanner.CanPreserveCurrentClientArea(
                new PixelRect(1000, 31, 2752, 1152),
                new PixelSize(2752, 1152),
                ultrawide));

        MonitorDescriptor leftMonitor = ultrawide with
        {
            Handle = new nint(43),
            Bounds = new PixelRect(-3440, 0, 3440, 1440),
            WorkArea = new PixelRect(-3440, 0, 3440, 1400),
            IsPrimary = false,
        };
        WindowPlacementPlan leftNative = WindowPlacementPlanner.Create(
            new PixelSize(3440, 1440),
            leftMonitor,
            frame);
        Assert.Equal(
            new PixelRect(-3448, -31, 3456, 1479),
            leftNative.OuterBounds);
        Assert.Equal(
            leftMonitor.Bounds,
            leftNative.ExpectedClientBounds);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => WindowPlacementPlanner.Create(
                new PixelSize(3442, 1440),
                ultrawide,
                frame));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => WindowPlacementPlanner.Create(
                new PixelSize(3276, 1372),
                ultrawide,
                frame with { Top = -1 }));
    }

    private static void OverlayPlacementAndFiltering()
    {
        PixelRect target = new(0, 0, 3840, 2160);
        PixelRect source = new(320, 180, 3200, 1800);
        PixelRect rightBottom = new(3200, 1880, 300, 100);
        PixelRect mappedRightBottom = OverlayPlacementPlanner.MapNativeSize(
            source,
            target,
            rightBottom);
        Assert.Equal(
            new PixelRect(3516, 2060, 300, 100),
            mappedRightBottom);
        Assert.Equal(rightBottom.Width, mappedRightBottom.Width);
        Assert.Equal(rightBottom.Height, mappedRightBottom.Height);

        Assert.Equal(
            new PixelRect(24, 24, 300, 100),
            OverlayPlacementPlanner.MapNativeSize(
                source,
                target,
                new PixelRect(340, 200, 300, 100)));
        Assert.Equal(
            new PixelRect(1770, 1030, 300, 100),
            OverlayPlacementPlanner.MapNativeSize(
                source,
                target,
                new PixelRect(1770, 1030, 300, 100)));
        Assert.Equal(
            new PixelRect(0, 0, 500, 200),
            OverlayPlacementPlanner.MapNativeSize(
                source,
                target,
                new PixelRect(-200, -100, 500, 200)));

        Assert.True(OverlayPlacementPlanner.ShouldMapPosition(source, rightBottom));
        Assert.False(
            OverlayPlacementPlanner.ShouldMapPosition(
                source,
                new PixelRect(0, 0, 100, 100)));

        OverlayWindowSnapshot loremaster = CreateOverlaySnapshot(
            new nint(101),
            processId: 2001,
            rightBottom,
            executablePath: @"C:\Users\Player\SpinsLoremaster\Loremaster.exe");
        HashSet<int> excluded = [9001, 9002];
        Assert.True(
            OverlayPlacementPlanner.IsEligible(
                loremaster,
                target,
                excluded));
        Assert.False(
            OverlayPlacementPlanner.IsEligible(
                loremaster with { IsTopmost = false },
                target,
                excluded));
        Assert.True(
            OverlayPlacementPlanner.IsEligible(
                loremaster with { IsTopmost = false },
                target,
                excluded,
                allowTemporaryNonTopmost: true));
        Assert.True(OverlayPlacementPlanner.IsRecognizedCompanion(loremaster));
        Assert.False(
            OverlayPlacementPlanner.IsRecognizedCompanion(
                loremaster with
                {
                    Title = "Notes",
                    ExecutablePath = @"C:\Tools\Utility.exe",
                }));

        OverlayWindowSnapshot eqLegendsOverlay = CreateOverlaySnapshot(
            new nint(111),
            processId: 2011,
            new PixelRect(120, 120, 600, 300),
            executablePath:
                @"C:\Users\Player\AppData\Local\Programs\everquest-companion\EQ Legends Companion.exe")
            with
            {
                ClassName = "Chrome_WidgetWin_1",
                Title = "Fight Overlay",
                IsTopmost = false,
                IsLayered = false,
            };
        Assert.True(
            OverlayPlacementPlanner.IsEqLegendsCompanionOverlay(
                eqLegendsOverlay));
        Assert.True(
            OverlayPlacementPlanner.IsRecognizedCompanion(eqLegendsOverlay));
        Assert.True(
            OverlayPlacementPlanner.IsEligible(
                eqLegendsOverlay,
                new PixelRect(0, 0, 1280, 720),
                excluded,
                allowTemporaryNonTopmost: true));
        Assert.True(
            OverlayPlacementPlanner.IsEqLegendsCompanionOverlay(
                eqLegendsOverlay with
                {
                    ExecutablePath = @"C:\Tools\EVERQUEST-COMPANION.EXE",
                }));
        Assert.True(
            OverlayPlacementPlanner.IsEqLegendsCompanionOverlay(
                eqLegendsOverlay with
                {
                    ExecutablePath = @"C:\Tools\eq-tools.exe",
                }));
        Assert.False(
            OverlayPlacementPlanner.IsEqLegendsCompanionOverlay(
                eqLegendsOverlay with { Title = "Cursor Ring" }));
        Assert.False(
            OverlayPlacementPlanner.IsEqLegendsCompanionOverlay(
                eqLegendsOverlay with { Title = "Companion Settings" }));
        Assert.False(
            OverlayPlacementPlanner.IsEqLegendsCompanionOverlay(
                eqLegendsOverlay with { ExecutablePath = null }));
        Assert.False(
            OverlayPlacementPlanner.IsEligible(
                eqLegendsOverlay with
                {
                    Bounds = new PixelRect(120, 0, 720, 820),
                },
                new PixelRect(0, 0, 1280, 720),
                excluded,
                allowTemporaryNonTopmost: true));

        OverlayWindowSnapshot eqLegendsMain = eqLegendsOverlay with
        {
            Handle = new nint(112),
            Bounds = new PixelRect(40, 40, 1280, 860),
            Title = "EQ Legends Companion",
            IsToolWindow = false,
        };
        Assert.False(
            OverlayPlacementPlanner.IsEqLegendsCompanionOverlay(eqLegendsMain));
        Assert.False(
            OverlayPlacementPlanner.IsRecognizedCompanion(eqLegendsMain));
        Assert.False(
            OverlayPlacementPlanner.IsEligible(
                eqLegendsMain with { IsTopmost = true },
                target,
                excluded));
        OverlayWindowSnapshot ordinaryTopmostTool = eqLegendsOverlay with
        {
            ExecutablePath = @"C:\Tools\Utility.exe",
            IsTopmost = true,
        };
        Assert.False(
            OverlayPlacementPlanner.IsEqLegendsCompanionOverlay(
                ordinaryTopmostTool));
        Assert.False(
            OverlayPlacementPlanner.IsRecognizedCompanion(
                ordinaryTopmostTool));
        Assert.True(
            OverlayPlacementPlanner.IsEligible(
                ordinaryTopmostTool,
                new PixelRect(0, 0, 1280, 720),
                excluded));
        Assert.False(
            OverlayPlacementPlanner.IsEligible(
                loremaster with { ProcessId = 9001 },
                target,
                excluded));
        Assert.False(
            OverlayPlacementPlanner.IsEligible(
                loremaster with { ClassName = "Shell_TrayWnd" },
                target,
                excluded));
        Assert.False(
            OverlayPlacementPlanner.IsEligible(
                loremaster with
                {
                    ExecutablePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                        "System32",
                        "system-overlay.exe"),
                },
                target,
                excluded));
        Assert.False(
            OverlayPlacementPlanner.IsEligible(
                loremaster with { Bounds = new PixelRect(0, 0, 3840, 2160) },
                target,
                excluded));
        Assert.False(
            OverlayPlacementPlanner.IsEligible(
                loremaster with { Bounds = new PixelRect(5000, 0, 200, 100) },
                target,
                excluded));
        Assert.False(
            OverlayPlacementPlanner.IsEligible(
                loremaster,
                target,
                excluded,
                scalingWindowHandle: loremaster.Handle));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => OverlayPlacementPlanner.MapNativeSize(
                new PixelRect(0, 0, 0, 100),
                target,
                rightBottom));
    }

    private static void Overlay125PercentSourceShadowMapping()
    {
        PixelRect target = new(0, 0, 3840, 2160);
        PixelRect source = new(384, 216, 3072, 1728);
        nint scalingWindow = new(9002);
        nint sourceWindow = new(9001);

        Assert.Equal(
            new WindowPlacementPoint(0, 0),
            OverlayPlacementPlanner.ResolveEffectivePointer(
                new WindowPlacementPoint(source.X, source.Y),
                source,
                target,
                scalingWindow,
                sourceWindow));
        Assert.Equal(
            new WindowPlacementPoint(125, 125),
            OverlayPlacementPlanner.ResolveEffectivePointer(
                new WindowPlacementPoint(source.X + 100, source.Y + 100),
                source,
                target,
                scalingWindow,
                sourceWindow));
        Assert.Equal(
            new WindowPlacementPoint(3839, 2159),
            OverlayPlacementPlanner.ResolveEffectivePointer(
                new WindowPlacementPoint(
                    source.X + source.Width - 1,
                    source.Y + source.Height - 1),
                source,
                target,
                scalingWindow,
                sourceWindow));

        WindowPlacementPoint scalingOwnedRaw = new(source.X + 100, source.Y + 100);
        Assert.Equal(
            scalingOwnedRaw,
            OverlayPlacementPlanner.ResolveEffectivePointer(
                scalingOwnedRaw,
                source,
                target,
                scalingWindow,
                scalingWindow));
    }

    private static void OverlayCompatibilityLifecycle()
    {
        PixelRect target = new(0, 0, 3840, 2160);
        PixelRect source = new(320, 180, 3200, 1800);
        OverlayWindowSnapshot first = CreateOverlaySnapshot(
            new nint(101),
            processId: 2001,
            new PixelRect(3200, 1880, 300, 100));
        OverlayWindowSnapshot second = CreateOverlaySnapshot(
            new nint(102),
            processId: 2002,
            new PixelRect(340, 200, 220, 90));
        OverlayWindowSnapshot ordinaryWindow = CreateOverlaySnapshot(
            new nint(103),
            processId: 2003,
            new PixelRect(700, 400, 600, 400)) with
        {
            IsTopmost = false,
            Title = "Ordinary utility",
            ExecutablePath = @"C:\Tools\Utility.exe",
        };
        FakeOverlayWindowApi windowApi = new(first, second, ordinaryWindow);
        windowApi.ForegroundWindowHandle = new nint(9001);
        windowApi.CursorPosition = new WindowPlacementPoint(source.X, source.Y);
        windowApi.WindowAtCursorHandle = new nint(9001);
        OverlayCompatibilityService service = new(windowApi);
        OverlayCompatibilitySession session = service.Capture(
            new OverlayCompatibilityCaptureRequest
            {
                SourceWindowHandle = new nint(9001),
                SourceProcessId = 9001,
                SourceRegion = source,
                TargetRegion = target,
                ExcludedProcessIds = new HashSet<int> { 9001, 9002 },
            });
        Assert.Equal(2, session.CapturedWindowCount);
        Assert.False(session.IsActive);

        OverlayCompatibilityUpdate activated = service.Activate(
            session,
            scalingWindowHandle: new nint(999),
            scalingProcessId: 9002,
            target);
        Assert.True(session.IsActive);
        Assert.Equal(2, activated.TrackedWindowCount);
        Assert.Equal(2, activated.MappedWindowCount);
        Assert.Equal(2, windowApi.MoveRequests.Count);
        Assert.Equal(2, windowApi.TopmostRequests.Count);
        Assert.Equal(
            new PixelRect(3516, 2060, 300, 100),
            windowApi.Inspect(first.Handle)!.Bounds);
        Assert.Equal(first.Bounds.Width, windowApi.Inspect(first.Handle)!.Bounds.Width);

        _ = service.Maintain(
            session,
            sourceOrScalingOutputIsForeground: false);
        Assert.Equal(4, windowApi.TopmostRequests.Count);
        Assert.False(windowApi.TopmostRequests[^1].Topmost);

        PixelRect externallyMoved = new(3400, 1850, 300, 100);
        windowApi.SetSnapshot(
            windowApi.Inspect(first.Handle)! with
            {
                Bounds = externallyMoved,
                IsTopmost = false,
            });
        OverlayWindowSnapshot lateOverlay = CreateOverlaySnapshot(
            new nint(104),
            processId: 2004,
            new PixelRect(1500, 100, 240, 80));
        windowApi.SetSnapshot(lateOverlay);

        OverlayCompatibilityUpdate maintained = service.Maintain(
            session,
            sourceOrScalingOutputIsForeground: true);
        Assert.Equal(3, maintained.TrackedWindowCount);
        Assert.Equal(2, maintained.MappedWindowCount);
        Assert.Equal(7, windowApi.TopmostRequests.Count);
        Assert.True(windowApi.TopmostRequests[^1].Topmost);
        Assert.Equal(lateOverlay.Bounds, windowApi.Inspect(lateOverlay.Handle)!.Bounds);
        Assert.True(session.TracksWindow(lateOverlay.Handle));

        OverlayCompatibilityUpdate restored = service.Restore(session);
        Assert.False(session.IsActive);
        Assert.Empty(restored.Warnings);
        Assert.Equal(externallyMoved, windowApi.Inspect(first.Handle)!.Bounds);
        Assert.Equal(second.Bounds, windowApi.Inspect(second.Handle)!.Bounds);
        Assert.Equal(3, windowApi.MoveRequests.Count);

        _ = service.Restore(session);
        Assert.Equal(3, windowApi.MoveRequests.Count);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => service.Activate(
                session,
                new nint(999),
                scalingProcessId: 0,
                target));
    }

    private static void EqLegendsCompanionOverlayLifecycle()
    {
        PixelRect target = new(0, 0, 3840, 2160);
        PixelRect source = new(320, 180, 3200, 1800);
        nint sourceWindow = new(9001);
        nint scalingWindow = new(9002);
        const int companionProcessId = 2401;
        const string companionExecutable =
            @"C:\Users\Player\AppData\Local\Programs\everquest-companion\EQ Legends Companion.exe";

        OverlayWindowSnapshot fightOverlay = CreateOverlaySnapshot(
            new nint(401),
            companionProcessId,
            new PixelRect(2900, 1600, 620, 420),
            companionExecutable) with
        {
            ClassName = "Chrome_WidgetWin_1",
            Title = "Fight Overlay",
            IsTopmost = false,
            IsLayered = false,
        };
        OverlayWindowSnapshot companionMainWindow = CreateOverlaySnapshot(
            new nint(402),
            companionProcessId,
            new PixelRect(200, 100, 1280, 860),
            companionExecutable) with
        {
            ClassName = "Chrome_WidgetWin_1",
            Title = "EQ Legends Companion",
            IsTopmost = false,
            IsToolWindow = false,
            IsLayered = false,
        };
        OverlayWindowSnapshot companionSettingsWindow = CreateOverlaySnapshot(
            new nint(405),
            companionProcessId,
            new PixelRect(600, 300, 500, 320),
            companionExecutable) with
        {
            ClassName = "Chrome_WidgetWin_1",
            Title = "Companion Settings",
            IsTopmost = false,
            IsToolWindow = true,
            IsLayered = false,
        };
        FakeOverlayWindowApi windowApi = new(
            fightOverlay,
            companionSettingsWindow,
            companionMainWindow)
        {
            ForegroundWindowHandle = sourceWindow,
            CursorPosition = new WindowPlacementPoint(source.X, source.Y),
            WindowAtCursorHandle = sourceWindow,
        };
        OverlayCompatibilityService service = new(windowApi);
        OverlayCompatibilitySession session = service.Capture(
            new OverlayCompatibilityCaptureRequest
            {
                SourceWindowHandle = sourceWindow,
                SourceProcessId = 9001,
                SourceRegion = source,
                TargetRegion = target,
                ExcludedProcessIds = new HashSet<int> { 9001, 9002 },
            });

        Assert.Equal(1, session.CapturedWindowCount);
        Assert.True(session.TracksWindow(fightOverlay.Handle));
        Assert.False(session.TracksWindow(companionSettingsWindow.Handle));
        Assert.False(session.TracksWindow(companionMainWindow.Handle));

        _ = service.Activate(
            session,
            scalingWindow,
            scalingProcessId: 9002,
            target);
        Assert.True(windowApi.Inspect(fightOverlay.Handle)!.IsTopmost);
        Assert.Equal(1, session.MappedWindowCount);
        Assert.Empty(windowApi.InputTransparencyRequests);

        OverlayWindowSnapshot zoneOverlay = CreateOverlaySnapshot(
            new nint(403),
            companionProcessId,
            new PixelRect(1700, 700, 520, 260),
            companionExecutable) with
        {
            ClassName = "Chrome_WidgetWin_1",
            Title = "Zone Overlay",
            IsTopmost = false,
            IsLayered = false,
        };
        windowApi.SetSnapshot(zoneOverlay);
        windowApi.RaiseWindowEvent(
            new OverlayWindowEvent(
                OverlayWindowEventKind.Shown,
                zoneOverlay.Handle));

        Assert.Equal(2, session.CapturedWindowCount);
        Assert.True(session.TracksWindow(zoneOverlay.Handle));
        Assert.False(session.TracksWindow(companionMainWindow.Handle));
        Assert.True(windowApi.Inspect(zoneOverlay.Handle)!.IsTopmost);
        Assert.Equal(zoneOverlay.Bounds, windowApi.Inspect(zoneOverlay.Handle)!.Bounds);
        Assert.Equal(1, session.MappedWindowCount);
        Assert.Empty(windowApi.InputTransparencyRequests);

        // Clicking an unlocked companion panel must make it interactive without
        // dropping either tracked panel behind Magpie.
        windowApi.RaiseWindowEvent(
            new OverlayWindowEvent(
                OverlayWindowEventKind.ForegroundChanged,
                zoneOverlay.Handle));
        Assert.True(windowApi.Inspect(fightOverlay.Handle)!.IsTopmost);
        Assert.True(windowApi.Inspect(zoneOverlay.Handle)!.IsTopmost);
        Assert.False(windowApi.Inspect(fightOverlay.Handle)!.IsInputTransparent);
        Assert.False(windowApi.Inspect(zoneOverlay.Handle)!.IsInputTransparent);
        Assert.Empty(windowApi.InputTransparencyRequests);
        _ = service.Maintain(
            session,
            sourceOrScalingOutputIsForeground: false,
            discoverNewWindows: false);
        Assert.True(windowApi.Inspect(fightOverlay.Handle)!.IsTopmost);
        Assert.True(windowApi.Inspect(zoneOverlay.Handle)!.IsTopmost);

        // The full Companion dashboard shares the PID but is not an overlay. It
        // must remain untracked and should put protected overlays in the normal
        // background state while the user configures the app.
        windowApi.RaiseWindowEvent(
            new OverlayWindowEvent(
                OverlayWindowEventKind.ForegroundChanged,
                companionMainWindow.Handle));
        Assert.False(session.TracksWindow(companionMainWindow.Handle));
        Assert.False(windowApi.Inspect(fightOverlay.Handle)!.IsTopmost);
        Assert.False(windowApi.Inspect(zoneOverlay.Handle)!.IsTopmost);

        windowApi.RaiseWindowEvent(
            new OverlayWindowEvent(
                OverlayWindowEventKind.ForegroundChanged,
                scalingWindow));
        Assert.True(windowApi.Inspect(fightOverlay.Handle)!.IsTopmost);
        Assert.True(windowApi.Inspect(zoneOverlay.Handle)!.IsTopmost);

        OverlayCompatibilityUpdate restored = service.Restore(session);
        Assert.Empty(restored.Warnings);
        Assert.False(windowApi.Inspect(fightOverlay.Handle)!.IsTopmost);
        Assert.False(windowApi.Inspect(zoneOverlay.Handle)!.IsTopmost);
        Assert.Equal(fightOverlay.Bounds, windowApi.Inspect(fightOverlay.Handle)!.Bounds);
        Assert.Equal(zoneOverlay.Bounds, windowApi.Inspect(zoneOverlay.Handle)!.Bounds);

        OverlayWindowSnapshot companionHiddenOverlay = zoneOverlay with
        {
            Handle = new nint(404),
            IsVisible = true,
            IsTopmost = false,
        };
        FakeOverlayWindowApi hiddenWindowApi = new(companionHiddenOverlay)
        {
            ForegroundWindowHandle = sourceWindow,
            CursorPosition = new WindowPlacementPoint(source.X, source.Y),
            WindowAtCursorHandle = sourceWindow,
        };
        OverlayCompatibilityService hiddenService = new(hiddenWindowApi);
        OverlayCompatibilitySession hiddenSession = hiddenService.Capture(
            new OverlayCompatibilityCaptureRequest
            {
                SourceWindowHandle = sourceWindow,
                SourceProcessId = 9001,
                SourceRegion = source,
                TargetRegion = target,
                ExcludedProcessIds = new HashSet<int> { 9001, 9002 },
            });
        Assert.Equal(1, hiddenSession.CapturedWindowCount);
        Assert.Empty(hiddenSession.Warnings);

        _ = hiddenService.Activate(
            hiddenSession,
            scalingWindow,
            scalingProcessId: 9002,
            target);
        hiddenWindowApi.SetSnapshot(
            hiddenWindowApi.Inspect(companionHiddenOverlay.Handle)! with
            {
                IsVisible = false,
                IsTopmost = false,
            });
        _ = hiddenService.Maintain(
            hiddenSession,
            sourceOrScalingOutputIsForeground: true,
            discoverNewWindows: true);
        Assert.False(
            hiddenWindowApi.Inspect(companionHiddenOverlay.Handle)!.IsVisible);
        Assert.Equal(1, hiddenSession.Warnings.Count);
        Assert.True(
            hiddenSession.Warnings[0].Contains(
                "Hide when unfocused",
                StringComparison.Ordinal));

        hiddenWindowApi.SetSnapshot(
            hiddenWindowApi.Inspect(companionHiddenOverlay.Handle)! with
            {
                IsVisible = true,
            });
        hiddenWindowApi.RaiseWindowEvent(
            new OverlayWindowEvent(
                OverlayWindowEventKind.Shown,
                companionHiddenOverlay.Handle));
        Assert.True(hiddenSession.TracksWindow(companionHiddenOverlay.Handle));
        Assert.True(
            hiddenWindowApi.Inspect(companionHiddenOverlay.Handle)!.IsTopmost);
        _ = hiddenService.Restore(hiddenSession);

        OverlayWindowSnapshot originalHandleOwner = fightOverlay with
        {
            Handle = new nint(406),
            ProcessId = 2402,
            IsTopmost = false,
        };
        FakeOverlayWindowApi reuseWindowApi = new(originalHandleOwner)
        {
            ForegroundWindowHandle = sourceWindow,
            CursorPosition = new WindowPlacementPoint(source.X, source.Y),
            WindowAtCursorHandle = sourceWindow,
        };
        OverlayCompatibilityService reuseService = new(reuseWindowApi);
        OverlayCompatibilitySession reuseSession = reuseService.Capture(
            new OverlayCompatibilityCaptureRequest
            {
                SourceWindowHandle = sourceWindow,
                SourceProcessId = 9001,
                SourceRegion = source,
                TargetRegion = target,
                ExcludedProcessIds = new HashSet<int> { 9001, 9002 },
            });
        _ = reuseService.Activate(
            reuseSession,
            scalingWindow,
            scalingProcessId: 9002,
            target);
        int requestsBeforeHandleReuse = reuseWindowApi.TopmostRequests.Count;
        reuseWindowApi.SetSnapshot(
            originalHandleOwner with
            {
                ProcessId = 7777,
                Title = "Unrelated utility",
                ExecutablePath = @"C:\Tools\Utility.exe",
                IsTopmost = false,
                IsToolWindow = false,
            });
        reuseWindowApi.RaiseWindowEvent(
            new OverlayWindowEvent(
                OverlayWindowEventKind.Shown,
                originalHandleOwner.Handle));
        Assert.False(reuseSession.TracksWindow(originalHandleOwner.Handle));
        Assert.Equal(
            requestsBeforeHandleReuse,
            reuseWindowApi.TopmostRequests.Count);

        OverlayWindowSnapshot replacementOverlay = zoneOverlay with
        {
            Handle = originalHandleOwner.Handle,
            ProcessId = 2403,
            IsTopmost = false,
        };
        reuseWindowApi.SetSnapshot(replacementOverlay);
        reuseWindowApi.RaiseWindowEvent(
            new OverlayWindowEvent(
                OverlayWindowEventKind.Shown,
                replacementOverlay.Handle));
        Assert.True(reuseSession.TracksWindow(replacementOverlay.Handle));
        Assert.True(reuseWindowApi.Inspect(replacementOverlay.Handle)!.IsTopmost);
        _ = reuseService.Restore(reuseSession);
    }

    private static void OverlayCompatibilityInputPassThrough()
    {
        PixelRect target = new(0, 0, 3840, 2160);
        PixelRect source = new(320, 180, 3200, 1800);
        nint sourceWindow = new(9001);
        nint scalingWindow = new(9002);
        OverlayWindowSnapshot opaqueOverlap = CreateOverlaySnapshot(
            new nint(301),
            processId: 2301,
            new PixelRect(1500, 800, 320, 40));
        OverlayWindowSnapshot transparentOverlap = CreateOverlaySnapshot(
            new nint(302),
            processId: 2302,
            new PixelRect(2200, 900, 300, 100)) with
        {
            IsInputTransparent = true,
        };
        OverlayWindowSnapshot opaqueOutsideSource = CreateOverlaySnapshot(
            new nint(303),
            processId: 2303,
            new PixelRect(0, 0, 100, 100));
        FakeOverlayWindowApi windowApi = new(
            opaqueOverlap,
            transparentOverlap,
            opaqueOutsideSource)
        {
            ForegroundWindowHandle = sourceWindow,
            CursorPosition = new WindowPlacementPoint(source.X, source.Y),
            WindowAtCursorHandle = sourceWindow,
        };
        OverlayCompatibilityService service = new(windowApi);
        OverlayCompatibilitySession session = service.Capture(
            new OverlayCompatibilityCaptureRequest
            {
                SourceWindowHandle = sourceWindow,
                SourceProcessId = 9001,
                SourceRegion = source,
                TargetRegion = target,
                ExcludedProcessIds = new HashSet<int> { 9001, 9002 },
            });

        _ = service.Activate(
            session,
            scalingWindow,
            scalingProcessId: 9002,
            target);
        Assert.True(windowApi.Inspect(opaqueOverlap.Handle)!.IsInputTransparent);
        Assert.True(windowApi.Inspect(transparentOverlap.Handle)!.IsInputTransparent);
        Assert.False(
            windowApi.Inspect(opaqueOutsideSource.Handle)!.IsInputTransparent);
        Assert.SequenceEqual(
            new[] { (opaqueOverlap.Handle, true) },
            windowApi.InputTransparencyRequests);

        // Magpie keeps the raw pointer in source coordinates while drawing it at
        // the scaled destination. Hovering the visible overlay must restore its
        // original opaque input style so its controls remain clickable.
        windowApi.CursorPosition = new WindowPlacementPoint(
            opaqueOverlap.Bounds.X + opaqueOverlap.Bounds.Width / 2,
            opaqueOverlap.Bounds.Y + opaqueOverlap.Bounds.Height / 2);
        windowApi.WindowAtCursorHandle = scalingWindow;
        _ = service.RefreshInputHandoff(session);
        Assert.True(windowApi.Inspect(opaqueOverlap.Handle)!.IsInputTransparent);

        windowApi.WindowAtCursorHandle = sourceWindow;
        _ = service.RefreshInputHandoff(session);
        Assert.False(windowApi.Inspect(opaqueOverlap.Handle)!.IsInputTransparent);

        windowApi.CursorPosition = new WindowPlacementPoint(source.X, source.Y);
        _ = service.RefreshInputHandoff(session);
        Assert.True(windowApi.Inspect(opaqueOverlap.Handle)!.IsInputTransparent);

        // Bringing the tracked overlay forward is an explicit interaction mode.
        // It must become clickable immediately, and the periodic maintenance call
        // must not reinterpret that overlay as gameplay foreground.
        windowApi.RaiseWindowEvent(
            new OverlayWindowEvent(
                OverlayWindowEventKind.ForegroundChanged,
                opaqueOverlap.Handle));
        Assert.False(windowApi.Inspect(opaqueOverlap.Handle)!.IsInputTransparent);
        int requestsInInteractionMode = windowApi.InputTransparencyRequests.Count;
        _ = service.Maintain(
            session,
            sourceOrScalingOutputIsForeground: true,
            discoverNewWindows: false);
        Assert.Equal(
            requestsInInteractionMode,
            windowApi.InputTransparencyRequests.Count);

        windowApi.RaiseWindowEvent(
            new OverlayWindowEvent(
                OverlayWindowEventKind.ForegroundChanged,
                scalingWindow));
        Assert.True(windowApi.Inspect(opaqueOverlap.Handle)!.IsInputTransparent);

        // The style is needed only while the native overlay physically shadows the
        // source. Moving it outside that source restores normal input immediately.
        windowApi.SetSnapshot(
            windowApi.Inspect(opaqueOverlap.Handle)! with
            {
                Bounds = new PixelRect(0, 0, 100, 100),
            });
        _ = service.Maintain(
            session,
            sourceOrScalingOutputIsForeground: true,
            discoverNewWindows: false);
        Assert.False(windowApi.Inspect(opaqueOverlap.Handle)!.IsInputTransparent);

        windowApi.SetSnapshot(
            windowApi.Inspect(opaqueOverlap.Handle)! with
            {
                Bounds = new PixelRect(1000, 500, 320, 120),
            });
        _ = service.Maintain(
            session,
            sourceOrScalingOutputIsForeground: true,
            discoverNewWindows: false);
        Assert.True(windowApi.Inspect(opaqueOverlap.Handle)!.IsInputTransparent);

        _ = service.Restore(session);
        Assert.False(windowApi.Inspect(opaqueOverlap.Handle)!.IsInputTransparent);
        Assert.True(windowApi.Inspect(transparentOverlap.Handle)!.IsInputTransparent);
        Assert.False(
            windowApi.Inspect(opaqueOutsideSource.Handle)!.IsInputTransparent);
        Assert.SequenceEqual(
            new[]
            {
                (opaqueOverlap.Handle, true),
                (opaqueOverlap.Handle, false),
                (opaqueOverlap.Handle, true),
                (opaqueOverlap.Handle, false),
                (opaqueOverlap.Handle, true),
                (opaqueOverlap.Handle, false),
                (opaqueOverlap.Handle, true),
                (opaqueOverlap.Handle, false),
            },
            windowApi.InputTransparencyRequests);

        OverlayWindowSnapshot restoreFailureOverlay = CreateOverlaySnapshot(
            new nint(304),
            processId: 2304,
            new PixelRect(1200, 700, 260, 90));
        FakeOverlayWindowApi restoreFailureApi = new(restoreFailureOverlay)
        {
            ForegroundWindowHandle = sourceWindow,
            CursorPosition = new WindowPlacementPoint(source.X, source.Y),
            WindowAtCursorHandle = sourceWindow,
        };
        OverlayCompatibilityService restoreFailureService = new(restoreFailureApi);
        OverlayCompatibilitySession restoreFailureSession =
            restoreFailureService.Capture(
                new OverlayCompatibilityCaptureRequest
                {
                    SourceWindowHandle = sourceWindow,
                    SourceProcessId = 9001,
                    SourceRegion = source,
                    TargetRegion = target,
                    ExcludedProcessIds = new HashSet<int> { 9001, 9002 },
                });
        _ = restoreFailureService.Activate(
            restoreFailureSession,
            scalingWindow,
            scalingProcessId: 9002,
            target);
        restoreFailureApi.RejectInputTransparencyChanges = true;
        restoreFailureApi.RaiseWindowEvent(
            new OverlayWindowEvent(
                OverlayWindowEventKind.ForegroundChanged,
                new nint(9999)));
        Assert.True(
            restoreFailureApi.Inspect(restoreFailureOverlay.Handle)!
                .IsInputTransparent);
        Assert.True(
            restoreFailureSession.Warnings.Any(
                warning => warning.Contains(
                    "original mouse-input behavior",
                    StringComparison.Ordinal)));
        restoreFailureApi.RejectInputTransparencyChanges = false;
        _ = restoreFailureService.Restore(restoreFailureSession);
        Assert.False(
            restoreFailureApi.Inspect(restoreFailureOverlay.Handle)!
                .IsInputTransparent);

        OverlayWindowSnapshot applyFailureOverlay = CreateOverlaySnapshot(
            new nint(305),
            processId: 2305,
            new PixelRect(1200, 700, 260, 90));
        FakeOverlayWindowApi applyFailureApi = new(applyFailureOverlay)
        {
            ForegroundWindowHandle = sourceWindow,
            CursorPosition = new WindowPlacementPoint(source.X, source.Y),
            WindowAtCursorHandle = sourceWindow,
            RejectInputTransparencyChanges = true,
        };
        OverlayCompatibilityService applyFailureService = new(applyFailureApi);
        OverlayCompatibilitySession applyFailureSession = applyFailureService.Capture(
            new OverlayCompatibilityCaptureRequest
            {
                SourceWindowHandle = sourceWindow,
                SourceProcessId = 9001,
                SourceRegion = source,
                TargetRegion = target,
                ExcludedProcessIds = new HashSet<int> { 9001, 9002 },
            });
        _ = applyFailureService.Activate(
            applyFailureSession,
            scalingWindow,
            scalingProcessId: 9002,
            target);
        Assert.False(
            applyFailureApi.Inspect(applyFailureOverlay.Handle)!
                .IsInputTransparent);
        Assert.True(
            applyFailureSession.Warnings.Any(
                warning => warning.Contains(
                    "temporarily click-through",
                    StringComparison.Ordinal)));
        _ = applyFailureService.Restore(applyFailureSession);
    }

    private static void OverlayCompatibilityEventRecovery()
    {
        PixelRect target = new(0, 0, 3840, 2160);
        PixelRect source = new(320, 180, 3200, 1800);
        nint sourceWindow = new(9001);
        nint scalingWindow = new(9002);
        OverlayWindowSnapshot loremaster = CreateOverlaySnapshot(
            new nint(201),
            processId: 2101,
            new PixelRect(3000, 1700, 300, 100),
            executablePath: @"C:\Users\Player\SpinsLoremaster\Loremaster.exe")
            with
            {
                IsTopmost = false,
                Title = "Loremaster",
            };
        OverlayWindowSnapshot ordinaryWindow = CreateOverlaySnapshot(
            new nint(202),
            processId: 2102,
            new PixelRect(800, 400, 500, 300),
            executablePath: @"C:\Tools\Utility.exe") with
        {
            IsTopmost = false,
            Title = "Ordinary utility",
        };
        OverlayWindowSnapshot alternateScalingWindow = CreateOverlaySnapshot(
            new nint(9003),
            processId: 9002,
            new PixelRect(0, 0, 40, 40),
            executablePath: @"C:\Runtime\Magpie.exe");
        FakeOverlayWindowApi windowApi = new(
            loremaster,
            ordinaryWindow,
            alternateScalingWindow);
        windowApi.ForegroundWindowHandle = sourceWindow;
        windowApi.CursorPosition = new WindowPlacementPoint(source.X, source.Y);
        windowApi.WindowAtCursorHandle = sourceWindow;
        OverlayCompatibilityService service = new(windowApi);
        OverlayCompatibilitySession session = service.Capture(
            new OverlayCompatibilityCaptureRequest
            {
                SourceWindowHandle = sourceWindow,
                SourceProcessId = 9001,
                SourceRegion = source,
                TargetRegion = target,
                ExcludedProcessIds = new HashSet<int> { 9001, 9002 },
            });
        Assert.Equal(1, session.CapturedWindowCount);

        _ = service.Activate(
            session,
            scalingWindow,
            scalingProcessId: 9002,
            target);
        Assert.True(windowApi.Inspect(loremaster.Handle)!.IsTopmost);
        Assert.True(windowApi.Inspect(loremaster.Handle)!.IsInputTransparent);
        Assert.Equal(1, windowApi.TopmostRequests.Count);

        windowApi.SetSnapshot(
            windowApi.Inspect(loremaster.Handle)! with { IsTopmost = false });
        windowApi.RaiseWindowEvent(
            new OverlayWindowEvent(
                OverlayWindowEventKind.ZOrderChanged,
                loremaster.Handle));
        Assert.True(windowApi.Inspect(loremaster.Handle)!.IsTopmost);
        Assert.Equal(2, windowApi.TopmostRequests.Count);

        windowApi.RaiseWindowEvent(
            new OverlayWindowEvent(
                OverlayWindowEventKind.ZOrderChanged,
                scalingWindow));
        Assert.Equal(3, windowApi.TopmostRequests.Count);

        windowApi.RaiseWindowEvent(
            new OverlayWindowEvent(
                OverlayWindowEventKind.ForegroundChanged,
                new nint(9999)));
        Assert.False(windowApi.Inspect(loremaster.Handle)!.IsTopmost);
        Assert.False(windowApi.Inspect(loremaster.Handle)!.IsInputTransparent);
        windowApi.RaiseWindowEvent(
            new OverlayWindowEvent(
                OverlayWindowEventKind.ForegroundChanged,
                alternateScalingWindow.Handle));
        Assert.False(windowApi.Inspect(loremaster.Handle)!.IsTopmost);
        windowApi.RaiseWindowEvent(
            new OverlayWindowEvent(
                OverlayWindowEventKind.ForegroundChanged,
                scalingWindow));
        Assert.True(windowApi.Inspect(loremaster.Handle)!.IsTopmost);
        Assert.True(windowApi.Inspect(loremaster.Handle)!.IsInputTransparent);

        int requestCountBeforeRestore = windowApi.TopmostRequests.Count;
        _ = service.Restore(session);
        Assert.True(windowApi.ObserverDisposed);
        Assert.False(windowApi.Inspect(loremaster.Handle)!.IsTopmost);
        windowApi.RaiseWindowEvent(
            new OverlayWindowEvent(
                OverlayWindowEventKind.ZOrderChanged,
                loremaster.Handle));
        Assert.Equal(
            requestCountBeforeRestore + 1,
            windowApi.TopmostRequests.Count);
    }

    private static void NativeOverlayObserverLifecycle()
    {
        using NativeOverlayWindowOrderObserver observer = new(_ => { });
    }

    private static OverlayWindowSnapshot CreateOverlaySnapshot(
        nint handle,
        int processId,
        PixelRect bounds,
        string executablePath = @"C:\Tools\Overlay.exe") =>
        new(
            handle,
            processId,
            "OverlayWindow",
            "Companion HUD",
            executablePath,
            bounds,
            IsVisible: true,
            IsMinimized: false,
            IsRootWindow: true,
            IsTopmost: true,
            IsToolWindow: true,
            IsLayered: true,
            IsInputTransparent: false);

    private static async Task WindowDiscoveryValidationAsync()
    {
        WindowDiscoveryService service = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => service.FindVisibleWindows(0));
        IReadOnlyList<WindowDescriptor> ownWindows = service.FindVisibleWindows(
            Environment.ProcessId);
        Assert.True(ownWindows.All(window => window.ProcessId == Environment.ProcessId));

        WindowDescriptor invalid = new(
            nint.Zero,
            Environment.ProcessId,
            string.Empty,
            string.Empty,
            new PixelRect(0, 0, 0, 0),
            new PixelRect(0, 0, 0, 0),
            IsMinimized: false,
            ExecutablePath: null);
        Assert.False(service.BringToForeground(invalid));

        using Process unstarted = new();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.WaitForVisibleWindowAsync(
                unstarted,
                TimeSpan.Zero)).ConfigureAwait(false);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.WaitForVisibleWindowAsync(
                unstarted,
                TimeSpan.FromMilliseconds(20))).ConfigureAwait(false);
    }

    private static async Task ManagedLaunchInteractionReadinessPolicyAsync()
    {
        Assert.Equal(
            TimeSpan.FromMilliseconds(100),
            WindowInteractionReadinessPolicy.DiscoveryPollInterval);
        Assert.Equal(
            TimeSpan.FromSeconds(1),
            WindowInteractionReadinessPolicy.ManagedLaunchStableDuration);
        Assert.Equal(
            11,
            WindowInteractionReadinessPolicy.ManagedLaunchRequiredStablePolls);
        Assert.Equal(
            4,
            WindowInteractionReadinessPolicy.CalculateRequiredStablePolls(
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMilliseconds(100)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => WindowInteractionReadinessPolicy.CalculateRequiredStablePolls(
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(100)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => WindowInteractionReadinessPolicy.CalculateRequiredStablePolls(
                TimeSpan.FromSeconds(1),
                TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => WindowInteractionReadinessPolicy.CalculateRequiredStablePolls(
                TimeSpan.FromSeconds(3),
                TimeSpan.FromMilliseconds(100)));

        PixelSize expectedSize = new(2752, 1152);
        WindowDescriptor expected = new(
            new nint(123),
            Environment.ProcessId,
            "EverQuest",
            "EQGameWindow",
            new PixelRect(0, 0, 2768, 1191),
            new PixelRect(8, 31, expectedSize.Width, expectedSize.Height),
            IsMinimized: false,
            ExecutablePath: @"C:\EQLegends\eqgame.exe");
        FakeWindowDiscoveryService windows = new()
        {
            AllowCalls = true,
            StableResult = expected,
        };
        using Process process = Process.GetCurrentProcess();

        WindowDescriptor actual =
            await WindowInteractionReadinessPolicy.WaitForManagedLaunchAsync(
                windows,
                process,
                expectedSize,
                TimeSpan.FromSeconds(2)).ConfigureAwait(false);

        Assert.Equal(expected, actual);
        Assert.Equal(1, windows.StableCalls);
        Assert.Equal(
            WindowInteractionReadinessPolicy.ManagedLaunchRequiredStablePolls,
            windows.StablePollRequirements.Single());
    }

    private static async Task JournalLifecycleAsync()
    {
        await using TempDirectory temp = new();
        string stateDirectory = Path.Combine(temp.Path, "state");
        FourKayJournalStore store = new();
        FourKayPreparedState older = CreateJournalState(
            temp.Path,
            FourKayJournalStatus.Prepared,
            new DateTimeOffset(2026, 7, 27, 10, 0, 0, TimeSpan.Zero));
        FourKayPreparedState newer = CreateJournalState(
            temp.Path,
            FourKayJournalStatus.Preparing,
            new DateTimeOffset(2026, 7, 27, 11, 0, 0, TimeSpan.Zero));

        older = await store.SaveNewAsync(stateDirectory, older).ConfigureAwait(false);
        newer = await store.SaveNewAsync(stateDirectory, newer).ConfigureAwait(false);
        Assert.True(File.Exists(older.JournalPath));
        Assert.True(File.Exists(newer.JournalPath));
        Assert.Contains(
            Path.Combine("sessions", older.SessionId.ToString("N"), "journal.json"),
            older.JournalPath);

        string raw = await File.ReadAllTextAsync(newer.JournalPath).ConfigureAwait(false);
        Assert.Contains("\"status\": \"preparing\"", raw);
        Assert.DoesNotContain("journalPath", raw);

        newer = newer with
        {
            Status = FourKayJournalStatus.Prepared,
            AppliedIniSha256 = new string('A', 64),
            AppliedChatFontSize = 5,
            Warnings = ["deterministic warning"],
        };
        newer = await store.UpdateAsync(newer).ConfigureAwait(false);
        FourKayPreparedState loaded = await store.LoadAsync(newer.JournalPath)
            .ConfigureAwait(false);
        Assert.Equal(FourKayJournalStatus.Prepared, loaded.Status);
        Assert.Equal(new string('A', 64), loaded.AppliedIniSha256);
        Assert.Equal(5, loaded.AppliedChatFontSize);
        Assert.Equal("deterministic warning", loaded.Warnings.Single());

        IReadOnlyList<FourKayPreparedState> active = await store.ListActiveAsync(stateDirectory)
            .ConfigureAwait(false);
        Assert.Equal(2, active.Count);
        Assert.Equal(newer.SessionId, active[0].SessionId);
        Assert.Equal(older.SessionId, active[1].SessionId);
        FourKayPreparedState? latest = await store.LoadLatestActiveAsync(stateDirectory)
            .ConfigureAwait(false);
        Assert.Equal(newer.SessionId, latest?.SessionId);

        string recoveryManifest = Path.Combine(temp.Path, "recovery", "manifest.json");
        newer = await store.MarkRestoredAsync(newer, recoveryManifest).ConfigureAwait(false);
        Assert.Equal(FourKayJournalStatus.Restored, newer.Status);
        Assert.True(newer.RestoredAtUtc is not null);
        Assert.Equal(recoveryManifest, newer.RecoveryBackupManifestPath);
        Assert.Equal(
            FourKayJournalStatus.Restored,
            (await store.LoadAsync(newer.JournalPath).ConfigureAwait(false)).Status);

        active = await store.ListActiveAsync(stateDirectory).ConfigureAwait(false);
        Assert.Equal(1, active.Count);
        Assert.Equal(older.SessionId, active[0].SessionId);
        older = await store.MarkRestoredAsync(older, null).ConfigureAwait(false);
        Assert.Empty(await store.ListActiveAsync(stateDirectory).ConfigureAwait(false));
        Assert.Null(await store.LoadLatestActiveAsync(stateDirectory).ConfigureAwait(false));
        Assert.Empty(
            Directory.GetFiles(
                Path.GetDirectoryName(older.JournalPath)!,
                "*.tmp",
                SearchOption.TopDirectoryOnly));
    }

    private static async Task JournalLegacyPreparedGeometryAsync()
    {
        await using TempDirectory temp = new();
        string journalPath = Path.Combine(temp.Path, FourKayJournalStore.JournalFileName);
        const string legacyJournal =
            """
            {
              "formatVersion": 1,
              "sessionId": "ae604911-7557-450f-ad2e-31254e2ba105",
              "status": "prepared",
              "preparedAtUtc": "2026-07-28T05:02:47.5283591+00:00",
              "restoredAtUtc": null,
              "eqDirectory": "C:\\EQLegends",
              "stateDirectory": "C:\\Users\\travi\\AppData\\Local\\SpinFOURKAYYY",
              "eqGamePath": "C:\\EQLegends\\eqgame.exe",
              "eqClientIniPath": "C:\\EQLegends\\eqclient.ini",
              "backupManifestPath": "C:\\Users\\travi\\AppData\\Local\\SpinFOURKAYYY\\backups\\20260728T050247479Z-650178d9f5554dd8b5c2add05fc3ee92\\manifest.json",
              "dpiCompatibility": {
                "executablePath": "C:\\EQLegends\\eqgame.exe",
                "valueExisted": false,
                "originalValue": null,
                "originalKind": "string",
                "appliedValue": "~ HIGHDPIAWARE"
              },
              "resolutionPlan": {
                "targetResolution": {
                  "width": 3440,
                  "height": 1440,
                  "aspectRatio": 2.388888888888889,
                  "area": 4953600
                },
                "sourceResolution": {
                  "width": 1720,
                  "height": 720,
                  "aspectRatio": 2.388888888888889,
                  "area": 1238400
                },
                "destinationContent": {
                  "x": 0,
                  "y": 0,
                  "width": 3440,
                  "height": 1440,
                  "size": {
                    "width": 3440,
                    "height": 1440,
                    "aspectRatio": 2.388888888888889,
                    "area": 4953600
                  }
                },
                "actualUiScale": 2,
                "filter": "fsr",
                "presetKind": "custom",
                "usesIntegerScale": true
              },
              "targetMonitorHandle": 7278555,
              "targetMonitorBounds": {
                "x": 0,
                "y": 0,
                "width": 3440,
                "height": 1440,
                "size": {
                  "width": 3440,
                  "height": 1440,
                  "aspectRatio": 2.388888888888889,
                  "area": 4953600
                }
              },
              "originalIniSha256": "BB87737880074578294964EB387B969B832B75B317CB8D3FF24AE7E4B402FB56",
              "appliedIniSha256": "9350280BBC7AA01B54ABB6C7B5D39FE758B83D5A9F2AA27550B8BD68C2824EE2",
              "chatFontSizeIncrease": 0,
              "appliedChatFontSize": null,
              "warnings": [],
              "recoveryBackupManifestPath": null
            }
            """;
        await File.WriteAllTextAsync(journalPath, legacyJournal).ConfigureAwait(false);

        FourKayPreparedState loaded = await new FourKayJournalStore()
            .LoadAsync(journalPath)
            .ConfigureAwait(false);

        Assert.Equal(FourKayJournalStatus.Prepared, loaded.Status);
        Assert.Equal(new PixelSize(1720, 720), loaded.ResolutionPlan.SourceResolution);
        Assert.Equal(new PixelSize(3440, 1440), loaded.ResolutionPlan.TargetResolution);
        Assert.Equal(
            new PixelRect(0, 0, 3440, 1440),
            loaded.ResolutionPlan.DestinationContent);
        Assert.Equal(new PixelRect(0, 0, 3440, 1440), loaded.TargetMonitorBounds);
        Assert.Equal(2.0, loaded.ResolutionPlan.ActualUiScale);
        Assert.True(loaded.ResolutionPlan.UsesIntegerScale);
        Assert.Equal(
            FourKayUiCompatibilityMode.UnspecifiedLegacy,
            loaded.UiCompatibilityMode);
    }

    private static async Task JournalPreparedUiModeCannotBeRelabeledAsync()
    {
        await using TempDirectory temp = new();
        string stateDirectory = Path.Combine(temp.Path, "state");
        FourKayJournalStore store = new();
        FourKayPreparedState strict = CreateJournalState(
            temp.Path,
            FourKayJournalStatus.Prepared,
            DateTimeOffset.UnixEpoch) with
        {
            UiCompatibilityMode = FourKayUiCompatibilityMode.SpinUiStrict,
        };
        strict = await store.SaveNewAsync(stateDirectory, strict).ConfigureAwait(false);

        InvalidOperationException blocked =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.UpdateAsync(
                    strict with
                    {
                        UiCompatibilityMode =
                            FourKayUiCompatibilityMode.GenericOrCustom,
                    })).ConfigureAwait(false);
        Assert.Contains("UI compatibility mode is immutable", blocked.Message);

        FourKayPreparedState persisted = await store.LoadAsync(strict.JournalPath)
            .ConfigureAwait(false);
        Assert.Equal(strict.SessionId, persisted.SessionId);
        Assert.Equal(FourKayJournalStatus.Prepared, persisted.Status);
        Assert.Equal(
            FourKayUiCompatibilityMode.SpinUiStrict,
            persisted.UiCompatibilityMode);
    }

    private static async Task JournalNewestRecoverySessionPreservesEachUiModeAsync()
    {
        await using TempDirectory temp = new();
        string stateDirectory = Path.Combine(temp.Path, "state");
        FourKayJournalStore store = new();
        FourKayPreparedState olderGeneric = CreateJournalState(
            temp.Path,
            FourKayJournalStatus.Prepared,
            new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.Zero));
        FourKayPreparedState newerStrict = CreateJournalState(
            temp.Path,
            FourKayJournalStatus.Prepared,
            new DateTimeOffset(2026, 7, 29, 11, 0, 0, TimeSpan.Zero)) with
        {
            UiCompatibilityMode = FourKayUiCompatibilityMode.SpinUiStrict,
        };
        FourKayPreparedState middleGeneric = CreateJournalState(
            temp.Path,
            FourKayJournalStatus.Prepared,
            new DateTimeOffset(2026, 7, 29, 10, 30, 0, TimeSpan.Zero));

        newerStrict = await store.SaveNewAsync(
            stateDirectory,
            newerStrict).ConfigureAwait(false);
        olderGeneric = await store.SaveNewAsync(
            stateDirectory,
            olderGeneric).ConfigureAwait(false);
        middleGeneric = await store.SaveNewAsync(
            stateDirectory,
            middleGeneric).ConfigureAwait(false);

        IReadOnlyList<FourKayPreparedState> allActive =
            await store.ListActiveAsync(stateDirectory).ConfigureAwait(false);
        Assert.Equal(3, allActive.Count);
        Assert.Equal(newerStrict.SessionId, allActive[0].SessionId);
        Assert.Equal(middleGeneric.SessionId, allActive[1].SessionId);
        Assert.Equal(olderGeneric.SessionId, allActive[2].SessionId);

        FourKayPreparedState? firstRecovery = allActive[0];
        Assert.Equal(newerStrict.SessionId, firstRecovery?.SessionId);
        Assert.Equal(
            FourKayUiCompatibilityMode.SpinUiStrict,
            firstRecovery?.UiCompatibilityMode);

        await store.MarkRestoredAsync(newerStrict, null).ConfigureAwait(false);
        FourKayPreparedState? secondRecovery =
            await store.LoadLatestActiveAsync(stateDirectory).ConfigureAwait(false);
        Assert.Equal(middleGeneric.SessionId, secondRecovery?.SessionId);
        Assert.Equal(
            FourKayUiCompatibilityMode.GenericOrCustom,
            secondRecovery?.UiCompatibilityMode);

        await store.MarkRestoredAsync(middleGeneric, null).ConfigureAwait(false);
        FourKayPreparedState? thirdRecovery =
            await store.LoadLatestActiveAsync(stateDirectory).ConfigureAwait(false);
        Assert.Equal(olderGeneric.SessionId, thirdRecovery?.SessionId);
        Assert.Equal(
            FourKayUiCompatibilityMode.GenericOrCustom,
            thirdRecovery?.UiCompatibilityMode);
    }

    private static async Task JournalErrorPathsAsync()
    {
        await using TempDirectory temp = new();
        string stateDirectory = Path.Combine(temp.Path, "state");
        FourKayJournalStore store = new();
        Assert.Empty(await store.ListActiveAsync(stateDirectory).ConfigureAwait(false));

        FourKayPreparedState state = CreateJournalState(
            temp.Path,
            FourKayJournalStatus.Preparing,
            DateTimeOffset.UnixEpoch);
        FourKayPreparedState located = await store.SaveNewAsync(stateDirectory, state)
            .ConfigureAwait(false);
        await Assert.ThrowsAsync<IOException>(
            () => store.SaveNewAsync(stateDirectory, state)).ConfigureAwait(false);
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.UpdateAsync(state)).ConfigureAwait(false);
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => store.UpdateAsync(located with
            {
                JournalPath = Path.Combine(temp.Path, "missing", "journal.json"),
            })).ConfigureAwait(false);

        string corruptDirectory = Path.Combine(
            stateDirectory,
            "sessions",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(corruptDirectory);
        string corruptPath = Path.Combine(
            corruptDirectory,
            FourKayJournalStore.JournalFileName);
        await File.WriteAllTextAsync(corruptPath, "{ definitely not json")
            .ConfigureAwait(false);
        await Assert.ThrowsAsync<JsonException>(
            () => store.LoadAsync(corruptPath)).ConfigureAwait(false);

        FourKayPreparedState unsupported = CreateJournalState(
            temp.Path,
            FourKayJournalStatus.Prepared,
            DateTimeOffset.UnixEpoch) with
        {
            FormatVersion = FourKayPreparedState.CurrentFormatVersion + 1,
        };
        unsupported = await store.SaveNewAsync(stateDirectory, unsupported)
            .ConfigureAwait(false);
        await Assert.ThrowsAsync<NotSupportedException>(
            () => store.LoadAsync(unsupported.JournalPath)).ConfigureAwait(false);
    }

    private static async Task PreparationExplicitCustomModeIgnoresInstalledSpinUiAssetsAsync()
    {
        await using TempDirectory temp = new();
        FakeClientFixture client = await FakeClientFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        string spinUiDirectory = Path.Combine(
            client.EqDirectory,
            "uifiles",
            "spinui_reloaded");
        Directory.CreateDirectory(spinUiDirectory);
        string defaultUiDirectory = Path.Combine(
            client.EqDirectory,
            "uifiles",
            "default");
        Directory.CreateDirectory(defaultUiDirectory);
        string characterLayoutPath = Path.Combine(
            client.EqDirectory,
            "UI_TestCharacter_testserver.ini");
        string characterProfilePath = Path.Combine(
            client.EqDirectory,
            "TestCharacter_testserver_LO1.ini");
        string userDataDirectory = Path.Combine(
            client.EqDirectory,
            "userdata",
            "TestCharacter");
        Directory.CreateDirectory(userDataDirectory);
        string userDataPath = Path.Combine(
            userDataDirectory,
            "LF_TestCharacter_testserver.ini");
        string spinUiProfilePath = Path.Combine(spinUiDirectory, "EQUI.xml");
        string defaultUiProfilePath = Path.Combine(defaultUiDirectory, "EQUI.xml");
        Dictionary<string, byte[]> sampleFiles = new(
            StringComparer.OrdinalIgnoreCase)
        {
            [characterLayoutPath] =
                Encoding.UTF8.GetBytes("[Main]\r\nWindowedMode=untouched\r\n"),
            [characterProfilePath] =
                Encoding.UTF8.GetBytes(
                    "[HotButtons]\r\nPage1Button1=Original\r\n"
                    + "[Socials]\r\nPage2Button3=Original\r\n"
                    + "[SpellLoadouts]\r\nLoadout1=Original\r\n"),
            [userDataPath] =
                Encoding.UTF8.GetBytes("[Friends]\r\nFriend1=Original\r\n"),
            [spinUiProfilePath] =
                Encoding.UTF8.GetBytes(
                    "<XML ID=\"EQInterfaceDefinitionLanguage\" />"),
            [defaultUiProfilePath] =
                Encoding.UTF8.GetBytes("<XML ID=\"DefaultUI\" />"),
        };
        DateTime protectedTimestampUtc =
            new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);
        foreach ((string path, byte[] content) in sampleFiles)
        {
            await File.WriteAllBytesAsync(path, content).ConfigureAwait(false);
            File.SetLastWriteTimeUtc(path, protectedTimestampUtc);
        }

        string stateDirectory = Path.Combine(temp.Path, "state");
        BackupService backup = new();
        FourKayJournalStore journal = new();
        FourKayPreparationService service = new(
            backup,
            new EqClientConfigurationService(),
            new FakeDpiCompatibilityService(),
            new FakeProcessDiscoveryService(),
            journal,
            FakeWindowPlacementService.Create4K());

        FourKayPreparedState prepared = await service.PrepareAsync(
            CreatePreparationRequest(
                client.EqDirectory,
                stateDirectory,
                chatFontSizeIncrease: 0) with
            {
                UiCompatibilityMode =
                    FourKayUiCompatibilityMode.GenericOrCustom,
            }).ConfigureAwait(false);

        Assert.Equal(
            FourKayUiCompatibilityMode.GenericOrCustom,
            prepared.UiCompatibilityMode);
        BackupManifest manifest = await backup.LoadManifestAsync(
            prepared.BackupManifestPath).ConfigureAwait(false);
        string[] expectedProtectedPaths =
        [
            Path.GetFullPath(client.EqClientIniPath),
            Path.GetFullPath(characterLayoutPath),
            Path.GetFullPath(characterProfilePath),
            Path.GetFullPath(userDataPath),
        ];
        Assert.Equal(
            EverQuestPlayerStateLocator.CurrentProtectionVersion,
            prepared.PlayerStateProtectionVersion);
        Assert.Equal(expectedProtectedPaths.Length, manifest.Entries.Count);
        Assert.Equal(expectedProtectedPaths.Length, prepared.ProtectedPlayerFiles.Count);
        foreach (string expectedPath in expectedProtectedPaths)
        {
            Assert.True(manifest.Entries.Any(
                entry => string.Equals(
                    entry.OriginalPath,
                    expectedPath,
                    StringComparison.OrdinalIgnoreCase)));
            Assert.True(prepared.ProtectedPlayerFiles.Any(
                path => string.Equals(
                    path,
                    expectedPath,
                    StringComparison.OrdinalIgnoreCase)));
        }

        Assert.False(manifest.Entries.Any(
            entry => string.Equals(
                entry.OriginalPath,
                spinUiProfilePath,
                StringComparison.OrdinalIgnoreCase)));
        Assert.False(manifest.Entries.Any(
            entry => string.Equals(
                entry.OriginalPath,
                defaultUiProfilePath,
                StringComparison.OrdinalIgnoreCase)));
        foreach ((string path, byte[] expectedContent) in sampleFiles)
        {
            Assert.True(File.Exists(path));
            Assert.True(
                expectedContent.SequenceEqual(
                    await File.ReadAllBytesAsync(path).ConfigureAwait(false)));
            Assert.Equal(protectedTimestampUtc, File.GetLastWriteTimeUtc(path));
        }

        FourKayPreparedState persisted = await journal.LoadAsync(prepared.JournalPath)
            .ConfigureAwait(false);
        Assert.Equal(
            FourKayUiCompatibilityMode.GenericOrCustom,
            persisted.UiCompatibilityMode);

        Dictionary<string, byte[]> changedProfileFiles = new(
            StringComparer.OrdinalIgnoreCase)
        {
            [characterLayoutPath] =
                Encoding.UTF8.GetBytes("[Main]\r\nWindowedMode=changed\r\n"),
            [characterProfilePath] =
                Encoding.UTF8.GetBytes(
                    "[HotButtons]\r\nPage1Button1=Changed\r\n"
                    + "[Socials]\r\nPage2Button3=Changed\r\n"),
            [userDataPath] =
                Encoding.UTF8.GetBytes("[Friends]\r\nFriend1=Changed\r\n"),
        };
        foreach ((string path, byte[] content) in changedProfileFiles)
        {
            await File.WriteAllBytesAsync(path, content).ConfigureAwait(false);
        }

        FourKayRestoreResult restored = await service.RestoreAsync(prepared)
            .ConfigureAwait(false);
        Assert.Equal(FourKayRestoreDisposition.Restored, restored.Disposition);
        Assert.True(restored.RecoveryBackupManifestPath is not null);
        BackupManifest recovery = await backup.LoadManifestAsync(
            restored.RecoveryBackupManifestPath!).ConfigureAwait(false);
        Assert.Equal(changedProfileFiles.Count, recovery.Entries.Count);
        foreach ((string path, byte[] changedContent) in changedProfileFiles)
        {
            BackupEntry entry = recovery.Entries.Single(
                candidate => string.Equals(
                    candidate.OriginalPath,
                    Path.GetFullPath(path),
                    StringComparison.OrdinalIgnoreCase));
            string payloadPath = Path.Combine(
                recovery.BackupDirectory,
                entry.BackupRelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            Assert.SequenceEqual(
                changedContent,
                await File.ReadAllBytesAsync(payloadPath).ConfigureAwait(false));
        }

        foreach ((string path, byte[] expectedContent) in sampleFiles)
        {
            Assert.True(File.Exists(path));
            Assert.True(
                expectedContent.SequenceEqual(
                    await File.ReadAllBytesAsync(path).ConfigureAwait(false)));
            Assert.Equal(protectedTimestampUtc, File.GetLastWriteTimeUtc(path));
        }
    }

    private static async Task PlayerStateProtectionScopeAsync()
    {
        await using TempDirectory temp = new();
        string eqDirectory = Path.Combine(temp.Path, "EverQuest");
        Directory.CreateDirectory(eqDirectory);

        string[] expectedRelativePaths =
        [
            "eqclient.ini",
            "_characters.ini",
            "eqlsPlayerData.ini",
            "notes.txt",
            "UI_TestCharacter_testserver.ini",
            "TestCharacter_testserver_LO1.ini",
            Path.Combine("userdata", "TestCharacter", "LF_TestCharacter.ini"),
            Path.Combine("userdata", "AddressBook.txt"),
            Path.Combine("AudioTriggers", "TestCharacter", "trigger.txt"),
        ];
        string[] excludedRelativePaths =
        [
            "defaults.ini",
            "LaunchPad-user.ini",
            Path.Combine("uifiles", "default", "UI_NotAProfile.ini"),
            Path.Combine("logs", "UI_NotAProfile.ini"),
            Path.Combine("config", "Test_server_LO1.ini"),
            Path.Combine("userdata", "static.xml"),
            Path.Combine("AudioTriggers", "sound.wav"),
        ];

        foreach (string relativePath in expectedRelativePaths.Concat(excludedRelativePaths))
        {
            string path = Path.Combine(eqDirectory, relativePath);
            Directory.CreateDirectory(
                Path.GetDirectoryName(path)
                    ?? throw new InvalidOperationException(
                        "The test file has no parent directory."));
            await File.WriteAllTextAsync(path, relativePath).ConfigureAwait(false);
        }

        IReadOnlyList<string> located =
            EverQuestPlayerStateLocator.FindExistingFiles(eqDirectory);
        Assert.Equal(expectedRelativePaths.Length, located.Count);
        foreach (string relativePath in expectedRelativePaths)
        {
            string fullPath = Path.GetFullPath(
                Path.Combine(eqDirectory, relativePath));
            Assert.True(located.Contains(
                fullPath,
                StringComparer.OrdinalIgnoreCase));
            Assert.True(
                EverQuestPlayerStateLocator.IsSupportedProtectedPath(
                    eqDirectory,
                    fullPath));
        }

        foreach (string relativePath in excludedRelativePaths)
        {
            string fullPath = Path.GetFullPath(
                Path.Combine(eqDirectory, relativePath));
            Assert.False(located.Contains(
                fullPath,
                StringComparer.OrdinalIgnoreCase));
            Assert.False(
                EverQuestPlayerStateLocator.IsSupportedProtectedPath(
                    eqDirectory,
                    fullPath));
        }

        Assert.False(
            EverQuestPlayerStateLocator.IsSupportedProtectedPath(
                eqDirectory,
                Path.Combine(temp.Path, "outside.ini")));
    }

    private static async Task PreparationAndPendingBlockerAsync()
    {
        await using TempDirectory temp = new();
        FakeClientFixture client = await FakeClientFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        string stateDirectory = Path.Combine(temp.Path, "state");
        FakeDpiCompatibilityService dpi = new();
        FakeProcessDiscoveryService processes = new();
        FakeWindowPlacementService windows = FakeWindowPlacementService.Create4K();
        FourKayJournalStore journal = new();
        FourKayPreparationService service = new(
            new BackupService(),
            new EqClientConfigurationService(),
            dpi,
            processes,
            journal,
            windows);

        FourKayPreparationRequest request = CreatePreparationRequest(
            client.EqDirectory,
            stateDirectory,
            chatFontSizeIncrease: 2) with
        {
            UiCompatibilityMode = FourKayUiCompatibilityMode.GenericOrCustom,
        };
        FourKayPreparedState prepared = await service.PrepareAsync(request)
            .ConfigureAwait(false);

        Assert.Equal(FourKayJournalStatus.Prepared, prepared.Status);
        Assert.Equal(Path.GetFullPath(client.EqDirectory), prepared.EqDirectory);
        Assert.Equal(Path.GetFullPath(client.EqGamePath), prepared.EqGamePath);
        Assert.Equal(Path.GetFullPath(client.EqClientIniPath), prepared.EqClientIniPath);
        Assert.Equal(42L, prepared.TargetMonitorHandle);
        Assert.Equal(new PixelRect(0, 0, 3840, 2160), prepared.TargetMonitorBounds);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(client.OriginalIniBytes)),
            prepared.OriginalIniSha256);
        Assert.Equal(
            Convert.ToHexString(
                SHA256.HashData(
                    await File.ReadAllBytesAsync(client.EqClientIniPath).ConfigureAwait(false))),
            prepared.AppliedIniSha256);
        Assert.Equal(5, prepared.AppliedChatFontSize);
        Assert.Equal(2, prepared.ChatFontSizeIncrease);
        Assert.Equal(
            FourKayUiCompatibilityMode.GenericOrCustom,
            prepared.UiCompatibilityMode);
        Assert.True(File.Exists(prepared.BackupManifestPath));
        Assert.True(File.Exists(prepared.JournalPath));
        Assert.Equal(1, dpi.CaptureCalls);
        Assert.Equal(1, dpi.ApplyCalls);
        Assert.Equal(0, dpi.RestoreCalls);

        EqClientVideoSettings settings = await new EqClientConfigurationService()
            .ReadAsync(client.EqClientIniPath).ConfigureAwait(false);
        Assert.Equal(new PixelSize(1920, 1080), settings.WindowedResolution);
        Assert.Equal(false, settings.Fullscreen);
        Assert.Equal(false, settings.AllowResize);
        Assert.Equal(5, settings.ChatFontSize);

        PendingRecoveryException blocker =
            await Assert.ThrowsAsync<PendingRecoveryException>(
                () => service.PrepareAsync(request)).ConfigureAwait(false);
        Assert.Equal(prepared.SessionId, blocker.PendingState.SessionId);
        Assert.Equal(1, dpi.ApplyCalls);
        Assert.Equal(
            1,
            Directory.GetDirectories(
                Path.Combine(stateDirectory, "backups"),
                "*",
                SearchOption.TopDirectoryOnly).Length);
        IReadOnlyList<FourKayPreparedState> active = await journal.ListActiveAsync(
            stateDirectory).ConfigureAwait(false);
        Assert.Equal(1, active.Count);
        Assert.Equal(prepared.SessionId, active[0].SessionId);
        Assert.Equal(
            FourKayUiCompatibilityMode.GenericOrCustom,
            active[0].UiCompatibilityMode);
    }

    private static async Task ManagedLaunchPreparationCommitsConfigurationOnlyAsync()
    {
        await using TempDirectory temp = new();
        FakeClientFixture client = await FakeClientFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        string layoutPath = Path.Combine(
            client.EqDirectory,
            "UI_TestCharacter_testserver.ini");
        byte[] personalLayout = Encoding.UTF8.GetBytes(
            "[Main]\r\nWindowedMode=2\r\n[HotButtons]\r\nButton1=/assist\r\n");
        await File.WriteAllBytesAsync(layoutPath, personalLayout).ConfigureAwait(false);

        string stateDirectory = Path.Combine(temp.Path, "state");
        BackupService backup = new();
        FourKayJournalStore journal = new();
        FourKayPreparationService service = new(
            backup,
            new EqClientConfigurationService(),
            new FakeDpiCompatibilityService(),
            new FakeProcessDiscoveryService(),
            journal,
            FakeWindowPlacementService.Create4K());

        FourKayPreparedState prepared = await service.PrepareAsync(
            CreatePreparationRequest(
                client.EqDirectory,
                stateDirectory,
                chatFontSizeIncrease: 0) with
            {
                KeepPreparedConfiguration = true,
            }).ConfigureAwait(false);

        Assert.Equal(FourKayJournalStatus.Committed, prepared.Status);
        Assert.True(prepared.KeepPreparedConfiguration);
        Assert.Equal(0, prepared.PlayerStateProtectionVersion);
        Assert.Empty(prepared.ProtectedPlayerFiles);
        Assert.Empty(await journal.ListActiveAsync(stateDirectory).ConfigureAwait(false));

        BackupManifest manifest = await backup.LoadManifestAsync(
            prepared.BackupManifestPath).ConfigureAwait(false);
        Assert.Equal(1, manifest.Entries.Count);
        Assert.Equal(
            Path.GetFullPath(client.EqClientIniPath),
            manifest.Entries[0].OriginalPath);
        Assert.SequenceEqual(
            personalLayout,
            await File.ReadAllBytesAsync(layoutPath).ConfigureAwait(false));

        EqClientVideoSettings settings = await new EqClientConfigurationService()
            .ReadAsync(client.EqClientIniPath).ConfigureAwait(false);
        Assert.Equal(new PixelSize(1920, 1080), settings.WindowedResolution);
        Assert.Equal(false, settings.Fullscreen);
        Assert.Equal(false, settings.Maximized);
        Assert.Equal(false, settings.AllowResize);
        Assert.Equal(0, settings.NativeUiScaleIndex);
    }

    private static async Task PreparationRollbackAsync()
    {
        await using TempDirectory temp = new();
        FakeClientFixture client = await FakeClientFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        string stateDirectory = Path.Combine(temp.Path, "state");
        FakeDpiCompatibilityService dpi = new();
        FakeProcessDiscoveryService processes = new();
        FourKayJournalStore journal = new();
        FourKayPreparationService service = new(
            new BackupService(),
            new ThrowingEqClientConfigurationService(),
            dpi,
            processes,
            journal,
            FakeWindowPlacementService.Create4K());

        FourKayPreparationException failure =
            await Assert.ThrowsAsync<FourKayPreparationException>(
                () => service.PrepareAsync(
                    CreatePreparationRequest(
                        client.EqDirectory,
                        stateDirectory,
                        chatFontSizeIncrease: 1))).ConfigureAwait(false);

        Assert.True(failure.InnerException is InvalidOperationException);
        Assert.True(failure.State is not null);
        Assert.Equal(
            FourKayJournalStatus.FailedAndRolledBack,
            failure.State!.Status);
        Assert.SequenceEqual(
            client.OriginalIniBytes,
            await File.ReadAllBytesAsync(client.EqClientIniPath).ConfigureAwait(false));
        Assert.Equal(1, dpi.CaptureCalls);
        Assert.Equal(1, dpi.ApplyCalls);
        Assert.Equal(1, dpi.RestoreCalls);
        FourKayPreparedState persisted = await journal.LoadAsync(failure.State.JournalPath)
            .ConfigureAwait(false);
        Assert.Equal(FourKayJournalStatus.FailedAndRolledBack, persisted.Status);
        Assert.Empty(await journal.ListActiveAsync(stateDirectory).ConfigureAwait(false));
    }

    private static async Task InterruptedDpiApplyRetainsRecoveryIntentAsync()
    {
        await using TempDirectory temp = new();
        FakeClientFixture client = await FakeClientFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        string stateDirectory = Path.Combine(temp.Path, "state");
        FakeDpiCompatibilityService dpi = new()
        {
            ApplyException = new IOException(
                "Injected interruption after the registry write may have started."),
        };
        FourKayJournalStore journal = new();
        FourKayPreparationService service = new(
            new BackupService(),
            new EqClientConfigurationService(),
            dpi,
            new FakeProcessDiscoveryService(),
            journal,
            FakeWindowPlacementService.Create4K());

        FourKayPreparationException failure =
            await Assert.ThrowsAsync<FourKayPreparationException>(
                () => service.PrepareAsync(
                    CreatePreparationRequest(
                        client.EqDirectory,
                        stateDirectory,
                        chatFontSizeIncrease: 0))).ConfigureAwait(false);

        Assert.True(failure.InnerException is IOException);
        Assert.True(failure.State is not null);
        Assert.Equal(
            "~ HIGHDPIAWARE",
            failure.State!.DpiCompatibility.AppliedValue);
        Assert.Equal(
            "~ HIGHDPIAWARE",
            dpi.LastAppliedIntent?.AppliedValue);
        Assert.Equal(1, dpi.ApplyCalls);
        Assert.Equal(1, dpi.RestoreCalls);
        Assert.Equal(
            FourKayJournalStatus.FailedAndRolledBack,
            failure.State.Status);
        Assert.SequenceEqual(
            client.OriginalIniBytes,
            await File.ReadAllBytesAsync(client.EqClientIniPath).ConfigureAwait(false));

        FourKayPreparedState persisted =
            await journal.LoadAsync(failure.State.JournalPath).ConfigureAwait(false);
        Assert.Equal("~ HIGHDPIAWARE", persisted.DpiCompatibility.AppliedValue);
        Assert.Equal(FourKayJournalStatus.FailedAndRolledBack, persisted.Status);

        FakeClientFixture racedClient = await FakeClientFixture.CreateAsync(
            Path.Combine(temp.Path, "precondition-race")).ConfigureAwait(false);
        string racedStateDirectory = Path.Combine(temp.Path, "race-state");
        FakeDpiCompatibilityService racedDpi = new()
        {
            ApplyException = new DpiCompatibilityValueChangedException(
                racedClient.EqGamePath),
        };
        FourKayPreparationService racedService = new(
            new BackupService(),
            new EqClientConfigurationService(),
            racedDpi,
            new FakeProcessDiscoveryService(),
            new FourKayJournalStore(),
            FakeWindowPlacementService.Create4K());
        FourKayPreparationException racedFailure =
            await Assert.ThrowsAsync<FourKayPreparationException>(
                () => racedService.PrepareAsync(
                    CreatePreparationRequest(
                        racedClient.EqDirectory,
                        racedStateDirectory,
                        chatFontSizeIncrease: 0))).ConfigureAwait(false);
        Assert.True(
            racedFailure.InnerException
                is DpiCompatibilityValueChangedException);
        Assert.Equal(0, racedDpi.RestoreCalls);
        Assert.Equal(
            FourKayJournalStatus.FailedAndRolledBack,
            racedFailure.State?.Status);
    }

    private static async Task RestoreConflictRecoveryAsync()
    {
        await using TempDirectory temp = new();
        FakeClientFixture client = await FakeClientFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        string stateDirectory = Path.Combine(temp.Path, "state");
        FakeDpiCompatibilityService dpi = new();
        FakeProcessDiscoveryService processes = new();
        FourKayJournalStore journal = new();
        BackupService backups = new();
        FourKayPreparationService service = new(
            backups,
            new EqClientConfigurationService(),
            dpi,
            processes,
            journal,
            FakeWindowPlacementService.Create4K());
        FourKayPreparedState prepared = await service.PrepareAsync(
            CreatePreparationRequest(client.EqDirectory, stateDirectory, 1))
            .ConfigureAwait(false);

        byte[] appliedBeforeTamperCheck =
            await File.ReadAllBytesAsync(client.EqClientIniPath).ConfigureAwait(false);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.RestoreAsync(
                prepared with
                {
                    DpiCompatibility = prepared.DpiCompatibility with
                    {
                        ExecutablePath = Path.Combine(
                            client.EqDirectory,
                            "other.exe"),
                    },
                })).ConfigureAwait(false);
        Assert.Equal(0, dpi.RestoreCalls);
        Assert.SequenceEqual(
            appliedBeforeTamperCheck,
            await File.ReadAllBytesAsync(client.EqClientIniPath).ConfigureAwait(false));

        byte[] conflict = Encoding.UTF8.GetBytes(
            "[Defaults]\r\nChatFontSize=9\r\nUserChangedThis=1\r\n");
        await File.WriteAllBytesAsync(client.EqClientIniPath, conflict).ConfigureAwait(false);
        FourKayRestoreResult restored = await service.RestoreAsync(prepared)
            .ConfigureAwait(false);

        Assert.Equal(FourKayRestoreDisposition.Restored, restored.Disposition);
        Assert.Equal(FourKayJournalStatus.Restored, restored.State.Status);
        Assert.True(restored.State.RestoredAtUtc is not null);
        Assert.True(restored.DpiRestore?.WasRestored == true);
        Assert.Equal(1, dpi.RestoreCalls);
        Assert.True(restored.RecoveryBackupManifestPath is not null);
        Assert.Equal(
            restored.RecoveryBackupManifestPath,
            restored.State.RecoveryBackupManifestPath);
        Assert.True(restored.Warnings.Any(
            warning => warning.Contains("changed after preparation", StringComparison.Ordinal)));
        Assert.SequenceEqual(
            client.OriginalIniBytes,
            await File.ReadAllBytesAsync(client.EqClientIniPath).ConfigureAwait(false));

        BackupManifest recovery = await backups.LoadManifestAsync(
            restored.RecoveryBackupManifestPath!).ConfigureAwait(false);
        Assert.Equal(1, recovery.Entries.Count);
        string recoveryPayload = Path.Combine(
            recovery.BackupDirectory,
            recovery.Entries[0].BackupRelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar));
        Assert.SequenceEqual(
            conflict,
            await File.ReadAllBytesAsync(recoveryPayload).ConfigureAwait(false));
        Assert.Null(await journal.LoadLatestActiveAsync(stateDirectory).ConfigureAwait(false));

        FourKayRestoreResult repeated = await service.RestoreAsync(restored.State)
            .ConfigureAwait(false);
        Assert.Equal(FourKayRestoreDisposition.AlreadyRestored, repeated.Disposition);
        Assert.Equal(1, dpi.RestoreCalls);
    }

    private static async Task RestoreGameStillRunningAsync()
    {
        await using TempDirectory temp = new();
        FakeClientFixture client = await FakeClientFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        string stateDirectory = Path.Combine(temp.Path, "state");
        FakeDpiCompatibilityService dpi = new();
        FakeProcessDiscoveryService processes = new();
        FourKayJournalStore journal = new();
        FourKayPreparationService service = new(
            new BackupService(),
            new EqClientConfigurationService(),
            dpi,
            processes,
            journal,
            FakeWindowPlacementService.Create4K());
        FourKayPreparedState prepared = await service.PrepareAsync(
            CreatePreparationRequest(client.EqDirectory, stateDirectory, 0))
            .ConfigureAwait(false);
        byte[] applied = await File.ReadAllBytesAsync(client.EqClientIniPath)
            .ConfigureAwait(false);

        processes.Current =
        [
            new ProcessDescriptor(4242, client.EqGamePath, DateTimeOffset.UnixEpoch),
        ];
        FourKayRestoreResult blocked = await service.RestoreAsync(prepared)
            .ConfigureAwait(false);
        Assert.Equal(FourKayRestoreDisposition.GameStillRunning, blocked.Disposition);
        Assert.Equal(4242, blocked.BlockingGameProcessIds.Single());
        Assert.True(blocked.Warnings.Single().Contains(
            "Exit EverQuest Legends",
            StringComparison.Ordinal));
        Assert.Equal(0, dpi.RestoreCalls);
        Assert.SequenceEqual(
            applied,
            await File.ReadAllBytesAsync(client.EqClientIniPath).ConfigureAwait(false));
        Assert.Equal(
            prepared.SessionId,
            (await journal.LoadLatestActiveAsync(stateDirectory).ConfigureAwait(false))
                ?.SessionId);
    }

    private static async Task RestoreFinalWriterRaceRemainsRecoverableAsync()
    {
        await using TempDirectory temp = new();
        FakeClientFixture client = await FakeClientFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        string launcherPath = Path.Combine(client.EqDirectory, "LaunchPad.exe");
        await File.WriteAllBytesAsync(launcherPath, [0x4D, 0x5A])
            .ConfigureAwait(false);
        string stateDirectory = Path.Combine(temp.Path, "state");
        FakeDpiCompatibilityService dpi = new();
        FakeProcessDiscoveryService processes = new()
        {
            FilterCurrentByExecutablePath = true,
        };
        CallbackBackupService backups = new();
        FourKayJournalStore journal = new();
        FourKayPreparationService service = new(
            backups,
            new EqClientConfigurationService(),
            dpi,
            processes,
            journal,
            FakeWindowPlacementService.Create4K());
        FourKayPreparedState prepared = await service.PrepareAsync(
            CreatePreparationRequest(client.EqDirectory, stateDirectory, 0))
            .ConfigureAwait(false);

        backups.AfterRestore = () =>
        {
            processes.Current =
            [
                new ProcessDescriptor(
                    9191,
                    launcherPath,
                    DateTimeOffset.UnixEpoch),
            ];
        };
        ClientConfigurationWriterRunningException raced =
            await Assert.ThrowsAsync<ClientConfigurationWriterRunningException>(
                () => service.RestoreAsync(prepared)).ConfigureAwait(false);
        Assert.Equal(launcherPath, raced.RunningWriters.Single().ExecutablePath);
        Assert.Equal(0, dpi.RestoreCalls);
        Assert.SequenceEqual(
            client.OriginalIniBytes,
            await File.ReadAllBytesAsync(client.EqClientIniPath).ConfigureAwait(false));
        Assert.Equal(
            prepared.SessionId,
            (await journal.LoadLatestActiveAsync(stateDirectory).ConfigureAwait(false))
                ?.SessionId);

        backups.AfterRestore = null;
        processes.Current = [];
        FourKayRestoreResult retry = await service.RestoreAsync(prepared)
            .ConfigureAwait(false);
        Assert.Equal(FourKayRestoreDisposition.Restored, retry.Disposition);
        Assert.Equal(1, dpi.RestoreCalls);
        Assert.Null(await journal.LoadLatestActiveAsync(stateDirectory).ConfigureAwait(false));
    }

    private static async Task PreparationValidationAsync()
    {
        await using TempDirectory temp = new();
        FakeClientFixture client = await FakeClientFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        FakeDpiCompatibilityService dpi = new();
        FakeProcessDiscoveryService processes = new();
        FakeWindowPlacementService windows = FakeWindowPlacementService.Create4K();
        FourKayPreparationService service = new(
            new BackupService(),
            new EqClientConfigurationService(),
            dpi,
            processes,
            new FourKayJournalStore(),
            windows);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.PrepareAsync(
                CreatePreparationRequest(
                    client.EqDirectory,
                    Path.Combine(temp.Path, "invalid-chat"),
                    chatFontSizeIncrease: 3))).ConfigureAwait(false);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.PrepareAsync(
                CreatePreparationRequest(
                    client.EqDirectory,
                    Path.Combine(temp.Path, "unspecified-ui"),
                    chatFontSizeIncrease: 0) with
                {
                    UiCompatibilityMode =
                        FourKayUiCompatibilityMode.UnspecifiedLegacy,
                })).ConfigureAwait(false);

        ResolutionPlan mismatchPlan = new ResolutionPlanner().CreatePlan(
            new PixelSize(2560, 1440),
            ResolutionPresetKind.Comfort);
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.PrepareAsync(new FourKayPreparationRequest
            {
                EqDirectory = client.EqDirectory,
                StateDirectory = Path.Combine(temp.Path, "mismatch"),
                ResolutionPlan = mismatchPlan,
                ChatFontSizeIncrease = 0,
            })).ConfigureAwait(false);

        processes.Current =
        [
            new ProcessDescriptor(98, client.EqGamePath, null),
        ];
        GameAlreadyRunningException running =
            await Assert.ThrowsAsync<GameAlreadyRunningException>(
                () => service.PrepareAsync(
                    CreatePreparationRequest(
                        client.EqDirectory,
                        Path.Combine(temp.Path, "running"),
                        0))).ConfigureAwait(false);
        Assert.Equal(98, running.ProcessIds.Single());
        Assert.Equal(0, dpi.CaptureCalls);
        Assert.Equal(0, dpi.ApplyCalls);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.PrepareAsync(
                CreatePreparationRequest(
                    Path.Combine(temp.Path, "missing-client"),
                    Path.Combine(temp.Path, "missing-state"),
                    0))).ConfigureAwait(false);
    }

    private static async Task PreparationInvalidNativeScalePreflightAsync()
    {
        await using TempDirectory temp = new();
        FakeClientFixture client = await FakeClientFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        string invalidText = Encoding.UTF8.GetString(client.OriginalIniBytes)
            .Replace("UIScale=0", "UIScale=not-a-number", StringComparison.Ordinal);
        byte[] invalidBytes = Encoding.UTF8.GetBytes(invalidText);
        await File.WriteAllBytesAsync(client.EqClientIniPath, invalidBytes)
            .ConfigureAwait(false);

        string stateDirectory = Path.Combine(temp.Path, "invalid-native-state");
        FakeDpiCompatibilityService dpi = new();
        FourKayPreparationService service = new(
            new BackupService(),
            new EqClientConfigurationService(),
            dpi,
            new FakeProcessDiscoveryService(),
            new FourKayJournalStore(),
            FakeWindowPlacementService.Create4K());

        InvalidDataException failure =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => service.PrepareAsync(
                    CreatePreparationRequest(
                        client.EqDirectory,
                        stateDirectory,
                        0))).ConfigureAwait(false);
        Assert.Contains("before creating a backup", failure.Message);
        Assert.SequenceEqual(
            invalidBytes,
            await File.ReadAllBytesAsync(client.EqClientIniPath).ConfigureAwait(false));
        Assert.Equal(0, dpi.CaptureCalls);
        Assert.Equal(0, dpi.ApplyCalls);
        Assert.False(Directory.Exists(Path.Combine(stateDirectory, "backups")));
        Assert.False(Directory.Exists(Path.Combine(stateDirectory, "sessions")));
    }

    private static async Task LaunchRequestValidationAsync()
    {
        await using TempDirectory temp = new();
        FourKayPreparedState prepared = CreateJournalState(
            temp.Path,
            FourKayJournalStatus.Prepared,
            DateTimeOffset.UnixEpoch);
        FakeProcessDiscoveryService processes = new();
        FakeWindowDiscoveryService windows = new();
        FakeWindowPlacementService placement = FakeWindowPlacementService.Create4K();
        FakeMagpiePortableConfigService config = new();
        FakeMagpieProcessService magpie = new();
        FakeScalingWindowInspector inspector = new();
        FourKayLaunchService service = new(
            processes,
            windows,
            placement,
            config,
            magpie,
            inspector);

        MethodInfo validateLaunchRequest = typeof(FourKayLaunchService).GetMethod(
            "ValidateLaunchRequest",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new TestFailureException(
                "The launch request validator is unavailable.");
        _ = validateLaunchRequest.Invoke(
            null,
            [CreateLaunchRequest(
                prepared with
                {
                    Status = FourKayJournalStatus.Committed,
                    KeepPreparedConfiguration = true,
                })]);
        MethodInfo getMagpieLauncherPath = typeof(FourKayLaunchService).GetMethod(
            "GetMagpieLauncherPath",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new TestFailureException(
                "The Magpie launcher-path policy is unavailable.");
        FourKayLaunchRequest enhancedRequest = CreateLaunchRequest(prepared) with
        {
            StartMode = FourKayGameStartMode.SpinTextureEnhanced,
            LauncherPath = Path.Combine(temp.Path, "SpinTexture.exe"),
            LauncherArguments = ["--play-enhanced", prepared.EqDirectory],
        };
        _ = validateLaunchRequest.Invoke(null, [enhancedRequest]);
        Assert.Null(getMagpieLauncherPath.Invoke(null, [enhancedRequest]));
        Assert.Equal(
            CreateLaunchRequest(prepared).LauncherPath,
            getMagpieLauncherPath.Invoke(
                null,
                [CreateLaunchRequest(prepared)]));

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.LaunchAndScaleAsync(null!)).ConfigureAwait(false);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.LaunchAndScaleAsync(CreateLaunchRequest(
                prepared with { Status = FourKayJournalStatus.Preparing })))
            .ConfigureAwait(false);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.LaunchAndScaleAsync(CreateLaunchRequest(
                prepared with
                {
                    UiCompatibilityMode =
                        FourKayUiCompatibilityMode.UnspecifiedLegacy,
                }))).ConfigureAwait(false);
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.LaunchAndScaleAsync(
                CreateLaunchRequest(prepared) with { LauncherPath = " " }))
            .ConfigureAwait(false);
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.LaunchAndScaleAsync(
                CreateLaunchRequest(prepared) with { MagpieDirectory = " " }))
            .ConfigureAwait(false);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.LaunchAndScaleAsync(
                CreateLaunchRequest(prepared) with
                {
                    StartMode = (FourKayGameStartMode)999,
                })).ConfigureAwait(false);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.LaunchAndScaleAsync(
                CreateLaunchRequest(prepared) with
                {
                    LauncherArguments = ["patchme"],
                })).ConfigureAwait(false);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.LaunchAndScaleAsync(enhancedRequest with
            {
                LauncherPath = Path.Combine(temp.Path, "UnknownTool.exe"),
            })).ConfigureAwait(false);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.LaunchAndScaleAsync(enhancedRequest with
            {
                LauncherArguments =
                ["--play-enhanced", prepared.EqDirectory, "ticket"],
            })).ConfigureAwait(false);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.LaunchAndScaleAsync(enhancedRequest with
            {
                LauncherArguments =
                ["--play-enhanced", Path.Combine(temp.Path, "Other EQ")],
            })).ConfigureAwait(false);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.LaunchAndScaleAsync(
                CreateLaunchRequest(prepared) with { GameStartTimeout = TimeSpan.Zero }))
            .ConfigureAwait(false);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.LaunchAndScaleAsync(
                CreateLaunchRequest(prepared) with { WindowTimeout = TimeSpan.Zero }))
            .ConfigureAwait(false);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.LaunchAndScaleAsync(
                CreateLaunchRequest(prepared) with { ScalingTimeout = TimeSpan.Zero }))
            .ConfigureAwait(false);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.AttachExistingAsync(new FourKayAttachRequest
            {
                EqDirectory = " ",
                MagpieDirectory = "magpie",
                Filter = ScalingFilter.Fsr,
            })).ConfigureAwait(false);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.AttachExistingAsync(new FourKayAttachRequest
            {
                EqDirectory = "eq",
                MagpieDirectory = "magpie",
                Filter = ScalingFilter.Fsr,
                WindowTimeout = TimeSpan.Zero,
            })).ConfigureAwait(false);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AttachExistingAsync(new FourKayAttachRequest
            {
                EqDirectory = "eq",
                MagpieDirectory = "magpie",
                Filter = ScalingFilter.Fsr,
                RequestedGenericClientSize = new PixelSize(1920, 1080),
                UiCompatibilityMode = FourKayUiCompatibilityMode.SpinUiStrict,
            })).ConfigureAwait(false);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.AttachExistingAsync(new FourKayAttachRequest
            {
                EqDirectory = "eq",
                MagpieDirectory = "magpie",
                Filter = ScalingFilter.Fsr,
                RequestedGenericClientSize = default(PixelSize),
            })).ConfigureAwait(false);

        Assert.Equal(0, magpie.InspectCalls);
        Assert.Equal(0, magpie.StartCalls);
        Assert.Equal(0, processes.FindCalls);
        Assert.Equal(0, config.WriteCalls);
        Assert.Equal(0, windows.InvocationCount);
        Assert.Equal(0, inspector.InvocationCount);
    }

    private static async Task
        AttachExpectedProcessIdentityRejectsBeforeEffectsAsync()
    {
        await using TempDirectory temp = new();
        FakeClientFixture client = await FakeClientFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        int processId = Environment.ProcessId;
        DateTimeOffset discoveredStart = DateTimeOffset.UnixEpoch;
        FakeProcessDiscoveryService processes = new()
        {
            Current =
            [
                new ProcessDescriptor(
                    processId,
                    client.EqGamePath,
                    discoveredStart),
            ],
        };
        FakeWindowDiscoveryService windows = new();
        FakeWindowPlacementService placement =
            FakeWindowPlacementService.Create4K();
        FakeMagpiePortableConfigService config = new();
        FakeMagpieProcessService magpie = new();
        FakeScalingWindowInspector inspector = new();
        FourKayLaunchService service = new(
            processes,
            windows,
            placement,
            config,
            magpie,
            inspector);

        FourKayAttachRequest validBase = new()
        {
            EqDirectory = client.EqDirectory,
            MagpieDirectory = @"C:\BundledMagpie",
            Filter = ScalingFilter.Fsr,
            ExpectedProcessId = processId,
            ExpectedProcessStartTimeUtc = discoveredStart,
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.AttachExistingAsync(
                validBase with { ExpectedProcessStartTimeUtc = null }))
            .ConfigureAwait(false);
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.AttachExistingAsync(
                validBase with { ExpectedProcessId = null }))
            .ConfigureAwait(false);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.AttachExistingAsync(
                validBase with
                {
                    ExpectedProcessId = 0,
                    ExpectedProcessStartTimeUtc = discoveredStart,
                }))
            .ConfigureAwait(false);

        Assert.Equal(0, magpie.InspectCalls);
        Assert.Equal(0, processes.FindCalls);
        Assert.Equal(0, windows.InvocationCount);
        Assert.Equal(0, placement.PlacementCalls);
        Assert.Equal(0, config.PrepareCalls);
        Assert.Equal(0, config.WriteCalls);
        Assert.Equal(0, magpie.StartCalls);
        Assert.Equal(0, inspector.InvocationCount);

        InvalidOperationException replacement =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.AttachExistingAsync(
                    validBase with
                    {
                        ExpectedProcessStartTimeUtc =
                            discoveredStart.AddSeconds(1),
                    })).ConfigureAwait(false);
        Assert.Contains("process changed", replacement.Message);
        Assert.Contains("No game window was resized", replacement.Message);
        Assert.Equal(1, processes.FindCalls);
        Assert.Equal(0, windows.InvocationCount);
        Assert.Equal(0, placement.CaptureCalls);
        Assert.Equal(0, placement.PlacementCalls);
        Assert.Equal(0, placement.RestoreCalls);
        Assert.Equal(0, config.PrepareCalls);
        Assert.Equal(0, config.WriteCalls);
        Assert.Equal(0, magpie.StartCalls);
        Assert.Equal(0, magpie.ShutdownExactCalls);
        Assert.Equal(0, inspector.InvocationCount);
    }

    private static async Task LaunchConfigurationIntegrityGuardsAsync()
    {
        await using TempDirectory temp = new();
        FakeClientFixture client = await FakeClientFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        FourKayPreparedState prepared = CreateJournalState(
            temp.Path,
            FourKayJournalStatus.Prepared,
            DateTimeOffset.UnixEpoch) with
        {
            EqDirectory = client.EqDirectory,
            EqGamePath = client.EqGamePath,
            EqClientIniPath = client.EqClientIniPath,
            DpiCompatibility =
                CreateJournalState(
                    temp.Path,
                    FourKayJournalStatus.Prepared,
                    DateTimeOffset.UnixEpoch).DpiCompatibility with
                {
                    ExecutablePath = client.EqGamePath,
                },
            AppliedIniSha256 = Convert.ToHexString(
                SHA256.HashData(client.OriginalIniBytes)),
            UiCompatibilityMode = FourKayUiCompatibilityMode.GenericOrCustom,
        };
        FakeProcessDiscoveryService launchProcesses = new();
        FakeMagpiePortableConfigService launchConfig = new();
        FakeMagpieProcessService launchMagpie = new();
        FourKayLaunchService launchService = new(
            launchProcesses,
            new FakeWindowDiscoveryService(),
            FakeWindowPlacementService.Create4K(),
            launchConfig,
            launchMagpie,
            new FakeScalingWindowInspector());

        FakeClientFixture otherClient = await FakeClientFixture.CreateAsync(
            Path.Combine(temp.Path, "other")).ConfigureAwait(false);
        InvalidDataException crossClient =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => launchService.LaunchAndScaleAsync(
                    CreateLaunchRequest(prepared with
                    {
                        EqGamePath = otherClient.EqGamePath,
                    }))).ConfigureAwait(false);
        Assert.Contains("same selected EverQuest Legends", crossClient.Message);
        Assert.Equal(0, launchProcesses.FindCalls);
        Assert.Equal(0, launchConfig.WriteCalls);

        await File.AppendAllTextAsync(
            client.EqClientIniPath,
            "ChangedAfterPrepare=1\r\n").ConfigureAwait(false);
        InvalidDataException changed =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => launchService.LaunchAndScaleAsync(
                    CreateLaunchRequest(prepared) with
                    {
                        LauncherPath = Path.Combine(
                            client.EqDirectory,
                            "LaunchPad.exe"),
                    })).ConfigureAwait(false);
        Assert.Contains("changed after this session was prepared", changed.Message);
        Assert.Equal(0, launchProcesses.FindCalls);
        Assert.Equal(0, launchConfig.WriteCalls);
        Assert.Equal(0, launchMagpie.StartCalls);

        // A non-native, missing, or absent UIScale is advisory-only for Attach:
        // the read-only check never blocks scaling and never edits the file.
        // With no running game process, Attach proceeds past the advisory and
        // stops at process discovery without touching any file.
        string nonNativeText = Encoding.UTF8.GetString(client.OriginalIniBytes)
            .Replace("UIScale=0", "UIScale=2", StringComparison.Ordinal);
        await File.WriteAllTextAsync(client.EqClientIniPath, nonNativeText)
            .ConfigureAwait(false);
        FakeProcessDiscoveryService attachProcesses = new();
        FakeMagpiePortableConfigService attachConfig = new();
        FakeMagpieProcessService attachMagpie = new();
        FourKayLaunchService attachService = new(
            attachProcesses,
            new FakeWindowDiscoveryService(),
            FakeWindowPlacementService.Create4K(),
            attachConfig,
            attachMagpie,
            new FakeScalingWindowInspector());
        InvalidOperationException stacked =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => attachService.AttachExistingAsync(new FourKayAttachRequest
                {
                    EqDirectory = client.EqDirectory,
                    MagpieDirectory = @"C:\BundledMagpie",
                    Filter = ScalingFilter.Fsr,
                    TargetMonitor = new nint(42),
                })).ConfigureAwait(false);
        Assert.Contains("not running", stacked.Message);
        Assert.Equal(1, attachProcesses.FindCalls);
        Assert.Equal(0, attachConfig.WriteCalls);
        Assert.Equal(0, attachMagpie.StartCalls);
        Assert.Equal(
            nonNativeText,
            await File.ReadAllTextAsync(client.EqClientIniPath)
                .ConfigureAwait(false));

        string missingText = Encoding.UTF8.GetString(client.OriginalIniBytes)
            .Replace("UIScale=0\r\n", string.Empty, StringComparison.Ordinal);
        await File.WriteAllTextAsync(client.EqClientIniPath, missingText)
            .ConfigureAwait(false);
        InvalidOperationException missing =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => attachService.AttachExistingAsync(new FourKayAttachRequest
                {
                    EqDirectory = client.EqDirectory,
                    MagpieDirectory = @"C:\BundledMagpie",
                    Filter = ScalingFilter.Fsr,
                    TargetMonitor = new nint(42),
                })).ConfigureAwait(false);
        Assert.Contains("not running", missing.Message);
        Assert.Equal(2, attachProcesses.FindCalls);
        Assert.Equal(0, attachConfig.WriteCalls);
        Assert.Equal(0, attachMagpie.StartCalls);
        Assert.Equal(
            missingText,
            await File.ReadAllTextAsync(client.EqClientIniPath)
                .ConfigureAwait(false));

        File.Delete(client.EqClientIniPath);
        InvalidOperationException absentIni =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => attachService.AttachExistingAsync(new FourKayAttachRequest
                {
                    EqDirectory = client.EqDirectory,
                    MagpieDirectory = @"C:\BundledMagpie",
                    Filter = ScalingFilter.Fsr,
                    TargetMonitor = new nint(42),
                })).ConfigureAwait(false);
        Assert.Contains("not running", absentIni.Message);
        Assert.Equal(3, attachProcesses.FindCalls);
        Assert.Equal(0, attachConfig.WriteCalls);
        Assert.Equal(0, attachMagpie.StartCalls);
        Assert.False(File.Exists(client.EqClientIniPath));
    }

    private static async Task LaunchTargetMonitorMismatchRejectsBeforeEffectsAsync()
    {
        await using TempDirectory temp = new();
        FakeClientFixture client = await FakeClientFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        FourKayPreparedState prepared = CreateJournalState(
            temp.Path,
            FourKayJournalStatus.Prepared,
            DateTimeOffset.UnixEpoch) with
        {
            EqDirectory = client.EqDirectory,
            EqGamePath = client.EqGamePath,
            EqClientIniPath = client.EqClientIniPath,
            DpiCompatibility =
                CreateJournalState(
                    temp.Path,
                    FourKayJournalStatus.Prepared,
                    DateTimeOffset.UnixEpoch).DpiCompatibility with
                {
                    ExecutablePath = client.EqGamePath,
                },
            AppliedIniSha256 = Convert.ToHexString(
                SHA256.HashData(client.OriginalIniBytes)),
            UiCompatibilityMode = FourKayUiCompatibilityMode.GenericOrCustom,
        };
        FakeProcessDiscoveryService processes = new();
        FakeMagpiePortableConfigService config = new();
        FakeMagpieProcessService magpie = new();
        FourKayLaunchService service = new(
            processes,
            new FakeWindowDiscoveryService(),
            FakeWindowPlacementService.Create4K(),
            config,
            magpie,
            new FakeScalingWindowInspector());
        string launcherPath = Path.Combine(client.EqDirectory, "LaunchPad.exe");

        InvalidOperationException movedBounds =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.LaunchAndScaleAsync(
                    CreateLaunchRequest(prepared with
                    {
                        TargetMonitorBounds = new PixelRect(0, 0, 2560, 1440),
                    }) with
                    {
                        LauncherPath = launcherPath,
                    })).ConfigureAwait(false);
        Assert.Contains(
            "wrong display or at the wrong size",
            movedBounds.Message);

        InvalidOperationException missingMonitor =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.LaunchAndScaleAsync(
                    CreateLaunchRequest(prepared with
                    {
                        TargetMonitorHandle = 43,
                    }) with
                    {
                        LauncherPath = launcherPath,
                    })).ConfigureAwait(false);
        Assert.Contains("no longer available", missingMonitor.Message);

        InvalidOperationException planMismatch =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.LaunchAndScaleAsync(
                    CreateLaunchRequest(prepared with
                    {
                        ResolutionPlan = new ResolutionPlanner().CreatePlan(
                            new PixelSize(2560, 1440),
                            ResolutionPresetKind.Comfort),
                    }) with
                    {
                        LauncherPath = launcherPath,
                    })).ConfigureAwait(false);
        Assert.Contains(
            "wrong display or at the wrong size",
            planMismatch.Message);

        Assert.Equal(0, processes.FindCalls);
        Assert.Equal(0, config.PrepareCalls);
        Assert.Equal(0, config.WriteCalls);
        Assert.Equal(0, magpie.StartCalls);
    }

    private static async Task LaunchExternalMagpieConflictAsync()
    {
        await using TempDirectory temp = new();
        FourKayPreparedState prepared = CreateJournalState(
            temp.Path,
            FourKayJournalStatus.Prepared,
            DateTimeOffset.UnixEpoch);
        FakeProcessDiscoveryService processes = new();
        FakeWindowDiscoveryService windows = new();
        FakeWindowPlacementService placement = FakeWindowPlacementService.Create4K();
        FakeMagpiePortableConfigService config = new();
        FakeMagpieProcessService magpie = new()
        {
            Instances =
            [
                new MagpieRunningInstance(
                    777,
                    @"C:\Tools\ExternalMagpie\Magpie.exe",
                    IsBundledInstance: false),
            ],
        };
        FakeScalingWindowInspector inspector = new();
        FourKayLaunchService service = new(
            processes,
            windows,
            placement,
            config,
            magpie,
            inspector);

        ExternalMagpieInstanceConflictException launchConflict =
            await Assert.ThrowsAsync<ExternalMagpieInstanceConflictException>(
                () => service.LaunchAndScaleAsync(CreateLaunchRequest(prepared)))
                .ConfigureAwait(false);
        Assert.Equal(777, launchConflict.ConflictingInstances.Single().ProcessId);

        ExternalMagpieInstanceConflictException attachConflict =
            await Assert.ThrowsAsync<ExternalMagpieInstanceConflictException>(
                () => service.AttachExistingAsync(new FourKayAttachRequest
                {
                    EqDirectory = prepared.EqDirectory,
                    MagpieDirectory = "bundled-magpie",
                    Filter = ScalingFilter.Fsr,
                })).ConfigureAwait(false);
        Assert.Equal(777, attachConflict.ConflictingInstances.Single().ProcessId);

        Assert.Equal(2, magpie.InspectCalls);
        Assert.Equal(0, magpie.ShutdownCalls);
        Assert.Equal(0, magpie.StartCalls);
        Assert.Equal(0, processes.FindCalls);
        Assert.Equal(0, processes.WaitCalls);
        Assert.Equal(0, config.WriteCalls);
        Assert.Equal(0, windows.InvocationCount);
        Assert.Equal(0, inspector.InvocationCount);
    }

    private static async Task AttachEnforcesExpectedClientResolutionAsync()
    {
        await using TempDirectory temp = new();
        FakeClientFixture client = await FakeClientFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        int currentProcessId = Environment.ProcessId;
        WindowDescriptor sourceWindow = new(
            new nint(123),
            currentProcessId,
            "EverQuest",
            "EQGameWindow",
            new PixelRect(20, 20, 1936, 1119),
            new PixelRect(28, 59, 1920, 1080),
            IsMinimized: false,
            ExecutablePath: client.EqGamePath);
        FakeProcessDiscoveryService processes = new()
        {
            Current =
            [
                new ProcessDescriptor(
                    currentProcessId,
                    client.EqGamePath,
                    DateTimeOffset.UnixEpoch),
            ],
        };
        FakeWindowDiscoveryService windows = new()
        {
            AllowCalls = true,
            StableResult = sourceWindow,
        };
        FakeWindowPlacementService placement = FakeWindowPlacementService.Create4K();
        FakeMagpiePortableConfigService config = new();
        FakeMagpieProcessService magpie = new();
        FakeScalingWindowInspector inspector = new();
        FourKayLaunchService service = new(
            processes,
            windows,
            placement,
            config,
            magpie,
            inspector);

        UnexpectedClientResolutionException mismatch =
            await Assert.ThrowsAsync<UnexpectedClientResolutionException>(
                () => service.AttachExistingAsync(new FourKayAttachRequest
                {
                    EqDirectory = client.EqDirectory,
                    MagpieDirectory = @"C:\BundledMagpie",
                    Filter = ScalingFilter.Fsr,
                    ExpectedClientSize = new PixelSize(2560, 1080),
                    TargetMonitor = new nint(42),
                })).ConfigureAwait(false);
        Assert.Equal(new PixelSize(2560, 1080), mismatch.Expected);
        Assert.Equal(new PixelSize(1920, 1080), mismatch.Actual);
        Assert.Equal(0, placement.PlacementCalls);
        Assert.Equal(0, config.WriteCalls);
        Assert.Equal(0, magpie.StartCalls);
        Assert.Equal(0, inspector.InvocationCount);
        Assert.True(windows.LastProcess is not null);
        Assert.Throws<InvalidOperationException>(
            () => _ = windows.LastProcess!.Handle);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.AttachExistingAsync(new FourKayAttachRequest
            {
                EqDirectory = client.EqDirectory,
                MagpieDirectory = @"C:\BundledMagpie",
                Filter = ScalingFilter.Fsr,
                ExpectedClientSize = default(PixelSize),
            })).ConfigureAwait(false);
        Assert.Equal(1, magpie.InspectCalls);
    }

    private static async Task AttachGenericRequestedClientSizeAsync()
    {
        await using TempDirectory temp = new();
        FakeClientFixture client = await FakeClientFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        int processId = Environment.ProcessId;
        PixelSize requestedSize = new(2560, 1440);
        WindowDescriptor original = CreateLaunchAcceptanceSourceWindow(
            client,
            processId);
        WindowDescriptor resized = original with
        {
            WindowBounds = new PixelRect(20, 20, 2576, 1479),
            ClientBounds = new PixelRect(
                original.ClientBounds.X,
                original.ClientBounds.Y,
                requestedSize.Width,
                requestedSize.Height),
        };
        FakeProcessDiscoveryService processes = new()
        {
            Current =
            [
                new ProcessDescriptor(
                    processId,
                    client.EqGamePath,
                    DateTimeOffset.UnixEpoch),
            ],
        };
        FakeWindowDiscoveryService windows = new()
        {
            AllowCalls = true,
        };
        windows.StableResults.Enqueue(original);
        windows.StableResults.Enqueue(resized);
        FakeWindowPlacementService placement =
            FakeWindowPlacementService.Create4K();
        FakeMagpiePortableConfigService config = new();
        FakeMagpieProcessService magpie = new()
        {
            StartResultFactory = () => new MagpieProcessStartResult(
                Process.GetCurrentProcess(),
                AlreadyRunning: false),
            ShutdownExactResult = true,
        };
        FakeScalingWindowInspector inspector = new()
        {
            AllowCleanupCalls = true,
            WaitResult = CreateExactSafeScalingInspection(
                processId,
                original.Handle,
                requestedSize,
                new PixelRect(0, 0, 3840, 2160)),
            InspectionResults = new Queue<MagpieScalingWindowInspection>(
            [
                CreateScalingInspection(isActive: false),
                CreateScalingInspection(isActive: false),
            ]),
        };
        FourKayLaunchService service = new(
            processes,
            windows,
            placement,
            config,
            magpie,
            inspector);

        byte[] iniBefore = await File.ReadAllBytesAsync(client.EqClientIniPath)
            .ConfigureAwait(false);
        FourKayLaunchResult? result = null;
        try
        {
            result = await service.AttachExistingAsync(new FourKayAttachRequest
            {
                EqDirectory = client.EqDirectory,
                MagpieDirectory = @"C:\BundledMagpie",
                Filter = ScalingFilter.Fsr,
                RequestedGenericClientSize = requestedSize,
                TargetMonitor = new nint(42),
                WindowTimeout = TimeSpan.FromSeconds(1),
                ScalingTimeout = TimeSpan.FromSeconds(1),
                UiCompatibilityMode =
                    FourKayUiCompatibilityMode.GenericOrCustom,
            }).ConfigureAwait(false);

            Assert.Equal(requestedSize, result.Placement.RequestedClientSize);
            Assert.Equal(requestedSize.Width, result.SourceWindow.ClientBounds.Width);
            Assert.Equal(requestedSize.Height, result.SourceWindow.ClientBounds.Height);
            Assert.Equal(ScalingFilter.Fsr, result.EffectiveFilter);
            Assert.Equal(1, placement.PlacementCalls);
            Assert.Equal(requestedSize, placement.RequestedClientSizes.Single());
            Assert.Equal(0, placement.RestoreCalls);
            Assert.True(
                result.Warnings.Any(
                    warning => warning.Contains(
                        "resized from 1920x1080 to 2560x1440",
                        StringComparison.Ordinal)));
            Assert.True(
                iniBefore.SequenceEqual(
                    await File.ReadAllBytesAsync(client.EqClientIniPath)
                        .ConfigureAwait(false)));
        }
        finally
        {
            result?.GameProcess.Dispose();
            result?.MagpieProcess.Process.Dispose();
        }
    }

    private static async Task AttachGenericResizeFailureRestoresExactGeometryAsync()
    {
        await using TempDirectory temp = new();
        FakeClientFixture client = await FakeClientFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        int processId = Environment.ProcessId;
        WindowDescriptor original = CreateLaunchAcceptanceSourceWindow(
            client,
            processId);
        WindowDescriptor replacement = original with { Handle = new nint(999) };
        FakeProcessDiscoveryService processes = new()
        {
            Current =
            [
                new ProcessDescriptor(
                    processId,
                    client.EqGamePath,
                    DateTimeOffset.UnixEpoch),
            ],
        };
        FakeWindowDiscoveryService windows = new() { AllowCalls = true };
        windows.StableResults.Enqueue(original);
        windows.StableResults.Enqueue(replacement);
        windows.StableResults.Enqueue(original);
        FakeWindowPlacementService placement =
            FakeWindowPlacementService.Create4K();
        FakeMagpiePortableConfigService config = new();
        FakeMagpieProcessService magpie = new();
        FourKayLaunchService service = new(
            processes,
            windows,
            placement,
            config,
            magpie,
            new FakeScalingWindowInspector());

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.AttachExistingAsync(new FourKayAttachRequest
                {
                    EqDirectory = client.EqDirectory,
                    MagpieDirectory = @"C:\BundledMagpie",
                    Filter = ScalingFilter.Fsr,
                    RequestedGenericClientSize = new PixelSize(2560, 1440),
                    TargetMonitor = new nint(42),
                    WindowTimeout = TimeSpan.FromSeconds(1),
                    ScalingTimeout = TimeSpan.FromSeconds(1),
                })).ConfigureAwait(false);
        Assert.Contains("exact process, HWND", failure.Message);
        Assert.Equal(1, placement.PlacementCalls);
        Assert.Equal(1, placement.RestoreCalls);
        Assert.Equal(original.WindowBounds, placement.LastRestoredWindowBounds);
        Assert.Equal(
            new PixelSize(1920, 1080),
            placement.LastRestoredClientSize);
        Assert.Equal(0, config.WriteCalls);
        Assert.Equal(0, magpie.StartCalls);
    }

    private static async Task AttachGenericResizeRollbackFailureIsFailClosedAsync()
    {
        await using TempDirectory temp = new();
        FakeClientFixture client = await FakeClientFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        int processId = Environment.ProcessId;
        WindowDescriptor original = CreateLaunchAcceptanceSourceWindow(
            client,
            processId);
        FakeProcessDiscoveryService processes = new()
        {
            Current =
            [
                new ProcessDescriptor(
                    processId,
                    client.EqGamePath,
                    DateTimeOffset.UnixEpoch),
            ],
        };
        FakeWindowDiscoveryService windows = new() { AllowCalls = true };
        windows.StableResults.Enqueue(original);
        windows.StableResults.Enqueue(original with { Handle = new nint(999) });
        FakeWindowPlacementService placement =
            FakeWindowPlacementService.Create4K();
        placement.ExactRestoreResult = false;
        FakeMagpiePortableConfigService config = new();
        FakeMagpieProcessService magpie = new();
        FourKayLaunchService service = new(
            processes,
            windows,
            placement,
            config,
            magpie,
            new FakeScalingWindowInspector());

        FourKayAttachResizeException failure =
            await Assert.ThrowsAsync<FourKayAttachResizeException>(
                () => service.AttachExistingAsync(new FourKayAttachRequest
                {
                    EqDirectory = client.EqDirectory,
                    MagpieDirectory = @"C:\BundledMagpie",
                    Filter = ScalingFilter.Fsr,
                    RequestedGenericClientSize = new PixelSize(2560, 1440),
                    TargetMonitor = new nint(42),
                    WindowTimeout = TimeSpan.FromSeconds(1),
                    ScalingTimeout = TimeSpan.FromSeconds(1),
                })).ConfigureAwait(false);
        Assert.Contains("exact process, HWND", failure.AttachFailure.Message);
        Assert.Contains(
            "previous game-window geometry was not restored exactly",
            failure.RollbackFailure.Message);
        Assert.Equal(1, placement.RestoreCalls);
        Assert.Equal(0, config.WriteCalls);
        Assert.Equal(0, magpie.StartCalls);
    }

    private static async Task AttachSameSizeFailuresRestoreFullPlacementAsync()
    {
        (FourKayUiCompatibilityMode Mode, PixelSize? ExpectedSize)[] cases =
        [
            (FourKayUiCompatibilityMode.GenericOrCustom, null),
            (FourKayUiCompatibilityMode.SpinUiStrict, new PixelSize(1920, 1080)),
        ];

        foreach ((FourKayUiCompatibilityMode mode, PixelSize? expectedSize) in cases)
        {
            await using TempDirectory temp = new();
            FakeClientFixture client = await FakeClientFixture.CreateAsync(temp.Path)
                .ConfigureAwait(false);
            int processId = Environment.ProcessId;
            WindowDescriptor original = CreateLaunchAcceptanceSourceWindow(
                client,
                processId);
            WindowGeometrySnapshot originalGeometry =
                CreateMaximizedGeometrySnapshot(original);
            FakeProcessDiscoveryService processes = new()
            {
                Current =
                [
                    new ProcessDescriptor(
                        processId,
                        client.EqGamePath,
                        DateTimeOffset.UnixEpoch),
                ],
            };
            FakeWindowDiscoveryService windows = new() { AllowCalls = true };
            windows.StableResults.Enqueue(original);
            windows.StableResults.Enqueue(
                original with { Handle = new nint(999) });
            windows.StableResults.Enqueue(original);
            FakeWindowPlacementService placement =
                FakeWindowPlacementService.Create4K();
            placement.CurrentGeometry = originalGeometry;
            FakeMagpiePortableConfigService config = new();
            FakeMagpieProcessService magpie = new();
            FourKayLaunchService service = new(
                processes,
                windows,
                placement,
                config,
                magpie,
                new FakeScalingWindowInspector());

            InvalidOperationException failure =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => service.AttachExistingAsync(new FourKayAttachRequest
                    {
                        EqDirectory = client.EqDirectory,
                        MagpieDirectory = @"C:\BundledMagpie",
                        Filter = ScalingFilter.Fsr,
                        ExpectedClientSize = expectedSize,
                        TargetMonitor = new nint(42),
                        WindowTimeout = TimeSpan.FromSeconds(1),
                        ScalingTimeout = TimeSpan.FromSeconds(1),
                        UiCompatibilityMode = mode,
                    })).ConfigureAwait(false);

            Assert.Contains("exact process, HWND", failure.Message);
            Assert.Equal(1, placement.PlacementCalls);
            Assert.Equal(1, placement.RestoreCalls);
            Assert.Equal(originalGeometry, placement.LastRestoredGeometry!);
            Assert.Equal(3u, placement.LastRestoredGeometry!.ShowCommand);
            Assert.Equal(2, placement.CaptureCalls);
            Assert.Equal(0, config.PrepareCalls);
            Assert.Equal(0, magpie.StartCalls);
        }
    }

    private static async Task AttachPostScalerFailureRestoresFullPlacementAsync()
    {
        await using TempDirectory temp = new();
        FakeClientFixture client = await FakeClientFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        int processId = Environment.ProcessId;
        PixelSize requestedSize = new(2560, 1440);
        WindowDescriptor original = CreateLaunchAcceptanceSourceWindow(
            client,
            processId);
        WindowDescriptor resized = original with
        {
            WindowBounds = new PixelRect(20, 20, 2576, 1479),
            ClientBounds = new PixelRect(
                original.ClientBounds.X,
                original.ClientBounds.Y,
                requestedSize.Width,
                requestedSize.Height),
        };
        WindowGeometrySnapshot originalGeometry =
            CreateMaximizedGeometrySnapshot(original);
        List<string> events = [];
        FakeProcessDiscoveryService processes = new()
        {
            Current =
            [
                new ProcessDescriptor(
                    processId,
                    client.EqGamePath,
                    DateTimeOffset.UnixEpoch),
            ],
        };
        FakeWindowDiscoveryService windows = new() { AllowCalls = true };
        windows.StableResults.Enqueue(original);
        windows.StableResults.Enqueue(resized);
        windows.StableResults.Enqueue(original);
        windows.VisibleWindowFallback = [original];
        FakeWindowPlacementService placement =
            FakeWindowPlacementService.Create4K();
        placement.CurrentGeometry = originalGeometry;
        placement.Events = events;
        FakeMagpiePortableConfigService config = new() { Events = events };
        FakeMagpieProcessService magpie = new()
        {
            Events = events,
            StartResultFactory = () => new MagpieProcessStartResult(
                Process.GetCurrentProcess(),
                AlreadyRunning: false),
            ShutdownExactResult = true,
        };
        MagpieScalingWindowInspection unsafeInspection =
            CreateExactSafeScalingInspection(
                processId,
                original.Handle,
                requestedSize,
                new PixelRect(0, 0, 3840, 2160)) with
            {
                IsSafeForInput = false,
                Issues = ["injected unsafe post-scaler mapping"],
            };
        FakeScalingWindowInspector inspector = new()
        {
            AllowCleanupCalls = true,
            Events = events,
            WaitResult = unsafeInspection,
            StopResult = true,
            InspectionResults = new Queue<MagpieScalingWindowInspection>(
            [
                CreateScalingInspection(isActive: false),
                CreateScalingInspection(isActive: false),
            ]),
        };
        FourKayLaunchService service = new(
            processes,
            windows,
            placement,
            config,
            magpie,
            inspector);

        UnsafeScalingAttachmentException failure =
            await Assert.ThrowsAsync<UnsafeScalingAttachmentException>(
                () => service.AttachExistingAsync(new FourKayAttachRequest
                {
                    EqDirectory = client.EqDirectory,
                    MagpieDirectory = @"C:\BundledMagpie",
                    Filter = ScalingFilter.Fsr,
                    RequestedGenericClientSize = requestedSize,
                    TargetMonitor = new nint(42),
                    WindowTimeout = TimeSpan.FromSeconds(1),
                    ScalingTimeout = TimeSpan.FromSeconds(1),
                    UiCompatibilityMode =
                        FourKayUiCompatibilityMode.GenericOrCustom,
                })).ConfigureAwait(false);

        Assert.Contains("unsafe post-scaler", failure.Message);
        Assert.Equal(1, placement.RestoreCalls);
        Assert.Equal(originalGeometry, placement.LastRestoredGeometry!);
        Assert.Equal(1, magpie.StartCalls);
        Assert.Equal(1, magpie.ShutdownExactCalls);
        Assert.Equal(1, config.RollbackCalls);
        int shutdownIndex = events.IndexOf("shutdown-exact");
        int rollbackIndex = events.IndexOf("rollback");
        int restoreIndex = events.IndexOf("restore-geometry");
        Assert.True(shutdownIndex >= 0);
        Assert.True(rollbackIndex > shutdownIndex);
        Assert.True(restoreIndex > rollbackIndex);
    }

    private static async Task AttachCleanupLeaseDefersGeometryAndRetriesAsync()
    {
        await using TempDirectory temp = new();
        FakeClientFixture client = await FakeClientFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        int processId = Environment.ProcessId;
        PixelSize requestedSize = new(2560, 1440);
        WindowDescriptor original = CreateLaunchAcceptanceSourceWindow(
            client,
            processId);
        WindowDescriptor resized = original with
        {
            WindowBounds = new PixelRect(20, 20, 2576, 1479),
            ClientBounds = new PixelRect(
                original.ClientBounds.X,
                original.ClientBounds.Y,
                requestedSize.Width,
                requestedSize.Height),
        };
        WindowGeometrySnapshot originalGeometry =
            CreateMaximizedGeometrySnapshot(original);
        List<string> events = [];
        FakeProcessDiscoveryService processes = new()
        {
            Current =
            [
                new ProcessDescriptor(
                    processId,
                    client.EqGamePath,
                    DateTimeOffset.UnixEpoch),
            ],
        };
        FakeWindowDiscoveryService windows = new() { AllowCalls = true };
        windows.StableResults.Enqueue(original);
        windows.StableResults.Enqueue(resized);
        windows.StableResults.Enqueue(original);
        windows.VisibleWindowFallback = [original];
        FakeWindowPlacementService placement =
            FakeWindowPlacementService.Create4K();
        placement.CurrentGeometry = originalGeometry;
        placement.Events = events;
        FakeMagpiePortableConfigService config = new() { Events = events };
        FakeMagpieProcessService magpie = new()
        {
            Events = events,
            StartResultFactory = () => new MagpieProcessStartResult(
                Process.GetCurrentProcess(),
                AlreadyRunning: false),
            ShutdownExactResult = false,
        };
        FakeScalingWindowInspector inspector = new()
        {
            AllowCleanupCalls = true,
            Events = events,
            StopResult = true,
            WaitResult = CreateExactSafeScalingInspection(
                processId,
                original.Handle,
                requestedSize,
                new PixelRect(0, 0, 3840, 2160)) with
            {
                IsSafeForInput = false,
                Issues = ["injected unresolved cleanup"],
            },
            InspectionResults = new Queue<MagpieScalingWindowInspection>(
            [
                CreateScalingInspection(isActive: false),
                CreateScalingInspection(isActive: false),
            ]),
        };
        FourKayLaunchService service = new(
            processes,
            windows,
            placement,
            config,
            magpie,
            inspector);

        MagpieScalingCleanupException failure =
            await Assert.ThrowsAsync<MagpieScalingCleanupException>(
                () => service.AttachExistingAsync(new FourKayAttachRequest
                {
                    EqDirectory = client.EqDirectory,
                    MagpieDirectory = @"C:\BundledMagpie",
                    Filter = ScalingFilter.Fsr,
                    RequestedGenericClientSize = requestedSize,
                    TargetMonitor = new nint(42),
                    WindowTimeout = TimeSpan.FromSeconds(1),
                    ScalingTimeout = TimeSpan.FromSeconds(1),
                    UiCompatibilityMode =
                        FourKayUiCompatibilityMode.GenericOrCustom,
                })).ConfigureAwait(false);
        MagpieScalingCleanupLease lease = failure.CleanupLease;
        try
        {
            Assert.Equal(0, placement.RestoreCalls);
            Assert.Equal(0, config.RollbackCalls);

            magpie.ShutdownExactResult = true;
            inspector.InspectionResults.Enqueue(
                CreateScalingInspection(isActive: false));
            inspector.InspectionResults.Enqueue(
                CreateScalingInspection(isActive: false));
            inspector.InspectionResults.Enqueue(
                CreateScalingInspection(isActive: false));
            inspector.InspectionResults.Enqueue(
                CreateScalingInspection(isActive: false));
            placement.ExactRestoreResult = false;
            events.Clear();

            MagpieScalingCleanupResolution unresolved =
                await service.ResolveCleanupLeaseAsync(
                    lease,
                    TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            Assert.False(unresolved.Resolved);
            Assert.Contains("could not yet be restored", unresolved.Message);
            Assert.Equal(1, config.RollbackCalls);
            Assert.Equal(1, placement.RestoreCalls);
            Assert.True(
                events.IndexOf("rollback")
                < events.IndexOf("restore-geometry"));

            placement.ExactRestoreResult = true;
            events.Clear();
            MagpieScalingCleanupResolution resolved =
                await service.ResolveCleanupLeaseAsync(
                    lease,
                    TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            Assert.True(resolved.Resolved);
            Assert.Contains("show state were also restored", resolved.Message);
            Assert.Equal(2, config.RollbackCalls);
            Assert.Equal(2, placement.RestoreCalls);
            Assert.Equal(originalGeometry, placement.LastRestoredGeometry!);
            Assert.True(
                events.IndexOf("rollback")
                < events.IndexOf("restore-geometry"));
        }
        finally
        {
            lease.Dispose();
        }
    }

    private static async Task AttachCleanupLeaseReplacementIsTerminalAsync()
    {
        await using TempDirectory temp = new();
        FakeClientFixture client = await FakeClientFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        int processId = Environment.ProcessId;
        PixelSize sourceSize = new(1920, 1080);
        DateTimeOffset originalStart = DateTimeOffset.UnixEpoch;
        WindowDescriptor original = CreateLaunchAcceptanceSourceWindow(
            client,
            processId);
        FakeProcessDiscoveryService processes = new()
        {
            Current =
            [
                new ProcessDescriptor(
                    processId,
                    client.EqGamePath,
                    originalStart),
            ],
        };
        FakeWindowDiscoveryService windows = new()
        {
            AllowCalls = true,
            StableResult = original,
        };
        FakeWindowPlacementService placement =
            FakeWindowPlacementService.Create4K();
        placement.CurrentGeometry = CreateMaximizedGeometrySnapshot(original);
        FakeMagpiePortableConfigService config = new();
        FakeMagpieProcessService magpie = new()
        {
            StartResultFactory = () => new MagpieProcessStartResult(
                Process.GetCurrentProcess(),
                AlreadyRunning: false),
            ShutdownExactResult = false,
        };
        FakeScalingWindowInspector inspector = new()
        {
            AllowCleanupCalls = true,
            StopResult = true,
            WaitResult = CreateExactSafeScalingInspection(
                processId,
                original.Handle,
                sourceSize,
                new PixelRect(0, 0, 3840, 2160)) with
            {
                IsSafeForInput = false,
                Issues = ["injected cleanup requiring a retained lease"],
            },
            InspectionResults = new Queue<MagpieScalingWindowInspection>(
            [
                CreateScalingInspection(isActive: false),
                CreateScalingInspection(isActive: false),
            ]),
        };
        FourKayLaunchService service = new(
            processes,
            windows,
            placement,
            config,
            magpie,
            inspector);

        MagpieScalingCleanupException failure =
            await Assert.ThrowsAsync<MagpieScalingCleanupException>(
                () => service.AttachExistingAsync(new FourKayAttachRequest
                {
                    EqDirectory = client.EqDirectory,
                    MagpieDirectory = @"C:\BundledMagpie",
                    Filter = ScalingFilter.Fsr,
                    ExpectedClientSize = sourceSize,
                    TargetMonitor = new nint(42),
                    WindowTimeout = TimeSpan.FromSeconds(1),
                    ScalingTimeout = TimeSpan.FromSeconds(1),
                    UiCompatibilityMode =
                        FourKayUiCompatibilityMode.GenericOrCustom,
                })).ConfigureAwait(false);
        MagpieScalingCleanupLease lease = failure.CleanupLease;
        try
        {
            Assert.Equal(0, placement.RestoreCalls);
            Assert.Equal(0, config.RollbackCalls);
            Assert.Equal(2, windows.StableCalls);

            // Simulate PID reuse by another exact-path EQ process. The captured
            // start time distinguishes it from the original process.
            processes.Current =
            [
                new ProcessDescriptor(
                    processId,
                    client.EqGamePath,
                    originalStart.AddSeconds(1)),
            ];
            magpie.ShutdownExactResult = true;
            inspector.InspectionResults.Enqueue(
                CreateScalingInspection(isActive: false));
            inspector.InspectionResults.Enqueue(
                CreateScalingInspection(isActive: false));

            MagpieScalingCleanupResolution resolved =
                await service.ResolveCleanupLeaseAsync(
                    lease,
                    TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            Assert.True(resolved.Resolved);
            Assert.Contains("no live original window remains", resolved.Message);
            Assert.Contains("No replacement process or window was touched", resolved.Message);
            Assert.Equal(1, config.RollbackCalls);
            Assert.Equal(0, placement.RestoreCalls);
            Assert.Equal(1, placement.CaptureCalls);
            Assert.Equal(2, windows.StableCalls);
        }
        finally
        {
            lease.Dispose();
        }
    }

    private static async Task LaunchAttachmentIdentityAndGeometryAreExactAsync()
    {
        await using TempDirectory temp = new();
        FakeClientFixture client = await FakeClientFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        int ownedProcessId = Environment.ProcessId;
        int otherProcessId = ownedProcessId == int.MaxValue
            ? ownedProcessId - 1
            : ownedProcessId + 1;
        WindowDescriptor sourceWindow = CreateLaunchAcceptanceSourceWindow(
            client,
            ownedProcessId);
        PixelRect expectedMonitor = new(0, 0, 3840, 2160);
        MagpieScalingWindowInspection exact =
            CreateExactSafeScalingInspection(
                ownedProcessId,
                sourceWindow.Handle,
                new PixelSize(1920, 1080),
                expectedMonitor);
        var rejectionCases = new[]
        {
            (
                Name: "different owner PID",
                Inspection: exact with
                {
                    ScalingProcessId = otherProcessId,
                },
                ExpectedIssue: "not owned by the exact dedicated"),
            (
                Name: "missing source region",
                Inspection: exact with
                {
                    SourceRegion = null,
                },
                ExpectedIssue: "physical source region does not match"),
            (
                Name: "wrong source size",
                Inspection: exact with
                {
                    SourceRegion = new PixelRect(28, 59, 1923, 1083),
                },
                ExpectedIssue: "physical source region does not match"),
            (
                Name: "wrong monitor",
                Inspection: exact with
                {
                    ScalingWindowBounds = new PixelRect(3840, 0, 2560, 1440),
                    MonitorBounds = new PixelRect(3840, 0, 2560, 1440),
                },
                ExpectedIssue: "filled a different monitor"),
        };

        foreach (var rejection in rejectionCases)
        {
            LaunchAcceptanceHarness harness =
                CreateLaunchAcceptanceHarness(
                    client,
                    sourceWindow,
                    rejection.Inspection);
            UnsafeScalingAttachmentException failure =
                await Assert.ThrowsAsync<UnsafeScalingAttachmentException>(
                    () => harness.Service.AttachExistingAsync(harness.Request))
                    .ConfigureAwait(false);
            Assert.Contains(rejection.ExpectedIssue, failure.Message);
            Assert.True(
                failure.Inspection.Issues.Any(
                    issue => issue.Contains(
                        rejection.ExpectedIssue,
                        StringComparison.Ordinal)),
                $"The {rejection.Name} case did not retain its exact issue.");
            Assert.False(failure.Inspection.IsSafeForInput);
            Assert.Equal(1, harness.Config.PrepareCalls);
            Assert.True(
                harness.Config.LastPreparedRequest?.AutoScaleFullscreen
                    == true);
            Assert.True(
                harness.Config.LastPreparedRequest?.ThreeDGameMode
                    == false);
            Assert.Equal(1, harness.Config.WriteCalls);
            Assert.Equal(1, harness.Config.RollbackCalls);
            Assert.Equal(1, harness.Magpie.StartCalls);
            Assert.Equal(1, harness.Magpie.ShutdownExactCalls);
            Assert.Equal(0, harness.Magpie.ShutdownCalls);
            Assert.Equal(1, harness.Inspector.StopCalls);
            Assert.Equal(
                ownedProcessId,
                harness.Inspector.LastExpectedScalingProcessId);
            Assert.True(harness.Magpie.StartedProcess is not null);
            Assert.Throws<InvalidOperationException>(
                () => _ = harness.Magpie.StartedProcess!.Handle);
        }

        MagpieScalingWindowInspection inactive = exact with
        {
            IsActive = false,
            IsSafeForInput = false,
            ScalingProcessId = null,
            MonitorBounds = null,
            SourceRegion = null,
            Issues = ["Magpie's scaling window is not active."],
        };
        LaunchAcceptanceHarness inactiveHarness =
            CreateLaunchAcceptanceHarness(client, sourceWindow, inactive);
        UnsafeScalingAttachmentException inactiveFailure =
            await Assert.ThrowsAsync<UnsafeScalingAttachmentException>(
                () => inactiveHarness.Service.AttachExistingAsync(
                    inactiveHarness.Request)).ConfigureAwait(false);
        Assert.Contains("scaling window is not active", inactiveFailure.Message);
        Assert.Contains("Engine log:", inactiveFailure.Message);
        Assert.DoesNotContain("not owned by the exact dedicated", inactiveFailure.Message);
        Assert.DoesNotContain("filled a different monitor", inactiveFailure.Message);
        Assert.DoesNotContain("physical source region", inactiveFailure.Message);
    }

    private static async Task LaunchExactAttachmentSucceedsWithoutCleanupAsync()
    {
        await using TempDirectory temp = new();
        FakeClientFixture client = await FakeClientFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        int ownedProcessId = Environment.ProcessId;
        WindowDescriptor sourceWindow = CreateLaunchAcceptanceSourceWindow(
            client,
            ownedProcessId);
        MagpieScalingWindowInspection exact =
            CreateExactSafeScalingInspection(
                ownedProcessId,
                sourceWindow.Handle,
                new PixelSize(1920, 1080),
                new PixelRect(0, 0, 3840, 2160));
        LaunchAcceptanceHarness harness =
            CreateLaunchAcceptanceHarness(client, sourceWindow, exact);
        List<string> events = [];
        harness.Config.Events = events;
        harness.Magpie.Events = events;
        harness.Inspector.Events = events;

        FourKayLaunchResult? result = null;
        try
        {
            result = await harness.Service.AttachExistingAsync(harness.Request)
                .ConfigureAwait(false);
            Assert.True(result.AttachedToExistingGame);
            Assert.True(result.ScalingInspection.IsSafeForInput);
            Assert.Equal(ownedProcessId, result.MagpieProcess.Process.Id);
            Assert.Equal(sourceWindow.Handle, result.SourceWindow.Handle);
            Assert.Equal(
                new PixelSize(1920, 1080),
                result.Placement.RequestedClientSize);
            Assert.Equal(
                new PixelRect(0, 0, 3840, 2160),
                result.ScalingInspection.MonitorBounds);
            Assert.Equal(1, harness.Config.PrepareCalls);
            Assert.True(
                harness.Config.LastPreparedRequest?.AutoScaleFullscreen
                    == true);
            Assert.True(
                harness.Config.LastPreparedRequest?.ThreeDGameMode
                    == false);
            MagpieProfileRequest preparedRequest =
                harness.Config.LastPreparedRequest
                ?? throw new TestFailureException(
                    "The launch did not prepare a Magpie profile request.");
            double expectedUiScaleFactor = Math.Min(
                (double)result.Placement.Monitor.Bounds.Width
                    / result.Placement.RequestedClientSize.Width,
                (double)result.Placement.Monitor.Bounds.Height
                    / result.Placement.RequestedClientSize.Height);
            Assert.NearlyEqual(
                expectedUiScaleFactor,
                preparedRequest.UiScaleFactor);
            Assert.Equal(1, harness.Config.WriteCalls);
            Assert.Equal(0, harness.Config.RollbackCalls);
            Assert.Equal(1, harness.Magpie.StartCalls);
            Assert.Equal(0, harness.Magpie.ShutdownExactCalls);
            Assert.Equal(0, harness.Magpie.ShutdownCalls);
            Assert.Equal(1, harness.Inspector.WaitCalls);
            Assert.Equal(0, harness.Inspector.StopCalls);
            Assert.Equal(1, harness.Inspector.InvocationCount);
            Assert.Equal(
                1,
                events.Count(item => item == "wait-scaling"));
        }
        finally
        {
            result?.LauncherProcess?.Dispose();
            result?.GameProcess.Dispose();
            result?.MagpieProcess.Process.Dispose();
        }
    }

    private static async Task LaunchOverlayCompatibilityActivatesAfterSafeAttachmentAsync()
    {
        await using TempDirectory temp = new();
        FakeClientFixture client = await FakeClientFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        int ownedProcessId = Environment.ProcessId;
        WindowDescriptor sourceWindow = CreateLaunchAcceptanceSourceWindow(
            client,
            ownedProcessId);
        MagpieScalingWindowInspection exact =
            CreateExactSafeScalingInspection(
                ownedProcessId,
                sourceWindow.Handle,
                new PixelSize(1920, 1080),
                new PixelRect(0, 0, 3840, 2160));
        PixelRect originalOverlayBounds = new(1600, 900, 260, 80);
        OverlayWindowSnapshot overlay = CreateOverlaySnapshot(
            new nint(701),
            processId: ownedProcessId + 100,
            originalOverlayBounds);
        FakeOverlayWindowApi overlayWindows = new(overlay);
        overlayWindows.ForegroundWindowHandle = sourceWindow.Handle;
        overlayWindows.CursorPosition = new WindowPlacementPoint(
            sourceWindow.ClientBounds.X,
            sourceWindow.ClientBounds.Y);
        overlayWindows.WindowAtCursorHandle = sourceWindow.Handle;
        OverlayCompatibilityService overlayCompatibility = new(overlayWindows);
        LaunchAcceptanceHarness harness =
            CreateLaunchAcceptanceHarness(
                client,
                sourceWindow,
                exact,
                overlayCompatibility);

        FourKayLaunchResult? result = null;
        try
        {
            result = await harness.Service.AttachExistingAsync(
                harness.Request with
                {
                    MaintainTopmostOverlays = true,
                }).ConfigureAwait(false);

            OverlayCompatibilitySession session = result.OverlayCompatibility
                ?? throw new TestFailureException(
                    "The verified launch did not retain its overlay session.");
            Assert.True(session.IsActive);
            Assert.Equal(1, session.CapturedWindowCount);
            Assert.Equal(1, session.MappedWindowCount);
            Assert.Equal(1, overlayWindows.TopmostRequests.Count);
            Assert.Equal(1, overlayWindows.MoveRequests.Count);
            Assert.True(
                overlayWindows.Inspect(overlay.Handle)!.Bounds
                    != originalOverlayBounds);

            _ = overlayCompatibility.Restore(session);
            Assert.Equal(
                originalOverlayBounds,
                overlayWindows.Inspect(overlay.Handle)!.Bounds);
            Assert.False(session.IsActive);
        }
        finally
        {
            result?.LauncherProcess?.Dispose();
            result?.GameProcess.Dispose();
            result?.MagpieProcess.Process.Dispose();
        }
    }

    private static async Task LiveScaleStrictAndNearestRejectBeforeEffectsAsync()
    {
        await using TempDirectory temp = new();
        using LiveScaleHarness harness =
            await CreateLiveScaleHarnessAsync(temp.Path).ConfigureAwait(false);
        ResolutionPlan requested = new ResolutionPlanner().CreateCustomPlan(
            harness.TargetSize,
            1.25,
            ScalingFilter.Fsr);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Service.AdjustLiveScaleAsync(
                harness.CreateRequest(requested) with
                {
                    ActiveLaunch = harness.ActiveLaunch with
                    {
                        UiCompatibilityMode =
                            FourKayUiCompatibilityMode.SpinUiStrict,
                    },
                })).ConfigureAwait(false);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Service.AdjustLiveScaleAsync(
                harness.CreateRequest(requested) with
                {
                    ActiveLaunch = harness.ActiveLaunch with
                    {
                        EffectiveFilter = ScalingFilter.NearestNeighbor,
                    },
                })).ConfigureAwait(false);

        ResolutionPlan nearestPlan = new ResolutionPlanner().CreateCustomPlan(
            harness.TargetSize,
            2.0,
            ScalingFilter.NearestNeighbor);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Service.AdjustLiveScaleAsync(
                harness.CreateRequest(nearestPlan))).ConfigureAwait(false);

        Assert.Equal(0, harness.Windows.InvocationCount);
        Assert.Equal(0, harness.Placement.PlacementCalls);
        Assert.Equal(0, harness.Inspector.InvocationCount);
        Assert.Equal(harness.PreviousSize, harness.ActiveLaunch.Placement.RequestedClientSize);
    }

    private static async Task LiveScaleSuccessfulCommitUsesExpectedGeometryAsync()
    {
        await using TempDirectory temp = new();
        using LiveScaleHarness harness =
            await CreateLiveScaleHarnessAsync(temp.Path).ConfigureAwait(false);
        ResolutionPlan requested = new ResolutionPlanner().CreateCustomPlan(
            harness.TargetSize,
            1.25,
            ScalingFilter.Fsr);
        WindowDescriptor resized = harness.CreateSourceWindow(
            requested.SourceResolution);
        harness.Windows.VisibleWindowResults.Enqueue([harness.ActiveLaunch.SourceWindow]);
        harness.Windows.StableResults.Enqueue(resized);
        harness.Inspector.InspectionResults.Enqueue(
            CreateScalingInspection(isActive: false));

        FourKayLiveScaleResult result =
            await harness.Service.AdjustLiveScaleAsync(
                harness.CreateRequest(requested)).ConfigureAwait(false);

        Assert.Equal(FourKayLiveScaleDisposition.Committed, result.Disposition);
        Assert.False(result.OutputIsActive);
        Assert.Equal(
            requested.SourceResolution,
            result.ActiveLaunch.Placement.RequestedClientSize);
        Assert.Equal(
            requested.SourceResolution,
            result.ActiveLaunch.Placement.ActualClientSize);
        Assert.Equal(
            requested.SourceResolution,
            result.ActiveLaunch.SourceWindow.ClientBounds.Size);
        Assert.Equal(
            requested.DestinationContent,
            result.ActiveLaunch.ExpectedDestinationRegion);
        Assert.Equal(harness.ActiveLaunch.SourceWindow.Handle, result.ActiveLaunch.SourceWindow.Handle);
        Assert.Equal(harness.ActiveLaunch.GameProcess.Id, result.ActiveLaunch.SourceWindow.ProcessId);
        Assert.Equal(1, harness.Placement.PlacementCalls);
        Assert.Equal(3, harness.Windows.FindVisibleCalls);
        Assert.Equal(1, harness.Windows.StableCalls);
        Assert.Equal(4, harness.Inspector.InvocationCount);
        Assert.Equal(0, harness.Inspector.StopCalls);
        Assert.Equal(
            harness.PreviousSize,
            harness.ActiveLaunch.Placement.RequestedClientSize);
    }

    private static async Task LiveScaleFailuresRestorePreviousGeometryAsync()
    {
        await using (TempDirectory resizeTemp = new())
        {
            using LiveScaleHarness resizeHarness =
                await CreateLiveScaleHarnessAsync(resizeTemp.Path)
                    .ConfigureAwait(false);
            ResolutionPlan requested = new ResolutionPlanner().CreateCustomPlan(
                resizeHarness.TargetSize,
                1.25,
                ScalingFilter.Fsr);
            resizeHarness.Windows.VisibleWindowResults.Enqueue(
                [resizeHarness.ActiveLaunch.SourceWindow]);
            resizeHarness.Windows.VisibleWindowResults.Enqueue(
                [resizeHarness.ActiveLaunch.SourceWindow]);
            resizeHarness.Windows.StableResults.Enqueue(
                resizeHarness.ActiveLaunch.SourceWindow);
            resizeHarness.Placement.ExactPlacementResults.Enqueue(false);
            resizeHarness.Placement.ExactPlacementResults.Enqueue(true);
            resizeHarness.Inspector.InspectionResults.Enqueue(
                CreateScalingInspection(isActive: false));
            resizeHarness.Inspector.InspectionResults.Enqueue(
                CreateScalingInspection(isActive: false));

            FourKayLiveScaleResult recovered =
                await resizeHarness.Service.AdjustLiveScaleAsync(
                    resizeHarness.CreateRequest(requested)).ConfigureAwait(false);

            Assert.Equal(
                FourKayLiveScaleDisposition.RecoveredPrevious,
                recovered.Disposition);
            Assert.Equal(
                resizeHarness.PreviousSize,
                recovered.ActiveLaunch.Placement.RequestedClientSize);
            Assert.False(recovered.OutputIsActive);
            Assert.Equal(2, resizeHarness.Placement.PlacementCalls);
            Assert.Equal(4, resizeHarness.Windows.FindVisibleCalls);
            Assert.Equal(1, resizeHarness.Windows.StableCalls);
        }

        await using (TempDirectory validationTemp = new())
        {
            using LiveScaleHarness validationHarness =
                await CreateLiveScaleHarnessAsync(validationTemp.Path)
                    .ConfigureAwait(false);
            ResolutionPlan requested = new ResolutionPlanner().CreateCustomPlan(
                validationHarness.TargetSize,
                1.25,
                ScalingFilter.Fsr);
            WindowDescriptor wrongSize = validationHarness.CreateSourceWindow(
                new PixelSize(
                    requested.SourceResolution.Width - 10,
                    requested.SourceResolution.Height - 10));
            validationHarness.Windows.VisibleWindowResults.Enqueue(
                [validationHarness.ActiveLaunch.SourceWindow]);
            validationHarness.Windows.VisibleWindowResults.Enqueue([wrongSize]);
            validationHarness.Windows.StableResults.Enqueue(wrongSize);
            validationHarness.Windows.StableResults.Enqueue(
                validationHarness.ActiveLaunch.SourceWindow);
            validationHarness.Placement.ExactPlacementResults.Enqueue(true);
            validationHarness.Placement.ExactPlacementResults.Enqueue(true);
            validationHarness.Inspector.InspectionResults.Enqueue(
                CreateScalingInspection(isActive: false));
            validationHarness.Inspector.InspectionResults.Enqueue(
                CreateScalingInspection(isActive: false));

            FourKayLiveScaleResult recovered =
                await validationHarness.Service.AdjustLiveScaleAsync(
                    validationHarness.CreateRequest(requested)).ConfigureAwait(false);

            Assert.Equal(
                FourKayLiveScaleDisposition.RecoveredPrevious,
                recovered.Disposition);
            Assert.Equal(
                validationHarness.PreviousSize,
                recovered.ActiveLaunch.SourceWindow.ClientBounds.Size);
            Assert.Equal(
                validationHarness.PreviousSize,
                recovered.ActiveLaunch.Placement.RequestedClientSize);
            Assert.False(recovered.OutputIsActive);
            Assert.Equal(2, validationHarness.Placement.PlacementCalls);
            Assert.Equal(4, validationHarness.Windows.FindVisibleCalls);
            Assert.Equal(2, validationHarness.Windows.StableCalls);
        }
    }

    private static async Task LiveScaleRollbackFailureRetainsBothCausesAsync()
    {
        await using TempDirectory temp = new();
        using LiveScaleHarness harness =
            await CreateLiveScaleHarnessAsync(temp.Path).ConfigureAwait(false);
        ResolutionPlan requested = new ResolutionPlanner().CreateCustomPlan(
            harness.TargetSize,
            1.25,
            ScalingFilter.Fsr);
        harness.Windows.VisibleWindowResults.Enqueue([harness.ActiveLaunch.SourceWindow]);
        harness.Windows.VisibleWindowResults.Enqueue([harness.ActiveLaunch.SourceWindow]);
        harness.Placement.ExactPlacementResults.Enqueue(false);
        harness.Placement.ExactPlacementResults.Enqueue(false);
        harness.Inspector.InspectionResults.Enqueue(
            CreateScalingInspection(isActive: false));
        harness.Inspector.InspectionResults.Enqueue(
            CreateScalingInspection(isActive: false));

        FourKayLiveScaleAdjustmentException failure =
            await Assert.ThrowsAsync<FourKayLiveScaleAdjustmentException>(
                () => harness.Service.AdjustLiveScaleAsync(
                    harness.CreateRequest(requested))).ConfigureAwait(false);

        Assert.Contains("could not be placed safely", failure.AdjustmentFailure.Message);
        Assert.Contains("could not be placed safely", failure.RollbackFailure.Message);
        Assert.Contains("Exact owned scaling cleanup is required", failure.Message);
        Assert.Equal(2, harness.Placement.PlacementCalls);
        Assert.Equal(2, harness.Windows.FindVisibleCalls);
        Assert.Equal(0, harness.Windows.StableCalls);
        Assert.False(harness.GameProcess.HasExited);
        Assert.False(harness.MagpieProcess.HasExited);
    }

    private static async Task LiveScaleFocusReturningFailsClosedAsync()
    {
        await using (TempDirectory sourceTemp = new())
        {
            using LiveScaleHarness sourceHarness =
                await CreateLiveScaleHarnessAsync(sourceTemp.Path)
                    .ConfigureAwait(false);
            ResolutionPlan requested = new ResolutionPlanner().CreateCustomPlan(
                sourceHarness.TargetSize,
                1.25,
                ScalingFilter.Fsr);
            sourceHarness.Foreground.ForegroundWindowResults.Enqueue(
                sourceHarness.ActiveLaunch.SourceWindow.Handle);

            InvalidOperationException failure =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => sourceHarness.Service.AdjustLiveScaleAsync(
                        sourceHarness.CreateRequest(requested)))
                    .ConfigureAwait(false);

            Assert.Contains("returned to the foreground", failure.Message);
            Assert.Equal(0, sourceHarness.Placement.PlacementCalls);
            Assert.Equal(0, sourceHarness.Windows.StableCalls);
        }

        await using (TempDirectory scalerTemp = new())
        {
            using LiveScaleHarness scalerHarness =
                await CreateLiveScaleHarnessAsync(scalerTemp.Path)
                    .ConfigureAwait(false);
            ResolutionPlan requested = new ResolutionPlanner().CreateCustomPlan(
                scalerHarness.TargetSize,
                1.25,
                ScalingFilter.Fsr);
            MagpieScalingWindowInspection active =
                CreateExactSafeScalingInspection(
                    scalerHarness.MagpieProcess.Id,
                    scalerHarness.ActiveLaunch.SourceWindow.Handle,
                    scalerHarness.PreviousSize,
                    scalerHarness.ActiveLaunch.Placement.Monitor.Bounds);
            scalerHarness.Inspector.InspectionResults.Enqueue(active);
            scalerHarness.Foreground.ForegroundWindowResults.Enqueue(
                active.ScalingWindowHandle);

            InvalidOperationException failure =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => scalerHarness.Service.AdjustLiveScaleAsync(
                        scalerHarness.CreateRequest(requested)))
                    .ConfigureAwait(false);

            Assert.Contains("returned to the foreground", failure.Message);
            Assert.Equal(0, scalerHarness.Inspector.StopCalls);
            Assert.Equal(0, scalerHarness.Placement.PlacementCalls);
        }

        await using (TempDirectory boundaryTemp = new())
        {
            using LiveScaleHarness boundaryHarness =
                await CreateLiveScaleHarnessAsync(boundaryTemp.Path)
                    .ConfigureAwait(false);
            ResolutionPlan requested = new ResolutionPlanner().CreateCustomPlan(
                boundaryHarness.TargetSize,
                1.25,
                ScalingFilter.Fsr);
            nint background = boundaryHarness.Foreground.ForegroundWindowHandle;
            boundaryHarness.Foreground.ForegroundWindowResults.Enqueue(background);
            boundaryHarness.Foreground.ForegroundWindowResults.Enqueue(background);
            boundaryHarness.Foreground.ForegroundWindowResults.Enqueue(
                boundaryHarness.ActiveLaunch.SourceWindow.Handle);
            boundaryHarness.Windows.StableResults.Enqueue(
                boundaryHarness.ActiveLaunch.SourceWindow);

            FourKayLiveScaleResult recovered =
                await boundaryHarness.Service.AdjustLiveScaleAsync(
                    boundaryHarness.CreateRequest(requested)).ConfigureAwait(false);

            Assert.Equal(
                FourKayLiveScaleDisposition.RecoveredPrevious,
                recovered.Disposition);
            Assert.Contains("returned to the foreground", recovered.Message);
            Assert.Equal(1, boundaryHarness.Placement.PlacementCalls);
            Assert.Equal(
                boundaryHarness.PreviousSize,
                recovered.ActiveLaunch.Placement.RequestedClientSize);
        }
    }

    private static async Task LiveScalePostResizeOutputValidationAsync()
    {
        await using (TempDirectory exactTemp = new())
        {
            using LiveScaleHarness exactHarness =
                await CreateLiveScaleHarnessAsync(exactTemp.Path)
                    .ConfigureAwait(false);
            ResolutionPlan requested = new ResolutionPlanner().CreateCustomPlan(
                exactHarness.TargetSize,
                1.25,
                ScalingFilter.Fsr);
            exactHarness.Windows.StableResults.Enqueue(
                exactHarness.CreateSourceWindow(requested.SourceResolution));
            exactHarness.Inspector.InspectionResults.Enqueue(
                CreateScalingInspection(isActive: false));
            exactHarness.Inspector.InspectionResults.Enqueue(
                CreateScalingInspection(isActive: false));
            exactHarness.Inspector.InspectionResults.Enqueue(
                CreateScalingInspection(isActive: false));
            MagpieScalingWindowInspection exactOutput =
                CreateExactSafeScalingInspection(
                    exactHarness.MagpieProcess.Id,
                    exactHarness.ActiveLaunch.SourceWindow.Handle,
                    requested.SourceResolution,
                    exactHarness.ActiveLaunch.Placement.Monitor.Bounds);
            exactHarness.Inspector.InspectionResults.Enqueue(exactOutput);

            FourKayLiveScaleResult committed =
                await exactHarness.Service.AdjustLiveScaleAsync(
                    exactHarness.CreateRequest(requested)).ConfigureAwait(false);

            Assert.Equal(
                FourKayLiveScaleDisposition.Committed,
                committed.Disposition);
            Assert.True(committed.OutputIsActive);
            Assert.True(committed.ActiveLaunch.ScalingInspection.IsSafeForInput);
            Assert.Equal(
                exactHarness.MagpieProcess.Id,
                committed.ActiveLaunch.ScalingInspection.ScalingProcessId);
        }

        await using (TempDirectory foreignTemp = new())
        {
            using LiveScaleHarness foreignHarness =
                await CreateLiveScaleHarnessAsync(foreignTemp.Path)
                    .ConfigureAwait(false);
            ResolutionPlan requested = new ResolutionPlanner().CreateCustomPlan(
                foreignHarness.TargetSize,
                1.25,
                ScalingFilter.Fsr);
            WindowDescriptor resized =
                foreignHarness.CreateSourceWindow(requested.SourceResolution);
            foreignHarness.Windows.StableResults.Enqueue(resized);
            foreignHarness.Windows.StableResults.Enqueue(
                foreignHarness.ActiveLaunch.SourceWindow);
            foreignHarness.Inspector.InspectionResults.Enqueue(
                CreateScalingInspection(isActive: false));
            foreignHarness.Inspector.InspectionResults.Enqueue(
                CreateScalingInspection(isActive: false));
            foreignHarness.Inspector.InspectionResults.Enqueue(
                CreateScalingInspection(isActive: false));
            MagpieScalingWindowInspection foreignOutput =
                CreateExactSafeScalingInspection(
                    foreignHarness.MagpieProcess.Id + 1,
                    foreignHarness.ActiveLaunch.SourceWindow.Handle,
                    requested.SourceResolution,
                    foreignHarness.ActiveLaunch.Placement.Monitor.Bounds);
            foreignHarness.Inspector.InspectionResults.Enqueue(foreignOutput);

            FourKayLiveScaleResult recovered =
                await foreignHarness.Service.AdjustLiveScaleAsync(
                    foreignHarness.CreateRequest(requested)).ConfigureAwait(false);

            Assert.Equal(
                FourKayLiveScaleDisposition.RecoveredPrevious,
                recovered.Disposition);
            Assert.False(recovered.OutputIsActive);
            Assert.Contains("replacement or foreign", recovered.Message);
            Assert.Equal(2, foreignHarness.Placement.PlacementCalls);
            Assert.Equal(
                foreignHarness.PreviousSize,
                recovered.ActiveLaunch.Placement.RequestedClientSize);
        }

        await using (TempDirectory unsafeTemp = new())
        {
            using LiveScaleHarness unsafeHarness =
                await CreateLiveScaleHarnessAsync(unsafeTemp.Path)
                    .ConfigureAwait(false);
            ResolutionPlan requested = new ResolutionPlanner().CreateCustomPlan(
                unsafeHarness.TargetSize,
                1.25,
                ScalingFilter.Fsr);
            unsafeHarness.Windows.StableResults.Enqueue(
                unsafeHarness.CreateSourceWindow(requested.SourceResolution));
            unsafeHarness.Windows.StableResults.Enqueue(
                unsafeHarness.ActiveLaunch.SourceWindow);
            unsafeHarness.Inspector.InspectionResults.Enqueue(
                CreateScalingInspection(isActive: false));
            unsafeHarness.Inspector.InspectionResults.Enqueue(
                CreateScalingInspection(isActive: false));
            unsafeHarness.Inspector.InspectionResults.Enqueue(
                CreateScalingInspection(isActive: false));
            MagpieScalingWindowInspection unsafeOutput =
                CreateExactSafeScalingInspection(
                    unsafeHarness.MagpieProcess.Id,
                    unsafeHarness.ActiveLaunch.SourceWindow.Handle,
                    requested.SourceResolution,
                    unsafeHarness.ActiveLaunch.Placement.Monitor.Bounds) with
                {
                    IsSafeForInput = false,
                    Issues = ["injected unsafe mouse map"],
                };
            unsafeHarness.Inspector.InspectionResults.Enqueue(unsafeOutput);
            unsafeHarness.Inspector.WaitResult = unsafeOutput;

            FourKayLiveScaleResult recovered =
                await unsafeHarness.Service.AdjustLiveScaleAsync(
                    unsafeHarness.CreateRequest(requested)).ConfigureAwait(false);

            Assert.Equal(
                FourKayLiveScaleDisposition.RecoveredPrevious,
                recovered.Disposition);
            Assert.False(recovered.OutputIsActive);
            Assert.Contains("safe mouse map", recovered.Message);
            Assert.Equal(2, unsafeHarness.Placement.PlacementCalls);
        }
    }

    private static async Task LiveScaleUnexpectedFailureAfterPlacementRollsBackAsync()
    {
        await using TempDirectory temp = new();
        using LiveScaleHarness harness =
            await CreateLiveScaleHarnessAsync(temp.Path).ConfigureAwait(false);
        ResolutionPlan requested = new ResolutionPlanner().CreateCustomPlan(
            harness.TargetSize,
            1.25,
            ScalingFilter.Fsr);
        harness.Windows.StableExceptions.Enqueue(
            new InjectedNonFatalException(
                "Injected unexpected post-placement failure."));
        harness.Windows.StableResults.Enqueue(harness.ActiveLaunch.SourceWindow);

        FourKayLiveScaleResult recovered =
            await harness.Service.AdjustLiveScaleAsync(
                harness.CreateRequest(requested)).ConfigureAwait(false);

        Assert.Equal(
            FourKayLiveScaleDisposition.RecoveredPrevious,
            recovered.Disposition);
        Assert.Contains(
            "Injected unexpected post-placement failure",
            recovered.Message);
        Assert.Equal(2, harness.Placement.PlacementCalls);
        Assert.Equal(2, harness.Windows.StableCalls);
        Assert.Equal(
            harness.PreviousSize,
            recovered.ActiveLaunch.Placement.RequestedClientSize);
    }

    private static async Task LiveScaleMonitorBoundsDriftRejectsAsync()
    {
        await using TempDirectory temp = new();
        using LiveScaleHarness harness =
            await CreateLiveScaleHarnessAsync(temp.Path).ConfigureAwait(false);
        ResolutionPlan requested = new ResolutionPlanner().CreateCustomPlan(
            harness.TargetSize,
            1.25,
            ScalingFilter.Fsr);
        MonitorDescriptor previousMonitor = harness.ActiveLaunch.Placement.Monitor;
        harness.Placement.MonitorsOverride =
        [
            previousMonitor with
            {
                Bounds = new PixelRect(0, 0, 2560, 1440),
                WorkArea = new PixelRect(0, 0, 2560, 1400),
            },
        ];

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => harness.Service.AdjustLiveScaleAsync(
                    harness.CreateRequest(requested))).ConfigureAwait(false);

        Assert.Contains("physical bounds changed", failure.Message);
        Assert.Equal(0, harness.Placement.PlacementCalls);
        Assert.Equal(0, harness.Windows.FindVisibleCalls);
        Assert.Equal(0, harness.Inspector.InvocationCount);
    }

    private static async Task LiveScaleUnrelatedIniChangesAreAllowedAsync()
    {
        await using TempDirectory temp = new();
        using LiveScaleHarness harness =
            await CreateLiveScaleHarnessAsync(temp.Path).ConfigureAwait(false);
        ResolutionPlan requested = new ResolutionPlanner().CreateCustomPlan(
            harness.TargetSize,
            1.25,
            ScalingFilter.Fsr);
        harness.Windows.StableResults.Enqueue(
            harness.CreateSourceWindow(requested.SourceResolution));
        bool changed = false;
        harness.Placement.AfterPlacement = () =>
        {
            if (changed)
            {
                return;
            }

            changed = true;
            File.AppendAllText(
                harness.Client.EqClientIniPath,
                "\r\n[UnrelatedRuntime]\r\nUserPreference=changed\r\n");
        };

        FourKayLiveScaleResult committed =
            await harness.Service.AdjustLiveScaleAsync(
                harness.CreateRequest(requested)).ConfigureAwait(false);

        Assert.Equal(
            FourKayLiveScaleDisposition.Committed,
            committed.Disposition);
        Assert.False(committed.OutputIsActive);
        Assert.True(changed);
        Assert.Contains(
            "UserPreference=changed",
            await File.ReadAllTextAsync(harness.Client.EqClientIniPath)
                .ConfigureAwait(false));
        EqClientVideoSettings settings =
            await new EqClientConfigurationService().ReadAsync(
                harness.Client.EqClientIniPath).ConfigureAwait(false);
        Assert.Equal(NativeUiScaleStatus.Valid, settings.NativeUiScaleStatus);
        Assert.Equal(0, settings.NativeUiScaleIndex);
    }

    private static async Task ConfigBoundaryFailuresRetainOwnershipAsync()
    {
        await using TempDirectory temp = new();
        FakeClientFixture client = await FakeClientFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        int currentProcessId = Environment.ProcessId;
        WindowDescriptor sourceWindow = new(
            new nint(123),
            currentProcessId,
            "EverQuest",
            "EQGameWindow",
            new PixelRect(20, 20, 1936, 1119),
            new PixelRect(28, 59, 1920, 1080),
            IsMinimized: false,
            ExecutablePath: client.EqGamePath);

        FourKayLaunchService CreateService(
            FakeMagpiePortableConfigService config,
            FakeMagpieProcessService magpie,
            FakeScalingWindowInspector inspector)
        {
            FakeProcessDiscoveryService processes = new()
            {
                Current =
                [
                    new ProcessDescriptor(
                        currentProcessId,
                        client.EqGamePath,
                        DateTimeOffset.UnixEpoch),
                ],
            };
            FakeWindowDiscoveryService windows = new()
            {
                AllowCalls = true,
                StableResult = sourceWindow,
                ForegroundResult = true,
            };
            return new FourKayLaunchService(
                processes,
                windows,
                FakeWindowPlacementService.Create4K(),
                config,
                magpie,
                inspector);
        }

        FourKayAttachRequest CreateRequest() => new()
        {
            EqDirectory = client.EqDirectory,
            MagpieDirectory = @"C:\BundledMagpie",
            Filter = ScalingFilter.Fsr,
            ExpectedClientSize = new PixelSize(1920, 1080),
            TargetMonitor = new nint(42),
        };

        InvalidOperationException applyFailure =
            new("Injected config apply failure after mutation.");
        FakeMagpiePortableConfigService applyConfig = new()
        {
            ApplyException = applyFailure,
        };
        FakeMagpieProcessService applyMagpie = new();
        FakeScalingWindowInspector applyInspector = new()
        {
            AllowCleanupCalls = true,
            InspectionResults = new Queue<MagpieScalingWindowInspection>(
            [
                CreateScalingInspection(isActive: false),
                CreateScalingInspection(isActive: false),
            ]),
        };
        InvalidOperationException observedApplyFailure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateService(applyConfig, applyMagpie, applyInspector)
                    .AttachExistingAsync(CreateRequest())).ConfigureAwait(false);
        Assert.Equal(applyFailure.Message, observedApplyFailure.Message);
        Assert.Equal(1, applyConfig.PrepareCalls);
        Assert.Equal(1, applyConfig.WriteCalls);
        Assert.Equal(1, applyConfig.RollbackCalls);
        Assert.Equal(0, applyMagpie.StartCalls);
        Assert.Equal(0, applyMagpie.ShutdownExactCalls);
        Assert.Equal(0, applyInspector.StopCalls);

        InvalidOperationException startFailure =
            new("Injected Magpie start failure.");
        FakeMagpiePortableConfigService startConfig = new();
        FakeMagpieProcessService startMagpie = new()
        {
            StartException = startFailure,
        };
        FakeScalingWindowInspector startInspector = new()
        {
            AllowCleanupCalls = true,
            InspectionResults = new Queue<MagpieScalingWindowInspection>(
            [
                CreateScalingInspection(isActive: false),
                CreateScalingInspection(isActive: false),
            ]),
        };
        InvalidOperationException observedStartFailure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateService(startConfig, startMagpie, startInspector)
                    .AttachExistingAsync(CreateRequest())).ConfigureAwait(false);
        Assert.Equal(startFailure.Message, observedStartFailure.Message);
        Assert.Equal(1, startConfig.PrepareCalls);
        Assert.Equal(1, startConfig.WriteCalls);
        Assert.Equal(1, startConfig.RollbackCalls);
        Assert.Equal(1, startMagpie.StartCalls);
        Assert.Equal(0, startMagpie.ShutdownExactCalls);
        Assert.Equal(0, startInspector.StopCalls);

        MagpieRunningInstance preApplyArrival = new(
            8801,
            @"C:\BundledMagpie\Magpie.exe",
            IsBundledInstance: true);
        FakeMagpiePortableConfigService preApplyConfig = new();
        FakeMagpieProcessService preApplyMagpie = new()
        {
            InspectionResults =
                new Queue<IReadOnlyList<MagpieRunningInstance>>(
                [
                    Array.Empty<MagpieRunningInstance>(),
                    Array.Empty<MagpieRunningInstance>(),
                    new[] { preApplyArrival },
                ]),
        };
        FakeScalingWindowInspector preApplyInspector = new();
        await Assert.ThrowsAsync<DedicatedMagpieConfigReloadRequiredException>(
            () => CreateService(preApplyConfig, preApplyMagpie, preApplyInspector)
                .AttachExistingAsync(CreateRequest())).ConfigureAwait(false);
        Assert.Equal(3, preApplyMagpie.InspectCalls);
        Assert.Equal(1, preApplyConfig.PrepareCalls);
        Assert.Equal(0, preApplyConfig.WriteCalls);
        Assert.Equal(0, preApplyConfig.RollbackCalls);
        Assert.Equal(0, preApplyMagpie.StartCalls);
        Assert.Equal(0, preApplyInspector.InvocationCount);

        Process raceWrapper = Process.GetCurrentProcess();
        MagpieRunningInstance racedInstance = new(
            currentProcessId,
            @"C:\BundledMagpie\Magpie.exe",
            IsBundledInstance: true);
        FakeMagpiePortableConfigService raceConfig = new();
        FakeMagpieProcessService raceMagpie = new()
        {
            InspectionResults =
                new Queue<IReadOnlyList<MagpieRunningInstance>>(
                [
                    Array.Empty<MagpieRunningInstance>(),
                    Array.Empty<MagpieRunningInstance>(),
                    Array.Empty<MagpieRunningInstance>(),
                    new[] { racedInstance },
                ]),
            StartResultFactory = () => new MagpieProcessStartResult(
                raceWrapper,
                AlreadyRunning: true),
        };
        FakeScalingWindowInspector raceInspector = new()
        {
            AllowCleanupCalls = true,
            InspectionResults = new Queue<MagpieScalingWindowInspection>(
            [
                CreateScalingInspection(isActive: false),
                CreateScalingInspection(isActive: false),
            ]),
        };
        MagpieScalingCleanupException raceFailure =
            await Assert.ThrowsAsync<MagpieScalingCleanupException>(
                () => CreateService(raceConfig, raceMagpie, raceInspector)
                    .AttachExistingAsync(CreateRequest())).ConfigureAwait(false);
        Assert.True(
            raceFailure.StartupFailure
                is DedicatedMagpieConfigReloadRequiredException);
        Assert.False(raceFailure.CleanupState.AttemptOwnedEngine);
        Assert.False(raceFailure.CleanupState.EngineAbsenceConfirmed);
        Assert.False(raceFailure.CleanupState.ConfigRollbackAttempted);
        Assert.True(raceFailure.PendingConfigTransaction is not null);
        Assert.Equal(1, raceConfig.WriteCalls);
        Assert.Equal(0, raceConfig.RollbackCalls);
        Assert.Equal(1, raceMagpie.StartCalls);
        Assert.Equal(0, raceMagpie.ShutdownExactCalls);
        Assert.Equal(0, raceInspector.StopCalls);
        Assert.False(raceFailure.CleanupLease.OwnsEngine);
        Assert.False(raceFailure.CleanupLease.AllowChangedContentRollback);
        Assert.True(
            ReferenceEquals(
                raceWrapper,
                raceFailure.CleanupLease.ExactProcessToken));
        _ = raceWrapper.Handle;
        raceFailure.CleanupLease.Dispose();
        Assert.Throws<InvalidOperationException>(() => _ = raceWrapper.Handle);
    }

    private static async Task PreApplyNewerWriterIsTerminallyPreservedAsync()
    {
        await using TempDirectory temp = new();
        MagpieFixture fixture = await MagpieFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        string configDirectory = Path.Combine(fixture.MagpieDirectory, "config");
        Directory.CreateDirectory(configDirectory);
        string configPath = Path.Combine(configDirectory, "config.json");
        byte[] original = Encoding.UTF8.GetBytes(
            "{\"language\":\"original\",\"scalingModes\":[],\"profiles\":[]}");
        await File.WriteAllBytesAsync(configPath, original).ConfigureAwait(false);

        MagpiePortableConfigService service = new();
        MagpiePortableConfigTransaction transaction =
            await service.PrepareTransactionAsync(
                fixture.CreateRequest(ScalingFilter.Fsr)).ConfigureAwait(false);
        byte[] newer = Encoding.UTF8.GetBytes("{\"newerWriter\":true}");
        await File.WriteAllBytesAsync(configPath, newer).ConfigureAwait(false);

        await Assert.ThrowsAsync<IOException>(
            () => service.ApplyTransactionAsync(transaction)).ConfigureAwait(false);
        MagpiePortableConfigRollbackResult rollback =
            await service.RollbackTransactionAsync(transaction).ConfigureAwait(false);
        Assert.Equal(
            MagpiePortableConfigRollbackDisposition.NewerContentPreserved,
            rollback.Disposition);
        Assert.True(rollback.Resolved);
        Assert.False(rollback.Restored);
        Assert.SequenceEqual(
            newer,
            await File.ReadAllBytesAsync(configPath).ConfigureAwait(false));
        Assert.Empty(
            Directory.GetFiles(
                configDirectory,
                "config.json.spinfourkayyy-recovery-*",
                SearchOption.TopDirectoryOnly));
    }

    private static async Task UnownedExactTokenCleanupIsPassiveAsync()
    {
        Process observedProcess = Process.GetCurrentProcess();
        FakeMagpiePortableConfigService config = new();
        FakeMagpieProcessService magpie = new()
        {
            Instances =
            [
                new MagpieRunningInstance(
                    Environment.ProcessId,
                    @"C:\BundledMagpie\Magpie.exe",
                    IsBundledInstance: true),
            ],
        };
        FakeScalingWindowInspector inspector = new()
        {
            AllowCleanupCalls = true,
            FallbackInspection = CreateScalingInspection(isActive: false),
        };
        FourKayLaunchService service = new(
            magpieConfig: config,
            magpieProcess: magpie,
            scalingInspector: inspector);
        using MagpieScalingCleanupLease lease = new(
            @"C:\BundledMagpie",
            new nint(123),
            CreateFakeMagpieTransaction(),
            observedProcess,
            ownsEngine: false,
            allowChangedContentRollback: false);

        MagpieScalingCleanupResolution resolution =
            await service.ResolveCleanupLeaseAsync(
                lease,
                TimeSpan.FromMilliseconds(15)).ConfigureAwait(false);
        Assert.False(resolution.Resolved);
        Assert.Equal(0, inspector.StopCalls);
        Assert.Equal(0, magpie.ShutdownExactCalls);
        Assert.Equal(0, magpie.ShutdownCalls);
        Assert.Equal(0, config.RollbackCalls);
        _ = observedProcess.Handle;
    }

    private static async Task NoTokenCleanupIsPassiveAsync()
    {
        FakeMagpiePortableConfigService config = new();
        FakeMagpieProcessService magpie = new()
        {
            Instances =
            [
                new MagpieRunningInstance(
                    9911,
                    @"C:\BundledMagpie\Magpie.exe",
                    IsBundledInstance: true),
            ],
        };
        FakeScalingWindowInspector inspector = new()
        {
            AllowCleanupCalls = true,
            FallbackInspection = CreateScalingInspection(isActive: true),
        };
        FourKayLaunchService service = new(
            magpieConfig: config,
            magpieProcess: magpie,
            scalingInspector: inspector);
        using MagpieScalingCleanupLease lease = new(
            @"C:\BundledMagpie",
            new nint(123),
            CreateFakeMagpieTransaction(),
            exactProcessToken: null,
            ownsEngine: false,
            allowChangedContentRollback: false);

        MagpieScalingCleanupResolution resolution =
            await service.ResolveCleanupLeaseAsync(
                lease,
                TimeSpan.FromMilliseconds(15)).ConfigureAwait(false);
        Assert.False(resolution.Resolved);
        Assert.Equal(0, inspector.StopCalls);
        Assert.Equal(0, magpie.ShutdownExactCalls);
        Assert.Equal(0, magpie.ShutdownCalls);
        Assert.Equal(0, config.RollbackCalls);
    }

    private static async Task ExactOwnedCleanupLeaseRetryAsync()
    {
        List<string> events = [];
        Process exactProcess = Process.GetCurrentProcess();
        FakeMagpiePortableConfigService config = new()
        {
            Events = events,
        };
        FakeMagpieProcessService magpie = new()
        {
            Events = events,
            ShutdownExactResult = true,
        };
        FakeScalingWindowInspector inspector = new()
        {
            AllowCleanupCalls = true,
            StopResult = true,
            Events = events,
            InspectionResults = new Queue<MagpieScalingWindowInspection>(
            [
                CreateScalingInspection(isActive: true),
                CreateScalingInspection(isActive: false),
            ]),
        };
        FourKayLaunchService service = new(
            magpieConfig: config,
            magpieProcess: magpie,
            scalingInspector: inspector);
        using MagpieScalingCleanupLease lease = new(
            @"C:\BundledMagpie",
            new nint(123),
            CreateFakeMagpieTransaction(),
            exactProcess,
            ownsEngine: true,
            allowChangedContentRollback: true);

        MagpieScalingCleanupResolution resolution =
            await service.ResolveCleanupLeaseAsync(
                lease,
                TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        Assert.True(resolution.Resolved);
        Assert.Equal(
            MagpiePortableConfigRollbackDisposition.Restored,
            resolution.RollbackDisposition);
        Assert.Equal(1, inspector.StopCalls);
        Assert.Equal(1, magpie.ShutdownExactCalls);
        Assert.Equal(0, magpie.ShutdownCalls);
        Assert.Equal(1, config.RollbackCalls);
        Assert.True(config.RollbackAllowChangedValues.Single());
        Assert.True(events.IndexOf("stop-scaling") < events.IndexOf("shutdown-exact"));
        Assert.True(events.IndexOf("shutdown-exact") < events.IndexOf("rollback"));
        _ = exactProcess.Handle;
    }

    private static async Task ChangedRollbackPermissionFollowsOwnershipAsync()
    {
        await using TempDirectory temp = new();
        MagpiePortableConfigService config = new();

        MagpieFixture unownedFixture =
            await MagpieFixture.CreateAsync(Path.Combine(temp.Path, "unowned"))
                .ConfigureAwait(false);
        MagpiePortableConfigTransaction unownedTransaction =
            await config.PrepareTransactionAsync(
                unownedFixture.CreateRequest(ScalingFilter.Fsr)).ConfigureAwait(false);
        await config.ApplyTransactionAsync(unownedTransaction).ConfigureAwait(false);
        byte[] unownedNewer = Encoding.UTF8.GetBytes("{\"unownedNewer\":true}");
        await File.WriteAllBytesAsync(
            unownedTransaction.ConfigPath,
            unownedNewer).ConfigureAwait(false);
        FakeMagpieProcessService unownedMagpie = new();
        FakeScalingWindowInspector unownedInspector = new()
        {
            AllowCleanupCalls = true,
            FallbackInspection = CreateScalingInspection(isActive: false),
        };
        FourKayLaunchService unownedService = new(
            magpieConfig: config,
            magpieProcess: unownedMagpie,
            scalingInspector: unownedInspector);
        using (MagpieScalingCleanupLease unownedLease = new(
            unownedFixture.MagpieDirectory,
            new nint(123),
            unownedTransaction,
            exactProcessToken: null,
            ownsEngine: false,
            allowChangedContentRollback: false))
        {
            MagpieScalingCleanupResolution unownedResolution =
                await unownedService.ResolveCleanupLeaseAsync(
                    unownedLease,
                    TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            Assert.True(unownedResolution.Resolved);
            Assert.Equal(
                MagpiePortableConfigRollbackDisposition.NewerContentPreserved,
                unownedResolution.RollbackDisposition);
        }

        Assert.SequenceEqual(
            unownedNewer,
            await File.ReadAllBytesAsync(unownedTransaction.ConfigPath)
                .ConfigureAwait(false));
        Assert.Empty(
            Directory.GetFiles(
                Path.GetDirectoryName(unownedTransaction.ConfigPath)!,
                "config.json.spinfourkayyy-recovery-*",
                SearchOption.TopDirectoryOnly));

        MagpieFixture ownedFixture =
            await MagpieFixture.CreateAsync(Path.Combine(temp.Path, "owned"))
                .ConfigureAwait(false);
        string ownedConfigDirectory = Path.Combine(
            ownedFixture.MagpieDirectory,
            "config");
        Directory.CreateDirectory(ownedConfigDirectory);
        string ownedConfigPath = Path.Combine(ownedConfigDirectory, "config.json");
        byte[] ownedOriginal = Encoding.UTF8.GetBytes(
            "{\"language\":\"owned-original\",\"scalingModes\":[],\"profiles\":[]}");
        await File.WriteAllBytesAsync(ownedConfigPath, ownedOriginal)
            .ConfigureAwait(false);
        MagpiePortableConfigTransaction ownedTransaction =
            await config.PrepareTransactionAsync(
                ownedFixture.CreateRequest(ScalingFilter.Fsr)).ConfigureAwait(false);
        await config.ApplyTransactionAsync(ownedTransaction).ConfigureAwait(false);
        byte[] ownedChanged = Encoding.UTF8.GetBytes("{\"ownedPostStartSave\":true}");
        await File.WriteAllBytesAsync(ownedConfigPath, ownedChanged)
            .ConfigureAwait(false);

        Process exactOwnedProcess = Process.GetCurrentProcess();
        FakeMagpieProcessService ownedMagpie = new()
        {
            ShutdownExactResult = true,
        };
        FakeScalingWindowInspector ownedInspector = new()
        {
            AllowCleanupCalls = true,
            InspectionResults = new Queue<MagpieScalingWindowInspection>(
            [
                CreateScalingInspection(isActive: false),
                CreateScalingInspection(isActive: false),
            ]),
        };
        FourKayLaunchService ownedService = new(
            magpieConfig: config,
            magpieProcess: ownedMagpie,
            scalingInspector: ownedInspector);
        using (MagpieScalingCleanupLease ownedLease = new(
            ownedFixture.MagpieDirectory,
            new nint(123),
            ownedTransaction,
            exactOwnedProcess,
            ownsEngine: true,
            allowChangedContentRollback: true))
        {
            MagpieScalingCleanupResolution ownedResolution =
                await ownedService.ResolveCleanupLeaseAsync(
                    ownedLease,
                    TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            Assert.True(ownedResolution.Resolved);
            Assert.Equal(
                MagpiePortableConfigRollbackDisposition.Restored,
                ownedResolution.RollbackDisposition);
        }

        Assert.SequenceEqual(
            ownedOriginal,
            await File.ReadAllBytesAsync(ownedConfigPath).ConfigureAwait(false));
        string[] recoveryCopies = Directory.GetFiles(
            ownedConfigDirectory,
            "config.json.spinfourkayyy-recovery-*",
            SearchOption.TopDirectoryOnly);
        Assert.Equal(1, recoveryCopies.Length);
        Assert.SequenceEqual(
            ownedChanged,
            await File.ReadAllBytesAsync(recoveryCopies[0]).ConfigureAwait(false));
    }

    private static async Task BundledPreflightRefusesWithoutMutationAsync()
    {
        await using TempDirectory temp = new();
        FakeClientFixture client = await FakeClientFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        FakeProcessDiscoveryService processes = new();
        FakeWindowDiscoveryService windows = new();
        FakeWindowPlacementService placement = FakeWindowPlacementService.Create4K();
        FakeMagpiePortableConfigService config = new();
        FakeMagpieProcessService magpie = new()
        {
            Instances =
            [
                new MagpieRunningInstance(
                    7711,
                    @"C:\BundledMagpie\Magpie.exe",
                    IsBundledInstance: true),
            ],
        };
        FakeScalingWindowInspector inspector = new();
        FourKayLaunchService service = new(
            processes,
            windows,
            placement,
            config,
            magpie,
            inspector);

        await Assert.ThrowsAsync<DedicatedMagpieConfigReloadRequiredException>(
            () => service.AttachExistingAsync(new FourKayAttachRequest
            {
                EqDirectory = client.EqDirectory,
                MagpieDirectory = @"C:\BundledMagpie",
                Filter = ScalingFilter.Fsr,
            })).ConfigureAwait(false);
        // Magpie loads its profile only at startup, so this application's own
        // engine is asked to quit rather than the player being told to close it.
        Assert.Equal(1, magpie.ShutdownCalls);

        // Its absence is confirmed instead of assumed. This engine never goes
        // away, so the launch still refuses and nothing at all is mutated.
        Assert.Equal(2, magpie.InspectCalls);
        Assert.Equal(0, magpie.ShutdownExactCalls);
        Assert.Equal(0, magpie.StartCalls);
        Assert.Equal(0, config.PrepareCalls);
        Assert.Equal(0, config.WriteCalls);
        Assert.Equal(0, config.RollbackCalls);
        Assert.Equal(0, processes.FindCalls);
        Assert.Equal(0, windows.InvocationCount);
        Assert.Equal(0, placement.PlacementCalls);
        Assert.Equal(0, inspector.InvocationCount);

        // An engine that refuses to quit is reported rather than worked around.
        FakeMagpiePortableConfigService stubbornConfig = new();
        FakeMagpieProcessService stubbornMagpie = new()
        {
            ShutdownResult = false,
            Instances =
            [
                new MagpieRunningInstance(
                    7712,
                    @"C:\BundledMagpie\Magpie.exe",
                    IsBundledInstance: true),
            ],
        };
        FakeScalingWindowInspector stubbornInspector = new();
        await Assert.ThrowsAsync<DedicatedMagpieConfigReloadRequiredException>(
            () => new FourKayLaunchService(
                    new FakeProcessDiscoveryService(),
                    new FakeWindowDiscoveryService(),
                    FakeWindowPlacementService.Create4K(),
                    stubbornConfig,
                    stubbornMagpie,
                    stubbornInspector)
                .AttachExistingAsync(new FourKayAttachRequest
                {
                    EqDirectory = client.EqDirectory,
                    MagpieDirectory = @"C:\BundledMagpie",
                    Filter = ScalingFilter.Fsr,
                })).ConfigureAwait(false);
        Assert.Equal(1, stubbornMagpie.ShutdownCalls);

        // A failed shutdown is terminal immediately; there is nothing to confirm.
        Assert.Equal(1, stubbornMagpie.InspectCalls);
        Assert.Equal(0, stubbornConfig.PrepareCalls);
        Assert.Equal(0, stubbornConfig.WriteCalls);

        // When the engine does go away, the launch proceeds past preflight and
        // writes the new profile instead of stopping the player.
        FakeMagpiePortableConfigService clearedConfig = new();
        FakeMagpieProcessService clearedMagpie = new()
        {
            InspectionResults = new Queue<IReadOnlyList<MagpieRunningInstance>>(
            [
                [
                    new MagpieRunningInstance(
                        7713,
                        @"C:\BundledMagpie\Magpie.exe",
                        IsBundledInstance: true),
                ],
                Array.Empty<MagpieRunningInstance>(),
                Array.Empty<MagpieRunningInstance>(),
                Array.Empty<MagpieRunningInstance>(),
            ]),
        };
        Exception? clearedOutcome = null;
        try
        {
            await new FourKayLaunchService(
                    new FakeProcessDiscoveryService(),
                    new FakeWindowDiscoveryService(),
                    FakeWindowPlacementService.Create4K(),
                    clearedConfig,
                    clearedMagpie,
                    new FakeScalingWindowInspector())
                .AttachExistingAsync(new FourKayAttachRequest
                {
                    EqDirectory = client.EqDirectory,
                    MagpieDirectory = @"C:\BundledMagpie",
                    Filter = ScalingFilter.Fsr,
                }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Later stages need window and process fakes this test does not
            // supply, so the attach still fails. Only the preflight outcome is
            // under test here: it must no longer be the reason for stopping.
            clearedOutcome = exception;
        }

        Assert.Equal(1, clearedMagpie.ShutdownCalls);
        Assert.False(
            clearedOutcome is DedicatedMagpieConfigReloadRequiredException,
            "A stale engine that quits must stop blocking the launch.");
    }

    private static async Task ReplacementAfterOwnedExitRemainsUntouchedAsync()
    {
        ProcessStartInfo exitInfo = new()
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        exitInfo.ArgumentList.Add("/d");
        exitInfo.ArgumentList.Add("/c");
        exitInfo.ArgumentList.Add("exit 0");
        using Process exitedOwnedProcess = Process.Start(exitInfo)
            ?? throw new TestFailureException(
                "Could not start the isolated exited-process fixture.");
        await exitedOwnedProcess.WaitForExitAsync().ConfigureAwait(false);

        FakeMagpiePortableConfigService config = new();
        FakeMagpieProcessService magpie = new()
        {
            Instances =
            [
                new MagpieRunningInstance(
                    8822,
                    @"C:\BundledMagpie\Magpie.exe",
                    IsBundledInstance: true),
            ],
        };
        FakeScalingWindowInspector inspector = new()
        {
            AllowCleanupCalls = true,
            FallbackInspection = CreateScalingInspection(isActive: false),
        };
        FourKayLaunchService service = new(
            magpieConfig: config,
            magpieProcess: magpie,
            scalingInspector: inspector);

        MagpieScalingCleanupResolution resolution =
            await service.StopExactOwnedSessionAsync(
                new nint(123),
                @"C:\BundledMagpie",
                exitedOwnedProcess,
                TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        Assert.False(resolution.Resolved);
        Assert.True(inspector.InvocationCount > 0);
        Assert.True(magpie.InspectCalls > 0);
        Assert.Equal(0, magpie.ShutdownExactCalls);
        Assert.Equal(0, magpie.ShutdownCalls);
        Assert.Equal(0, config.RollbackCalls);
    }

    private static async Task StopExactOwnedSessionRestoresAttachGeometryAsync()
    {
        await using TempDirectory temp = new();
        FakeClientFixture client = await FakeClientFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        int processId = Environment.ProcessId;
        DateTimeOffset processStart = DateTimeOffset.UnixEpoch;
        WindowDescriptor original = CreateLaunchAcceptanceSourceWindow(
            client,
            processId);
        WindowGeometrySnapshot originalGeometry =
            CreateMaximizedGeometrySnapshot(original);
        WindowGeometrySnapshot scaledGeometry = originalGeometry with
        {
            WindowBounds = new PixelRect(0, 0, 3840, 2160),
            ClientSize = new PixelSize(3840, 2160),
            PlacementFlags = 0,
            ShowCommand = 1,
            NormalWindowBounds = new PixelRect(0, 0, 3840, 2160),
        };
        ProcessDescriptor exactProcess = new(
            processId,
            client.EqGamePath,
            processStart);
        AttachWindowRecoveryState recovery = new(
            exactProcess,
            client.EqGamePath,
            original,
            originalGeometry,
            TimeSpan.FromSeconds(1));

        async Task<(
            MagpieScalingCleanupResolution Resolution,
            FakeWindowPlacementService Placement,
            FakeMagpieProcessService Magpie,
            FakeScalingWindowInspector Inspector,
            List<string> Events)> RunStopAsync(bool exactRestoreResult)
        {
            List<string> events = [];
            FakeProcessDiscoveryService processes = new()
            {
                Current = [exactProcess],
            };
            FakeWindowDiscoveryService windows = new()
            {
                AllowCalls = true,
                StableResult = original,
                VisibleWindowFallback = [original],
            };
            FakeWindowPlacementService placement =
                FakeWindowPlacementService.Create4K();
            placement.CurrentGeometry = scaledGeometry;
            placement.ExactRestoreResult = exactRestoreResult;
            placement.Events = events;
            FakeMagpieProcessService magpie = new()
            {
                ShutdownExactResult = true,
                Events = events,
            };
            FakeScalingWindowInspector inspector = new()
            {
                AllowCleanupCalls = true,
                StopResult = true,
                Events = events,
                InspectionResults =
                    new Queue<MagpieScalingWindowInspection>(
                    [
                        CreateScalingInspection(
                            isActive: true,
                            scalingProcessId: processId),
                        CreateScalingInspection(isActive: false),
                    ]),
            };
            FourKayLaunchService service = new(
                processes,
                windows,
                placement,
                new FakeMagpiePortableConfigService(),
                magpie,
                inspector);
            using Process exactEngine = Process.GetCurrentProcess();
            MagpieScalingCleanupResolution resolution =
                await service.StopExactOwnedSessionAsync(
                    original.Handle,
                    @"C:\BundledMagpie",
                    exactEngine,
                    TimeSpan.FromSeconds(1),
                    recovery).ConfigureAwait(false);
            return (resolution, placement, magpie, inspector, events);
        }

        var restored = await RunStopAsync(exactRestoreResult: true)
            .ConfigureAwait(false);
        Assert.True(restored.Resolution.Resolved);
        Assert.Contains(
            "pre-Attach game-window geometry and show state were also restored",
            restored.Resolution.Message);
        Assert.Equal(1, restored.Placement.RestoreCalls);
        Assert.Equal(originalGeometry, restored.Placement.LastRestoredGeometry!);
        Assert.Equal(originalGeometry, restored.Placement.CurrentGeometry);
        Assert.Equal(1, restored.Magpie.ShutdownExactCalls);
        Assert.Equal(1, restored.Inspector.StopCalls);
        Assert.True(
            restored.Events.IndexOf("shutdown-exact")
                < restored.Events.IndexOf("restore-geometry"));

        var failedRestore = await RunStopAsync(exactRestoreResult: false)
            .ConfigureAwait(false);
        Assert.False(failedRestore.Resolution.Resolved);
        Assert.Contains(
            "could not yet be restored",
            failedRestore.Resolution.Message);
        Assert.Equal(1, failedRestore.Placement.RestoreCalls);
        Assert.Equal(originalGeometry, failedRestore.Placement.LastRestoredGeometry!);
        Assert.Equal(scaledGeometry, failedRestore.Placement.CurrentGeometry);
        Assert.Equal(1, failedRestore.Magpie.ShutdownExactCalls);
        Assert.Equal(1, failedRestore.Inspector.StopCalls);
    }

    private static async Task SameSourceUnownedOwnerPidRemainsUntouchedAsync()
    {
        int unownedProcessId = Environment.ProcessId == int.MaxValue
            ? Environment.ProcessId - 1
            : Environment.ProcessId + 1;
        Process leaseProcess = Process.GetCurrentProcess();
        FakeMagpiePortableConfigService leaseConfig = new();
        FakeMagpieProcessService leaseMagpie = new()
        {
            ShutdownExactResult = false,
        };
        FakeScalingWindowInspector leaseInspector = new()
        {
            AllowCleanupCalls = true,
            FallbackInspection = CreateScalingInspection(
                isActive: true,
                scalingProcessId: unownedProcessId),
        };
        FourKayLaunchService leaseService = new(
            magpieConfig: leaseConfig,
            magpieProcess: leaseMagpie,
            scalingInspector: leaseInspector);
        using (MagpieScalingCleanupLease lease = new(
            @"C:\BundledMagpie",
            new nint(123),
            CreateFakeMagpieTransaction(),
            leaseProcess,
            ownsEngine: true,
            allowChangedContentRollback: true))
        {
            MagpieScalingCleanupResolution leaseResolution =
                await leaseService.ResolveCleanupLeaseAsync(
                    lease,
                    TimeSpan.FromMilliseconds(15)).ConfigureAwait(false);
            Assert.False(leaseResolution.Resolved);
            Assert.Equal(0, leaseInspector.StopCalls);
            Assert.True(leaseMagpie.ShutdownExactCalls > 0);
            Assert.Equal(0, leaseConfig.RollbackCalls);
        }

        Process sessionProcess = Process.GetCurrentProcess();
        FakeMagpieProcessService sessionMagpie = new()
        {
            ShutdownExactResult = false,
        };
        FakeScalingWindowInspector sessionInspector = new()
        {
            AllowCleanupCalls = true,
            FallbackInspection = CreateScalingInspection(
                isActive: true,
                scalingProcessId: unownedProcessId),
        };
        FourKayLaunchService sessionService = new(
            magpieProcess: sessionMagpie,
            scalingInspector: sessionInspector);
        using (sessionProcess)
        {
            MagpieScalingCleanupResolution sessionResolution =
                await sessionService.StopExactOwnedSessionAsync(
                    new nint(123),
                    @"C:\BundledMagpie",
                    sessionProcess,
                    TimeSpan.FromMilliseconds(15)).ConfigureAwait(false);
            Assert.False(sessionResolution.Resolved);
            Assert.Equal(0, sessionInspector.StopCalls);
            Assert.True(sessionMagpie.ShutdownExactCalls > 0);
            Assert.Equal(0, sessionMagpie.ShutdownCalls);
        }
    }

    private static async Task FailedScalingCleanupIsFailClosedAsync()
    {
        MethodInfo cleanupMethod = typeof(FourKayLaunchService).GetMethod(
            "CleanupFailedScalingAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new TestFailureException(
                "The failed-scaling cleanup path is unavailable for validation.");

        List<string> directEvents = [];
        FakeScalingWindowInspector directInspector = new()
        {
            AllowCleanupCalls = true,
            StopResult = true,
            Events = directEvents,
            InspectionResults = new Queue<MagpieScalingWindowInspection>(
                [
                    CreateScalingInspection(isActive: false),
                    CreateScalingInspection(isActive: false),
                ]),
        };
        FakeMagpieProcessService directMagpie = new()
        {
            Events = directEvents,
        };
        FakeMagpiePortableConfigService directConfig = new()
        {
            Events = directEvents,
        };
        FourKayLaunchService directService = new(
            scalingInspector: directInspector,
            magpieProcess: directMagpie,
            magpieConfig: directConfig);
        MagpieFailureCleanupState direct = await InvokeCleanupAsync(
            cleanupMethod,
            directService,
            new MagpieProcessStartResult(
                Process.GetCurrentProcess(),
                AlreadyRunning: false),
            CreateFakeMagpieTransaction()).ConfigureAwait(false);
        Assert.True(direct.StopScalingReportedSuccess);
        Assert.True(direct.InactiveAfterStop);
        Assert.True(direct.AttemptOwnedEngine);
        Assert.True(direct.ExactShutdownAttempted);
        Assert.True(direct.ExactShutdownReportedSuccess);
        Assert.True(direct.EngineAbsenceConfirmed);
        Assert.True(direct.ConfigRollbackAttempted);
        Assert.True(direct.ConfigRollbackReportedSuccess);
        Assert.True(direct.InactiveAfterCleanup);
        Assert.Empty(direct.Issues);
        Assert.Equal(1, directMagpie.ShutdownExactCalls);
        Assert.Equal(1, directConfig.RollbackCalls);
        Assert.True(
            directEvents.IndexOf("stop-scaling")
            < directEvents.IndexOf("shutdown-exact"));
        Assert.True(
            directEvents.IndexOf("shutdown-exact")
            < directEvents.IndexOf("rollback"));

        FakeScalingWindowInspector fallbackInspector = new()
        {
            AllowCleanupCalls = true,
            StopResult = false,
            InspectionResults = new Queue<MagpieScalingWindowInspection>(
            [
                CreateScalingInspection(isActive: true),
                CreateScalingInspection(isActive: false),
            ]),
        };
        FakeMagpieProcessService fallbackMagpie = new()
        {
            ShutdownExactResult = true,
        };
        FakeMagpiePortableConfigService fallbackConfig = new();
        FourKayLaunchService fallbackService = new(
            scalingInspector: fallbackInspector,
            magpieProcess: fallbackMagpie,
            magpieConfig: fallbackConfig);
        MagpieFailureCleanupState fallback = await InvokeCleanupAsync(
            cleanupMethod,
            fallbackService,
            new MagpieProcessStartResult(
                Process.GetCurrentProcess(),
                AlreadyRunning: false),
            CreateFakeMagpieTransaction()).ConfigureAwait(false);
        Assert.False(fallback.StopScalingReportedSuccess);
        Assert.False(fallback.InactiveAfterStop);
        Assert.True(fallback.ExactShutdownAttempted);
        Assert.True(fallback.ExactShutdownReportedSuccess);
        Assert.True(fallback.ConfigRollbackReportedSuccess);
        Assert.True(fallback.InactiveAfterCleanup);
        Assert.True(fallback.Issues.Any(
            issue => issue.Contains("did not confirm", StringComparison.Ordinal)));
        Assert.Equal(1, fallbackMagpie.ShutdownExactCalls);
        Assert.Equal(1, fallbackConfig.RollbackCalls);

        FakeScalingWindowInspector unresolvedInspector = new()
        {
            AllowCleanupCalls = true,
            StopException = new InvalidOperationException("injected stop failure"),
            InspectionResults = new Queue<MagpieScalingWindowInspection>(
            [
                CreateScalingInspection(isActive: true),
                CreateScalingInspection(isActive: true),
            ]),
        };
        FakeMagpieProcessService unresolvedMagpie = new()
        {
            ShutdownExactResult = false,
            Instances =
            [
                new MagpieRunningInstance(
                    Environment.ProcessId,
                    @"C:\BundledMagpie\Magpie.exe",
                    IsBundledInstance: true),
            ],
        };
        FakeMagpiePortableConfigService unresolvedConfig = new();
        FourKayLaunchService unresolvedService = new(
            scalingInspector: unresolvedInspector,
            magpieProcess: unresolvedMagpie,
            magpieConfig: unresolvedConfig);
        MagpieFailureCleanupState unresolved = await InvokeCleanupAsync(
            cleanupMethod,
            unresolvedService,
            new MagpieProcessStartResult(
                Process.GetCurrentProcess(),
                AlreadyRunning: false),
            CreateFakeMagpieTransaction()).ConfigureAwait(false);
        Assert.True(unresolved.ExactShutdownAttempted);
        Assert.False(unresolved.ExactShutdownReportedSuccess);
        Assert.False(unresolved.EngineAbsenceConfirmed);
        Assert.False(unresolved.ConfigRollbackAttempted);
        Assert.False(unresolved.ConfigRollbackReportedSuccess);
        Assert.False(unresolved.InactiveAfterCleanup);
        Assert.True(unresolved.Issues.Any(
            issue => issue.Contains("still active", StringComparison.Ordinal)));
        Assert.Equal(1, unresolvedMagpie.ShutdownExactCalls);
        Assert.Equal(0, unresolvedConfig.RollbackCalls);
    }

    private static async Task ScalingFailureStateAndDisposalAsync()
    {
        await using TempDirectory temp = new();
        FakeClientFixture client = await FakeClientFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        int currentProcessId = Environment.ProcessId;
        WindowDescriptor sourceWindow = new(
            new nint(123),
            currentProcessId,
            "EverQuest",
            "EQGameWindow",
            new PixelRect(20, 20, 1936, 1119),
            new PixelRect(28, 59, 1920, 1080),
            IsMinimized: false,
            ExecutablePath: client.EqGamePath);

        FakeProcessDiscoveryService processes = new()
        {
            Current =
            [
                new ProcessDescriptor(
                    currentProcessId,
                    client.EqGamePath,
                    DateTimeOffset.UnixEpoch),
            ],
        };
        FakeWindowDiscoveryService windows = new()
        {
            AllowCalls = true,
            StableResult = sourceWindow,
            ForegroundResult = true,
        };
        FakeMagpieProcessService magpie = new()
        {
            StartResultFactory = () => new MagpieProcessStartResult(
                Process.GetCurrentProcess(),
                AlreadyRunning: false),
            ShutdownExactResult = true,
        };
        FakeMagpiePortableConfigService config = new()
        {
            RollbackDisposition =
                MagpiePortableConfigRollbackDisposition.RetryRequired,
        };
        FakeScalingWindowInspector inspector = new()
        {
            AllowCleanupCalls = true,
            WaitResult = CreateScalingInspection(isActive: true),
            StopResult = false,
            InspectionResults = new Queue<MagpieScalingWindowInspection>(
            [
                CreateScalingInspection(isActive: true),
                CreateScalingInspection(isActive: false),
            ]),
        };
        FourKayLaunchService service = new(
            processes,
            windows,
            FakeWindowPlacementService.Create4K(),
            config,
            magpie,
            inspector);

        MagpieScalingCleanupException failure =
            await Assert.ThrowsAsync<MagpieScalingCleanupException>(
                () => service.AttachExistingAsync(new FourKayAttachRequest
                {
                    EqDirectory = client.EqDirectory,
                    MagpieDirectory = @"C:\BundledMagpie",
                    Filter = ScalingFilter.Fsr,
                    TargetMonitor = new nint(42),
                })).ConfigureAwait(false);
        Assert.True(failure.StartupFailure is UnsafeScalingAttachmentException);
        Assert.True(failure.CleanupState.ExactShutdownAttempted);
        Assert.True(failure.CleanupState.ExactShutdownReportedSuccess);
        Assert.False(failure.CleanupState.ConfigRollbackReportedSuccess);
        Assert.False(failure.CleanupState.ConfigRollbackResolved);
        Assert.True(failure.CleanupState.InactiveAfterCleanup);
        Assert.True(failure.PendingConfigTransaction is not null);
        Assert.True(failure.CleanupLease.OwnsEngine);
        Assert.True(failure.CleanupLease.AllowChangedContentRollback);
        Assert.Equal(1, magpie.ShutdownExactCalls);
        Assert.Equal(1, config.RollbackCalls);
        Assert.True(windows.LastProcess is not null);
        Assert.Throws<InvalidOperationException>(
            () => _ = windows.LastProcess!.Handle);
        Assert.True(magpie.StartedProcess is not null);
        _ = magpie.StartedProcess!.Handle;
        failure.CleanupLease.Dispose();
        Assert.Throws<InvalidOperationException>(
            () => _ = magpie.StartedProcess!.Handle);

        FakeProcessDiscoveryService cancelledProcesses = new()
        {
            Current = processes.Current,
        };
        FakeWindowDiscoveryService cancelledWindows = new()
        {
            AllowCalls = true,
            StableResult = sourceWindow,
            ForegroundResult = true,
        };
        FakeMagpieProcessService cancelledMagpie = new()
        {
            StartResultFactory = () => new MagpieProcessStartResult(
                Process.GetCurrentProcess(),
                AlreadyRunning: false),
        };
        FakeScalingWindowInspector cancelledInspector = new()
        {
            AllowCleanupCalls = true,
            WaitException = new OperationCanceledException(
                "Injected verification cancellation."),
            StopResult = true,
            InspectionResults = new Queue<MagpieScalingWindowInspection>(
                [
                    CreateScalingInspection(isActive: false),
                    CreateScalingInspection(isActive: false),
                ]),
        };
        FakeMagpiePortableConfigService cancelledConfig = new();
        FourKayLaunchService cancelledService = new(
            cancelledProcesses,
            cancelledWindows,
            FakeWindowPlacementService.Create4K(),
            cancelledConfig,
            cancelledMagpie,
            cancelledInspector);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => cancelledService.AttachExistingAsync(new FourKayAttachRequest
            {
                EqDirectory = client.EqDirectory,
                MagpieDirectory = @"C:\BundledMagpie",
                Filter = ScalingFilter.Fsr,
                ExpectedClientSize = new PixelSize(1920, 1080),
                TargetMonitor = new nint(42),
            })).ConfigureAwait(false);
        Assert.Equal(1, cancelledMagpie.ShutdownExactCalls);
        Assert.Equal(1, cancelledConfig.RollbackCalls);
        Assert.True(cancelledWindows.LastProcess is not null);
        Assert.Throws<InvalidOperationException>(
            () => _ = cancelledWindows.LastProcess!.Handle);
        Assert.True(cancelledMagpie.StartedProcess is not null);
        Assert.Throws<InvalidOperationException>(
            () => _ = cancelledMagpie.StartedProcess!.Handle);
    }

    private static async Task<MagpieFailureCleanupState> InvokeCleanupAsync(
        MethodInfo cleanupMethod,
        FourKayLaunchService service,
        MagpieProcessStartResult? magpie,
        MagpiePortableConfigTransaction transaction)
    {
        object? invocation = cleanupMethod.Invoke(
            service,
            [new nint(123), @"C:\BundledMagpie", magpie, transaction]);
        if (invocation is not Task<MagpieFailureCleanupState> cleanupTask)
        {
            throw new TestFailureException(
                "The failed-scaling cleanup path returned an unexpected task.");
        }

        return await cleanupTask.ConfigureAwait(false);
    }

    private static WindowDescriptor CreateLaunchAcceptanceSourceWindow(
        FakeClientFixture client,
        int processId) =>
        new(
            new nint(123),
            processId,
            "EverQuest",
            "EQGameWindow",
            new PixelRect(20, 20, 1936, 1119),
            new PixelRect(28, 59, 1920, 1080),
            IsMinimized: false,
            ExecutablePath: client.EqGamePath);

    private static WindowGeometrySnapshot CreateMaximizedGeometrySnapshot(
        WindowDescriptor source) =>
        new(
            source.WindowBounds,
            new PixelSize(
                source.ClientBounds.Width,
                source.ClientBounds.Height),
            PlacementFlags: 2,
            ShowCommand: 3,
            MinimumPosition: new WindowPlacementPoint(-1, -1),
            MaximumPosition: new WindowPlacementPoint(18, 12),
            NormalWindowBounds: new PixelRect(240, 160, 1616, 939));

    private static async Task<LiveScaleHarness> CreateLiveScaleHarnessAsync(
        string root)
    {
        FakeClientFixture client = await FakeClientFixture.CreateAsync(root)
            .ConfigureAwait(false);
        string nativeFixture = Path.Combine(Environment.SystemDirectory, "ping.exe");
        if (!File.Exists(nativeFixture))
        {
            throw new TestFailureException(
                "The Windows ping executable is unavailable for exact process-path fixtures.");
        }

        File.Copy(nativeFixture, client.EqGamePath, overwrite: true);
        string magpieDirectory = Path.Combine(root, "Bundled Magpie");
        Directory.CreateDirectory(magpieDirectory);
        string magpiePath = Path.Combine(magpieDirectory, "Magpie.exe");
        File.Copy(nativeFixture, magpiePath, overwrite: true);

        Process? gameProcess = null;
        Process? magpieProcess = null;
        try
        {
            gameProcess = StartLongRunningNativeFixture(client.EqGamePath);
            magpieProcess = StartLongRunningNativeFixture(magpiePath);
            PixelSize previousSize = new(1920, 1080);
            FakeWindowPlacementService placement =
                FakeWindowPlacementService.Create4K();
            MonitorDescriptor monitor = placement.GetMonitors().Single();
            WindowDescriptor sourceWindow = CreateLiveScaleSourceWindow(
                gameProcess,
                client.EqGamePath,
                previousSize);
            WindowPlacementResult initialPlacement = new(
                IsExactAndOnScreen: true,
                previousSize,
                ActualClientSize: previousSize,
                WindowBounds: sourceWindow.WindowBounds,
                monitor,
                Issues: []);
            PixelRect destination = monitor.Bounds;
            MagpieScalingWindowInspection inactive =
                CreateScalingInspection(isActive: false);
            FourKayLaunchResult activeLaunch = new(
                LauncherProcess: null,
                gameProcess,
                sourceWindow,
                initialPlacement,
                EffectiveFilter: ScalingFilter.Fsr,
                UiCompatibilityMode: FourKayUiCompatibilityMode.GenericOrCustom,
                ExpectedDestinationRegion: destination,
                magpieDirectory,
                new MagpiePortableConfigResult(
                    Path.Combine(magpieDirectory, "config", "config.json"),
                    ScalingModeIndex: 0,
                    ProfileIndex: 0,
                    ContentChanged: false),
                new MagpieProcessStartResult(
                    magpieProcess,
                    AlreadyRunning: false),
                inactive,
                AttachedToExistingGame: true,
                Warnings: []);
            FakeWindowDiscoveryService windows = new()
            {
                AllowCalls = true,
                VisibleWindowFallback = [sourceWindow],
                UpdateVisibleFallbackFromStableResult = true,
            };
            FakeScalingWindowInspector inspector = new()
            {
                AllowCleanupCalls = true,
                StopResult = true,
                FallbackInspection = inactive,
            };
            FakeForegroundWindowService foreground = new();
            FourKayLaunchService service = new(
                windowDiscovery: windows,
                windowPlacement: placement,
                scalingInspector: inspector,
                foregroundWindow: foreground);
            return new LiveScaleHarness(
                service,
                client,
                gameProcess,
                magpieProcess,
                activeLaunch,
                windows,
                placement,
                inspector,
                foreground);
        }
        catch
        {
            StopNativeFixtureProcess(magpieProcess);
            StopNativeFixtureProcess(gameProcess);
            throw;
        }
    }

    private static Process StartLongRunningNativeFixture(string executablePath)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-t");
        startInfo.ArgumentList.Add("127.0.0.1");
        Process process = Process.Start(startInfo)
            ?? throw new TestFailureException(
                $"Could not start the native process fixture '{executablePath}'.");
        if (process.WaitForExit(100))
        {
            process.Dispose();
            throw new TestFailureException(
                $"The native process fixture '{executablePath}' exited during startup.");
        }

        return process;
    }

    private static WindowDescriptor CreateLiveScaleSourceWindow(
        Process gameProcess,
        string executablePath,
        PixelSize clientSize)
    {
        return new WindowDescriptor(
            new nint(123),
            gameProcess.Id,
            "EverQuest",
            "EQGameWindow",
            new PixelRect(
                100,
                100,
                clientSize.Width + 16,
                clientSize.Height + 39),
            new PixelRect(108, 139, clientSize.Width, clientSize.Height),
            IsMinimized: false,
            ExecutablePath: executablePath);
    }

    private static void StopNativeFixtureProcess(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                _ = process.WaitForExit(5_000);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or System.ComponentModel.Win32Exception
                or NotSupportedException)
        {
            // Test cleanup is best-effort; the assertions already completed.
        }
        finally
        {
            process.Dispose();
        }
    }

    private sealed class LiveScaleHarness : IDisposable
    {
        public LiveScaleHarness(
            FourKayLaunchService service,
            FakeClientFixture client,
            Process gameProcess,
            Process magpieProcess,
            FourKayLaunchResult activeLaunch,
            FakeWindowDiscoveryService windows,
            FakeWindowPlacementService placement,
            FakeScalingWindowInspector inspector,
            FakeForegroundWindowService foreground)
        {
            Service = service;
            Client = client;
            GameProcess = gameProcess;
            MagpieProcess = magpieProcess;
            ActiveLaunch = activeLaunch;
            Windows = windows;
            Placement = placement;
            Inspector = inspector;
            Foreground = foreground;
        }

        public FourKayLaunchService Service { get; }

        public FakeClientFixture Client { get; }

        public Process GameProcess { get; }

        public Process MagpieProcess { get; }

        public FourKayLaunchResult ActiveLaunch { get; }

        public FakeWindowDiscoveryService Windows { get; }

        public FakeWindowPlacementService Placement { get; }

        public FakeScalingWindowInspector Inspector { get; }

        public FakeForegroundWindowService Foreground { get; }

        public PixelSize PreviousSize =>
            ActiveLaunch.Placement.RequestedClientSize;

        public PixelSize TargetSize => ActiveLaunch.Placement.Monitor.Bounds.Size;

        public FourKayLiveScaleRequest CreateRequest(ResolutionPlan plan) =>
            new()
            {
                ActiveLaunch = ActiveLaunch,
                RequestedPlan = plan,
                WindowTimeout = TimeSpan.FromSeconds(1),
            };

        public WindowDescriptor CreateSourceWindow(PixelSize clientSize) =>
            CreateLiveScaleSourceWindow(
                GameProcess,
                Client.EqGamePath,
                clientSize);

        public void Dispose()
        {
            StopNativeFixtureProcess(MagpieProcess);
            StopNativeFixtureProcess(GameProcess);
        }
    }

    private static LaunchAcceptanceHarness CreateLaunchAcceptanceHarness(
        FakeClientFixture client,
        WindowDescriptor sourceWindow,
        MagpieScalingWindowInspection waitResult,
        IOverlayCompatibilityService? overlayCompatibility = null)
    {
        FakeProcessDiscoveryService processes = new()
        {
            Current =
            [
                new ProcessDescriptor(
                    sourceWindow.ProcessId,
                    client.EqGamePath,
                    DateTimeOffset.UnixEpoch),
            ],
        };
        FakeWindowDiscoveryService windows = new()
        {
            AllowCalls = true,
            StableResult = sourceWindow,
            ForegroundResult = true,
        };
        FakeWindowPlacementService placement =
            FakeWindowPlacementService.Create4K();
        FakeMagpiePortableConfigService config = new();
        FakeMagpieProcessService magpie = new()
        {
            StartResultFactory = () => new MagpieProcessStartResult(
                Process.GetCurrentProcess(),
                AlreadyRunning: false),
            ShutdownExactResult = true,
        };
        FakeScalingWindowInspector inspector = new()
        {
            AllowCleanupCalls = true,
            WaitResult = waitResult,
            StopResult = false,
            InspectionResults = new Queue<MagpieScalingWindowInspection>(
            [
                CreateScalingInspection(isActive: false),
                CreateScalingInspection(isActive: false),
            ]),
        };
        FourKayLaunchService service = new(
            processes,
            windows,
            placement,
            config,
            magpie,
            inspector,
            overlayCompatibility: overlayCompatibility);
        FourKayAttachRequest request = new()
        {
            EqDirectory = client.EqDirectory,
            MagpieDirectory = @"C:\BundledMagpie",
            Filter = ScalingFilter.Fsr,
            ExpectedClientSize = new PixelSize(1920, 1080),
            TargetMonitor = new nint(42),
            WindowTimeout = TimeSpan.FromSeconds(1),
            ScalingTimeout = TimeSpan.FromSeconds(1),
        };
        return new LaunchAcceptanceHarness(
            service,
            request,
            config,
            magpie,
            inspector);
    }

    private static MagpieScalingWindowInspection CreateExactSafeScalingInspection(
        int scalingProcessId,
        nint sourceWindowHandle,
        PixelSize sourceSize,
        PixelRect monitorBounds)
    {
        PixelRect sourceRegion = new(
            28,
            59,
            sourceSize.Width,
            sourceSize.Height);
        CoordinateMappingValidationResult coordinateValidation =
            new CoordinateMappingValidator().Validate(
                sourceRegion,
                monitorBounds);
        if (!coordinateValidation.IsSafe)
        {
            throw new TestFailureException(
                "The exact launch-acceptance fixture must have a safe coordinate map.");
        }

        return new MagpieScalingWindowInspection(
            IsActive: true,
            IsVisible: true,
            IsFullscreen: true,
            IsWindowedScaling: false,
            IsSafeForInput: true,
            ScalingWindowHandle: new nint(456),
            ScalingProcessId: scalingProcessId,
            SourceWindowHandle: sourceWindowHandle,
            ScalingWindowBounds: monitorBounds,
            MonitorBounds: monitorBounds,
            SourceRegion: sourceRegion,
            DestinationRegion: monitorBounds,
            CoordinateValidation: coordinateValidation,
            Issues: []);
    }

    private sealed record LaunchAcceptanceHarness(
        FourKayLaunchService Service,
        FourKayAttachRequest Request,
        FakeMagpiePortableConfigService Config,
        FakeMagpieProcessService Magpie,
        FakeScalingWindowInspector Inspector);

    private static MagpiePortableConfigTransaction CreateFakeMagpieTransaction() =>
        new(
            @"C:\BundledMagpie\config\config.json",
            OriginalFileExisted: true,
            OriginalContent: Encoding.UTF8.GetBytes("{\"original\":true}"),
            OriginalAttributes: FileAttributes.Normal,
            OriginalLastWriteTimeUtc: DateTime.UnixEpoch,
            AppliedContent: Encoding.UTF8.GetBytes("{\"autoScale\":1}"),
            ScalingModeIndex: 0,
            ProfileIndex: 1);

    private static MagpieScalingWindowInspection CreateScalingInspection(
        bool isActive,
        int? scalingProcessId = null)
    {
        return new MagpieScalingWindowInspection(
            IsActive: isActive,
            IsVisible: isActive,
            IsFullscreen: isActive,
            IsWindowedScaling: false,
            IsSafeForInput: false,
            ScalingWindowHandle: isActive ? new nint(456) : nint.Zero,
            ScalingProcessId: isActive
                ? scalingProcessId ?? Environment.ProcessId
                : null,
            SourceWindowHandle: isActive ? new nint(123) : nint.Zero,
            ScalingWindowBounds: null,
            MonitorBounds: null,
            SourceRegion: null,
            DestinationRegion: null,
            CoordinateValidation: null,
            Issues: isActive ? ["still active"] : ["inactive"]);
    }

    private static FourKayPreparedState CreateJournalState(
        string root,
        FourKayJournalStatus status,
        DateTimeOffset preparedAtUtc)
    {
        string eqDirectory = Path.GetFullPath(Path.Combine(root, "EverQuest"));
        string stateDirectory = Path.GetFullPath(Path.Combine(root, "state"));
        string eqGamePath = Path.Combine(eqDirectory, "eqgame.exe");
        return new FourKayPreparedState
        {
            SessionId = Guid.NewGuid(),
            Status = status,
            PreparedAtUtc = preparedAtUtc,
            EqDirectory = eqDirectory,
            StateDirectory = stateDirectory,
            EqGamePath = eqGamePath,
            EqClientIniPath = Path.Combine(eqDirectory, "eqclient.ini"),
            BackupManifestPath = Path.Combine(stateDirectory, "backup", "manifest.json"),
            DpiCompatibility = new DpiCompatibilitySnapshot(
                eqGamePath,
                ValueExisted: false,
                OriginalValue: null,
                Microsoft.Win32.RegistryValueKind.String,
                AppliedValue: "~ HIGHDPIAWARE"),
            ResolutionPlan = new ResolutionPlanner().CreatePlan(
                new PixelSize(3840, 2160),
                ResolutionPresetKind.Comfort),
            TargetMonitorHandle = 42,
            TargetMonitorBounds = new PixelRect(0, 0, 3840, 2160),
            OriginalIniSha256 = new string('0', 64),
            AppliedIniSha256 = status == FourKayJournalStatus.Prepared
                ? new string('1', 64)
                : null,
            ChatFontSizeIncrease = 1,
            AppliedChatFontSize = status == FourKayJournalStatus.Prepared ? 4 : null,
            UiCompatibilityMode = FourKayUiCompatibilityMode.GenericOrCustom,
        };
    }

    private static FourKayPreparationRequest CreatePreparationRequest(
        string eqDirectory,
        string stateDirectory,
        int chatFontSizeIncrease)
    {
        return new FourKayPreparationRequest
        {
            EqDirectory = eqDirectory,
            StateDirectory = stateDirectory,
            ResolutionPlan = new ResolutionPlanner().CreatePlan(
                new PixelSize(3840, 2160),
                ResolutionPresetKind.Comfort),
            TargetMonitor = new nint(42),
            ChatFontSizeIncrease = chatFontSizeIncrease,
            UiCompatibilityMode = FourKayUiCompatibilityMode.GenericOrCustom,
        };
    }

    private static FourKayLaunchRequest CreateLaunchRequest(FourKayPreparedState state)
    {
        return new FourKayLaunchRequest
        {
            PreparedState = state,
            LauncherPath = Path.Combine(state.EqDirectory, "LaunchPad.exe"),
            MagpieDirectory = "Magpie",
            GameStartTimeout = TimeSpan.FromSeconds(1),
            WindowTimeout = TimeSpan.FromSeconds(1),
            ScalingTimeout = TimeSpan.FromSeconds(1),
        };
    }

    private static JsonElement FindNamed(JsonElement array, string name)
    {
        foreach (JsonElement element in array.EnumerateArray())
        {
            if (string.Equals(
                element.GetProperty("name").GetString(),
                name,
                StringComparison.Ordinal))
            {
                return element;
            }
        }

        throw new TestFailureException($"JSON array did not contain '{name}'.");
    }

    private static TResult? InvokePrivate<TResult>(MethodInfo method, params object?[]? arguments)
    {
        object? result = method.Invoke(null, arguments);
        return (TResult?)result;
    }

    private static int CountPreamble(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> preamble)
    {
        int count = 0;
        for (int index = 0; index <= bytes.Length - preamble.Length; index++)
        {
            if (bytes[index..].StartsWith(preamble))
            {
                count++;
            }
        }

        return count;
    }

    private static void SupersededEngineInstancesAreNotForeign()
    {
        // The engine is provisioned per application version, so updating moves
        // the path Magpie runs from. A copy left in the tray by the previous
        // version must be recognised as this application's own engine.
        const string root = @"C:\Users\Player\AppData\Local\SpinFOURKAYYY\engine-runtime";
        string current = Path.Combine(root, "app-1.0.7-magpie-0.12.1", "Magpie.exe");
        string previous = Path.Combine(root, "app-1.0.6-magpie-0.12.1", "Magpie.exe");

        Assert.Equal(
            MagpieInstanceOrigin.BundledCurrent,
            MagpieProcessService.ClassifyOrigin(current, current, root));
        Assert.Equal(
            MagpieInstanceOrigin.OwnPreviousRuntime,
            MagpieProcessService.ClassifyOrigin(previous, current, root));

        // Casing must not change the verdict; Windows paths are not case
        // sensitive and the upgrade path routinely varies it.
        Assert.Equal(
            MagpieInstanceOrigin.OwnPreviousRuntime,
            MagpieProcessService.ClassifyOrigin(
                previous.ToUpperInvariant(),
                current,
                root));

        // A real separate installation stays foreign and still needs consent.
        Assert.Equal(
            MagpieInstanceOrigin.Foreign,
            MagpieProcessService.ClassifyOrigin(
                @"C:\Program Files\Magpie\Magpie.exe",
                current,
                root));

        // A sibling of the runtime root is not inside it.
        Assert.Equal(
            MagpieInstanceOrigin.Foreign,
            MagpieProcessService.ClassifyOrigin(
                @"C:\Users\Player\AppData\Local\SpinFOURKAYYY\somewhere-else\Magpie.exe",
                current,
                root));

        // A folder inside the runtime root that is not a provisioned runtime
        // name is not ours either.
        Assert.Equal(
            MagpieInstanceOrigin.Foreign,
            MagpieProcessService.ClassifyOrigin(
                Path.Combine(root, "unrelated", "Magpie.exe"),
                current,
                root));

        // An unreadable path must never be closed without asking.
        Assert.Equal(
            MagpieInstanceOrigin.Foreign,
            MagpieProcessService.ClassifyOrigin(null, current, root));
        Assert.Equal(
            MagpieInstanceOrigin.Foreign,
            MagpieProcessService.ClassifyOrigin("   ", current, root));
        Assert.Equal(
            MagpieInstanceOrigin.Foreign,
            MagpieProcessService.ClassifyOrigin(previous, current, null));

        // The origin drives which instances need consent.
        MagpieRunningInstance superseded = new(4321, previous, false)
        {
            Origin = MagpieInstanceOrigin.OwnPreviousRuntime,
        };
        Assert.True(superseded.IsSupersededOwnRuntime);
        Assert.False(superseded.IsForeignInstance);
        Assert.False(superseded.IsBundledInstance);

        // Callers that only know the old boolean keep their previous meaning.
        Assert.True(
            new MagpieRunningInstance(1, previous, IsBundledInstance: false)
                .IsForeignInstance);
        Assert.Equal(
            MagpieInstanceOrigin.BundledCurrent,
            new MagpieRunningInstance(1, current, IsBundledInstance: true).Origin);

        Assert.True(
            MagpieRuntimeProvisioner.IsRuntimeDirectoryName("app-1.0.7-magpie-0.12.1"));
        Assert.False(MagpieRuntimeProvisioner.IsRuntimeDirectoryName("backups"));
        Assert.False(
            MagpieRuntimeProvisioner.IsRuntimeDirectoryName(
                ".app-1.0.7-magpie-0.12.1.pending-abc"));
        Assert.False(MagpieRuntimeProvisioner.IsRuntimeDirectoryName(null));
    }

    private static async Task SupersededEngineRuntimePruningAsync()
    {
        await using TempDirectory temp = new();
        string root = Path.Combine(temp.Path, "engine-runtime");
        const string currentKey = "app-1.0.7-magpie-0.12.1";
        string[] created =
        [
            currentKey,
            "app-1.0.5-magpie-0.12.1",
            "app-1.0.6-magpie-0.12.1",
            "backups",
            ".app-1.0.7-magpie-0.12.1.broken-abc",
        ];
        foreach (string name in created)
        {
            Directory.CreateDirectory(Path.Combine(root, name));
            await File.WriteAllTextAsync(
                Path.Combine(root, name, "Magpie.exe"),
                "engine").ConfigureAwait(false);
        }

        string liveRuntime = Path.Combine(root, "app-1.0.6-magpie-0.12.1");
        IReadOnlyList<string> removed =
            MagpieRuntimeProvisioner.PruneSupersededRuntimes(
                root,
                currentKey,
                candidate => string.Equals(
                    Path.TrimEndingDirectorySeparator(candidate),
                    liveRuntime,
                    StringComparison.OrdinalIgnoreCase));

        // Only the superseded runtime with no live engine is removed.
        Assert.Equal(1, removed.Count);
        Assert.Equal(
            Path.Combine(root, "app-1.0.5-magpie-0.12.1"),
            Path.TrimEndingDirectorySeparator(removed[0]));

        Assert.True(
            Directory.Exists(Path.Combine(root, currentKey)),
            "The current runtime must never be pruned.");
        Assert.True(
            Directory.Exists(liveRuntime),
            "A runtime with a live engine must be left completely alone.");
        Assert.True(
            File.Exists(Path.Combine(liveRuntime, "Magpie.exe")),
            "A live runtime must not be partially deleted.");
        Assert.True(
            Directory.Exists(Path.Combine(root, "backups")),
            "Unrelated folders must not be touched.");
        Assert.True(
            Directory.Exists(Path.Combine(root, ".app-1.0.7-magpie-0.12.1.broken-abc")),
            "Quarantine folders are not runtimes and must not be pruned here.");
        Assert.False(Directory.Exists(Path.Combine(root, "app-1.0.5-magpie-0.12.1")));

        // The in-use predicate is the only thing protecting that runtime: once
        // its engine is gone, the next run removes it.
        IReadOnlyList<string> afterEngineExit =
            MagpieRuntimeProvisioner.PruneSupersededRuntimes(root, currentKey);
        Assert.Equal(1, afterEngineExit.Count);
        Assert.False(Directory.Exists(liveRuntime));
        Assert.True(Directory.Exists(Path.Combine(root, currentKey)));

        // With nothing superseded left, further runs are a no-op, not an error.
        Assert.Empty(
            MagpieRuntimeProvisioner.PruneSupersededRuntimes(root, currentKey));

        // A missing root is not an error either.
        Assert.Empty(
            MagpieRuntimeProvisioner.PruneSupersededRuntimes(
                Path.Combine(temp.Path, "no-such-root"),
                currentKey));
    }

    private static void StartupSwitchInterpretation()
    {
        Assert.Equal(AutoPlayMode.None, StartupCommandLine.Parse(null).AutoPlay);
        Assert.Equal(AutoPlayMode.None, StartupCommandLine.Parse([]).AutoPlay);
        Assert.False(StartupCommandLine.Parse([]).RequestsAutoPlay);

        StartupCommandLine play = StartupCommandLine.Parse(["--play"]);
        Assert.Equal(AutoPlayMode.OfficialLauncher, play.AutoPlay);
        Assert.True(play.RequestsAutoPlay);
        Assert.Null(play.AutoPlayRejection);
        Assert.False(play.SkipRunningGameDetection);

        // Switch order, casing, and surrounding whitespace must not change the
        // interpretation; a desktop shortcut is edited by hand more often than
        // the application is.
        StartupCommandLine enhanced = StartupCommandLine.Parse(
            ["--updated-from", "1.0.6", "  --MANUAL  ", "--Play-Enhanced"]);
        Assert.Equal(AutoPlayMode.SpinTextureEnhanced, enhanced.AutoPlay);
        Assert.True(enhanced.SkipRunningGameDetection);
        Assert.Null(enhanced.AutoPlayRejection);

        // Unrecognized switches are ignored so a future release can add one
        // without older builds refusing to open.
        Assert.Equal(
            AutoPlayMode.OfficialLauncher,
            StartupCommandLine.Parse(["--future-switch", "--play"]).AutoPlay);

        // The updater's version value is consumed, so a malformed pair can
        // never be misread as a launch request.
        Assert.Equal(
            AutoPlayMode.None,
            StartupCommandLine.Parse(["--updated-from", "--play"]).AutoPlay);

        // Ambiguity refuses instead of silently choosing one launch path.
        StartupCommandLine conflict =
            StartupCommandLine.Parse(["--play", "--play-enhanced"]);
        Assert.Equal(AutoPlayMode.None, conflict.AutoPlay);
        Assert.False(conflict.RequestsAutoPlay);
        Assert.Contains(
            "--play-enhanced",
            conflict.AutoPlayRejection ?? string.Empty);

        // Repeats are idempotent and blank entries are inert.
        Assert.Equal(
            AutoPlayMode.OfficialLauncher,
            StartupCommandLine.Parse(["--play", "--play"]).AutoPlay);
        Assert.Equal(
            AutoPlayMode.None,
            StartupCommandLine.Parse(["", "   "]).AutoPlay);
    }

    private static async Task DesktopShortcutPlanContentsAsync()
    {
        await using TempDirectory temp = new();
        ShortcutFixture fixture = await ShortcutFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);

        DesktopShortcutPlan normal = DesktopShortcutPlan.Create(
            DesktopShortcutKind.NormalPlay,
            fixture.ApplicationPath,
            fixture.LegendsDirectory);
        Assert.Equal("EverQuest (SpinFOURKAYYY).lnk", normal.FileName);
        Assert.Equal(StartupCommandLine.PlayArgument, normal.Arguments);
        Assert.Equal(fixture.ApplicationPath, normal.TargetPath);
        Assert.Equal(fixture.ApplicationDirectory, normal.WorkingDirectory);
        Assert.Equal(fixture.EqGamePath, normal.IconPath);
        Assert.Equal(0, normal.IconIndex);
        Assert.True(normal.UsesLegendsIcon);

        DesktopShortcutPlan enhanced = DesktopShortcutPlan.Create(
            DesktopShortcutKind.EnhancedPlay,
            fixture.ApplicationPath,
            fixture.LegendsDirectory);
        Assert.Equal("Enhanced EverQuest (SpinFOURKAYYY).lnk", enhanced.FileName);
        Assert.Equal(StartupCommandLine.PlayEnhancedArgument, enhanced.Arguments);

        // The two shortcuts must never collide on one desktop.
        Assert.False(
            string.Equals(
                normal.FileName,
                enhanced.FileName,
                StringComparison.OrdinalIgnoreCase),
            "The normal and enhanced shortcuts share one file name.");

        // Without an installed client the shortcut still gets a real icon file
        // rather than a dangling reference.
        DesktopShortcutPlan missingClient = DesktopShortcutPlan.Create(
            DesktopShortcutKind.NormalPlay,
            fixture.ApplicationPath,
            Path.Combine(temp.Path, "Not Installed"));
        Assert.Equal(fixture.ApplicationPath, missingClient.IconPath);
        Assert.False(missingClient.UsesLegendsIcon);
        Assert.Equal(
            fixture.ApplicationPath,
            DesktopShortcutPlan.Create(
                DesktopShortcutKind.NormalPlay,
                fixture.ApplicationPath,
                null).IconPath);

        // A shortcut is never planned for an application that is not there.
        _ = Assert.Throws<FileNotFoundException>(
            () => DesktopShortcutPlan.Create(
                DesktopShortcutKind.NormalPlay,
                Path.Combine(fixture.ApplicationDirectory, "Missing.exe"),
                fixture.LegendsDirectory));
        _ = Assert.Throws<ArgumentException>(
            () => DesktopShortcutPlan.Create(
                DesktopShortcutKind.NormalPlay,
                "   ",
                fixture.LegendsDirectory));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => DesktopShortcutPlan.Create(
                (DesktopShortcutKind)7,
                fixture.ApplicationPath,
                fixture.LegendsDirectory));

        Assert.Equal(
            Path.Combine(temp.Path, normal.FileName),
            normal.ResolveDestinationPath(temp.Path));
    }

    private static async Task WindowsShortcutVerifiedRoundTripAsync()
    {
        await using TempDirectory temp = new();
        ShortcutFixture fixture = await ShortcutFixture.CreateAsync(temp.Path)
            .ConfigureAwait(false);
        string desktop = Path.Combine(temp.Path, "Desktop");
        Directory.CreateDirectory(desktop);

        // The shell's shortcut component is apartment-threaded, and the
        // application always calls it from the WPF UI thread. Exercising it on
        // an STA thread keeps the test on the same path as production.
        await RunOnStaThreadAsync(() =>
        {
            DesktopShortcutPlan plan = DesktopShortcutPlan.Create(
                DesktopShortcutKind.EnhancedPlay,
                fixture.ApplicationPath,
                fixture.LegendsDirectory);
            string shortcutPath = plan.ResolveDestinationPath(desktop);

            WindowsShortcutSnapshot saved =
                WindowsShortcutService.Save(plan, shortcutPath);
            Assert.True(
                File.Exists(shortcutPath),
                "The shortcut file was not created.");
            Assert.Equal(fixture.ApplicationPath, saved.TargetPath);
            Assert.Equal(StartupCommandLine.PlayEnhancedArgument, saved.Arguments);
            Assert.Equal(fixture.ApplicationDirectory, saved.WorkingDirectory);
            Assert.Equal(fixture.EqGamePath, saved.IconPath);
            Assert.Equal(0, saved.IconIndex);
            Assert.Equal(plan.Description, saved.Description);

            WindowsShortcutSnapshot reread =
                WindowsShortcutService.Read(shortcutPath);
            Assert.Equal(saved.TargetPath, reread.TargetPath);
            Assert.Equal(saved.Arguments, reread.Arguments);
            Assert.Equal(saved.IconPath, reread.IconPath);

            // Replacing an existing shortcut leaves exactly one file behind.
            _ = WindowsShortcutService.Save(plan, shortcutPath);
            Assert.Equal(1, Directory.GetFiles(desktop).Length);

            // A missing destination folder refuses instead of creating one.
            _ = Assert.Throws<DirectoryNotFoundException>(
                () => WindowsShortcutService.Save(
                    plan,
                    Path.Combine(temp.Path, "No Such Folder", plan.FileName)));
            _ = Assert.Throws<FileNotFoundException>(
                () => WindowsShortcutService.Read(
                    Path.Combine(desktop, "missing.lnk")));
        }).ConfigureAwait(false);
    }

    private static Task RunOnStaThreadAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        TaskCompletionSource completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}

internal sealed class TestRunner
{
    private readonly List<(string Name, Func<Task> Run)> _tests = [];

    public void Add(string name, Action test)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(test);
        _tests.Add((name, () =>
        {
            test();
            return Task.CompletedTask;
        }
        ));
    }

    public void Add(string name, Func<Task> test)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(test);
        _tests.Add((name, test));
    }

    public async Task<int> RunAsync()
    {
        int failures = 0;
        foreach ((string name, Func<Task> run) in _tests)
        {
            try
            {
                await run().ConfigureAwait(false);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("[PASS] ");
                Console.ResetColor();
                Console.WriteLine(name);
            }
            catch (Exception exception)
            {
                failures++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("[FAIL] ");
                Console.ResetColor();
                Console.WriteLine(name);
                Console.WriteLine($"       {exception.GetType().Name}: {exception.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{_tests.Count - failures}/{_tests.Count} passed; {failures} failed.");
        return failures == 0 ? 0 : 1;
    }
}

internal static class Assert
{
    public static void True(bool condition, string? message = null)
    {
        if (!condition)
        {
            throw new TestFailureException(message ?? "Expected true, but found false.");
        }
    }

    public static void False(bool condition, string? message = null) =>
        True(!condition, message ?? "Expected false, but found true.");

    public static void Null(object? value)
    {
        if (value is not null)
        {
            throw new TestFailureException($"Expected null, but found '{value}'.");
        }
    }

    public static void Empty<T>(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Any())
        {
            throw new TestFailureException("Expected an empty sequence.");
        }
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new TestFailureException($"Expected '{expected}', but found '{actual}'.");
        }
    }

    public static void NearlyEqual(double expected, double actual, double tolerance = 0.0001)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new TestFailureException(
                $"Expected '{expected}' ± {tolerance}, but found '{actual}'.");
        }
    }

    public static void Contains(string expectedSubstring, string actual)
    {
        if (!actual.Contains(expectedSubstring, StringComparison.Ordinal))
        {
            throw new TestFailureException($"Expected text to contain '{expectedSubstring}'.");
        }
    }

    public static void DoesNotContain(string unexpectedSubstring, string actual)
    {
        if (actual.Contains(unexpectedSubstring, StringComparison.Ordinal))
        {
            throw new TestFailureException(
                $"Expected text not to contain '{unexpectedSubstring}'.");
        }
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new TestFailureException("The sequences are not equal.");
        }
    }

    public static TException Throws<TException>(Action action)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            throw new TestFailureException(
                $"Expected {typeof(TException).Name}, but caught {exception.GetType().Name}.",
                exception);
        }

        throw new TestFailureException(
            $"Expected {typeof(TException).Name}, but no exception was thrown.");
    }

    public static async Task<TException> ThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (TException exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            throw new TestFailureException(
                $"Expected {typeof(TException).Name}, but caught {exception.GetType().Name}.",
                exception);
        }

        throw new TestFailureException(
            $"Expected {typeof(TException).Name}, but no exception was thrown.");
    }

    public static async Task<TException> ThrowsAnyAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        return await ThrowsAsync<TException>(action).ConfigureAwait(false);
    }
}

internal sealed class TestFailureException : Exception
{
    public TestFailureException(string message)
        : base(message)
    {
    }

    public TestFailureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal sealed class InjectedNonFatalException : Exception
{
    public InjectedNonFatalException(string message)
        : base(message)
    {
    }
}

internal sealed class TempDirectory : IAsyncDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "SpinFOURKAYYY.SelfTest",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}

internal sealed record ShortcutFixture(
    string ApplicationDirectory,
    string ApplicationPath,
    string LegendsDirectory,
    string EqGamePath)
{
    public static async Task<ShortcutFixture> CreateAsync(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        string applicationDirectory = Path.Combine(root, "SpinFOURKAYYY");
        Directory.CreateDirectory(applicationDirectory);
        string applicationPath =
            Path.Combine(applicationDirectory, "SpinFOURKAYYY.exe");
        await File.WriteAllTextAsync(applicationPath, "application")
            .ConfigureAwait(false);

        string legendsDirectory = Path.Combine(root, "EverQuest Legends");
        Directory.CreateDirectory(legendsDirectory);
        string eqGamePath = Path.Combine(legendsDirectory, "eqgame.exe");
        await File.WriteAllTextAsync(eqGamePath, "client").ConfigureAwait(false);

        return new ShortcutFixture(
            applicationDirectory,
            applicationPath,
            legendsDirectory,
            eqGamePath);
    }
}

internal sealed record MagpieFixture(
    string MagpieDirectory,
    string GameExecutable,
    string LauncherExecutable)
{
    public static async Task<MagpieFixture> CreateAsync(string root)
    {
        string magpieDirectory = Path.Combine(root, "Magpie");
        string gameDirectory = Path.Combine(root, "Game");
        Directory.CreateDirectory(magpieDirectory);
        Directory.CreateDirectory(gameDirectory);

        string magpie = Path.Combine(magpieDirectory, "Magpie.exe");
        string game = Path.Combine(gameDirectory, "eqgame.exe");
        string launcher = Path.Combine(gameDirectory, "LaunchPad.exe");
        await File.WriteAllBytesAsync(magpie, [0x4D, 0x5A]).ConfigureAwait(false);
        await File.WriteAllBytesAsync(game, [0x4D, 0x5A]).ConfigureAwait(false);
        await File.WriteAllBytesAsync(launcher, [0x4D, 0x5A]).ConfigureAwait(false);
        return new MagpieFixture(magpieDirectory, game, launcher);
    }

    public MagpieProfileRequest CreateRequest(ScalingFilter filter) =>
        new()
        {
            MagpieDirectory = MagpieDirectory,
            SourceExecutablePath = GameExecutable,
            SourceWindowClass = "EverQuestWndClass",
            LauncherPath = LauncherExecutable,
            Filter = filter,
        };
}

internal sealed record FakeClientFixture(
    string EqDirectory,
    string EqGamePath,
    string EqClientIniPath,
    byte[] OriginalIniBytes)
{
    public static async Task<FakeClientFixture> CreateAsync(string root)
    {
        string eqDirectory = Path.Combine(root, "EverQuest Legends");
        Directory.CreateDirectory(eqDirectory);
        string eqGamePath = Path.Combine(eqDirectory, "eqgame.exe");
        string eqClientIniPath = Path.Combine(eqDirectory, "eqclient.ini");
        await File.WriteAllBytesAsync(eqGamePath, [0x4D, 0x5A]).ConfigureAwait(false);

        const string original =
            "; user content must survive\r\n"
            + "[Defaults]\r\n"
            + "AllowResize=1\r\n"
            + "Maximized=1\r\n"
            + "ChatFontSize=3\r\n"
            + "UIScale=0\r\n"
            + "UnrelatedSetting=preserve\r\n"
            + "[VideoMode]\r\n"
            + "Width=3440\r\n"
            + "Height=1440\r\n"
            + "WindowedWidth=3440\r\n"
            + "WindowedHeight=1440\r\n"
            + "Fullscreen=1\r\n";
        byte[] originalBytes = Encoding.UTF8.GetBytes(original);
        await File.WriteAllBytesAsync(eqClientIniPath, originalBytes).ConfigureAwait(false);
        return new FakeClientFixture(
            eqDirectory,
            eqGamePath,
            eqClientIniPath,
            originalBytes);
    }
}

internal sealed class CallbackBackupService : IBackupService
{
    private readonly BackupService _inner = new();

    public Action? AfterRestore { get; set; }

    public Task<BackupManifest> CreateAsync(
        IEnumerable<string> sourcePaths,
        string backupRoot,
        CancellationToken cancellationToken = default)
    {
        return _inner.CreateAsync(sourcePaths, backupRoot, cancellationToken);
    }

    public Task<BackupManifest> LoadManifestAsync(
        string manifestPath,
        CancellationToken cancellationToken = default)
    {
        return _inner.LoadManifestAsync(manifestPath, cancellationToken);
    }

    public Task<BackupVerificationResult> VerifyAsync(
        BackupManifest manifest,
        CancellationToken cancellationToken = default)
    {
        return _inner.VerifyAsync(manifest, cancellationToken);
    }

    public async Task RestoreAsync(
        BackupManifest manifest,
        CancellationToken cancellationToken = default)
    {
        await _inner.RestoreAsync(manifest, cancellationToken).ConfigureAwait(false);
        AfterRestore?.Invoke();
    }
}

internal sealed class ThrowingEqClientConfigurationService : IEqClientConfigurationService
{
    private readonly EqClientConfigurationService _reader = new();

    public Task<EqClientVideoSettings> ReadAsync(
        string eqClientIniPath,
        CancellationToken cancellationToken = default)
    {
        return _reader.ReadAsync(eqClientIniPath, cancellationToken);
    }

    public async Task ApplyWindowedResolutionAsync(
        string eqClientIniPath,
        PixelSize sourceResolution,
        CancellationToken cancellationToken = default)
    {
        _ = sourceResolution;
        await WritePartialAndFailAsync(eqClientIniPath, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<EqClientConfigurationApplyResult> ApplyWindowedResolutionAsync(
        string eqClientIniPath,
        PixelSize sourceResolution,
        int chatFontSizeIncrease,
        CancellationToken cancellationToken = default)
    {
        _ = sourceResolution;
        _ = chatFontSizeIncrease;
        await WritePartialAndFailAsync(eqClientIniPath, cancellationToken)
            .ConfigureAwait(false);
        throw new TestFailureException("The throwing configuration fake unexpectedly returned.");
    }

    private static async Task WritePartialAndFailAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            path,
            "PARTIALLY MUTATED",
            cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException("Injected configuration failure.");
    }
}

internal sealed class FakeDpiCompatibilityService : IDpiCompatibilityService
{
    public int CaptureCalls { get; private set; }

    public int ApplyCalls { get; private set; }

    public int RestoreCalls { get; private set; }

    public Exception? ApplyException { get; set; }

    public DpiCompatibilitySnapshot? LastAppliedIntent { get; private set; }

    public DpiRestoreResult RestoreResult { get; set; } = new(
        WasRestored: true,
        RestoredExactly: true,
        CurrentValueChangedAfterApply: false,
        DpiTokenConflict: false,
        ResultValue: null);

    public DpiCompatibilitySnapshot Capture(string executablePath)
    {
        CaptureCalls++;
        string fullPath = Path.GetFullPath(executablePath);
        return new DpiCompatibilitySnapshot(
            fullPath,
            ValueExisted: false,
            OriginalValue: null,
            Microsoft.Win32.RegistryValueKind.String,
            AppliedValue: "~ HIGHDPIAWARE");
    }

    public DpiCompatibilitySnapshot ApplyApplicationAware(string executablePath)
    {
        return ApplyApplicationAware(Capture(executablePath));
    }

    public DpiCompatibilitySnapshot ApplyApplicationAware(
        DpiCompatibilitySnapshot intendedChange)
    {
        ArgumentNullException.ThrowIfNull(intendedChange);
        ApplyCalls++;
        LastAppliedIntent = intendedChange;
        if (ApplyException is not null)
        {
            throw ApplyException;
        }

        return intendedChange;
    }

    public DpiRestoreResult Restore(DpiCompatibilitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        RestoreCalls++;
        return RestoreResult;
    }
}

internal sealed class FakeProcessDiscoveryService : IProcessDiscoveryService
{
    public IReadOnlyList<ProcessDescriptor> Current { get; set; } = [];

    public bool FilterCurrentByExecutablePath { get; set; }

    public int FindCalls { get; private set; }

    public int FindByNameCalls { get; private set; }

    public int WaitCalls { get; private set; }

    public IReadOnlyList<ProcessDescriptor> FindByExecutableName(
        string executableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);
        FindByNameCalls++;
        return Current.Where(
            process => string.Equals(
                Path.GetFileName(process.ExecutablePath),
                Path.GetFileName(executableName),
                StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    public IReadOnlyList<ProcessDescriptor> FindByExecutablePath(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        FindCalls++;
        return FilterCurrentByExecutablePath
            ? Current.Where(
                process => string.Equals(
                    Path.GetFullPath(process.ExecutablePath),
                    Path.GetFullPath(executablePath),
                    StringComparison.OrdinalIgnoreCase)).ToArray()
            : Current;
    }

    public Task<ProcessDescriptor> WaitForExecutableAsync(
        string executablePath,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        _ = timeout;
        cancellationToken.ThrowIfCancellationRequested();
        WaitCalls++;
        return Current.Count > 0
            ? Task.FromResult(Current[0])
            : Task.FromException<ProcessDescriptor>(
                new TimeoutException("The fake has no running process."));
    }
}

internal sealed class FakeOverlayWindowApi : IOverlayWindowApi
{
    private readonly Dictionary<nint, OverlayWindowSnapshot> _windows = [];
    private Action<OverlayWindowEvent>? _windowEventCallback;

    public FakeOverlayWindowApi(params OverlayWindowSnapshot[] windows)
    {
        foreach (OverlayWindowSnapshot window in windows)
        {
            SetSnapshot(window);
        }
    }

    public List<(nint Handle, bool Topmost)> TopmostRequests { get; } = [];

    public List<(nint Handle, PixelRect Bounds)> MoveRequests { get; } = [];

    public List<(nint Handle, bool InputTransparent)> InputTransparencyRequests
    {
        get;
    } = [];

    public nint ForegroundWindowHandle { get; set; }

    public WindowPlacementPoint? CursorPosition { get; set; }

    public nint WindowAtCursorHandle { get; set; }

    public bool RejectInputTransparencyChanges { get; set; }

    public bool ObserverDisposed { get; private set; }

    public IReadOnlyList<OverlayWindowSnapshot> EnumerateTopLevelWindows() =>
        _windows.Values.ToArray();

    public OverlayWindowSnapshot? Inspect(nint windowHandle) =>
        _windows.TryGetValue(windowHandle, out OverlayWindowSnapshot? snapshot)
            ? snapshot
            : null;

    public nint GetForegroundWindowHandle() => ForegroundWindowHandle;

    public bool TryGetCursorPosition(out WindowPlacementPoint position)
    {
        if (CursorPosition is { } current)
        {
            position = current;
            return true;
        }

        position = default;
        return false;
    }

    public nint WindowFromPoint(WindowPlacementPoint position)
    {
        _ = position;
        return WindowAtCursorHandle;
    }

    public bool SetTopmostWithoutActivation(nint windowHandle, bool topmost)
    {
        if (!_windows.TryGetValue(
            windowHandle,
            out OverlayWindowSnapshot? snapshot))
        {
            return false;
        }

        TopmostRequests.Add((windowHandle, topmost));
        _windows[windowHandle] = snapshot with { IsTopmost = topmost };
        return true;
    }

    public bool SetInputTransparentWithoutActivation(
        nint windowHandle,
        bool inputTransparent)
    {
        if (!_windows.TryGetValue(
                windowHandle,
                out OverlayWindowSnapshot? snapshot))
        {
            return false;
        }

        InputTransparencyRequests.Add((windowHandle, inputTransparent));
        if (RejectInputTransparencyChanges)
        {
            return false;
        }

        _windows[windowHandle] = snapshot with
        {
            IsInputTransparent = inputTransparent,
        };
        return true;
    }

    public bool MoveWithoutResizing(nint windowHandle, PixelRect bounds)
    {
        if (!_windows.TryGetValue(
            windowHandle,
            out OverlayWindowSnapshot? snapshot))
        {
            return false;
        }

        MoveRequests.Add((windowHandle, bounds));
        _windows[windowHandle] = snapshot with { Bounds = bounds };
        return true;
    }

    public IDisposable ObserveWindowOrderChanges(
        Action<OverlayWindowEvent> callback)
    {
        _windowEventCallback = callback;
        ObserverDisposed = false;
        return new CallbackDisposable(() =>
        {
            _windowEventCallback = null;
            ObserverDisposed = true;
        });
    }

    public void RaiseWindowEvent(OverlayWindowEvent windowEvent)
    {
        if (windowEvent.Kind == OverlayWindowEventKind.ForegroundChanged)
        {
            ForegroundWindowHandle = windowEvent.WindowHandle;
        }

        _windowEventCallback?.Invoke(windowEvent);
    }

    public void SetSnapshot(OverlayWindowSnapshot snapshot)
    {
        _windows[snapshot.Handle] = snapshot;
    }

    private sealed class CallbackDisposable(Action callback) : IDisposable
    {
        private Action? _callback = callback;

        public void Dispose()
        {
            Interlocked.Exchange(ref _callback, null)?.Invoke();
        }
    }
}

internal sealed class FakeWindowPlacementService : IWindowPlacementService
{
    private readonly IReadOnlyList<MonitorDescriptor> _monitors;

    private FakeWindowPlacementService(IReadOnlyList<MonitorDescriptor> monitors)
    {
        _monitors = monitors;
        CurrentGeometry = new WindowGeometrySnapshot(
            new PixelRect(20, 20, 1936, 1119),
            new PixelSize(1920, 1080),
            PlacementFlags: 0,
            ShowCommand: 1,
            new WindowPlacementPoint(-1, -1),
            new WindowPlacementPoint(-1, -1),
            new PixelRect(20, 20, 1936, 1119));
    }

    public int PlacementCalls { get; private set; }

    public int CaptureCalls { get; private set; }

    public int RestoreCalls { get; private set; }

    public List<PixelSize> RequestedClientSizes { get; } = [];

    public PixelRect? LastRestoredWindowBounds { get; private set; }

    public PixelSize? LastRestoredClientSize { get; private set; }

    public WindowGeometrySnapshot? LastRestoredGeometry { get; private set; }

    public WindowGeometrySnapshot CurrentGeometry { get; set; }

    public Queue<bool> ExactPlacementResults { get; } = new();

    public bool ExactRestoreResult { get; set; } = true;

    public Exception? RestoreException { get; set; }

    public IReadOnlyList<MonitorDescriptor>? MonitorsOverride { get; set; }

    public Action? AfterPlacement { get; set; }

    public List<string>? Events { get; set; }

    public static FakeWindowPlacementService Create4K() =>
        new(
        [
            new MonitorDescriptor(
                new nint(42),
                new PixelRect(0, 0, 3840, 2160),
                new PixelRect(0, 0, 3840, 2120),
                IsPrimary: true),
        ]);

    public IReadOnlyList<MonitorDescriptor> GetMonitors() =>
        MonitorsOverride ?? _monitors;

    public WindowGeometrySnapshot CaptureWindowGeometry(nint windowHandle)
    {
        _ = windowHandle;
        CaptureCalls++;
        Events?.Add("capture-geometry");
        return CurrentGeometry;
    }

    public WindowPlacementResult CenterFixedClientArea(
        nint windowHandle,
        PixelSize clientSize,
        nint targetMonitor = default)
    {
        _ = windowHandle;
        PlacementCalls++;
        Events?.Add("place");
        RequestedClientSizes.Add(clientSize);
        MonitorDescriptor monitor = targetMonitor == nint.Zero
            ? _monitors[0]
            : _monitors.Single(item => item.Handle == targetMonitor);
        bool isExact = ExactPlacementResults.Count == 0
            || ExactPlacementResults.Dequeue();
        WindowPlacementResult result = new(
            IsExactAndOnScreen: isExact,
            clientSize,
            clientSize,
            new PixelRect(100, 100, clientSize.Width, clientSize.Height),
            monitor,
            Issues: isExact ? [] : ["injected placement failure"]);
        CurrentGeometry = CurrentGeometry with
        {
            WindowBounds = result.WindowBounds!.Value,
            ClientSize = clientSize,
            PlacementFlags = 0,
            ShowCommand = 1,
            NormalWindowBounds = result.WindowBounds.Value,
        };
        AfterPlacement?.Invoke();
        return result;
    }

    public WindowGeometryRestoreResult RestoreExactWindowGeometry(
        nint windowHandle,
        WindowGeometrySnapshot geometry)
    {
        _ = windowHandle;
        RestoreCalls++;
        Events?.Add("restore-geometry");
        LastRestoredGeometry = geometry;
        LastRestoredWindowBounds = geometry.WindowBounds;
        LastRestoredClientSize = geometry.ClientSize;
        if (RestoreException is not null)
        {
            throw RestoreException;
        }

        if (ExactRestoreResult)
        {
            CurrentGeometry = geometry;
        }

        return new WindowGeometryRestoreResult(
            ExactRestoreResult,
            geometry,
            ExactRestoreResult ? geometry : CurrentGeometry,
            ExactRestoreResult ? [] : ["injected exact-geometry restore failure"]);
    }
}

internal sealed class FakeWindowDiscoveryService : IWindowDiscoveryService
{
    public int InvocationCount { get; private set; }

    public bool AllowCalls { get; set; }

    public WindowDescriptor? StableResult { get; set; }

    public bool ForegroundResult { get; set; }

    public int FindVisibleCalls { get; private set; }

    public int StableCalls { get; private set; }

    public List<int> StablePollRequirements { get; } = [];

    public Queue<IReadOnlyList<WindowDescriptor>> VisibleWindowResults { get; } =
        new();

    public Queue<WindowDescriptor> StableResults { get; } = new();

    public IReadOnlyList<WindowDescriptor>? VisibleWindowFallback { get; set; }

    public bool UpdateVisibleFallbackFromStableResult { get; set; }

    public Queue<Exception> StableExceptions { get; } = new();

    public Process? LastProcess { get; private set; }

    public IReadOnlyList<WindowDescriptor> FindVisibleWindows(int processId)
    {
        _ = processId;
        if (AllowCalls)
        {
            InvocationCount++;
            FindVisibleCalls++;
            return VisibleWindowResults.Count > 0
                ? VisibleWindowResults.Dequeue()
                : VisibleWindowFallback
                    ?? throw new TestFailureException(
                        "The window-discovery fake has no visible-window result.");
        }

        return Unexpected<IReadOnlyList<WindowDescriptor>>();
    }

    public WindowDescriptor? FindBestForProcess(int processId)
    {
        _ = processId;
        return Unexpected<WindowDescriptor?>();
    }

    public Task<WindowDescriptor> WaitForVisibleWindowAsync(
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        _ = process;
        _ = timeout;
        _ = cancellationToken;
        return Unexpected<Task<WindowDescriptor>>();
    }

    public Task<WindowDescriptor> WaitForStableVisibleWindowAsync(
        Process process,
        PixelSize expectedClientSize,
        TimeSpan timeout,
        int requiredStablePolls = 3,
        CancellationToken cancellationToken = default)
    {
        _ = process;
        _ = expectedClientSize;
        _ = timeout;
        _ = requiredStablePolls;
        _ = cancellationToken;
        if (AllowCalls)
        {
            InvocationCount++;
            StableCalls++;
            StablePollRequirements.Add(requiredStablePolls);
            LastProcess = process;
            if (StableExceptions.Count > 0)
            {
                return Task.FromException<WindowDescriptor>(
                    StableExceptions.Dequeue());
            }

            WindowDescriptor result =
                StableResults.Count > 0
                    ? StableResults.Dequeue()
                    : StableResult
                        ?? throw new TestFailureException(
                            "The window-discovery fake has no stable result.");
            if (UpdateVisibleFallbackFromStableResult)
            {
                VisibleWindowFallback = [result];
            }

            return Task.FromResult(result);
        }

        return Unexpected<Task<WindowDescriptor>>();
    }

    public Task<WindowDescriptor> WaitForStableVisibleWindowAsync(
        Process process,
        TimeSpan timeout,
        int requiredStablePolls = 3,
        CancellationToken cancellationToken = default)
    {
        _ = process;
        _ = timeout;
        _ = requiredStablePolls;
        _ = cancellationToken;
        if (AllowCalls)
        {
            InvocationCount++;
            StableCalls++;
            StablePollRequirements.Add(requiredStablePolls);
            LastProcess = process;
            if (StableExceptions.Count > 0)
            {
                return Task.FromException<WindowDescriptor>(
                    StableExceptions.Dequeue());
            }

            WindowDescriptor result =
                StableResults.Count > 0
                    ? StableResults.Dequeue()
                    : StableResult
                        ?? throw new TestFailureException(
                            "The window-discovery fake has no stable result.");
            if (UpdateVisibleFallbackFromStableResult)
            {
                VisibleWindowFallback = [result];
            }

            return Task.FromResult(result);
        }

        return Unexpected<Task<WindowDescriptor>>();
    }

    public bool BringToForeground(WindowDescriptor window)
    {
        _ = window;
        if (AllowCalls)
        {
            InvocationCount++;
            return ForegroundResult;
        }

        return Unexpected<bool>();
    }

    private T Unexpected<T>()
    {
        InvocationCount++;
        throw new TestFailureException("Window discovery was unexpectedly invoked.");
    }
}

internal sealed class FakeForegroundWindowService : IForegroundWindowService
{
    public nint ForegroundWindowHandle { get; set; } = new nint(999);

    public Queue<nint> ForegroundWindowResults { get; } = new();

    public Dictionary<nint, WindowRuntimeSnapshot> Snapshots { get; } = new();

    public int ForegroundCalls { get; private set; }

    public int InspectionCalls { get; private set; }

    public nint GetForegroundWindowHandle()
    {
        ForegroundCalls++;
        return ForegroundWindowResults.Count > 0
            ? ForegroundWindowResults.Dequeue()
            : ForegroundWindowHandle;
    }

    public WindowRuntimeSnapshot InspectWindow(nint windowHandle)
    {
        InspectionCalls++;
        return Snapshots.TryGetValue(windowHandle, out WindowRuntimeSnapshot? snapshot)
            ? snapshot
            : new WindowRuntimeSnapshot(
                windowHandle,
                IsValid: true,
                ProcessId: Environment.ProcessId,
                ClassName: "SpinFOURKAYYY",
                ClientBounds: new PixelRect(0, 0, 1200, 800),
                MonitorHandle: new nint(42));
    }
}

internal sealed class FakeMagpiePortableConfigService : IMagpiePortableConfigService
{
    public int WriteCalls { get; private set; }

    public int PrepareCalls { get; private set; }

    public int RollbackCalls { get; private set; }

    public Exception? ApplyException { get; set; }

    public MagpiePortableConfigRollbackDisposition RollbackDisposition { get; set; } =
        MagpiePortableConfigRollbackDisposition.Restored;

    public Queue<MagpiePortableConfigRollbackDisposition> RollbackDispositions { get; } =
        new();

    public List<bool> RollbackAllowChangedValues { get; } = [];

    public List<string>? Events { get; set; }

    public MagpieProfileRequest? LastPreparedRequest { get; private set; }

    public Task<MagpiePortableConfigTransaction> PrepareTransactionAsync(
        MagpieProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        PrepareCalls++;
        LastPreparedRequest = request;
        Events?.Add("prepare");
        return Task.FromResult(
            new MagpiePortableConfigTransaction(
                Path.Combine(request.MagpieDirectory, "config", "config.json"),
                OriginalFileExisted: true,
                OriginalContent: Encoding.UTF8.GetBytes("{\"original\":true}"),
                OriginalAttributes: FileAttributes.Normal,
                OriginalLastWriteTimeUtc: DateTime.UnixEpoch,
                AppliedContent: Encoding.UTF8.GetBytes("{\"autoScale\":1}"),
                ScalingModeIndex: 0,
                ProfileIndex: 1));
    }

    public Task<MagpiePortableConfigResult> ApplyTransactionAsync(
        MagpiePortableConfigTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        cancellationToken.ThrowIfCancellationRequested();
        WriteCalls++;
        Events?.Add("apply");
        if (ApplyException is not null)
        {
            return Task.FromException<MagpiePortableConfigResult>(ApplyException);
        }

        return Task.FromResult(
            new MagpiePortableConfigResult(
                transaction.ConfigPath,
                transaction.ScalingModeIndex,
                transaction.ProfileIndex,
                ContentChanged: true));
    }

    public Task<MagpiePortableConfigRollbackResult> RollbackTransactionAsync(
        MagpiePortableConfigTransaction transaction,
        bool allowChangedContentAfterEngineExit = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        cancellationToken.ThrowIfCancellationRequested();
        RollbackCalls++;
        RollbackAllowChangedValues.Add(allowChangedContentAfterEngineExit);
        Events?.Add("rollback");
        MagpiePortableConfigRollbackDisposition disposition =
            RollbackDispositions.Count > 0
                ? RollbackDispositions.Dequeue()
                : RollbackDisposition;
        return Task.FromResult(
            new MagpiePortableConfigRollbackResult(
                disposition,
                disposition switch
                {
                    MagpiePortableConfigRollbackDisposition.Restored => null,
                    MagpiePortableConfigRollbackDisposition.NewerContentPreserved =>
                        "injected newer content preservation",
                    _ => "injected rollback failure",
                }));
    }

    public Task<MagpiePortableConfigResult> WriteAsync(
        MagpieProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        return WriteCoreAsync(request, cancellationToken);
    }

    private async Task<MagpiePortableConfigResult> WriteCoreAsync(
        MagpieProfileRequest request,
        CancellationToken cancellationToken)
    {
        MagpiePortableConfigTransaction transaction =
            await PrepareTransactionAsync(request, cancellationToken).ConfigureAwait(false);
        return await ApplyTransactionAsync(transaction, cancellationToken)
            .ConfigureAwait(false);
    }
}

internal sealed class RouteHttpMessageHandler(
    IReadOnlyDictionary<string, byte[]> routes) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? requestUri = request.RequestUri?.AbsoluteUri;
        if (requestUri is not null && routes.TryGetValue(requestUri, out byte[]? body))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
                RequestMessage = request,
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("No deterministic test route exists."),
            RequestMessage = request,
        });
    }
}

internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

internal sealed class FakeMagpieProcessService : IMagpieProcessService
{
    public IReadOnlyList<MagpieRunningInstance> Instances { get; set; } = [];

    public Queue<IReadOnlyList<MagpieRunningInstance>> InspectionResults { get; set; } = new();

    public int InspectCalls { get; private set; }

    public int StartCalls { get; private set; }

    public int ShutdownCalls { get; private set; }

    public int ShutdownExactCalls { get; private set; }

    public bool ShutdownResult { get; set; } = true;

    public bool ShutdownExactResult { get; set; } = true;

    public Exception? ShutdownException { get; set; }

    public Exception? ShutdownExactException { get; set; }

    public Exception? StartException { get; set; }

    public Func<MagpieProcessStartResult>? StartResultFactory { get; set; }

    public Process? StartedProcess { get; private set; }

    public List<string>? Events { get; set; }

    public IReadOnlyList<MagpieRunningInstance> InspectRunningInstances(
        string magpieDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(magpieDirectory);
        InspectCalls++;
        Events?.Add("inspect-processes");
        return InspectionResults.Count > 0
            ? InspectionResults.Dequeue()
            : Instances;
    }

    public Process? TryFindRunning(string magpieDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(magpieDirectory);
        return null;
    }

    public MagpieProcessStartResult StartPortable(
        string magpieDirectory,
        bool startInTray = true)
    {
        _ = magpieDirectory;
        _ = startInTray;
        StartCalls++;
        Events?.Add("start");
        if (StartException is not null)
        {
            throw StartException;
        }

        if (StartResultFactory is not null)
        {
            MagpieProcessStartResult result = StartResultFactory();
            StartedProcess = result.Process;
            return result;
        }

        throw new TestFailureException("Magpie process launch was unexpectedly attempted.");
    }

    public Task<bool> ShutdownBundledAsync(
        string magpieDirectory,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(magpieDirectory);
        _ = timeout;
        cancellationToken.ThrowIfCancellationRequested();
        ShutdownCalls++;
        if (ShutdownException is not null)
        {
            return Task.FromException<bool>(ShutdownException);
        }

        return Task.FromResult(ShutdownResult);
    }

    public Task<bool> ShutdownExactAsync(
        string magpieDirectory,
        Process process,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(magpieDirectory);
        ArgumentNullException.ThrowIfNull(process);
        _ = timeout;
        cancellationToken.ThrowIfCancellationRequested();
        ShutdownExactCalls++;
        Events?.Add("shutdown-exact");
        if (ShutdownExactException is not null)
        {
            return Task.FromException<bool>(ShutdownExactException);
        }

        return Task.FromResult(ShutdownExactResult);
    }
}

internal sealed class FakeScalingWindowInspector : IMagpieScalingWindowInspector
{
    public int InvocationCount { get; private set; }

    public int StartCalls { get; private set; }

    public int WaitCalls { get; private set; }

    public int StopCalls { get; private set; }

    public int? LastExpectedScalingProcessId { get; private set; }

    public bool AllowCleanupCalls { get; set; }

    public bool StopResult { get; set; }

    public Exception? StopException { get; set; }

    public Queue<MagpieScalingWindowInspection> InspectionResults { get; set; } = new();

    public MagpieScalingWindowInspection? FallbackInspection { get; set; }

    public MagpieScalingWindowInspection? WaitResult { get; set; }

    public Exception? WaitException { get; set; }

    public List<string>? Events { get; set; }

    public MagpieScalingWindowInspection Inspect(nint expectedSourceWindow = default)
    {
        _ = expectedSourceWindow;
        if (AllowCleanupCalls)
        {
            InvocationCount++;
            Events?.Add("inspect-scaling");
            if (InspectionResults.Count == 0)
            {
                if (FallbackInspection is not null)
                {
                    return FallbackInspection;
                }

                throw new TestFailureException(
                    "The scaling-inspection fake has no queued result.");
            }

            return InspectionResults.Dequeue();
        }

        return Unexpected<MagpieScalingWindowInspection>();
    }

    public Task<MagpieScalingWindowInspection> WaitForSafeAttachmentAsync(
        nint expectedSourceWindow,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        _ = expectedSourceWindow;
        _ = timeout;
        _ = cancellationToken;
        if (AllowCleanupCalls)
        {
            InvocationCount++;
            WaitCalls++;
            Events?.Add("wait-scaling");
            if (WaitException is not null)
            {
                return Task.FromException<MagpieScalingWindowInspection>(
                    WaitException);
            }

            return Task.FromResult(
                WaitResult
                ?? throw new TestFailureException(
                    "The scaling-inspection fake has no wait result."));
        }

        return Unexpected<Task<MagpieScalingWindowInspection>>();
    }

    public Task<bool> StopScalingAsync(
        nint expectedSourceWindow,
        int expectedScalingProcessId,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        _ = expectedSourceWindow;
        _ = timeout;
        _ = cancellationToken;
        if (AllowCleanupCalls)
        {
            InvocationCount++;
            StopCalls++;
            LastExpectedScalingProcessId = expectedScalingProcessId;
            Events?.Add("stop-scaling");
            return StopException is null
                ? Task.FromResult(StopResult)
                : Task.FromException<bool>(StopException);
        }

        return Unexpected<Task<bool>>();
    }

    private T Unexpected<T>()
    {
        InvocationCount++;
        throw new TestFailureException("Scaling inspection was unexpectedly invoked.");
    }
}
