using System;
using System.Collections.Generic;

namespace DesktopBuddy.Domain.Physics;

/// <summary>
/// Immutable result of resolving a stored zoom preference against a client
/// size. Client dimensions are pixels; room dimensions are physics world units.
/// </summary>
public readonly record struct RoomLayout(
    int ClientWidth,
    int ClientHeight,
    double StoredZoom,
    double EffectiveZoom,
    double RoomWidth,
    double RoomHeight);

/// <summary>
/// Confirmed FR-001 zoom/room-floor policy. This remains Godot-free so resize
/// and settings behavior can be exhaustively tested without an engine runtime.
/// </summary>
public static class RoomLayoutPolicy
{
    public const int DefaultClientWidth = 480;
    public const int DefaultClientHeight = 360;
    public const int MinimumRoomWidth = 360;
    public const int MinimumRoomHeight = 270;

    private static readonly double[] ZoomValues = { 0.75, 1.0, 1.25, 1.5, 1.75, 2.0 };
    private static readonly IReadOnlyList<double> ReadOnlyZoomValues = Array.AsReadOnly(ZoomValues);

    public static IReadOnlyList<double> SupportedZooms => ReadOnlyZoomValues;

    public static RoomLayout Resolve(int clientWidth, int clientHeight, double storedZoom)
    {
        ValidateClientSize(clientWidth, clientHeight);
        ValidateSupportedZoom(storedZoom);

        double maximumZoom = Math.Min(
            (double)clientWidth / MinimumRoomWidth,
            (double)clientHeight / MinimumRoomHeight);

        double largestAvailableZoom = ZoomValues[0];
        for (int index = 1; index < ZoomValues.Length; index++)
        {
            if (ZoomValues[index] > maximumZoom + 1e-9)
            {
                break;
            }

            largestAvailableZoom = ZoomValues[index];
        }

        double effectiveZoom = Math.Min(storedZoom, largestAvailableZoom);
        return new RoomLayout(
            clientWidth,
            clientHeight,
            storedZoom,
            effectiveZoom,
            clientWidth / effectiveZoom,
            clientHeight / effectiveZoom);
    }

    public static bool IsZoomAvailable(int clientWidth, int clientHeight, double zoom)
    {
        ValidateClientSize(clientWidth, clientHeight);
        ValidateSupportedZoom(zoom);
        return clientWidth / zoom >= MinimumRoomWidth - 1e-9 &&
               clientHeight / zoom >= MinimumRoomHeight - 1e-9;
    }

    private static void ValidateClientSize(int width, int height)
    {
        if (width < MinimumRoomWidth || height < MinimumRoomHeight)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                $"Client size must be at least {MinimumRoomWidth}x{MinimumRoomHeight}.");
        }
    }

    private static void ValidateSupportedZoom(double zoom)
    {
        for (int index = 0; index < ZoomValues.Length; index++)
        {
            if (Math.Abs(ZoomValues[index] - zoom) <= 1e-9)
            {
                return;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(zoom), zoom, "Zoom is not a confirmed supported value.");
    }
}
