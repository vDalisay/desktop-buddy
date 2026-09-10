using System.Collections.Generic;
using System.Text.Json.Serialization;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Persistence.Sharing;

namespace DesktopBuddy.Serialization;

/// <summary>
/// Source-generated serialization metadata for the game assembly's persisted types.
///
/// The companion to DesktopBuddy.Domain's DomainJsonContext; see that type for why this exists.
/// Domain roots are re-declared here where this assembly serializes them directly, because a
/// resolver only answers for the types its own context was generated for.
/// </summary>
[JsonSerializable(typeof(RoomPaintingLibraryEntry))]
[JsonSerializable(typeof(WorkshopProvenance))]
[JsonSerializable(typeof(CharacterDocument))]
// Written into a [JsonExtensionData] bag rather than as a document root.
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
