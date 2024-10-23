using System;

namespace NAudio.Vorbis;

internal static class NullableUtils
{
    private const string ErrorMessage = "Sample provider or steam decoder is not initialized";

    public static T GetValueOrThrowIfNull<T>(this T? value) where T : struct => value ?? throw new InvalidOperationException(ErrorMessage);
    public static T GetValueOrThrowIfNull<T>(this T? value) where T : class  => value ?? throw new InvalidOperationException(ErrorMessage);
}