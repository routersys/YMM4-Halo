using System.Numerics;

namespace Halo;

internal static class HaloSettings
{
    public const int ColorChannels = 3;
    public const int TexelChannels = 4;
    public const int TexelBytes = TexelChannels * sizeof(ushort);
    public const double RadiansPerDegree = Math.PI / 180.0;

    public const int RingTableWidth = 1024;
    public const double RingStartDegrees = 10.0;
    public const double RingEndDegrees = 60.0;
    public const double RingOuterTaperDegrees = 5.0;

    public const double ScatterTexelDegrees = 0.3;
    public const int ScatterWidth = 600;
    public const int ScatterHeight = 267;
    public const double ScatterElevationHalfSpanDegrees = 40.0;
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
    public const double MaximumPlateThickness = 0.5;
    public const double MinimumColumnLength = 1.0;
    public const double MaximumColumnLength = 6.0;

    public const int RandomInclinationSteps = 900;
    public const int RandomAzimuthSteps = 120;
    public const int EndFaceSubdivisions = 4;
    public const int PlateSpinSteps = 120;
    public const int PlateTiltRadialNodes = 6;
    public const int PlateTiltAngleNodes = 12;
    public const int ColumnSpinSteps = 90;
    public const int ColumnAzimuthSteps = 360;
    public const double SegmentSubStepTexels = 0.5;

    public static readonly float MinimumHalfFieldTangent = GetHalfFieldOfViewTangent(MinimumFieldOfViewDegrees);
    public static readonly float MaximumHalfFieldTangent = GetHalfFieldOfViewTangent(MaximumFieldOfViewDegrees);

    public static readonly double[] ColumnTiltQuantiles = [-1.2815515655446004, -0.5244005101, 0.0, 0.5244005101, 1.2815515655446004];

    public static double GetRingStepDegrees()
        => (RingEndDegrees - RingStartDegrees) / (RingTableWidth - 1);

    public static double GetRingAngleDegrees(int index)
        => RingStartDegrees + index * GetRingStepDegrees();

    public static double GetSunRadiusDegrees(double sunDiameterDegrees)
        => Math.Clamp(sunDiameterDegrees, 0.0, MaximumSunDiameterDegrees) / 2.0;

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
