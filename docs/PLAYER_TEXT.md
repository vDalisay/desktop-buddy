# Player-facing copy

Every line of text the player reads, pulled straight from source.
Rewrite the **Text** column and hand the file back; the copy is reapplied verbatim.
Names, ids and node keys are not copy — leave those columns alone.
The **Context** column is background for whoever is rewriting: where the line
appears, what the player is doing at that moment, and what the line has to
achieve. It is not player-facing and does not need rewriting.

Generated from:

- tutorial prompts: `TextFor` in `src/Onboarding/FirstSessionGuidanceController.cs`
- tool descriptions: `ContentDisplayName.Usage` in `src/UI/ContentDisplayName.cs`
- settings descriptions: `src/CharacterEditor/CharacterEditorHost.SettingsRows.cs`
- Help mode: `ExplicitHelp` in `src/Onboarding/FirstSessionGuidanceController.cs`

---

## 0. What the game is

**Desktop Buddy** is a Windows desktop-idler and physics sandbox (Godot 4.6.1,
C#), a clean-room spiritual successor to *Interactive Buddy*. Everything in it
is original: art, audio, character, tools, progression.

**The thing on screen.** A small transparent, bordered box sits on the player's
real desktop. Inside it lives one buddy: an immortal six-body robot/mannequin
puppet driven by real physics springs. He stands, walks, jumps, catches thrown
objects, flinches, resists being grabbed, gets knocked out, and picks himself
back up. He is never destroyed and never dies — however hard he is hit, he
recovers.

**What the player does.** Grab him and fling him around. Hit him with bats,
gloves, guns, grenades and fire. Or feed him, pet him, tickle him and patch him
up with a repair kit. Both directions pay: rough physical play converts pain
into credits immediately, while good mood raises passive income. Credits are
spent in the Inventory on permanent tool unlocks — roughly a two-hour
completionist catalogue that the player can work through in any order.

**Two modes.** *Play Mode* is the bordered sandbox with full UI, where the box
captures the mouse. *Work Mode* shrinks Buddy into a tiny always-on-top
companion that sits on top of the player's real work, passes clicks through to
the apps behind him, keeps typing away and keeps earning while the player gets
on with their day.

**Customization.** *Paint ▸ Buddy* paints directly onto the 3D character's body
layers; *Paint ▸ Background* paints the room behind him; *Buddy Studio* is the
dress-up workshop for noses, eyes, glasses, hats, tops and shoes, bought per
style with credits. Characters are saved locally and any one of them can be
made the live Buddy.

**The look and the voice.** The shell is deliberately Windows 98: grey chrome,
a title bar, a command bar, chunky beveled buttons, a status bar along the
bottom. The buddy himself is a clean, softly-lit 3D character inside that retro
frame. The writing follows the same taste — dry, warm, understated British,
second person, never shouty, never cute for its own sake, and never cruel about
Buddy even while handing the player a shotgun. Short sentences. Plain words.
The joke is in the deadpan, not in exclamation marks.

**Who reads this text.** A first-time player who has never seen the game, on
their own desktop, mid-tutorial or hovering something they do not recognise.
Copy has to teach the control in one read and get out of the way.

---

## 1. Tutorial prompts

In walkthrough order. A few steps have two variants, marked *(like this)* —
both are shown and both can be rewritten.

These appear one at a time in the first-session walkthrough, in a prompt strip
over the game. Each step waits for the player to actually perform the action
before advancing, so every line must name the exact input. This is the only
tutorial the game has.

| # | Step id | Text | Context |
| --- | --- | --- | --- |
| 1 | `grab_buddy` | Hi! Let me introduce you to your buddy. Click and hold your left mouse button on your Buddy to grab him. | Very first line of the game. Play Mode sandbox, no tool equipped. Introduces the character and the core verb — mouse-drag physics — before any UI exists. Sets the tone for everything after it. |
| 2 | `open_inventory` | Now open the Inventory in the top-left corner. This is where you can buy and equip all sorts of different tools so you and your Buddy can play together. | Points at the Win98 command bar at the top of the window. Pure navigation: get the player to click one named button and learn where the shop lives. |
| 3 | `purchase_baseball_bat` | You can use your Credits in the top-right to buy all kinds of things. You earn more by playing with your Buddy. For now, lets buy and equip the Baseball Bat.| Inventory panel is open. The heaviest teaching step: it has to explain the shop list, where credits are shown, how credits are earned, and that buying auto-equips. |
| 4 | `charged_bat_hit` | Nice stuff! You can hold the right mouse button to charge your bat up for a big swing. Some other tools have some extra interaction by clicking or holding the right mouse button. Try them out yourself later, but for now try hitting the buddy with a charged swing. | Back in the sandbox with the bat equipped. Teaches the charge-and-release pattern that most offensive tools reuse, and reassures the player that a weak swing still passes the step. |
| 5 | `unequip_tool` | To unequip a tool you can switch in the Inventory or press the 'D' button to drop it. You can re-equip dropped tools by double-clicking it. Try to drop it now. | Teaches the drop hotkey and that dropped tools become real objects lying in the room that can be picked back up. |
| 6 | `open_paint_buddy` | Your Buddy could use a little colour. Open Paint ▸ Buddy in the menu above to give it a new look. | Navigation into the first customization workshop, via the Paint menu in the command bar. Hands the player a reason to care. |
| 7 | `create_buddy` | *(slots free)* Does this look familiar? First, let's create a new Buddy for you. Click on '+ New Character', give it a name, and you are ready to paint.<br>*(no slots left)* Hmm, you've been here before so choose a Buddy from the Characters list instead. | Paint Buddy editor, character column on the left. Two variants because a returning player may already have filled every save slot; the second variant must not read as an error or a punishment. |
| 8 | `select_paint_brush` | There are many tools to choose from. Let's start with my favourite which is the Brush tool to start painting. | Paint tool column. One click, one tool. Keeps the momentum. |
| 9 | `select_paint_color` | Here you can choose any colour you like. The big button to the right opens up the advanced color pallette. Let's just choose one of these for now. | Colour palette along the bottom of the paint editor. Deliberately no right answer — the player picks whatever they like. |
| 10 | `paint_buddy` | Paint away! When you are happy, click on the 'Save' button below. | The player drags on the 3D character to paint. Teaches that Save is what keeps the work, and sets a generous target area so nobody fails the step. |
| 11 | `use_painted_buddy` | Click on the 'Use Character' to start playing with your new Buddy! | The one genuinely confusing distinction in the editor: saving a character is not the same as making it the live Buddy. This line exists to draw that line clearly. |
| 12 | `admire_painted_buddy` | Beautiful! Buddy has never looked better. | Back in the sandbox, painted Buddy on screen. No action required — a beat of dry praise that rewards the detour, whatever the player actually painted. |
| 13 | `open_paint_background` | Your Buddy deserves a better room that matches it's new style. Open Paint ▸ Background to start painting the room. | Bridges from character painting to room painting so the second workshop feels like a continuation rather than a repeat. |
| 14 | `select_background_spray` | Let's go with something new but nostalgic, the Spray tool! | Background tool grid. A different tool from step 8 on purpose, to show the toolset is not the same one. |
| 15 | `select_background_color` | Choose a nice matching colour from the Palette. | Background palette. Same free choice as step 9. |
| 16 | `paint_background` | Click and drag anywhere on the room behind your Buddy to spray it. | The player paints on the room backdrop itself. Establishes that the whole visible room is a canvas. |
| 17 | `float_paint_background_panel` | We need to admire your drawing mroe. Drag any panel by its title bar and move it outside of the game window. You can also click the red pin button. | Teaches the game's most unusual feature: panels can be torn out of the window and live as real separate desktop windows. Framed as solving a problem the player is already having. |
| 18 | `save_exit_paint_background` | Looks good! Click Save and Exit to keep it. | Closes out the background editor with the single combined action button. |
| 19 | `open_buddy_studio` | Now lets bring your buddy more up to style. Open Buddy Studio so we can customise your Buddy with lots of apparel. | Navigation into the dress-up workshop. Signals that the tour is nearly over. |
| 20 | `select_nose_category` | Let's see, your buddy could use a new nose. Click on the Nose category to see what we've got. | Category list down the left of Buddy Studio. Nose is the teaching example throughout steps 20–25. |
| 21 | `select_nose_button_style` | This 'Button nose' could be fun! Click on it once to preview it without buying it. | Teaches the preview-before-buy rule: one click is safe and spends nothing. |
| 22 | `buy_studio_item` | It looks great! Let's click on the 'Buy' button. Alternatively, you can double-click on an item to buy it. | The purchase step. Mentions the double-click shortcut so the player is not surprised later when a double-click spends credits. |
| 23 | `equip_studio_item` | Let's equip it for now. You can swap it later, or choose the default style to remove it. | Separates owning from wearing, and reassures the player that cosmetic choices are never permanent. |
| 24 | `save_buddy_studio` | Click on the 'Save' button to keep this beautiful nose. | Blunt warning step — unsaved studio changes really are discarded on exit. |
| 25 | `exit_buddy_studio` | *(nose already worn)* Your Buddy was already wearing that nose, so there is no need to save. Let's exit to the play screen. <br>*(normal)* Let's exit to the play screen. | Two variants: if the player already owned and wore that nose, Save is disabled and the tutorial would otherwise look broken. The first variant explains the greyed-out button instead of ignoring it. |
| 26 | `admire_studio_buddy` | Now that is what I call a nose. Now your buddy is looking mighty fine! | Reward beat back in the sandbox, mirroring step 12. No action required. |
| 27 | `enter_work_mode` | Last but not least: Work Mode. Enter work mode for when you need to concentrate and want to let your buddy sit beside you. | Introduces the game's signature mode. Has to sell the idea in one sentence before the player clicks, because the screen changes dramatically. |
| 28 | `drag_work_companion` | Click and hold on the Buddy with the left mouse button to drag your companion wherever you want it. | First Work Mode step. All game chrome is gone; only the tiny companion remains on the real desktop, so the line must name what is still clickable. |
| 29 | `resize_work_companion` | Click and hold the resize button ↘ with the left mouse button, then drag to make your companion bigger or smaller. | Teaches the resize grip in the small control cluster that appears on hover. |
| 30 | `toggle_work_counter` | Your Buddy will earn monmey while you work. Click on the screen to switch between this session's total count and your lifetime total count. | Explains the tiny CRT counter on the companion's desk and its two display modes. |
| 31 | `exit_work_mode` | Ready to head back to play mode? Double-click on your Buddy or click on the 'X' button to return. | The escape hatch. Critical line — without it a player can be stranded in a mode with almost no visible UI. |
| 32 | `farewell` | Well that was it, I hope you'll become the best of buds! If you ever need help, click on the '?' in the title bar and hover over anything on screen for context, or restart the tutorial from the settings screen. Have fun with your Buddy! | Final line of the tutorial. Hands the player the self-serve tool (Help mode, section 4) and gets out of the way. |

## 2. Tool descriptions

Shown in the footer of the Inventory and Tools panels when a row is hovered.

One line per tool, read while the player is deciding whether to spend credits.
It must state the actual mouse controls, because nothing else in the game
teaches a tool's inputs. Keep it to controls plus the one thing that makes the
tool distinct — this is a control reference, not sales copy.

| Tool | Text | Context |
| --- | --- | --- |
| Grab | Click and hold Buddy with the left mouse button to drag him. Release while moving to fling him. | The free default tool, owned from the start. The baseline everything else is compared against. |
| Power Grab | Drag Buddy with the left mouse button, just like Grab, but with much more pull and a harder throw. | A paid upgrade of Grab. The line's whole job is to justify the price against a tool the player already has. |
| Brush | Click and hold the left mouse button, then brush your Buddy slowly. It should have a favourite spot... | Care tool: raises mood and passive income. The hint about a favourite spot invites experimentation without spelling out where it is. |
| Feather | Click and hold the left mouse button to wiggle the Feather over Buddy. Keep going too long and it will get grumpy. | Care tool that flips to annoyance if overused. The warning is the point — it is the only tool that punishes overuse. |
| Baseball Bat | Hold the right mouse button to charge it, then release to swing. | First offensive purchase in the catalogue and the tutorial's teaching weapon. Introduces the charge-and-release pattern. |
| Boxing Glove | Swing the glove into your Buddy. | Momentum-based: damage comes from raw cursor speed, with no button press at all. Unusual enough that the line has to say so. |
| Baseball | Right-click to drop a baseball. Grab it with the left mouse button, then hold the right mouse button and pull back to throw. | First thrown object. Establishes the three-part drop / grab / pull-back-and-release pattern shared by every throwable below. |
| Soccer Ball | Right-click to drop the ball. Grab it with the left mouse button, then hold the right mouse button and pull back to kick it across the room. | Heavier throwable than the baseball. Same controls, bigger impact. |
| Meal | Right-click to drop a treat, though I'm not sure what it's made of... | Care item that Buddy walks over and eats on his own — or that can be thrown at his head. The line deliberately offers both readings. |
| Drink | Right-click to drop a drink. It also makes for a good projectile. | Companion item to the Meal, same dual use. |
| Repair Kit | Right-click to drop a kit, then grab it with the left mouse button and throw it at your Buddy to patch it up. | Healing item. Must be thrown into him by the player to work — he will not pick it up himself, and a knocked-out or burning Buddy cannot use one. |
| Grenade | Right-click to drop a grenade. Grab it with the left mouse button, then hold the right mouse button and pull back to lob it. Pulling back also pulls the pin. | Explosive. The pin is pulled by the throw motion itself, so the fuse only starts when the player commits — a rule the player cannot discover safely any other way. |
| Nerf Blaster | Left-click to have some friendly fire. | First firearm, cheap and harmless-feeling. Introduces point-and-click firing and the R reload key. |
| Pistol | Left-click to have some less friendly fire. | The straight, accurate firearm. Same controls as the blaster, considerably more pain. |
| Shotgun | Left-click to fire a spread of pellets with some hefty knockback. | Fires a spread that scores as one hit. Range matters more than aim, which is what the line is there to convey. |
| Fire Sprayer | Hold the left mouse button to spray burning fuel. Anything it touches catches fire. | Continuous-fire tool that sets Buddy — and other objects — on fire, with damage continuing after the player stops. |

## 3. Settings descriptions

Shown in the footer of the Settings panel when a row is hovered.

The player is in the Settings list deciding whether to change something. Each
line explains what the control does in plain terms and, where it matters, the
trade-off — quieter but less feedback, faster but hotter, safer but less
spectacle. Accessibility rows in particular need to say plainly what a
sensitive player is protected from.

| Setting | Control | Text | Context |
| --- | --- | --- | --- |
| Master Volume | Slider | Controls every sound in the game. The volume sliders below are all relative to this one. | Top of the audio group. Has to establish that the sliders under it are relative to this one. |
| Sound Effects | Slider | Controls gameplay sounds, think of hits, gunfire and explosions. | Gameplay audio bus. The list of examples is what tells the player which bus a given sound belongs to. |
| Interface Sounds | Slider | Controls menu clicks, button presses, purchase chimes and save confirmations. | UI audio bus, separated so the player can silence menus without silencing the game. |
| Mute While Working | Toggle | Mutes the game so your Buddy will not interrupt your work or calls. | Work Mode courtesy option, for players who keep Buddy up during actual work or calls. |
| Mute Work Typing | Toggle | Mutes only Buddy's keyboard sounds in Work Mode. Everything else stays audible. | Narrower version of the row above — the typing loop is the one sound people tire of first. |
| V-Sync | Toggle | Matches the frame rate to your monitor's refresh rate. Turning it off can reduce input lag, but may cause screen tearing. | Graphics group. States both sides of the trade so the player can choose knowingly. |
| Frame Limit | Choice | Sets the maximum frame rate of your game. | Matters more than usual here: this is an app people leave running all day, often on a laptop. |
| Background Frame Limit | Choice | Sets the frame limit while the game is hidden or in the background. | The setting that makes leaving Buddy running cheap. Worth making that benefit explicit. |
| UI Scale | Choice | Changes the size of menus, buttons and text. | The note about panels reopening prevents the player thinking the setting failed to apply. |
| Buddy Size | Choice | Changes how large your Buddy and the room appear inside the window. It does not resize the window itself. | Easily confused with UI Scale and with resizing the window; the second sentence is the disambiguation. |
| Modern UI Motion | Toggle | Adds short, smooth transitions to menus and previews. | Sits between the retro shell and modern comfort, and is subordinate to the accessibility toggle below. |
| Always On Top | Toggle | Keeps Buddy's window above your other windows. | Core desktop-behaviour setting, on by default. |
| Monitor | Choice | Chooses which display Buddy appears on. | Multi-monitor placement. Nothing more to explain — the shortest line in the sheet, and it should stay short. |
| Reduced Motion | Toggle | Reduces or removes camera kicks, launches and sweeping menu transitions. | Accessibility. Must name the specific motions it removes so a motion-sensitive player can judge it before enabling. |
| Screen Shake | Toggle | Lets heavy hits and explosions shake the camera. Turn this off for a steady view. | Accessibility and taste. Separate from Reduced Motion so the player can keep one without the other. |
| Reduced Particles | Toggle | Shows fewer sparks, smoke puffs and pieces of debris. | Both a performance lever and a visual-calm one. |
| Photosensitivity Safe | Toggle | Limits rapid flashes and strobing from fire, explosions and muzzle flashes. | Safety setting. Must be precise about which effects are capped — vagueness here is not acceptable to the player who needs it. |
| Start In | Choice | Chooses whether the game starts in Work Mode or Play Mode. | Chooses Work or Play Mode at startup. |
| Hide For Full-Screen Apps | Toggle | Hides Buddy while a game, video or presentation is full-screen. | Stops an always-on-top Buddy from covering someone's film or slide deck. |
| Start With Windows | Toggle | Launches Desktop Buddy when you sign in to Windows. | Startup registration. Deliberately plain — this is a permission-shaped setting and should not be sold. |
| Work/Play Hotkey | Hotkey | Sets the keyboard shortcut for switching between Work Mode and Play Mode. | Rebindable global hotkey for the mode toggle. |
| Drop Tool Hotkey | Hotkey | Sets the shortcut for dropping an equipped tool into the room. Double-click the tool to equip it again. | The `D` key taught in tutorial step 5. Repeats the re-equip trick for players who skipped or forgot the tutorial. |
| Save Folder | Action | Opens the folder containing your progress, settings and saved characters. | Button, not a setting. Opens the save directory in Explorer for backup or sharing. |
| Reset Progress | Action | Starts your gameplay progress over from the beginning. Your settings and saved characters are kept. | Destructive action. The second sentence is the important half: it tells the player exactly what survives. |
| Show Tutorial Again | Action | Restarts the first-session tutorial. Nothing else is reset. | Restarts section 1. The reassurance exists so it is not mistaken for the reset above it. |

## 4. Help mode

Shown in the Help popup when the `?` button is active and the region is hovered.
Both the **Title** and the **Text** are player-facing.

Help mode is the game's self-serve manual: the player presses `?` in the title
bar and hovers anything on screen to have it explained. These entries are read
out of order, long after the tutorial, by someone who is confused *right now*
about the specific thing under their cursor. Each one must stand alone with no
assumed context. Titles are short labels; text is one or two sentences.

| Region | Title | Text | Context |
| --- | --- | --- | --- |
| `Win98CommandBar` | Top bar | Use this bar to open Inventory, Tools, Paint, Buddy Studio, Work and the other main areas. | The Win98 menu strip across the top of the window — the game's main navigation. Orientation entry for a lost player. |
| `Win98BalanceLabel` | Credits | Shows your current money count. Earn them by playing with your Buddy or using Work Mode. You can spend them on tools and customisation. Rough play pays the most. | The credit counter in the top-right. The one place the whole economy loop is explained outside the tutorial. |
| `Win98ShopCommand` | Inventory | Buy new tools and toys here. Anything you buy is equipped straight away. | Command-bar button. Distinguishes the shop from the Tools menu next to it. |
| `Win98ToolsCommand` | Tools | Equip any tool you already own. | The counterpart to Inventory: switching, not buying. |
| `Win98PaintCommand` | Paint | Open the workshops for painting Buddy or the room background. | Command-bar entry that opens into the two paint workshops. |
| `Win98WorkCommand` | Work | Enter Work Mode, where Buddy becomes a small always-on-top companion and keeps earning while you work. | Entry point to Work Mode. Must describe the mode fully — clicking it changes the whole screen. |
| `ContextHelpButton` | Help | Select ? to turn Help mode on, then hover over anything on screen to learn what it does. Select ? again to leave. | The `?` button itself, explaining the mode the player is currently in — including how to get out of it. |
| `Win98StatusBar` | Status bar | The left side shows some extra details when needed. The right side shows your equipped tool. | The strip along the bottom of the window, split into two independent halves. |
| `StatusText` | Status message | Shows the latest message from the game, including purchases, saves and other confirmations. | Left half of the status bar. Where confirmations land, which is where players look after an action seems to do nothing. |
| `ActiveToolStatusText` | Equipped tool | Shows the tool currently on your cursor. Equip a different one from Inventory or Tools. | Right half of the status bar, and the reliable answer to "what am I holding?". |
| `Win98CharacterColumn` | Characters | Choose the Buddy you want to edit. | Left column of Paint Buddy. Also points at the layer panel, since the two are usually confused. |
| `Win98PaintLayerPanel` | Layers | Choose or hide which body-part layer becomes paintable. Hidden layers cannot be painted on and will reappear when you leave the editor. | Layer list in Paint Buddy. The hidden-layer rule is the main source of "why won't my brush work?". |
| `Win98PaintToolColumn` | Paint tools | Choose the Brush or Eraser, change its size, rotate the preview, Undo or Redo, and adjust the view. | The paint tool column as a whole — an inventory of what is in the strip. |
| `Win98PaintViewportFrame` | Paint canvas | Paint directly onto Buddy here. The brush follows the surface and only affects the selected layer. | The framed 3D viewport in Paint Buddy. Explains that painting projects onto the 3D model, not a flat image. |
| `CharacterPaintCanvas` | Paint canvas | Click and drag to paint Buddy. The selected tool, colour, size and layer control the result. | The inner canvas within that frame — hovering the drawing surface itself rather than its border. |
| `Win98PaintColorFooter` | Colours and actions | Choose a paint colour here. Save keeps your changes, Use Character applies them in Play Mode, and Exit leaves the editor. | Footer strip of Paint Buddy. Restates the save-versus-apply distinction from tutorial step 11. |
| `PaintPresetPalette` | Palette | Select a swatch to use a saved colour, or open the colour wheel for the full picker. | The swatch row, plus where to find the full colour picker. |
| `PaintPrimaryActions` | Character actions | Save keeps your changes. Use Character applies them in Play Mode. Reset restores the saved version, and Exit leaves Paint Buddy. | The four action buttons, each with its one-clause definition. Reset is the one players most need defined before pressing. |
| `PaintBackgroundPanel` | Paint Background | Paint the room backdrop here. Select Save and Exit when you want to keep the result. | The floating background-paint panel — the one from tutorial step 17 that can be torn out of the window. |
| `PaintToolGrid` | Background tools | Choose Brush, Pen, Spray, Fill, Eraser, Pick Colour, a shape tool or Undo. | Tool grid in the background painter, which has a wider toolset than Paint Buddy. |
| `PaintBrushSizeRow` | Brush size | Changes the size of the active background brush. You can also use your scroll wheel. | Brush-size control in that panel. |
| `PaintBackgroundPalettePanel` | Background palette | Choose the active colour, add a custom swatch or open the full colour picker. You can also delete them buy pressing the 'delete' button. | Colour section of the background painter, including custom swatches. |
| `EnvironmentBackgroundInputBlocker` | Background canvas | Paint directly onto the visible room. The tool panel hides while you drag so it does not cover your work. | The room itself while background painting. The auto-hiding panel would otherwise look like a bug. |
| `BuddyStudioCategories` | Categories | Choose the part of Buddy you want to customise, such as his eyes, glasses, headwear, top or shoes. | Category column in Buddy Studio, with examples so the player knows what is on offer. |
| `BuddyStudioPreviewPane` | Preview | Preview the selected style here. Bought items can be moved or resized in this preview pane before you save. | The live 3D preview. Some items can be nudged and scaled, which is not otherwise discoverable. |
| `BuddyStudioCatalogPane` | Styles | Select an item once to preview it for free. Owned items can be equipped while unowned styles show their price. | The style grid. Reassures the player that clicking around costs nothing. |
| `BuddyStudioInspectorPane` | Colour and ownership | Change available colours and check whether the selected style is owned, equipped or available to buy. | Right-hand inspector: colour options plus the ownership state of the previewed item. |
| `BuddyStudioBuy` | Buy / Equip | Buy the selected style permanently, or equip it if you already own it. | The one button that changes label with context, which is exactly why it needs an entry. |
| `BuddyStudioActions` | Studio actions | Save applies your character changes. Exit leaves Buddy Studio and warns you if anything is unsaved. | Save and Exit in the studio, including the unsaved-changes prompt. |
| `WorkCompanionRoot` | Work companion | Click and hold Buddy or the computer with the left mouse button, then drag the companion anywhere. Double-click Buddy to return to Play Mode. | The whole companion in Work Mode. The most important Help entry in the game — it carries the way back out. |
| `WorkControlCluster` | Companion controls | Use these controls to resize, pause or exit. | The small hover-revealed control cluster, which deliberately ignores companion scale so it stays clickable when tiny. |
| `WorkCrtCounter` | Work counter | Shows how much work you have done. The number increments per action, keyboard press or mouse click. Click on the screen to switch between this session count and your lifetime total count. | The tiny CRT screen on the companion's desk and its two counting modes. |
| `WorkResizeButton` | Resize | Click and hold this button with the left mouse button, then drag to resize the Work companion. The controls themselves stay the same size. | The ↘ grip. A hold-and-drag, not a click — worth stating, since a single click appears to do nothing. |
| `WorkMotionToggle` | Motion | Pauses or resumes Buddy's Work animations. Counters and rewards continue either way. | Pause button. The reassurance about earnings is the point: pausing costs the player nothing. |
| `WorkExitButton` | Exit Work Mode | Return to Play Mode. You can also double-click Buddy. | The X button, plus its shortcut. Second half of the way back out of Work Mode. |
