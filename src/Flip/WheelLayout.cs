using System.Windows;
using Flip.Core;

namespace Flip;

internal static class WheelLayout
{
    internal const double Size = 420;
    internal const double PrimaryOuterRadius = 207;
    internal const double FanInnerRadius = PrimaryOuterRadius + 10;
    internal const double FanOuterRadius = 282;
    internal const int PrimaryCapacity = 8;
    internal const int MaxFanChildren = 10;
    internal static readonly Point Center = new(Size / 2, Size / 2);

    internal sealed record Slot(string Label, string? Format, string[] Children);

    internal static Slot[] Partition(IReadOnlyList<string> rankedFormats)
    {
        if (rankedFormats.Count <= PrimaryCapacity)
            return rankedFormats.Select(format => new Slot(FormatRegistry.GetWheelShortLabel(format), format, [])).ToArray();

        string extraLabel = ExtraLabel();
        if (rankedFormats.Count > 7 + MaxFanChildren)
        {
            var twoSlotCandidates = rankedFormats.Skip(6).ToArray();
            var splitSlots = rankedFormats.Take(6)
                .Select(format => new Slot(FormatRegistry.GetWheelShortLabel(format), format, []))
                .ToList();

            var grouped = twoSlotCandidates
                .Select(format => (Format: format, Label: SemanticGroupLabel(format)))
                .GroupBy(item => item.Label, StringComparer.Ordinal)
                .OrderByDescending(g => g.Key != extraLabel ? g.Count() : -1)
                .ToArray();

            if (grouped.Length >= 2 && grouped[0].Key != extraLabel)
            {
                string firstLabel = grouped[0].Key;
                string[] firstChildren = grouped[0].Select(x => x.Format).ToArray();
                string[] secondChildren = twoSlotCandidates.Where(f => SemanticGroupLabel(f) != firstLabel).ToArray();
                string[] secondDistinct = secondChildren.Select(SemanticGroupLabel).Distinct(StringComparer.Ordinal).ToArray();
                string secondLabel = secondDistinct.Length == 1 && secondDistinct[0] != firstLabel
                    ? secondDistinct[0]
                    : extraLabel;

                splitSlots.Add(new Slot(firstLabel, null, firstChildren));
                splitSlots.Add(new Slot(secondLabel, null, secondChildren));
                return splitSlots.ToArray();
            }

            int half = (twoSlotCandidates.Length + 1) / 2;
            string[] part1 = twoSlotCandidates.Take(half).ToArray();
            string[] part2 = twoSlotCandidates.Skip(half).ToArray();
            string[] labels1 = part1.Select(SemanticGroupLabel).Distinct(StringComparer.Ordinal).ToArray();
            string label1 = labels1.Length == 1 ? labels1[0] : extraLabel;
            string label2 = label1 == extraLabel ? Loc.T("+ FORMATI", "+ FORMATS") : extraLabel;
            splitSlots.Add(new Slot(label1, null, part1));
            splitSlots.Add(new Slot(label2, null, part2));
            return splitSlots.ToArray();
        }

        const int directCount = PrimaryCapacity - 1;
        var slots = rankedFormats.Take(directCount)
            .Select(format => new Slot(FormatRegistry.GetWheelShortLabel(format), format, []))
            .ToList();

        var remaining = rankedFormats.Skip(directCount).ToArray();
        string[] distinctGroupLabels = remaining.Select(SemanticGroupLabel).Distinct(StringComparer.Ordinal).ToArray();
        string singleLabel = distinctGroupLabels.Length == 1 ? distinctGroupLabels[0] : extraLabel;
        slots.Add(new Slot(singleLabel, null, remaining));
        return slots.ToArray();
    }

    private static string ExtraLabel() => Loc.T("+ EXTRA", "+ MORE");

    private static string SemanticGroupLabel(string format) =>
        FormatRegistry.Find(format)?.Category switch
        {
            FormatCategory.RasterAndRaw or FormatCategory.Vector => Loc.T("+ IMMAGINI", "+ IMAGES"),
            FormatCategory.DocumentAndEmail or FormatCategory.Hwp or FormatCategory.TextAndMarkup => "+ DOC",
            FormatCategory.Spreadsheet or FormatCategory.Presentation => "+ OFFICE",
            FormatCategory.Pdf => "+ PDF",
            FormatCategory.EbookAndComic => "+ EBOOK",
            FormatCategory.Audio => "+ AUDIO",
            FormatCategory.Video => "+ VIDEO",
            FormatCategory.DataAndConfig => Loc.T("+ DATI", "+ DATA"),
            FormatCategory.ArchiveAndDisk => Loc.T("+ ARCHIVI", "+ ARCHIVES"),
            FormatCategory.Cad or FormatCategory.Model3D or FormatCategory.Geospatial => "+ 3D/CAD",
            FormatCategory.Subtitle => Loc.T("+ SOTTOT.", "+ SUBS"),
            _ => ExtraLabel()
        };

    internal static Point At(double radius, double angle)
    {
        double radians = angle * Math.PI / 180;
        return new(Center.X + radius * Math.Sin(radians), Center.Y - radius * Math.Cos(radians));
    }

    internal static double Radius(Point point) => (point - Center).Length;

    internal sealed record Fan(int Count, double Start, double Sweep, double OuterRadius)
    {
        internal double Step => Sweep / Count;
        internal double Gap => Math.Min(1.1, Step * .09);
        internal double Angle(int index) => Start + (index + .5) * Step;

        internal int Hit(Point point)
        {
            double radius = Radius(point);
            if (radius < FanInnerRadius || radius > OuterRadius) return -1;
            var delta = point - Center;
            double angle = Math.Atan2(delta.X, -delta.Y) * 180 / Math.PI;
            double offset = ((angle - Start) % 360 + 360) % 360;
            if (offset >= Sweep) return -1;
            int index = (int)(offset / Step);
            double within = offset - index * Step;
            return within >= Gap && within <= Step - Gap ? index : -1;
        }
    }

    internal static Fan? FitFan(int parentIndex, int primaryCount, int childCount, Rect workArea)
    {
        double parentAngle = parentIndex * 360.0 / primaryCount;
        double preferredSweep = Math.Min(156, childCount * 24);
        // Keep space for the stroke. Near an edge, turn the fan toward free space before narrowing it.
        workArea.Inflate(-3, -3);
        for (double radius = FanOuterRadius; radius >= 246; radius -= 12)
        {
            if (FindAtRadius(radius, preferredSweep) is { } fan) return fan;
        }

        for (double sweep = preferredSweep - 6; sweep >= 12; sweep -= 6)
        {
            if (FindAtRadius(246, sweep) is { } fan) return fan;
        }
        return null;

        Fan? FindAtRadius(double radius, double sweep)
        {
            for (int shift = 0; shift <= 180; shift++)
            {
                if (TryFit(parentAngle + shift, radius, sweep) is { } clockwise) return clockwise;
                if (shift > 0 && TryFit(parentAngle - shift, radius, sweep) is { } counterclockwise) return counterclockwise;
            }
            return null;
        }

        Fan? TryFit(double angle, double radius, double sweep)
        {
            double start = angle - sweep / 2, end = start + sweep;
            if (!workArea.Contains(At(radius, start)) || !workArea.Contains(At(radius, end)) ||
                !workArea.Contains(At(FanInnerRadius, start)) || !workArea.Contains(At(FanInnerRadius, end))) return null;

            // Axis extrema can fall between the endpoints of an arc.
            for (double cardinal = Math.Ceiling(start / 90) * 90; cardinal < end; cardinal += 90)
            {
                if (!workArea.Contains(At(radius, cardinal)) || !workArea.Contains(At(FanInnerRadius, cardinal))) return null;
            }
            return new(childCount, start, sweep, radius);
        }
    }
}
