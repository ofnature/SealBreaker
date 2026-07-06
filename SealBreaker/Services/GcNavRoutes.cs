using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SealBreaker.Services;

/// <summary>Hardcoded GC town walk routes — replayed forward to targets and reversed on return.</summary>
internal static class GcNavRoutes
{
    public const int TownCount = 3;

    private static readonly Vector3[] LimsaUpperDeckFromLanding =
    [
        new(18.770107f, 40.0f, 72.961945f),
        new(30.109232f, 40.130413f, 74.90621f),
        new(41.72349f, 39.99995f, 74.00146f),
        new(54.20084f, 39.99995f, 73.15479f),
        new(66.95751f, 40.0f, 72.64367f),
        new(82.17485f, 40.24671f, 72.67927f),
        new(92.24029f, 40.27537f, 74.23495f),
        new(93.0f, 40.0f, 74.5f),
        new(95.68933f, 40.250282f, 74.54028f),
    ];

    private static readonly Vector3[] LimsaAftcastleToGcCorridor =
    [
        new(-79.5f, 40.0f, -13.0f),
        new(-75.0f, 40.0f, -14.5f),
        new(-71.5f, 40.0f, -16.0f),
        new(-70.0f, 40.0f, -17.0f),
        new(-67.8f, 40.0f, -18.1f),
    ];

    private static readonly Vector3[] LimsaRepairToMender =
    [
        new(0f, 40f, 78f),
        new(1.4003145f, 44.499996f, 143.35721f),
        new(10f, 44f, 158f),
    ];

    public static readonly Vector3 LimsaRepairStairCorner = new(0f, 40f, 78f);
    public static readonly Vector3 LimsaRepairStairLanding = new(1.4003145f, 44.499996f, 143.35721f);
    public static readonly Vector3 LimsaRepairApproach = new(10f, 44f, 158f);
    public static readonly Vector3 LimsaRepairMenderPos = new(10f, 44.5f, 160f);
    public static readonly Vector3 GridaniaRepairPos = new(24f, -8f, 93f);
    public const string GridaniaRepairName = "Erkenbaud";
    public static readonly Vector3 UldahRepairPos = new(-155f, 12f, -24f);
    public const string UldahRepairName = "Hehezofu";

    private static readonly Vector3[] GridaniaEntryWaypoints =
    [
        new(24f, 1f, 25f),
        new(-31f, -3f, 13f),
        new(-59f, -1f, 11f),
        new(-67f, -0.5f, -8f),
    ];

    private static readonly Vector3[] GridaniaRepairWaypoints =
    [
        new(-59f, -1f, 11f),
        new(-17f, -3f, 13f),
        new(39f, -1.6f, 63f),
        new(65f, -1f, 79f),
        new(54.60f, -8f, 106.90f), // door lineup — almost straight walk to the mender from here
        new(37.04f, -8f, 101.15f),
        GridaniaRepairPos,
    ];

    private static readonly Vector3[] UldahEntryWaypoints =
    [
        new(-130f, -2f, -153f),
        new(-94f, 4f, -114f),
        new(-123f, 4f, -88f),
        new(-141f, 4f, -106f),
    ];

    private static readonly Vector3[] UldahRepairToGcWaypoints =
    [
        new(-147f, 12f, -24f),
        new(-123f, 4f, -88f),
        new(-141f, 4f, -106f),
    ];

    private static readonly Vector3[] UldahRepairToMenderWaypoints =
    [
        new(-123f, 4f, -88f),
        UldahRepairPos,
    ];

    private static readonly Vector3[][] BakedGcApproach =
    [
        LimsaUpperDeckFromLanding,
        GridaniaEntryWaypoints,
        UldahEntryWaypoints,
    ];

    private static readonly Vector3[][] BakedGcCorridor =
    [
        LimsaAftcastleToGcCorridor,
        [],
        [],
    ];

    private static readonly Vector3[][] BakedRepair =
    [
        LimsaRepairToMender,
        GridaniaRepairWaypoints,
        UldahRepairToMenderWaypoints,
    ];

    private static readonly Vector3[][] BakedRepairReturn =
    [
        [],
        [],
        UldahRepairToGcWaypoints,
    ];

    public static int BakedGcApproachCount(int gcIdx) => BakedGcApproach[gcIdx].Length;
    public static int BakedGcCorridorCount(int gcIdx) => BakedGcCorridor[gcIdx].Length;
    public static int BakedRepairCount(int gcIdx) => BakedRepair[gcIdx].Length;
    public static int BakedRepairReturnCount(int gcIdx)
    {
        gcIdx = ClampGc(gcIdx);
        if (BakedRepairReturn[gcIdx].Length > 0)
            return BakedRepairReturn[gcIdx].Length;

        return gcIdx == 0 ? BakedRepair[0].Length : 0;
    }

    public static bool HasGcApproachRoute(Configuration cfg, int gcIdx) =>
        GetGcApproachPath(cfg, gcIdx).Length > 0;

    public static bool HasGcCorridorRoute(Configuration cfg, int gcIdx) =>
        GetGcCorridorPath(cfg, gcIdx).Length > 0;

    public static bool HasRepairRoute(Configuration cfg, int gcIdx) =>
        GetRepairPath(cfg, gcIdx).Length > 0;

    public static bool HasRepairReturnRoute(Configuration cfg, int gcIdx) =>
        GetRepairReturnPath(cfg, gcIdx).Length > 0;

    public static Vector3[] GetGcApproachPath(Configuration cfg, int gcIdx)
    {
        gcIdx = ClampGc(gcIdx);
        var town = cfg.TownNav(gcIdx);
        if (town.UseCustomGcNavWaypoints && town.GcNavWaypoints.Count > 0)
            return town.GcNavWaypoints.Select(w => w.ToVector3()).ToArray();

        return BakedGcApproach[gcIdx];
    }

    public static Vector3[] GetGcCorridorPath(Configuration cfg, int gcIdx)
    {
        gcIdx = ClampGc(gcIdx);
        var town = cfg.TownNav(gcIdx);
        if (town.UseCustomGcCorridorWaypoints && town.GcCorridorWaypoints.Count > 0)
            return town.GcCorridorWaypoints.Select(w => w.ToVector3()).ToArray();

        return BakedGcCorridor[gcIdx];
    }

    public static Vector3[] GetRepairPath(Configuration cfg, int gcIdx)
    {
        gcIdx = ClampGc(gcIdx);
        var town = cfg.TownNav(gcIdx);
        if (town.UseCustomRepairNavWaypoints && town.RepairNavWaypoints.Count > 0)
            return town.RepairNavWaypoints.Select(w => w.ToVector3()).ToArray();

        return BakedRepair[gcIdx];
    }

    public static Vector3[] GetRepairReturnPath(Configuration cfg, int gcIdx)
    {
        gcIdx = ClampGc(gcIdx);
        var town = cfg.TownNav(gcIdx);
        if (town.UseCustomRepairReturnNavWaypoints && town.RepairReturnNavWaypoints.Count > 0)
            return town.RepairReturnNavWaypoints.Select(w => w.ToVector3()).ToArray();

        if (BakedRepairReturn[gcIdx].Length > 0)
            return BakedRepairReturn[gcIdx];

        var forward = GetRepairPath(cfg, gcIdx);
        if (gcIdx == 0 && forward.Length < BakedRepair[0].Length)
            return OrderRoute(BakedRepair[0], reverse: true);

        return OrderRoute(forward, reverse: true);
    }

    public static Vector3[] OrderRoute(IReadOnlyList<Vector3> points, bool reverse)
    {
        if (points.Count == 0)
            return [];

        return reverse ? points.Reverse().ToArray() : points.ToArray();
    }

    private static int ClampGc(int gcIdx) => Math.Clamp(gcIdx, 0, TownCount - 1);
}
