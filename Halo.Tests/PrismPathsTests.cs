namespace Halo.Tests;

public sealed class PrismPathsTests
{
    const double Green = 550.0;
    const double Degrees = 180.0 / Math.PI;

    static double Index => IceOptics.RefractiveIndex(Green);

    static Vec3 Horizontal(double degrees)
        => new(-Math.Cos(degrees / Degrees), -Math.Sin(degrees / Degrees), 0.0);

    static double Deviation(Vec3 incoming, Vec3 exit)
        => Math.Acos(Math.Clamp(exit.Dot(incoming), -1.0, 1.0)) * Degrees;

    static double MinimumDeviation(double apexDegrees, double index)
        => 2.0 * Math.Asin(index * Math.Sin(apexDegrees / 2.0 / Degrees)) * Degrees - apexDegrees;

    static PrismPath[] Enumerate(Vec3 incoming, double length, PathKinds kinds)
    {
        var buffer = new PrismPath[PrismGeometry.MaximumPathCount];
        var count = PrismGeometry.Enumerate(incoming, length, Index, kinds, buffer);
        return buffer[..count];
    }

    [Theory]
    [InlineData(0.9)]
    [InlineData(0.5)]
    [InlineData(0.1)]
    public void RefractionObeysSnellsLawInThePlaneOfIncidence(double cosine)
    {
        var normal = new Vec3(0.0, 0.0, 1.0);
        var sine = Math.Sqrt(1.0 - cosine * cosine);
        var incoming = new Vec3(sine, 0.0, -cosine);

        var inner = PrismGeometry.Refract(incoming, normal, Index);

        Assert.Equal(1.0, inner.Length, 1e-12);
        Assert.Equal(sine, Index * (inner.X), 1e-12);
        Assert.Equal(0.0, inner.Y, 1e-12);
        Assert.True(inner.Z < 0.0);
    }

    [Fact]
    public void ARayAlongTheNormalIsNotBent()
    {
        var inner = PrismGeometry.Refract(new Vec3(0.0, 0.0, -1.0), new Vec3(0.0, 0.0, 1.0), Index);

        Assert.Equal(0.0, inner.X, 1e-12);
        Assert.Equal(-1.0, inner.Z, 1e-12);
    }

    [Fact]
    public void TheSideFacesAreSixUnitNormalsSpacedSixtyDegreesApart()
    {
        for (var face = 0; face < PrismGeometry.SideFaceCount; face++)
        {
            var normal = PrismGeometry.Normal(face);
            var next = PrismGeometry.Normal((face + 1) % PrismGeometry.SideFaceCount);

            Assert.Equal(1.0, normal.Length, 1e-12);
            Assert.Equal(0.0, normal.Z, 1e-12);
            Assert.Equal(0.5, normal.Dot(next), 1e-12);
        }

        Assert.Equal(new Vec3(0.0, 0.0, 1.0), PrismGeometry.Normal(PrismGeometry.UpperEnd));
        Assert.Equal(new Vec3(0.0, 0.0, -1.0), PrismGeometry.Normal(PrismGeometry.LowerEnd));
        Assert.Equal(Math.Sqrt(3.0) / 2.0, PrismGeometry.Apothem, 1e-12);
        Assert.Equal(1.5 * Math.Sqrt(3.0), PrismGeometry.EndFaceArea, 1e-12);
    }

    [Fact]
    public void TheMinimumDeviationOfTheSixtyDegreePrismMatchesTheClosedForm()
    {
        var smallest = double.MaxValue;
        for (var angle = 0.0; angle < 360.0; angle += 0.01)
        {
            var incoming = Horizontal(angle);
            foreach (var path in Enumerate(incoming, 10.0, PathKinds.SideToSide))
            {
                if (PrismGeometry.TryRefract(incoming, path, Index, out var exit, out _))
                    smallest = Math.Min(smallest, Deviation(incoming, exit));
            }
        }

        Assert.Equal(MinimumDeviation(60.0, Index), smallest, 2e-3);
        Assert.Equal(21.9, smallest, 0.1);
    }

    [Fact]
    public void TheMinimumDeviationOfTheNinetyDegreePrismMatchesTheClosedFormForBothEntryFaces()
    {
        var fromSide = double.MaxValue;
        var fromEnd = double.MaxValue;
        for (var angle = 1.0; angle < 89.0; angle += 0.01)
        {
            var side = PrismGeometry.Normal(0);
            var downward = new Vec3(-Math.Cos(angle / Degrees) * side.X, -Math.Cos(angle / Degrees) * side.Y, -Math.Sin(angle / Degrees));
            foreach (var path in Enumerate(downward, 0.2, PathKinds.SideToEnd))
            {
                if (PrismGeometry.TryRefract(downward, path, Index, out var exit, out _))
                    fromSide = Math.Min(fromSide, Deviation(downward, exit));
            }

            var toSide = new Vec3(Math.Sin(angle / Degrees) * side.X, Math.Sin(angle / Degrees) * side.Y, -Math.Cos(angle / Degrees));
            foreach (var path in Enumerate(toSide, 3.0, PathKinds.EndToSide))
            {
                if (PrismGeometry.TryRefract(toSide, path, Index, out var exit, out _))
                    fromEnd = Math.Min(fromEnd, Deviation(toSide, exit));
            }
        }

        Assert.Equal(MinimumDeviation(90.0, Index), fromSide, 5e-3);
        Assert.Equal(MinimumDeviation(90.0, Index), fromEnd, 5e-3);
        Assert.Equal(46.0, fromSide, 0.1);
    }

    [Fact]
    public void ARayNeverLeavesThroughTheFaceParallelToItsEntryFace()
    {
        var random = new Random(3);
        for (var trial = 0; trial < 500; trial++)
        {
            var incoming = new Vec3(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5).Normalized();
            foreach (var path in Enumerate(incoming, 1.0 + 3.0 * random.NextDouble(), PathKinds.SideToSide | PathKinds.SideToEnd | PathKinds.EndToSide))
            {
                if (path.Entry < PrismGeometry.SideFaceCount)
                    Assert.NotEqual((path.Entry + 3) % PrismGeometry.SideFaceCount, path.Exit);
                Assert.False(path.Entry >= PrismGeometry.SideFaceCount && path.Exit >= PrismGeometry.SideFaceCount);
            }
        }
    }

    [Fact]
    public void EveryPathHasADistinctLabelWithinTheLabelRange()
    {
        var random = new Random(5);
        for (var trial = 0; trial < 300; trial++)
        {
            var incoming = new Vec3(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5).Normalized();
            var paths = Enumerate(incoming, 0.3 + 2.0 * random.NextDouble(), PathKinds.SideToSide | PathKinds.SideToEnd | PathKinds.EndToSide | PathKinds.Reflection);

            Assert.Equal(paths.Length, paths.Select(path => path.Label).Distinct().Count());
            Assert.All(paths, path => Assert.InRange(path.Label, 0, PrismGeometry.LabelCount - 1));
            Assert.All(paths, path => Assert.True(path.Geometry > 0.0));
        }
    }

    [Fact]
    public void OnlyFacesTurnedTowardTheSunReceiveLight()
    {
        var incoming = new Vec3(-1.0, 0.0, 0.0);

        var entries = Enumerate(incoming, 1.0, PathKinds.SideToSide | PathKinds.SideToEnd | PathKinds.Reflection).Select(path => path.Entry).Distinct().Order().ToArray();

        Assert.All(entries, entry => Assert.True(-incoming.Dot(PrismGeometry.Normal(entry)) > 0.0));
        Assert.Equal(2, entries.Length);
    }

    [Fact]
    public void ALevelRayLeavesNoLightForTheEndFaces()
    {
        var incoming = Horizontal(10.0);

        var paths = Enumerate(incoming, 0.5, PathKinds.SideToEnd);

        Assert.Empty(paths);
    }

    [Fact]
    public void ARayThroughAVeryThinPlateLeavesThroughTheEndFaceWithTheWholeEntryArea()
    {
        var incoming = new Vec3(-Math.Cos(0.5), -0.0, -Math.Sin(0.5));
        const double Length = 1e-6;

        var paths = Enumerate(incoming, Length, PathKinds.SideToSide | PathKinds.SideToEnd);

        var cosine = -incoming.Dot(PrismGeometry.Normal(0));
        var end = paths.Single(path => path.Entry == 0);
        Assert.Equal(PrismGeometry.LowerEnd, end.Exit);
        Assert.Equal(cosine * Length, end.Geometry, 1e-12);
    }

    [Fact]
    public void ALongCrystalLetsAllLightEnteringAnEndFaceLeaveThroughTheSides()
    {
        var incoming = new Vec3(-0.4, 0.1, -0.9).Normalized();

        var paths = Enumerate(incoming, 1000.0, PathKinds.EndToSide);

        var cosine = -incoming.Dot(PrismGeometry.Normal(PrismGeometry.UpperEnd));
        Assert.Equal(cosine * PrismGeometry.EndFaceArea, paths.Sum(path => path.Geometry), 1e-9);
        Assert.All(paths, path => Assert.Equal(PrismGeometry.UpperEnd, path.Entry));
    }

    [Fact]
    public void ALongCrystalLetsAllLightEnteringASideFaceReachASideFaceOrTheOppositeOne()
    {
        var incoming = Horizontal(7.0);
        const double Length = 5.0;

        var cosine = -incoming.Dot(PrismGeometry.Normal(0));
        var paths = Enumerate(incoming, Length, PathKinds.SideToSide).Where(path => path.Entry == 0).ToArray();

        Assert.True(paths.Sum(path => path.Geometry) <= cosine * Length + 1e-12);
        Assert.True(paths.Sum(path => path.Geometry) > 0.0);
    }

    [Fact]
    public void AnObliqueExitFaceTotallyReflectsARayThatEntersAlongTheNormal()
    {
        var incoming = -PrismGeometry.Normal(0);

        var refracted = PrismGeometry.TryRefract(incoming, new PrismPath(0, 2, 1.0), Index, out _, out var transmittance);

        Assert.False(refracted);
        Assert.Equal(0.0, transmittance);
    }

    [Fact]
    public void ARayThroughASixtyDegreePrismLosesLightToTheTwoSurfaces()
    {
        var incoming = Horizontal(0.0);
        var path = Enumerate(incoming, 10.0, PathKinds.SideToSide).First(candidate => PrismGeometry.TryRefract(incoming, candidate, Index, out _, out _));

        PrismGeometry.TryRefract(incoming, path, Index, out var exit, out var transmittance);

        Assert.Equal(1.0, exit.Length, 1e-12);
        Assert.InRange(transmittance, 0.7, 0.99);
        Assert.True(path.Geometry > 0.0);
    }

    [Fact]
    public void ReflectionFromASideFaceFollowsTheMirrorLawWithTheFresnelWeight()
    {
        var incoming = new Vec3(-0.8, -0.3, -0.2).Normalized();
        var path = Enumerate(incoming, 2.0, PathKinds.Reflection).First();

        var reflected = PrismGeometry.TryReflect(incoming, path, Index, out var exit, out var reflectance);

        var normal = PrismGeometry.Normal(path.Entry);
        var cosine = -incoming.Dot(normal);
        Assert.True(reflected);
        Assert.Equal(1.0, exit.Length, 1e-12);
        Assert.Equal(cosine, exit.Dot(normal), 1e-12);
        Assert.Equal(IceOptics.Reflectance(cosine, 1.0, Index), reflectance, 1e-12);
        Assert.Equal(2.0 * cosine, path.Geometry, 1e-12);
        Assert.Equal(PrismGeometry.ReflectionExit, path.Exit);
    }

    [Fact]
    public void ReflectionFromAFaceTurnedAwayFromTheSunIsRefused()
    {
        var incoming = new Vec3(-1.0, 0.0, 0.0);
        var away = PrismGeometry.Normal(3);

        var reflected = PrismGeometry.TryReflect(incoming, new PrismPath(3, PrismGeometry.ReflectionExit, 1.0), Index, out _, out _);

        Assert.True(-incoming.Dot(away) < 0.0);
        Assert.False(reflected);
    }
}
