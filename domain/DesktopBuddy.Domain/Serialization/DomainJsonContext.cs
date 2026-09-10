using System.Collections.Generic;
using System.Text.Json.Serialization;
using DesktopBuddy.Domain.Automation;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Sharing;
using DesktopBuddy.Domain.Telemetry;

namespace DesktopBuddy.Domain.Serialization;

/// <summary>
/// Source-generated serialization metadata for every persisted domain type.
///
/// NativeAOT strips the reflection metadata that <see cref="System.Text.Json"/> otherwise builds a
/// serializer from at runtime, so a reflection-based load throws and the save reads as corrupt.
/// Generating the metadata at compile time keeps the exported build able to read its own files.
///
/// No serializer option is declared here on purpose. Each store keeps the
/// <see cref="System.Text.Json.JsonSerializerOptions"/> it already had and attaches this context as
/// its resolver, so naming, indentation and ignore conditions stay exactly as written and the save
/// format cannot drift. DomainJsonRoundTripTests holds that byte-for-byte.
/// </summary>
[JsonSerializable(typeof(ProgressSave))]
[JsonSerializable(typeof(LocalSettingsSave))]
[JsonSerializable(typeof(CharacterDocument))]
[JsonSerializable(typeof(ShareManifest))]
[JsonSerializable(typeof(TelemetryEnvelope))]
[JsonSerializable(typeof(TelemetryFrame))]
[JsonSerializable(typeof(InputTrace))]
// Written into the [JsonExtensionData] bag rather than as a document root.
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
public sealed partial class DomainJsonContext : JsonSerializerContext;
