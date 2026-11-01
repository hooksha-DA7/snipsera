using System;
using System.Collections.Generic;

namespace Framelet;

// Match visible vertical detail rather than letting white margins or a small animated widget dominate.
internal static class ScrollAlignment
{
    internal static bool IsSameView(ScrollFrame previous, ScrollFrame next)
    {
        int count = 0, matched = 0, bands = 0;
        foreach (var point in Points(previous))
        {
            int index = (point.Y * previous.Width + point.X) * 4; count++;
            if (Difference(previous.Pixels, index, next.Pixels, index) <= 8)
            { matched++; bands |= 1 << Math.Min(7, point.Y * 8 / previous.Height); }
        }
        int covered = 0; for (int bit = 0; bit < 8; bit++) if ((bands & (1 << bit)) != 0) covered++;
        return count >= 24 && covered >= 3 && matched >= count * .88;
    }
    internal static double? Find(ScrollFrame previous, ScrollFrame next)
    {
        var points = Points(previous);
        int width = previous.Width, height = previous.Height;
        if (points.Count < 24) return null;
        int stride = Math.Max(1, (points.Count + 1535) / 1536);
        int limit = height - Math.Max(48, height / 8);
        var coarse = new double[2 * limit + 1]; Array.Fill(coarse, double.PositiveInfinity);
        for (int shift = -limit; shift <= limit; shift++)
        {
            var score = Score(shift, 0); if (score.Count >= 24) coarse[shift + limit] = score.Cost;
        }
        // Refine several independent peaks at subpixel offsets. Chrome can re-rasterize text between whole physical pixels.
        var costs = new double[coarse.Length]; Array.Fill(costs, double.PositiveInfinity);
        var fractions = new double[coarse.Length];
        var seeds = new List<int>();
        for (int peak = 0; peak < 8; peak++)
        {
            int seed = 0; double lowest = double.PositiveInfinity;
            for (int shift = -limit; shift <= limit; shift++)
            {
                // Compare independent local minima, rather than neighbours in the same broad gradient basin.
                if (shift > -limit && coarse[shift + limit] > coarse[shift + limit - 1]) continue;
                if (shift < limit && coarse[shift + limit] > coarse[shift + limit + 1]) continue;
                bool nearby = false; foreach (int old in seeds) if (Math.Abs(shift - old) <= 2) { nearby = true; break; }
                if (!nearby && coarse[shift + limit] < lowest) { lowest = coarse[shift + limit]; seed = shift; }
            }
            if (double.IsPositiveInfinity(lowest)) break;
            seeds.Add(seed);
            for (int shift = Math.Max(-limit, seed - 1); shift <= Math.Min(limit, seed + 1); shift++)
                for (int fraction = -4; fraction <= 4; fraction++)
                {
                    var score = Score(shift, fraction / 8d);
                    if (score.Count >= 24 && score.Matched >= score.Count * .72 && score.Cost < costs[shift + limit])
                    { costs[shift + limit] = score.Cost; fractions[shift + limit] = fraction / 8d; }
                }
        }
        int best = 0; double bestCost = double.PositiveInfinity;
        for (int shift = -limit; shift <= limit; shift++)
            if (costs[shift + limit] < bestCost) { bestCost = costs[shift + limit]; best = shift; }
        double alternative = double.PositiveInfinity;
        for (int shift = -limit; shift <= limit; shift++)
            if (Math.Abs(shift - best) > 2) alternative = Math.Min(alternative, costs[shift + limit]);
        // Ambiguous repeated lines cannot establish a safe seam, even when one score is marginally better.
        if (bestCost > 12 || alternative - bestCost < Math.Max(.35, bestCost * .2)) return null;
        return best == 0 ? IsSameView(previous, next) ? 0 : null : best + fractions[best + limit];

        (double Cost, int Count, int Matched) Score(int shift, double fraction)
        {
            int count = 0, matched = 0; double sum = 0;
            for (int p = 0; p < points.Count; p += stride)
            {
                var point = points[p]; int nextY = point.Y - shift;
                if (nextY < 1 || nextY >= height - 1) continue;
                int a = (point.Y * width + point.X) * 4, b = (nextY * width + point.X) * 4;
                double error = SmoothedDifference(previous.Pixels, a, next.Pixels, b, width * 4, fraction);
                sum += Math.Min(48, error); count++; if (error <= 12) matched++;
            }
            return (count == 0 ? double.PositiveInfinity : sum / count, count, matched);
        }
    }
    private static List<(int X, int Y)> Points(ScrollFrame frame)
    {
        var result = new List<(int X, int Y)>();
        for (int pass = 0; pass < 2; pass++)
        {
            int row = frame.Width * 4 * (pass == 0 ? 1 : 2);
            double contrast = pass == 0 ? 12 : 2;
            for (int y = 2; y < frame.Height - 2; y += Math.Max(1, frame.Height / 192))
                for (int x = 2; x < frame.Width - 2; x += Math.Max(1, frame.Width / 96))
                {
                    int p = (y * frame.Width + x) * 4;
                    if (Difference(frame.Pixels, p - row, frame.Pixels, p + row) >= contrast) result.Add((x, y));
                }
            if (result.Count >= 24) break;
            result.Clear();
        }
        return result;
    }
    private static double Difference(byte[] a, int ai, byte[] b, int bi) =>
        (Math.Abs(a[ai] - b[bi]) + Math.Abs(a[ai + 1] - b[bi + 1]) + Math.Abs(a[ai + 2] - b[bi + 2])) / 3d;
    private static double SmoothedDifference(byte[] a, int ai, byte[] b, int bi, int row, double fraction)
    {
        double error = 0;
        for (int channel = 0; channel < 3; channel++)
        {
            int p = ai + channel, q = bi + channel;
            double sample = a[p - row] + 2 * a[p] + a[p + row];
            if (fraction != 0)
            {
                int adjacent = p + (fraction > 0 ? row : -row);
                sample = sample * (1 - Math.Abs(fraction)) + (a[adjacent - row] + 2 * a[adjacent] + a[adjacent + row]) * Math.Abs(fraction);
            }
            error += Math.Abs(sample - b[q - row] - 2 * b[q] - b[q + row]);
        }
        // Matching only: retain original unfiltered pixels in the stitched output.
        return error / 12d;
    }
}
