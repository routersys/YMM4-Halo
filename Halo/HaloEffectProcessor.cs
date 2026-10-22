using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using Vortice.DCommon;
using Vortice.DXGI;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Player.Video.Effects;

namespace Halo;

internal sealed class HaloEffectProcessor(IGraphicsDevicesAndContext devices, HaloEffect item) : VideoEffectProcessorBase(devices)
{
    private static readonly float[] EmptyScatter = new float[ScatterGrid.Width * ScatterGrid.Height * HaloSettings.ColorChannels];

    private readonly IGraphicsDevicesAndContext _devices = devices;
    private readonly HaloEffect _item = item;
    private HaloCustomEffect? _effect;
    private ID2D1Bitmap1? _ring;
    private ID2D1Bitmap1? _scatter;
    private float[]? _plate;
    private float[]? _column;
    private bool _isFirst = true;
    private RawKey _plateKey;
    private RawKey _columnKey;
    private RingKey _ringKey;
    private ScatterKey _scatterKey;
    private ConstantKey _constantKey;

    public override DrawDescription Update(EffectDescription effectDescription)
    {
        try
        {
            return UpdateCore(effectDescription);
        }
        catch (Exception exception)
        {
            HaloTelemetry.Report(exception);
            throw;
        }
    }

    private DrawDescription UpdateCore(EffectDescription effectDescription)
    {
        if (IsPassThroughEffect || _effect is null || _ring is null || _scatter is null)
            return effectDescription.DrawDescription;

        var frame = effectDescription.ItemPosition.Frame;
        var length = effectDescription.ItemDuration.Frame;
        var fps = effectDescription.FPS;
        var amount = (float)(_item.Amount.GetValue(frame, length, fps) / 100.0);
        if (_isFirst || _constantKey.Amount != amount)
            _effect.Amount = amount;

        if (amount <= 0f)
        {
            _constantKey = _constantKey with { Amount = amount };
            _isFirst = false;
            return effectDescription.DrawDescription;
        }

        var elevation = Math.Clamp(_item.SunElevation.GetValue(frame, length, fps), HaloSettings.MinimumSunElevationDegrees, HaloSettings.MaximumSunElevationDegrees);
        var quantized = HaloSettings.QuantizeSunElevation(elevation);
        var sunDiameter = Math.Clamp(_item.SunDiameter.GetValue(frame, length, fps), 0.0, HaloSettings.MaximumSunDiameterDegrees);
        var saturation = Math.Clamp(_item.Saturation.GetValue(frame, length, fps) / 100.0, 0.0, HaloSettings.MaximumSaturation);
        var randomShare = Math.Clamp(_item.RandomHalos.GetValue(frame, length, fps) / 100.0, 0.0, 1.0);
        var plateShare = Math.Clamp(_item.PlateHalos.GetValue(frame, length, fps) / 100.0, 0.0, 1.0);
        var columnShare = Math.Clamp(_item.ColumnArcs.GetValue(frame, length, fps) / 100.0, 0.0, 1.0);
        var tilt = Math.Clamp(_item.TiltSpread.GetValue(frame, length, fps), HaloSettings.MinimumTiltDegrees, HaloSettings.MaximumTiltDegrees);

        var ringKey = new RingKey(sunDiameter, randomShare, saturation);
        if (_isFirst || _ringKey != ringKey)
        {
            WriteTexture(_ring, HaloTable.ComposeRing(sunDiameter, randomShare, saturation), HaloSettings.RingTableWidth);
            _ringKey = ringKey;
        }

        var plateKey = new RawKey(quantized, Math.Clamp(_item.PlateThickness.GetValue(frame, length, fps), HaloSettings.MinimumPlateThickness, HaloSettings.MaximumPlateThickness), tilt);
        if (plateShare > 0.0 && (_plate is null || _plateKey != plateKey))
        {
            _plate = CrystalScatter.Integrate(CrystalFamily.Plate, plateKey.Elevation, plateKey.Length, plateKey.Tilt);
            _plateKey = plateKey;
        }

        var columnKey = new RawKey(quantized, Math.Clamp(_item.ColumnLength.GetValue(frame, length, fps), HaloSettings.MinimumColumnLength, HaloSettings.MaximumColumnLength), tilt);
        if (columnShare > 0.0 && (_column is null || _columnKey != columnKey))
        {
            _column = CrystalScatter.Integrate(CrystalFamily.Column, columnKey.Elevation, columnKey.Length, columnKey.Tilt);
            _columnKey = columnKey;
        }

        var scatterKey = new ScatterKey(quantized, plateShare, columnShare, sunDiameter, saturation, plateShare > 0.0 ? _plateKey : default, columnShare > 0.0 ? _columnKey : default);
        if (_isFirst || _scatterKey != scatterKey)
        {
            WriteTexture(_scatter, HaloTable.ComposeScatter(plateShare > 0.0 ? _plate! : EmptyScatter, columnShare > 0.0 ? _column! : EmptyScatter, plateShare, columnShare, quantized, sunDiameter, saturation), ScatterGrid.Width);
            _scatterKey = scatterKey;
        }

        var direction = _item.SunDirection.GetValue(frame, length, fps);
        var pitch = Math.Clamp(_item.CameraPitch.GetValue(frame, length, fps), HaloSettings.MinimumCameraPitchDegrees, HaloSettings.MaximumCameraPitchDegrees);
        var sun = HaloSettings.GetSunDirection(elevation, direction, pitch);
        var constantKey = new ConstantKey(
            amount,
            sun.X,
            sun.Y,
            sun.Z,
            HaloSettings.GetHalfFieldOfViewTangent(_item.FieldOfView.GetValue(frame, length, fps)),
            (float)Math.Cos(pitch * HaloSettings.RadiansPerDegree),
            (float)Math.Sin(pitch * HaloSettings.RadiansPerDegree),
            (float)(Math.Clamp(direction, -180.0, 180.0) * HaloSettings.RadiansPerDegree),
            (float)elevation);
        if (_isFirst || _constantKey.SunX != constantKey.SunX)
            _effect.SunX = constantKey.SunX;
        if (_isFirst || _constantKey.SunY != constantKey.SunY)
            _effect.SunY = constantKey.SunY;
        if (_isFirst || _constantKey.SunZ != constantKey.SunZ)
            _effect.SunZ = constantKey.SunZ;
        if (_isFirst || _constantKey.HalfFieldTangent != constantKey.HalfFieldTangent)
            _effect.HalfFieldTangent = constantKey.HalfFieldTangent;
        if (_isFirst || _constantKey.PitchCosine != constantKey.PitchCosine)
            _effect.PitchCosine = constantKey.PitchCosine;
        if (_isFirst || _constantKey.PitchSine != constantKey.PitchSine)
            _effect.PitchSine = constantKey.PitchSine;
        if (_isFirst || _constantKey.SunAzimuth != constantKey.SunAzimuth)
            _effect.SunAzimuth = constantKey.SunAzimuth;
        if (_isFirst || _constantKey.SunElevation != constantKey.SunElevation)
            _effect.SunElevation = constantKey.SunElevation;

        _constantKey = constantKey;
        _isFirst = false;
        return effectDescription.DrawDescription;
    }

    private static void WriteTexture(ID2D1Bitmap1 bitmap, Half[] texels, int width)
    {
        var handle = GCHandle.Alloc(texels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyFromMemory(handle.AddrOfPinnedObject(), width * HaloSettings.TexelBytes);
        }
        finally
        {
            handle.Free();
        }
    }

    private ID2D1Bitmap1 CreateTexture(IGraphicsDevicesAndContext devices, int width, int height)
    {
        var properties = new BitmapProperties1(new PixelFormat(Format.R16G16B16A16_Float, Vortice.DCommon.AlphaMode.Premultiplied), 96f, 96f, BitmapOptions.None);
        var empty = new Half[width * height * HaloSettings.TexelChannels];
        var handle = GCHandle.Alloc(empty, GCHandleType.Pinned);
        try
        {
            var bitmap = ((ID2D1DeviceContext1)devices.DeviceContext).CreateBitmap(
                new Vortice.Mathematics.SizeI(width, height),
                handle.AddrOfPinnedObject(),
                width * HaloSettings.TexelBytes,
                properties);
            disposer.Collect(bitmap);
            return bitmap;
        }
        finally
        {
            handle.Free();
        }
    }

    protected override ID2D1Image? CreateEffect(IGraphicsDevicesAndContext devices)
    {
        var effect = new HaloCustomEffect(devices);
        if (!effect.IsEnabled)
        {
            effect.Dispose();
            return null;
        }

        _effect = effect;
        disposer.Collect(effect);
        _ring = CreateTexture(devices, HaloSettings.RingTableWidth, 1);
        _scatter = CreateTexture(devices, ScatterGrid.Width, ScatterGrid.Height);
        _effect.SetInput(1, _ring, true);
        _effect.SetInput(2, _scatter, true);
        var output = effect.Output;
        disposer.Collect(output);
        return output;
    }

    protected override void setInput(ID2D1Image? input)
    {
        _effect?.SetInput(0, input, true);
    }

    protected override void ClearEffectChain()
    {
        try
        {
            _effect?.SetInput(0, null, true);
            _isFirst = true;
        }
        catch (Exception exception)
        {
            HaloTelemetry.Report(exception);
            throw;
        }
    }

    private readonly record struct RawKey(double Elevation, double Length, double Tilt);

    private readonly record struct RingKey(double SunDiameter, double Share, double Saturation);

    private readonly record struct ScatterKey(double Elevation, double PlateShare, double ColumnShare, double SunDiameter, double Saturation, RawKey Plate, RawKey Column);

    private readonly record struct ConstantKey(float Amount, float SunX, float SunY, float SunZ, float HalfFieldTangent, float PitchCosine, float PitchSine, float SunAzimuth, float SunElevation);
}
