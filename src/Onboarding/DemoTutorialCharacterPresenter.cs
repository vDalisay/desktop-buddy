using System;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Onboarding;

/// <summary>
/// Temporary tutorial helper art. Dropping the owner's final image at
/// res://assets/ui/tutorial/tutorial_guide.png automatically replaces the procedural placeholder;
/// tutorial authority and persistence never depend on the art asset.
/// </summary>
public sealed partial class DemoTutorialCharacterPresenter : ITutorialCharacterPresenter
{
    public const string OptionalArtPath = "res://assets/ui/tutorial/tutorial_guide.png";

    private readonly FirstSessionGuidanceController _owner;
    private TutorialBuddyCard? _card;

    public DemoTutorialCharacterPresenter(FirstSessionGuidanceController owner) =>
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public void Present(string stepId, string text)
    {
        if (!GodotObject.IsInstanceValid(_owner) || !_owner.IsInsideTree())
            return;

        if (!GodotObject.IsInstanceValid(_card))
        {
            // The guide is a square inside the tutorial window, not a second window trailing it.
            if (!GodotObject.IsInstanceValid(_owner.GuideSlot))
                return;
            _card = new TutorialBuddyCard
            {
                Name = "DemoTutorialBuddy",
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _card.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _owner.GuideSlot.AddChild(_card);
        }

        _card!.SetStep(stepId);
        _card.Visible = true;
    }

    public void Dismiss()
    {
        if (GodotObject.IsInstanceValid(_card))
            _card!.Visible = false;
    }

    private sealed partial class TutorialBuddyCard : Control
    {
        private string _stepId = TutorialStepIds.GrabBuddy;
        private Texture2D? _ownerArt;
        private bool _artChecked;

        public void SetStep(string stepId)
        {
            _stepId = stepId;
            EnsureOptionalArt();
            QueueRedraw();
        }

        public override void _Draw()
        {
            Rect2 rect = new(Vector2.Zero, Size);
            DrawRect(rect, Win98ThemeFactory.Face, true);
            DrawLine(new Vector2(0, 0), new Vector2(Size.X, 0), Colors.White, 2);
            DrawLine(new Vector2(0, 0), new Vector2(0, Size.Y), Colors.White, 2);
            DrawLine(new Vector2(0, Size.Y - 1), new Vector2(Size.X, Size.Y - 1), new Color("404040"), 2);
            DrawLine(new Vector2(Size.X - 1, 0), new Vector2(Size.X - 1, Size.Y), new Color("404040"), 2);

            // Match the other Win98 submenus instead of presenting the helper as an unframed card.
            DrawRect(new Rect2(3, 3, Math.Max(0, Size.X - 6), 20), Win98ThemeFactory.ActiveTitle, true);
            DrawString(
                ThemeDB.FallbackFont,
                new Vector2(8, 18),
                "Guide",
                HorizontalAlignment.Left,
                -1,
                12,
                Colors.White);

            Rect2 artRect = new(8, 28, Math.Max(0, Size.X - 16), Math.Max(0, Size.Y - 36));
            if (GodotObject.IsInstanceValid(_ownerArt))
            {
                DrawTextureRect(_ownerArt!, artRect, false);
                return;
            }

            DrawProceduralPlaceholder(artRect);
        }

        private void EnsureOptionalArt()
        {
            if (_artChecked)
                return;
            _artChecked = true;
            if (ResourceLoader.Exists(OptionalArtPath))
                _ownerArt = GD.Load<Texture2D>(OptionalArtPath);
        }

        private void DrawProceduralPlaceholder(Rect2 artRect)
        {
            bool customize = _stepId is
                TutorialStepIds.OpenPaintBuddy or TutorialStepIds.SelectPaintBrush or
                TutorialStepIds.SelectPaintColor or TutorialStepIds.PaintBuddy or
                TutorialStepIds.UsePaintedBuddy or
                TutorialStepIds.AdmirePaintedBuddy or TutorialStepIds.OpenPaintBackground or
                TutorialStepIds.SelectBackgroundSpray or TutorialStepIds.SelectBackgroundColor or
                TutorialStepIds.PaintBackground or TutorialStepIds.FloatPaintBackgroundPanel or
                TutorialStepIds.SaveAndExitPaintBackground or TutorialStepIds.OpenBuddyStudio or
                TutorialStepIds.SelectNoseCategory or TutorialStepIds.SelectNoseButtonStyle or
                TutorialStepIds.BuyStudioItem or TutorialStepIds.EquipStudioItem or
                TutorialStepIds.SaveBuddyStudio or TutorialStepIds.ExitBuddyStudio or
                TutorialStepIds.AdmireStudioBuddy;
            bool work = _stepId is
                TutorialStepIds.EnterWorkMode or TutorialStepIds.DragWorkCompanion or
                TutorialStepIds.ResizeWorkCompanion or TutorialStepIds.ToggleWorkCounter or
                TutorialStepIds.ExitWorkMode;
            bool shop = _stepId is
                TutorialStepIds.OpenInventory or TutorialStepIds.PurchaseBaseballBat or
                TutorialStepIds.ChargedBatHit or TutorialStepIds.UnequipTool;

            Vector2 center = artRect.GetCenter() + new Vector2(0, -2);
            Color body = customize ? new Color("f2cf68") : work ? new Color("9dc5e8") : new Color("dfb6dc");
            Color outline = new("343434");

            DrawCircle(center + new Vector2(0, -20), 23, body);
            DrawArc(center + new Vector2(0, -20), 23, 0, Mathf.Tau, 32, outline, 2, true);
            DrawRect(new Rect2(center.X - 22, center.Y + 2, 44, 39), body, true);
            DrawLine(new Vector2(center.X - 22, center.Y + 2), new Vector2(center.X - 22, center.Y + 38), outline, 2);
            DrawLine(new Vector2(center.X + 22, center.Y + 2), new Vector2(center.X + 22, center.Y + 38), outline, 2);
            DrawCircle(center + new Vector2(-27, 16), 8, body);
            DrawCircle(center + new Vector2(27, 16), 8, body);

            Vector2 eyeLeft = center + new Vector2(-8, -23);
            Vector2 eyeRight = center + new Vector2(8, -23);
            DrawCircle(eyeLeft, 2.2f, outline);
            DrawCircle(eyeRight, 2.2f, outline);
            DrawArc(center + new Vector2(0, -14), 7, 0.15f, Mathf.Pi - 0.15f, 12, outline, 1.6f, true);

            if (work)
                DrawWorkProps(center, outline);
            else if (customize)
                DrawPaintProps(center, outline);
            else if (shop)
                DrawShopProps(center, outline);
            else
                DrawInteractionProps(center, outline);
        }

        private void DrawInteractionProps(Vector2 center, Color outline)
        {
            Vector2 cursor = center + new Vector2(31, -30);
            Vector2[] arrow =
            [
                cursor,
                cursor + new Vector2(2, 16),
                cursor + new Vector2(5, 12),
                cursor + new Vector2(10, 19),
                cursor + new Vector2(14, 16),
                cursor + new Vector2(9, 10),
                cursor + new Vector2(15, 9),
            ];
            DrawPolyline(arrow, outline, 2, true);
        }

        private void DrawShopProps(Vector2 center, Color outline)
        {
            Rect2 tinyWindow = new(center + new Vector2(25, -38), new Vector2(27, 25));
            DrawRect(tinyWindow, new Color("c0c0c0"), true);
            DrawRect(new Rect2(tinyWindow.Position, new Vector2(tinyWindow.Size.X, 6)), new Color("000080"), true);
            DrawRect(tinyWindow, outline, false, 1);
            DrawString(ThemeDB.FallbackFont, tinyWindow.Position + new Vector2(5, 19), "$", HorizontalAlignment.Left, 12, 10, outline);
        }

        private void DrawPaintProps(Vector2 center, Color outline)
        {
            Vector2 start = center + new Vector2(23, -37);
            Vector2 end = start + new Vector2(16, 24);
            DrawLine(start, end, outline, 5, true);
            DrawLine(start - new Vector2(2, 2), start + new Vector2(5, 5), new Color("e56b5d"), 6, true);
            DrawCircle(end + new Vector2(4, 4), 4, new Color("3a70d8"));
        }

        private void DrawWorkProps(Vector2 center, Color outline)
        {
            DrawRect(new Rect2(center + new Vector2(-16, -29), new Vector2(12, 8)), new Color(0, 0, 0, 0), false, 2);
            DrawRect(new Rect2(center + new Vector2(4, -29), new Vector2(12, 8)), new Color(0, 0, 0, 0), false, 2);
            DrawLine(center + new Vector2(-4, -25), center + new Vector2(4, -25), outline, 2);
            Rect2 crt = new(center + new Vector2(22, 11), new Vector2(31, 24));
            DrawRect(crt, new Color("595959"), true);
            DrawRect(new Rect2(crt.Position + new Vector2(4, 4), new Vector2(23, 13)), new Color("112711"), true);
            DrawString(ThemeDB.FallbackFont, crt.Position + new Vector2(8, 15), "123", HorizontalAlignment.Left, 20, 7, new Color("65c75a"));
        }
    }
}
