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
        // so the hats sit centred on the head by construction rather than each one carrying
        // its own correction — and let the player nudge and resize from there.
        if (visual.Slot == CharacterFeatureSlot.Headwear)
        {
            ApplyFeatureTransform(root, appearance.Transform, headRadius);
            root.Position += new Vector3(0.0f, 0.14f * headRadius, -0.48f * headRadius);
        }

        switch (visual.Kind)
        {
            case BuddyCosmeticVisualKind.HairShortSweep:
                // It used to be three ellipsoids built at the anchor itself, which leans 0.48
                // radii forward — so the whole style hung off the front of the face with a
                // bare crown and nothing behind (owner report 2026-08-21). Same negative-Z
                // offsets the bob uses: a cap over the top, a back, then the sweep and flick.
                AddEllipsoid(root, "SweepCap", new Vector3(0, -0.22f, -0.48f), new Vector3(0.94f, 0.60f, 0.94f), headRadius, color, visual.Layer, hemisphere: true);
                AddEllipsoid(root, "SweepBack", new Vector3(0, -0.54f, -0.86f), new Vector3(0.84f, 0.54f, 0.48f), headRadius, color, visual.Layer);
                AddEllipsoid(root, "SweepFront", new Vector3(-0.16f, -0.14f, -0.08f), new Vector3(0.88f, 0.30f, 0.44f), headRadius, color, visual.Layer);
                AddEllipsoid(root, "SweepTip", new Vector3(0.54f, -0.26f, -0.16f), new Vector3(0.40f, 0.22f, 0.36f), headRadius, color, visual.Layer);
                break;
            case BuddyCosmeticVisualKind.HairBobBangs:
                // Symmetrical ear-length bob: skull cap, a straight fringe band across the
                // forehead, side curtains down to the ear line and a small outward flick.
                // The cap is a hemisphere, so everything below its flat underside had to be
                // covered by something else — and it was not: the back showed a bare band and
                // the sides a gap (owner report 2026-08-22). The back is now one full ellipsoid
                // that starts above the cap's rim and reaches past the jaw, and each side
                // curtain overlaps both it and the cap instead of hanging between them.
                AddEllipsoid(root, "BobCap", new Vector3(0, -0.22f, -0.48f), new Vector3(1.02f, 0.84f, 1.04f), headRadius, color, visual.Layer, hemisphere: true);
                AddEllipsoid(root, "BobBack", new Vector3(0, -0.30f, -0.72f), new Vector3(1.00f, 0.98f, 0.86f), headRadius, color, visual.Layer);
                AddEllipsoid(root, "BobFringe", new Vector3(0, -0.17f, 0.30f), new Vector3(0.86f, 0.24f, 0.32f), headRadius, color, visual.Layer);
                foreach (float bobSide in new[] { -1.0f, 1.0f })
                {
                    AddEllipsoid(root, $"BobSide{bobSide}", new Vector3(bobSide * 0.78f, -0.72f, -0.34f), new Vector3(0.40f, 0.80f, 0.86f), headRadius, color, visual.Layer);
                    AddEllipsoid(root, $"BobFlick{bobSide}", new Vector3(bobSide * 0.92f, -1.24f, -0.34f), new Vector3(0.34f, 0.26f, 0.52f), headRadius, color, visual.Layer);
                }
                break;
            case BuddyCosmeticVisualKind.HairTwinBraids:
                // Moulded toy pigtails: one solid cap and back, a centre-parted fringe, and a
                // braid on each side built as three shrinking beads so it reads as plaited.
                AddEllipsoid(root, "BraidCap", new Vector3(0, -0.22f, -0.48f), new Vector3(1.02f, 0.86f, 1.04f), headRadius, color, visual.Layer, hemisphere: true);
                AddEllipsoid(root, "BraidBack", new Vector3(0, -0.34f, -0.70f), new Vector3(0.98f, 0.92f, 0.84f), headRadius, color, visual.Layer);
                foreach (float braidSide in new[] { -1.0f, 1.0f })
                {
                    AddEllipsoid(root, $"BraidFringe{braidSide}", new Vector3(braidSide * 0.34f, -0.14f, 0.22f), new Vector3(0.52f, 0.26f, 0.34f), headRadius, color, visual.Layer);
                    AddEllipsoid(root, $"BraidTie{braidSide}", new Vector3(braidSide * 0.92f, -0.62f, -0.30f), new Vector3(0.34f, 0.34f, 0.40f), headRadius, color.Darkened(0.15f), visual.Layer);
                    AddEllipsoid(root, $"BraidTop{braidSide}", new Vector3(braidSide * 1.02f, -1.02f, -0.30f), new Vector3(0.34f, 0.34f, 0.36f), headRadius, color, visual.Layer);
                    AddEllipsoid(root, $"BraidMid{braidSide}", new Vector3(braidSide * 1.06f, -1.42f, -0.30f), new Vector3(0.30f, 0.30f, 0.32f), headRadius, color, visual.Layer);
                    AddEllipsoid(root, $"BraidTip{braidSide}", new Vector3(braidSide * 1.08f, -1.76f, -0.30f), new Vector3(0.22f, 0.24f, 0.24f), headRadius, color, visual.Layer);
                }
                break;
            case BuddyCosmeticVisualKind.HairLongWaves:
                // The other toy silhouette: one continuous sheet of hair falling past the
                // shoulders, with a soft wave at the hem and a swept fringe over one brow.
                AddEllipsoid(root, "WaveCap", new Vector3(0, -0.24f, -0.48f), new Vector3(1.04f, 0.88f, 1.06f), headRadius, color, visual.Layer, hemisphere: true);
                AddEllipsoid(root, "WaveBack", new Vector3(0, -0.46f, -0.66f), new Vector3(1.02f, 1.10f, 0.90f), headRadius, color, visual.Layer);
                AddEllipsoid(root, "WaveFall", new Vector3(0, -1.50f, -0.62f), new Vector3(0.96f, 0.90f, 0.66f), headRadius, color, visual.Layer);
                AddEllipsoid(root, "WaveHem", new Vector3(0, -2.20f, -0.58f), new Vector3(0.86f, 0.44f, 0.60f), headRadius, color, visual.Layer);
                AddEllipsoid(root, "WaveFringe", new Vector3(-0.18f, -0.16f, 0.22f), new Vector3(0.84f, 0.28f, 0.38f), headRadius, color, visual.Layer);
                foreach (float waveSide in new[] { -1.0f, 1.0f })
                    AddEllipsoid(root, $"WaveSide{waveSide}", new Vector3(waveSide * 0.82f, -1.10f, -0.34f), new Vector3(0.40f, 1.20f, 0.80f), headRadius, color, visual.Layer);
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
            // Every crown below is a hemisphere with equal scale on all three axes — a real
            // dome. They used to be flattened ellipsoids, which read as an oval pressed onto
            // the head rather than a hat sitting on it (owner report 2026-08-21). The base
            // height is chosen so the hat comes down the sides past the widest part of the
            // skull instead of perching on the very top; headwear_fit measures both.
            case BuddyCosmeticVisualKind.HeadwearSoftCap:
                AddEllipsoid(root, "Crown", new Vector3(0, -0.48f, 0), new Vector3(0.97f, 0.97f, 0.97f), headRadius, color, visual.Layer, hemisphere: true);
                AddBox(root, "Brim", new Vector3(0, -0.44f * headRadius, 0.78f * headRadius), new Vector3(1.00f * headRadius, 0.10f * headRadius, 0.90f * headRadius), color, visual.Layer);
                break;
            case BuddyCosmeticVisualKind.HeadwearKnitBeanie:
                AddEllipsoid(root, "BeanieCrown", new Vector3(0, -0.52f, 0), new Vector3(0.96f, 0.96f, 0.96f), headRadius, color, visual.Layer, hemisphere: true);
                AddCylinder(root, "BeanieCuff", new Vector3(0, -0.45f * headRadius, 0), 1.00f * headRadius, 0.22f * headRadius, 0f, color.Lightened(0.10f), visual.Layer, topRadius: 1.00f * headRadius);
                AddEllipsoid(root, "BeaniePom", new Vector3(0, 0.42f, 0), new Vector3(0.16f, 0.16f, 0.16f), headRadius, color.Lightened(0.10f), visual.Layer);
                break;
            case BuddyCosmeticVisualKind.HeadwearWideBrim:
                AddEllipsoid(root, "BrimCrown", new Vector3(0, -0.48f, 0), new Vector3(0.93f, 0.93f, 0.93f), headRadius, color, visual.Layer, hemisphere: true);
                AddCylinder(root, "WideBrim", new Vector3(0, -0.48f * headRadius, 0), 1.30f * headRadius, 0.09f * headRadius, 0f, color, visual.Layer, topRadius: 1.30f * headRadius);
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
            // Wrinkles, chiselled cheeks and stubble were cut (owner instruction 2026-08-21):
            // at this scale a crease reads as a scratch on the paint however it is drawn, and
            // the beard could not hug the jaw without burying the mouth. Freckles and blush
            // survive because they are patches, which the face has room for.
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
                // Two plain ovals, as they were before the hatching pass (owner instruction
                // 2026-08-21). A blush is a patch of colour and wants nothing drawn on it.
                ApplyFeatureTransform(root, appearance.Transform, headRadius);
                foreach (float rosySide in new[] { -1.0f, 1.0f })
                    AddEllipsoid(root, $"Rosy{rosySide}", new Vector3(rosySide * 0.42f, -0.14f, 0), new Vector3(0.30f, 0.22f, 0.03f), headRadius, color, visual.Layer);
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
                AddEllipsoid(root, "CapCrown", new Vector3(0, -0.48f, 0), new Vector3(0.98f, 0.98f, 0.98f), headRadius, color, visual.Layer, hemisphere: true);
                // A long peak, out past the front of the head where a cap's actually shades
                // the eyes (owner instruction 2026-08-21); the old one stopped short of it.
                AddBox(root, "CapBrim", new Vector3(0, -0.44f * headRadius, 0.85f * headRadius), new Vector3(1.00f * headRadius, 0.10f * headRadius, 1.10f * headRadius), color.Darkened(0.12f), visual.Layer);
                AddEllipsoid(root, "CapButton", new Vector3(0, 0.44f, 0), new Vector3(0.10f, 0.10f, 0.10f), headRadius, color.Darkened(0.12f), visual.Layer);
                break;
            case BuddyCosmeticVisualKind.HeadwearSunHat:
                // A straw sun hat: round crown, ribbon band, and a soft brim that droops rather
                // than sticking out flat — three stacked discs of falling radius do the droop
                // without needing a mesh of its own.
                AddEllipsoid(root, "SunCrown", new Vector3(0, -0.44f, 0), new Vector3(0.92f, 0.92f, 0.92f), headRadius, color, visual.Layer, hemisphere: true);
                AddCylinder(root, "SunBand", new Vector3(0, -0.34f * headRadius, 0), 0.97f * headRadius, 0.20f * headRadius, 0f, color.Darkened(0.28f), visual.Layer, topRadius: 0.97f * headRadius);
                AddCylinder(root, "SunBrimInner", new Vector3(0, -0.40f * headRadius, 0), 1.42f * headRadius, 0.08f * headRadius, 0f, color, visual.Layer, topRadius: 1.42f * headRadius);
                AddCylinder(root, "SunBrimMid", new Vector3(0, -0.45f * headRadius, 0), 1.30f * headRadius, 0.07f * headRadius, 0f, color, visual.Layer, topRadius: 1.44f * headRadius);
                AddCylinder(root, "SunBrimEdge", new Vector3(0, -0.50f * headRadius, 0), 1.10f * headRadius, 0.06f * headRadius, 0f, color.Darkened(0.08f), visual.Layer, topRadius: 1.32f * headRadius);
                break;
            case BuddyCosmeticVisualKind.HeadwearFedora:
                AddEllipsoid(root, "FedoraCrown", new Vector3(0, -0.48f, 0), new Vector3(0.93f, 0.93f, 0.93f), headRadius, color, visual.Layer, hemisphere: true);
                AddBox(root, "FedoraPinch", new Vector3(0, 0.42f * headRadius, 0), new Vector3(0.14f * headRadius, 0.22f * headRadius, 0.70f * headRadius), color.Darkened(0.18f), visual.Layer);
                AddCylinder(root, "FedoraBand", new Vector3(0, -0.40f * headRadius, 0), 0.97f * headRadius, 0.20f * headRadius, 0f, color.Lightened(0.30f), visual.Layer, topRadius: 1.00f * headRadius);
                AddCylinder(root, "FedoraBrim", new Vector3(0, -0.48f * headRadius, 0), 1.28f * headRadius, 0.09f * headRadius, 0f, color, visual.Layer, topRadius: 1.28f * headRadius);
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
