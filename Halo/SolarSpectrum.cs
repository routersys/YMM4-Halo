namespace Halo;

internal readonly record struct Tristimulus(double X, double Y, double Z)
{
    public static Tristimulus operator +(Tristimulus left, Tristimulus right)
        => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    public static Tristimulus operator *(double scale, Tristimulus value)
        => new(scale * value.X, scale * value.Y, scale * value.Z);
}

internal readonly record struct SpectralBand(double Wavelength, Tristimulus Weight);

internal readonly record struct ColorLobe(double Amplitude, double Center, double LeftScale, double RightScale)
{
    public double Evaluate(double wavelength)
    {
        var t = (wavelength - Center) * (wavelength < Center ? LeftScale : RightScale);
        return Amplitude * Math.Exp(-0.5 * t * t);
    }
}

internal static class SolarSpectrum
{
    private const double PlanckConstant = 6.62607015e-34;
    private const double LightSpeed = 299792458.0;
    private const double BoltzmannConstant = 1.380649e-23;

    private static readonly ColorLobe[] MatchingLobesX =
    [
        new(0.362, 442.0, 0.0624, 0.0374),
        new(1.056, 599.8, 0.0264, 0.0323),
        new(-0.065, 501.1, 0.0490, 0.0382),
    ];

    private static readonly ColorLobe[] MatchingLobesY =
    [
        new(0.821, 568.8, 0.0213, 0.0247),
        new(0.286, 530.9, 0.0613, 0.0322),
    ];

    private static readonly ColorLobe[] MatchingLobesZ =
    [
        new(1.217, 437.0, 0.0845, 0.0278),
        new(0.681, 459.0, 0.0385, 0.0725),
    ];

    private static readonly Lazy<Tristimulus[]> WeightTable = new(BuildWeights);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, SpectralBand[]> BandTables = new();

    public static Tristimulus[] Weights => WeightTable.Value;

    public static Tristimulus White
    {
        get
        {
            var white = new Tristimulus();
            foreach (var weight in Weights)
                white += weight;
            return white;
        }
    }

    public static SpectralBand[] Bands(int stride)
        => BandTables.GetOrAdd(stride, BuildBands);

    public static double Radiance(double wavelengthNanometers, double temperature)
    {
        var wavelength = wavelengthNanometers * 1e-9;
        var exponent = PlanckConstant * LightSpeed / (wavelength * BoltzmannConstant * temperature);
        return 2.0 * PlanckConstant * LightSpeed * LightSpeed / (Math.Pow(wavelength, 5) * (Math.Exp(exponent) - 1.0));
    }

    public static Tristimulus ColorMatching(double wavelengthNanometers)
        => new(Sum(MatchingLobesX, wavelengthNanometers), Sum(MatchingLobesY, wavelengthNanometers), Sum(MatchingLobesZ, wavelengthNanometers));

    public static (double R, double G, double B) ToLinearSrgb(Tristimulus value)
        => (
            3.2406 * value.X - 1.5372 * value.Y - 0.4986 * value.Z,
            -0.9689 * value.X + 1.8758 * value.Y + 0.0415 * value.Z,
            0.0557 * value.X - 0.2040 * value.Y + 1.0570 * value.Z);

    private static SpectralBand[] BuildBands(int stride)
    {
        var weights = Weights;
        var bands = new SpectralBand[(weights.Length + stride - 1) / stride];
        for (var band = 0; band < bands.Length; band++)
        {
            var total = new Tristimulus();
            var wavelength = 0.0;
            var luminance = 0.0;
            for (var index = band * stride; index < Math.Min((band + 1) * stride, weights.Length); index++)
            {
                total += weights[index];
                wavelength += weights[index].Y * HaloSettings.GetWavelengthNanometers(index);
                luminance += weights[index].Y;
            }

            bands[band] = new SpectralBand(wavelength / luminance, total);
        }

        return bands;
    }

    private static Tristimulus[] BuildWeights()
    {
        var count = HaloSettings.GetWavelengthCount();
        var weights = new Tristimulus[count];
        var luminance = 0.0;
        for (var index = 0; index < count; index++)
        {
            var wavelength = HaloSettings.GetWavelengthNanometers(index);
            weights[index] = Radiance(wavelength, HaloSettings.SolarTemperature) * ColorMatching(wavelength);
            luminance += weights[index].Y;
        }

        for (var index = 0; index < count; index++)
            weights[index] = (1.0 / luminance) * weights[index];
        return weights;
    }

    private static double Sum(ColorLobe[] lobes, double wavelength)
    {
        var sum = 0.0;
        foreach (var lobe in lobes)
            sum += lobe.Evaluate(wavelength);
        return sum;
    }
}
