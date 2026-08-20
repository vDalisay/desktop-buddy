# Player-facing copy

Every line of text the player reads, pulled straight from source.
Rewrite the **Text** column and hand the file back; the copy is reapplied verbatim.
Names, ids and node keys are not copy — leave those columns alone.

Generated from:

- tutorial prompts: `TextFor` in `src/Onboarding/FirstSessionGuidanceController.cs`
- tool descriptions: `ContentDisplayName.Usage` in `src/UI/ContentDisplayName.cs`
- settings descriptions: `src/CharacterEditor/CharacterEditorHost.SettingsRows.cs`
- Help mode: `ExplicitHelp` in `src/Onboarding/FirstSessionGuidanceController.cs`

---

## 1. Tutorial prompts

In walkthrough order. A few steps have two variants, marked *(like this)* —
both are shown and both can be rewritten.

| # | Step id | Text |
| --- | --- | --- |
| 1 | `grab_buddy` | Say hello. Press and hold the left mouse button on Buddy to pick him up, then fling him around the room. Let go when you have had your fun. |
| 2 | `open_inventory` | Time to go shopping. Open Inventory, up in the top-left corner — that is where every tool you own lives. |
| 3 | `purchase_baseball_bat` | This is the Inventory: everything you can buy and equip, all in one list. Your credits are counted up in the top-right corner, and playing with Buddy is what earns them — the rougher the play, the bigger the payout. Grab the Baseball Bat: buy it for 1 credit, or equip it if you already own it. Anything you buy is equipped straight away. |
| 4 | `charged_bat_hit` | Batter up. The bat is already in your hands. Hold right mouse to wind up, then let go and swing. Any amount of charge is enough for this lesson; a full charge hurts a lot more — and pays a lot more. |
| 5 | `unequip_tool` | Done with a tool? Press D to drop it. You are back to bare hands — and to pick it up again, just double-click the dropped tool. |
| 6 | `open_paint_buddy` | Buddy is looking a little plain. Open Paint ▸ Buddy and let us fix that. |
| 7 | `create_buddy` | *(slots free)* First, a buddy of your own to work on. Press + New Character, give him a name, and he is the one you paint from here on.<br>*(no slots left)* No free character slots left — so pick the buddy you want to work on from the Characters list instead. Everything after this works exactly the same. |
| 8 | `select_paint_brush` | Pick up the Brush — it is the one you will use most. |
| 9 | `select_paint_color` | Now pick any colour that takes your fancy. |
| 10 | `paint_buddy` | Paint away — anywhere across Buddy's torso will do. Press Save when you are happy with it. |
| 11 | `use_painted_buddy` | Saving keeps it; Use Character puts it on the real Buddy. Choose Use Character to apply it and head back. |
| 12 | `admire_painted_buddy` | Look at that. Genuine artistry — Buddy has never looked better. Wear it with pride. |
| 13 | `open_paint_background` | Buddy has a new look — the room deserves one too. Open Paint ▸ Background. |
| 14 | `select_background_spray` | Grab the Spray can this time. |
| 15 | `select_background_color` | Pick any colour you like from the palette. |
| 16 | `paint_background` | Now spray somewhere on the room behind Buddy. |
| 17 | `float_paint_background_panel` | The tool panel is in the way. Drag it by its title bar right out of the game window and park it off to the side — the 📌 button does the same. It becomes its own desktop window, and you get the whole background to admire. |
| 18 | `save_exit_paint_background` | Lovely. Choose Save and Exit to keep it. |
| 19 | `open_buddy_studio` | One more workshop to show you. Open Buddy Studio — this is where Buddy gets his hair, glasses, hats and everything else. |
| 20 | `select_nose_category` | Start with the Nose category. |
| 21 | `select_nose_button_style` | Click the Button nose once. A single click only previews it — watch it appear on Buddy in the preview window. |
| 22 | `buy_studio_item` | Like it? Buy it with the button on the right. Double-clicking the style itself does the same thing. |
| 23 | `equip_studio_item` | Now equip it. You can come back here any time to equip something else, or switch a slot back to its free default to take it off again. |
| 24 | `save_buddy_studio` | Save it, or Buddy loses his brand-new nose. |
| 25 | `exit_buddy_studio` | *(nose already worn)* Turns out he was already wearing that nose, so there was nothing to save — Save stays greyed out until something actually changes. That is Buddy Studio. Exit when you are ready.<br>*(normal)* That is Buddy Studio. Exit when you are ready. |
| 26 | `admire_studio_buddy` | Now that is a nose. Painted, kitted out, and frankly better dressed than most of us — Buddy is looking sharp. |
| 27 | `enter_work_mode` | Last stop. Work Mode shrinks Buddy into a tiny companion that sits on top of your real desktop and keeps earning while you get on with things. Open Work. |
| 28 | `drag_work_companion` | Hold the left mouse button on Buddy or on the computer, then move the mouse to drag the companion wherever you want it. |
| 29 | `resize_work_companion` | Hold the left mouse button on the ↘ resize button and move the mouse to make the companion bigger or smaller. |
| 30 | `toggle_work_counter` | That little screen counts what you have done. Click it to switch between this session and your lifetime total. |
| 31 | `exit_work_mode` | Done working? Double-click Buddy, or press X, to come back. |
| 32 | `farewell` | And that is everything. If you ever forget what something does, hit the ? button in the title bar and hover over it — Help mode will explain anything on screen. Go have fun with him. Goodbye! |

## 2. Tool descriptions

Shown in the footer of the Inventory and Tools panels when a row is hovered.

| Tool | Text |
| --- | --- |
| Grab | Hold left mouse on Buddy to drag him, and let go while moving to fling him. |
| Power Grab | The same left-mouse drag as Grab, with a far stronger pull and a much harder throw. |
| Brush | Hold left mouse and stroke slowly over Buddy — he has a favourite spot. |
| Feather | Hold left mouse and wiggle over Buddy; keep it up too long and he turns grumpy. |
| Baseball Bat | Hold right mouse to wind up through three charge stages, then let go to swing. |
| Boxing Glove | Swing the cursor into Buddy — the faster the glove moves, the harder it lands. |
| Baseball | Right mouse drops a ball at the cursor; grab it with left mouse, then hold right mouse and pull back to hurl it. |
| Soccer Ball | Right mouse drops the ball; grab it with left mouse, then hold right mouse and pull back to boot it across the room. |
| Meal | Right mouse drops a meal at the cursor; Buddy eats it where it lands, or grab it and pull back with right mouse to throw it at him. |
| Drink | Right mouse drops a drink at the cursor; Buddy takes it from there, or grab it and pull back with right mouse to throw it. |
| Repair Kit | Right mouse drops a kit; grab it with left mouse and throw it into Buddy to patch him back up. |
| Grenade | Right mouse drops a grenade; grab it with left mouse, then hold right mouse and pull back to lob it in an arc — pulling back also pulls the pin. |
| Nerf Blaster | Left mouse fires darts wherever the cursor points; press R to reload. |
| Pistol | Left mouse fires at the cursor; press R to reload when the magazine runs dry. |
| Shotgun | Left mouse fires a spread of pellets — brutal up close; press R to chamber the next shell. |
| Fire Sprayer | Hold left mouse to spray burning fuel; whatever it touches catches alight. |

## 3. Settings descriptions

Shown in the footer of the Settings panel when a row is hovered.

| Setting | Control | Text |
| --- | --- | --- |
| Master Volume | Slider | Every sound in the game at once. Turn this down and everything below it follows. |
| Sound Effects | Slider | Bat hits, gunfire, explosions, footsteps and the buddy's own yelps and laughs. |
| Interface Sounds | Slider | Menu clicks, button presses, purchase chimes and save confirmations. |
| Mute While Working | Toggle | Silence the game completely while the companion is on your desktop, so it never interrupts you. |
| Mute Work Typing | Toggle | Silence just the keyboard clatter the companion makes while it types. Everything else stays audible. |
| V-Sync | Toggle | Match your monitor's refresh rate. Off can show tearing but shaves a little input lag. |
| Frame Limit | Choice | Upper limit on frames per second while you are using the game. Lower caps mean less heat and battery drain. |
| Background Frame Limit | Choice | Frame cap that applies once the window is hidden or in the background, so a minimised buddy costs almost nothing. |
| UI Scale | Choice | Size of every menu, button and label. Open panels reopen at the new size. |
| Buddy Size | Choice | How large the buddy and his room are drawn inside the window. The window itself does not change. |
| Modern UI Motion | Toggle | Keep the Win98 look but allow short smooth menu and preview transitions. Reduced Motion overrides this. |
| Always On Top | Toggle | Keep the buddy's window above everything else, so he never disappears behind your work. |
| Monitor | Choice | Which display the buddy lives on. |
| Reduced Motion | Toggle | Damp or remove big movements: camera kicks, launches, and sweeping menu transitions. |
| Screen Shake | Toggle | Let heavy hits and explosions jolt the camera. Turn off for a completely steady view. |
| Reduced Particles | Toggle | Emit far fewer sparks, smoke puffs and debris. Helps on slower machines. |
| Photosensitivity Safe | Toggle | Cap rapid flashing and strobing from fire, explosions and muzzle flare. |
| Start In | Choice | Which interaction mode a launch begins in. |
| Hide For Full-Screen Apps | Toggle | Step aside while a game, video, or presentation owns the screen. |
| Start With Windows | Toggle | Launch the buddy when you log in. |
| Work/Play Hotkey | Hotkey | The keyboard shortcut that switches between Work and Play. |
| Drop Tool Hotkey | Hotkey | Drop a compatible equipped physical tool into the room. Double-click it to re-equip. |
| Save Folder | Action | Open the folder holding progress, settings, and characters. |
| Reset Progress | Action | Return gameplay to a first run. Settings and saved characters are kept. |
| Show Tutorial Again | Action | Replay the first-session walkthrough from the beginning. Nothing else is reset. |

## 4. Help mode

Shown in the Help popup when the `?` button is active and the region is hovered.
Both the **Title** and the **Text** are player-facing.

| Region | Title | Text |
| --- | --- | --- |
| `Win98CommandBar` | Top bar | Open Inventory, Tools, Paint, Buddy Studio, Work and the other main workspaces here. |
| `Win98BalanceLabel` | Credits | Your current credits. Earn them by playing with Buddy — rough play pays the most — and in Work Mode, then spend them on tools and customization. |
| `Win98ShopCommand` | Inventory | Buy tools and toys. Anything you buy is equipped straight away. |
| `Win98ToolsCommand` | Tools | Switch between the tools you already own. |
| `Win98PaintCommand` | Paint | Paint Buddy or paint the room background. |
| `Win98WorkCommand` | Work | Shrink Buddy into a small always-on-top companion that earns while you work. |
| `ContextHelpButton` | Help | Turn on Help mode, then hover anything on screen to have it explained. Press it again to leave. |
| `Win98StatusBar` | Status bar | The left side reports what just happened; the right side always shows the tool you have equipped. |
| `StatusText` | Status message | The most recent message from the game — purchases, saves, and other confirmations appear here. |
| `ActiveToolStatusText` | Equipped tool | The tool currently on your cursor. Change it from Inventory or the Tools menu. |
| `Win98CharacterColumn` | Characters | Choose which local character you are editing. The layer panel below controls which body part receives paint. |
| `Win98PaintLayerPanel` | Layers | Choose which body-part layer receives paint. Hidden layers cannot receive paint and return when you leave the editor. |
| `Win98PaintToolColumn` | Paint tools | Choose a brush or eraser, change brush size, rotate the preview, undo/redo, and adjust the view. |
| `Win98PaintViewportFrame` | Paint canvas | Draw directly on Buddy here. Your brush follows the visible 3D surface and the selected layer filter. |
| `CharacterPaintCanvas` | Paint canvas | Draw directly on Buddy. Drag to paint; the current brush, color, size and layer determine the result. |
| `Win98PaintColorFooter` | Colors and actions | Choose paint colors here. Save stores the character; Use Character applies it to the live Buddy; Exit leaves the editor. |
| `PaintPresetPalette` | Palette | Pick a saved color quickly. The color-wheel button opens the full picker. |
| `PaintPrimaryActions` | Character actions | Save stores changes, Use Character applies this character, Reset restores the saved version, and Exit leaves Paint Buddy. |
| `PaintBackgroundPanel` | Paint Background | Paint the room backdrop with the same simple paint workflow. Save and Exit keeps the result. |
| `PaintToolGrid` | Background tools | Choose Brush, Pen, Spray, Fill, Eraser, Pick Color, shapes, or Undo. |
| `PaintBrushSizeRow` | Brush size | Change how large the active background brush is. |
| `PaintBackgroundPalettePanel` | Background palette | Choose the active background color, add a custom swatch, or open the full color picker. |
| `EnvironmentBackgroundInputBlocker` | Background canvas | Paint directly on the visible room. The tool panel hides while you drag so it does not cover the canvas. |
| `BuddyStudioCategories` | Categories | Choose which part of Buddy you want to customize, such as eyes, glasses, headwear, tops, or shoes. |
| `BuddyStudioPreviewPane` | Preview | Preview the selected cosmetic here. Supported cosmetics can be moved or resized before saving. |
| `BuddyStudioCatalogPane` | Styles | Single-click a style to preview it. Owned styles can be equipped; unowned styles show their price. |
| `BuddyStudioInspectorPane` | Color and ownership | Change supported colors and see whether the previewed style is owned, equipped, or available to buy. |
| `BuddyStudioBuy` | Buy / Equip | Buy an unowned style permanently, or equip a style you already own. |
| `BuddyStudioActions` | Studio actions | Save applies the current character changes. Exit leaves Buddy Studio and asks about unsaved changes when needed. |
| `WorkCompanionRoot` | Work companion | Hold the left mouse button on Buddy or the computer to drag the companion anywhere. Double-click Buddy to return to Play Mode. |
| `WorkControlCluster` | Companion controls | Resize, pause and exit. They appear whenever the pointer is over the companion and stay the same size however large or small you make it. |
| `WorkCrtCounter` | Work counter | Shows how much you have done. Click the screen to switch between this session and your lifetime total. |
| `WorkResizeButton` | Resize | Hold the left mouse button on this control and move the mouse to resize the Work companion. The controls themselves keep their size. |
| `WorkMotionToggle` | Motion | Pause or resume Buddy's Work animations. Counters and rewards continue either way. |
| `WorkExitButton` | Exit Work Mode | Return to normal Play Mode. Double-clicking Buddy does the same thing. |
