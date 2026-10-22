using System.Numerics;
using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace Halo;

internal sealed class HaloCustomEffect(IGraphicsDevicesAndContext devices)
    : D2D1CustomShaderEffectBase(Create<EffectImpl>(devices))
{
    internal enum PropertyIndex
    {
        Amount = 0,
        SunX,
        SunY,
        SunZ,
        HalfFieldTangent,
        PitchCosine,
        PitchSine,
        SunAzimuth,
        SunElevation,
    }

    public float Amount { set => SetValue((int)PropertyIndex.Amount, value); }
    public float SunX { set => SetValue((int)PropertyIndex.SunX, value); }
    public float SunY { set => SetValue((int)PropertyIndex.SunY, value); }
    public float SunZ { set => SetValue((int)PropertyIndex.SunZ, value); }
    public float HalfFieldTangent { set => SetValue((int)PropertyIndex.HalfFieldTangent, value); }
    public float PitchCosine { set => SetValue((int)PropertyIndex.PitchCosine, value); }
    public float PitchSine { set => SetValue((int)PropertyIndex.PitchSine, value); }
    public float SunAzimuth { set => SetValue((int)PropertyIndex.SunAzimuth, value); }
    public float SunElevation { set => SetValue((int)PropertyIndex.SunElevation, value); }

    private const float DefaultHalfFieldTangent = 1f;

    [CustomEffect(3)]
    internal sealed class EffectImpl : D2D1CustomShaderEffectImplBase<EffectImpl>
    {
        private ConstantBuffer _cb;
        private RawRect _ringRect = new(0, 0, HaloSettings.RingTableWidth, 1);
        private RawRect _scatterRect = new(0, 0, ScatterGrid.Width, ScatterGrid.Height);

        [CustomEffectProperty(PropertyType.Float, (int)PropertyIndex.Amount)]
        public float Amount { get => _cb.Amount; set { _cb.Amount = Sanitize(value, 0f, 1f); UpdateConstants(); } }

        [CustomEffectProperty(PropertyType.Float, (int)PropertyIndex.SunX)]
        public float SunX { get => _cb.SunX; set { _cb.SunX = Sanitize(value, -1f, 1f); UpdateConstants(); } }

        [CustomEffectProperty(PropertyType.Float, (int)PropertyIndex.SunY)]
        public float SunY { get => _cb.SunY; set { _cb.SunY = Sanitize(value, -1f, 1f); UpdateConstants(); } }

        [CustomEffectProperty(PropertyType.Float, (int)PropertyIndex.SunZ)]
        public float SunZ { get => _cb.SunZ; set { _cb.SunZ = Sanitize(value, -1f, 1f); UpdateConstants(); } }

        [CustomEffectProperty(PropertyType.Float, (int)PropertyIndex.HalfFieldTangent)]
        public float HalfFieldTangent { get => _cb.HalfFieldTangent; set { _cb.HalfFieldTangent = float.IsNaN(value) ? DefaultHalfFieldTangent : Math.Clamp(value, HaloSettings.MinimumHalfFieldTangent, HaloSettings.MaximumHalfFieldTangent); UpdateConstants(); } }

        [CustomEffectProperty(PropertyType.Float, (int)PropertyIndex.PitchCosine)]
        public float PitchCosine { get => _cb.PitchCosine; set { _cb.PitchCosine = Sanitize(value, -1f, 1f); UpdateConstants(); } }

        [CustomEffectProperty(PropertyType.Float, (int)PropertyIndex.PitchSine)]
        public float PitchSine { get => _cb.PitchSine; set { _cb.PitchSine = Sanitize(value, -1f, 1f); UpdateConstants(); } }

        [CustomEffectProperty(PropertyType.Float, (int)PropertyIndex.SunAzimuth)]
        public float SunAzimuth { get => _cb.SunAzimuth; set { _cb.SunAzimuth = Sanitize(value, -MathF.PI, MathF.PI); UpdateConstants(); } }

        [CustomEffectProperty(PropertyType.Float, (int)PropertyIndex.SunElevation)]
        public float SunElevation { get => _cb.SunElevation; set { _cb.SunElevation = Sanitize(value, (float)HaloSettings.MinimumSunElevationDegrees, (float)HaloSettings.MaximumSunElevationDegrees); UpdateConstants(); } }

        public EffectImpl() : base(ShaderResourceUri.Get("Halo"))
        {
            _cb.SunZ = 1f;
            _cb.HalfFieldTangent = DefaultHalfFieldTangent;
            _cb.PitchCosine = 1f;
            _cb.RingStart = (float)HaloSettings.RingStartDegrees;
            _cb.RingSpan = (float)(HaloSettings.RingEndDegrees - HaloSettings.RingStartDegrees);
            _cb.RingWidth = HaloSettings.RingTableWidth;
            _cb.ScatterWidth = ScatterGrid.Width;
            _cb.ScatterHeight = ScatterGrid.Height;
            _cb.ScatterTexel = (float)HaloSettings.ScatterTexelDegrees;
            _cb.ScatterHalfSpan = (float)HaloSettings.ScatterElevationHalfSpanDegrees;
        }

        private static float Sanitize(float value, float minimum, float maximum)
            => float.IsNaN(value) ? 0f : Math.Clamp(value, minimum, maximum);

        protected override void UpdateConstants()
        {
            if (drawInformation is null)
                return;

            try
            {
                drawInformation.SetPixelShaderConstantBuffer(_cb);
            }
            catch (Exception exception)
            {
                HaloTelemetry.Report(exception);
                throw;
            }
        }

        public override void MapInputRectsToOutputRect(
            RawRect[] inputRects,
            RawRect[] inputOpaqueSubRects,
            out RawRect outputRect,
            out RawRect outputOpaqueSubRect)
        {
            inputRect = ClampInputRect(inputRects[0]);
            if (inputRects.Length > 1)
                _ringRect = inputRects[1];
            if (inputRects.Length > 2)
                _scatterRect = inputRects[2];
            outputRect = inputRect;
            outputOpaqueSubRect = default;
            if (inputRect.Right <= inputRect.Left || inputRect.Bottom <= inputRect.Top)
                return;

            _cb.InputBounds = new Vector4(inputRect.Left, inputRect.Top, inputRect.Right, inputRect.Bottom);
            UpdateConstants();
        }

        public override void MapOutputRectToInputRects(RawRect outputRect, RawRect[] inputRects)
        {
            if (inputRects.Length > 0)
                inputRects[0] = outputRect;
            if (inputRects.Length > 1)
                inputRects[1] = _ringRect;
            if (inputRects.Length > 2)
                inputRects[2] = _scatterRect;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ConstantBuffer
        {
            public Vector4 InputBounds;
            public float SunX;
            public float SunY;
            public float SunZ;
            public float HalfFieldTangent;
            public float PitchCosine;
            public float PitchSine;
            public float SunAzimuth;
            public float SunElevation;
            public float Amount;
            public float RingStart;
            public float RingSpan;
            public float RingWidth;
            public float ScatterWidth;
            public float ScatterHeight;
            public float ScatterTexel;
            public float ScatterHalfSpan;
        }
    }
}
