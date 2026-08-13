using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using DesktopBuddy.CharacterEditor.BuddyStudio;
using DesktopBuddy.Domain.Characters;

namespace DesktopBuddy.Buddy.Presentation3D.Characters;

public enum BuddyCosmeticAnchorId
{
    HeadFront,
    HeadCrown,
    LeftEar,
    RightEar,
    EyeGroup,
    TorsoFront,
    TorsoAttachment,
    LeftFoot,
    RightFoot,
}

public enum BuddyCosmeticRenderLayer
{
    Top = 10,
    FaceDetail = 20,
    Hair = 30,
    Glasses = 40,
    Accessory = 50,
    Headwear = 60,
}

public enum BuddyCosmeticVisualKind
{
    None,
    HairShortSweep,
    NoseButton,
    EarsRoundTabs,
    WorkClassicGlasses,
    HeadwearSoftCap,
    TopUtilityBib,
    ShoesSoftSteps,
    GeneratedAsset,
}

public sealed record BuddyCosmeticVisualDefinition(
    string CosmeticId,
    CharacterFeatureSlot Slot,
    BuddyCosmeticAnchorId Anchor,
    BuddyCosmeticRenderLayer Layer,
    BuddyCosmeticVisualKind Kind,
    BuddyCosmeticAnchorId? SecondaryAnchor = null,
    GeneratedBuddyCosmeticResource? GeneratedResource = null);

/// <summary>
/// Project-owned mapping from stable cosmetic IDs to trusted render families and anchors.
/// Legacy shipped visuals remain procedural; Asset Forge entries arrive through the separately
/// validated generated registry and never allow character JSON to name a resource path.
/// </summary>
public sealed class BuddyCosmeticVisualCatalog
{
    private readonly IReadOnlyDictionary<string, BuddyCosmeticVisualDefinition> _definitions;
    private readonly CharacterFeatureCatalog _cosmetics;

    public BuddyCosmeticVisualCatalog(
        CharacterFeatureCatalog? cosmetics = null,
        BuddyGeneratedCosmeticRegistry? generated = null)
    {
        if (cosmetics is null && generated is null)
            generated = BuddyGeneratedCosmeticRegistry.Current;
        _cosmetics = cosmetics ?? generated!.FeatureCatalog;

        var definitions = new Dictionary<string, BuddyCosmeticVisualDefinition>(StringComparer.Ordinal);
        Add(definitions, CharacterFeatureIds.FaceClassicPlate, CharacterFeatureSlot.Face, BuddyCosmeticAnchorId.HeadFront, BuddyCosmeticRenderLayer.FaceDetail, BuddyCosmeticVisualKind.None);
        Add(definitions, CharacterFeatureIds.HairNone, CharacterFeatureSlot.Hair, BuddyCosmeticAnchorId.HeadCrown, BuddyCosmeticRenderLayer.Hair, BuddyCosmeticVisualKind.None);
        Add(definitions, CharacterFeatureIds.HairShortSweep, CharacterFeatureSlot.Hair, BuddyCosmeticAnchorId.HeadCrown, BuddyCosmeticRenderLayer.Hair, BuddyCosmeticVisualKind.HairShortSweep);
        Add(definitions, CharacterFeatureIds.NoseNone, CharacterFeatureSlot.Nose, BuddyCosmeticAnchorId.HeadFront, BuddyCosmeticRenderLayer.FaceDetail, BuddyCosmeticVisualKind.None);
        Add(definitions, CharacterFeatureIds.NoseButton, CharacterFeatureSlot.Nose, BuddyCosmeticAnchorId.HeadFront, BuddyCosmeticRenderLayer.FaceDetail, BuddyCosmeticVisualKind.NoseButton);
        Add(definitions, CharacterFeatureIds.EarsNone, CharacterFeatureSlot.Ears, BuddyCosmeticAnchorId.LeftEar, BuddyCosmeticRenderLayer.FaceDetail, BuddyCosmeticVisualKind.None, BuddyCosmeticAnchorId.RightEar);
        Add(definitions, CharacterFeatureIds.EarsRoundTabs, CharacterFeatureSlot.Ears, BuddyCosmeticAnchorId.LeftEar, BuddyCosmeticRenderLayer.FaceDetail, BuddyCosmeticVisualKind.EarsRoundTabs, BuddyCosmeticAnchorId.RightEar);
        Add(definitions, CharacterFeatureIds.GlassesNone, CharacterFeatureSlot.Glasses, BuddyCosmeticAnchorId.EyeGroup, BuddyCosmeticRenderLayer.Glasses, BuddyCosmeticVisualKind.None);
        Add(definitions, CharacterFeatureIds.GlassesWorkClassic, CharacterFeatureSlot.Glasses, BuddyCosmeticAnchorId.EyeGroup, BuddyCosmeticRenderLayer.Glasses, BuddyCosmeticVisualKind.WorkClassicGlasses);
        Add(definitions, CharacterFeatureIds.HeadwearNone, CharacterFeatureSlot.Headwear, BuddyCosmeticAnchorId.HeadCrown, BuddyCosmeticRenderLayer.Headwear, BuddyCosmeticVisualKind.None);
        Add(definitions, CharacterFeatureIds.HeadwearSoftCap, CharacterFeatureSlot.Headwear, BuddyCosmeticAnchorId.HeadCrown, BuddyCosmeticRenderLayer.Headwear, BuddyCosmeticVisualKind.HeadwearSoftCap);
        Add(definitions, CharacterFeatureIds.TopNone, CharacterFeatureSlot.Tops, BuddyCosmeticAnchorId.TorsoFront, BuddyCosmeticRenderLayer.Top, BuddyCosmeticVisualKind.None);
        Add(definitions, CharacterFeatureIds.TopUtilityBib, CharacterFeatureSlot.Tops, BuddyCosmeticAnchorId.TorsoFront, BuddyCosmeticRenderLayer.Top, BuddyCosmeticVisualKind.TopUtilityBib);
        Add(definitions, CharacterFeatureIds.ShoesNone, CharacterFeatureSlot.Shoes, BuddyCosmeticAnchorId.LeftFoot, BuddyCosmeticRenderLayer.Top, BuddyCosmeticVisualKind.None, BuddyCosmeticAnchorId.RightFoot);
        Add(definitions, CharacterFeatureIds.ShoesSoftSteps, CharacterFeatureSlot.Shoes, BuddyCosmeticAnchorId.LeftFoot, BuddyCosmeticRenderLayer.Top, BuddyCosmeticVisualKind.ShoesSoftSteps, BuddyCosmeticAnchorId.RightFoot);

        if (generated is not null)
        {
            foreach (GeneratedBuddyCosmeticResource resource in generated.Entries)
            {
                if (!_cosmetics.Contains(resource.Slot, resource.FeatureId))
                    throw new InvalidOperationException($"Generated visual '{resource.FeatureId}' is not in its composed feature catalogue.");
                AddGenerated(definitions, resource);
            }
        }

        foreach (BuddyCosmeticVisualDefinition definition in definitions.Values)
            if (!_cosmetics.Contains(definition.Slot, definition.CosmeticId))
                throw new InvalidOperationException($"Visual '{definition.CosmeticId}' is not in its domain category.");

        foreach (CharacterFeatureSlot slot in new[]
                 {
                     CharacterFeatureSlot.Hair,
                     CharacterFeatureSlot.Face,
                     CharacterFeatureSlot.Nose,
                     CharacterFeatureSlot.Ears,
                     CharacterFeatureSlot.Glasses,
                     CharacterFeatureSlot.Headwear,
                     CharacterFeatureSlot.Tops,
                     CharacterFeatureSlot.Shoes,
                 })
        {
            foreach (string id in _cosmetics.GetIds(slot))
                if (!definitions.ContainsKey(id))
                    throw new InvalidOperationException($"Missing trusted visual registration for '{id}'.");
        }

        _definitions = new ReadOnlyDictionary<string, BuddyCosmeticVisualDefinition>(definitions);
    }

    public IEnumerable<BuddyCosmeticVisualDefinition> Definitions => _definitions.Values;

    public BuddyCosmeticVisualDefinition Resolve(CharacterFeatureSlot slot, string cosmeticId, out bool usedFallback)
    {
        if (_definitions.TryGetValue(cosmeticId, out BuddyCosmeticVisualDefinition? definition) && definition.Slot == slot)
        {
            usedFallback = false;
            return definition;
        }
        string fallbackId = _cosmetics.GetDefaultId(slot);
        usedFallback = true;
        return _definitions[fallbackId];
    }

    private static void AddGenerated(
        IDictionary<string, BuddyCosmeticVisualDefinition> definitions,
        GeneratedBuddyCosmeticResource resource)
    {
        BuddyCosmeticAnchorId anchor = resource.Slot switch
        {
            CharacterFeatureSlot.Glasses => BuddyCosmeticAnchorId.EyeGroup,
            _ => throw new InvalidOperationException($"Asset Forge v1 does not support generated {resource.Slot} visuals."),
        };
        if (!definitions.TryAdd(resource.FeatureId,
                new BuddyCosmeticVisualDefinition(
                    resource.FeatureId,
                    resource.Slot,
                    anchor,
                    BuddyCosmeticRenderLayer.Glasses,
                    BuddyCosmeticVisualKind.GeneratedAsset,
                    GeneratedResource: resource)))
            throw new InvalidOperationException($"Duplicate trusted cosmetic visual '{resource.FeatureId}'.");
    }

    private static void Add(
        IDictionary<string, BuddyCosmeticVisualDefinition> definitions,
        string id,
        CharacterFeatureSlot slot,
        BuddyCosmeticAnchorId anchor,
        BuddyCosmeticRenderLayer layer,
        BuddyCosmeticVisualKind kind,
        BuddyCosmeticAnchorId? secondaryAnchor = null)
    {
        if (!definitions.TryAdd(id, new BuddyCosmeticVisualDefinition(id, slot, anchor, layer, kind, secondaryAnchor)))
            throw new InvalidOperationException($"Duplicate trusted cosmetic visual '{id}'.");
    }
}
