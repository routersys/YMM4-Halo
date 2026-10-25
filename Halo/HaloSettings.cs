using System.Numerics;

namespace Halo;

internal static class HaloSettings
{
    public const int ColorChannels = 3;
    public const int TexelChannels = 4;
    public const int TexelBytes = TexelChannels * sizeof(ushort);
    public const double RadiansPerDegree = Math.PI / 180.0;
    public const double HalfTurnDegrees = 180.0;
    public const double FullTurnDegrees = 360.0;

    public const int RingTableWidth = 1024;
    public const double RingStartDegrees = 10.0;
    public const double RingEndDegrees = 60.0;
    public const double RingOuterTaperDegrees = 5.0;

    public const double ScatterTexelDegrees = 0.3;
    public const int ScatterWidth = 600;
    public const int ScatterHeight = 267;
    public const double ScatterElevationHalfSpanDegrees = 40.0;
    public const double ScatterElevationTaperDegrees = 8.0;
    public const double ScatterMinimumCosine = 0.05;

    public const double WavelengthStartNanometers = 400.0;
    public const double WavelengthEndNanometers = 780.0;
    public const double WavelengthStepNanometers = 20.0;
    public const double ReferenceWavelengthNanometers = 550.0;
    public const double SolarTemperature = 5772.0;

    public const double RingPeakBrightness = 0.35;
    public const double ReferenceSunDiameterDegrees = 0.53;
    public const double RandomCrystalLengthRatio = 1.0;

    public const double SunElevationStepDegrees = 0.25;
    public const double MinimumSunElevationDegrees = 0.0;
    public const double MaximumSunElevationDegrees = 90.0;
    public const double MaximumSunDiameterDegrees = 2.0;
    public const double MaximumSaturation = 2.0;
    public const double MinimumFieldOfViewDegrees = 10.0;
    public const double MaximumFieldOfViewDegrees = 150.0;
    public const double MinimumCameraPitchDegrees = -60.0;
    public const double MaximumCameraPitchDegrees = 90.0;
    public const double MinimumTiltDegrees = 0.1;
    public const double MaximumTiltDegrees = 5.0;
    public const double MinimumPlateThickness = 0.05;
    public const double MaximumPlateThickness = 1.0;
    public const double MinimumColumnLength = 1.0;
    public const double MaximumColumnLength = 6.0;

    public const int RandomInclinationSteps = 450;
    public const int RandomAzimuthSteps = 60;
    public const int EndFaceSubdivisions = 4;
    public const int PlateSpinSteps = 60;
    public const int PlateTiltRadialNodes = 8;
    public const int PlateTiltAngleSteps = 24;
    public const int ColumnSpinSteps = 45;
    public const int ColumnAzimuthSteps = 180;
    public const int ColumnWavelengthStride = 2;
    public const int StripsPerTask = 4;
    public const int MaximumSubSteps = 24;
    public const double SubStepTexels = 1.0;

    public static readonly float MinimumHalfFieldTangent = GetHalfFieldOfViewTangent(MinimumFieldOfViewDegrees);
    public static readonly float MaximumHalfFieldTangent = GetHalfFieldOfViewTangent(MaximumFieldOfViewDegrees);

    public static readonly double[] ColumnTiltQuantiles =
    [
        -1.593218818023, -0.967421566102, -0.58945579785, -0.282216147063, 0.0,
        0.282216147063, 0.58945579785, 0.967421566102, 1.593218818023,
    ];

    public static double GetRingStepDegrees()
        => (RingEndDegrees - RingStartDegrees) / (RingTableWidth - 1);

    public static double GetRingAngleDegrees(int index)
        => RingStartDegrees + index * GetRingStepDegrees();

    public static double GetSunRadiusDegrees(double sunDiameterDegrees)
        => Math.Clamp(sunDiameterDegrees, 0.0, MaximumSunDiameterDegrees) / 2.0;

    public static double GetPlateTiltQuantile(int node)
        => Math.Sqrt(-2.0 * Math.Log(1.0 - (node + 0.5) / PlateTiltRadialNodes));

    public static int GetWavelengthCount()
        => (int)Math.Round((WavelengthEndNanometers - WavelengthStartNanometers) / WavelengthStepNanometers) + 1;

    public static double GetWavelengthNanometers(int index)
        => WavelengthStartNanometers + index * WavelengthStepNanometers;

    public static double QuantizeSunElevation(double sunElevationDegrees)
        => Math.Round(Math.Clamp(sunElevationDegrees, MinimumSunElevationDegrees, MaximumSunElevationDegrees) / SunElevationStepDegrees) * SunElevationStepDegrees;

    public static double GetScatterAzimuthDegrees(int column)
        => (column + 0.5) * ScatterTexelDegrees;

    public static double GetScatterElevationDegrees(int row, double sunElevationDegrees)
        => sunElevationDegrees - ScatterElevationHalfSpanDegrees + (row + 0.5) * ScatterTexelDegrees;

    public static float GetHalfFieldOfViewTangent(double fieldOfViewDegrees)
    {
        var field = Math.Clamp(fieldOfViewDegrees, MinimumFieldOfViewDegrees, MaximumFieldOfViewDegrees);
        return (float)Math.Tan(field * RadiansPerDegree / 2.0);
    }

    public static Vector3 GetSunDirection(double sunElevationDegrees, double directionDegrees, double pitchDegrees)
    {
        var elevation = Math.Clamp(sunElevationDegrees, MinimumSunElevationDegrees, MaximumSunElevationDegrees) * RadiansPerDegree;
        var direction = directionDegrees * RadiansPerDegree;
        var pitch = Math.Clamp(pitchDegrees, MinimumCameraPitchDegrees, MaximumCameraPitchDegrees) * RadiansPerDegree;
        var x = Math.Cos(elevation) * Math.Sin(direction);
        var y = -Math.Sin(elevation);
        var z = Math.Cos(elevation) * Math.Cos(direction);
        var cameraY = y * Math.Cos(pitch) + z * Math.Sin(pitch);
        var cameraZ = -y * Math.Sin(pitch) + z * Math.Cos(pitch);
        return new Vector3((float)x, (float)cameraY, (float)cameraZ);
    }
}
