using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using DesktopBuddy.CharacterEditor.BuddyStudio;
using DesktopBuddy.Domain.Characters;

namespace DesktopBuddy.Buddy.Presentation3D.Characters;

public enum BuddyCosmeticAnchorId { HeadFront, HeadCrown, LeftEar, RightEar, EyeGroup, TorsoBody, TorsoFront, TorsoAttachment, LeftFoot, RightFoot }
public enum BuddyCosmeticRenderLayer { Top = 10, FaceDetail = 20, Hair = 30, Glasses = 40, Accessory = 50, Headwear = 60 }
public enum BuddyCosmeticVisualKind { None, HairShortSweep, HairBobBangs, HairBuzzCut, NoseButton, NoseTriangle, NoseBroadOval, EarsRoundTabs, EarsPointedTips, EarsFlatDiscs, WorkClassicGlasses, GlassesRoundWire, GlassesShades, HeadwearSoftCap, HeadwearKnitBeanie, HeadwearWideBrim, TopUtilityBib, ShoesSoftSteps, FaceWrinkles, FaceChiseledCheeks, FaceFreckles, FaceRosyCheeks, FaceStubble, HairElderTufts, NosePointedBeak, NoseWideFlat, NoseUpturned, NoseHooked, NoseTinyDot, EarsElf, GlassesSquareFrames, GlassesCatEye, GlassesAviators, GlassesHalfMoon, GlassesVisor, HeadwearBallCap, HeadwearSunflowerHat, HeadwearFedora, GeneratedAsset }
public enum BuddyCosmeticApplicationMode { Attachment, PartReplacement, PairedPartReplacement }

public sealed record BuddyCosmeticVisualDefinition(string CosmeticId, CharacterFeatureSlot Slot, BuddyCosmeticAnchorId Anchor, BuddyCosmeticRenderLayer Layer, BuddyCosmeticVisualKind Kind, BuddyCosmeticAnchorId? SecondaryAnchor = null, GeneratedBuddyCosmeticResource? GeneratedResource = null, BuddyCosmeticApplicationMode ApplicationMode = BuddyCosmeticApplicationMode.Attachment);

public sealed class BuddyCosmeticVisualCatalog
{
    private readonly IReadOnlyDictionary<string, BuddyCosmeticVisualDefinition> _definitions;
    private readonly CharacterFeatureCatalog _cosmetics;

    public BuddyCosmeticVisualCatalog(CharacterFeatureCatalog? cosmetics = null, BuddyGeneratedCosmeticRegistry? generated = null)
    {
        if (cosmetics is null && generated is null) generated = BuddyGeneratedCosmeticRegistry.Current;
        _cosmetics = cosmetics ?? generated!.FeatureCatalog;
        var definitions = new Dictionary<string, BuddyCosmeticVisualDefinition>(StringComparer.Ordinal);
        Add(definitions, CharacterFeatureIds.FaceClassicPlate, CharacterFeatureSlot.Face, BuddyCosmeticAnchorId.HeadFront, BuddyCosmeticRenderLayer.FaceDetail, BuddyCosmeticVisualKind.None);
        Add(definitions, CharacterFeatureIds.HairNone, CharacterFeatureSlot.Hair, BuddyCosmeticAnchorId.HeadCrown, BuddyCosmeticRenderLayer.Hair, BuddyCosmeticVisualKind.None);
        Add(definitions, CharacterFeatureIds.HairShortSweep, CharacterFeatureSlot.Hair, BuddyCosmeticAnchorId.HeadCrown, BuddyCosmeticRenderLayer.Hair, BuddyCosmeticVisualKind.HairShortSweep);
        Add(definitions, CharacterFeatureIds.HairBobBangs, CharacterFeatureSlot.Hair, BuddyCosmeticAnchorId.HeadCrown, BuddyCosmeticRenderLayer.Hair, BuddyCosmeticVisualKind.HairBobBangs);
        Add(definitions, CharacterFeatureIds.HairBuzzCut, CharacterFeatureSlot.Hair, BuddyCosmeticAnchorId.HeadCrown, BuddyCosmeticRenderLayer.Hair, BuddyCosmeticVisualKind.HairBuzzCut);
        Add(definitions, CharacterFeatureIds.NoseNone, CharacterFeatureSlot.Nose, BuddyCosmeticAnchorId.HeadFront, BuddyCosmeticRenderLayer.FaceDetail, BuddyCosmeticVisualKind.None);
        Add(definitions, CharacterFeatureIds.NoseButton, CharacterFeatureSlot.Nose, BuddyCosmeticAnchorId.HeadFront, BuddyCosmeticRenderLayer.FaceDetail, BuddyCosmeticVisualKind.NoseButton);
        Add(definitions, CharacterFeatureIds.NoseTriangle, CharacterFeatureSlot.Nose, BuddyCosmeticAnchorId.HeadFront, BuddyCosmeticRenderLayer.FaceDetail, BuddyCosmeticVisualKind.NoseTriangle);
        Add(definitions, CharacterFeatureIds.NoseBroadOval, CharacterFeatureSlot.Nose, BuddyCosmeticAnchorId.HeadFront, BuddyCosmeticRenderLayer.FaceDetail, BuddyCosmeticVisualKind.NoseBroadOval);
        Add(definitions, CharacterFeatureIds.EarsNone, CharacterFeatureSlot.Ears, BuddyCosmeticAnchorId.LeftEar, BuddyCosmeticRenderLayer.FaceDetail, BuddyCosmeticVisualKind.None, BuddyCosmeticAnchorId.RightEar);
        Add(definitions, CharacterFeatureIds.EarsRoundTabs, CharacterFeatureSlot.Ears, BuddyCosmeticAnchorId.LeftEar, BuddyCosmeticRenderLayer.FaceDetail, BuddyCosmeticVisualKind.EarsRoundTabs, BuddyCosmeticAnchorId.RightEar);
        Add(definitions, CharacterFeatureIds.EarsPointedTips, CharacterFeatureSlot.Ears, BuddyCosmeticAnchorId.LeftEar, BuddyCosmeticRenderLayer.FaceDetail, BuddyCosmeticVisualKind.EarsPointedTips, BuddyCosmeticAnchorId.RightEar);
        Add(definitions, CharacterFeatureIds.EarsFlatDiscs, CharacterFeatureSlot.Ears, BuddyCosmeticAnchorId.LeftEar, BuddyCosmeticRenderLayer.FaceDetail, BuddyCosmeticVisualKind.EarsFlatDiscs, BuddyCosmeticAnchorId.RightEar);
        Add(definitions, CharacterFeatureIds.GlassesNone, CharacterFeatureSlot.Glasses, BuddyCosmeticAnchorId.EyeGroup, BuddyCosmeticRenderLayer.Glasses, BuddyCosmeticVisualKind.None);
        Add(definitions, CharacterFeatureIds.GlassesWorkClassic, CharacterFeatureSlot.Glasses, BuddyCosmeticAnchorId.EyeGroup, BuddyCosmeticRenderLayer.Glasses, BuddyCosmeticVisualKind.WorkClassicGlasses);
        Add(definitions, CharacterFeatureIds.GlassesRoundWire, CharacterFeatureSlot.Glasses, BuddyCosmeticAnchorId.EyeGroup, BuddyCosmeticRenderLayer.Glasses, BuddyCosmeticVisualKind.GlassesRoundWire);
        Add(definitions, CharacterFeatureIds.GlassesShades, CharacterFeatureSlot.Glasses, BuddyCosmeticAnchorId.EyeGroup, BuddyCosmeticRenderLayer.Glasses, BuddyCosmeticVisualKind.GlassesShades);
        Add(definitions, CharacterFeatureIds.HeadwearNone, CharacterFeatureSlot.Headwear, BuddyCosmeticAnchorId.HeadCrown, BuddyCosmeticRenderLayer.Headwear, BuddyCosmeticVisualKind.None);
        Add(definitions, CharacterFeatureIds.HeadwearSoftCap, CharacterFeatureSlot.Headwear, BuddyCosmeticAnchorId.HeadCrown, BuddyCosmeticRenderLayer.Headwear, BuddyCosmeticVisualKind.HeadwearSoftCap);
        Add(definitions, CharacterFeatureIds.HeadwearKnitBeanie, CharacterFeatureSlot.Headwear, BuddyCosmeticAnchorId.HeadCrown, BuddyCosmeticRenderLayer.Headwear, BuddyCosmeticVisualKind.HeadwearKnitBeanie);
        Add(definitions, CharacterFeatureIds.HeadwearWideBrim, CharacterFeatureSlot.Headwear, BuddyCosmeticAnchorId.HeadCrown, BuddyCosmeticRenderLayer.Headwear, BuddyCosmeticVisualKind.HeadwearWideBrim);
        Add(definitions, CharacterFeatureIds.TopNone, CharacterFeatureSlot.Tops, BuddyCosmeticAnchorId.TorsoBody, BuddyCosmeticRenderLayer.Top, BuddyCosmeticVisualKind.None);
        Add(definitions, CharacterFeatureIds.TopUtilityBib, CharacterFeatureSlot.Tops, BuddyCosmeticAnchorId.TorsoBody, BuddyCosmeticRenderLayer.Top, BuddyCosmeticVisualKind.TopUtilityBib, applicationMode: BuddyCosmeticApplicationMode.PartReplacement);
        Add(definitions, CharacterFeatureIds.ShoesNone, CharacterFeatureSlot.Shoes, BuddyCosmeticAnchorId.LeftFoot, BuddyCosmeticRenderLayer.Top, BuddyCosmeticVisualKind.None, BuddyCosmeticAnchorId.RightFoot);
        Add(definitions, CharacterFeatureIds.ShoesSoftSteps, CharacterFeatureSlot.Shoes, BuddyCosmeticAnchorId.LeftFoot, BuddyCosmeticRenderLayer.Top, BuddyCosmeticVisualKind.ShoesSoftSteps, BuddyCosmeticAnchorId.RightFoot, applicationMode: BuddyCosmeticApplicationMode.PairedPartReplacement);
        Add(definitions, CharacterFeatureIds.FaceWrinkles, CharacterFeatureSlot.Face, BuddyCosmeticAnchorId.HeadFront, BuddyCosmeticRenderLayer.FaceDetail, BuddyCosmeticVisualKind.FaceWrinkles);
        Add(definitions, CharacterFeatureIds.FaceChiseledCheeks, CharacterFeatureSlot.Face, BuddyCosmeticAnchorId.HeadFront, BuddyCosmeticRenderLayer.FaceDetail, BuddyCosmeticVisualKind.FaceChiseledCheeks);
        Add(definitions, CharacterFeatureIds.FaceFreckles, CharacterFeatureSlot.Face, BuddyCosmeticAnchorId.HeadFront, BuddyCosmeticRenderLayer.FaceDetail, BuddyCosmeticVisualKind.FaceFreckles);
        Add(definitions, CharacterFeatureIds.FaceRosyCheeks, CharacterFeatureSlot.Face, BuddyCosmeticAnchorId.HeadFront, BuddyCosmeticRenderLayer.FaceDetail, BuddyCosmeticVisualKind.FaceRosyCheeks);
        Add(definitions, CharacterFeatureIds.FaceStubble, CharacterFeatureSlot.Face, BuddyCosmeticAnchorId.HeadFront, BuddyCosmeticRenderLayer.FaceDetail, BuddyCosmeticVisualKind.FaceStubble);
        Add(definitions, CharacterFeatureIds.HairElderTufts, CharacterFeatureSlot.Hair, BuddyCosmeticAnchorId.HeadCrown, BuddyCosmeticRenderLayer.Hair, BuddyCosmeticVisualKind.HairElderTufts);
        Add(definitions, CharacterFeatureIds.NosePointedBeak, CharacterFeatureSlot.Nose, BuddyCosmeticAnchorId.HeadFront, BuddyCosmeticRenderLayer.FaceDetail, BuddyCosmeticVisualKind.NosePointedBeak);
        Add(definitions, CharacterFeatureIds.NoseWideFlat, CharacterFeatureSlot.Nose, BuddyCosmeticAnchorId.HeadFront, BuddyCosmeticRenderLayer.FaceDetail, BuddyCosmeticVisualKind.NoseWideFlat);
        Add(definitions, CharacterFeatureIds.NoseUpturned, CharacterFeatureSlot.Nose, BuddyCosmeticAnchorId.HeadFront, BuddyCosmeticRenderLayer.FaceDetail, BuddyCosmeticVisualKind.NoseUpturned);
        Add(definitions, CharacterFeatureIds.NoseHooked, CharacterFeatureSlot.Nose, BuddyCosmeticAnchorId.HeadFront, BuddyCosmeticRenderLayer.FaceDetail, BuddyCosmeticVisualKind.NoseHooked);
        Add(definitions, CharacterFeatureIds.NoseTinyDot, CharacterFeatureSlot.Nose, BuddyCosmeticAnchorId.HeadFront, BuddyCosmeticRenderLayer.FaceDetail, BuddyCosmeticVisualKind.NoseTinyDot);
        Add(definitions, CharacterFeatureIds.EarsElf, CharacterFeatureSlot.Ears, BuddyCosmeticAnchorId.LeftEar, BuddyCosmeticRenderLayer.FaceDetail, BuddyCosmeticVisualKind.EarsElf, BuddyCosmeticAnchorId.RightEar);
        Add(definitions, CharacterFeatureIds.GlassesSquareFrames, CharacterFeatureSlot.Glasses, BuddyCosmeticAnchorId.EyeGroup, BuddyCosmeticRenderLayer.Glasses, BuddyCosmeticVisualKind.GlassesSquareFrames);
        Add(definitions, CharacterFeatureIds.GlassesCatEye, CharacterFeatureSlot.Glasses, BuddyCosmeticAnchorId.EyeGroup, BuddyCosmeticRenderLayer.Glasses, BuddyCosmeticVisualKind.GlassesCatEye);
        Add(definitions, CharacterFeatureIds.GlassesAviators, CharacterFeatureSlot.Glasses, BuddyCosmeticAnchorId.EyeGroup, BuddyCosmeticRenderLayer.Glasses, BuddyCosmeticVisualKind.GlassesAviators);
        Add(definitions, CharacterFeatureIds.GlassesHalfMoon, CharacterFeatureSlot.Glasses, BuddyCosmeticAnchorId.EyeGroup, BuddyCosmeticRenderLayer.Glasses, BuddyCosmeticVisualKind.GlassesHalfMoon);
        Add(definitions, CharacterFeatureIds.GlassesVisor, CharacterFeatureSlot.Glasses, BuddyCosmeticAnchorId.EyeGroup, BuddyCosmeticRenderLayer.Glasses, BuddyCosmeticVisualKind.GlassesVisor);
        Add(definitions, CharacterFeatureIds.HeadwearBallCap, CharacterFeatureSlot.Headwear, BuddyCosmeticAnchorId.HeadCrown, BuddyCosmeticRenderLayer.Headwear, BuddyCosmeticVisualKind.HeadwearBallCap);
        Add(definitions, CharacterFeatureIds.HeadwearSunflowerHat, CharacterFeatureSlot.Headwear, BuddyCosmeticAnchorId.HeadCrown, BuddyCosmeticRenderLayer.Headwear, BuddyCosmeticVisualKind.HeadwearSunflowerHat);
        Add(definitions, CharacterFeatureIds.HeadwearFedora, CharacterFeatureSlot.Headwear, BuddyCosmeticAnchorId.HeadCrown, BuddyCosmeticRenderLayer.Headwear, BuddyCosmeticVisualKind.HeadwearFedora);
        if (generated is not null)
            foreach (GeneratedBuddyCosmeticResource resource in generated.Entries) AddGenerated(definitions, resource);
        foreach (BuddyCosmeticVisualDefinition definition in definitions.Values)
            if (!_cosmetics.Contains(definition.Slot, definition.CosmeticId)) throw new InvalidOperationException($"Visual '{definition.CosmeticId}' is not in its domain category.");
        foreach (CharacterFeatureSlot slot in new[] { CharacterFeatureSlot.Hair, CharacterFeatureSlot.Face, CharacterFeatureSlot.Nose, CharacterFeatureSlot.Ears, CharacterFeatureSlot.Glasses, CharacterFeatureSlot.Headwear, CharacterFeatureSlot.Tops, CharacterFeatureSlot.Shoes })
            foreach (string id in _cosmetics.GetIds(slot))
                if (!definitions.ContainsKey(id)) throw new InvalidOperationException($"Missing trusted visual registration for '{id}'.");
        _definitions = new ReadOnlyDictionary<string, BuddyCosmeticVisualDefinition>(definitions);
    }

    public IEnumerable<BuddyCosmeticVisualDefinition> Definitions => _definitions.Values;
    public BuddyCosmeticVisualDefinition Resolve(CharacterFeatureSlot slot, string cosmeticId, out bool usedFallback)
    {
        if (_definitions.TryGetValue(cosmeticId, out BuddyCosmeticVisualDefinition? definition) && definition.Slot == slot) { usedFallback = false; return definition; }
        usedFallback = true;
        return _definitions[_cosmetics.GetDefaultId(slot)];
    }

    private static void AddGenerated(IDictionary<string, BuddyCosmeticVisualDefinition> definitions, GeneratedBuddyCosmeticResource resource)
    {
        BuddyCosmeticVisualDefinition definition = resource.Slot switch
        {
            CharacterFeatureSlot.Glasses => new(resource.FeatureId, resource.Slot, BuddyCosmeticAnchorId.EyeGroup, BuddyCosmeticRenderLayer.Glasses, BuddyCosmeticVisualKind.GeneratedAsset, GeneratedResource: resource),
            CharacterFeatureSlot.Tops => new(resource.FeatureId, resource.Slot, BuddyCosmeticAnchorId.TorsoBody, BuddyCosmeticRenderLayer.Top, BuddyCosmeticVisualKind.GeneratedAsset, GeneratedResource: resource, ApplicationMode: BuddyCosmeticApplicationMode.PartReplacement),
            CharacterFeatureSlot.Shoes => new(resource.FeatureId, resource.Slot, BuddyCosmeticAnchorId.LeftFoot, BuddyCosmeticRenderLayer.Top, BuddyCosmeticVisualKind.GeneratedAsset, BuddyCosmeticAnchorId.RightFoot, resource, BuddyCosmeticApplicationMode.PairedPartReplacement),
            _ => throw new InvalidOperationException($"Asset Forge does not support generated {resource.Slot} visuals yet."),
        };
        if (!definitions.TryAdd(resource.FeatureId, definition)) throw new InvalidOperationException($"Duplicate trusted cosmetic visual '{resource.FeatureId}'.");
    }

    private static void Add(IDictionary<string, BuddyCosmeticVisualDefinition> definitions, string id, CharacterFeatureSlot slot, BuddyCosmeticAnchorId anchor, BuddyCosmeticRenderLayer layer, BuddyCosmeticVisualKind kind, BuddyCosmeticAnchorId? secondaryAnchor = null, GeneratedBuddyCosmeticResource? generatedResource = null, BuddyCosmeticApplicationMode applicationMode = BuddyCosmeticApplicationMode.Attachment)
    {
        if (!definitions.TryAdd(id, new BuddyCosmeticVisualDefinition(id, slot, anchor, layer, kind, secondaryAnchor, generatedResource, applicationMode))) throw new InvalidOperationException($"Duplicate trusted cosmetic visual '{id}'.");
    }
}
