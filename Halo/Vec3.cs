namespace Halo;

internal readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 operator +(Vec3 left, Vec3 right)
        => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    public static Vec3 operator -(Vec3 left, Vec3 right)
        => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    public static Vec3 operator -(Vec3 value)
        => new(-value.X, -value.Y, -value.Z);

    public static Vec3 operator *(double scale, Vec3 value)
        => new(scale * value.X, scale * value.Y, scale * value.Z);

    public double Dot(Vec3 other)
        => X * other.X + Y * other.Y + Z * other.Z;

    public Vec3 Cross(Vec3 other)
        => new(Y * other.Z - Z * other.Y, Z * other.X - X * other.Z, X * other.Y - Y * other.X);

    public double Length
        => Math.Sqrt(Dot(this));

    public Vec3 Normalized()
        => (1.0 / Length) * this;
}
