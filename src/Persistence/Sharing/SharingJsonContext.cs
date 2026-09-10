using System.Text.Json.Serialization;

namespace DesktopBuddy.Persistence.Sharing;

/// <summary>
/// Source-generated metadata for the sharing stores' persisted types.
///
/// Kept in this folder rather than alongside the other contexts on purpose: the itch scope removes
/// <c>src/Persistence/Sharing/**</c> wholesale, so declaring these types from a context that
/// survives that removal would not compile. Living here, the context is dropped exactly when the
/// types it describes are.
/// </summary>
[JsonSerializable(typeof(RoomPaintingLibraryEntry))]
[JsonSerializable(typeof(WorkshopProvenance))]
internal sealed partial class SharingJsonContext : JsonSerializerContext;
