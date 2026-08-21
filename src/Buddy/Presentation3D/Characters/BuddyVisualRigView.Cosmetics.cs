using System;
using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D.Characters;
using DesktopBuddy.CharacterEditor.BuddyStudio;
using DesktopBuddy.Domain.Characters;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

public partial class BuddyVisualRigView
{
    private readonly Dictionary<BuddyCosmeticAnchorId, Node3D> _cosmeticAnchors = [];
    private BuddyCosmeticVisualCatalog? _cosmeticVisualCatalog;
    private Node3D? _faceVisual;
    private Node3D? _hairVisual;
    private Node3D? _noseVisual;
    private Node3D? _earsVisual;
    private Node3D? _rightEarsVisual;
    private Node3D? _glassesVisual;
    private Node3D? _headwearVisual;
    private Node3D? _topVisual;
    private Node3D? _shoesVisual;
    private Node3D? _rightShoesVisual;
    private CompiledFeatureAppearance? _faceAppearance;
    private CompiledFeatureAppearance? _hairAppearance;
    private CompiledFeatureAppearance? _noseAppearance;
    private CompiledFeatureAppearance? _earsAppearance;
    private CompiledFeatureAppearance? _glassesAppearance;
    private CompiledFeatureAppearance? _headwearAppearance;
    private CompiledFeatureAppearance? _topAppearance;
    private CompiledFeatureAppearance? _shoesAppearance;
    private bool _torsoVisualReplaced;
    private bool _leftFootVisualReplaced;
    private bool _rightFootVisualReplaced;

    public Node3D GetCosmeticAnchor(BuddyCosmeticAnchorId anchor)
    {
        EnsureInitialized();
        EnsureCosmeticAnchors();
        return _cosmeticAnchors[anchor];
    }

    public Node3D? GetCosmeticVisual(CharacterFeatureSlot slot) => slot switch
    {
        CharacterFeatureSlot.Face => _faceVisual,
        CharacterFeatureSlot.Hair => _hairVisual,
        CharacterFeatureSlot.Nose => _noseVisual,
        CharacterFeatureSlot.Ears => _earsVisual,
        CharacterFeatureSlot.Glasses => _glassesVisual,
        CharacterFeatureSlot.Headwear => _headwearVisual,
        CharacterFeatureSlot.Tops => _topVisual,
        CharacterFeatureSlot.Shoes => _shoesVisual,
        _ => null,
    };

    public Node3D? GetPairedCosmeticVisual(CharacterFeatureSlot slot) => slot switch
    {
        CharacterFeatureSlot.Ears => _rightEarsVisual,
        CharacterFeatureSlot.Shoes => _rightShoesVisual,
        _ => null,
    };

    public bool IsPartVisualReplaced(BuddyPartId partId) => partId switch
    {
        BuddyPartId.Torso => _torsoVisualReplaced,
        BuddyPartId.LeftFoot => _leftFootVisualReplaced,
        BuddyPartId.RightFoot => _rightFootVisualReplaced,
        _ => false,
    };

    private void ApplyCosmeticAppearance(CompiledCharacterAppearance appearance)
    {
        EnsureCosmeticAnchors();
        _cosmeticVisualCatalog ??= new BuddyCosmeticVisualCatalog();

        UpdateVisual(CharacterFeatureSlot.Face, appearance.Face, ref _faceAppearance, ref _faceVisual);
        UpdateVisual(CharacterFeatureSlot.Hair, appearance.Hair, ref _hairAppearance, ref _hairVisual);
        UpdateVisual(CharacterFeatureSlot.Nose, appearance.Nose, ref _noseAppearance, ref _noseVisual);
        UpdatePairedVisual(CharacterFeatureSlot.Ears, appearance.Ears, ref _earsAppearance, ref _earsVisual, ref _rightEarsVisual);
        UpdateVisual(CharacterFeatureSlot.Glasses, appearance.Glasses, ref _glassesAppearance, ref _glassesVisual);
        UpdateVisual(CharacterFeatureSlot.Headwear, appearance.Headwear, ref _headwearAppearance, ref _headwearVisual);
        UpdateVisual(CharacterFeatureSlot.Tops, appearance.Tops, ref _topAppearance, ref _topVisual);
        UpdatePairedVisual(CharacterFeatureSlot.Shoes, appearance.Shoes, ref _shoesAppearance, ref _shoesVisual, ref _rightShoesVisual);

        bool hidesHair = BuddyGeneratedCosmeticRegistry.Current.FeatureCatalog.TryGetDefinition(
            appearance.Headwear.ResolvedFeatureId,
            out CosmeticDefinition headwear) && headwear.HidesHair;
        if (GodotObject.IsInstanceValid(_hairVisual)) _hairVisual!.Visible = !hidesHair;

        BuddyCosmeticVisualDefinition top = _cosmeticVisualCatalog.Resolve(CharacterFeatureSlot.Tops, appearance.Tops.ResolvedFeatureId, out _);
        BuddyCosmeticVisualDefinition shoes = _cosmeticVisualCatalog.Resolve(CharacterFeatureSlot.Shoes, appearance.Shoes.ResolvedFeatureId, out _);
        SetPartReplacementState(BuddyPartId.Torso,
            top.ApplicationMode == BuddyCosmeticApplicationMode.PartReplacement && top.Kind != BuddyCosmeticVisualKind.None);
        bool replaceFeet = shoes.ApplicationMode == BuddyCosmeticApplicationMode.PairedPartReplacement && shoes.Kind != BuddyCosmeticVisualKind.None;
        SetPartReplacementState(BuddyPartId.LeftFoot, replaceFeet);
        SetPartReplacementState(BuddyPartId.RightFoot, replaceFeet);
    }

    private void ClearCosmeticAppearance()
    {
        RemoveVisual(ref _faceVisual);
        RemoveVisual(ref _hairVisual);
        RemoveVisual(ref _noseVisual);
        RemoveVisual(ref _earsVisual);
        RemoveVisual(ref _rightEarsVisual);
        RemoveVisual(ref _glassesVisual);
        RemoveVisual(ref _headwearVisual);
        RemoveVisual(ref _topVisual);
        RemoveVisual(ref _shoesVisual);
        RemoveVisual(ref _rightShoesVisual);
        _faceAppearance = null;
        _hairAppearance = null;
        _noseAppearance = null;
        _earsAppearance = null;
        _glassesAppearance = null;
        _headwearAppearance = null;
        _topAppearance = null;
        _shoesAppearance = null;
        SetPartReplacementState(BuddyPartId.Torso, false);
        SetPartReplacementState(BuddyPartId.LeftFoot, false);
        SetPartReplacementState(BuddyPartId.RightFoot, false);
    }

    private void SetPartReplacementState(BuddyPartId partId, bool replaced)
    {
        int index = (int)partId;
        switch (partId)
        {
            case BuddyPartId.Torso: _torsoVisualReplaced = replaced; break;
            case BuddyPartId.LeftFoot: _leftFootVisualReplaced = replaced; break;
            case BuddyPartId.RightFoot: _rightFootVisualReplaced = replaced; break;
            default: throw new ArgumentOutOfRangeException(nameof(partId), partId, "Only torso/foot visual replacements are supported.");
        }
        if (GodotObject.IsInstanceValid(_partMeshes[index])) _partMeshes[index].Visible = !replaced;
        if (GodotObject.IsInstanceValid(_partOutlines[index])) _partOutlines[index].Visible = !replaced;
        if (_paintLayers[index] is MeshInstance3D paint && GodotObject.IsInstanceValid(paint))
            paint.Visible = !replaced && _surfaceUnderlays[index] is not null;
    }

    private void UpdateVisual(CharacterFeatureSlot slot, in CompiledFeatureAppearance appearance, ref CompiledFeatureAppearance? activeAppearance, ref Node3D? activeVisual)
    {
        if (activeAppearance == appearance) return;
        RemoveVisual(ref activeVisual);
        activeAppearance = appearance;
        BuddyCosmeticVisualDefinition visual = _cosmeticVisualCatalog!.Resolve(slot, appearance.ResolvedFeatureId, out _);
        if (visual.Kind == BuddyCosmeticVisualKind.None) return;
        Node3D anchor = _cosmeticAnchors[visual.Anchor];
        activeVisual = new Node3D { Name = $"Cosmetic_{slot}" };
        anchor.AddChild(activeVisual);
        BuildVisual(activeVisual, null, visual, appearance);
    }

    private void UpdatePairedVisual(CharacterFeatureSlot slot, in CompiledFeatureAppearance appearance, ref CompiledFeatureAppearance? activeAppearance, ref Node3D? primaryVisual, ref Node3D? secondaryVisual)
    {
        if (activeAppearance == appearance) return;
        RemoveVisual(ref primaryVisual);
        RemoveVisual(ref secondaryVisual);
        activeAppearance = appearance;
        BuddyCosmeticVisualDefinition visual = _cosmeticVisualCatalog!.Resolve(slot, appearance.ResolvedFeatureId, out _);
        if (visual.Kind == BuddyCosmeticVisualKind.None) return;
        if (visual.SecondaryAnchor is not BuddyCosmeticAnchorId secondaryAnchor)
            throw new InvalidOperationException($"Paired cosmetic '{visual.CosmeticId}' has no secondary anchor.");
        primaryVisual = CreateVisualRoot(slot, visual.Anchor, "Left");
        secondaryVisual = CreateVisualRoot(slot, secondaryAnchor, "Right");
        BuildVisual(primaryVisual, secondaryVisual, visual, appearance);
    }

    private Node3D CreateVisualRoot(CharacterFeatureSlot slot, BuddyCosmeticAnchorId anchor, string suffix)
    {
        var root = new Node3D { Name = $"Cosmetic_{slot}_{suffix}" };
        _cosmeticAnchors[anchor].AddChild(root);
        return root;
    }

    private void BuildVisual(Node3D root, Node3D? pairedRoot, BuddyCosmeticVisualDefinition visual, in CompiledFeatureAppearance appearance)
    {
        Color color = ToGodotColor(appearance.Color);
        float headRadius = PartMeshRadius(BuddyPartId.Head);

        // The crown anchor sits 0.48 radii in FRONT of the head centre, because it is where
        // hair hangs from and hair is built with matching negative offsets. Hats were built at
        // the anchor itself, so every one of them rode forward onto the buddy's face with its
        // brim across his eyes (owner report 2026-08-21). Undo the anchor's lean here, once,
        // and lift them onto the crown: the hats then sit centred on the head by construction
        // rather than each one carrying its own correction.
        if (visual.Slot == CharacterFeatureSlot.Headwear)
            root.Position = new Vector3(0.0f, 0.14f * headRadius, -0.48f * headRadius);

        switch (visual.Kind)
        {
            case BuddyCosmeticVisualKind.HairShortSweep:
                AddEllipsoid(root, "SweepLeft", new Vector3(-0.28f, 0.10f, 0), new Vector3(0.78f, 0.30f, 0.34f), headRadius, color, visual.Layer);
                AddEllipsoid(root, "SweepCenter", new Vector3(0.10f, 0.22f, 0.02f), new Vector3(0.92f, 0.34f, 0.38f), headRadius, color, visual.Layer);
                AddEllipsoid(root, "SweepTip", new Vector3(0.48f, 0.02f, 0.01f), new Vector3(0.52f, 0.22f, 0.30f), headRadius, color, visual.Layer);
                break;
            case BuddyCosmeticVisualKind.HairBobBangs:
                // Symmetrical ear-length bob: skull cap, a straight fringe band across the
                // forehead, side curtains down to the ear line and a small outward flick.
                AddEllipsoid(root, "BobCap", new Vector3(0, -0.17f, -0.48f), new Vector3(0.92f, 0.62f, 0.95f), headRadius, color, visual.Layer, hemisphere: true);
                AddEllipsoid(root, "BobBack", new Vector3(0, -0.62f, -0.80f), new Vector3(0.92f, 0.80f, 0.55f), headRadius, color, visual.Layer);
                AddEllipsoid(root, "BobFringe", new Vector3(0, -0.17f, 0.30f), new Vector3(0.82f, 0.22f, 0.30f), headRadius, color, visual.Layer);
                foreach (float bobSide in new[] { -1.0f, 1.0f })
                {
                    AddEllipsoid(root, $"BobSide{bobSide}", new Vector3(bobSide * 0.80f, -0.80f, -0.28f), new Vector3(0.34f, 0.62f, 0.74f), headRadius, color, visual.Layer);
                    AddEllipsoid(root, $"BobFlick{bobSide}", new Vector3(bobSide * 0.96f, -1.16f, -0.28f), new Vector3(0.30f, 0.22f, 0.46f), headRadius, color, visual.Layer);
                }
                break;
            case BuddyCosmeticVisualKind.HairBuzzCut:
                AddEllipsoid(root, "BuzzCap", new Vector3(0, -0.14f, -0.48f), new Vector3(0.88f, 0.52f, 0.88f), headRadius, color, visual.Layer, hemisphere: true);
                break;
            case BuddyCosmeticVisualKind.NoseButton:
                ApplyFeatureTransform(root, appearance.Transform, headRadius);
                AddEllipsoid(root, "Button", Vector3.Zero, new Vector3(0.24f, 0.18f, 0.14f), headRadius, color, visual.Layer);
                break;
            case BuddyCosmeticVisualKind.NoseTriangle:
                ApplyFeatureTransform(root, appearance.Transform, headRadius);
                AddPrism(root, "Triangle", new Vector3(0, -0.06f * headRadius, 0), new Vector3(0.30f, 0.30f, 0.16f) * headRadius, 180f, color, visual.Layer);
                break;
            case BuddyCosmeticVisualKind.NoseBroadOval:
                ApplyFeatureTransform(root, appearance.Transform, headRadius);
                AddEllipsoid(root, "BroadOval", Vector3.Zero, new Vector3(0.42f, 0.16f, 0.12f), headRadius, color, visual.Layer);
                break;
            case BuddyCosmeticVisualKind.EarsRoundTabs:
                if (pairedRoot is null) throw new InvalidOperationException("Ear visuals require both trusted ear anchors.");
                ApplyFeatureTransform(root, appearance.Transform, headRadius);
                ApplyFeatureTransform(pairedRoot, appearance.Transform, headRadius);
                AddEllipsoid(root, "LeftTab", Vector3.Zero, new Vector3(0.28f, 0.48f, 0.22f), headRadius, color, visual.Layer);
                AddEllipsoid(pairedRoot, "RightTab", Vector3.Zero, new Vector3(0.28f, 0.48f, 0.22f), headRadius, color, visual.Layer);
                break;
            case BuddyCosmeticVisualKind.EarsPointedTips:
                if (pairedRoot is null) throw new InvalidOperationException("Ear visuals require both trusted ear anchors.");
                ApplyFeatureTransform(root, appearance.Transform, headRadius);
                ApplyFeatureTransform(pairedRoot, appearance.Transform, headRadius);
                AddCylinder(root, "LeftTip", new Vector3(-0.10f * headRadius, 0, 0), 0.22f * headRadius, 0.50f * headRadius, 90f, color, visual.Layer);
                AddCylinder(pairedRoot, "RightTip", new Vector3(0.10f * headRadius, 0, 0), 0.22f * headRadius, 0.50f * headRadius, -90f, color, visual.Layer);
                break;
            case BuddyCosmeticVisualKind.EarsFlatDiscs:
                if (pairedRoot is null) throw new InvalidOperationException("Ear visuals require both trusted ear anchors.");
                ApplyFeatureTransform(root, appearance.Transform, headRadius);
                ApplyFeatureTransform(pairedRoot, appearance.Transform, headRadius);
                AddCylinder(root, "LeftDisc", new Vector3(-0.06f * headRadius, 0, 0), 0.42f * headRadius, 0.10f * headRadius, 90f, color, visual.Layer, topRadius: 0.42f * headRadius);
                AddCylinder(pairedRoot, "RightDisc", new Vector3(0.06f * headRadius, 0, 0), 0.42f * headRadius, 0.10f * headRadius, -90f, color, visual.Layer, topRadius: 0.42f * headRadius);
                break;
            case BuddyCosmeticVisualKind.WorkClassicGlasses:
                ApplyFeatureTransform(root, appearance.Transform, headRadius);
                AddGlasses(root, headRadius, color, visual.Layer);
                break;
            case BuddyCosmeticVisualKind.GlassesRoundWire:
                ApplyFeatureTransform(root, appearance.Transform, headRadius);
                foreach (float wireSide in new[] { -1.0f, 1.0f })
                    AddRing(root, $"WireLens{wireSide}", new Vector3(wireSide * 0.38f * headRadius, 0.10f * headRadius, 0), 0.20f * headRadius, 0.26f * headRadius, color, visual.Layer);
                AddBox(root, "WireBridge", new Vector3(0, 0.10f * headRadius, 0), new Vector3(headRadius * 0.26f, headRadius * 0.05f, headRadius * 0.05f), color, visual.Layer);
                break;
            case BuddyCosmeticVisualKind.GlassesShades:
                ApplyFeatureTransform(root, appearance.Transform, headRadius);
                foreach (float shadeSide in new[] { -1.0f, 1.0f })
                    AddBox(root, $"ShadeLens{shadeSide}", new Vector3(shadeSide * 0.34f * headRadius, 0.08f * headRadius, 0), new Vector3(headRadius * 0.56f, headRadius * 0.32f, headRadius * 0.07f), color, visual.Layer);
                AddBox(root, "ShadeBar", new Vector3(0, 0.28f * headRadius, 0), new Vector3(headRadius * 1.28f, headRadius * 0.09f, headRadius * 0.07f), color, visual.Layer);
                break;
            case BuddyCosmeticVisualKind.GeneratedAsset:
                if (visual.Slot == CharacterFeatureSlot.Glasses)
                {
                    ApplyFeatureTransform(root, appearance.Transform, headRadius);
                    AddGeneratedAsset(root, visual, headRadius, false, color);
                }
                else if (visual.Slot == CharacterFeatureSlot.Tops)
                {
                    AddGeneratedAsset(root, visual, PartMeshRadius(BuddyPartId.Torso), false, color);
                }
                else if (visual.Slot == CharacterFeatureSlot.Shoes)
                {
                    if (pairedRoot is null) throw new InvalidOperationException("Generated Shoes require both trusted foot anchors.");
                    float generatedFootRadius = PartMeshRadius(BuddyPartId.LeftFoot);
                    AddGeneratedAsset(root, visual, generatedFootRadius, true, color);
                    AddGeneratedAsset(pairedRoot, visual, generatedFootRadius, false, color);
                }
                else throw new InvalidOperationException($"Unsupported generated slot {visual.Slot}.");
                break;
            case BuddyCosmeticVisualKind.HeadwearSoftCap:
                AddEllipsoid(root, "Crown", Vector3.Zero, new Vector3(1.05f, 0.42f, 0.58f), headRadius, color, visual.Layer);
                AddBox(root, "Brim", new Vector3(0.28f * headRadius, -0.18f * headRadius, 0.24f * headRadius), new Vector3(0.90f * headRadius, 0.12f * headRadius, 0.34f * headRadius), color, visual.Layer);
                break;
            case BuddyCosmeticVisualKind.HeadwearKnitBeanie:
                AddEllipsoid(root, "BeanieCrown", Vector3.Zero, new Vector3(1.08f, 0.54f, 0.66f), headRadius, color, visual.Layer);
                AddEllipsoid(root, "BeanieCuff", new Vector3(0, -0.04f, 0), new Vector3(1.12f, 0.18f, 0.70f), headRadius, color.Lightened(0.10f), visual.Layer);
                AddEllipsoid(root, "BeaniePom", new Vector3(0, 0.58f, -0.04f), new Vector3(0.22f, 0.22f, 0.22f), headRadius, color.Lightened(0.10f), visual.Layer);
                break;
            case BuddyCosmeticVisualKind.HeadwearWideBrim:
                AddEllipsoid(root, "BrimCrown", new Vector3(0, -0.06f, 0), new Vector3(0.88f, 0.46f, 0.54f), headRadius, color, visual.Layer);
                AddCylinder(root, "WideBrim", new Vector3(0, -0.26f * headRadius, 0), 1.24f * headRadius, 0.09f * headRadius, 0f, color, visual.Layer, topRadius: 1.24f * headRadius);
                break;
            case BuddyCosmeticVisualKind.TopUtilityBib:
                float torsoRadius = PartMeshRadius(BuddyPartId.Torso);
                AddEllipsoid(root, "UtilityTorso", Vector3.Zero, new Vector3(1.04f, 1.02f, 0.78f), torsoRadius, color, visual.Layer);
                AddBox(root, "Bib", new Vector3(0, 0, torsoRadius * 0.72f), new Vector3(torsoRadius * 1.02f, torsoRadius * 0.80f, torsoRadius * 0.10f), color.Lightened(0.08f), visual.Layer);
                break;
            case BuddyCosmeticVisualKind.ShoesSoftSteps:
                if (pairedRoot is null) throw new InvalidOperationException("Shoe visuals require both trusted foot anchors.");
                float footRadius = PartMeshRadius(BuddyPartId.LeftFoot);
                Vector3 shoePosition = new(0, -footRadius * 0.08f, footRadius * 0.14f);
                Vector3 shoeScale = new(1.08f, 0.72f, 1.18f);
                AddEllipsoid(root, "LeftStep", shoePosition / footRadius, shoeScale, footRadius, color, visual.Layer);
                AddEllipsoid(pairedRoot, "RightStep", shoePosition / footRadius, shoeScale, footRadius, color, visual.Layer);
                break;
            // ---- Second cosmetic wave (owner instruction 2026-08-21) --------------------
            // Face styles are marks on the skin, so they are laid out in the same normalized
            // space the drawn face uses and bent back onto the head's curve. The first pass
            // built them from straight boxes and one wide slab, which read as stickers stuck
            // on the face rather than as a crease or a beard (owner report 2026-08-21).
            case BuddyCosmeticVisualKind.FaceWrinkles:
                ApplyFeatureTransform(root, appearance.Transform, headRadius);
                // Two shallow bows across the forehead, the upper one shorter, the way a brow
                // furrow actually stacks.
                AddFaceArc(root, "BrowLow", new Vector2(0.0f, 0.30f), 0.30f, 0.10f, 18.0f, 162.0f, 0.040f, 11, headRadius, color, visual.Layer, taper: 0.55f);
                AddFaceArc(root, "BrowHigh", new Vector2(0.0f, 0.43f), 0.24f, 0.08f, 22.0f, 158.0f, 0.036f, 9, headRadius, color, visual.Layer, taper: 0.6f);
                foreach (float wrinkleSide in new[] { -1.0f, 1.0f })
                {
                    // Crow's feet fan from the outer eye corner; the middle one is the longest.
                    for (int ray = -1; ray <= 1; ray++)
                    {
                        float spread = ray * 0.075f;
                        float length = ray == 0 ? 0.15f : 0.11f;
                        AddFaceLine(root, $"Crow{wrinkleSide}_{ray}",
                            new Vector2(wrinkleSide * 0.38f, 0.13f + spread),
                            new Vector2(wrinkleSide * (0.38f + length), 0.13f + (spread * 1.9f)),
                            0.034f, 5, headRadius, color, visual.Layer, taper: 0.6f);
                    }

                    // The nasolabial fold: nose wing down past the mouth corner.
                    AddFaceArc(root, $"Fold{wrinkleSide}", new Vector2(wrinkleSide * 0.02f, -0.12f), wrinkleSide * 0.26f, 0.24f, 70.0f, -40.0f, 0.038f, 8, headRadius, color, visual.Layer, taper: 0.5f);
                }
                break;
            case BuddyCosmeticVisualKind.FaceChiseledCheeks:
                ApplyFeatureTransform(root, appearance.Transform, headRadius);
                foreach (float cheekSide in new[] { -1.0f, 1.0f })
                {
                    // One long hollow curving in under the cheekbone, and a short jaw accent
                    // below it. Together they read as bone rather than as two straight sticks.
                    AddFaceArc(root, $"Hollow{cheekSide}", new Vector2(cheekSide * 0.14f, -0.02f), cheekSide * 0.34f, 0.32f, 62.0f, -52.0f, 0.046f, 10, headRadius, color, visual.Layer, taper: 0.45f);
                    AddFaceArc(root, $"Jaw{cheekSide}", new Vector2(cheekSide * 0.10f, -0.30f), cheekSide * 0.30f, 0.22f, 8.0f, -62.0f, 0.040f, 7, headRadius, color, visual.Layer, taper: 0.55f);
                }
                break;
            case BuddyCosmeticVisualKind.FaceFreckles:
                ApplyFeatureTransform(root, appearance.Transform, headRadius);
                foreach (float freckleSide in new[] { -1.0f, 1.0f })
                {
                    // Scattered across the cheek and over the bridge, in two loose rows.
                    AddFaceDot(root, $"FreckleA{freckleSide}", new Vector2(freckleSide * 0.30f, -0.13f), 0.045f, headRadius, color, visual.Layer);
                    AddFaceDot(root, $"FreckleB{freckleSide}", new Vector2(freckleSide * 0.43f, -0.05f), 0.042f, headRadius, color, visual.Layer);
                    AddFaceDot(root, $"FreckleC{freckleSide}", new Vector2(freckleSide * 0.38f, -0.22f), 0.036f, headRadius, color, visual.Layer);
                    AddFaceDot(root, $"FreckleD{freckleSide}", new Vector2(freckleSide * 0.52f, -0.16f), 0.033f, headRadius, color, visual.Layer);
                    AddFaceDot(root, $"FreckleE{freckleSide}", new Vector2(freckleSide * 0.13f, -0.09f), 0.030f, headRadius, color, visual.Layer);
                }
                break;
            case BuddyCosmeticVisualKind.FaceRosyCheeks:
                ApplyFeatureTransform(root, appearance.Transform, headRadius);
                foreach (float rosySide in new[] { -1.0f, 1.0f })
                {
                    AddFaceBlob(root, $"Rosy{rosySide}", new Vector2(rosySide * 0.38f, -0.11f), new Vector2(0.25f, 0.17f), headRadius, color, visual.Layer);
                    // Three little hatch strokes over the blush, the way a Mii's cheeks are drawn.
                    for (int hatch = -1; hatch <= 1; hatch++)
                    {
                        AddFaceLine(root, $"RosyHatch{rosySide}_{hatch}",
                            new Vector2((rosySide * 0.38f) + (hatch * 0.10f), -0.20f),
                            new Vector2((rosySide * 0.38f) + (hatch * 0.10f) + (rosySide * 0.05f), -0.02f),
                            0.026f, 4, headRadius, color.Lightened(0.35f), visual.Layer, taper: 0.5f);
                    }
                }
                break;
            case BuddyCosmeticVisualKind.FaceStubble:
                ApplyFeatureTransform(root, appearance.Transform, headRadius);
                // A beard follows the jaw. The first pass was one flat slab across the middle
                // of the face, which buried the mouth; this is three arcs hugging the lower
                // head outline, tapering up towards the ears, plus a chin patch and a
                // moustache clear of the mouth.
                AddFaceArc(root, "BeardOuter", Vector2.Zero, 0.74f, 0.70f, 202.0f, 338.0f, 0.115f, 15, headRadius, color, visual.Layer, taper: 0.55f);
                AddFaceArc(root, "BeardMid", Vector2.Zero, 0.60f, 0.58f, 210.0f, 330.0f, 0.105f, 13, headRadius, color, visual.Layer, taper: 0.5f);
                AddFaceArc(root, "BeardInner", Vector2.Zero, 0.44f, 0.46f, 224.0f, 316.0f, 0.090f, 9, headRadius, color, visual.Layer, taper: 0.45f);
                AddFaceBlob(root, "BeardChin", new Vector2(0.0f, -0.50f), new Vector2(0.20f, 0.13f), headRadius, color, visual.Layer);
                foreach (float lipSide in new[] { -1.0f, 1.0f })
                {
                    AddFaceArc(root, $"Moustache{lipSide}", new Vector2(lipSide * 0.09f, -0.15f), lipSide * 0.11f, 0.07f, 120.0f, -20.0f, 0.055f, 5, headRadius, color, visual.Layer, taper: 0.4f);
                }
                break;

            case BuddyCosmeticVisualKind.HairElderTufts:
                // Bald on top on purpose: two side tufts and a band round the back, the classic
                // horseshoe. Nothing covers the crown, so the head shows through.
                foreach (float tuftSide in new[] { -1.0f, 1.0f })
                {
                    AddEllipsoid(root, $"ElderTuft{tuftSide}", new Vector3(tuftSide * 0.86f, -0.62f, -0.46f), new Vector3(0.26f, 0.34f, 0.58f), headRadius, color, visual.Layer);
                    AddEllipsoid(root, $"ElderFlick{tuftSide}", new Vector3(tuftSide * 1.00f, -0.86f, -0.42f), new Vector3(0.20f, 0.16f, 0.36f), headRadius, color, visual.Layer);
                }
                AddEllipsoid(root, "ElderBack", new Vector3(0, -0.70f, -0.94f), new Vector3(0.74f, 0.30f, 0.34f), headRadius, color, visual.Layer);
                break;

            case BuddyCosmeticVisualKind.NosePointedBeak:
                ApplyFeatureTransform(root, appearance.Transform, headRadius);
                AddCylinder(root, "Beak", new Vector3(0, -0.06f * headRadius, 0), 0.16f * headRadius, 0.50f * headRadius, 180f, color, visual.Layer);
                break;
            case BuddyCosmeticVisualKind.NoseWideFlat:
                ApplyFeatureTransform(root, appearance.Transform, headRadius);
                AddEllipsoid(root, "WideFlat", Vector3.Zero, new Vector3(0.54f, 0.12f, 0.09f), headRadius, color, visual.Layer);
                break;
            case BuddyCosmeticVisualKind.NoseUpturned:
                ApplyFeatureTransform(root, appearance.Transform, headRadius);
                AddEllipsoid(root, "UpturnedBridge", new Vector3(0, -0.02f, 0), new Vector3(0.26f, 0.20f, 0.14f), headRadius, color, visual.Layer);
                AddEllipsoid(root, "UpturnedTip", new Vector3(0, 0.11f, 0.05f), new Vector3(0.19f, 0.12f, 0.12f), headRadius, color, visual.Layer);
                break;
            case BuddyCosmeticVisualKind.NoseHooked:
                ApplyFeatureTransform(root, appearance.Transform, headRadius);
                AddEllipsoid(root, "HookBridge", new Vector3(0, 0.20f, 0), new Vector3(0.14f, 0.26f, 0.10f), headRadius, color, visual.Layer);
                AddEllipsoid(root, "HookMid", new Vector3(0, 0.02f, 0.03f), new Vector3(0.18f, 0.24f, 0.14f), headRadius, color, visual.Layer);
                AddEllipsoid(root, "HookTip", new Vector3(0, -0.16f, 0.02f), new Vector3(0.21f, 0.15f, 0.13f), headRadius, color, visual.Layer);
                break;
            case BuddyCosmeticVisualKind.NoseTinyDot:
                ApplyFeatureTransform(root, appearance.Transform, headRadius);
                AddEllipsoid(root, "TinyDot", Vector3.Zero, new Vector3(0.12f, 0.12f, 0.10f), headRadius, color, visual.Layer);
                break;

            case BuddyCosmeticVisualKind.EarsElf:
                if (pairedRoot is null) throw new InvalidOperationException("Ear visuals require both trusted ear anchors.");
                ApplyFeatureTransform(root, appearance.Transform, headRadius);
                ApplyFeatureTransform(pairedRoot, appearance.Transform, headRadius);
                AddEllipsoid(root, "LeftElfLobe", new Vector3(-0.04f, -0.10f, 0), new Vector3(0.22f, 0.32f, 0.20f), headRadius, color, visual.Layer);
                AddEllipsoid(pairedRoot, "RightElfLobe", new Vector3(0.04f, -0.10f, 0), new Vector3(0.22f, 0.32f, 0.20f), headRadius, color, visual.Layer);
                AddCylinder(root, "LeftElfTip", new Vector3(-0.12f * headRadius, 0.16f * headRadius, 0), 0.19f * headRadius, 0.72f * headRadius, 32f, color, visual.Layer);
                AddCylinder(pairedRoot, "RightElfTip", new Vector3(0.12f * headRadius, 0.16f * headRadius, 0), 0.19f * headRadius, 0.72f * headRadius, -32f, color, visual.Layer);
                break;

            case BuddyCosmeticVisualKind.GlassesSquareFrames:
                ApplyFeatureTransform(root, appearance.Transform, headRadius);
                foreach (float squareSide in new[] { -1.0f, 1.0f })
                {
                    float squareX = squareSide * 0.40f * headRadius;
                    AddBox(root, $"SquareTop{squareSide}", new Vector3(squareX, 0.28f * headRadius, 0), new Vector3(0.60f * headRadius, 0.09f * headRadius, 0.09f * headRadius), color, visual.Layer);
                    AddBox(root, $"SquareBottom{squareSide}", new Vector3(squareX, -0.24f * headRadius, 0), new Vector3(0.60f * headRadius, 0.09f * headRadius, 0.09f * headRadius), color, visual.Layer);
                    AddBox(root, $"SquareOuter{squareSide}", new Vector3(squareX + squareSide * 0.26f * headRadius, 0.02f * headRadius, 0), new Vector3(0.09f * headRadius, 0.60f * headRadius, 0.09f * headRadius), color, visual.Layer);
                    AddBox(root, $"SquareInner{squareSide}", new Vector3(squareX - squareSide * 0.26f * headRadius, 0.02f * headRadius, 0), new Vector3(0.09f * headRadius, 0.60f * headRadius, 0.09f * headRadius), color, visual.Layer);
                }
                AddBox(root, "SquareBridge", new Vector3(0, 0.02f * headRadius, 0), new Vector3(0.28f * headRadius, 0.08f * headRadius, 0.08f * headRadius), color, visual.Layer);
                break;
            case BuddyCosmeticVisualKind.GlassesCatEye:
                ApplyFeatureTransform(root, appearance.Transform, headRadius);
                foreach (float catSide in new[] { -1.0f, 1.0f })
                {
                    AddBox(root, $"CatLens{catSide}", new Vector3(catSide * 0.38f * headRadius, 0.06f * headRadius, 0), new Vector3(0.58f * headRadius, 0.34f * headRadius, 0.07f * headRadius), color, visual.Layer, catSide * -10f);
                    AddPrism(root, $"CatFlick{catSide}", new Vector3(catSide * 0.68f * headRadius, 0.24f * headRadius, 0), new Vector3(0.30f * headRadius, 0.26f * headRadius, 0.07f * headRadius), catSide * -55f, color, visual.Layer);
                }
                AddBox(root, "CatBridge", new Vector3(0, 0.10f * headRadius, 0), new Vector3(0.24f * headRadius, 0.07f * headRadius, 0.07f * headRadius), color, visual.Layer);
                break;
            case BuddyCosmeticVisualKind.GlassesAviators:
                ApplyFeatureTransform(root, appearance.Transform, headRadius);
                foreach (float aviatorSide in new[] { -1.0f, 1.0f })
                    AddEllipsoid(root, $"AviatorLens{aviatorSide}", new Vector3(aviatorSide * 0.38f, 0.02f, 0), new Vector3(0.34f, 0.30f, 0.05f), headRadius, color, visual.Layer);
                AddBox(root, "AviatorBar", new Vector3(0, 0.22f * headRadius, 0), new Vector3(1.16f * headRadius, 0.07f * headRadius, 0.07f * headRadius), color, visual.Layer);
                break;
            case BuddyCosmeticVisualKind.GlassesHalfMoon:
                // Reading glasses: rims below the eye line only, so the eyes look over the top.
                ApplyFeatureTransform(root, appearance.Transform, headRadius);
                foreach (float moonSide in new[] { -1.0f, 1.0f })
                {
                    AddBox(root, $"MoonRim{moonSide}", new Vector3(moonSide * 0.38f * headRadius, -0.10f * headRadius, 0), new Vector3(0.56f * headRadius, 0.20f * headRadius, 0.07f * headRadius), color, visual.Layer);
                    AddBox(root, $"MoonArm{moonSide}", new Vector3(moonSide * 0.66f * headRadius, 0.04f * headRadius, 0), new Vector3(0.07f * headRadius, 0.24f * headRadius, 0.07f * headRadius), color, visual.Layer);
                }
                AddBox(root, "MoonBridge", new Vector3(0, -0.02f * headRadius, 0), new Vector3(0.26f * headRadius, 0.07f * headRadius, 0.07f * headRadius), color, visual.Layer);
                break;
            case BuddyCosmeticVisualKind.GlassesVisor:
                ApplyFeatureTransform(root, appearance.Transform, headRadius);
                AddBox(root, "VisorLens", new Vector3(0, 0.06f * headRadius, 0), new Vector3(1.46f * headRadius, 0.42f * headRadius, 0.08f * headRadius), color, visual.Layer);
                AddBox(root, "VisorStrap", new Vector3(0, 0.32f * headRadius, 0), new Vector3(1.52f * headRadius, 0.10f * headRadius, 0.08f * headRadius), color.Lightened(0.18f), visual.Layer);
                break;

            case BuddyCosmeticVisualKind.HeadwearBallCap:
                AddEllipsoid(root, "CapCrown", Vector3.Zero, new Vector3(1.02f, 0.52f, 0.62f), headRadius, color, visual.Layer, hemisphere: true);
                AddBox(root, "CapBrim", new Vector3(0, -0.10f * headRadius, 0.62f * headRadius), new Vector3(0.92f * headRadius, 0.10f * headRadius, 0.62f * headRadius), color.Darkened(0.12f), visual.Layer);
                AddEllipsoid(root, "CapButton", new Vector3(0, 0.44f, 0), new Vector3(0.12f, 0.12f, 0.12f), headRadius, color.Darkened(0.12f), visual.Layer);
                break;
            case BuddyCosmeticVisualKind.HeadwearSunHat:
                // A straw sun hat: rounded crown, ribbon band, and a soft brim that droops
                // rather than sticking out flat — three stacked discs of falling radius do the
                // droop without needing a mesh of its own.
                AddEllipsoid(root, "SunCrown", new Vector3(0, 0.06f, 0), new Vector3(0.80f, 0.46f, 0.58f), headRadius, color, visual.Layer, hemisphere: true);
                AddEllipsoid(root, "SunCrownTop", new Vector3(0, 0.30f, 0), new Vector3(0.74f, 0.22f, 0.54f), headRadius, color, visual.Layer);
                AddCylinder(root, "SunBand", new Vector3(0, -0.06f * headRadius, 0), 0.84f * headRadius, 0.20f * headRadius, 0f, color.Darkened(0.28f), visual.Layer, topRadius: 0.84f * headRadius);
                AddCylinder(root, "SunBrimInner", new Vector3(0, -0.17f * headRadius, 0), 1.42f * headRadius, 0.08f * headRadius, 0f, color, visual.Layer, topRadius: 1.42f * headRadius);
                AddCylinder(root, "SunBrimMid", new Vector3(0, -0.24f * headRadius, 0), 1.30f * headRadius, 0.07f * headRadius, 0f, color, visual.Layer, topRadius: 1.44f * headRadius);
                AddCylinder(root, "SunBrimEdge", new Vector3(0, -0.30f * headRadius, 0), 1.10f * headRadius, 0.06f * headRadius, 0f, color.Darkened(0.08f), visual.Layer, topRadius: 1.32f * headRadius);
                break;
            case BuddyCosmeticVisualKind.HeadwearFedora:
                AddEllipsoid(root, "FedoraCrown", new Vector3(0, 0.14f, 0), new Vector3(0.80f, 0.62f, 0.66f), headRadius, color, visual.Layer);
                AddBox(root, "FedoraPinch", new Vector3(0, 0.50f * headRadius, 0), new Vector3(0.16f * headRadius, 0.30f * headRadius, 0.60f * headRadius), color.Darkened(0.18f), visual.Layer);
                AddCylinder(root, "FedoraBand", new Vector3(0, -0.12f * headRadius, 0), 0.84f * headRadius, 0.20f * headRadius, 0f, color.Lightened(0.30f), visual.Layer, topRadius: 0.84f * headRadius);
                AddCylinder(root, "FedoraBrim", new Vector3(0, -0.24f * headRadius, 0), 1.22f * headRadius, 0.09f * headRadius, 0f, color, visual.Layer, topRadius: 1.22f * headRadius);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(visual), visual.Kind, "Unsupported trusted cosmetic visual kind.");
        }
    }

    private void AddGeneratedAsset(Node3D root, BuddyCosmeticVisualDefinition visual, float targetRadius, bool mirrorX, Color color)
    {
        GeneratedBuddyCosmeticResource resource = visual.GeneratedResource ?? throw new InvalidOperationException($"Generated visual '{visual.CosmeticId}' has no trusted generated resource.");
        if (!GodotObject.IsInstanceValid(resource.MeshScene) || !GodotObject.IsInstanceValid(resource.AlbedoTexture))
            throw new InvalidOperationException($"Generated visual '{visual.CosmeticId}' has missing imported assets.");
        Node scene = resource.MeshScene!.Instantiate();
        if (scene is not Node3D scene3D)
        {
            scene.QueueFree();
            throw new InvalidOperationException($"Generated visual '{visual.CosmeticId}' GLB root must be Node3D.");
        }
        root.AddChild(scene3D);
        scene3D.Name = "GeneratedMesh";
        scene3D.Scale = Vector3.One * targetRadius;
        scene3D.RotationDegrees = mirrorX ? new Vector3(0, 180f, 0) : Vector3.Zero;
        var meshes = new List<MeshInstance3D>();
        if (scene3D is MeshInstance3D rootMesh) meshes.Add(rootMesh);
        meshes.AddRange(scene3D.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>());
        if (meshes.Count != 1)
        {
            scene3D.QueueFree();
            throw new InvalidOperationException($"Generated visual '{visual.CosmeticId}' must contain exactly one authored mesh node; found {meshes.Count}.");
        }
        MeshInstance3D instance = meshes[0];
        StandardMaterial3D material = _materials.CreateLitTexturedMaterial(resource.AlbedoTexture!, color);
        material.ResourceName = $"BuddyGenerated_{visual.CosmeticId}";
        material.RenderPriority = (int)visual.Layer;
        instance.MaterialOverride = material;
        instance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        instance.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit;

        if (visual.Slot is CharacterFeatureSlot.Tops or CharacterFeatureSlot.Shoes)
        {
            var outline = new MeshInstance3D
            {
                Name = "GeneratedOutline",
                Mesh = instance.Mesh,
                MaterialOverride = _materials.CreateScaledOutlineMaterial(targetRadius),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
                Scale = Vector3.One * _materials.ReplacementOutlineScale(targetRadius),
            };
            instance.AddChild(outline);
        }
    }

    private void EnsureCosmeticAnchors()
    {
        if (_cosmeticAnchors.Count > 0) return;
        float headRadius = PartMeshRadius(BuddyPartId.Head);
        float torsoRadius = PartMeshRadius(BuddyPartId.Torso);
        float epsilon = _trustedProfile.FaceDepthEpsilon;
        AddAnchor(BuddyCosmeticAnchorId.HeadFront, GetPartSocket(BuddyPartId.Head), new Vector3(0, 0, headRadius + epsilon * 2));
        AddAnchor(BuddyCosmeticAnchorId.HeadCrown, GetPartSocket(BuddyPartId.Head), new Vector3(0, headRadius * 0.72f, headRadius * 0.48f));
        AddAnchor(BuddyCosmeticAnchorId.LeftEar, GetPartSocket(BuddyPartId.Head), new Vector3(-headRadius, 0, 0));
        AddAnchor(BuddyCosmeticAnchorId.RightEar, GetPartSocket(BuddyPartId.Head), new Vector3(headRadius, 0, 0));
        AddAnchor(BuddyCosmeticAnchorId.EyeGroup, GetPartSocket(BuddyPartId.Head), new Vector3(0, 0, headRadius + epsilon * 3));
        AddAnchor(BuddyCosmeticAnchorId.TorsoBody, GetPartSocket(BuddyPartId.Torso), Vector3.Zero);
        AddAnchor(BuddyCosmeticAnchorId.TorsoFront, GetPartSocket(BuddyPartId.Torso), new Vector3(0, 0, torsoRadius + epsilon * 2));
        AddAnchor(BuddyCosmeticAnchorId.TorsoAttachment, GetPartSocket(BuddyPartId.Torso), new Vector3(0, 0, torsoRadius + epsilon * 3));
        AddAnchor(BuddyCosmeticAnchorId.LeftFoot, GetPartSocket(BuddyPartId.LeftFoot), Vector3.Zero);
        AddAnchor(BuddyCosmeticAnchorId.RightFoot, GetPartSocket(BuddyPartId.RightFoot), Vector3.Zero);
    }

    private void AddAnchor(BuddyCosmeticAnchorId id, Node3D parent, Vector3 position)
    {
        var anchor = new Node3D { Name = $"CosmeticAnchor_{id}", Position = position, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit };
        parent.AddChild(anchor);
        _cosmeticAnchors.Add(id, anchor);
    }

    private static void ApplyFeatureTransform(Node3D root, in NormalizedFeatureTransform transform, float radius)
    {
        root.Position = new Vector3((float)transform.OffsetX * radius * 0.35f, (float)transform.OffsetY * radius * 0.35f, 0);
        root.Scale = Vector3.One * (float)transform.Scale;
    }

    private void AddGlasses(Node3D root, float radius, Color color, BuddyCosmeticRenderLayer layer)
    {
        float lensWidth = radius * 0.62f;
        float lensHeight = radius * 0.42f;
        float frame = radius * 0.08f;
        float center = radius * 0.38f;
        foreach (float sign in new[] { -1.0f, 1.0f })
        {
            float x = sign * center;
            AddBox(root, $"LensTop{sign}", new Vector3(x, lensHeight * 0.5f, 0), new Vector3(lensWidth, frame, frame), color, layer);
            AddBox(root, $"LensBottom{sign}", new Vector3(x, -lensHeight * 0.5f, 0), new Vector3(lensWidth, frame, frame), color, layer);
            AddBox(root, $"LensLeft{sign}", new Vector3(x - lensWidth * 0.5f, 0, 0), new Vector3(frame, lensHeight, frame), color, layer);
            AddBox(root, $"LensRight{sign}", new Vector3(x + lensWidth * 0.5f, 0, 0), new Vector3(frame, lensHeight, frame), color, layer);
        }
        AddBox(root, "Bridge", Vector3.Zero, new Vector3(radius * 0.22f, frame, frame), color, layer);
    }

    /// <summary>
    /// How far back a point at normalized distance <paramref name="nx"/>,<paramref name="ny"/>
    /// from the face's centre has to sit to lie on the head instead of floating in front of
    /// it. The drawn face is a flat plate, which is fine for a decal seen head-on, but Buddy
    /// Studio now turns the preview — so face marks are wrapped onto the sphere.
    /// </summary>
    private static float FaceSurfaceZ(float nx, float ny)
    {
        float squared = (nx * nx) + (ny * ny);
        return Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - squared)) - 1.0f;
    }

    /// <summary>One soft mark on the face, wrapped onto the head's curve.</summary>
    private void AddFaceDot(Node3D root, string name, Vector2 at, float size, float headRadius, Color color, BuddyCosmeticRenderLayer layer) =>
        AddEllipsoid(root, name, new Vector3(at.X, at.Y, FaceSurfaceZ(at.X, at.Y)), new Vector3(size, size, 0.02f), headRadius, color, layer);

    /// <summary>An oval patch — a blush, a chin patch — wrapped onto the head's curve.</summary>
    private void AddFaceBlob(Node3D root, string name, Vector2 at, Vector2 size, float headRadius, Color color, BuddyCosmeticRenderLayer layer) =>
        AddEllipsoid(root, name, new Vector3(at.X, at.Y, FaceSurfaceZ(at.X, at.Y)), new Vector3(size.X, size.Y, 0.02f), headRadius, color, layer);

    /// <summary>
    /// A curved crease, drawn as a run of beads along an elliptical arc and tapered towards
    /// both ends so it fades out rather than stopping dead. Straight boxes gave hard-cornered
    /// bars that read as stickers; a real crease bends and thins.
    /// </summary>
    private void AddFaceArc(Node3D root, string name, Vector2 center, float radiusX, float radiusY, float startDegrees, float endDegrees, float thickness, int beads, float headRadius, Color color, BuddyCosmeticRenderLayer layer, float taper = 0.0f)
    {
        for (int index = 0; index < beads; index++)
        {
            float t = beads <= 1 ? 0.5f : index / (float)(beads - 1);
            float angle = Mathf.DegToRad(Mathf.Lerp(startDegrees, endDegrees, t));
            AddFaceDot(
                root,
                $"{name}_{index}",
                center + new Vector2(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY),
                thickness * Mathf.Lerp(1.0f, 1.0f - taper, Mathf.Abs((t * 2.0f) - 1.0f)),
                headRadius,
                color,
                layer);
        }
    }

    /// <summary>The straight-line form of <see cref="AddFaceArc"/>, for short rays.</summary>
    private void AddFaceLine(Node3D root, string name, Vector2 from, Vector2 to, float thickness, int beads, float headRadius, Color color, BuddyCosmeticRenderLayer layer, float taper = 0.0f)
    {
        for (int index = 0; index < beads; index++)
        {
            float t = beads <= 1 ? 0.5f : index / (float)(beads - 1);
            AddFaceDot(
                root,
                $"{name}_{index}",
                from.Lerp(to, t),
                thickness * Mathf.Lerp(1.0f, 1.0f - taper, Mathf.Abs((t * 2.0f) - 1.0f)),
                headRadius,
                color,
                layer);
        }
    }

    private void AddEllipsoid(Node3D root, string name, Vector3 normalizedPosition, Vector3 normalizedScale, float radius, Color color, BuddyCosmeticRenderLayer layer, bool hemisphere = false)
    {
        var mesh = new SphereMesh { Radius = radius, Height = hemisphere ? radius : radius * 2, IsHemisphere = hemisphere };
        var instance = CosmeticMesh(name, mesh, color, layer);
        instance.Position = normalizedPosition * radius;
        instance.Scale = normalizedScale;
        root.AddChild(instance);
    }

    private void AddBox(Node3D root, string name, Vector3 position, Vector3 size, Color color, BuddyCosmeticRenderLayer layer, float rollDegrees = 0.0f)
    {
        var instance = CosmeticMesh(name, new BoxMesh { Size = size }, color, layer);
        instance.Position = position;
        instance.RotationDegrees = new Vector3(0, 0, rollDegrees);
        root.AddChild(instance);
    }
    /// <summary>Triangular prism, rolled about Z so the apex can point up or down.</summary>
    private void AddPrism(Node3D root, string name, Vector3 position, Vector3 size, float rollDegrees, Color color, BuddyCosmeticRenderLayer layer)
    {
        var instance = CosmeticMesh(name, new PrismMesh { Size = size }, color, layer);
        instance.Position = position;
        instance.RotationDegrees = new Vector3(0, 0, rollDegrees);
        root.AddChild(instance);
    }

    /// <summary>Cylinder or cone, rolled about Z so the axis can point sideways instead of up.</summary>
    private void AddCylinder(Node3D root, string name, Vector3 position, float bottomRadius, float height, float rollDegrees, Color color, BuddyCosmeticRenderLayer layer, float topRadius = 0.0f)
    {
        var instance = CosmeticMesh(name, new CylinderMesh { TopRadius = topRadius, BottomRadius = bottomRadius, Height = height }, color, layer);
        instance.Position = position;
        instance.RotationDegrees = new Vector3(0, 0, rollDegrees);
        root.AddChild(instance);
    }

    /// <summary>Torus stood up in the XY plane, for round lens frames.</summary>
    private void AddRing(Node3D root, string name, Vector3 position, float innerRadius, float outerRadius, Color color, BuddyCosmeticRenderLayer layer)
    {
        var instance = CosmeticMesh(name, new TorusMesh { InnerRadius = innerRadius, OuterRadius = outerRadius }, color, layer);
        instance.Position = position;
        instance.RotationDegrees = new Vector3(90f, 0, 0);
        root.AddChild(instance);
    }

    private MeshInstance3D CosmeticMesh(string name, PrimitiveMesh mesh, Color color, BuddyCosmeticRenderLayer layer)
    {
        StandardMaterial3D material = _materials.CreateLitMaterial(color);
        material.ResourceName = $"BuddyCosmetic_{name}";
        material.RenderPriority = (int)layer;
        return new MeshInstance3D { Name = name, Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, MaterialOverride = material, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit };
    }

    private static void RemoveVisual(ref Node3D? visual)
    {
        if (!GodotObject.IsInstanceValid(visual)) { visual = null; return; }
        visual!.GetParent()?.RemoveChild(visual);
        visual.QueueFree();
        visual = null;
    }
}
