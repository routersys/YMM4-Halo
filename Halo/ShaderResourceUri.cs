namespace Halo;

internal static class ShaderResourceUri
{
    public static Uri Get(string shaderName) => new($"pack://application:,,,/Halo;component/Resources/Shader/{shaderName}.cso", UriKind.Absolute);
}
