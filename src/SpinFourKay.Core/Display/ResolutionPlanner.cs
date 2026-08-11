using System.Collections.ObjectModel;

namespace SpinFourKay.Core.Display;

/// <summary>
/// Produces lower-resolution game surfaces that Magpie can scale to the monitor.
/// </summary>
public sealed class ResolutionPlanner
{
    private const double MinimumScale = 1.0;
    private const double MaximumScale = 4.0;
    // EverQuest's stock character-select canvas is 800x600. Allowing the
    // rendered client below that size can clip or misplace controls such as
    // Enter World before a character UI layout has even loaded.
    public const int MinimumInteractiveWidth = 800;
    public const int MinimumInteractiveHeight = 600;

    private static readonly ReadOnlyCollection<ResolutionPreset> KnownPresets =
        new List<ResolutionPreset>
        {
            new(
                ResolutionPresetKind.Balanced,
                "Balanced",
                "Renders at roughly two-thirds of the display size for a 1.5x larger UI.",
                1.5,
                ScalingFilter.Nis),
            new(
                ResolutionPresetKind.Comfort,
                "Comfort",
                "Renders at half of the display size for a 2x larger UI.",
                2.0,
                ScalingFilter.Nis),
            new(
                ResolutionPresetKind.PixelCrisp,
                "Pixel Crisp",
                "Uses exact 2x nearest-neighbor scaling when the display dimensions permit it.",
                2.0,
                ScalingFilter.NearestNeighbor),
        }.AsReadOnly();

    private readonly IReadOnlyList<ResolutionPreset> _presets = KnownPresets;
    private readonly int _minimumWidth = MinimumInteractiveWidth;
    private readonly int _minimumHeight = MinimumInteractiveHeight;

    public IReadOnlyList<ResolutionPreset> Presets => _presets;

    public ResolutionPlan CreatePlan(PixelSize targetResolution, ResolutionPresetKind presetKind)
    {
        if (presetKind == ResolutionPresetKind.Custom)
        {
            throw new ArgumentException(
                "Use CreateCustomPlan when selecting the custom preset.",
                nameof(presetKind));
        }

        ResolutionPreset preset = Presets.FirstOrDefault(item => item.Kind == presetKind)
            ?? throw new ArgumentOutOfRangeException(
                nameof(presetKind),
                presetKind,
                "The resolution preset is not supported.");

        return Create(
            targetResolution,
            preset.DesiredUiScale,
            preset.Filter,
            preset.Kind);
    }

    public ResolutionPlan CreateCustomPlan(
        PixelSize targetResolution,
        double desiredUiScale,
        ScalingFilter filter)
    {
        return Create(
            targetResolution,
            desiredUiScale,
            filter,
            ResolutionPresetKind.Custom);
    }

    private ResolutionPlan Create(
        PixelSize target,
        double desiredUiScale,
        ScalingFilter filter,
        ResolutionPresetKind presetKind)
    {
        if (!double.IsFinite(desiredUiScale)
            || desiredUiScale < MinimumScale
            || desiredUiScale > MaximumScale)
        {
            throw new ArgumentOutOfRangeException(
                nameof(desiredUiScale),
                desiredUiScale,
                $"UI scale must be between {MinimumScale:0.#} and {MaximumScale:0.#}.");
        }

        int minimumWidth = Math.Min(_minimumWidth, target.Width);
        int minimumHeight = Math.Min(_minimumHeight, target.Height);
        double maximumSafeScale = Math.Min(
            (double)target.Width / minimumWidth,
            (double)target.Height / minimumHeight);
        double effectiveUiScale = Math.Min(desiredUiScale, maximumSafeScale);

        int width = RoundToEven(target.Width / effectiveUiScale);
        int height = RoundToEven(target.Height / effectiveUiScale);

        width = Math.Clamp(width, minimumWidth, target.Width);
        height = Math.Clamp(height, minimumHeight, target.Height);

        // Nearest-neighbor is useful only when every source pixel maps to an exact block.
        if (filter == ScalingFilter.NearestNeighbor)
        {
            bool exactIntegerSourceFound = false;
            int requestedIntegerScale = Math.Clamp((int)Math.Round(desiredUiScale), 1, 4);
            for (int integerScale = requestedIntegerScale; integerScale >= 2; integerScale--)
            {
                int exactWidth = target.Width / integerScale;
                int exactHeight = target.Height / integerScale;
                if (target.Width % integerScale == 0
                    && target.Height % integerScale == 0
                    && exactWidth >= minimumWidth
                    && exactHeight >= minimumHeight)
                {
                    width = exactWidth;
                    height = exactHeight;
                    exactIntegerSourceFound = true;
                    break;
                }
            }

            // Without an enlarging integer divisor, nearest-neighbor would
            // silently collapse to a 1:1 pass instead of the requested scale.
            if (!exactIntegerSourceFound)
            {
                filter = ScalingFilter.Nis;
            }
        }

        PixelSize source = new(width, height);
        PixelRect destination = CalculateFittedDestination(source, target);
        double actualScale = Math.Min(
            (double)destination.Width / source.Width,
            (double)destination.Height / source.Height);

        return new ResolutionPlan(
            target,
            source,
            destination,
            actualScale,
            filter,
            presetKind);
    }

    private static int RoundToEven(double value)
    {
        int rounded = checked((int)Math.Round(value, MidpointRounding.AwayFromZero));
        return rounded % 2 == 0 ? rounded : rounded + 1;
    }

    private static PixelRect CalculateFittedDestination(PixelSize source, PixelSize target)
    {
        double scale = Math.Min(
            (double)target.Width / source.Width,
            (double)target.Height / source.Height);

        int width = Math.Min(target.Width, (int)Math.Floor(source.Width * scale));
        int height = Math.Min(target.Height, (int)Math.Floor(source.Height * scale));
        int x = (target.Width - width) / 2;
        int y = (target.Height - height) / 2;

        return new PixelRect(x, y, width, height);
    }
}
